#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Bots;
using IvoryState.Gameplay.Input;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Test de bout en bout de la conduite (<c>-e2eDrive</c>, builds de dev) : marcher jusqu'au taxi
    /// libre le plus proche, monter, accélérer 6 s, freiner, descendre. Écrit <c>[E2E-DRIVE] RÉUSSI</c>
    /// ou <c>ÉCHEC</c> avec la distance parcourue et la vitesse max.
    /// </summary>
    internal sealed class DriveScenarioBot : MonoBehaviour, IMovementInputProvider, IActionInputProvider
    {
        private const string LogTag = "E2E-DRIVE";
        private const float Timeout = 120f;

        private enum Step { WaitPlayer, WalkToTaxi, WaitSeat, Accelerate, Brake, WaitExit, Done }

        private readonly NavPathFollower _follower = new NavPathFollower();
        private Step _step;
        private float _stepStart;
        private float _startTime;
        private bool _interact;
        private float _nextInteract;
        private VehicleMotor _taxi;
        private Vector3 _driveStart;
        private float _maxSpeed;
        private float _exitedAt = -1f;

        public Vector2 Move { get; private set; }
        public bool Sprint => false;
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
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-e2eDrive") < 0)
                return;

            var go = new GameObject(nameof(DriveScenarioBot));
            DontDestroyOnLoad(go);
            go.AddComponent<DriveScenarioBot>();
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
                        if (_taxi == null)
                        {
                            Finish(false, "aucun taxi libre");
                            return;
                        }

                        _follower.SetDestination(player.transform.position, _taxi.transform.position);
                        GameLog.Info(LogTag, "Taxi visé à " + Mathf.RoundToInt(Vector3.Distance(player.transform.position, _taxi.transform.position)) + " m.");
                        Enter(Step.WalkToTaxi);
                    }
                    break;

                case Step.WalkToTaxi:
                    if (player.CurrentVehicle == _taxi)
                    {
                        QueueInteract();
                        Enter(Step.WaitSeat);
                    }
                    else
                    {
                        Move = _follower.GetDirection(player.transform.position);
                    }
                    break;

                case Step.WaitSeat:
                    if (player.Driving.IsDriving && player.Driving.Vehicle != null)
                    {
                        _driveStart = _taxi.transform.position;
                        GameLog.Info(LogTag, "Au volant.");
                        Enter(Step.Accelerate);
                    }
                    else if (Time.time - _stepStart > 3f)
                    {
                        QueueInteract(); // nouvelle tentative (requête perdue ou refusée)
                    }
                    break;

                case Step.Accelerate:
                    Move = new Vector2(0f, 1f);
                    _maxSpeed = Mathf.Max(_maxSpeed, _taxi.Speed);
                    if (Time.time - _stepStart > 6f)
                        Enter(Step.Brake);
                    break;

                case Step.Brake:
                    Move = new Vector2(0f, -1f);
                    if (Mathf.Abs(_taxi.Speed) < 0.3f || Time.time - _stepStart > 4f)
                    {
                        float distance = Vector3.Distance(_driveStart, _taxi.transform.position);
                        GameLog.Info(LogTag, "Parcouru " + distance.ToString("0.0") + " m, vitesse max " +
                                             (_maxSpeed * 3.6f).ToString("0") + " km/h.");
                        QueueInteract();
                        Enter(Step.WaitExit);
                    }
                    break;

                case Step.WaitExit:
                    if (!player.Driving.IsDriving)
                    {
                        // Laisser arriver le recalage serveur (position de sortie, à côté de la portière).
                        if (_exitedAt < 0f)
                            _exitedAt = Time.time;
                        if (Time.time - _exitedAt < 1.5f)
                            break;

                        float distance = Vector3.Distance(_driveStart, _taxi.transform.position);
                        float gap = Vector3.Distance(player.transform.position, _taxi.transform.position);
                        bool ok = distance > 15f && gap > 1f && gap < 5f && !_taxi.HasDriver;
                        Finish(ok, "taxi déplacé de " + distance.ToString("0.0") + " m, joueur descendu à " +
                                   gap.ToString("0.0") + " m du taxi, taxi libre : " + !_taxi.HasDriver);
                    }
                    else if (Time.time - _stepStart > 3f)
                    {
                        QueueInteract();
                        _stepStart = Time.time;
                    }
                    break;
            }
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
                if (sqr < bestSqr)
                {
                    best = all[i];
                    bestSqr = sqr;
                }
            }

            return best;
        }
    }
}
#endif
