using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.Input
{
    /// <summary>
    /// Affichage du joystick (Image 2) : grand cercle translucide à bordure blanche en bas à gauche,
    /// visible au repos à pied et masqué lorsqu'on est en véhicule.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VirtualJoystickView : MonoBehaviour
    {
        [SerializeField] private MobilePlayerControls _controls;
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _base;
        [SerializeField] private RectTransform _knob;

        [Tooltip("Taille du bouton mobile par rapport au socle.")]
        [SerializeField, Range(0.2f, 0.8f)] private float _knobScale = 0.42f;

        private bool _visible = true;
        private float _lastRadius = -1f;

        private void Start()
        {
            RestyleGlass();
            SetVisible(false);
        }

        private void RestyleGlass()
        {
            if (_base != null && _base.TryGetComponent(out Image baseImg))
                baseImg.color = new Color(1f, 1f, 1f, 0.72f);
            Transform inner = _base != null ? _base.Find("Fond") : null;
            if (inner != null && inner.TryGetComponent(out Image innerImg))
                innerImg.color = new Color(0.82f, 0.84f, 0.88f, 0.25f);

            if (_knob != null && _knob.TryGetComponent(out Image knobImg))
                knobImg.color = new Color(1f, 1f, 1f, 0.82f);
            Transform core = _knob != null ? _knob.Find("Coeur") : null;
            if (core != null && core.TryGetComponent(out Image coreImg))
            {
                coreImg.color = new Color(0.88f, 0.90f, 0.94f, 0.45f);
                ((RectTransform)core).sizeDelta = new Vector2(54f, 54f);
            }
        }

        private void LateUpdate()
        {
            if (_controls == null)
                return;

            bool inVehicle = PlayerDriving.Local != null && PlayerDriving.Local.IsInVehicle;
            bool shouldShow = PlayerNetworkIdentity.Local != null && !inVehicle;
            if (shouldShow != _visible)
                SetVisible(shouldShow);

            if (!shouldShow)
                return;

            float scaleFactor = _canvas != null && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
            float radius = Mathf.Max(110f, _controls.JoystickRadiusPixels / scaleFactor * 1.35f);
            if (!Mathf.Approximately(radius, _lastRadius))
            {
                _lastRadius = radius;
                _base.sizeDelta = new Vector2(radius * 2f, radius * 2f);
                _knob.sizeDelta = _base.sizeDelta * _knobScale;
            }

            _base.position = _controls.JoystickOrigin;
            _knob.position = _controls.JoystickKnob;
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_base != null)
                _base.gameObject.SetActive(visible);
            if (_knob != null)
                _knob.gameObject.SetActive(visible);
        }
    }
}
