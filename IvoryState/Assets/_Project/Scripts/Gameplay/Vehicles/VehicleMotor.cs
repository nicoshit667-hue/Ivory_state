using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using IvoryState.Core;
using IvoryState.Gameplay.Input;
using UnityEngine;

namespace IvoryState.Gameplay.Vehicles
{
    /// <summary>
    /// Véhicule (taxi) à conduite arcade, prédit côté client et contrôlé par le serveur, comme PlayerMotor.
    /// <para>
    /// <b>Propriété :</b> à l'arrêt, le véhicule appartient au serveur. Quand un joueur monte au volant,
    /// le serveur lui en donne la propriété : ses entrées pilotent alors le véhicule sans latence ressentie,
    /// et le serveur rejoue la même simulation pour faire foi. À la descente, le serveur reprend la propriété.
    /// </para>
    /// <para>
    /// <b>Modèle :</b> vitesse longitudinale + lacet, sans Rigidbody. C'est déterministe et peu coûteux sur
    /// mobile. Le CharacterController gère le sol et les trottoirs ; un balayage de la carrosserie (boîte de
    /// 1,8 × 4,4 m) arrête l'avant ou l'arrière contre les murs, les poteaux, les joueurs et les autres
    /// voitures, et empêche de tourner « dans » un obstacle. Rien ne traverse rien (voir GameLayers).
    /// </para>
    /// <para>
    /// <b>Relief :</b> la boîte de la carrosserie prend la pente du sol entre les essieux (une côte n'est
    /// pas un mur), la voiture reste plaquée au sol dans les descentes, et la caisse visible s'incline
    /// (tangage et roulis) côté client.
    /// </para>
    /// Joystick : haut/bas = accélérer/freiner (marche arrière), gauche/droite = tourner.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class VehicleMotor : TickNetworkBehaviour
    {
        #region Données réseau

        /// <summary>Entrées quantifiées : 2 octets par tick.</summary>
        public struct DriveData : IReplicateData
        {
            public sbyte Throttle;
            public sbyte Steer;

            private uint _tick;

            public static DriveData Create(float throttle, float steer)
            {
                return new DriveData
                {
                    Throttle = (sbyte)Mathf.RoundToInt(Mathf.Clamp(throttle, -1f, 1f) * 127f),
                    Steer = (sbyte)Mathf.RoundToInt(Mathf.Clamp(steer, -1f, 1f) * 127f),
                };
            }

            public float GetThrottle() => Throttle / 127f;
            public float GetSteer() => Steer / 127f;

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct VehicleState : IReconcileData
        {
            public Vector3 Position;
            public float Yaw;
            public float Speed;
            public float VerticalVelocity;

            private uint _tick;

            public VehicleState(Vector3 position, float yaw, float speed, float verticalVelocity)
            {
                Position = position;
                Yaw = yaw;
                Speed = speed;
                VerticalVelocity = verticalVelocity;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        #endregion

        public const int NoDriver = -1;

        [Header("Conduite")]
        [Tooltip("Vitesse max en marche avant (m/s). 16 m/s ≈ 58 km/h : vitesse urbaine.")]
        [SerializeField, Range(5f, 40f)] private float _maxSpeed = 16f;
        [SerializeField, Range(1f, 10f)] private float _maxReverseSpeed = 5f;
        [SerializeField, Range(1f, 20f)] private float _acceleration = 5f;
        [SerializeField, Range(1f, 30f)] private float _brakeDeceleration = 12f;
        [Tooltip("Ralentissement sans accélérer (frottements).")]
        [SerializeField, Range(0.1f, 10f)] private float _coastDeceleration = 2.5f;
        [Tooltip("Vitesse de rotation max (°/s), atteinte à partir de quelques m/s.")]
        [SerializeField, Range(20f, 180f)] private float _turnRate = 75f;
        [SerializeField] private float _gravity = -25f;

        [Header("Réseau")]
        [SerializeField, Range(1, 10)] private int _serverReconcileInterval = 2;

        [Header("Visuel")]
        [SerializeField] private Transform _graphicalTransform;

        [Tooltip("Position du conducteur, en local (pour la caméra et la sortie).")]
        [SerializeField] private Vector3 _driverExitOffset = new Vector3(-2.2f, 0f, 0f);

        private static readonly List<VehicleMotor> s_all = new List<VehicleMotor>();

        // Identifiant d'objet du joueur au volant (NoDriver si libre). Écrit par le serveur, lu par tous.
        private readonly SyncVar<int> _driverObjectId = new SyncVar<int>(NoDriver);

        private CharacterController _controller;
        private float _speed;
        private float _yaw;
        private float _verticalVelocity;
        private DriveData _lastTickedData;

        /// <summary>Véhicules présents (côté client comme serveur), pour trouver le plus proche.</summary>
        public static IReadOnlyList<VehicleMotor> All => s_all;

        // --- Voiture personnelle (même modèle que le taxi : carrosserie repeinte, sans lumignon) ---
        /// <summary>Couleurs des voitures personnelles (index 1…) ; 0 = taxi.</summary>
        public static readonly Color[] Paints =
        {
            new Color(0.95f, 0.72f, 0.04f), // taxi (Corolla jaune)
            new Color(0.92f, 0.92f, 0.90f), // blanche
            new Color(0.06f, 0.06f, 0.07f), // noire
            new Color(0.62f, 0.05f, 0.05f), // rouge
            new Color(0.08f, 0.20f, 0.50f), // bleue
            new Color(0.45f, 0.47f, 0.50f), // grise
        };

        public static readonly string[] PaintNames = { "Taxi", "Berline blanche", "Berline noire", "Berline rouge", "Berline bleue", "Berline grise" };

        private readonly SyncVar<byte> _paint = new SyncVar<byte>(0);
        private readonly SyncVar<string> _ownerName = new SyncVar<string>(string.Empty);

        /// <summary>Voiture personnelle (sinon taxi de la ville).</summary>
        public bool IsPrivate => _paint.Value != 0;
        public byte Paint => _paint.Value;
        public string OwnerName => _ownerName.Value;

        /// <summary>Serveur : clé du propriétaire (compte), vide pour un taxi.</summary>
        public string ServerOwnerKey { get; private set; } = string.Empty;

        [Server]
        public void ServerMakePrivate(byte paint, string ownerKey, string ownerName)
        {
            _paint.Value = (byte)Mathf.Clamp(paint, 1, Paints.Length - 1);
            _ownerName.Value = ownerName ?? string.Empty;
            ServerOwnerKey = ownerKey ?? string.Empty;
        }

        private MaterialPropertyBlock _paintBlock;

        /// <summary>Client : carrosserie à la couleur du propriétaire, lumignon de taxi masqué.</summary>
        private void ApplyPaint()
        {
            Transform graphics = GraphicalTransform;
            foreach (Renderer renderer in graphics.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == "Lumignon")
                {
                    renderer.enabled = !IsPrivate;
                }
                else if (renderer.name == "Carrosserie")
                {
                    _paintBlock ??= new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(_paintBlock);
                    _paintBlock.SetColor("_BaseColor", Paints[Mathf.Clamp(_paint.Value, 0, Paints.Length - 1)]);
                    renderer.SetPropertyBlock(_paintBlock);
                }
            }
        }

        public int DriverObjectId => _driverObjectId.Value;
        public bool HasDriver => _driverObjectId.Value != NoDriver;
        public float Speed => _speed;
        public float Yaw => _yaw;
        public Transform GraphicalTransform => _graphicalTransform != null ? _graphicalTransform : transform;

        /// <summary>Point de sortie, côté conducteur (monde).</summary>
        public Vector3 DriverExitPosition => transform.TransformPoint(_driverExitOffset);

        /// <summary>Descente d'un passager : côté opposé à la portière du conducteur.</summary>
        public Vector3 PassengerExitPosition =>
            transform.TransformPoint(new Vector3(-_driverExitOffset.x, _driverExitOffset.y, _driverExitOffset.z));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_all.Clear();
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            SetTickCallbacks(TickCallback.Tick);
            _paint.OnChange += (_, _, asServer) => { if (!asServer) ApplyPaint(); };
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SetupTilt();
            if (_paint.Value != 0)
                ApplyPaint();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _yaw = transform.eulerAngles.y;
            s_all.Add(this);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            s_all.Remove(this);
        }

        [Server]
        public void ServerSetDriver(int playerObjectId)
        {
            _driverObjectId.Value = playerObjectId;
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildDriveData());
            CreateReconcile();
        }

        private DriveData BuildDriveData()
        {
            // Le conducteur (propriétaire) produit les entrées. Sans conducteur, le serveur est le
            // contrôleur et envoie des entrées neutres : le véhicule ralentit et s'arrête.
            if (!IsOwner)
                return default;

            IMovementInputProvider input = PlayerInputRegistry.Provider;
            if (input == null)
                return default;

            Vector2 stick = input.Move;
            return DriveData.Create(stick.y, stick.x);
        }

        public override void CreateReconcile()
        {
            if (IsServerStarted && _serverReconcileInterval > 1 &&
                (TimeManager.LocalTick + (uint)ObjectId) % (uint)_serverReconcileInterval != 0)
                return;

            PerformReconcile(new VehicleState(transform.position, _yaw, _speed, _verticalVelocity));
        }

        [Replicate]
        private void PerformReplicate(DriveData data, ReplicateState state = ReplicateState.Invalid,
            Channel channel = Channel.Unreliable)
        {
            float delta = (float)TimeManager.TickDelta;

            // Véhicules des autres : on prolonge la dernière entrée d'un tick (même logique que les piétons).
            if (!IsServerStarted && !IsOwner)
            {
                if (state.ContainsTicked())
                    _lastTickedData = data;
                else if (state.IsFuture())
                {
                    if (data.GetTick() - _lastTickedData.GetTick() > 1)
                        data = default;
                    else
                        data = _lastTickedData;
                }
            }

            Simulate(data, delta);
        }

        private void Simulate(DriveData data, float delta)
        {
            float throttle = data.GetThrottle();
            float steer = data.GetSteer();

            // --- Vitesse longitudinale ---
            if (throttle > 0.05f)
            {
                _speed = _speed < 0f
                    ? Mathf.MoveTowards(_speed, 0f, _brakeDeceleration * delta)          // freine la marche arrière
                    : Mathf.MoveTowards(_speed, _maxSpeed * throttle, _acceleration * delta);
            }
            else if (throttle < -0.05f)
            {
                _speed = _speed > 0.5f
                    ? Mathf.MoveTowards(_speed, 0f, _brakeDeceleration * delta)          // frein
                    : Mathf.MoveTowards(_speed, -_maxReverseSpeed * -throttle, _acceleration * delta);
            }
            else
            {
                _speed = Mathf.MoveTowards(_speed, 0f, _coastDeceleration * delta);
            }

            // --- Direction : on ne tourne pas à l'arrêt ; sens inversé en marche arrière ---
            float steerFactor = Mathf.Clamp01(Mathf.Abs(_speed) / 4f) * Mathf.Sign(_speed);
            _yaw = Mathf.Repeat(_yaw + steer * _turnRate * steerFactor * delta, 360f);
            Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);
            // La carrosserie suit la pente (sinon l'avant « bute » dans la côte et la voiture s'arrête).
            Quaternion body = WithGroundPitch(rotation);
            // Tourner ne doit pas faire entrer l'avant ou l'arrière dans un obstacle.
            if (Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, _yaw)) > 0.01f && BodyOverlaps(body) &&
                !BodyOverlaps(WithGroundPitch(transform.rotation)))
            {
                _yaw = transform.eulerAngles.y;
                rotation = Quaternion.Euler(0f, _yaw, 0f);
                body = WithGroundPitch(rotation);
            }
            transform.rotation = rotation;

            // --- Gravité : au sol, la voiture est plaquée (plus vite elle roule, plus fort), elle ne
            // décolle pas en haut d'une côte ni dans une descente ---
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f - Mathf.Abs(_speed) * 0.4f;
            _verticalVelocity = Mathf.Max(_verticalVelocity + _gravity * delta, -40f);

            Vector3 forward = rotation * Vector3.forward;
            Vector3 before = transform.position;
            float allowed = SweepDistance(body, body * Vector3.forward * Mathf.Sign(_speed), Mathf.Abs(_speed) * delta);
            if (allowed < Mathf.Abs(_speed) * delta)
                _speed = Mathf.Sign(_speed) * allowed / delta; // choc : on s'arrête au contact
            _controller.Move((forward * _speed + Vector3.up * _verticalVelocity) * delta);

            // Contre un mur, la vitesse retombe à ce qui a réellement été parcouru (calcul identique
            // côté client et serveur, donc pas de désaccord).
            float travelled = Vector3.Dot(transform.position - before, forward) / delta;
            if (Mathf.Abs(travelled) < Mathf.Abs(_speed))
                _speed = travelled;
        }

        #region Carrosserie

        // Demi-dimensions de la carrosserie (berline 1,8 × 4,4 m). Le bas de la boîte est à 0,4 m du sol :
        // le trottoir (0,15 m) et les joints des ponts restent franchissables, les bordures se montent par le
        // CharacterController.
        private static readonly Vector3 BodyHalfExtents = new Vector3(0.9f, 0.5f, 2.2f);
        private static readonly Vector3 BodyCenter = new Vector3(0f, 0.9f, 0f);
        private const float Skin = 0.05f;

        // Sondes du sol : sous les essieux (empattement de 2,6 m) et sous les flancs.
        private const float AxleHalf = 1.3f;
        private const float TrackHalf = 0.75f;
        /// <summary>Au-delà (pente de 45 %), ce n'est plus un sol : on n'incline pas sur un mur.</summary>
        private const float MaxPitch = 24f;

        /// <summary>Hauteur du sol sous un point (décor statique, surfaces praticables seulement).</summary>
        private static bool ProbeGround(Vector3 point, out float height)
        {
            if (Physics.Raycast(point + Vector3.up * 1.6f, Vector3.down, out RaycastHit hit, 3.4f, Core.GameLayers.WorldMask,
                    QueryTriggerInteraction.Ignore) && hit.normal.y > 0.6f)
            {
                height = hit.point.y;
                return true;
            }
            height = 0f;
            return false;
        }

        /// <summary>Orientation de la carrosserie : le cap, plus la pente du sol entre l'essieu arrière et l'avant.</summary>
        private Quaternion WithGroundPitch(Quaternion yawRotation)
        {
            Vector3 forward = yawRotation * Vector3.forward;
            Vector3 position = transform.position;
            if (!ProbeGround(position + forward * AxleHalf, out float front) || !ProbeGround(position - forward * AxleHalf, out float rear))
                return yawRotation;
            float pitch = Mathf.Clamp(-Mathf.Atan2(front - rear, AxleHalf * 2f) * Mathf.Rad2Deg, -MaxPitch, MaxPitch);
            return yawRotation * Quaternion.Euler(pitch, 0f, 0f);
        }

        private static readonly RaycastHit[] s_hits = new RaycastHit[16];
        private static readonly Collider[] s_overlaps = new Collider[16];

        /// <summary>Distance parcourable avant de toucher un obstacle (balayage de la carrosserie).</summary>
        private float SweepDistance(Quaternion rotation, Vector3 direction, float distance)
        {
            if (distance <= 0f)
                return 0f;

            Vector3 center = transform.position + rotation * BodyCenter;
            int count = Physics.BoxCastNonAlloc(center, BodyHalfExtents, direction, s_hits, rotation, distance + Skin,
                Core.GameLayers.VehicleBlockingMask, QueryTriggerInteraction.Ignore);

            float allowed = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_hits[i];
                if (IsOwnCollider(hit.collider) || hit.distance <= 0f)
                    continue; // déjà en contact au départ : on laisse le CharacterController dégager
                if (hit.normal.y > 0.6f && !IsVehicleOrPlayer(hit.collider))
                    continue; // un sol en pente (bas d'une côte, rampe de pont) : la capsule le monte
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - Skin));
            }
            return allowed;
        }

        /// <summary>Vrai si la carrosserie, orientée ainsi, chevaucherait un obstacle.</summary>
        private bool BodyOverlaps(Quaternion rotation)
        {
            Vector3 center = transform.position + rotation * BodyCenter;
            int count = Physics.OverlapBoxNonAlloc(center, BodyHalfExtents, s_overlaps, rotation,
                Core.GameLayers.VehicleBlockingMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!IsOwnCollider(s_overlaps[i]))
                    return true;
            }
            return false;
        }

        private bool IsOwnCollider(Collider collider)
        {
            return collider.transform == transform || collider.transform.IsChildOf(transform);
        }

        private static bool IsVehicleOrPlayer(Collider collider)
        {
            int layer = collider.gameObject.layer;
            return layer == Core.GameLayers.Player || layer == Core.GameLayers.Vehicle || layer == Core.GameLayers.VehicleBody;
        }

        #endregion

        #region Inclinaison visible (client)

        private Transform _tilt;
        private Quaternion _tiltRotation = Quaternion.identity;

        /// <summary>
        /// Client : la carrosserie et les roues passent sous un pivot « Inclinaison » (sous Graphics, que le
        /// lisseur réseau déplace) ; on l'incline selon le sol, sans toucher à la simulation.
        /// </summary>
        private void SetupTilt()
        {
            Transform graphics = GraphicalTransform;
            if (_tilt != null || graphics == transform)
                return;
            _tilt = new GameObject("Inclinaison").transform;
            _tilt.SetParent(graphics, false);
            var children = new List<Transform>();
            foreach (Transform child in graphics)
            {
                if (child != _tilt)
                    children.Add(child);
            }
            foreach (Transform child in children)
                child.SetParent(_tilt, false);
        }

        private void LateUpdate()
        {
            if (_tilt == null)
                return;
            Transform graphics = GraphicalTransform;
            Vector3 p = graphics.position, f = graphics.forward, r = graphics.right;
            Quaternion target = Quaternion.identity;
            if (ProbeGround(p + f * AxleHalf, out float front) && ProbeGround(p - f * AxleHalf, out float rear) &&
                ProbeGround(p + r * TrackHalf, out float right) && ProbeGround(p - r * TrackHalf, out float left))
            {
                float pitch = Mathf.Clamp(-Mathf.Atan2(front - rear, AxleHalf * 2f) * Mathf.Rad2Deg, -MaxPitch, MaxPitch);
                float roll = Mathf.Clamp(Mathf.Atan2(left - right, TrackHalf * 2f) * Mathf.Rad2Deg, -12f, 12f);
                target = Quaternion.Euler(pitch, 0f, roll);
            }
            _tiltRotation = Quaternion.Slerp(_tiltRotation, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            _tilt.localRotation = _tiltRotation;
        }

        #endregion

        [Reconcile]
        private void PerformReconcile(VehicleState state, Channel channel = Channel.Unreliable)
        {
            _yaw = state.Yaw;
            _speed = state.Speed;
            _verticalVelocity = state.VerticalVelocity;

            _controller.enabled = false;
            transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
            _controller.enabled = true;
        }
    }
}
