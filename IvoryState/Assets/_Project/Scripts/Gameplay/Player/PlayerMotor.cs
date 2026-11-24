using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using IvoryState.Core;
using IvoryState.Gameplay.Input;
using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    /// <summary>
    /// Déplacement du personnage, serveur qui fait autorité, avec prédiction côté client
    /// (Client-Side Prediction de FishNet).
    /// <para>
    /// À chaque tick :
    /// <list type="number">
    /// <item>Le propriétaire échantillonne ses entrées et simule le mouvement <b>immédiatement</b>
    /// (aucune latence ressentie sur le tactile).</item>
    /// <item>Les entrées compressées (5 octets) partent vers le serveur.</item>
    /// <item>Le serveur rejoue la <b>même</b> simulation avec ces entrées : c'est sa position qui fait foi.</item>
    /// <item>Le serveur renvoie l'état (reconcile). Si le client a divergé, il se recale puis
    /// rejoue ses entrées non confirmées. Le visuel est lissé par NetworkTickSmoother.</item>
    /// </list>
    /// Le client n'envoie jamais de position ni de vitesse : impossible de se téléporter ou
    /// de courir plus vite en trichant. Les vitesses vivent uniquement dans ce composant.
    /// </para>
    /// <para>
    /// Règle d'or : tout ce qui influence le mouvement doit être calculé dans
    /// <see cref="PerformReplicate"/>, uniquement à partir des données répliquées et de l'état réconcilié.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : TickNetworkBehaviour
    {
        #region Données réseau

        /// <summary>
        /// Entrées d'un tick, quantifiées pour la bande passante mobile :
        /// 2 × sbyte (direction) + ushort (lacet caméra) + byte (drapeaux) = 5 octets.
        /// </summary>
        public struct MoveData : IReplicateData
        {
            private const float AxisScale = 127f;
            private const float YawToUShort = 65536f / 360f;
            private const byte JumpFlag = 1 << 0;
            private const byte SprintFlag = 1 << 1;

            public sbyte MoveX;
            public sbyte MoveY;
            public ushort ViewYaw;
            public byte Flags;

            private uint _tick;

            public static MoveData Create(Vector2 move, float viewYawDegrees, bool jump, bool sprint)
            {
                move = Vector2.ClampMagnitude(move, 1f);

                return new MoveData
                {
                    MoveX = (sbyte)Mathf.RoundToInt(move.x * AxisScale),
                    MoveY = (sbyte)Mathf.RoundToInt(move.y * AxisScale),
                    ViewYaw = (ushort)(Mathf.Repeat(viewYawDegrees, 360f) * YawToUShort),
                    Flags = (byte)((jump ? JumpFlag : 0) | (sprint ? SprintFlag : 0)),
                };
            }

            public Vector2 GetMove() => new Vector2(MoveX / AxisScale, MoveY / AxisScale);
            public float GetViewYaw() => ViewYaw / YawToUShort;
            public bool IsJumping() => (Flags & JumpFlag) != 0;
            public bool IsSprinting() => (Flags & SprintFlag) != 0;
            public void ClearJump() => Flags &= unchecked((byte)~JumpFlag);

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        /// <summary>État qui fait foi, envoyé par le serveur pour recaler le client.</summary>
        public struct ReconcileData : IReconcileData
        {
            public Vector3 Position;
            public float VerticalVelocity;
            public float BodyYaw;

            private uint _tick;

            public ReconcileData(Vector3 position, float verticalVelocity, float bodyYaw)
            {
                Position = position;
                VerticalVelocity = verticalVelocity;
                BodyYaw = bodyYaw;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        #endregion

        private const float GroundStickVelocity = -2f;

        [Header("Déplacement")]
        [Tooltip("Vitesse de marche (m/s), joystick poussé au maximum.")]
        [SerializeField, Range(0.5f, 6f)] private float _walkSpeed = 2.6f;

        [Tooltip("Vitesse de course (m/s).")]
        [SerializeField, Range(2f, 12f)] private float _sprintSpeed = 5.5f;

        [Tooltip("Vitesse de rotation du corps vers la direction de marche (°/s).")]
        [SerializeField, Range(90f, 1440f)] private float _turnSpeed = 720f;

        [Header("Saut et gravité")]
        [SerializeField, Range(0.2f, 3f)] private float _jumpHeight = 1.1f;
        [SerializeField] private float _gravity = -25f;
        [SerializeField] private float _maxFallSpeed = -40f;

        [Header("Détection du sol")]
        [SerializeField, Range(0.01f, 0.5f)] private float _groundCheckDistance = 0.12f;
        [Tooltip("Ce qui compte comme sol. Exclut les joueurs : ils se traversent (voir GameLayers).")]
        [SerializeField] private LayerMask _groundMask = GameLayers.WorldMask;

        [Header("Réseau")]
        [Tooltip("Le serveur envoie l'état qui fait foi (reconcile) un tick sur N. 2 = 15 Hz à 30 ticks/s. " +
                 "Les entrées, elles, partent à chaque tick. Plus N est grand, moins il y a de trafic, " +
                 "mais plus les corrections sont grosses quand le client s'est trompé.")]
        [SerializeField, Range(1, 10)] private int _serverReconcileInterval = 2;

        [Header("Visuel")]
        [Tooltip("Enfant portant le modèle 3D et le NetworkTickSmoother. Suivi par la caméra.")]
        [SerializeField] private Transform _graphicalTransform;

        private CharacterController _characterController;
        private PlayerNeeds _needs;
        private Vehicles.PlayerDriving _driving;
        private Combat.PlayerCombat _combat;
        private IMovementInputProvider _serverInput;
        private float _verticalVelocity;
        private float _bodyYaw;
        private bool _forceReliableReconcile;
        private uint _ignoreReconcileBeforeTick;
        private MoveData _lastTickedData;

        // Diagnostic (propriétaire uniquement) : positions prédites par tick, comparées aux reconciles.
        private const int PredictionHistorySize = 128;
        private Vector3[] _predictedPositions;
        private uint[] _predictedTicks;

        /// <summary>Transform lissé à suivre par la caméra et les effets (jamais la racine).</summary>
        public Transform GraphicalTransform => _graphicalTransform != null ? _graphicalTransform : transform;

        /// <summary>Vitesse horizontale du dernier tick (pour les animations).</summary>
        public float PlanarSpeed { get; private set; }

        public bool IsGrounded { get; private set; }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _needs = GetComponent<PlayerNeeds>();
            _driving = GetComponent<Vehicles.PlayerDriving>();
            _combat = GetComponent<Combat.PlayerCombat>();
            SetTickCallbacks(TickCallback.Tick);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _bodyYaw = transform.eulerAngles.y;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner && !IsServerStarted)
            {
                _predictedPositions = new Vector3[PredictionHistorySize];
                _predictedTicks = new uint[PredictionHistorySize];
            }
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
            // Avec un CharacterController (sans simulation physique), le reconcile peut être
            // créé dans le même tick, juste après le mouvement.
            CreateReconcile();
        }

        /// <summary>
        /// Serveur uniquement : pilote un personnage sans propriétaire (bots de test, plus tard PNJ).
        /// Le serveur devient alors le « contrôleur » et produit lui-même les entrées.
        /// </summary>
        public void SetServerInput(IMovementInputProvider input)
        {
            _serverInput = input;
        }

        private MoveData BuildMoveData()
        {
            // Seul le contrôleur (le propriétaire, ou le serveur si l'objet n'a pas de propriétaire)
            // produit des entrées ; les autres passent default et FishNet y substitue les données reçues.
            if (!IsController)
                return default;

            // Au volant, le joystick pilote le véhicule : le personnage envoie des entrées neutres. Sinon
            // le serveur exécuterait après la descente les entrées encore en file d'attente, et le
            // personnage repartirait tout seul.
            if (_driving != null && _driving.IsInVehicle)
                return default;

            IMovementInputProvider input = IsOwner ? PlayerInputRegistry.Provider : _serverInput;
            if (input == null)
                return default;

            return MoveData.Create(input.Move, input.ViewYaw, input.ConsumeJump(), input.Sprint);
        }

        public override void CreateReconcile()
        {
            // Après une téléportation serveur : envoi sur canal fiable. FishNet envoie toujours un
            // reconcile fiable, même quand ses renvois automatiques sont épuisés (entrées neutres).
            if (IsServerStarted && _forceReliableReconcile)
            {
                _forceReliableReconcile = false;
                PerformReconcile(new ReconcileData(transform.position, _verticalVelocity, _bodyYaw), Channel.Reliable);
                return;
            }

            // Serveur : un tick sur N seulement. Décalé par ObjectId pour que tous les joueurs
            // n'envoient pas leur état au même tick (trafic lissé au lieu de pics).
            if (IsServerStarted && _serverReconcileInterval > 1 &&
                (TimeManager.LocalTick + (uint)ObjectId) % (uint)_serverReconcileInterval != 0)
                return;

            // Le client crée aussi son reconcile local à chaque tick : il sert de repli si un paquet
            // serveur est perdu, et ne coûte rien en réseau.
            PerformReconcile(new ReconcileData(transform.position, _verticalVelocity, _bodyYaw));
        }

        [Replicate]
        private void PerformReplicate(MoveData data, ReplicateState state = ReplicateState.Invalid,
            Channel channel = Channel.Unreliable)
        {
            float delta = (float)TimeManager.TickDelta;

            // Joueurs distants vus par un client : on prolonge leur dernière entrée d'un tick
            // pour masquer la latence, sans aller plus loin (sinon trop d'erreurs de prédiction).
            if (!IsServerStarted && !IsOwner)
            {
                if (state.ContainsTicked())
                {
                    _lastTickedData = data;
                }
                else if (state.IsFuture())
                {
                    if (data.GetTick() - _lastTickedData.GetTick() > 1)
                    {
                        // Une très légère poussée vers le bas garde le CharacterController à jour
                        // face aux autres colliders (recommandation FishNet).
                        _characterController.Move(Vector3.down * delta);
                        return;
                    }

                    data = _lastTickedData;
                    data.ClearJump(); // un saut ne se prédit pas
                }
            }

            Simulate(data, delta);

            // Mémorise la position prédite de ce tick (hors rejeu) pour mesurer l'erreur au reconcile.
            if (_predictedPositions != null && state.ContainsTicked())
            {
                uint tick = data.GetTick();
                int slot = (int)(tick % PredictionHistorySize);
                _predictedTicks[slot] = tick;
                _predictedPositions[slot] = transform.position;
            }
        }

        /// <summary>Serveur : déplace instantanément le personnage (descente de véhicule…).</summary>
        [Server]
        public void ServerTeleport(Vector3 position)
        {
            Place(position);

            // Garantit que le propriétaire reçoit cette nouvelle position : sinon un recalage plus ancien
            // (encore au volant) peut arriver après son placement local et le ramener dans le véhicule.
            _forceReliableReconcile = true;
        }

        /// <summary>
        /// Place le personnage sans simulation. Utilisé à l'identique par le serveur et le client pour les
        /// déplacements instantanés déterministes (descente de véhicule). FishNet n'envoie pas de recalage
        /// tant que les entrées du joueur sont neutres : sans ce placement local, le client ne verrait sa
        /// nouvelle position qu'à son premier pas, avec un saut visible.
        /// </summary>
        public void Place(Vector3 position)
        {
            bool wasEnabled = _characterController.enabled;
            _characterController.enabled = false;
            transform.position = position;
            _characterController.enabled = wasEnabled;

            // Client : les recalages antérieurs à ce placement décrivent l'état d'avant (au volant). Ils
            // peuvent arriver après lui (canal non fiable, ordre non garanti) : on les ignore.
            if (!IsServerStarted)
                _ignoreReconcileBeforeTick = TimeManager.LocalTick;
        }

        private void Simulate(MoveData data, float delta)
        {
            // Au volant ou passager : le personnage (masqué) suit le véhicule, sans simulation propre.
            if (_driving != null && _driving.IsInVehicle)
            {
                Vehicles.VehicleMotor vehicle = _driving.Vehicle;
                if (vehicle != null)
                    transform.position = vehicle.transform.position;
                PlanarSpeed = 0f;
                _verticalVelocity = 0f;
                return;
            }

            // Validation : la magnitude ne peut pas dépasser 1, même avec un client modifié.
            Vector2 input = data.GetMove();
            if (input.sqrMagnitude > 1f)
                input.Normalize();
            // Taser : au sol, plus de déplacement ni de saut (état synchronisé, donc même résultat partout).
            bool stunned = _combat != null && _combat.IsStunned;
            if (stunned)
                input = Vector2.zero;

            // Déplacement relatif à la caméra du joueur.
            Vector3 wishDirection = Quaternion.Euler(0f, data.GetViewYaw(), 0f) * new Vector3(input.x, 0f, input.y);
            // Affamé : plus de course. Vérifié ici, dans la simulation, donc aussi par le serveur.
            bool canSprint = _needs == null || !_needs.IsExhausted;
            float speed = canSprint && data.IsSprinting() && input.sqrMagnitude > 0.5f ? _sprintSpeed : _walkSpeed;
            Vector3 planarVelocity = wishDirection * speed;

            IsGrounded = CheckGrounded();
            if (IsGrounded && _verticalVelocity < 0f)
                _verticalVelocity = GroundStickVelocity;

            if (!stunned && data.IsJumping() && IsGrounded)
                _verticalVelocity = Mathf.Sqrt(2f * -_gravity * _jumpHeight);

            _verticalVelocity = Mathf.Max(_verticalVelocity + _gravity * delta, _maxFallSpeed);

            // Arme à feu en main : le corps fait face à la caméra (on vise devant soi, même en reculant).
            bool armed = !stunned && _combat != null && _combat.EquippedFirearm != null;
            if (armed || wishDirection.sqrMagnitude > 0.0001f)
            {
                float targetYaw = armed
                    ? data.GetViewYaw()
                    : Mathf.Atan2(wishDirection.x, wishDirection.z) * Mathf.Rad2Deg;
                _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, targetYaw, _turnSpeed * delta);
                transform.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);
            }

            PlanarSpeed = planarVelocity.magnitude;
            _characterController.Move(new Vector3(planarVelocity.x, _verticalVelocity, planarVelocity.z) * delta);
        }

        /// <summary>
        /// Détection du sol déterministe, basée uniquement sur la position : contrairement à
        /// CharacterController.isGrounded, elle reste juste pendant les rejeux de prédiction.
        /// </summary>
        private bool CheckGrounded()
        {
            float radius = _characterController.radius;
            float castRadius = radius * 0.9f;

            // Le pivot du personnage est aux pieds. La sphère part de l'intérieur de notre propre
            // capsule : un SphereCast ignore les colliders qu'il chevauche au départ, donc le nôtre.
            Vector3 origin = transform.position + Vector3.up * (radius + _characterController.skinWidth);
            float distance = _characterController.skinWidth + (radius - castRadius) + _groundCheckDistance;

            return Physics.SphereCast(origin, castRadius, Vector3.down, out _, distance,
                _groundMask, QueryTriggerInteraction.Ignore);
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData data, Channel channel = Channel.Unreliable)
        {
            if (!IsServerStarted && data.GetTick() < _ignoreReconcileBeforeTick)
                return; // état périmé, antérieur à un placement local (voir Place)

            if (_predictedPositions != null)
            {
                uint tick = data.GetTick();
                int slot = (int)(tick % PredictionHistorySize);
                if (_predictedTicks[slot] == tick)
                    PredictionDiagnostics.Record(Vector3.Distance(_predictedPositions[slot], data.Position));
            }

            _verticalVelocity = data.VerticalVelocity;
            _bodyYaw = data.BodyYaw;

            // Indispensable : désactiver le CharacterController avant de le déplacer, sinon son
            // collider interne reste à l'ancienne position jusqu'au prochain Move.
            // On restaure l'état précédent : au volant, le CharacterController doit rester désactivé.
            bool wasEnabled = _characterController.enabled;
            _characterController.enabled = false;
            transform.SetPositionAndRotation(data.Position, Quaternion.Euler(0f, data.BodyYaw, 0f));
            _characterController.enabled = wasEnabled;
        }
    }
}
