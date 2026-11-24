using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Décimation QEM (Quadric Error Metric) et optimisation mémoire pour mobile des maillages Poly Haven.
    /// Réduit les modèles haute définition (10k–40k triangles) vers deux niveaux de détail légers (LOD0
    /// proche, LOD1 lointain) tout en préservant les sous-maillages, les coutures UV et les normales.
    /// Les maillages générés sont mis en cache dans <c>Assets/_Project/Art/MobileProps</c>.
    /// </summary>
    internal static class MobileMeshOptimizer
    {
        public const string CacheFolder = "Assets/_Project/Art/MobileProps";

        // Version d'algorithme : incrémenter pour forcer la régénération du cache si la logique change.
        private const int FormatVersion = 2;

        public static void EnsureMobileMesh(string key, Mesh source, int targetTris, out Mesh result)
        {
            result = source;
            if (source == null)
                return;

            int sourceTris = source.triangles.Length / 3;
            if (sourceTris <= targetTris || targetTris < 24)
                return;

            EnsureCacheFolder();
            string assetPath = $"{CacheFolder}/{key}_v{FormatVersion}_{targetTris}.asset";
            var cached = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (cached != null && cached.triangles.Length / 3 <= Mathf.Max(targetTris + 16, (int)(targetTris * 1.05f)))
            {
                result = cached;
                return;
            }

            Mesh simplified = Simplify(source, targetTris, key);
            if (simplified == null)
                return;

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(simplified, assetPath);
            result = simplified;
        }

        private static void EnsureCacheFolder()
        {
            if (AssetDatabase.IsValidFolder(CacheFolder))
                return;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art"))
                AssetDatabase.CreateFolder("Assets/_Project", "Art");
            AssetDatabase.CreateFolder("Assets/_Project/Art", "MobileProps");
        }

        #region Simplification QEM

        private struct SymmetricMatrix
        {
            public double a00, a01, a02, a03;
            public double a11, a12, a13;
            public double a22, a23;
            public double a33;

            public void AddPlane(Vector3 n, double d, double weight = 1.0)
            {
                double nx = n.x, ny = n.y, nz = n.z;
                a00 += nx * nx * weight; a01 += nx * ny * weight; a02 += nx * nz * weight; a03 += nx * d * weight;
                a11 += ny * ny * weight; a12 += ny * nz * weight; a13 += ny * d * weight;
                a22 += nz * nz * weight; a23 += nz * d * weight;
                a33 += d * d * weight;
            }

            public void Add(in SymmetricMatrix o)
            {
                a00 += o.a00; a01 += o.a01; a02 += o.a02; a03 += o.a03;
                a11 += o.a11; a12 += o.a12; a13 += o.a13;
                a22 += o.a22; a23 += o.a23;
                a33 += o.a33;
            }

            public double Evaluate(Vector3 p)
            {
                double x = p.x, y = p.y, z = p.z;
                return a00 * x * x + 2.0 * a01 * x * y + 2.0 * a02 * x * z + 2.0 * a03 * x
                     + a11 * y * y + 2.0 * a12 * y * z + 2.0 * a13 * y
                     + a22 * z * z + 2.0 * a23 * z
                     + a33;
            }
        }

        private struct TriData
        {
            public int v0, v1, v2;
            public int submesh;
            public Vector3 normal;
            public bool deleted;
        }

        private struct CollapseCandidate : IComparable<CollapseCandidate>
        {
            public double cost;
            public int sA, sB;
            public Vector3 targetPos;
            public int stampA, stampB;

            public int CompareTo(CollapseCandidate other) => cost.CompareTo(other.cost);
        }

        private sealed class MinHeap
        {
            private readonly List<CollapseCandidate> _items = new List<CollapseCandidate>(1024);

            public int Count => _items.Count;

            public void Push(in CollapseCandidate item)
            {
                _items.Add(item);
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_items[p].cost <= _items[i].cost)
                        break;
                    (_items[p], _items[i]) = (_items[i], _items[p]);
                    i = p;
                }
            }

            public CollapseCandidate Pop()
            {
                CollapseCandidate root = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int left = (i << 1) + 1;
                    if (left >= _items.Count)
                        break;
                    int right = left + 1;
                    int smallest = right < _items.Count && _items[right].cost < _items[left].cost ? right : left;
                    if (_items[i].cost <= _items[smallest].cost)
                        break;
                    (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                    i = smallest;
                }
                return root;
            }
        }

        private static Mesh Simplify(Mesh source, int targetTris, string name)
        {
            Vector3[] srcPos = source.vertices;
            Vector3[] srcNorm = source.normals;
            Vector2[] srcUv = source.uv;
            if (srcPos == null || srcPos.Length == 0)
                return null;

            bool hasNorm = srcNorm != null && srcNorm.Length == srcPos.Length;
            bool hasUv = srcUv != null && srcUv.Length == srcPos.Length;
            int subCount = source.subMeshCount;
            float diag = Mathf.Max(1e-4f, source.bounds.size.magnitude);
            float invDiag = 1f / diag;
            float weldSqr = (diag * 1e-4f) * (diag * 1e-4f);
            double minArea2 = (double)diag * diag * 1e-10;

            // Copie mutable des attributs de sommets.
            int vertCount = srcPos.Length;
            var vPos = new Vector3[vertCount];
            var vNorm = new Vector3[vertCount];
            var vUv = new Vector2[vertCount];
            Array.Copy(srcPos, vPos, vertCount);
            if (hasNorm) Array.Copy(srcNorm, vNorm, vertCount);
            if (hasUv) Array.Copy(srcUv, vUv, vertCount);

            // Regroupe les sommets géométriquement confondus (coutures UV / arêtes vives) en nœuds spatiaux.
            var posToSpatial = new Dictionary<long, int>(vertCount);
            var vertToSpatial = new int[vertCount];
            var spatialPositions = new List<Vector3>(vertCount);
            var spatialVerts = new List<List<int>>(vertCount);
            for (int i = 0; i < vertCount; i++)
            {
                Vector3 p = vPos[i];
                long hash = HashPos(p * invDiag);
                int spId = -1;
                if (posToSpatial.TryGetValue(hash, out int existing) && (spatialPositions[existing] - p).sqrMagnitude < weldSqr)
                    spId = existing;
                if (spId < 0)
                {
                    spId = spatialPositions.Count;
                    spatialPositions.Add(p);
                    spatialVerts.Add(new List<int>(2));
                    posToSpatial[hash] = spId;
                }
                vertToSpatial[i] = spId;
                spatialVerts[spId].Add(i);
            }

            int spCount = spatialPositions.Count;
            var spRemoved = new bool[spCount];
            var spStamp = new int[spCount];
            var spQuadrics = new SymmetricMatrix[spCount];
            var spTris = new List<int>[spCount];
            for (int s = 0; s < spCount; s++)
                spTris[s] = new List<int>(6);

            // Extrait tous les triangles par sous-maillage (erreurs normalisées par invDiag pour tolérer les petits FBX Blender).
            var tris = new List<TriData>(source.triangles.Length / 3);
            var spEdgeCount = new Dictionary<(int, int), int>();
            double invDiag2 = (double)invDiag * invDiag;
            for (int sm = 0; sm < subCount; sm++)
            {
                int[] indices = source.GetTriangles(sm);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                    int s0 = vertToSpatial[i0], s1 = vertToSpatial[i1], s2 = vertToSpatial[i2];
                    if (s0 == s1 || s1 == s2 || s0 == s2)
                        continue;

                    Vector3 p0 = spatialPositions[s0], p1 = spatialPositions[s1], p2 = spatialPositions[s2];
                    Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                    double area2 = cross.magnitude;
                    if (area2 < minArea2)
                        continue;

                    Vector3 n = (cross / (float)area2);
                    double d = -Vector3.Dot(n, p0 * invDiag);
                    double weight = Math.Max(area2 * invDiag2, 1e-8);
                    spQuadrics[s0].AddPlane(n, d, weight);
                    spQuadrics[s1].AddPlane(n, d, weight);
                    spQuadrics[s2].AddPlane(n, d, weight);

                    int tIdx = tris.Count;
                    tris.Add(new TriData { v0 = i0, v1 = i1, v2 = i2, submesh = sm, normal = n, deleted = false });
                    spTris[s0].Add(tIdx);
                    spTris[s1].Add(tIdx);
                    spTris[s2].Add(tIdx);

                    IncrementEdge(spEdgeCount, s0, s1);
                    IncrementEdge(spEdgeCount, s1, s2);
                    IncrementEdge(spEdgeCount, s2, s0);
                }
            }

            // Pénalités de bordure et de coutures UV/matériau pour protéger les silhouettes.
            const double borderWeight = 25.0;
            var spSeam = new bool[spCount];
            for (int s = 0; s < spCount; s++)
            {
                if (spatialVerts[s].Count > 1)
                    spSeam[s] = true;
            }
            for (int t = 0; t < tris.Count; t++)
            {
                TriData tri = tris[t];
                int[] sv = { vertToSpatial[tri.v0], vertToSpatial[tri.v1], vertToSpatial[tri.v2] };
                for (int e = 0; e < 3; e++)
                {
                    int a = sv[e], b = sv[(e + 1) % 3];
                    var edgeKey = a < b ? (a, b) : (b, a);
                    if (spEdgeCount[edgeKey] == 1)
                    {
                        Vector3 pa = spatialPositions[a], pb = spatialPositions[b];
                        Vector3 edgeDir = (pb - pa).normalized;
                        Vector3 wallN = Vector3.Cross(tri.normal, edgeDir).normalized;
                        if (wallN.sqrMagnitude > 0.5f)
                        {
                            double wd = -Vector3.Dot(wallN, pa * invDiag);
                            spQuadrics[a].AddPlane(wallN, wd, borderWeight);
                            spQuadrics[b].AddPlane(wallN, wd, borderWeight);
                        }
                        spSeam[a] = true;
                        spSeam[b] = true;
                    }
                }
            }

            var heap = new MinHeap();
            foreach (KeyValuePair<(int, int), int> pair in spEdgeCount)
                PushEdge(pair.Key.Item1, pair.Key.Item2, spatialPositions, spQuadrics, spSeam, spStamp, invDiag, heap);

            int liveTris = tris.Count;
            var neighborSet = new HashSet<int>();
            while (liveTris > targetTris && heap.Count > 0)
            {
                CollapseCandidate cand = heap.Pop();
                if (spRemoved[cand.sA] || spRemoved[cand.sB])
                    continue;
                if (cand.stampA != spStamp[cand.sA] || cand.stampB != spStamp[cand.sB])
                    continue;

                if (!TryCollapse(cand.sA, cand.sB, cand.targetPos, tris, vertToSpatial, spatialPositions,
                        spatialVerts, spQuadrics, spSeam, spTris, spRemoved, spStamp, vPos, vNorm, vUv, hasNorm, hasUv, (float)minArea2, ref liveTris))
                    continue;

                // Réévalue les arêtes incidentes au nœud fusionné sB.
                neighborSet.Clear();
                foreach (int tIdx in spTris[cand.sB])
                {
                    TriData tri = tris[tIdx];
                    if (tri.deleted) continue;
                    int[] sArr = { vertToSpatial[tri.v0], vertToSpatial[tri.v1], vertToSpatial[tri.v2] };
                    foreach (int nb in sArr)
                    {
                        if (nb != cand.sB && !spRemoved[nb])
                            neighborSet.Add(nb);
                    }
                }
                foreach (int nb in neighborSet)
                    PushEdge(cand.sB, nb, spatialPositions, spQuadrics, spSeam, spStamp, invDiag, heap);
            }

            return BuildMesh(name, source, tris, subCount, vPos, vNorm, vUv, hasNorm, hasUv);
        }

        private static bool TryCollapse(
            int sA, int sB, Vector3 newPos,
            List<TriData> tris, int[] vertToSpatial, List<Vector3> spatialPositions,
            List<List<int>> spatialVerts, SymmetricMatrix[] spQuadrics, bool[] spSeam,
            List<int>[] spTris, bool[] spRemoved, int[] spStamp,
            Vector3[] vPos, Vector3[] vNorm, Vector2[] vUv, bool hasNorm, bool hasUv,
            float minArea2, ref int liveTris)
        {
            // Vérifie retournements de normales, triangles dégénérés et correspondance UV.
            for (int k = 0; k < 2; k++)
            {
                int srcSp = k == 0 ? sA : sB;
                int otherSp = k == 0 ? sB : sA;
                foreach (int tIdx in spTris[srcSp])
                {
                    TriData tri = tris[tIdx];
                    if (tri.deleted) continue;
                    int a = vertToSpatial[tri.v0], b = vertToSpatial[tri.v1], c = vertToSpatial[tri.v2];
                    if (a == otherSp || b == otherSp || c == otherSp)
                        continue; // sera supprimé par la fusion

                    Vector3 p0 = (a == srcSp) ? newPos : spatialPositions[a];
                    Vector3 p1 = (b == srcSp) ? newPos : spatialPositions[b];
                    Vector3 p2 = (c == srcSp) ? newPos : spatialPositions[c];
                    Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                    float area = cross.magnitude;
                    if (area < minArea2)
                        return false;
                    if (Vector3.Dot(cross / area, tri.normal) < 0.35f)
                        return false;
                }
            }

            // Associe chaque sommet de sA à son équivalent dans sB (plus proche en UV) pour zéro déchirure UV.
            var remap = new Dictionary<int, int>(spatialVerts[sA].Count);
            foreach (int vA in spatialVerts[sA])
            {
                int bestVb = -1;
                float bestUvSqr = float.MaxValue;
                foreach (int vB in spatialVerts[sB])
                {
                    float uvSqr = hasUv ? (vUv[vA] - vUv[vB]).sqrMagnitude : 0f;
                    if (uvSqr < bestUvSqr)
                    {
                        bestUvSqr = uvSqr;
                        bestVb = vB;
                    }
                }
                if (bestVb < 0 || (hasUv && bestUvSqr > 0.08f))
                    return false; // préserve strictement les coutures UV distinctes
                remap[vA] = bestVb;
            }

            // Calcul d'interpolation t (0 = sB, 1 = sA) pour ajuster doucement UV/normales si fusion au milieu.
            Vector3 oldA = spatialPositions[sA], oldB = spatialPositions[sB];
            float edgeLenSqr = (oldA - oldB).sqrMagnitude;
            float tA = edgeLenSqr > 1e-10f ? Mathf.Clamp01(Vector3.Dot(newPos - oldB, oldA - oldB) / edgeLenSqr) : 0f;

            foreach (KeyValuePair<int, int> pair in remap)
            {
                int vA = pair.Key, vB = pair.Value;
                vPos[vB] = newPos;
                if (hasUv)
                    vUv[vB] = Vector2.Lerp(vUv[vB], vUv[vA], tA);
                if (hasNorm)
                    vNorm[vB] = Vector3.Slerp(vNorm[vB], vNorm[vA], tA).normalized;
            }
            foreach (int vB in spatialVerts[sB])
                vPos[vB] = newPos;

            spatialPositions[sB] = newPos;
            spQuadrics[sB].Add(spQuadrics[sA]);
            spSeam[sB] = spSeam[sB] || spSeam[sA];
            spRemoved[sA] = true;
            spStamp[sB]++;

            // Réaffecte les triangles de sA et supprime ceux dégénérés (partageant sA et sB).
            foreach (int tIdx in spTris[sA])
            {
                TriData tri = tris[tIdx];
                if (tri.deleted) continue;

                if (remap.TryGetValue(tri.v0, out int rv0)) tri.v0 = rv0;
                if (remap.TryGetValue(tri.v1, out int rv1)) tri.v1 = rv1;
                if (remap.TryGetValue(tri.v2, out int rv2)) tri.v2 = rv2;

                int x0 = vertToSpatial[tri.v0], x1 = vertToSpatial[tri.v1], x2 = vertToSpatial[tri.v2];
                if (x0 == x1 || x1 == x2 || x0 == x2)
                {
                    tri.deleted = true;
                    tris[tIdx] = tri;
                    liveTris--;
                    continue;
                }

                Vector3 cross = Vector3.Cross(spatialPositions[x1] - spatialPositions[x0], spatialPositions[x2] - spatialPositions[x0]);
                float area = cross.magnitude;
                if (area < minArea2)
                {
                    tri.deleted = true;
                    tris[tIdx] = tri;
                    liveTris--;
                    continue;
                }

                tri.normal = cross / area;
                tris[tIdx] = tri;
                spTris[sB].Add(tIdx);
            }

            // Met à jour la normale des triangles qui touchaient déjà sB.
            for (int i = 0; i < spTris[sB].Count; i++)
            {
                int tIdx = spTris[sB][i];
                TriData tri = tris[tIdx];
                if (tri.deleted) continue;
                Vector3 cross = Vector3.Cross(vPos[tri.v1] - vPos[tri.v0], vPos[tri.v2] - vPos[tri.v0]);
                float area = cross.magnitude;
                if (area < minArea2)
                {
                    tri.deleted = true;
                    tris[tIdx] = tri;
                    liveTris--;
                    continue;
                }
                tri.normal = cross / area;
                tris[tIdx] = tri;
            }
            return true;
        }

        private static void PushEdge(
            int sA, int sB, List<Vector3> positions, SymmetricMatrix[] quadrics,
            bool[] spSeam, int[] spStamp, float invDiag, MinHeap heap)
        {
            if (sA == sB) return;
            SymmetricMatrix q = quadrics[sA];
            q.Add(quadrics[sB]);

            Vector3 pA = positions[sA], pB = positions[sB];
            Vector3 pMid = (pA + pB) * 0.5f;

            // Évaluation quadratique en repère normalisé (indépendant de l'échelle FBX).
            double errA = q.Evaluate(pA * invDiag);
            double errB = q.Evaluate(pB * invDiag);
            double errMid = q.Evaluate(pMid * invDiag);

            Vector3 bestPos;
            double bestErr;
            int keep, drop;
            if (errA <= errB && (spSeam[sA] || errA <= errMid))
            {
                bestPos = pA; bestErr = errA; keep = sA; drop = sB;
            }
            else if (errB <= errA && (spSeam[sB] || errB <= errMid))
            {
                bestPos = pB; bestErr = errB; keep = sB; drop = sA;
            }
            else
            {
                bestPos = pMid; bestErr = errMid; keep = sB; drop = sA;
            }

            double lenBonus = (pA - pB).magnitude * invDiag * 1e-4;
            double seamMult = (spSeam[drop] && !spSeam[keep]) ? 20.0 : (spSeam[drop] && spSeam[keep] ? 3.5 : 1.0);
            heap.Push(new CollapseCandidate
            {
                cost = Math.Max(0.0, bestErr) * seamMult + lenBonus,
                sA = drop,
                sB = keep,
                targetPos = bestPos,
                stampA = spStamp[drop],
                stampB = spStamp[keep],
            });
        }

        private static Mesh BuildMesh(
            string name, Mesh source, List<TriData> tris, int subCount,
            Vector3[] vPos, Vector3[] vNorm, Vector2[] vUv, bool hasNorm, bool hasUv)
        {
            var oldToNew = new int[vPos.Length];
            Array.Fill(oldToNew, -1);

            var outPos = new List<Vector3>(vPos.Length / 2);
            var outNorm = hasNorm ? new List<Vector3>(vPos.Length / 2) : null;
            var outUv = hasUv ? new List<Vector2>(vPos.Length / 2) : null;
            var subTris = new List<int>[subCount];
            for (int sm = 0; sm < subCount; sm++)
                subTris[sm] = new List<int>(tris.Count);

            int MapVert(int oldIdx)
            {
                int n = oldToNew[oldIdx];
                if (n >= 0) return n;
                n = outPos.Count;
                oldToNew[oldIdx] = n;
                outPos.Add(vPos[oldIdx]);
                outNorm?.Add(vNorm[oldIdx]);
                outUv?.Add(vUv[oldIdx]);
                return n;
            }

            for (int i = 0; i < tris.Count; i++)
            {
                TriData tri = tris[i];
                if (tri.deleted) continue;
                int a = MapVert(tri.v0), b = MapVert(tri.v1), c = MapVert(tri.v2);
                if (a == b || b == c || a == c) continue;
                subTris[tri.submesh].Add(a);
                subTris[tri.submesh].Add(b);
                subTris[tri.submesh].Add(c);
            }

            var mesh = new Mesh { name = name };
            if (outPos.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(outPos);
            if (outNorm != null) mesh.SetNormals(outNorm);
            if (outUv != null) mesh.SetUVs(0, outUv);
            mesh.subMeshCount = subCount;
            for (int sm = 0; sm < subCount; sm++)
                mesh.SetTriangles(subTris[sm], sm, false);
            if (!hasNorm)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            MeshUtility.Optimize(mesh);
            return mesh;
        }

        private static void IncrementEdge(Dictionary<(int, int), int> map, int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            map[key] = map.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        private static long HashPos(Vector3 p)
        {
            long x = Mathf.RoundToInt(p.x * 10000f);
            long y = Mathf.RoundToInt(p.y * 10000f);
            long z = Mathf.RoundToInt(p.z * 10000f);
            return ((x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L));
        }

        #endregion
    }
}
