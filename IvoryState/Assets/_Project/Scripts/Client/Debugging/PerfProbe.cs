using System;
using System.Globalization;
using IvoryState.Core.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Mesure et réglages de performance pilotés par la ligne de commande, pour tester sur un vrai
    /// téléphone sans recompiler. Sur Android, les arguments passent par l'intent :
    /// <c>adb shell am start -n com.ivorystate.game/com.unity3d.player.UnityPlayerGameActivity -e unity "-perfLog -renderScale 0.6"</c>
    /// <list type="bullet">
    /// <item><c>-perfLog</c> : moyenne d'images par seconde toutes les 5 s dans le journal ([Perf]).</item>
    /// <item><c>-renderScale x</c> : résolution de rendu (0,5 à 1).</item>
    /// <item><c>-noPost</c> : sans post-traitement. <c>-noShadows</c> : sans ombres.</item>
    /// <item><c>-fps n</c> : fréquence d'images visée.</item>
    /// </list>
    /// Le nombre de passants se règle avec <c>-pedestrians n</c> (lu par AmbientPedestrians).
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        private const float Window = 5f;

        private int _frames;
        private float _windowStart;
        private float _worstFrame;
        private static int s_targetFps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode)
                return;

            string[] args = Environment.GetCommandLineArgs();
            bool any = false;

            if (TryGetFloat(args, "-renderScale", out float scale) &&
                GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = Mathf.Clamp(scale, 0.3f, 1f);
                any = true;
            }

            if (Array.IndexOf(args, "-noShadows") >= 0 &&
                GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpShadows)
            {
                urpShadows.shadowDistance = 0f;
                any = true;
            }

            if (Array.IndexOf(args, "-noPost") >= 0)
            {
                foreach (var data in FindObjectsByType<UniversalAdditionalCameraData>(FindObjectsSortMode.None))
                    data.renderPostProcessing = false;
                any = true;
            }

            // -hide Relief,Foret,Vendeur : masque tous les objets de ces noms (diagnostic : quelle famille
            // d'objets coûte le plus sur téléphone). Noms des objets créés par le générateur de ville.
            int hide = Array.IndexOf(args, "-hide");
            if (hide >= 0 && hide + 1 < args.Length)
            {
                var names = new System.Collections.Generic.HashSet<string>(args[hide + 1].Split(','));
                int hidden = 0;
                foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    bool match = names.Contains(t.name);
                    foreach (string prefix in names)
                    {
                        if (prefix.EndsWith("*") && t.name.StartsWith(prefix.TrimEnd('*'), StringComparison.Ordinal))
                            match = true;
                    }
                    if (match && t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        hidden++;
                    }
                }
                GameLog.Info("Perf", hidden + " objets masqués (" + args[hide + 1] + ").");
                any = true;
            }

            if (TryGetFloat(args, "-hour", out float hour))
            {
                Client.World.DayNightCycle.ForcedHour = Mathf.Repeat(hour, 24f);
                any = true;
            }

            if (TryGetFloat(args, "-fps", out float fps))
            {
                s_targetFps = Mathf.RoundToInt(fps); // appliqué dans Update : NetworkBootstrap le règle aussi au démarrage
                any = true;
            }

            if (Array.IndexOf(args, "-perfLog") >= 0 || any)
            {
                var go = new GameObject("PerfProbe");
                DontDestroyOnLoad(go);
                go.AddComponent<PerfProbe>();
                GameLog.Info("Perf", "Sonde active : " + string.Join(" ", args));
            }
        }

        public static bool TryGetFloat(string[] args, string name, out float value)
        {
            value = 0f;
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length &&
                   float.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void Update()
        {
            if (s_targetFps > 0 && Application.targetFrameRate != s_targetFps)
                Application.targetFrameRate = s_targetFps;
            _frames++;
            _worstFrame = Mathf.Max(_worstFrame, Time.unscaledDeltaTime);
            float elapsed = Time.unscaledTime - _windowStart;
            if (elapsed < Window)
                return;

            GameLog.Info("Perf", "fps=" + (_frames / elapsed).ToString("0.0", CultureInfo.InvariantCulture) +
                                 " pire_ms=" + (_worstFrame * 1000f).ToString("0", CultureInfo.InvariantCulture) +
                                 " echelle=" + QualitySettings.resolutionScalingFixedDPIFactor.ToString("0.00", CultureInfo.InvariantCulture) +
                                 " ecran=" + Screen.width + "x" + Screen.height +
                                 " heure=" + Client.World.DayNightCycle.CurrentHour.ToString("0.0", CultureInfo.InvariantCulture) +
                                 " nuit=" + Client.World.DayNightCycle.NightFactor.ToString("0.00", CultureInfo.InvariantCulture));
            _frames = 0;
            _worstFrame = 0f;
            _windowStart = Time.unscaledTime;
        }
    }
}
