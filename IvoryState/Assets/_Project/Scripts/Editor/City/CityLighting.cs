using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Éclairage et rendu « cinéma » de la ville, tenable sur mobile :
    /// ciel procédural, lumière ambiante à trois couleurs, brume chaude, post-traitement (tonemapping
    /// ACES, bloom au quart de résolution, étalonnage chaud, vignette), ombres douces.
    /// <para>
    /// Le reflet du ciel (vitres, eau) vient d'une génération de l'éclairage d'environnement, sans
    /// lightmaps : rien de lourd à embarquer.
    /// </para>
    /// </summary>
    internal static class CityLighting
    {
        private const string SkyPath = "Assets/_Project/Art/Materials/M_Sky.mat";
        private const string ProfilePath = "Assets/_Project/Settings/Rendering/CityVolumeProfile.asset";
        private const string LightingSettingsPath = "Assets/_Project/Settings/Rendering/CityLighting.lighting";
        private const string MobilePipelinePath = "Assets/Settings/Mobile_RPAsset.asset";

        /// <summary>Heure utilisée pour l'éclairage de la scène dans l'éditeur (fin d'après-midi).</summary>
        public const float EditorHour = 16.9f;

        /// <summary>Configure le ciel, l'ambiance, la brume, le volume de post-traitement et la caméra.</summary>
        public static void Setup(Light sun, Camera camera)
        {
            EnsureFolder("Assets/_Project/Settings/Rendering");

            // --- Ciel ---
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, SkyPath);
            }
            sky.SetFloat("_SunDisk", 2f);            // disque de soleil de haute qualité
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 1.25f); // ciel plus chargé : couchers de soleil plus orangés
            sky.SetColor("_SkyTint", new Color(0.52f, 0.54f, 0.58f));
            sky.SetColor("_GroundColor", new Color(0.42f, 0.38f, 0.34f));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 128;

            // Brume : perspective atmosphérique (la ville lointaine se fond dans l'air chaud et humide).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 420f;

            // --- Soleil ---
            sun.shadows = LightShadows.Soft;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            Client.World.DayNightCycle.Apply(sun, EditorHour, sky);

            // --- Caméra ---
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            cameraData.renderShadows = true;
            camera.farClipPlane = 450f;

            // --- Post-traitement ---
            var volumeGo = new GameObject("PostProcess");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0;
            volume.sharedProfile = BuildProfile();

            ConfigureMobilePipeline();
        }

        private static VolumeProfile BuildProfile()
        {
            AssetDatabase.DeleteAsset(ProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.68f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.8f));
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(5);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.35f);
            color.contrast.Override(14f);
            color.saturation.Override(8f);

            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(9f);
            white.tint.Override(2f);

            // Ombres légèrement bleutées, hautes lumières chaudes : le contraste « cinéma ».
            var split = profile.Add<SplitToning>(true);
            split.shadows.Override(new Color(0.42f, 0.5f, 0.56f));
            split.highlights.Override(new Color(0.62f, 0.52f, 0.42f));
            split.balance.Override(-10f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.45f);

            foreach (VolumeComponent component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Ombres un peu plus lointaines sur mobile (une seule cascade : coût maîtrisé).
        /// <para>
        /// ATTENTION : ne jamais modifier ici les réglages qui changent les mots-clés de shader (ombres
        /// douces, cascades, lumières additionnelles…). URP précalcule dans l'asset un « préfiltrage »
        /// des variantes, mis à jour seulement par l'inspecteur ; une modification par script le rend
        /// incohérent et le build Android élimine alors toutes les variantes d'affichage (écran noir
        /// sur téléphone, constaté le 2026-11-16). Ces réglages se changent dans l'inspecteur.
        /// </para>
        /// </summary>
        private static void ConfigureMobilePipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobilePipelinePath);
            if (asset == null)
                return;

            var so = new SerializedObject(asset);
            so.FindProperty("m_ShadowDistance").floatValue = 60f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// Génère l'éclairage d'environnement (sonde de réflexion du ciel) de la scène ouverte, déjà
        /// sauvegardée. Pas de lightmaps : GI précalculée et temps réel désactivées.
        /// </summary>
        public static void BakeEnvironment()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "CityLighting" };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }
            settings.bakedGI = false;
            settings.realtimeGI = false;
            EditorUtility.SetDirty(settings);

            Lightmapping.lightingSettings = settings;
            if (!Lightmapping.Bake())
                Debug.LogWarning("[City] Génération de l'éclairage d'environnement impossible.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
