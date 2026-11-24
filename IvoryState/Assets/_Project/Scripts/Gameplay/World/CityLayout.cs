using System;
using System.Collections.Generic;
using UnityEngine;

namespace IvoryState.Gameplay.World
{
    /// <summary>
    /// Disposition de la ville (Resources/CityLayout.json), calculée hors de Unity par
    /// tools/citylayout/build_layout.py d'après le tracé d'Abidjan (OpenStreetMap, ODbL) : grands axes et
    /// ponts, trottoirs, bâtiments le long des rues, lieux particuliers et monuments à leur place réelle,
    /// repères sur les trottoirs, eau (lagune et océan), forêts, aéroport et relief (SRTM).
    /// <para>
    /// Partagée par l'éditeur (génération de la ville) et le jeu (serveur et client) : les lieux qui ont
    /// une position connue du code (entrées des logements, hôpitaux, boutiques, guichets, point
    /// d'apparition) sont pris parmi les repères par <see cref="Spot"/>, de façon déterministe, et tombent
    /// donc au même endroit des deux côtés.
    /// </para>
    /// <para>Angles : en degrés, sens trigonométrique depuis +x (comme le script) ; <see cref="UnityYaw"/> convertit.</para>
    /// </summary>
    public static class CityLayout
    {
        [Serializable] public class DistrictData { public string name; public float x, z, radius, yaw; }
        /// <summary>
        /// Rue : classe (0 voie rapide … 3 rue de quartier), nom OSM, largeur, polyligne <c>p</c>, ponts <c>b</c>
        /// (segments a-b), impasses <c>de</c> (début, fin : 1 = cul-de-sac), anneau de rond-point <c>ring</c>.
        /// </summary>
        [Serializable] public class RoadData { public int cls; public string name; public float width; public float[] p; public float[] b; public int[] de; public bool ring; }
        /// <summary>
        /// Carrefour d'au moins trois branches : rayon de la chaussée commune <c>r</c>, feux <c>s</c> (1 = oui),
        /// sur un rond-point <c>ring</c>, branches <c>a</c> = (dx, dz, largeur, classe) à la suite.
        /// </summary>
        [Serializable] public class JunctionData { public float x, z, r; public int s, ring; public float[] a; }
        /// <summary>Rond-point : centre et rayon de l'îlot central (l'anneau est une rue <c>ring</c>).</summary>
        [Serializable] public class RoundaboutData { public float x, z, r; public string name; }
        [Serializable] public class SidewalkData { public float[] p; }
        [Serializable] public class BlockData { public float x, z, yaw, size; public string district, special, name; }
        [Serializable] public class BuildingData { public float x, z, yaw, w, d, r, fx, fz; public string district; public int row; }
        [Serializable] public class AnchorData { public float x, z, fx, fz; public string district; public int road; }
        [Serializable] public class WaterMaskData { public float x, z, cell; public int nx, nz; public int[] runs; }
        /// <summary>Monument modélisé dans Blender : emprise w x d, façade tournée vers (fx, fz).</summary>
        [Serializable] public class LandmarkData { public string key, name, osm, district; public float x, z, yaw, w, d, fx, fz, offset; }
        [Serializable] public class PolyData { public string name; public float width; public float[] p; }
        [Serializable] public class AirportData { public PolyData[] fence, runways, taxiways, aprons, terminals; }
        /// <summary>Altitudes du sol en décimètres, grille régulière (cases de <c>cell</c> m).</summary>
        [Serializable] public class HeightData { public float x, z, cell; public int nx, nz; public int[] h; }

        [Serializable]
        public class Data
        {
            public string source;
            public float[] bounds;
            public DistrictData[] districts;
            public RoadData[] roads;
            public JunctionData[] junctions;
            public RoundaboutData[] roundabouts;
            public float sidewalkWidth;
            public SidewalkData[] sidewalks;
            public BlockData[] blocks;
            public LandmarkData[] landmarks;
            /// <summary>Parkings extérieurs des bâtiments utilisables (clé = monument).</summary>
            public LandmarkData[] parkings;
            public BuildingData[] buildings;
            public AnchorData[] anchors;
            public float[] trees;
            public AirportData airport;
            public WaterMaskData waterMask;
            public WaterMaskData forestMask;
            public HeightData height;
        }

        public const float SidewalkHeight = 0.15f;

        private static bool s_planarSpots;

        /// <summary>
        /// Éditeur seulement : pendant la génération, la ville est construite à plat puis posée sur le relief
        /// en une passe ; les repères (<see cref="Spot"/>, <see cref="PlazaCenter"/>) restent donc à plat.
        /// </summary>
        public static bool PlanarSpots
        {
            get => s_planarSpots;
            set
            {
                if (s_planarSpots == value)
                    return;
                s_planarSpots = value;
                Version++;
            }
        }

        /// <summary>Change quand les positions des repères changent : les catalogues qui les gardent se recalculent.</summary>
        public static int Version { get; private set; }

