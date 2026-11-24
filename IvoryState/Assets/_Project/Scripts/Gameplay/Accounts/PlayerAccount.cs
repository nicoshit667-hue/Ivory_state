using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace IvoryState.Gameplay.Accounts
{
    /// <summary>
    /// Lien entre un joueur connecté et son compte Nakama (sauvegarde de l'argent, du pseudo, de la satiété).
    /// <para>
    /// Le client envoie son jeton de session, et le serveur le vérifie auprès de Nakama : un jeton
    /// inventé ou celui d'un autre joueur est refusé. Le service de comptes (assembly Server) charge
    /// ensuite le profil, puis sauvegarde chaque changement.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAccount : NetworkBehaviour
    {
        private readonly SyncVar<bool> _linked =
            new SyncVar<bool>(new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private float _nextAttempt;

        /// <summary>Vrai une fois le profil chargé depuis le serveur de sauvegarde.</summary>
        public bool IsLinked => _linked.Value;

        /// <summary>Identifiant du compte (serveur uniquement, null si non lié).</summary>
        public string ServerUserId { get; private set; }

        public static PlayerAccount Local { get; private set; }

        /// <summary>Gestionnaire serveur (vérification du jeton, chargement), enregistré par l'assembly Server.</summary>
        public static Action<PlayerAccount, string> ServerAuthenticateHandler { get; set; }

        /// <summary>Levé côté serveur juste avant la disparition du joueur (sauvegarde finale).</summary>
        public static event Action<PlayerAccount> ServerStopping;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            ServerAuthenticateHandler = null;
            ServerDeviceHandler = null;
            ServerEmailHandler = null;
            LocalAuthResult = null;
            ServerStopping = null;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
                Local = this;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local == this)
                Local = null;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (ServerUserId != null)
                ServerStopping?.Invoke(this);
        }

        /// <summary>Client : transmet le jeton de session Nakama au serveur de jeu.</summary>
        public void SendSessionToken(string token)
        {
            if (IsOwner && !string.IsNullOrEmpty(token))
                ServerAuthenticate(token);
        }

        [ServerRpc]
        private void ServerAuthenticate(string token)
        {
            // Une tentative toutes les 3 s au plus ; un compte déjà lié ne se relie pas.
            if (ServerUserId != null || Time.unscaledTime < _nextAttempt || token == null || token.Length > 4096)
                return;
            _nextAttempt = Time.unscaledTime + 3f;

            ServerAuthenticateHandler?.Invoke(this, token);
        }

        /// <summary>
        /// Gestionnaire serveur de la connexion par identifiant d'appareil : le serveur de jeu
        /// s'authentifie lui-même auprès de Nakama (en local). Nakama n'a donc pas besoin d'être
        /// accessible depuis Internet : un seul port (UDP, celui du jeu) suffit pour jouer en ligne.
        /// </summary>
        public static Action<PlayerAccount, string> ServerDeviceHandler { get; set; }

        /// <summary>Client : transmet l'identifiant d'installation (GUID tiré au premier lancement).</summary>
        public void SendDeviceId(string deviceId)
        {
            if (IsOwner && IsValidDeviceId(deviceId))
                ServerAuthenticateDevice(deviceId);
        }

        [ServerRpc]
        private void ServerAuthenticateDevice(string deviceId)
        {
            if (ServerUserId != null || Time.unscaledTime < _nextAttempt || !IsValidDeviceId(deviceId))
                return;
            _nextAttempt = Time.unscaledTime + 3f;

            ServerDeviceHandler?.Invoke(this, deviceId);
        }

        #region Compte par e-mail

        /// <summary>
        /// Gestionnaire serveur de l'inscription / connexion par e-mail : (compte, e-mail, empreinte du mot
        /// de passe, pseudo — null pour une connexion). Enregistré par le service de comptes ; absent en
        /// mode hors-ligne (le serveur accepte alors le pseudo sans sauvegarde).
        /// </summary>
        public static Action<PlayerAccount, string, string, string> ServerEmailHandler { get; set; }

        /// <summary>Client : réponse du serveur (succès, message, création du personnage nécessaire).</summary>
        public static event Action<bool, string, bool> LocalAuthResult;

        /// <summary>Client : création de compte. <paramref name="passwordHash"/> : voir <see cref="HashPassword"/>.</summary>
        public void Register(string email, string passwordHash, string pseudo)
        {
            if (IsOwner)
                ServerEmail(email ?? "", passwordHash ?? "", pseudo ?? "");
        }

        /// <summary>Client : connexion à un compte existant.</summary>
        public void Login(string email, string passwordHash)
        {
            if (IsOwner)
                ServerEmail(email ?? "", passwordHash ?? "", null);
        }

        [ServerRpc]
        private void ServerEmail(string email, string passwordHash, string pseudo)
        {
            if (ServerUserId != null || Time.unscaledTime < _nextAttempt)
                return;
            _nextAttempt = Time.unscaledTime + 2f;

            email = email.Trim().ToLowerInvariant();
            if (email.Length < 5 || email.Length > 120 || email.IndexOf('@') < 1 || email.IndexOf('.') < 0)
            {
                ServerReply(false, "Adresse e-mail invalide.", false);
                return;
            }
            if (passwordHash.Length != 64)
            {
                ServerReply(false, "Mot de passe invalide.", false);
                return;
            }
            if (pseudo != null && Social.PlayerNames.ValidateAccountPseudo(pseudo) == null)
            {
                ServerReply(false, "Pseudo : 3 à 16 lettres, sans chiffre ni espace.", false);
                return;
            }

            if (ServerEmailHandler != null)
            {
                ServerEmailHandler(this, email, passwordHash, pseudo);
                return;
            }

            // Hors-ligne (pas de Nakama) : pseudo appliqué, rien n'est sauvegardé.
            if (pseudo != null && TryGetComponent(out Player.PlayerNetworkIdentity identity))
                identity.ServerApplyName(pseudo);
            ServerReply(true, "Mode hors-ligne : ta progression ne sera pas sauvegardée.", true);
        }

        /// <summary>Serveur : réponse au propriétaire (résultat de l'inscription ou de la connexion).</summary>
        [Server]
        public void ServerReply(bool ok, string message, bool needsAppearance)
        {
            if (Owner.IsValid)
                TargetAuthResult(Owner, ok, message ?? "", needsAppearance);
        }

        [TargetRpc]
        private void TargetAuthResult(FishNet.Connection.NetworkConnection connection, bool ok, string message, bool needsAppearance)
        {
            LocalAuthResult?.Invoke(ok, message, needsAppearance);
        }

        /// <summary>
        /// Empreinte du mot de passe calculée sur le téléphone (SHA-256 de l'e-mail et du mot de passe) :
        /// le mot de passe lui-même ne circule jamais. La connexion de jeu n'étant pas chiffrée, cela
        /// évite qu'un mot de passe réutilisé ailleurs puisse être lu sur le réseau.
        /// </summary>
        public static string HashPassword(string email, string password)
        {
            string salted = (email ?? "").Trim().ToLowerInvariant() + ":" + (password ?? "") + ":ivory-state";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(salted));
                var hex = new System.Text.StringBuilder(64);
                foreach (byte b in digest)
                    hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        #endregion

        /// <summary>Identifiant accepté : 10 à 128 caractères, lettres, chiffres et tirets (format Nakama).</summary>
        public static bool IsValidDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || deviceId.Length < 10 || deviceId.Length > 128)
                return false;
            foreach (char c in deviceId)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
                if (!ok)
                    return false;
            }
            return true;
        }

        [Server]
        public void ServerMarkLinked(string userId)
        {
            ServerUserId = userId;
            _linked.Value = true;
        }
    }
}
