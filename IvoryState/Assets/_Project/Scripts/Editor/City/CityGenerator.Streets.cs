using System.Collections.Generic;
using IvoryState.Gameplay.World;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Rues d'après les carrefours calculés par tools/citylayout/build_layout.py : passages piétons et feux
    /// tricolores par branche (à droite des voitures qui arrivent, tournés vers elles), îlots des ronds-points,
    /// et index des chaussées pour que le mobilier (lampadaires, abribus, panneaux…) ne tombe jamais sur une rue.
    /// </summary>
    internal static partial class CityGenerator
    {
        /// <summary>Segments des rues rangés par cases de 32 m : distance au bord de chaussée le plus proche.</summary>
        private sealed class RoadIndex
        {
            private const float Cell = 32f;
            private readonly Dictionary<Vector2Int, List<(Vector2 a, Vector2 b, float half, int road)>> _cells =
                new Dictionary<Vector2Int, List<(Vector2, Vector2, float, int)>>();

            public RoadIndex(CityLayout.Data layout)
            {
                for (int r = 0; r < layout.roads.Length; r++)
                {
                    CityLayout.RoadData road = layout.roads[r];
                    for (int i = 0; i + 3 < road.p.Length; i += 2)
                    {
                        var a = new Vector2(road.p[i], road.p[i + 1]);
                        var b = new Vector2(road.p[i + 2], road.p[i + 3]);
                        float half = road.width * 0.5f;
                        int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - half) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + half) / Cell);
                        int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - half) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + half) / Cell);
                        for (int x = x0; x <= x1; x++)
                        {
                            for (int z = z0; z <= z1; z++)
                            {
                                var key = new Vector2Int(x, z);
                                if (!_cells.TryGetValue(key, out var list))
                                    _cells[key] = list = new List<(Vector2, Vector2, float, int)>();
                                list.Add((a, b, half, r));
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Distance de <paramref name="p"/> au bord de la chaussée la plus proche (négative : sur la chaussée),
            /// en ignorant la rue <paramref name="ignoreRoad"/>. 99 au-delà de 32 m.
            /// </summary>
            public float Clearance(Vector3 p, int ignoreRoad = -1)
            {
                var q = new Vector2(p.x, p.z);
                float best = 99f;
                int cx = Mathf.FloorToInt(q.x / Cell), cz = Mathf.FloorToInt(q.y / Cell);
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    for (int z = cz - 1; z <= cz + 1; z++)
                    {
                        if (!_cells.TryGetValue(new Vector2Int(x, z), out var list))
                            continue;
                        foreach ((Vector2 a, Vector2 b, float half, int road) in list)
                        {
                            if (road == ignoreRoad)
                                continue;
                            Vector2 ab = b - a;
                            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                            best = Mathf.Min(best, Vector2.Distance(q, a + ab * t) - half);
                        }
                    }
                }
                return best;
            }
        }

        private static RoadIndex s_roadIndex;

        /// <summary>Prépare l'index des chaussées et la liste des carrefours (avant les routes et le mobilier).</summary>
        private static void PrepareStreets(CityLayout.Data layout)
        {
            s_roadIndex = new RoadIndex(layout);
            s_intersections.Clear();
            foreach (CityLayout.JunctionData junction in layout.junctions)
                s_intersections.Add(new RoadIntersection { Center = new Vector3(junction.x, 0f, junction.z), Radius = junction.r, Junction = junction });
        }

        /// <summary>Branches d'un carrefour : direction horizontale (depuis le centre), largeur, classe.</summary>
        private static IEnumerable<(Vector3 dir, float width, int cls)> Arms(CityLayout.JunctionData junction)
        {
            for (int i = 0; i + 3 < junction.a.Length; i += 4)
                yield return (new Vector3(junction.a[i], 0f, junction.a[i + 1]).normalized, junction.a[i + 2], (int)junction.a[i + 3]);
        }

        /// <summary>
        /// Passages piétons zébrés et lignes d'arrêt sur chaque branche des carrefours à feux et des carrefours
        /// d'un grand axe (hors ronds-points).
        /// </summary>
        private static void AddJunctionCrosswalks(Dictionary<Vector2Int, BoxMeshBuilder> lines, float markY)
        {
            foreach (RoadIntersection inter in s_intersections)
            {
                CityLayout.JunctionData j = inter.Junction;
                if (j == null || j.ring != 0)
                    continue;
                bool major = false;
                foreach (var arm in Arms(j))
                    major |= arm.cls <= 1;
                if (j.s == 0 && !major)
                    continue;
                foreach ((Vector3 dir, float width, int cls) in Arms(j))
                {
                    if (cls > 2)
                        continue;
                    Vector3 across = Vector3.Cross(Vector3.up, dir);
                    Vector3 walk = inter.Center + dir * (j.r + 1.7f);
                    if (CityLayout.IsWater(walk.x, walk.z))
                        continue;
                    BoxMeshBuilder lb = ChunkBuilder(lines, ChunkOf(walk.x, walk.z));
                    for (float t = -width * 0.5f + 0.9f; t <= width * 0.5f - 0.9f; t += 0.95f)
                        lb.AddFlatQuad(walk + across * t + Vector3.up * markY, dir, 2.5f, 0.44f);
                    // Ligne d'arrêt sur la voie qui arrive (à droite du conducteur, qui roule vers le centre).
                    Vector3 stop = inter.Center + dir * (j.r + 3.6f) - across * (width * 0.25f);
                    lb.AddFlatQuad(stop + Vector3.up * markY, across, width * 0.46f, 0.3f);
                }
            }
        }

        /// <summary>
        /// Feux tricolores des carrefours à feux (positions OpenStreetMap) : un poteau par branche, sur le
        /// trottoir à droite des voitures qui arrivent, juste avant le passage piéton, tourné vers elles.
        /// Deux phases : les branches alignées avec la première passent ensemble, les autres ensuite.
        /// </summary>
        private static void CreateLayoutTrafficLights(Transform root,
            Dictionary<(Vector2Int, Material), BoxMeshBuilder> chunkKit, List<Mesh> meshes)
        {
            if (!BlenderCityKit.HasModel("SM_TrafficLight"))
                return;

            var signalRoot = new GameObject("FeuxTricolores").transform;
            signalRoot.SetParent(root, false);
            var lit = new BoxMeshBuilder[6];
            for (int i = 0; i < 6; i++)
                lit[i] = new BoxMeshBuilder();
            int poles = 0;

            foreach (RoadIntersection inter in s_intersections)
            {
                CityLayout.JunctionData j = inter.Junction;
                if (j == null || j.s == 0)
                    continue;
                Vector3 first = Vector3.zero;
                foreach ((Vector3 dir, float width, int cls) in Arms(j))
                {
                    if (first == Vector3.zero)
                        first = dir;
                    int phase = Mathf.Abs(Vector3.Dot(dir, first)) > 0.707f ? 0 : 3;
                    Vector3 right = -Vector3.Cross(Vector3.up, dir); // à droite de qui roule vers le centre
                    Vector3 basePos = Vector3.zero;
                    bool placed = false;
                    for (float along = j.r + 0.6f; along < j.r + 7f; along += 0.8f)
                    {
                        basePos = inter.Center + dir * along + right * (width * 0.5f + 0.9f);
                        if (s_roadIndex.Clearance(basePos) > 0.35f && !CityLayout.IsWater(basePos.x, basePos.z))
                        {
                            placed = true;
                            break;
                        }
                    }
                    if (!placed)
                        continue;
                    basePos.y = SidewalkHeight;
                    Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
                    BlenderCityKit.StampChunked("SM_TrafficLight", ChunkOf(basePos.x, basePos.z), chunkKit,
                        Matrix4x4.TRS(basePos, rot, Vector3.one), null, Color.white, 0);
                    CityDecor.PoleBases.Add(new Vector4(basePos.x, basePos.y, basePos.z, 0.14f));
                    poles++;

                    // Deux têtes par poteau : primaire (+Z local) vers la branche, secondaire (+X local) vers la rue croisée.
                    foreach ((Vector3 localDir, int group) in new[] { (Vector3.forward, phase), (Vector3.right, 3 - phase) })
                    {
                        Vector3 facing = rot * localDir;
                        Vector3 headOrigin = basePos + facing * 0.392f;
                        Vector3 across = Vector3.Cross(Vector3.up, facing);
                        for (int k = 0; k < 3; k++)
                        {
                            float yOff = 2.90f + (1 - k) * 0.30f - 0.08f;
                            Vector3 c = headOrigin + Vector3.up * yOff;
                            // Petite lentille carrée tournée vers la route (face avant vers « facing »).
                            lit[group + k].AddFace(c + across * 0.08f - Vector3.up * 0.08f, c + across * 0.08f + Vector3.up * 0.08f,
                                c - across * 0.08f + Vector3.up * 0.08f, c - across * 0.08f - Vector3.up * 0.08f);
                        }
                    }
                }
            }

            Color[] colors = { new Color(1f, 0.12f, 0.08f), new Color(1f, 0.65f, 0.05f), new Color(0.15f, 1f, 0.35f) };
            string[] names = { "Rouge", "Orange", "Vert" };
            var phaseA = new Renderer[3];
            var phaseB = new Renderer[3];
            for (int i = 0; i < 6; i++)
            {
                int colorIndex = i % 3;
                Material material = CityMaterials.NightLight("M_Feu_" + names[colorIndex], colors[colorIndex], 2.2f, 2.6f, null, false);
                GameObject go = AddMeshObject(signalRoot, (i < 3 ? "FeuNS_" : "FeuEO_") + names[colorIndex], lit[i], material, meshes);
                if (go == null)
                    continue;
                go.GetComponent<MeshRenderer>().SetShadowsOff();
                (i < 3 ? phaseA : phaseB)[colorIndex] = go.GetComponent<MeshRenderer>();
            }
            signalRoot.gameObject.AddComponent<Client.World.TrafficLights>().EditorSetup(phaseA, phaseB);
            Debug.Log("[City] Feux tricolores : " + poles + " poteaux.");
        }

        /// <summary>
        /// Îlots centraux des ronds-points : bordure haute (0,35 m, les voitures ne montent pas dessus), pelouse
        /// et cocotiers. Le collider suit la bordure.
        /// </summary>
        private static void CreateRoundaboutIslands(Transform city, Material curbMaterial, List<Mesh> meshes)
        {
            CityLayout.RoundaboutData[] islands = CityLayout.Layout.roundabouts;
            if (islands.Length == 0)
                return;
            var root = new GameObject("RondsPoints").transform;
            root.SetParent(city, false);
            var curb = new BoxMeshBuilder();
            var lawn = new BoxMeshBuilder();
            Material grass = CityMaterials.Plain("M_IlotPelouse", new Color(0.30f, 0.45f, 0.18f), 0.05f);
            const float height = 0.35f;
            const int sides = 24;
            foreach (CityLayout.RoundaboutData island in islands)
            {
                var center = new Vector3(island.x, 0f, island.z);
                float r = Mathf.Max(2.5f, island.r);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 o0 = center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r;
                    Vector3 o1 = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                    Vector3 i0 = center + (o0 - center) * ((r - 0.5f) / r);
                    Vector3 i1 = center + (o1 - center) * ((r - 0.5f) / r);
                    Vector3 up = Vector3.up * height;
                    curb.AddFace(o0 - Vector3.up * 0.3f, o0 + up, o1 + up, o1 - Vector3.up * 0.3f); // flanc
                    curb.AddFace(o0 + up, i0 + up, i1 + up, o1 + up);                           // dessus de la bordure
                }
                lawn.AddFlatDisc(center, r - 0.45f, height - 0.03f, sides);
                // Cocotiers : un au centre, d'autres autour si l'îlot est grand.
                PalmBuilder.Place(root, center + Vector3.up * (height - 0.03f), s_decor.Next(PalmBuilder.VariantCount), s_decor.Next(360));
                if (r > 5.5f)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k * Mathf.PI * 2f / 3f + 0.4f;
                        PalmBuilder.Place(root, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r * 0.6f) + Vector3.up * (height - 0.03f),
                            s_decor.Next(PalmBuilder.VariantCount), s_decor.Next(360));
                    }
                }
            }
            GameObject curbObject = AddMeshObject(root, "Bordures", curb, curbMaterial, meshes);
            if (curbObject != null)
                curbObject.AddComponent<MeshCollider>().sharedMesh = curbObject.GetComponent<MeshFilter>().sharedMesh;
            GameObject lawnObject = AddMeshObject(root, "Pelouses", lawn, grass, meshes);
            if (lawnObject != null)
                lawnObject.AddComponent<MeshCollider>().sharedMesh = lawnObject.GetComponent<MeshFilter>().sharedMesh;
            Debug.Log("[City] Ronds-points : " + islands.Length + " îlots.");
        }
    }
}
