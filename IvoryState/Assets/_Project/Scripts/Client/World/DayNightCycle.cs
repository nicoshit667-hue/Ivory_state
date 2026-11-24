using IvoryState.Core.Networking;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Client.World
{
    /// <summary>
    /// Cycle jour/nuit : soleil (ou lune), ciel, lumière ambiante, brume, reflets et lumières de la ville
    /// suivent l'heure du jeu.
    /// <para>
    /// <b>Synchronisé sans réseau :</b> l'heure est calculée à partir du tick FishNet, que chaque client
    /// connaît déjà, aligné sur celui du serveur. Tous les joueurs voient donc la même heure, sans aucun
    /// message supplémentaire. Hors connexion (menu), l'heure avance localement.
    /// </para>
    /// <para>
    /// Rendu : l'heure dorée (soleil rasant orangé, brume chaude) est le moment fort visé par la
    /// direction artistique. La nuit, la variable globale de shader <c>_IvoryNight</c> allume les
    /// fenêtres, les boutiques, les lampadaires et leurs halos (aucune vraie lumière : trop coûteux
    /// sur mobile).
    /// </para>
    /// Une journée complète dure <see cref="_dayLengthMinutes"/> minutes réelles (24 par défaut :
    /// une heure de jeu par minute). Le serveur démarre en fin d'après-midi.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightCycle : MonoBehaviour
    {
        [SerializeField] private Light _sun;
        [SerializeField, Range(2f, 120f)] private float _dayLengthMinutes = 24f;
        [SerializeField, Range(0f, 24f)] private float _startHour = 16.5f;

        private static readonly int NightId = Shader.PropertyToID("_IvoryNight");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        // Palette : plein jour, heure dorée, nuit.
        private static readonly Color SunNoon = new Color(1f, 0.95f, 0.88f);
        private static readonly Color SunGolden = new Color(1f, 0.56f, 0.28f);
        private static readonly Color Moon = new Color(0.55f, 0.65f, 1f);
        private static readonly Color SkyDay = new Color(0.78f, 0.86f, 1.0f);
        private static readonly Color SkyGolden = new Color(0.86f, 0.70f, 0.62f);
        private static readonly Color SkyNight = new Color(0.30f, 0.35f, 0.50f);
        private static readonly Color EquatorDay = new Color(0.80f, 0.76f, 0.70f);
        private static readonly Color EquatorGolden = new Color(0.92f, 0.64f, 0.44f);
        // La nuit, l'éclairage public et les enseignes renvoient une lueur orangée sur les façades.
        private static readonly Color EquatorNight = new Color(0.42f, 0.33f, 0.27f);
        private static readonly Color GroundDay = new Color(0.45f, 0.39f, 0.32f);
        private static readonly Color GroundNight = new Color(0.20f, 0.15f, 0.11f);

        // Le soleil se lève à l'est et se couche à l'ouest (un peu au sud) : en fin de journée, les rues
        // nord-sud ont une façade dorée et une façade à l'ombre.
        private const float SunYaw = -100f;
        private static readonly Color FogDay = new Color(0.74f, 0.79f, 0.85f);
        private static readonly Color FogGolden = new Color(0.86f, 0.63f, 0.43f);
        private static readonly Color FogNight = new Color(0.07f, 0.08f, 0.13f);

        /// <summary>Heure courante du jeu (0 à 24).</summary>
        public static float CurrentHour { get; private set; } = 16.5f;

        /// <summary>Diagnostic : heure figée (option -hour de PerfProbe). Négative = cycle normal.</summary>
        public static float ForcedHour { get; set; } = -1f;

        /// <summary>0 en plein jour, 1 en pleine nuit (lumières de la ville).</summary>
        public static float NightFactor { get; private set; }

        private static readonly char[] s_clock = new char[5];
        private static int s_lastClockMinute = -1;
        private static string s_clockText = "16:30";

        private Material _skyInstance;

        /// <summary>« 14:05 ». Reconstruit une fois par minute de jeu seulement.</summary>
        public static string ClockText
        {
            get
            {
                int totalMinutes = (int)(CurrentHour * 60f);
                if (totalMinutes == s_lastClockMinute)
                    return s_clockText;

                s_lastClockMinute = totalMinutes;
                int hours = totalMinutes / 60 % 24, minutes = totalMinutes % 60;
                s_clock[0] = (char)('0' + hours / 10);
                s_clock[1] = (char)('0' + hours % 10);
                s_clock[2] = ':';
                s_clock[3] = (char)('0' + minutes / 10);
                s_clock[4] = (char)('0' + minutes % 10);
                s_clockText = new string(s_clock);
                return s_clockText;
            }
        }

        private void Start()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Copie du ciel : on ne modifie jamais l'asset partagé (l'éditeur le sauvegarderait).
            if (RenderSettings.skybox != null)
            {
                _skyInstance = new Material(RenderSettings.skybox);
                RenderSettings.skybox = _skyInstance;
            }
        }

        private void OnDestroy()
        {
            if (_skyInstance != null)
                Destroy(_skyInstance);
        }

        private void Update()
        {
            CurrentHour = ForcedHour >= 0f ? ForcedHour : ComputeHour();
            Apply(_sun, CurrentHour, _skyInstance);
        }

        /// <summary>
        /// Applique l'éclairage d'une heure donnée. Statique : utilisé aussi par l'éditeur pour les
        /// captures d'écran (sans mode Play). Aucune allocation.
        /// </summary>
        public static void Apply(Light sun, float hour, Material skyMaterial)
        {
            // Élévation du soleil : lever à 6 h, zénith à 12 h, coucher à 18 h.
            float elevation = (hour - 6f) / 12f * 180f;
            float sinE = Mathf.Sin(elevation * Mathf.Deg2Rad);
            float daylight = Mathf.Clamp01(sinE * 2.5f);
            // Heure dorée : soleil à moins de ~20° au-dessus de l'horizon.
            float golden = sinE > -0.05f ? Mathf.Clamp01(1f - Mathf.Abs(sinE) * 3f) : 0f;
            float night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.10f, sinE));
            NightFactor = night;

            if (sun != null)
            {
                if (sinE > -0.02f)
                {
                    sun.transform.rotation = Quaternion.Euler(Mathf.Clamp(elevation, 2f, 178f), SunYaw, 0f);
                    sun.color = Color.Lerp(SunNoon, SunGolden, golden);
                    sun.intensity = Mathf.Lerp(0.15f, 1.9f, daylight) * Mathf.Lerp(1f, 1.15f, golden);
                    sun.shadowStrength = 0.9f;
                }
                else
                {
                    // Nuit : lune haute et froide, ombres douces.
                    sun.transform.rotation = Quaternion.Euler(55f, 150f, 0f);
                    sun.color = Moon;
                    sun.intensity = 0.38f;
                    sun.shadowStrength = 0.5f;
                }
            }

            Color sky = Color.Lerp(Color.Lerp(SkyNight, SkyDay, daylight), SkyGolden, golden * 0.6f);
            Color equator = Color.Lerp(Color.Lerp(EquatorNight, EquatorDay, daylight), EquatorGolden, golden * 0.7f);
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = Color.Lerp(GroundNight, GroundDay, daylight);
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(FogNight, FogDay, daylight), FogGolden, golden * 0.75f);
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(daylight + golden * 0.5f));

            if (skyMaterial != null)
                skyMaterial.SetFloat(ExposureId, Mathf.Lerp(0.12f, 1.25f, Mathf.Clamp01(sinE * 4f + 0.3f)));

            Shader.SetGlobalFloat(NightId, night);
        }

        private float ComputeHour()
        {
            double seconds;
            NetworkBootstrap bootstrap = NetworkBootstrap.Instance;
            if (bootstrap != null && (bootstrap.IsClientRunning || bootstrap.IsServerRunning))
            {
                var timeManager = bootstrap.NetworkManager.TimeManager;
                seconds = timeManager.Tick * timeManager.TickDelta;
            }
            else
            {
                seconds = Time.time;
            }

            double hoursElapsed = seconds / (_dayLengthMinutes * 60.0) * 24.0;
            return (float)((_startHour + hoursElapsed) % 24.0);
        }
    }
}
