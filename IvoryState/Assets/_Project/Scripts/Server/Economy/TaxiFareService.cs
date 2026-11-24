using System.Collections.Generic;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Server.Economy
{
    /// <summary>
    /// Métier de chauffeur de taxi (serveur uniquement).
    /// <list type="number">
    /// <item>Un joueur au volant d'un taxi, sans autre mission, se voit proposer un client à prendre à
    /// 40-200 m (une adresse de la ville).</item>
    /// <item>Il s'arrête à moins de 8 m de l'adresse (moins de 3 m/s) : le client monte et donne sa
    /// destination, à 150-500 m.</item>
    /// <item>Arrêt à destination : paie = 500 FCFA + 6 FCFA/m (distance de la course, arrondie à 25).</item>
    /// </list>
    /// Descendre du taxi ou dépasser le délai annule la course. Toutes les positions et vitesses
    /// utilisées sont celles du serveur.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class TaxiFareService : MonoBehaviour
    {
        private const float CheckInterval = 0.5f;
        private const float OfferDelay = 3f;          // après la montée dans le taxi
        private const float ArrivalRadius = 8f;
        private const float MaxStopSpeed = 3f;
        private const float PickupMin = 40f, PickupMax = 200f;
        private const float DropoffMin = 150f, DropoffMax = 500f;
        private const float AssumedSpeed = 8f;       // m/s, pour le délai
        private const float StreetDetourFactor = 1.4f;
        private const float ExtraSeconds = 40f;
        private const long BaseFare = 500, FarePerMeter = 6, Rounding = 25;

        private sealed class Fare
        {
            public PlayerDriving Driver;
            public int TargetId;
            public bool Onboard;
            public float RideDistance;
            public float Deadline;
        }

        private readonly Dictionary<PlayerDriving, Fare> _fares = new Dictionary<PlayerDriving, Fare>();
        private readonly Dictionary<PlayerDriving, float> _drivingSince = new Dictionary<PlayerDriving, float>();
        private readonly List<PlayerDriving> _toRemove = new List<PlayerDriving>();
        private readonly List<DeliveryPoint> _candidates = new List<DeliveryPoint>();
        private readonly System.Random _random = new System.Random();
        private float _nextCheck;

        private void Update()
        {
            if (Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + CheckInterval;

            float now = Time.time;
            var drivers = PlayerDriving.ServerInstances;
            for (int i = 0; i < drivers.Count; i++)
            {
                PlayerDriving driver = drivers[i];
                VehicleMotor taxi = driver.IsDriving ? driver.Vehicle : null;
                if (taxi != null && taxi.IsPrivate)
                    taxi = null; // voiture personnelle : pas de clients

                if (taxi == null)
                {
                    _drivingSince.Remove(driver);
                    continue;
                }

                if (!_drivingSince.ContainsKey(driver))
                    _drivingSince[driver] = now;

                if (_fares.TryGetValue(driver, out Fare fare))
                    UpdateFare(driver, taxi, fare, now);
                else if (now - _drivingSince[driver] > OfferDelay && driver.Economy != null && !driver.Economy.HasDelivery &&
                         !_callByDriver.ContainsKey(driver))
                    OfferFare(driver, taxi, now);
            }

            CleanUp(now);
            UpdateCalls(now);
        }

        #region Courses commandées par un joueur (téléphone)

        // Un taxi commandé appartient à son client : lui seul monte directement, et il décide qui
        // l'accompagne (4 passagers au plus). Il paie à l'arrivée, ou au prorata s'il descend avant.
        private const float CallSearchSeconds = 120f;
        private const float CallExtraSeconds = 90f;
        private const float PickupMoveThreshold = 20f;
        private const float DestinationRadius = 25f;
        private const float MinCallDistance = 30f;

        private sealed class TaxiCall
        {
            public PlayerServices Customer;
            public PlayerDriving CustomerDriving;
            public PlayerEconomy CustomerEconomy;
            public string CustomerName;
            public Vector3 Pickup;
            public Vector3 Destination;
            public long Fare;
            public PlayerDriving Driver;
            public float Deadline;
            public float CreatedAt;
            public bool Onboard;
            public float StartDistance;
        }

        private readonly List<TaxiCall> _calls = new List<TaxiCall>();
        private readonly Dictionary<PlayerDriving, TaxiCall> _callByDriver = new Dictionary<PlayerDriving, TaxiCall>();

        private void OnEnable()
        {
            PlayerServices.ServerTaxiCallHandler = OnCall;
            PlayerServices.ServerTaxiCancelHandler = OnCancel;
            PlayerDriving.ServerBoardingRule = BoardingRule;
            PlayerDriving.ServerRideChanged += OnRideChanged;
        }

        private void OnDisable()
        {
            if (PlayerServices.ServerTaxiCallHandler == (System.Action<PlayerServices, Vector3>)OnCall)
                PlayerServices.ServerTaxiCallHandler = null;
            if (PlayerServices.ServerTaxiCancelHandler == (System.Action<PlayerServices>)OnCancel)
                PlayerServices.ServerTaxiCancelHandler = null;
            PlayerDriving.ServerBoardingRule = null;
            PlayerDriving.ServerRideChanged -= OnRideChanged;
        }

        private TaxiCall FindCall(PlayerServices customer)
        {
            for (int i = 0; i < _calls.Count; i++)
            {
                if (_calls[i].Customer == customer)
                    return _calls[i];
            }
            return null;
        }

        private void OnCall(PlayerServices customer, Vector3 destination)
        {
            if (customer == null)
                return;
            customer.TryGetComponent(out PlayerEconomy economy);
            customer.TryGetComponent(out PlayerDriving driving);
            if (economy == null || driving == null)
                return;

            if (FindCall(customer) != null)
            {
                economy.ServerNotify("Tu as déjà un taxi commandé.");
                return;
            }
            if (driving.IsInVehicle)
            {
                economy.ServerNotify("Descends d'abord du véhicule pour commander un taxi.");
                return;
            }

            Vector3 pickup = customer.transform.position;
            if (Gameplay.Homes.StudioLayout.IsIndoors(pickup))
            {
                economy.ServerNotify("Sors d'abord de chez toi : le taxi t'attendra dans la rue.");
                return;
            }
            Vector3 flat = destination - pickup;
            flat.y = 0f;
            if (flat.sqrMagnitude < MinCallDistance * MinCallDistance)
            {
                economy.ServerNotify("Tu y es presque : pas besoin de taxi !");
                return;
            }

            long fare = PlayerServices.Catalog.TaxiFare(pickup, destination);
            if (economy.Balance < fare)
            {
                economy.ServerNotify("Pas assez d'argent pour cette course (" + Money.Format(fare) + ").");
                return;
            }

            var call = new TaxiCall
            {
                Customer = customer, CustomerDriving = driving, CustomerEconomy = economy,
                CustomerName = customer.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "Un client",
                Pickup = pickup, Destination = destination, Fare = fare, CreatedAt = Time.time,
            };
            _calls.Add(call);
            customer.ServerSetTaxiCallInfo(destination, fare);
            customer.ServerSetTaxiState(PlayerServices.TaxiState.Searching);
            economy.ServerNotify("Taxi commandé (" + Money.Format(fare) + ", payé à l'arrivée). Recherche d'un chauffeur…");
            TryAssign(call, Time.time);
        }

        private void OnCancel(PlayerServices customer)
        {
            TaxiCall call = FindCall(customer);
            if (call == null)
                return;
            if (call.Onboard)
            {
                call.CustomerEconomy.ServerNotify("Tu es à bord : descends du taxi pour arrêter la course.");
                return;
            }
            if (call.Driver != null)
                call.Driver.Economy?.ServerNotify(call.CustomerName + " a annulé sa commande.");
            call.CustomerEconomy.ServerNotify("Commande de taxi annulée.");
            EndCall(call);
        }

        /// <summary>Confie la course au chauffeur (joueur au volant d'un taxi) le plus proche et disponible.</summary>
        private void TryAssign(TaxiCall call, float now)
        {
            PlayerDriving best = null;
            float bestSqr = float.MaxValue;
            var drivers = PlayerDriving.ServerInstances;
            for (int i = 0; i < drivers.Count; i++)
            {
                PlayerDriving driver = drivers[i];
                if (driver == null || !driver.IsDriving || driver == call.CustomerDriving || driver.Vehicle == null || driver.Vehicle.IsPrivate)
                    continue;
                if (_callByDriver.ContainsKey(driver))
                    continue;
                if (_fares.TryGetValue(driver, out Fare npc) && npc.Onboard)
                    continue; // déjà un client (PNJ) à bord
                float sqr = (driver.Vehicle.transform.position - call.Pickup).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = driver;
                    bestSqr = sqr;
                }
            }
            if (best == null)
                return;

            if (_fares.ContainsKey(best))
                Cancel(best, null); // le client PNJ attendra un autre taxi

            float distance = Mathf.Sqrt(bestSqr);
            call.Driver = best;
            call.Deadline = now + distance * StreetDetourFactor / AssumedSpeed + CallExtraSeconds;
            _callByDriver[best] = call;
            call.Customer.ServerSetTaxiState(PlayerServices.TaxiState.Coming);

            string driverName = best.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "un chauffeur";
            call.Customer.ServerSetTaxiDriver(driverName, best.Vehicle != null ? best.Vehicle.ObjectId : -1);
            call.CustomerEconomy.ServerNotify("Ton taxi arrive : " + driverName + " est à " + Mathf.RoundToInt(distance) +
                                              " m. Monte quand il est là.");
            best.Economy?.ServerNotify("Course commandée par " + call.CustomerName + " (" + Money.Format(call.Fare) +
                                       ") : va le chercher, il est sur ton GPS (" + Mathf.RoundToInt(distance) + " m).");
            if (best.TryGetComponent(out PlayerServices driverServices))
                driverServices.ServerSuggestWaypoint(call.Pickup);
        }

        private void UpdateCalls(float now)
        {
            for (int i = _calls.Count - 1; i >= 0; i--)
            {
                TaxiCall call = _calls[i];
                if (call.Customer == null || call.CustomerDriving == null)
                {
                    if (call.Driver != null)
                        call.Driver.Economy?.ServerNotify("Le client s'est déconnecté : course annulée.");
                    EndCall(call);
                    continue;
                }

                bool assigned = !ReferenceEquals(call.Driver, null);
                bool driverLost = assigned && (call.Driver == null || !call.Driver.IsDriving);
                if (driverLost && call.Onboard)
                {
                    // Le chauffeur est descendu (ou parti) en pleine course : paiement au prorata.
                    VehicleMotor ride = call.CustomerDriving.Vehicle;
                    Vector3 from = ride != null ? ride.transform.position : call.Customer.transform.position;
                    Settle(call, ProratedFare(call, from), "Le chauffeur a arrêté la course.");
                    EndCall(call);
                    continue;
                }
                if (driverLost)
                    Unassign(call, "Le chauffeur a quitté son taxi : on t'en cherche un autre.");
                if (ReferenceEquals(call.Driver, null))
                {
                    if (now - call.CreatedAt > CallSearchSeconds)
                    {
                        call.CustomerEconomy.ServerNotify("Aucun chauffeur de taxi disponible pour l'instant. Réessaie plus tard.");
                        EndCall(call);
                        continue;
                    }
                    TryAssign(call, now);
                    continue;
                }

                if (!call.Onboard)
                {
                    if (now > call.Deadline)
                    {
                        call.Driver.Economy?.ServerNotify("Trop lent : la course de " + call.CustomerName + " passe à un autre chauffeur.");
                        Unassign(call, "Ton chauffeur est en retard : on t'en cherche un autre.");
                        call.CreatedAt = now; // nouvelle recherche complète
                        continue;
                    }
                    // Le client s'est déplacé : le GPS du chauffeur suit.
                    Vector3 position = call.Customer.transform.position;
                    if ((position - call.Pickup).sqrMagnitude > PickupMoveThreshold * PickupMoveThreshold)
                    {
                        call.Pickup = position;
                        if (call.Driver.TryGetComponent(out PlayerServices driverServices))
                            driverServices.ServerSuggestWaypoint(position);
                    }
                    continue;
                }

                // À bord : arrivée quand le taxi s'arrête près de la destination.
                VehicleMotor taxi = call.CustomerDriving.Vehicle;
                if (taxi == null)
                    continue;
                Vector3 offset = call.Destination - taxi.transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= DestinationRadius * DestinationRadius && Mathf.Abs(taxi.Speed) <= MaxStopSpeed)
                {
                    Settle(call, call.Fare, "Arrivé à destination !");
                    EndCall(call);
                }
            }
        }

        private void Unassign(TaxiCall call, string customerMessage)
        {
            RemoveDriverEntry(call);
            call.Driver = null;
            if (call.Customer != null)
            {
                call.Customer.ServerSetTaxiDriver(string.Empty, -1);
                call.Customer.ServerSetTaxiState(PlayerServices.TaxiState.Searching);
                call.CustomerEconomy.ServerNotify(customerMessage);
            }
        }

        // Recherche par valeur : fonctionne même si le chauffeur a été détruit (déconnexion).
        private void RemoveDriverEntry(TaxiCall call)
        {
            PlayerDriving key = null;
            bool found = false;
            foreach (KeyValuePair<PlayerDriving, TaxiCall> pair in _callByDriver)
            {
                if (pair.Value == call)
                {
                    key = pair.Key;
                    found = true;
                }
            }
            if (found)
                _callByDriver.Remove(key);
        }

        private void EndCall(TaxiCall call)
        {
            _calls.Remove(call);
            RemoveDriverEntry(call);
            if (call.Customer != null)
                call.Customer.ServerSetTaxiState(PlayerServices.TaxiState.None);
        }

        private static long ProratedFare(TaxiCall call, Vector3 position)
        {
            Vector3 flat = call.Destination - position;
            flat.y = 0f;
            float progress = flat.magnitude <= DestinationRadius ? 1f : Mathf.Clamp01(1f - flat.magnitude / call.StartDistance);
            long amount = (long)(call.Fare * progress);
            return (amount + 12) / 25 * 25;
        }

        /// <summary>Le client paie le chauffeur (un seul paiement, pour tout le groupe).</summary>
        private void Settle(TaxiCall call, long amount, string message)
        {
            if (call.Driver == null || call.CustomerEconomy == null)
                return;
            if (amount <= 0)
            {
                call.CustomerEconomy.ServerNotify(message + " Rien à payer.");
                return;
            }
            string driverName = call.Driver.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "chauffeur";
            if (call.CustomerEconomy.ServerTrySpend(amount, "taxi avec " + driverName))
            {
                call.Driver.Economy?.ServerAdd(amount, "course de taxi : " + call.CustomerName);
                if (identity != null)
                    identity.ServerAddXp(35, "taxi");
                call.CustomerEconomy.ServerNotify(message + " Course payée : " + Money.Format(amount) + ".");
                call.Driver.Economy?.ServerNotify("Course payée par " + call.CustomerName + " : +" + Money.Format(amount));
            }
            else
            {
                call.CustomerEconomy.ServerNotify(message + " Tu n'avais pas assez d'argent pour payer la course.");
                call.Driver.Economy?.ServerNotify(call.CustomerName + " n'a pas pu payer la course.");
            }
        }

        /// <summary>
        /// Règle de montée : dans un taxi commandé, le client monte directement et décide pour les
        /// autres (une fois à bord) ; sinon le chauffeur décide.
        /// </summary>
        private PlayerDriving BoardingRule(PlayerDriving requester, VehicleMotor vehicle, out string refusal)
        {
            refusal = null;
            PlayerDriving driver = PlayerDriving.ServerFindDriver(vehicle);
            if (driver != null && _callByDriver.TryGetValue(driver, out TaxiCall call))
            {
                if (requester == call.CustomerDriving)
                    return null;
                if (call.Onboard)
                    return call.CustomerDriving;
                refusal = "Ce taxi est réservé par un client.";
                return null;
            }
            return driver;
        }

        private void OnRideChanged(PlayerDriving player, VehicleMotor vehicle, bool boarded)
        {
            for (int i = 0; i < _calls.Count; i++)
            {
                TaxiCall call = _calls[i];
                if (call.CustomerDriving != player || call.Driver == null)
                    continue;

                if (boarded && !call.Onboard && vehicle != null && call.Driver.Vehicle == vehicle)
                {
                    call.Onboard = true;
                    Vector3 flat = call.Destination - vehicle.transform.position;
                    flat.y = 0f;
                    call.StartDistance = Mathf.Max(flat.magnitude, 1f);
                    call.Customer.ServerSetTaxiState(PlayerServices.TaxiState.Riding);
                    call.CustomerEconomy.ServerNotify("En route ! Tes amis peuvent monter : tu acceptes chacun.");
                    call.Driver.Economy?.ServerNotify(call.CustomerName + " est à bord. Destination sur ton GPS.");
                    if (call.Driver.TryGetComponent(out PlayerServices driverServices))
                        driverServices.ServerSuggestWaypoint(call.Destination);
                    return;
                }

                if (!boarded && call.Onboard)
                {
                    // Descente avant l'arrivée : paiement au prorata du chemin fait.
                    long amount = ProratedFare(call, player.transform.position);
                    Settle(call, amount, amount >= call.Fare ? "Arrivé à destination !" : "Course interrompue.");
                    EndCall(call);
                    return;
                }
            }
        }

        #endregion

        private void OfferFare(PlayerDriving driver, VehicleMotor taxi, float now)
        {
            DeliveryPoint pickup = PickPoint(taxi.transform.position, PickupMin, PickupMax);
            if (pickup == null)
                return;

            float distance = Vector3.Distance(taxi.transform.position, pickup.transform.position);
            float limit = distance * StreetDetourFactor / AssumedSpeed + ExtraSeconds;
            _fares[driver] = new Fare { Driver = driver, TargetId = pickup.Id, Deadline = now + limit };

            driver.Economy.ServerSetDelivery(pickup.Id, (ushort)Mathf.CeilToInt(limit), MissionKind.TaxiPickup);
            driver.Economy.ServerNotify("Un client t'attend devant « " + pickup.DisplayName + " » (" +
                                        Mathf.RoundToInt(distance) + " m).");
        }

        private void UpdateFare(PlayerDriving driver, VehicleMotor taxi, Fare fare, float now)
        {
            if (!WorldInteractable.TryGet(fare.TargetId, out WorldInteractable target))
            {
                Cancel(driver, null);
                return;
            }

            Vector3 offset = target.transform.position - taxi.transform.position;
            offset.y = 0f;
            bool arrived = offset.sqrMagnitude <= ArrivalRadius * ArrivalRadius && Mathf.Abs(taxi.Speed) <= MaxStopSpeed;
            if (!arrived)
                return;

            if (!fare.Onboard)
            {
                DeliveryPoint destination = PickPoint(target.transform.position, DropoffMin, DropoffMax);
                if (destination == null)
                {
                    Cancel(driver, "Le client a changé d'avis.");
                    return;
                }

                fare.Onboard = true;
                fare.TargetId = destination.Id;
                fare.RideDistance = Vector3.Distance(target.transform.position, destination.transform.position);
                float limit = fare.RideDistance * StreetDetourFactor / AssumedSpeed + ExtraSeconds;
                fare.Deadline = now + limit;

                driver.Economy.ServerSetDelivery(destination.Id, (ushort)Mathf.CeilToInt(limit), MissionKind.TaxiDropoff);
                driver.Economy.ServerNotify("Client à bord ! Direction « " + destination.DisplayName + " » (" +
                                            Mathf.RoundToInt(fare.RideDistance) + " m). Course : " +
                                            Money.Format(ComputeFare(fare.RideDistance)) + ".");
                return;
            }

            long pay = ComputeFare(fare.RideDistance);
            _fares.Remove(driver);
            driver.Economy.ServerClearDelivery();
            driver.Economy.ServerAdd(pay, "course de taxi : " + target.DisplayName);
            driver.GetComponent<PlayerNetworkIdentity>()?.ServerAddXp(40, "taxi");
            driver.Economy.ServerNotify("Course terminée, merci chauffeur ! +" + Money.Format(pay));
            _drivingSince[driver] = now; // prochain client après le délai habituel
        }

        private void CleanUp(float now)
        {
            _toRemove.Clear();
            foreach (KeyValuePair<PlayerDriving, Fare> pair in _fares)
            {
                PlayerDriving driver = pair.Key;
                if (driver == null || !driver.IsDriving || now > pair.Value.Deadline)
                    _toRemove.Add(driver);
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                PlayerDriving driver = _toRemove[i];
                string reason = driver == null ? null
                    : !driver.IsDriving ? "Tu as quitté le taxi : la course est annulée."
                    : "Trop tard, le client a pris un autre taxi.";
                Cancel(driver, reason);
            }
        }

        private void Cancel(PlayerDriving driver, string reason)
        {
            _fares.Remove(driver);
            if (driver == null || driver.Economy == null)
                return;

            driver.Economy.ServerClearDelivery();
            if (reason != null)
                driver.Economy.ServerNotify(reason);
        }

        private DeliveryPoint PickPoint(Vector3 from, float minDistance, float maxDistance)
        {
            _candidates.Clear();
            var all = WorldInteractable.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is DeliveryPoint point)
                {
                    float sqr = (point.transform.position - from).sqrMagnitude;
                    if (sqr >= minDistance * minDistance && sqr <= maxDistance * maxDistance)
                        _candidates.Add(point);
                }
            }

            return _candidates.Count > 0 ? _candidates[_random.Next(_candidates.Count)] : null;
        }

        private static long ComputeFare(float distance)
        {
            long raw = BaseFare + (long)(distance * FarePerMeter);
            return (raw + Rounding / 2) / Rounding * Rounding;
        }
    }
}
