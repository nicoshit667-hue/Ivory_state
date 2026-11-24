using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    public interface ICommerceServer
    {
        void SendDirectory(PlayerEconomy player);
        void Buy(PlayerEconomy player, int commerceId);
        void Sell(PlayerEconomy player, int commerceId);
        void ToggleOpen(PlayerEconomy player, int commerceId);
        void CollectVault(PlayerEconomy player);
    }
    /// <summary>Type de mission en cours, pour le libellé du HUD.</summary>
    public enum MissionKind : byte
    {
        Delivery = 0,
        TaxiPickup = 1,
        TaxiDropoff = 2,
    }

    /// <summary>
    /// Argent et mission de livraison du joueur.
    /// <para>
    /// <b>Seul le serveur écrit</b> (SyncVar en écriture serveur), et <b>seul le propriétaire lit</b> :
    /// personne d'autre ne connaît ton solde. Les méthodes <c>Server*</c> sont appelées par la
    /// logique économique côté serveur (assembly Server) ; un client qui les appellerait ne
    /// modifierait que sa copie locale, immédiatement écrasée par le serveur.
    /// </para>
    /// <para>
    /// Persistance : en mémoire pour l'instant (solde perdu à la déconnexion). À brancher sur
    /// Nakama (portefeuille transactionnel) à l'étape backend.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEconomy : NetworkBehaviour
    {
        public const int NoDelivery = -1;

        private readonly SyncVar<long> _balance =
            new SyncVar<long>(new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<int> _deliveryTargetId =
            new SyncVar<int>(NoDelivery, new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<ushort> _deliveryTimeLimit =
            new SyncVar<ushort>(new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<MissionKind> _missionKind =
            new SyncVar<MissionKind>(new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        public static PlayerEconomy Local { get; private set; }
        public static ICommerceServer CommerceHandler { get; set; }

        /// <summary>Type de la mission en cours (libellé du HUD).</summary>
        public MissionKind Mission => _missionKind.Value;

        /// <summary>Message destiné au joueur local (toast du HUD).</summary>
        public static event Action<string> LocalNotification;
        public static event Action<PlayerEconomy> ServerCommercesChanged;
        public static event Action LocalCommercesChanged;

        private readonly SyncVar<string> _ownedCommercesCsv =
            new SyncVar<string>(string.Empty, new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<long> _commerceVault =
            new SyncVar<long>(0, new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        public string OwnedCommercesCsv => _ownedCommercesCsv.Value ?? string.Empty;
        public long CommerceVault => _commerceVault.Value;

        public long Balance => _balance.Value;
        public int DeliveryTargetId => _deliveryTargetId.Value;
        public bool HasDelivery => _deliveryTargetId.Value != NoDelivery;

        /// <summary>Durée accordée (s) pour la livraison en cours.</summary>
        public ushort DeliveryTimeLimit => _deliveryTimeLimit.Value;

        /// <summary>Heure locale (Time.time) où la mission en cours a été reçue par ce client.</summary>
        public float DeliveryReceivedAt { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            CommerceHandler = null;
            LocalNotification = null;
            ServerBalanceChanged = null;
            ServerCashChanged = null;
            ServerCommercesChanged = null;
            LocalCommercesChanged = null;
            LocalHistoryReceived = null;
        }

        private void Awake()
        {
            _deliveryTargetId.OnChange += OnDeliveryChanged;
            _ownedCommercesCsv.OnChange += OnCommercesChanged;
            _commerceVault.OnChange += OnCommerceVaultChanged;
        }

        private void OnDestroy()
        {
            _deliveryTargetId.OnChange -= OnDeliveryChanged;
            _ownedCommercesCsv.OnChange -= OnCommercesChanged;
            _commerceVault.OnChange -= OnCommerceVaultChanged;
            if (Local == this)
                Local = null;
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

        private void OnCommercesChanged(string previous, string next, bool asServer)
        {
            if (!asServer && IsOwner)
                LocalCommercesChanged?.Invoke();
        }

        private void OnCommerceVaultChanged(long previous, long next, bool asServer)
        {
            if (!asServer && IsOwner)
                LocalCommercesChanged?.Invoke();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _balance.Value = Money.StartingBalance;
        }

        private void OnDeliveryChanged(int previous, int next, bool asServer)
        {
            if (!asServer)
                DeliveryReceivedAt = Time.time;
        }

        #region Serveur

        /// <summary>
        /// Serveur : chaque variation du solde (montant signé, raison). Le service de comptes
        /// la répercute sur le portefeuille Nakama, avec historique.
        /// </summary>
        public static event Action<PlayerEconomy, long, string> ServerBalanceChanged;

        [Server]
        public void ServerAdd(long amount, string reason = "")
        {
            if (amount <= 0)
                return;

            _balance.Value += amount;
            Record(amount, reason);
            ServerBalanceChanged?.Invoke(this, amount, reason);
        }

        #region Historique (appli Pingoin)

        public const int HistorySize = 30;

        private struct Entry
        {
            public long Time;   // secondes Unix
            public long Delta;
            public string Reason;
        }

        private readonly System.Collections.Generic.List<Entry> _history = new System.Collections.Generic.List<Entry>(HistorySize);
        private float _nextHistoryRequest;

        /// <summary>Client : historique reçu (du plus récent au plus ancien) : dates Unix, montants signés, raisons.</summary>
        public static event Action<long[], long[], string[]> LocalHistoryReceived;

        private void Record(long delta, string reason)
        {
            reason = string.IsNullOrEmpty(reason) ? "opération" : reason.Replace('|', ' ').Replace(';', ' ');
            if (reason.Length > 48)
                reason = reason.Substring(0, 48);
            _history.Insert(0, new Entry { Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Delta = delta, Reason = reason });
            if (_history.Count > HistorySize)
                _history.RemoveAt(_history.Count - 1);
        }

        /// <summary>Client : demande l'historique des dépenses et revenus.</summary>
        public void RequestHistory()
        {
            if (IsOwner)
                ServerHistory();
        }

        [ServerRpc]
        private void ServerHistory()
        {
            if (Time.unscaledTime < _nextHistoryRequest)
                return;
            _nextHistoryRequest = Time.unscaledTime + 1f;
            var times = new long[_history.Count];
            var deltas = new long[_history.Count];
            var reasons = new string[_history.Count];
            for (int i = 0; i < _history.Count; i++)
            {
                times[i] = _history[i].Time;
                deltas[i] = _history[i].Delta;
                reasons[i] = _history[i].Reason;
            }
            if (Owner.IsValid)
                TargetHistory(Owner, times, deltas, reasons);
        }

        [TargetRpc]
        private void TargetHistory(NetworkConnection connection, long[] times, long[] deltas, string[] reasons)
        {
            LocalHistoryReceived?.Invoke(times, deltas, reasons);
        }

        /// <summary>Texte compact pour la sauvegarde : « date|montant|raison;… ».</summary>
        [Server]
        public string ServerSerializeHistory()
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < _history.Count; i++)
            {
                if (i > 0)
                    text.Append(';');
                text.Append(_history[i].Time).Append('|').Append(_history[i].Delta).Append('|').Append(_history[i].Reason);
            }
            return text.ToString();
        }

        [Server]
        public void ServerLoadHistory(string data)
        {
            _history.Clear();
            if (string.IsNullOrEmpty(data))
                return;
            foreach (string entry in data.Split(';'))
            {
                string[] parts = entry.Split('|');
                if (parts.Length == 3 && long.TryParse(parts[0], out long time) && long.TryParse(parts[1], out long delta) && _history.Count < HistorySize)
                    _history.Add(new Entry { Time = time, Delta = delta, Reason = parts[2] });
            }
        }

        #endregion

        /// <summary>Débite si le solde suffit. Renvoie faux sans rien changer sinon.</summary>
        [Server]
        public bool ServerTrySpend(long amount, string reason = "")
        {
            if (amount < 0 || _balance.Value < amount)
                return false;

            _balance.Value -= amount;
            if (amount > 0)
            {
                Record(-amount, reason);
                ServerBalanceChanged?.Invoke(this, -amount, reason);
            }
            return true;
        }

        // --- Espèces (argent liquide, en plus du compte Pingoin) ---
        // Les petits services (étals de rue, gbaka…) se paient en espèces ; on retire au guichet Pingoin
        // ou à la banque. Les espèces peuvent être volées (agression), pas le compte Pingoin.
        public const long StartingCash = 2000;

        private readonly SyncVar<long> _cash =
            new SyncVar<long>(StartingCash, new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        /// <summary>Serveur : les espèces ont changé (à sauvegarder tout de suite dans le profil).</summary>
        public static event Action<PlayerEconomy> ServerCashChanged;

        public long Cash => _cash.Value;

        [Server]
        public void ServerSetCash(long cash) => _cash.Value = cash < 0 ? 0 : cash;

        [Server]
        public void ServerAddCash(long amount)
        {
            if (amount <= 0)
                return;
            _cash.Value += amount;
            ServerCashChanged?.Invoke(this);
        }

        /// <summary>Paie en espèces si on en a assez. Renvoie faux sans rien changer sinon.</summary>
        [Server]
        public bool ServerTrySpendCash(long amount)
        {
            if (amount < 0 || _cash.Value < amount)
                return false;
            _cash.Value -= amount;
            if (amount > 0)
                ServerCashChanged?.Invoke(this);
            return true;
        }

        /// <summary>Retrait : du compte Pingoin vers les espèces.</summary>
        [Server]
        public bool ServerWithdraw(long amount)
        {
            if (amount <= 0 || !ServerTrySpend(amount, "retrait d'espèces"))
                return false;
            _cash.Value += amount;
            ServerCashChanged?.Invoke(this);
            return true;
        }

        /// <summary>Dépôt : des espèces vers le compte Pingoin.</summary>
        [Server]
        public bool ServerDeposit(long amount)
        {
            if (amount <= 0 || _cash.Value < amount)
                return false;
            _cash.Value -= amount;
            ServerAdd(amount, "dépôt d'espèces");
            ServerCashChanged?.Invoke(this);
            return true;
        }

        /// <summary>Serveur : solde chargé depuis la sauvegarde (aucun événement : ce n'est pas un gain).</summary>
        [Server]
        public void ServerSetBalance(long balance)
        {
            _balance.Value = balance < 0 ? 0 : balance;
        }

        [Server]
        public void ServerSetDelivery(int targetId, ushort timeLimitSeconds, MissionKind kind = MissionKind.Delivery)
        {
            _missionKind.Value = kind;
            _deliveryTimeLimit.Value = timeLimitSeconds;
            _deliveryTargetId.Value = targetId;
        }

        [Server]
        public void ServerClearDelivery()
        {
            _deliveryTargetId.Value = NoDelivery;
        }

        /// <summary>Affiche un message au joueur (si c'est un vrai joueur connecté).</summary>
        [Server]
        public void ServerNotify(string message)
        {
            if (Owner.IsValid)
                TargetNotify(Owner, message);
        }

        #endregion

        [TargetRpc]
        private void TargetNotify(NetworkConnection connection, string message)
        {
            LocalNotification?.Invoke(message);
        }

        #region Commerces (propriété, caisse, annuaire)

        public void RequestCommerceDirectory() { if (IsOwner) ServerCommerceDirectory(); }
        public void RequestBuyCommerce(int commerceId) { if (IsOwner) ServerBuyCommerce(commerceId); }
        public void RequestSellCommerce(int commerceId) { if (IsOwner) ServerSellCommerce(commerceId); }
        public void RequestToggleCommerceOpen(int commerceId) { if (IsOwner) ServerToggleCommerceOpen(commerceId); }
        public void RequestCollectCommerceVault() { if (IsOwner) ServerCollectCommerceVault(); }

        [ServerRpc] private void ServerCommerceDirectory() => CommerceHandler?.SendDirectory(this);
        [ServerRpc] private void ServerBuyCommerce(int commerceId) => CommerceHandler?.Buy(this, commerceId);
        [ServerRpc] private void ServerSellCommerce(int commerceId) => CommerceHandler?.Sell(this, commerceId);
        [ServerRpc] private void ServerToggleCommerceOpen(int commerceId) => CommerceHandler?.ToggleOpen(this, commerceId);
        [ServerRpc] private void ServerCollectCommerceVault() => CommerceHandler?.CollectVault(this);

        [Server]
        public void ServerLoadCommerces(string csv, long vault, bool notifySave = false)
        {
            _ownedCommercesCsv.Value = csv ?? string.Empty;
            _commerceVault.Value = vault < 0 ? 0 : vault;
            if (notifySave)
                ServerCommercesChanged?.Invoke(this);
        }

        [Server]
        public void ServerAddCommerceRevenue(long amount)
        {
            if (amount <= 0)
                return;
            _commerceVault.Value += amount;
            ServerCommercesChanged?.Invoke(this);
        }

        [Server]
        public void ServerSendCommerceDirectory(int[] ids, string[] ownerNames, bool[] mine, bool[] openStates)
        {
            if (Owner.IsValid)
                TargetCommerceDirectory(Owner, ids, ownerNames, mine, openStates);
        }

        [TargetRpc]
        private void TargetCommerceDirectory(NetworkConnection connection, int[] ids, string[] ownerNames, bool[] mine, bool[] openStates)
        {
            CommerceRegistryClient.ApplySnapshot(ids, ownerNames, mine, openStates);
        }

        #endregion
    }
}
