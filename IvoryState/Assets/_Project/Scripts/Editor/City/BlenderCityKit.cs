using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Charge, configure et tamponne les modèles 3D réalistes modélisés dans Blender (<c>Art/Models/City/*.fbx</c>)
    /// dans les maillages fusionnés par tronçon de 128 m et par matériau (optimisation mobile : zéro draw call
    /// supplémentaire par bâtiment, lampadaire ou mobilier, et zéro fuite de mémoire runtime).
    /// </summary>
    internal static class BlenderCityKit
    {
        public const string ModelsFolder = "Assets/_Project/Art/Models/City";

        private sealed class SubmeshPart
        {
            public Mesh Mesh;
            public int SubmeshIndex;
            public string MaterialSlot;
        }

        private sealed class KitModel
        {
            public string Name;
            public Bounds Bounds;
            public readonly List<SubmeshPart> Parts = new List<SubmeshPart>();
        }

        private static readonly Dictionary<string, KitModel> s_cache = new Dictionary<string, KitModel>();

        public static Material TrimMat { get; private set; }
        public static Material GlassMat { get; private set; }
        public static Material RoofTileMat { get; private set; }
        public static Material RoofMetalMat { get; private set; }
        public static Material DarkMetalMat { get; private set; }
        public static Material GalvSteelMat { get; private set; }
        public static Material ConcreteMat { get; private set; }
        public static Material WoodMat { get; private set; }
        public static Material PaintedMat { get; private set; }

        public static void Prepare()
        {
            s_cache.Clear();
            TrimMat = CityMaterials.Painted();
            GlassMat = CityMaterials.BuildingGlass();
            RoofTileMat = CityMaterials.Roof("M_RoofTiles", "clay_roof_tiles_02", 2.2f, 0.18f);
            RoofMetalMat = CityMaterials.Roof("M_RoofMetal", "corrugated_iron_02", 1.8f, 0.35f);
            DarkMetalMat = CityMaterials.Metal("M_CityDarkMetal", new Color(0.16f, 0.17f, 0.19f), 0.72f, 0.45f);
            GalvSteelMat = CityMaterials.Metal("M_CityGalvSteel", new Color(0.66f, 0.68f, 0.71f), 0.84f, 0.52f);
            ConcreteMat = CityMaterials.Concrete();
            WoodMat = CityMaterials.Wood();
            PaintedMat = CityMaterials.Painted();

            if (Directory.Exists(Path.GetFullPath(ModelsFolder)))
                AssetDatabase.Refresh();
        }

        public static void ClearCache() => s_cache.Clear();

        public static bool HasModel(string name) => LoadModel(name) != null;

        public static Bounds GetBounds(string name)
        {
            KitModel m = LoadModel(name);
            return m != null ? m.Bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>
        /// Tamponne toutes les parties d'un modèle Blender dans des constructeurs regroupés par matériau.
        /// </summary>
        public static void Stamp(string modelName, Dictionary<Material, BoxMeshBuilder> builders,
            Matrix4x4 matrix, Material wallMaterial, Color wallTint, int seed,
            Color? roofTint = null, Vector3? uvSizeScale = null,
            BoxMeshBuilder ledBulbBuilder = null, BoxMeshBuilder sodiumBulbBuilder = null)
        {
            KitModel model = LoadModel(modelName);
            if (model == null)
                return;

            foreach (SubmeshPart part in model.Parts)
            {
                string slot = part.MaterialSlot;
                if (slot == "Mat_LampLED" && ledBulbBuilder != null)
                {
                    ledBulbBuilder.AddSubmesh(part.Mesh, part.SubmeshIndex, matrix, Color.white, seed, false, null, true);
                    continue;
                }
                if (slot == "Mat_LampSodium" && sodiumBulbBuilder != null)
                {
                    sodiumBulbBuilder.AddSubmesh(part.Mesh, part.SubmeshIndex, matrix, Color.white, seed, false, null, true);
                    continue;
                }

                Material targetMat = ResolveMaterial(slot, wallMaterial);
                if (targetMat == null)
                    continue;

                if (!builders.TryGetValue(targetMat, out BoxMeshBuilder builder))
                {
                    builder = new BoxMeshBuilder();
                    builders[targetMat] = builder;
                }

                bool isWall = slot == "Mat_FacadeWall";
                bool isRoof = slot == "Mat_RoofTile" || slot == "Mat_RoofMetal";
                bool isGlass = slot == "Mat_GlassNight";

                Color tint = Color.white;
                bool multiply = false;
                if (isWall)
                {
                    tint = wallTint;
                    multiply = true;
                }
                else if (isRoof && roofTint.HasValue)
                {
                    tint = roofTint.Value;
                    multiply = true;
                }

                builder.AddSubmesh(part.Mesh, part.SubmeshIndex, matrix, tint, seed, multiply,
                    (isWall || isRoof || slot == "Mat_Concrete") ? uvSizeScale : null, !isGlass);
            }
        }

        /// <summary>
        /// Variante qui achemine directement dans un dictionnaire indexé par <c>(tronçon, matériau)</c>.
        /// </summary>
        public static void StampChunked(string modelName, Vector2Int chunk,
            Dictionary<(Vector2Int, Material), BoxMeshBuilder> chunkBuilders,
            Matrix4x4 matrix, Material wallMaterial, Color wallTint, int seed,
            Color? roofTint = null, Vector3? uvSizeScale = null,
            BoxMeshBuilder ledBulbBuilder = null, BoxMeshBuilder sodiumBulbBuilder = null)
        {
            KitModel model = LoadModel(modelName);
            if (model == null)
                return;

            foreach (SubmeshPart part in model.Parts)
            {
                string slot = part.MaterialSlot;
                if (slot == "Mat_LampLED" && ledBulbBuilder != null)
                {
                    ledBulbBuilder.AddSubmesh(part.Mesh, part.SubmeshIndex, matrix, Color.white, seed, false, null, true);
                    continue;
                }
                if (slot == "Mat_LampSodium" && sodiumBulbBuilder != null)
                {
                    sodiumBulbBuilder.AddSubmesh(part.Mesh, part.SubmeshIndex, matrix, Color.white, seed, false, null, true);
                    continue;
                }

                Material targetMat = ResolveMaterial(slot, wallMaterial);
                if (targetMat == null)
                    continue;

                var key = (chunk, targetMat);
                if (!chunkBuilders.TryGetValue(key, out BoxMeshBuilder builder))
                {
                    builder = new BoxMeshBuilder();
                    chunkBuilders[key] = builder;
                }

                bool isWall = slot == "Mat_FacadeWall";
                bool isRoof = slot == "Mat_RoofTile" || slot == "Mat_RoofMetal";
                bool isGlass = slot == "Mat_GlassNight";

                Color tint = Color.white;
                bool multiply = false;
                if (isWall)
                {
                    tint = wallTint;
                    multiply = true;
                }
                else if (isRoof && roofTint.HasValue)
                {
                    tint = roofTint.Value;
                    multiply = true;
                }

                builder.AddSubmesh(part.Mesh, part.SubmeshIndex, matrix, tint, seed, multiply,
                    (isWall || isRoof || slot == "Mat_Concrete") ? uvSizeScale : null, !isGlass);
            }
        }

        /// <summary>Crée des objets enfants à partir d'un dictionnaire <c>Material -> BoxMeshBuilder</c> (pour points isolés).</summary>
        public static void BuildObjects(Transform parent, string namePrefix,
            Dictionary<Material, BoxMeshBuilder> builders, List<Mesh> meshes, bool castShadows = true)
        {
            int idx = 0;
            foreach (var pair in builders)
            {
                if (pair.Value.IsEmpty)
                    continue;
                var go = new GameObject($"{namePrefix}_{idx++}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                go.isStatic = true;
                Mesh mesh = pair.Value.Build($"{parent.name}_{namePrefix}_{pair.Key.name}");
                meshes.Add(mesh);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = pair.Key;
                if (!castShadows)
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        public static Material ResolveMaterial(string slotName, Material wallMaterial)
        {
            return slotName switch
            {
                "Mat_FacadeWall" => wallMaterial != null ? wallMaterial : PaintedMat,
                "Mat_FacadeTrim" => TrimMat,
                "Mat_GlassNight" => GlassMat,
                "Mat_RoofTile" => RoofTileMat,
                "Mat_RoofMetal" => RoofMetalMat,
                "Mat_DarkMetal" => DarkMetalMat,
                "Mat_GalvSteel" => GalvSteelMat,
                "Mat_Concrete" => ConcreteMat,
                "Mat_Wood" => WoodMat,
                "Mat_AccentPaint" => PaintedMat,
                "Mat_LampLED" => CityDecor.BulbMaterial,
                "Mat_LampSodium" => CityDecor.SodiumBulbMaterial,
                _ => PaintedMat,
            };
        }

        private static KitModel LoadModel(string name)
        {
            if (s_cache.TryGetValue(name, out KitModel cached))
                return cached;

            string fbxPath = ModelsFolder + "/" + name + ".fbx";
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            }
            if (importer == null)
            {
                Debug.LogWarning("[BlenderCityKit] Modèle introuvable : " + fbxPath);
                s_cache[name] = null;
                return null;
            }

            bool needsReimport =
                !importer.isReadable ||
                importer.addCollider ||
                importer.importCameras ||
                importer.importLights ||
                importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.meshCompression != ModelImporterMeshCompression.High ||
                importer.bakeAxisConversion;

            if (needsReimport)
            {
                importer.isReadable = true;
                importer.addCollider = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.meshCompression = ModelImporterMeshCompression.High;
                importer.bakeAxisConversion = false;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
            }

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (root == null)
            {
                s_cache[name] = null;
                return null;
            }

            var kit = new KitModel { Name = name };
            bool hasBounds = false;
            Bounds total = new Bounds();
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                var renderer = mf.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null)
                    continue;
                if (!hasBounds)
                {
                    total = mesh.bounds;
                    hasBounds = true;
                }
                else
                {
                    total.Encapsulate(mesh.bounds);
                }

                Material[] mats = renderer.sharedMaterials;
                int subCount = Math.Min(mesh.subMeshCount, mats.Length);
                for (int sm = 0; sm < subCount; sm++)
                {
                    string matName = mats[sm] != null ? CleanSlotName(mats[sm].name) : "Mat_AccentPaint";
                    kit.Parts.Add(new SubmeshPart
                    {
                        Mesh = mesh,
                        SubmeshIndex = sm,
                        MaterialSlot = matName,
                    });
                }
            }
            kit.Bounds = total;
            s_cache[name] = kit;
            return kit;
        }

        private static string CleanSlotName(string raw)
        {
            string s = raw.Replace(" (Instance)", "");
            int dot = s.LastIndexOf('.');
            if (dot > 0 && int.TryParse(s.Substring(dot + 1), out _))
                s = s.Substring(0, dot);
            return s;
        }
    }
}
