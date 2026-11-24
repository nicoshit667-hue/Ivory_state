using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Accumule des pavés (bâtiments, trottoirs, réservoirs…) dans un seul maillage.
    /// <para>
    /// Optimisation mobile : un bloc de 25 bâtiments donne <b>un</b> maillage et <b>un</b> draw call
    /// par matériau, au lieu de 25 objets. Les faces du dessous (jamais visibles) sont omises.
    /// </para>
    /// <para>
    /// Données de sommet, lues par le shader <c>IvoryState/Facade</c> :
    /// <list type="bullet">
    /// <item>UV0 en mètres : les textures se répètent à l'échelle réelle ;</item>
    /// <item>UV1 = grille des ouvertures (x = travée, y = étage), ou (-1, -1) pour un mur plein ;</item>
    /// <item>couleur : RVB = teinte de l'immeuble, A = graine aléatoire (fenêtres allumées, rideaux…) ;</item>
    /// <item>tangentes, pour le relief de l'enduit.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class BoxMeshBuilder
    {
        /// <summary>Largeur visée d'une travée (une fenêtre) et hauteur d'un étage, en mètres.</summary>
        public const float BayWidth = 3.2f;
        public const float FloorHeight = 3f;

        private static readonly Vector2 Plain = new Vector2(-1f, -1f);

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector4> _tangents = new List<Vector4>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _bays = new List<Vector2>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<int> _triangles = new List<int>();

        private Color32 _color = new Color32(255, 255, 255, 0);

        public bool IsEmpty => _vertices.Count == 0;

        /// <summary>
        /// Rubans et disques plats en UV monde (x, z) : deux chaussées qui se chevauchent (carrefour) montrent
        /// exactement la même texture, donc aucun scintillement visible.
        /// </summary>
        public bool WorldUv { get; set; }

        /// <summary>Teinte (RVB) et graine (0-255) des prochaines faces.</summary>
        public void SetColor(Color tint, int seed = 0)
        {
            Color32 c = tint;
            c.a = (byte)(seed & 0xFF);
            _color = c;
        }

        /// <summary>Ajoute un pavé plein (sans fenêtres) défini par son centre de base (au sol) et sa taille.</summary>
        public void AddBox(Vector3 baseCenter, Vector3 size)
        {
            AddBoxInternal(baseCenter, size, false, true);
        }

        /// <summary>Pavé sans face du dessus (ex. : corps d'un immeuble coiffé d'une dalle).</summary>
        public void AddBoxSides(Vector3 baseCenter, Vector3 size)
        {
            AddBoxInternal(baseCenter, size, false, false);
        }

        /// <summary>
        /// Immeuble : murs avec grille de fenêtres (une travée d'environ <see cref="BayWidth"/> m par
        /// fenêtre, un étage tous les <see cref="FloorHeight"/> m), toit plat.
        /// </summary>
        public void AddBuilding(Vector3 baseCenter, Vector3 size, bool roof = true)
        {
            AddBoxInternal(baseCenter, size, true, roof);
        }

        private void AddBoxInternal(Vector3 baseCenter, Vector3 size, bool windows, bool top)
        {
            Vector3 half = new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            Vector3 min = baseCenter - half;
            Vector3 max = baseCenter + half + new Vector3(0f, size.y, 0f);

            if (top)
            {
                // UV du dessus en coordonnées monde : les dalles voisines se raccordent.
                AddQuad(new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                        new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), Vector3.up,
                        new Vector2(min.x, min.z), size.x, size.z, Plain, Vector2.zero);
            }

            Vector2 floors = windows ? new Vector2(0f, size.y / FloorHeight) : Vector2.zero;
            AddWall(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), size.y, Vector3.back, windows, floors);
            AddWall(new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), size.y, Vector3.forward, windows, floors);
            AddWall(new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z), size.y, Vector3.left, windows, floors);
            AddWall(new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), size.y, Vector3.right, windows, floors);
        }

        /// <summary>Mur vertical de <paramref name="from"/> à <paramref name="to"/> (au sol), vu depuis <paramref name="normal"/>.</summary>
        private void AddWall(Vector3 from, Vector3 to, float height, Vector3 normal, bool windows, Vector2 floors)
        {
            float width = Vector3.Distance(from, to);
            Vector3 up = new Vector3(0f, height, 0f);
            Vector2 bays = Plain;
            Vector2 baySize = Vector2.zero;
            if (windows && width >= 2f)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(width / BayWidth));
                bays = new Vector2(0f, 0f);
                baySize = new Vector2(count, floors.y);
            }

            AddQuad(from, from + up, to + up, to, normal, Vector2.zero, width, height, bays, baySize);
        }

        /// <summary>Cylindre vertical (réservoir d'eau, poteau…), sans face du dessous.</summary>
        public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments = 8)
        {
            float circumference = 2f * Mathf.PI * radius;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var p0 = baseCenter + new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = baseCenter + new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Vector3 normal = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                // Ordre p0 -> p1 : face tournée vers l'extérieur (sens horaire vu de dehors).
                AddQuad(p0, p0 + Vector3.up * height, p1 + Vector3.up * height, p1, normal,
                        new Vector2(circumference * i / segments, 0f), circumference / segments, height, Plain, Vector2.zero);
            }

            // Couvercle : éventail de triangles.
            int center = _vertices.Count;
            AddVertex(baseCenter + Vector3.up * height, Vector3.up, new Vector4(1f, 0f, 0f, -1f), Vector2.zero, Plain);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var offset = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                AddVertex(baseCenter + Vector3.up * height + offset, Vector3.up, new Vector4(1f, 0f, 0f, -1f),
                          new Vector2(offset.x, offset.z), Plain);
            }
            for (int i = 0; i < segments; i++)
            {
                _triangles.Add(center); _triangles.Add(center + i + 2); _triangles.Add(center + i + 1);
            }
        }

        /// <summary>
        /// Quadrilatère horizontal (marquage au sol, flaque…), centré, orienté par <paramref name="right"/>.
        /// </summary>
        public void AddFlatQuad(Vector3 center, Vector3 right, float length, float width, bool unitUv = false)
        {
            Vector3 r = right.normalized * (length * 0.5f);
            Vector3 f = Vector3.Cross(r.normalized, Vector3.up) * (width * 0.5f);
            AddQuad(center - r - f, center - r + f, center + r + f, center + r - f, Vector3.up,
                    Vector2.zero, unitUv ? 1f : length, unitUv ? 1f : width, Plain, Vector2.zero);
        }

        /// <summary>Poutre de section carrée de <paramref name="from"/> à <paramref name="to"/> (fil, bras de lampadaire…).</summary>
        public void AddBeam(Vector3 from, Vector3 to, float thickness)
        {
            Vector3 dir = (to - from).normalized;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(side, dir).normalized;
            float h = thickness * 0.5f;
            float length = Vector3.Distance(from, to);
            Vector3[] offsets = { up * h + side * h, side * h - up * h, -up * h - side * h, up * h - side * h };

            for (int i = 0; i < 4; i++)
            {
                Vector3 o0 = offsets[i], o1 = offsets[(i + 1) % 4];
                Vector3 normal = (o0 + o1).normalized;
                Vector3 a = from + o0, b = to + o0, c = to + o1, d = from + o1;
                // Sens horaire vu de l'extérieur (convention Unity) : sinon on inverse le quadrilatère.
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
                    (b, d) = (d, b);
                AddQuad(a, b, c, d, normal, Vector2.zero, thickness, length, Plain, Vector2.zero);
            }
        }

        #region Formes libres (monuments, véhicules, arbres, routes)

        /// <summary>Quadrilatère quelconque a-b-c-d (sens horaire vu de devant), normale calculée.</summary>
        public void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude < 0.5f)
                normal = Vector3.Cross(c - a, d - a).normalized;
            float width = Vector3.Distance(a, d), height = Vector3.Distance(a, b);
            AddQuad(a, b, c, d, normal, Vector2.zero, width, height, Plain, Vector2.zero);
        }

        /// <summary>Triangle a-b-c (sens horaire vu de devant), normale calculée.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 tangent = (c - a).normalized;
            var t = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
            int start = _vertices.Count;
            AddVertex(a, normal, t, new Vector2(0f, 0f), Plain);
            AddVertex(b, normal, t, new Vector2(0f, Vector3.Distance(a, b)), Plain);
            AddVertex(c, normal, t, new Vector2(Vector3.Distance(a, c), 0f), Plain);
            _triangles.Add(start); _triangles.Add(start + 1); _triangles.Add(start + 2);
        }

        /// <summary>
        /// Pavé orienté : centre de base <paramref name="baseCenter"/>, tourné de <paramref name="yaw"/>°
        /// autour de la verticale. <paramref name="windows"/> : grille de fenêtres sur les côtés.
        /// </summary>
        public void AddOrientedBox(Vector3 baseCenter, Vector3 size, float yaw, bool windows = false, bool top = true)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 hx = q * new Vector3(size.x * 0.5f, 0f, 0f);
            Vector3 hz = q * new Vector3(0f, 0f, size.z * 0.5f);
            Vector3 up = new Vector3(0f, size.y, 0f);
            Vector3 p0 = baseCenter - hx - hz, p1 = baseCenter + hx - hz, p2 = baseCenter + hx + hz, p3 = baseCenter - hx + hz;
            Vector2 floors = windows ? new Vector2(0f, size.y / FloorHeight) : Vector2.zero;
            AddWall(p0, p1, size.y, (q * Vector3.back), windows, floors);
            AddWall(p1, p2, size.y, (q * Vector3.right), windows, floors);
            AddWall(p2, p3, size.y, (q * Vector3.forward), windows, floors);
            AddWall(p3, p0, size.y, (q * Vector3.left), windows, floors);
            if (top)
                AddQuad(p0 + up, p3 + up, p2 + up, p1 + up, Vector3.up, new Vector2(p0.x, p0.z), size.x, size.z, Plain, Vector2.zero);
        }

        /// <summary>Pavé tourné dans les trois axes (poutre inclinée, hauban, toit en pente…), centré.</summary>
        public void AddRotatedBox(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 x = rotation * new Vector3(size.x * 0.5f, 0f, 0f);
            Vector3 y = rotation * new Vector3(0f, size.y * 0.5f, 0f);
            Vector3 z = rotation * new Vector3(0f, 0f, size.z * 0.5f);
            Vector3 c = center;
            // Six faces, chacune vue de l'extérieur (sens horaire).
            AddFace(c - x - y - z, c - x + y - z, c + x + y - z, c + x - y - z); // arrière (-z)
            AddFace(c + x - y + z, c + x + y + z, c - x + y + z, c - x - y + z); // avant (+z)
            AddFace(c - x - y + z, c - x + y + z, c - x + y - z, c - x - y - z); // gauche (-x)
            AddFace(c + x - y - z, c + x + y - z, c + x + y + z, c + x - y + z); // droite (+x)
            AddFace(c - x + y - z, c - x + y + z, c + x + y + z, c + x + y - z); // dessus
            AddFace(c - x - y + z, c - x - y - z, c + x - y - z, c + x - y + z); // dessous
        }

        /// <summary>Dôme (demi-ellipsoïde) posé sur <paramref name="baseCenter"/>.</summary>
        public void AddDome(Vector3 baseCenter, float radius, float height, int segments = 12, int rings = 5)
        {
            for (int r = 0; r < rings; r++)
            {
                float a0 = r * Mathf.PI * 0.5f / rings, a1 = (r + 1) * Mathf.PI * 0.5f / rings;
                float r0 = Mathf.Cos(a0) * radius, r1 = Mathf.Cos(a1) * radius;
                float y0 = Mathf.Sin(a0) * height, y1 = Mathf.Sin(a1) * height;
                for (int s = 0; s < segments; s++)
                {
                    float b0 = s * Mathf.PI * 2f / segments, b1 = (s + 1) * Mathf.PI * 2f / segments;
                    Vector3 p00 = baseCenter + new Vector3(Mathf.Cos(b0) * r0, y0, Mathf.Sin(b0) * r0);
                    Vector3 p01 = baseCenter + new Vector3(Mathf.Cos(b1) * r0, y0, Mathf.Sin(b1) * r0);
                    Vector3 p10 = baseCenter + new Vector3(Mathf.Cos(b0) * r1, y1, Mathf.Sin(b0) * r1);
                    Vector3 p11 = baseCenter + new Vector3(Mathf.Cos(b1) * r1, y1, Mathf.Sin(b1) * r1);
                    if (r == rings - 1)
                        AddTriangle(p00, p10, p01);
                    else
                        AddFace(p00, p10, p11, p01);
                }
            }
        }

        /// <summary>Ellipsoïde à facettes (feuillage d'arbre, ballon…).</summary>
        public void AddEllipsoid(Vector3 center, Vector3 radii, int segments = 7, int rings = 4)
        {
            for (int r = 0; r < rings; r++)
            {
                float a0 = -Mathf.PI * 0.5f + r * Mathf.PI / rings, a1 = -Mathf.PI * 0.5f + (r + 1) * Mathf.PI / rings;
                for (int s = 0; s < segments; s++)
                {
                    float b0 = s * Mathf.PI * 2f / segments, b1 = (s + 1) * Mathf.PI * 2f / segments;
                    Vector3 P(float a, float b) => center + new Vector3(Mathf.Cos(a) * Mathf.Cos(b) * radii.x, Mathf.Sin(a) * radii.y, Mathf.Cos(a) * Mathf.Sin(b) * radii.z);
                    Vector3 p00 = P(a0, b0), p01 = P(a0, b1), p10 = P(a1, b0), p11 = P(a1, b1);
                    if (r == 0)
                        AddTriangle(p00, p10, p11);
                    else if (r == rings - 1)
                        AddTriangle(p00, p10, p01);
                    else
                        AddFace(p00, p10, p11, p01);
                }
            }
        }

        /// <summary>Cône (ou tronc de cône si <paramref name="topRadius"/> &gt; 0) vertical.</summary>
        public void AddCone(Vector3 baseCenter, float radius, float topRadius, float height, int segments = 8)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 b0 = baseCenter + new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 b1 = baseCenter + new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Vector3 t0 = baseCenter + new Vector3(Mathf.Cos(a0) * topRadius, height, Mathf.Sin(a0) * topRadius);
                Vector3 t1 = baseCenter + new Vector3(Mathf.Cos(a1) * topRadius, height, Mathf.Sin(a1) * topRadius);
                if (topRadius <= 0.001f)
                    AddTriangle(b0, t0, b1);
                else
                    AddFace(b0, t0, t1, b1);
            }
        }

        /// <summary>Toit à quatre pans (villa, pavillon) posé sur <paramref name="baseCenter"/> avec débord <paramref name="overhang"/>.</summary>
        public void AddHipRoof(Vector3 baseCenter, Vector2 footprint, float height, float overhang = 0.45f)
        {
            float hx = footprint.x * 0.5f + overhang;
            float hz = footprint.y * 0.5f + overhang;
            Vector3 p00 = baseCenter + new Vector3(-hx, 0f, -hz);
            Vector3 p10 = baseCenter + new Vector3(hx, 0f, -hz);
            Vector3 p11 = baseCenter + new Vector3(hx, 0f, hz);
            Vector3 p01 = baseCenter + new Vector3(-hx, 0f, hz);
            if (hx >= hz)
            {
                float rx = Mathf.Max(0.25f, hx - hz * 0.85f);
                Vector3 r0 = baseCenter + new Vector3(-rx, height, 0f);
                Vector3 r1 = baseCenter + new Vector3(rx, height, 0f);
                AddFace(p00, r0, r1, p10); // pan sud
                AddFace(p11, r1, r0, p01); // pan nord
                AddTriangle(p01, r0, p00); // croupe ouest
                AddTriangle(p10, r1, p11); // croupe est
            }
            else
            {
                float rz = Mathf.Max(0.25f, hz - hx * 0.85f);
                Vector3 r0 = baseCenter + new Vector3(0f, height, -rz);
                Vector3 r1 = baseCenter + new Vector3(0f, height, rz);
                AddFace(p01, r1, r0, p00); // pan ouest
                AddFace(p10, r0, r1, p11); // pan est
                AddTriangle(p00, r0, p10); // croupe sud
                AddTriangle(p11, r1, p01); // croupe nord
            }
            // Sous-face du débord (visible depuis la cour ou la rue).
            AddFace(p01, p00, p10, p11);
        }

        /// <summary>
        /// Calcule les demi-largeurs latérales (bissectrice normée + échelle d'onglet) et l'abscisse
        /// curviligne le long d'une polyligne, pour que les tronçons voisins partagent exactement les
        /// mêmes sommets aux coutures de 128 m et gardent une largeur constante dans les virages.
        /// </summary>
        public static void ComputeRibbonSides(IList<Vector3> points, float width, out Vector3[] sides, out float[] travel)
        {
            int n = points.Count;
            sides = new Vector3[n];
            travel = new float[n];
            if (n < 2)
                return;
            float half = width * 0.5f;
            for (int i = 0; i < n; i++)
            {
                Vector3 d0 = i > 0 ? points[i] - points[i - 1] : points[1] - points[0];
                Vector3 d1 = i < n - 1 ? points[i + 1] - points[i] : points[n - 1] - points[n - 2];
                d0.y = 0f;
                d1.y = 0f;
                if (d0.sqrMagnitude > 1e-6f)
                    d0.Normalize();
                if (d1.sqrMagnitude > 1e-6f)
                    d1.Normalize();
                Vector3 forward = d0 + d1;
                forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : (d1.sqrMagnitude > 1e-4f ? d1 : Vector3.forward);
                Vector3 normal = Vector3.Cross(Vector3.up, forward);
                Vector3 segNormal = Vector3.Cross(Vector3.up, d1.sqrMagnitude > 1e-4f ? d1 : forward);
                float cos = Mathf.Max(0.45f, Mathf.Abs(Vector3.Dot(normal, segNormal)));
                sides[i] = normal * (half / cos);
                if (i > 0)
                    travel[i] = travel[i - 1] + Vector3.Distance(points[i], points[i - 1]);
            }
        }

        /// <summary>Ajoute un segment de ruban entre deux coupes précalculées (sans fissure aux bords de tronçon).</summary>
        public void AddRibbonSegment(Vector3 p0, Vector3 side0, float v0, Vector3 p1, Vector3 side1, float v1, float width, float lift = 0f)
        {
            int start = _vertices.Count;
            Vector3 up = Vector3.up * lift;
            Vector3 t0 = side0.sqrMagnitude > 1e-6f ? side0.normalized : Vector3.right;
            Vector3 t1 = side1.sqrMagnitude > 1e-6f ? side1.normalized : Vector3.right;
            Vector3 a = p0 + up - side0, b = p0 + up + side0, c = p1 + up - side1, d = p1 + up + side1;
            AddVertex(a, Vector3.up, new Vector4(t0.x, t0.y, t0.z, 1f), WorldUv ? new Vector2(a.x, a.z) : new Vector2(0f, v0), Plain);
            AddVertex(b, Vector3.up, new Vector4(t0.x, t0.y, t0.z, 1f), WorldUv ? new Vector2(b.x, b.z) : new Vector2(width, v0), Plain);
            AddVertex(c, Vector3.up, new Vector4(t1.x, t1.y, t1.z, 1f), WorldUv ? new Vector2(c.x, c.z) : new Vector2(0f, v1), Plain);
            AddVertex(d, Vector3.up, new Vector4(t1.x, t1.y, t1.z, 1f), WorldUv ? new Vector2(d.x, d.z) : new Vector2(width, v1), Plain);
            _triangles.Add(start); _triangles.Add(start + 2); _triangles.Add(start + 3);
            _triangles.Add(start); _triangles.Add(start + 3); _triangles.Add(start + 1);
        }

        /// <summary>Disque horizontal plat (arrondi de carrefour ou d'extrémité de chaussée).</summary>
        public void AddFlatDisc(Vector3 center, float radius, float lift = 0f, int segments = 10)
        {
            int c = _vertices.Count;
            Vector3 p = center + Vector3.up * lift;
            var t = new Vector4(1f, 0f, 0f, 1f);
            AddVertex(p, Vector3.up, t, WorldUv ? new Vector2(p.x, p.z) : new Vector2(radius, radius), Plain);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float dx = Mathf.Cos(a) * radius, dz = Mathf.Sin(a) * radius;
                AddVertex(p + new Vector3(dx, 0f, dz), Vector3.up, t, WorldUv ? new Vector2(p.x + dx, p.z + dz) : new Vector2(radius + dx, radius + dz), Plain);
            }
            for (int i = 0; i < segments; i++)
            {
                _triangles.Add(c); _triangles.Add(c + i + 2); _triangles.Add(c + i + 1);
            }
        }

        /// <summary>
        /// Ruban posé au sol le long d'une polyligne (route, chemin, piste) : <paramref name="width"/> m de
        /// large, UV en mètres (U en travers, V le long).
        /// </summary>
        public void AddRibbon(IList<Vector3> points, float width, float lift = 0f)
        {
            if (points.Count < 2)
                return;
            ComputeRibbonSides(points, width, out Vector3[] sides, out float[] travel);
            int previous = -1;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 side = sides[i];
                int start = _vertices.Count;
                Vector3 p = points[i] + Vector3.up * lift;
                Vector3 tn = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
                var t = new Vector4(tn.x, tn.y, tn.z, 1f);
                AddVertex(p - side, Vector3.up, t, new Vector2(0f, travel[i]), Plain);
                AddVertex(p + side, Vector3.up, t, new Vector2(width, travel[i]), Plain);
                if (previous >= 0)
                {
                    _triangles.Add(previous); _triangles.Add(start); _triangles.Add(start + 1);
                    _triangles.Add(previous); _triangles.Add(start + 1); _triangles.Add(previous + 1);
                }
                previous = start;
            }
        }

        /// <summary>
        /// Ajoute un segment de trottoir chanfreiné (bordures inclinées type T2 de chaque côté) entre deux coupes
        /// précalculées par <see cref="ComputeRibbonSides"/>, sans couture aux bords des tronçons de 128 m.
        /// </summary>
        public void AddSidewalkSegment(Vector3 p0, Vector3 side0, float v0, Vector3 p1, Vector3 side1, float v1,
            float width, float height = 0.15f)
        {
            const float bevelRatio = 0.94f; // ~9 cm de chanfrein sur un trottoir de 3 m
            const float bottomY = -0.02f;
            Vector3 lBot0 = p0 - side0 + Vector3.up * bottomY;
            Vector3 lTop0 = p0 - side0 * bevelRatio + Vector3.up * height;
            Vector3 rTop0 = p0 + side0 * bevelRatio + Vector3.up * height;
            Vector3 rBot0 = p0 + side0 + Vector3.up * bottomY;

            Vector3 lBot1 = p1 - side1 + Vector3.up * bottomY;
            Vector3 lTop1 = p1 - side1 * bevelRatio + Vector3.up * height;
            Vector3 rTop1 = p1 + side1 * bevelRatio + Vector3.up * height;
            Vector3 rBot1 = p1 + side1 + Vector3.up * bottomY;

            // Chanfrein gauche, dalle supérieure, chanfrein droit (sens horaire vu de l'extérieur).
            AddCurbStrip(lBot0, lTop0, lTop1, lBot1, 0f, 0.22f, v0, v1);
            AddCurbStrip(lTop0, rTop0, rTop1, lTop1, 0.22f, width - 0.22f, v0, v1);
            AddCurbStrip(rTop0, rBot0, rBot1, rTop1, width - 0.22f, width, v0, v1);
        }

        /// <summary>Ferme l'extrémité d'un ruban de trottoir (sens horaire vu depuis <paramref name="outward"/>).</summary>
        public void AddSidewalkCap(Vector3 p, Vector3 side, Vector3 outward, float width, float height = 0.15f, bool startCap = true)
        {
            const float bevelRatio = 0.94f;
            const float bottomY = -0.02f;
            Vector3 lBot = p - side + Vector3.up * bottomY;
            Vector3 lTop = p - side * bevelRatio + Vector3.up * height;
            Vector3 rTop = p + side * bevelRatio + Vector3.up * height;
            Vector3 rBot = p + side + Vector3.up * bottomY;
            if (startCap)
                AddFace(lBot, lTop, rTop, rBot);
            else
                AddFace(rBot, rTop, lTop, lBot);
        }

        private void AddCurbStrip(Vector3 a0, Vector3 b0, Vector3 b1, Vector3 a1, float u0, float u1, float v0, float v1)
        {
            Vector3 normal = Vector3.Cross(a1 - a0, b0 - a0).normalized;
            if (normal.sqrMagnitude < 0.25f)
                normal = Vector3.up;
            Vector3 tan = (b0 - a0);
            tan = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.right;
            var t = new Vector4(tan.x, tan.y, tan.z, 1f);
            int start = _vertices.Count;
            AddVertex(a0, normal, t, new Vector2(u0, v0), Plain);
            AddVertex(b0, normal, t, new Vector2(u1, v0), Plain);
            AddVertex(b1, normal, t, new Vector2(u1, v1), Plain);
            AddVertex(a1, normal, t, new Vector2(u0, v1), Plain);
            _triangles.Add(start); _triangles.Add(start + 3); _triangles.Add(start + 2);
            _triangles.Add(start); _triangles.Add(start + 2); _triangles.Add(start + 1);
        }

        /// <summary>
        /// Copie un sous-maillage importé (ex. modèle Blender FBX) dans ce constructeur, en appliquant
        /// transformation, mise à l'échelle métrique des UV0, préservation optionnelle d'UV1 (vitres) et teinte.
        /// </summary>
        public void AddSubmesh(Mesh source, int submeshIndex, Matrix4x4 matrix, Color tint, int seed,
            bool multiplyVertexColor = true, Vector3? uvSizeScale = null, bool forcePlainUv1 = false)
        {
            if (source == null || submeshIndex < 0 || submeshIndex >= source.subMeshCount)
                return;
            int[] tris = source.GetTriangles(submeshIndex);
            if (tris == null || tris.Length < 3)
                return;

            Vector3[] verts = source.vertices;
            Vector3[] norms = source.normals;
            Vector4[] tans = source.tangents;
            Vector2[] uvs = source.uv;
            Vector2[] uv2s = source.uv2;
            Color32[] cols = source.colors32;

            bool hasNorm = norms != null && norms.Length == verts.Length;
            bool hasTan = tans != null && tans.Length == verts.Length;
            bool hasUv0 = uvs != null && uvs.Length == verts.Length;
            bool hasUv2 = uv2s != null && uv2s.Length == verts.Length;
            bool hasCol = cols != null && cols.Length == verts.Length;

            Vector3 sizeScale = uvSizeScale ?? Vector3.one;
            bool scaleUv = uvSizeScale.HasValue &&
                           (Mathf.Abs(sizeScale.x - 1f) > 1e-3f ||
                            Mathf.Abs(sizeScale.y - 1f) > 1e-3f ||
                            Mathf.Abs(sizeScale.z - 1f) > 1e-3f);
            bool flipWinding = matrix.determinant < 0f;
            Matrix4x4 invT = matrix.inverse.transpose;
            byte seedByte = (byte)(seed & 0xFF);

            var remap = new Dictionary<int, int>(tris.Length / 2);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int i0 = MapVertex(tris[i]);
                int i1 = MapVertex(tris[i + 1]);
                int i2 = MapVertex(tris[i + 2]);
                if (flipWinding)
                {
                    _triangles.Add(i0); _triangles.Add(i2); _triangles.Add(i1);
                }
                else
                {
                    _triangles.Add(i0); _triangles.Add(i1); _triangles.Add(i2);
                }
            }

            int MapVertex(int srcIdx)
            {
                if (remap.TryGetValue(srcIdx, out int existing))
                    return existing;

                Vector3 pos = matrix.MultiplyPoint3x4(verts[srcIdx]);
                Vector3 srcN = hasNorm ? norms[srcIdx] : Vector3.up;
                Vector3 n = invT.MultiplyVector(srcN).normalized;
                Vector4 srcT = hasTan ? tans[srcIdx] : new Vector4(1f, 0f, 0f, 1f);
                Vector3 tDir = matrix.MultiplyVector(new Vector3(srcT.x, srcT.y, srcT.z)).normalized;
                if (tDir.sqrMagnitude < 1e-5f)
                    tDir = Vector3.right;
                var tangent = new Vector4(tDir.x, tDir.y, tDir.z, flipWinding ? -srcT.w : srcT.w);

                Vector2 uv0 = hasUv0 ? uvs[srcIdx] : Vector2.zero;
                if (scaleUv)
                {
                    float ax = Mathf.Abs(srcN.x), ay = Mathf.Abs(srcN.y), az = Mathf.Abs(srcN.z);
                    if (ay >= ax && ay >= az)
                        uv0 = new Vector2(uv0.x * sizeScale.x, uv0.y * sizeScale.z);
                    else if (ax >= az)
                        uv0 = new Vector2(uv0.x * sizeScale.z, uv0.y * sizeScale.y);
                    else
                        uv0 = new Vector2(uv0.x * sizeScale.x, uv0.y * sizeScale.y);
                }

                Vector2 uv1 = (!forcePlainUv1 && hasUv2) ? uv2s[srcIdx] : Plain;
                Color baseCol = hasCol ? (Color)cols[srcIdx] : Color.white;
                Color32 c32 = multiplyVertexColor
                    ? new Color(baseCol.r * tint.r, baseCol.g * tint.g, baseCol.b * tint.b, 1f)
                    : baseCol;
                c32.a = seedByte;

                int idx = _vertices.Count;
                Color32 savedColor = _color;
                _color = c32;
                AddVertex(pos, n, tangent, uv0, uv1);
                _color = savedColor;
                remap[srcIdx] = idx;
                return idx;
            }
        }

        #endregion

        /// <summary>
        /// Quadrilatère a-b-c-d (a en bas à gauche, b en haut à gauche…). UV0 en mètres à partir de
        /// <paramref name="uvOrigin"/> ; UV1 de <paramref name="bayOrigin"/> à bayOrigin + bayCount.
        /// </summary>
        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
            Vector2 uvOrigin, float width, float height, Vector2 bayOrigin, Vector2 bayCount)
        {
            int start = _vertices.Count;

            // Tangente : sens des U croissants ; signe tel que cross(normale, tangente) * w = sens des V.
            Vector3 tangent = (d - a).normalized;
            Vector3 bitangent = (b - a).normalized;
            float w = Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0f ? -1f : 1f;
            var t = new Vector4(tangent.x, tangent.y, tangent.z, w);

            bool plain = bayOrigin.x < 0f;
            AddVertex(a, normal, t, uvOrigin, plain ? Plain : bayOrigin);
            AddVertex(b, normal, t, uvOrigin + new Vector2(0f, height), plain ? Plain : bayOrigin + new Vector2(0f, bayCount.y));
            AddVertex(c, normal, t, uvOrigin + new Vector2(width, height), plain ? Plain : bayOrigin + bayCount);
            AddVertex(d, normal, t, uvOrigin + new Vector2(width, 0f), plain ? Plain : bayOrigin + new Vector2(bayCount.x, 0f));

            _triangles.Add(start); _triangles.Add(start + 1); _triangles.Add(start + 2);
            _triangles.Add(start); _triangles.Add(start + 2); _triangles.Add(start + 3);
        }

        /// <summary>
        /// Repère appliqué à tout ce qui est ajouté (position et rotation autour de la verticale), pour
        /// construire un objet en coordonnées locales et le poser tourné dans le maillage commun (bâtiments
        /// alignés sur leur rue). Identité par défaut ; <see cref="ClearFrame"/> pour revenir au repère du maillage.
        /// </summary>
        public void SetFrame(Vector3 origin, float yaw)
        {
            _frameRotation = Quaternion.Euler(0f, yaw, 0f);
            _frameOrigin = origin;
            _framed = true;
        }

        public void ClearFrame() => _framed = false;

        private bool _framed;
        private Quaternion _frameRotation = Quaternion.identity;
        private Vector3 _frameOrigin;

        private void AddVertex(Vector3 position, Vector3 normal, Vector4 tangent, Vector2 uv, Vector2 bay)
        {
            if (_framed)
            {
                position = _frameOrigin + _frameRotation * position;
                normal = _frameRotation * normal;
                Vector3 t = _frameRotation * new Vector3(tangent.x, tangent.y, tangent.z);
                tangent = new Vector4(t.x, t.y, t.z, tangent.w);
            }
            _vertices.Add(position);
            _normals.Add(normal);
            _tangents.Add(tangent);
            _uvs.Add(uv);
            _bays.Add(bay);
            _colors.Add(_color);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetTangents(_tangents);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _bays);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: false);
            return mesh;
        }
    }
}
