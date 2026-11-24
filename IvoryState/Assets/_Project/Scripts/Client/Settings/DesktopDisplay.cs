using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IvoryState.Client.Settings
{
    /// <summary>
    /// Plein écran sur PC (client Windows) : le jeu démarre en plein écran (fenêtré sans bordure, à la
    /// résolution de l'écran) et F11 bascule entre plein écran et fenêtre 1280 × 720. Le choix est gardé
    /// d'une partie à l'autre. Alt+Entrée (géré par Unity) marche aussi.
    /// <para>
    /// Sans effet dans l'éditeur, sur téléphone, en batchmode (serveur, bots) et quand la ligne de commande
    /// impose déjà un mode (<c>-screen-fullscreen</c>, <c>-window-mode</c>, <c>-popupwindow</c> : tests locaux
    /// à plusieurs fenêtres).
    /// </para>
    /// </summary>
    public sealed class DesktopDisplay : MonoBehaviour
    {
        private const string PrefKey = "display.fullscreen";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor || Application.isBatchMode || Application.isMobilePlatform)
                return;
            var go = new GameObject("DesktopDisplay");
            DontDestroyOnLoad(go);
            go.AddComponent<DesktopDisplay>();
            if (!CommandLineSetsMode())
                Apply(PlayerPrefs.GetInt(PrefKey, 1) == 1);
        }

        private static bool CommandLineSetsMode()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg == "-screen-fullscreen" || arg == "-window-mode" || arg == "-popupwindow")
                    return true;
            }
            return false;
        }

        private static void Apply(bool fullscreen)
        {
            if (fullscreen)
            {
                Resolution native = Screen.currentResolution;
                Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
            }
            else
            {
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f11Key.wasPressedThisFrame)
                return;
            bool fullscreen = Screen.fullScreenMode == FullScreenMode.Windowed;
            PlayerPrefs.SetInt(PrefKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
            Apply(fullscreen);
        }
    }
}
