using System;
using System.Collections.Generic;
using IvoryState.Core.Backend;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Server.Backend;
using UnityEngine;

namespace IvoryState.Server.Economy
{
    /// <summary>
    /// Registre et gestion serveur des commerces d'Abidjan (maquis, restaurants, fast-foods,
    /// boutiques et pharmacies) : achat, revente, ouverture/fermeture, encaissement des commissions
    /// et persistance Nakama (<c>ivory_commerces_load</c> / <c>ivory_commerce_save</c>).
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class CommerceService : MonoBehaviour, ICommerceServer
    {
        private const string LogTag = "Commerces";

        [Serializable] private class LoadRequest { public int count; }
        [Serializable] private class LoadedCommerce { public int id; public string owner; public string owner_name; public bool is_open = true; public long vault; }
        [Serializable] private class LoadResponse { public LoadedCommerce[] commerces; }
        [Serializable] private class SaveRequest { public int id; public string owner; public string owner_name; public bool is_open; public long vault; }

        private sealed class Entry
        {
            public string Owner = string.Empty;
            public string OwnerName = string.Empty;
            public bool IsOpen = true;
            public long Vault;
            public bool IsFree => string.IsNullOrEmpty(Owner);
        }

        private readonly Dictionary<int, Entry> _registry = new Dictionary<int, Entry>();
        private AccountService _accounts;
        private bool _loaded;

        public static CommerceService Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            for (int i = 0; i < CommerceCatalog.All.Length; i++)
                _registry[CommerceCatalog.All[i].Id] = new Entry();
        }

        private void OnEnable()
        {
            PlayerEconomy.CommerceHandler = this;
            AccountService.PropertyLinked += OnLinked;
        }

        private void OnDisable()
        {
            if (PlayerEconomy.CommerceHandler == (ICommerceServer)this)
                PlayerEconomy.CommerceHandler = null;
            AccountService.PropertyLinked -= OnLinked;
            if (Instance == this)
                Instance = null;
        }

        private async void Start()
        {
            _accounts = GetComponent<AccountService>();
            if (_accounts == null || !_accounts.IsEnabled)
            {
                _loaded = true;
                return;
            }

            try
            {
                var req = new LoadRequest { count = CommerceCatalog.Count };
                NakamaHttp.Result result = await NakamaHttp.Rpc(_accounts.BaseUrl, _accounts.HttpKey, "ivory_commerces_load", JsonUtility.ToJson(req));
                if (!result.Ok)
                {
                    GameLog.Warn(LogTag, "Registre commerces illisible (" + result.Status + ") : mode mémoire.");
                    _loaded = true;
                    return;
                }

                string body = result.Body.Replace("\"commerces\":{}", "\"commerces\":[]");
                LoadResponse response = JsonUtility.FromJson<LoadResponse>(body);
                if (response?.commerces != null)
                {
                    foreach (LoadedCommerce item in response.commerces)
                    {
                        if (!CommerceCatalog.IsValid(item.id) || !_registry.TryGetValue(item.id, out Entry entry))
                            continue;
                        entry.Owner = item.owner ?? string.Empty;
                        entry.OwnerName = item.owner_name ?? string.Empty;
                        entry.IsOpen = item.is_open;
                        entry.Vault = Math.Max(0L, item.vault);
                    }
                }
                _loaded = true;

                foreach (PlayerNetworkIdentity identity in PlayerNetworkIdentity.ServerInstances)
                {
                    if (identity != null && identity.TryGetComponent(out PlayerAccount acc) && acc.ServerUserId != null)
                        OnLinked(acc, acc.ServerUserId);
                }
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Chargement des commerces interrompu : " + exception);
                _loaded = true;
            }
        }

        private void OnLinked(PlayerAccount account, string userId)
        {
            if (!_loaded || account == null || !account.TryGetComponent(out PlayerEconomy economy))
                return;

            var owned = new List<int>();
            long vaultTotal = economy.CommerceVault;
            foreach (KeyValuePair<int, Entry> pair in _registry)
            {
                if (pair.Value.Owner == userId)
                {
                    owned.Add(pair.Key);
                    vaultTotal += pair.Value.Vault;
                    if (pair.Value.Vault > 0)
                    {
                        pair.Value.Vault = 0;
                        Persist(pair.Key, pair.Value);
                    }
                }
            }
            owned.Sort();
            economy.ServerLoadCommerces(CommerceCatalog.SerializeIds(owned), vaultTotal);
            SendDirectory(economy);
        }

