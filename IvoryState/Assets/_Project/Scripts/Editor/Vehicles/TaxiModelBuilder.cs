using System;
using System.Collections.Generic;
using System.IO;
using IvoryState.Editor.City;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Editor.Vehicles
{
    /// <summary>
    /// Taxi d'Abidjan (berline Corolla E90 modélisée dans Blender, avec repli procédural) et base des
    /// voitures personnelles (repeintes par <see cref="Gameplay.Vehicles.VehicleMotor"/>).
    /// <para>
    /// Charge <c>Art/Models/Vehicles/VH_Taxi_Corolla.fbx</c> (+ <c>_LOD1.fbx</c> pour le <see cref="LODGroup"/>)
    /// quand présent, applique les matériaux URP mobile et <c>IvoryState/NightLight</c> (<c>_IvoryNight</c>),
    /// et conserve les noms attendus par <c>VehicleMotor</c> (<c>Carrosserie</c>, <c>Lumignon</c>, <c>Roue_*</c>).
    /// </para>
    /// </summary>
    internal static class TaxiModelBuilder
    {
        public const string MeshAssetPath = "Assets/_Project/Art/Generated/TaxiMeshes.asset";
        public const string BlenderFbxPath = "Assets/_Project/Art/Models/Vehicles/VH_Taxi_Corolla.fbx";
        public const string BlenderLod1FbxPath = "Assets/_Project/Art/Models/Vehicles/VH_Taxi_Corolla_LOD1.fbx";

        // Sections de la caisse : z, demi-largeur bas, demi-largeur ceinture, bas, haut (coffre / capot).
        private static readonly (float z, float hwBot, float hwMid, float bot, float top)[] BodySections =
        {
            (-2.18f, 0.77f, 0.80f, 0.36f, 0.81f),
            (-2.02f, 0.83f, 0.87f, 0.29f, 0.96f),
            (-1.68f, 0.86f, 0.90f, 0.27f, 1.00f),
            (-1.05f, 0.86f, 0.90f, 0.27f, 0.99f),
            ( 0.00f, 0.86f, 0.90f, 0.27f, 0.97f),
            ( 1.00f, 0.86f, 0.90f, 0.27f, 0.95f),
            ( 1.68f, 0.85f, 0.89f, 0.28f, 0.89f),
            ( 2.02f, 0.82f, 0.86f, 0.30f, 0.80f),
            ( 2.18f, 0.76f, 0.79f, 0.36f, 0.67f),
        };

        // Sections de l'habitacle : z, demi-largeur ceinture, demi-largeur toit, hauteur du toit.
        private static readonly Vector4[] CabinSections =
        {
            new Vector4(-1.14f, 0.81f, 0.63f, 1.00f),
            new Vector4(-0.62f, 0.81f, 0.64f, 1.44f),
            new Vector4( 0.40f, 0.81f, 0.64f, 1.46f),
            new Vector4( 1.08f, 0.81f, 0.62f, 0.96f),
        };

        private const float Belt = 0.94f;
        private const float WheelRadius = 0.34f;
        private const float WheelWidth = 0.22f;
        private const float WheelX = 0.80f;
        private const float FrontAxleZ = 1.35f;
        private const float RearAxleZ = -1.35f;

        private sealed class Part
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
        }

        public static void Build(Transform graphics)
        {
            if (TryBuildFromBlenderFbx(graphics))
                return;

            BuildProceduralFallback(graphics);
        }

        private static bool TryBuildFromBlenderFbx(Transform graphics)
        {
            GameObject lod0Asset = LoadBlenderFbx(BlenderFbxPath);
            if (lod0Asset == null)
                return false;

            Dictionary<string, Material> matMap = CreateUnityMaterialMap();

            var lod0Root = new GameObject("LOD0").transform;
            lod0Root.SetParent(graphics, false);
            List<Renderer> lod0Renderers = PopulateFromFbx(lod0Asset, lod0Root, matMap, castShadows: true);
            if (lod0Renderers.Count == 0)
            {
                UnityEngine.Object.DestroyImmediate(lod0Root.gameObject);
                return false;
            }
            NormalizeOrientationAndGround(lod0Root);

            GameObject lod1Asset = LoadBlenderFbx(BlenderLod1FbxPath);
            if (lod1Asset != null)
            {
                var lod1Root = new GameObject("LOD1").transform;
                lod1Root.SetParent(graphics, false);
                List<Renderer> lod1Renderers = PopulateFromFbx(lod1Asset, lod1Root, matMap, castShadows: false);
                lod1Root.localPosition = lod0Root.localPosition;
                lod1Root.localRotation = lod0Root.localRotation;

                var lodGroup = graphics.gameObject.AddComponent<LODGroup>();
                lodGroup.SetLODs(new[]
                {
                    new LOD(0.15f, lod0Renderers.ToArray()),
                    new LOD(0.018f, lod1Renderers.ToArray()),
                });
                lodGroup.RecalculateBounds();
            }

            Debug.Log("[TaxiModelBuilder] Modèle Blender Corolla chargé : " + BlenderFbxPath +
                      (lod1Asset != null ? " (+ LOD1)" : ""));
            return true;
        }

        private static GameObject LoadBlenderFbx(string assetPath)
        {
            if (!File.Exists(Path.GetFullPath(assetPath)))
                return null;

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            }
            if (importer == null)
                return null;

            bool needsReimport =
                importer.addCollider ||
                importer.importCameras ||
                importer.importLights ||
                importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.meshCompression != ModelImporterMeshCompression.Medium ||
                importer.bakeAxisConversion ||
                importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard;

            if (needsReimport)
            {
                importer.isReadable = false;
                importer.addCollider = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.meshCompression = ModelImporterMeshCompression.Medium;
                importer.bakeAxisConversion = false;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }

        private static List<Renderer> PopulateFromFbx(GameObject fbxAsset, Transform parent,
            Dictionary<string, Material> matMap, bool castShadows)
        {
            var renderers = new List<Renderer>();
            Matrix4x4 rootInv = fbxAsset.transform.worldToLocalMatrix;

            foreach (MeshFilter srcMf in fbxAsset.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = srcMf.sharedMesh;
                var srcMr = srcMf.GetComponent<MeshRenderer>();
                if (mesh == null || srcMr == null)
                    continue;

                string cleanName = CleanBlenderName(srcMf.gameObject.name);
                // Sur mobile, les vitres sont opaques réfléchissantes (zéro overdraw) : l'intérieur est inutile.
                if (cleanName == "Interieur")
                    continue;

                var go = new GameObject(cleanName, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);

                Matrix4x4 rel = rootInv * srcMf.transform.localToWorldMatrix;
                go.transform.localPosition = rel.GetColumn(3);
                go.transform.localRotation = rel.rotation;
                go.transform.localScale = rel.lossyScale;

                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var dstMr = go.GetComponent<MeshRenderer>();

                Material[] srcMats = srcMr.sharedMaterials;
                var mapped = new Material[srcMats.Length];
                for (int i = 0; i < srcMats.Length; i++)
                {
                    string slot = srcMats[i] != null ? CleanBlenderName(srcMats[i].name) : string.Empty;
                    mapped[i] = matMap.TryGetValue(slot, out Material resolved) ? resolved : matMap["M_Plastiques"];
                }
                dstMr.sharedMaterials = mapped;

                bool majorCaster = castShadows && (cleanName == "Carrosserie" || cleanName.StartsWith("Roue", StringComparison.Ordinal));
                dstMr.shadowCastingMode = majorCaster ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderers.Add(dstMr);
            }

            return renderers;
        }

        private static void NormalizeOrientationAndGround(Transform lodRoot)
        {
            // Vérifie automatiquement (depuis Mesh.bounds, fiable même en -batchmode) que l'avant pointe vers +Z
            // et que le bas des pneus repose exactement sur Y = 0.
            Matrix4x4 rootInv = lodRoot.worldToLocalMatrix;
            Transform headlights = lodRoot.Find("Phares");
            if (headlights != null)
            {
                var mf = headlights.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Vector3 center = (rootInv * headlights.localToWorldMatrix).MultiplyPoint3x4(mf.sharedMesh.bounds.center);
                    if (center.z < -0.1f)
                        lodRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }

            rootInv = lodRoot.worldToLocalMatrix;
            float minY = float.MaxValue;
            foreach (MeshFilter mf in lodRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = rootInv * mf.transform.localToWorldMatrix;
                Vector3 c = b.center;
                Vector3 e = b.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float y = m.MultiplyPoint3x4(new Vector3(c.x + e.x * sx, c.y + e.y * sy, c.z + e.z * sz)).y;
                    if (y < minY)
                        minY = y;
                }
            }
            if (minY < float.MaxValue && Mathf.Abs(minY) > 0.002f)
            {
                Vector3 pos = lodRoot.localPosition;
                pos.y -= minY;
                lodRoot.localPosition = pos;
            }
        }

        private static Dictionary<string, Material> CreateUnityMaterialMap()
        {
            return new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                ["M_Carrosserie"]       = Paint("M_TaxiPaint",      new Color(0.95f, 0.72f, 0.04f), 0.12f, 0.84f),
                ["M_Vitres"]            = Paint("M_TaxiGlass",      new Color(0.05f, 0.07f, 0.10f), 0.34f, 0.96f),
                ["M_Plastiques"]        = Paint("M_TaxiTrim",       new Color(0.08f, 0.085f, 0.092f), 0.04f, 0.34f),
                ["M_Plastiques_Sombre"] = Paint("M_TaxiTrimDark",   new Color(0.03f, 0.032f, 0.036f), 0.0f, 0.18f),
                ["M_Chrome"]            = Paint("M_TaxiChrome",     new Color(0.82f, 0.84f, 0.87f), 0.92f, 0.86f),
                ["M_Plaques"]           = Paint("M_TaxiPlate",      new Color(0.92f, 0.90f, 0.84f), 0.04f, 0.28f),
                ["M_Phares"]            = CityMaterials.NightLight("M_TaxiHeadlight", new Color(1f, 0.96f, 0.88f), 0.95f, 5.5f, null, false),
                ["M_Feux"]              = CityMaterials.NightLight("M_TaxiTaillight", new Color(0.92f, 0.06f, 0.03f), 0.55f, 2.8f, null, false),
                ["M_Clignotants"]       = CityMaterials.NightLight("M_TaxiAmber",     new Color(0.95f, 0.42f, 0.03f), 0.55f, 1.8f, null, false),
                ["M_FeuRecul"]          = Paint("M_TaxiReverse",    new Color(0.86f, 0.88f, 0.90f), 0.15f, 0.85f),
                ["M_Lumignon_Cadre"]    = Paint("M_TaxiSignFrame",  new Color(0.94f, 0.93f, 0.90f), 0.02f, 0.65f),
                ["M_Lumignon"]          = CityMaterials.NightLight("M_TaxiSign",      new Color(1f, 0.52f, 0.08f), 1.10f, 3.2f, null, false),
                ["M_Pneu"]              = Paint("M_Tyre",           new Color(0.055f, 0.058f, 0.062f), 0f, 0.18f),
                ["M_Jante_Tole"]        = Paint("M_TaxiRim",        new Color(0.72f, 0.74f, 0.77f), 0.84f, 0.72f),
                ["M_Frein"]             = Paint("M_TaxiBrake",      new Color(0.10f, 0.11f, 0.12f), 0.50f, 0.35f),
            };
        }

        private static string CleanBlenderName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;
            string s = raw.Replace(" (Instance)", "");
            int dot = s.LastIndexOf('.');
            if (dot > 0 && int.TryParse(s.Substring(dot + 1), out _))
                s = s.Substring(0, dot);
            return s;
        }

        private static void BuildProceduralFallback(Transform graphics)
        {
            var paint = new Part();
            var glass = new Part();
            var trim = new Part();
            var chrome = new Part();
            var lights = new Part();
            var rear = new Part();
            var amber = new Part();
            var plate = new Part();

            BuildBody(paint);
            BuildCabinAndPillars(paint, glass, trim);
            BuildWheelArches(paint, trim);
            BuildExteriorDetails(trim, chrome, lights, rear, amber, plate);

            var meshes = new List<Mesh>();
            Material paintMaterial = Paint("M_TaxiPaint", new Color(0.96f, 0.42f, 0.04f), 0.35f, 0.82f);
            Material glassMaterial = Paint("M_TaxiGlass", new Color(0.06f, 0.08f, 0.11f), 0.28f, 0.96f);
            Material trimMaterial = Paint("M_TaxiTrim", new Color(0.08f, 0.08f, 0.09f), 0.05f, 0.32f);
            Material chromeMaterial = Paint("M_TaxiChrome", new Color(0.82f, 0.84f, 0.87f), 0.92f, 0.88f);
            Material plateMaterial = Paint("M_TaxiPlate", new Color(0.92f, 0.90f, 0.84f), 0.05f, 0.25f);

            AddObject(graphics, "Carrosserie", paint, paintMaterial, meshes, true, 52f);
            AddObject(graphics, "Vitres", glass, glassMaterial, meshes, true, 45f);
            AddObject(graphics, "Plastiques", trim, trimMaterial, meshes, false, 35f);
            AddObject(graphics, "Chrome", chrome, chromeMaterial, meshes, false, 45f);
            AddObject(graphics, "Plaques", plate, plateMaterial, meshes, false, 20f);
            AddObject(graphics, "Phares", lights, CityMaterials.NightLight("M_TaxiHeadlight", new Color(1f, 0.96f, 0.88f), 1.0f, 5.5f, null, false), meshes, false, 30f);
            AddObject(graphics, "Feux", rear, CityMaterials.NightLight("M_TaxiTaillight", new Color(1f, 0.08f, 0.05f), 0.6f, 2.8f, null, false), meshes, false, 30f);
            AddObject(graphics, "Clignotants", amber, CityMaterials.NightLight("M_TaxiAmber", new Color(1f, 0.55f, 0.05f), 0.5f, 1.8f, null, false), meshes, false, 30f);

            // Lumignon « TAXI » profilé sur le toit (masqué sur les voitures personnelles par VehicleMotor).
            var sign = new Part();
            AddLoftedSign(sign, new Vector3(0f, 1.47f, 0.02f), 0.58f, 0.44f, 0.16f, 0.24f, 0.14f);
            AddObject(graphics, "Lumignon", sign, CityMaterials.NightLight("M_TaxiSign", new Color(1f, 0.85f, 0.35f), 1.2f, 3.2f, null, false), meshes, false, 25f);

            // Roues détaillées : pneu chanfreiné + disque sombre + jante alliage 5 branches.
            Material tyre = Paint("M_Tyre", new Color(0.06f, 0.06f, 0.07f), 0f, 0.18f);
            Material rim = Paint("M_TaxiRim", new Color(0.76f, 0.78f, 0.81f), 0.90f, 0.72f);
            Material brake = Paint("M_TaxiBrake", new Color(0.18f, 0.19f, 0.20f), 0.6f, 0.4f);
            Mesh tyreMesh = BuildTyreMesh(WheelRadius, WheelWidth);
            Mesh rimMesh = BuildAlloyRimMesh(0.225f, WheelWidth);
            Mesh brakeMesh = BuildBrakeDiscMesh(0.16f, 0.04f);
            meshes.Add(tyreMesh);
            meshes.Add(rimMesh);
            meshes.Add(brakeMesh);

            foreach (Vector3 position in new[]
            {
                new Vector3(-WheelX, WheelRadius, FrontAxleZ), new Vector3(WheelX, WheelRadius, FrontAxleZ),
                new Vector3(-WheelX, WheelRadius, RearAxleZ),  new Vector3(WheelX, WheelRadius, RearAxleZ),
            })
            {
                var wheel = new GameObject("Roue");
                wheel.transform.SetParent(graphics, false);
                wheel.transform.localPosition = position;
                wheel.transform.localRotation = Quaternion.Euler(0f, position.x < 0f ? 180f : 0f, 0f);
                AddRenderer(wheel.transform, "Pneu", tyreMesh, tyre, true);
                AddRenderer(wheel.transform, "Jante", rimMesh, rim, false);
                AddRenderer(wheel.transform, "Disque", brakeMesh, brake, false);
            }

            AssetDatabase.DeleteAsset(MeshAssetPath);
            AssetDatabase.CreateAsset(meshes[0], MeshAssetPath);
            for (int i = 1; i < meshes.Count; i++)
                AssetDatabase.AddObjectToAsset(meshes[i], meshes[0]);
        }

        #region Caisse et habitacle

        private static Vector3[] BodyRing((float z, float hwBot, float hwMid, float bot, float top) s)
        {
            float mid = Mathf.Lerp(s.bot, s.top, 0.45f);
            float crown = s.top + (s.z > 0.9f || s.z < -1.1f ? 0.025f : 0f);
            return new[]
            {
                new Vector3(-s.hwBot,        s.bot,        s.z),
                new Vector3(-s.hwMid * 0.99f, s.bot + 0.14f, s.z),
                new Vector3(-s.hwMid,         mid,          s.z),
                new Vector3(-s.hwMid * 0.96f, s.top - 0.07f, s.z),
                new Vector3(-s.hwMid * 0.82f, s.top,        s.z),
                new Vector3( 0f,              crown,        s.z),
                new Vector3( s.hwMid * 0.82f, s.top,        s.z),
                new Vector3( s.hwMid * 0.96f, s.top - 0.07f, s.z),
                new Vector3( s.hwMid,         mid,          s.z),
                new Vector3( s.hwMid * 0.99f, s.bot + 0.14f, s.z),
                new Vector3( s.hwBot,         s.bot,        s.z),
            };
        }

        private static void BuildBody(Part paint)
        {
            var rings = new List<Vector3[]>(BodySections.Length);
            foreach (var s in BodySections)
                rings.Add(BodyRing(s));
            Loft(paint, rings, true, null);
            Cap(paint, rings[0], Vector3.back);
            Cap(paint, rings[rings.Count - 1], Vector3.forward);

            // Petit becquet sur le bord arrière du coffre.
            AddBox(paint, new Vector3(0f, 0.955f, -2.06f), new Vector3(1.56f, 0.035f, 0.10f));
        }

        private static void BuildCabinAndPillars(Part paint, Part glass, Part trim)
        {
            var rings = new List<Vector3[]>(CabinSections.Length);
            foreach (Vector4 s in CabinSections)
            {
                float z = s.x, hwB = s.y, hwT = s.z, top = s.w;
                rings.Add(new[]
                {
                    new Vector3(-hwB, Belt, z),
                    new Vector3(-hwT, top - 0.05f, z),
                    new Vector3(-hwT * 0.86f, top, z),
                    new Vector3(0f, top + 0.025f, z),
                    new Vector3(hwT * 0.86f, top, z),
                    new Vector3(hwT, top - 0.05f, z),
                    new Vector3(hwB, Belt, z),
                });
            }

            // Toit (arêtes 1..4 sur le segment central) en tôle peinte, vitrage autour.
            Loft(glass, rings, false, (segment, edge) => segment == 1 && edge >= 1 && edge <= 4 ? paint : null);

            // Montants A (avant), B (milieu), C (arrière) et linteau latéral de toit pour encadrer les vitres.
            Vector3 pA_Bot = new Vector3(0.79f, Belt, 1.07f), pA_Top = new Vector3(0.625f, 1.44f, 0.40f);
            Vector3 pC_Bot = new Vector3(0.79f, Belt, -1.12f), pC_Top = new Vector3(0.635f, 1.43f, -0.62f);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 mirror = new Vector3(side, 1f, 1f);
                AddStrut(paint, Vector3.Scale(pA_Bot, mirror), Vector3.Scale(pA_Top, mirror), 0.065f, 0.06f);
                AddStrut(paint, Vector3.Scale(pC_Bot, mirror), Vector3.Scale(pC_Top, mirror), 0.095f, 0.07f);
                // Montant B vertical sombre entre les portières avant et arrière.
                AddStrut(trim, new Vector3(side * 0.775f, Belt, -0.06f), new Vector3(side * 0.648f, 1.455f, -0.08f), 0.085f, 0.05f);
                // Baguette de toit peinte reliant A -> C.
                AddStrut(paint, new Vector3(side * 0.635f, 1.425f, -0.63f), new Vector3(side * 0.625f, 1.44f, 0.42f), 0.075f, 0.055f);
                // Joint noir de ceinture de caisse sous les vitres latérales.
                AddBox(trim, new Vector3(side * 0.825f, Belt + 0.01f, -0.03f), new Vector3(0.035f, 0.035f, 2.02f));
            }
            // Antenne aileron de requin sur l'arrière du toit.
            AddLoftedSign(trim, new Vector3(0f, 1.475f, -0.78f), 0.05f, 0.02f, 0.075f, 0.16f, 0.06f);
        }

        private static void BuildWheelArches(Part paint, Part trim)
        {
            const int arcSteps = 8;
            const float rOut = 0.43f, rIn = 0.37f;
            foreach (float side in new[] { -1f, 1f })
            {
                foreach (float cz in new[] { FrontAxleZ, RearAxleZ })
                {
                    float xOut = side * 0.915f;
                    float xIn = side * 0.855f;
                    for (int i = 0; i < arcSteps; i++)
                    {
                        float a0 = Mathf.Lerp(15f, 165f, i / (float)arcSteps) * Mathf.Deg2Rad;
                        float a1 = Mathf.Lerp(15f, 165f, (i + 1) / (float)arcSteps) * Mathf.Deg2Rad;
                        Vector3 o0 = new Vector3(xOut, WheelRadius + Mathf.Sin(a0) * rOut, cz + Mathf.Cos(a0) * rOut);
                        Vector3 o1 = new Vector3(xOut, WheelRadius + Mathf.Sin(a1) * rOut, cz + Mathf.Cos(a1) * rOut);
                        Vector3 i0 = new Vector3(xIn, WheelRadius + Mathf.Sin(a0) * rIn, cz + Mathf.Cos(a0) * rIn);
                        Vector3 i1 = new Vector3(xIn, WheelRadius + Mathf.Sin(a1) * rIn, cz + Mathf.Cos(a1) * rIn);
                        // Bande peinte d'aile élargie + liseré sombre intérieur.
                        AddQuad(paint, o0, i0, i1, o1, new Vector3(side, 0f, 0f));
                        Vector3 wall0 = new Vector3(xIn, WheelRadius + Mathf.Sin(a0) * rIn, cz + Mathf.Cos(a0) * rIn);
                        Vector3 wall1 = new Vector3(xIn, WheelRadius + Mathf.Sin(a1) * rIn, cz + Mathf.Cos(a1) * rIn);
                        Vector3 deep0 = new Vector3(side * 0.68f, wall0.y, wall0.z);
                        Vector3 deep1 = new Vector3(side * 0.68f, wall1.y, wall1.z);
                        AddQuad(trim, wall0, deep0, deep1, wall1, Vector3.down);
                    }
                }
            }
        }

        private static void BuildExteriorDetails(Part trim, Part chrome, Part lights, Part rear, Part amber, Part plate)
        {
            // Pare-chocs avant & arrière sculptés (en relief + lame basse + jonc chromé).
            AddBox(trim, new Vector3(0f, 0.36f, 2.21f), new Vector3(1.60f, 0.18f, 0.11f));
            AddBox(trim, new Vector3(0f, 0.25f, 2.23f), new Vector3(1.50f, 0.05f, 0.09f));
            AddBox(chrome, new Vector3(0f, 0.44f, 2.26f), new Vector3(1.42f, 0.025f, 0.02f));

            AddBox(trim, new Vector3(0f, 0.37f, -2.21f), new Vector3(1.62f, 0.20f, 0.11f));
            AddBox(trim, new Vector3(0f, 0.26f, -2.22f), new Vector3(1.46f, 0.06f, 0.08f));
            AddBox(chrome, new Vector3(0f, 0.46f, -2.26f), new Vector3(1.46f, 0.025f, 0.02f));

            // Calandre avant avec entourage chromé, lames et sigle central.
            AddBox(trim, new Vector3(0f, 0.56f, 2.205f), new Vector3(0.62f, 0.16f, 0.05f));
            AddBox(chrome, new Vector3(0f, 0.64f, 2.225f), new Vector3(0.64f, 0.025f, 0.02f));
            AddBox(chrome, new Vector3(0f, 0.48f, 2.225f), new Vector3(0.64f, 0.025f, 0.02f));
            AddBox(chrome, new Vector3(0f, 0.56f, 2.23f), new Vector3(0.11f, 0.09f, 0.02f));
            AddBox(chrome, new Vector3(0f, 0.56f, 2.225f), new Vector3(0.56f, 0.02f, 0.02f));

            // Plaques d'immatriculation avant et arrière.
            AddBox(plate, new Vector3(0f, 0.36f, 2.268f), new Vector3(0.46f, 0.105f, 0.015f));
            AddBox(plate, new Vector3(0f, 0.56f, -2.20f), new Vector3(0.46f, 0.105f, 0.02f));
            AddBox(chrome, new Vector3(0f, 0.77f, -2.195f), new Vector3(0.54f, 0.025f, 0.025f)); // baguette coffre

            // Phares avant, feux de jour, antibrouillards et clignotants d'angle.
            foreach (float side in new[] { -1f, 1f })
            {
                AddBox(trim, new Vector3(side * 0.54f, 0.59f, 2.195f), new Vector3(0.38f, 0.14f, 0.04f));
                AddBox(lights, new Vector3(side * 0.52f, 0.60f, 2.215f), new Vector3(0.30f, 0.10f, 0.025f));
                AddBox(amber, new Vector3(side * 0.71f, 0.59f, 2.205f), new Vector3(0.08f, 0.10f, 0.03f));
                AddBox(lights, new Vector3(side * 0.58f, 0.35f, 2.255f), new Vector3(0.14f, 0.06f, 0.02f)); // antibrouillard

                // Blocs optiques arrière rouges + clignotant ambre + recul.
                AddBox(trim, new Vector3(side * 0.56f, 0.70f, -2.195f), new Vector3(0.40f, 0.17f, 0.04f));
                AddBox(rear, new Vector3(side * 0.54f, 0.72f, -2.215f), new Vector3(0.34f, 0.11f, 0.025f));
                AddBox(amber, new Vector3(side * 0.67f, 0.65f, -2.215f), new Vector3(0.11f, 0.045f, 0.025f));
                AddBox(lights, new Vector3(side * 0.45f, 0.65f, -2.215f), new Vector3(0.10f, 0.045f, 0.025f)); // feu recul
            }
            // Troisième feu stop sous la lunette arrière.
            AddBox(rear, new Vector3(0f, 1.03f, -1.14f), new Vector3(0.28f, 0.035f, 0.03f));

            // Rétroviseurs sur bras, baguettes de portes, poignées et doubles sorties d'échappement.
            foreach (float side in new[] { -1f, 1f })
            {
                AddBox(trim, new Vector3(side * 0.88f, 0.99f, 0.91f), new Vector3(0.08f, 0.04f, 0.05f));
                AddBox(trim, new Vector3(side * 0.97f, 1.02f, 0.90f), new Vector3(0.14f, 0.095f, 0.15f));
                // Baguette latérale de portière.
                AddBox(trim, new Vector3(side * 0.905f, 0.56f, -0.02f), new Vector3(0.025f, 0.045f, 1.78f));
                // Poignées de porte avant et arrière.
                AddBox(chrome, new Vector3(side * 0.905f, 0.84f, 0.16f), new Vector3(0.03f, 0.035f, 0.15f));
                AddBox(chrome, new Vector3(side * 0.905f, 0.84f, -0.62f), new Vector3(0.03f, 0.035f, 0.15f));
                // Clignotant latéral sur l'aile avant.
                AddBox(amber, new Vector3(side * 0.905f, 0.72f, 0.95f), new Vector3(0.02f, 0.035f, 0.08f));
            }
            // Double échappement chromé.
            foreach (float ex in new[] { -0.52f, -0.43f })
                AddBox(chrome, new Vector3(ex, 0.25f, -2.26f), new Vector3(0.055f, 0.055f, 0.12f));
            // Essuie-glaces à la base du pare-brise.
            AddBox(trim, new Vector3(-0.22f, 0.98f, 1.12f), new Vector3(0.42f, 0.025f, 0.03f));
            AddBox(trim, new Vector3(0.26f, 0.98f, 1.12f), new Vector3(0.42f, 0.025f, 0.03f));
        }

        private static void AddLoftedSign(Part part, Vector3 baseCenter, float wBot, float wTop, float dBot, float dTop, float h)
        {
            float xb = wBot * 0.5f, xt = wTop * 0.5f, zb = dBot * 0.5f, zt = dTop * 0.5f;
            Vector3 b0 = baseCenter + new Vector3(-xb, 0f, -zb), b1 = baseCenter + new Vector3(-xb, 0f, zb);
            Vector3 b2 = baseCenter + new Vector3(xb, 0f, zb),  b3 = baseCenter + new Vector3(xb, 0f, -zb);
            Vector3 t0 = baseCenter + new Vector3(-xt, h, -zt), t1 = baseCenter + new Vector3(-xt, h, zt);
            Vector3 t2 = baseCenter + new Vector3(xt, h, zt),   t3 = baseCenter + new Vector3(xt, h, -zt);
            AddQuad(part, b0, t0, t1, b1, Vector3.left);
            AddQuad(part, b2, t2, t3, b3, Vector3.right);
            AddQuad(part, b1, t1, t2, b2, Vector3.forward);
            AddQuad(part, b3, t3, t0, b0, Vector3.back);
            AddQuad(part, t0, t3, t2, t1, Vector3.up);
            AddQuad(part, b0, b1, b2, b3, Vector3.down);
        }

        private static void AddStrut(Part part, Vector3 from, Vector3 to, float thicknessX, float thicknessZ)
        {
            Vector3 dir = to - from;
            if (dir.sqrMagnitude < 1e-6f)
                return;
            Vector3 mid = (from + to) * 0.5f;
            Quaternion q = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            Vector3 hx = q * new Vector3(thicknessX * 0.5f, 0f, 0f);
            Vector3 hy = q * new Vector3(0f, dir.magnitude * 0.5f + 0.01f, 0f);
            Vector3 hz = q * new Vector3(0f, 0f, thicknessZ * 0.5f);
            AddQuad(part, mid - hx - hy - hz, mid - hx + hy - hz, mid + hx + hy - hz, mid + hx - hy - hz, q * Vector3.back);
            AddQuad(part, mid + hx - hy + hz, mid + hx + hy + hz, mid - hx + hy + hz, mid - hx - hy + hz, q * Vector3.forward);
            AddQuad(part, mid - hx - hy + hz, mid - hx + hy + hz, mid - hx + hy - hz, mid - hx - hy - hz, q * Vector3.left);
            AddQuad(part, mid + hx - hy - hz, mid + hx + hy - hz, mid + hx + hy + hz, mid + hx - hy + hz, q * Vector3.right);
        }

        /// <summary>
        /// Relie des anneaux successifs par des quadrilatères (sommets partagés le long d'une même
        /// arête : ombrage lisse). <paramref name="redirect"/> envoie un quadrilatère vers une autre partie.
        /// </summary>
        private static void Loft(Part part, List<Vector3[]> rings, bool closed, System.Func<int, int, Part> redirect)
        {
            int count = rings[0].Length;
            int edges = closed ? count : count - 1;
            for (int r = 0; r < rings.Count - 1; r++)
            {
                Vector3 axis = Vector3.zero;
                foreach (Vector3 p in rings[r]) axis += p;
                axis /= count;

                for (int e = 0; e < edges; e++)
                {
                    Part target = redirect?.Invoke(r, e) ?? part;
                    Vector3 a = rings[r][e], b = rings[r][(e + 1) % count];
                    Vector3 c = rings[r + 1][(e + 1) % count], d = rings[r + 1][e];
                    Vector3 outward = (a + b) * 0.5f - new Vector3(axis.x, axis.y, a.z);
                    AddQuad(target, a, b, c, d, outward);
                }
            }
        }

        private static void Cap(Part part, Vector3[] ring, Vector3 normal)
        {
            Vector3 center = Vector3.zero;
            foreach (Vector3 p in ring) center += p;
            center /= ring.Length;
            for (int i = 0; i < ring.Length; i++)
                AddTriangle(part, center, ring[i], ring[(i + 1) % ring.Length], normal);
        }

        #endregion

        #region Primitives et roues

        private static void AddQuad(Part part, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            AddTriangle(part, a, b, c, outward);
            AddTriangle(part, a, c, d, outward);
        }

        /// <summary>Triangle orienté vers <paramref name="outward"/> (sens horaire vu de l'extérieur, convention Unity).</summary>
        private static void AddTriangle(Part part, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
                (b, c) = (c, b);
            int start = part.Vertices.Count;
            part.Vertices.Add(a); part.Vertices.Add(b); part.Vertices.Add(c);
            part.Triangles.Add(start); part.Triangles.Add(start + 1); part.Triangles.Add(start + 2);
        }

        private static void AddBox(Part part, Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] normals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (Vector3 n in normals)
            {
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                Vector3 c = center + Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
                AddQuad(part, c - du - dv, c - du + dv, c + du + dv, c + du - dv, n);
            }
        }

        /// <summary>Pneu chanfreiné (20 segments, bande de roulement et flancs biseautés), axe X.</summary>
        private static Mesh BuildTyreMesh(float radius, float width)
        {
            const int segments = 20;
            var part = new Part();
            float hw = width * 0.5f, treadHalf = hw * 0.78f, rimR = radius * 0.65f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0));
                Vector3 d1 = new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1));
                Vector3 mid = (d0 + d1) * 0.5f;

                Vector3 t0 = d0 * radius, t1 = d1 * radius;
                Vector3 s0 = d0 * (radius * 0.93f), s1 = d1 * (radius * 0.93f);
                Vector3 r0 = d0 * rimR, r1 = d1 * rimR;

                // Bande de roulement + deux chanfreins d'épaule + flancs jusqu'au bord de jante.
                AddQuad(part, t0 - Vector3.right * treadHalf, t0 + Vector3.right * treadHalf, t1 + Vector3.right * treadHalf, t1 - Vector3.right * treadHalf, mid);
                AddQuad(part, s0 + Vector3.right * hw, t0 + Vector3.right * treadHalf, t1 + Vector3.right * treadHalf, s1 + Vector3.right * hw, (mid + Vector3.right).normalized);
                AddQuad(part, t0 - Vector3.right * treadHalf, s0 - Vector3.right * hw, s1 - Vector3.right * hw, t1 - Vector3.right * treadHalf, (mid - Vector3.right).normalized);
                AddQuad(part, r0 + Vector3.right * (hw * 0.96f), s0 + Vector3.right * hw, s1 + Vector3.right * hw, r1 + Vector3.right * (hw * 0.96f), Vector3.right);
                AddQuad(part, s0 - Vector3.right * hw, r0 - Vector3.right * (hw * 0.96f), r1 - Vector3.right * (hw * 0.96f), s1 - Vector3.right * hw, Vector3.left);
            }
            return ToMesh(part, "Taxi_Pneu", 48f);
        }

        /// <summary>Jante alliage à 5 branches, rebord extérieur et moyeu central (+X vers l'extérieur).</summary>
        private static Mesh BuildAlloyRimMesh(float radius, float wheelWidth)
        {
            const int segments = 15;
            var part = new Part();
            float xOut = wheelWidth * 0.49f;
            float xHub = xOut - 0.025f;
            float rIn = radius * 0.86f;

            // Cercle de jante extérieur.
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)), d1 = new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1));
                AddQuad(part, d0 * rIn + Vector3.right * xOut, d0 * radius + Vector3.right * xOut, d1 * radius + Vector3.right * xOut, d1 * rIn + Vector3.right * xOut, Vector3.right);
            }
            // Moyeu central + 5 branches radiales.
            const float hubR = 0.065f;
            for (int i = 0; i < 10; i++)
            {
                float a0 = i * Mathf.PI * 2f / 10f, a1 = (i + 1) * Mathf.PI * 2f / 10f;
                Vector3 p0 = new Vector3(xHub + 0.022f, Mathf.Sin(a0) * hubR, Mathf.Cos(a0) * hubR);
                Vector3 p1 = new Vector3(xHub + 0.022f, Mathf.Sin(a1) * hubR, Mathf.Cos(a1) * hubR);
                AddTriangle(part, new Vector3(xHub + 0.028f, 0f, 0f), p0, p1, Vector3.right);
            }
            for (int s = 0; s < 5; s++)
            {
                float a = s * Mathf.PI * 2f / 5f;
                Vector3 radial = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                Vector3 tangent = new Vector3(0f, -Mathf.Cos(a), Mathf.Sin(a));
                Vector3 root = radial * (hubR * 0.7f) + Vector3.right * (xHub + 0.015f);
                Vector3 tip = radial * (rIn + 0.01f) + Vector3.right * (xOut - 0.002f);
                Vector3 w0 = tangent * 0.022f, w1 = tangent * 0.017f;
                AddQuad(part, root - w0, tip - w1, tip + w1, root + w0, Vector3.right);
                AddQuad(part, root - w0, root - w0 - Vector3.right * 0.025f, tip - w1 - Vector3.right * 0.025f, tip - w1, -tangent);
                AddQuad(part, tip + w1, tip + w1 - Vector3.right * 0.025f, root + w0 - Vector3.right * 0.025f, root + w0, tangent);
            }
            return ToMesh(part, "Taxi_Jante", 30f);
        }

        private static Mesh BuildBrakeDiscMesh(float radius, float thickness)
        {
            const int segments = 12;
            var part = new Part();
            float x = 0.055f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = new Vector3(x, Mathf.Sin(a0) * radius, Mathf.Cos(a0) * radius);
                Vector3 p1 = new Vector3(x, Mathf.Sin(a1) * radius, Mathf.Cos(a1) * radius);
                AddTriangle(part, new Vector3(x, 0f, 0f), p0, p1, Vector3.right);
            }
            return ToMesh(part, "Taxi_Disque", 0f);
        }

        /// <summary>
        /// Maillage final. Les sommets proches sont soudés quand l'angle entre faces est faible
        /// (<paramref name="smoothAngle"/>) : carrosserie lisse, arêtes vives conservées.
        /// </summary>
        private static Mesh ToMesh(Part part, string name, float smoothAngle)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(part.Vertices);
            mesh.SetTriangles(part.Triangles, 0);
            mesh.SetNormals(SmoothNormals(part, smoothAngle));
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Normales lissées par position : chaque sommet moyenne les normales des faces qui le touchent
        /// et qui forment avec la sienne un angle inférieur à <paramref name="smoothAngle"/>.
        /// </summary>
        private static List<Vector3> SmoothNormals(Part part, float smoothAngle)
        {
            int triangleCount = part.Triangles.Count / 3;
            var faceNormals = new Vector3[triangleCount];
            var byPosition = new Dictionary<Vector3Int, List<int>>();
            for (int t = 0; t < triangleCount; t++)
            {
                Vector3 a = part.Vertices[part.Triangles[t * 3]], b = part.Vertices[part.Triangles[t * 3 + 1]], c = part.Vertices[part.Triangles[t * 3 + 2]];
                faceNormals[t] = Vector3.Cross(b - a, c - a).normalized;
                for (int k = 0; k < 3; k++)
                {
                    Vector3Int key = Quantize(part.Vertices[part.Triangles[t * 3 + k]]);
                    if (!byPosition.TryGetValue(key, out List<int> list))
                        byPosition[key] = list = new List<int>();
                    list.Add(t);
                }
            }

            float cosLimit = Mathf.Cos(smoothAngle * Mathf.Deg2Rad);
            var normals = new List<Vector3>(new Vector3[part.Vertices.Count]);
            for (int t = 0; t < triangleCount; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int vertex = part.Triangles[t * 3 + k];
                    Vector3 sum = Vector3.zero;
                    foreach (int other in byPosition[Quantize(part.Vertices[vertex])])
                    {
                        if (Vector3.Dot(faceNormals[other], faceNormals[t]) >= cosLimit)
                            sum += faceNormals[other];
                    }
                    normals[vertex] = sum.sqrMagnitude > 0f ? sum.normalized : faceNormals[t];
                }
            }
            return normals;
        }

        private static Vector3Int Quantize(Vector3 p)
        {
            return new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));
        }

        #endregion

        #region Objets et matériaux

        private static void AddObject(Transform parent, string name, Part part, Material material, List<Mesh> meshes, bool shadows, float smoothAngle)
        {
            if (part.Vertices.Count == 0)
                return;
            Mesh mesh = ToMesh(part, "Taxi_" + name, smoothAngle);
            meshes.Add(mesh);
            AddRenderer(parent, name, mesh, material, shadows);
        }

        private static void AddRenderer(Transform parent, string name, Mesh mesh, Material material, bool shadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        private static Material Paint(string name, Color color, float metallic, float smoothness)
        {
            Material material = CityMaterials.Plain(name, color, smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        #endregion
    }
}
