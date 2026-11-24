using System.Collections.Generic;
using UnityEngine;

namespace IvoryState.Gameplay.World
{
    /// <summary>
    /// Graphe des rues de la ville (d'après <see cref="CityLayout"/>) pour le GPS : l'itinéraire suit le tracé
    /// des rues, au plus court (A*). Les rues sont reliées là où elles se croisent ou se touchent (bout d'une
    /// rue posé sur une autre), sauf sur un pont (croisement à niveaux différents).
    /// <para>Construit une fois, à la première demande (quelques millisecondes).</para>
    /// </summary>
    public static class RoadNetwork
    {
        private struct Edge
        {
            public int To;
            public float Length;
        }

        private static List<Vector2> s_nodes;
        private static List<List<Edge>> s_edges;
        // Segments du graphe (a, b) rangés par cases, pour trouver le point de rue le plus proche.
        private static Dictionary<Vector2Int, List<(int a, int b)>> s_cells;
        private const float Cell = 40f;

        private static void EnsureBuilt()
        {
            if (s_nodes != null)
                return;
            s_nodes = new List<Vector2>();
            s_edges = new List<List<Edge>>();
            s_cells = new Dictionary<Vector2Int, List<(int, int)>>();

            CityLayout.RoadData[] roads = CityLayout.Layout.roads;
            int n = roads.Length;
            var polys = new List<Vector2>[n];
            var boxes = new Rect[n];
            for (int r = 0; r < n; r++)
            {
                float[] p = roads[r].p;
                var poly = new List<Vector2>(p.Length / 2);
                for (int i = 0; i + 1 < p.Length; i += 2)
                    poly.Add(new Vector2(p[i], p[i + 1]));
                polys[r] = poly;
                Vector2 min = poly.Count > 0 ? poly[0] : Vector2.zero, max = min;
                foreach (Vector2 v in poly)
                {
                    min = Vector2.Min(min, v);
                    max = Vector2.Max(max, v);
                }
                float pad = roads[r].width * 0.5f + 2f;
                boxes[r] = Rect.MinMaxRect(min.x - pad, min.y - pad, max.x + pad, max.y + pad);
            }

            // Coupes de chaque rue : (segment, t) -> nœud. Les sommets d'abord.
            var cuts = new List<(int seg, float t, int node)>[n];
            for (int r = 0; r < n; r++)
            {
                cuts[r] = new List<(int, float, int)>();
                for (int i = 0; i < polys[r].Count; i++)
                {
                    int seg = Mathf.Min(i, Mathf.Max(0, polys[r].Count - 2));
                    cuts[r].Add((seg, i == polys[r].Count - 1 && i > 0 ? 1f : 0f, AddNode(polys[r][i])));
                }
            }

            for (int r = 0; r < n; r++)
            {
                List<Vector2> a = polys[r];
                for (int q = r + 1; q < n; q++)
                {
                    if (!boxes[r].Overlaps(boxes[q]))
                        continue;
                    List<Vector2> b = polys[q];
                    // Croisements.
                    for (int i = 0; i + 1 < a.Count; i++)
                    {
                        for (int j = 0; j + 1 < b.Count; j++)
                        {
                            if (!Intersect(a[i], a[i + 1], b[j], b[j + 1], out float t, out float u))
                                continue;
                            Vector2 hit = Vector2.Lerp(a[i], a[i + 1], t);
                            if (OnBridge(roads[r], hit) || OnBridge(roads[q], hit))
                                continue;
                            int node = AddNode(hit);
                            cuts[r].Add((i, t, node));
                            cuts[q].Add((j, u, node));
                        }
                    }
                }
            }
            // Bouts de rue posés sur une autre rue (carrefours en T, raccords).
            for (int r = 0; r < n; r++)
            {
                for (int end = 0; end < 2; end++)
                {
                    List<Vector2> a = polys[r];
                    if (a.Count < 2)
                        continue;
                    Vector2 p = end == 0 ? a[0] : a[a.Count - 1];
                    int endNode = end == 0 ? cuts[r][0].node : cuts[r][a.Count - 1].node;
                    for (int q = 0; q < n; q++)
                    {
                        if (q == r || !boxes[q].Contains(p))
                            continue;
                        List<Vector2> b = polys[q];
                        float best = float.MaxValue, bestT = 0f;
                        int bestSeg = -1;
                        for (int j = 0; j + 1 < b.Count; j++)
                        {
                            float t = Project(p, b[j], b[j + 1]);
                            float d = Vector2.Distance(p, Vector2.Lerp(b[j], b[j + 1], t));
                            if (d < best)
                            {
                                best = d;
                                bestT = t;
                                bestSeg = j;
                            }
                        }
                        if (bestSeg < 0 || best > roads[q].width * 0.5f + 1.5f)
                            continue;
                        Vector2 on = Vector2.Lerp(b[bestSeg], b[bestSeg + 1], bestT);
                        if (OnBridge(roads[q], on))
                            continue;
                        int node = AddNode(on);
                        cuts[q].Add((bestSeg, bestT, node));
                        if (node != endNode)
                            Link(endNode, node);
                    }
                }
            }

            // Arêtes le long de chaque rue, entre coupes consécutives.
            for (int r = 0; r < n; r++)
            {
                List<(int seg, float t, int node)> list = cuts[r];
                list.Sort((x, y) => x.seg != y.seg ? x.seg.CompareTo(y.seg) : x.t.CompareTo(y.t));
                for (int k = 0; k + 1 < list.Count; k++)
                {
                    if (list[k].node != list[k + 1].node)
                        Link(list[k].node, list[k + 1].node);
                }
            }
        }

