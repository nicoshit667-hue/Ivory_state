using System.IO;
using IvoryState.Client.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Carte de la ville : vue du ciel orthographique, nord en haut, rendue une fois à la génération
    /// (midi, sans brume ni ombres longues) et enregistrée en PNG 1024 × 1024. Le jeu l'affiche
    /// telle quelle (mini-carte, carte plein écran) : aucune caméra supplémentaire sur le téléphone.
    /// </summary>
    internal static class MapBaker
    {
        public const int Size = 4096;
        private const string ScenePath = "Assets/_Project/Scenes/Bootstrap.unity";
        private const string TexturePath = "Assets/_Project/Art/Generated/T_CityMap.png";

        /// <summary>Recalcule seulement la carte, sans régénérer la ville (menu ou -executeMethod).</summary>
        [MenuItem("Ivory State/Recalculer la carte", priority = 51)]
        public static void BakeFromScene()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            Texture2D map = Bake(TexturePath, MapExtent);
            var hud = Object.FindFirstObjectByType<Client.UI.MapHud>(FindObjectsInactive.Include);
            if (hud != null)
                Assign(hud, map);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("[Map] Carte recalculée.");
        }

        /// <summary>
        /// Demi-côté couvert par la carte (m), centrée sur l'origine : toute la disposition d'Abidjan (de
        /// Port-Bouët à Abobo), arrondi à la dizaine.
        /// </summary>
        public static float MapExtent
        {
            get
            {
                Rect b = Gameplay.World.CityLayout.Bounds;
                float half = Mathf.Max(Mathf.Max(-b.xMin, b.xMax), Mathf.Max(-b.yMin, b.yMax));
                return Mathf.Ceil((half + 10f) / 10f) * 10f;
            }
        }

        /// <summary>Donne la carte et son étendue au HUD.</summary>
        public static void Assign(Client.UI.MapHud hud, Texture2D map)
        {
            var so = new SerializedObject(hud);
            so.FindProperty("_mapTexture").objectReferenceValue = map;
            so.FindProperty("_worldExtent").floatValue = MapExtent;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static Texture2D Bake(string assetPath, float extent)
        {
            var go = new GameObject("MapCamera");
            var camera = go.AddComponent<UnityEngine.Camera>();
            camera.orthographic = true;
            camera.orthographicSize = extent;
            camera.aspect = 1f;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 1200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.35f, 0.33f, 0.28f);
            // Regard vers le bas, haut de l'image = +Z (nord).
            camera.transform.SetPositionAndRotation(new Vector3(0f, 600f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            Light sun = RenderSettings.sun;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            DayNightCycle.Apply(sun, 13f, null);

            // Sans ombres : la caméra est à 500 m, bien au-delà de la distance d'ombre ; l'échantillonnage
            // hors zone noircissait les trottoirs. Valeur d'origine restaurée juste après.
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float shadowDistance = pipeline != null ? pipeline.shadowDistance : 0f;
            if (pipeline != null)
                pipeline.shadowDistance = 0f;

            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            camera.targetTexture = target;
            camera.Render();
            if (pipeline != null)
                pipeline.shadowDistance = shadowDistance;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            string fullPath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, image.EncodeToPNG());

            camera.targetTexture = null;
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(go);

            // Retour à l'éclairage de la scène.
            RenderSettings.fog = fog;
            DayNightCycle.Apply(sun, CityLighting.EditorHour, RenderSettings.skybox);

            AssetDatabase.ImportAsset(assetPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.maxTextureSize = Size;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
