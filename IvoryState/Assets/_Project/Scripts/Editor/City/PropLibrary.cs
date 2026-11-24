using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Objets de décor Poly Haven (CC0) : import adapté au mobile (décimation QEM sous budget de
    /// triangles, textures limitées à 256–512 px en ASTC 6x6 sur Android, deux niveaux LOD + culling,
    /// pas de collider FBX ni d'animation) et matériau URP instancié.
    /// </summary>
    internal static class PropLibrary
    {
        private const string ModelRoot = "Assets/_Project/ThirdParty/PolyHaven/Models/";
        /// <summary>Armes d'autres sources (CGTrader, Sketchfab), rangées selon la même convention :
        /// <c>&lt;id&gt;/&lt;id&gt;_1k.fbx</c> et <c>&lt;id&gt;/textures/&lt;id&gt;_diff_1k.jpg</c>.</summary>
        private const string WeaponRoot = "Assets/_Project/ThirdParty/Weapons/Models/";

        private static string RootOf(string id) => Directory.Exists(WeaponRoot + id) ? WeaponRoot : ModelRoot;

        private readonly struct MobileBudget
        {
            public readonly int Lod0Tris;
            public readonly int Lod1Tris;
            public readonly int TextureSize;

            public MobileBudget(int lod0Tris, int lod1Tris, int textureSize)
            {
                Lod0Tris = lod0Tris;
                Lod1Tris = lod1Tris;
                TextureSize = textureSize;
            }
        }

        private static readonly MobileBudget DefaultSmall = new MobileBudget(900, 280, 256);
        private static readonly MobileBudget DefaultMedium = new MobileBudget(1500, 420, 512);
        private static readonly MobileBudget DefaultHero = new MobileBudget(2200, 580, 512);

        private static readonly Dictionary<string, MobileBudget> Budgets = new Dictionary<string, MobileBudget>
        {
            // Grands meubles / pièces héroïques
            ["sofa_03"] = DefaultHero,
            ["Sofa_01"] = DefaultHero,
            ["sofa_02"] = DefaultHero,
            ["GothicBed_01"] = DefaultHero,
            ["Chandelier_01"] = new MobileBudget(2000, 520, 256),
            ["dining_table"] = DefaultHero,
            ["outdoor_table_chair_set_01"] = DefaultHero,
            ["ArmChair_01"] = new MobileBudget(1400, 380, 512),

            // Mobilier / équipements moyens
            ["wheelchair_01"] = new MobileBudget(2100, 550, 256),
            ["tool_cart"] = new MobileBudget(1800, 480, 512),
            ["portable_generator"] = new MobileBudget(1800, 450, 512),
            ["electric_stove"] = new MobileBudget(1500, 420, 512),
            ["exterior_aircon_unit"] = new MobileBudget(1100, 320, 512),
            ["planter_box_01"] = new MobileBudget(1000, 280, 512),
            ["wooden_bookshelf_worn"] = DefaultMedium,
            ["steel_frame_shelves_01"] = DefaultMedium,
            ["wooden_display_shelves_01"] = DefaultMedium,
            ["hand_truck"] = new MobileBudget(1300, 360, 256),
            ["round_wooden_table_01"] = new MobileBudget(1200, 360, 512),
            ["ClassicConsole_01"] = DefaultMedium,
            ["shrub_02"] = new MobileBudget(1600, 420, 512),
            ["shrub_sorrel_01"] = new MobileBudget(1100, 320, 256),
            ["calathea_orbifolia_01"] = new MobileBudget(2200, 550, 256),
            ["standing_chalkboard_01"] = new MobileBudget(1200, 360, 512),
            ["SchoolDesk_01"] = DefaultMedium,
            ["Barrel_02"] = new MobileBudget(1000, 320, 512),

            // Petits objets & bric-à-brac nombreux en rue (texture 256 px, <= 950 tris LOD0)
            ["plastic_crate_01"] = DefaultSmall,
            ["plastic_container"] = DefaultSmall,
            ["cardboard_box_01"] = DefaultSmall,
            ["metal_trash_can"] = DefaultSmall,
            ["small_lpg_tank"] = DefaultSmall,
            ["classic_laptop"] = DefaultSmall,
            ["ornate_mirror_01"] = DefaultSmall,
            ["vintage_microwave"] = DefaultSmall,
            ["plastic_jerrycan"] = DefaultSmall,
            ["bananas"] = DefaultSmall,
            ["rusted_wheel_rim_01"] = DefaultSmall,
            ["potted_plant_04"] = new MobileBudget(1400, 420, 256),
            ["ceiling_fan"] = new MobileBudget(1200, 340, 256),
            ["boombox"] = DefaultSmall,
            ["CashRegister_01"] = DefaultSmall,
            ["tire_pump"] = DefaultSmall,
            ["wooden_crate_01"] = DefaultSmall,
            ["trashbag"] = DefaultSmall,
            ["old_tyre"] = DefaultSmall,
            ["barrel_03"] = DefaultSmall,
            ["wall_clock"] = new MobileBudget(1250, 360, 256),
            ["plastic_monobloc_chair_01"] = new MobileBudget(1050, 320, 256),
            ["modern_coffee_table_01"] = DefaultSmall,
            ["medical_box"] = new MobileBudget(1000, 300, 256),
            ["medical_tape"] = new MobileBudget(600, 200, 256),
            ["WetFloorSign_01"] = new MobileBudget(250, 120, 256),
            ["fire_alarm"] = new MobileBudget(700, 220, 256),
            ["office_notepads"] = new MobileBudget(450, 160, 256),
            ["ceramic_vase_01"] = new MobileBudget(900, 260, 256),
            ["football"] = new MobileBudget(750, 240, 256),
            ["painted_wooden_stool"] = new MobileBudget(680, 220, 256),
            ["Shelf_01"] = new MobileBudget(220, 100, 256),
            ["painted_wooden_cabinet_02"] = new MobileBudget(970, 280, 512),

            // Armes (vitrines des quincailleries, armureries)
            ["ak47"] = new MobileBudget(2200, 600, 512),
            ["glock17"] = new MobileBudget(1800, 500, 512),
            ["taser"] = new MobileBudget(1800, 500, 512),
            ["fish_knife"] = DefaultSmall,
        };

        private sealed class PropEntry
        {
            public GameObject Prefab;
            public readonly Dictionary<Mesh, (Mesh lod0, Mesh lod1)> Optimized = new Dictionary<Mesh, (Mesh, Mesh)>();
        }

        private static readonly Dictionary<string, PropEntry> s_cache = new Dictionary<string, PropEntry>();

        public static void ClearCache() => s_cache.Clear();

        private static MobileBudget BudgetFor(string id) =>
            Budgets.TryGetValue(id, out MobileBudget budget) ? budget : DefaultMedium;

        private static PropEntry GetEntry(string id)
        {
            if (s_cache.TryGetValue(id, out PropEntry cached))
                return cached;

            string root = RootOf(id);
            string fbx = root + id + "/" + id + "_1k.fbx";
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[City] Modèle introuvable : " + fbx + " (lancer tools/fetch-assets.ps1)");
                s_cache[id] = null;
                return null;
            }

            MobileBudget budget = BudgetFor(id);

            string texDir = root + id + "/textures";
            var diffuseTextures = new List<string>();
            if (Directory.Exists(texDir))
            {
                foreach (string pattern in new[] { "*_diff_1k.jpg", "*_diff_1k.png" })
                {
                    foreach (string file in Directory.GetFiles(texDir, pattern))
                        diffuseTextures.Add(file.Replace('\\', '/'));
                }
            }
            string fallbackDiff = root + id + "/textures/" + id + "_diff_1k.jpg";
            if (diffuseTextures.Count == 0)
                diffuseTextures.Add(fallbackDiff);

            // Import mobile strict : lecture permise uniquement pendant la décimation éditeur ? Non, isReadable = false côté runtime;
            // MeshUtility.SetMeshes ou AssetDatabase charge directement les données Mesh en éditeur même si isReadable = false.
            bool changed = importer.addCollider || importer.isReadable || importer.importCameras || importer.importLights ||
                           importer.importAnimation || importer.animationType != ModelImporterAnimationType.None ||
                           importer.meshCompression != ModelImporterMeshCompression.High;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.meshCompression = ModelImporterMeshCompression.High;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

            Dictionary<AssetImporter.SourceAssetIdentifier, Object> existingMap = importer.GetExternalObjectMap();
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                if (asset is not Material embedded)
                    continue;

                string chosenDiff = PickDiffuse(id, embedded.name, diffuseTextures);
                string matKey = diffuseTextures.Count == 1
                    ? "M_Prop_" + id
                    : "M_Prop_" + id + "_" + Sanitize(embedded.name);
                Material material = CityMaterials.GetOrCreate(matKey, litShader);
                material.SetTexture("_BaseMap", CityMaterials.LoadTexture(chosenDiff, false, 1, budget.TextureSize));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.25f);
                material.DisableKeyword("_NORMALMAP");
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);

                var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name);
                if (!existingMap.TryGetValue(key, out Object current) || current != material)
                {
                    importer.AddRemap(key, material);
                    changed = true;
                }
            }

            if (changed)
                importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var entry = new PropEntry { Prefab = model };

            // Prépare les variantes décimées (LOD0 / LOD1) pour chaque maillage interne dépassant le budget.
            MeshFilter[] filters = model != null ? model.GetComponentsInChildren<MeshFilter>(true) : System.Array.Empty<MeshFilter>();
            int totalSourceTris = 0;
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh != null)
                    totalSourceTris += mf.sharedMesh.triangles.Length / 3;
            }
            foreach (MeshFilter mf in filters)
            {
                Mesh src = mf.sharedMesh;
                if (src == null || entry.Optimized.ContainsKey(src))
                    continue;
                int srcTris = src.triangles.Length / 3;
                float share = totalSourceTris > 0 ? srcTris / (float)totalSourceTris : 1f;
                int target0 = Mathf.Max(48, Mathf.RoundToInt(budget.Lod0Tris * share));
                int target1 = Mathf.Max(32, Mathf.RoundToInt(budget.Lod1Tris * share));
                string safeMeshName = Sanitize(id + "_" + src.name);
                MobileMeshOptimizer.EnsureMobileMesh(safeMeshName + "_lod0", src, target0, out Mesh lod0);
                Mesh lod1 = lod0;
                if (srcTris > 450 && target1 < (lod0.triangles.Length / 3) * 0.85f)
                    MobileMeshOptimizer.EnsureMobileMesh(safeMeshName + "_lod1", src, target1, out lod1);
                entry.Optimized[src] = (lod0, lod1);
            }

            s_cache[id] = entry;
            return entry;
        }

        /// <summary>Modèle prêt à instancier, ou null s'il n'a pas été téléchargé.</summary>
        public static GameObject Get(string id) => GetEntry(id)?.Prefab;

        private static string PickDiffuse(string id, string materialName, List<string> textures)
        {
            if (textures.Count == 1)
                return textures[0];
            string lowerMat = materialName.ToLowerInvariant();
            string best = textures[0];
            int bestScore = -1;
            foreach (string tex in textures)
            {
                string stem = Path.GetFileNameWithoutExtension(tex).ToLowerInvariant();
                stem = stem.Replace(id.ToLowerInvariant() + "_", "").Replace("_diff_1k", "");
                int score = 0;
                foreach (string token in stem.Split('_'))
                {
                    if (token.Length > 0 && lowerMat.Contains(token))
                        score += token.Length + (token.Contains("rusted") ? 0 : 4);
                }
                if (!stem.Contains("rusted"))
                    score += 1;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = tex;
                }
            }
            return best;
        }

        private static string Sanitize(string name)
        {
            var chars = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
                chars[i] = char.IsLetterOrDigit(name[i]) ? name[i] : '_';
            return new string(chars);
        }

        /// <summary>
        /// Place un objet de décor mobile (LOD0 + LOD1 si gain utile, culling écran, zéro ombre portée
        /// inutile sur le petit mobilier).
        /// </summary>
        public static GameObject Place(string id, Transform parent, Vector3 localPosition, float yaw,
            float scale = 1f, bool castShadows = false, float cullScreenSize = 0.025f, bool solid = true, float pitch = 0f)
        {
            PropEntry entry = GetEntry(id);
            if (entry == null || entry.Prefab == null)
                return null;

            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
            go.transform.localScale = Vector3.one * scale;
            go.isStatic = true;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.Prefab, go.transform);
            bool hasDistinctLod1 = false;
            foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null && entry.Optimized.TryGetValue(mf.sharedMesh, out var variants))
                {
                    if (variants.lod1 != variants.lod0)
                        hasDistinctLod1 = true;
                    mf.sharedMesh = variants.lod0;
                }
            }
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = true;

            Renderer[] lod0Renderers = instance.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in lod0Renderers)
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

            if (solid && lod0Renderers.Length > 0)
            {
                Quaternion rotation = go.transform.rotation;
                go.transform.rotation = Quaternion.identity;
                Bounds bounds = lod0Renderers[0].bounds;
                for (int i = 1; i < lod0Renderers.Length; i++)
                    bounds.Encapsulate(lod0Renderers[i].bounds);
                var box = go.AddComponent<BoxCollider>();
                box.center = go.transform.InverseTransformPoint(bounds.center);
                box.size = new Vector3(bounds.size.x / scale, bounds.size.y / scale, bounds.size.z / scale);
                go.transform.rotation = rotation;
            }

            var lod = go.AddComponent<LODGroup>();
            if (hasDistinctLod1 && cullScreenSize < 0.09f)
            {
                var lod1Instance = Object.Instantiate(instance, go.transform);
                lod1Instance.name = "LOD1";
                // Restaure d'abord les références originales ? Non : chaque MeshFilter de lod1Instance pointe encore vers lod0 si déjà remplacé,
                // donc on associe directement depuis les MeshFilters du prefab d'origine.
                MeshFilter[] srcFilters = entry.Prefab.GetComponentsInChildren<MeshFilter>(true);
                MeshFilter[] dstFilters = lod1Instance.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < Mathf.Min(srcFilters.Length, dstFilters.Length); i++)
                {
                    Mesh src = srcFilters[i].sharedMesh;
                    if (src != null && entry.Optimized.TryGetValue(src, out var variants))
                        dstFilters[i].sharedMesh = variants.lod1;
                }
                Renderer[] lod1Renderers = lod1Instance.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in lod1Renderers)
                    r.shadowCastingMode = ShadowCastingMode.Off;
                lod.SetLODs(new[]
                {
                    new LOD(0.11f, lod0Renderers),
                    new LOD(cullScreenSize, lod1Renderers),
                });
            }
            else
            {
                lod.SetLODs(new[] { new LOD(cullScreenSize, lod0Renderers) });
            }

            return go;
        }

        /// <summary>Utilitaire éditeur : affiche dans le journal les triangles et dimensions effectifs (LOD0 / LOD1 mobile).</summary>
        public static void InspectAll()
        {
            ClearCache();
            var dirs = new List<string>(Directory.GetDirectories(Path.GetFullPath(ModelRoot)));
            if (Directory.Exists(WeaponRoot))
                dirs.AddRange(Directory.GetDirectories(Path.GetFullPath(WeaponRoot)));
            var root = new GameObject("PropInspectTemp");
            foreach (string dir in dirs)
            {
                string id = Path.GetFileName(dir);
                PropEntry entry = GetEntry(id);
                if (entry == null || entry.Prefab == null)
                    continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(entry.Prefab, root.transform);
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                int srcTris = 0, lod0Tris = 0, lod1Tris = 0;
                foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    srcTris += mf.sharedMesh.triangles.Length / 3;
                    if (entry.Optimized.TryGetValue(mf.sharedMesh, out var v))
                    {
                        lod0Tris += v.lod0.triangles.Length / 3;
                        lod1Tris += v.lod1.triangles.Length / 3;
                    }
                    else
                    {
                        lod0Tris += mf.sharedMesh.triangles.Length / 3;
                        lod1Tris += mf.sharedMesh.triangles.Length / 3;
                    }
                }
                Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);
                Debug.Log($"[PropInspect] {id,-28} src={srcTris,6} lod0={lod0Tris,5} lod1={lod1Tris,4} tex={BudgetFor(id).TextureSize,3} size=({b.size.x:F2}, {b.size.y:F2}, {b.size.z:F2})");
            }
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
        }
    }
}