        private static readonly Dictionary<Vector2Int, int> s_nodeKeys = new Dictionary<Vector2Int, int>();

        private static int AddNode(Vector2 p)
        {
            var key = new Vector2Int(Mathf.RoundToInt(p.x * 2f), Mathf.RoundToInt(p.y * 2f));
            if (s_nodeKeys.TryGetValue(key, out int existing))
                return existing;
            s_nodes.Add(p);
            s_edges.Add(new List<Edge>(4));
            s_nodeKeys[key] = s_nodes.Count - 1;
            return s_nodes.Count - 1;
        }

        private static void Link(int a, int b)
        {
            float length = Vector2.Distance(s_nodes[a], s_nodes[b]);
            s_edges[a].Add(new Edge { To = b, Length = length });
            s_edges[b].Add(new Edge { To = a, Length = length });
            Vector2 pa = s_nodes[a], pb = s_nodes[b];
            int x0 = Mathf.FloorToInt(Mathf.Min(pa.x, pb.x) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(pa.x, pb.x) / Cell);
            int z0 = Mathf.FloorToInt(Mathf.Min(pa.y, pb.y) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(pa.y, pb.y) / Cell);
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!s_cells.TryGetValue(key, out var list))
                        s_cells[key] = list = new List<(int, int)>();
                    list.Add((a, b));
                }
            }
        }

        private static float Project(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            return Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        }

        private static bool Intersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out float t, out float u)
        {
            Vector2 da = a1 - a0, db = b1 - b0;
            float den = da.x * db.y - da.y * db.x;
            t = u = 0f;
            if (Mathf.Abs(den) < 1e-6f)
                return false;
            Vector2 w = b0 - a0;
            t = (w.x * db.y - w.y * db.x) / den;
            u = (w.x * da.y - w.y * da.x) / den;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private static bool OnBridge(CityLayout.RoadData road, Vector2 p)
        {
            for (int i = 0; road.b != null && i + 3 < road.b.Length; i += 4)
            {
                var a = new Vector2(road.b[i], road.b[i + 1]);
                var b = new Vector2(road.b[i + 2], road.b[i + 3]);
                if (Vector2.Distance(p, Vector2.Lerp(a, b, Project(p, a, b))) < road.width * 0.5f + 2f)
                    return true;
            }
            return false;
        }

        /// <summary>Point de rue le plus proche (segment a-b du graphe, position sur ce segment).</summary>
        private static bool Nearest(Vector2 p, out int a, out int b, out Vector2 on)
        {
            a = b = -1;
            on = p;
            float best = float.MaxValue;
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.y / Cell);
            for (int ring = 0; ring <= 6 && a < 0; ring++)
            {
                for (int x = cx - ring; x <= cx + ring; x++)
                {
                    for (int z = cz - ring; z <= cz + ring; z++)
                    {
                        if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Abs(z - cz)) != ring || !s_cells.TryGetValue(new Vector2Int(x, z), out var list))
                            continue;
                        foreach ((int sa, int sb) in list)
                        {
                            Vector2 q = Vector2.Lerp(s_nodes[sa], s_nodes[sb], Project(p, s_nodes[sa], s_nodes[sb]));
                            float d = (q - p).sqrMagnitude;
                            if (d < best)
                            {
                                best = d;
                                a = sa;
                                b = sb;
                                on = q;
                            }
                        }
                    }
                }
            }
            return a >= 0;
        }

        /// <summary>
        /// Itinéraire par les rues de <paramref name="from"/> à <paramref name="to"/> (points au sol, y du
        /// relief). Faux si aucune rue n'est assez proche ; le trajet direct suffit alors.
        /// </summary>
        public static bool TryRoute(Vector3 from, Vector3 to, List<Vector3> path)
        {
            path.Clear();
            EnsureBuilt();
            var start = new Vector2(from.x, from.z);
            var goal = new Vector2(to.x, to.z);
            if (s_nodes.Count == 0 || !Nearest(start, out int sa, out int sb, out Vector2 sOn) || !Nearest(goal, out int ga, out int gb, out Vector2 gOn))
                return false;

            // Même tronçon : en ligne (presque) droite.
            if ((sa == ga && sb == gb) || (sa == gb && sb == ga))
            {
                AddPoint(path, start);
                AddPoint(path, sOn);
                AddPoint(path, gOn);
                AddPoint(path, goal);
                return true;
            }

            // A* : départ virtuel relié aux deux bouts du tronçon le plus proche, arrivée de même.
            int count = s_nodes.Count;
            var g = new float[count];
            var came = new int[count];
            var closed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                g[i] = float.MaxValue;
                came[i] = -1;
            }
            var open = new SortedSet<(float f, int node)>();
            void Seed(int node, float cost)
            {
                if (cost < g[node])
                {
                    if (g[node] < float.MaxValue)
                        open.Remove((g[node] + Vector2.Distance(s_nodes[node], gOn), node));
                    g[node] = cost;
                    came[node] = -2; // relié au départ
                    open.Add((cost + Vector2.Distance(s_nodes[node], gOn), node));
                }
            }
            Seed(sa, Vector2.Distance(sOn, s_nodes[sa]));
            Seed(sb, Vector2.Distance(sOn, s_nodes[sb]));
            float bestTotal = float.MaxValue;
            int bestLast = -1;
            while (open.Count > 0)
            {
                (float f, int node) = open.Min;
                open.Remove(open.Min);
                if (f >= bestTotal)
                    break;
                if (closed[node])
                    continue;
                closed[node] = true;
                if (node == ga || node == gb)
                {
                    float total = g[node] + Vector2.Distance(s_nodes[node], gOn);
                    if (total < bestTotal)
                    {
                        bestTotal = total;
                        bestLast = node;
                    }
                }
                foreach (Edge edge in s_edges[node])
                {
                    if (closed[edge.To])
                        continue;
                    float cost = g[node] + edge.Length;
                    if (cost < g[edge.To])
                    {
                        if (g[edge.To] < float.MaxValue)
                            open.Remove((g[edge.To] + Vector2.Distance(s_nodes[edge.To], gOn), edge.To));
                        g[edge.To] = cost;
                        came[edge.To] = node;
                        open.Add((cost + Vector2.Distance(s_nodes[edge.To], gOn), edge.To));
                    }
                }
            }
            if (bestLast < 0)
                return false;

            var reversed = new List<Vector2>();
            for (int node = bestLast; node >= 0; node = came[node])
                reversed.Add(s_nodes[node]);
            AddPoint(path, start);
            AddPoint(path, sOn);
            for (int i = reversed.Count - 1; i >= 0; i--)
                AddPoint(path, reversed[i]);
            AddPoint(path, gOn);
            AddPoint(path, goal);
            return true;
        }

        private static void AddPoint(List<Vector3> path, Vector2 p)
        {
            if (path.Count > 0)
            {
                Vector3 last = path[path.Count - 1];
                if ((new Vector2(last.x, last.z) - p).sqrMagnitude < 0.25f)
                    return;
            }
            path.Add(new Vector3(p.x, CityLayout.GroundHeight(p.x, p.y), p.y));
        }
    }
}