        private static float SpotGround(float x, float z) => s_planarSpots ? 0f : GroundHeight(x, z);

        private static Data s_data;
        private static bool[] s_water;
        private static bool[] s_forest;
        private static Dictionary<string, List<AnchorData>> s_ranked;

        public static Data Layout
        {
            get
            {
                if (s_data == null)
                    Load();
                return s_data;
            }
        }

        private static void Load()
        {
            TextAsset asset = Resources.Load<TextAsset>("CityLayout");
            s_data = asset != null ? JsonUtility.FromJson<Data>(asset.text) : new Data();
            s_data.districts ??= new DistrictData[0];
            s_data.roads ??= new RoadData[0];
            s_data.junctions ??= new JunctionData[0];
            s_data.roundabouts ??= new RoundaboutData[0];
            s_data.sidewalks ??= new SidewalkData[0];
            s_data.blocks ??= new BlockData[0];
            s_data.buildings ??= new BuildingData[0];
            s_data.anchors ??= new AnchorData[0];
            s_data.landmarks ??= new LandmarkData[0];
            s_data.parkings ??= new LandmarkData[0];
            s_data.trees ??= new float[0];
            s_data.bounds ??= new[] { -500f, -500f, 500f, 500f };

            s_water = Decode(s_data.waterMask);
            s_forest = Decode(s_data.forestMask);

            // Repères classés par quartier : les grands axes d'abord, puis du centre du quartier vers l'extérieur.
            s_ranked = new Dictionary<string, List<AnchorData>>();
            foreach (DistrictData district in s_data.districts)
            {
                var list = new List<AnchorData>();
                foreach (AnchorData anchor in s_data.anchors)
                {
                    if (anchor.district == district.name)
                        list.Add(anchor);
                }
                list.Sort((a, b) =>
                {
                    int major = (a.road <= 2 ? 0 : 1).CompareTo(b.road <= 2 ? 0 : 1);
                    if (major != 0)
                        return major;
                    float da = (a.x - district.x) * (a.x - district.x) + (a.z - district.z) * (a.z - district.z);
                    float db = (b.x - district.x) * (b.x - district.x) + (b.z - district.z) * (b.z - district.z);
                    return da.CompareTo(db);
                });
                s_ranked[district.name] = list;
            }
        }

        private static bool[] Decode(WaterMaskData mask)
        {
            var cells = new bool[mask != null ? mask.nx * mask.nz : 0];
            if (mask?.runs == null)
                return cells;
            for (int i = 0; i + 2 < mask.runs.Length; i += 3)
            {
                int row = mask.runs[i], start = mask.runs[i + 1], length = mask.runs[i + 2];
                for (int c = start; c < start + length && c < mask.nx; c++)
                    cells[row * mask.nx + c] = true;
            }
            return cells;
        }

        private static bool Sample(WaterMaskData mask, bool[] cells, float x, float z)
        {
            if (mask == null || cells == null || cells.Length == 0)
                return false;
            int i = Mathf.FloorToInt((x - mask.x) / mask.cell), j = Mathf.FloorToInt((z - mask.z) / mask.cell);
            return i >= 0 && j >= 0 && i < mask.nx && j < mask.nz && cells[j * mask.nx + i];
        }

        /// <summary>
        /// Altitude du sol (m) : relief SRTM lissé, nul sur l'eau. Juste au-delà de la carte (abords, 3,5 km),
        /// l'altitude du bord continue ; plus loin (intérieurs, lointains), 0.
        /// Les trottoirs sont <see cref="SidewalkHeight"/> plus haut, les ponts suivent leur propre profil.
        /// </summary>
        public static float GroundHeight(float x, float z)
        {
            HeightData h = Layout.height;
            if (h?.h == null || h.h.Length == 0)
                return 0f;
            float fx = (x - h.x) / h.cell, fz = (z - h.z) / h.cell;
            const float margin = 3500f;
            if (fx * h.cell < -margin || fz * h.cell < -margin || (fx - h.nx + 1) * h.cell > margin || (fz - h.nz + 1) * h.cell > margin)
                return 0f;
            fx = Mathf.Clamp(fx, 0f, h.nx - 1);
            fz = Mathf.Clamp(fz, 0f, h.nz - 1);
            int i = Mathf.Min((int)fx, h.nx - 2), j = Mathf.Min((int)fz, h.nz - 2);
            float tx = fx - i, tz = fz - j;
            int row = j * h.nx + i;
            float a = h.h[row], b = h.h[row + 1], c = h.h[row + h.nx], d = h.h[row + h.nx + 1];
            return ((a * (1f - tx) + b * tx) * (1f - tz) + (c * (1f - tx) + d * tx) * tz) * 0.1f;
        }

        /// <summary>Point posé au sol (même x, z ; y = altitude du sol + <paramref name="lift"/>).</summary>
        public static Vector3 OnGround(Vector3 p, float lift = 0f) => new Vector3(p.x, GroundHeight(p.x, p.z) + lift, p.z);

