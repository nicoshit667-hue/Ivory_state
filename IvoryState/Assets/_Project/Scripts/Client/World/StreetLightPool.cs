using UnityEngine;

namespace IvoryState.Client.World
{
    /// <summary>
    /// Éclairage public réel autour du joueur, la nuit.
    /// <para>
    /// La ville compte plus de 700 lampadaires : en faire de vraies lumières serait ruineux sur mobile.
    /// On garde donc leurs ampoules et halos « peints » (shader NightLight), et un petit groupe fixe de
    /// lumières ponctuelles (8 par défaut) se pose sur les lampadaires les plus proches de la caméra :
    /// façades, personnages et voitures sont alors réellement éclairés là où le joueur regarde.
    /// </para>
    /// <para>
    /// Coût : un tri partiel toutes les 0,25 s (aucune allocation), lumières sans ombres, éteintes
    /// le jour. Rien sur un serveur headless.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreetLightPool : MonoBehaviour
    {
        private const float RefreshInterval = 0.25f;

        [SerializeField] private CityLamps _lamps;
        [SerializeField, Range(0, 16)] private int _lightCount = 8;
        [SerializeField] private float _range = 16f;
        [SerializeField] private float _intensity = 3.2f;
        [SerializeField] private Color _ledColor = new Color(1f, 0.88f, 0.72f);
        [SerializeField] private Color _sodiumColor = new Color(1f, 0.62f, 0.30f);

        private Light[] _lights;
        private int[] _nearest;
        private float[] _nearestDistance;
        private Transform _camera;
        private float _nextRefresh;

        private void Start()
        {
            if (Application.isBatchMode || _lamps == null || _lightCount == 0)
            {
                enabled = false;
                return;
            }

            _lights = new Light[_lightCount];
            _nearest = new int[_lightCount];
            _nearestDistance = new float[_lightCount];
            for (int i = 0; i < _lightCount; i++)
            {
                var go = new GameObject("StreetLight_" + i);
                go.transform.SetParent(transform, false);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = _range;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.enabled = false;
                _lights[i] = light;
            }
        }

        private void Update()
        {
            float night = DayNightCycle.NightFactor;
            if (night < 0.02f)
            {
                for (int i = 0; i < _lights.Length; i++)
                    _lights[i].enabled = false;
                return;
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }

            float intensity = _intensity * night;
            for (int i = 0; i < _lights.Length; i++)
                _lights[i].intensity = intensity;
        }

        /// <summary>Sélectionne les lampadaires les plus proches de la caméra (tri par insertion borné).</summary>
        private void Refresh()
        {
            if (_camera == null)
            {
                Camera main = Camera.main;
                if (main == null)
                    return;
                _camera = main.transform;
            }

            Vector3 eye = _camera.position;
            int found = 0;
            Vector4[] lamps = _lamps.Lamps;
            for (int i = 0; i < lamps.Length; i++)
            {
                float dx = lamps[i].x - eye.x, dz = lamps[i].z - eye.z;
                float d = dx * dx + dz * dz;
                if (found == _nearest.Length && d >= _nearestDistance[found - 1])
                    continue;

                int slot = found < _nearest.Length ? found++ : found - 1;
                while (slot > 0 && _nearestDistance[slot - 1] > d)
                {
                    _nearest[slot] = _nearest[slot - 1];
                    _nearestDistance[slot] = _nearestDistance[slot - 1];
                    slot--;
                }
                _nearest[slot] = i;
                _nearestDistance[slot] = d;
            }

            for (int i = 0; i < _lights.Length; i++)
            {
                Light light = _lights[i];
                if (i >= found)
                {
                    light.enabled = false;
                    continue;
                }

                Vector4 lamp = lamps[_nearest[i]];
                // Un peu sous l'ampoule : la lumière tombe sur la rue et le bas des façades.
                light.transform.position = new Vector3(lamp.x, lamp.y - 0.6f, lamp.z);
                light.color = lamp.w > 0.5f ? _sodiumColor : _ledColor;
                light.enabled = true;
            }
        }
    }
}
