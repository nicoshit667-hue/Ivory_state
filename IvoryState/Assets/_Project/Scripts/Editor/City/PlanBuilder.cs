using System.Collections.Generic;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Outil de construction d'intérieurs à partir de plans (cotes en mètres, repère local : x vers
    /// l'est, z vers le nord, y vers le haut). Chaque matériau a son propre BoxMeshBuilder ; les colliders,
    /// les plafonniers et les objets 3D Poly Haven (<see cref="Props"/>) sont listés à part. Utilisé par
    /// les gabarits de CityGenerator.Plans et CityGenerator.Hospitals.
    /// </summary>
    internal sealed class PlanBuilder
    {
        public enum Facing { North, East, South, West }

        public const float Thickness = 0.15f;
        public const float DoorWidth = 1.35f;
        public const float DoorHeight = 2.55f;
        private const float SnapDistance = 1.3f;

        public readonly float Scale;
        public readonly Dictionary<string, (BoxMeshBuilder builder, Material material)> Parts =
            new Dictionary<string, (BoxMeshBuilder, Material)>();
        public readonly List<(Vector3 center, Vector3 size, Quaternion rotation)> Colliders =
            new List<(Vector3, Vector3, Quaternion)>();
        public readonly List<Vector3> Lights = new List<Vector3>();
        public readonly List<(string id, Vector3 position, float yaw, float scale, bool solid, float cull)> Props =
            new List<(string, Vector3, float, float, bool, float)>();

        private readonly List<(bool alongX, float at, float from, float to, float y, float height)> _walls =
            new List<(bool, float, float, float, float, float)>();

        public PlanBuilder(float scale = 1f) => Scale = scale;

        public BoxMeshBuilder P(string name) => Parts[name].builder;

        public void Register(string name, Material material) => Parts[name] = (new BoxMeshBuilder(), material);

        // --- Passage du plan au jeu ---

        public float S(float planValue) => planValue * Scale;

        public Vector3 Sc(Vector3 plan) => new Vector3(plan.x * Scale, plan.y, plan.z * Scale);

        public static float YawOf(Facing facing) => facing switch
        {
            Facing.North => 0f,
            Facing.East => 90f,
            Facing.South => 180f,
            _ => 270f,
        };

        /// <summary>Point du plan agrandi en gardant l'écart réel au mur le plus proche (par axe).</summary>
        public Vector3 Place(float x, float z, float y)
        {
            float sx = x * Scale, sz = z * Scale;
            float bestX = SnapDistance, bestZ = SnapDistance;
            foreach ((bool alongX, float at, float from, float to, float wallY, float height) in _walls)
            {
                if (y < wallY - 0.5f || y > wallY + height - 0.05f)
                    continue;
                if (alongX)
                {
                    if (x < from - 0.3f || x > to + 0.3f)
                        continue;
                    float d = z - at;
                    if (Mathf.Abs(d) < bestZ)
                    {
                        bestZ = Mathf.Abs(d);
                        sz = at * Scale + d;
                    }
                }
                else
                {
                    if (z < from - 0.3f || z > to + 0.3f)
                        continue;
                    float d = x - at;
                    if (Mathf.Abs(d) < bestX)
                    {
                        bestX = Mathf.Abs(d);
                        sx = at * Scale + d;
                    }
                }
            }
            return new Vector3(sx, y, sz);
        }

        // --- Primitives et objets Poly Haven ---

        public void WorldProp(string id, Vector3 position, float yaw, float scale = 1f, bool solid = false, float cull = 0.004f) =>
            Props.Add((id, position, yaw, scale, solid, cull));

        public void PlanProp(string id, float x, float z, float y, float yaw, float scale = 1f, bool solid = false, float cull = 0.004f) =>
            Props.Add((id, Place(x, z, y), yaw, scale, solid, cull));

        public void AreaProp(string id, float x, float z, float y, float yaw, float scale = 1f, bool solid = false, float cull = 0.004f) =>
            Props.Add((id, Sc(new Vector3(x, y, z)), yaw, scale, solid, cull));

        /// <summary>Pavé posé au sol (centre de la base), complet (6 faces), avec ou sans collider.</summary>
        public void WorldBox(string part, Vector3 baseCenter, Vector3 size, bool solid)
        {
            P(part).AddRotatedBox(baseCenter + new Vector3(0f, size.y * 0.5f, 0f), size, Quaternion.identity);
            if (solid)
                Colliders.Add((baseCenter + new Vector3(0f, size.y * 0.5f, 0f), size, Quaternion.identity));
        }

        public void WorldSolid(Vector3 center, Vector3 size) => Colliders.Add((center, size, Quaternion.identity));

        public void Box(string part, Vector3 planBaseCenter, Vector3 size, bool solid) =>
            WorldBox(part, Place(planBaseCenter.x, planBaseCenter.z, planBaseCenter.y), size, solid);

        public void Area(string part, Vector3 planBaseCenter, Vector3 planSize, bool solid = false) =>
            WorldBox(part, Sc(planBaseCenter), new Vector3(planSize.x * Scale, planSize.y, planSize.z * Scale), solid);

        public void AreaSolid(Vector3 planCenter, Vector3 planSize) =>
            WorldSolid(Sc(planCenter), new Vector3(planSize.x * Scale, planSize.y, planSize.z * Scale));

        /// <summary>Dalle horizontale (sol ou plafond) de x0…x1, z0…z1 (plan), dessus à la hauteur y.</summary>
        public void Slab(string part, float x0, float z0, float x1, float z1, float y, float thickness = 0.2f)
        {
            x0 *= Scale; x1 *= Scale; z0 *= Scale; z1 *= Scale;
            var center = new Vector3((x0 + x1) * 0.5f, y - thickness * 0.5f, (z0 + z1) * 0.5f);
            var size = new Vector3(x1 - x0, thickness, z1 - z0);
            P(part).AddRotatedBox(center, size, Quaternion.identity);
            Colliders.Add((center, size, Quaternion.identity));
        }

        public void Wall(string part, float x0, float z0, float x1, float z1, float y, float height, params float[] doors) =>
            WideWall(part, x0, z0, x1, z1, y, height, DoorWidth, doors);

        public void WideWall(string part, float x0, float z0, float x1, float z1, float y, float height, float doorWidth, params float[] doors)
        {
            bool alongX = Mathf.Abs(x1 - x0) >= Mathf.Abs(z1 - z0);
            _walls.Add(alongX
                ? (true, z0, Mathf.Min(x0, x1), Mathf.Max(x0, x1), y, height)
                : (false, x0, Mathf.Min(z0, z1), Mathf.Max(z0, z1), y, height));

            float length = (alongX ? Mathf.Abs(x1 - x0) : Mathf.Abs(z1 - z0)) * Scale;
            float start = (alongX ? Mathf.Min(x0, x1) : Mathf.Min(z0, z1)) * Scale;
            float fixedCoord = (alongX ? z0 : x0) * Scale;
            var cuts = new List<float>();
            foreach (float door in doors)
                cuts.Add(door * Scale);
            cuts.Sort();
            float cursor = 0f;
            void Piece(float from, float to, float bottom, float top)
            {
                if (to - from < 0.02f || top - bottom < 0.02f)
                    return;
                float mid = start + (from + to) * 0.5f;
                Vector3 center = alongX
                    ? new Vector3(mid, y + (bottom + top) * 0.5f, fixedCoord)
                    : new Vector3(fixedCoord, y + (bottom + top) * 0.5f, mid);
                Vector3 size = alongX
                    ? new Vector3(to - from, top - bottom, Thickness)
                    : new Vector3(Thickness, top - bottom, to - from);
                P(part).AddRotatedBox(center, size, Quaternion.identity);
                Colliders.Add((center, size, Quaternion.identity));
            }
            float cutH = Mathf.Min(DoorHeight + (doorWidth > DoorWidth + 0.05f ? 0.35f : 0f), height - 0.05f);
            bool hasFrame = Parts.ContainsKey("CadreFenetre");
            foreach (float door in cuts)
            {
                float a = Mathf.Clamp(door - doorWidth * 0.5f, 0f, length);
                float b = Mathf.Clamp(door + doorWidth * 0.5f, 0f, length);
                Piece(cursor, a, 0f, height);
                Piece(a, b, cutH, height);
                if (hasFrame && b - a >= 0.4f)
                {
                    const float frameW = 0.065f, frameD = Thickness + 0.045f;
                    BoxMeshBuilder frame = P("CadreFenetre");
                    foreach (float edge in new[] { a, b })
                    {
                        Vector3 jc = alongX
                            ? new Vector3(start + edge, y + cutH * 0.5f, fixedCoord)
                            : new Vector3(fixedCoord, y + cutH * 0.5f, start + edge);
                        frame.AddRotatedBox(jc, alongX ? new Vector3(frameW, cutH, frameD) : new Vector3(frameD, cutH, frameW), Quaternion.identity);
                    }
                    Vector3 hc = alongX
                        ? new Vector3(start + (a + b) * 0.5f, y + cutH + frameW * 0.5f, fixedCoord)
                        : new Vector3(fixedCoord, y + cutH + frameW * 0.5f, start + (a + b) * 0.5f);
                    frame.AddRotatedBox(hc, alongX ? new Vector3(b - a + frameW, frameW, frameD) : new Vector3(frameD, frameW, b - a + frameW), Quaternion.identity);
                }
                cursor = b;
            }
            Piece(cursor, length, 0f, height);
        }

        /// <summary>Fenêtre encadrée avec traverse centrale et vitre lumineuse (collée au mur).</summary>
        public void Window(Vector3 planCenter, Facing wall, float width, float height = 1.15f)
        {
            float floor = planCenter.y - 1.4f;
            Vector3 c = Place(planCenter.x, planCenter.z, floor);
            c.y = planCenter.y;
            width *= Scale;
            bool ns = wall == Facing.North || wall == Facing.South;
            Vector3 glass = ns ? new Vector3(width, height, 0.03f) : new Vector3(0.03f, height, width);
            P("Lumiere").AddRotatedBox(c, glass, Quaternion.identity);

            // Encadrement en bois/alu et meneau central.
            const float frame = 0.06f, depth = 0.07f;
            P("CadreFenetre").AddRotatedBox(c + Vector3.up * (height * 0.5f + frame * 0.5f),
                ns ? new Vector3(width + frame * 2f, frame, depth) : new Vector3(depth, frame, width + frame * 2f), Quaternion.identity);
            P("CadreFenetre").AddRotatedBox(c - Vector3.up * (height * 0.5f + frame * 0.5f),
                ns ? new Vector3(width + frame * 2f, frame, depth + 0.04f) : new Vector3(depth + 0.04f, frame, width + frame * 2f), Quaternion.identity);
            Vector3 sideOff = ns ? new Vector3(width * 0.5f + frame * 0.5f, 0f, 0f) : new Vector3(0f, 0f, width * 0.5f + frame * 0.5f);
            Vector3 jamb = ns ? new Vector3(frame, height, depth) : new Vector3(depth, height, frame);
            P("CadreFenetre").AddRotatedBox(c + sideOff, jamb, Quaternion.identity);
            P("CadreFenetre").AddRotatedBox(c - sideOff, jamb, Quaternion.identity);
            if (width >= 1.2f)
                P("CadreFenetre").AddRotatedBox(c, ns ? new Vector3(frame * 0.7f, height, depth * 0.7f) : new Vector3(depth * 0.7f, height, frame * 0.7f), Quaternion.identity);
        }

        public void Light(Vector3 planPosition)
        {
            Vector3 position = Sc(planPosition);
            P("Lumiere").AddRotatedBox(position - new Vector3(0f, 0.02f, 0f), new Vector3(0.50f, 0.03f, 0.50f), Quaternion.identity);
            Lights.Add(position - new Vector3(0f, 0.40f, 0f));
        }

        /// <summary>Plafonnier LED suspendu économe pour mobile (~28 triangles fusionnés, zéro objet Poly Haven lourd).</summary>
        public void CeilingLuminaire(float x, float z, float ceilingY, bool alongX = true)
        {
            Vector3 pos = Sc(new Vector3(x, ceilingY, z));
            const float drop = 0.22f;
            float panelY = ceilingY - drop;
            Vector3 bodySize = alongX ? new Vector3(1.55f, 0.055f, 0.38f) : new Vector3(0.38f, 0.055f, 1.55f);
            Vector3 lensSize = alongX ? new Vector3(1.45f, 0.02f, 0.30f) : new Vector3(0.30f, 0.02f, 1.45f);
            P("Metal").AddRotatedBox(new Vector3(pos.x, panelY, pos.z), bodySize, Quaternion.identity);
            P("Lumiere").AddRotatedBox(new Vector3(pos.x, panelY - 0.03f, pos.z), lensSize, Quaternion.identity);
            foreach (float d in new[] { -0.52f, 0.52f })
            {
                Vector3 rodPos = new Vector3(pos.x + (alongX ? d : 0f), ceilingY - drop * 0.5f, pos.z + (alongX ? 0f : d));
                P("Metal").AddRotatedBox(rodPos, new Vector3(0.025f, drop, 0.025f), Quaternion.identity);
            }
        }

        /// <summary>
        /// Escalier ajouré : limons inclinés, marches flottantes en bois, balustres métalliques et main
        /// courante côté couloir ; rampe inclinée invisible pour marcher.
        /// </summary>
        public void Stairs(float x0, float x1, float zBottom, float zTop, float yBottom, float yTop)
        {
            x0 *= Scale; x1 *= Scale; zBottom *= Scale; zTop *= Scale;
            int steps = Mathf.Max(8, Mathf.RoundToInt((yTop - yBottom) / 0.18f));
            float run = (zTop - zBottom) / steps, rise = (yTop - yBottom) / steps;
            float width = x1 - x0;
            for (int i = 0; i < steps; i++)
            {
                float z = zBottom + run * (i + 0.5f);
                float top = yBottom + rise * (i + 1);
                // Marche en bois (épaisseur 7 cm, sans gros bloc massif dessous).
                P("Bois").AddRotatedBox(new Vector3((x0 + x1) * 0.5f, top - 0.035f, z),
                    new Vector3(width, 0.07f, run + 0.04f), Quaternion.identity);
                // Balustre vertical côté ouvert (est), tous les 2 marches.
                if (i % 2 == 0)
                    P("Metal").AddRotatedBox(new Vector3(x1 - 0.04f, top + 0.45f, z), new Vector3(0.035f, 0.9f, 0.035f), Quaternion.identity);
            }
            float length = Mathf.Sqrt((zTop - zBottom) * (zTop - zBottom) + (yTop - yBottom) * (yTop - yBottom));
            float angle = Mathf.Atan2(yTop - yBottom, zTop - zBottom) * Mathf.Rad2Deg;
            var center = new Vector3((x0 + x1) * 0.5f, (yBottom + yTop) * 0.5f, (zBottom + zTop) * 0.5f);
            // Deux limons latéraux sous les marches.
            foreach (float sx in new[] { x0 + 0.15f, x1 - 0.15f })
            {
                P("CadreFenetre").AddRotatedBox(new Vector3(sx, (yBottom + yTop) * 0.5f - 0.08f, (zBottom + zTop) * 0.5f),
                    new Vector3(0.08f, 0.14f, length), Quaternion.Euler(-angle, 0f, 0f));
            }
            Colliders.Add((center, new Vector3(width, 0.1f, length), Quaternion.Euler(-angle, 0f, 0f)));
            // Main courante et poteaux d'extrémité.
            P("Metal").AddRotatedBox(new Vector3(x1 - 0.04f, (yBottom + yTop) * 0.5f + 0.90f, (zBottom + zTop) * 0.5f),
                new Vector3(0.05f, 0.05f, length), Quaternion.Euler(-angle, 0f, 0f));
        }

        // --- Mobilier (modèles 3D Poly Haven + détails architecturaux ; position du plan) ---

        private static bool Rotated(Facing facing) => facing == Facing.East || facing == Facing.West;

        private static Vector3 Dir(Facing facing) => facing switch
        {
            Facing.North => Vector3.forward,
            Facing.South => Vector3.back,
            Facing.East => Vector3.right,
            _ => Vector3.left,
        };

        /// <summary>Lit deux places (modèle 3D GothicBed_01 + table de chevet) ; <paramref name="head"/> côté mur.</summary>
        public void Bed(float x, float z, float y, Facing head, bool withNightstand = true)
        {
            bool rot = Rotated(head);
            Vector3 size = rot ? new Vector3(2.0f, 0.65f, 1.5f) : new Vector3(1.5f, 0.65f, 2.0f);
            Vector3 c = Place(x, z, y);
            WorldSolid(c + new Vector3(0f, 0.35f, 0f), new Vector3(size.x, 0.7f, size.z));
            // En GothicBed_01, le pied/tête s'aligne sur Z (1,49 × 2,04). YawOf(Opposite) place la tête côté mur.
            WorldProp("GothicBed_01", c, YawOf(Opposite(head)), 0.98f, false, 0.004f);
            if (withNightstand)
            {
                Vector3 side = rot ? Vector3.forward : Vector3.right;
                Vector3 headEdge = c + Dir(head) * 0.72f + side * 1.05f;
                WorldProp("painted_wooden_nightstand", headEdge, YawOf(Opposite(head)), 0.90f, false, 0.004f);
            }
        }

        private static Facing Opposite(Facing f) => f switch
        {
            Facing.North => Facing.South,
            Facing.South => Facing.North,
            Facing.East => Facing.West,
            _ => Facing.East,
        };

        /// <summary>Lit d'hôpital (1 place, cadre métallique, barrières et potence à perfusion) à une position du jeu.</summary>
        public void HospitalBed(Vector3 c, Facing head, bool withIvPole = true)
        {
            bool rot = Rotated(head);
            Vector3 size = rot ? new Vector3(2.05f, 0.5f, 1.02f) : new Vector3(1.02f, 0.5f, 2.05f);
            WorldBox("Metal", c, size, false);
            WorldSolid(c + new Vector3(0f, 0.3f, 0f), new Vector3(size.x, 0.65f, size.z));
            WorldBox("Draps", c + new Vector3(0f, 0.5f, 0f), size - new Vector3(0.08f, 0.36f, 0.08f), false);
            WorldBox("Couverture", c + new Vector3(0f, 0.64f, 0f) - Dir(head) * 0.3f, rot ? new Vector3(1.22f, 0.05f, 0.96f) : new Vector3(0.96f, 0.05f, 1.22f), false);
            Vector3 headPos = c + Dir(head) * ((rot ? size.x : size.z) * 0.5f - 0.22f);
            WorldBox("Draps", headPos + new Vector3(0f, 0.64f, 0f), rot ? new Vector3(0.32f, 0.14f, 0.64f) : new Vector3(0.64f, 0.14f, 0.32f), false);
            WorldBox("Metal", c + Dir(head) * ((rot ? size.x : size.z) * 0.5f), rot ? new Vector3(0.06f, 1.08f, 1.02f) : new Vector3(1.02f, 1.08f, 0.06f), false);
            WorldBox("Metal", c - Dir(head) * ((rot ? size.x : size.z) * 0.5f), rot ? new Vector3(0.05f, 0.85f, 1.02f) : new Vector3(1.02f, 0.85f, 0.05f), false);
            Vector3 railSide = rot ? Vector3.forward : Vector3.right;
            foreach (float side in new[] { -0.5f, 0.5f })
            {
                Vector3 railPos = c + railSide * side + new Vector3(0f, 0.68f, 0f);
                WorldBox("Metal", railPos, rot ? new Vector3(1.05f, 0.14f, 0.04f) : new Vector3(0.04f, 0.14f, 1.05f), false);
            }
            if (withIvPole)
            {
                Vector3 ivBase = c + Dir(head) * 0.72f + railSide * 0.68f;
                WorldBox("Metal", ivBase, new Vector3(0.32f, 0.04f, 0.32f), false);
                WorldBox("Metal", ivBase, new Vector3(0.035f, 1.95f, 0.035f), false);
                WorldBox("Draps", ivBase + new Vector3(0f, 1.68f, 0f), new Vector3(0.11f, 0.20f, 0.06f), false);
            }
        }

        public void Wardrobe(float x, float z, float y, float width, Facing front)
        {
            bool rot = Rotated(front);
            Vector3 c = Place(x, z, y);
            Vector3 size = rot ? new Vector3(0.58f, 2.0f, width) : new Vector3(width, 2.0f, 0.58f);
            WorldBox("Bois", c, size, true);
            // Poignées doubles de penderie côté façade.
            Vector3 outDir = Dir(front);
            Vector3 across = rot ? Vector3.forward : Vector3.right;
            foreach (float s in new[] { -0.08f, 0.08f })
                WorldBox("Metal", c + outDir * 0.30f + across * s + Vector3.up * 0.95f, new Vector3(0.03f, 0.28f, 0.03f), false);
        }

        /// <summary>Plan de travail avec cuisinière 3D, micro-ondes 3D, évier et robinet métallique.</summary>
        public void Counter(float x, float z, float y, float length, Facing front, bool hob, bool sink)
        {
            bool rot = Rotated(front);
            Vector3 c = Place(x, z, y);
            Vector3 along = rot ? Vector3.forward : Vector3.right;
            float counterLen = hob ? Mathf.Max(1.0f, length - 0.55f) : length;
            Vector3 counterCenter = hob ? c - along * 0.28f : c;

            Vector3 size = rot ? new Vector3(0.62f, 0.88f, counterLen) : new Vector3(counterLen, 0.88f, 0.62f);
            WorldBox("Bois", counterCenter, size, true);
            WorldBox("Metal", counterCenter + new Vector3(0f, 0.88f, 0f), size + new Vector3(0.03f, -0.84f, 0.03f), false);

            if (sink)
            {
                Vector3 sinkPos = counterCenter - along * (counterLen * 0.22f);
                WorldBox("Metal", sinkPos + new Vector3(0f, 0.92f, 0f), new Vector3(0.46f, 0.03f, 0.40f), false);
                WorldBox("Metal", sinkPos - Dir(front) * 0.18f + new Vector3(0f, 0.95f, 0f), new Vector3(0.04f, 0.24f, 0.04f), false);
                WorldBox("Metal", sinkPos - Dir(front) * 0.10f + new Vector3(0f, 1.16f, 0f),
                    rot ? new Vector3(0.16f, 0.03f, 0.03f) : new Vector3(0.03f, 0.03f, 0.16f), false);
            }
            if (hob)
            {
                Vector3 stovePos = c + along * (length * 0.5f - 0.26f);
                WorldProp("electric_stove", stovePos, YawOf(front), 1.0f, false, 0.004f);
                WorldSolid(stovePos + new Vector3(0f, 0.45f, 0f), new Vector3(0.55f, 0.9f, 0.65f));
                WorldProp("vintage_microwave", counterCenter + along * (counterLen * 0.25f) + new Vector3(0f, 0.92f, 0f),
                    YawOf(front), 0.48f, false, 0.004f);
            }
        }

        public void Fridge(float x, float z, float y, Facing front = Facing.South)
        {
            Vector3 c = Place(x, z, y);
            WorldBox("Metal", c, new Vector3(0.68f, 1.82f, 0.68f), true);
            bool rot = Rotated(front);
            Vector3 f = Dir(front);
            // Séparation congélateur et poignées.
            WorldBox("Sombre", c + f * 0.345f + Vector3.up * 1.25f, rot ? new Vector3(0.02f, 0.03f, 0.64f) : new Vector3(0.64f, 0.03f, 0.02f), false);
            Vector3 side = rot ? Vector3.forward : Vector3.right;
            WorldBox("Sanitaire", c + f * 0.36f + side * 0.22f + Vector3.up * 0.6f, new Vector3(0.03f, 0.45f, 0.03f), false);
            WorldBox("Sanitaire", c + f * 0.36f + side * 0.22f + Vector3.up * 1.38f, new Vector3(0.03f, 0.28f, 0.03f), false);
        }

        public void Toilet(float x, float z, float y)
        {
            Vector3 c = Place(x, z, y);
            WorldBox("Sanitaire", c, new Vector3(0.4f, 0.42f, 0.6f), true);
            WorldBox("Sanitaire", c + new Vector3(0f, 0.42f, 0.25f), new Vector3(0.4f, 0.35f, 0.18f), false);
        }

        public void Washbasin(float x, float z, float y, Facing wallBack = Facing.North)
        {
            Vector3 c = Place(x, z, y);
            WorldBox("Sanitaire", c + new Vector3(0f, 0.75f, 0f), new Vector3(0.55f, 0.14f, 0.42f), false);
            WorldBox("Sanitaire", c, new Vector3(0.18f, 0.75f, 0.18f), false);
            WorldBox("Metal", c + new Vector3(0f, 0.89f, 0f) - Dir(Opposite(wallBack)) * 0.12f, new Vector3(0.04f, 0.14f, 0.04f), false);
            // Miroir mural au-dessus du lavabo.
            WorldProp("ornate_mirror_01", c + new Vector3(0f, 1.52f, 0f) + Dir(wallBack) * 0.18f, YawOf(Opposite(wallBack)), 1.05f, false, 0.004f);
        }

        public void Shower(float x, float z, float y, float size = 0.9f)
        {
            Vector3 c = Place(x, z, y);
            WorldBox("Faience", c, new Vector3(size, 0.06f, size), false);
            WorldBox("Metal", c + new Vector3(0f, 1.95f, 0f), new Vector3(0.2f, 0.04f, 0.2f), false);
        }

        public void Bathtub(float x, float z, float y, Facing along)
        {
            bool rot = Rotated(along);
            WorldBox("Sanitaire", Place(x, z, y), rot ? new Vector3(1.7f, 0.55f, 0.75f) : new Vector3(0.75f, 0.55f, 1.7f), true);
        }

        /// <summary>Canapé 3D Poly Haven (<paramref name="facing"/> : direction du regard assis).</summary>
        public void Sofa(float x, float z, float y, float width, Facing facing, bool modern = false)
        {
            bool rot = Rotated(facing);
            Vector3 c = Place(x, z, y);
            Vector3 seat = rot ? new Vector3(0.85f, 0.85f, width) : new Vector3(width, 0.85f, 0.85f);
            WorldSolid(c + new Vector3(0f, 0.42f, 0f), seat);
            if (modern)
            {
                float s = Mathf.Clamp(width / 1.81f, 0.85f, 1.35f);
                WorldProp("sofa_02", c, YawOf(facing), s, false, 0.004f);
            }
            else
            {
                float s = Mathf.Clamp(width / 1.57f, 0.9f, 1.35f);
                WorldProp("Sofa_01", c, YawOf(facing), s, false, 0.004f);
            }
        }

        /// <summary>Meuble télé 3D + écran plat (ou téléviseur 3D en studio).</summary>
        public void Tv(float x, float z, float y, Facing facing, bool flatscreen = true)
        {
            bool rot = Rotated(facing);
            Vector3 c = Place(x, z, y);
            WorldSolid(c + new Vector3(0f, 0.45f, 0f), rot ? new Vector3(0.55f, 0.9f, 1.4f) : new Vector3(1.4f, 0.9f, 0.55f));
            WorldProp("ClassicConsole_01", c, YawOf(facing), 0.82f, false, 0.004f);
            if (flatscreen)
            {
                Vector3 screenBase = c + new Vector3(0f, 0.78f, 0f);
                WorldBox("Metal", screenBase, rot ? new Vector3(0.22f, 0.10f, 0.36f) : new Vector3(0.36f, 0.10f, 0.22f), false);
                WorldBox("Sombre", screenBase + new Vector3(0f, 0.10f, 0f) - Dir(facing) * 0.02f,
                    rot ? new Vector3(0.05f, 0.68f, 1.18f) : new Vector3(1.18f, 0.68f, 0.05f), false);
            }
            else
            {
                WorldProp("Television_01", c + new Vector3(0f, 0.78f, 0f), YawOf(facing), 1.15f, false, 0.004f);
            }
        }

        /// <summary>Table à manger 3D + chaises en bois Poly Haven.</summary>
        public void Table(float x, float z, float y, float width, float depth, int chairs)
        {
            Vector3 c = Place(x, z, y);
            WorldSolid(c + new Vector3(0f, 0.37f, 0f), new Vector3(width, 0.75f, depth));
            bool wideX = width >= depth;
            if (chairs <= 2 && width <= 1.3f)
            {
                WorldProp("round_wooden_table_01", c, 0f, 0.74f, false, 0.004f);
            }
            else
            {
                float s = Mathf.Clamp(Mathf.Max(width, depth) / 2.25f, 0.68f, 0.92f);
                WorldProp("dining_table", c, wideX ? 0f : 90f, s, false, 0.004f);
            }

            int perSide = Mathf.Max(1, chairs / 2);
            for (int i = 0; i < chairs; i++)
            {
                int side = i % 2 == 0 ? 1 : -1;
                float span = Mathf.Max(0.4f, (wideX ? width : depth) - 0.55f);
                float t = perSide == 1 ? 0f : ((i / 2) / (float)(perSide - 1) - 0.5f) * span;
                Vector3 pos = wideX
                    ? c + new Vector3(t, 0f, side * (depth * 0.5f + 0.32f))
                    : c + new Vector3(side * (width * 0.5f + 0.32f), 0f, t);
                float yaw = wideX ? (side > 0 ? 180f : 0f) : (side > 0 ? 270f : 90f);
                WorldProp("painted_wooden_chair_01", pos, yaw, 0.95f, false, 0.004f);
            }
        }

        /// <summary>Rangée de chaises d'attente (chaises monoblocs 3D le long de x ou de z).</summary>
        public void ChairRow(float x, float z, float y, int count, bool alongX, Facing facing = Facing.North)
        {
            Vector3 c = Place(x, z, y);
            Vector3 step = alongX ? new Vector3(0.6f, 0f, 0f) : new Vector3(0f, 0f, 0.6f);
            Vector3 first = c - step * ((count - 1) * 0.5f);
            float yaw = YawOf(facing);
            for (int i = 0; i < count; i++)
                WorldProp("plastic_monobloc_chair_01", first + step * i, yaw, 0.78f, false, 0.004f);
            WorldSolid(c + new Vector3(0f, 0.25f, 0f), alongX ? new Vector3(count * 0.6f, 0.5f, 0.5f) : new Vector3(0.5f, 0.5f, count * 0.6f));
        }

        public void CoffeeTable(float x, float z, float y, bool rotated = false) =>
            PlanProp("modern_coffee_table_01", x, z, y, rotated ? 90f : 0f, 1.0f, false, 0.004f);

        public void Rug(float x, float z, float y, float width, float depth) =>
            WorldBox("Pagne", Place(x, z, y), new Vector3(width, 0.014f, depth), false);
    }
}
