using FishNet.Object;
using IvoryState.Gameplay.Input;
using IvoryState.Gameplay.Interaction;
using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    /// <summary>
    /// Interactions du joueur avec le monde.
    /// <list type="bullet">
    /// <item><b>Client (propriétaire)</b> : cherche le point le plus proche à portée (pour le bouton
    /// d'action du HUD) et envoie la demande au serveur quand le joueur appuie.</item>
    /// <item><b>Serveur</b> : revérifie tout (point existant, distance mesurée sur la position
    /// serveur, cadence) avant de transmettre à la logique métier.</item>
    /// </list>
    /// La recherche ne passe pas par la physique : une simple boucle sur les points chargés,
    /// 6 fois par seconde, sans allocation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        private const float ScanInterval = 0.15f;
        private const float ClientCooldown = 0.3f;
        private const float ServerCooldown = 0.25f;

        // Tolérance serveur : la position serveur peut avoir quelques ticks de retard sur celle du client.
        private const float ServerRangeTolerance = 1.5f;

        private float _nextScan;
        private float _nextClientRequest;
        private float _nextServerRequest;

        public PlayerEconomy Economy { get; private set; }
        public PlayerNeeds Needs { get; private set; }

        /// <summary>Point d'interaction actuellement à portée (joueur local uniquement).</summary>
        public WorldInteractable Current { get; private set; }

        /// <summary>Véhicule libre à portée, s'il est plus proche que tout point d'interaction.</summary>
        public Vehicles.VehicleMotor CurrentVehicle { get; private set; }

        /// <summary>Autre joueur à portée quand aucun point ni véhicule n'est ciblé (inspection de profil).</summary>
        public PlayerNetworkIdentity CurrentPlayer { get; private set; }

        /// <summary>En voiture : portail à portée (bouton « Portail » du HUD de conduite).</summary>
        public WorldDoor CurrentGate { get; private set; }

        /// <summary>Texte du bouton d'action (null = bouton masqué).</summary>
        public string CurrentPrompt
        {
            get
            {
                if (Driving != null && Driving.IsInVehicle)
                    return "Descendre du taxi";
                if (CurrentVehicle != null)
                    return CurrentVehicle.HasDriver ? "Monter comme passager" : "Prendre le volant";
                if (Current != null)
                    return Current.Prompt;
                return CurrentPlayer != null ? "Voir le profil de " + CurrentPlayer.DisplayName : null;
            }
        }

        public Vehicles.PlayerDriving Driving { get; private set; }

        private const float VehicleRange = 3.5f;
        private const float PlayerInspectRange = 2.8f;

        /// <summary>Le joueur local, pour le HUD.</summary>
        public static PlayerInteractor Local { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalMenuRequested = null;
            LocalTravelStarted = null;
            LocalProfileInspectRequested = null;
        }

        private void Awake()
        {
            Economy = GetComponent<PlayerEconomy>();
            Needs = GetComponent<PlayerNeeds>();
            Driving = GetComponent<Vehicles.PlayerDriving>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
            {
                Local = this;
                // Portes ouvertes ou fermées par d'autres joueurs avant notre arrivée.
                if (!IsServerStarted)
                    ServerRequestDoorStates();
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local == this)
                Local = null;
        }

        private void Update()
        {
            if (!IsOwner || !IsClientStarted)
                return;

            bool driving = Driving != null && Driving.IsInVehicle;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                if (driving)
                {
                    Current = null;
                    CurrentVehicle = null;
                    CurrentPlayer = null;
                    Vehicles.VehicleMotor vehicle = Driving.Vehicle;
                    CurrentGate = WorldDoor.NearestVehicleGate(vehicle != null ? vehicle.transform.position : transform.position);
                }
                else
                {
                    CurrentGate = null;
                    Current = FindNearest(out float pointSqr);
                    CurrentVehicle = FindNearestFreeVehicle(pointSqr);
                    CurrentPlayer = Current == null && CurrentVehicle == null ? FindNearestPlayer() : null;
                }
            }

            IActionInputProvider actions = PlayerInputRegistry.Actions;
            if (actions == null || !actions.ConsumeInteract() || Time.unscaledTime < _nextClientRequest)
                return;

            _nextClientRequest = Time.unscaledTime + ClientCooldown;
            if (driving)
                Driving.RequestExit();
            else if (CurrentVehicle != null)
                Driving?.RequestEnter(CurrentVehicle);
            else if (Current is MenuInteractable menu)
                LocalMenuRequested?.Invoke(menu); // le HUD affiche les choix, puis appelle RequestChoice
            else if (Current != null)
                ServerRequestInteract(Current.Id);
            else if (CurrentPlayer != null)
                LocalProfileInspectRequested?.Invoke(CurrentPlayer.ObjectId);
        }

        /// <summary>Client : le joueur local a ouvert un menu (boutique, gare…). Écouté par le HUD.</summary>
        public static event System.Action<MenuInteractable> LocalMenuRequested;

        /// <summary>Client : le joueur local va être déplacé (trajet en gbaka). Écouté par le HUD (fondu).</summary>
        public static event System.Action<string> LocalTravelStarted;

        /// <summary>Client : le joueur local veut consulter le profil d'un autre joueur à proximité.</summary>
        public static event System.Action<int> LocalProfileInspectRequested;

        /// <summary>Client : choix d'une option dans le menu d'un point.</summary>
        public void RequestChoice(MenuInteractable menu, int option, bool keep = false)
        {
            if (!IsOwner || menu == null || option < 0 || option >= menu.OptionCount)
                return;
            if (Time.unscaledTime < _nextClientRequest)
                return;
            _nextClientRequest = Time.unscaledTime + ClientCooldown;
            ServerRequestChoice(menu.Id, option, keep);
        }

        [ServerRpc]
        private void ServerRequestChoice(int interactableId, int option, bool keep)
        {
            if (Time.unscaledTime < _nextServerRequest)
                return;
            _nextServerRequest = Time.unscaledTime + ServerCooldown;

            if (!WorldInteractable.TryGet(interactableId, out WorldInteractable target) || !(target is MenuInteractable menu))
                return;
            if (option < 0 || option >= menu.OptionCount)
                return;

            float maxRange = target.Range + ServerRangeTolerance;
            if ((target.transform.position - transform.position).sqrMagnitude > maxRange * maxRange)
                return;

            InteractionRouter.ServerChoiceHandler?.Invoke(this, menu, option, keep);
        }

        /// <summary>
        /// Serveur : déplace instantanément le joueur (trajet en gbaka). Le propriétaire place aussi son
        /// personnage localement, sans attendre le recalage, et affiche un fondu.
        /// </summary>
        [Server]
        public void ServerTravel(Vector3 destination, string description)
        {
            if (Driving != null && Driving.IsInVehicle)
                return;
            PlayerMotor motor = GetComponent<PlayerMotor>();
            if (motor == null)
                return;
            motor.ServerTeleport(destination);
            if (Owner.IsValid)
                TargetTravel(Owner, destination, description);
        }

        [FishNet.Object.TargetRpc]
        private void TargetTravel(FishNet.Connection.NetworkConnection connection, Vector3 destination, string description)
        {
            if (IsServerStarted)
                return; // hôte : déjà placé par le serveur
            PlayerMotor motor = GetComponent<PlayerMotor>();
            if (motor != null)
                motor.Place(destination);
            LocalTravelStarted?.Invoke(description);
        }

        /// <summary>
        /// Véhicule le plus proche (libre : on prend le volant ; conduit : on demande à monter), s'il est
        /// plus proche que le point trouvé.
        /// </summary>
        private Vehicles.VehicleMotor FindNearestFreeVehicle(float maxSqr)
        {
            Vector3 position = transform.position;
            Vehicles.VehicleMotor best = null;
            float bestSqr = Mathf.Min(maxSqr, VehicleRange * VehicleRange);

            var all = Vehicles.VehicleMotor.All;
            for (int i = 0; i < all.Count; i++)
            {
                Vehicles.VehicleMotor vehicle = all[i];
                if (vehicle == null)
                    continue;

                float sqr = (vehicle.transform.position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = vehicle;
                    bestSqr = sqr;
                }
            }

            return best;
        }

        private PlayerNetworkIdentity FindNearestPlayer()
        {
            Vector3 position = transform.position;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f)
                forward.Normalize();

            PlayerNetworkIdentity best = null;
            float bestSqr = PlayerInspectRange * PlayerInspectRange;
            var all = PlayerNetworkIdentity.ClientInstances;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerNetworkIdentity candidate = all[i];
                if (candidate == null || candidate.gameObject == gameObject)
                    continue;
                if (candidate.TryGetComponent(out Vehicles.PlayerDriving driving) && driving.IsInVehicle)
                    continue;

                Vector3 delta = candidate.transform.position - position;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;
                if (sqr > bestSqr)
                    continue;
                if (sqr > 1.2f && forward.sqrMagnitude > 0.001f && Vector3.Dot(delta.normalized, forward) < 0.35f)
                    continue;

                best = candidate;
                bestSqr = sqr;
            }
            return best;
        }

        private WorldInteractable FindNearest(out float bestSqr)
        {
            Vector3 position = transform.position;
            WorldInteractable best = null;
            bestSqr = float.MaxValue;

            var all = WorldInteractable.All;
            for (int i = 0; i < all.Count; i++)
            {
                WorldInteractable candidate = all[i];
                float sqr = (candidate.transform.position - position).sqrMagnitude;
                float range = candidate.Range;
                if (sqr <= range * range && sqr < bestSqr)
                {
                    best = candidate;
                    bestSqr = sqr;
                }
            }

            return best;
        }

        [ServerRpc]
        private void ServerRequestInteract(int interactableId)
        {
            if (Time.unscaledTime < _nextServerRequest)
                return;
            _nextServerRequest = Time.unscaledTime + ServerCooldown;

            if (!WorldInteractable.TryGet(interactableId, out WorldInteractable target))
                return;

            float maxRange = target.Range + ServerRangeTolerance;
            if ((target.transform.position - transform.position).sqrMagnitude > maxRange * maxRange)
                return;

            if (target is WorldDoor door)
            {
                ServerToggleDoor(door);
                return;
            }

            InteractionRouter.ServerHandler?.Invoke(this, target);
        }

        #region Portes et portails

        /// <summary>Client : ouvre ou ferme le portail à portée de la voiture (bouton du HUD de conduite).</summary>
        public void RequestToggleGate()
        {
            if (!IsOwner || CurrentGate == null || Time.unscaledTime < _nextClientRequest)
                return;
            _nextClientRequest = Time.unscaledTime + ClientCooldown;
            ServerRequestGate(CurrentGate.Id);
        }

        [ServerRpc]
        private void ServerRequestGate(int doorId)
        {
            if (Time.unscaledTime < _nextServerRequest)
                return;
            _nextServerRequest = Time.unscaledTime + ServerCooldown;
            if (Driving == null || !Driving.IsInVehicle)
                return;
            if (!WorldInteractable.TryGet(doorId, out WorldInteractable target) || !(target is WorldDoor door) || !door.VehicleGate)
                return;
            // Au volant, la position serveur du joueur suit la voiture.
            float maxRange = WorldDoor.VehicleRange + ServerRangeTolerance + 2f;
            if ((door.transform.position - transform.position).sqrMagnitude > maxRange * maxRange)
                return;
            ServerToggleDoor(door);
        }

        /// <summary>Serveur : bascule la porte et prévient tous les joueurs connectés.</summary>
        private static void ServerToggleDoor(WorldDoor door)
        {
            bool open = !door.IsOpen;
            door.SetOpen(open);
            var players = Vehicles.PlayerDriving.ServerInstances;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == null)
                    continue;
                PlayerInteractor interactor = players[i].GetComponent<PlayerInteractor>();
                if (interactor != null && interactor.Owner.IsValid && !interactor.Owner.IsLocalClient)
                    interactor.TargetDoorChanged(interactor.Owner, door.Id, open);
            }
        }

        [FishNet.Object.TargetRpc]
        private void TargetDoorChanged(FishNet.Connection.NetworkConnection connection, int doorId, bool open)
        {
            if (WorldInteractable.TryGet(doorId, out WorldInteractable target) && target is WorldDoor door)
                door.SetOpen(open);
        }

        private static readonly System.Collections.Generic.List<int> s_doorIds = new System.Collections.Generic.List<int>();
        private static readonly System.Collections.Generic.List<bool> s_doorStates = new System.Collections.Generic.List<bool>();

        [ServerRpc]
        private void ServerRequestDoorStates()
        {
            DoorStates.CollectChanged(s_doorIds, s_doorStates);
            if (s_doorIds.Count > 0)
                TargetDoorStates(Owner, s_doorIds.ToArray(), s_doorStates.ToArray());
        }

        [FishNet.Object.TargetRpc]
        private void TargetDoorStates(FishNet.Connection.NetworkConnection connection, int[] doorIds, bool[] open)
        {
            for (int i = 0; i < doorIds.Length && i < open.Length; i++)
            {
                if (WorldInteractable.TryGet(doorIds[i], out WorldInteractable target) && target is WorldDoor door)
                    door.SetOpen(open[i], true);
            }
        }

        #endregion
    }
}
