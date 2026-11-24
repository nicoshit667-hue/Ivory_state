using System;
using System.Collections.Generic;
using IvoryState.Core.Backend;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using IvoryState.Server.Backend;
using IvoryState.Server.Homes;
using IvoryState.Server.Vehicles;
using UnityEngine;

namespace IvoryState.Server.Economy
{
    /// <summary>
    /// Djassa (serveur uniquement) : annonces de vente entre joueurs.
    /// <list type="bullet">
    /// <item>Voiture : retirée du garage du vendeur dès la mise en vente (rendue s'il retire l'annonce).</item>
    /// <item>Maison achetée : reste habitée jusqu'à la vente, puis change de propriétaire.</item>
    /// <item>Achat : l'acheteur paie, le vendeur est crédité même s'il est déconnecté (portefeuille Nakama).</item>
    /// <item>Vente d'une voiture à l'État : moitié du prix neuf, tout de suite.</item>
    /// </list>
    /// Annonces persistées dans Nakama (le bien mis de côté ne doit pas disparaître à un redémarrage).
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class DjassaService : MonoBehaviour
    {
        private const string LogTag = "Djassa";
        private const int MaxAdsShown = 30;
        private const int MaxAdsPerSeller = 5;

        [Serializable] private class Ad { public int id; public string seller; public string seller_name; public string kind; public int item; public string extra; public long price; public long created; public bool delete; }
        [Serializable] private class LoadResponse { public Ad[] ads; }
        [Serializable] private class Empty { }

        private readonly List<Ad> _ads = new List<Ad>();
        private AccountService _accounts;
        private bool _loaded;
        private int _nextId = 1;

        private void OnEnable() => PlayerMarket.ServerHandler = Handle;
        private void OnDisable() => PlayerMarket.ServerHandler = null;

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
                NakamaHttp.Result result = await NakamaHttp.Rpc(_accounts.BaseUrl, _accounts.HttpKey, "ivory_djassa_load", JsonUtility.ToJson(new Empty()));
                if (!result.Ok)
                {
                    GameLog.Error(LogTag, "Annonces illisibles (" + result.Status + ") : " + result.Error);
                    return; // pas de chargement : pas de vente (on ne perd pas de biens mis de côté)
                }
                LoadResponse response = JsonUtility.FromJson<LoadResponse>(result.Body.Replace("\"ads\":{}", "\"ads\":[]"));
                if (response?.ads != null)
                {
                    foreach (Ad ad in response.ads)
                    {
                        if (ad.id <= 0)
                            continue;
                        _ads.Add(ad);
                        _nextId = Mathf.Max(_nextId, ad.id + 1);
                    }
                }
                _loaded = true;
                GameLog.Info(LogTag, _ads.Count + " annonce(s) chargée(s).");
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Chargement des annonces interrompu : " + exception);
            }
        }

        private static string KeyOf(PlayerMarket player) =>
            player.TryGetComponent(out PlayerAccount account) ? account.ServerUserId : null;

        private void Handle(PlayerMarket player, PlayerMarket.Request request, int index, long price)
        {
            if (player == null || !player.TryGetComponent(out PlayerEconomy economy))
                return;
            if (request == PlayerMarket.Request.List)
            {
                SendAds(player);
                return;
            }
            if (request == PlayerMarket.Request.BuyStoreFood || request == PlayerMarket.Request.BuyStoreMedicine)
            {
                BuyFromStore(player, economy, request == PlayerMarket.Request.BuyStoreMedicine, index);
                SendAds(player);
                return;
            }
            if (!_loaded)
            {
                economy.ServerNotify("Djassa est indisponible pour le moment.");
                return;
            }
            string userId = KeyOf(player);
            if (userId == null)
            {
                economy.ServerNotify("Il faut un compte pour vendre ou acheter sur Djassa.");
                return;
            }

            switch (request)
            {
                case PlayerMarket.Request.SellCar: SellCar(player, economy, userId, index, price); break;
                case PlayerMarket.Request.SellHouse: SellHouse(player, economy, userId, index, price); break;
                case PlayerMarket.Request.SellItem: SellItem(player, economy, userId, index, price); break;
                case PlayerMarket.Request.Buy: Buy(player, economy, userId, index); break;
                case PlayerMarket.Request.Cancel: Cancel(player, economy, userId, index); break;
                case PlayerMarket.Request.SellCarToState: SellCarToState(player, economy, index); break;
            }
            SendAds(player);
        }

        private int CountAds(string userId)
        {
            int count = 0;
            foreach (Ad ad in _ads)
            {
                if (ad.seller == userId)
                    count++;
            }
            return count;
        }

        private static bool ValidPrice(long price, PlayerEconomy economy)
        {
            if (price >= PlayerMarket.MinPrice && price <= PlayerMarket.MaxPrice)
                return true;
            economy.ServerNotify("Prix invalide : entre " + Money.Format(PlayerMarket.MinPrice) + " et " + Money.Format(PlayerMarket.MaxPrice) + ".");
            return false;
        }

        private void SellCar(PlayerMarket player, PlayerEconomy economy, string userId, int index, long price)
        {
            if (!ValidPrice(price, economy) || !player.TryGetComponent(out PlayerGarage garage))
                return;
            if (CountAds(userId) >= MaxAdsPerSeller)
            {
                economy.ServerNotify("Tu as déjà " + MaxAdsPerSeller + " annonces en ligne.");
                return;
            }
            GarageService.Instance?.StoreCarOf(garage);
            byte paint = garage.ServerRemoveCarAt(index);
            if (paint == 0)
                return;
            AddAd(player, userId, PlayerMarket.KindCar, paint, price);
            economy.ServerNotify("Annonce publiée : " + VehicleMotor.PaintNames[paint] + " à " + Money.Format(price) + ".");
        }

        private void SellHouse(PlayerMarket player, PlayerEconomy economy, string userId, int id, long price)
        {
            if (!ValidPrice(price, economy) || !player.TryGetComponent(out PlayerHome home))
                return;
            if (CountAds(userId) >= MaxAdsPerSeller)
            {
                economy.ServerNotify("Tu as déjà " + MaxAdsPerSeller + " annonces en ligne.");
                return;
            }
            if (!home.IsOwnerOf(id))
                id = home.Property;
            if (!PropertyCatalog.IsValid(id) || !PropertyCatalog.IsForSale(PropertyCatalog.Get(id).Kind) ||
                PropertyService.Instance == null || !PropertyService.Instance.IsOwnedBy(id, userId))
            {
                economy.ServerNotify("Seule une maison que tu as achetée peut être vendue sur Djassa.");
                return;
            }
            foreach (Ad ad in _ads)
            {
                if (ad.kind == PlayerMarket.KindHouse && ad.item == id)
                {
                    economy.ServerNotify("Ta maison est déjà en vente.");
                    return;
                }
            }
            AddAd(player, userId, PlayerMarket.KindHouse, id, price);
            economy.ServerNotify("Annonce publiée : ta maison à " + Money.Format(price) + ". Tu y habites jusqu'à la vente.");
        }

        private void SellItem(PlayerMarket player, PlayerEconomy economy, string userId, int slot, long price)
        {
            if (!ValidPrice(price, economy) || !player.TryGetComponent(out PlayerInventory bag))
                return;
            if (CountAds(userId) >= MaxAdsPerSeller)
            {
                economy.ServerNotify("Tu as déjà " + MaxAdsPerSeller + " annonces en ligne.");
                return;
            }
            if (!bag.ServerConsumeOne(slot, out InventoryItem item))
            {
                economy.ServerNotify("Objet introuvable dans ton sac.");
                return;
            }
            string extra = PlayerMarket.PackItemExtra(item.Name, item.Satiety, item.Hydration, item.Health);
            AddAd(player, userId, PlayerMarket.KindItem, 0, price, extra);
            economy.ServerNotify("Annonce publiée : " + item.Name + " à " + Money.Format(price) + ".");
        }

        private void AddAd(PlayerMarket player, string userId, string kind, int item, long price, string extra = "")
        {
            var ad = new Ad
            {
                id = _nextId++, seller = userId,
                seller_name = player.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?",
                kind = kind, item = item, extra = extra ?? string.Empty, price = price, created = RentRules.Now,
            };
            _ads.Add(ad);
            Persist(ad);
        }

        private void Buy(PlayerMarket buyer, PlayerEconomy economy, string userId, int adId)
        {
            Ad ad = _ads.Find(a => a.id == adId);
            if (ad == null)
            {
                economy.ServerNotify("Cette annonce n'existe plus.");
                return;
            }
            if (ad.seller == userId)
            {
                economy.ServerNotify("C'est ta propre annonce.");
                return;
            }
            string buyerName = buyer.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?";

            if (ad.kind == PlayerMarket.KindCar)
            {
                if (!buyer.TryGetComponent(out PlayerGarage garage) || garage.Cars.Count >= PlayerGarage.Rules.MaxCars)
                {
                    economy.ServerNotify("Ton garage est plein.");
                    return;
                }
                if (!economy.ServerTrySpend(ad.price, "Djassa : achat " + VehicleMotor.PaintNames[ad.item] + " de " + ad.seller_name))
                {
                    economy.ServerNotify("Pas assez d'argent (" + Money.Format(ad.price) + ").");
                    return;
                }
                garage.ServerAddCar((byte)ad.item);
                economy.ServerNotify("Achat réussi : " + VehicleMotor.PaintNames[ad.item] + " ! Elle est dans ton Garage.");
            }
            else if (ad.kind == PlayerMarket.KindHouse)
            {
                if (!buyer.TryGetComponent(out PlayerHome home) || !home.CanTakeProperty)
                {
                    economy.ServerNotify("Tu as déjà 2 logements (le maximum).");
                    return;
                }
                if (PropertyService.Instance == null || !PropertyService.Instance.IsOwnedBy(ad.item, ad.seller))
                {
                    economy.ServerNotify("Cette maison n'est plus à vendre.");
                    Remove(ad);
                    return;
                }
                if (!economy.ServerTrySpend(ad.price, "Djassa : achat d'une maison à " + ad.seller_name))
                {
                    economy.ServerNotify("Pas assez d'argent (" + Money.Format(ad.price) + ").");
                    return;
                }
                PropertyService.Instance.ServerTransfer(ad.item, home, userId, buyerName);
                economy.ServerNotify("Félicitations, la maison est à toi ! (" + PropertyCatalog.Get(ad.item).Label + ")");
            }
            else if (ad.kind == PlayerMarket.KindItem)
            {
                PlayerMarket.TryUnpackItemExtra(ad.extra, out string itemName, out byte satiety, out byte hydration, out byte health);
                if (!buyer.TryGetComponent(out PlayerInventory bag) || !bag.ServerCanAdd(itemName, satiety, hydration, health))
                {
                    economy.ServerNotify("Ton sac est plein (ou tu ne peux pas porter cet objet).");
                    return;
                }
                if (!economy.ServerTrySpend(ad.price, "Djassa : achat " + itemName + " de " + ad.seller_name))
                {
                    economy.ServerNotify("Pas assez d'argent (" + Money.Format(ad.price) + ").");
                    return;
                }
                bag.ServerTryAdd(itemName, satiety, hydration, health);
                economy.ServerNotify("Achat réussi : " + itemName + " ! Ajouté dans ton Sac.");
            }
            else
            {
                return;
            }

            if (identity != null)
                identity.ServerAddXp(8, "achat");
            PaySeller(ad, buyerName);
            Remove(ad);
            GameLog.Info(LogTag, buyerName + " achète l'annonce " + ad.id + " (" + ad.kind + ") à " + ad.seller_name + " pour " + ad.price + ".");
        }

        private static void BuyFromStore(PlayerMarket buyer, PlayerEconomy economy, bool medicine, int index)
        {
            DjassaStoreCatalog.StoreItem[] catalog = medicine ? DjassaStoreCatalog.Medicines : DjassaStoreCatalog.Foods;
            if (index < 0 || index >= catalog.Length)
                return;
            DjassaStoreCatalog.StoreItem item = catalog[index];
            if (!buyer.TryGetComponent(out PlayerInventory bag) || !bag.ServerCanAdd(item.Name, item.Satiety, item.Hydration, item.Health))
            {
                economy.ServerNotify("Ton sac est plein : fais de la place pour recevoir cet article.");
                return;
            }
            if (!economy.ServerTrySpend(item.Price, "Djassa (" + item.StoreName + ") : " + item.Name))
            {
                economy.ServerNotify("Pas assez d'argent (" + Money.Format(item.Price) + ").");
                return;
            }
            bag.ServerTryAdd(item.Name, item.Satiety, item.Hydration, item.Health);
            if (buyer.TryGetComponent(out PlayerNetworkIdentity identity))
                identity.ServerAddXp(8, "achat");
            economy.ServerNotify(item.StoreName + " : " + item.Name + " livré dans ton Sac !");
        }

        private void PaySeller(Ad ad, string buyerName)
        {
            string reason = "Djassa : vente à " + buyerName;
            var markets = FindObjectsByType<PlayerMarket>(FindObjectsSortMode.None);
            foreach (PlayerMarket market in markets)
            {
                if (KeyOf(market) == ad.seller && market.TryGetComponent(out PlayerEconomy economy))
                {
                    economy.ServerAdd(ad.price, reason);
                    economy.ServerNotify("Djassa : " + buyerName + " a acheté ton annonce. +" + Money.Format(ad.price));
                    return;
                }
            }
            _accounts?.ServerCreditOffline(ad.seller, ad.price, reason);
        }

        private void Cancel(PlayerMarket player, PlayerEconomy economy, string userId, int adId)
        {
            Ad ad = _ads.Find(a => a.id == adId);
            if (ad == null || ad.seller != userId)
                return;
            if (ad.kind == PlayerMarket.KindCar)
            {
                if (!player.TryGetComponent(out PlayerGarage garage) || !garage.ServerAddCar((byte)ad.item))
                {
                    economy.ServerNotify("Ton garage est plein : libère une place pour récupérer ta voiture.");
                    return;
                }
            }
            else if (ad.kind == PlayerMarket.KindItem)
            {
                PlayerMarket.TryUnpackItemExtra(ad.extra, out string itemName, out byte satiety, out byte hydration, out byte health);
                if (!player.TryGetComponent(out PlayerInventory bag) || !bag.ServerCanAdd(itemName, satiety, hydration, health))
                {
                    economy.ServerNotify("Ton sac est plein : libère une place pour récupérer " + itemName + ".");
                    return;
                }
                bag.ServerTryAdd(itemName, satiety, hydration, health);
            }
            Remove(ad);
            economy.ServerNotify("Annonce retirée.");
        }

        private static void SellCarToState(PlayerMarket player, PlayerEconomy economy, int index)
        {
            if (!player.TryGetComponent(out PlayerGarage garage))
                return;
            GarageService.Instance?.StoreCarOf(garage);
            byte paint = garage.ServerRemoveCarAt(index);
            if (paint == 0)
                return;
            long amount = PlayerGarage.Rules.CarPrices[paint] / 2;
            economy.ServerAdd(amount, "revente à l'État : " + VehicleMotor.PaintNames[paint]);
            economy.ServerNotify("Voiture revendue à l'État : +" + Money.Format(amount) + " (moitié du prix neuf).");
        }

        private void Remove(Ad ad)
        {
            _ads.Remove(ad);
            ad.delete = true;
            Persist(ad);
        }

        private void SendAds(PlayerMarket player)
        {
            string userId = KeyOf(player);
            int count = Mathf.Min(MaxAdsShown, _ads.Count);
            var ids = new int[count];
            var sellers = new string[count];
            var kinds = new string[count];
            var items = new int[count];
            var extras = new string[count];
            var prices = new long[count];
            var mine = new bool[count];
            for (int i = 0; i < count; i++)
            {
                Ad ad = _ads[_ads.Count - 1 - i]; // les plus récentes d'abord
                ids[i] = ad.id;
                sellers[i] = ad.seller_name;
                kinds[i] = ad.kind;
                items[i] = ad.item;
                extras[i] = ad.extra ?? string.Empty;
                prices[i] = ad.price;
                mine[i] = userId != null && ad.seller == userId;
            }
            player.ServerSendAds(ids, sellers, kinds, items, extras, prices, mine);
        }

        private async void Persist(Ad ad)
        {
            if (_accounts == null || !_accounts.IsEnabled)
                return;
            try
            {
                NakamaHttp.Result result = await NakamaHttp.Rpc(_accounts.BaseUrl, _accounts.HttpKey, "ivory_djassa_save", JsonUtility.ToJson(ad));
                if (!result.Ok)
                    GameLog.Warn(LogTag, "Annonce " + ad.id + " non sauvegardée (" + result.Status + ") : " + result.Error);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Sauvegarde de l'annonce interrompue : " + exception);
            }
        }
    }
}
