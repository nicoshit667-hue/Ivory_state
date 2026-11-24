using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Gameplay.Combat
{
    /// <summary>
    /// Armes à feu (pistolet, kalach, taser).
    /// <list type="bullet">
    /// <item>On équipe une arme du sac (onglet Armes). Tout le monde voit l'arme en main.</item>
    /// <item>Le client vise (centre de l'écran, avec une aide à la visée) et envoie la cible et la
    /// direction ; le serveur revérifie tout : arme équipée, balles, cadence, portée, angle, ligne de
    /// vue, zones protégées (service de combat).</item>
    /// <item>Recharger consomme un chargeur du sac. Les balles restantes du chargeur en cours sont
    /// gardées par arme pendant la session.</item>
    /// <item>Taser : la cible reste au sol <see cref="Firearms.StunSeconds"/> s (plus de déplacement).</item>
    /// </list>
    /// </summary>
    public sealed partial class PlayerCombat
    {
        private const float AimAssistDegrees = 4f;
        private const float HipAssistDegrees = 12f;
        private const float BodyCenterHeight = 1.1f;
        private const float ShotOriginHeight = 1.4f;

        /// <summary>Arme à feu en main (0 = aucune). Visible de tous (modèle en main, posture).</summary>
        private readonly SyncVar<byte> _equipped = new SyncVar<byte>(0);

        /// <summary>Balles dans le chargeur de l'arme en main. Propriétaire seulement.</summary>
        private readonly SyncVar<byte> _loaded = new SyncVar<byte>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<bool> _reloading = new SyncVar<bool>(false,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        /// <summary>Touché par un taser : au sol, immobile. Visible de tous (la simulation du déplacement le lit).</summary>
        private readonly SyncVar<bool> _stunned = new SyncVar<bool>(false);

        public FirearmDef EquippedFirearm => Firearms.ById(_equipped.Value);
        public int Loaded => _loaded.Value;
        public bool IsReloading => _reloading.Value;
        public bool IsStunned => _stunned.Value;

        /// <summary>Client local : mode visée (caméra rapprochée, aide à la visée réduite).</summary>
        public static bool LocalAiming { get; set; }

        /// <summary>
        /// Serveur : un tir demandé (tireur, cible visée ou null, direction). Rend vrai si le tir part
        /// (une balle est alors retirée du chargeur).
        /// </summary>
        public static Func<PlayerCombat, PlayerCombat, Vector3, bool> ServerFireHandler { get; set; }

        /// <summary>Client : un taser vient de me mettre au sol (pseudo du tireur).</summary>
        public static event Action<string> LocalStunned;

        // Serveur.
        private readonly byte[] _serverLoadedByWeapon = new byte[8];
        private float _serverNextFire;
        private float _reloadEnd = -1f;
        private float _stunEnd = -1f;
        private float _nextHolsterCheck;
        private PlayerInventory _bag;

        // Client.
        private float _nextClientFire;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFirearmStatics()
        {
            LocalAiming = false;
            ServerFireHandler = null;
            LocalStunned = null;
            s_shotClip = null;
            s_taserClip = null;
        }

        private PlayerInventory Bag => _bag != null ? _bag : (_bag = GetComponent<PlayerInventory>());

        #region Client : équiper, tirer, recharger

        /// <summary>Client : prendre en main l'arme à feu du sac (ou la ranger si elle est déjà en main).</summary>
        public void RequestEquipToggle(string weaponName)
        {
            if (!IsOwner)
                return;
            FirearmDef def = Firearms.ByName(weaponName);
            if (def == null)
                return;
            ServerEquip(_equipped.Value == def.Id ? (byte)0 : def.Id);
        }

        public void RequestHolster()
        {
            if (IsOwner && _equipped.Value != 0)
                ServerEquip(0);
        }

        /// <summary>Client : tirer vers le centre de l'écran. Recharge tout seul si le chargeur est vide.</summary>
        public bool TryFire()
        {
            FirearmDef def = EquippedFirearm;
            if (!IsOwner || def == null || _reloading.Value || _stunned.Value || Time.unscaledTime < _nextClientFire)
                return false;
            if (_loaded.Value == 0)
            {
                _nextClientFire = Time.unscaledTime + 0.5f;
                RequestReload();
                return false;
            }
            _nextClientFire = Time.unscaledTime + def.FireInterval;

            Vector3 origin = transform.position + Vector3.up * ShotOriginHeight;
            Camera camera = Camera.main;
            Ray ray = camera != null
                ? camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : new Ray(origin, (_motor != null ? _motor.GraphicalTransform : transform).forward);

            PlayerCombat target = FindShotTarget(def, ray, LocalAiming ? AimAssistDegrees : HipAssistDegrees);
            Vector3 direction = target != null
                ? target.transform.position + Vector3.up * BodyCenterHeight - origin
                : ray.origin + ray.direction * def.Range - origin;
            ServerFire(target, direction.normalized);
            return true;
        }

        public void RequestReload()
        {
            FirearmDef def = EquippedFirearm;
            if (IsOwner && def != null && !_reloading.Value && _loaded.Value < def.MagazineSize)
                ServerReload();
        }

        /// <summary>Joueur le plus proche de la ligne de visée, à portée et devant le tireur.</summary>
        private PlayerCombat FindShotTarget(FirearmDef def, Ray ray, float coneDegrees)
        {
            PlayerCombat best = null;
            float bestAngle = coneDegrees;
            Vector3 position = transform.position;
            var players = PlayerNetworkIdentity.ClientInstances;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerNetworkIdentity other = players[i];
                if (other == null || other.gameObject == gameObject || !other.TryGetComponent(out PlayerCombat combat))
                    continue;
                if (other.TryGetComponent(out Vehicles.PlayerDriving driving) && driving.IsInVehicle)
                    continue;
                Vector3 center = other.transform.position + Vector3.up * BodyCenterHeight;
                if ((center - position).sqrMagnitude > def.Range * def.Range)
                    continue;
                Vector3 toTarget = center - ray.origin;
                if (Vector3.Dot(toTarget, ray.direction) <= 0f)
                    continue;
                float angle = Vector3.Angle(ray.direction, toTarget);
                if (angle > bestAngle)
                    continue;
                best = combat;
                bestAngle = angle;
            }
            return best;
        }

        #endregion

        #region Serveur

        [ServerRpc]
        private void ServerEquip(byte id)
        {
            FirearmDef def = Firearms.ById(id);
            if (id != 0 && (def == null || Bag == null || !Bag.Has(def.Name) || _stunned.Value || IsBusyForWeapons()))
                return;
            ServerSetEquipped(id);
        }

        [Server]
        private void ServerSetEquipped(byte id)
        {
            byte current = _equipped.Value;
            if (current == id)
                return;
            if (current != 0 && current < _serverLoadedByWeapon.Length)
                _serverLoadedByWeapon[current] = _loaded.Value;
            _reloadEnd = -1f;
            _reloading.Value = false;
            _equipped.Value = id;
            _loaded.Value = id != 0 && id < _serverLoadedByWeapon.Length ? _serverLoadedByWeapon[id] : (byte)0;
        }

        [Server]
        public void ServerHolster() => ServerSetEquipped(0);

        [ServerRpc]
        private void ServerFire(PlayerCombat target, Vector3 direction)
        {
            FirearmDef def = EquippedFirearm;
            if (def == null || _reloading.Value || _stunned.Value || _loaded.Value == 0)
                return;
            if (Time.unscaledTime < _serverNextFire || direction.sqrMagnitude < 0.5f)
                return;
            _serverNextFire = Time.unscaledTime + def.FireInterval * 0.85f;
            if (target == this)
                target = null;
            if (ServerFireHandler != null && ServerFireHandler(this, target, direction.normalized))
                _loaded.Value--;
        }

        [ServerRpc]
        private void ServerReload()
        {
            FirearmDef def = EquippedFirearm;
            if (def == null || _reloading.Value || _loaded.Value >= def.MagazineSize || _stunned.Value)
                return;
            if (Bag == null || !Bag.Has(def.Ammo))
            {
                if (TryGetComponent(out PlayerEconomy economy))
                    economy.ServerNotify("Plus de « " + def.Ammo + " » dans ton sac : l'armurerie en vend.");
                return;
            }
            _reloading.Value = true;
            _reloadEnd = Time.time + def.ReloadTime;
        }

        /// <summary>Serveur : taser. La cible reste au sol, sans pouvoir bouger ni tirer.</summary>
        [Server]
        public void ServerStun(float seconds, string attackerName)
        {
            _stunned.Value = true;
            _stunEnd = Time.time + seconds;
            ServerSetEquipped(0);
            if (Owner.IsValid)
                TargetStunned(Owner, attackerName);
        }

        /// <summary>Serveur : traçante et détonation chez tout le monde, avertissement de la victime touchée.</summary>
        [Server]
        public void ServerPlayShot(Vector3 end, PlayerCombat victim, string attackerName, bool knockout)
        {
            FirearmDef def = EquippedFirearm;
            ObserversShot(end, _equipped.Value);
            if (victim != null && victim.Owner.IsValid && def != null && !def.Stun)
                victim.TargetHit(victim.Owner, attackerName, knockout);
        }

        [TargetRpc]
        private void TargetStunned(FishNet.Connection.NetworkConnection connection, string attackerName)
        {
            LocalStunned?.Invoke(attackerName);
        }

        private bool IsBusyForWeapons()
        {
            if (TryGetComponent(out Vehicles.PlayerDriving driving) && driving.IsInVehicle)
                return true;
            return _recoveringUntil.Value > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private void Update()
        {
            if (!IsServerInitialized)
                return;

            if (_reloadEnd > 0f && Time.time >= _reloadEnd)
            {
                _reloadEnd = -1f;
                _reloading.Value = false;
                FirearmDef def = EquippedFirearm;
                if (def != null && Bag != null && Bag.ServerTryRemoveOne(def.Ammo))
                    _loaded.Value = (byte)def.MagazineSize;
            }

            if (_stunEnd > 0f && Time.time >= _stunEnd)
            {
                _stunEnd = -1f;
                _stunned.Value = false;
            }

            // Arme vendue, voiture, hôpital : l'arme retourne dans le sac.
            if (_equipped.Value != 0 && Time.time >= _nextHolsterCheck)
            {
                _nextHolsterCheck = Time.time + 0.5f;
                FirearmDef def = EquippedFirearm;
                if (def == null || Bag == null || !Bag.Has(def.Name) || IsBusyForWeapons())
                    ServerSetEquipped(0);
            }
        }

        #endregion

        #region Visuel (tous les clients)

        private const string WeaponResourcesRoot = "Weapons/";
        private static AudioClip s_shotClip;
        private static AudioClip s_taserClip;
        private static Material s_tracerMaterial;
        private static Material s_taserMaterial;

        private GameObject _weaponModel;
        private byte _shownWeapon;
        private Animator _animator;
        private Quaternion _bodyRestRotation = Quaternion.identity;
        private bool _bodyRestKnown;
        private float _stunBlend;

        private void LateUpdate()
        {
            UpdateLunge();
            UpdateWeaponInHand();
            UpdateStunPose();
        }

        private Transform AvatarRoot
        {
            get
            {
                if (_motor == null)
                    return null;
                Transform graphics = _motor.GraphicalTransform;
                return graphics != transform && graphics.childCount > 0 ? graphics.GetChild(0) : null;
            }
        }

        private void UpdateWeaponInHand()
        {
            byte id = _equipped.Value;
            if (TryGetComponent(out Vehicles.PlayerDriving driving) && driving.IsInVehicle)
                id = 0;

            if (id != _shownWeapon)
            {
                _shownWeapon = id;
                if (_weaponModel != null)
                    Destroy(_weaponModel);
                _weaponModel = null;
                FirearmDef def = Firearms.ById(id);
                GameObject prefab = def != null ? Resources.Load<GameObject>(WeaponResourcesRoot + def.Model) : null;
                if (prefab != null)
                {
                    _weaponModel = Instantiate(prefab, transform);
                    _weaponModel.name = "ArmeEnMain";
                }
            }

            if (_weaponModel == null || _motor == null)
                return;

            Transform graphics = _motor.GraphicalTransform;
            if (_animator == null)
                _animator = graphics.GetComponentInChildren<Animator>();
            Transform hand = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Vector3 forward = graphics.forward;
            Vector3 position = hand != null
                ? hand.position + forward * 0.04f
                : graphics.position + graphics.rotation * new Vector3(0.25f, 1.1f, 0.35f);
            _weaponModel.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>Taser : le personnage bascule au sol, puis se relève.</summary>
        private void UpdateStunPose()
        {
            Transform body = AvatarRoot;
            if (body == null)
                return;
            float target = _stunned.Value ? 1f : 0f;
            if (Mathf.Approximately(_stunBlend, target) && target == 0f)
                return;
            if (!_bodyRestKnown)
            {
                _bodyRestRotation = body.localRotation;
                _bodyRestKnown = true;
            }
            _stunBlend = Mathf.MoveTowards(_stunBlend, target, Time.deltaTime * (target > 0f ? 4f : 1.5f));
            body.localRotation = _bodyRestRotation * Quaternion.Euler(-88f * _stunBlend, 0f, 0f);
        }

        [ObserversRpc]
        private void ObserversShot(Vector3 end, byte weaponId)
        {
            FirearmDef def = Firearms.ById(weaponId);
            if (def == null)
                return;
            Vector3 muzzle = _weaponModel != null
                ? _weaponModel.transform.position + _weaponModel.transform.forward * def.Length
                : transform.position + Vector3.up * ShotOriginHeight;

            Material material = def.Stun ? TaserMaterial : TracerMaterial;
            if (material != null)
            {
                var go = new GameObject(def.Stun ? "TirTaser" : "Tracante");
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.positionCount = 2;
                line.SetPosition(0, muzzle);
                line.SetPosition(1, end);
                line.widthMultiplier = def.Stun ? 0.012f : 0.025f;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                Destroy(go, def.Stun ? 0.35f : 0.06f);
            }

            AudioClip clip = def.Stun ? TaserClip : ShotClip;
            AudioSource.PlayClipAtPoint(clip, muzzle, def.Stun ? 0.6f : 1f);
        }

        private static Material TracerMaterial => s_tracerMaterial != null ? s_tracerMaterial
            : (s_tracerMaterial = Resources.Load<Material>(WeaponResourcesRoot + "M_Tracer"));

        private static Material TaserMaterial => s_taserMaterial != null ? s_taserMaterial
            : (s_taserMaterial = Resources.Load<Material>(WeaponResourcesRoot + "M_TaserWire"));

        /// <summary>Détonation synthétisée : bruit blanc à décroissance rapide, plus un claquement grave.</summary>
        private static AudioClip ShotClip
        {
            get
            {
                if (s_shotClip != null)
                    return s_shotClip;
                const int rate = 22050;
                int samples = (int)(rate * 0.45f);
                var data = new float[samples];
                var random = new System.Random(7);
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)rate;
                    float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                    float crack = noise * Mathf.Exp(-t * 38f);
                    float boom = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 9f) * 0.6f;
                    data[i] = Mathf.Clamp((crack + boom) * 0.9f, -1f, 1f);
                }
                s_shotClip = AudioClip.Create("Detonation", samples, 1, rate, false);
                s_shotClip.SetData(data, 0);
                return s_shotClip;
            }
        }

        /// <summary>Grésillement du taser : signal carré haché, avec du bruit.</summary>
        private static AudioClip TaserClip
        {
            get
            {
                if (s_taserClip != null)
                    return s_taserClip;
                const int rate = 22050;
                int samples = (int)(rate * 0.6f);
                var data = new float[samples];
                var random = new System.Random(3);
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)rate;
                    float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 95f * t));
                    float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                    float envelope = Mathf.Clamp01(t * 40f) * Mathf.Clamp01((0.6f - t) * 6f);
                    data[i] = (square * 0.35f + noise * 0.25f) * envelope;
                }
                s_taserClip = AudioClip.Create("Taser", samples, 1, rate, false);
                s_taserClip.SetData(data, 0);
                return s_taserClip;
            }
        }

        /// <summary>Appelé par <see cref="OnStopClient"/> : plus de visée locale ni d'arme affichée.</summary>
        private void StopFirearmClient()
        {
            if (IsOwner)
                LocalAiming = false;
            if (_weaponModel != null)
                Destroy(_weaponModel);
            _weaponModel = null;
            _shownWeapon = 0;
        }

        #endregion
    }
}
