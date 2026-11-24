#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Bots;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Input;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Test de bout en bout des transferts entre joueurs (builds de dev).
    /// <list type="bullet">
    /// <item><c>-e2eTransfer</c> : écrit les messages du serveur dans le log (<c>[E2E-TRANSFER] …</c>).</item>
    /// <item><c>-e2eTransferSend</c> : en plus, rejoint le joueur réel le plus proche et lui envoie 1 000 FCFA.</item>
    /// </list>
    /// </summary>
    internal sealed class TransferScenarioBot : MonoBehaviour, IMovementInputProvider
    {
        private const string LogTag = "E2E-TRANSFER";
        private const long Amount = 1000;

        private readonly NavPathFollower _follower = new NavPathFollower();
        private bool _send;
        private bool _sent;
        private float _startAt = -1f;
        private float _nextRepath;

        public Vector2 Move { get; private set; }
        public bool Sprint => false;
        public float ViewYaw => 0f;
        public bool ConsumeJump() => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool send = Array.IndexOf(args, "-e2eTransferSend") >= 0;
            if (!send && Array.IndexOf(args, "-e2eTransfer") < 0)
                return;

            var go = new GameObject(nameof(TransferScenarioBot));
            DontDestroyOnLoad(go);
            go.AddComponent<TransferScenarioBot>()._send = send;
        }

        private void OnEnable()
        {
            PlayerEconomy.LocalNotification += OnNotification;
            if (_send)
                PlayerInputRegistry.Register(this);
        }

        private void OnDisable()
        {
            PlayerEconomy.LocalNotification -= OnNotification;
            PlayerInputRegistry.Unregister(this);
        }

        private static void OnNotification(string message)
        {
            GameLog.Info(LogTag, message);
        }

        private void Start()
        {
            // L'émetteur s'enregistre aussi après les contrôles de la scène.
            if (_send)
                PlayerInputRegistry.Register(this);
        }

        private void Update()
        {
            Move = Vector2.zero;
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (!_send || _sent || local == null || PlayerTransfers.Local == null)
                return;

            if (_startAt < 0f)
                _startAt = Time.time + 6f; // laisser le destinataire apparaître
            if (Time.time < _startAt)
                return;

            PlayerNetworkIdentity target = NearestRealPlayer(local);
            if (target == null)
                return;

            Vector3 offset = target.transform.position - local.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > 2.5f * 2.5f)
            {
                if (Time.time >= _nextRepath)
                {
                    _follower.SetDestination(local.transform.position, target.transform.position);
                    _nextRepath = Time.time + 1f;
                }

                Move = _follower.GetDirection(local.transform.position);
                return;
            }

            PlayerTransfers.Local.RequestSend(target, Amount);
            GameLog.Info(LogTag, "envoi de " + Amount + " à " + target.DisplayName);
            _sent = true;
        }

        private static PlayerNetworkIdentity NearestRealPlayer(PlayerNetworkIdentity local)
        {
            PlayerNetworkIdentity best = null;
            float bestSqr = float.MaxValue;
            var players = PlayerNetworkIdentity.ClientInstances;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerNetworkIdentity other = players[i];
                if (other == null || other == local || !other.Owner.IsValid)
                    continue;
                float sqr = (other.transform.position - local.transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = other;
                    bestSqr = sqr;
                }
            }

            return best;
        }
    }
}
#endif
