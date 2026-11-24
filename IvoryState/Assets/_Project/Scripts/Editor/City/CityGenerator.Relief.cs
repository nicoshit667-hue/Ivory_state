using System.Collections.Generic;
using IvoryState.Gameplay.World;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Relief de la ville (altitudes SRTM de CityLayout.json, pente limitée à 12 %) : la ville est
    /// construite à plat, puis <see cref="ApplyRelief"/> la pose sur le sol en une passe :
    /// <list type="bullet">
    /// <item>maillages fusionnés en coordonnées monde (routes, trottoirs, mobilier, places...) : chaque sommet
    /// suit le sol (les lieux particuliers et les monuments sont sur des plateformes aplanies) ;</item>
    /// <item>objets placés (points de jeu, prefabs, feux, arbres de la place...) : relevés en bloc ;</item>
    /// <item>colliders posés directement sur les conteneurs : relevés à leur centre.</item>
    /// </list>
    /// Le sol lui-même, les bâtiments (base au niveau du trottoir, socle sur le côté en pente), les ponts
    /// (profil en dos d'âne au-dessus de la lagune) et la forêt sont construits directement en relief.
    /// </summary>
    internal static partial class CityGenerator
    {
        private struct BridgeSpan
        {
            public Vector3 A, B, Dir;
            public float Width, Length, HeightA, HeightB, Rise;
        }

        private static readonly List<BridgeSpan> s_bridgeSpans = new List<BridgeSpan>();
        private const float BridgeGrade = 0.08f;     // pente des rampes des ponts
        private const float BridgeMaxRise = 3.4f;    // tablier au plus haut au-dessus des berges
        private const float PavedSink = 0.14f;       // sol abaissé sous les rues : pas de relief qui perce le bitume

        /// <summary>Racines construites directement en relief : la passe de relief ne les touche pas.</summary>
        private static readonly HashSet<string> s_reliefBuilt = new HashSet<string>
        {
            "Relief", "Lagune", "Quais", "Batiments", "Ponts", "Foret", "Bounds", "PoteauxSolides", "EclairagePublic",
        };

        private static float Ground(float x, float z) => CityLayout.GroundHeight(x, z);

        private static float Smooth01(float v)
        {
            v = Mathf.Clamp01(v);
            return v * v * (3f - 2f * v);
        }

        private static void PrepareBridgeSpans(List<(Vector3 a, Vector3 b, float width)> bridges)
        {
            s_bridgeSpans.Clear();
            foreach ((Vector3 a, Vector3 b, float width) in bridges)
            {
                Vector3 ab = b - a;
                ab.y = 0f;
                float length = ab.magnitude;
                if (length < 4f)
                    continue;
                // Rampes en S : pente maxi 1,5 x hauteur / longueur de rampe, gardée sous BridgeGrade.
                float rise = Mathf.Min(BridgeMaxRise, BridgeGrade * length / 3f);
                s_bridgeSpans.Add(new BridgeSpan
                {
                    A = a, B = b, Dir = ab / length, Width = width, Length = length, Rise = rise,
                    HeightA = Ground(a.x, a.z), HeightB = Ground(b.x, b.z),
                });
            }
        }

        /// <summary>Hauteur du tablier à la fraction <paramref name="t"/> (0 = berge A, 1 = berge B).</summary>
        private static float BridgeDeck(in BridgeSpan span, float t)
        {
            float ramp = Mathf.Max(0.01f, 1.5f * span.Rise / BridgeGrade / span.Length);
            float up = Smooth01(t / ramp) * Smooth01((1f - t) / ramp);
            return Mathf.Lerp(span.HeightA, span.HeightB, Mathf.Clamp01(t)) + span.Rise * up;
        }

        /// <summary>Surface roulante : le tablier sur les ponts, le sol ailleurs.</summary>
        private static float RoadSurface(float x, float z)
        {
            float best = float.NegativeInfinity;
            foreach (BridgeSpan span in s_bridgeSpans)
            {
                float dx = x - span.A.x, dz = z - span.A.z;
                float along = dx * span.Dir.x + dz * span.Dir.z;
                if (along < 0f || along > span.Length)
                    continue;
                float across = Mathf.Abs(dx * span.Dir.z - dz * span.Dir.x);
                if (across > span.Width * 0.5f + 2.5f)
                    continue;
                best = Mathf.Max(best, BridgeDeck(span, along / span.Length));
            }
            return best > float.NegativeInfinity ? best : Ground(x, z);
        }

        /// <summary>Polyligne redécoupée tous les <paramref name="step"/> m : ses sommets peuvent suivre le relief.</summary>
        private static List<Vector3> Densify(List<Vector3> points, float step)
        {
            var dense = new List<Vector3>(points.Count * 2);
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / step));
                for (int k = 0; k < n; k++)
                    dense.Add(Vector3.Lerp(a, b, k / (float)n));
            }
            if (points.Count > 0)
                dense.Add(points[points.Count - 1]);
            return dense;
        }

        #region Passe de relief

        /// <summary>Pose sur le relief tout ce qui a été construit à plat sous <paramref name="city"/>.</summary>
        private static void ApplyRelief(Transform city, List<Mesh> generated)
        {
            s_displaced.Clear();
            var generatedSet = new HashSet<Mesh>(generated);
            int meshes = 0, objects = 0;
            foreach (Transform child in city)
                Visit(child, false);
            Debug.Log("[City] Relief appliqué : " + meshes + " maillages déformés, " + objects + " objets relevés.");

            void Visit(Transform t, bool roads)
            {
                if (t.parent == city && s_reliefBuilt.Contains(t.name))
                    return;
                roads |= t.name == "Routes";
                if (!IsWorldIdentity(t))
                {
                    Vector3 p = t.position;
                    t.position = new Vector3(p.x, p.y + Ground(p.x, p.z), p.z);
                    objects++;
                    return;
                }

                var filter = t.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    if (generatedSet.Contains(filter.sharedMesh))
                    {
                        Displace(filter.sharedMesh, roads);
                        meshes++;
                        var meshCollider = t.GetComponent<MeshCollider>();
                        if (meshCollider != null && meshCollider.sharedMesh == filter.sharedMesh)
                        {
                            meshCollider.sharedMesh = null;
                            meshCollider.sharedMesh = filter.sharedMesh;
                        }
                    }
                    else
                    {
                        // Maillage partagé (prefab, primitive) posé à l'origine : on relève l'objet.
                        Vector3 p = t.position;
                        t.position = new Vector3(p.x, p.y + Ground(p.x, p.z), p.z);
                        objects++;
                        return;
                    }
                }
                foreach (Collider collider in t.GetComponents<Collider>())
                {
                    switch (collider)
                    {
                        case BoxCollider box:
                            box.center += Vector3.up * Ground(box.center.x, box.center.z);
                            break;
                        case SphereCollider sphere:
                            sphere.center += Vector3.up * Ground(sphere.center.x, sphere.center.z);
                            break;
                        case CapsuleCollider capsule:
                            capsule.center += Vector3.up * Ground(capsule.center.x, capsule.center.z);
                            break;
                        case MeshCollider mc when mc.sharedMesh != null && filter == null && generatedSet.Contains(mc.sharedMesh):
                            Displace(mc.sharedMesh, roads);
                            Mesh m = mc.sharedMesh;
                            mc.sharedMesh = null;
                            mc.sharedMesh = m;
                            meshes++;
                            break;
                    }
                }
                var children = new List<Transform>();
                foreach (Transform c in t)
                    children.Add(c);
                foreach (Transform c in children)
                    Visit(c, roads);
            }
        }

        private static bool IsWorldIdentity(Transform t)
        {
            Matrix4x4 m = t.localToWorldMatrix;
            for (int i = 0; i < 16; i++)
            {
                if (Mathf.Abs(m[i] - Matrix4x4.identity[i]) > 1e-4f)
                    return false;
            }
            return true;
        }

        private static readonly HashSet<Mesh> s_displaced = new HashSet<Mesh>();

        private static void Displace(Mesh mesh, bool roads)
        {
            if (!s_displaced.Add(mesh))
                return;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                vertices[i].y = v.y + (roads ? RoadSurface(v.x, v.z) : Ground(v.x, v.z));
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        #endregion

        #region Sol sous les rues

        /// <summary>
        /// Cases du sol couvertes par une rue ou un trottoir (grille des sommets du relief) : le sol y est
        /// abaissé de <see cref="PavedSink"/> pour que ses triangles ne percent jamais le bitume en pente.
        /// </summary>
        private static bool[] PavedVertices(float x0, float z0, int nx, int nz, float cell)
        {
            var paved = new bool[(nx + 1) * (nz + 1)];
            CityLayout.Data layout = CityLayout.Layout;
            float sidewalk = layout.sidewalkWidth > 0f ? layout.sidewalkWidth : 3f;
            void Stamp(Vector3 a, Vector3 b, float radius)
            {
                int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius - x0) / cell));
                int i1 = Mathf.Min(nx, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + radius - x0) / cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - radius - z0) / cell));
                int j1 = Mathf.Min(nz, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + radius - z0) / cell));
                Vector3 ab = b - a;
                float l2 = Mathf.Max(ab.x * ab.x + ab.z * ab.z, 1e-4f);
                for (int i = i0; i <= i1; i++)
                {
                    for (int j = j0; j <= j1; j++)
                    {
                        float x = x0 + i * cell, z = z0 + j * cell;
                        float t = Mathf.Clamp01(((x - a.x) * ab.x + (z - a.z) * ab.z) / l2);
                        float ex = a.x + ab.x * t - x, ez = a.z + ab.z * t - z;
                        if (ex * ex + ez * ez <= radius * radius)
                            paved[i * (nz + 1) + j] = true;
                    }
                }
            }
            foreach (CityLayout.RoadData road in layout.roads)
            {
                List<Vector3> pts = Points(road.p);
                for (int k = 0; k + 1 < pts.Count; k++)
                    Stamp(pts[k], pts[k + 1], road.width * 0.5f + sidewalk + cell * 0.75f);
            }
            if (layout.airport?.runways != null)
            {
                foreach (CityLayout.PolyData strip in Concat(layout.airport.runways, layout.airport.taxiways))
                {
                    List<Vector3> pts = Points(strip.p);
                    for (int k = 0; k + 1 < pts.Count; k++)
                        Stamp(pts[k], pts[k + 1], strip.width * 0.5f + cell * 0.75f);
                }
            }
            return paved;
        }

        private static IEnumerable<CityLayout.PolyData> Concat(CityLayout.PolyData[] a, CityLayout.PolyData[] b)
        {
            if (a != null)
            {
                foreach (CityLayout.PolyData p in a)
                    yield return p;
            }
            if (b != null)
            {
                foreach (CityLayout.PolyData p in b)
                    yield return p;
            }
        }

        #endregion

        #region Forêt du Banco et forêts classées

        /// <summary>
        /// Arbres des forêts (positions de CityLayout.trees) : tronc et houppier bas-poly (~70 triangles),
        /// fusionnés par tronçon de 128 m, teintes variées. Collisions : un cylindre par tronc.
        /// </summary>
        private static void CreateLayoutForest(Transform city, List<Mesh> meshes)
        {
            float[] trees = CityLayout.Layout.trees;
            if (trees == null || trees.Length < 2)
                return;
            var root = new GameObject("Foret").transform;
            root.SetParent(city, false);
            root.gameObject.isStatic = true;
            var trunks = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var crowns = new Dictionary<Vector2Int, BoxMeshBuilder>();
            var colliders = new Dictionary<Vector2Int, GameObject>();
            var random = new System.Random(5310);
            Color[] greens =
            {
                new Color(0.20f, 0.36f, 0.16f), new Color(0.26f, 0.42f, 0.18f), new Color(0.17f, 0.30f, 0.14f),
                new Color(0.30f, 0.45f, 0.20f), new Color(0.23f, 0.33f, 0.12f),
            };
            for (int i = 0; i + 1 < trees.Length; i += 2)
            {
                float x = trees[i], z = trees[i + 1];
                float y = Ground(x, z) - 0.2f;
                Vector2Int chunk = ChunkOf(x, z);
                float scale = 0.75f + (float)random.NextDouble() * 0.7f;
                float trunkHeight = 5.5f * scale;
                float crown = 3.2f * scale;
                BoxMeshBuilder trunk = ChunkBuilder(trunks, chunk);
                trunk.SetColor(Color.white, random.Next(256));
                trunk.AddCylinder(new Vector3(x, y, z), 0.28f * scale, trunkHeight + 0.6f, 5);
                BoxMeshBuilder leaves = ChunkBuilder(crowns, chunk);
                leaves.SetColor(greens[random.Next(greens.Length)], random.Next(256));
                float cx = x + ((float)random.NextDouble() - 0.5f) * 0.8f, cz = z + ((float)random.NextDouble() - 0.5f) * 0.8f;
                leaves.AddEllipsoid(new Vector3(cx, y + trunkHeight + crown * 0.35f, cz), new Vector3(crown, crown * 0.75f, crown), 6, 3);
                if (random.NextDouble() < 0.6)
                {
                    float off = crown * 0.55f;
                    float a = (float)random.NextDouble() * Mathf.PI * 2f;
                    leaves.AddEllipsoid(new Vector3(cx + Mathf.Cos(a) * off, y + trunkHeight + crown * 0.05f, cz + Mathf.Sin(a) * off),
                        new Vector3(crown * 0.7f, crown * 0.55f, crown * 0.7f), 6, 3);
                }

                if (!colliders.TryGetValue(chunk, out GameObject holder))
                {
                    holder = new GameObject("Troncs_" + chunk.x + "_" + chunk.y);
                    holder.transform.SetParent(root, false);
                    holder.isStatic = true;
                    colliders[chunk] = holder;
                }
                var capsule = holder.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(x, y + trunkHeight * 0.5f, z);
                capsule.radius = 0.35f * scale;
                capsule.height = trunkHeight;
            }
            Material bark = CityMaterials.Bark();
            Material foliage = CityMaterials.Foliage();
            foreach (var pair in trunks)
                AddMeshObject(root, "Troncs_" + pair.Key.x + "_" + pair.Key.y + "_M", pair.Value, bark, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            foreach (var pair in crowns)
                AddMeshObject(root, "Feuillage_" + pair.Key.x + "_" + pair.Key.y, pair.Value, foliage, meshes);
            Debug.Log("[City] Forêts : " + trees.Length / 2 + " arbres.");
        }

        #endregion

        #region Parkings des bâtiments utilisables

        /// <summary>
        /// Parkings extérieurs (CHU, mairie, commissariat, palais, lieux de culte, stade) : bitume, places
        /// en épi de 2,6 m sur deux rangées, bordures et panneau « P ». Construits à plat sur leur plateforme.
        /// </summary>
        private static void CreateParkings(Transform city, Material asphalt, List<Mesh> meshes)
        {
            CityLayout.LandmarkData[] parkings = CityLayout.Layout.parkings;
            if (parkings == null || parkings.Length == 0)
                return;
            var root = new GameObject("Parkings").transform;
            root.SetParent(city, false);
            var paved = new BoxMeshBuilder();
            var lines = new BoxMeshBuilder();
            var curbs = new BoxMeshBuilder();
            var sign = new BoxMeshBuilder();
            var blue = new BoxMeshBuilder();
            foreach (CityLayout.LandmarkData p in parkings)
            {
                float yaw = CityLayout.UnityYaw(p.yaw);
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 along = q * Vector3.right, across = q * Vector3.forward;
                var c = new Vector3(p.x, 0f, p.z);
                paved.AddFlatQuad(c + Vector3.up * 0.04f, along, p.w, p.d);
                // Deux rangées de places perpendiculaires à l'allée centrale (5 m de profondeur, 2,6 m de large).
                foreach (float side in new[] { -1f, 1f })
                {
                    float rowCenter = side * (p.d * 0.5f - 2.6f);
                    for (float s = -p.w * 0.5f + 1f; s <= p.w * 0.5f - 1f + 0.01f; s += 2.6f)
                        lines.AddFlatQuad(c + along * s + across * rowCenter + Vector3.up * 0.06f, across, 5f, 0.12f);
                    lines.AddFlatQuad(c + across * (side * (p.d * 0.5f - 5.1f)) + Vector3.up * 0.06f, along, p.w - 2f, 0.12f);
                }
                // Bordures sur trois côtés (l'entrée reste ouverte du côté du bâtiment).
                curbs.AddRotatedBox(c + across * (p.d * 0.5f + 0.15f) + Vector3.up * 0.08f, new Vector3(p.w, 0.16f, 0.3f), q);
                curbs.AddRotatedBox(c - across * (p.d * 0.5f + 0.15f) + Vector3.up * 0.08f, new Vector3(p.w, 0.16f, 0.3f), q);
                curbs.AddRotatedBox(c + along * (p.w * 0.5f + 0.15f) + Vector3.up * 0.08f, new Vector3(0.3f, 0.16f, p.d), q);
                // Panneau « P ».
                Vector3 post = c - along * (p.w * 0.5f - 0.6f) + across * (p.d * 0.5f - 0.6f);
                sign.AddBox(post, new Vector3(0.08f, 2.6f, 0.08f));
                blue.AddRotatedBox(post + Vector3.up * 2.75f, new Vector3(0.7f, 0.7f, 0.06f), q);
                sign.AddRotatedBox(post + Vector3.up * 2.75f + across * 0.035f, new Vector3(0.16f, 0.44f, 0.02f), q);
                sign.AddRotatedBox(post + Vector3.up * 2.9f + across * 0.035f + along * 0.1f, new Vector3(0.22f, 0.16f, 0.02f), q);
            }
            GameObject ground = AddMeshObject(root, "Bitume", paved, asphalt, meshes);
            if (ground != null)
            {
                ground.GetComponent<MeshRenderer>().SetShadowsOff();
                ground.AddComponent<MeshCollider>().sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
            }
            AddMeshObject(root, "Marquages", lines, s_markingMaterial, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            AddMeshObject(root, "Bordures", curbs, CityMaterials.Concrete(), meshes);
            AddMeshObject(root, "Panneaux", sign, CityMaterials.Metal("M_SignWhite", new Color(0.92f, 0.92f, 0.92f), 0.2f, 0.4f), meshes);
            AddMeshObject(root, "PanneauxBleus", blue, CityMaterials.Plain("M_SignBlue", new Color(0.08f, 0.28f, 0.72f), 0.3f), meshes);
            Debug.Log("[City] " + parkings.Length + " parkings de bâtiments.");
        }

        #endregion

        #region Aéroport Félix Houphouët-Boigny (Port-Bouët)

        /// <summary>
        /// Aéroport d'après OpenStreetMap : piste 03/21 avec marquages, voies de circulation, aires de
        /// stationnement, aérogares (agrandies x2 : la ville est au 1/8, pas les bâtiments), tour de
        /// contrôle, clôture. Construit à plat : la plateforme de l'aéroport est aplanie dans le relief.
        /// (Aérogares agrandies x1,5 en réalité, pour ne pas déborder sur les rues voisines.)
        /// </summary>
        private static void CreateAirport(Transform city, Material asphalt, List<Mesh> meshes)
        {
            CityLayout.AirportData airport = CityLayout.Layout.airport;
            if (airport?.runways == null || airport.runways.Length == 0)
                return;
            var root = new GameObject("Aeroport").transform;
            root.SetParent(city, false);
            var paved = new BoxMeshBuilder();
            var marks = new BoxMeshBuilder();
            var concrete = new BoxMeshBuilder();
            var glass = new BoxMeshBuilder();
            var fence = new BoxMeshBuilder();

            // Aires de stationnement (béton) : polygones triangulés en éventail depuis leur centre.
            if (airport.aprons != null)
            {
                foreach (CityLayout.PolyData apron in airport.aprons)
                {
                    List<Vector3> ring = Points(apron.p);
                    if (ring.Count < 3)
                        continue;
                    Vector3 c = Vector3.zero;
                    foreach (Vector3 p in ring)
                        c += p;
                    c /= ring.Count;
                    for (int i = 0; i < ring.Count; i++)
                    {
                        Vector3 a = ring[i] + Vector3.up * 0.04f, b = ring[(i + 1) % ring.Count] + Vector3.up * 0.04f;
                        Vector3 o = c + Vector3.up * 0.04f;
                        // Face vers le haut quel que soit le sens de l'anneau OSM.
                        if (Vector3.Cross(b - o, a - o).y >= 0f)
                            concrete.AddTriangle(o, b, a);
                        else
                            concrete.AddTriangle(o, a, b);
                    }
                }
            }

            foreach (CityLayout.PolyData strip in Concat(airport.runways, airport.taxiways))
            {
                List<Vector3> points = Points(strip.p);
                if (points.Count < 2)
                    continue;
                bool runway = System.Array.IndexOf(airport.runways, strip) >= 0;
                float lift = runway ? 0.06f : 0.05f;
                BoxMeshBuilder.ComputeRibbonSides(points, strip.width, out Vector3[] sides, out float[] travel);
                for (int i = 0; i + 1 < points.Count; i++)
                    paved.AddRibbonSegment(points[i], sides[i], travel[i], points[i + 1], sides[i + 1], travel[i + 1], strip.width, lift);
                if (!runway)
                {
                    paved.AddFlatDisc(points[0], strip.width * 0.5f, lift);
                    paved.AddFlatDisc(points[points.Count - 1], strip.width * 0.5f, lift);
                    continue;
                }

                // Piste : axe discontinu, bandes latérales, seuils (« piano ») et numéros simplifiés.
                Vector3 start = points[0], end = points[points.Count - 1];
                Vector3 dir = (end - start).normalized;
                float length = Vector3.Distance(start, end);
                Vector3 across = Vector3.Cross(Vector3.up, dir);
                float y = lift + 0.015f;
                for (float s = 30f; s < length - 30f; s += 18f)
                    marks.AddFlatQuad(start + dir * s + Vector3.up * y, dir, 9f, 0.45f);
                foreach (float side in new[] { -1f, 1f })
                    marks.AddFlatQuad((start + end) * 0.5f + across * side * (strip.width * 0.5f - 0.6f) + Vector3.up * y, dir, length - 4f, 0.35f);
                foreach ((Vector3 p, Vector3 d) in new[] { (start, dir), (end, -dir) })
                {
                    for (float t = -strip.width * 0.5f + 1.5f; t <= strip.width * 0.5f - 1.5f; t += 1.6f)
                        marks.AddFlatQuad(p + d * 8f + across * t + Vector3.up * y, d, 10f, 0.7f);
                    marks.AddFlatQuad(p + d * 26f + Vector3.up * y, d, 8f, strip.width * 0.5f);
                }
            }

            // Aérogares : emprise OSM agrandie x2 autour de son centre, façades vitrées, toit plat.
            Vector3 towerSpot = Vector3.zero;
            bool hasTower = false;
            if (airport.terminals != null)
            {
                foreach (CityLayout.PolyData terminal in airport.terminals)
                {
                    List<Vector3> ring = Points(terminal.p);
                    if (ring.Count < 3)
                        continue;
                    Vector3 min = ring[0], max = ring[0];
                    foreach (Vector3 p in ring)
                    {
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    Vector3 c = (min + max) * 0.5f;
                    var size = new Vector3(Mathf.Max(18f, (max.x - min.x) * 1.5f), 9f, Mathf.Max(14f, (max.z - min.z) * 1.5f));
                    concrete.AddBox(c + Vector3.down * 0.2f, new Vector3(size.x + 4f, 0.4f, size.z + 4f));
                    glass.AddBoxSides(c, new Vector3(size.x, 7f, size.z));
                    concrete.AddBox(c + Vector3.up * 7f, new Vector3(size.x + 3f, 1.2f, size.z + 3f));
                    AddBoxCollider(root.gameObject, c + Vector3.up * 4f, new Vector3(size.x, 8f, size.z));
                    if (!hasTower || terminal.name.Contains("International"))
                    {
                        towerSpot = c + new Vector3(size.x * 0.5f + 14f, 0f, 0f);
                        hasTower = true;
                    }
                }
            }
            if (hasTower)
            {
                concrete.AddCylinder(towerSpot, 2.2f, 26f, 10);
                glass.AddCylinder(towerSpot + Vector3.up * 26f, 4.2f, 4f, 12);
                concrete.AddCylinder(towerSpot + Vector3.up * 30f, 4.6f, 0.6f, 12);
                AddBoxCollider(root.gameObject, towerSpot + Vector3.up * 13f, new Vector3(4.4f, 26f, 4.4f));
            }

            // Clôture grillagée sur le pourtour (poteaux et lisses), coupée là où passe une route.
            if (airport.fence != null)
            {
                foreach (CityLayout.PolyData ring in airport.fence)
                {
                    List<Vector3> pts = Points(ring.p);
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
                        float len = Vector3.Distance(a, b);
                        for (float s = 0f; s < len; s += 4f)
                        {
                            Vector3 p = Vector3.Lerp(a, b, s / Mathf.Max(len, 0.01f));
                            if (CityLayout.IsWater(p.x, p.z) || NearRoad(p, 3f))
                                continue;
                            fence.AddBox(p, new Vector3(0.12f, 2.4f, 0.12f));
                            Vector3 q = Vector3.Lerp(a, b, Mathf.Min(1f, (s + 4f) / Mathf.Max(len, 0.01f)));
                            if (!NearRoad(q, 3f))
                            {
                                fence.AddBeam(p + Vector3.up * 2.3f, q + Vector3.up * 2.3f, 0.05f);
                                fence.AddBeam(p + Vector3.up * 1.2f, q + Vector3.up * 1.2f, 0.05f);
                                fence.AddBeam(p + Vector3.up * 0.2f, q + Vector3.up * 0.2f, 0.05f);
                            }
                        }
                    }
                }
            }

            AddMeshObject(root, "Pistes", paved, asphalt, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            AddMeshObject(root, "Marquages", marks, s_markingMaterial, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            AddMeshObject(root, "Beton", concrete, CityMaterials.Concrete(), meshes);
            AddMeshObject(root, "Vitrages", glass, CityMaterials.BuildingGlass(), meshes);
            AddMeshObject(root, "Cloture", fence, CityMaterials.Metal("M_Fence", new Color(0.55f, 0.57f, 0.58f), 0.6f, 0.35f), meshes);
            // Le béton des pistes et des aires sert aussi de sol (collider), à plat sur la plateforme.
            Transform pistes = root.Find("Pistes");
            if (pistes != null)
                pistes.gameObject.AddComponent<MeshCollider>().sharedMesh = pistes.GetComponent<MeshFilter>().sharedMesh;
            Debug.Log("[City] Aéroport : " + airport.runways.Length + " piste(s), " + (airport.taxiways?.Length ?? 0) + " voies de circulation.");
        }

        private static bool NearRoad(Vector3 p, float margin)
        {
            foreach (CityLayout.RoadData road in CityLayout.Layout.roads)
            {
                for (int i = 0; i + 3 < road.p.Length; i += 2)
                {
                    float ax = road.p[i], az = road.p[i + 1], bx = road.p[i + 2], bz = road.p[i + 3];
                    float dx = bx - ax, dz = bz - az;
                    float l2 = Mathf.Max(dx * dx + dz * dz, 1e-4f);
                    float t = Mathf.Clamp01(((p.x - ax) * dx + (p.z - az) * dz) / l2);
                    float ex = ax + dx * t - p.x, ez = az + dz * t - p.z;
                    float r = road.width * 0.5f + margin;
                    if (ex * ex + ez * ez < r * r)
                        return true;
                }
            }
            return false;
        }

        #endregion
    }
}
