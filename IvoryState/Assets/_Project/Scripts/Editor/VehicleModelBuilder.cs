using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Editor
{
    /// <summary>
    /// Modèles de véhicules téléchargés (Sketchfab, CC-BY) : matériaux URP et préfabs prêts à poser.
    /// <list type="bullet">
    /// <item>Source : <c>ThirdParty/Vehicles/Models/&lt;id&gt;/&lt;id&gt;_1k.fbx</c>, textures 1K dans <c>textures/</c> et la fiche
    /// <c>unity_materials.json</c> (écrites par les outils Blender/Python de conversion, voir CREDITS.md).</item>
    /// <item>Matériaux : opaque (couleur, relief, carte métal/brillance), autocollant (découpe alpha) ou vitre
    /// (transparente, teintée). Associés aux matériaux du FBX par leur nom.</item>
    /// <item>Préfab : <c>Prefabs/Vehicles/Models/PF_Vehicle_&lt;id&gt;.prefab</c>, pivot au sol au centre, avant vers +Z,
    /// sans collider ni script (la conduite viendra les brancher).</item>
    /// </list>
    /// </summary>
    internal static class VehicleModelBuilder
    {
        private const string SourceRoot = "Assets/_Project/ThirdParty/Vehicles/Models/";
        private const string PrefabRoot = "Assets/_Project/Prefabs/Vehicles/Models/";

        /// <summary>
        /// Rotation ajoutée au modèle dans son préfab. Les FBX convertis (avant vers -Y dans Blender) arrivent
        /// déjà avec l'avant vers +Z dans Unity : vérifié en image, rien à ajouter.
        /// </summary>
        private static readonly Quaternion ModelRotation = Quaternion.identity;

        [Serializable]
        private sealed class MaterialSpec
        {
            public string name;
            public string mode;
            public float[] color;
            public float metallic;
            public float smoothness;
            public string baseMap;
            public string normal;
            public string ms;
        }

        [Serializable]
        private sealed class VehicleSpec
        {
            public string id;
            public MaterialSpec[] materials;
        }

        [MenuItem("Ivory State/Véhicules/Construire les modèles de véhicules")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(PrefabRoot);
            int count = 0;
            foreach (string dir in Directory.GetDirectories(SourceRoot))
            {
                string id = Path.GetFileName(dir);
                if (Build(id))
                    count++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Véhicules] " + count + " préfabs construits dans " + PrefabRoot);
        }

        public static IEnumerable<string> PrefabPaths()
        {
            foreach (string dir in Directory.GetDirectories(SourceRoot))
                yield return PrefabRoot + "PF_Vehicle_" + Path.GetFileName(dir) + ".prefab";
        }

        private static bool Build(string id)
        {
            string folder = SourceRoot + id + "/";
            string fbxPath = folder + id + "_1k.fbx";
            string specPath = folder + "unity_materials.json";
            if (!File.Exists(fbxPath) || !File.Exists(specPath))
            {
                Debug.LogWarning("[Véhicules] " + id + " : FBX ou fiche manquante.");
                return false;
            }

            // « base » est un mot réservé en C# : on renomme la clé avant lecture.
            string json = File.ReadAllText(specPath).Replace("\"base\":", "\"baseMap\":");
            var spec = JsonUtility.FromJson<VehicleSpec>(json);

            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

            string matFolder = folder + "Materials/";
            Directory.CreateDirectory(matFolder);
            foreach (MaterialSpec m in spec.materials)
            {
                Material material = BuildMaterial(folder, matFolder, id, m);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), material);
            }
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var root = new GameObject("PF_Vehicle_" + id);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.name = "Modele";
            // Composer, pas remplacer : pour un FBX à un seul objet, Unity met la correction d'axes
            // (Blender Z vers le haut -> Unity Y) sur la racine elle-même.
            instance.transform.localRotation = ModelRotation * instance.transform.localRotation;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);

            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabRoot + "PF_Vehicle_" + id + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log($"[Véhicules] {id} : {spec.materials.Length} matériaux, taille {bounds.size.x:F2} × {bounds.size.y:F2} × {bounds.size.z:F2} m");
            return true;
        }

        private static Material BuildMaterial(string folder, string matFolder, string id, MaterialSpec m)
        {
            string safe = MakeSafe(m.name);
            string path = matFolder + "M_" + id + "_" + safe + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            string mode = string.IsNullOrEmpty(m.mode) ? "opaque" : m.mode;
            bool glass = mode == "glass";
            bool cutout = mode == "cutout";
            Texture2D baseMap = LoadTexture(folder, m.baseMap, false, glass || cutout);
            Texture2D normal = LoadTexture(folder, m.normal, true, false);
            Texture2D ms = LoadTexture(folder, m.ms, false, false, linear: true);

            Color color = m.color != null && m.color.Length >= 3
                ? new Color(m.color[0], m.color[1], m.color[2], m.color.Length > 3 ? m.color[3] : 1f)
                : Color.white;
            if (baseMap != null)
                color = Color.white; // la teinte grise par défaut du FBX assombrirait la texture

            material.SetTexture("_BaseMap", baseMap);
            material.SetTexture("_BumpMap", normal);
            SetKeyword(material, "_NORMALMAP", normal != null);
            material.SetTexture("_MetallicGlossMap", ms);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", ms != null);
            material.SetFloat("_SmoothnessTextureChannel", 0f);

            string lower = m.name.ToLowerInvariant();
            bool optics = lower.Contains("optic") || lower.Contains("light");
            if (ms != null)
            {
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Smoothness", 1f);
            }
            else
            {
                // Le « métal = 1 » des FBX importés est faux : valeurs raisonnables selon la pièce.
                material.SetFloat("_Metallic", glass || optics ? 0f : Mathf.Min(m.metallic, 0.3f));
                material.SetFloat("_Smoothness", glass ? 0.92f : optics ? 0.75f : Mathf.Clamp(m.smoothness, 0.2f, 0.7f));
            }

            if (glass)
            {
                color = baseMap != null ? new Color(1f, 1f, 1f, 0.75f) : new Color(0.08f, 0.10f, 0.12f, 0.55f);
                SetTransparent(material);
            }
            else if (cutout)
            {
                SetOpaque(material, alphaClip: true);
            }
            else
            {
                SetOpaque(material, alphaClip: false);
            }
            material.SetColor("_BaseColor", color);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetOpaque(Material material, bool alphaClip)
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", false);
            SetKeyword(material, "_ALPHATEST_ON", alphaClip);
            material.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Opaque");
            material.renderQueue = alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        }

        private static void SetTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", true);
            SetKeyword(material, "_ALPHATEST_ON", false);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        private static Texture2D LoadTexture(string folder, string file, bool normalMap, bool alpha, bool linear = false)
        {
            if (string.IsNullOrEmpty(file))
                return null;
            string path = folder + "textures/" + file;
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                Debug.LogWarning("[Véhicules] Texture introuvable : " + path);
                return null;
            }
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            bool srgb = !linear && !normalMap;
            bool changed = importer.textureType != type || importer.maxTextureSize != 1024 || importer.sRGBTexture != srgb ||
                           importer.alphaIsTransparency != alpha || !android.overridden || android.format != TextureImporterFormat.ASTC_6x6;
            if (changed)
            {
                importer.textureType = type;
                importer.maxTextureSize = 1024;
                importer.sRGBTexture = srgb;
                importer.alphaIsTransparency = alpha;
                importer.mipmapEnabled = true;
                importer.streamingMipmaps = true;
                importer.textureCompression = TextureImporterCompression.Compressed;
                android.overridden = true;
                android.maxTextureSize = 1024;
                android.format = TextureImporterFormat.ASTC_6x6;
                android.compressionQuality = 50;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static string MakeSafe(string name)
        {
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                    chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
