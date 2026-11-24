using System;
using FishNet.Managing;
using FishNet.Transporting;
using IvoryState.Core.Diagnostics;
using UnityEngine;

namespace IvoryState.Core.Networking
{
    /// <summary>
    /// Démarre l'instance dans le bon rôle réseau (serveur, client ou hôte) et applique
    /// les réglages propres à chaque plateforme.
    /// <para>
    /// Ordre de résolution du rôle :
    /// <list type="number">
    /// <item>Build Dedicated Server (<c>UNITY_SERVER</c>) : toujours serveur.</item>
    /// <item>Argument de ligne de commande (<c>-server</c>, <c>-client</c>, <c>-host</c>).</item>
    /// <item>Éditeur : <see cref="NetworkConfig.EditorLaunchMode"/>.</item>
    /// <item>Mobile en production : client automatique.</item>
    /// <item>Sinon : aucun, le panneau de dev prend le relais.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Le code serveur vit dans l'assembly <c>IvoryState.Server</c>, absente des builds clients.
    /// Elle s'abonne à <see cref="ServerStarted"/> au chargement : Core ne la référence jamais,
    /// ce qui garantit qu'aucune logique serveur (économie, validation) n'est livrée aux joueurs.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        private const string LogTag = "Network";

        [SerializeField] private NetworkConfig _config;

        private NetworkManager _networkManager;
        private LaunchArguments _args;
        private bool _subscribed;

        /// <summary>Levé quand le serveur est prêt (aussi en mode hôte).</summary>
        public static event Action<NetworkBootstrap> ServerStarted;

        /// <summary>Levé quand le serveur s'arrête.</summary>
        public static event Action<NetworkBootstrap> ServerStopped;

        /// <summary>Vrai si l'assembly serveur est compilée dans ce build.</summary>
        public static bool ServerModuleAvailable { get; private set; }

        public static NetworkBootstrap Instance { get; private set; }

        public NetworkManager NetworkManager => _networkManager;
        public NetworkConfig Config => _config;
        public LaunchMode CurrentMode { get; private set; }

        /// <summary>Arguments de ligne de commande, lus une fois dans Awake.</summary>
        public LaunchArguments Arguments => _args;

        /// <summary>Adresse du serveur de jeu à laquelle le client s'est connecté (Nakama est sur la même machine).</summary>
        public string ConnectedAddress { get; private set; }

        public bool IsServerRunning => _networkManager != null && _networkManager.ServerManager.Started;
        public bool IsClientRunning => _networkManager != null && _networkManager.ClientManager.Started;

        /// <summary>Appelé par l'assembly serveur au chargement. Ne pas appeler ailleurs.</summary>
        public static void RegisterServerModule()
        {
            ServerModuleAvailable = true;
        }

