using IvoryState.Core.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Panneau de diagnostic réseau et de connexion (développement).
    /// <para>
    /// <b>Anti-chevauchement :</b> en jeu (connecté), le panneau complet est masqué par défaut afin de
    /// laisser le coin supérieur gauche 100 % libre pour les compteurs économiques (<c>ECO</c>,
    /// <c>PINGOIN</c>, <c>ESPECE</c>). Une pastille discrète placée <b>sous</b> le bloc d'argent
    /// (ou la touche <c>F3</c> sur PC) permet d'ouvrir/refermer les détails réseau sans jamais
    /// chevaucher l'interface du joueur.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevConnectionPanel : MonoBehaviour
    {
        public static DevConnectionPanel Instance { get; private set; }

        public static void ToggleExpanded()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Instance != null)
                Instance._expanded = !Instance._expanded;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const float ReferenceHeight = 720f;
        private const float StatusRefreshInterval = 0.5f;
        private const string AddressKey = "ivory.serverAddress";

        // Position verticale sous les 3 lignes d'économie (ECO / PINGOIN / ESPECE) en coordonnées 720p.
        private const float BelowEconomyY = 136f;

        private string _address;
        private string _status = string.Empty;
        private string _compactBadge = "DEV";
        private float _nextStatusRefresh;
        private NetworkBootstrap _bootstrap;
        private Input.MobilePlayerControls _controls;
        private int _framesInWindow;
        private float _fps;
        private bool _expanded;

        private void Start()
        {
            Instance = this;
            _bootstrap = NetworkBootstrap.Instance;
            _address = PlayerPrefs.GetString(AddressKey, _bootstrap != null ? _bootstrap.Config.DefaultAddress : "127.0.0.1");

#if UNITY_SERVER
            enabled = false;
#endif
        }

        private void Update()
        {
            _framesInWindow++;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame)
                _expanded = !_expanded;

            if (_bootstrap == null || Time.unscaledTime < _nextStatusRefresh)
                return;

            float elapsed = Time.unscaledTime - (_nextStatusRefresh - StatusRefreshInterval);
            _fps = elapsed > 0f ? _framesInWindow / elapsed : 0f;
            _framesInWindow = 0;

            _nextStatusRefresh = Time.unscaledTime + StatusRefreshInterval;
            _status = BuildStatus();
        }

        private string BuildStatus()
        {
            var nm = _bootstrap.NetworkManager;
            int fpsInt = Mathf.RoundToInt(_fps);
            long ping = _bootstrap.IsClientRunning && nm != null ? nm.TimeManager.RoundTripTime : 0;
            _compactBadge = _bootstrap.IsClientRunning
                ? "⚙ Dev • " + fpsInt + " FPS • " + ping + " ms"
                : "⚙ Dev • " + fpsInt + " FPS";

            string status = "Mode : " + _bootstrap.CurrentMode + "   " + fpsInt + " img/s";

            if (_bootstrap.IsServerRunning && nm != null)
                status += "\nServeur : " + nm.ServerManager.Clients.Count + " client(s)";

            if (_bootstrap.IsClientRunning && nm != null)
                status += "\nClient : connecté — ping " + ping + " ms";

            if (_controls == null)
                _controls = FindAnyObjectByType<Input.MobilePlayerControls>();
            if (_controls != null)
                status += "\nDoigts : " + _controls.TouchCount + "  joy " + _controls.MoveFingerId +
                          "  cam " + _controls.LookFingerId;

            return status;
        }

        private void OnGUI()
        {
            if (_bootstrap == null)
                return;

            // Masque la pastille et le panneau de dev sur les écrans d'accueil, de connexion et de création
            // (sauf si le développeur l'ouvre explicitement avec F3).
            if (UI.UIManager.Instance != null && UI.UIManager.Instance.IsMenuOpen && !_expanded)
                return;

            float scale = Mathf.Max(1f, Screen.height / ReferenceHeight);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            bool running = _bootstrap.IsServerRunning || _bootstrap.IsClientRunning;

            if (!running)
            {
                // Hors connexion : panneau placé sous la zone d'économie pour ne rien masquer.
                GUILayout.BeginArea(new Rect(16f, BelowEconomyY, 270f, 230f), GUI.skin.box);
                GUILayout.Label("Ivory State — Connexion (dev)");
                GUILayout.Label("Serveur (hôte ou hôte:port) :");
                _address = GUILayout.TextField(_address);

                if (GUILayout.Button("Client (Rejoindre)", GUILayout.Height(34f)))
                {
                    PlayerPrefs.SetString(AddressKey, _address.Trim());
                    PlayerPrefs.Save();
                    _bootstrap.StartClient(_address);
                }
                if (NetworkBootstrap.ServerModuleAvailable)
                {
                    if (GUILayout.Button("Hôte (serveur + client)", GUILayout.Height(30f)))
                        _bootstrap.StartHost();
                    if (GUILayout.Button("Serveur seul", GUILayout.Height(28f)))
                        _bootstrap.StartServer();
                }
                GUILayout.EndArea();
                return;
            }

            // En jeu : pastille compacte repliée par défaut, positionnée sous les compteurs FCFA.
            if (!_expanded)
            {
                if (GUI.Button(new Rect(18f, BelowEconomyY, 175f, 24f), _compactBadge))
                    _expanded = true;
                return;
            }

            GUILayout.BeginArea(new Rect(18f, BelowEconomyY, 250f, 155f), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Réseau (dev)");
            if (GUILayout.Button("Masquer", GUILayout.Width(72f), GUILayout.Height(22f)))
                _expanded = false;
            GUILayout.EndHorizontal();

            GUILayout.Label(_status);

            if (GUILayout.Button("Déconnecter", GUILayout.Height(28f)))
                _bootstrap.StopAll();

            GUILayout.EndArea();
        }
#else
        private void Awake()
        {
            enabled = false;
        }
#endif
    }
}
