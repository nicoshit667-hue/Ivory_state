#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Bots;
using IvoryState.Gameplay.Input;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Test de bout en bout des commerces de rue, activé par <c>-e2eShops</c> (builds de dev) : depuis le point
    /// d'apparition, le bot marche jusqu'au kiosque (StorePoint), à l'étal (ShopPoint) puis au commerce
    /// (CommercePoint) les plus proches, comme un joueur (vraies entrées), et achète un article « pour plus
    /// tard ». Chaque étape et chaque réponse du serveur sont écrites dans le log (<c>[E2E]</c>).
    /// </summary>
    internal sealed class ShopScenarioBot : MonoBehaviour, IMovementInputProvider, IActionInputProvider
    {
        private const string LogTag = "E2E";
        private const float StepTimeout = 150f;

        private readonly Queue<Type> _kinds = new Queue<Type>(new[] { typeof(StorePoint), typeof(ShopPoint), typeof(CommercePoint) });
        private readonly NavPathFollower _follower = new NavPathFollower();
        private WorldInteractable _target;
        private float _stepStartedAt;
        private bool _started, _interactQueued, _asked;
        private float _askedAt;
        private int _passed, _failed;
        private Vector3 _lastCheckedPosition;
        private float _nextProgressCheck;
        private static string s_lastMessage;

        public Vector2 Move { get; private set; }
        public bool Sprint => true;
        public float ViewYaw => 0f;
        public bool ConsumeJump() => false;

        public bool ConsumeInteract()
        {
            bool interact = _interactQueued;
            _interactQueued = false;
            return interact;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-e2eShops") < 0)
                return;
            var go = new GameObject(nameof(ShopScenarioBot));
            DontDestroyOnLoad(go);
            go.AddComponent<ShopScenarioBot>();
        }

        private void OnEnable()
        {
            PlayerInputRegistry.Register(this);
            PlayerEconomy.LocalNotification += OnNotification;
            PlayerInteractor.LocalMenuRequested += OnMenu;
        }

        private void OnDisable()
        {
            PlayerInputRegistry.Unregister(this);
            PlayerEconomy.LocalNotification -= OnNotification;
            PlayerInteractor.LocalMenuRequested -= OnMenu;
        }

        private static void OnNotification(string message)
        {
            s_lastMessage = message;
            GameLog.Info(LogTag, "Message serveur : " + message);
        }

        private void OnMenu(MenuInteractable menu)
        {
            GameLog.Info(LogTag, "Menu ouvert : " + menu.DisplayName + " (" + menu.OptionCount + " choix).");
        }

        private void Update()
        {
            PlayerInteractor player = PlayerInteractor.Local;
            Move = Vector2.zero;
            if (player == null || player.Economy == null || player.Economy.Balance <= 0)
                return;
            if (!_started)
            {
                _started = true;
                Next(player);
                return;
            }
            if (_target == null)
                return;

            if (Time.time - _stepStartedAt > StepTimeout)
            {
                Fail("délai dépassé vers « " + _target.DisplayName + " » (à " +
                     Vector3.Distance(player.transform.position, _target.transform.position).ToString("F1") + " m, point visé : " +
                     (player.Current != null ? player.Current.DisplayName : "aucun") + ")", player);
                return;
            }

            if (_asked)
            {
                if (s_lastMessage != null)
                {
                    GameLog.Info(LogTag, "OK : " + _target.GetType().Name + " « " + _target.DisplayName + " » a répondu.");
                    _passed++;
                    Next(player);
                }
                else if (Time.time - _askedAt > 6f)
                {
                    Fail("aucune réponse du serveur pour « " + _target.DisplayName + " »", player);
                }
                return;
            }

            Vector3 position = player.transform.position;
            if (player.Current != _target)
            {
                if (Time.time >= _nextProgressCheck)
                {
                    if ((position - _lastCheckedPosition).sqrMagnitude < 0.25f)
                        _follower.SetDestination(position, _target.transform.position);
                    _lastCheckedPosition = position;
                    _nextProgressCheck = Time.time + 2f;
                }
                Move = _follower.GetDirection(position);
                return;
            }

            s_lastMessage = null;
            _asked = true;
            _askedAt = Time.time;
            if (_target is MenuInteractable menu)
            {
                _interactQueued = true; // ouvre le menu, comme le joueur
                player.RequestChoice(menu, 0, true);
            }
            else
            {
                _interactQueued = true;
            }
            GameLog.Info(LogTag, "À portée de « " + _target.DisplayName + " » : achat demandé.");
        }

        private void Fail(string detail, PlayerInteractor player)
        {
            GameLog.Info(LogTag, "ÉCHEC : " + detail);
            _failed++;
            Next(player);
        }

        private void Next(PlayerInteractor player)
        {
            _target = null;
            _asked = false;
            while (_kinds.Count > 0 && _target == null)
            {
                Type kind = _kinds.Dequeue();
                _target = FindNearest(kind, player.transform.position);
                if (_target == null)
                    GameLog.Info(LogTag, "Aucun " + kind.Name + " dans la scène.");
            }
            if (_target == null)
            {
                GameLog.Info(LogTag, (_failed == 0 ? "RÉUSSI" : "ÉCHEC") + " : " + _passed + " commerce(s) OK, " + _failed + " en échec.");
                enabled = false;
                return;
            }
            GameLog.Info(LogTag, "Vers " + _target.GetType().Name + " « " + _target.DisplayName + " » (" +
                                 Vector3.Distance(player.transform.position, _target.transform.position).ToString("F0") + " m).");
            _stepStartedAt = Time.time;
            _follower.SetDestination(player.transform.position, _target.transform.position);
            _lastCheckedPosition = player.transform.position;
            _nextProgressCheck = Time.time + 2f;
        }

        private static WorldInteractable FindNearest(Type kind, Vector3 from)
        {
            WorldInteractable best = null;
            float bestSqr = float.MaxValue;
            var all = WorldInteractable.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].GetType() != kind)
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
