using System;
using System.Globalization;
using FishNet.Managing;
using FishNet.Transporting;
using IvoryState.Core.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;

namespace IvoryState.Server.Diagnostics
{
    /// <summary>
    /// Écrit périodiquement une ligne d'état du serveur dans le log, facile à analyser par script :
    /// <code>[Stats] joueurs=4 objets=100 fps=30.0 frame_max_ms=41.2 ticks=30.0 entrant_ko_s=3.1 entrant_pkt_s=124 gc0=0 mem_mo=95</code>
    /// <list type="bullet">
    /// <item><b>fps / frame_max_ms</b> : le serveur headless tourne à 60 images/s (2 par tick,
    /// voir HeadlessFrameLimiter). Si fps baisse, le serveur est saturé.</item>
    /// <item><b>ticks</b> : ticks réseau simulés par seconde (doit rester à 30).</item>
    /// <item><b>entrant</b> : trafic reçu des clients (entrées), sans les en-têtes IP/UDP (+28 o/paquet).</item>
    /// <item><b>gc0</b> : collectes du ramasse-miettes depuis la dernière ligne (idéalement 0).</item>
    /// </list>
    /// Aucune allocation hors de l'écriture de la ligne, toutes les quelques secondes.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ServerStatsReporter : MonoBehaviour
    {
        private const string LogTag = "Stats";
        private const float Interval = 5f;

        private NetworkManager _networkManager;
        private Transport _transport;

        private float _windowStart;
        private int _frames;
        private float _maxFrame;
        private int _ticks;
        private long _bytesIn;
        private int _packetsIn;
        private int _gcAtWindowStart;

        public void Initialize(NetworkManager networkManager)
        {
            Shutdown();

            _networkManager = networkManager;
            _transport = networkManager.TransportManager.Transport;

            _transport.OnServerReceivedData += OnServerReceivedData;
            _networkManager.TimeManager.OnTick += OnTick;

            ResetWindow();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (_transport != null)
                _transport.OnServerReceivedData -= OnServerReceivedData;
            if (_networkManager != null)
                _networkManager.TimeManager.OnTick -= OnTick;

            _transport = null;
            _networkManager = null;
        }

        private void OnServerReceivedData(ServerReceivedDataArgs args)
        {
            _bytesIn += args.Data.Count;
            _packetsIn++;
        }

        private void OnTick()
        {
            _ticks++;
        }

        private void Update()
        {
            if (_networkManager == null)
                return;

            _frames++;
            float delta = Time.unscaledDeltaTime;
            if (delta > _maxFrame)
                _maxFrame = delta;

            float elapsed = Time.unscaledTime - _windowStart;
            if (elapsed < Interval)
                return;

            int players = _networkManager.ServerManager.Clients.Count;
            int objects = _networkManager.ServerManager.Objects.Spawned.Count;
            int gc = GC.CollectionCount(0) - _gcAtWindowStart;
            long memMb = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);

            CultureInfo inv = CultureInfo.InvariantCulture;
            GameLog.Info(LogTag,
                "joueurs=" + players +
                " objets=" + objects +
                " fps=" + (_frames / elapsed).ToString("0.0", inv) +
                " frame_max_ms=" + (_maxFrame * 1000f).ToString("0.0", inv) +
                " ticks=" + (_ticks / elapsed).ToString("0.0", inv) +
                " entrant_ko_s=" + (_bytesIn / 1024f / elapsed).ToString("0.0", inv) +
                " entrant_pkt_s=" + (_packetsIn / elapsed).ToString("0", inv) +
                " gc0=" + gc +
                " mem_mo=" + memMb);

            ResetWindow();
        }

        private void ResetWindow()
        {
            _windowStart = Time.unscaledTime;
            _frames = 0;
            _maxFrame = 0f;
            _ticks = 0;
            _bytesIn = 0;
            _packetsIn = 0;
            _gcAtWindowStart = GC.CollectionCount(0);
        }
    }
}
