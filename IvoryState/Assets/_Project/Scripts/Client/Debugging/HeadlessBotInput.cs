#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Globalization;
using FishNet.Transporting;
using IvoryState.Core.Diagnostics;
using IvoryState.Core.Networking;
using IvoryState.Gameplay.Bots;
using IvoryState.Gameplay.Input;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Client automatique, activé par l'argument <c>-bot</c> (builds de dev uniquement).
    /// <list type="bullet">
    /// <item>Le personnage erre comme un bot serveur (<see cref="WanderBotBrain"/>),
    /// dans un rayon <c>-botSpread</c>.</item>
    /// <item>Toutes les 5 s, une ligne <c>[ClientStats]</c> donne le trafic <b>reçu</b> par ce client
    /// (la donnée clé sur mobile) et le nombre de joueurs qu'il voit.</item>
    /// </list>
    /// </summary>
    internal sealed class HeadlessBotInput : MonoBehaviour, IMovementInputProvider
    {
        private const string LogTag = "ClientStats";
        private const float StatsInterval = 5f;
        private const float CrowdRadius = 6f;

        private WanderBotBrain _brain;
        private Transport _transport;
        private float _windowStart;
        private long _bytesIn;
        private int _packetsIn;

        public Vector2 Move => _brain != null ? _brain.Move : Vector2.zero;
        public bool Sprint => _brain != null && _brain.Sprint;
        public float ViewYaw => 0f;
        public bool ConsumeJump() => _brain != null && _brain.ConsumeJump();

        // AfterSceneLoad : après l'OnEnable des contrôles tactiles de la scène, donc le bot
        // s'enregistre en dernier et remplace leur source d'entrées.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!LaunchArguments.FromEnvironment().Bot)
                return;

            var go = new GameObject(nameof(HeadlessBotInput));
            DontDestroyOnLoad(go);
            go.AddComponent<HeadlessBotInput>();
        }

        private void OnEnable()
        {
            PlayerInputRegistry.Register(this);
            PlayerNetworkIdentity.LocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        private void OnDisable()
        {
            PlayerInputRegistry.Unregister(this);
            PlayerNetworkIdentity.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            if (_transport != null)
                _transport.OnClientReceivedData -= OnClientReceivedData;
        }

        private void OnLocalPlayerSpawned(PlayerNetworkIdentity player)
        {
            NetworkBootstrap bootstrap = NetworkBootstrap.Instance;
            float spread = bootstrap != null ? bootstrap.Arguments.BotSpread : 0f;
            Vector3 center = bootstrap != null ? bootstrap.Config.SpawnCenter : Vector3.zero;

            _brain = new WanderBotBrain(player.transform, center, spread > 0f ? spread : CrowdRadius,
                Random.Range(int.MinValue, int.MaxValue)); // graine différente pour chaque processus

            if (_transport == null && bootstrap != null)
            {
                _transport = bootstrap.NetworkManager.TransportManager.Transport;
                _transport.OnClientReceivedData += OnClientReceivedData;
            }

            _windowStart = Time.unscaledTime;
            GameLog.Info(LogTag, "Mode bot actif (rayon " + (spread > 0f ? spread : CrowdRadius) + " m).");
        }

        private void OnClientReceivedData(ClientReceivedDataArgs args)
        {
            _bytesIn += args.Data.Count;
            _packetsIn++;
        }

        private void Update()
        {
            if (_brain == null)
                return;

            float elapsed = Time.unscaledTime - _windowStart;
            if (elapsed < StatsInterval)
                return;

            CultureInfo inv = CultureInfo.InvariantCulture;
            GameLog.Info(LogTag,
                "visibles=" + PlayerNetworkIdentity.ClientVisibleCount +
                " recu_ko_s=" + (_bytesIn / 1024f / elapsed).ToString("0.0", inv) +
                " recu_pkt_s=" + (_packetsIn / elapsed).ToString("0", inv) +
                " erreur_moy_cm=" + (PredictionDiagnostics.AverageError * 100f).ToString("0.0", inv) +
                " erreur_max_cm=" + (PredictionDiagnostics.MaxError * 100f).ToString("0.0", inv) +
                " corrections_visibles=" + PredictionDiagnostics.VisibleCorrections +
                " reconciles=" + PredictionDiagnostics.Samples);

            PredictionDiagnostics.Reset();
            _windowStart = Time.unscaledTime;
            _bytesIn = 0;
            _packetsIn = 0;
        }
    }
}
#endif