        public bool IsCommerceOpen(int commerceId)
        {
            return !_registry.TryGetValue(commerceId, out Entry entry) || entry.IsFree || entry.IsOpen;
        }

        public string GetOwnerName(int commerceId)
        {
            return _registry.TryGetValue(commerceId, out Entry entry) ? entry.OwnerName : string.Empty;
        }

        public void RecordSale(int commerceId, long salePrice)
        {
            if (!_registry.TryGetValue(commerceId, out Entry entry) || entry.IsFree || !entry.IsOpen)
                return;
            if (!CommerceCatalog.TryGet(commerceId, out CommerceDefinition def))
                return;

            long commission = Math.Max(25L, (salePrice * def.CommissionPercent) / 100L);
            PlayerEconomy onlineOwner = FindOnlineEconomy(entry.Owner);
            if (onlineOwner != null)
            {
                onlineOwner.ServerAddCommerceRevenue(commission);
            }
            else
            {
                entry.Vault += commission;
                Persist(commerceId, entry);
            }
        }

        public void SendDirectory(PlayerEconomy player)
        {
            if (player == null)
                return;
            string userId = UserIdOf(player);
            int count = CommerceCatalog.All.Length;
            var ids = new int[count];
            var ownerNames = new string[count];
            var mine = new bool[count];
            var openStates = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int id = CommerceCatalog.All[i].Id;
                ids[i] = id;
                if (_registry.TryGetValue(id, out Entry entry))
                {
                    ownerNames[i] = entry.OwnerName ?? string.Empty;
                    mine[i] = !string.IsNullOrEmpty(userId) && entry.Owner == userId;
                    openStates[i] = entry.IsFree || entry.IsOpen;
                }
                else
                {
                    ownerNames[i] = string.Empty;
                    openStates[i] = true;
                }
            }
            player.ServerSendCommerceDirectory(ids, ownerNames, mine, openStates);
        }

        public void Buy(PlayerEconomy player, int commerceId)
        {
            if (!_loaded || player == null || !CommerceCatalog.TryGet(commerceId, out CommerceDefinition def))
                return;
            string userId = UserIdOf(player);
            if (string.IsNullOrEmpty(userId))
            {
                player.ServerNotify("Compte non lié : impossible d'acheter un commerce.");
                return;
            }
            if (!_registry.TryGetValue(commerceId, out Entry entry) || !entry.IsFree)
            {
                player.ServerNotify("Ce commerce appartient déjà à " + (entry != null ? entry.OwnerName : "un autre joueur") + ".");
                SendDirectory(player);
                return;
            }

            List<int> owned = CommerceCatalog.ParseIds(player.OwnedCommercesCsv);
            if (owned.Contains(commerceId))
                return;
            if (owned.Count >= CommerceCatalog.MaxOwnedCommerces)
            {
                player.ServerNotify("Tu gères déjà " + CommerceCatalog.MaxOwnedCommerces + " commerces (maximum).");
                return;
            }

            if (!player.ServerTrySpend(def.PurchasePrice, "achat commerce : " + def.Name))
            {
                player.ServerNotify("Pas assez d'argent : " + Money.Format(def.PurchasePrice) + ".");
                return;
            }

            string ownerName = player.TryGetComponent(out PlayerNetworkIdentity idComp) ? idComp.DisplayName : "Commerçant";
            entry.Owner = userId;
            entry.OwnerName = ownerName;
            entry.IsOpen = true;
            entry.Vault = 0;
            Persist(commerceId, entry);

            owned.Add(commerceId);
            owned.Sort();
            player.ServerLoadCommerces(CommerceCatalog.SerializeIds(owned), player.CommerceVault, notifySave: true);
            idComp?.ServerAddXp(50, "commerce");
            player.ServerNotify("Félicitations ! Tu gères désormais " + def.Name + " (" + def.CommissionPercent + "% par vente).");
            BroadcastDirectories();
        }

        public void Sell(PlayerEconomy player, int commerceId)
        {
            if (!_loaded || player == null || !CommerceCatalog.TryGet(commerceId, out CommerceDefinition def))
                return;
            string userId = UserIdOf(player);
            if (string.IsNullOrEmpty(userId) || !_registry.TryGetValue(commerceId, out Entry entry) || entry.Owner != userId)
            {
                player.ServerNotify("Tu ne possèdes pas ce commerce.");
                return;
            }

            long refund = (def.PurchasePrice * 65L) / 100L;
            long vault = player.CommerceVault + entry.Vault;
            entry.Owner = string.Empty;
            entry.OwnerName = string.Empty;
            entry.IsOpen = true;
            entry.Vault = 0;
            Persist(commerceId, entry);

            List<int> owned = CommerceCatalog.ParseIds(player.OwnedCommercesCsv);
            owned.Remove(commerceId);
            player.ServerLoadCommerces(CommerceCatalog.SerializeIds(owned), vault, notifySave: true);
            player.ServerAdd(refund, "vente commerce : " + def.Name);
            player.ServerNotify(def.Name + " revendu : +" + Money.Format(refund) + ".");
            BroadcastDirectories();
        }

        public void ToggleOpen(PlayerEconomy player, int commerceId)
        {
            if (!_loaded || player == null || !CommerceCatalog.TryGet(commerceId, out CommerceDefinition def))
                return;
            string userId = UserIdOf(player);
            if (string.IsNullOrEmpty(userId) || !_registry.TryGetValue(commerceId, out Entry entry) || entry.Owner != userId)
                return;

            entry.IsOpen = !entry.IsOpen;
            Persist(commerceId, entry);
            player.ServerNotify(def.Name + (entry.IsOpen ? " est maintenant OUVERT." : " est maintenant FERMÉ."));
            BroadcastDirectories();
        }

        public void CollectVault(PlayerEconomy player)
        {
            if (player == null)
                return;
            long amount = player.CommerceVault;
            if (amount <= 0)
            {
                player.ServerNotify("La caisse de tes commerces est vide.");
                return;
            }
            player.ServerLoadCommerces(player.OwnedCommercesCsv, 0, notifySave: true);
            player.ServerAdd(amount, "recettes commerces");
            player.ServerNotify("Recettes encaissées : +" + Money.Format(amount) + " sur ton compte Pingoin.");
        }

        private void BroadcastDirectories()
        {
            foreach (PlayerNetworkIdentity id in PlayerNetworkIdentity.ServerInstances)
            {
                if (id != null && id.Owner.IsValid && id.TryGetComponent(out PlayerEconomy eco))
                    SendDirectory(eco);
            }
        }

        private static string UserIdOf(PlayerEconomy player)
        {
            if (player.TryGetComponent(out PlayerAccount acc) && !string.IsNullOrEmpty(acc.ServerUserId))
                return acc.ServerUserId;
            return player.TryGetComponent(out PlayerNetworkIdentity id) ? "local:" + id.ObjectId : string.Empty;
        }

        private static PlayerEconomy FindOnlineEconomy(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return null;
            foreach (PlayerNetworkIdentity id in PlayerNetworkIdentity.ServerInstances)
            {
                if (id != null && id.TryGetComponent(out PlayerEconomy eco) && UserIdOf(eco) == userId)
                    return eco;
            }
            return null;
        }

        private async void Persist(int commerceId, Entry entry)
        {
            if (_accounts == null || !_accounts.IsEnabled)
                return;
            try
            {
                var req = new SaveRequest
                {
                    id = commerceId,
                    owner = entry.Owner ?? string.Empty,
                    owner_name = entry.OwnerName ?? string.Empty,
                    is_open = entry.IsOpen,
                    vault = entry.Vault,
                };
                NakamaHttp.Result result = await NakamaHttp.Rpc(_accounts.BaseUrl, _accounts.HttpKey, "ivory_commerce_save", JsonUtility.ToJson(req));
                if (!result.Ok)
                    GameLog.Warn(LogTag, "Commerce " + commerceId + " non sauvegardé (" + result.Status + ") : " + result.Error);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Sauvegarde commerce interrompue : " + exception);
            }
        }
    }
}
