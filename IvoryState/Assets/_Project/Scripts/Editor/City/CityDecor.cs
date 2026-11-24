using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Mobilier urbain et vie des rues : lampadaires, poteaux électriques et fils, marquages au sol,
    /// cocotiers, maquis (tables et chaises), bric-à-brac des trottoirs.
    /// <para>
    /// Mobile : tout ce qui est métallique est fusionné dans le maillage de façades du bloc (zéro draw
    /// call de plus) ; ampoules et halos lumineux partagent un maillage par bloc ; les objets Poly
    /// Haven et les cocotiers disparaissent quand ils deviennent minuscules à l'écran.
    /// </para>
    /// </summary>
    internal static class CityDecor
    {
        public const string PoolTexturePath = "Assets/_Project/Art/Generated/T_LightPool.png";

        private static readonly Color LampMetal = new Color(0.30f, 0.31f, 0.32f);
        private static readonly Color WoodPole = new Color(0.36f, 0.27f, 0.19f);
        private static readonly Color Wire = new Color(0.05f, 0.05f, 0.05f);

        private static readonly string[] Clutter =
        {
            "trashbag", "old_tyre", "plastic_jerrycan", "barrel_03", "Barrel_02", "cardboard_box_01", "plastic_container", "plastic_crate_01", "metal_trash_can",
        };

        /// <summary>Têtes de lampadaires (monde ; w = 0 LED, 1 sodium), pour l'éclairage réel de nuit.</summary>
        public static readonly List<Vector4> LampHeads = new List<Vector4>();

        /// <summary>Repère local → monde en cours de décoration (bloc ou place, éventuellement tourné).</summary>
        public static Matrix4x4 Frame = Matrix4x4.identity;

        /// <summary>Pieds des lampadaires et poteaux (monde ; w = rayon), pour leurs colliders.</summary>
        public static readonly List<Vector4> PoleBases = new List<Vector4>();

        public static Material BulbMaterial { get; private set; }
        public static Material PoolMaterial { get; private set; }
        public static Material SodiumBulbMaterial { get; private set; }

        public static void Prepare()
        {
            Texture2D pool = BuildPoolTexture();
            BulbMaterial = CityMaterials.NightLight("M_LampBulb", new Color(1f, 0.93f, 0.8f), 0.6f, 6f, null, false);
            SodiumBulbMaterial = CityMaterials.NightLight("M_LampBulbSodium", new Color(1f, 0.62f, 0.25f), 0.5f, 5f, null, false);
            PoolMaterial = CityMaterials.NightLight("M_LightPool", new Color(1f, 0.70f, 0.40f), 0f, 2.8f, pool, true);
            BlenderCityKit.Prepare();
            PalmBuilder.Prepare();
            PropLibrary.ClearCache();
            LampHeads.Clear();
            PoleBases.Clear();
            Frame = Matrix4x4.identity;
        }

        #region Lampadaires et poteaux

        /// <summary>
        /// Lampadaire Blender réaliste (simple ou double crosse), bras tendu vers la chaussée (<paramref name="toRoad"/>).
        /// Tamponné dans <paramref name="chunkBuilders"/> pour conserver un draw call par matériau et tronçon.
        /// </summary>
        public static void AddStreetLampChunked(Vector2Int chunk,
            Dictionary<(Vector2Int, Material), BoxMeshBuilder> chunkBuilders,
            BoxMeshBuilder bulbs, BoxMeshBuilder pools,
            Vector3 basePosition, Vector3 toRoad, float sidewalkHeight, bool doubleArm = false)
        {
            Vector3 dir = new Vector3(toRoad.x, 0f, toRoad.z);
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Matrix4x4 matrix = Matrix4x4.TRS(basePosition, rot, Vector3.one);

            string model = doubleArm ? "SM_StreetLamp_Double" : "SM_StreetLamp_Single";
            BlenderCityKit.StampChunked(model, chunk, chunkBuilders, matrix, null, Color.white, 0,
                ledBulbBuilder: bulbs);

            RegisterStreetLampLights(bulbs, pools, basePosition, dir, sidewalkHeight, doubleArm);
        }

        /// <summary>Variante locale (Place du Plateau, blocs spéciaux) regroupée dans <paramref name="builders"/>.</summary>
        public static void AddStreetLamp(Dictionary<Material, BoxMeshBuilder> builders,
            BoxMeshBuilder bulbs, BoxMeshBuilder pools,
            Vector3 basePosition, Vector3 toRoad, float sidewalkHeight, bool doubleArm = false)
        {
            Vector3 dir = new Vector3(toRoad.x, 0f, toRoad.z);
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Matrix4x4 matrix = Matrix4x4.TRS(basePosition, rot, Vector3.one);

            string model = doubleArm ? "SM_StreetLamp_Double" : "SM_StreetLamp_Single";
            BlenderCityKit.Stamp(model, builders, matrix, null, Color.white, 0, ledBulbBuilder: bulbs);

            RegisterStreetLampLights(bulbs, pools, basePosition, dir, sidewalkHeight, doubleArm);
        }

        private static void RegisterStreetLampLights(BoxMeshBuilder bulbs, BoxMeshBuilder pools,
            Vector3 basePosition, Vector3 toRoad, float sidewalkHeight, bool doubleArm)
        {
            Vector3 foot = Frame.MultiplyPoint3x4(basePosition);
            PoleBases.Add(new Vector4(foot.x, foot.y, foot.z, 0.16f));

            float reach = doubleArm ? 1.72f : 1.80f;
            Vector3 headCenter = basePosition + toRoad * reach + Vector3.up * 7.52f;
            Vector3 world = Frame.MultiplyPoint3x4(headCenter);
            LampHeads.Add(new Vector4(world.x, world.y, world.z, 0f));

            Vector3 onRoad = new Vector3(headCenter.x, 0.03f, headCenter.z) + toRoad * 0.8f;
            pools.AddFlatQuad(onRoad, Vector3.right, 11f, 11f, true);
            if (doubleArm)
            {
                Vector3 backHead = basePosition - toRoad * reach + Vector3.up * 7.52f;
                Vector3 backOnRoad = new Vector3(backHead.x, 0.03f, backHead.z) - toRoad * 0.8f;
                pools.AddFlatQuad(backOnRoad, Vector3.right, 10f, 10f, true);
            }
            pools.AddFlatQuad(new Vector3(basePosition.x, sidewalkHeight + 0.02f, basePosition.z), Vector3.right, 6f, 6f, true);
        }

        /// <summary>
        /// Lampadaire moderne au bord du trottoir (repli procédural si un appelant utilise l'ancienne signature).
        /// </summary>
        public static void AddStreetLamp(BoxMeshBuilder metal, BoxMeshBuilder bulbs, BoxMeshBuilder pools,
            Vector3 basePosition, Vector3 toRoad, float sidewalkHeight)
        {
            const float height = 7.5f, arm = 1.8f;
            metal.SetColor(LampMetal, 0);
            metal.AddCylinder(basePosition, 0.09f, height, 6);
            Vector3 foot = Frame.MultiplyPoint3x4(basePosition);
            PoleBases.Add(new Vector4(foot.x, foot.y, foot.z, 0.12f));
            Vector3 top = basePosition + Vector3.up * (height - 0.1f);
            Vector3 headCenter = top + toRoad * arm + Vector3.up * 0.25f;
            metal.AddBeam(top, headCenter, 0.08f);
            metal.AddBox(headCenter - Vector3.up * 0.06f, new Vector3(0.7f, 0.12f, 0.7f));

            bulbs.SetColor(Color.white, 0);
            bulbs.AddBox(headCenter - Vector3.up * 0.09f, new Vector3(0.5f, 0.03f, 0.5f));
            Vector3 world = Frame.MultiplyPoint3x4(headCenter);
            LampHeads.Add(new Vector4(world.x, world.y, world.z, 0f));

            // Halos : sur la chaussée sous la tête, et autour du pied sur le trottoir.
            Vector3 onRoad = new Vector3(headCenter.x, 0.03f, headCenter.z) + toRoad * 0.8f;
            pools.AddFlatQuad(onRoad, Vector3.right, 11f, 11f, true);
            pools.AddFlatQuad(new Vector3(basePosition.x, sidewalkHeight + 0.02f, basePosition.z), Vector3.right, 6f, 6f, true);
        }

        /// <summary>
        /// Poteau électrique Blender réaliste (collier béton, traverse métallique, isolateurs céramiques,
        /// coffret et crosse sodium optionnelle). Renvoie les deux points d'attache des fils.
        /// </summary>
        public static (Vector3, Vector3) AddPowerPole(Dictionary<Material, BoxMeshBuilder> builders,
            BoxMeshBuilder sodiumBulbs, BoxMeshBuilder pools, Vector3 basePosition, Vector3 along,
            Vector3 outward, bool hasLamp, float sidewalkHeight)
        {
            Vector3 dir = new Vector3(outward.x, 0f, outward.z);
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Matrix4x4 matrix = Matrix4x4.TRS(basePosition, rot, Vector3.one);

            BlenderCityKit.Stamp(hasLamp ? "SM_PowerPole" : "SM_PowerPole_NoLamp", builders, matrix,
                null, Color.white, 0, sodiumBulbBuilder: sodiumBulbs);

            return RegisterPowerPoleAnchors(pools, basePosition, along, dir, hasLamp, sidewalkHeight);
        }

        /// <summary>Variante tronçonnée (pour les rues d'Abidjan) du poteau électrique Blender.</summary>
        public static (Vector3, Vector3) AddPowerPoleChunked(Vector2Int chunk,
            Dictionary<(Vector2Int, Material), BoxMeshBuilder> chunkBuilders,
            BoxMeshBuilder sodiumBulbs, BoxMeshBuilder pools, Vector3 basePosition, Vector3 along,
            Vector3 outward, bool hasLamp, float sidewalkHeight)
        {
            Vector3 dir = new Vector3(outward.x, 0f, outward.z);
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Matrix4x4 matrix = Matrix4x4.TRS(basePosition, rot, Vector3.one);

            BlenderCityKit.StampChunked(hasLamp ? "SM_PowerPole" : "SM_PowerPole_NoLamp", chunk, chunkBuilders,
                matrix, null, Color.white, 0, sodiumBulbBuilder: sodiumBulbs);

            return RegisterPowerPoleAnchors(pools, basePosition, along, dir, hasLamp, sidewalkHeight);
        }

        private static (Vector3, Vector3) RegisterPowerPoleAnchors(BoxMeshBuilder pools,
            Vector3 basePosition, Vector3 along, Vector3 outward, bool hasLamp, float sidewalkHeight)
        {
            Vector3 foot = Frame.MultiplyPoint3x4(basePosition);
            PoleBases.Add(new Vector4(foot.x, foot.y, foot.z, 0.18f));
            if (hasLamp && pools != null)
            {
                Vector3 head = basePosition + Vector3.up * 6.32f + outward * 0.98f;
                Vector3 world = Frame.MultiplyPoint3x4(head);
                LampHeads.Add(new Vector4(world.x, world.y, world.z, 1f));
                pools.AddFlatQuad(new Vector3(head.x, 0.03f, head.z) + outward * 0.4f, Vector3.right, 9f, 9f, true);
                pools.AddFlatQuad(new Vector3(basePosition.x, sidewalkHeight + 0.02f, basePosition.z), Vector3.right, 5f, 5f, true);
            }
            Vector3 across = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.Cross(Vector3.up, outward).normalized;
            Vector3 crossCenter = basePosition + Vector3.up * 8.06f - outward * 0.14f;
            return (crossCenter - across * 0.70f, crossCenter + across * 0.70f);
        }

        /// <summary>Poteau électrique en bois avec traverse ; renvoie les deux points d'attache des fils.</summary>
        public static (Vector3, Vector3) AddPowerPole(BoxMeshBuilder metal, Vector3 basePosition, Vector3 along)
        {
            const float height = 8.5f;
            metal.SetColor(WoodPole, 0);
            metal.AddCylinder(basePosition, 0.12f, height, 6);
            Vector3 foot = Frame.MultiplyPoint3x4(basePosition);
            PoleBases.Add(new Vector4(foot.x, foot.y, foot.z, 0.15f));
            Vector3 across = Vector3.Cross(Vector3.up, along).normalized;
            Vector3 bar = basePosition + Vector3.up * (height - 0.6f);
            metal.AddBeam(bar - across * 0.8f, bar + across * 0.8f, 0.1f);
            return (bar - across * 0.7f + Vector3.up * 0.06f, bar + across * 0.7f + Vector3.up * 0.06f);
        }

        /// <summary>Fil électrique entre deux poteaux, avec une flèche (affaissement) au milieu.</summary>
        public static void AddWire(BoxMeshBuilder metal, Vector3 from, Vector3 to, float sag)
        {
            const int segments = 5;
            metal.SetColor(Wire, 0);
            Vector3 previous = from;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = Vector3.Lerp(from, to, t) - Vector3.up * (sag * 4f * t * (1f - t));
                metal.AddBeam(previous, point, 0.035f);
                previous = point;
            }
        }

        #endregion

        #region Marquages au sol

        /// <summary>
        /// Lignes discontinues au centre des rues et passages piétons aux carrefours. Pas de marquage
        /// sur la lagune (sauf sur les ponts).
        /// </summary>
        public static void AddRoadMarkings(BoxMeshBuilder markings, int gridRadius, float cellSize, float roadWidth,
            System.Func<float, float, bool> isWater)
        {
            const float y = 0.02f, dash = 3f, gap = 5f, lineWidth = 0.15f;
            float extent = (gridRadius + 0.5f) * cellSize;
            markings.SetColor(Color.white, 0);

            for (int i = -gridRadius; i < gridRadius; i++)
            {
                float road = (i + 0.5f) * cellSize;
                for (float s = -extent + dash; s < extent - dash; s += dash + gap)
                {
                    if (NearIntersection(s, gridRadius, cellSize, roadWidth))
                        continue;
                    if (!isWater(road, s))
                        markings.AddFlatQuad(new Vector3(road, y, s), Vector3.forward, dash, lineWidth);
                    if (!isWater(s, road))
                        markings.AddFlatQuad(new Vector3(s, y, road), Vector3.right, dash, lineWidth);
                }
            }

            // Passages piétons : bandes de 0,5 m parallèles à la circulation, 4 par carrefour.
            for (int i = -gridRadius; i < gridRadius; i++)
            {
                for (int j = -gridRadius; j < gridRadius; j++)
                {
                    var center = new Vector3((i + 0.5f) * cellSize, y, (j + 0.5f) * cellSize);
                    float offset = roadWidth * 0.5f + 2.2f;
                    AddCrosswalk(markings, center + Vector3.forward * offset, Vector3.forward, roadWidth, isWater);
                    AddCrosswalk(markings, center - Vector3.forward * offset, Vector3.forward, roadWidth, isWater);
                    AddCrosswalk(markings, center + Vector3.right * offset, Vector3.right, roadWidth, isWater);
                    AddCrosswalk(markings, center - Vector3.right * offset, Vector3.right, roadWidth, isWater);
                }
            }
        }

        private static void AddCrosswalk(BoxMeshBuilder markings, Vector3 center, Vector3 traffic, float roadWidth,
            System.Func<float, float, bool> isWater)
        {
            if (isWater(center.x, center.z))
                return;
            Vector3 across = Vector3.Cross(Vector3.up, traffic).normalized;
            for (float t = -roadWidth * 0.5f + 1f; t <= roadWidth * 0.5f - 1f; t += 1f)
                markings.AddFlatQuad(center + across * t, traffic, 3f, 0.5f);
        }

        private static bool NearIntersection(float s, int gridRadius, float cellSize, float roadWidth)
        {
            float local = Mathf.Repeat(s + cellSize * 0.5f, cellSize);
            float distance = Mathf.Min(local, cellSize - local);
            return distance < roadWidth * 0.5f + 6f;
        }

        #endregion

        #region Maquis et trottoirs

        /// <summary>
        /// Terrasse de maquis autour d'un étal : tables rondes, chaises monobloc, casiers de boissons.
        /// <paramref name="along"/> = direction du trottoir, <paramref name="inward"/> = vers les immeubles.
        /// </summary>
        public static void AddMaquis(Transform parent, Vector3 stall, Vector3 along, Vector3 inward, System.Random random)
        {
            for (int t = 0; t < 2; t++)
            {
                Vector3 table = stall + along * (3.2f + t * 2.8f) + inward * 0.3f;
                PropLibrary.Place("round_wooden_table_01", parent, table, random.Next(360), 1f, true, 0.02f);
                int chairs = 3 + random.Next(2);
                bool useStool = t == 1 || random.Next(2) == 0;
                for (int c = 0; c < chairs; c++)
                {
                    float angle = (c / (float)chairs + (float)random.NextDouble() * 0.1f) * 360f;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (useStool ? 0.78f : 0.85f);
                    // Tabouret en bois léger (676 tris) ou chaise monobloc tournée vers la table.
                    PropLibrary.Place(useStool ? "painted_wooden_stool" : "plastic_monobloc_chair_01",
                        parent, table + offset, angle + 180f + random.Next(-20, 20), 1f, false, 0.02f);
                }
            }

            // Casiers / bacs empilés, fût d'eau et ardoise de menu devant le maquis.
            Vector3 crates = stall + inward * 1.1f - along * 1.2f;
            int stack = 2 + random.Next(3);
            for (int i = 0; i < stack; i++)
                PropLibrary.Place(i == 0 ? "plastic_crate_01" : "plastic_container", parent, crates + Vector3.up * (i * 0.32f), random.Next(-8, 8), 1f, false, 0.02f);
            float roadYaw = Quaternion.LookRotation(-inward, Vector3.up).eulerAngles.y;
            PropLibrary.Place("standing_chalkboard_01", parent, stall - along * 1.45f - inward * 0.4f, roadYaw + 18f, 0.92f, false, 0.018f, false);
            PropLibrary.Place("Barrel_02", parent, stall + inward * 1.35f + along * 1.5f, random.Next(360), 1f, true, 0.02f);
        }

        /// <summary>Bric-à-brac le long des façades (sacs, pneus, bidons, fûts…) dans les quartiers populaires.</summary>
        public static void AddClutter(Transform parent, Vector3 position, System.Random random)
        {
            string id = Clutter[random.Next(Clutter.Length)];
            if (id == "old_tyre")
                PropLibrary.Place(id, parent, position + Vector3.up * 0.08f, random.Next(360), 1f, false, 0.03f, pitch: 90f);
            else
                PropLibrary.Place(id, parent, position, random.Next(360), 1f, false, 0.03f);
        }

        #endregion

        #region Texture des halos

        private static Texture2D BuildPoolTexture()
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float v = Mathf.Clamp01(1f - r);
                    byte b = (byte)(v * v * v * 255f);
                    pixels[y * size + x] = new Color32(b, b, b, 255);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            string fullPath = Path.GetFullPath(PoolTexturePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(PoolTexturePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(PoolTexturePath);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 128;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PoolTexturePath);
        }

        #endregion
    }
}
