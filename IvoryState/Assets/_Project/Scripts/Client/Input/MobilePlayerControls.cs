using IvoryState.Client.Settings;
using IvoryState.Client.UI;
using IvoryState.Gameplay.Input;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace IvoryState.Client.Input
{
    /// <summary>
    /// Contrôles tactiles du joueur local, façon jeu mobile en monde ouvert :
    /// <list type="bullet">
    /// <item><b>Joystick bas-gauche</b> : fixe ou flottant selon <see cref="GameSettings.FixedJoystick"/>.
    /// Pousser au-delà du cercle ou maintenir le bouton Courir = courir.</item>
    /// <item><b>Caméra</b> : glisser le pouce droit n'importe où sur la moitié droite.</item>
    /// <item><b>Conduite</b> : en véhicule, le joystick s'efface au profit de <see cref="DrivingHud"/>.</item>
    /// </list>
    /// Repli PC (éditeur, builds de test) : ZQSD/WASD + Maj + Espace.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobilePlayerControls : MonoBehaviour, IMovementInputProvider, IActionInputProvider
    {
        private const int NoFinger = -1;
        private const int MousePointerId = int.MaxValue; // jamais utilisé par un vrai doigt
        private const float FallbackDpi = 160f;

        private enum PointerPhase : byte { Began, Held, Ended }

        public static MobilePlayerControls Instance { get; private set; }

        private static readonly System.Collections.Generic.List<RectTransform> s_blockingRects =
            new System.Collections.Generic.List<RectTransform>();

        /// <summary>
        /// Zones d'interface (menus, boutons UGUI) : une touche qui y commence est laissée à l'UI,
        /// sans faire bouger le joueur ni tourner la caméra. Seules les zones actives comptent.
        /// </summary>
        public static void RegisterBlockingRect(RectTransform rect)
        {
            if (rect != null && !s_blockingRects.Contains(rect))
                s_blockingRects.Add(rect);
        }

        public static void UnregisterBlockingRect(RectTransform rect)
        {
            s_blockingRects.Remove(rect);
        }

        /// <summary>Vrai pendant une saisie de texte : le clavier ne déplace plus le personnage.</summary>
        public static bool KeyboardSuspended { get; set; }

        /// <summary>Multiplicateur de sensibilité de la caméra (réglage du téléphone, 0,5 à 2).</summary>
        public static float LookSensitivity { get; set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            s_blockingRects.Clear();
            KeyboardSuspended = false;
        }

        /// <summary>Vrai si un champ de saisie UGUI a le focus (chat, pseudo…) : ZQSD sert alors à écrire.</summary>
        private static bool IsTypingText()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent(out UnityEngine.UI.InputField field) && field.isFocused;
        }

        private static bool IsOverBlockingUi(Vector2 position)
        {
            for (int i = 0; i < s_blockingRects.Count; i++)
            {
                if (IsOverButton(s_blockingRects[i], position))
                    return true;
            }

            return false;
        }

        [Header("Zones de l'écran")]
        [Tooltip("Largeur de la zone joystick, en fraction de l'écran (depuis la gauche).")]
        [SerializeField, Range(0.2f, 0.8f)] private float _moveZoneWidth = 0.5f;

        [Tooltip("Hauteur de la zone joystick, en fraction de l'écran (depuis le bas). Le haut reste libre pour l'UI.")]
        [SerializeField, Range(0.3f, 1f)] private float _moveZoneHeight = 0.7f;

        [Tooltip("Zone du bouton de saut (RectTransform d'un Canvas en Screen Space - Overlay).")]
        [SerializeField] private RectTransform _jumpButton;

        [Tooltip("Zone du bouton d'action (affiché seulement près d'un point d'interaction).")]
        [SerializeField] private RectTransform _interactButton;

        [Header("Joystick")]
        [Tooltip("Rayon du joystick en pouces physiques : même taille sous le doigt quelle que soit la densité d'écran.")]
        [SerializeField, Range(0.2f, 1f)] private float _joystickRadiusInches = 0.48f;

        [SerializeField, Range(0f, 0.5f)] private float _deadZone = 0.12f;

        [Tooltip("Le joueur court quand le doigt dépasse ce multiple du rayon.")]
        [SerializeField, Range(1f, 2f)] private float _sprintPushFactor = 1.35f;

        [Tooltip("La course ne s'arrête que sous ce multiple du rayon (hystérésis : pas d'alternance marche/course " +
                 "quand le doigt tremble autour du seuil).")]
        [SerializeField, Range(0.5f, 2f)] private float _sprintReleaseFactor = 1.1f;

        [Header("Caméra")]
        [Tooltip("Degrés de rotation par pouce glissé.")]
        [SerializeField, Range(30f, 400f)] private float _lookDegreesPerInch = 140f;

        [SerializeField] private float _minPitch = -10f;
        [SerializeField] private float _maxPitch = 65f;
        [SerializeField] private float _initialPitch = 18f;

        private int _moveFingerId = NoFinger;
        private int _lookFingerId = NoFinger;
        private Vector2 _joystickOrigin;
        private Vector2 _joystickKnob;
        private float _pixelsPerInch;

        private Vector2 _move;
        private bool _sprint;
        private bool _jumpQueued;
        private bool _interactQueued;
        private float _yaw;
        private float _pitch;

        // Remis à faux à chaque frame : un pointeur suivi qui n'est plus vu est relâché.
        private bool _moveSeen;
        private bool _lookSeen;

        /// <summary>Maintenu par le bouton tactile « Courir » en bas à droite.</summary>
        public bool SprintButtonHeld { get; set; }

        /// <summary>Déclenche un saut depuis un bouton du HUD.</summary>
        public void QueueJump() => _jumpQueued = true;

        /// <summary>Déclenche une interaction depuis un bouton du HUD.</summary>
        public void QueueInteract() => _interactQueued = true;

        #region IMovementInputProvider

        public Vector2 Move => _move;
        public bool Sprint => _sprint || SprintButtonHeld;
        public float ViewYaw => _yaw;

        public bool ConsumeJump()
        {
            bool jump = _jumpQueued;
            _jumpQueued = false;
            return jump;
        }

        public bool ConsumeInteract()
        {
            bool interact = _interactQueued;
            _interactQueued = false;
            return interact;
        }

        #endregion

        /// <summary>Tangage (pitch) de la caméra en degrés.</summary>
        public float Pitch => _pitch;

        public bool IsJoystickActive => _moveFingerId != NoFinger;

        /// <summary>Position de repos du joystick (par défaut en bas à gauche, ou personnalisée dans les paramètres).</summary>
        public Vector2 RestingJoystickOrigin
        {
            get
            {
                if (GameSettings.TryGetHudLayout(HudControlId.Joystick, out HudControlLayout layout) && layout.Customized)
                    return new Vector2(layout.NormX * Screen.width, layout.NormY * Screen.height);
                return new Vector2(Screen.width * 0.125f, Screen.height * 0.22f);
            }
        }

        /// <summary>Diagnostic (panneau de dev) : doigts posés et doigts attribués au joystick et à la caméra.</summary>
        public int TouchCount => _previousTouchCount;
        public int MoveFingerId => _moveFingerId;
        public int LookFingerId => _lookFingerId;

        // Identifiants des doigts posés, frame courante et précédente (10 = maximum de l'écran tactile).
        private readonly int[] _currentTouchIds = new int[10];
        private readonly int[] _previousTouchIds = new int[10];
        private int _currentTouchCount;
        private int _previousTouchCount;
        public Vector2 JoystickOrigin => _moveFingerId != NoFinger ? _joystickOrigin : RestingJoystickOrigin;
        public Vector2 JoystickKnob => _moveFingerId != NoFinger ? _joystickKnob : RestingJoystickOrigin;
        public float JoystickRadiusPixels
        {
            get
            {
                float scale = GameSettings.TryGetHudLayout(HudControlId.Joystick, out HudControlLayout layout) && layout.Customized
                    ? layout.Scale
                    : 1f;
                return _joystickRadiusInches * _pixelsPerInch * scale;
            }
        }

        /// <summary>Oriente la vue (ex. derrière le personnage à son apparition).</summary>
        public void SetYaw(float yaw)
        {
            _yaw = yaw;
        }

        private void Awake()
        {
            Instance = this;
            _pitch = _initialPitch;
        }

        private void OnEnable()
        {
            Instance = this;
            _pixelsPerInch = Screen.dpi > 0f ? Screen.dpi : FallbackDpi;
            LookSensitivity = GameSettings.CameraSensitivity;
            GameSettings.Changed += OnSettingsChanged;
            PlayerInputRegistry.Register(this);
        }

        private void OnDisable()
        {
            GameSettings.Changed -= OnSettingsChanged;
            PlayerInputRegistry.Unregister(this);
            ReleaseAll();
        }

        private static void OnSettingsChanged()
        {
            LookSensitivity = GameSettings.CameraSensitivity;
        }

        private void OnApplicationPause(bool paused)
        {
            // Retour d'arrière-plan : les doigts « fantômes » ne doivent pas faire marcher le joueur.
            if (paused)
                ReleaseAll();
        }

        private void Update()
        {
            _moveSeen = false;
            _lookSeen = false;

            ProcessTouches();
            ProcessMouse();

            if (!_moveSeen && _moveFingerId != NoFinger)
                ReleaseJoystick();
            if (!_lookSeen)
                _lookFingerId = NoFinger;

            ProcessKeyboardFallback();

            // Bouton tactile « Courir » : fait aussi courir vers l'avant si le joystick n'est pas touché.
            if (SprintButtonHeld)
            {
                _sprint = true;
                if (_move.sqrMagnitude < 0.01f)
                    _move = new Vector2(0f, 1f);
            }

            // En conduite : les pédales et boutons de direction pilotent le véhicule.
            if (PlayerDriving.Local != null && PlayerDriving.Local.IsDriving &&
                DrivingHud.Instance != null && DrivingHud.Instance.HasInput)
            {
                _move = new Vector2(DrivingHud.Instance.Steer, DrivingHud.Instance.Throttle);
                _sprint = false;
            }
        }

        private void ProcessTouches()
        {
            _currentTouchCount = 0;
            Touchscreen screen = Touchscreen.current;
            if (screen == null)
            {
                _previousTouchCount = 0;
                return;
            }

            var slots = screen.touches;
            for (int i = 0; i < slots.Count; i++)
            {
                TouchControl slot = slots[i];
                int id = slot.touchId.ReadValue();
                if (id == 0)
                    continue;

                TouchPhase touchPhase = slot.phase.ReadValue();
                Vector2 position = slot.position.ReadValue();

                if (touchPhase == TouchPhase.Ended || touchPhase == TouchPhase.Canceled)
                {
                    HandlePointer(id, position, Vector2.zero, PointerPhase.Ended);
                    continue;
                }

                if (touchPhase != TouchPhase.Began && touchPhase != TouchPhase.Moved && touchPhase != TouchPhase.Stationary)
                    continue;

                bool isNew = !ContainsId(_previousTouchIds, _previousTouchCount, id);
                HandlePointer(id, position, isNew ? Vector2.zero : slot.delta.ReadValue(),
                    isNew ? PointerPhase.Began : PointerPhase.Held);

                if (_currentTouchCount < _currentTouchIds.Length)
                    _currentTouchIds[_currentTouchCount++] = id;
            }

            System.Array.Copy(_currentTouchIds, _previousTouchIds, _currentTouchCount);
            _previousTouchCount = _currentTouchCount;
        }

        private static bool ContainsId(int[] ids, int count, int id)
        {
            for (int i = 0; i < count; i++)
            {
                if (ids[i] == id)
                    return true;
            }

            return false;
        }

        private void ProcessMouse()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            Vector2 position = mouse.position.ReadValue();
            Vector2 delta = mouse.delta.ReadValue();

            var left = mouse.leftButton;
            if (left.wasPressedThisFrame)
                HandlePointer(MousePointerId, position, Vector2.zero, PointerPhase.Began);
            else if (left.isPressed)
                HandlePointer(MousePointerId, position, delta, PointerPhase.Held);
            else if (left.wasReleasedThisFrame)
                HandlePointer(MousePointerId, position, delta, PointerPhase.Ended);

            if (mouse.rightButton.isPressed)
                ApplyLook(delta);
#endif
        }

        private void HandlePointer(int id, Vector2 position, Vector2 delta, PointerPhase phase)
        {
            if (phase == PointerPhase.Began)
            {
                if (IsOverBlockingUi(position))
                    return;

                if (IsOverButton(_jumpButton, position))
                {
                    _jumpQueued = true;
                    return;
                }

                if (IsOverButton(_interactButton, position))
                {
                    _interactQueued = true;
                    return;
                }

                if (_moveFingerId == NoFinger && IsInMoveZone(position))
                {
                    _moveFingerId = id;
                    _joystickOrigin = GameSettings.FixedJoystick ? RestingJoystickOrigin : position;
                }
                else if (_lookFingerId == NoFinger)
                {
                    _lookFingerId = id;
                }
            }

            bool ended = phase == PointerPhase.Ended;

            if (id == _moveFingerId)
            {
                _moveSeen |= !ended;
                if (ended)
                    ReleaseJoystick();
                else
                    UpdateJoystick(position);
            }
            else if (id == _lookFingerId)
            {
                _lookSeen |= !ended;
                if (!ended)
                    ApplyLook(delta);
            }
        }

        private void UpdateJoystick(Vector2 fingerPosition)
        {
            float radius = JoystickRadiusPixels;
            Vector2 offset = fingerPosition - _joystickOrigin;
            float distance = offset.magnitude;

            _sprint = _sprint ? distance > radius * _sprintReleaseFactor : distance > radius * _sprintPushFactor;

            Vector2 clamped = distance > radius ? offset * (radius / distance) : offset;
            _joystickKnob = _joystickOrigin + clamped;

            float normalized = clamped.magnitude / radius;
            if (normalized < _deadZone)
            {
                _move = Vector2.zero;
                return;
            }

            float magnitude = Mathf.InverseLerp(_deadZone, 1f, normalized);
            _move = clamped / (normalized * radius) * magnitude;
        }

        private void ApplyLook(Vector2 deltaPixels)
        {
            float degreesPerPixel = _lookDegreesPerInch * LookSensitivity / _pixelsPerInch;
            _yaw = Mathf.Repeat(_yaw + deltaPixels.x * degreesPerPixel, 360f);
            _pitch = Mathf.Clamp(_pitch - deltaPixels.y * degreesPerPixel, _minPitch, _maxPitch);
        }

        private void ProcessKeyboardFallback()
        {
            Keyboard keyboard = KeyboardSuspended || IsTypingText() ? null : Keyboard.current;

            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame)
                    _jumpQueued = true;
                if (keyboard.eKey.wasPressedThisFrame)
                    _interactQueued = true;
            }

            if (_moveFingerId != NoFinger)
                return;

            _move = Vector2.zero;
            _sprint = false;

            if (keyboard == null)
                return;

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

            if (x != 0f || y != 0f)
            {
                _move = new Vector2(x, y).normalized;
                _sprint = keyboard.leftShiftKey.isPressed;
            }
        }

        private bool IsInMoveZone(Vector2 position)
        {
            if (PlayerDriving.Local != null && PlayerDriving.Local.IsDriving)
                return false;

            if (GameSettings.TryGetHudLayout(HudControlId.Joystick, out HudControlLayout layout) && layout.Customized)
            {
                float zoneRadius = Mathf.Max(Screen.height * 0.22f, JoystickRadiusPixels * 2.4f);
                if (Vector2.Distance(position, RestingJoystickOrigin) <= zoneRadius)
                    return true;
                if (layout.NormX >= 0.5f)
                    return position.x >= Screen.width * (1f - _moveZoneWidth) && position.y < Screen.height * _moveZoneHeight;
            }

            return position.x < Screen.width * _moveZoneWidth && position.y < Screen.height * _moveZoneHeight;
        }

        private static bool IsOverButton(RectTransform button, Vector2 position)
        {
            return button != null && button.gameObject.activeInHierarchy &&
                   RectTransformUtility.RectangleContainsScreenPoint(button, position, null);
        }

        private void ReleaseJoystick()
        {
            _moveFingerId = NoFinger;
            _move = Vector2.zero;
            _sprint = false;
        }

        private void ReleaseAll()
        {
            ReleaseJoystick();
            _lookFingerId = NoFinger;
            _jumpQueued = false;
            _interactQueued = false;
            SprintButtonHeld = false;
        }
    }
}
