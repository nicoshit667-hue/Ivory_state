using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using IvoryState.Gameplay.Networking;
using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    /// <summary>
    /// Identité réseau d'un joueur : présente sur le prefab joueur, côté client comme serveur.
    /// <para>
    /// Le serveur écrit les données (pseudo, etc.) et FishNet les réplique automatiquement
    /// vers les clients qui observent ce joueur. Un client ne peut jamais modifier ces valeurs.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNetworkIdentity : NetworkBehaviour
    {
        /// <summary>Le joueur contrôlé sur cet appareil (null côté serveur dédié).</summary>
        public static PlayerNetworkIdentity Local { get; private set; }

        /// <summary>Levé une fois quand le joueur local est prêt (caméra, UI, contrôles…).</summary>
        public static event Action<PlayerNetworkIdentity> LocalPlayerSpawned;

        // Écriture serveur uniquement, lecture par les observateurs (réglage par défaut de FishNet).
        private readonly SyncVar<string> _displayName = new SyncVar<string>();

        public string DisplayName => _displayName.Value;

        // Apparence choisie à la création du personnage (255 = pas encore choisie : avatar tiré du pseudo).
        public const byte NoAppearance = 255;
        public const int OutfitCount = 4;
        private readonly SyncVar<byte> _model = new SyncVar<byte>(NoAppearance);
        private readonly SyncVar<byte> _outfit = new SyncVar<byte>(0);
        private readonly SyncVar<int> _xp = new SyncVar<int>(0);
        private readonly SyncVar<byte> _jobStatus = new SyncVar<byte>((byte)PlayerJobStatus.Civil);
        private readonly SyncVar<int> _profileViews = new SyncVar<int>(0);
        private readonly SyncVar<int> _profilePrivacy = new SyncVar<int>(0);
        private float _nextAppearanceChange;
        private float _nextJobStatusChange;
        private float _nextPrivacyChange;

        /// <summary>Index du modèle (catalogue des avatars), ou <see cref="NoAppearance"/>.</summary>
        public byte Model => _model.Value;
        public byte Outfit => _outfit.Value;
        public bool HasAppearance => _model.Value != NoAppearance;
        public int Xp => _xp.Value;
        public int Level => PlayerProgression.GetLevel(_xp.Value);
        public PlayerJobStatus JobStatus => PlayerProfileMetaCatalog.IsValid(_jobStatus.Value) ? (PlayerJobStatus)_jobStatus.Value : PlayerJobStatus.Civil;
        public int ProfileViews => _profileViews.Value;
        public int ProfilePrivacy => _profilePrivacy.Value;

        /// <summary>Serveur : apparence validée (création du personnage), pour la sauvegarde du profil.</summary>
        public static event Action<PlayerNetworkIdentity> ServerAppearanceChanged;

        /// <summary>Serveur : XP modifiée, pour la sauvegarde du profil.</summary>
        public static event Action<PlayerNetworkIdentity> ServerXpChanged;

        /// <summary>Serveur : métier/statut, vues ou confidentialité modifiés.</summary>
        public static event Action<PlayerNetworkIdentity> ServerProfileMetaChanged;

        /// <summary>Client : XP du joueur local mise à jour.</summary>
        public static event Action<PlayerNetworkIdentity> LocalXpChanged;

        /// <summary>Client : métier/statut, vues ou confidentialité du joueur local mis à jour.</summary>
        public static event Action<PlayerNetworkIdentity> LocalProfileMetaChanged;

        public void RequestSetJobStatus(PlayerJobStatus status)
        {
            if (IsOwner && PlayerProfileMetaCatalog.IsValid((byte)status))
                ServerSetJobStatus((byte)status);
        }

        [ServerRpc]
        private void ServerSetJobStatus(byte status)
        {
            if (_jobStatus.Value == status || Time.unscaledTime < _nextJobStatusChange || !PlayerProfileMetaCatalog.IsValid(status))
                return;
            _nextJobStatusChange = Time.unscaledTime + 0.5f;
            _jobStatus.Value = status;
            ServerProfileMetaChanged?.Invoke(this);
        }

        public void RequestSetProfilePrivacy(int mask)
        {
            if (IsOwner)
                ServerSetProfilePrivacy(mask & (int)ProfilePrivacyFlags.All);
        }

        [ServerRpc]
        private void ServerSetProfilePrivacy(int mask)
        {
            int clamped = mask & (int)ProfilePrivacyFlags.All;
            if (_profilePrivacy.Value == clamped || Time.unscaledTime < _nextPrivacyChange)
                return;
            _nextPrivacyChange = Time.unscaledTime + 0.15f;
            _profilePrivacy.Value = clamped;
            ServerProfileMetaChanged?.Invoke(this);
        }

        [Server]
        public void ServerLoadProfileMeta(byte jobStatus, int profileViews, int profilePrivacy)
        {
            _jobStatus.Value = PlayerProfileMetaCatalog.IsValid(jobStatus) ? jobStatus : (byte)PlayerJobStatus.Civil;
            _profileViews.Value = Mathf.Max(0, profileViews);
            _profilePrivacy.Value = profilePrivacy & (int)ProfilePrivacyFlags.All;
        }

        [Server]
        public void ServerIncrementProfileViews()
        {
            _profileViews.Value = Mathf.Max(0, _profileViews.Value + 1);
            ServerProfileMetaChanged?.Invoke(this);
        }

        /// <summary>Client : choix de l'apparence (création du personnage).</summary>
        public void RequestAppearance(int model, int outfit)
        {
            if (IsOwner && model >= 0 && model < NoAppearance && outfit >= 0 && outfit < OutfitCount)
                ServerSetAppearance((byte)model, (byte)outfit);
        }

        [ServerRpc]
        private void ServerSetAppearance(byte model, byte outfit)
        {
            if (Time.unscaledTime < _nextAppearanceChange || model >= 64 || outfit >= OutfitCount)
                return;
            _nextAppearanceChange = Time.unscaledTime + 1f;
            // Après la création du personnage : même modèle, et seulement une tenue achetée (boutiques).
            if (HasAppearance && (model != _model.Value || !ServerOwnsOutfit(outfit)))
                return;
            ServerGrantOutfit(outfit);
            ServerApplyAppearance(model, outfit);
            ServerAppearanceChanged?.Invoke(this);
        }

        // --- Garde-robe (serveur) : tenues achetées, bit n° i = tenue i (la tenue de base est offerte) ---
        public int ServerOwnedOutfits { get; private set; } = 1;

        public bool ServerOwnsOutfit(int outfit) => outfit == 0 || (ServerOwnedOutfits & (1 << outfit)) != 0;

        [Server]
        public void ServerGrantOutfit(int outfit) => ServerOwnedOutfits |= 1 << Mathf.Clamp(outfit, 0, 30);

        [Server]
        public void ServerLoadOutfits(int mask) => ServerOwnedOutfits = mask | 1;

        /// <summary>Serveur : change la tenue portée (boutique) et déclenche la sauvegarde.</summary>
        [Server]
        public void ServerWearOutfit(byte outfit)
        {
            if (!HasAppearance || outfit >= OutfitCount)
                return;
            ServerGrantOutfit(outfit);
            ServerApplyAppearance(_model.Value, outfit);
            ServerAppearanceChanged?.Invoke(this);
        }

        /// <summary>Serveur : applique une apparence (choix du joueur ou profil sauvegardé).</summary>
        [Server]
        public void ServerApplyAppearance(byte model, byte outfit)
        {
            _model.Value = model;
            _outfit.Value = (byte)Mathf.Min(outfit, OutfitCount - 1);
        }

        /// <summary>Serveur : charge l'XP sauvegardée depuis Nakama.</summary>
        [Server]
        public void ServerLoadXp(int xp)
        {
            _xp.Value = Mathf.Max(0, xp);
        }

        /// <summary>Serveur : ajoute de l'XP au joueur, notifie en cas de montée de niveau.</summary>
        [Server]
        public void ServerAddXp(int amount, string reason = null)
        {
            if (amount <= 0)
                return;

            int beforeLevel = Level;
            _xp.Value = Mathf.Max(0, _xp.Value + amount);
            int afterLevel = Level;

            if (afterLevel > beforeLevel && TryGetComponent(out PlayerEconomy economy))
            {
                string title = PlayerProgression.GetRankTitle(afterLevel);
                economy.ServerNotify($"Niveau {afterLevel} atteint ! ({title})");
            }

            ServerXpChanged?.Invoke(this);
        }

        private static readonly System.Collections.Generic.List<PlayerNetworkIdentity> s_clientInstances =
            new System.Collections.Generic.List<PlayerNetworkIdentity>(64);

        /// <summary>Joueurs actuellement répliqués sur ce client (soi compris) : étiquettes de nom, etc.</summary>
        public static System.Collections.Generic.IReadOnlyList<PlayerNetworkIdentity> ClientInstances => s_clientInstances;

        /// <summary>
        /// Nombre de joueurs actuellement répliqués sur ce client (soi compris).
        /// Mesure directe de la gestion de l'intérêt : un client ne reçoit que les joueurs proches.
        /// </summary>
        public static int ClientVisibleCount => s_clientInstances.Count;

        [Tooltip("Rendu du corps, recoloré selon l'identifiant du joueur (tenue).")]
        [SerializeField] private Renderer _bodyRenderer;

        private bool _countedOnClient;
        private float _nextNameChange;

        private static readonly System.Collections.Generic.List<PlayerNetworkIdentity> s_serverInstances =
            new System.Collections.Generic.List<PlayerNetworkIdentity>(128);

        /// <summary>Joueurs présents sur le serveur (annuaire du téléphone, etc.).</summary>
        public static System.Collections.Generic.IReadOnlyList<PlayerNetworkIdentity> ServerInstances => s_serverInstances;

        // Tenues : couleurs franches, faciles à distinguer de loin. Une couleur par identifiant, identique
        // sur tous les écrans, sans aucun échange réseau.
        private static readonly Color[] Outfits =
        {
            new Color(0.90f, 0.30f, 0.15f), new Color(0.15f, 0.60f, 0.35f), new Color(0.95f, 0.78f, 0.15f),
            new Color(0.20f, 0.40f, 0.85f), new Color(0.80f, 0.20f, 0.45f), new Color(0.55f, 0.30f, 0.70f),
            new Color(0.10f, 0.70f, 0.75f), new Color(0.95f, 0.55f, 0.70f),
        };

        private static Material[] s_outfitMaterials;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalPlayerSpawned = null;
            ServerAppearanceChanged = null;
            ServerXpChanged = null;
            ServerProfileMetaChanged = null;
            LocalXpChanged = null;
            LocalProfileMetaChanged = null;
            s_clientInstances.Clear();
            s_serverInstances.Clear();
            s_outfitMaterials = null;
        }

        /// <summary>
        /// Applique la tenue liée à l'identifiant d'objet. Les matériaux sont partagés entre tous les
        /// joueurs de même couleur (8 au total), compatibles SRP Batcher.
        /// </summary>
        private void ApplyOutfit()
        {
            if (_bodyRenderer == null || Application.isBatchMode)
                return;

            if (s_outfitMaterials == null)
            {
                s_outfitMaterials = new Material[Outfits.Length];
                for (int i = 0; i < Outfits.Length; i++)
                {
                    s_outfitMaterials[i] = new Material(_bodyRenderer.sharedMaterial);
                    s_outfitMaterials[i].SetColor("_BaseColor", Outfits[i]);
                }
            }

            _bodyRenderer.sharedMaterial = s_outfitMaterials[Mathf.Abs(ObjectId) % Outfits.Length];
        }

        private void Awake()
        {
            _displayName.OnChange += OnDisplayNameChanged;
            _xp.OnChange += OnXpChanged;
            _jobStatus.OnChange += OnProfileMetaByteChanged;
            _profileViews.OnChange += OnProfileMetaIntChanged;
            _profilePrivacy.OnChange += OnProfileMetaIntChanged;
        }

        private void OnDestroy()
        {
            _displayName.OnChange -= OnDisplayNameChanged;
            _xp.OnChange -= OnXpChanged;
            _jobStatus.OnChange -= OnProfileMetaByteChanged;
            _profileViews.OnChange -= OnProfileMetaIntChanged;
            _profilePrivacy.OnChange -= OnProfileMetaIntChanged;
            if (Local == this)
                Local = null;
        }

        private void OnXpChanged(int previous, int next, bool asServer)
        {
            if (!asServer && IsOwner)
                LocalXpChanged?.Invoke(this);
        }

        private void OnProfileMetaByteChanged(byte previous, byte next, bool asServer)
        {
            if (!asServer && IsOwner)
                LocalProfileMetaChanged?.Invoke(this);
        }

        private void OnProfileMetaIntChanged(int previous, int next, bool asServer)
        {
            if (!asServer && IsOwner)
                LocalProfileMetaChanged?.Invoke(this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Provisoire : le vrai pseudo viendra du backend (Nakama) après authentification.
            _displayName.Value = Owner.IsValid ? "Joueur_" + Owner.ClientId : "Bot_" + ObjectId;

            PlayerRelevance.Register(NetworkObject);
            s_serverInstances.Add(this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            PlayerRelevance.Unregister(NetworkObject);
            s_serverInstances.Remove(this);
        }

        /// <summary>Demande de pseudo (joueur local). Le serveur valide et peut ajuster.</summary>
        public void RequestName(string name)
        {
            if (IsOwner && Social.PlayerNames.Sanitize(name) != null)
                ServerSetName(name);
        }

        [ServerRpc]
        private void ServerSetName(string requested)
        {
            // Un changement toutes les 5 s au plus.
            if (Time.unscaledTime < _nextNameChange)
                return;
            _nextNameChange = Time.unscaledTime + 5f;

            ServerApplyName(requested);
        }

        /// <summary>
        /// Serveur : nettoie, rend unique et applique un pseudo (demande du joueur ou pseudo sauvegardé).
        /// Renvoie le pseudo appliqué, ou null s'il était invalide.
        /// </summary>
        [Server]
        public string ServerApplyName(string requested)
        {
            string name = Social.PlayerNames.Sanitize(requested);
            if (name == null)
                return null;

            // Unicité parmi les joueurs connectés : suffixe numérique si le pseudo est pris.
            string unique = name;
            for (int suffix = 2; IsNameTaken(unique); suffix++)
            {
                string tail = suffix.ToString();
                unique = (name.Length + tail.Length > Social.PlayerNames.MaxLength
                    ? name.Substring(0, Social.PlayerNames.MaxLength - tail.Length)
                    : name) + tail;
            }

            Core.Diagnostics.GameLog.Info("Names", _displayName.Value + " devient " + unique + ".");
            _displayName.Value = unique;
            return unique;
        }

        private bool IsNameTaken(string name)
        {
            var all = s_serverInstances;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != this && string.Equals(all[i].DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            s_clientInstances.Add(this);
            _countedOnClient = true;
            ApplyOutfit();

            if (!IsOwner)
                return;

            Local = this;
            LocalPlayerSpawned?.Invoke(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (_countedOnClient)
            {
                s_clientInstances.Remove(this);
                _countedOnClient = false;
            }

            if (Local == this)
                Local = null;
        }

        private void OnDisplayNameChanged(string previous, string next, bool asServer)
        {
#if UNITY_EDITOR
            // Confort de debug uniquement : renommer un GameObject alloue, inutile en build.
            gameObject.name = "Player [" + next + "]";
#endif
        }
    }
}
