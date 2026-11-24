using System;
using System.Collections;
using IvoryState.Core.Networking;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Contrôleur du flux d'accueil et d'authentification (Écrans 1 et 2) :
    /// <list type="bullet">
    /// <item><b>Écran 1 (<c>MainMenuPanel</c>)</b> : logo <c>IVORY STATE</c>, menu vertical empilé
    /// (<c>JOUER</c>, <c>SE CONNECTER</c>, <c>CRÉER UN COMPTE</c>, <c>PARAMÈTRES</c>), indicateur
    /// <c>RÉSEAU : CONNECTÉ</c> en haut à droite et pied de page <c>v1.2.0 (DEV)</c>.</item>
    /// <item><b>Écran 2 (<c>RegisterPanel</c> / <c>Login</c>)</b> : validation e-mail, mot de passe avec
    /// bouton afficher/masquer, confirmation de mot de passe, bannière d'erreur dynamique et transition
    /// vers l'Écran 3 (<c>CharacterCreationPanel</c>).</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AuthFlowController : MonoBehaviour
    {
        public static AuthFlowController Instance { get; private set; }

        private const string EmailKey = "ivory.account.email";
        private const string HashKey = "ivory.account.hash";
        private const int MinPasswordLength = 6;

        // Charte graphique Dark Glassmorphism + Ambre
        private static readonly Color GlassPanel = new Color(0.11f, 0.12f, 0.14f, 0.85f);
        private static readonly Color GlassCard = new Color(0.13f, 0.14f, 0.17f, 0.90f);
        private static readonly Color GlassButton = new Color(0.18f, 0.20f, 0.24f, 0.88f);
        private static readonly Color FieldBg = new Color(0.07f, 0.08f, 0.10f, 0.92f);
        private static readonly Color Amber = new Color(0.91f, 0.51f, 0.10f, 1f);
        private static readonly Color BorderSubtle = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color Emerald = new Color(0.22f, 0.85f, 0.45f, 1f);
        private static readonly Color ErrorBg = new Color(0.85f, 0.20f, 0.20f, 0.22f);
        private static readonly Color ErrorText = new Color(1f, 0.52f, 0.48f, 1f);

        private Font _font;
        private RectTransform _mainMenuPanel;
        private RectTransform _registerPanel;

        // Éléments Écran 1 (MainMenuPanel)
        private Text _networkBadgeText;
        private Image _networkDot;
        private Text _footerStatusText;

        // Éléments Écran 2 (RegisterPanel / Login)
        private Text _authCardTitle;
        private Text _authCardSubtitle;
        private InputField _emailInput;
        private InputField _passwordInput;
        private InputField _confirmPasswordInput;
        private RectTransform _confirmRow;
        private Text _passwordToggleLabel;
        private RectTransform _errorBanner;
        private Text _errorText;
        private Text _submitBtnLabel;
        private Text _switchModeLinkLabel;

        private bool _isRegisterMode = true;
        private bool _passwordVisible;
        private bool _waitingServer;
        private float _waitingSince;
        private bool _pendingCreationCommit;
        private bool _suppressNextSpawnMenu;
        private bool _reconnectingSession;
        private float _nextAutoConnectTime;
        private float _lastConnectStartTime;
        private int _connectAttemptCount;
        private string _stagedEmail;
        private string _stagedHash;
        private string _stagedPseudo;
        private Action<string> _creatorErrorCallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            PlayerAccount.LocalAuthResult += OnServerAuthResult;
        }

        private void OnDisable()
        {
            PlayerAccount.LocalAuthResult -= OnServerAuthResult;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Initialize(RectTransform root, Font font)
        {
            _font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            HudIconLibrary.EnsureInitialized();

            BuildMainMenuPanel(root);
            BuildRegisterPanel(root);

            UIManager.Instance?.RegisterPanel(UIScreenState.MainMenu, _mainMenuPanel);
            UIManager.Instance?.RegisterPanel(UIScreenState.Register, _registerPanel);
            UIManager.Instance?.RegisterPanel(UIScreenState.Login, _registerPanel);
        }

        public void OnLocalPlayerSpawned(PlayerNetworkIdentity player)
        {
            if (_suppressNextSpawnMenu)
            {
                _suppressNextSpawnMenu = false;
                return;
            }

            if (UIManager.Instance != null &&
                (UIManager.Instance.CurrentState == UIScreenState.Login ||
                 UIManager.Instance.CurrentState == UIScreenState.Register ||
                 UIManager.Instance.CurrentState == UIScreenState.CharacterCreation))
            {
                return;
            }

            UIManager.Instance?.TransitionTo(UIScreenState.MainMenu);
        }

        private void Update()
        {
            EnsureClientConnected();

            if (_mainMenuPanel != null && _mainMenuPanel.gameObject.activeSelf)
            {
                bool connected = NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.IsClientRunning;
                _networkDot.color = connected ? Emerald : Amber;
                _networkBadgeText.text = connected ? "RÉSEAU : CONNECTÉ" : "RÉSEAU : CONNEXION...";
                string savedEmail = PlayerPrefs.GetString(EmailKey, string.Empty);
                string addr = NetworkBootstrap.Instance != null && !string.IsNullOrEmpty(NetworkBootstrap.Instance.ConnectedAddress)
                    ? NetworkBootstrap.Instance.ConnectedAddress : "127.0.0.1";
                _footerStatusText.text = connected
                    ? (string.IsNullOrEmpty(savedEmail) ? "Connecté au serveur (" + addr + ") • Prêt" : "Session active : " + savedEmail)
                    : "Connexion automatique au serveur (" + addr + ")...";
            }

            // Sécurité anti-blocage : si le serveur a lié le compte (SyncVar IsLinked == true) pendant une attente,
            // on ferme immédiatement l'écran de connexion pour entrer en jeu.
            if (_waitingServer && !_reconnectingSession)
            {
                float elapsed = Time.unscaledTime - _waitingSince;
                if (PlayerAccount.Local != null && PlayerAccount.Local.IsLinked && elapsed > 0.35f)
                {
                    bool needsApp = PlayerNetworkIdentity.Local != null && !PlayerNetworkIdentity.Local.HasAppearance;
                    OnServerAuthResult(true, "Connecté.", needsApp);
                }
                else if (elapsed > 8f)
                {
                    _waitingServer = false;
                    ShowError("Délai d'attente dépassé, réessaie.", true);
                    _creatorErrorCallback?.Invoke("Délai d'attente dépassé, réessaie.");
                }
            }
        }

        /// <summary>
        /// Assure la connexion automatique du client au serveur dès l'ouverture du jeu
        /// (même lorsque <c>IvoryState.exe</c> est lancé par double-clic sans argument <c>-client</c>).
        /// Alterne automatiquement entre le serveur local (<c>127.0.0.1</c>) et l'adresse distante configurée
        /// si la première adresse ne répond pas sous 5 secondes.
        /// </summary>
        private void EnsureClientConnected()
        {
            if (AccountPanel.IsAutomatedClient() || _reconnectingSession)
                return;

            NetworkBootstrap bootstrap = NetworkBootstrap.Instance;
            if (bootstrap == null || bootstrap.IsServerRunning)
                return;

            // ClientManager.Started (IsClientRunning) vaut true uniquement une fois la connexion établie.
            if (bootstrap.IsClientRunning)
            {
                _connectAttemptCount = 0;
                return;
            }

            if (Time.unscaledTime < _nextAutoConnectTime)
                return;

            if (_connectAttemptCount > 0 && bootstrap.NetworkManager != null)
                bootstrap.NetworkManager.ClientManager.StopConnection();

            string cliAddr = bootstrap.Arguments.Address;
            string savedAddr = PlayerPrefs.GetString("ivory.serverAddress",
                bootstrap.Config != null && !string.IsNullOrWhiteSpace(bootstrap.Config.DefaultAddress)
                    ? bootstrap.Config.DefaultAddress : "127.0.0.1").Trim();

            string target;
            if (!string.IsNullOrWhiteSpace(cliAddr))
            {
                target = cliAddr;
            }
            else if (_connectAttemptCount % 2 == 0 || string.IsNullOrEmpty(savedAddr))
            {
                target = "127.0.0.1";
            }
            else
            {
                target = savedAddr;
            }

            _connectAttemptCount++;
            _lastConnectStartTime = Time.unscaledTime;
            _nextAutoConnectTime = Time.unscaledTime + 5.0f;
            bootstrap.StartClient(target);
        }

        #region Navigation, Connexion & Déconnexion

        /// <summary>
        /// Déconnecte le compte joueur actuel (appelé depuis le Téléphone ou le menu),
        /// efface la session mémorisée, réinitialise la connexion client pour libérer le profil sur le serveur
        /// et affiche l'écran de connexion.
        /// </summary>
        public void Logout()
        {
            PlayerPrefs.DeleteKey(EmailKey);
            PlayerPrefs.DeleteKey(HashKey);
            PlayerPrefs.Save();

            _stagedEmail = string.Empty;
            _stagedHash = string.Empty;
            _stagedPseudo = string.Empty;
            _waitingServer = false;
            _pendingCreationCommit = false;

            if (_emailInput != null) _emailInput.text = string.Empty;
            if (_passwordInput != null) _passwordInput.text = string.Empty;
            if (_confirmPasswordInput != null) _confirmPasswordInput.text = string.Empty;

            OpenAuthScreen(false);
            ShowError("Tu es déconnecté. Connecte-toi ou crée un nouveau compte.", false);

            if (NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.IsClientRunning)
            {
                StopAllCoroutines();
                StartCoroutine(RestartClientSessionCoroutine(null));
            }
        }

        private IEnumerator RestartClientSessionCoroutine(Action onReconnected)
        {
            _reconnectingSession = true;
            _suppressNextSpawnMenu = true;

            NetworkBootstrap bootstrap = NetworkBootstrap.Instance;
            if (bootstrap != null && bootstrap.NetworkManager != null)
            {
                string addr = !string.IsNullOrEmpty(bootstrap.ConnectedAddress) ? bootstrap.ConnectedAddress : "127.0.0.1";
                if (bootstrap.IsClientRunning)
                    bootstrap.NetworkManager.ClientManager.StopConnection();

                float timeout = Time.unscaledTime + 3f;
                while (bootstrap.IsClientRunning && Time.unscaledTime < timeout)
                    yield return null;

                yield return new WaitForSecondsRealtime(0.2f);

                if (!bootstrap.IsClientRunning)
                    bootstrap.StartClient(addr);

                timeout = Time.unscaledTime + 5f;
                while ((PlayerAccount.Local == null || PlayerAccount.Local.IsLinked) && Time.unscaledTime < timeout)
                    yield return null;
            }

            _reconnectingSession = false;
            onReconnected?.Invoke();
        }

        /// <summary>Bouton JOUER de la page d'accueil : reprend la session sauvegardée ou ouvre l'inscription.</summary>
        public void OnPlayClicked()
        {
            string savedEmail = PlayerPrefs.GetString(EmailKey, string.Empty);
            string savedHash = PlayerPrefs.GetString(HashKey, string.Empty);

            if (PlayerAccount.Local != null && PlayerAccount.Local.IsLinked && !string.IsNullOrEmpty(savedEmail))
            {
                UIManager.Instance?.TransitionTo(UIScreenState.InGame);
                return;
            }

            if (!string.IsNullOrEmpty(savedEmail) && savedHash.Length == 64 && PlayerAccount.Local != null)
            {
                OpenAuthScreen(false);
                _stagedEmail = savedEmail;
                _stagedHash = savedHash;
                _emailInput.text = savedEmail;
                _pendingCreationCommit = false;
                _waitingServer = true;
                _waitingSince = Time.unscaledTime;
                ShowError("Reconnexion automatique en cours…", false);
                PlayerAccount.Local.Login(savedEmail, savedHash);
                return;
            }

            OpenAuthScreen(true);
        }

        public void OpenAuthScreen(bool registerMode)
        {
            _isRegisterMode = registerMode;
            _waitingServer = false;
            _passwordVisible = false;

            _authCardTitle.text = registerMode ? "INSCRIPTION" : "CONNEXION";
            _authCardSubtitle.text = registerMode ? "Nouveau joueur — Crée ton profil citoyen" : "Bon retour dans la métropole d'Abidjan";
            _confirmRow.gameObject.SetActive(registerMode);
            _submitBtnLabel.text = registerMode ? "CRÉER MON COMPTE" : "SE CONNECTER";
            _switchModeLinkLabel.text = registerMode
                ? "Déjà un compte ? <color=#F59E0B>Se connecter</color>"
                : "Nouveau sur Ivory State ? <color=#F59E0B>Créer un compte</color>";

            _passwordInput.contentType = InputField.ContentType.Password;
            _confirmPasswordInput.contentType = InputField.ContentType.Password;
            _passwordInput.ForceLabelUpdate();
            _confirmPasswordInput.ForceLabelUpdate();
            _passwordToggleLabel.text = "AFFICHER";

            HideError();
            UIManager.Instance?.TransitionTo(registerMode ? UIScreenState.Register : UIScreenState.Login);
        }

        private void TogglePasswordVisibility()
        {
            _passwordVisible = !_passwordVisible;
            var mode = _passwordVisible ? InputField.ContentType.Standard : InputField.ContentType.Password;
            _passwordInput.contentType = mode;
            _confirmPasswordInput.contentType = mode;
            _passwordInput.ForceLabelUpdate();
            _confirmPasswordInput.ForceLabelUpdate();
            _passwordToggleLabel.text = _passwordVisible ? "MASQUER" : "AFFICHER";
        }

        private void OnSubmitAuthForm()
        {
            if (_waitingServer)
                return;

            string email = _emailInput.text != null ? _emailInput.text.Trim().ToLowerInvariant() : string.Empty;
            string password = _passwordInput.text ?? string.Empty;
            string confirm = _confirmPasswordInput.text ?? string.Empty;

            if (!IsValidEmail(email))
            {
                ShowError("Veuillez entrer une adresse e-mail valide (ex: joueur@ivory.ci).", true);
                return;
            }

            if (password.Length < MinPasswordLength)
            {
                ShowError("Le mot de passe doit contenir au moins " + MinPasswordLength + " caractères.", true);
                return;
            }

            if (_isRegisterMode && password != confirm)
            {
                ShowError("Les deux mots de passe ne correspondent pas.", true);
                return;
            }

            _stagedEmail = email;
            _stagedHash = PlayerAccount.HashPassword(email, password);
            HideError();

            if (_isRegisterMode)
            {
                // Dirige le nouveau joueur vers l'Écran 3 (Choix de pseudo & Création d'apparence 3D).
                WardrobeShopManager.Instance?.PrepareCharacterCreation();
                UIManager.Instance?.TransitionTo(UIScreenState.CharacterCreation);
                return;
            }

            // Mode Connexion : authentification directe auprès du serveur.
            if (PlayerAccount.Local == null && !_reconnectingSession)
            {
                ShowError("Connexion au serveur de jeu en cours, réessaie dans une seconde.", true);
                return;
            }

            _waitingServer = true;
            _waitingSince = Time.unscaledTime;
            _pendingCreationCommit = false;
            ShowError("Vérification des identifiants…", false);

            // Si un compte était déjà lié sur cette connexion réseau, on réinitialise la session avant d'envoyer Login.
            if (PlayerAccount.Local == null || PlayerAccount.Local.IsLinked || _reconnectingSession)
            {
                string prevEmail = PlayerPrefs.GetString(EmailKey, string.Empty);
                string prevHash = PlayerPrefs.GetString(HashKey, string.Empty);
                if (PlayerAccount.Local != null && PlayerAccount.Local.IsLinked &&
                    string.Equals(_stagedEmail, prevEmail, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_stagedHash, prevHash, StringComparison.Ordinal))
                {
                    bool needsApp = PlayerNetworkIdentity.Local != null && !PlayerNetworkIdentity.Local.HasAppearance;
                    OnServerAuthResult(true, "Connecté.", needsApp);
                    return;
                }

                StopAllCoroutines();
                StartCoroutine(RestartClientSessionCoroutine(() =>
                {
                    _waitingSince = Time.unscaledTime;
                    PlayerAccount.Local?.Login(_stagedEmail, _stagedHash);
                }));
                return;
            }

            PlayerAccount.Local.Login(_stagedEmail, _stagedHash);
        }

        /// <summary>
        /// Appelé par l'Écran 3 (<c>CONFIRMER ET JOUER</c>) après validation du pseudo et de l'apparence 3D.
        /// </summary>
        public void CompleteRegistrationFromCreator(string validatedPseudo, Action<string> onError)
        {
            if (_waitingServer)
                return;

            _stagedPseudo = validatedPseudo;

            // Si le joueur s'était déjà connecté (Login existant qui n'avait pas encore d'apparence),
            // le compte est déjà lié sur le serveur : on valide directement l'apparence 3D et on entre en jeu !
            if (!_isRegisterMode && PlayerAccount.Local != null && PlayerAccount.Local.IsLinked)
            {
                Characters.CharacterCustomizer.Instance?.CommitAndSave();
                UIManager.Instance?.TransitionTo(UIScreenState.InGame);
                return;
            }

            if (PlayerAccount.Local == null && !_reconnectingSession)
            {
                onError?.Invoke("Non connecté au serveur de jeu.");
                return;
            }

            // Si l'utilisateur n'était pas passé par le formulaire e-mail (ex. test direct), génère un profil local.
            if (string.IsNullOrEmpty(_stagedEmail) || string.IsNullOrEmpty(_stagedHash))
            {
                _stagedEmail = validatedPseudo.ToLowerInvariant() + "@ivory.ci";
                _stagedHash = PlayerAccount.HashPassword(_stagedEmail, "ivory123456");
            }

            _waitingServer = true;
            _waitingSince = Time.unscaledTime;
            _pendingCreationCommit = true;
            _creatorErrorCallback = onError;

            if (PlayerAccount.Local == null || PlayerAccount.Local.IsLinked || _reconnectingSession)
            {
                StopAllCoroutines();
                StartCoroutine(RestartClientSessionCoroutine(() =>
                {
                    _waitingSince = Time.unscaledTime;
                    PlayerAccount.Local?.Register(_stagedEmail, _stagedHash, validatedPseudo);
                }));
                return;
            }

            PlayerAccount.Local.Register(_stagedEmail, _stagedHash, validatedPseudo);
        }

        private void OnServerAuthResult(bool ok, string message, bool needsAppearance)
        {
            _waitingServer = false;

            if (!ok)
            {
                if (_pendingCreationCommit && UIManager.Instance != null && UIManager.Instance.CurrentState == UIScreenState.CharacterCreation)
                {
                    // Si l'erreur concerne l'e-mail déjà inscrit, ramène sur l'écran 2 ; si c'est le pseudo, affiche sur l'écran 3.
                    if (message != null && message.IndexOf("e-mail", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        OpenAuthScreen(false);
                        _emailInput.text = _stagedEmail;
                        ShowError(message, true);
                    }
                    else
                    {
                        _creatorErrorCallback?.Invoke(message);
                    }
                }
                else
                {
                    if (UIManager.Instance != null && UIManager.Instance.CurrentState == UIScreenState.MainMenu)
                        OpenAuthScreen(false);
                    ShowError(message, true);
                }
                _pendingCreationCommit = false;
                return;
            }

            HideError();

            if (!string.IsNullOrEmpty(_stagedEmail) && !string.IsNullOrEmpty(_stagedHash))
            {
                PlayerPrefs.SetString(EmailKey, _stagedEmail);
                PlayerPrefs.SetString(HashKey, _stagedHash);
                PlayerPrefs.Save();
            }

            if (_pendingCreationCommit)
            {
                _pendingCreationCommit = false;
                Characters.CharacterCustomizer.Instance?.CommitAndSave();
                UIManager.Instance?.TransitionTo(UIScreenState.InGame);
                return;
            }

            if (needsAppearance)
            {
                _isRegisterMode = false;
                WardrobeShopManager.Instance?.PrepareCharacterCreation();
                UIManager.Instance?.TransitionTo(UIScreenState.CharacterCreation);
            }
            else
            {
                UIManager.Instance?.TransitionTo(UIScreenState.InGame);
            }
        }

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || email.Length < 5 || email.Length > 120)
                return false;
            int at = email.IndexOf('@');
            int dot = email.LastIndexOf('.');
            return at >= 1 && dot > at + 1 && dot < email.Length - 1;
        }

        private void ShowError(string msg, bool isError)
        {
            if (string.IsNullOrEmpty(msg))
            {
                HideError();
                return;
            }
            _errorBanner.gameObject.SetActive(true);
            _errorBanner.GetComponent<Image>().color = isError ? ErrorBg : new Color(0.91f, 0.51f, 0.10f, 0.20f);
            _errorText.color = isError ? ErrorText : new Color(1f, 0.88f, 0.65f, 1f);
            _errorText.text = msg;
        }

        private void HideError()
        {
            if (_errorBanner != null)
                _errorBanner.gameObject.SetActive(false);
        }

        #endregion

        #region Construction Écran 1 : MainMenuPanel

        private void BuildMainMenuPanel(RectTransform root)
        {
            _mainMenuPanel = MakePanel("MainMenuPanel", root, Color.white);

            // --- Haut-Gauche : Logo IVORY STATE ---
            RectTransform brand = MakeBox("BrandHeader", _mainMenuPanel, new Color(0f, 0f, 0f, 0f),
                new Vector2(0f, 1f), new Vector2(520f, 110f), new Vector2(64f, -42f));
            MakeBox("AmberBar", brand, Amber, new Vector2(0f, 1f), new Vector2(8f, 82f), new Vector2(0f, -6f));
            Sprite ivoryLogo = HudIconLibrary.IvoryLogo;
            if (ivoryLogo != null)
            {
                MakeImg("LogoImage", brand, ivoryLogo, Color.white,
                    new Vector2(0f, 1f), new Vector2(300f, 62f), new Vector2(24f, 0f));
            }
            else
            {
                Text title = MakeText("LogoTitle", brand, "IVORY STATE", 52, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(490f, 62f), new Vector2(24f, 0f));
                title.fontStyle = FontStyle.Bold;
            }
            Text sub = MakeText("LogoSub", brand, "MÉTROPOLE D'ABIDJAN • MONDE OUVERT", 18, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(490f, 28f), new Vector2(26f, 18f));
            sub.color = Amber;
            sub.fontStyle = FontStyle.Bold;

            // --- Haut-Droite : Statut Réseau + Bouton Engrenage Paramètres ---
            RectTransform netPill = MakeRounded("NetworkStatusPill", _mainMenuPanel, GlassPanel,
                new Vector2(1f, 1f), new Vector2(268f, 52f), new Vector2(-128f, -44f));
            AddBorder(netPill, BorderSubtle);
            netPill.GetComponent<Image>().raycastTarget = true;
            var netPillBtn = netPill.gameObject.AddComponent<Button>();
            netPillBtn.targetGraphic = netPill.GetComponent<Image>();
            netPillBtn.onClick.AddListener(Debugging.DevConnectionPanel.ToggleExpanded);
            RectTransform dot = MakeImg("Dot", netPill, HudIconLibrary.Circle, Emerald, new Vector2(0f, 0.5f), new Vector2(14f, 14f), new Vector2(24f, 0f));
            _networkDot = dot.GetComponent<Image>();
            _networkBadgeText = MakeText("NetText", netPill, "RÉSEAU : CONNECTÉ", 17, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(216f, 40f), new Vector2(44f, 0f));
            _networkBadgeText.fontStyle = FontStyle.Bold;

            RectTransform gearBtn = MakeRounded("TopSettingsBtn", _mainMenuPanel, GlassPanel,
                new Vector2(1f, 1f), new Vector2(52f, 52f), new Vector2(-64f, -44f));
            AddBorder(gearBtn, BorderSubtle);
            gearBtn.GetComponent<Image>().raycastTarget = true;
            var gButton = gearBtn.gameObject.AddComponent<Button>();
            gButton.targetGraphic = gearBtn.GetComponent<Image>();
            gButton.onClick.AddListener(() => PhoneHud.Instance?.OpenSettings());
            MakePaddedIcon("GearIcon", gearBtn, HudIconLibrary.ParameterGear, Color.white, 0.22f);

            // --- Milieu-Gauche : Menu vertical empilé dans conteneur Dark Glassmorphism ---
            RectTransform menuCard = MakeRounded("MenuStackContainer", _mainMenuPanel, GlassPanel,
                new Vector2(0f, 0.5f), new Vector2(430f, 430f), new Vector2(64f, -20f));
            AddBorder(menuCard, BorderSubtle);

            Button btnPlay = MakeMenuButton("BtnPlay", menuCard, "JOUER", 28, Amber, Color.black,
                new Vector2(374f, 78f), new Vector2(0f, -28f));
            btnPlay.onClick.AddListener(OnPlayClicked);

            Button btnLogin = MakeMenuButton("BtnLogin", menuCard, "SE CONNECTER", 22, GlassButton, Color.white,
                new Vector2(374f, 66f), new Vector2(0f, -124f));
            AddBorder((RectTransform)btnLogin.transform, BorderSubtle);
            btnLogin.onClick.AddListener(() => OpenAuthScreen(false));

            Button btnRegister = MakeMenuButton("BtnRegister", menuCard, "CRÉER UN COMPTE", 22, GlassButton, Color.white,
                new Vector2(374f, 66f), new Vector2(0f, -206f));
            AddBorder((RectTransform)btnRegister.transform, BorderSubtle);
            btnRegister.onClick.AddListener(() => OpenAuthScreen(true));

            Button btnSettings = MakeMenuButton("BtnSettings", menuCard, "PARAMÈTRES", 22, GlassButton, Color.white,
                new Vector2(374f, 66f), new Vector2(0f, -288f));
            AddBorder((RectTransform)btnSettings.transform, BorderSubtle);
            btnSettings.onClick.AddListener(() => PhoneHud.Instance?.OpenSettings());

            MakeText("MenuHint", menuCard, "Sauvegarde cloud sécurisée • Multijoueur temps réel", 15, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(380f, 30f), new Vector2(0f, 16f)).color = new Color(1f, 1f, 1f, 0.55f);

            // --- Pied de page (Bas) ---
            RectTransform footer = MakeBox("FooterBar", _mainMenuPanel, new Color(0.04f, 0.05f, 0.07f, 0.72f),
                new Vector2(0.5f, 0f), new Vector2(1920f, 48f), Vector2.zero);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.sizeDelta = new Vector2(0f, 48f);

            MakeText("FooterLeft", footer, "IVORY STATE • CÔTE D'IVOIRE", 16, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(400f, 36f), new Vector2(64f, 0f)).color = new Color(1f, 1f, 1f, 0.65f);
            _footerStatusText = MakeText("FooterCenter", footer, "Connecté au serveur d'Abidjan", 16, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(600f, 36f), Vector2.zero);
            _footerStatusText.color = new Color(1f, 1f, 1f, 0.75f);
            MakeText("FooterRight", footer, "v1.2.0 (DEV)", 16, TextAnchor.MiddleRight,
                new Vector2(1f, 0.5f), new Vector2(260f, 36f), new Vector2(-64f, 0f)).color = Amber;
        }

        #endregion

        #region Construction Écran 2 : RegisterPanel / Login

        private void BuildRegisterPanel(RectTransform root)
        {
            _registerPanel = MakePanel("RegisterPanel", root, new Color(0.72f, 0.72f, 0.76f, 1f));

            // --- Haut-Gauche : Bouton RETOUR + Logo IVORY STATE ---
            Button backBtn = MakeActionBtn("BtnBack", _registerPanel, "‹   RETOUR", 20, GlassPanel, Color.white,
                new Vector2(0f, 1f), new Vector2(170f, 52f), new Vector2(64f, -44f));
            AddBorder((RectTransform)backBtn.transform, BorderSubtle);
            backBtn.onClick.AddListener(() => UIManager.Instance?.TransitionTo(UIScreenState.MainMenu));

            Sprite registerLogo = HudIconLibrary.IvoryLogo;
            if (registerLogo != null)
            {
                MakeImg("TopLogo", _registerPanel, registerLogo, Color.white,
                    new Vector2(0f, 1f), new Vector2(240f, 52f), new Vector2(256f, -44f));
            }
            else
            {
                Text logo = MakeText("TopLogo", _registerPanel, "IVORY <color=#F59E0B>STATE</color>", 32, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(320f, 52f), new Vector2(256f, -44f));
                logo.supportRichText = true;
                logo.fontStyle = FontStyle.Bold;
            }

            // --- Carte Centrale Translucide (INSCRIPTION / Nouveau joueur) ---
            RectTransform card = MakeRounded("AuthCard", _registerPanel, GlassCard,
                new Vector2(0.5f, 0.5f), new Vector2(640f, 720f), new Vector2(0f, -10f));
            AddBorder(card, BorderSubtle);

            // Liseré supérieur ambre
            MakeBox("TopAccent", card, Amber, new Vector2(0.5f, 1f), new Vector2(640f, 6f), Vector2.zero);

            _authCardTitle = MakeText("CardTitle", card, "INSCRIPTION", 38, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(560f, 48f), new Vector2(0f, -32f));
            _authCardTitle.fontStyle = FontStyle.Bold;

            _authCardSubtitle = MakeText("CardSubtitle", card, "Nouveau joueur — Crée ton profil citoyen", 19, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(560f, 28f), new Vector2(0f, -80f));
            _authCardSubtitle.color = Amber;

            // Champ 1 : Adresse e-mail
            _emailInput = BuildAuthField(card, "FieldEmail", "ADRESSE E-MAIL", "exemple@ivorystate.ci",
                -132f, InputField.ContentType.EmailAddress, "@", out _, out _);

            // Champ 2 : Mot de passe + bouton Afficher/Masquer
            _passwordInput = BuildAuthField(card, "FieldPassword", "MOT DE PASSE", "6 caractères minimum",
                -244f, InputField.ContentType.Password, "🔒", out RectTransform passBox, out _);

            Button togglePassBtn = MakeActionBtn("BtnTogglePass", passBox, "AFFICHER", 14, new Color(1f, 1f, 1f, 0.08f), Amber,
                new Vector2(1f, 0.5f), new Vector2(96f, 40f), new Vector2(-12f, 0f));
            _passwordToggleLabel = togglePassBtn.GetComponentInChildren<Text>();
            _passwordToggleLabel.fontStyle = FontStyle.Bold;
            togglePassBtn.onClick.AddListener(TogglePasswordVisibility);

            // Champ 3 : Confirmer le mot de passe
            _confirmPasswordInput = BuildAuthField(card, "FieldConfirmPassword", "CONFIRMER LE MOT DE PASSE", "Retapez votre mot de passe",
                -356f, InputField.ContentType.Password, "🔒", out _, out _confirmRow);

            // Zone de message d'erreur dynamique (masquée par défaut)
            _errorBanner = MakeRounded("ErrorBanner", card, ErrorBg,
                new Vector2(0.5f, 1f), new Vector2(544f, 52f), new Vector2(0f, -468f));
            _errorText = MakeText("ErrorText", _errorBanner, string.Empty, 17, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(516f, 46f), Vector2.zero);
            _errorText.color = ErrorText;
            _errorBanner.gameObject.SetActive(false);

            // Bouton principal ambre-orange : CRÉER MON COMPTE
            Button submitBtn = MakeActionBtn("BtnSubmitAuth", card, "CRÉER MON COMPTE", 24, Amber, Color.black,
                new Vector2(0.5f, 0f), new Vector2(544f, 72f), new Vector2(0f, 84f));
            _submitBtnLabel = submitBtn.GetComponentInChildren<Text>();
            _submitBtnLabel.fontStyle = FontStyle.Bold;
            submitBtn.onClick.AddListener(OnSubmitAuthForm);

            // Lien en bas de carte : Déjà un compte ? Se connecter
            Button switchBtn = MakeActionBtn("BtnSwitchAuthMode", card, "Déjà un compte ? <color=#F59E0B>Se connecter</color>",
                19, new Color(0f, 0f, 0f, 0f), new Color(0.85f, 0.87f, 0.90f, 1f),
                new Vector2(0.5f, 0f), new Vector2(544f, 44f), new Vector2(0f, 24f));
            _switchModeLinkLabel = switchBtn.GetComponentInChildren<Text>();
            _switchModeLinkLabel.supportRichText = true;
            switchBtn.onClick.AddListener(() => OpenAuthScreen(!_isRegisterMode));
        }

        private InputField BuildAuthField(RectTransform parent, string name, string label, string placeholder,
            float y, InputField.ContentType contentType, string iconGlyph, out RectTransform boxOut, out RectTransform rowOut)
        {
            rowOut = MakeBox(name, parent, new Color(0f, 0f, 0f, 0f), new Vector2(0.5f, 1f), new Vector2(544f, 94f), new Vector2(0f, y));
            Text lbl = MakeText("Label", rowOut, label, 16, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(544f, 24f), Vector2.zero);
            lbl.color = new Color(0.78f, 0.80f, 0.84f, 0.95f);
            lbl.fontStyle = FontStyle.Bold;

            boxOut = MakeRounded("InputBox", rowOut, FieldBg, new Vector2(0.5f, 0f), new Vector2(544f, 62f), Vector2.zero);
            AddBorder(boxOut, BorderSubtle);
            boxOut.GetComponent<Image>().raycastTarget = true;

            // Pastille icône à gauche
            RectTransform iconBadge = MakeRounded("IconBadge", boxOut, new Color(1f, 1f, 1f, 0.06f),
                new Vector2(0f, 0.5f), new Vector2(44f, 44f), new Vector2(10f, 0f));
            Text glyph = MakeText("Glyph", iconBadge, iconGlyph, 20, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(44f, 44f), Vector2.zero);
            glyph.color = Amber;
            glyph.fontStyle = FontStyle.Bold;

            Text textComp = MakeText("Text", boxOut, string.Empty, 22, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(360f, 52f), new Vector2(68f, 0f));
            textComp.supportRichText = false;

            Text placeholderComp = MakeText("Placeholder", boxOut, placeholder, 21, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(360f, 52f), new Vector2(68f, 0f));
            placeholderComp.color = new Color(1f, 1f, 1f, 0.32f);
            placeholderComp.fontStyle = FontStyle.Italic;

            var input = boxOut.gameObject.AddComponent<InputField>();
            input.targetGraphic = boxOut.GetComponent<Image>();
            input.textComponent = textComp;
            input.placeholder = placeholderComp;
            input.contentType = contentType;
            input.characterLimit = 120;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        #endregion

        #region Helpers UI

        private static Sprite s_backdropSprite;

        private static RectTransform MakePanel(string name, RectTransform parent, Color bgColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = GetOrCreateMenuBackdropSprite();
            img.color = bgColor;
            img.raycastTarget = true;
            return rect;
        }

        /// <summary>
        /// Génère un fond d'écran 16:9 complet et 100 % opaque représentant la skyline du Plateau (Abidjan)
        /// et la lagune Ébrié au crépuscule ambré, afin que la scène 3D en jeu ne soit jamais visible
        /// derrière la page d'accueil ou l'écran de connexion/inscription.
        /// </summary>
        private static Sprite GetOrCreateMenuBackdropSprite()
        {
            if (s_backdropSprite != null)
                return s_backdropSprite;

            const int w = 480;
            const int h = 270;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "AbidjanMenuBackdrop",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[w * h];
            Color zenith = new Color(0.04f, 0.06f, 0.11f, 1f);
            Color midSky = new Color(0.11f, 0.09f, 0.16f, 1f);
            Color horizonAmber = new Color(0.68f, 0.32f, 0.10f, 1f);
            Color waterDeep = new Color(0.03f, 0.05f, 0.09f, 1f);
            Color buildingDark = new Color(0.05f, 0.06f, 0.09f, 1f);
            Color windowGlow = new Color(0.96f, 0.68f, 0.22f, 1f);

            // Profil des tours de la skyline du Plateau (xMin, xMax, topY en coordonnées normalisées 0..1)
            (float x0, float x1, float top)[] towers =
            {
                (0.04f, 0.09f, 0.48f),
                (0.10f, 0.15f, 0.56f),
                (0.16f, 0.20f, 0.44f),
                (0.24f, 0.30f, 0.62f),
                (0.31f, 0.35f, 0.51f),
                (0.37f, 0.44f, 0.68f),
                (0.45f, 0.50f, 0.54f),
                (0.52f, 0.58f, 0.72f),
                (0.59f, 0.65f, 0.58f),
                (0.67f, 0.73f, 0.65f),
                (0.74f, 0.79f, 0.49f),
                (0.81f, 0.88f, 0.60f),
                (0.89f, 0.95f, 0.46f),
            };

            const float horizonY = 0.33f;

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    Color c;

                    if (ny >= horizonY)
                    {
                        // Ciel crépusculaire + halo solaire ambré
                        float skyT = Mathf.Clamp01((ny - horizonY) / (1f - horizonY));
                        c = skyT < 0.38f
                            ? Color.Lerp(horizonAmber, midSky, skyT / 0.38f)
                            : Color.Lerp(midSky, zenith, (skyT - 0.38f) / 0.62f);

                        float sunDist = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.66f, 0.38f));
                        float sunGlow = Mathf.Exp(-sunDist * 5.2f) * 0.55f;
                        c += new Color(0.95f, 0.52f, 0.12f, 0f) * sunGlow;

                        // Silhouette des tours de la métropole + fenêtres éclairées
                        for (int t = 0; t < towers.Length; t++)
                        {
                            var tw = towers[t];
                            if (nx >= tw.x0 && nx <= tw.x1 && ny <= tw.top)
                            {
                                c = buildingDark;
                                int wx = Mathf.FloorToInt((nx - tw.x0) * 260f);
                                int wy = Mathf.FloorToInt((ny - horizonY) * 180f);
                                if (wx % 3 == 1 && wy % 4 == 1 && ny < tw.top - 0.02f)
                                {
                                    int hash = ((wx * 37) ^ (wy * 73) ^ (t * 19)) & 7;
                                    if (hash < 4)
                                        c = Color.Lerp(buildingDark, windowGlow, 0.72f);
                                }
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Lagune Ébrié + reflets ambrés de la ville
                        float waterT = Mathf.Clamp01((horizonY - ny) / horizonY);
                        c = Color.Lerp(new Color(0.24f, 0.13f, 0.08f, 1f), waterDeep, Mathf.Sqrt(waterT));
                        float sunRefDist = Mathf.Abs(nx - 0.66f);
                        float shimmer = Mathf.Exp(-sunRefDist * 7.5f) * (1f - waterT * 0.75f);
                        float wave = 0.65f + 0.35f * Mathf.Sin(ny * 190f + nx * 25f);
                        c += new Color(0.88f, 0.45f, 0.10f, 0f) * (shimmer * wave * 0.45f);
                    }

                    // Vignette sombre douce sur les bords pour sublimer les panneaux Dark Glassmorphism
                    float vigX = Mathf.SmoothStep(0f, 1f, Mathf.Min(nx * 3.5f, (1f - nx) * 4.5f));
                    float vigY = Mathf.SmoothStep(0f, 1f, Mathf.Min(ny * 4.5f, (1f - ny) * 4.5f));
                    float vig = Mathf.Lerp(0.52f, 1f, vigX * vigY);
                    c.r = Mathf.Clamp01(c.r * vig);
                    c.g = Mathf.Clamp01(c.g * vig);
                    c.b = Mathf.Clamp01(c.b * vig);
                    c.a = 1f;

                    pixels[y * w + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            s_backdropSprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
            return s_backdropSprite;
        }

        private static RectTransform MakeBox(string name, Transform parent, Color color, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rect;
        }

        private static RectTransform MakeRounded(string name, Transform parent, Color color, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            RectTransform rect = MakeBox(name, parent, color, anchor, size, pos);
            var img = rect.GetComponent<Image>();
            img.sprite = HudIconLibrary.RoundedBox;
            img.type = Image.Type.Sliced;
            return rect;
        }

        private static RectTransform MakeImg(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            RectTransform rect = MakeBox(name, parent, color, anchor, size, pos);
            Image img = rect.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            return rect;
        }

        private static RectTransform MakePaddedIcon(string name, Transform parent, Sprite sprite, Color color, float padding)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(padding, padding);
            rect.anchorMax = new Vector2(1f - padding, 1f - padding);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return rect;
        }

        private static void AddBorder(RectTransform rect, Color borderColor)
        {
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(1.2f, 1.2f);
        }

        private Text MakeText(string name, Transform parent, string value, int size, TextAnchor align, Vector2 anchor, Vector2 box, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = box;
            rect.anchoredPosition = pos;
            var txt = go.GetComponent<Text>();
            txt.font = _font;
            txt.fontSize = size;
            txt.alignment = align;
            txt.color = Color.white;
            txt.text = value;
            txt.raycastTarget = false;
            return txt;
        }

        private Button MakeMenuButton(string name, Transform parent, string label, int fontSize, Color bgColor, Color textColor, Vector2 size, Vector2 pos)
        {
            return MakeActionBtn(name, parent, label, fontSize, bgColor, textColor, new Vector2(0.5f, 1f), size, pos);
        }

        private Button MakeActionBtn(string name, Transform parent, string label, int fontSize, Color bgColor, Color textColor, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            RectTransform rect = MakeRounded(name, parent, bgColor, anchor, size, pos);
            Image img = rect.GetComponent<Image>();
            img.raycastTarget = true;
            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            Text txt = MakeText("Label", rect, label, fontSize, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), size, Vector2.zero);
            txt.color = textColor;
            txt.fontStyle = FontStyle.Bold;
            return btn;
        }

        #endregion
    }
}
