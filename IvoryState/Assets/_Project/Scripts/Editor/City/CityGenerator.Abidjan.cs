using System.Collections.Generic;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.World;
using Unity.AI.Navigation;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Ville d'après le tracé d'Abidjan (Resources/CityLayout.json, voir tools/citylayout) : relief et
    /// lagune, quais, grands axes et ponts, trottoirs, bâtiments alignés sur leurs rues, lieux
    /// particuliers, points de jeu sur les trottoirs, lampadaires et cocotiers, stations de taxi.
    /// <para>
    /// Mobile : tout est fusionné par tronçon de 128 m (culling) et par matériau ; les collisions des
    /// trottoirs et des bâtiments sont des MeshCollider par tronçon (pas un objet par bâtiment).
    /// </para>
    /// </summary>
    internal static partial class CityGenerator
    {
        private const float LayoutChunk = 128f;
        private const float TerrainCell = 4f;
        private const float WaterLevel = -0.45f;
        private const float LagoonDepth = -2.4f;

        private static readonly District Attecoube = new District
        {
            Name = "Attécoubé", LotsPerSide = 5, MinHeight = 3f, MaxHeight = 9f, Coverage = 0.9f, LotFill = 0.8f, HasFood = true,
            Colors = new[] { new Color(0.86f, 0.72f, 0.52f), new Color(0.72f, 0.80f, 0.66f), new Color(0.88f, 0.66f, 0.60f) },
            PlaceTypes = new[] { "Maquis", "Atelier", "Boutique", "Cabine", "Menuiserie", "Kiosque" },
            Style = FacadeStyle.Popular, BalconyChance = 0.1f, AwningChance = 0.55f, TankChance = 0.7f, PalmChance = 0.15f,
            PowerPoles = true, ClutterCount = 8,
        };

        private static readonly District Koumassi = new District
        {
            Name = "Koumassi", LotsPerSide = 4, MinHeight = 4f, MaxHeight = 14f, Coverage = 0.9f, LotFill = 0.85f, HasFood = true,
            Colors = new[] { new Color(0.84f, 0.62f, 0.44f), new Color(0.78f, 0.74f, 0.58f), new Color(0.66f, 0.58f, 0.52f) },
            PlaceTypes = new[] { "Marché", "Maquis", "Garage", "Boutique", "Quincaillerie", "Pharmacie" },
            Style = FacadeStyle.Popular, BalconyChance = 0.3f, AwningChance = 0.6f, TankChance = 0.6f, PalmChance = 0.15f,
            PowerPoles = true, ClutterCount = 6,
        };

        private static readonly District Abobo = new District
        {
            Name = "Abobo", LotsPerSide = 5, MinHeight = 3f, MaxHeight = 12f, Coverage = 0.9f, LotFill = 0.85f, HasFood = true,
            Colors = new[] { new Color(0.84f, 0.66f, 0.48f), new Color(0.74f, 0.72f, 0.60f), new Color(0.86f, 0.62f, 0.52f) },
            PlaceTypes = new[] { "Maquis", "Boutique", "Garage", "Cabine", "Atelier de couture", "Pharmacie" },
            Style = FacadeStyle.Popular, BalconyChance = 0.15f, AwningChance = 0.65f, TankChance = 0.7f, PalmChance = 0.1f,
            PowerPoles = true, ClutterCount = 8,
        };

        private static readonly District Riviera = new District
        {
            Name = "Riviera", LotsPerSide = 3, MinHeight = 5f, MaxHeight = 12f, Coverage = 0.6f, LotFill = 0.55f,
            Colors = new[] { new Color(0.96f, 0.92f, 0.82f), new Color(0.88f, 0.90f, 0.86f), new Color(0.97f, 0.86f, 0.76f) },
            PlaceTypes = new[] { "Villa", "Clinique", "Supermarché", "École privée", "Restaurant", "Pâtisserie" },
            Style = FacadeStyle.Villa, BalconyChance = 0.45f, TankChance = 0.3f, PalmChance = 0.7f,
        };

        private static readonly District PortBouet = new District
        {
            Name = "Port-Bouët", LotsPerSide = 4, MinHeight = 3f, MaxHeight = 10f, Coverage = 0.85f, LotFill = 0.8f, HasFood = true,
            Colors = new[] { new Color(0.90f, 0.78f, 0.58f), new Color(0.70f, 0.80f, 0.78f), new Color(0.88f, 0.70f, 0.56f) },
            PlaceTypes = new[] { "Maquis", "Poissonnerie", "Boutique", "Bar de plage", "Garage", "Pharmacie" },
            Style = FacadeStyle.Popular, BalconyChance = 0.2f, AwningChance = 0.6f, TankChance = 0.6f, PalmChance = 0.55f,
            PowerPoles = true, ClutterCount = 6,
        };

        private static District DistrictNamed(string name)
        {
            foreach (District d in AllDistricts)
            {
                if (d.Name == name)
                    return d;
            }
            return Yopougon;
        }

        private static Vector2Int ChunkOf(float x, float z) =>
            new Vector2Int(Mathf.FloorToInt(x / LayoutChunk), Mathf.FloorToInt(z / LayoutChunk));

        private static BoxMeshBuilder ChunkBuilder<TKey>(Dictionary<TKey, BoxMeshBuilder> builders, TKey key)
        {
            if (!builders.TryGetValue(key, out BoxMeshBuilder builder))
            {
                builder = new BoxMeshBuilder();
                builders[key] = builder;
            }
            return builder;
        }

        /// <summary>Angle Unity d'une direction horizontale (le +x local suit cette direction).</summary>
        private static float YawAlong(Vector3 direction) => -Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;

        private static List<Vector3> Points(float[] flat)
        {
            var points = new List<Vector3>(flat.Length / 2);
            for (int i = 0; i + 1 < flat.Length; i += 2)
                points.Add(new Vector3(flat[i], 0f, flat[i + 1]));
            return points;
        }

        /// <summary>Génère toute la ville extérieure d'après la disposition.</summary>
        private static void GenerateFromLayout(Transform city, Transform interactables, System.Random random, Material asphalt,
            Material sidewalk, Material water, Material plazaMaterial, List<Mesh> meshes, ref int nextId)
        {
            CityLayout.Data layout = CityLayout.Layout;
            var bridges = new List<(Vector3 a, Vector3 b, float width)>();
            foreach (CityLayout.RoadData road in layout.roads)
            {
                for (int i = 0; road.b != null && i + 3 < road.b.Length; i += 4)
                    bridges.Add((new Vector3(road.b[i], 0f, road.b[i + 1]), new Vector3(road.b[i + 2], 0f, road.b[i + 3]), road.width));
            }

            PrepareBridgeSpans(bridges);
            PrepareStreets(layout);
            CreateLayoutTerrain(city, bridges, meshes);
            CreateWaterPlane(city, water, meshes);
            CreateShores(city, layout.waterMask);
            CreateLayoutRoads(city, layout, asphalt, meshes);
            CreateBridges(city, bridges, meshes);
            CreateLayoutSidewalks(city, layout, sidewalk, meshes);
            CreateRoundaboutIslands(city, sidewalk, meshes);

            // Lieux particuliers d'abord (la place : les plus petits identifiants, près du point d'apparition).
            foreach (CityLayout.BlockData block in layout.blocks)
            {
                if (block.special == "Plaza")
                    CreatePlaza(city, interactables, plazaMaterial, new Vector3(block.x, 0f, block.z), CityLayout.UnityYaw(block.yaw), ref nextId);
            }
            foreach (CityLayout.BlockData block in layout.blocks)
            {
                if (string.IsNullOrEmpty(block.special) || block.special == "Plaza")
                    continue;
                if (!System.Enum.TryParse(block.special, out Special kind))
                    continue;
                CreateSpecialBlock(city, interactables, DistrictNamed(block.district), kind, block.name,
                    new Vector3(block.x, 0f, block.z), CityLayout.UnityYaw(block.yaw), sidewalk, meshes, ref nextId);
            }

            CreateLayoutBuildings(city, layout, meshes);
            CreateStreetDecor(city, layout, meshes);
            CreateLayoutInteractables(city, interactables, random, ref nextId);
            CreateLayoutTaxiStands(city);
            CreateLayoutForest(city, meshes);
            CreateParkings(city, asphalt, meshes);
            CreateAirport(city, asphalt, meshes);
            CreateLayoutBounds(city);
            Debug.Log("[City] Disposition d'Abidjan : " + layout.buildings.Length + " bâtiments, " + layout.roads.Length +
                      " rues, " + bridges.Count + " ponts, " + layout.blocks.Length + " lieux.");
        }

        #region Relief, lagune, quais

        private static void CreateLayoutTerrain(Transform city, List<(Vector3 a, Vector3 b, float width)> bridges, List<Mesh> meshes)
        {
            var root = new GameObject("Relief").transform;
            root.SetParent(city, false);
            root.gameObject.isStatic = true;
            Material ground = CityMaterials.Ground();
            Rect bounds = CityLayout.Bounds;
            CityLayout.WaterMaskData mask = CityLayout.Layout.waterMask;
            // Grille calée sur le masque d'eau (cases de 4 m), débordant de 40 m autour de la carte.
            float x0 = (mask != null ? mask.x : bounds.xMin) - 40f, z0 = (mask != null ? mask.z : bounds.yMin) - 40f;
            int nx = Mathf.CeilToInt((bounds.xMax + 40f - x0) / TerrainCell), nz = Mathf.CeilToInt((bounds.yMax + 40f - z0) / TerrainCell);
            const int chunkCells = 32;
            bool[] paved = PavedVertices(x0, z0, nx, nz, TerrainCell);
            var forestFloor = new Color(0.20f, 0.29f, 0.14f);

            float WetOffset(float x, float z)
            {
                // Les 4 cases autour du sommet : rive en pente douce.
                int wet = 0;
                float h = TerrainCell * 0.5f;
                if (Wet(x - h, z - h)) wet++;
                if (Wet(x + h, z - h)) wet++;
                if (Wet(x - h, z + h)) wet++;
                if (Wet(x + h, z + h)) wet++;
                return wet switch { 0 => 0f, 1 => -0.35f, 2 => -1.1f, 3 => -1.9f, _ => LagoonDepth };
            }

            bool Wet(float x, float z)
            {
                // Au-delà du masque (bordure du sol, abords) : l'eau ou la terre du bord de la carte continue.
                if (mask != null)
                {
                    x = Mathf.Clamp(x, mask.x + 2f, mask.x + mask.nx * mask.cell - 2f);
                    z = Mathf.Clamp(z, mask.z + 2f, mask.z + mask.nz * mask.cell - 2f);
                }
                if (CityLayout.IsWater(x, z))
                    return true;
                // Sous les ponts : l'eau, pas une digue.
                foreach ((Vector3 a, Vector3 b, float width) in bridges)
                {
                    Vector3 ab = b - a;
                    float t = Mathf.Clamp01(Vector3.Dot(new Vector3(x, 0f, z) - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.01f));
                    Vector3 p = a + ab * t;
                    if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < (width * 0.5f + 1.5f) * (width * 0.5f + 1.5f) && t > 0.02f && t < 0.98f)
                        return true;
                }
                return false;
            }

            for (int cx = 0; cx * chunkCells < nx; cx++)
            {
                for (int cz = 0; cz * chunkCells < nz; cz++)
                {
                    int countX = Mathf.Min(chunkCells, nx - cx * chunkCells), countZ = Mathf.Min(chunkCells, nz - cz * chunkCells);
                    var vertices = new List<Vector3>();
                    var colors = new List<Color32>();
                    var uvs = new List<Vector2>();
                    var triangles = new List<int>();
                    int stride = countZ + 1;
                    for (int i = 0; i <= countX; i++)
                    {
                        for (int j = 0; j <= countZ; j++)
                        {
                            float x = x0 + (cx * chunkCells + i) * TerrainCell, z = z0 + (cz * chunkCells + j) * TerrainCell;
                            float wetOffset = WetOffset(x, z);
                            float y = Ground(x, z) + wetOffset;
                            if (paved[(cx * chunkCells + i) * (nz + 1) + cz * chunkCells + j])
                                y -= PavedSink;
                            vertices.Add(new Vector3(x, y, z));
                            float n = Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f + 9f);
                            Color c = Color.Lerp(Grass, DryGrass, n * 0.8f);
                            if (CityLayout.IsForest(x, z))
                                c = Color.Lerp(c, forestFloor, 0.75f);
                            if (wetOffset < 0f)
                                c = Color.Lerp(c, Sand, Mathf.Clamp01(-wetOffset * 1.5f));
                            Color32 c32 = c;
                            c32.a = (byte)((cx * chunkCells + i) * 31 + (cz * chunkCells + j) * 17);
                            colors.Add(c32);
                            uvs.Add(new Vector2(x, z));
                        }
                    }
                    for (int i = 0; i < countX; i++)
                    {
                        for (int j = 0; j < countZ; j++)
                        {
                            int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                            triangles.Add(a); triangles.Add(b); triangles.Add(d);
                            triangles.Add(a); triangles.Add(d); triangles.Add(c);
                        }
                    }
                    var mesh = new Mesh { name = "Relief_" + cx + "_" + cz };
                    mesh.SetVertices(vertices);
                    mesh.SetColors(colors);
                    mesh.SetUVs(0, uvs);
                    var plain = new List<Vector2>(vertices.Count);
                    for (int k = 0; k < vertices.Count; k++)
                        plain.Add(new Vector2(-1f, -1f));
                    mesh.SetUVs(1, plain);
                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateNormals();
                    mesh.RecalculateTangents();
                    mesh.RecalculateBounds();
                    meshes.Add(mesh);
                    var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                    go.transform.SetParent(root, false);
                    go.isStatic = true;
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = ground;
                    renderer.SetShadowsOff();
                    go.GetComponent<MeshCollider>().sharedMesh = mesh;
                }
            }

            // Abords : le sol se prolonge de 3 km au-delà de la carte (l'océan reste de l'eau), jusqu'à
            // l'horizon et la brume : plus de vide au bord du monde. Pas de collider (murs invisibles au bord).
            var sv = new List<Vector3>();
            var sc = new List<Color32>();
            var st = new List<int>();
            var perimeter = new List<(Vector3 p, Vector3 n)>();
            for (int i = 0; i <= nx; i++) perimeter.Add((new Vector3(x0 + i * TerrainCell, 0f, z0), Vector3.back));
            for (int j = 0; j <= nz; j++) perimeter.Add((new Vector3(x0 + nx * TerrainCell, 0f, z0 + j * TerrainCell), Vector3.right));
            for (int i = nx; i >= 0; i--) perimeter.Add((new Vector3(x0 + i * TerrainCell, 0f, z0 + nz * TerrainCell), Vector3.forward));
            for (int j = nz; j >= 0; j--) perimeter.Add((new Vector3(x0, 0f, z0 + j * TerrainCell), Vector3.left));
            const float reach = 3000f;
            Vector3 Inner(Vector3 p)
            {
                float wetOffset = WetOffset(p.x, p.z);
                return new Vector3(p.x, Ground(p.x, p.z) + wetOffset, p.z);
            }
            Vector3 Outer(Vector3 inner, Vector3 n) => new Vector3(inner.x, inner.y < -0.2f ? LagoonDepth : inner.y, inner.z) + n * reach;
            // Même rendu que le sol (couleurs par sommet, UV en mètres) ; sable sous l'eau.
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                if (Vector3.Cross(b - a, c - a).y < 0f)
                    (b, d) = (d, b);
                int start = sv.Count;
                foreach (Vector3 v in new[] { a, b, c, d })
                {
                    sv.Add(v);
                    Color col = v.y < -0.2f ? Sand : Color.Lerp(Grass, DryGrass, Mathf.PerlinNoise(v.x * 0.03f + 5f, v.z * 0.03f + 9f) * 0.8f);
                    Color32 c32 = col;
                    c32.a = 0;
                    sc.Add(c32);
                }
                st.Add(start); st.Add(start + 1); st.Add(start + 2);
                st.Add(start); st.Add(start + 2); st.Add(start + 3);
            }
            for (int k = 0; k + 1 < perimeter.Count; k++)
            {
                (Vector3 pa, Vector3 na) = perimeter[k];
                (Vector3 pb, Vector3 nb) = perimeter[k + 1];
                Vector3 ia = Inner(pa), ib = Inner(pb);
                if (na == nb)
                    Face(ia, ib, Outer(ib, nb), Outer(ia, na));
                else
                    Face(ia, Outer(ia, na), Outer(ia, na) + nb * reach, Outer(ia, nb)); // coin
            }
            var skirtMesh = new Mesh { name = "Relief_Abords", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            skirtMesh.SetVertices(sv);
            skirtMesh.SetColors(sc);
            var skirtUv = new List<Vector2>(sv.Count);
            var skirtPlain = new List<Vector2>(sv.Count);
            foreach (Vector3 v in sv)
            {
                skirtUv.Add(new Vector2(v.x, v.z));
                skirtPlain.Add(new Vector2(-1f, -1f));
            }
            skirtMesh.SetUVs(0, skirtUv);
            skirtMesh.SetUVs(1, skirtPlain);
            skirtMesh.SetTriangles(st, 0);
            skirtMesh.RecalculateNormals();
            skirtMesh.RecalculateTangents();
            skirtMesh.RecalculateBounds();
            meshes.Add(skirtMesh);
            var abords = new GameObject("Abords", typeof(MeshFilter), typeof(MeshRenderer));
            abords.transform.SetParent(root, false);
            abords.isStatic = true;
            abords.GetComponent<MeshFilter>().sharedMesh = skirtMesh;
            abords.GetComponent<MeshRenderer>().sharedMaterial = ground;
            abords.GetComponent<MeshRenderer>().SetShadowsOff();
        }

        /// <summary>Surface de la lagune : un plan sous le niveau du sol (caché par la terre ferme).</summary>
        private static void CreateWaterPlane(Transform city, Material water, List<Mesh> meshes)
        {
            Rect b = CityLayout.Bounds;
            var builder = new BoxMeshBuilder();
            builder.AddFlatQuad(new Vector3(b.center.x, WaterLevel, b.center.y), Vector3.right, b.width + 6200f, b.height + 6200f);
            GameObject go = AddMeshObject(city, "Lagune", builder, water, meshes);
            go?.GetComponent<MeshRenderer>().SetShadowsOff();
        }

        /// <summary>
        /// Quais : barrières invisibles au-dessus de l'eau (plages du masque fusionnées en rectangles), qui
        /// empêchent de tomber à l'eau. Exclues du NavMesh (pas de passants « sur l'eau »).
        /// </summary>
        private static void CreateShores(Transform city, CityLayout.WaterMaskData mask)
        {
            if (mask?.runs == null)
                return;
            var root = new GameObject("Quais");
            root.transform.SetParent(city, false);
            var modifier = root.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = 1; // Not Walkable
            modifier.applyToChildren = true;

            // Fusion verticale des plages identiques de lignes consécutives.
            var open = new Dictionary<(int start, int length), (int firstRow, int lastRow)>();
            var rects = new List<(int start, int length, int firstRow, int lastRow)>();
            int currentRow = -1;
            var seen = new HashSet<(int, int)>();
            void Close(int row)
            {
                var finished = new List<(int, int)>();
                foreach (var pair in open)
                {
                    if (!seen.Contains(pair.Key))
                        finished.Add(pair.Key);
                }
                foreach (var key in finished)
                {
                    rects.Add((key.Item1, key.Item2, open[key].firstRow, open[key].lastRow));
                    open.Remove(key);
                }
                seen.Clear();
            }
            for (int i = 0; i + 2 < mask.runs.Length; i += 3)
            {
                int row = mask.runs[i], start = mask.runs[i + 1], length = mask.runs[i + 2];
                if (row != currentRow)
                {
                    if (currentRow >= 0)
                        Close(currentRow);
                    // Une ligne sautée ferme tout.
                    if (row != currentRow + 1)
                        Close(row);
                    currentRow = row;
                }
                var key = (start, length);
                seen.Add(key);
                open[key] = open.TryGetValue(key, out var span) ? (span.firstRow, row) : (row, row);
            }
            seen.Clear();
            Close(currentRow + 1);

            foreach ((int start, int length, int firstRow, int lastRow) in rects)
            {
                float x = mask.x + (start + length * 0.5f) * mask.cell;
                float z = mask.z + (firstRow + lastRow + 1) * 0.5f * mask.cell;
                AddBoxCollider(root, new Vector3(x, 1.0f, z), new Vector3(length * mask.cell, 3f, (lastRow - firstRow + 1) * mask.cell));
            }
        }

        private static void CreateLayoutBounds(Transform city)
        {
            Rect b = CityLayout.Bounds;
            var bounds = new GameObject("Bounds");
            bounds.transform.SetParent(city, false);
            const float height = 140f, bottom = -20f, thickness = 2f;
            float y = bottom + height / 2f;
            AddBoxCollider(bounds, new Vector3(b.center.x, y, b.yMax), new Vector3(b.width, height, thickness));
            AddBoxCollider(bounds, new Vector3(b.center.x, y, b.yMin), new Vector3(b.width, height, thickness));
            AddBoxCollider(bounds, new Vector3(b.xMax, y, b.center.y), new Vector3(thickness, height, b.height));
            AddBoxCollider(bounds, new Vector3(b.xMin, y, b.center.y), new Vector3(thickness, height, b.height));
        }

        #endregion

        #region Routes, ponts, trottoirs

        private struct RoadIntersection
        {
            public Vector3 Center;
            public float Radius;
            /// <summary>Carrefour de la disposition (branches, feux), null pour un carrefour sans données.</summary>
            public CityLayout.JunctionData Junction;
        }

        private static readonly List<RoadIntersection> s_intersections = new List<RoadIntersection>();

        private static void CreateLayoutRoads(Transform city, CityLayout.Data layout, Material asphalt, List<Mesh> meshes)
        {
            var root = new GameObject("Routes").transform;
            root.SetParent(city, false);
            var paved = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var lines = new Dictionary<Vector2Int, BoxMeshBuilder>();
            // Carrefours : préparés par PrepareStreets d'après la disposition (branches, feux).

            foreach (CityLayout.RoadData road in layout.roads)
            {
                List<Vector3> points = Points(road.p);
                if (points.Count < 2)
                    continue;
                // Les petites rues un peu au-dessus des grandes : pas de scintillement aux carrefours.
                float lift = 0.03f + 0.006f * road.cls;

                // Coupes calculées sur toute la rue pour qu'aucune couture n'apparaisse aux bords des tronçons de 128 m ;
                // sommets tous les 4 m pour suivre le relief et le profil des ponts.
                List<Vector3> dense = Densify(points, 4f);
                BoxMeshBuilder.ComputeRibbonSides(dense, road.width, out Vector3[] sides, out float[] travel);
                for (int i = 0; i + 1 < dense.Count; i++)
                {
                    Vector3 mid = (dense[i] + dense[i + 1]) * 0.5f;
                    BoxMeshBuilder pb = ChunkBuilder(paved, ChunkOf(mid.x, mid.z));
                    pb.WorldUv = true;
                    pb.AddRibbonSegment(dense[i], sides[i], travel[i], dense[i + 1], sides[i + 1], travel[i + 1], road.width, lift);
                }

                // Arrondis d'extrémité et de carrefour (comme stroke-linecap="round" sur le plan) ; une impasse
                // finit par une aire de retournement.
                float halfWidth = road.width * 0.5f;
                Vector3 pStart = points[0], pEnd = points[points.Count - 1];
                bool deadStart = road.de != null && road.de.Length > 0 && road.de[0] != 0;
                bool deadEnd = road.de != null && road.de.Length > 1 && road.de[1] != 0;
                BoxMeshBuilder sb = ChunkBuilder(paved, ChunkOf(pStart.x, pStart.z));
                sb.WorldUv = true;
                sb.AddFlatDisc(pStart, deadStart ? halfWidth + 3f : halfWidth, lift, 16);
                BoxMeshBuilder eb = ChunkBuilder(paved, ChunkOf(pEnd.x, pEnd.z));
                eb.WorldUv = true;
                eb.AddFlatDisc(pEnd, deadEnd ? halfWidth + 3f : halfWidth, lift, 16);

                if (road.cls > 2)
                    continue;

                float markY = lift + 0.015f;
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    Vector3 a = points[i], b = points[i + 1];
                    Vector3 dir = b - a;
                    float length = dir.magnitude;
                    if (length < 1f)
                        continue;
                    dir /= length;
                    Vector3 across = Vector3.Cross(Vector3.up, dir);

                    // Bandes de rive continues sur les grands axes (cls <= 1) et double ligne médiane continue sur cls == 0.
                    if (road.cls <= 1 && length > 1.5f)
                    {
                        float edgeOff = halfWidth - 0.35f;
                        for (float s = 0f; s < length - 0.4f; s += 8f)
                        {
                            float segLen = Mathf.Min(8.05f, length - s);
                            if (segLen < 0.8f)
                                continue;
                            Vector3 mid = a + dir * (s + segLen * 0.5f);
                            if (NearJunction(mid, 5f))
                                continue;
                            BoxMeshBuilder lb = ChunkBuilder(lines, ChunkOf(mid.x, mid.z));
                            lb.AddFlatQuad(mid + across * edgeOff + Vector3.up * markY, dir, segLen, 0.14f);
                            lb.AddFlatQuad(mid - across * edgeOff + Vector3.up * markY, dir, segLen, 0.14f);
                            if (road.cls == 0)
                            {
                                lb.AddFlatQuad(mid + across * 0.17f + Vector3.up * markY, dir, segLen, 0.12f);
                                lb.AddFlatQuad(mid - across * 0.17f + Vector3.up * markY, dir, segLen, 0.12f);
                            }
                        }
                    }

                    // Ligne médiane discontinue sur les avenues et rues de quartier (cls 1 et 2).
                    if (road.cls >= 1)
                    {
                        for (float s = 4.5f; s < length - 1.5f; s += 9f)
                        {
                            Vector3 p = a + dir * s;
                            if (NearJunction(p, 5f))
                                continue;
                            ChunkBuilder(lines, ChunkOf(p.x, p.z)).AddFlatQuad(p + Vector3.up * markY, dir, 3.2f, 0.15f);
                        }
                    }
                }

            }

            // Passages piétons et lignes d'arrêt aux carrefours (une fois par branche, pas par rue).
            AddJunctionCrosswalks(lines, 0.03f + 0.015f + 0.02f);

            // La chaussée est solide : les voitures roulent sur le bitume (lisse, au profil des ponts), pas sur
            // le sol abaissé dessous.
            foreach (var pair in paved)
            {
                GameObject bitume = AddMeshObject(root, "Bitume_" + pair.Key.x + "_" + pair.Key.y, pair.Value, asphalt, meshes);
                if (bitume == null)
                    continue;
                bitume.GetComponent<MeshRenderer>().SetShadowsOff();
                bitume.AddComponent<MeshCollider>().sharedMesh = bitume.GetComponent<MeshFilter>().sharedMesh;
            }
            foreach (var pair in lines)
                AddMeshObject(root, "Marquage_" + pair.Key.x + "_" + pair.Key.y, pair.Value, s_markingMaterial, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
        }

        private static bool TryFindRoadCrossing(List<Vector3> a, List<Vector3> b, out Vector3 cross)
        {
            for (int i = 0; i + 1 < a.Count; i++)
            {
                Vector3 a0 = a[i], a1 = a[i + 1];
                for (int j = 0; j + 1 < b.Count; j++)
                {
                    Vector3 b0 = b[j], b1 = b[j + 1];
                    if (SegmentsIntersectXZ(a0, a1, b0, b1, out cross))
                        return true;
                }
            }
            cross = Vector3.zero;
            return false;
        }

        private static bool SegmentsIntersectXZ(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, out Vector3 hit)
        {
            float dAx = a1.x - a0.x, dAz = a1.z - a0.z;
            float dBx = b1.x - b0.x, dBz = b1.z - b0.z;
            float denom = dAx * dBz - dAz * dBx;
            if (Mathf.Abs(denom) > 1e-4f)
            {
                float t = ((b0.x - a0.x) * dBz - (b0.z - a0.z) * dBx) / denom;
                float u = ((b0.x - a0.x) * dAz - (b0.z - a0.z) * dAx) / denom;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
                {
                    hit = new Vector3(a0.x + t * dAx, 0f, a0.z + t * dAz);
                    return true;
                }
            }
            hit = Vector3.zero;
            return false;
        }

        /// <summary>Dans un carrefour (rayon de sa chaussée commune + <paramref name="margin"/>).</summary>
        private static bool NearJunction(Vector3 p, float margin)
        {
            foreach (RoadIntersection inter in s_intersections)
            {
                float r = inter.Radius + margin;
                if ((inter.Center - p).sqrMagnitude < r * r)
                    return true;
            }
            return false;
        }

        private static bool NearMajorIntersection(Vector3 p, float radius)
        {
            float r2 = radius * radius;
            foreach (RoadIntersection inter in s_intersections)
            {
                if ((inter.Center - p).sqrMagnitude < r2)
                    return true;
            }
            return false;
        }

        private static void AddIntersectionCrosswalkOnRoad(List<Vector3> points, float width,
            RoadIntersection inter, float markY, Dictionary<Vector2Int, BoxMeshBuilder> lines)
        {
            float offset = inter.Radius + 2.8f;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                Vector3 ab = b - a;
                float len = ab.magnitude;
                if (len < 6f)
                    continue;
                Vector3 dir = ab / len;
                float proj = Vector3.Dot(inter.Center - a, dir);
                Vector3 closest = a + dir * Mathf.Clamp(proj, 0f, len);
                if ((closest - inter.Center).sqrMagnitude > 4f * 4f)
                    continue;

                Vector3 across = Vector3.Cross(Vector3.up, dir);
                foreach (float sign in new[] { -1f, 1f })
                {
                    float s = proj + sign * offset;
                    if (s < 2.5f || s > len - 2.5f)
                        continue;
                    Vector3 walkCenter = a + dir * s;
                    if (CityLayout.IsWater(walkCenter.x, walkCenter.z) ||
                        CityLayout.IsWater(walkCenter.x + dir.x * 6f, walkCenter.z + dir.z * 6f) ||
                        CityLayout.IsWater(walkCenter.x - dir.x * 6f, walkCenter.z - dir.z * 6f))
                        continue;
                    BoxMeshBuilder lb = ChunkBuilder(lines, ChunkOf(walkCenter.x, walkCenter.z));
                    for (float t = -width * 0.5f + 1.0f; t <= width * 0.5f - 1.0f; t += 0.95f)
                        lb.AddFlatQuad(walkCenter + across * t + Vector3.up * markY, dir, 2.5f, 0.44f);
                    // Ligne d'arrêt avant le passage piéton.
                    Vector3 stopPos = walkCenter + dir * (sign * 1.85f);
                    lb.AddFlatQuad(stopPos + Vector3.up * markY, across, width * 0.46f, 0.28f);
                }
            }
        }

        /// <summary>
        /// Ponts réalistes : tablier en béton par tronçons de 6 m qui suit le profil du pont (rampes en S de
        /// part et d'autre, dos d'âne au-dessus de la lagune), piles avant-becs allongées jusqu'au tablier,
        /// garde-corps et lampadaires.
        /// </summary>
        private static void CreateBridges(Transform city, List<(Vector3 a, Vector3 b, float width)> bridges, List<Mesh> meshes)
        {
            var root = new GameObject("Ponts");
            root.transform.SetParent(city, false);
            var concrete = new BoxMeshBuilder();
            var kitBuilders = new Dictionary<Material, BoxMeshBuilder>();
            var bulbs = new BoxMeshBuilder();
            var pools = new BoxMeshBuilder();
            CityDecor.Frame = Matrix4x4.identity;
            bool hasPier = BlenderCityKit.HasModel("SM_BridgePier");
            Bounds pierBounds = hasPier ? BlenderCityKit.GetBounds("SM_BridgePier") : default;
            bool hasRailing = BlenderCityKit.HasModel("SM_BridgeRailing");

            foreach (BridgeSpan s in s_bridgeSpans)
            {
                Vector3 dir = s.Dir;
                Vector3 across = Vector3.Cross(Vector3.up, dir);
                float span = s.Length, width = s.Width;
                Quaternion flat = Quaternion.Euler(0f, YawAlong(dir), 0f);

                Vector3 DeckPoint(float d)
                {
                    Vector3 p = s.A + dir * d;
                    p.y = d < 0f || d > span ? Ground(p.x, p.z) : BridgeDeck(s, d / span);
                    return p;
                }

                // Tablier (dessus = surface roulante, 0,68 m d'épaisseur) : 3 m de débord sur chaque berge.
                int pieces = Mathf.Max(2, Mathf.CeilToInt((span + 6f) / 6f));
                float pieceLength = (span + 6f) / pieces;
                for (int k = 0; k < pieces; k++)
                {
                    float d0 = -3f + k * pieceLength;
                    Vector3 p0 = DeckPoint(d0), p1 = DeckPoint(d0 + pieceLength);
                    Vector3 seg = p1 - p0;
                    float segLength = seg.magnitude;
                    Quaternion q = Quaternion.FromToRotation(dir, seg / segLength) * flat;
                    Vector3 up = q * Vector3.up;
                    Vector3 mid = (p0 + p1) * 0.5f;
                    concrete.AddRotatedBox(mid - up * 0.34f, new Vector3(segLength + 0.08f, 0.68f, width + 1.5f), q);
                    var deck = new GameObject("Tablier");
                    deck.transform.SetParent(root.transform, false);
                    deck.transform.SetPositionAndRotation(mid - up * 0.3f, q);
                    deck.AddComponent<BoxCollider>().size = new Vector3(segLength + 0.08f, 0.66f, width + 1.5f);

                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 c = mid + across * side * (width * 0.5f + 0.42f);
                        var rail = new GameObject("GardeCorps");
                        rail.transform.SetParent(root.transform, false);
                        rail.transform.SetPositionAndRotation(c + up * 0.6f, q);
                        rail.AddComponent<BoxCollider>().size = new Vector3(segLength + 0.08f, 1.2f, 0.42f);
                        if (hasRailing)
                        {
                            Quaternion railRot = Quaternion.FromToRotation(dir, seg / segLength) * Quaternion.LookRotation(across * side, Vector3.up);
                            BlenderCityKit.Stamp("SM_BridgeRailing", kitBuilders,
                                Matrix4x4.TRS(c, railRot, new Vector3(segLength / 4f, 1f, 1f)), null, Color.white, k);
                        }
                        else
                        {
                            concrete.AddRotatedBox(c + up * 0.35f, new Vector3(segLength + 0.08f, 0.7f, 0.35f), q);
                        }
                    }
                }

                // Piles : de leur pied (fond de la lagune) au-dessous du tablier relevé.
                if (hasPier)
                {
                    int pierCount = Mathf.Max(1, Mathf.FloorToInt(span / 24f));
                    float pierScaleX = (width + 1.5f) / 13.8f;
                    Quaternion dirRot = Quaternion.LookRotation(dir, Vector3.up);
                    for (int p = 1; p <= pierCount; p++)
                    {
                        float d = span * p / (pierCount + 1);
                        Vector3 pierPos = s.A + dir * d;
                        float lift = BridgeDeck(s, d / span);
                        float bottom = pierBounds.min.y, top = pierBounds.max.y;
                        float sy = top - bottom > 0.1f ? (top + lift - bottom) / (top - bottom) : 1f;
                        pierPos.y = bottom - bottom * sy;
                        BlenderCityKit.Stamp("SM_BridgePier", kitBuilders,
                            Matrix4x4.TRS(pierPos, dirRot, new Vector3(pierScaleX, sy, 1f)), null, Color.white, p);
                    }
                }

                // Lampadaires tous les 26 m sur le tablier.
                foreach (float side in new[] { -1f, 1f })
                {
                    for (float d = span * 0.1f; d <= span * 0.9f + 0.1f; d += 26f)
                    {
                        Vector3 lampPos = DeckPoint(d) + across * side * (width * 0.5f + 0.27f);
                        CityDecor.AddStreetLamp(kitBuilders, bulbs, pools, lampPos, -across * side, lampPos.y + 0.05f);
                    }
                }
            }
            AddMeshObject(root.transform, "Beton", concrete, CityMaterials.Concrete(), meshes);
            BlenderCityKit.BuildObjects(root.transform, "EquipementsPont", kitBuilders, meshes);
            AddLightMeshes(root.transform, bulbs, pools, CityDecor.BulbMaterial, meshes);
        }

        /// <summary>Trottoirs réalistes : rubans continus avec bordures béton chanfreinées (type T2) sans couture.</summary>
        private static void CreateLayoutSidewalks(Transform city, CityLayout.Data layout, Material sidewalk, List<Mesh> meshes)
        {
            var root = new GameObject("Trottoirs").transform;
            root.SetParent(city, false);
            var builders = new Dictionary<Vector2Int, BoxMeshBuilder>();
            float width = layout.sidewalkWidth > 0f ? layout.sidewalkWidth : 3f;
            foreach (CityLayout.SidewalkData walk in layout.sidewalks)
            {
                List<Vector3> points = Densify(Points(walk.p), 4f);
                if (points.Count < 2)
                    continue;
                BoxMeshBuilder.ComputeRibbonSides(points, width, out Vector3[] sides, out float[] travel);
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    if ((points[i + 1] - points[i]).sqrMagnitude < 0.25f)
                        continue;
                    Vector3 mid = (points[i] + points[i + 1]) * 0.5f;
                    ChunkBuilder(builders, ChunkOf(mid.x, mid.z)).AddSidewalkSegment(
                        points[i], sides[i], travel[i], points[i + 1], sides[i + 1], travel[i + 1], width, SidewalkHeight);
                }
                Vector3 dStart = (points[1] - points[0]).normalized;
                ChunkBuilder(builders, ChunkOf(points[0].x, points[0].z)).AddSidewalkCap(
                    points[0], sides[0], -dStart, width, SidewalkHeight, true);
                int last = points.Count - 1;
                Vector3 dEnd = (points[last] - points[last - 1]).normalized;
                ChunkBuilder(builders, ChunkOf(points[last].x, points[last].z)).AddSidewalkCap(
                    points[last], sides[last], dEnd, width, SidewalkHeight, false);
            }
            foreach (var pair in builders)
            {
                GameObject go = AddMeshObject(root, "Trottoir_" + pair.Key.x + "_" + pair.Key.y, pair.Value, sidewalk, meshes);
                if (go == null)
                    continue;
                go.GetComponent<MeshRenderer>().SetShadowsOff();
                go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                MarkSidewalkArea(go);
            }
        }

        #endregion

        #region Bâtiments

        private static void CreateLayoutBuildings(Transform city, CityLayout.Data layout, List<Mesh> meshes)
        {
            var root = new GameObject("Batiments").transform;
            root.SetParent(city, false);
            var chunkMaterials = new Dictionary<(Vector2Int, Material), BoxMeshBuilder>();
            var facades = new Dictionary<(Vector2Int, District), BoxMeshBuilder>();
            var roofsTiles = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var roofsMetal = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var solids = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var plinths = new Dictionary<Vector2Int, BoxMeshBuilder>();
            bool useBlenderKit = BlenderCityKit.HasModel("SM_Villa_Coloniale") &&
                                 BlenderCityKit.HasModel("SM_Maison_Populaire_A") &&
                                 BlenderCityKit.HasModel("SM_Tower_Floor2");

            for (int buildingIndex = 0; buildingIndex < layout.buildings.Length; buildingIndex++)
            {
                CityLayout.BuildingData b = layout.buildings[buildingIndex];
                District district = DistrictNamed(b.district);
                float yaw = CityLayout.UnityYaw(b.yaw);
                float height = Mathf.Lerp(district.MinHeight, district.MaxHeight, b.r * b.r) * (b.row == 2 ? 0.8f : 1f);
                height = Mathf.Max(3f, Mathf.Round(height / 3f) * 3f);
                Color color = district.Colors[(int)(b.r * 997f) % district.Colors.Length];
                var size = new Vector3(b.w, height, b.d);
                float baseY = BuildingBase(b, out float lowY);
                var basePosition = new Vector3(b.x, baseY, b.z);
                Vector2Int chunk = ChunkOf(b.x, b.z);
                if (baseY - lowY > 0.05f)
                    ChunkBuilder(plinths, chunk).AddOrientedBox(new Vector3(b.x, lowY - 0.4f, b.z),
                        new Vector3(b.w + 0.1f, baseY - lowY + 0.42f, b.d + 0.1f), yaw, false, false);

                if (useBlenderKit)
                {
                    Vector3 toStreet = new Vector3(-b.fx, 0f, -b.fz);
                    toStreet = toStreet.sqrMagnitude > 1e-4f ? toStreet.normalized : Vector3.forward;
                    Quaternion rot = Quaternion.LookRotation(toStreet, Vector3.up);
                    StampBlenderBuilding(chunk, chunkMaterials, district, b, basePosition, rot, size, color);
                }
                else
                {
                    Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 normalLocal = Quaternion.Inverse(q) * new Vector3(b.fx, 0f, b.fz);
                    Vector3 street = normalLocal.z > 0f ? Vector3.back : Vector3.forward;
                    BoxMeshBuilder facade = ChunkBuilder(facades, (chunk, district));
                    BoxMeshBuilder roofTiles = ChunkBuilder(roofsTiles, chunk);
                    BoxMeshBuilder roofMetal = ChunkBuilder(roofsMetal, chunk);
                    facade.SetFrame(basePosition, yaw);
                    roofTiles.SetFrame(basePosition, yaw);
                    roofMetal.SetFrame(basePosition, yaw);
                    AddBuildingVisual(facade, district, Vector3.zero, size, color, street, roofTiles, roofMetal);
                    facade.ClearFrame();
                    roofTiles.ClearFrame();
                    roofMetal.ClearFrame();
                }
                ChunkBuilder(solids, chunk).AddOrientedBox(new Vector3(b.x, lowY - 0.4f, b.z),
                    size + Vector3.up * (baseY - lowY + 0.4f), yaw, false, true);
            }
            foreach (var pair in plinths)
                AddMeshObject(root, "Socles_" + pair.Key.x + "_" + pair.Key.y, pair.Value, CityMaterials.Concrete(), meshes);

            foreach (var pair in chunkMaterials)
            {
                if (!pair.Value.IsEmpty)
                    AddMeshObject(root, pair.Key.Item2.name + "_" + pair.Key.Item1.x + "_" + pair.Key.Item1.y,
                        pair.Value, pair.Key.Item2, meshes);
            }
            foreach (var pair in facades)
            {
                (Vector2Int chunk, District district) = pair.Key;
                AddMeshObject(root, district.Name + "_" + chunk.x + "_" + chunk.y, pair.Value, s_facades[district], meshes);
            }
            Material tileRoofMat = CityMaterials.Roof("M_RoofTiles", "clay_roof_tiles_02", 2.2f, 0.18f);
            Material metalRoofMat = CityMaterials.Roof("M_RoofMetal", "corrugated_iron_02", 1.8f, 0.35f);
            foreach (var pair in roofsTiles)
            {
                if (!pair.Value.IsEmpty)
                    AddMeshObject(root, "ToitsTuiles_" + pair.Key.x + "_" + pair.Key.y, pair.Value, tileRoofMat, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            }
            foreach (var pair in roofsMetal)
            {
                if (!pair.Value.IsEmpty)
                    AddMeshObject(root, "ToitsToles_" + pair.Key.x + "_" + pair.Key.y, pair.Value, metalRoofMat, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            }
            foreach (var pair in solids)
            {
                Mesh mesh = pair.Value.Build("Collisions_" + pair.Key.x + "_" + pair.Key.y);
                meshes.Add(mesh);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root, false);
                go.isStatic = true;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }

        /// <summary>
        /// Niveau du rez-de-chaussée : le sol devant la façade (côté rue), et <paramref name="lowY"/> le point
        /// le plus bas de l'emprise (le socle descend jusque-là sur un terrain en pente).
        /// </summary>
        private static float BuildingBase(CityLayout.BuildingData b, out float lowY)
        {
            float frontX = b.x - b.fx * b.d * 0.5f, frontZ = b.z - b.fz * b.d * 0.5f;
            float baseY = Ground(frontX, frontZ);
            float ya = b.yaw * Mathf.Deg2Rad;
            float ux = Mathf.Cos(ya) * b.w * 0.5f, uz = Mathf.Sin(ya) * b.w * 0.5f;
            float vx = -Mathf.Sin(ya) * b.d * 0.5f, vz = Mathf.Cos(ya) * b.d * 0.5f;
            lowY = baseY;
            foreach ((float su, float sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                lowY = Mathf.Min(lowY, Ground(b.x + su * ux + sv * vx, b.z + su * uz + sv * vz));
            return baseY;
        }

        private static void StampBlenderBuilding(Vector2Int chunk,
            Dictionary<(Vector2Int, Material), BoxMeshBuilder> chunkMaterials,
            District district, CityLayout.BuildingData b, Vector3 basePos, Quaternion rot, Vector3 size, Color color)
        {
            System.Random r = s_decor;
            float jitter = Mathf.Lerp(0.90f, 1.08f, (float)r.NextDouble());
            int seed = r.Next(256);
            Color wallTint = color * jitter;
            Color tileRoof = TileRoofColors[r.Next(TileRoofColors.Length)] * jitter;
            Color metalRoof = MetalRoofColors[r.Next(MetalRoofColors.Length)] * jitter;
            Material wallMat = s_facades[district];

            void StampPart(string model, float y, Vector3 refSize, float scaleY, Color? roofCol = null)
            {
                var scale = new Vector3(size.x / refSize.x, scaleY, size.z / refSize.z);
                Matrix4x4 xf = Matrix4x4.TRS(basePos + Vector3.up * y, rot, scale);
                BlenderCityKit.StampChunked(model, chunk, chunkMaterials, xf, wallMat, wallTint, seed, roofCol, scale);
            }

            if (district.Style == FacadeStyle.Tower)
            {
                var refVec = new Vector3(14.5f, 6f, 14.5f);
                StampPart("SM_Tower_Podium", 0f, refVec, 1f);
                float midTotal = Mathf.Max(6f, size.y - 12f);
                int pairs = Mathf.Max(1, Mathf.RoundToInt(midTotal / 6f));
                float pairScaleY = midTotal / (pairs * 6f);
                for (int i = 0; i < pairs; i++)
                    StampPart("SM_Tower_Floor2", 6f + i * 6f * pairScaleY, refVec, pairScaleY);
                StampPart("SM_Tower_Crown", 6f + midTotal, refVec, 1f);
                return;
            }

            if (district.Style == FacadeStyle.Villa)
            {
                var villaRef = new Vector3(14f, 6f, 14f);
                if (size.y <= 4.5f)
                {
                    string villa = r.NextDouble() < 0.42 ? "SM_Villa_Moderne" : "SM_Villa_Coloniale";
                    StampPart(villa, 0f, villaRef, 1f, tileRoof);
                }
                else if (size.y <= 9.5f)
                {
                    float sy = Mathf.Clamp(size.y / 6.3f, 0.90f, 1.25f);
                    StampPart("SM_Villa_Duplex", 0f, villaRef, sy, tileRoof);
                }
                else
                {
                    var resRef = new Vector3(12f, 3f, 14.5f);
                    StampPart("SM_Floor_GroundRes", 0f, resRef, 1f);
                    int mid = Mathf.Max(1, Mathf.RoundToInt((size.y - 3.2f) / 3f));
                    for (int i = 0; i < mid; i++)
                        StampPart("SM_Floor_MidMarcory", 3.2f + i * 3f, resRef, 1f);
                    StampPart("SM_Roof_HipTile", 3.2f + mid * 3f, resRef, 1f, tileRoof);
                }
                return;
            }

            if (district.Style == FacadeStyle.Popular && size.y <= 4.5f)
            {
                var popRef = new Vector3(8.5f, 3.2f, 10.5f);
                string house = (b.row == 1 && r.NextDouble() < 0.55) ? "SM_Maison_Populaire_B" : "SM_Maison_Populaire_A";
                float sy = Mathf.Clamp(size.y / 3.2f, 0.92f, 1.15f);
                StampPart(house, 0f, popRef, sy, metalRoof);
                return;
            }

            if (district.Style == FacadeStyle.Residential)
            {
                if (size.y <= 4.5f)
                {
                    StampPart(r.NextDouble() < 0.5 ? "SM_Villa_Moderne" : "SM_Villa_Coloniale", 0f, new Vector3(14f, 6f, 14f), 1f, tileRoof);
                    return;
                }
                var resRef = new Vector3(12f, 3f, 14.5f);
                bool groundShop = b.row == 1 && r.NextDouble() < 0.45;
                if (groundShop)
                    StampPart("SM_Floor_GroundShop", 0f, new Vector3(10f, 3.3f, 12f), 1f);
                else
                    StampPart("SM_Floor_GroundRes", 0f, resRef, 1f);
                float groundH = groundShop ? 3.3f : 3.2f;
                int midCount = Mathf.Max(1, Mathf.RoundToInt((size.y - groundH) / 3f));
                for (int i = 0; i < midCount; i++)
                    StampPart("SM_Floor_MidMarcory", groundH + i * 3f, resRef, 1f);
                if (midCount <= 1)
                    StampPart("SM_Roof_HipTile", groundH + midCount * 3f, resRef, 1f, tileRoof);
                else
                    StampPart("SM_Roof_Parapet", groundH + midCount * 3f, new Vector3(10f, 3f, 12f), 1f);
                return;
            }

            // Quartiers populaires multi-étages (R+1 à R+5).
            var modRef = new Vector3(10f, 3f, 12f);
            bool hasShop = b.row == 1 || r.NextDouble() < 0.5;
            if (hasShop)
                StampPart("SM_Floor_GroundShop", 0f, modRef, 1f);
            else
                StampPart("SM_Floor_GroundRes", 0f, new Vector3(12f, 3.2f, 14.5f), 1f);
            float baseH = hasShop ? 3.3f : 3.2f;
            int floors = Mathf.Max(1, Mathf.RoundToInt((size.y - baseH) / 3f));
            for (int i = 0; i < floors; i++)
                StampPart("SM_Floor_MidPopular", baseH + i * 3f, modRef, 1f);
            if (floors <= 1 && r.NextDouble() < 0.55)
                StampPart("SM_Roof_HipMetal", baseH + floors * 3f, modRef, 1f, metalRoof);
            else
                StampPart("SM_Roof_Parapet", baseH + floors * 3f, modRef, 1f);
        }

        #endregion

        #region Mobilier des rues, points de jeu, taxis

        /// <summary>Lampadaires, poteaux électriques, abribus, caniveaux, panneaux, feux tricolores et cocotiers.</summary>
        private static void CreateStreetDecor(Transform city, CityLayout.Data layout, List<Mesh> meshes)
        {
            var root = new GameObject("Mobilier").transform;
            root.SetParent(city, false);
            CityDecor.Frame = Matrix4x4.identity;
            var chunkKit = new Dictionary<(Vector2Int, Material), BoxMeshBuilder>();
            var bulbs = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var sodiumBulbs = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var pools = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var wires = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var streetProps = new GameObject("MobilierRues").transform;
            streetProps.SetParent(root, false);
            int lampIndex = 0;
            float width = layout.sidewalkWidth > 0f ? layout.sidewalkWidth : 3f;

            District NearestDistrictAt(Vector3 p)
            {
                if (layout.districts == null || layout.districts.Length == 0)
                    return Yopougon;
                string bestName = layout.districts[0].name;
                float bestD2 = float.MaxValue;
                foreach (CityLayout.DistrictData d in layout.districts)
                {
                    float dx = d.x - p.x, dz = d.z - p.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < bestD2)
                    {
                        bestD2 = d2;
                        bestName = d.name;
                    }
                }
                return DistrictNamed(bestName);
            }

            for (int roadIndex = 0; roadIndex < layout.roads.Length; roadIndex++)
            {
                CityLayout.RoadData road = layout.roads[roadIndex];
                if (road.cls > 2 || road.ring)
                    continue;
                List<Vector3> points = Points(road.p);
                bool hasPoles = road.cls == 2 && NearestDistrictAt(points[0]).PowerPoles;
                float spacing = hasPoles ? 24f : 32f;
                float travelled = 0f, nextLamp = 14f;
                (Vector3, Vector3) prevWire = default;
                bool hasPrevWire = false;

                for (int i = 0; i + 1 < points.Count; i++)
                {
                    Vector3 a = points[i], b = points[i + 1];
                    Vector3 dir = b - a;
                    float length = dir.magnitude;
                    if (length < 0.5f)
                        continue;
                    dir /= length;
                    Vector3 across = Vector3.Cross(Vector3.up, dir);
                    while (nextLamp < travelled + length)
                    {
                        Vector3 p = a + dir * (nextLamp - travelled);
                        float side = hasPoles ? 1f : (((int)(nextLamp / spacing)) % 2 == 0 ? 1f : -1f);
                        Vector3 foot = p + across * side * (road.width * 0.5f + 0.5f);
                        // Pas sur une chaussée (la sienne dans un virage, une rue croisée ou parallèle), pas dans un carrefour.
                        if (!CityLayout.IsWater(foot.x, foot.z) && !OnBridge(road, foot) &&
                            s_roadIndex.Clearance(foot) > 0.25f && !NearJunction(foot, 1.5f))
                        {
                            Vector2Int chunk = ChunkOf(foot.x, foot.z);
                            Vector3 toRoad = -across * side;
                            if (hasPoles)
                            {
                                bool litPole = lampIndex % 2 == 0;
                                (Vector3, Vector3) anchors = CityDecor.AddPowerPoleChunked(chunk, chunkKit,
                                    ChunkBuilder(sodiumBulbs, chunk), ChunkBuilder(pools, chunk),
                                    foot + Vector3.up * SidewalkHeight, dir, toRoad, litPole, SidewalkHeight);
                                if (hasPrevWire && (prevWire.Item1 - anchors.Item1).sqrMagnitude < 36f * 36f)
                                {
                                    BoxMeshBuilder wb = ChunkBuilder(wires, chunk);
                                    CityDecor.AddWire(wb, prevWire.Item1, anchors.Item1, 0.55f);
                                    CityDecor.AddWire(wb, prevWire.Item2, anchors.Item2, 0.60f);
                                }
                                prevWire = anchors;
                                hasPrevWire = true;
                            }
                            else
                            {
                                CityDecor.AddStreetLampChunked(chunk, chunkKit, ChunkBuilder(bulbs, chunk),
                                    ChunkBuilder(pools, chunk), foot + Vector3.up * SidewalkHeight, toRoad, SidewalkHeight, false);
                            }

                            if (!NearMajorIntersection(p, 16f) && (p - a).sqrMagnitude > 64f && (p - b).sqrMagnitude > 64f)
                            {
                                Quaternion faceRoad = Quaternion.LookRotation(toRoad, Vector3.up);
                                // Caniveau avec grille d'évacuation au fil de la bordure (grands axes).
                                if (road.cls <= 1 && lampIndex % 2 == 0 && BlenderCityKit.HasModel("SM_StormDrain") &&
                                    s_roadIndex.Clearance(p + across * side * (road.width * 0.5f), roadIndex) > 0.5f)
                                {
                                    Vector3 drainPos = p + across * side * (road.width * 0.5f) + Vector3.up * 0.03f;
                                    BlenderCityKit.StampChunked("SM_StormDrain", chunk, chunkKit,
                                        Matrix4x4.TRS(drainPos, faceRoad, Vector3.one), null, Color.white, lampIndex);
                                }

                                Vector3 innerSidewalk = p + across * side * (road.width * 0.5f + width * 0.65f) + Vector3.up * SidewalkHeight;
                                // Abribus / abri gbaka sur les grands axes (espacement large, hors carrefours).
                                if (road.cls <= 1 && lampIndex % 5 == 2 && !NearMajorIntersection(p, 24f) &&
                                    (p - a).sqrMagnitude > 144f && (p - b).sqrMagnitude > 144f &&
                                    s_roadIndex.Clearance(p + across * side * (road.width * 0.5f + width * 0.55f)) > 1.6f &&
                                    BlenderCityKit.HasModel("SM_BusShelter"))
                                {
                                    Vector3 shelterPos = p + across * side * (road.width * 0.5f + width * 0.55f) + Vector3.up * SidewalkHeight;
                                    BlenderCityKit.StampChunked("SM_BusShelter", chunk, chunkKit,
                                        Matrix4x4.TRS(shelterPos, faceRoad, Vector3.one), null, Color.white, lampIndex);
                                    AddBoxCollider(streetProps.gameObject, shelterPos + Vector3.up * 1.1f, new Vector3(3.6f, 2.2f, 1.2f));
                                    if (BlenderCityKit.HasModel("SM_StreetBin"))
                                    {
                                        BlenderCityKit.StampChunked("SM_StreetBin", chunk, chunkKit,
                                            Matrix4x4.TRS(shelterPos + dir * 2.4f, faceRoad, Vector3.one), null, Color.white, lampIndex);
                                    }
                                }
                                else if (s_roadIndex.Clearance(innerSidewalk) < 0.6f)
                                {
                                    // Trottoir trop étroit à cet endroit (rue voisine) : rien de plus.
                                }
                                else if (lampIndex % 6 == 1)
                                {
                                    PropLibrary.Place("utility_box_01", streetProps, innerSidewalk, faceRoad.eulerAngles.y, 1f, true, 0.03f);
                                }
                                else if (lampIndex % 8 == 3)
                                {
                                    PropLibrary.Place("painted_wooden_bench", streetProps, innerSidewalk, faceRoad.eulerAngles.y + 180f, 1f, true, 0.03f);
                                }
                                else if (road.cls <= 1 && lampIndex % 7 == 4 && BlenderCityKit.HasModel("SM_RoadSign_Speed"))
                                {
                                    Quaternion faceTraffic = Quaternion.LookRotation(-dir, Vector3.up);
                                    string sign = (lampIndex % 14 == 4) ? "SM_RoadSign_Pedestrian" : "SM_RoadSign_Speed";
                                    BlenderCityKit.StampChunked(sign, chunk, chunkKit,
                                        Matrix4x4.TRS(foot + Vector3.up * SidewalkHeight, faceTraffic, Vector3.one),
                                        null, Color.white, lampIndex);
                                }
                            }
                            lampIndex++;
                        }
                        else
                        {
                            hasPrevWire = false;
                        }
                        nextLamp += spacing;
                    }
                    travelled += length;
                }
            }

            // Glissières de sécurité le long des voies longeant directement la lagune (hors ponts).
            if (BlenderCityKit.HasModel("SM_Guardrail"))
            {
                foreach (CityLayout.RoadData road in layout.roads)
                {
                    if (road.cls > 1)
                        continue;
                    List<Vector3> points = Points(road.p);
                    for (int i = 0; i + 1 < points.Count; i++)
                    {
                        Vector3 a = points[i], b = points[i + 1];
                        Vector3 ab = b - a;
                        float len = ab.magnitude;
                        if (len < 4f)
                            continue;
                        Vector3 dir = ab / len;
                        Vector3 across = Vector3.Cross(Vector3.up, dir);
                        for (float s = 2f; s <= len - 2f; s += 4f)
                        {
                            Vector3 p = a + dir * s;
                            if (NearMajorIntersection(p, 13.5f) || OnBridge(road, p))
                                continue;
                            foreach (float side in new[] { -1f, 1f })
                            {
                                Vector3 curb = p + across * side * (road.width * 0.5f + 0.25f);
                                Vector3 bankCheck = p + across * side * (road.width * 0.5f + 5.5f);
                                Vector3 endA = curb - dir * 1.9f;
                                Vector3 endB = curb + dir * 1.9f;
                                if (!CityLayout.IsWater(curb.x, curb.z) &&
                                    !CityLayout.IsWater(endA.x, endA.z) &&
                                    !CityLayout.IsWater(endB.x, endB.z) &&
                                    CityLayout.IsWater(bankCheck.x, bankCheck.z))
                                {
                                    Quaternion faceRoad = Quaternion.LookRotation(-across * side, Vector3.up);
                                    BlenderCityKit.StampChunked("SM_Guardrail", ChunkOf(curb.x, curb.z), chunkKit,
                                        Matrix4x4.TRS(curb + Vector3.up * 0.05f, faceRoad, Vector3.one),
                                        null, Color.white, 0);
                                }
                            }
                        }
                    }
                }
            }

            // Feux tricolores réalistes aux carrefours des grands axes.
            CreateLayoutTrafficLights(root, chunkKit, meshes);

            // Cocotiers : devant une façade sur cinq dans les quartiers où il y en a.
            var palms = new GameObject("Cocotiers").transform;
            palms.SetParent(root, false);
            for (int i = 0; i < layout.anchors.Length; i += 5)
            {
                CityLayout.AnchorData anchor = layout.anchors[i];
                District district = DistrictNamed(anchor.district);
                if (s_decor.NextDouble() >= district.PalmChance)
                    continue;
                Vector3 toRoad = new Vector3(anchor.fx, 0f, anchor.fz);
                Vector3 along = Vector3.Cross(Vector3.up, toRoad);
                Vector3 p = new Vector3(anchor.x, SidewalkHeight, anchor.z) + toRoad * (width * 0.3f) + along * 3f;
                PalmBuilder.Place(palms, p, s_decor.Next(PalmBuilder.VariantCount), s_decor.Next(360));
            }

            foreach (var pair in chunkKit)
            {
                if (!pair.Value.IsEmpty)
                    AddMeshObject(root, "Mobilier_" + pair.Key.Item2.name + "_" + pair.Key.Item1.x + "_" + pair.Key.Item1.y,
                        pair.Value, pair.Key.Item2, meshes);
            }
            foreach (var pair in wires)
            {
                if (!pair.Value.IsEmpty)
                    AddMeshObject(root, "Cables_" + pair.Key.x + "_" + pair.Key.y, pair.Value, CityMaterials.Painted(), meshes);
            }
            foreach (var pair in bulbs)
            {
                var holder = new GameObject("Lumieres_" + pair.Key.x + "_" + pair.Key.y).transform;
                holder.SetParent(root, false);
                pools.TryGetValue(pair.Key, out BoxMeshBuilder poolBuilder);
                AddLightMeshes(holder, pair.Value, poolBuilder ?? new BoxMeshBuilder(), CityDecor.BulbMaterial, meshes);
            }
            foreach (var pair in sodiumBulbs)
            {
                var holder = new GameObject("LumieresSodium_" + pair.Key.x + "_" + pair.Key.y).transform;
                holder.SetParent(root, false);
                BoxMeshBuilder poolBuilder = (!bulbs.ContainsKey(pair.Key) && pools.TryGetValue(pair.Key, out var pb)) ? pb : new BoxMeshBuilder();
                AddLightMeshes(holder, pair.Value, poolBuilder, CityDecor.SodiumBulbMaterial, meshes);
            }
        }

        private static bool OnBridge(CityLayout.RoadData road, Vector3 p)
        {
            for (int i = 0; road.b != null && i + 3 < road.b.Length; i += 4)
            {
                var a = new Vector3(road.b[i], 0f, road.b[i + 1]);
                var b = new Vector3(road.b[i + 2], 0f, road.b[i + 3]);
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.01f));
                if ((a + ab * t - p).sqrMagnitude < (road.width + 4f) * (road.width + 4f))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Points de jeu sur les trottoirs de chaque quartier (rangs libres de CityPlaces) : un dépôt,
        /// des points de livraison, des étals de nourriture et des vendeuses de boissons.
        /// </summary>
        private static void CreateLayoutInteractables(Transform city, Transform interactables, System.Random random, ref int nextId)
        {
            Material deliveryMaterial = GetOrCreateMaterial("M_Delivery", new Color(0.25f, 0.8f, 0.35f));
            Material shopMaterial = GetOrCreateMaterial("M_Shop", new Color(0.95f, 0.55f, 0.15f));
            Material jobMaterial = GetOrCreateMaterial("M_JobBoard", new Color(0.2f, 0.45f, 0.95f));
            var decor = new GameObject("DecorPoints").transform;
            decor.SetParent(city, false);

            // Quincailleries : machettes et couteaux (rangés dans le sac, voir Gameplay.Combat.Weapons).
            foreach ((string district, int rank) in new[] { ("Adjamé", 17), ("Yopougon", 13), ("Treichville", 15) })
            {
                CityLayout.Place place = CityLayout.Spot(district, rank);
                string vendor = NextVendorName();
                var store = CreatePoint<StorePoint>(interactables, nextId++, "Quincaillerie de " + district, place.Position);
                store.transform.rotation = Quaternion.LookRotation(-place.ToRoad, Vector3.up);
                store.EditorSetupStore(vendor, "Outils, machettes, couteaux… Qu'est-ce qu'il te faut ?", new[]
                {
                    Product(Gameplay.Combat.Weapons.Machete, Gameplay.Combat.Weapons.Price, 0, 0),
                    Product(Gameplay.Combat.Weapons.Knife, Gameplay.Combat.Weapons.Price, 0, 0),
                });
                AddKiosk(store.transform, s_pendingMeshes);
                AddVendor(store.transform, new Vector3(0f, 0f, 1.75f), 180f, s_vendorIndex++, true);
                PropLibrary.Place("machete", store.transform, new Vector3(0.45f, 1.08f, 1.18f), 55f, 1f, false, 0.02f, pitch: 90f);
                for (int c = 0; c < 3; c++)
                    PropLibrary.Place("cement_bag", store.transform, new Vector3(-1.55f, c * 0.17f, 1.0f), -10f + c * 14f, 1f, c == 0, 0.02f);
                PropLibrary.Place("hand_truck", store.transform, new Vector3(-1.65f, 0f, 1.65f), -35f, 1f, true, 0.02f);
                PropLibrary.Place("small_lpg_tank", store.transform, new Vector3(1.55f, 0f, 0.9f), 20f, 1f, true, 0.02f);
                PropLibrary.Place("small_lpg_tank", store.transform, new Vector3(1.55f, 0f, 1.45f), 160f, 1f, false, 0.02f);
                PropLibrary.Place("portable_generator", store.transform, new Vector3(1.65f, 0f, 2.0f), 25f, 1f, true, 0.02f);
            }

            // Armurerie : armes à feu et munitions, réservées aux détenteurs d'un port d'arme (vendu ici en
            // attendant l'application du gouvernement). Voir Gameplay.Combat.Firearms.
            {
                CityLayout.Place place = CityLayout.Spot("Plateau", 5);
                var store = CreatePoint<StorePoint>(interactables, nextId++, "Armurerie du Plateau", place.Position);
                store.transform.rotation = Quaternion.LookRotation(-place.ToRoad, Vector3.up);
                var products = new System.Collections.Generic.List<StorePoint.Product>
                {
                    Product(Gameplay.Combat.Weapons.Permit, Gameplay.Combat.Weapons.PermitPrice, 0, 0),
                };
                foreach (Gameplay.Combat.FirearmDef def in Gameplay.Combat.Firearms.All)
                    products.Add(Product(def.Name, def.Price, 0, 0));
                foreach (Gameplay.Combat.FirearmDef def in Gameplay.Combat.Firearms.All)
                    products.Add(Product(def.Ammo, def.AmmoPrice, 0, 0));
                store.EditorSetupStore("Commandant Koffi", "Armes et munitions, port d'arme obligatoire. Qu'est-ce qu'il te faut ?", products.ToArray());
                AddKiosk(store.transform, s_pendingMeshes);
                AddVendor(store.transform, new Vector3(0f, 0f, 1.75f), 180f, s_vendorIndex++, true);
                PropLibrary.Place("glock17", store.transform, new Vector3(-0.35f, 1.08f, 1.18f), 80f, 1f, false, 0.02f);
                PropLibrary.Place("taser", store.transform, new Vector3(0.05f, 1.08f, 1.18f), 100f, 1f, false, 0.02f);
                PropLibrary.Place("ak47", store.transform, new Vector3(0.0f, 1.08f, 1.40f), 0f, 1f, false, 0.02f);
            }

            foreach (CityLayout.DistrictData data in CityLayout.Layout.districts)
            {
                District district = DistrictNamed(data.name);
                int count = CityLayout.SpotCount(data.name);
                bool depot = false;
                int deliveries = 0, food = 0;
                for (int rank = CityPlaces.FirstFreeRank; rank < count; rank += 3)
                {
                    CityLayout.Place place = CityLayout.Spot(data.name, rank);
                    if (!depot)
                    {
                        depot = true;
                        var board = CreatePoint<JobBoardPoint>(interactables, nextId++, "Dépôt Express " + district.Name, place.Position);
                        AddJobBoardVisual(board.transform, Vector3.zero, jobMaterial);
                        continue;
                    }
                    int roll = random.Next(10);
                    if (roll < 4 && deliveries < 16)
                    {
                        deliveries++;
                        string placeName = district.PlaceTypes[random.Next(district.PlaceTypes.Length)] + " " + district.Name + " n°" + nextId;
                        var point = CreatePoint<DeliveryPoint>(interactables, nextId++, placeName, place.Position);
                        AddVisual(point.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(2f, 0.02f, 2f), deliveryMaterial, false);
                        AddVisual(point.transform, PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f), new Vector3(0.15f, 2.4f, 0.15f), deliveryMaterial, true);
                    }
                    else if (roll < 6 && district.HasFood && food < 6)
                    {
                        food++;
                        Vector3 stall = place.Position + place.Along * 1.5f;
                        CreateShop(interactables, ref nextId, Foods[random.Next(Foods.Length)], Vendors[random.Next(Vendors.Length)], stall, shopMaterial);
                        CityDecor.AddMaquis(decor, stall, place.Along, -place.ToRoad, s_decor);
                    }
                    else if (roll == 6 && (district.HasFood || district == Marcory))
                    {
                        s_pendingDrinks.Add((city, place.Position - place.Along * 1.5f));
                    }
                }
            }
        }

        /// <summary>Stations de taxis : au bord de la chaussée, près du centre de chaque quartier.</summary>
        private static void CreateLayoutTaxiStands(Transform city)
        {
            var root = new GameObject("TaxiStands").transform;
            root.SetParent(city, false);
            int index = 0;
            foreach (CityLayout.DistrictData data in CityLayout.Layout.districts)
            {
                for (int k = 0; k < (data.name == "Plateau" ? 3 : 1); k++)
                {
                    int rank = (data.name == "Plateau" && k == 0) ? 3 : CityPlaces.FirstFreeRank - 1 - k * 2;
                    CityLayout.Place place = CityLayout.Spot(data.name, rank);
                    var stand = new GameObject("TaxiStand_" + index++);
                    stand.transform.SetParent(root, false);
                    stand.transform.localPosition = new Vector3(place.Position.x, 0.1f, place.Position.z) + place.ToRoad * 4.5f;
                    stand.transform.localRotation = Quaternion.LookRotation(place.Along, Vector3.up);
                    stand.AddComponent<Gameplay.Vehicles.TaxiStand>();
                }
            }
        }

        #endregion
    }
}