        /// <summary>Vrai dans une forêt (parc national du Banco, forêts classées).</summary>
        public static bool IsForest(float x, float z) => Sample(Layout.forestMask, s_forest, x, z);

        public static LandmarkData Landmark(string key)
        {
            foreach (LandmarkData landmark in Layout.landmarks)
            {
                if (landmark.key == key)
                    return landmark;
            }
            return null;
        }

        /// <summary>Angle Unity (autour de +y, sens horaire depuis +z) correspondant à un angle du plan.</summary>
        public static float UnityYaw(float planYaw) => -planYaw;

        public static Rect Bounds
        {
            get
            {
                float[] b = Layout.bounds;
                return Rect.MinMaxRect(b[0], b[1], b[2], b[3]);
            }
        }

        /// <summary>Vrai sur la lagune (hors des ponts).</summary>
        public static bool IsWater(float x, float z) => Sample(Layout.waterMask, s_water, x, z);

        public static BlockData Special(string name)
        {
            foreach (BlockData block in Layout.blocks)
            {
                if (block.name == name)
                    return block;
            }
            return null;
        }

        /// <summary>Centre de la place du Plateau (point d'apparition), au niveau du trottoir.</summary>
        public static Vector3 PlazaCenter
        {
            get
            {
                foreach (BlockData block in Layout.blocks)
                {
                    if (block.special == "Plaza")
                        return new Vector3(block.x, SpotGround(block.x, block.z) + SidewalkHeight, block.z);
                }
                return new Vector3(0f, SpotGround(0f, 0f) + SidewalkHeight, 0f);
            }
        }

        public struct Place
        {
            /// <summary>Sur le trottoir, devant une façade.</summary>
            public Vector3 Position;
            /// <summary>Vers la chaussée (horizontal, normé).</summary>
            public Vector3 ToRoad;
            /// <summary>Le long du trottoir (horizontal, normé).</summary>
            public Vector3 Along;
        }

        /// <summary>
        /// N-ième repère de trottoir d'un quartier (grands axes et centre d'abord). Chaque usage du code
        /// prend son propre rang (voir <see cref="CityPlaces"/>) pour que deux lieux ne se superposent pas.
        /// </summary>
        public static Place Spot(string district, int rank)
        {
            _ = Layout;
            if (!s_ranked.TryGetValue(district, out List<AnchorData> list) || list.Count == 0)
                return new Place { Position = PlazaCenter, ToRoad = Vector3.forward, Along = Vector3.right };
            AnchorData a = list[Mathf.Clamp(rank, 0, list.Count - 1) % list.Count];
            var toRoad = new Vector3(a.fx, 0f, a.fz).normalized;
            return new Place
            {
                Position = new Vector3(a.x, SpotGround(a.x, a.z) + SidewalkHeight, a.z),
                ToRoad = toRoad,
                Along = Vector3.Cross(Vector3.up, toRoad),
            };
        }

        /// <summary>
        /// Devant la façade d'un monument (au bord de son emprise, côté rue, <paramref name="outside"/> m au-delà).
        /// </summary>
        public static bool TryLandmarkFront(string key, float outside, out Place place)
        {
            LandmarkData landmark = Landmark(key);
            if (landmark == null)
            {
                place = default;
                return false;
            }
            var toRoad = new Vector3(landmark.fx, 0f, landmark.fz).normalized;
            Vector3 p = new Vector3(landmark.x, 0f, landmark.z) + toRoad * (landmark.d * 0.5f + outside);
            place = new Place
            {
                Position = new Vector3(p.x, SpotGround(p.x, p.z) + SidewalkHeight, p.z),
                ToRoad = toRoad,
                Along = Vector3.Cross(Vector3.up, toRoad),
            };
            return true;
        }

        public static int SpotCount(string district)
        {
            _ = Layout;
            return s_ranked.TryGetValue(district, out List<AnchorData> list) ? list.Count : 0;
        }
    }

    /// <summary>
    /// Rangs des repères de trottoir réservés aux lieux connus du code (un rang par quartier ne sert
    /// qu'une fois). Les rangs libres servent aux points ordinaires (livraisons, étals…).
    /// </summary>
    public static class CityPlaces
    {
        // Logements
        public static CityLayout.Place Residence => CityLayout.Spot("Treichville", 0);

        // Hôpitaux (CHU)
        public static CityLayout.Place HospitalTreichville => CityLayout.Spot("Treichville", 6);
        /// <summary>CHU de Cocody : devant le bâtiment modélisé, à sa vraie place (repli : repère de trottoir).</summary>
        public static CityLayout.Place HospitalCocody =>
            CityLayout.TryLandmarkFront("PF_Hopital_CHU", 2.5f, out CityLayout.Place front) ? front : CityLayout.Spot("Cocody", 4);
        public static CityLayout.Place HospitalYopougon => CityLayout.Spot("Yopougon", 4);

        /// <summary>Rangs à partir desquels les points ordinaires peuvent se servir (au-delà des lieux réservés).</summary>
        public const int FirstFreeRank = 24;
    }
}
