using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Boussole en haut de l'écran : bandeau gradué (N, NE, E, SE, S, SO, O, NO) qui défile avec le
    /// regard de la caméra. Les 8 libellés sont créés une fois et seulement déplacés ensuite.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompassHud : MonoBehaviour
    {
        private static readonly string[] Labels = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };

        [SerializeField] private RectTransform _strip;
        [SerializeField] private Text _labelTemplate;
        [SerializeField] private float _visibleDegrees = 150f;

        private Text[] _labels;
        private Transform _camera;

        private void Awake()
        {
            _labels = new Text[Labels.Length];
            for (int i = 0; i < Labels.Length; i++)
            {
                Text label = i == 0 ? _labelTemplate : Instantiate(_labelTemplate, _strip);
                label.text = Labels[i];
                // Points cardinaux plus visibles, nord en couleur.
                label.fontSize = i % 2 == 0 ? 30 : 22;
                label.color = i == 0 ? new Color(1f, 0.45f, 0.3f) : Color.white;
                _labels[i] = label;
            }
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                UnityEngine.Camera main = UnityEngine.Camera.main;
                if (main == null)
                    return;
                _camera = main.transform;
            }

            Vector3 forward = _camera.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pixelsPerDegree = _strip.rect.width / _visibleDegrees;
            float half = _visibleDegrees * 0.5f;

            for (int i = 0; i < _labels.Length; i++)
            {
                float delta = Mathf.DeltaAngle(yaw, i * 45f);
                bool show = Mathf.Abs(delta) < half;
                Text label = _labels[i];
                if (label.enabled != show)
                    label.enabled = show;
                if (!show)
                    continue;
                label.rectTransform.anchoredPosition = new Vector2(delta * pixelsPerDegree, 0f);
                // Fondu sur les bords du bandeau.
                Color c = label.color;
                c.a = 1f - Mathf.Clamp01((Mathf.Abs(delta) - half * 0.6f) / (half * 0.4f));
                label.color = c;
            }
        }
    }
}
