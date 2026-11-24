#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using IvoryState.Core.Diagnostics;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Capture d'écran automatique en jeu (builds de dev), pour la vérification visuelle sans intervention :
    /// <c>-screenshot chemin.png [-screenshotDelay 25] [-screenshotQuit]</c>.
    /// Combiné à <c>-bot</c>, il montre le jeu tel qu'un joueur le voit : HUD, ville, passants, éclairage.
    /// </summary>
    internal sealed class DevScreenshot : MonoBehaviour
    {
        private string _path;
        private float _delay = 25f;
        private bool _quit;
        private int _frameAfterCapture = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-screenshot");
            if (index < 0 || index + 1 >= args.Length)
                return;

            var go = new GameObject(nameof(DevScreenshot));
            DontDestroyOnLoad(go);
            var shot = go.AddComponent<DevScreenshot>();
            shot._path = args[index + 1];
            shot._quit = Array.IndexOf(args, "-screenshotQuit") >= 0;

            int delayIndex = Array.IndexOf(args, "-screenshotDelay");
            if (delayIndex >= 0 && delayIndex + 1 < args.Length &&
                float.TryParse(args[delayIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float delay))
                shot._delay = delay;
        }

        private void Update()
        {
            if (_frameAfterCapture < 0 && Time.realtimeSinceStartup >= _delay)
            {
                ScreenCapture.CaptureScreenshot(_path);
                GameLog.Info("Screenshot", "Capture : " + _path);
                _frameAfterCapture = Time.frameCount + 10; // l'écriture se fait en fin de frame
            }
            else if (_quit && _frameAfterCapture > 0 && Time.frameCount >= _frameAfterCapture)
            {
                Application.Quit();
            }
        }
    }
}
#endif
