using System.Collections.Generic;
using IvoryState.Gameplay.Interaction;
using UnityEditor;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Lieux de vie de la ville : superettes où l'on entre (vendeuse derrière le comptoir), marchés
    /// (étals tenus par des marchands), gares de gbaka, terrains de foot et de basket, parcs, églises,
    /// mosquées, feux tricolores.
    /// <para>
    /// Chaque bloc « spécial » remplace un bloc d'immeubles de la grille (voir <see cref="SpecialBlocks"/>).
    /// Les vendeurs sont des <see cref="Client.Characters.VendorNpc"/> : purement visuels, créés sur
    /// les clients seulement.
    /// </para>
    /// </summary>
    internal static partial class CityGenerator
    {
        private enum Special
        {
            None, Pyramide, Cathedrale, GrandeMosquee, GrandMarche, GareRoutiere, Stade, Mosquee,
            HotelIvoire, ParcBasket, Gare, Foot, Basket, Eglise, PalaisCulture, MarcheCouvert,
        }

        /// <summary>Blocs remplacés par un lieu particulier, dans leur commune.</summary>
        private static readonly Dictionary<Vector2Int, (Special kind, string name)> SpecialBlocks = new Dictionary<Vector2Int, (Special, string)>
        {
            // Plateau
            { new Vector2Int(-1, 0), (Special.Pyramide, "La Pyramide") },
            { new Vector2Int(1, 1), (Special.Cathedrale, "Cathédrale Saint-Paul") },
            { new Vector2Int(-1, 1), (Special.GrandeMosquee, "Grande Mosquée du Plateau") },
            // Adjamé
            { new Vector2Int(0, 3), (Special.GrandMarche, "Grand Marché d'Adjamé") },
            { new Vector2Int(-1, 4), (Special.GareRoutiere, "Gare routière d'Adjamé") },
            { new Vector2Int(1, 2), (Special.Stade, "Stade Félix Houphouët-Boigny") },
            { new Vector2Int(1, 4), (Special.Mosquee, "Mosquée d'Adjamé") },
            // Cocody
            { new Vector2Int(3, 0), (Special.HotelIvoire, "Hôtel Ivoire") },
            { new Vector2Int(2, 2), (Special.ParcBasket, "Jardin de Cocody") },
            { new Vector2Int(4, 4), (Special.Gare, "Gare de Cocody") },
            // Yopougon
            { new Vector2Int(-3, 2), (Special.Foot, "Terrain de Yopougon") },
            { new Vector2Int(-2, 3), (Special.Basket, "Playground de Yopougon") },
            { new Vector2Int(-3, 4), (Special.Gare, "Gare de Yopougon") },
            { new Vector2Int(-2, 1), (Special.Eglise, "Église Saint-André de Yopougon") },
            // Treichville
            { new Vector2Int(-2, -2), (Special.PalaisCulture, "Palais de la Culture") },
            { new Vector2Int(-1, -3), (Special.MarcheCouvert, "Marché de Treichville") },
            { new Vector2Int(-3, -3), (Special.Foot, "Terrain de Treichville") },
            { new Vector2Int(-3, -4), (Special.Mosquee, "Mosquée de Treichville") },
            { new Vector2Int(-4, -2), (Special.Gare, "Gare de Treichville") },
            // Marcory
            { new Vector2Int(3, -2), (Special.ParcBasket, "Parc de Marcory") },
            { new Vector2Int(2, -3), (Special.Gare, "Gare de Marcory") },
            { new Vector2Int(3, -4), (Special.Eglise, "Paroisse Sainte-Thérèse de Marcory") },
        };

        private static readonly string[] VendorNames =
        {
            "Tantie Awa", "Maman Adjoua", "Tantie Rose", "Mama Brou", "Tantie Fanta", "Maman Akissi", "Tantie Mariam",
            "Vieux Koffi", "Tonton Bamba", "Mama Kouassi", "Tantie Aya", "Maman Salimata", "Tonton Yao", "Tantie Clémentine",
        };

        private static Client.Characters.CharacterModels s_characterModels;
        private static int s_vendorIndex;

        private static StorePoint.Product Product(string name, long price, int satiety, int hydration)
        {
            return new StorePoint.Product { Name = name, Price = price, Satiety = satiety, Hydration = hydration };
        }

        private static string NextVendorName() => VendorNames[s_decor.Next(VendorNames.Length)];

        #region Catalogues

        private static StorePoint.Product[] SuperetteProducts() => new[]
        {
            Product("Eau minérale 1,5 L", 400, 0, 60),
            Product("Sucrerie bien glacée", 400, 5, 35),
            Product("Pain baguette", 200, 20, 0),
            Product("Pain + sardines", 700, 35, 0),
            Product("Biscuits", 250, 12, 0),
            Product("Yaourt", 300, 10, 10),
        };

        private static readonly (string stall, StorePoint.Product[] products, Color[] produce)[] MarketStalls =
        {
            ("Fruits", new[] { Product("Bananes (3)", 200, 10, 5), Product("Mangue", 250, 12, 10), Product("Ananas en tranches", 300, 10, 20), Product("Papaye", 300, 12, 15) },
                new[] { new Color(0.95f, 0.80f, 0.15f), new Color(0.95f, 0.55f, 0.10f), new Color(0.45f, 0.65f, 0.15f) }),
            ("Poisson braisé", new[] { Product("Attiéké poisson braisé", 1500, 55, 0), Product("Garba", 1000, 40, 0), Product("Alloco poisson", 1000, 35, 0) },
                new[] { new Color(0.55f, 0.50f, 0.42f), new Color(0.90f, 0.85f, 0.70f), new Color(0.75f, 0.45f, 0.20f) }),
            ("Plats du jour", new[] { Product("Placali sauce graine", 1200, 50, 0), Product("Foutou sauce claire", 1500, 55, 5), Product("Riz sauce arachide", 1000, 45, 0) },
                new[] { new Color(0.85f, 0.35f, 0.15f), new Color(0.95f, 0.92f, 0.85f), new Color(0.60f, 0.35f, 0.20f) }),
            ("Boissons fraîches", new[] { Product("Eau en sachet", 100, 0, 35), Product("Bissap", 300, 5, 45), Product("Gnamakoudji", 300, 5, 45), Product("Noix de coco fraîche", 500, 5, 50) },
                new[] { new Color(0.65f, 0.10f, 0.20f), new Color(0.90f, 0.85f, 0.40f), new Color(0.35f, 0.55f, 0.20f) }),
            ("Beignets", new[] { Product("Beignets (5)", 250, 15, 0), Product("Pain + omelette", 600, 30, 0), Product("Gâteau", 200, 10, 0) },
                new[] { new Color(0.80f, 0.55f, 0.25f), new Color(0.95f, 0.85f, 0.45f), new Color(0.70f, 0.45f, 0.25f) }),
            ("Vivriers", new[] { Product("Igname braisée", 500, 30, 0), Product("Maïs grillé", 200, 12, 0), Product("Arachides", 200, 15, 0) },
                new[] { new Color(0.55f, 0.40f, 0.25f), new Color(0.95f, 0.80f, 0.30f), new Color(0.80f, 0.65f, 0.45f) }),
        };

        private static StorePoint.Product[] KioskProducts() => new[]
        {
            Product("Eau en sachet", 100, 0, 35),
            Product("Sucrerie", 400, 5, 35),
            Product("Pain + omelette", 600, 30, 0),
            Product("Café Touba", 200, 2, 15),
        };

        #endregion

        #region Vendeurs

        private static void AddVendor(Transform parent, Vector3 localPosition, float yaw, int model, bool lively)
        {
            if (s_characterModels == null)
                s_characterModels = AssetDatabase.LoadAssetAtPath<Client.Characters.CharacterModels>(Characters.CharacterSetup.ModelsPath);
            var go = new GameObject("Vendeur");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<Client.Characters.VendorNpc>().EditorSetup(s_characterModels, model, lively);
        }

        private static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        /// <summary>Petit kiosque modélisé dans Blender (comptoir, panneau menu, auvent incliné), au point d'un StorePoint.</summary>
        private static void AddKiosk(Transform store, List<Mesh> meshes)
        {
            if (BlenderCityKit.HasModel("SM_FoodKiosk"))
            {
                var builders = new Dictionary<Material, BoxMeshBuilder>();
                Matrix4x4 xf = Matrix4x4.TRS(new Vector3(0f, 0f, 1.15f), Quaternion.Euler(0f, 180f, 0f), Vector3.one);
                BlenderCityKit.Stamp("SM_FoodKiosk", builders, xf, null, Color.white, s_decor.Next(256));
                BlenderCityKit.BuildObjects(store, "Kiosque", builders, meshes);
                AddBoxCollider(store.gameObject, new Vector3(0f, 0.55f, 1.15f), new Vector3(2.35f, 1.1f, 0.8f));
                return;
            }
            var b = new BoxMeshBuilder();
            b.SetColor(new Color(0.95f, 0.55f, 0.12f), 11);
            b.AddBox(new Vector3(0f, 0f, 1.2f), new Vector3(2.4f, 1.05f, 0.7f));
            b.SetColor(new Color(0.85f, 0.85f, 0.82f), 12);
            b.AddBox(new Vector3(0f, 0f, 2.2f), new Vector3(2.4f, 2.4f, 0.15f));
            b.SetColor(new Color(0.55f, 0.56f, 0.58f), 13);
            b.AddRotatedBox(new Vector3(0f, 2.6f, 1.5f), new Vector3(3f, 0.06f, 2.2f), Quaternion.Euler(-8f, 0f, 0f));
            foreach (float x in new[] { -1.15f, 1.15f })
                b.AddBox(new Vector3(x, 0f, 0.6f), new Vector3(0.08f, 2.5f, 0.08f));
            AddMeshObject(store, "Kiosque", b, CityMaterials.Painted(), meshes);
            AddBoxCollider(store.gameObject, new Vector3(0f, 0.55f, 1.2f), new Vector3(2.4f, 1.1f, 0.7f));
        }

        #endregion

        #region Superette (bâtiment où l'on entre)

        /// <summary>
        /// Superette au rez-de-chaussée d'un immeuble : porte côté rue, rayons, frigo, comptoir et
        /// vendeuse. Les étages au-dessus restent pleins. Remplace le bâtiment du lot.
        /// </summary>
        private static void AddSuperette(GameObject block, BoxMeshBuilder facade, BoxMeshBuilder interior, District district,
            Vector3 baseCenter, float footprint, float height, Color color, Vector3 street, Transform interactables, ref int nextId)
        {
            const float storey = 4.0f, wall = 0.3f, door = 2.6f, doorH = 2.95f;
            Vector3 across = Vector3.Cross(Vector3.up, street);
            float half = footprint * 0.5f;
            height = Mathf.Max(height, storey + 3f);

            // Murs du rez-de-chaussée : façade percée d'une haute porte vitrée encadrée, trois murs pleins.
            facade.SetColor(color, s_decor.Next(256));
            float side = (footprint - door) * 0.5f;
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 c = baseCenter + street * (half - wall * 0.5f) + across * s * (door * 0.5f + side * 0.5f);
                Vector3 size = Oriented(street, wall, storey, side);
                facade.AddBox(c, size);
                AddBoxCollider(block, c + Vector3.up * storey * 0.5f, size);
            }
            Vector3 lintel = baseCenter + street * (half - wall * 0.5f) + Vector3.up * doorH;
            facade.AddBox(lintel, Oriented(street, wall, storey - doorH, door));
            // Encadrement de la porte de boutique côté trottoir.
            interior.SetColor(new Color(0.20f, 0.22f, 0.24f), 8);
            foreach (float s in new[] { -1f, 1f })
                interior.AddBox(baseCenter + street * (half - wall * 0.5f) + across * s * (door * 0.5f + 0.04f), Oriented(street, wall + 0.06f, doorH, 0.09f));
            interior.AddBox(baseCenter + street * (half - wall * 0.5f) + Vector3.up * doorH, Oriented(street, wall + 0.06f, 0.09f, door + 0.16f));

            Vector3 back = baseCenter - street * (half - wall * 0.5f);
            facade.AddBox(back, Oriented(street, wall, storey, footprint));
            AddBoxCollider(block, back + Vector3.up * storey * 0.5f, Oriented(street, wall, storey, footprint));
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 c = baseCenter + across * s * (half - wall * 0.5f);
                Vector3 size = Oriented(street, footprint, storey, wall);
                facade.AddBox(c, size);
                AddBoxCollider(block, c + Vector3.up * storey * 0.5f, size);
            }

            // Étages pleins au-dessus, dalle-plafond (visible de l'intérieur).
            Vector3 upper = baseCenter + Vector3.up * storey;
            facade.AddBuilding(upper, new Vector3(footprint, height - storey, footprint));
            AddBoxCollider(block, upper + Vector3.up * (height - storey) * 0.5f, new Vector3(footprint, height - storey, footprint));
            interior.SetColor(new Color(0.92f, 0.92f, 0.90f), 1);
            interior.AddRotatedBox(baseCenter + Vector3.up * (storey - 0.1f), new Vector3(footprint - 0.1f, 0.2f, footprint - 0.1f), Quaternion.identity);

            // Enseigne et auvent.
            facade.SetColor(SignColors[s_decor.Next(SignColors.Length)], 3);
            facade.AddBox(baseCenter + street * (half + 0.08f) + Vector3.up * 3.35f, Oriented(street, 0.15f, 0.55f, Mathf.Min(footprint * 0.8f, 6f)));
            facade.SetColor(AwningColors[s_decor.Next(AwningColors.Length)], 4);
            facade.AddBox(baseCenter + street * (half + 0.75f) + Vector3.up * 3.05f, Oriented(street, 1.5f, 0.06f, footprint * 0.9f));

            // Intérieur : carrelage, rayons 3D, frigo, comptoir avec caisse enregistreuse et néons.
            interior.SetColor(new Color(0.85f, 0.83f, 0.78f), 2);
            interior.AddFlatQuad(baseCenter + Vector3.up * 0.01f, street, footprint - wall * 2f, footprint - wall * 2f);
            float depth = footprint - wall * 2f;
            float alongYaw = YawOf(street);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 shelf = baseCenter + across * s * (half - wall - 0.35f) + street * 0.8f;
                Vector3 shelfSize = Oriented(street, depth * 0.55f, 2f, 0.6f);
                AddBoxCollider(block, shelf + Vector3.up, shelfSize);
                PropLibrary.Place("steel_frame_shelves_01", block.transform, shelf, alongYaw + (s > 0 ? 270f : 90f), 1.0f, false, 0.015f, false);
                // Produits sur les tablettes : rangées de boîtes colorées.
                for (int row = 0; row < 3; row++)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        interior.SetColor(SignColors[(row * 7 + k * 3 + (s > 0 ? 1 : 0)) % SignColors.Length], k);
                        float along = -0.36f + k * 0.18f;
                        interior.AddBox(shelf + street * along + Vector3.up * (0.45f + row * 0.52f), new Vector3(0.16f, 0.22f, 0.16f));
                    }
                }
            }
            Vector3 fridge = baseCenter - street * (half - wall - 0.45f) + across * (half - wall - 0.9f);
            interior.SetColor(new Color(0.95f, 0.96f, 0.98f), 5);
            interior.AddBox(fridge, Oriented(street, 0.8f, 2f, 1.2f));
            interior.SetColor(new Color(0.75f, 0.12f, 0.12f), 6);
            interior.AddBox(fridge + street * 0.41f + Vector3.up * 1.6f, Oriented(street, 0.02f, 0.3f, 1.1f));

            Vector3 counter = baseCenter - street * (half - wall - 2.1f) - across * 0.6f;
            interior.SetColor(new Color(0.40f, 0.28f, 0.18f), 7);
            interior.AddBox(counter, Oriented(street, 0.7f, 1.05f, 2.6f));
            AddBoxCollider(block, counter + Vector3.up * 0.52f, Oriented(street, 0.7f, 1.05f, 2.6f));
            PropLibrary.Place("CashRegister_01", block.transform, counter + Vector3.up * 1.05f, alongYaw, 0.65f, false, 0.01f, false);
            // Plafonnier LED léger fusionné (~12 tris) au lieu du néon FBX lourd + accessoires boutique.
            interior.SetColor(new Color(0.96f, 0.97f, 0.98f), 9);
            interior.AddBox(baseCenter + Vector3.up * (storey - 0.24f), Oriented(street, 1.5f, 0.06f, 0.45f));
            PropLibrary.Place("WetFloorSign_01", block.transform, baseCenter + street * 0.35f + across * 1.1f, alongYaw + 20f, 1.0f, false, 0.015f, false);
            PropLibrary.Place("standing_chalkboard_01", block.transform, baseCenter + street * (half + 0.85f) + across * 1.5f, alongYaw + 15f, 0.92f, false, 0.018f, false);

            // Vendeuse derrière le comptoir, tournée vers la porte ; point d'achat devant le comptoir.
            string vendor = NextVendorName();
            Transform blockTransform = block.transform;
            Vector3 vendorLocal = counter - street * 0.9f;
            AddVendor(blockTransform, vendorLocal, YawOf(street), s_vendorIndex++, false);
            Vector3 customer = blockTransform.TransformPoint(counter + street * 1.3f);
            var store = CreatePoint<StorePoint>(interactables, nextId++, "Superette de " + vendor, customer);
            store.EditorSetupStore(vendor, "Bonjour ! Tout est frais ici, qu'est-ce qu'il te faut ?", SuperetteProducts());
        }

        #endregion

        #region Blocs spéciaux

        /// <summary>Lieu particulier (monument, marché, gare…) sur une dalle de 64 m, posée et tournée.</summary>
        private static void CreateSpecialBlock(Transform city, Transform interactables, District district, Special kind, string name,
            Vector3 center, float yaw, Material sidewalk, List<Mesh> meshes, ref int nextId)
        {
            (Special kind, string name) special = (kind, name);
            var block = new GameObject(special.name);
            block.transform.SetParent(city, false);
            block.transform.localPosition = center;
            block.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            block.isStatic = true;
            MarkSidewalkArea(block);

            if (ArchitectureSetup.ReplacesSpecialBlock(special.kind.ToString()))
            {
                // Les monuments détaillés modélisés sous Blender remplacent le bloc procédural tout en gardant des vendeurs en bordure de parvis.
                if (special.kind == Special.Cathedrale)
                {
                    AddVendor(block.transform, new Vector3(-8f, 0.15f, 37.5f), 180f, s_vendorIndex++, false);
                    AddVendor(block.transform, new Vector3(8f, 0.15f, 37.5f), 200f, s_vendorIndex++, false);
                }
                else if (special.kind == Special.GrandeMosquee)
                {
                    AddVendor(block.transform, new Vector3(-7f, 0.15f, 38.0f), 180f, s_vendorIndex++, false);
                }
                return;
            }

            var slab = new BoxMeshBuilder();
            slab.AddBox(Vector3.zero, new Vector3(BlockSize, SidewalkHeight, BlockSize));
            AddMeshObject(block.transform, "Trottoir", slab, sidewalk, meshes);
            AddBoxCollider(block, new Vector3(0f, SidewalkHeight / 2f, 0f), new Vector3(BlockSize, SidewalkHeight, BlockSize));

            var painted = new BoxMeshBuilder();   // matériau peint (teinte par sommet)
            var facade = new BoxMeshBuilder();    // matériau du quartier (fenêtres)
            var ground = new BoxMeshBuilder();    // sols (gazon, latérite, terrain…)
            Material groundMaterial = CityMaterials.Painted();
            Transform t = block.transform;
            float y = SidewalkHeight;

            switch (special.kind)
            {
                case Special.Pyramide: Landmarks.Pyramide(block, facade, painted, ground, y); AddPark(block, painted, ground, y, new Vector3(0f, 0f, -19f), 9f, meshes); break;
                case Special.Cathedrale: Landmarks.Cathedrale(block, painted, ground, y); break;
                case Special.GrandeMosquee: Landmarks.Mosquee(block, painted, ground, y, true); break;
                case Special.Mosquee: Landmarks.Mosquee(block, painted, ground, y, false); break;
                case Special.Eglise: Landmarks.Eglise(block, painted, ground, y); break;
                case Special.Stade: Landmarks.Stade(block, painted, ground, y); AddStoreKiosk(t, interactables, new Vector3(0f, y, -27f), 0f, "Buvette du stade", meshes, ref nextId); break;
                case Special.HotelIvoire: Landmarks.HotelIvoire(block, facade, painted, ground, y, meshes); break;
                case Special.PalaisCulture: Landmarks.PalaisCulture(block, painted, ground, y); break;
                case Special.GrandMarche: CreateMarket(block, painted, ground, y, false, interactables, meshes, ref nextId); break;
                case Special.MarcheCouvert: CreateMarket(block, painted, ground, y, true, interactables, meshes, ref nextId); break;
                case Special.GareRoutiere: CreateGbakaStation(block, painted, ground, y, special.name, district.Name, 8, interactables, meshes, ref nextId); break;
                case Special.Gare: CreateGbakaStation(block, painted, ground, y, special.name, district.Name, 4, interactables, meshes, ref nextId); break;
                case Special.Foot: CreateFootballField(block, painted, ground, y, interactables, meshes, ref nextId); break;
                case Special.Basket: CreateBasketballCourt(block, painted, ground, y, Vector3.zero); AddPark(block, painted, ground, y, new Vector3(0f, 0f, 20f), 10f, meshes); break;
                case Special.ParcBasket:
                    AddPark(block, painted, ground, y, new Vector3(0f, 0f, -8f), 20f, meshes);
                    CreateBasketballCourt(block, painted, ground, y, new Vector3(0f, 0f, 19f));
                    AddStoreKiosk(t, interactables, new Vector3(-22f, y, -24f), 0f, "Kiosque du parc", meshes, ref nextId);
                    break;
            }

            // Lampadaires, cocotiers et bric-à-brac du trottoir, comme les autres blocs.
            var metal = new BoxMeshBuilder();
            DecorateBlock(t, metal, district, meshes);
            AddMeshObject(t, "Mobilier", metal, s_facades[district], meshes);

            AddMeshObject(t, "Sols", ground, groundMaterial, meshes)?.GetComponent<MeshRenderer>().SetShadowsOff();
            AddMeshObject(t, "Monument", painted, CityMaterials.Painted(), meshes);
            AddMeshObject(t, "Batiments", facade, s_facades[district], meshes);
        }

        private static void AddStoreKiosk(Transform block, Transform interactables, Vector3 local, float yaw, string name,
            List<Mesh> meshes, ref int nextId)
        {
            string vendor = NextVendorName();
            var store = CreatePoint<StorePoint>(interactables, nextId++, name, block.TransformPoint(local));
            store.transform.rotation = block.rotation * Quaternion.Euler(0f, yaw, 0f);
            store.EditorSetupStore(vendor, "Viens, viens ! C'est frais !", KioskProducts());
            AddKiosk(store.transform, meshes);
            AddVendor(store.transform, new Vector3(0f, 0f, 1.75f), 180f, s_vendorIndex++, true);
        }

        #endregion

        #region Marché

        /// <summary>
        /// Marché : allées d'étals sous bâches colorées (ou sous une grande halle), chaque étal tenu par
        /// un marchand avec ses produits (fruits, poisson braisé, plats, boissons, beignets, vivriers).
        /// </summary>
        private static void CreateMarket(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y, bool covered,
            Transform interactables, List<Mesh> meshes, ref int nextId)
        {
            ground.SetColor(new Color(0.62f, 0.45f, 0.32f), 3);
            ground.AddFlatQuad(new Vector3(0f, y + 0.01f, 0f), Vector3.right, 56f, 56f);

            int stallIndex = 0;
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    var stall = new Vector3(-20f + col * 10f, y, -19.5f + row * 13f);
                    var info = MarketStalls[(row * 5 + col + s_decor.Next(2)) % MarketStalls.Length];
                    AddMarketStall(block, painted, stall, info.produce, covered);

                    string vendor = NextVendorName();
                    var point = CreatePoint<StorePoint>(interactables, nextId++, info.stall + " de " + vendor,
                        block.transform.TransformPoint(stall + new Vector3(0f, 0f, -1.9f)));
                    point.EditorSetupStore(vendor, MarketGreeting(stallIndex++), info.products);
                    AddVendor(block.transform, stall + new Vector3(0f, 0f, 1.3f), 180f, s_vendorIndex++, true);
                }
            }

            if (covered)
            {
                // Halle : piliers et grande toiture en tôle à deux pentes, lanterneau au faîte.
                painted.SetColor(new Color(0.80f, 0.78f, 0.72f), 21);
                for (int i = -2; i <= 2; i++)
                {
                    foreach (float z in new[] { -27f, 27f })
                        painted.AddBox(new Vector3(i * 13f, y, z), new Vector3(0.6f, 7f, 0.6f));
                }
                foreach (int i in new[] { -2, 2 })
                {
                    foreach (float z in new[] { -27f, 27f })
                        AddBoxCollider(block, new Vector3(i * 13f, y + 3.5f, z), new Vector3(0.6f, 7f, 0.6f));
                }
                painted.SetColor(new Color(0.55f, 0.58f, 0.60f), 22);
                painted.AddRotatedBox(new Vector3(0f, y + 8.2f, -14f), new Vector3(58f, 0.12f, 29f), Quaternion.Euler(9f, 0f, 0f));
                painted.AddRotatedBox(new Vector3(0f, y + 8.2f, 14f), new Vector3(58f, 0.12f, 29f), Quaternion.Euler(-9f, 0f, 0f));
                painted.SetColor(new Color(0.85f, 0.35f, 0.15f), 23);
                painted.AddBox(new Vector3(0f, y + 10.4f, 0f), new Vector3(58f, 0.8f, 3f));
                // Fronton d'entrée.
                painted.SetColor(new Color(0.95f, 0.65f, 0.15f), 24);
                painted.AddBox(new Vector3(0f, y + 6.6f, -27.4f), new Vector3(20f, 1.4f, 0.3f));
            }
            else
            {
                // Portique d'entrée coloré.
                painted.SetColor(new Color(0.20f, 0.55f, 0.30f), 25);
                foreach (float x in new[] { -5f, 5f })
                {
                    painted.AddBox(new Vector3(x, y, -28f), new Vector3(0.6f, 5f, 0.6f));
                    AddBoxCollider(block, new Vector3(x, y + 2.5f, -28f), new Vector3(0.6f, 5f, 0.6f));
                }
                painted.SetColor(new Color(0.95f, 0.65f, 0.15f), 26);
                painted.AddBox(new Vector3(0f, y + 5f, -28f), new Vector3(11f, 1.3f, 0.5f));
            }
        }

        private static string MarketGreeting(int index)
        {
            string[] greetings =
            {
                "Ma chérie, viens voir, c'est pas cher !", "Mon fils, approche, je te fais un bon prix !",
                "C'est frais de ce matin, goûte seulement !", "Viens, on va s'arranger !", "Client, client ! Viens manger !",
            };
            return greetings[index % greetings.Length];
        }

        private static void AddMarketStall(GameObject block, BoxMeshBuilder b, Vector3 stall, Color[] produce, bool covered)
        {
            // Table en bois.
            b.SetColor(new Color(0.45f, 0.32f, 0.20f), s_decor.Next(256));
            b.AddBox(stall, new Vector3(3f, 0.9f, 1.3f));
            AddBoxCollider(block, stall + new Vector3(0f, 0.45f, 0f), new Vector3(3f, 0.9f, 1.3f));
            // Produits en tas + régimes de bananes et caisses.
            for (int i = 0; i < 6; i++)
            {
                b.SetColor(produce[i % produce.Length], s_decor.Next(256));
                var p = stall + new Vector3(-1.1f + (i % 3) * 1.1f, 0.9f, -0.3f + (i / 3) * 0.6f);
                b.AddEllipsoid(p + Vector3.up * 0.12f, new Vector3(0.35f, 0.16f, 0.25f), 6, 3);
            }
            if (s_decor.Next(3) == 0)
                PropLibrary.Place("bananas", block.transform, stall + new Vector3(0.8f, 0.94f, -0.1f), s_decor.Next(360), 1.0f, false, 0.015f, false);
            PropLibrary.Place(s_decor.Next(2) == 0 ? "wooden_crate_01" : "plastic_crate_01", block.transform,
                stall + new Vector3(-1.85f, 0f, 0.2f), s_decor.Next(360), 1.0f, false, 0.018f);
            // Bassines et seaux au sol.
            b.SetColor(new Color(0.15f, 0.35f, 0.75f), 3);
            b.AddCylinder(stall + new Vector3(1.8f, 0f, 0.4f), 0.35f, 0.35f, 8);
            if (covered)
                return;
            // Bâche sur quatre piquets.
            b.SetColor(CanopyColors[s_decor.Next(CanopyColors.Length)], 5);
            b.AddRotatedBox(stall + new Vector3(0f, 2.5f, 0.2f), new Vector3(3.6f, 0.05f, 3.2f), Quaternion.Euler(-6f, 0f, 0f));
            b.SetColor(new Color(0.30f, 0.30f, 0.30f), 6);
            foreach (float px in new[] { -1.7f, 1.7f })
            {
                foreach (float pz in new[] { -1.3f, 1.6f })
                    b.AddBox(stall + new Vector3(px, 0f, pz), new Vector3(0.07f, pz > 0f ? 2.65f : 2.35f, 0.07f));
            }
        }

        #endregion

        #region Gares de gbaka

        /// <summary>
        /// Gare de gbaka : parking en bitume, abri de quai avec bancs 3D, minibus garés, diables/colis,
        /// apprenti qui appelle les voyageurs, kiosque. Le point de la gare est devant l'abri.
        /// </summary>
        private static void CreateGbakaStation(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y, string name,
            string commune, int buses, Transform interactables, List<Mesh> meshes, ref int nextId)
        {
            ground.SetColor(new Color(0.30f, 0.30f, 0.31f), 2);
            ground.AddFlatQuad(new Vector3(0f, y + 0.01f, 0f), Vector3.right, 56f, 56f);
            // Places marquées.
            ground.SetColor(new Color(0.90f, 0.88f, 0.80f), 1);
            for (int i = 0; i <= buses; i++)
                ground.AddFlatQuad(new Vector3(-24f + i * (48f / Mathf.Max(1, buses)), y + 0.02f, 4f), Vector3.forward, 7f, 0.15f);

            // Abri de quai le long du côté sud avec bancs en bois 3D et colis de voyageurs.
            painted.SetColor(new Color(0.85f, 0.83f, 0.78f), 31);
            for (int i = -3; i <= 3; i++)
            {
                painted.AddBox(new Vector3(i * 7f, y, -18f), new Vector3(0.35f, 3.4f, 0.35f));
                AddBoxCollider(block, new Vector3(i * 7f, y + 1.7f, -18f), new Vector3(0.35f, 3.4f, 0.35f));
            }
            painted.SetColor(new Color(0.20f, 0.50f, 0.30f), 32);
            painted.AddRotatedBox(new Vector3(0f, y + 3.6f, -20f), new Vector3(46f, 0.15f, 6f), Quaternion.Euler(-5f, 0f, 0f));
            for (int i = -2; i <= 2; i++)
                PropLibrary.Place("painted_wooden_bench", block.transform, new Vector3(i * 8f + 4f, y, -21f), 0f, 1.35f, true, 0.015f);
            PropLibrary.Place("hand_truck", block.transform, new Vector3(-16f, y, -19.5f), 40f, 1.0f, true, 0.015f);
            PropLibrary.Place("wooden_crate_01", block.transform, new Vector3(-14.5f, y, -20.2f), 15f, 1.0f, true, 0.015f);
            PropLibrary.Place("plastic_jerrycan", block.transform, new Vector3(13f, y, -20.0f), -20f, 1.0f, false, 0.015f);

            // Minibus garés en épi, face au quai.
            for (int i = 0; i < buses; i++)
            {
                float x = -24f + (i + 0.5f) * (48f / buses);
                var position = new Vector3(x, y, 6f);
                PlaceGbaka(block.transform, position, 180f + (i % 2 == 0 ? -4f : 4f), i, meshes);
            }

            // Point de la gare devant l'abri ; les voyageurs arrivent sur le parking, devant le quai.
            var station = CreatePoint<GbakaStationPoint>(interactables, nextId++, name, block.transform.TransformPoint(new Vector3(0f, y, -14f)));
            station.EditorSetupStation(name, commune, new Vector3(3f, 0.3f, 5f));
            station.transform.rotation = block.transform.rotation;
            AddVendor(block.transform, new Vector3(-2f, y, -12.5f), 180f, s_vendorIndex++, true); // l'apprenti
            AddVendor(block.transform, new Vector3(4f, y, -21.5f), 0f, s_vendorIndex++, false);   // voyageur qui attend
            AddVendor(block.transform, new Vector3(-9f, y, -21.5f), 20f, s_vendorIndex++, false);

            AddStoreKiosk(block.transform, interactables, new Vector3(22f, y, -24f), 0f, "Kiosque de la gare", meshes, ref nextId);
        }

        private static readonly (Color body, Color band)[] GbakaLiveries =
        {
            (new Color(0.12f, 0.50f, 0.25f), new Color(0.95f, 0.80f, 0.15f)),
            (new Color(0.92f, 0.90f, 0.85f), new Color(0.10f, 0.45f, 0.25f)),
            (new Color(0.15f, 0.35f, 0.70f), new Color(0.95f, 0.95f, 0.92f)),
            (new Color(0.85f, 0.65f, 0.10f), new Color(0.15f, 0.40f, 0.20f)),
        };

        private static readonly Dictionary<int, Mesh> s_gbakaMeshes = new Dictionary<int, Mesh>();

        /// <summary>Gbaka (minibus de 12 places) : maillage partagé par livrée, un collider par véhicule.</summary>
        private static void PlaceGbaka(Transform parent, Vector3 local, float yaw, int variant, List<Mesh> meshes)
        {
            int livery = variant % GbakaLiveries.Length;
            if (!s_gbakaMeshes.TryGetValue(livery, out Mesh mesh) || mesh == null)
            {
                mesh = BuildGbakaMesh(GbakaLiveries[livery].body, GbakaLiveries[livery].band, "Gbaka_" + livery);
                s_gbakaMeshes[livery] = mesh;
                meshes.Add(mesh);
            }
            var go = new GameObject("Gbaka", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.isStatic = true;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = CityMaterials.Painted();
            AddBoxCollider(go, new Vector3(0f, 1.15f, 0f), new Vector3(2.05f, 2.1f, 5.2f));
        }

        private static Mesh BuildGbakaMesh(Color body, Color band, string name)
        {
            var b = new BoxMeshBuilder();
            var dark = new Color(0.08f, 0.09f, 0.10f);
            var glass = new Color(0.16f, 0.22f, 0.27f);
            var rimSilver = new Color(0.76f, 0.78f, 0.80f);

            // Caisse principale et pavillon affiné à l'avant.
            b.SetColor(body, 40);
            b.AddBox(new Vector3(0f, 0.36f, -0.05f), new Vector3(1.98f, 0.92f, 5.05f));          // bas de caisse
            b.AddRotatedBox(new Vector3(0f, 0.82f, 2.48f), new Vector3(1.90f, 0.50f, 0.34f), Quaternion.Euler(14f, 0f, 0f)); // nez avant
            b.AddBox(new Vector3(0f, 1.44f, -0.42f), new Vector3(1.94f, 0.98f, 4.25f));          // pavillon
            b.SetColor(band, 41);
            b.AddBox(new Vector3(0f, 1.06f, -0.04f), new Vector3(2.01f, 0.16f, 5.08f));          // bande centrale de livrée
            b.AddBox(new Vector3(0f, 2.41f, -0.42f), new Vector3(1.88f, 0.05f, 4.18f));          // pavillon supérieur

            // Vitrage sombre latéral et arrière + pare-brise incliné encadré de montants.
            b.SetColor(glass, 42);
            b.AddBox(new Vector3(0f, 1.54f, -0.45f), new Vector3(1.96f, 0.62f, 3.85f));
            b.AddBox(new Vector3(0f, 1.56f, -2.55f), new Vector3(1.50f, 0.56f, 0.04f));          // lunette arrière
            b.AddRotatedBox(new Vector3(0f, 1.94f, 1.92f), new Vector3(1.76f, 0.96f, 0.05f), Quaternion.Euler(-24f, 0f, 0f));
            b.SetColor(body, 43);
            foreach (float px in new[] { -0.89f, 0.89f })
                b.AddRotatedBox(new Vector3(px, 1.92f, 1.94f), new Vector3(0.10f, 1.00f, 0.10f), Quaternion.Euler(-24f, 0f, 0f)); // montants A
            foreach (float pz in new[] { -1.95f, -0.95f, 0.05f, 1.05f })
                b.AddBox(new Vector3(0f, 1.50f, pz), new Vector3(1.98f, 0.70f, 0.09f));          // montants entre vitres
            b.AddRotatedBox(new Vector3(0f, 1.54f, 2.24f), new Vector3(1.94f, 0.30f, 0.56f), Quaternion.Euler(-20f, 0f, 0f));

            // Rétroviseurs latéraux, marchepied et porte coulissante côté droit.
            b.SetColor(dark, 44);
            b.AddBox(new Vector3(0.99f, 0.44f, 0.58f), new Vector3(0.05f, 1.65f, 1.10f));        // porte ouverte
            b.AddBox(new Vector3(1.06f, 0.34f, 0.58f), new Vector3(0.14f, 0.05f, 1.15f));        // marchepied
            foreach (float side in new[] { -1f, 1f })
            {
                b.AddBox(new Vector3(side * 1.05f, 1.48f, 1.90f), new Vector3(0.16f, 0.04f, 0.04f));
                b.AddBox(new Vector3(side * 1.12f, 1.42f, 1.88f), new Vector3(0.07f, 0.22f, 0.13f));
            }

            // Roues rondes à 16 facettes + jantes argentées + arches sombres.
            foreach (float x in new[] { -0.90f, 0.90f })
            {
                foreach (float z in new[] { -1.60f, 1.52f })
                {
                    var c = new Vector3(x, 0.36f, z);
                    b.SetColor(dark, 44);
                    for (int k = 0; k < 4; k++)
                        b.AddRotatedBox(c, new Vector3(0.28f, 0.70f, 0.70f), Quaternion.Euler(k * 22.5f, 0f, 0f));
                    b.SetColor(rimSilver, 45);
                    for (int k = 0; k < 3; k++)
                        b.AddRotatedBox(c + new Vector3(x > 0 ? 0.04f : -0.04f, 0f, 0f), new Vector3(0.22f, 0.40f, 0.40f), Quaternion.Euler(k * 30f, 0f, 0f));
                }
            }

            // Pare-chocs, calandre, phares, clignotants ambre, feux arrière et plaques.
            b.SetColor(dark, 46);
            b.AddBox(new Vector3(0f, 0.28f, 2.58f), new Vector3(1.98f, 0.22f, 0.16f));
            b.AddBox(new Vector3(0f, 0.28f, -2.59f), new Vector3(1.98f, 0.22f, 0.14f));
            b.AddBox(new Vector3(0f, 0.74f, 2.56f), new Vector3(0.95f, 0.22f, 0.05f));           // calandre
            b.SetColor(new Color(0.92f, 0.90f, 0.84f), 47);
            b.AddBox(new Vector3(0f, 0.39f, 2.665f), new Vector3(0.46f, 0.11f, 0.02f));          // plaque avant
            b.AddBox(new Vector3(0f, 0.62f, -2.585f), new Vector3(0.46f, 0.11f, 0.02f));         // plaque arrière
            b.SetColor(new Color(1f, 0.96f, 0.78f), 48);
            foreach (float x in new[] { -0.70f, 0.70f })
                b.AddBox(new Vector3(x, 0.82f, 2.56f), new Vector3(0.32f, 0.18f, 0.04f));        // phares
            b.SetColor(new Color(1f, 0.55f, 0.05f), 49);
            foreach (float x in new[] { -0.89f, 0.89f })
                b.AddBox(new Vector3(x, 0.82f, 2.54f), new Vector3(0.08f, 0.18f, 0.05f));        // clignotants
            b.SetColor(new Color(0.88f, 0.10f, 0.08f), 50);
            foreach (float x in new[] { -0.82f, 0.82f })
                b.AddBox(new Vector3(x, 0.88f, -2.57f), new Vector3(0.18f, 0.34f, 0.04f));       // feux arrière

            // Galerie de toit tubulaire (rambardes, traverses) et bagages ficelés.
            b.SetColor(new Color(0.24f, 0.25f, 0.26f), 51);
            foreach (float rx in new[] { -0.86f, 0.86f })
            {
                b.AddBox(new Vector3(rx, 2.58f, -0.45f), new Vector3(0.05f, 0.05f, 3.3f));
                for (float rz = -2.0f; rz <= 1.11f; rz += 0.78f)
                    b.AddBox(new Vector3(rx, 2.45f, rz), new Vector3(0.04f, 0.15f, 0.04f));
            }
            for (float rz = -2.0f; rz <= 1.11f; rz += 0.62f)
                b.AddBox(new Vector3(0f, 2.47f, rz), new Vector3(1.74f, 0.04f, 0.04f));
            b.SetColor(new Color(0.62f, 0.52f, 0.36f), 52);
            b.AddBox(new Vector3(-0.32f, 2.50f, -0.95f), new Vector3(0.75f, 0.38f, 0.65f));
            b.SetColor(new Color(0.55f, 0.38f, 0.20f), 53);
            b.AddBox(new Vector3(0.38f, 2.50f, 0.25f), new Vector3(0.62f, 0.34f, 0.85f));
            b.SetColor(new Color(0.18f, 0.35f, 0.60f), 54);
            b.AddBox(new Vector3(0.20f, 2.50f, -1.45f), new Vector3(0.65f, 0.30f, 0.55f));
            return b.Build(name);
        }

        #endregion

        #region Sports et parcs

        private static void AddLine(BoxMeshBuilder b, Vector3 from, Vector3 to, float width)
        {
            Vector3 dir = to - from;
            b.AddFlatQuad((from + to) * 0.5f, dir, dir.magnitude, width);
        }

        private static void AddCircle(BoxMeshBuilder b, Vector3 center, float radius, float width, int segments = 24)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                AddLine(b, center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius, center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius, width);
            }
        }

        /// <summary>Terrain de foot de quartier : latérite, lignes à la chaux, buts, petite tribune, buvette.</summary>
        private static void CreateFootballField(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y,
            Transform interactables, List<Mesh> meshes, ref int nextId)
        {
            const float length = 50f, width = 32f;
            ground.SetColor(new Color(0.66f, 0.40f, 0.26f), 4);
            ground.AddFlatQuad(new Vector3(0f, y + 0.01f, 2f), Vector3.right, 56f, 50f);
            ground.SetColor(new Color(0.95f, 0.95f, 0.92f), 1);
            float ly = y + 0.03f;
            var c = new Vector3(0f, ly, 4f);
            Vector3 hx = Vector3.right * (length / 2f), hz = Vector3.forward * (width / 2f);
            AddLine(ground, c - hx - hz, c + hx - hz, 0.12f);
            AddLine(ground, c - hx + hz, c + hx + hz, 0.12f);
            AddLine(ground, c - hx - hz, c - hx + hz, 0.12f);
            AddLine(ground, c + hx - hz, c + hx + hz, 0.12f);
            AddLine(ground, c - hz, c + hz, 0.12f);
            AddCircle(ground, c, 5f, 0.12f);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 goalLine = c + hx * s;
                AddLine(ground, goalLine + Vector3.forward * 8f, goalLine + Vector3.forward * 8f - hx.normalized * s * 9f, 0.12f);
                AddLine(ground, goalLine - Vector3.forward * 8f, goalLine - Vector3.forward * 8f - hx.normalized * s * 9f, 0.12f);
                AddLine(ground, goalLine + Vector3.forward * 8f - hx.normalized * s * 9f, goalLine - Vector3.forward * 8f - hx.normalized * s * 9f, 0.12f);

                // But : poteaux et barre blancs, filet suggéré par une toile claire inclinée.
                painted.SetColor(Color.white, 2);
                foreach (float z in new[] { -2.5f, 2.5f })
                {
                    painted.AddBox(goalLine + Vector3.forward * z - Vector3.up * 0.02f, new Vector3(0.12f, 2.1f, 0.12f));
                    AddBoxCollider(block, goalLine + Vector3.forward * z + Vector3.up * 1.03f, new Vector3(0.12f, 2.1f, 0.12f));
                }
                painted.AddBox(goalLine + Vector3.up * 2.05f, new Vector3(0.12f, 0.12f, 5.1f));
                painted.SetColor(new Color(0.85f, 0.85f, 0.85f), 3);
                painted.AddRotatedBox(goalLine + hx.normalized * s * 0.8f + Vector3.up * 1.05f, new Vector3(0.02f, 2.1f, 5f), Quaternion.Euler(0f, 0f, s * 20f));
            }

            // Petite tribune en béton (trois gradins) le long du côté sud.
            painted.SetColor(new Color(0.70f, 0.70f, 0.68f), 5);
            for (int i = 0; i < 3; i++)
            {
                var step = new Vector3(0f, y, -16.5f - i * 1.1f);
                painted.AddBox(step, new Vector3(30f, 0.45f * (i + 1), 1.1f));
                AddBoxCollider(block, step + Vector3.up * 0.225f * (i + 1), new Vector3(30f, 0.45f * (i + 1), 1.1f));
            }
            // Supporters sur la tribune, joueurs en attente au bord, ballon et fût d'eau.
            for (int i = 0; i < 5; i++)
                AddVendor(block.transform, new Vector3(-12f + i * 6f, y + 0.45f * (1 + i % 3), -16.5f - (i % 3) * 1.1f), 0f, s_vendorIndex++, true);
            PropLibrary.Place("football", block.transform, new Vector3(1.5f, y, 3.2f), 25f, 1.15f, false, 0.018f, false);
            PropLibrary.Place("Barrel_02", block.transform, new Vector3(-22f, y, -16f), 45f, 1.0f, true, 0.02f);

            AddStoreKiosk(block.transform, interactables, new Vector3(22f, y, -22f), 0f, "Buvette du terrain", meshes, ref nextId);
        }

        /// <summary>Terrain de basket : dalle bleue, lignes, deux paniers, grillage.</summary>
        private static void CreateBasketballCourt(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y, Vector3 offset)
        {
            const float length = 28f, width = 15f;
            Vector3 c = offset + new Vector3(0f, y + 0.02f, 0f);
            ground.SetColor(new Color(0.22f, 0.40f, 0.62f), 6);
            ground.AddFlatQuad(c, Vector3.right, length + 4f, width + 4f);
            ground.SetColor(new Color(0.85f, 0.45f, 0.20f), 7);
            ground.AddFlatQuad(c + Vector3.up * 0.005f, Vector3.right, length, width);
            ground.SetColor(Color.white, 8);
            Vector3 lc = c + Vector3.up * 0.012f;
            Vector3 hx = Vector3.right * (length / 2f), hz = Vector3.forward * (width / 2f);
            AddLine(ground, lc - hx - hz, lc + hx - hz, 0.08f);
            AddLine(ground, lc - hx + hz, lc + hx + hz, 0.08f);
            AddLine(ground, lc - hx - hz, lc - hx + hz, 0.08f);
            AddLine(ground, lc + hx - hz, lc + hx + hz, 0.08f);
            AddLine(ground, lc - hz, lc + hz, 0.08f);
            AddCircle(ground, lc, 1.8f, 0.08f, 16);

            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 basePosition = c + hx * s + Vector3.right * s * 0.8f;
                painted.SetColor(new Color(0.25f, 0.25f, 0.27f), 9);
                painted.AddBox(basePosition, new Vector3(0.18f, 3.3f, 0.18f));
                AddBoxCollider(block, basePosition + Vector3.up * 1.65f, new Vector3(0.18f, 3.3f, 0.18f));
                painted.AddBox(basePosition + Vector3.up * 3.2f - Vector3.right * s * 0.6f, new Vector3(1.2f, 0.1f, 0.1f));
                painted.SetColor(Color.white, 10);
                painted.AddBox(basePosition + Vector3.up * 2.9f - Vector3.right * s * 1.2f, new Vector3(0.05f, 1.05f, 1.8f));
                painted.SetColor(new Color(0.95f, 0.40f, 0.10f), 11);
                Vector3 ring = basePosition + Vector3.up * 3.05f - Vector3.right * s * 1.45f;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    painted.AddBox(ring + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.23f, new Vector3(0.06f, 0.03f, 0.06f));
                }
            }

            // Grillage : poteaux et main courante.
            painted.SetColor(new Color(0.35f, 0.36f, 0.38f), 12);
            float gx = length / 2f + 2f, gz = width / 2f + 2f;
            for (float x = -gx; x <= gx + 0.01f; x += 4f)
            {
                foreach (float z in new[] { -gz, gz })
                    painted.AddBox(c + new Vector3(x, -0.02f, z), new Vector3(0.06f, 3f, 0.06f));
            }
            foreach (float z in new[] { -gz, gz })
                painted.AddBox(c + new Vector3(0f, 2.95f, z), new Vector3(gx * 2f, 0.05f, 0.05f));

            AddVendor(block.transform, offset + new Vector3(-6f, y, -2f), 90f, s_vendorIndex++, true);
            AddVendor(block.transform, offset + new Vector3(5f, y, 3f), 250f, s_vendorIndex++, true);
        }

        /// <summary>Jardin : pelouse, allée en boucle, arbres, bancs, fontaine.</summary>
        private static void AddPark(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y, Vector3 center, float radius, List<Mesh> meshes)
        {
            ground.SetColor(new Color(0.30f, 0.50f, 0.20f), 13);
            ground.AddFlatQuad(center + new Vector3(0f, y + 0.012f, 0f), Vector3.right, radius * 2.6f, radius * 1.9f);
            var path = new List<Vector3>();
            for (int i = 0; i <= 32; i++)
            {
                float a = i * Mathf.PI * 2f / 32f;
                path.Add(center + new Vector3(Mathf.Cos(a) * radius * 0.85f, y + 0.02f, Mathf.Sin(a) * radius * 0.6f));
            }
            ground.SetColor(new Color(0.78f, 0.68f, 0.50f), 14);
            ground.AddRibbon(path, 2.2f, 0.005f);

            // Fontaine au centre.
            painted.SetColor(new Color(0.80f, 0.78f, 0.72f), 15);
            painted.AddCylinder(center + Vector3.up * y, 2.4f, 0.6f, 12);
            painted.SetColor(new Color(0.25f, 0.50f, 0.70f), 16);
            painted.AddCylinder(center + Vector3.up * (y + 0.05f), 2.1f, 0.58f, 12);
            painted.SetColor(new Color(0.80f, 0.78f, 0.72f), 17);
            painted.AddCylinder(center + Vector3.up * y, 0.35f, 1.8f, 8);
            AddBoxCollider(block, center + Vector3.up * (y + 0.3f), new Vector3(4.6f, 0.6f, 4.6f));

            // Arbres, bancs en bois 3D et jardinières autour de l'allée.
            var trunks = new BoxMeshBuilder();
            var crowns = new BoxMeshBuilder();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f + 0.2f;
                var p = center + new Vector3(Mathf.Cos(a) * radius * 1.15f, y, Mathf.Sin(a) * radius * 0.8f);
                AddTree(trunks, crowns, p, 0.9f, s_decor, false);
                AddBoxCollider(block, p + Vector3.up * 2f, new Vector3(0.5f, 4f, 0.5f));
                var bench = center + new Vector3(Mathf.Cos(a + 0.4f) * radius * 0.55f, y, Mathf.Sin(a + 0.4f) * radius * 0.4f);
                PropLibrary.Place("painted_wooden_bench", block.transform, bench, -a * Mathf.Rad2Deg + 90f, 1.2f, true, 0.015f);
                if (i % 2 == 0)
                {
                    Vector3 planter = center + new Vector3(Mathf.Cos(a) * radius * 0.4f, y, Mathf.Sin(a) * radius * 0.28f);
                    PropLibrary.Place("planter_box_01", block.transform, planter, -a * Mathf.Rad2Deg, 1.0f, true, 0.015f);
                    PropLibrary.Place("calathea_orbifolia_01", block.transform, planter + Vector3.up * 0.34f, -a * Mathf.Rad2Deg, 0.36f, false, 0.015f, false);
                }
            }
            AddMeshObject(block.transform, "ParcTroncs", trunks, CityMaterials.Bark(), meshes);
            AddMeshObject(block.transform, "ParcFeuillage", crowns, CityMaterials.Foliage(), meshes);
            AddVendor(block.transform, center + new Vector3(radius * 0.5f, y, -radius * 0.3f), 30f, s_vendorIndex++, false);
        }

        #endregion

        #region Feux tricolores

        /// <summary>
        /// Feux aux carrefours des grandes avenues : un poteau à chaque coin, une tête par axe. Les lampes
        /// allumées sont regroupées en six maillages (voir <see cref="Client.World.TrafficLights"/>).
        /// </summary>
        private static void CreateTrafficLights(Transform city, List<Mesh> meshes)
        {
            var root = new GameObject("FeuxTricolores").transform;
            root.SetParent(city, false);
            var poles = new BoxMeshBuilder();
            var lit = new BoxMeshBuilder[6];
            for (int i = 0; i < 6; i++)
                lit[i] = new BoxMeshBuilder();

            float[] axes = { -114f, -38f, 38f, 114f };
            float corner = RoadWidth / 2f + 1.2f;
            foreach (float ix in axes)
            {
                foreach (float iz in axes)
                {
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        foreach (float sz in new[] { -1f, 1f })
                        {
                            var basePosition = new Vector3(ix + sx * corner, SidewalkHeight, iz + sz * corner);
                            // Pas de collider : au coin du trottoir, un poteau bloquait les taxis qui
                            // tournent serré (le coin est mordu par l'arrière du véhicule).
                            poles.SetColor(new Color(0.18f, 0.19f, 0.20f), 0);
                            poles.AddBox(basePosition, new Vector3(0.14f, 3.2f, 0.14f));
                            // Tête nord-sud (face aux véhicules qui arrivent de ce côté) et tête est-ouest.
                            AddSignalHead(poles, lit, 0, basePosition + Vector3.up * 2.4f, new Vector3(0f, 0f, sz));
                            AddSignalHead(poles, lit, 3, basePosition + Vector3.up * 2.4f, new Vector3(sx, 0f, 0f));
                        }
                    }
                }
            }

            AddMeshObject(root, "Poteaux", poles, CityMaterials.Painted(), meshes);
            Color[] colors = { new Color(1f, 0.12f, 0.08f), new Color(1f, 0.65f, 0.05f), new Color(0.15f, 1f, 0.35f) };
            string[] names = { "Rouge", "Orange", "Vert" };
            var northSouth = new Renderer[3];
            var eastWest = new Renderer[3];
            for (int i = 0; i < 6; i++)
            {
                int colorIndex = i % 3;
                Material material = CityMaterials.NightLight("M_Feu_" + names[colorIndex], colors[colorIndex], 2.2f, 2.6f, null, false);
                GameObject go = AddMeshObject(root, (i < 3 ? "FeuNS_" : "FeuEO_") + names[colorIndex], lit[i], material, meshes);
                go.GetComponent<MeshRenderer>().SetShadowsOff();
                (i < 3 ? northSouth : eastWest)[colorIndex] = go.GetComponent<MeshRenderer>();
            }
            root.gameObject.AddComponent<Client.World.TrafficLights>().EditorSetup(northSouth, eastWest);
        }

        /// <summary>Boîtier noir à trois feux, tourné vers <paramref name="facing"/> ; lampes allumées dans <paramref name="lit"/>.</summary>
        private static void AddSignalHead(BoxMeshBuilder poles, BoxMeshBuilder[] lit, int group, Vector3 position, Vector3 facing)
        {
            poles.SetColor(new Color(0.08f, 0.08f, 0.09f), 1);
            Vector3 size = Mathf.Abs(facing.x) > 0.5f ? new Vector3(0.25f, 0.95f, 0.32f) : new Vector3(0.32f, 0.95f, 0.25f);
            Vector3 head = position + facing * 0.2f;
            poles.AddBox(head, size);
            for (int k = 0; k < 3; k++)
            {
                // Rouge en haut, orange au milieu, vert en bas.
                Vector3 lamp = head + facing * 0.13f + Vector3.up * (0.78f - k * 0.3f);
                Vector3 lampSize = Mathf.Abs(facing.x) > 0.5f ? new Vector3(0.02f, 0.2f, 0.2f) : new Vector3(0.2f, 0.2f, 0.02f);
                poles.SetColor(new Color(0.15f, 0.15f, 0.15f), 2);
                poles.AddBox(lamp - facing * 0.005f, lampSize);
                lit[group + k].AddBox(lamp + facing * 0.01f, lampSize);
            }
        }

        #endregion
    }
}
