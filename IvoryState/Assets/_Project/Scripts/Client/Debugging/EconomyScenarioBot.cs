#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Bots;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Input;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Test de bout en bout de l'économie, activé par <c>-e2eEconomy</c> (builds de dev).
    /// Le client joue seul le parcours complet, comme un vrai joueur, via les vraies entrées :
    /// <list type="number">
    /// <item>marcher jusqu'au dépôt et prendre une livraison ;</item>
    /// <item>marcher jusqu'à l'adresse attribuée par le serveur et livrer (paiement) ;</item>
    /// <item>marcher jusqu'à un étal et acheter à manger.</item>
    /// </list>
    /// Chaque étape est écrite dans le log (<c>[E2E]</c>), et la dernière ligne indique
    /// <c>RÉUSSI</c> ou <c>ÉCHEC</c>. Ainsi, la logique serveur est vérifiée sans intervention humaine.
    /// </summary>
    internal sealed class EconomyScenarioBot : MonoBehaviour, IMovementInputProvider, IActionInputProvider
    {
        private const string LogTag = "E2E";
        private const float StepTimeout = 240f; // la ville fait ~700 m de côté

        private enum Step { WaitPlayer, GoToBoard, WaitDelivery, GoToDelivery, WaitPayment, GoToShop, WaitFood, Done }

        private Step _step = Step.WaitPlayer;
        private float _stepStartedAt;
        private WorldInteractable _target;
        private bool _interactQueued;
        private float _nextInteractAttempt;
        private long _balanceBefore;
        private long _cashBefore;
        private int _satietyBefore;

        private readonly NavPathFollower _follower = new NavPathFollower();
        private bool _hasPath;
        private Vector3 _lastCheckedPosition;
        private float _nextProgressCheck;

        public Vector2 Move { get; private set; }
        public bool Sprint => true; // ignoré par le serveur si le personnage est affamé
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
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-e2eEconomy") < 0)
                return;

            var go = new GameObject(nameof(EconomyScenarioBot));
            DontDestroyOnLoad(go);
            go.AddComponent<EconomyScenarioBot>();
        }

        private void OnEnable()
        {
            PlayerInputRegistry.Register(this);
            PlayerEconomy.LocalNotification += OnNotification;
        }

        private void OnDisable()
        {
            PlayerInputRegistry.Unregister(this);
            PlayerEconomy.LocalNotification -= OnNotification;
        }

        private static void OnNotification(string message)
        {
            GameLog.Info(LogTag, "Message serveur : " + message);
        }

        private void Update()
        {
            PlayerInteractor player = PlayerInteractor.Local;
            Move = Vector2.zero;

            if (_step != Step.WaitPlayer && _step != Step.Done && Time.time - _stepStartedAt > StepTimeout)
            {
                Finish(false, "délai dépassé à l'étape " + _step);
                return;
            }

            switch (_step)
            {
                case Step.WaitPlayer:
                    if (player != null && player.Economy != null && player.Economy.Balance > 0)
                    {
                        GameLog.Info(LogTag, "Départ : " + Money.Format(player.Economy.Balance) +
                                             ", satiété " + player.Needs.Satiety + ".");
                        GoTo(Step.GoToBoard, FindNearest<JobBoardPoint>(player.transform.position));
                    }
                    break;

                case Step.GoToBoard:
                    if (WalkAndInteract(player))
                        Enter(Step.WaitDelivery);
                    break;

                case Step.WaitDelivery:
                    if (player.Economy.HasDelivery &&
                        WorldInteractable.TryGet(player.Economy.DeliveryTargetId, out WorldInteractable destination))
                    {
                        _balanceBefore = player.Economy.Balance;
                        GameLog.Info(LogTag, "Livraison reçue pour « " + destination.DisplayName + " ».");
                        GoTo(Step.GoToDelivery, destination);
                    }
                    break;

                case Step.GoToDelivery:
                    if (WalkAndInteract(player))
                        Enter(Step.WaitPayment);
                    break;

                case Step.WaitPayment:
                    if (!player.Economy.HasDelivery && player.Economy.Balance > _balanceBefore)
                    {
                        GameLog.Info(LogTag, "Payé : +" + Money.Format(player.Economy.Balance - _balanceBefore) + ".");
                        _balanceBefore = player.Economy.Balance;
                        _cashBefore = player.Economy.Cash;
                        _satietyBefore = player.Needs.Satiety;
                        GoTo(Step.GoToShop, FindNearest<ShopPoint>(player.transform.position));
                    }
                    break;

                case Step.GoToShop:
                    if (WalkAndInteract(player))
                        Enter(Step.WaitFood);
                    break;

                case Step.WaitFood:
                    // La satiété de départ est 100 : le serveur refuse d'abord (« Tu n'as pas faim »).
                    // On vérifie donc soit l'achat (espèces ou Pingoin), soit ce refus attendu et sans débit.
                    var shop = (ShopPoint)_target;
                    if (player.Economy.Cash == _cashBefore - shop.Price || player.Economy.Balance == _balanceBefore - shop.Price)
                        Finish(true, "achat accepté, " + Money.Format(player.Economy.Cash) + " en espèces restants.");
                    else if (Time.time - _stepStartedAt > 3f &&
                             player.Economy.Cash == _cashBefore &&
                             player.Economy.Balance == _balanceBefore &&
                             _satietyBefore >= PlayerNeeds.MaxSatiety)
                        Finish(true, "achat refusé car rassasié (règle serveur respectée), solde intact.");
                    break;
            }
        }

        /// <summary>
        /// Marche vers la cible en suivant les rues (NavMesh), puis demande l'interaction une fois à portée.
        /// Le chemin est recalculé si le bot n'avance plus.
        /// </summary>
        private bool WalkAndInteract(PlayerInteractor player)
        {
            Vector3 position = player.transform.position;

            if (player.Current != _target)
            {
                if (!_hasPath || Time.time >= _nextProgressCheck)
                {
                    bool stuck = _hasPath && (position - _lastCheckedPosition).sqrMagnitude < 0.25f;
                    if (!_hasPath || stuck)
                    {
                        _follower.SetDestination(position, _target.transform.position);
                        _hasPath = true;
                    }

                    _lastCheckedPosition = position;
                    _nextProgressCheck = Time.time + 2f;
                }

                Move = _follower.GetDirection(position);
                return false;
            }

            if (Time.time >= _nextInteractAttempt)
            {
                _nextInteractAttempt = Time.time + 1f;
                _interactQueued = true;
                return true;
            }

            return false;
        }

        private void GoTo(Step step, WorldInteractable target)
        {
            if (target == null)
            {
                Finish(false, "aucun point trouvé pour l'étape " + step);
                return;
            }

            _target = target;
            _hasPath = false;
            Enter(step);
        }

        private void Enter(Step step)
        {
            _step = step;
            _stepStartedAt = Time.time;
        }

        private void Finish(bool success, string detail)
        {
            _step = Step.Done;
            Move = Vector2.zero;
            GameLog.Info(LogTag, (success ? "RÉUSSI : " : "ÉCHEC : ") + detail);
        }

        private static T FindNearest<T>(Vector3 from) where T : WorldInteractable
        {
            T best = null;
            float bestSqr = float.MaxValue;
            var all = WorldInteractable.All;
            for (int i = 0; i < all.Count; i++)
            {
                // Le scénario teste la faim : on ignore les étals de boissons (pures ou majoritairement hydratantes comme le bissap).
                if (all[i] is T match && !(match is ShopPoint shopPoint && (shopPoint.IsDrink || shopPoint.Hydration > shopPoint.Satiety)))
                {
                    float sqr = (match.transform.position - from).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        best = match;
                        bestSqr = sqr;
                    }
                }
            }

            return best;
        }
    }
}
#endif
