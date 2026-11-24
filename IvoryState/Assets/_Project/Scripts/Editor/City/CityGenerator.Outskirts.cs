using System.Collections.Generic;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Abords de la ville : relief, boulevards aux virages arrondis, forêt du Banco, collines à l'horizon.
    /// <para>
    /// Le centre (la grille de rues) reste plat, à y = 0. Autour, une couronne de
    /// <see cref="OuterExtent"/> - <see cref="CoreExtent"/> m porte un vrai terrain (maillage + MeshCollider) :
    /// collines douces, colline boisée du Banco au nord-ouest, plateau de Cocody à l'est, berges de la
    /// lagune. Deux boulevards courbes (au nord et au sud de la lagune) prolongent les rues de la grille.
    /// </para>
    /// <para>
    /// Mobile : terrain découpé en tronçons de 96 m (culling), arbres fusionnés par tronçon (deux draw
    /// calls : troncs et feuillage), collines lointaines en un seul maillage sans collider.
    /// </para>
    /// </summary>
    internal static partial class CityGenerator
    {
        private const float CoreExtent = (GridRadius + 0.5f) * CellSize;   // 342 m
        private const float OuterExtent = CoreExtent + 216f;                 // 558 m (multiple du pas)
        private const float TerrainStep = 6f;
        private const int ChunkCells = 16;                                   // 96 m
        private const float RingRadius = 440f;
        private const float BoulevardWidth = 12f;
        private const float TrailWidth = 3.2f;
        private static readonly Vector2 BancoCenter = new Vector2(-470f, 470f);
        private const float BancoRadius = 190f;

        private sealed class Road
        {
            public string Name;
            public readonly List<Vector3> Points = new List<Vector3>();
            public float Width;
            public bool Dirt;
        }

        private static readonly List<Road> s_roads = new List<Road>();
        private static readonly Dictionary<Vector2Int, List<(Vector3 point, float halfWidth)>> s_roadGrid =
            new Dictionary<Vector2Int, List<(Vector3, float)>>();
        private const float RoadGridCell = 16f;

        private static float LagoonZ => LagoonRow * CellSize;

        private static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Distance hors du centre plat (négative à l'intérieur).</summary>
        private static float CoreDistance(float x, float z) => Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - CoreExtent;

        /// <summary>Relief naturel (avant aplanissement sous les routes).</summary>
        private static float NaturalHeight(float x, float z)
        {
            float d = CoreDistance(x, z);
            if (d <= 0f)
                return 0f;

            float dz = Mathf.Abs(z - LagoonZ);
            if (dz < 26f)
                return -1.5f;                                            // fond de la lagune
            if (dz < 34f)
                return Mathf.Lerp(-1.5f, 0.2f, (dz - 26f) / 8f);         // berge

            float ramp = Smooth(0f, 90f, d);
            float noise = Mathf.PerlinNoise(x * 0.0055f + 31.7f, z * 0.0055f + 12.3f);
            float detail = Mathf.PerlinNoise(x * 0.02f + 3.1f, z * 0.02f + 7.9f);
            float hills = 1.5f + 11f * noise + 1.5f * detail;
            float bx = x - BancoCenter.x, bz = z - BancoCenter.y;
            float banco = 30f * Mathf.Exp(-(bx * bx + bz * bz) / (2f * 120f * 120f));
            float cocody = 9f * Smooth(250f, 480f, x) * Smooth(LagoonZ + 60f, LagoonZ + 180f, z);
            float h = ramp * (hills + banco + cocody);
            h *= Smooth(34f, 110f, dz);                                  // les berges restent basses
            return Mathf.Max(0.2f * Smooth(0f, 12f, d), h);
        }

        #region Routes

        private static void PlanOutskirtRoads()
        {
            s_roads.Clear();
            s_roadGrid.Clear();

            // Boulevard du Nord : de la rive nord de la lagune (z = -38) jusqu'à l'autre rive, par le nord.
            var north = new List<Vector2> { new Vector2(CoreExtent, -38f) };
            AddSuperellipseArc(north, -Mathf.Asin(Mathf.Pow(38f / RingRadius, 2f)), Mathf.PI + Mathf.Asin(Mathf.Pow(38f / RingRadius, 2f)));
            north.Add(new Vector2(-CoreExtent, -38f));
            AddRoad("Boulevard du Nord", north, BoulevardWidth, false);

            // Boulevard du Sud (z = -114, rive sud) : par le sud.
            var south = new List<Vector2> { new Vector2(CoreExtent, -114f) };
            float s = Mathf.Asin(Mathf.Pow(114f / RingRadius, 2f));
            AddSuperellipseArc(south, -s, -Mathf.PI + s);
            south.Add(new Vector2(-CoreExtent, -114f));
            AddRoad("Boulevard du Sud", south, BoulevardWidth, false);

            // Rues de la grille prolongées jusqu'aux boulevards.
            foreach (float x in new[] { -190f, -38f, 38f, 190f })
            {
                AddRoad("Avenue nord " + x, new List<Vector2> { new Vector2(x, CoreExtent), new Vector2(x, RingZ(x) + 2f) }, BoulevardWidth, false);
                AddRoad("Avenue sud " + x, new List<Vector2> { new Vector2(x, -CoreExtent), new Vector2(x, -RingZ(x) - 2f) }, BoulevardWidth, false);
            }
            foreach (float z in new[] { 114f, 266f, -190f, -266f })
            {
                AddRoad("Avenue est " + z, new List<Vector2> { new Vector2(CoreExtent, z), new Vector2(RingZ(z) + 2f, z) }, BoulevardWidth, false);
                AddRoad("Avenue ouest " + z, new List<Vector2> { new Vector2(-CoreExtent, z), new Vector2(-RingZ(z) - 2f, z) }, BoulevardWidth, false);
            }

            // Piste du Banco : du boulevard jusqu'au sommet boisé, puis une boucle sous les arbres.
            float cornerAngle = Mathf.PI * 0.75f;
            Vector2 start = Superellipse(cornerAngle);
            var trail = new List<Vector2> { start };
            var rng = new System.Random(4242);
            Vector2 toward = (BancoCenter - start).normalized;
            Vector2 side = new Vector2(-toward.y, toward.x);
            for (int i = 1; i <= 6; i++)
            {
                float t = i / 7f;
                Vector2 p = Vector2.Lerp(start, BancoCenter - toward * 60f, t) + side * Mathf.Sin(t * Mathf.PI * 2f) * 14f;
                trail.Add(p);
            }
            for (int i = 0; i <= 28; i++)
            {
                float a = Mathf.Atan2(-toward.y, -toward.x) + i * Mathf.PI * 2f / 28f;
                float r = 60f + (float)(rng.NextDouble() - 0.5) * 10f;
                trail.Add(BancoCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            AddRoad("Piste du Banco", trail, TrailWidth, true);
        }

        private static Vector2 Superellipse(float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return new Vector2(RingRadius * Mathf.Sign(c) * Mathf.Sqrt(Mathf.Abs(c)), RingRadius * Mathf.Sign(s) * Mathf.Sqrt(Mathf.Abs(s)));
        }

        /// <summary>Position du boulevard (superellipse) sur l'axe perpendiculaire, au point <paramref name="along"/>.</summary>
        private static float RingZ(float along)
        {
            float u = Mathf.Abs(along) / RingRadius;
            return RingRadius * Mathf.Pow(Mathf.Max(0f, 1f - u * u * u * u), 0.25f);
        }

        private static void AddSuperellipseArc(List<Vector2> points, float from, float to)
        {
            const int steps = 1600;
            for (int i = 0; i <= steps; i++)
                points.Add(Superellipse(Mathf.Lerp(from, to, i / (float)steps)));
        }

        /// <summary>Rééchantillonne tous les 4 m, applique le relief lissé (pente douce) et indexe la route.</summary>
        private static void AddRoad(string name, List<Vector2> path, float width, bool dirt)
        {
            var road = new Road { Name = name, Width = width, Dirt = dirt };

            // Rééchantillonnage régulier (courbe de Catmull-Rom pour arrondir les angles des polylignes courtes).
            var dense = new List<Vector2>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 p0 = path[Mathf.Max(i - 1, 0)], p1 = path[i], p2 = path[i + 1], p3 = path[Mathf.Min(i + 2, path.Count - 1)];
                int sub = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 2f));
                for (int k = 0; k < sub; k++)
                    dense.Add(CatmullRom(p0, p1, p2, p3, k / (float)sub));
            }
            dense.Add(path[path.Count - 1]);

            var flat = new List<Vector2> { dense[0] };
            float carry = 0f;
            for (int i = 1; i < dense.Count; i++)
            {
                carry += Vector2.Distance(dense[i], dense[i - 1]);
                if (carry >= 4f || i == dense.Count - 1)
                {
                    flat.Add(dense[i]);
                    carry = 0f;
                }
            }

            // Hauteurs : relief naturel, moyenné sur ±24 m, jamais sous l'eau.
            var raw = new float[flat.Count];
            for (int i = 0; i < flat.Count; i++)
                raw[i] = NaturalHeight(flat[i].x, flat[i].y);
            for (int i = 0; i < flat.Count; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int k = -6; k <= 6; k++)
                {
                    int j = Mathf.Clamp(i + k, 0, flat.Count - 1);
                    sum += raw[j];
                    count++;
                }
                float h = Mathf.Max(0.15f, sum / count);
                // Les extrémités qui touchent la grille plate restent à 0.
                if (CoreDistance(flat[i].x, flat[i].y) < 1f)
                    h = 0f;
                road.Points.Add(new Vector3(flat[i].x, h, flat[i].y));
            }

            s_roads.Add(road);
            foreach (Vector3 point in road.Points)
            {
                var cell = new Vector2Int(Mathf.FloorToInt(point.x / RoadGridCell), Mathf.FloorToInt(point.z / RoadGridCell));
                if (!s_roadGrid.TryGetValue(cell, out var list))
                    s_roadGrid[cell] = list = new List<(Vector3, float)>();
                list.Add((point, width * 0.5f));
            }
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>Point de route le plus proche (distance au bord de la chaussée, hauteur de la route).</summary>
        private static bool NearestRoad(float x, float z, float search, out float edgeDistance, out float roadHeight)
        {
            edgeDistance = float.MaxValue;
            roadHeight = 0f;
            int cx = Mathf.FloorToInt(x / RoadGridCell), cz = Mathf.FloorToInt(z / RoadGridCell);
            int reach = Mathf.CeilToInt(search / RoadGridCell);
            bool found = false;
            for (int i = -reach; i <= reach; i++)
            {
                for (int j = -reach; j <= reach; j++)
                {
                    if (!s_roadGrid.TryGetValue(new Vector2Int(cx + i, cz + j), out var list))
                        continue;
                    foreach ((Vector3 point, float halfWidth) in list)
                    {
                        float dx = point.x - x, dz = point.z - z;
                        float edge = Mathf.Sqrt(dx * dx + dz * dz) - halfWidth;
                        if (edge < edgeDistance)
                        {
                            edgeDistance = edge;
                            roadHeight = point.y;
                            found = true;
                        }
                    }
                }
            }
            return found && edgeDistance < search;
        }

        /// <summary>Hauteur du sol (relief naturel, aplani sous les routes et sur leurs bas-côtés).</summary>
        private static float GroundHeight(float x, float z)
        {
            float natural = NaturalHeight(x, z);
            if (!NearestRoad(x, z, 14f, out float edge, out float roadHeight))
                return natural;
            float target = roadHeight - 0.04f;
            if (edge <= 2f)
                return target;
            return Mathf.Lerp(target, natural, Smooth(2f, 14f, edge));
        }

        #endregion

        #region Terrain

        private static readonly Color Grass = new Color(0.33f, 0.44f, 0.20f);
        private static readonly Color DryGrass = new Color(0.52f, 0.50f, 0.28f);
        private static readonly Color Laterite = new Color(0.62f, 0.33f, 0.20f);
        private static readonly Color Sand = new Color(0.78f, 0.70f, 0.52f);
        private static readonly Color ForestFloor = new Color(0.22f, 0.28f, 0.14f);

        private static float BancoFactor(float x, float z)
        {
            float d = Vector2.Distance(new Vector2(x, z), BancoCenter);
            return 1f - Smooth(BancoRadius * 0.6f, BancoRadius, d);
        }

        private static Color GroundColor(float x, float z, float height)
        {
            float n = Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f + 9f);
            Color c = Color.Lerp(Grass, DryGrass, n * 0.8f);
            c = Color.Lerp(c, ForestFloor, BancoFactor(x, z) * 0.85f);
            float dz = Mathf.Abs(z - LagoonZ);
            if (dz < 46f)
                c = Color.Lerp(Sand, c, Smooth(34f, 46f, dz));
            if (NearestRoad(x, z, 6f, out float edge, out _))
                c = Color.Lerp(Laterite, c, Smooth(0.5f, 6f, edge));
            if (height > 18f)
                c = Color.Lerp(c, ForestFloor, 0.3f);
            return c;
        }

        /// <summary>Terrain des abords, par tronçons de 96 m (MeshCollider), sans la grille centrale.</summary>
        private static void CreateTerrain(Transform city, List<Mesh> meshes)
        {
            var root = new GameObject("Relief").transform;
            root.SetParent(city, false);
            root.gameObject.isStatic = true;
            Material ground = CityMaterials.Ground();

            int cells = Mathf.RoundToInt(OuterExtent * 2f / TerrainStep);
            int chunks = Mathf.CeilToInt(cells / (float)ChunkCells);
            for (int cx = 0; cx < chunks; cx++)
            {
                for (int cz = 0; cz < chunks; cz++)
                {
                    Mesh mesh = BuildTerrainChunk(cx * ChunkCells, cz * ChunkCells, Mathf.Min(ChunkCells, cells - cx * ChunkCells),
                        Mathf.Min(ChunkCells, cells - cz * ChunkCells), "Relief_" + cx + "_" + cz);
                    if (mesh == null)
                        continue;
                    meshes.Add(mesh);
                    var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                    go.transform.SetParent(root, false);
                    go.isStatic = true;
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    go.GetComponent<MeshRenderer>().sharedMaterial = ground;
                    go.GetComponent<MeshCollider>().sharedMesh = mesh;
                }
            }
        }

        private static Mesh BuildTerrainChunk(int startX, int startZ, int countX, int countZ, string name)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            int stride = countZ + 1;

            for (int i = 0; i <= countX; i++)
            {
                for (int j = 0; j <= countZ; j++)
                {
                    float x = -OuterExtent + (startX + i) * TerrainStep;
                    float z = -OuterExtent + (startZ + j) * TerrainStep;
                    float h = GroundHeight(x, z);
                    vertices.Add(new Vector3(x, h, z));
                    Color c = GroundColor(x, z, h);
                    Color32 c32 = c;
                    c32.a = (byte)((startX + i) * 31 + (startZ + j) * 17);
                    colors.Add(c32);
                    uvs.Add(new Vector2(x, z));
                }
            }

            for (int i = 0; i < countX; i++)
            {
                for (int j = 0; j < countZ; j++)
                {
                    float x0 = -OuterExtent + (startX + i) * TerrainStep, x1 = x0 + TerrainStep;
                    float z0 = -OuterExtent + (startZ + j) * TerrainStep, z1 = z0 + TerrainStep;
                    bool inCore = x0 >= -CoreExtent - 0.01f && x1 <= CoreExtent + 0.01f && z0 >= -CoreExtent - 0.01f && z1 <= CoreExtent + 0.01f;
                    if (inCore)
                        continue;
                    int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(d);
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                }
            }

            if (triangles.Count == 0)
                return null;

            var mesh = new Mesh { name = name };
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
            return mesh;
        }

        /// <summary>Routes des abords : bitume (boulevards) ou latérite (piste), posées sur le relief.</summary>
        private static void CreateOutskirtRoads(Transform city, Material asphalt, List<Mesh> meshes)
        {
            var root = new GameObject("Boulevards").transform;
            root.SetParent(city, false);
            var paved = new BoxMeshBuilder();
            var dirt = new BoxMeshBuilder();
            var lines = new BoxMeshBuilder();
            foreach (Road road in s_roads)
            {
                (road.Dirt ? dirt : paved).AddRibbon(road.Points, road.Width, 0.03f);
                if (road.Dirt)
                    continue;

                // Ligne médiane discontinue : tirets de 3 m tous les 8 m.
                for (int i = 0; i + 1 < road.Points.Count; i += 2)
                {
                    Vector3 a = road.Points[i], b = road.Points[i + 1];
                    Vector3 dir = b - a;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.01f)
                        continue;
                    lines.AddFlatQuad((a + b) * 0.5f + Vector3.up * 0.05f, dir, 3f, 0.15f);
                }
            }

            AddMeshObject(root, "Bitume", paved, asphalt, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            Material laterite = CityMaterials.Plain("M_Laterite", new Color(0.60f, 0.34f, 0.22f), 0.05f);
            AddMeshObject(root, "Piste", dirt, laterite, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            AddMeshObject(root, "LigneMediane", lines, s_markingMaterial, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
        }

        #endregion

        #region Forêt et arbres

        /// <summary>
        /// Arbres : forêt dense du Banco (colline nord-ouest), bosquets épars ailleurs, cocotiers sur les
        /// berges. Fusionnés par tronçon de 96 m ; un collider par tronc.
        /// </summary>
        private static void CreateForest(Transform city, List<Mesh> meshes)
        {
            var root = new GameObject("Foret").transform;
            root.SetParent(city, false);
            root.gameObject.isStatic = true;
            Material bark = CityMaterials.Bark();
            Material leaves = CityMaterials.Foliage();
            var rng = new System.Random(1957); // le Banco est un parc national depuis 1953 ; graine arbitraire

            var trunks = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var crowns = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var colliders = new Dictionary<Vector2Int, GameObject>();

            const float spacing = 8f;
            for (float x = -OuterExtent + 4f; x < OuterExtent - 4f; x += spacing)
            {
                for (float z = -OuterExtent + 4f; z < OuterExtent - 4f; z += spacing)
                {
                    float px = x + (float)(rng.NextDouble() - 0.5) * spacing * 0.9f;
                    float pz = z + (float)(rng.NextDouble() - 0.5) * spacing * 0.9f;
                    double roll = rng.NextDouble();
                    if (CoreDistance(px, pz) < 6f)
                        continue;
                    float dz = Mathf.Abs(pz - LagoonZ);
                    if (dz < 36f)
                        continue;
                    if (NearestRoad(px, pz, 9f, out _, out _))
                        continue;
                    if (IsReservedOutskirt(px, pz))
                        continue;

                    float banco = BancoFactor(px, pz);
                    float density = Mathf.Lerp(0.04f, 0.6f, banco);
                    if (roll > density)
                        continue;

                    var key = new Vector2Int(Mathf.FloorToInt((px + OuterExtent) / 96f), Mathf.FloorToInt((pz + OuterExtent) / 96f));
                    if (!trunks.TryGetValue(key, out BoxMeshBuilder trunk))
                    {
                        trunks[key] = trunk = new BoxMeshBuilder();
                        crowns[key] = new BoxMeshBuilder();
                        var holder = new GameObject("Troncs_" + key.x + "_" + key.y);
                        holder.transform.SetParent(root, false);
                        holder.isStatic = true;
                        colliders[key] = holder;
                    }

                    float ground = GroundHeight(px, pz);
                    var basePosition = new Vector3(px, ground - 0.2f, pz);
                    float scale = Mathf.Lerp(0.8f, 1.35f, (float)rng.NextDouble()) * Mathf.Lerp(1f, 1.4f, banco);
                    AddTree(trunk, crowns[key], basePosition, scale, rng, banco > 0.5f);
                    AddBoxCollider(colliders[key], basePosition + Vector3.up * 2f, new Vector3(0.6f * scale, 4f, 0.6f * scale));
                }
            }

            foreach (KeyValuePair<Vector2Int, BoxMeshBuilder> entry in trunks)
            {
                string suffix = entry.Key.x + "_" + entry.Key.y;
                AddMeshObject(root, "ArbresTroncs_" + suffix, entry.Value, bark, meshes);
                AddMeshObject(root, "ArbresFeuillage_" + suffix, crowns[entry.Key], leaves, meshes);
            }

            // Cocotiers sur les berges de la lagune, hors de la grille.
            for (float x = -OuterExtent + 10f; x < OuterExtent - 10f; x += 13f)
            {
                if (Mathf.Abs(x) < CoreExtent + 8f)
                    continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = LagoonZ + side * (38f + (float)rng.NextDouble() * 6f);
                    if (NearestRoad(x, z, 7f, out _, out _) || rng.NextDouble() < 0.35)
                        continue;
                    PalmBuilder.Place(root, new Vector3(x, GroundHeight(x, z) - 0.1f, z), rng.Next(PalmBuilder.VariantCount), rng.Next(360));
                }
            }
        }

        private static readonly Color[] LeafColors =
        {
            new Color(0.20f, 0.36f, 0.14f), new Color(0.26f, 0.42f, 0.16f), new Color(0.16f, 0.30f, 0.12f), new Color(0.32f, 0.44f, 0.18f),
        };

        /// <summary>Arbre feuillu : tronc conique, 3 à 5 masses de feuillage à facettes.</summary>
        private static void AddTree(BoxMeshBuilder trunk, BoxMeshBuilder crown, Vector3 basePosition, float scale, System.Random rng, bool tall)
        {
            float height = (tall ? 7f : 4.5f) * scale;
            trunk.SetColor(new Color(0.42f, 0.33f, 0.25f), rng.Next(256));
            trunk.AddCone(basePosition, 0.32f * scale, 0.18f * scale, height, 6);

            crown.SetColor(LeafColors[rng.Next(LeafColors.Length)], rng.Next(256));
            // Masses de feuillage arrondies (7 × 4 facettes), décalées et inclinées pour casser la symétrie.
            int blobs = tall ? 2 + rng.Next(2) : 2;
            Vector3 top = basePosition + Vector3.up * height;
            for (int i = 0; i < blobs; i++)
            {
                var offset = new Vector3((float)(rng.NextDouble() - 0.5) * 3.2f, (float)rng.NextDouble() * 1.6f - 0.4f, (float)(rng.NextDouble() - 0.5) * 3.2f) * scale;
                float r = Mathf.Lerp(2.0f, 3.2f, (float)rng.NextDouble()) * scale;
                crown.SetColor(LeafColors[rng.Next(LeafColors.Length)], rng.Next(256));
                crown.AddEllipsoid(top + offset, new Vector3(r, r * Mathf.Lerp(0.6f, 0.8f, (float)rng.NextDouble()), r * 0.9f), 7, 4);
            }
        }

        #endregion

        #region Lieux des abords

        // Zones réservées dans les abords (belvédère…) : pas d'arbre dessus.
        private static readonly List<(Vector2 center, float radius)> s_reserved = new List<(Vector2, float)>();

        private static bool IsReservedOutskirt(float x, float z)
        {
            foreach ((Vector2 center, float radius) in s_reserved)
            {
                if ((new Vector2(x, z) - center).sqrMagnitude < radius * radius)
                    return true;
            }
            return false;
        }

        /// <summary>Belvédère au sommet du Banco : terrasse, bancs, kiosque de boissons et vue sur la ville.</summary>
        private static void CreateBancoViewpoint(Transform city, Transform interactables, List<Mesh> meshes, ref int nextId)
        {
            Vector2 c = BancoCenter;
            float h = GroundHeight(c.x, c.y);
            var root = new GameObject("Belvédère du Banco").transform;
            root.SetParent(city, false);
            root.localPosition = new Vector3(c.x, h, c.y);

            var stone = new BoxMeshBuilder();
            stone.SetColor(new Color(0.70f, 0.62f, 0.50f), 7);
            stone.AddBox(new Vector3(0f, -1.5f, 0f), new Vector3(18f, 1.8f, 18f));
            stone.SetColor(new Color(0.45f, 0.32f, 0.22f), 9);
            foreach (Vector3 bench in new[] { new Vector3(-5f, 0.3f, 7f), new Vector3(5f, 0.3f, 7f), new Vector3(-7f, 0.3f, -2f) })
                stone.AddBox(bench, new Vector3(2.4f, 0.45f, 0.6f));
            // Garde-corps tourné vers la ville (sud-est).
            stone.SetColor(new Color(0.35f, 0.30f, 0.26f), 3);
            stone.AddOrientedBox(new Vector3(6f, 0.3f, -6f), new Vector3(14f, 1f, 0.2f), 45f);
            AddMeshObject(root, "Terrasse", stone, CityMaterials.Painted(), meshes);
            AddBoxCollider(root.gameObject, new Vector3(0f, -0.6f, 0f), new Vector3(18f, 1.8f, 18f));

            var store = CreatePoint<Gameplay.Interaction.StorePoint>(interactables, nextId++, "Kiosque du Banco", root.localPosition + new Vector3(-4f, 0.3f, -5f));
            store.EditorSetupStore("Gardien Konan", "Il fait chaud là-haut, hein ? Bois quelque chose !", new[]
            {
                Product("Eau minérale 50 cl", 300, 0, 40),
                Product("Jus de gingembre", 300, 5, 40),
                Product("Arachides grillées", 200, 15, 0),
            });
            AddKiosk(store.transform, meshes);
            AddVendor(store.transform, new Vector3(0f, 0f, 1.6f), 180f, 3, false);
            s_reserved.Add((c, 16f));
        }

        #endregion

        #region Horizon

        /// <summary>Collines lointaines (sans collider) jusqu'à 3 km, lagune comprise.</summary>
        private static void CreateHorizon(Transform city, List<Mesh> meshes)
        {
            const float far = 2976f;     // multiple de 62 m, comme OuterExtent
            const float step = 62f;
            int count = Mathf.RoundToInt(far * 2f / step);
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var uvs = new List<Vector2>();
            var plain = new List<Vector2>();
            var triangles = new List<int>();

            for (int i = 0; i <= count; i++)
            {
                for (int j = 0; j <= count; j++)
                {
                    float x = -far + i * step, z = -far + j * step;
                    bool inside = Mathf.Abs(x) < OuterExtent - 0.5f && Mathf.Abs(z) < OuterExtent - 0.5f;
                    float h;
                    if (inside)
                        h = -8f;
                    else
                    {
                        float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                        h = NaturalHeight(x, z) - 0.3f;
                        if (Mathf.Abs(z - LagoonZ) >= 34f)
                            h += 40f * Smooth(OuterExtent + 100f, 2200f, d) * Mathf.PerlinNoise(x * 0.0015f + 2f, z * 0.0015f + 5f);
                    }
                    vertices.Add(new Vector3(x, h, z));
                    Color32 c = Color.Lerp(Grass, ForestFloor, 0.4f + 0.4f * Mathf.PerlinNoise(x * 0.004f, z * 0.004f));
                    c.a = (byte)(i * 13 + j * 7);
                    colors.Add(c);
                    uvs.Add(new Vector2(x, z));
                    plain.Add(new Vector2(-1f, -1f));
                }
            }

            int stride = count + 1;
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    float x0 = -far + i * step, z0 = -far + j * step;
                    if (x0 >= -OuterExtent - 0.5f && x0 + step <= OuterExtent + 0.5f && z0 >= -OuterExtent - 0.5f && z0 + step <= OuterExtent + 0.5f)
                        continue;
                    int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(d);
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                }
            }

            var mesh = new Mesh { name = "Horizon", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, plain);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            meshes.Add(mesh);

            var go = new GameObject("Horizon", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(city, false);
            go.isStatic = true;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CityMaterials.Ground();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Lagune lointaine, à l'est et à l'ouest.
            var lagoon = new BoxMeshBuilder();
            foreach (float sign in new[] { -1f, 1f })
                lagoon.AddFlatQuad(new Vector3(sign * (OuterExtent + far) / 2f, 0.02f, LagoonZ), Vector3.right, far - OuterExtent, BlockSize);
            AddMeshObject(city, "LaguneLointaine", lagoon, CityMaterials.Water(), meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
        }

        #endregion
    }

    internal static class RendererExtensions
    {
        public static void SetShadowsOff(this MeshRenderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
