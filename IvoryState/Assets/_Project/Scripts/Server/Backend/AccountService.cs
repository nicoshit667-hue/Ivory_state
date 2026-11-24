using System;
using System.Collections.Generic;
using System.IO;
using IvoryState.Core.Backend;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;

namespace IvoryState.Server.Backend
{
    /// <summary>
    /// Sauvegarde des joueurs dans Nakama (serveur de jeu uniquement).
    /// <list type="number">
    /// <item><b>Connexion :</b> le jeton envoyé par le client est vérifié auprès de Nakama (GET /v2/account),
    /// puis le profil (argent, satiété, pseudo) est chargé. Un nouveau compte reçoit le solde de départ.</item>
    /// <item><b>Argent :</b> chaque gain ou dépense est appliqué au portefeuille Nakama, dans l'ordre, avec sa
    /// raison (historique consultable dans la console). En cas de coupure réseau : 3 nouvelles tentatives.
    /// Si Nakama refuse, le solde est resynchronisé.</item>
    /// <item><b>Profil :</b> sauvegardé toutes les 60 s et à la déconnexion.</item>
    /// </list>
    /// Sans clé http configurée, le service reste inactif (mode hors-ligne, argent en mémoire).
    /// Clé : variable d'environnement <c>IVORY_NAKAMA_HTTP_KEY</c>, argument <c>-nakamaHttpKey</c>, ou
    /// <c>backend/.env</c> trouvé en remontant depuis le build (confort de développement).
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class AccountService : MonoBehaviour
    {
        private const string LogTag = "Account";
        private const float SaveInterval = 60f;
        private const int MaxAttempts = 3;

        [Serializable] private class LoadRequest { public string user_id; public long starting_balance; public string pseudo; public int studio_count; }
        [Serializable] private class WalletRequest { public string user_id; public long delta; public string reason; }
        [Serializable] private class SaveRequest { public string user_id; public int satiety; public int hydration; public int health; public string pseudo; public string inventory; public int model = -1; public int outfit; public string friends; public string history; public long rent_paid_until; public long rent_debt; public bool homeless; public long aid_at; public int outfits = 1; public bool license; public string cars; public long rental_until; public int rental_paint; public float last_x; public float last_y; public float last_z; public long last_seen; public int primary_home; public long cash; public int xp; public int job_status; public int profile_views; public int profile_privacy; public string commerces; public long commerce_vault; }
        [Serializable] private class ProfileResponse { public double balance; public double satiety; public double hydration = 100; public double health = 100; public string pseudo; public string inventory; public int model = -1; public int outfit; public string friends; public string history; public int studio; public string roommate; public double rent_paid_until; public double rent_debt; public bool homeless; public double aid_at; public int outfits = 1; public bool license; public string cars; public double rental_until; public int rental_paint; public double last_x; public double last_y; public double last_z; public double last_seen; public int primary_home; public double cash = -1; public int xp; public int job_status; public int profile_views; public int profile_privacy; public string commerces; public double commerce_vault; public bool created; }
        [Serializable] private class HomeRequest { public string user_id; public string pseudo; public int studio_count; }
        [Serializable] private class HomeResponse { public int studio; public string roommate; }

        private struct WalletOp
        {
            public string UserId;
            public long Delta;
            public string Reason;
            public PlayerEconomy Economy;
        }

        private readonly Dictionary<string, PlayerAccount> _accountsByUser = new Dictionary<string, PlayerAccount>();
        private readonly Queue<WalletOp> _walletOps = new Queue<WalletOp>();
        private readonly List<PlayerAccount> _saveBuffer = new List<PlayerAccount>();
        private string _baseUrl;
        private string _httpKey;
        private string _serverKey;
        private bool _processing;
        private float _nextSave;

        public bool IsEnabled { get; private set; }

        /// <summary>Un joueur vient d'être lié à son compte (joueur, identifiant Nakama).</summary>
        public static event Action<PlayerAccount, string> PropertyLinked;

        public string BaseUrl => _baseUrl;
        public string HttpKey => _httpKey;

        public void Initialize()
        {
            _baseUrl = ReadSetting("-nakamaUrl", "IVORY_NAKAMA_URL") ?? "http://127.0.0.1:7350";
            _httpKey = ReadSetting("-nakamaHttpKey", "IVORY_NAKAMA_HTTP_KEY") ?? ReadDotEnv("NAKAMA_HTTP_KEY");
            _serverKey = ReadSetting("-nakamaServerKey", "IVORY_NAKAMA_SERVER_KEY") ?? ReadDotEnv("NAKAMA_SERVER_KEY") ?? "ivory-dev-key";

            if (string.IsNullOrEmpty(_httpKey))
            {
                GameLog.Warn(LogTag, "Aucune clé Nakama : mode hors-ligne (l'argent n'est pas sauvegardé).");
                return;
            }

            IsEnabled = true;
            PlayerAccount.ServerAuthenticateHandler = Authenticate;
            PlayerAccount.ServerDeviceHandler = AuthenticateDevice;
            PlayerAccount.ServerEmailHandler = AuthenticateEmail;
            PlayerNetworkIdentity.ServerAppearanceChanged += OnAppearanceChanged;
            PlayerNetworkIdentity.ServerXpChanged += OnXpChanged;
            PlayerNetworkIdentity.ServerProfileMetaChanged += OnProfileMetaChanged;
            PlayerFriends.ServerFriendsChanged += OnFriendsChanged;
            Gameplay.Homes.PlayerHome.ServerRentChanged += OnRentChanged;
            Gameplay.Vehicles.PlayerGarage.ServerChanged += OnGarageChanged;
            PlayerAccount.ServerStopping += OnPlayerStopping;
            PlayerEconomy.ServerBalanceChanged += OnBalanceChanged;
            PlayerEconomy.ServerCashChanged += OnCashChanged;
            PlayerEconomy.ServerCommercesChanged += OnCommercesChanged;
            _nextSave = Time.unscaledTime + SaveInterval;
            GameLog.Info(LogTag, "Sauvegarde Nakama active : " + _baseUrl);
        }

        private void OnDestroy()
        {
            if (!IsEnabled)
                return;

            PlayerAccount.ServerAuthenticateHandler = null;
            PlayerAccount.ServerDeviceHandler = null;
            PlayerAccount.ServerEmailHandler = null;
            PlayerNetworkIdentity.ServerAppearanceChanged -= OnAppearanceChanged;
            PlayerNetworkIdentity.ServerXpChanged -= OnXpChanged;
            PlayerNetworkIdentity.ServerProfileMetaChanged -= OnProfileMetaChanged;
            PlayerFriends.ServerFriendsChanged -= OnFriendsChanged;
            Gameplay.Homes.PlayerHome.ServerRentChanged -= OnRentChanged;
            Gameplay.Vehicles.PlayerGarage.ServerChanged -= OnGarageChanged;
            PlayerAccount.ServerStopping -= OnPlayerStopping;
            PlayerEconomy.ServerBalanceChanged -= OnBalanceChanged;
            PlayerEconomy.ServerCashChanged -= OnCashChanged;
            PlayerEconomy.ServerCommercesChanged -= OnCommercesChanged;
        }

        #region Connexion et chargement

        // async void imposé par le gestionnaire (Action) : toute exception est rattrapée ici, sinon elle
        // se perdrait dans le contexte de synchronisation au lieu d'arriver dans les journaux du serveur.
        private async void Authenticate(PlayerAccount account, string token)
        {
            try
            {
                await AuthenticateAsync(account, token);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Connexion du compte interrompue : " + exception);
            }
        }

        /// <summary>
        /// Connexion par identifiant d'appareil : le serveur de jeu ouvre lui-même la session Nakama
        /// (en local), puis suit le chemin habituel avec le jeton obtenu.
        /// </summary>
        private async void AuthenticateDevice(PlayerAccount account, string deviceId)
        {
            try
            {
                string token = await NakamaHttp.AuthenticateDevice(_baseUrl, _serverKey, deviceId);
                if (account == null || !account.IsSpawned)
                    return;
                if (string.IsNullOrEmpty(token))
                {
                    GameLog.Warn(LogTag, "Session Nakama refusée pour un appareil (clé serveur ?).");
                    account.GetComponent<PlayerEconomy>()?.ServerNotify("Sauvegarde indisponible pour le moment.");
                    return;
                }
                await AuthenticateAsync(account, token);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Connexion par appareil interrompue : " + exception);
            }
        }

        /// <summary>
        /// Inscription (pseudo fourni) ou connexion (pseudo null) par e-mail. Le mot de passe reçu est
        /// déjà une empreinte calculée sur le téléphone. Réponse explicite au joueur dans tous les cas.
        /// </summary>
        private async void AuthenticateEmail(PlayerAccount account, string email, string passwordHash, string pseudo)
        {
            try
            {
                bool create = pseudo != null;
                (NakamaHttp.Result result, string token) = await NakamaHttp.AuthenticateEmail(_baseUrl, _serverKey, email, passwordHash, create, pseudo);
                if (account == null || !account.IsSpawned)
                    return;

                if (string.IsNullOrEmpty(token))
                {
                    string message = result.Status == 409 ? "Ce pseudo est déjà pris : choisis-en un autre."
                        : result.Status == 401 ? (create ? "Cet e-mail a déjà un compte (mot de passe différent)." : "Mot de passe incorrect.")
                        : result.Status == 404 ? "Aucun compte avec cet e-mail : crée un compte."
                        : result.Status == 400 ? "E-mail ou mot de passe refusé."
                        : "Serveur de comptes indisponible, réessaie dans un instant.";
                    GameLog.Warn(LogTag, "Authentification par e-mail refusée (" + result.Status + ") : " + result.Error);
                    account.ServerReply(false, message, false);
                    return;
                }

                await AuthenticateAsync(account, token, pseudo, true);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Authentification par e-mail interrompue : " + exception);
                if (account != null && account.IsSpawned)
                    account.ServerReply(false, "Erreur du serveur de comptes.", false);
            }
        }

        private void OnFriendsChanged(PlayerFriends friends)
        {
            if (friends.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                SaveProfile(account);
        }

        private void OnGarageChanged(Gameplay.Vehicles.PlayerGarage garage)
        {
            if (garage.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                SaveProfile(account);
        }

        private void OnRentChanged(Gameplay.Homes.PlayerHome home)
        {
            if (home.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                SaveProfile(account);
        }

        private void OnCashChanged(PlayerEconomy economy)
        {
            if (economy.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                SaveProfile(account);
        }

        /// <summary>Crédite un joueur déconnecté directement dans son portefeuille Nakama (gain au loto…).</summary>
        public async void ServerCreditOffline(string userId, long amount, string reason)
        {
            if (!IsEnabled || string.IsNullOrEmpty(userId) || amount <= 0)
                return;
            try
            {
                var request = new WalletRequest { user_id = userId, delta = amount, reason = reason };
                NakamaHttp.Result result = await NakamaHttp.Rpc(_baseUrl, _httpKey, "ivory_wallet_apply", JsonUtility.ToJson(request));
                if (!result.Ok)
                    GameLog.Warn(LogTag, "Crédit hors ligne refusé (" + result.Status + ") : " + result.Error);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Crédit hors ligne interrompu : " + exception);
            }
        }

        /// <summary>Libère le studio du joueur dans le registre (SDF, déménagement).</summary>
        public async void ServerReleaseHome(PlayerAccount account)
        {
            if (!IsEnabled || account == null || account.ServerUserId == null)
                return;
            try
            {
                var request = new HomeRequest { user_id = account.ServerUserId };
                NakamaHttp.Result result = await NakamaHttp.Rpc(_baseUrl, _httpKey, "ivory_home_release", JsonUtility.ToJson(request));
                if (!result.Ok)
                    GameLog.Warn(LogTag, "Studio non libéré (" + result.Status + ") : " + result.Error);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Libération du studio interrompue : " + exception);
            }
        }

        /// <summary>Attribue un studio au joueur (ancien SDF) ; le rappel reçoit (studio, colocataire).</summary>
        public async void ServerAssignHome(PlayerAccount account, Action<int, string> done)
        {
            if (!IsEnabled || account == null || account.ServerUserId == null)
            {
                done?.Invoke(0, string.Empty);
                return;
            }
            try
            {
                var request = new HomeRequest
                {
                    user_id = account.ServerUserId,
                    pseudo = account.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "",
                    studio_count = Gameplay.Homes.StudioLayout.Count,
                };
                NakamaHttp.Result result = await NakamaHttp.Rpc(_baseUrl, _httpKey, "ivory_home_assign", JsonUtility.ToJson(request));
                HomeResponse response = result.Ok ? JsonUtility.FromJson<HomeResponse>(result.Body) : null;
                if (account == null || !account.IsSpawned)
                    return;
                done?.Invoke(response != null ? response.studio : 0, response != null ? response.roommate : string.Empty);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Attribution du studio interrompue : " + exception);
                done?.Invoke(0, string.Empty);
            }
        }

        private void OnAppearanceChanged(PlayerNetworkIdentity identity)
        {
            if (identity.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                SaveProfile(account);
        }

        private void OnXpChanged(PlayerNetworkIdentity identity)
        {
            if (identity.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                SaveProfile(account);
        }

        private void OnProfileMetaChanged(PlayerNetworkIdentity identity)
        {
            if (identity.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                SaveProfile(account);
        }

        private void OnCommercesChanged(PlayerEconomy economy)
        {
            if (economy.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                SaveProfile(account);
        }

        private Awaitable AuthenticateAsync(PlayerAccount account, string token) => AuthenticateAsync(account, token, null, false);

        private async Awaitable AuthenticateAsync(PlayerAccount account, string token, string newPseudo, bool reply)
        {
            string userId = await NakamaHttp.GetUserId(_baseUrl, token);
            if (account == null || !account.IsSpawned)
                return;

            PlayerEconomy economy = account.GetComponent<PlayerEconomy>();
            if (userId == null)
            {
                economy?.ServerNotify("Sauvegarde indisponible : jeton refusé.");
                if (reply)
                    account.ServerReply(false, "Session refusée par le serveur de comptes.", false);
                return;
            }

            // Un compte ne joue qu'une fois à la fois : la deuxième connexion est refusée.
            if (_accountsByUser.TryGetValue(userId, out PlayerAccount existing) && existing != null && existing != account)
            {
                GameLog.Warn(LogTag, "Compte " + userId + " déjà connecté : nouvelle connexion refusée.");
                if (reply)
                {
                    account.ServerReply(false, "Ce compte est déjà connecté sur un autre appareil.", false);
                    return;
                }
                account.Owner.Disconnect(true);
                return;
            }

            // Le studio est attribué par Nakama au premier chargement d'un compte qui a un pseudo.
            var request = new LoadRequest
            {
                user_id = userId, starting_balance = Money.StartingBalance,
                pseudo = newPseudo ?? string.Empty, studio_count = Gameplay.Homes.StudioLayout.Count,
            };
            NakamaHttp.Result result = await NakamaHttp.Rpc(_baseUrl, _httpKey, "ivory_load_profile", JsonUtility.ToJson(request));
            if (account == null || !account.IsSpawned)
                return;

            if (!result.Ok)
            {
                GameLog.Error(LogTag, "Chargement du profil impossible (" + result.Status + ") : " + result.Error);
                economy?.ServerNotify("Sauvegarde indisponible pour le moment.");
                return;
            }

            ProfileResponse profile = JsonUtility.FromJson<ProfileResponse>(result.Body);
            if (profile == null)
            {
                GameLog.Error(LogTag, "Profil vide renvoyé par Nakama pour " + userId + ".");
                economy?.ServerNotify("Sauvegarde indisponible pour le moment.");
                return;
            }

            economy?.ServerSetBalance((long)profile.balance);
            PlayerNeeds loadedNeeds = account.GetComponent<PlayerNeeds>();
            if (loadedNeeds != null)
            {
                loadedNeeds.ServerSetSatiety((int)profile.satiety);
                loadedNeeds.ServerSetHydration((int)profile.hydration);
                // Jamais de reconnexion « mourant » : au moins 20 de santé.
                loadedNeeds.ServerSetHealth(Mathf.Max(20, (int)profile.health));
            }
            PlayerNetworkIdentity identity = account.GetComponent<PlayerNetworkIdentity>();
            string pseudo = !string.IsNullOrEmpty(profile.pseudo) ? profile.pseudo : newPseudo;
            if (!string.IsNullOrEmpty(pseudo))
                identity?.ServerApplyName(pseudo);
            if (account.TryGetComponent(out PlayerInventory bag))
                bag.ServerLoad(profile.inventory);
            if (account.TryGetComponent(out PlayerFriends friends))
                friends.ServerLoad(profile.friends);
            economy?.ServerLoadHistory(profile.history);
            economy?.ServerSetCash(profile.cash < 0 ? PlayerEconomy.StartingCash : (long)profile.cash);
            if (account.TryGetComponent(out Gameplay.Homes.PlayerHome home))
            {
                home.ServerSetHome(profile.homeless ? 0 : profile.studio, profile.roommate);
                home.ServerLoadRent((long)profile.rent_paid_until, (long)profile.rent_debt, profile.homeless, (long)profile.aid_at);
                home.ServerLoadPrimary(profile.primary_home);
            }
            bool hasAppearance = profile.model >= 0 && profile.model < PlayerNetworkIdentity.NoAppearance;
            if (hasAppearance)
                identity?.ServerApplyAppearance((byte)profile.model, (byte)Mathf.Clamp(profile.outfit, 0, PlayerNetworkIdentity.OutfitCount - 1));
            if (account.TryGetComponent(out Gameplay.Vehicles.PlayerGarage garage))
                garage.ServerLoad(profile.license, profile.cars, (long)profile.rental_until, (byte)Mathf.Clamp(profile.rental_paint, 0, 255));
            // Garde-robe : tenues achetées, plus celle qu'il porte (choisie à la création).
            identity?.ServerLoadOutfits(profile.outfits);
            if (hasAppearance)
                identity?.ServerGrantOutfit(Mathf.Clamp(profile.outfit, 0, PlayerNetworkIdentity.OutfitCount - 1));

            account.ServerMarkLinked(userId);
            _accountsByUser[userId] = account;

            GameLog.Info(LogTag, "Compte " + userId + " lié, solde " + (long)profile.balance + " FCFA.");
            string welcome = profile.created || newPseudo != null && string.IsNullOrEmpty(profile.pseudo)
                ? "Compte créé, bienvenue à Ivory State !"
                : "Bon retour ! Solde : " + Money.Format((long)profile.balance);
            economy?.ServerNotify(welcome);
            if (reply)
                account.ServerReply(true, welcome, !hasAppearance);

            // Nouveau compte : pseudo enregistré tout de suite (même si le joueur quitte avant 60 s).
            if (newPseudo != null && string.IsNullOrEmpty(profile.pseudo))
                SaveProfile(account);

            // Nouveau joueur : son studio l'attend, le GPS l'y conduit.
            bool newcomer = profile.created || newPseudo != null && string.IsNullOrEmpty(profile.pseudo);
            Gameplay.Homes.HomeEntrance residence = Gameplay.Homes.HomeEntrance.Residence;
            if (newcomer && home != null && home.HasHome && residence != null)
            {
                if (account.TryGetComponent(out PlayerServices services))
                    services.ServerSuggestWaypoint(residence.transform.position);
                economy?.ServerNotify("Ton studio t'attend à la " + residence.DisplayName + " (Treichville). Suis le trait bleu du GPS.");
            }

            // Biens immobiliers (location, achat) : retrouvés dans le registre chargé au démarrage.
            PropertyLinked?.Invoke(account, userId);

            // Progression / niveau : charge l'XP sauvegardée, ou calcule un socle initial pour les anciens comptes.
            if (identity != null)
            {
                int initialXp = profile.xp > 0
                    ? profile.xp
                    : PlayerProgression.ComputeLegacyBaseXp(
                        (economy != null ? economy.Balance + economy.Cash : 0L),
                        garage != null ? garage.Cars.Count : 0,
                        garage != null && garage.HasLicense,
                        home != null ? home.PropertyCount : 0,
                        identity.ServerOwnedOutfits);
                identity.ServerLoadXp(initialXp);
                identity.ServerLoadProfileMeta((byte)profile.job_status, profile.profile_views, profile.profile_privacy);
            }
            economy?.ServerLoadCommerces(profile.commerces, (long)profile.commerce_vault);

            // Reconnexion : chez soi après au moins un jour d'absence, sinon là où on s'était arrêté.
            if (!newcomer && profile.last_seen > 0 && home != null && account.TryGetComponent(out PlayerInteractor interactor))
            {
                bool longAbsence = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)profile.last_seen >= 24 * 3600;
                var last = new Vector3((float)profile.last_x, (float)profile.last_y, (float)profile.last_z);
                Vector3? spot = Homes.HomeService.ResolveReconnect(home, last, longAbsence);
                if (spot.HasValue)
                    interactor.ServerTravel(spot.Value, longAbsence ? "De retour à la maison" : "Bon retour");
            }
        }

        #endregion

        #region Portefeuille

        private void OnBalanceChanged(PlayerEconomy economy, long delta, string reason)
        {
            if (!economy.TryGetComponent(out PlayerAccount account) || account.ServerUserId == null)
                return; // pas (encore) lié : rien à sauvegarder

            _walletOps.Enqueue(new WalletOp { UserId = account.ServerUserId, Delta = delta, Reason = reason, Economy = economy });
            if (!_processing)
                ProcessWalletOps();
        }

        /// <summary>Applique les opérations une par une, dans l'ordre (un seul traitement à la fois).</summary>
        private async void ProcessWalletOps()
        {
            _processing = true;
            try
            {
                while (_walletOps.Count > 0)
                {
                    WalletOp op = _walletOps.Peek();
                    try
                    {
                        await ApplyWalletOp(op);
                    }
                    catch (Exception exception)
                    {
                        // Une opération en échec ne doit pas bloquer celles qui suivent dans la file.
                        GameLog.Error(LogTag, "Opération de portefeuille abandonnée (" + op.Delta + ", " + op.Reason + ") : " + exception);
                    }
                    _walletOps.Dequeue();
                }
            }
            finally
            {
                _processing = false;
            }
        }

        private async Awaitable ApplyWalletOp(WalletOp op)
        {
            var request = new WalletRequest { user_id = op.UserId, delta = op.Delta, reason = op.Reason ?? "" };
            string payload = JsonUtility.ToJson(request);

            NakamaHttp.Result result = default;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                result = await NakamaHttp.Rpc(_baseUrl, _httpKey, "ivory_wallet_apply", payload);
                // Réponse du serveur (même un refus) : inutile d'insister.
                if (result.Ok || result.Status >= 400)
                    break;
                await Awaitable.WaitForSecondsAsync(attempt);
            }

            if (!result.Ok)
            {
                GameLog.Error(LogTag, "Portefeuille non appliqué (" + op.Delta + ", " + op.Reason + ") : " +
                                      result.Status + " " + result.Error + ". Resynchronisation.");
                await Resync(op.UserId, op.Economy);
            }
        }

        private async Awaitable Resync(string userId, PlayerEconomy economy)
        {
            var request = new LoadRequest { user_id = userId, starting_balance = Money.StartingBalance };
            NakamaHttp.Result result = await NakamaHttp.Rpc(_baseUrl, _httpKey, "ivory_load_profile", JsonUtility.ToJson(request));
            if (!result.Ok || economy == null)
                return;

            ProfileResponse profile = JsonUtility.FromJson<ProfileResponse>(result.Body);
            if (profile != null)
                economy.ServerSetBalance((long)profile.balance);
        }

        #endregion

        #region Profil

        private void Update()
        {
            if (!IsEnabled)
                return;
            RetryPendingSaves();
            if (Time.unscaledTime < _nextSave)
                return;
            _nextSave = Time.unscaledTime + SaveInterval;

            _saveBuffer.Clear();
            foreach (PlayerAccount account in _accountsByUser.Values)
            {
                if (account != null)
                    _saveBuffer.Add(account);
            }

            for (int i = 0; i < _saveBuffer.Count; i++)
                SaveProfile(_saveBuffer[i]);
        }

        private void OnPlayerStopping(PlayerAccount account)
        {
            SaveProfile(account);
            _accountsByUser.Remove(account.ServerUserId);
        }

        /// <summary>Les valeurs sont capturées tout de suite : le joueur peut disparaître avant l'envoi.</summary>
        private void SaveProfile(PlayerAccount account)
        {
            var request = new SaveRequest
            {
                user_id = account.ServerUserId,
                satiety = account.TryGetComponent(out PlayerNeeds needs) ? needs.Satiety : PlayerNeeds.MaxSatiety,
                hydration = needs != null ? needs.Hydration : PlayerNeeds.MaxHydration,
                health = needs != null ? needs.Health : PlayerNeeds.MaxHealth,
                pseudo = account.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "",
                inventory = account.TryGetComponent(out PlayerInventory bag) ? bag.ServerSerialize() : "",
                model = identity != null && identity.HasAppearance ? identity.Model : -1,
                outfit = identity != null ? identity.Outfit : 0,
                outfits = identity != null ? identity.ServerOwnedOutfits : 1,
                friends = account.TryGetComponent(out PlayerFriends friends) ? friends.ServerSerialize() : "",
                history = account.TryGetComponent(out PlayerEconomy wallet) ? wallet.ServerSerializeHistory() : "",
                xp = identity != null ? identity.Xp : 0,
                job_status = identity != null ? (int)identity.JobStatus : 0,
                profile_views = identity != null ? identity.ProfileViews : 0,
                profile_privacy = identity != null ? identity.ProfilePrivacy : 0,
                commerces = wallet != null ? wallet.OwnedCommercesCsv : "",
                commerce_vault = wallet != null ? wallet.CommerceVault : 0,
            };
            request.cash = wallet != null ? wallet.Cash : 0;
            // Dernière position (au volant : celle du véhicule) et heure, pour la reconnexion.
            Vector3 position = account.transform.position;
            if (account.TryGetComponent(out Gameplay.Vehicles.PlayerDriving driving) && driving.Vehicle != null)
                position = driving.Vehicle.transform.position;
            request.last_x = position.x;
            request.last_y = position.y;
            request.last_z = position.z;
            request.last_seen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (account.TryGetComponent(out Gameplay.Vehicles.PlayerGarage garage))
            {
                request.license = garage.HasLicense;
                request.cars = garage.CarsText;
                request.rental_until = garage.RentalUntil;
                request.rental_paint = garage.RentalPaint;
            }
            if (account.TryGetComponent(out Gameplay.Homes.PlayerHome home) && home.ServerRentLoaded)
            {
                request.primary_home = home.ServerPrimaryChoice;
                request.rent_paid_until = home.ServerPaidUntil;
                request.rent_debt = home.RentDebt;
                request.homeless = home.IsHomeless;
                request.aid_at = home.ServerAidAt;
            }

            EnqueueSave(request.user_id, JsonUtility.ToJson(request), ++_saveSequence);
        }

        // Sauvegardes sérialisées par joueur et renvoyées en cas d'échec (Nakama injoignable, délai dépassé) :
        // un seul envoi en cours par compte (évite qu'une rafale de retraits ne s'écrase dans le désordre) et
        // nouvel essai toutes les 10 s, même après la déconnexion.
        private const float RetryInterval = 10f;
        private readonly Dictionary<string, long> _latestSave = new Dictionary<string, long>();
        private readonly HashSet<string> _inFlightSaves = new HashSet<string>();
        private readonly Dictionary<string, (string json, long sequence)> _queuedSaves = new Dictionary<string, (string, long)>();
        private readonly Dictionary<string, (string json, long sequence)> _pendingSaves = new Dictionary<string, (string, long)>();
        private readonly List<string> _retryBuffer = new List<string>();
        private long _saveSequence;
        private float _nextRetry;

        private void EnqueueSave(string userId, string json, long sequence)
        {
            if (string.IsNullOrEmpty(userId))
                return;
            if (!_latestSave.TryGetValue(userId, out long latest) || sequence > latest)
                _latestSave[userId] = sequence;
            if (_inFlightSaves.Contains(userId))
            {
                if (!_queuedSaves.TryGetValue(userId, out var existing) || sequence >= existing.sequence)
                    _queuedSaves[userId] = (json, sequence);
                return;
            }
            _pendingSaves.Remove(userId);
            SendSave(userId, json, sequence);
        }

        private async void SendSave(string userId, string json, long sequence)
        {
            if (string.IsNullOrEmpty(userId))
                return;
            _inFlightSaves.Add(userId);
            bool ok = false;
            try
            {
                NakamaHttp.Result result = await NakamaHttp.Rpc(_baseUrl, _httpKey, "ivory_save_profile", json);
                ok = result.Ok;
                if (!ok)
                    GameLog.Warn(LogTag, "Profil non sauvegardé (" + result.Status + ") : " + result.Error + " — nouvel essai dans " + RetryInterval + " s.");
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Sauvegarde du profil interrompue : " + exception);
            }
            finally
            {
                _inFlightSaves.Remove(userId);
            }

            // Une photo plus récente a été prise pendant l'envoi : on l'envoie tout de suite.
            if (_queuedSaves.TryGetValue(userId, out var queued))
            {
                _queuedSaves.Remove(userId);
                SendSave(userId, queued.json, queued.sequence);
                return;
            }

            if (ok)
            {
                if (_pendingSaves.TryGetValue(userId, out var pending) && pending.sequence <= sequence)
                    _pendingSaves.Remove(userId);
                return;
            }
            // Ne garder que la plus récente : une ancienne photo ne doit pas écraser une plus neuve.
            if (_latestSave.TryGetValue(userId, out long newest) && newest == sequence)
                _pendingSaves[userId] = (json, sequence);
        }

        private void RetryPendingSaves()
        {
            if (_pendingSaves.Count == 0 || Time.unscaledTime < _nextRetry)
                return;
            _nextRetry = Time.unscaledTime + RetryInterval;
            _retryBuffer.Clear();
            _retryBuffer.AddRange(_pendingSaves.Keys);
            foreach (string userId in _retryBuffer)
            {
                (string json, long sequence) = _pendingSaves[userId];
                _pendingSaves.Remove(userId);
                if (_latestSave.TryGetValue(userId, out long newest) && newest > sequence)
                    continue;
                EnqueueSave(userId, json, sequence);
            }
        }

        #endregion

        #region Configuration

        private static string ReadSetting(string argument, string environmentVariable)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, argument);
            if (index >= 0 && index + 1 < args.Length)
                return args[index + 1];

            string value = Environment.GetEnvironmentVariable(environmentVariable);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>Développement : lit backend/.env en remontant depuis le dossier du build ou du projet.</summary>
        private static string ReadDotEnv(string key)
        {
            DirectoryInfo directory = new DirectoryInfo(Application.dataPath);
            for (int depth = 0; depth < 6 && directory != null; depth++, directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, "backend", ".env");
                if (!File.Exists(path))
                    continue;

                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.StartsWith(key + "=", StringComparison.Ordinal))
                        return line.Substring(key.Length + 1).Trim();
                }
            }

            return null;
        }

        #endregion
    }
}
