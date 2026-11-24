using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace IvoryState.Editor
{
    /// <summary>
    /// Réglages Android du projet, applicables en un clic ou en ligne de commande :
    /// <c>-buildTarget Android -executeMethod IvoryState.Editor.AndroidSetup.ConfigureAndBuild</c>.
    /// <list type="bullet">
    /// <item>JDK : OpenJDK 17 extrait dans le profil utilisateur (pas de droits administrateur nécessaires).</item>
    /// <item>IL2CPP en ARM64 : obligatoire pour le Play Store et plus rapide à l'exécution.</item>
    /// <item>Android 8.0 minimum (API 26) : couvre l'essentiel du parc de téléphones en Côte d'Ivoire.</item>
    /// <item>Paysage uniquement : joystick à gauche, caméra et actions à droite.</item>
    /// </list>
    /// </summary>
    public static class AndroidSetup
    {
        public const string ApplicationId = "com.ivorystate.game";

        private static readonly string UserJdkPath =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Unity\\OpenJDK17";

        [MenuItem("Ivory State/Android/Appliquer les réglages Android", priority = 30)]
        public static void Configure()
        {
            ConfigureJdk();

            PlayerSettings.companyName = "Ivory State";
            PlayerSettings.productName = "Ivory State";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Nakama tourne pour l'instant en HTTP simple sur le réseau local : sans cela, Android
            // refuse la requête (« Insecure connection not allowed »). À repasser en HTTPS avec le
            // serveur en ligne.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            AssetDatabase.SaveAssets();
            Debug.Log("[Android] Réglages appliqués : " + ApplicationId + ", IL2CPP ARM64, API 26+, paysage.");
        }

        /// <summary>
        /// Réglages puis APK de développement (Builds/Android/IvoryState-dev.apk).
        /// <para>
        /// IMPORTANT : le projet doit déjà être sur Android au lancement de Unity
        /// (<c>-buildTarget Android</c> en ligne de commande, ou File › Build Profiles). Si Unity bascule
        /// de Windows à Android dans la même session, le tri des variantes de shader d'URP se fait avec
        /// les mauvaises informations et élimine toutes les variantes d'affichage : APK à écran noir
        /// (constaté deux fois le 2026-11-16). Le build est donc refusé dans ce cas.
        /// </para>
        /// </summary>
        public static void ConfigureAndBuild()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                const string message = "[Android] Build refusé : le projet est sur " + "{0}" +
                                       ". Relancer Unity avec -buildTarget Android (sinon l'APK affiche un écran noir).";
                Debug.LogError(string.Format(message, EditorUserBuildSettings.activeBuildTarget));
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                return;
            }

            Configure();
            IvoryBuildMenu.BuildAndroidDevClient();
        }

        /// <summary>
        /// Indique à Unity le JDK à utiliser. L'API publique (AndroidExternalToolsSettings) vit dans
        /// l'extension Android de l'éditeur : on y accède par réflexion pour compiler même sans le module.
        /// </summary>
        private static void ConfigureJdk()
        {
            if (!System.IO.Directory.Exists(UserJdkPath))
            {
                Debug.LogWarning("[Android] JDK introuvable dans " + UserJdkPath + " : JDK intégré de Unity utilisé.");
                return;
            }

            Type settings = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
            PropertyInfo jdkRoot = settings?.GetProperty("jdkRootPath", BindingFlags.Public | BindingFlags.Static);
            if (jdkRoot != null && jdkRoot.CanWrite)
            {
                jdkRoot.SetValue(null, UserJdkPath);
            }
            else
            {
                EditorPrefs.SetBool("JdkUseEmbedded", false);
                EditorPrefs.SetString("JdkPath", UserJdkPath);
            }

            Debug.Log("[Android] JDK : " + UserJdkPath);
        }
    }

#if UNITY_ANDROID
    /// <summary>
    /// Redirige les dossiers <c>build/</c> de Gradle hors du répertoire OneDrive (<c>%LOCALAPPDATA%/Unity/IvoryGradleBuild</c>)
    /// afin d'éviter les verrous <c>AccessDeniedException</c> / <c>Unable to delete directory</c> d'OneDrive pendant
    /// l'empaquetage de l'APK, tout en recopiant le résultat vers <c>launcher/build/outputs/apk</c> attendu par Unity.
    /// </summary>
    internal sealed class AndroidGradleBuildDirFix : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        private const string Marker = "// IvoryState: local buildDir outside OneDrive";

        public int callbackOrder => 999;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string gradleRoot = System.IO.Directory.GetParent(unityLibraryPath)?.FullName;
            if (string.IsNullOrEmpty(gradleRoot))
                return;

            string rootGradle = System.IO.Path.Combine(gradleRoot, "build.gradle");
            if (System.IO.File.Exists(rootGradle))
            {
                string content = System.IO.File.ReadAllText(rootGradle);
                if (!content.Contains(Marker))
                {
                    const string subprojectsBlock =
                        Marker + "\n" +
                        "subprojects {\n" +
                        "    def localBase = System.getenv(\"LOCALAPPDATA\") ?: System.getProperty(\"java.io.tmpdir\")\n" +
                        "    layout.buildDirectory.set(file(\"${localBase}/Unity/IvoryGradleBuild/${project.name}\"))\n" +
                        "}\n\n";

                    int cleanIdx = content.IndexOf("tasks.register('clean'", StringComparison.Ordinal);
                    content = cleanIdx >= 0
                        ? content.Insert(cleanIdx, subprojectsBlock)
                        : content + "\n" + subprojectsBlock;
                    System.IO.File.WriteAllText(rootGradle, content);
                }
            }

            string launcherGradle = System.IO.Path.Combine(gradleRoot, "launcher", "build.gradle");
            if (System.IO.File.Exists(launcherGradle))
            {
                string content = System.IO.File.ReadAllText(launcherGradle);
                if (!content.Contains(Marker))
                {
                    const string copyHook =
                        "\n" + Marker + "\n" +
                        "tasks.configureEach { t ->\n" +
                        "    if (t.name == 'assembleDebug' || t.name == 'assembleRelease') {\n" +
                        "        t.doLast {\n" +
                        "            def variant = t.name == 'assembleDebug' ? 'debug' : 'release'\n" +
                        "            def srcDir = layout.buildDirectory.dir(\"outputs/apk/${variant}\").get().asFile\n" +
                        "            def dstDir = file(\"${projectDir}/build/outputs/apk/${variant}\")\n" +
                        "            if (srcDir.exists()) {\n" +
                        "                dstDir.mkdirs()\n" +
                        "                copy {\n" +
                        "                    from srcDir\n" +
                        "                    into dstDir\n" +
                        "                }\n" +
                        "            }\n" +
                        "        }\n" +
                        "    }\n" +
                        "}\n";
                    System.IO.File.WriteAllText(launcherGradle, content + copyHook);
                }
            }

            Debug.Log("[Android] Répertoire Gradle intermédiaire redirigé hors OneDrive (%LOCALAPPDATA%/Unity/IvoryGradleBuild).");
        }
    }
#endif
}
