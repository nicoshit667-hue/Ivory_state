using FishNet.Object;
using FishNet.Object.Synchronizing;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Gameplay.Vehicles
{
    /// <summary>
    /// Monter dans un véhicule et en descendre.
    /// <para>
    /// Le serveur décide de tout : il vérifie la distance et que la place est libre, donne la propriété
    /// du véhicule au conducteur (qui le pilote alors avec prédiction), puis la reprend à la descente.
    /// Si le joueur se déconnecte au volant, le véhicule reste dans la rue, libre.
    /// </para>
    /// <para>
    /// Au volant, le personnage est masqué et suit le véhicule. Sa position serveur suit donc la voiture,
    /// ce qui garde la gestion de l'intérêt correcte : on reçoit ce qui entoure le véhicule.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDriving : NetworkBehaviour
    {
        public const int NoVehicle = -1;
        private const float EnterRange = 4.5f; // tolérance serveur incluse

        public const int MaxPassengers = 4;

        private readonly SyncVar<int> _vehicleObjectId = new SyncVar<int>(NoVehicle);
        private readonly SyncVar<int> _rideObjectId = new SyncVar<int>(NoVehicle);

        private PlayerMotor _motor;
        private PlayerEconomy _economy;
        private CharacterController _characterController;
        private VehicleMotor _vehicle;

        public static PlayerDriving Local { get; private set; }

        private static readonly System.Collections.Generic.List<PlayerDriving> s_serverInstances =
            new System.Collections.Generic.List<PlayerDriving>(128);

        /// <summary>Joueurs côté serveur (pour les courses de taxi).</summary>
        public static System.Collections.Generic.IReadOnlyList<PlayerDriving> ServerInstances => s_serverInstances;

        public PlayerEconomy Economy => _economy;

        /// <summary>Au volant (conducteur).</summary>
        public bool IsDriving => _vehicleObjectId.Value != NoVehicle;

        /// <summary>Assis à l'arrière d'un véhicule conduit par un autre joueur.</summary>
        public bool IsPassenger => _rideObjectId.Value != NoVehicle;

        /// <summary>Dans un véhicule, conducteur ou passager : le personnage est masqué et suit le véhicule.</summary>
        public bool IsInVehicle => IsDriving || IsPassenger;

        private int OccupiedId => IsDriving ? _vehicleObjectId.Value : _rideObjectId.Value;

        /// <summary>Véhicule occupé, conduit ou non (null si à pied ou pas encore répliqué).</summary>
        public VehicleMotor Vehicle
        {
            get
            {
                if (!IsInVehicle)
                    return null;
                int id = OccupiedId;
                if (_vehicle == null || _vehicle.ObjectId != id)
                    _vehicle = Find(id);
                return _vehicle;
            }
        }

        /// <summary>Serveur : clé du joueur (compte Nakama, sinon objet réseau).</summary>
        public string ServerKey =>
            TryGetComponent(out Accounts.PlayerAccount account) && account.ServerUserId != null ? account.ServerUserId : "objet:" + ObjectId;

        /// <summary>Serveur : nombre de passagers d'un véhicule.</summary>
        public static int ServerPassengerCount(VehicleMotor vehicle)
        {
            int count = 0;
            for (int i = 0; i < s_serverInstances.Count; i++)
            {
                if (s_serverInstances[i] != null && s_serverInstances[i]._rideObjectId.Value == vehicle.ObjectId)
                    count++;
            }
            return count;
        }

        /// <summary>Serveur : un joueur monte ou descend comme passager. (joueur, véhicule, monté ?)</summary>
        public static event System.Action<PlayerDriving, VehicleMotor, bool> ServerRideChanged;

        /// <summary>
        /// Règle de montée (serveur, posée par le service des taxis) : renvoie le joueur qui doit accepter
        /// (null = montée directe) ou remplit <paramref name="refusal"/> pour refuser.
        /// Sans règle : le chauffeur décide.
        /// </summary>
        public delegate PlayerDriving BoardingRule(PlayerDriving requester, VehicleMotor vehicle, out string refusal);

        public static BoardingRule ServerBoardingRule { get; set; }

        /// <summary>Client : quelqu'un veut monter dans le taxi dont je décide. (objet du demandeur, nom)</summary>
        public static event System.Action<int, string> LocalBoardingRequest;

        private const float BoardingRequestSeconds = 30f;
        private const float BoardingWaitRange = 8f;

        private struct PendingBoarding
        {
            public VehicleMotor Vehicle;
            public PlayerDriving Authority;
            public float Expires;
        }

        private static readonly System.Collections.Generic.Dictionary<PlayerDriving, PendingBoarding> s_pendingBoardings =
            new System.Collections.Generic.Dictionary<PlayerDriving, PendingBoarding>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            s_serverInstances.Clear();
            ServerRideChanged = null;
            ServerBoardingRule = null;
            LocalBoardingRequest = null;
            s_pendingBoardings.Clear();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            s_serverInstances.Add(this);
        }

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _economy = GetComponent<PlayerEconomy>();
            _characterController = GetComponent<CharacterController>();
            _vehicleObjectId.OnChange += OnVehicleChanged;
            _rideObjectId.OnChange += OnRideChanged;
        }

        private void OnDestroy()
        {
            _vehicleObjectId.OnChange -= OnVehicleChanged;
            _rideObjectId.OnChange -= OnRideChanged;
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
            s_serverInstances.Remove(this);
            s_pendingBoardings.Remove(this);
            ServerExit(); // déconnexion au volant : le véhicule est libéré
        }

        private void OnVehicleChanged(int previous, int next, bool asServer)
        {
            bool driving = next != NoVehicle;

            // Descente, côté client : même placement que le serveur (à côté de la portière), calculé
            // sur la copie locale du véhicule, à l'arrêt donc quasi identique.
            if (!asServer && !driving && previous != NoVehicle && !IsServerStarted)
            {
                VehicleMotor previousVehicle = Find(previous);
                if (previousVehicle != null)
                {
                    _characterController.enabled = true;
                    _motor?.Place(previousVehicle.DriverExitPosition);
                }
            }

            _vehicle = driving ? Find(next) : null;

            // Personnage masqué et sans collision tant qu'il est au volant.
            SetHidden(IsInVehicle);
        }

        private void OnRideChanged(int previous, int next, bool asServer)
        {
            bool riding = next != NoVehicle;
            if (!asServer && !riding && previous != NoVehicle && !IsServerStarted)
            {
                VehicleMotor previousVehicle = Find(previous);
                if (previousVehicle != null)
                {
                    _characterController.enabled = true;
                    _motor?.Place(previousVehicle.PassengerExitPosition);
                }
            }

            _vehicle = riding ? Find(next) : (IsDriving ? Find(_vehicleObjectId.Value) : null);
            SetHidden(IsInVehicle);
        }

        private void SetHidden(bool hidden)
        {
            _characterController.enabled = !hidden;
            if (_motor != null && _motor.GraphicalTransform != transform)
                _motor.GraphicalTransform.gameObject.SetActive(!hidden);
        }

        #region Client

        /// <summary>Monte au volant si le véhicule est libre, sinon à l'arrière s'il reste une place.</summary>
        public void RequestEnter(VehicleMotor vehicle)
        {
            if (IsOwner && !IsInVehicle && vehicle != null)
                ServerRequestEnter(vehicle);
        }

        public void RequestExit()
        {
            if (IsOwner && IsInVehicle)
                ServerRequestExit();
        }

        #endregion

        #region Serveur

        [ServerRpc]
        private void ServerRequestEnter(VehicleMotor vehicle)
        {
            if (vehicle == null || !vehicle.IsSpawned || IsInVehicle)
                return;
            if ((vehicle.transform.position - transform.position).sqrMagnitude > EnterRange * EnterRange)
                return;

            if (vehicle.HasDriver)
            {
                if (ServerPassengerCount(vehicle) >= MaxPassengers)
                {
                    _economy?.ServerNotify("Ce taxi est plein.");
                    return;
                }

                // Qui décide ? Le client d'une course commandée, sinon le chauffeur.
                string refusal = null;
                PlayerDriving authority = ServerBoardingRule != null
                    ? ServerBoardingRule(this, vehicle, out refusal)
                    : ServerFindDriver(vehicle);
                if (refusal != null)
                {
                    _economy?.ServerNotify(refusal);
                    return;
                }
                if (authority == null || authority == this)
                {
                    ServerBoard(vehicle);
                    return;
                }

                s_pendingBoardings[this] = new PendingBoarding
                {
                    Vehicle = vehicle, Authority = authority, Expires = Time.unscaledTime + BoardingRequestSeconds,
                };
                string name = TryGetComponent(out Player.PlayerNetworkIdentity identity) ? identity.DisplayName : "Quelqu'un";
                if (authority.Owner.IsValid)
                    authority.TargetBoardingRequest(authority.Owner, ObjectId, name);
                _economy?.ServerNotify("Demande envoyée : on te répond…");
                return;
            }

            // Voiture personnelle : seul son propriétaire la conduit.
            if (vehicle.IsPrivate && vehicle.ServerOwnerKey != ServerKey)
            {
                _economy?.ServerNotify("Cette voiture appartient à " + vehicle.OwnerName + ".");
                return;
            }
            // Permis de conduire (auto-école).
            if (PlayerGarage.Rules.LicenseRequired && TryGetComponent(out PlayerGarage garage) && !garage.HasLicense)
            {
                _economy?.ServerNotify("Il te faut le permis de conduire : passe-le à l'auto-école (" +
                                       IvoryState.Gameplay.Economy.Money.Format(PlayerGarage.Rules.LicensePrice) + ").");
                return;
            }

            vehicle.GiveOwnership(Owner);
            vehicle.ServerSetDriver(ObjectId);
            _vehicleObjectId.Value = vehicle.ObjectId;
            _vehicle = vehicle;

            _economy?.ServerNotify("Au volant ! Joystick : avancer / freiner / tourner. Bouton d'action pour descendre.");
        }

        [ServerRpc]
        private void ServerRequestExit()
        {
            ServerExit();
        }

        [Server]
        private void ServerBoard(VehicleMotor vehicle)
        {
            s_pendingBoardings.Remove(this);
            _rideObjectId.Value = vehicle.ObjectId;
            _vehicle = vehicle;
            _economy?.ServerNotify("Tu es passager. Bouton d'action pour descendre.");
            ServerRideChanged?.Invoke(this, vehicle, true);
        }

        /// <summary>Serveur : fait monter un joueur sans demande (client d'une course arrivé au taxi).</summary>
        [Server]
        public bool ServerTryBoard(VehicleMotor vehicle)
        {
            if (vehicle == null || IsInVehicle || ServerPassengerCount(vehicle) >= MaxPassengers)
                return false;
            ServerBoard(vehicle);
            return true;
        }

        /// <summary>Client : réponse du client (ou du chauffeur) à une demande de montée.</summary>
        public void RespondBoarding(int requesterObjectId, bool accept)
        {
            if (IsOwner)
                ServerRespondBoarding(requesterObjectId, accept);
        }

        [ServerRpc]
        private void ServerRespondBoarding(int requesterObjectId, bool accept)
        {
            PlayerDriving requester = null;
            for (int i = 0; i < s_serverInstances.Count; i++)
            {
                if (s_serverInstances[i] != null && s_serverInstances[i].ObjectId == requesterObjectId)
                    requester = s_serverInstances[i];
            }
            if (requester == null || !s_pendingBoardings.TryGetValue(requester, out PendingBoarding pending) ||
                pending.Authority != this)
                return;

            s_pendingBoardings.Remove(requester);
            VehicleMotor vehicle = pending.Vehicle;
            bool stillThere = vehicle != null && vehicle.IsSpawned && vehicle.HasDriver && !requester.IsInVehicle &&
                              Time.unscaledTime <= pending.Expires &&
                              (vehicle.transform.position - requester.transform.position).sqrMagnitude <=
                              BoardingWaitRange * BoardingWaitRange;
            if (!accept)
            {
                requester._economy?.ServerNotify("On a refusé que tu montes.");
                return;
            }
            if (!stillThere)
            {
                _economy?.ServerNotify("Trop tard : la personne n'est plus à côté du taxi.");
                return;
            }
            if (ServerPassengerCount(vehicle) >= MaxPassengers)
            {
                requester._economy?.ServerNotify("Ce taxi est plein.");
                return;
            }
            requester.ServerBoard(vehicle);
        }

        [TargetRpc]
        private void TargetBoardingRequest(FishNet.Connection.NetworkConnection connection, int requesterObjectId, string name)
        {
            LocalBoardingRequest?.Invoke(requesterObjectId, name);
        }

        /// <summary>Serveur : conducteur d'un véhicule.</summary>
        public static PlayerDriving ServerFindDriver(VehicleMotor vehicle)
        {
            for (int i = 0; i < s_serverInstances.Count; i++)
            {
                if (s_serverInstances[i] != null && s_serverInstances[i]._vehicleObjectId.Value == vehicle.ObjectId)
                    return s_serverInstances[i];
            }
            return null;
        }

        [Server]
        public void ServerExit()
        {
            if (IsPassenger)
            {
                VehicleMotor ride = Vehicle;
                _rideObjectId.Value = NoVehicle;
                _vehicle = null;
                if (ride != null)
                    _motor?.ServerTeleport(ride.PassengerExitPosition);
                ServerRideChanged?.Invoke(this, ride, false);
                return;
            }

            if (!IsDriving)
                return;

            VehicleMotor vehicle = Vehicle;
            Vector3 exitPosition = transform.position;
            if (vehicle != null)
            {
                exitPosition = vehicle.DriverExitPosition;
                vehicle.ServerSetDriver(VehicleMotor.NoDriver);
                if (vehicle.IsSpawned)
                    vehicle.RemoveOwnership();
            }

            _vehicleObjectId.Value = NoVehicle;
            _vehicle = null;
            _motor?.ServerTeleport(exitPosition);
        }

        #endregion

        private VehicleMotor Find(int objectId)
        {
            var all = VehicleMotor.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].ObjectId == objectId)
                    return all[i];
            }

            return null;
        }
    }
}
