using System;
using System.Collections.Generic;
using IvoryState.Client.Cameras;
using IvoryState.Client.Characters;
using IvoryState.Client.Input;
using IvoryState.Gameplay.Interaction;
using UnityEngine;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// États de navigation des écrans principaux d'Ivory State.
    /// </summary>
    public enum UIScreenState : byte
    {
        /// <summary>En jeu (monde ouvert actif, HUD de gameplay visible).</summary>
        InGame = 0,
        /// <summary>Écran 1 : Page d'accueil principale (JOUER, SE CONNECTER, CRÉER UN COMPTE, PARAMÈTRES).</summary>
        MainMenu = 1,
        /// <summary>Écran 2 (mode Connexion) : Connexion à un compte existant.</summary>
        Login = 2,
        /// <summary>Écran 2 (mode Inscription) : Création d'un nouveau compte.</summary>
        Register = 3,
        /// <summary>Écran 3 : Choix de pseudo et création d'apparence 3D sur socle.</summary>
        CharacterCreation = 4,
        /// <summary>Écran 4 : Changement d'apparence &amp; boutique de vêtements en jeu.</summary>
        InGameWardrobe = 5,
    }

    /// <summary>
    /// Machine à états centralisée (<c>UIManager</c>) gérant les transitions fluides (fondu + glissement)
    /// entre <c>MainMenu</c>, <c>Login</c>, <c>Register</c>, <c>CharacterCreation</c> et <c>InGameWardrobe</c>,
    /// sans jamais superposer deux écrans actifs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        public event Action<UIScreenState> StateChanged;

        private readonly Dictionary<UIScreenState, RectTransform> _panels = new Dictionary<UIScreenState, RectTransform>();
        private readonly Dictionary<UIScreenState, CanvasGroup> _groups = new Dictionary<UIScreenState, CanvasGroup>();
        private readonly Dictionary<Transform, bool> _savedHudChildStates = new Dictionary<Transform, bool>();

        private UIScreenState _currentState = UIScreenState.InGame;
        private CanvasGroup _activeGroup;
        private RectTransform _activePanel;
        private float _transitionProgress = 1f;
        private Transform _canvasRoot;

        public UIScreenState CurrentState => _currentState;
        public bool IsMenuOpen => _currentState != UIScreenState.InGame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Enregistre un panneau UI auprès de la machine à états.</summary>
        public void RegisterPanel(UIScreenState state, RectTransform panel)
        {
            if (panel == null)
                return;

            _panels[state] = panel;
            if (!panel.TryGetComponent(out CanvasGroup group))
                group = panel.gameObject.AddComponent<CanvasGroup>();
            _groups[state] = group;
            panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Bascule vers l'écran demandé en désactivant immédiatement tous les autres panneaux
        /// et en animant l'apparition par fondu fluide.
        /// </summary>
        public void TransitionTo(UIScreenState nextState)
        {
            // Désactive strictement tous les panneaux enregistrés pour empêcher toute superposition.
            foreach (KeyValuePair<UIScreenState, RectTransform> entry in _panels)
            {
                if (entry.Value != null)
                {
                    bool active = entry.Key == nextState;
                    // Login et Register partagent le panneau RegisterPanel si enregistré sur les deux états.
                    if (!active && (nextState == UIScreenState.Login || nextState == UIScreenState.Register) &&
                        _panels.TryGetValue(nextState, out RectTransform targetRect) && entry.Value == targetRect)
                    {
                        active = true;
                    }

                    entry.Value.gameObject.SetActive(active);
                    if (active)
                        MobilePlayerControls.RegisterBlockingRect(entry.Value);
                    else
                        MobilePlayerControls.UnregisterBlockingRect(entry.Value);
                }
            }

            _currentState = nextState;
            MobilePlayerControls.KeyboardSuspended = nextState != UIScreenState.InGame;

            // Masque l'intégralité du HUD et des touches du joueur dès qu'un écran de menu est ouvert.
            SetGameplayHudVisible(nextState == UIScreenState.InGame);

            if (_panels.TryGetValue(nextState, out _activePanel) && _groups.TryGetValue(nextState, out _activeGroup))
            {
                if (nextState == UIScreenState.MainMenu || nextState == UIScreenState.Login || nextState == UIScreenState.Register)
                {
                    _transitionProgress = 1f;
                    _activeGroup.alpha = 1f;
                }
                else
                {
                    _transitionProgress = 0f;
                    _activeGroup.alpha = 0f;
                }
            }
            else
            {
                _activePanel = null;
                _activeGroup = null;
                _transitionProgress = 1f;
            }

            // Pilotage automatique de la caméra 3D et du socle selon l'écran actif.
            switch (nextState)
            {
                case UIScreenState.MainMenu:
                case UIScreenState.Login:
                case UIScreenState.Register:
                    CharacterCustomizer.Instance?.EndPreview();
                    TurntableCameraController.Instance?.EnablePanoramicMenuCamera();
                    break;

                case UIScreenState.CharacterCreation:
                case UIScreenState.InGameWardrobe:
                    CharacterCustomizer.Instance?.BeginPreview();
                    TurntableCameraController.Instance?.EnableCharacterTurntable(TurntableViewMode.FullBody);
                    break;

                case UIScreenState.InGame:
                    CharacterCustomizer.Instance?.EndPreview();
                    TurntableCameraController.Instance?.RestoreGameplayCamera();
                    break;
            }

            StateChanged?.Invoke(nextState);
        }

        /// <summary>
        /// Masque ou restaure tous les éléments du HUD de jeu (compteurs d'argent, mini-carte, barres de vie,
        /// chat, joystick tactile, boutons de saut/combat/micro/dock/conduite) afin qu'aucune touche joueur
        /// n'apparaisse sur les écrans d'accueil, de connexion, de création ou de garde-robe.
        /// </summary>
        private void SetGameplayHudVisible(bool visible)
        {
            if (_canvasRoot == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                _canvasRoot = canvas != null ? canvas.transform : (transform.parent != null && transform.parent.parent != null ? transform.parent.parent : transform.parent);
            }

            if (_canvasRoot == null)
                return;

            // Place toujours le conteneur Phone/Compte au premier plan du Canvas.
            if (transform.parent != null && transform.parent.parent == _canvasRoot)
                transform.parent.SetAsLastSibling();
            if (!visible)
                transform.SetAsLastSibling();

            // Active/désactive les MonoBehaviours du HUD sur le Canvas pour empêcher leur Update() de réafficher des boutons.
            if (_canvasRoot.TryGetComponent(out PlayerHud playerHud))
                playerHud.enabled = visible;
            if (_canvasRoot.TryGetComponent(out CombatHud combatHud))
                combatHud.enabled = visible;
            if (_canvasRoot.TryGetComponent(out TutorialHud tutorialHud))
                tutorialHud.enabled = visible;
            if (_canvasRoot.TryGetComponent(out VirtualJoystickView joystickView))
                joystickView.enabled = visible;
            if (_canvasRoot.TryGetComponent(out DrivingHud drivingHud))
                drivingHud.enabled = visible;

            if (!visible)
            {
                int count = _canvasRoot.childCount;
                for (int i = 0; i < count; i++)
                {
                    Transform child = _canvasRoot.GetChild(i);
                    if (child == transform || child == transform.parent)
                        continue;

                    if (!_savedHudChildStates.ContainsKey(child))
                        _savedHudChildStates[child] = child.gameObject.activeSelf;

                    if (child.gameObject.activeSelf)
                        child.gameObject.SetActive(false);
                }

                // Dans le conteneur Phone (parent de Compte), masque aussi PhoneButton / Call / Request / Residence,
                // mais garde ChoiceMenu actif pour que ChoiceMenuHud reste abonné à LocalMenuRequested en jeu.
                if (transform.parent != null && transform.parent != _canvasRoot)
                {
                    int pCount = transform.parent.childCount;
                    for (int i = 0; i < pCount; i++)
                    {
                        Transform sibling = transform.parent.GetChild(i);
                        if (sibling == transform || sibling.name == "Smartphone")
                            continue;
                        if (sibling.TryGetComponent(out ChoiceMenuHud choiceMenu))
                        {
                            choiceMenu.CloseMenu();
                            continue;
                        }
                        if (sibling.gameObject.activeSelf)
                            sibling.gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                foreach (KeyValuePair<Transform, bool> kv in _savedHudChildStates)
                {
                    if (kv.Key != null)
                        kv.Key.gameObject.SetActive(kv.Value);
                }
                _savedHudChildStates.Clear();
            }
        }

        /// <summary>Ouvre l'Écran 4 (Changement d'apparence en jeu) pour une boutique de vêtements.</summary>
        public void OpenInGameWardrobe(ClothingStorePoint store = null)
        {
            WardrobeShopManager.Instance?.OpenForStore(store);
            TransitionTo(UIScreenState.InGameWardrobe);
        }

        private void Update()
        {
            if (_transitionProgress < 1f && _activeGroup != null)
            {
                _transitionProgress = Mathf.MoveTowards(_transitionProgress, 1f, Time.unscaledDeltaTime * 5.5f);
                _activeGroup.alpha = Mathf.SmoothStep(0f, 1f, _transitionProgress);
            }

            // Si un élément du HUD a été instancié tardivement (ex. dans PlayerHud.Start()) alors qu'un menu est ouvert,
            // on le masque immédiatement.
            if (IsMenuOpen && _canvasRoot != null)
            {
                int count = _canvasRoot.childCount;
                for (int i = 0; i < count; i++)
                {
                    Transform child = _canvasRoot.GetChild(i);
                    if (child == transform || child == transform.parent)
                        continue;
                    if (child.gameObject.activeSelf)
                    {
                        if (!_savedHudChildStates.ContainsKey(child))
                            _savedHudChildStates[child] = true;
                        child.gameObject.SetActive(false);
                    }
                }
            }
        }
    }
}
