using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IvoryState.Editor
{
    /// <summary>
    /// Builds en un clic : serveur et client, pour le test local et la production.
    /// Les builds sont écrits dans IvoryState/Builds/ (ignoré par git).
    /// </summary>
    public static class IvoryBuildMenu
    {
        private const string Root = "Ivory State/Build/";
        private const string ServerDefine = "IVORY_SERVER";

        [MenuItem(Root + "Serveur + Client Windows (test local)", priority = -1)]
        public static void BuildWindowsBoth()
        {
            BuildWindowsTestServer();
            BuildWindowsTestClient();
        }

        [MenuItem(Root + "Serveur Windows (test local)", priority = 0)]
        public static void BuildWindowsTestServer()
        {
            // Build Windows standard + define IVORY_SERVER : contient le module serveur,
            // lancé en headless par -batchmode -nographics (voir run_server.bat).
            string folder = Path.Combine(BuildsRoot, "Server-Windows");
            bool ok = Build(new BuildPlayerOptions
            {
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                locationPathName = Path.Combine(folder, "IvoryStateServer.exe"),
                extraScriptingDefines = new[] { ServerDefine },
                options = BuildOptions.Development,
            });

            if (ok)
            {
                File.WriteAllText(Path.Combine(folder, "run_server.bat"),
                    "@echo off\r\n" +
                    "IvoryStateServer.exe -batchmode -nographics -server -port 7770 -maxPlayers 100 -logFile -\r\n");
            }
        }

        [MenuItem(Root + "Client Windows (test local)", priority = 1)]
        public static void BuildWindowsTestClient()
        {
            Build(new BuildPlayerOptions
            {
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                locationPathName = Path.Combine(BuildsRoot, "Client-Windows", "IvoryState.exe"),
                options = BuildOptions.Development,
            });
        }

        [MenuItem(Root + "Serveur Linux dédié (production)", priority = 20)]
        public static void BuildLinuxDedicatedServer()
        {
            if (!RequireModule(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64,
                    "Linux Dedicated Server Build Support"))
                return;

            // Subtarget Server : headless natif, définit UNITY_SERVER, retire rendu et audio.
            Build(new BuildPlayerOptions
            {
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                locationPathName = Path.Combine(BuildsRoot, "Server-Linux", "IvoryStateServer.x86_64"),
                options = BuildOptions.None,
            });
        }

        [MenuItem(Root + "Client Android (development)", priority = 21)]
        public static void BuildAndroidDevClient()
        {
            if (!RequireModule(BuildTargetGroup.Android, BuildTarget.Android, "Android Build Support"))
                return;

            Build(new BuildPlayerOptions
            {
                target = BuildTarget.Android,
                locationPathName = Path.Combine(BuildsRoot, "Android", "IvoryState-dev.apk"),
                options = BuildOptions.Development,
            });
        }

        private static string BuildsRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));

        private static bool RequireModule(BuildTargetGroup group, BuildTarget target, string moduleName)
        {
            if (BuildPipeline.IsBuildTargetSupported(group, target))
                return true;

            EditorUtility.DisplayDialog("Module manquant",
                "Installe le module « " + moduleName + " » pour cette version d'Unity via Unity Hub " +
                "(Installs > ⚙ > Add modules).", "OK");
            return false;
        }

        private static bool Build(BuildPlayerOptions options)
        {
            options.scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (options.scenes.Length == 0)
            {
                Debug.LogError("[Build] Aucune scène dans les Build Settings. Lance d'abord Ivory State/Setup.");
                return false;
            }

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[Build] OK : " + options.locationPathName +
                          " (" + (summary.totalSize / (1024f * 1024f)).ToString("0.0") + " Mo, " +
                          summary.totalTime.TotalSeconds.ToString("0") + " s)");
                return true;
            }

            Debug.LogError("[Build] Échec (" + summary.result + ") : " + options.locationPathName);
            return false;
        }
    }
}
