using System.Collections.Generic;
using IvoryState.Gameplay.Interaction;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Génère la ville en « greybox » : volumes simples, à remplacer progressivement par les vrais modèles.
    /// <para>
    /// Plan inspiré de la géographie d'Abidjan, sans la copier : une lagune traverse la ville d'est
    /// en ouest. Au nord : le Plateau (tours d'affaires) au centre, Adjamé (marchés) au-dessus,
    /// Cocody (villas) à l'est et Yopougon (quartier populaire) à l'ouest. Au sud : Treichville et
    /// Marcory. Deux ponts relient les rives. La place centrale (point d'apparition) est au Plateau,
    /// au bord de la lagune.
    /// </para>
    /// <para>
    /// Grille de 9 × 9 cellules de 76 m (bloc de 64 m + rue de 12 m), soit environ 690 m de côté.
    /// La génération est déterministe (graine fixe) : client et serveur ont exactement la même ville,
    /// donc les mêmes identifiants de points d'interaction.
    /// </para>
    /// <para>
    /// Optimisations mobiles : un maillage fusionné par bloc et par matériau, un BoxCollider par
    /// bâtiment (pas de MeshCollider) et des maillages enregistrés dans un seul asset.
    /// </para>
    /// </summary>
    internal static partial class CityGenerator
    {
        public const string MeshAssetPath = "Assets/_Project/Art/Generated/CityMeshes.asset";
        public const string NavMeshAssetPath = "Assets/_Project/Art/Generated/CityNavMesh.asset";
        private const string MaterialFolder = "Assets/_Project/Art/Materials";

        private const int GridRadius = 4;          // cellules de -4 à +4
        private const float BlockSize = 64f;
        private const float RoadWidth = 12f;
        private const float CellSize = BlockSize + RoadWidth;
        private const float SidewalkWidth = 3f;
        private const float SidewalkHeight = 0.15f; // franchissable (step offset 0,35 m)
        private const int LagoonRow = -1;
        // Ponts sur les rues nord-sud à l'ouest de ces colonnes : x = -38 m et +38 m, de part et d'autre de la place.
        private static readonly int[] BridgeColumns = { 0, 1 };

        private sealed class District
        {
            public string Name;
            public int LotsPerSide;
            public float MinHeight, MaxHeight;
            public float Coverage;      // part des lots construits
            public float LotFill;       // part du lot occupée par le bâtiment
            public Color[] Colors;
            public string[] PlaceTypes;
            public bool HasFood;        // étals de nourriture dans ce quartier

            // Rendu et décor (sans effet sur le gameplay ni sur les identifiants des points).
            public FacadeStyle Style;
            public float BalconyChance; // immeubles avec balcons côté rue
            public float AwningChance;  // auvents et enseignes au-dessus des boutiques
            public float TankChance;    // réservoirs d'eau noirs sur les toits
            public float PalmChance;    // cocotiers le long des trottoirs
            public bool PowerPoles;     // poteaux en bois et fils (sinon lampadaires modernes)
            public int ClutterCount;    // objets abandonnés sur les trottoirs, par bloc
        }

        private static readonly District Plateau = new District
        {
            Name = "Plateau", LotsPerSide = 2, MinHeight = 30f, MaxHeight = 110f, Coverage = 0.9f, LotFill = 0.8f,
            Colors = new[] { new Color(0.55f, 0.65f, 0.75f), new Color(0.75f, 0.78f, 0.80f), new Color(0.40f, 0.50f, 0.62f) },
            PlaceTypes = new[] { "Banque", "Ministère", "Cabinet d'avocats", "Siège social", "Agence de voyage", "Assurances" },
            Style = FacadeStyle.Tower, PalmChance = 0.6f,
        };

        private static readonly District Adjame = new District
        {
            Name = "Adjamé", LotsPerSide = 4, MinHeight = 6f, MaxHeight = 16f, Coverage = 0.95f, LotFill = 0.85f, HasFood = true,
            Colors = new[] { new Color(0.80f, 0.50f, 0.35f), new Color(0.85f, 0.65f, 0.45f), new Color(0.70f, 0.45f, 0.40f) },
            PlaceTypes = new[] { "Boutique de pagnes", "Grossiste", "Quincaillerie", "Gare de gbaka", "Marchand de téléphones", "Pharmacie" },
            Style = FacadeStyle.Popular, BalconyChance = 0.25f, AwningChance = 0.7f, TankChance = 0.6f, PalmChance = 0.05f,
            PowerPoles = true, ClutterCount = 7,
        };

        private static readonly District Cocody = new District
        {
            Name = "Cocody", LotsPerSide = 3, MinHeight = 5f, MaxHeight = 9f, Coverage = 0.65f, LotFill = 0.55f,
            Colors = new[] { new Color(0.95f, 0.90f, 0.78f), new Color(0.90f, 0.92f, 0.88f), new Color(0.98f, 0.85f, 0.75f) },
            PlaceTypes = new[] { "Villa", "Ambassade", "Clinique", "École privée", "Pâtisserie", "Restaurant" },
            Style = FacadeStyle.Villa, BalconyChance = 0.4f, TankChance = 0.3f, PalmChance = 0.75f,
        };

        private static readonly District Yopougon = new District
        {
            Name = "Yopougon", LotsPerSide = 5, MinHeight = 4f, MaxHeight = 11f, Coverage = 0.9f, LotFill = 0.8f, HasFood = true,
            Colors = new[] { new Color(0.85f, 0.75f, 0.55f), new Color(0.75f, 0.85f, 0.70f), new Color(0.90f, 0.70f, 0.65f) },
            PlaceTypes = new[] { "Maquis", "Salon de coiffure", "Cabine", "Atelier de couture", "Kiosque", "Cyber" },
            Style = FacadeStyle.Popular, BalconyChance = 0.15f, AwningChance = 0.6f, TankChance = 0.7f, PalmChance = 0.1f,
            PowerPoles = true, ClutterCount = 8,
        };

        private static readonly District Treichville = new District
        {
            Name = "Treichville", LotsPerSide = 4, MinHeight = 6f, MaxHeight = 18f, Coverage = 0.9f, LotFill = 0.85f, HasFood = true,
            Colors = new[] { new Color(0.85f, 0.60f, 0.40f), new Color(0.80f, 0.70f, 0.55f), new Color(0.65f, 0.55f, 0.50f) },
            PlaceTypes = new[] { "Marché", "Boutique", "Maquis", "Garage", "Pressing", "Librairie" },
            Style = FacadeStyle.Popular, BalconyChance = 0.35f, AwningChance = 0.6f, TankChance = 0.6f, PalmChance = 0.15f,
            PowerPoles = true, ClutterCount = 6,
        };

        private static readonly District Marcory = new District
        {
            Name = "Marcory", LotsPerSide = 4, MinHeight = 8f, MaxHeight = 24f, Coverage = 0.85f, LotFill = 0.75f,
            Colors = new[] { new Color(0.80f, 0.80f, 0.78f), new Color(0.70f, 0.75f, 0.80f), new Color(0.85f, 0.78f, 0.70f) },
            PlaceTypes = new[] { "Supermarché", "Résidence", "Boulangerie", "Salle de sport", "Bureau", "Pharmacie" },
            Style = FacadeStyle.Residential, BalconyChance = 0.6f, AwningChance = 0.3f, TankChance = 0.4f, PalmChance = 0.3f,
            ClutterCount = 2,
        };

        // Propriété (et non champ) : Attecoube et Koumassi sont déclarés dans un autre fichier partiel,
        // dont l'ordre d'initialisation n'est pas garanti.
        private static District[] s_allDistricts;
        private static District[] AllDistricts => s_allDistricts ??= new[]
        {
            Plateau, Adjame, Cocody, Yopougon, Treichville, Marcory, Attecoube, Koumassi, Abobo, Riviera, PortBouet,
        };

        private struct FoodItem
        {
            public string Stall, Item;
            public long Price;
            public int Satiety;
            public int Hydration;
        }

        private static readonly FoodItem[] Foods =
        {
            new FoodItem { Stall = "Garba", Item = "un garba", Price = 1000, Satiety = 40 },
            new FoodItem { Stall = "Alloco", Item = "un alloco", Price = 500, Satiety = 20 },
            new FoodItem { Stall = "Attiéké poisson", Item = "un attiéké poisson", Price = 1500, Satiety = 55 },
            new FoodItem { Stall = "Brochettes", Item = "des brochettes", Price = 700, Satiety = 25 },
        };

        private static readonly string[] Vendors = { "Tantie Awa", "Maman Adjoua", "Chez Koffi", "Tantie Rose", "Chez Fanta", "Mama Brou" };

        // Boissons : apaisent la soif (jauge d'hydratation). Ajoutées après tous les autres points
        // (identifiants nouveaux, ceux des points existants ne bougent pas).
        private static readonly FoodItem[] Drinks =
        {
            new FoodItem { Stall = "Eau en sachet", Item = "un sachet d'eau", Price = 100, Satiety = 0, Hydration = 35 },
            new FoodItem { Stall = "Bissap", Item = "un bissap", Price = 300, Satiety = 5, Hydration = 45 },
            new FoodItem { Stall = "Gnamakoudji", Item = "un gnamakoudji", Price = 300, Satiety = 5, Hydration = 45 },
        };

        private static readonly List<(Transform parent, Vector3 position)> s_pendingDrinks = new List<(Transform, Vector3)>();

        /// <summary>Crée les étals de boissons notés pendant la génération, avec de nouveaux identifiants.</summary>
        private static void CreateDrinkStalls(Transform interactables, ref int nextId)
        {
            Material shopMaterial = GetOrCreateMaterial("M_Shop", new Color(0.95f, 0.55f, 0.15f));
            foreach ((Transform parent, Vector3 position) in s_pendingDrinks)
            {
                FoodItem drink = Drinks[s_decor.Next(Drinks.Length)];
                Vector3 world = parent.TransformPoint(position);
                CreateShop(interactables, ref nextId, drink, Vendors[s_decor.Next(Vendors.Length)], world, shopMaterial);
            }
            s_pendingDrinks.Clear();
        }

        /// <summary>Quartier d'une cellule de la grille (null = lagune, ou place centrale si (0,0)).</summary>
        private static District DistrictAt(int x, int z)
        {
            if (z == LagoonRow)
                return null;
            if (z > LagoonRow)
            {
                if (x <= -2) return Yopougon;
                if (x >= 2) return Cocody;
                return z >= 2 ? Adjame : Plateau;
            }

            return x <= 0 ? Treichville : Marcory;
        }

        private static Vector3 CellCenter(int x, int z) => new Vector3(x * CellSize, 0f, z * CellSize);

        public static void Generate(Transform parent)
        {
            // Construite à plat (repères compris), puis posée sur le relief en une passe (ApplyRelief).
            IvoryState.Gameplay.World.CityLayout.PlanarSpots = true;
            try
            {
                GenerateCity(parent);
            }
            finally
            {
                IvoryState.Gameplay.World.CityLayout.PlanarSpots = false;
            }
        }

        private static void GenerateCity(Transform parent)
        {
            // Deux générateurs aléatoires : « random » fixe la disposition et les identifiants des points
            // d'interaction (à ne jamais modifier), « s_decor » ne sert qu'à l'habillage. On peut ainsi
            // enrichir le décor sans décaler les identifiants partagés par le client et le serveur.
            var random = new System.Random(20261115);
            s_decor = new System.Random(77031);
            s_districtsWithDepot.Clear();
            s_pendingDrinks.Clear();
            s_reserved.Clear();
            s_gbakaMeshes.Clear();
            s_vendorIndex = 0;
            s_characterModels = null;
            var meshes = new List<Mesh>();
            s_pendingMeshes = meshes;
            int nextId = 1;

            var city = new GameObject("City").transform;
            city.SetParent(parent, false);

            DeleteLegacyMaterials();
            CityDecor.Prepare();
            Material asphalt = CityMaterials.Lit("M_Asphalt", "asphalt_02", 0.2f, 0.25f, new Color(0.85f, 0.85f, 0.85f), true, 4);
            Material sidewalk = CityMaterials.Lit("M_Sidewalk", "concrete_pavement_02", 0.5f, 0.15f, new Color(0.92f, 0.88f, 0.82f), true, 2);
            Material water = CityMaterials.Water();
            // Place du Plateau : dalles de pierre claire (le cube primitif a des UV de 0 à 1 par face).
            Material plaza = CityMaterials.Lit("M_Plaza", "concrete_pavement_02", BlockSize / 2.5f, 0.2f, new Color(1f, 0.97f, 0.92f), true, 2);
            s_markingMaterial = CityMaterials.Plain("M_RoadMarking", new Color(0.86f, 0.86f, 0.82f), 0.3f);
            foreach (District d in AllDistricts)
                s_facades[d] = CityMaterials.Facade("M_Facade_" + d.Name, d.Style);

            var interactables = new GameObject("InteractionPoints").transform;
            interactables.SetParent(city, false);

            // Ville d'après le tracé d'Abidjan : relief, lagune, routes et ponts, trottoirs, bâtiments,
            // lieux particuliers (la place du Plateau d'abord : ses points ont les plus petits identifiants).
            GenerateFromLayout(city, interactables, random, asphalt, sidewalk, water, plaza, meshes, ref nextId);

            // Boissons : vendeuses notées pendant la génération.
            CreateDrinkStalls(interactables, ref nextId);
            // Boutiques de vêtements : nouveaux identifiants, à la suite (rien d'existant ne bouge).
            CreateClothingShops(interactables, ref nextId);
            CreateVehicleShops(interactables, ref nextId);
            CreateBlackMarket(interactables, ref nextId);
            CreateCashPoints(interactables, ref nextId);
            CreateCommerces(interactables, ref nextId);
            CreateBladeVendor(interactables, ref nextId);

            // Logements : entrée de la résidence (rue) et studios (hors de « City » : pas dans le NavMesh).
            CreateResidenceEntrance(city, meshes);
            CreatePropertyEntrances(city, meshes);
            CreateHospitalEntrances(city, meshes);
            CreateStudios(parent, meshes);

            // Architecture réaliste modélisée dans Blender (classée par commune sous City/Architecture).
            ArchitectureSetup.PlaceAllInScene(city);

            // Relief : tout ce qui précède est posé sur le sol (routes, trottoirs, mobilier, points de jeu...).
            ApplyRelief(city, meshes);
            for (int i = 0; i < CityDecor.LampHeads.Count; i++)
            {
                Vector4 head = CityDecor.LampHeads[i];
                CityDecor.LampHeads[i] = new Vector4(head.x, head.y + Ground(head.x, head.z), head.z, head.w);
            }

            // Éclairage public réel de nuit : registre des lampadaires + groupe de lumières mobiles.
            var lampsObject = new GameObject("EclairagePublic");
            lampsObject.transform.SetParent(city, false);
            var lamps = lampsObject.AddComponent<Client.World.CityLamps>();
            lamps.EditorSetLamps(CityDecor.LampHeads.ToArray());
            var pool = lampsObject.AddComponent<Client.World.StreetLightPool>();
            var poolSettings = new SerializedObject(pool);
            poolSettings.FindProperty("_lamps").objectReferenceValue = lamps;
            poolSettings.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[City] " + CityDecor.LampHeads.Count + " lampadaires enregistrés.");

            // Lampadaires et poteaux solides (leur maillage est fusionné avec les façades : colliders à part).
            var poles = new GameObject("PoteauxSolides");
            poles.transform.SetParent(city, false);
            poles.isStatic = true;
            foreach (Vector4 pole in CityDecor.PoleBases)
            {
                float side = pole.w * 2f;
                AddBoxCollider(poles, new Vector3(pole.x, pole.y + Ground(pole.x, pole.z) + 2f, pole.z), new Vector3(side, 4f, side));
            }

            // Statique pour le batching et l'occlusion, mais hors GI précalculée (pas de lightmaps :
            // UV1 sert à la grille des fenêtres, pas à un atlas de lightmap).
            foreach (Transform t in city.GetComponentsInChildren<Transform>(true))
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                if ((flags & StaticEditorFlags.ContributeGI) != 0)
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~StaticEditorFlags.ContributeGI);
            }

            SaveMeshes(meshes);
            BakeNavMesh(city.gameObject);

            Debug.Log("[City] Ville générée : " + (nextId - 1) + " points d'interaction.");
        }

        private static System.Random s_decor;
        private static Material s_markingMaterial;
        private static readonly Dictionary<District, Material> s_facades = new Dictionary<District, Material>();

        /// <summary>Supprime les matériaux unis de la version « greybox » (remplacés par les façades texturées).</summary>
        private static void DeleteLegacyMaterials()
        {
            foreach (District d in AllDistricts)
            {
                for (int i = 0; i < d.Colors.Length; i++)
                    AssetDatabase.DeleteAsset(MaterialFolder + "/M_" + d.Name + "_" + i + ".mat");
            }
        }

        #region Sol, limites, lagune

        /// <summary>Chaussée : un seul grand quadrilatère (UV en mètres) et un collider plat.</summary>
        private static void CreateGround(Transform city, Material asphalt, float extent, List<Mesh> meshes)
        {
            var builder = new BoxMeshBuilder();
            builder.AddFlatQuad(Vector3.zero, Vector3.right, extent * 2f, extent * 2f);
            var ground = AddMeshObject(city, "Ground", builder, asphalt, meshes);
            ground.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AddBoxCollider(ground, new Vector3(0f, -0.5f, 0f), new Vector3(extent * 2f, 1f, extent * 2f));
            // Au-delà : relief des abords et collines lointaines (voir CityGenerator.Outskirts).
        }

        private static void CreateRoadMarkings(Transform city, List<Mesh> meshes)
        {
            var builder = new BoxMeshBuilder();
            CityDecor.AddRoadMarkings(builder, GridRadius, CellSize, RoadWidth, IsWater);
            var markings = AddMeshObject(city, "Marquages", builder, s_markingMaterial, meshes);
            markings.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Vrai si le point (x, z) est sur la lagune (hors ponts).</summary>
        private static bool IsWater(float x, float z)
        {
            float zCenter = LagoonRow * CellSize;
            if (z < zCenter - BlockSize / 2f || z > zCenter + BlockSize / 2f)
                return false;
            foreach (int column in BridgeColumns)
            {
                float roadCenter = column * CellSize - CellSize / 2f;
                if (Mathf.Abs(x - roadCenter) <= RoadWidth / 2f)
                    return false;
            }
            return true;
        }

        /// <summary>Murs invisibles en bordure de carte.</summary>
        private static void CreateBounds(Transform city, float extent)
        {
            var bounds = new GameObject("Bounds");
            bounds.transform.SetParent(city, false);
            // Du fond de la lagune au-dessus des plus hautes collines des abords.
            const float height = 140f, bottom = -20f, thickness = 2f;
            float size = extent * 2f;
            float y = bottom + height / 2f;

            AddBoxCollider(bounds, new Vector3(0f, y, extent), new Vector3(size, height, thickness));
            AddBoxCollider(bounds, new Vector3(0f, y, -extent), new Vector3(size, height, thickness));
            AddBoxCollider(bounds, new Vector3(extent, y, 0f), new Vector3(thickness, height, size));
            AddBoxCollider(bounds, new Vector3(-extent, y, 0f), new Vector3(thickness, height, size));
        }

        /// <summary>
        /// Lagune : bande d'eau d'est en ouest. Les quais sont des barrières invisibles qui empêchent
        /// de tomber à l'eau. Deux ponts prolongent les rues nord-sud.
        /// </summary>
        private static void CreateLagoon(Transform city, Material water, Material railMaterial, float extent)
        {
            var lagoon = new GameObject("Lagune");
            lagoon.transform.SetParent(city, false);

            // Les quais (barrières invisibles de 3 m posées sur l'eau) ne doivent pas devenir des
            // chemins : sinon le NavMesh couvre leur dessus et les passants « marchent sur l'eau ».
            var modifier = lagoon.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = 1; // Not Walkable
            modifier.applyToChildren = true;

            float zCenter = LagoonRow * CellSize;
            float zMin = zCenter - BlockSize / 2f;
            float zMax = zCenter + BlockSize / 2f;
            float depth = zMax - zMin;

            // Ponts : sur les rues nord-sud situées juste à l'ouest des colonnes choisies.
            var bridgeRanges = new List<Vector2>();
            foreach (int column in BridgeColumns)
            {
                float roadCenter = column * CellSize - CellSize / 2f;
                bridgeRanges.Add(new Vector2(roadCenter - RoadWidth / 2f, roadCenter + RoadWidth / 2f));
            }

            bridgeRanges.Sort((a, b) => a.x.CompareTo(b.x));

            // Segments d'eau entre les ponts.
            float start = -extent;
            foreach (Vector2 bridge in bridgeRanges)
            {
                CreateWaterSegment(lagoon.transform, water, start, bridge.x, zMin, zMax);
                CreateBridge(lagoon.transform, railMaterial, bridge, zMin, zMax);
                start = bridge.y;
            }

            CreateWaterSegment(lagoon.transform, water, start, extent, zMin, zMax);
        }

        private static void CreateWaterSegment(Transform parent, Material water, float xMin, float xMax, float zMin, float zMax)
        {
            if (xMax - xMin < 0.5f)
                return;

            float width = xMax - xMin, depth = zMax - zMin;
            var center = new Vector3((xMin + xMax) / 2f, 0f, (zMin + zMax) / 2f);

            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "Eau";
            Object.DestroyImmediate(surface.GetComponent<Collider>());
            surface.transform.SetParent(parent, false);
            surface.transform.localPosition = center + new Vector3(0f, 0.03f, 0f);
            surface.transform.localScale = new Vector3(width, 0.02f, depth);
            surface.GetComponent<MeshRenderer>().sharedMaterial = water;
            surface.isStatic = true;

            // Barrière invisible : le quai.
            AddBoxCollider(surface.transform.parent.gameObject, center + new Vector3(0f, 1.5f, 0f), new Vector3(width, 3f, depth));
        }

        private static void CreateBridge(Transform parent, Material railMaterial, Vector2 xRange, float zMin, float zMax)
        {
            float length = zMax - zMin;
            float zCenter = (zMin + zMax) / 2f;
            var bridge = new GameObject("Pont");
            bridge.transform.SetParent(parent, false);

            // Garde-corps de chaque côté (visibles, 1,1 m : on ne les enjambe pas en sautant de côté).
            foreach (float x in new[] { xRange.x + 0.2f, xRange.y - 0.2f })
            {
                GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = "GardeCorps";
                rail.transform.SetParent(bridge.transform, false);
                rail.transform.localPosition = new Vector3(x, 0.55f, zCenter);
                rail.transform.localScale = new Vector3(0.4f, 1.1f, length);
                rail.GetComponent<MeshRenderer>().sharedMaterial = railMaterial;
                rail.isStatic = true;
            }
        }

        #endregion

        #region Taxis

        /// <summary>
        /// Stations de taxis, sur la chaussée des rues nord-sud qui longent la place (x = ±38 m) et
        /// dans les quartiers. Orientées dans l'axe de la rue, garées sur le côté (à 3 m du centre).
        /// </summary>
        private static void CreateTaxiStands(Transform city)
        {
            var root = new GameObject("TaxiStands").transform;
            root.SetParent(city, false);

            const float laneOffset = 3f;
            float west = -CellSize / 2f, east = CellSize / 2f;

            // (x, z, cap en degrés) : cap 0 = vers le nord (+z).
            Vector3[] stands =
            {
                new Vector3(east + laneOffset, -10f, 0f), new Vector3(east + laneOffset, 10f, 0f),
                new Vector3(west - laneOffset, -10f, 180f), new Vector3(west - laneOffset, 10f, 180f),
                new Vector3(west - laneOffset, 2.5f * CellSize, 180f),    // Adjamé
                new Vector3(east + laneOffset, -2.5f * CellSize, 0f),     // Marcory
                new Vector3(west - laneOffset, -3f * CellSize, 180f),     // Treichville
                new Vector3(-2.5f * CellSize, 1f * CellSize, 90f),        // Yopougon (rue est-ouest)
                new Vector3(2.5f * CellSize, 1f * CellSize, 270f),        // Cocody (rue est-ouest)
            };

            for (int i = 0; i < stands.Length; i++)
            {
                var stand = new GameObject("TaxiStand_" + i);
                stand.transform.SetParent(root, false);
                // Les rues est-ouest passent entre deux rangées de blocs : z décalé d'une demi-cellule.
                Vector3 position = new Vector3(stands[i].x, 0.1f, stands[i].y);
                if (Mathf.Approximately(stands[i].z, 90f) || Mathf.Approximately(stands[i].z, 270f))
                    position.z += CellSize / 2f + laneOffset;
                stand.transform.localPosition = position;
                stand.transform.localRotation = Quaternion.Euler(0f, stands[i].z, 0f);
                stand.AddComponent<Gameplay.Vehicles.TaxiStand>();
            }
        }

        #endregion

        #region Place centrale

        private static void CreatePlaza(Transform city, Transform interactables, Material plazaMaterial, Vector3 center, float yaw, ref int nextId)
        {
            var plaza = new GameObject("Place du Plateau");
            plaza.transform.SetParent(city, false);
            plaza.transform.SetLocalPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
            MarkSidewalkArea(plaza);
            Transform p = plaza.transform;

            GameObject pavement = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pavement.name = "Pavage";
            pavement.transform.SetParent(plaza.transform, false);
            pavement.transform.localPosition = new Vector3(0f, SidewalkHeight / 2f, 0f);
            pavement.transform.localScale = new Vector3(BlockSize, SidewalkHeight, BlockSize);
            pavement.GetComponent<MeshRenderer>().sharedMaterial = plazaMaterial;
            pavement.isStatic = true;

            Material shopMaterial = GetOrCreateMaterial("M_Shop", new Color(0.95f, 0.55f, 0.15f));
            Material jobMaterial = GetOrCreateMaterial("M_JobBoard", new Color(0.2f, 0.45f, 0.95f));

            // Dépôt et étals autour de la place, à quelques pas du point d'apparition.
            var board = CreatePoint<JobBoardPoint>(interactables, nextId++, "Dépôt Express du Plateau", p.TransformPoint(new Vector3(0f, SidewalkHeight, -14f)));
            board.transform.rotation = p.rotation;
            AddJobBoardVisual(board.transform, new Vector3(0f, 0f, -0.6f), jobMaterial);

            CreateShop(interactables, ref nextId, Foods[0], "Tantie Awa", p.TransformPoint(new Vector3(10f, SidewalkHeight, 10f)), shopMaterial);
            CreateShop(interactables, ref nextId, Foods[1], "Maman Adjoua", p.TransformPoint(new Vector3(-10f, SidewalkHeight, 10f)), shopMaterial);
            s_pendingDrinks.Insert(0, (p, new Vector3(0f, SidewalkHeight, 12.5f))); // vendeuse d'eau de la place

            // Habillage : terrasses devant les deux étals, bancs, jardinières, cocotiers et lampadaires.
            var decor = new GameObject("Decor").transform;
            decor.SetParent(plaza.transform, false);
            CityDecor.AddMaquis(decor, new Vector3(10f, SidewalkHeight, 10f), Vector3.right, Vector3.forward, s_decor);
            CityDecor.AddMaquis(decor, new Vector3(-10f, SidewalkHeight, 10f), Vector3.left, Vector3.forward, s_decor);

            foreach (float bz in new[] { -6f, 4f })
            {
                PropLibrary.Place("painted_wooden_bench", decor, new Vector3(-16f, SidewalkHeight, bz), 270f, 1f, true, 0.03f);
                PropLibrary.Place("painted_wooden_bench", decor, new Vector3(16f, SidewalkHeight, bz), 90f, 1f, true, 0.03f);
            }
            foreach (float px in new[] { -16f, 16f })
            {
                PropLibrary.Place("planter_box_01", decor, new Vector3(px, SidewalkHeight, -1f), 90f, 1f, true, 0.02f);
                PropLibrary.Place("potted_plant_04", decor, new Vector3(px, SidewalkHeight + 0.36f, -1f), px < 0f ? 20f : 200f, 1.6f, false, 0.03f);
            }
            PropLibrary.Place("small_lpg_tank", decor, new Vector3(11.6f, SidewalkHeight, 10.9f), 25f, 1f, true, 0.02f);
            PropLibrary.Place("small_lpg_tank", decor, new Vector3(-11.6f, SidewalkHeight, 10.9f), 140f, 1f, true, 0.02f);
            PropLibrary.Place("Barrel_02", decor, new Vector3(-12.4f, SidewalkHeight, 10.2f), 30f, 1f, true, 0.02f);
            PropLibrary.Place("standing_chalkboard_01", decor, new Vector3(11.6f, SidewalkHeight, 8.7f), 205f, 0.95f, false, 0.02f, false);
            PropLibrary.Place("standing_chalkboard_01", decor, new Vector3(-11.6f, SidewalkHeight, 8.7f), 155f, 0.95f, false, 0.02f, false);
            Vector3 boomboxPos = p.InverseTransformPoint(p.TransformPoint(new Vector3(10f, SidewalkHeight, 10f)) + new Vector3(0.7f, 1.0f, 0.75f));
            PropLibrary.Place("boombox", decor, boomboxPos, 165f, 1f, false, 0.01f);

            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f + 15f;
                Vector3 position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 26f;
                // On laisse libres les abords du dépôt (sud) et le chemin vers les ponts.
                if (position.z < -18f && Mathf.Abs(position.x) < 10f)
                    continue;
                PalmBuilder.Place(decor, new Vector3(position.x, SidewalkHeight, position.z), s_decor.Next(PalmBuilder.VariantCount), s_decor.Next(360));
            }

            var lampBuilders = new Dictionary<Material, BoxMeshBuilder>();
            var bulbs = new BoxMeshBuilder();
            var pools = new BoxMeshBuilder();
            CityDecor.Frame = p.localToWorldMatrix;
            AddPerimeterLamps(lampBuilders, bulbs, pools, new[] { -20f, 0f, 20f });
            // Bornes anti-stationnement et corbeilles de propreté le long du parvis.
            float plazaEdge = BlockSize / 2f - 0.8f;
            for (int side = 0; side < 4; side++)
            {
                Vector3 outward = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.forward;
                Vector3 along = Vector3.Cross(Vector3.up, outward);
                Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);
                foreach (float t in new[] { -26f, -12f, 12f, 26f })
                {
                    BlenderCityKit.Stamp("SM_Bollard", lampBuilders,
                        Matrix4x4.TRS(outward * plazaEdge + along * t + Vector3.up * SidewalkHeight, rot, Vector3.one),
                        null, Color.white, 0);
                }
                BlenderCityKit.Stamp("SM_StreetBin", lampBuilders,
                    Matrix4x4.TRS(outward * (plazaEdge - 1.1f) + along * 8f + Vector3.up * SidewalkHeight, rot, Vector3.one),
                    null, Color.white, 0);
            }
            CityDecor.Frame = Matrix4x4.identity;
            var meshes = s_pendingMeshes;
            BlenderCityKit.BuildObjects(decor, "Lampadaires", lampBuilders, meshes);
            AddLightMeshes(decor, bulbs, pools, CityDecor.BulbMaterial, meshes);
        }

        /// <summary>Maillages créés hors de <see cref="CreateBlock"/>, enregistrés avec les autres.</summary>
        private static List<Mesh> s_pendingMeshes = new List<Mesh>();

        /// <summary>Lampadaires modernes au bord des 4 trottoirs d'un bloc, aux abscisses <paramref name="positions"/>.</summary>
        private static void AddPerimeterLamps(Dictionary<Material, BoxMeshBuilder> builders,
            BoxMeshBuilder bulbs, BoxMeshBuilder pools, float[] positions)
        {
            float edge = BlockSize / 2f - 0.5f;
            for (int side = 0; side < 4; side++)
            {
                Vector3 outward = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.forward;
                Vector3 along = Vector3.Cross(Vector3.up, outward);
                foreach (float t in positions)
                {
                    Vector3 basePosition = outward * edge + along * t + Vector3.up * SidewalkHeight;
                    CityDecor.AddStreetLamp(builders, bulbs, pools, basePosition, outward, SidewalkHeight);
                }
            }
        }

        private static void AddLightMeshes(Transform parent, BoxMeshBuilder bulbs, BoxMeshBuilder pools, Material bulbMaterial, List<Mesh> meshes)
        {
            var bulbObject = AddMeshObject(parent, "Ampoules", bulbs, bulbMaterial, meshes);
            if (bulbObject != null)
                bulbObject.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var poolObject = AddMeshObject(parent, "Halos", pools, CityDecor.PoolMaterial, meshes);
            if (poolObject != null)
            {
                var renderer = poolObject.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        #endregion

        #region Blocs

        private static void CreateBlock(Transform city, Transform interactables, District district, int x, int z,
            System.Random random, Material sidewalk, List<Mesh> meshes, ref int nextId)
        {
            Vector3 center = CellCenter(x, z);
            var block = new GameObject(district.Name + "_" + x + "_" + z);
            block.transform.SetParent(city, false);
            block.transform.localPosition = center;
            block.isStatic = true;
            MarkSidewalkArea(block);

            // Trottoir : dalle surélevée couvrant tout le bloc ; les bâtiments sont posés dessus.
            var sidewalkBuilder = new BoxMeshBuilder();
            sidewalkBuilder.AddBox(Vector3.zero, new Vector3(BlockSize, SidewalkHeight, BlockSize));
            AddMeshObject(block.transform, "Trottoir", sidewalkBuilder, sidewalk, meshes);
            AddBoxCollider(block, new Vector3(0f, SidewalkHeight / 2f, 0f), new Vector3(BlockSize, SidewalkHeight, BlockSize));

            // Bâtiments : lots réguliers, tous dans un seul maillage (teinte par sommet, un draw call).
            var facade = new BoxMeshBuilder();
            var interior = new BoxMeshBuilder();
            float buildable = BlockSize - SidewalkWidth * 2f;
            float lotSize = buildable / district.LotsPerSide;

            // Une superette (bâtiment où l'on entre) dans un bloc sur deux, sur un lot du bord.
            int last = district.LotsPerSide - 1;
            Vector2Int storeLot = new Vector2Int(-1, -1);
            if (s_decor.NextDouble() < 0.5)
            {
                int along = s_decor.Next(district.LotsPerSide);
                switch (s_decor.Next(4))
                {
                    case 0: storeLot = new Vector2Int(0, along); break;
                    case 1: storeLot = new Vector2Int(last, along); break;
                    case 2: storeLot = new Vector2Int(along, 0); break;
                    default: storeLot = new Vector2Int(along, last); break;
                }
            }

            for (int lx = 0; lx < district.LotsPerSide; lx++)
            {
                for (int lz = 0; lz < district.LotsPerSide; lz++)
                {
                    // Tirages de « random » : mêmes appels, même ordre qu'avant (identifiants stables).
                    if (random.NextDouble() > district.Coverage)
                        continue;

                    float lotX = -buildable / 2f + (lx + 0.5f) * lotSize;
                    float lotZ = -buildable / 2f + (lz + 0.5f) * lotSize;
                    float fill = district.LotFill * Mathf.Lerp(0.85f, 1f, (float)random.NextDouble());
                    float footprint = lotSize * fill;
                    float height = Mathf.Lerp(district.MinHeight, district.MaxHeight, (float)(random.NextDouble() * random.NextDouble()));
                    height = Mathf.Round(height / 3f) * 3f; // étages de 3 m

                    Color color = district.Colors[random.Next(district.Colors.Length)];
                    var baseCenter = new Vector3(lotX, SidewalkHeight, lotZ);
                    var size = new Vector3(footprint, height, footprint);

                    if (lx == storeLot.x && lz == storeLot.y && footprint >= 8f)
                    {
                        Vector3 street = lx == 0 ? Vector3.left : lx == last ? Vector3.right : lz == 0 ? Vector3.back : Vector3.forward;
                        AddSuperette(block, facade, interior, district, baseCenter, footprint, height, color, street, interactables, ref nextId);
                        continue;
                    }

                    AddBuildingVisual(facade, district, baseCenter, size, color);
                    AddBoxCollider(block, baseCenter + new Vector3(0f, height / 2f, 0f), size);
                }
            }

            DecorateBlock(block.transform, facade, district, meshes);
            AddMeshObject(block.transform, "Batiments", facade, s_facades[district], meshes);
            AddMeshObject(block.transform, "Interieurs", interior, CityMaterials.Painted(), meshes);

            CreateBlockInteractables(interactables, block.transform, district, center, random, ref nextId);
        }

        #endregion

        #region Habillage des immeubles

        private static readonly Color SlabColor = new Color(0.74f, 0.72f, 0.68f);
        private static readonly Color TankColor = new Color(0.07f, 0.07f, 0.07f);
        private static readonly Color RoofGrey = new Color(0.55f, 0.55f, 0.55f);

        // Tôles et bâches des auvents, enseignes des boutiques.
        private static readonly Color[] AwningColors =
        {
            new Color(0.55f, 0.32f, 0.20f), new Color(0.20f, 0.40f, 0.65f), new Color(0.30f, 0.55f, 0.30f),
            new Color(0.75f, 0.20f, 0.15f), new Color(0.62f, 0.62f, 0.60f),
        };

        private static readonly Color[] SignColors =
        {
            new Color(0.95f, 0.80f, 0.15f), new Color(0.95f, 0.45f, 0.10f), new Color(0.15f, 0.45f, 0.80f),
            new Color(0.85f, 0.15f, 0.20f), new Color(0.15f, 0.60f, 0.35f), new Color(0.92f, 0.92f, 0.90f),
        };

        /// <summary>
        /// Un immeuble : murs à fenêtres, dalle de toit débordante, et selon le quartier balcons,
        /// auvent, enseigne, réservoirs d'eau ou couronne de tour. Uniquement visuel (le collider reste
        /// le pavé de base) ; tirages sur <see cref="s_decor"/>.
        /// </summary>
        private static readonly Color[] TileRoofColors =
        {
            new Color(0.68f, 0.30f, 0.18f), new Color(0.56f, 0.24f, 0.15f),
            new Color(0.72f, 0.38f, 0.21f), new Color(0.42f, 0.30f, 0.25f),
        };

        private static readonly Color[] MetalRoofColors =
        {
            new Color(0.55f, 0.57f, 0.60f), new Color(0.58f, 0.28f, 0.22f),
            new Color(0.32f, 0.42f, 0.50f), new Color(0.28f, 0.45f, 0.33f),
        };

        private static void AddBuildingVisual(BoxMeshBuilder facade, District district, Vector3 baseCenter, Vector3 size, Color color,
            Vector3? streetSide = null, BoxMeshBuilder roofsTiles = null, BoxMeshBuilder roofsMetal = null)
        {
            System.Random r = s_decor;
            float jitter = Mathf.Lerp(0.9f, 1.08f, (float)r.NextDouble());
            int seed = r.Next(256);
            facade.SetColor(color * jitter, seed);

            float height = size.y;
            bool tower = district.Style == FacadeStyle.Tower;

            // Côté rue : donné (lots le long des rues), sinon direction du bord de bloc le plus proche.
            Vector3 street = streetSide ?? (Mathf.Abs(baseCenter.x) >= Mathf.Abs(baseCenter.z)
                ? new Vector3(Mathf.Sign(baseCenter.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(baseCenter.z)));
            Vector3 across = Vector3.Cross(Vector3.up, street);
            // Emprise rectangulaire : demi-profondeur côté rue et largeur de la façade sur rue.
            bool alongX = Mathf.Abs(street.x) > 0.5f;
            float halfDepth = (alongX ? size.x : size.z) * 0.5f;
            float frontWidth = alongX ? size.z : size.x;
            float footprint = Mathf.Min(size.x, size.z);

            if (tower)
            {
                facade.AddBuilding(baseCenter, size);
                float roof = baseCenter.y + height;
                if (height >= 45f)
                {
                    // Couronne en retrait : silhouette plus élancée.
                    float crown = Mathf.Round(Mathf.Lerp(6f, 12f, (float)r.NextDouble()) / 3f) * 3f;
                    var crownSize = new Vector3(size.x * 0.7f, crown, size.z * 0.7f);
                    facade.AddBuilding(new Vector3(baseCenter.x, roof, baseCenter.z), crownSize);
                    roof += crown;
                }

                // Équipements techniques sur le toit.
                facade.SetColor(RoofGrey, seed);
                for (int i = 0; i < 2 + r.Next(3); i++)
                {
                    var offset = new Vector3(((float)r.NextDouble() - 0.5f) * size.x * 0.5f, 0f, ((float)r.NextDouble() - 0.5f) * size.z * 0.5f);
                    facade.AddBox(new Vector3(baseCenter.x, roof, baseCenter.z) + offset, new Vector3(2.5f, 1.6f, 1.8f));
                }
                return;
            }

            // Corps de l'immeuble, coiffé d'une dalle de béton débordante (typique des constructions locales).
            facade.AddBuilding(baseCenter, size, roof: false);
            facade.SetColor(SlabColor * jitter, seed);
            facade.AddBox(baseCenter + Vector3.up * height, new Vector3(size.x + 0.5f, 0.3f, size.z + 0.5f));
            Vector3 roofCenter = baseCenter + Vector3.up * (height + 0.3f);

            // Balcons côté rue, à chaque étage.
            int floors = Mathf.RoundToInt(height / BoxMeshBuilder.FloorHeight);
            if (floors >= 2 && r.NextDouble() < district.BalconyChance)
            {
                float width = frontWidth * 0.82f;
                for (int floor = 1; floor < floors; floor++)
                {
                    float y = floor * BoxMeshBuilder.FloorHeight;
                    Vector3 slab = baseCenter + street * (halfDepth + 0.55f) + Vector3.up * (y - 0.15f);
                    facade.SetColor(SlabColor * jitter, seed);
                    facade.AddBox(slab, Oriented(street, 1.1f, 0.15f, width));
                    // Garde-corps maçonné, de la couleur de l'immeuble.
                    facade.SetColor(color * jitter * 1.05f, seed);
                    facade.AddBox(baseCenter + street * (halfDepth + 1.06f) + Vector3.up * y, Oriented(street, 0.08f, 1.0f, width));
                }
            }

            // Rez-de-chaussée commerçant : auvent en tôle et enseigne colorée.
            if (r.NextDouble() < district.AwningChance)
            {
                facade.SetColor(AwningColors[r.Next(AwningColors.Length)], seed);
                Vector3 awning = baseCenter + street * (halfDepth + 0.7f) + Vector3.up * 2.65f;
                facade.AddBox(awning, Oriented(street, 1.4f, 0.06f, frontWidth * 0.9f));

                facade.SetColor(SignColors[r.Next(SignColors.Length)], seed);
                float signWidth = Mathf.Min(frontWidth * 0.7f, Mathf.Lerp(2.5f, 5f, (float)r.NextDouble()));
                Vector3 sign = baseCenter + street * (halfDepth + 0.06f) + across * (((float)r.NextDouble() - 0.5f) * (frontWidth - signWidth) * 0.8f)
                               + Vector3.up * 2.75f;
                facade.AddBox(sign, Oriented(street, 0.12f, 0.6f, signWidth));
            }

            // Toitures locales en pente sur les villas et maisons basses (tuiles ou tôles ondulées), sinon réservoirs sur dalle.
            bool tileRoof = roofsTiles != null &&
                ((district.Style == FacadeStyle.Villa && height <= 9.5f) || (district.Style == FacadeStyle.Residential && height <= 6.5f));
            bool metalRoof = !tileRoof && roofsMetal != null && district.Style == FacadeStyle.Popular && height <= 6.5f && r.NextDouble() < 0.55;
            if (tileRoof)
            {
                roofsTiles.SetColor(TileRoofColors[r.Next(TileRoofColors.Length)] * jitter, seed);
                roofsTiles.AddHipRoof(roofCenter, new Vector2(size.x + 0.3f, size.z + 0.3f), Mathf.Lerp(1.5f, 2.2f, (float)r.NextDouble()), 0.45f);
            }
            else if (metalRoof)
            {
                roofsMetal.SetColor(MetalRoofColors[r.Next(MetalRoofColors.Length)] * jitter, seed);
                roofsMetal.AddHipRoof(roofCenter, new Vector2(size.x + 0.2f, size.z + 0.2f), Mathf.Lerp(0.85f, 1.3f, (float)r.NextDouble()), 0.4f);
            }

            // Condenseurs de climatisation sur la façade haute dans les quartiers résidentiels/villas.
            if ((district.Style == FacadeStyle.Villa || district.Style == FacadeStyle.Residential) && floors >= 2 && r.NextDouble() < 0.45)
            {
                facade.SetColor(RoofGrey, seed);
                float acShift = ((float)r.NextDouble() - 0.5f) * (frontWidth * 0.55f);
                Vector3 acPos = baseCenter + street * (halfDepth + 0.16f) + across * acShift + Vector3.up * (height - 1.35f);
                facade.AddBox(acPos, Oriented(street, 0.32f, 0.52f, 0.78f));
            }

            // Réservoirs d'eau en polyéthylène noir sur les toits plats uniquement.
            if (!tileRoof && !metalRoof && r.NextDouble() < district.TankChance)
            {
                facade.SetColor(TankColor, seed);
                int tanks = 1 + r.Next(2);
                for (int i = 0; i < tanks; i++)
                {
                    var offset = new Vector3(((float)r.NextDouble() - 0.5f) * size.x * 0.6f, 0f, ((float)r.NextDouble() - 0.5f) * size.z * 0.6f);
                    facade.AddCylinder(roofCenter + offset, 0.6f, 1.3f, 8);
                }
            }
        }

        /// <summary>Taille d'un pavé orienté : <paramref name="depth"/> selon la rue, <paramref name="width"/> en travers.</summary>
        private static Vector3 Oriented(Vector3 street, float depth, float height, float width)
        {
            return Mathf.Abs(street.x) > 0.5f ? new Vector3(depth, height, width) : new Vector3(width, height, depth);
        }

        /// <summary>
        /// Mobilier des trottoirs du bloc : lampadaires ou poteaux et fils, cocotiers, bric-à-brac.
        /// Les pièces métalliques rejoignent le maillage de façades.
        /// </summary>
        private static void DecorateBlock(Transform block, BoxMeshBuilder facade, District district, List<Mesh> meshes)
        {
            System.Random r = s_decor;
            var decor = new GameObject("Decor").transform;
            decor.SetParent(block, false);
            CityDecor.Frame = block.localToWorldMatrix;
            var kitBuilders = new Dictionary<Material, BoxMeshBuilder>();
            var bulbs = new BoxMeshBuilder();
            var sodiumBulbs = new BoxMeshBuilder();
            var pools = new BoxMeshBuilder();
            float curb = BlockSize / 2f - 0.5f;

            if (district.PowerPoles)
            {
                // Poteaux électriques réalistes tous les 17 m environ, reliés par deux fils ; une ampoule au sodium sur deux.
                float[] positions = { -25f, -8.5f, 8.5f, 25f };
                for (int side = 0; side < 4; side++)
                {
                    Vector3 outward = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.forward;
                    Vector3 along = Vector3.Cross(Vector3.up, outward);
                    (Vector3, Vector3) previous = default;
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Vector3 basePosition = outward * curb + along * positions[i] + Vector3.up * SidewalkHeight;
                        bool hasLamp = i % 2 == 0;
                        (Vector3, Vector3) anchors = CityDecor.AddPowerPole(kitBuilders, sodiumBulbs, pools,
                            basePosition, along, outward, hasLamp, SidewalkHeight);
                        if (i > 0)
                        {
                            CityDecor.AddWire(facade, previous.Item1, anchors.Item1, 0.5f);
                            CityDecor.AddWire(facade, previous.Item2, anchors.Item2, 0.55f);
                        }
                        previous = anchors;
                    }
                }
            }
            else
            {
                AddPerimeterLamps(kitBuilders, bulbs, pools, new[] { -17f, 17f });
            }

            BlenderCityKit.BuildObjects(decor, "MobilierKit", kitBuilders, meshes);

            // Cocotiers le long du trottoir, entre les lampadaires et les points de livraison.
            float[] palmSpots = { -24f, -9f, 9f, 24f };
            for (int side = 0; side < 4; side++)
            {
                Vector3 outward = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.forward;
                Vector3 along = Vector3.Cross(Vector3.up, outward);
                foreach (float t in palmSpots)
                {
                    if (r.NextDouble() >= district.PalmChance)
                        continue;
                    Vector3 position = outward * (BlockSize / 2f - 1.3f) + along * t + Vector3.up * SidewalkHeight;
                    PalmBuilder.Place(decor, position, r.Next(PalmBuilder.VariantCount), r.Next(360));
                }
            }

            // Vendeuse de boissons (eau en sachet, bissap…) dans les quartiers animés, sur un trottoir.
            if (district.HasFood || district == Marcory)
            {
                if (r.NextDouble() < 0.45)
                {
                    int side = r.Next(4);
                    Vector3 outward = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.forward;
                    Vector3 along = Vector3.Cross(Vector3.up, outward);
                    float t = r.Next(2) == 0 ? -13.5f : 13.5f;
                    s_pendingDrinks.Add((block, outward * (BlockSize / 2f - 2f) + along * t + Vector3.up * SidewalkHeight));
                }
            }

            // Bric-à-brac contre les façades (hors coins et milieux de côtés, où sont les points de jeu).
            for (int i = 0; i < district.ClutterCount; i++)
            {
                int side = r.Next(4);
                Vector3 outward = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.forward;
                Vector3 along = Vector3.Cross(Vector3.up, outward);
                float t = Mathf.Lerp(4f, 26f, (float)r.NextDouble()) * (r.Next(2) == 0 ? -1f : 1f);
                float inset = Mathf.Lerp(2.3f, 2.8f, (float)r.NextDouble());
                CityDecor.AddClutter(decor, outward * (BlockSize / 2f - inset) + along * t + Vector3.up * SidewalkHeight, r);
            }

            Material bulb = district.PowerPoles ? CityDecor.SodiumBulbMaterial : CityDecor.BulbMaterial;
            AddLightMeshes(decor, district.PowerPoles ? sodiumBulbs : bulbs, pools, bulb, meshes);
        }

        /// <summary>
        /// Adresses de livraison sur les trottoirs (au moins une par bloc), étals dans les quartiers
        /// commerçants et un dépôt par quartier.
        /// </summary>
        private static void CreateBlockInteractables(Transform interactables, Transform block, District district, Vector3 center,
            System.Random random, ref int nextId)
        {
            Material deliveryMaterial = GetOrCreateMaterial("M_Delivery", new Color(0.25f, 0.8f, 0.35f));
            Material shopMaterial = GetOrCreateMaterial("M_Shop", new Color(0.95f, 0.55f, 0.15f));
            Material jobMaterial = GetOrCreateMaterial("M_JobBoard", new Color(0.2f, 0.45f, 0.95f));

            // Sur le trottoir, à 1,5 m du bord : accessible depuis la rue, hors des bâtiments.
            float edge = BlockSize / 2f - 1.5f;
            Vector3[] sides =
            {
                new Vector3(0f, SidewalkHeight, edge), new Vector3(0f, SidewalkHeight, -edge),
                new Vector3(edge, SidewalkHeight, 0f), new Vector3(-edge, SidewalkHeight, 0f),
            };

            int deliveries = 1 + random.Next(2);
            int firstSide = random.Next(4);
            for (int i = 0; i < deliveries; i++)
            {
                Vector3 offset = sides[(firstSide + i) % 4];
                string placeName = district.PlaceTypes[random.Next(district.PlaceTypes.Length)] + " " +
                                   district.Name + " n°" + nextId;
                var point = CreatePoint<DeliveryPoint>(interactables, nextId++, placeName, center + offset);
                AddVisual(point.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(2f, 0.02f, 2f), deliveryMaterial, false);
                AddVisual(point.transform, PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f), new Vector3(0.15f, 2.4f, 0.15f), deliveryMaterial, true);
            }

            // Coin du bloc : étal de nourriture (quartiers commerçants, un bloc sur deux).
            if (district.HasFood && random.NextDouble() < 0.5)
            {
                float corner = BlockSize / 2f - 2f;
                var position = center + new Vector3(corner * (random.Next(2) * 2 - 1), SidewalkHeight, corner * (random.Next(2) * 2 - 1));
                CreateShop(interactables, ref nextId, Foods[random.Next(Foods.Length)], Vendors[random.Next(Vendors.Length)], position, shopMaterial);

                // Terrasse de maquis le long du trottoir, en direction du milieu du bloc.
                Vector3 local = position - center;
                var along = new Vector3(-Mathf.Sign(local.x), 0f, 0f);
                var inward = new Vector3(0f, 0f, -Mathf.Sign(local.z));
                CityDecor.AddMaquis(block.Find("Decor") ?? block, local, along, inward, s_decor);
            }

            // Un dépôt par quartier, dans le premier bloc rencontré de ce quartier.
            if (!s_districtsWithDepot.Contains(district.Name))
            {
                s_districtsWithDepot.Add(district.Name);
                var depotOffset = sides[(firstSide + 2) % 4];
                var board = CreatePoint<JobBoardPoint>(interactables, nextId++, "Dépôt Express " + district.Name, center + depotOffset * 0.92f);
                AddJobBoardVisual(board.transform, Vector3.zero, jobMaterial);
            }
        }

        private static readonly HashSet<string> s_districtsWithDepot = new HashSet<string>();

        private static void CreateShop(Transform parent, ref int nextId, FoodItem food, string vendor, Vector3 position, Material material)
        {
            var shop = CreatePoint<ShopPoint>(parent, nextId++, food.Stall + " de " + vendor, position);
            shop.EditorSetupShop(food.Item, food.Price, food.Satiety, food.Hydration);

            if (BlenderCityKit.HasModel("SM_FoodKiosk") && s_pendingMeshes != null)
            {
                var builders = new Dictionary<Material, BoxMeshBuilder>();
                Matrix4x4 xf = Matrix4x4.TRS(new Vector3(0f, 0f, 0.9f), Quaternion.Euler(0f, 180f, 0f), Vector3.one);
                BlenderCityKit.Stamp("SM_FoodKiosk", builders, xf, null, Color.white, s_decor.Next(256));
                BlenderCityKit.BuildObjects(shop.transform, "Etal", builders, s_pendingMeshes);
                AddBoxCollider(shop.gameObject, new Vector3(0f, 0.55f, 0.9f), new Vector3(2.35f, 1.1f, 0.8f));
                return;
            }

            // Étal : comptoir en bois, bâche colorée sur quatre piquets (le comptoir garde son collider).
            Material wood = CityMaterials.Plain("M_StallWood", new Color(0.42f, 0.28f, 0.16f), 0.2f);
            Material pole = CityMaterials.Plain("M_StallPole", new Color(0.25f, 0.25f, 0.25f), 0.4f);
            int canopyIndex = s_decor.Next(CanopyColors.Length);
            Material canopy = CityMaterials.Plain("M_StallCanopy_" + canopyIndex, CanopyColors[canopyIndex], 0.1f);
            AddVisual(shop.transform, PrimitiveType.Cube, new Vector3(0f, 0.5f, 0.8f), new Vector3(2f, 1f, 1f), wood, true);
            AddVisual(shop.transform, PrimitiveType.Cube, new Vector3(0f, 2.3f, 0.8f), new Vector3(2.6f, 0.06f, 1.8f), canopy, false);
            foreach (float px in new[] { -1.2f, 1.2f })
            {
                foreach (float pz in new[] { 0.0f, 1.6f })
                    AddVisual(shop.transform, PrimitiveType.Cylinder, new Vector3(px, 1.15f, pz), new Vector3(0.05f, 1.15f, 0.05f), pole, false);
            }
        }

        /// <summary>
        /// Panneau du dépôt : tableau d'affichage modélisé dans Blender (socles béton, poteaux acier, auvent, fiches).
        /// </summary>
        private static void AddJobBoardVisual(Transform parent, Vector3 offset, Material boardMaterial)
        {
            if (BlenderCityKit.HasModel("SM_JobBoard") && s_pendingMeshes != null)
            {
                var builders = new Dictionary<Material, BoxMeshBuilder>();
                BlenderCityKit.Stamp("SM_JobBoard", builders, Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one),
                    boardMaterial, Color.white, 0);
                BlenderCityKit.BuildObjects(parent, "PanneauDepot", builders, s_pendingMeshes);
                AddBoxCollider(parent.gameObject, offset + new Vector3(0f, 1.4f, 0f), new Vector3(2.45f, 2.4f, 0.30f));
                return;
            }
            Material pole = CityMaterials.Plain("M_StallPole", new Color(0.22f, 0.23f, 0.25f), 0.45f);
            Material accent = CityMaterials.Plain("M_JobAccent", new Color(0.95f, 0.58f, 0.12f), 0.25f);
            Material paper = CityMaterials.Plain("M_JobPaper", new Color(0.95f, 0.93f, 0.88f), 0.1f);
            Material noteYellow = CityMaterials.Plain("M_JobNoteYellow", new Color(0.98f, 0.88f, 0.45f), 0.1f);

            AddVisual(parent, PrimitiveType.Cube, offset + new Vector3(0f, 1.55f, 0f), new Vector3(2.45f, 1.32f, 0.10f), boardMaterial, true);
            // Cadre périmétrique + bandeau supérieur orange.
            AddVisual(parent, PrimitiveType.Cube, offset + new Vector3(0f, 2.22f, 0f), new Vector3(2.62f, 0.14f, 0.16f), pole, false);
            AddVisual(parent, PrimitiveType.Cube, offset + new Vector3(0f, 0.88f, 0f), new Vector3(2.62f, 0.10f, 0.16f), pole, false);
            AddVisual(parent, PrimitiveType.Cube, offset + new Vector3(0f, 2.36f, 0.04f), new Vector3(2.1f, 0.22f, 0.14f), accent, false);
            AddVisual(parent, PrimitiveType.Cube, offset + new Vector3(0f, 2.52f, 0.14f), new Vector3(2.85f, 0.08f, 0.58f), pole, false);
            foreach (float x in new[] { -1.25f, 1.25f })
            {
                AddVisual(parent, PrimitiveType.Cylinder, offset + new Vector3(x, 1.25f, 0f), new Vector3(0.08f, 1.25f, 0.08f), pole, false);
                AddVisual(parent, PrimitiveType.Cube, offset + new Vector3(x, 0.10f, 0f), new Vector3(0.28f, 0.20f, 0.28f), pole, false);
            }

            // Fiches d'annonces et bons de livraison punaisés (des deux faces).
            for (int i = 0; i < 6; i++)
            {
                float fx = -0.88f + i * 0.35f;
                float fy = 1.32f + (i % 2) * 0.36f;
                Material sheet = i % 3 == 1 ? noteYellow : paper;
                AddVisual(parent, PrimitiveType.Cube, offset + new Vector3(fx, fy, 0.06f), new Vector3(0.26f, 0.34f, 0.01f), sheet, false);
                AddVisual(parent, PrimitiveType.Cube, offset + new Vector3(-fx, fy, -0.06f), new Vector3(0.26f, 0.34f, 0.01f), sheet, false);
            }
        }

        private static readonly Color[] CanopyColors =
        {
            new Color(0.80f, 0.18f, 0.15f), new Color(0.95f, 0.65f, 0.10f), new Color(0.15f, 0.45f, 0.75f), new Color(0.20f, 0.55f, 0.30f),
        };

        #endregion

        #region Utilitaires

        private static T CreatePoint<T>(Transform parent, int id, string displayName, Vector3 position) where T : WorldInteractable
        {
            var go = new GameObject(displayName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.isStatic = true;
            var point = go.AddComponent<T>();
            point.EditorSetup(id, displayName);
            return point;
        }

        private static void AddVisual(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool collider)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = type.ToString();
            if (!collider)
                Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = scale;
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;
            visual.isStatic = true;
        }

        private static GameObject AddMeshObject(Transform parent, string name, BoxMeshBuilder builder, Material material, List<Mesh> meshes)
        {
            if (builder.IsEmpty)
                return null;

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.isStatic = true;

            Mesh mesh = builder.Build(parent.name + "_" + name);
            meshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// Trottoirs et places : zone NavMesh n° 3 (même coût que la chaussée pour les piétons ; les
        /// véhicules pilotés par chemin la paient cher, voir NavPathFollower.SidewalkCost).
        /// </summary>
        private static void MarkSidewalkArea(GameObject go)
        {
            var modifier = go.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = Gameplay.Bots.NavPathFollower.SidewalkArea;
            modifier.applyToChildren = true;
        }

        private static void AddBoxCollider(GameObject go, Vector3 center, Vector3 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder(MaterialFolder))
                    AssetDatabase.CreateFolder("Assets/_Project/Art", "Materials");
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Tous les maillages générés dans un seul asset (remplacé à chaque génération). Le premier
        /// maillage sert d'objet principal : aucun script spécifique, donc rien d'éditeur dans les builds.
        /// </summary>
        private static void SaveMeshes(List<Mesh> meshes)
        {
            EnsureFolder("Assets/_Project/Art/Generated");
            AssetDatabase.DeleteAsset(MeshAssetPath);
            if (meshes.Count == 0)
                return;

            AssetDatabase.CreateAsset(meshes[0], MeshAssetPath);
            for (int i = 1; i < meshes.Count; i++)
                AssetDatabase.AddObjectToAsset(meshes[i], meshes[0]);

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// NavMesh de la ville : chemins pour les bots, et plus tard pour les piétons et véhicules PNJ.
        /// Calculé à partir des colliders (sol, trottoirs, bâtiments, quais).
        /// </summary>
        private static void BakeNavMesh(GameObject city)
        {
            var surface = city.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~(1 << Core.GameLayers.Player);
            surface.BuildNavMesh();

            AssetDatabase.DeleteAsset(NavMeshAssetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAssetPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        #endregion
    }
}
