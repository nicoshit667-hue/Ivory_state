using IvoryState.Client.Characters;
using IvoryState.Gameplay.Player;
using UnityEngine;
using UnityEngine.EventSystems;

namespace IvoryState.Client.Cameras
{
    /// <summary>
    /// Modes de cadrage de la caméra d'aperçu personnage (Écrans 3 et 4).
    /// </summary>
    public enum TurntableViewMode : byte
    {
        /// <summary>Vue en pied (corps entier sur le socle lumineux).</summary>
        FullBody = 0,
        /// <summary>Zoom portrait (visage, coiffure et haut du corps).</summary>
        FaceZoom = 1,
    }

    /// <summary>
    /// Contrôleur de plateau tournant (<c>IDragHandler</c>) et de cadrage caméra :
    /// <list type="bullet">
    /// <item>Rotation tactile fluide à <b>360°</b> par glissement horizontal du doigt sur la zone du personnage.</item>
    /// <item>Transitions douces (<c>SmoothDamp</c>) entre la vue <b>Corps entier</b> et le zoom <b>Visage / Haut du corps</b>.</item>
    /// <item>Mode panoramique d'accueil (<c>MainMenu</c>) surplombant la métropole d'Abidjan.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurntableCameraController : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        public static TurntableCameraController Instance { get; private set; }

        [SerializeField] private float _rotationSensitivity = 0.45f;
        [SerializeField] private float _zoomSmoothTime = 0.18f;

        private bool _turntableActive;
        private bool _panoramicActive;
        private TurntableViewMode _viewMode = TurntableViewMode.FullBody;

        private float _currentDist = 2.65f;
        private float _currentHeight = 1.05f;
        private float _currentLateralOffset = 0.55f;
        private float _distVel;
        private float _heightVel;
        private float _lateralVel;
        private float _panoramicAngle;

        private ThirdPersonCameraRig _gameplayRig;

        public TurntableViewMode ViewMode => _viewMode;

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
            RestoreGameplayCamera();
        }

        /// <summary>Active la vue panoramique de la page d'accueil (Écran 1).</summary>
        public void EnablePanoramicMenuCamera()
        {
            _panoramicActive = true;
            _turntableActive = false;
            SetGameplayRigEnabled(false);
        }

        /// <summary>Active la caméra studio face au personnage sur son socle (Écrans 3 et 4).</summary>
        public void EnableCharacterTurntable(TurntableViewMode initialMode = TurntableViewMode.FullBody)
        {
            _panoramicActive = false;
            _turntableActive = true;
            _viewMode = initialMode;
            SetGameplayRigEnabled(false);
        }

        /// <summary>Rend le contrôle de la caméra au rig 3e personne en jeu.</summary>
        public void RestoreGameplayCamera()
        {
            _panoramicActive = false;
            _turntableActive = false;
            SetGameplayRigEnabled(true);
        }

        /// <summary>Bascule entre la vue Corps entier et le zoom Visage / Haut du corps.</summary>
        public void SetViewMode(TurntableViewMode mode)
        {
            _viewMode = mode;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Capture le début du glissement tactile sur la zone 3D.
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_turntableActive)
                return;

            float deltaYaw = -eventData.delta.x * _rotationSensitivity;
            CharacterCustomizer.Instance?.RotatePreview(deltaYaw);
        }

        private void LateUpdate()
        {
            if (!_panoramicActive && !_turntableActive)
                return;

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null)
                return;

            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;

            if (_panoramicActive)
            {
                _panoramicAngle += Time.unscaledDeltaTime * 4.5f;
                Vector3 center = local != null ? local.transform.position : new Vector3(23.7f, 1f, -1.1f);
                float rad = _panoramicAngle * Mathf.Deg2Rad;
                Vector3 camPos = center + new Vector3(Mathf.Cos(rad) * 14f, 6.5f, Mathf.Sin(rad) * 14f);
                Vector3 lookTarget = center + new Vector3(0f, 1.5f, 0f);
                cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(lookTarget - camPos, Vector3.up));
                return;
            }

            if (local == null)
                return;

            // Cadrage studio : personnage décalé sur la moitié gauche pour laisser la place au panneau de droite.
            float targetDist = _viewMode == TurntableViewMode.FaceZoom ? 1.35f : 2.65f;
            float targetHeight = _viewMode == TurntableViewMode.FaceZoom ? 1.56f : 1.02f;
            float targetLateral = _viewMode == TurntableViewMode.FaceZoom ? 0.32f : 0.62f;

            _currentDist = Mathf.SmoothDamp(_currentDist, targetDist, ref _distVel, _zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            _currentHeight = Mathf.SmoothDamp(_currentHeight, targetHeight, ref _heightVel, _zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            _currentLateralOffset = Mathf.SmoothDamp(_currentLateralOffset, targetLateral, ref _lateralVel, _zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

            Transform playerTr = local.transform;
            Vector3 forward = playerTr.forward;
            Vector3 right = playerTr.right;

            Vector3 focusPoint = playerTr.position + Vector3.up * _currentHeight + right * _currentLateralOffset;
            Vector3 studioCamPos = playerTr.position + Vector3.up * (_currentHeight + 0.08f) + forward * _currentDist + right * (_currentLateralOffset * 0.85f);

            cam.transform.SetPositionAndRotation(studioCamPos, Quaternion.LookRotation(focusPoint - studioCamPos, Vector3.up));
        }

        private void SetGameplayRigEnabled(bool enabled)
        {
            if (_gameplayRig == null)
            {
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                if (cam != null)
                    _gameplayRig = cam.GetComponent<ThirdPersonCameraRig>();
            }
            if (_gameplayRig != null)
                _gameplayRig.enabled = enabled;
        }
    }
}
