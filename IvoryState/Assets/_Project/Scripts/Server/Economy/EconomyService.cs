using System.Collections.Generic;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Server.Economy
{
    /// <summary>
    /// Règles économiques, exécutées uniquement par le serveur (absentes des builds clients).
    /// <list type="bullet">
    /// <item><b>Étals</b> : paiement, puis satiété rendue.</item>
    /// <item><b>Livraisons</b> : le dépôt attribue une adresse à au moins <see cref="MinDeliveryDistance"/> m.
    /// Le joueur doit s'y rendre avant la fin du délai et il est payé selon la distance.</item>
    /// <item><b>Faim et soif</b> : la satiété baisse d'un point toutes les <see cref="HungerInterval"/> s
    /// (~33 min), l'hydratation toutes les <see cref="ThirstInterval"/> s (~20 min).</item>
    /// <item><b>Santé</b> : voir <see cref="UpdateHealth"/>.</item>
    /// </list>
    /// Tous les montants sont calculés ici à partir des données du serveur. Le client n'envoie
    /// qu'un identifiant de point, déjà validé par PlayerInteractor (existence, distance, cadence).
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class EconomyService : MonoBehaviour
    {
        // --- Réglages d'équilibrage ---
        private const float HungerInterval = 20f;
        private const float ThirstInterval = 12f;   // la soif vient plus vite que la faim (de 100 à 0 en ~20 min)
        private const float HealthInterval = 10f;
        private const int RecoveryThreshold = 40;   // bien nourri ET hydraté au-dessus de ce seuil : la santé remonte
        private const float MinDeliveryDistance = 30f;
        private const float MaxDeliveryDistance = 250f;   // à pied ; les véhicules permettront plus loin
        private const float StreetDetourFactor = 1.4f;    // trajet par les rues ≈ 1,4 × la ligne droite
        private const long DeliveryBasePay = 300;
        private const long DeliveryPayPerMeter = 15;
        private const long DeliveryPayRounding = 25;
        private const float DeliveryAssumedSpeed = 2.5f; // m/s : délai calculé à allure de marche
        private const float DeliveryExtraSeconds = 25f;

        private struct DeliveryJob
        {
            public PlayerEconomy Economy;
            public int TargetId;
            public float Distance;
            public float Deadline;
        }

        private readonly Dictionary<int, DeliveryJob> _jobsByPlayerObject = new Dictionary<int, DeliveryJob>();
        private readonly List<int> _expired = new List<int>();
        private readonly List<DeliveryPoint> _candidates = new List<DeliveryPoint>();
        private readonly System.Random _random = new System.Random();

        private float _nextHungerTick;
        private float _nextThirstTick;
        private float _nextHealthTick;
        private float _nextExpiryCheck;

        private void OnEnable()
        {
            InteractionRouter.ServerHandler = Handle;
            InteractionRouter.ServerChoiceHandler = HandleChoice;
            PlayerServices.ServerHandler = HandleService;
            _nextHungerTick = Time.time + HungerInterval;
            _nextThirstTick = Time.time + ThirstInterval;
            _nextHealthTick = Time.time + HealthInterval;
        }

        private void OnDisable()
        {
            if (InteractionRouter.ServerHandler == (System.Action<PlayerInteractor, WorldInteractable>)Handle)
                InteractionRouter.ServerHandler = null;
            if (InteractionRouter.ServerChoiceHandler == (System.Action<PlayerInteractor, MenuInteractable, int, bool>)HandleChoice)
                InteractionRouter.ServerChoiceHandler = null;
            if (PlayerServices.ServerHandler == (System.Action<PlayerServices, PlayerServices.Request, int>)HandleService)
                PlayerServices.ServerHandler = null;
        }

        #region Téléphone : repas livrés et offres d'emploi

        private struct FoodOrder
        {
            public PlayerServices Player;
            public PlayerServices.Dish Dish;
            public long Paid;
            public float ReadyAt;
        }

        private readonly List<FoodOrder> _orders = new List<FoodOrder>();

        private void HandleService(PlayerServices services, PlayerServices.Request kind, int index)
        {
            if (!services.TryGetComponent(out PlayerInteractor player) || player.Economy == null)
                return;
            PlayerEconomy economy = player.Economy;

            if (kind == PlayerServices.Request.Food)
            {
                PlayerServices.Dish dish = PlayerServices.Catalog.Dishes[index];
                long total = dish.Price + PlayerServices.Catalog.DeliveryFee;
                foreach (FoodOrder pending in _orders)
                {
                    if (pending.Player == services)
                    {
                        economy.ServerNotify("Une commande est déjà en route.");
                        return;
                    }
                }
                if (!economy.ServerTrySpend(total, "commande : " + dish.Name))
                {
                    economy.ServerNotify("Pas assez d'argent : " + Money.Format(total) + " avec la livraison.");
                    return;
                }
                _orders.Add(new FoodOrder { Player = services, Dish = dish, Paid = total, ReadyAt = Time.time + PlayerServices.Catalog.DeliveryDelay });
                economy.ServerNotify(dish.Restaurant + " prépare ta commande : livraison dans " +
                                     Mathf.RoundToInt(PlayerServices.Catalog.DeliveryDelay) + " s.");
                return;
            }

            switch (index)
            {
                case PlayerServices.Catalog.DeliveryJob:
                    TakeDelivery(player, player.transform.position);
                    break;
                case PlayerServices.Catalog.TaxiJob:
                    economy.ServerNotify("Chauffeur : monte dans un taxi libre, les courses arriveront toutes seules.");
                    break;
                default:
                    economy.ServerNotify("Cette offre arrive bientôt.");
                    break;
            }
        }

        /// <summary>Livre les repas prêts dans le sac (remboursé si le sac est plein ou le joueur parti).</summary>
        private void DeliverOrders()
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                FoodOrder order = _orders[i];
                if (Time.time < order.ReadyAt)
                    continue;
                _orders.RemoveAt(i);
                if (order.Player == null || !order.Player.IsSpawned)
                    continue;
                if (!order.Player.TryGetComponent(out PlayerEconomy economy))
                    continue;
                if (order.Player.TryGetComponent(out PlayerInventory bag) &&
                    bag.ServerTryAdd(order.Dish.Name, order.Dish.Satiety, order.Dish.Hydration))
                {
                    economy.ServerNotify("Le livreur est passé : " + order.Dish.Name + " est dans ton sac.");
                }
                else
                {
                    economy.ServerAdd(order.Paid, "remboursement commande : " + order.Dish.Name);
                    economy.ServerNotify("Sac plein : commande remboursée.");
                }
            }
        }

        #endregion

        private static void HandleChoice(PlayerInteractor player, MenuInteractable menu, int option, bool keep)
        {
            switch (menu)
            {
                case StorePoint store: BuyProduct(player, store, option, keep); break;
                case CommercePoint commerce: BuyCommerceProduct(player, commerce, option, keep); break;
                case GbakaStationPoint station: TakeGbaka(player, station, option); break;
                case ClothingStorePoint clothing: BuyOutfit(player, clothing, option); break;
                case CashPointPoint cashPoint: UseCashPoint(player, cashPoint, option); break;
                case DrivingSchoolPoint: Vehicles.GarageService.Instance?.HandleDrivingSchool(player); break;
                case DealershipPoint dealer: Vehicles.GarageService.Instance?.HandleDealership(player, dealer, option); break;
            }
        }

        /// <summary>Guichet Pingoin ou banque : retrait d'espèces, ou dépôt de toutes ses espèces.</summary>
        private static void UseCashPoint(PlayerInteractor player, CashPointPoint point, int option)
        {
            PlayerEconomy economy = player.Economy;
            if (economy == null || option < 0 || option >= point.OptionCount)
                return;
            if (option == point.DepositOption)
            {
                long cash = economy.Cash;
                if (cash <= 0 || !economy.ServerDeposit(cash))
                {
                    economy.ServerNotify("Tu n'as pas d'espèces à déposer.");
                    return;
                }
                economy.ServerNotify("Dépôt : " + Money.Format(cash) + " sur ton compte Pingoin.");
                return;
            }
            long amount = CashPointPoint.Withdrawals[option];
            if (!economy.ServerWithdraw(amount))
            {
                economy.ServerNotify("Solde Pingoin insuffisant pour retirer " + Money.Format(amount) + ".");
                return;
            }
            economy.ServerNotify("Retrait : " + Money.Format(amount) + " en espèces. Garde-les bien !");
        }

        /// <summary>Tenue : achetée une fois (garde-robe), puis remise gratuitement.</summary>
        private static void BuyOutfit(PlayerInteractor player, ClothingStorePoint store, int option)
        {
            PlayerEconomy economy = player.Economy;
            if (economy == null || !player.TryGetComponent(out PlayerNetworkIdentity identity) || !identity.HasAppearance)
                return;
            if (option < 0 || option >= Mathf.Min(ClothingStorePoint.Outfits.Count, PlayerNetworkIdentity.OutfitCount))
                return;

            string name = ClothingStorePoint.Outfits.Names[option];
            if (identity.ServerOwnsOutfit(option))
            {
                identity.ServerWearOutfit((byte)option);
                economy.ServerNotify("Tu portes ta " + name.ToLowerInvariant() + ".");
                return;
            }
            long price = store.OptionPrice(option);
            if (!economy.ServerTrySpend(price, "vêtements : " + name))
            {
                economy.ServerNotify("Pas assez d'argent pour la " + name.ToLowerInvariant() + " (" + Money.Format(price) + ").");
                return;
            }
            identity.ServerWearOutfit((byte)option);
            identity.ServerAddXp(30, "tenue");
            economy.ServerNotify("Nouvelle tenue : " + name + " ! Elle est à toi pour toujours.");
        }

        #region Boutiques et marché

        private static void BuyProduct(PlayerInteractor player, StorePoint store, int option, bool keep)
        {
            if (!store.TryGetProduct(option, out StorePoint.Product product))
                return;

            BuyConsumable(player, store.VendorName, product.Name, product.Price,
                (byte)Mathf.Clamp(product.Satiety, 0, 100),
                (byte)Mathf.Clamp(product.Hydration, 0, 100),
                (byte)Mathf.Clamp(product.Health, 0, 100),
                keep, commerceId: 0);
        }

        private static void BuyCommerceProduct(PlayerInteractor player, CommercePoint commerce, int option, bool keep)
        {
            if (CommerceService.Instance != null && !CommerceService.Instance.IsCommerceOpen(commerce.CommerceId))
            {
                player.Economy?.ServerNotify(commerce.MenuTitle + " est actuellement fermé par son gérant.");
                return;
            }
            if (!commerce.TryGetConsumable(option, out ConsumableDefinition item))
                return;

            BuyConsumable(player, commerce.MenuTitle, item.DisplayName, item.DefaultPrice,
                item.Satiety, item.Hydration, item.Health, keep, commerce.CommerceId);
        }

        private static void BuyConsumable(PlayerInteractor player, string vendorName, string itemName, long price,
            byte satiety, byte hydration, byte health, bool keep, int commerceId)
        {
            PlayerEconomy economy = player.Economy;
            PlayerNeeds needs = player.Needs;
            if (economy == null || needs == null)
                return;

            // Pour plus tard : dans le sac, sans condition de faim ou de soif. Armes, munitions et port
            // d'arme vont toujours au sac.
            if (keep || Gameplay.Combat.Weapons.GoesToBag(itemName))
            {
                if (!player.TryGetComponent(out PlayerInventory bag))
                    return;
                if (Gameplay.Combat.Weapons.RequiresPermit(itemName) && !bag.Has(Gameplay.Combat.Weapons.Permit))
                {
                    economy.ServerNotify(vendorName + " : « Pas d'arme à feu ni de munitions sans port d'arme. Achète-le d'abord (" +
                                         Money.Format(Gameplay.Combat.Weapons.PermitPrice) + "). »");
                    return;
                }
                if (itemName == Gameplay.Combat.Weapons.Permit && bag.Has(itemName))
                {
                    economy.ServerNotify("Tu as déjà un port d'arme dans ton sac.");
                    return;
                }
                if (Gameplay.Combat.Firearms.ByName(itemName) != null && bag.Has(itemName))
                {
                    economy.ServerNotify("Tu as déjà un " + itemName.ToLowerInvariant() + " dans ton sac.");
                    return;
                }
                // Payer d'abord, puis ranger : jamais d'objet gratuit (remboursé si le sac est plein).
                if (!economy.ServerTrySpend(price, "achat (sac) : " + itemName))
                {
                    economy.ServerNotify("Pas assez d'argent pour " + itemName + " (" + Money.Format(price) + ").");
                    return;
                }
                if (!bag.ServerTryAdd(itemName, satiety, hydration, health))
                {
                    economy.ServerAdd(price, "remboursement (sac plein) : " + itemName);
                    economy.ServerNotify("Ton sac est plein (" + PlayerInventory.MaxSlots + " emplacements).");
                    return;
                }
                if (commerceId > 0)
                    CommerceService.Instance?.RecordSale(commerceId, price);
                economy.ServerNotify(itemName + " rangé dans ton sac : -" + Money.Format(price) + ".");
                player.GetComponent<PlayerNetworkIdentity>()?.ServerAddXp(8, "achat");
                return;
            }

            bool heals = health > 0 && needs.Health < PlayerNeeds.MaxHealth;
            bool feeds = satiety > 0 && needs.Satiety < PlayerNeeds.MaxSatiety;
            bool quenches = hydration > 0 && needs.Hydration < PlayerNeeds.MaxHydration;
            if (!heals && !feeds && !quenches)
            {
                string msg = health > 0 && satiety == 0 && hydration == 0
                    ? vendorName + " : « Tu es déjà en pleine forme, garde-le plutôt dans ton sac ! »"
                    : (satiety > 0 ? vendorName + " : « Tu n'as plus faim, garde ton argent ! »"
                                   : vendorName + " : « Tu n'as pas soif, reviens tout à l'heure ! »");
                economy.ServerNotify(msg);
                return;
            }

            if (!economy.ServerTrySpend(price, "achat : " + itemName))
            {
                economy.ServerNotify("Pas assez d'argent pour " + itemName + " (" + Money.Format(price) + ").");
                return;
            }

            if (health > 0) needs.ServerChangeHealth(health);
            if (satiety > 0) needs.ServerChangeSatiety(satiety);
            if (hydration > 0) needs.ServerChangeHydration(hydration);
            if (commerceId > 0)
                CommerceService.Instance?.RecordSale(commerceId, price);
            player.GetComponent<PlayerNetworkIdentity>()?.ServerAddXp(8, "achat");
            economy.ServerNotify(itemName + " : -" + Money.Format(price) + ". Merci, à la prochaine !");
        }

        #endregion

        #region Gbaka

        private static void TakeGbaka(PlayerInteractor player, GbakaStationPoint station, int option)
        {
            PlayerEconomy economy = player.Economy;
            if (economy == null || option >= station.Destinations.Count)
                return;
            if (player.Driving != null && player.Driving.IsInVehicle)
                return;

            GbakaStationPoint destination = station.Destinations[option];
            long fare = station.FareTo(destination);
            // Le gbaka se paie en espèces.
            if (!economy.ServerTrySpendCash(fare))
            {
                economy.ServerNotify("L'apprenti : « C'est " + Money.Format(fare) + " en espèces, hein ! » Retire de l'argent à un guichet Pingoin.");
                return;
            }

            string description = station.Describe(destination);
            player.ServerTravel(destination.ArrivalPosition, description);
            player.GetComponent<PlayerNetworkIdentity>()?.ServerAddXp(5, "gbaka");
            economy.ServerNotify("Gbaka " + description + ". Bienvenue à " + destination.Commune + " !");
        }

        #endregion

        private void Handle(PlayerInteractor player, WorldInteractable target)
        {
            switch (target)
            {
                case ShopPoint shop: Buy(player, shop); break;
                case JobBoardPoint board: TakeDelivery(player, board); break;
                case DeliveryPoint point: Deliver(player, point); break;
            }
        }

        #region Étals

        private static void Buy(PlayerInteractor player, ShopPoint shop)
        {
            PlayerEconomy economy = player.Economy;
            PlayerNeeds needs = player.Needs;
            if (economy == null || needs == null)
                return;

            bool feeds = shop.Satiety > 0 && needs.Satiety < PlayerNeeds.MaxSatiety;
            bool quenches = shop.Hydration > 0 && needs.Hydration < PlayerNeeds.MaxHydration;
            if (!feeds && !quenches)
            {
                economy.ServerNotify(shop.IsDrink ? "Tu n'as pas soif." : "Tu n'as pas faim.");
                return;
            }

            // Étal de rue : paiement en espèces.
            if (!economy.ServerTrySpendCash(shop.Price))
            {
                economy.ServerNotify("Pas assez d'espèces pour " + shop.ItemName + " (" + Money.Format(shop.Price) +
                                     "). Retire de l'argent à un guichet Pingoin.");
                return;
            }

            needs.ServerChangeSatiety(shop.Satiety);
            needs.ServerChangeHydration(shop.Hydration);
            player.GetComponent<PlayerNetworkIdentity>()?.ServerAddXp(6, "achat");
            economy.ServerNotify("Tu as acheté " + shop.ItemName + " : -" + Money.Format(shop.Price) + " en espèces");
        }

        #endregion

        #region Livraisons

        private void TakeDelivery(PlayerInteractor player, JobBoardPoint board) => TakeDelivery(player, board.transform.position);

        /// <summary>Mission de livraison au départ de <paramref name="origin"/> (dépôt, ou position du joueur depuis le téléphone).</summary>
        private void TakeDelivery(PlayerInteractor player, Vector3 origin)
        {
            PlayerEconomy economy = player.Economy;
            if (economy == null)
                return;

            if (_jobsByPlayerObject.ContainsKey(player.ObjectId) || economy.HasDelivery)
            {
                economy.ServerNotify("Tu as déjà une mission en cours.");
                return;
            }

            DeliveryPoint destination = PickDestination(origin);
            if (destination == null)
            {
                economy.ServerNotify("Aucune livraison disponible pour le moment.");
                return;
            }

            float distance = Vector3.Distance(origin, destination.transform.position);
            float timeLimit = distance * StreetDetourFactor / DeliveryAssumedSpeed + DeliveryExtraSeconds;

            _jobsByPlayerObject[player.ObjectId] = new DeliveryJob
            {
                Economy = economy,
                TargetId = destination.Id,
                Distance = distance,
                Deadline = Time.time + timeLimit,
            };

            economy.ServerSetDelivery(destination.Id, (ushort)Mathf.CeilToInt(timeLimit));
            economy.ServerNotify("Livraison pour " + destination.DisplayName + " (" + Mathf.RoundToInt(distance) +
                                 " m). Paie : " + Money.Format(ComputePay(distance)) + ".");
        }

        private void Deliver(PlayerInteractor player, DeliveryPoint point)
        {
            PlayerEconomy economy = player.Economy;
            if (economy == null)
                return;

            if (!_jobsByPlayerObject.TryGetValue(player.ObjectId, out DeliveryJob job) || job.TargetId != point.Id)
            {
                economy.ServerNotify(job.Economy == null
                    ? "Prends d'abord une livraison au dépôt."
                    : "Ce n'est pas la bonne adresse.");
                return;
            }

            long pay = ComputePay(job.Distance);
            _jobsByPlayerObject.Remove(player.ObjectId);
            economy.ServerClearDelivery();
            economy.ServerAdd(pay, "livraison : " + point.DisplayName);
            player.GetComponent<PlayerNetworkIdentity>()?.ServerAddXp(35, "livraison");
            economy.ServerNotify("Colis livré ! +" + Money.Format(pay));
        }

        private DeliveryPoint PickDestination(Vector3 from)
        {
            _candidates.Clear();
            var all = WorldInteractable.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is DeliveryPoint point)
                {
                    float sqr = (point.transform.position - from).sqrMagnitude;
                    if (sqr >= MinDeliveryDistance * MinDeliveryDistance && sqr <= MaxDeliveryDistance * MaxDeliveryDistance)
                        _candidates.Add(point);
                }
            }

            return _candidates.Count > 0 ? _candidates[_random.Next(_candidates.Count)] : null;
        }

        private static long ComputePay(float distance)
        {
            long raw = DeliveryBasePay + (long)(distance * DeliveryPayPerMeter);
            return (raw + DeliveryPayRounding / 2) / DeliveryPayRounding * DeliveryPayRounding;
        }

        #endregion

        private void Update()
        {
            float now = Time.time;
            if (_orders.Count > 0)
                DeliverOrders();

            if (now >= _nextHungerTick)
            {
                _nextHungerTick = now + HungerInterval;
                var all = PlayerNeeds.ServerInstances;
                for (int i = 0; i < all.Count; i++)
                {
                    // Seuls les vrais joueurs ont faim (pas les bots de test sans propriétaire).
                    if (all[i].Owner.IsValid)
                        all[i].ServerChangeSatiety(-1);
                }
            }

            if (now >= _nextThirstTick)
            {
                _nextThirstTick = now + ThirstInterval;
                var all = PlayerNeeds.ServerInstances;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Owner.IsValid)
                        all[i].ServerChangeHydration(-1);
                }
            }

            if (now >= _nextHealthTick)
            {
                _nextHealthTick = now + HealthInterval;
                UpdateHealth();
            }

            if (now >= _nextExpiryCheck)
            {
                _nextExpiryCheck = now + 1f;
                CheckExpiredDeliveries(now);
            }
        }

        /// <summary>
        /// Santé : baisse quand on a très faim ou très soif (jauge à 0), remonte quand on est bien nourri
        /// et hydraté. À 0 : malaise, les secours ramènent le joueur à la clinique (point d'apparition).
        /// </summary>
        private static void UpdateHealth()
        {
            var all = PlayerNeeds.ServerInstances;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerNeeds needs = all[i];
                if (!needs.Owner.IsValid)
                    continue;

                if (needs.Satiety == 0 || needs.Hydration == 0)
                    needs.ServerChangeHealth(-2);
                else if (needs.Satiety >= RecoveryThreshold && needs.Hydration >= RecoveryThreshold)
                    needs.ServerChangeHealth(1);

                if (needs.Health == 0)
                    Faint(needs);
            }
        }

        private static void Faint(PlayerNeeds needs)
        {
            // Malaise : réveil à l'hôpital le plus proche (frais médicaux, une minute de soins).
            if (Combat.HospitalService.Instance != null)
            {
                Combat.HospitalService.Instance.Admit(needs, Gameplay.Combat.HospitalCatalog.KnockoutFee, "Malaise (faim ou soif)");
                return;
            }
            needs.ServerSetHealth(35);
            needs.ServerSetSatiety(Mathf.Max(needs.Satiety, 30));
            needs.ServerSetHydration(Mathf.Max(needs.Hydration, 30));
            Core.Networking.NetworkBootstrap bootstrap = Core.Networking.NetworkBootstrap.Instance;
            if (bootstrap != null && needs.TryGetComponent(out PlayerMotor motor) &&
                !(needs.TryGetComponent(out Gameplay.Vehicles.PlayerDriving driving) && driving.IsInVehicle))
                motor.ServerTeleport(bootstrap.Config.SpawnCenter + Vector3.up * 0.2f);
            if (needs.TryGetComponent(out PlayerEconomy economy))
                economy.ServerNotify("Malaise ! Les secours t'ont conduit à la clinique. Mange et bois !");
        }

        private void CheckExpiredDeliveries(float now)
        {
            _expired.Clear();
            foreach (KeyValuePair<int, DeliveryJob> pair in _jobsByPlayerObject)
            {
                // Joueur déconnecté (objet détruit) ou délai dépassé.
                if (pair.Value.Economy == null || now > pair.Value.Deadline)
                    _expired.Add(pair.Key);
            }

            for (int i = 0; i < _expired.Count; i++)
            {
                DeliveryJob job = _jobsByPlayerObject[_expired[i]];
                _jobsByPlayerObject.Remove(_expired[i]);

                if (job.Economy != null)
                {
                    job.Economy.ServerClearDelivery();
                    job.Economy.ServerNotify("Livraison expirée. Le client a annulé la commande.");
                }
            }
        }
    }
}
