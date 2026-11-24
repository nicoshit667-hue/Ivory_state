using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IvoryState.Editor.Debugging
{
    /// <summary>
    /// Régénération à distance dans l'éditeur ouvert : quand le fichier Builds/regenerate.request apparaît
    /// (écrit par un outil ou par Claude), l'éditeur recharge les scripts, puis lance la configuration
    /// complète (SetupAll) et, si le fichier contient « snapshots », les photos de contrôle. Le résultat est
    /// écrit dans Builds/regenerate.done. Évite de devoir fermer Unity pour regénérer la ville.
    /// Rien ne se passe en mode Play, pendant une compilation ou en mode batch.
    /// </summary>
    [InitializeOnLoad]
    internal static class RegenerationWatcher
    {
        private static double _nextPoll;

        static RegenerationWatcher()
        {
            if (!Application.isBatchMode)
                EditorApplication.update += Poll;
        }

        private static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds");

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + 2.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            string request = Path.Combine(Folder, "regenerate.request");
            if (!File.Exists(request))
                return;

            // D'abord les scripts : si un recompile démarre, on revient après le rechargement du domaine.
            AssetDatabase.Refresh();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            string what = File.ReadAllText(request).Trim().ToLowerInvariant();
            File.Delete(request);
            string result;
            try
            {
                if (!what.Contains("nosetup"))
                    IvoryProjectSetup.SetupAll();
                if (what.Contains("snapshots"))
                    InteriorSnapshots.Capture();
                if (what.Contains("buildserver"))
                    IvoryBuildMenu.BuildWindowsTestServer();
                if (what.Contains("play"))
                    EditorApplication.isPlaying = true;
                result = "ok " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception exception)
            {
                result = "erreur " + exception;
                Debug.LogException(exception);
            }
            File.WriteAllText(Path.Combine(Folder, "regenerate.done"), result);
            Debug.Log("[Regeneration] " + result);
        }
    }
}
