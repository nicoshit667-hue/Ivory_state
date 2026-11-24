using System.Collections.Generic;
using FishNet.Object;
using IvoryState.Core.Diagnostics;
using IvoryState.Core.Networking;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.AI;

namespace IvoryState.Server.Vehicles
{
    /// <summary>
    /// Permis et voitures personnelles (serveur uniquement).
    /// <list type="bullet">
    /// <item>Auto-école : 25 000 F, permis à vie (exigé pour prendre le volant).</item>
    /// <item>Concessionnaire : achat d'une berline (gardée pour toujours, 5 au plus) ou location 24 h ;
    /// la voiture apparaît sur le parking.</item>
    /// <item>Téléphone (Garage) : sortir une de ses voitures près de soi, ou la ranger. Une seule voiture
    /// sortie par joueur ; elle disparaît quand il se déconnecte, après 15 min sans conducteur, ou à la
    /// fin de la location.</item>
    /// </list>
    /// Les voitures personnelles sont le modèle du taxi, repeint, sans lumignon ni clients.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class GarageService : MonoBehaviour
    {
        private const string LogTag = "Garage";
        private const float IdleSeconds = 15f * 60f;
        private const float CheckInterval = 5f;

        private sealed class ActiveCar
        {
            public NetworkObject Object;
            public VehicleMotor Motor;
            public PlayerGarage Owner;
            public bool Rental;
            public float IdleSince = -1f;
        }

        public static GarageService Instance { get; private set; }

        private readonly Dictionary<string, ActiveCar> _active = new Dictionary<string, ActiveCar>();
        private readonly List<string> _remove = new List<string>();
        private float _nextCheck;

        private void Awake() => Instance = this;

        private void OnEnable() => PlayerGarage.ServerRequestHandler = OnRequest;

        private void OnDisable()
        {
            PlayerGarage.ServerRequestHandler = null;
            if (Instance == this)
                Instance = null;
        }

        private static string KeyOf(PlayerGarage garage) =>
            garage.TryGetComponent(out PlayerDriving driving) ? driving.ServerKey : "objet:" + garage.ObjectId;

        #region Auto-école et concessionnaire

        public void HandleDrivingSchool(PlayerInteractor player)
        {
            if (!player.TryGetComponent(out PlayerGarage garage) || player.Economy == null)
                return;
            if (garage.HasLicense)
            {
                player.Economy.ServerNotify("Tu as déjà ton permis de conduire.");
                return;
            }
            if (!player.Economy.ServerTrySpend(PlayerGarage.Rules.LicensePrice, "permis de conduire"))
            {
                player.Economy.ServerNotify("Le permis coûte " + Money.Format(PlayerGarage.Rules.LicensePrice) + ". Reviens quand tu les as.");
                return;
            }
            garage.ServerGrantLicense();
            player.GetComponent<PlayerNetworkIdentity>()?.ServerAddXp(50, "permis");
            player.Economy.ServerNotify("Félicitations, tu as ton permis de conduire ! Tu peux prendre le volant.");
        }

        public void HandleDealership(PlayerInteractor player, DealershipPoint dealer, int option)
        {
            if (!player.TryGetComponent(out PlayerGarage garage) || player.Economy == null || option < 0 || option >= dealer.OptionCount)
                return;
            PlayerEconomy economy = player.Economy;
            byte paint = dealer.PaintFor(option);
            long price = dealer.OptionPrice(option);
            bool rent = option == dealer.RentOption;
            string label = dealer.OptionLabel(option);

            if (!rent && garage.Cars.Count >= PlayerGarage.Rules.MaxCars)
            {
                economy.ServerNotify("Ton garage est plein (" + PlayerGarage.Rules.MaxCars + " voitures au plus).");
                return;
            }
            if (!economy.ServerTrySpend(price, (rent ? "location : " : "achat : ") + label))
            {
                economy.ServerNotify("Pas assez d'argent : il faut " + Money.Format(price) + ".");
                return;
            }

            if (rent)
                garage.ServerSetRental(paint, RentRules.Now + PlayerGarage.Rules.RentSeconds);
            else
                garage.ServerAddCar(paint);

            player.GetComponent<PlayerNetworkIdentity>()?.ServerAddXp(rent ? 20 : 100, rent ? "location voiture" : "achat voiture");

            Spawn(garage, paint, rent, dealer.DeliveryPosition, dealer.DeliveryRotation);
            economy.ServerNotify((rent ? "Location : " : "Félicitations, ta ") + label.ToLowerInvariant() +
                                 (rent ? " pour 24 h" : " est à toi") + " ! Elle t'attend sur le parking." +
                                 (garage.HasLicense || !PlayerGarage.Rules.LicenseRequired ? string.Empty : " Il te faudra le permis pour la conduire."));
        }

        #endregion

        #region Téléphone : sortir / ranger

        private void OnRequest(PlayerGarage garage, int index)
        {
            if (garage == null || !garage.TryGetComponent(out PlayerEconomy economy))
                return;
            if (index == PlayerGarage.StoreIndex)
            {
                if (Store(KeyOf(garage)))
                    economy.ServerNotify("Voiture rangée au garage.");
                return;
            }

            byte paint;
            bool rental = index == PlayerGarage.RentalIndex;
            if (rental)
            {
                if (garage.RentalUntil <= RentRules.Now)
                {
                    economy.ServerNotify("Tu n'as pas de location en cours.");
                    return;
                }
                paint = garage.RentalPaint;
            }
            else
            {
                if (index < 0 || index >= garage.Cars.Count)
                    return;
                paint = garage.Cars[index];
            }

            Vector3 position = garage.transform.position;
            if (StudioLayout.IsIndoors(position) || (garage.TryGetComponent(out PlayerDriving driving) && driving.IsInVehicle))
            {
                economy.ServerNotify("Sors dans la rue pour faire venir ta voiture.");
                return;
            }

            // Une place à côté du joueur, sur la chaussée si possible.
            Vector3 spot = position + garage.transform.right * 3.5f;
            if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                spot = hit.position;
            Spawn(garage, paint, rental, spot + Vector3.up * 0.1f, Quaternion.Euler(0f, garage.transform.eulerAngles.y, 0f));
            economy.ServerNotify("Ta " + VehicleMotor.PaintNames[paint].ToLowerInvariant() + " est là, à côté de toi.");
        }

        private void Spawn(PlayerGarage garage, byte paint, bool rental, Vector3 position, Quaternion rotation)
        {
            NetworkBootstrap bootstrap = NetworkBootstrap.Instance;
            if (bootstrap == null || bootstrap.Config.VehiclePrefab == null)
                return;
            string key = KeyOf(garage);
            Store(key); // une seule voiture sortie à la fois

            NetworkObject car = bootstrap.NetworkManager.GetPooledInstantiated(bootstrap.Config.VehiclePrefab, position, rotation, true);
            bootstrap.NetworkManager.ServerManager.Spawn(car);
            VehicleMotor motor = car.GetComponent<VehicleMotor>();
            string ownerName = garage.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?";
            motor.ServerMakePrivate(paint, key, ownerName);
            _active[key] = new ActiveCar { Object = car, Motor = motor, Owner = garage, Rental = rental };
            GameLog.Info(LogTag, ownerName + " sort sa " + VehicleMotor.PaintNames[paint] + (rental ? " (location)" : string.Empty) + ".");
        }

        /// <summary>Range la voiture sortie d'un joueur (vente sur Djassa…).</summary>
        public void StoreCarOf(PlayerGarage garage)
        {
            if (garage != null)
                Store(KeyOf(garage));
        }

        private bool Store(string key)
        {
            if (!_active.TryGetValue(key, out ActiveCar car))
                return false;
            _active.Remove(key);
            Despawn(car);
            return true;
        }

        private static void Despawn(ActiveCar car)
        {
            if (car.Object == null || !car.Object.IsSpawned)
                return;
            // Personne au volant ni à bord ne reste « dans » une voiture qui disparaît.
            var players = PlayerDriving.ServerInstances;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].Vehicle == car.Motor)
                    players[i].ServerExit();
            }
            car.Object.Despawn();
        }

        #endregion

        private void Update()
        {
            if (Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + CheckInterval;

            long now = RentRules.Now;
            _remove.Clear();
            foreach (KeyValuePair<string, ActiveCar> pair in _active)
            {
                ActiveCar car = pair.Value;
                if (car.Object == null || !car.Object.IsSpawned)
                {
                    _remove.Add(pair.Key);
                    continue;
                }
                // Propriétaire parti, location finie, ou voiture abandonnée trop longtemps.
                bool ownerGone = car.Owner == null;
                bool rentalOver = car.Rental && (ownerGone || car.Owner.RentalUntil <= now);
                if (car.Motor.HasDriver)
                    car.IdleSince = -1f;
                else if (car.IdleSince < 0f)
                    car.IdleSince = Time.time;
                bool idle = car.IdleSince >= 0f && Time.time - car.IdleSince > IdleSeconds;
                if (ownerGone || rentalOver || idle)
                {
                    if (rentalOver && !ownerGone && car.Owner.TryGetComponent(out PlayerEconomy economy))
                        economy.ServerNotify("Ta location est terminée : la voiture est rendue au concessionnaire.");
                    Despawn(car);
                    _remove.Add(pair.Key);
                }
            }
            foreach (string key in _remove)
                _active.Remove(key);
        }
    }
}
