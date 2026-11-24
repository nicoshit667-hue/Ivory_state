#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Bots;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Input;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Test de bout en bout du métier de chauffeur (<c>-e2eFare</c>, builds de dev) : monter dans un taxi,
    /// attendre un client, le prendre puis le déposer en conduisant seul sur le NavMesh, et vérifier
    /// le paiement. Écrit <c>[E2E-FARE] RÉUSSI</c> ou <c>ÉCHEC</c>.
    /// </summary>
    internal sealed class FareScenarioBot : MonoBehaviour, IMovementInputProvider, IActionInputProvider
    {
        private const string LogTag = "E2E-FARE";
        private const float Timeout = 300f;
        private const float StopDistance = 5f;

        private enum Step { WaitPlayer, WalkToTaxi, WaitSeat, WaitFare, DriveToPickup, WaitOnboard, DriveToDropoff, WaitPay, Done }

        private readonly NavPathFollower _follower = new NavPathFollower();
        private Step _step;
        private float _startTime;
        private float _stepStart;
        private bool _interact;
        private float _nextInteract;
        private VehicleMotor _taxi;
        private WorldInteractable _target;
        private float _nextRepath;
        private Vector3 _lastProgressPosition;
        private float _nextProgressCheck;
        private float _reverseUntil;
        private float _reverseSteer;
        private float _lastSteer;
        private int _stuckCount;
        private long _balanceBefore;

        public Vector2 Move { get; private set; }
        public bool Sprint => true;
        public float ViewYaw => 0f;
        public bool ConsumeJump() => false;

        public bool ConsumeInteract()
        {
            bool value = _interact;
            _interact = false;
            return value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-e2eFare") < 0)
                return;

            var go = new GameObject(nameof(FareScenarioBot));
            DontDestroyOnLoad(go);
            go.AddComponent<FareScenarioBot>();
            PlayerEconomy.LocalNotification += message => GameLog.Info(LogTag, "Message serveur : " + message);
        }

        private void OnEnable() => PlayerInputRegistry.Register(this);
        private void OnDisable() => PlayerInputRegistry.Unregister(this);

        private void Update()
        {
            PlayerInteractor player = PlayerInteractor.Local;
            Move = Vector2.zero;

            if (_step != Step.WaitPlayer && _step != Step.Done && Time.time - _startTime > Timeout)
            {
                Finish(false, "délai dépassé à l'étape " + _step);
                return;
            }

            switch (_step)
            {
                case Step.WaitPlayer:
                    if (player != null && VehicleMotor.All.Count > 0)
                    {
                        _startTime = Time.time;
                        _taxi = NearestFreeTaxi(player.transform.position);
                        if (_taxi == null) { Finish(false, "aucun taxi libre"); return; }
                        _follower.SidewalkCost = 1f; // à pied
                        _follower.EdgeClearance = 0f;
                        _follower.SetDestination(player.transform.position, _taxi.transform.position);
                        Enter(Step.WalkToTaxi);
                    }
                    break;

                case Step.WalkToTaxi:
                    if (player.CurrentVehicle == _taxi) { QueueInteract(); Enter(Step.WaitSeat); }
                    else Move = _follower.GetDirection(player.transform.position);
                    break;

                case Step.WaitSeat:
                    if (player.Driving.IsDriving) { GameLog.Info(LogTag, "Au volant, en attente d'un client."); Enter(Step.WaitFare); }
                    else if (Time.time - _stepStart > 3f) { QueueInteract(); _stepStart = Time.time; }
                    break;

                case Step.WaitFare:
                    if (TryGetMission(player, MissionKind.TaxiPickup, out _target))
                    {
                        _balanceBefore = player.Economy.Balance;
                        BeginDrive(Step.DriveToPickup);
                    }
                    break;

                case Step.DriveToPickup:
                    // Client perdu (« trop tard ») ou changement d'état : on suit la mission serveur active.
                    if (TryGetMission(player, MissionKind.TaxiDropoff, out _target))
                    {
                        BeginDrive(Step.DriveToDropoff);
                    }
                    else if (!TryGetMission(player, MissionKind.TaxiPickup, out WorldInteractable latest))
                    {
                        Enter(Step.WaitFare);
                    }
                    else if (latest != _target)
                    {
                        _target = latest;
                        BeginDrive(Step.DriveToPickup);
                    }
                    else if (DriveTo(_target)) Enter(Step.WaitOnboard);
                    break;

                case Step.WaitOnboard:
                    Move = new Vector2(0f, HoldStopThrottle()); // reste à l'arrêt sans reculer
                    if (TryGetMission(player, MissionKind.TaxiDropoff, out _target)) BeginDrive(Step.DriveToDropoff);
                    else if (!player.Economy.HasDelivery) Enter(Step.WaitFare);
                    else if (Time.time - _stepStart > 5f) BeginDrive(Step.DriveToPickup); // pas assez près : on se rapproche
                    break;

                case Step.DriveToDropoff:
                    if (!TryGetMission(player, MissionKind.TaxiDropoff, out WorldInteractable dropoff))
                    {
                        // Course annulée (« trop tard ») : on bascule sur un éventuel nouveau client.
                        if (TryGetMission(player, MissionKind.TaxiPickup, out _target))
                        {
                            _balanceBefore = player.Economy.Balance;
                            BeginDrive(Step.DriveToPickup);
                        }
                        else
                            Enter(Step.WaitFare);
                    }
                    else if (dropoff != _target)
                    {
                        _target = dropoff;
                        BeginDrive(Step.DriveToDropoff);
                    }
                    else if (DriveTo(_target)) Enter(Step.WaitPay);
                    break;

                case Step.WaitPay:
                    Move = new Vector2(0f, HoldStopThrottle());
                    if (player.Economy.Balance > _balanceBefore && !player.Economy.HasDelivery)
                        Finish(true, "course payée +" + Money.Format(player.Economy.Balance - _balanceBefore) +
                                     " en " + Mathf.RoundToInt(Time.time - _startTime) + " s.");
                    else if (!TryGetMission(player, MissionKind.TaxiDropoff, out _))
                    {
                        if (TryGetMission(player, MissionKind.TaxiPickup, out _target))
                        {
                            _balanceBefore = player.Economy.Balance;
                            BeginDrive(Step.DriveToPickup);
                        }
                        else
                            Enter(Step.WaitFare);
                    }
                    else if (Time.time - _stepStart > 5f)
                        BeginDrive(Step.DriveToDropoff);
                    break;
            }
        }

        private float HoldStopThrottle()
        {
            if (_taxi == null) return 0f;
            if (_taxi.Speed > 0.5f) return -1f;
            if (_taxi.Speed < -0.5f) return 1f;
            return 0f;
        }

        private static bool TryGetMission(PlayerInteractor player, MissionKind kind, out WorldInteractable target)
        {
            target = null;
            return player.Economy.HasDelivery && player.Economy.Mission == kind &&
                   WorldInteractable.TryGet(player.Economy.DeliveryTargetId, out target);
        }

        private void BeginDrive(Step step)
        {
            _nextRepath = 0f;
            _reverseUntil = 0f;
            _lastProgressPosition = _taxi.transform.position;
            _nextProgressCheck = Time.time + 3f;
            GameLog.Info(LogTag, step + " → « " + _target.DisplayName + " » à " +
                                 Mathf.RoundToInt(Vector3.Distance(_taxi.transform.position, _target.transform.position)) + " m.");
            Enter(step);
        }

        /// <summary>Conduit vers la cible ; renvoie vrai une fois arrêté à proximité.</summary>
        private bool DriveTo(WorldInteractable target)
        {
            Vector3 position = _taxi.transform.position;
            Vector3 toTarget = target.transform.position - position;
            toTarget.y = 0f;
            float remaining = toTarget.magnitude;

            if (remaining < StopDistance)
            {
                Move = new Vector2(0f, HoldStopThrottle()); // freine puis maintient à l'arrêt
                return Mathf.Abs(_taxi.Speed) < 1f;
            }

            if (Time.time >= _nextRepath)
            {
                _follower.SidewalkCost = 20f;  // en taxi : la chaussée, pas les places ni les marchés
                _follower.EdgeClearance = 3.5f; // au milieu de la voie, loin des lampadaires
                _follower.SetDestination(position, target.transform.position);
                _nextRepath = Time.time + 2f;
            }

            // Coincé contre un obstacle : marche arrière en braquant à l'opposé.
            if (Time.time >= _nextProgressCheck)
            {
                bool reversing = false;
                if ((position - _lastProgressPosition).sqrMagnitude < 4f && Time.time >= _reverseUntil)
                {
                    // Recul plus long, braqué à fond à l'opposé : sans cela, le taxi reculait tout droit et
                    // revenait buter au même endroit. Puis nouveau chemin depuis la position dégagée.
                    _reverseUntil = Time.time + 2.5f;
                    // Côté de dégagement inconnu à l'avance : on alterne d'un blocage au suivant.
                    float away = _lastSteer >= 0f ? -1f : 1f;
                    _reverseSteer = _stuckCount++ % 2 == 0 ? away : -away;
                    _nextRepath = Time.time + 2.6f;
                    reversing = true;
                    GameLog.Info(LogTag, "Bloqué en " + position.ToString("F1") + " (vitesse " + _taxi.Speed.ToString("F1") +
                                         ", cap " + _taxi.transform.eulerAngles.y.ToString("F0") + "°) : marche arrière.");
                }
                _lastProgressPosition = position;
                _nextProgressCheck = Time.time + (reversing ? 5.5f : 3f);
            }

            Vector2 direction = _follower.GetDirection(position);
            Vector3 forward = _taxi.transform.forward;
            float angle = Vector3.SignedAngle(new Vector3(forward.x, 0f, forward.z), new Vector3(direction.x, 0f, direction.y), Vector3.up);
            float steer = Mathf.Clamp(angle / 35f, -1f, 1f);

            if (Time.time < _reverseUntil)
            {
                // En marche arrière, le braquage est inversé par le véhicule : le nez part du côté de
                // _reverseSteer opposé, donc s'écarte de l'obstacle vers lequel il tournait.
                Move = new Vector2(_reverseSteer, -1f);
                return false;
            }
            _lastSteer = steer;

            // Ralentit dans les virages serrés et à l'approche.
            float throttle = Mathf.Abs(angle) > 60f ? 0.35f : 1f;
            if (remaining < 25f)
                throttle = Mathf.Abs(_taxi.Speed) > 6f ? -1f : 0.5f;

            Move = new Vector2(steer, throttle);
            return false;
        }

        private void QueueInteract()
        {
            if (Time.time < _nextInteract)
                return;
            _nextInteract = Time.time + 0.5f;
            _interact = true;
        }

        private void Enter(Step step)
        {
            _step = step;
            _stepStart = Time.time;
        }

        private void Finish(bool success, string detail)
        {
            _step = Step.Done;
            GameLog.Info(LogTag, (success ? "RÉUSSI : " : "ÉCHEC : ") + detail);
        }

        private static VehicleMotor NearestFreeTaxi(Vector3 from)
        {
            VehicleMotor best = null;
            float bestSqr = float.MaxValue;
            var all = VehicleMotor.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || all[i].HasDriver)
                    continue;
                float sqr = (all[i].transform.position - from).sqrMagnitude;
                if (sqr < bestSqr) { best = all[i]; bestSqr = sqr; }
            }

            return best;
        }
    }
}
#endif