        // Remise à zéro des statiques : indispensable si "Enter Play Mode Options" désactive
        // le rechargement de domaine (Play Mode instantané dans l'éditeur).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ServerStarted = null;
            ServerStopped = null;
            ServerModuleAvailable = false;
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // FishNet détruit lui-même le NetworkManager en double (paramètre Persistence).
                enabled = false;
                return;
            }

            Instance = this;
            _networkManager = GetComponent<NetworkManager>();
            _args = LaunchArguments.FromEnvironment();

            if (_config == null)
                GameLog.Error(LogTag, "Aucune NetworkConfig assignée sur NetworkBootstrap.");
        }

        // Start (et non Awake) : les sous-managers de FishNet sont initialisés dans leur propre Awake.
        private void Start()
        {
            if (Instance != this || _config == null)
                return;

            Subscribe();

            CurrentMode = ResolveLaunchMode();
            ApplyPlatformSettings(CurrentMode);

            switch (CurrentMode)
            {
                case LaunchMode.Server: StartServer(); break;
                case LaunchMode.Client: StartClient(); break;
                case LaunchMode.Host: StartHost(); break;
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this)
                Instance = null;
        }

        #region API publique

        public bool StartServer()
        {
            if (!ServerModuleAvailable)
            {
                GameLog.Error(LogTag, "Ce build ne contient pas le module serveur (build client). Démarrage serveur refusé.");
                return false;
            }

            if (IsServerRunning)
                return true;

            Transport transport = _networkManager.TransportManager.Transport;
            transport.SetPort(ResolvePort());
            transport.SetMaximumClients(_args.MaxPlayers > 0 ? _args.MaxPlayers : _config.MaxPlayers);
            _networkManager.TimeManager.SetTickRate(_config.TickRate);
            NetworkTuning.ApplyServer(_networkManager);

            CurrentMode = LaunchMode.Server;
            return _networkManager.ServerManager.StartConnection();
        }

        public bool StartClient(string address = null)
        {
            if (IsClientRunning)
                return true;

            string target = !string.IsNullOrWhiteSpace(address) ? address
                : !string.IsNullOrWhiteSpace(_args.Address) ? _args.Address
                : _config.DefaultAddress;

            if (CurrentMode != LaunchMode.Host)
                CurrentMode = LaunchMode.Client;

            target = target.Trim();
            ConnectedAddress = target == "localhost" ? "127.0.0.1" : target;
            _wantsClient = CurrentMode == LaunchMode.Client; // pas de reconnexion automatique en mode hôte

            // « hôte:port » (tunnel playit.gg, port public différent de 7770) ou « hôte » seul.
            SplitAddress(target, ResolvePort(), out string host, out ushort port);
            if (host == "localhost")
                host = "127.0.0.1";

            NetworkTuning.ApplyClient(_networkManager);
            GameLog.Info(LogTag, "Connexion à " + host + ":" + port);
            return _networkManager.ClientManager.StartConnection(host, port);
        }

        /// <summary>Sépare « hôte:port » ; sans port valide, <paramref name="defaultPort"/> s'applique.</summary>
        public static void SplitAddress(string address, ushort defaultPort, out string host, out ushort port)
        {
            host = address;
            port = defaultPort;
            int colon = address.LastIndexOf(':');
            // Une seule occurrence de « : » : ce n'est pas une adresse IPv6.
            if (colon > 0 && colon == address.IndexOf(':') &&
                ushort.TryParse(address.Substring(colon + 1), out ushort parsed) && parsed != 0)
            {
                host = address.Substring(0, colon);
                port = parsed;
            }
        }

        /// <summary>Serveur + client local. Pratique pour tester seul, jamais utilisé en production.</summary>
        public bool StartHost()
        {
            if (!StartServer())
                return false;

            CurrentMode = LaunchMode.Host;
            return StartClient("localhost");
        }

        public void StopAll()
        {
            // Arrêt volontaire : pas de reconnexion automatique.
            _wantsClient = false;
            _reconnectAt = -1f;

            if (IsClientRunning)
                _networkManager.ClientManager.StopConnection();
            if (IsServerRunning)
                _networkManager.ServerManager.StopConnection(true);

            CurrentMode = LaunchMode.None;
        }

        #endregion

        private LaunchMode ResolveLaunchMode()
        {
#if UNITY_SERVER
            return LaunchMode.Server;
#else
            if (_args.Mode != LaunchMode.None)
                return _args.Mode;
#if UNITY_EDITOR
            return _config.EditorLaunchMode;
#elif (UNITY_ANDROID || UNITY_IOS) && !DEVELOPMENT_BUILD
            return LaunchMode.Client;
#else
            return LaunchMode.None;
#endif
#endif
        }

        private ushort ResolvePort()
        {
            return _args.Port != 0 ? _args.Port : _config.Port;
        }

        private void ApplyPlatformSettings(LaunchMode mode)
        {
            Application.runInBackground = true;

            // targetFrameRate est ignoré en batchmode : limiteur manuel. Serveur : 2 frames par tick,
            // pour limiter l'irrégularité des ticks. Client bot : 1 frame par tick suffit.
            if (Application.isBatchMode)
            {
                int fps = mode == LaunchMode.Server ? _config.TickRate * 2 : _config.TickRate;
                gameObject.AddComponent<HeadlessFrameLimiter>().SetTargetFrameRate(fps);
            }

#if !UNITY_EDITOR
            QualitySettings.vSyncCount = 0;

            if (mode == LaunchMode.Server)
            {
                // Un serveur headless n'a rien à afficher : on cale la boucle sur le tick
                // réseau pour ne pas brûler du CPU inutilement.
                Application.targetFrameRate = _config.TickRate;
                AudioListener.pause = true;
            }
            else
            {
                // Un client bot headless n'a pas besoin de plus d'une frame par tick : plus de bots
                // tiennent sur la même machine lors des tests de charge.
                // Téléphone : 30 img/s stables plutôt que 60 visés et jamais tenus (chauffe, puis
                // ralentissement du processeur graphique). Les bots restent à une image par tick.
                int target = Application.isMobilePlatform ? Mathf.Min(30, _config.ClientTargetFrameRate) : _config.ClientTargetFrameRate;
                Application.targetFrameRate = _args.Bot ? _config.TickRate : target;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
#endif
        }

        #region Événements FishNet

        private void Subscribe()
        {
            if (_subscribed)
                return;

            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _networkManager == null)
                return;

            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            _subscribed = false;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    GameLog.Info(LogTag, "Serveur démarré sur le port " + ResolvePort() + ".");
                    ServerStarted?.Invoke(this);
                    break;

                case LocalConnectionState.Stopped:
                    GameLog.Info(LogTag, "Serveur arrêté.");
                    ServerStopped?.Invoke(this);
                    break;
            }
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    GameLog.Info(LogTag, "Client connecté.");
                    _reconnectAttempts = 0;
                    break;

                case LocalConnectionState.Stopped:
                    GameLog.Info(LogTag, "Client déconnecté.");
                    ScheduleReconnect();
                    break;
            }
        }

        #region Reconnexion automatique

        // Délais entre tentatives (s) : réseau mobile instable, serveur redémarré, premier lancement
        // ralenti par l'antivirus… Au-delà, on continue toutes les 30 s.
        private static readonly float[] ReconnectDelays = { 2f, 4f, 8f, 16f, 30f };

        private bool _wantsClient;
        private int _reconnectAttempts;
        private float _reconnectAt = -1f;

        /// <summary>Vrai pendant l'attente d'une nouvelle tentative de connexion (affichable par l'UI).</summary>
        public bool IsReconnecting => _reconnectAt >= 0f;

        private void ScheduleReconnect()
        {
            if (!_wantsClient)
                return;

            float delay = ReconnectDelays[Mathf.Min(_reconnectAttempts, ReconnectDelays.Length - 1)];
            _reconnectAttempts++;
            _reconnectAt = Time.unscaledTime + delay;
            GameLog.Warn(LogTag, "Connexion perdue : nouvelle tentative dans " + delay + " s (essai " + _reconnectAttempts + ").");
        }

        private void Update()
        {
            if (_reconnectAt < 0f || Time.unscaledTime < _reconnectAt)
                return;

            _reconnectAt = -1f;
            if (_wantsClient && !IsClientRunning)
                StartClient(ConnectedAddress);
        }

        #endregion

        #endregion
    }
}
