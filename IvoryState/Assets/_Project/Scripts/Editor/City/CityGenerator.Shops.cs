using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Vehicles;
using UnityEditor;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Commerces (identifiants à la suite des autres points) : boutiques de vêtements (comptoir, portant,
    /// mannequins, étagères, caisse), auto-école (voiture d'entraînement, cônes, glissière),
    /// concessionnaires (berlines d'exposition, servante d'atelier), marché black (pneus, jantes, outillage)
    /// et guichets d'espèces.
    /// </summary>
    internal static partial class CityGenerator
    {
        private struct ShopSpot
        {
            public string Name;
            public Vector3 Position;   // trottoir
            public Vector3 Inward;     // vers l'intérieur du bloc (la boutique est derrière le point)
        }

        private static ShopSpot Shop(string name, string district, int rank)
        {
            Gameplay.World.CityLayout.Place place = Gameplay.World.CityLayout.Spot(district, rank);
            return new ShopSpot { Name = name, Position = place.Position, Inward = -place.ToRoad };
        }

        private static ShopSpot[] ClothingShops => new[]
        {
            Shop("Pagnes et Mode d'Adjamé", "Adjamé", 5),
            Shop("Boutique Élégance (Treichville)", "Treichville", 9),
            Shop("Cocody Fashion", "Cocody", 16),
            Shop("Biétry Style (Marcory)", "Marcory", 9),
        };

        private static ShopSpot DrivingSchool => Shop("Auto-école La Confiance (Adjamé)", "Adjamé", 8);

        private static ShopSpot[] Dealerships => new[]
        {
            Shop("Ivoire Auto (Marcory Zone 4)", "Marcory", 12),
            Shop("Riviera Motors (Cocody)", "Cocody", 19),
        };

        /// <summary>Pose une berline d'exposition statique (modèle PF_Taxi sans lumignon ni scripts réseau).</summary>
        private static void PlaceShowroomCar(Transform parent, Vector3 localPos, float yaw, int paintIndex)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/PF_Taxi.prefab");
            Transform source = prefab != null ? prefab.transform.Find("Graphics") : null;
            if (source == null)
                return;

            var car = Object.Instantiate(source.gameObject, parent);
            car.name = "VoitureExpo_" + paintIndex;
            car.transform.localPosition = localPos;
            car.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            car.isStatic = true;

            foreach (Component c in car.GetComponents<Component>())
            {
                if (c is not Transform)
                    Object.DestroyImmediate(c);
            }

            Material bodyMat = CityMaterials.Plain("M_ShowroomCar_" + paintIndex,
                VehicleMotor.Paints[Mathf.Clamp(paintIndex, 1, VehicleMotor.Paints.Length - 1)], 0.84f);
            bodyMat.SetFloat("_Metallic", 0.35f);
            EditorUtility.SetDirty(bodyMat);

            foreach (Transform t in car.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = true;
            foreach (MeshRenderer r in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name == "Lumignon")
                    r.gameObject.SetActive(false);
                else if (r.name == "Carrosserie")
                    r.sharedMaterial = bodyMat;
            }
            AddBoxCollider(car, new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 1.3f, 4.4f));
        }

        /// <summary>Auto-école et concessionnaires (identifiants à la suite des boutiques de vêtements).</summary>
        private static void CreateVehicleShops(Transform interactables, ref int nextId)
        {
            Material white = CityMaterials.Plain("M_ShowroomWhite", new Color(0.94f, 0.95f, 0.96f), 0.4f);
            Material blue = CityMaterials.Plain("M_ShowroomBlue", new Color(0.10f, 0.30f, 0.65f), 0.3f);
            Material red = CityMaterials.Plain("M_SchoolRed", new Color(0.80f, 0.12f, 0.10f), 0.2f);
            Material cone = CityMaterials.Plain("M_TrafficCone", new Color(1f, 0.45f, 0.05f), 0.3f);
            Material pole = CityMaterials.Plain("M_StallPole", new Color(0.25f, 0.25f, 0.25f), 0.4f);

            var school = CreatePoint<DrivingSchoolPoint>(interactables, nextId++, DrivingSchool.Name, DrivingSchool.Position);
            school.transform.rotation = Quaternion.LookRotation(DrivingSchool.Inward, Vector3.up);
            AddVisual(school.transform, PrimitiveType.Cube, new Vector3(0f, 0.55f, 1.1f), new Vector3(2.4f, 1.1f, 0.8f), white, true);
            AddVisual(school.transform, PrimitiveType.Cylinder, new Vector3(1.5f, 1.4f, 1.3f), new Vector3(0.08f, 1.4f, 0.08f), pole, false);
            AddVisual(school.transform, PrimitiveType.Cube, new Vector3(1.5f, 3.0f, 1.3f), new Vector3(1.6f, 0.7f, 0.08f), red, false);
            AddVisual(school.transform, PrimitiveType.Cube, new Vector3(1.5f, 3.0f, 1.25f), new Vector3(1.2f, 0.35f, 0.04f), white, false);
            for (int i = 0; i < 3; i++)
                AddVisual(school.transform, PrimitiveType.Cylinder, new Vector3(-1.4f + i * 0.5f, 0.3f, -0.6f), new Vector3(0.25f, 0.3f, 0.25f), cone, false);
            PlaceShowroomCar(school.transform, new Vector3(-4.4f, 0f, -0.2f), 96f, 1);
            PropLibrary.Place("tire_pump", school.transform, new Vector3(1.9f, 0f, 0.8f), 180f, 1.0f, false, 0.015f, false);
            PropLibrary.Place("classic_laptop", school.transform, new Vector3(0f, 1.1f, 1.1f), 180f, 0.5f, false, 0.01f, false);

            for (int d = 0; d < Dealerships.Length; d++)
            {
                ShopSpot spot = Dealerships[d];
                var dealer = CreatePoint<DealershipPoint>(interactables, nextId++, spot.Name, spot.Position);
                dealer.transform.rotation = Quaternion.LookRotation(spot.Inward, Vector3.up);
                dealer.EditorSetupDealership(spot.Name, new Vector3(0f, 0f, -5.5f));
                AddVisual(dealer.transform, PrimitiveType.Cube, new Vector3(0f, 0.55f, 1.2f), new Vector3(3f, 1.1f, 0.9f), white, true);
                AddVisual(dealer.transform, PrimitiveType.Cube, new Vector3(0f, 2.9f, 1.1f), new Vector3(4.2f, 0.1f, 2.6f), blue, false);
                foreach (float px in new[] { -2f, 2f })
                {
                    AddVisual(dealer.transform, PrimitiveType.Cylinder, new Vector3(px, 1.45f, 0f), new Vector3(0.07f, 1.45f, 0.07f), pole, false);
                    AddVisual(dealer.transform, PrimitiveType.Cylinder, new Vector3(px, 2.2f, 2.3f), new Vector3(0.05f, 2.2f, 0.05f), pole, false);
                    AddVisual(dealer.transform, PrimitiveType.Cube, new Vector3(px + 0.45f, 3.8f, 2.3f), new Vector3(0.8f, 1.2f, 0.03f), blue, false);
                }

                // Deux berlines exposées le long du trottoir + matériel d'atelier.
                int colorA = d == 0 ? 3 : 4;
                int colorB = d == 0 ? 1 : 2;
                PlaceShowroomCar(dealer.transform, new Vector3(-4.8f, 0f, -0.25f), 102f, colorA);
                PlaceShowroomCar(dealer.transform, new Vector3(4.8f, 0f, -0.25f), 258f, colorB);
                PropLibrary.Place("tool_cart", dealer.transform, new Vector3(-2.3f, 0f, 1.3f), 15f, 0.9f, true, 0.015f);
                PropLibrary.Place("CashRegister_01", dealer.transform, new Vector3(0.7f, 1.1f, 1.2f), 180f, 0.6f, false, 0.01f, false);
                PropLibrary.Place("planter_box_01", dealer.transform, new Vector3(2.5f, 0f, 0.8f), 0f, 1.0f, true, 0.015f);
                PropLibrary.Place("calathea_orbifolia_01", dealer.transform, new Vector3(2.5f, 0.34f, 0.8f), 0f, 0.36f, false, 0.015f, false);
                PropLibrary.Place("standing_chalkboard_01", dealer.transform, new Vector3(-2.6f, 0f, 0.1f), 195f, 0.95f, false, 0.018f, false);
            }
        }

        private static ShopSpot BlackMarket => Shop("Marché black d'Adjamé", "Adjamé", 11);

        /// <summary>Marché black (lieu de Djassa) : bâches, étals, pneus, jantes, jerricans, poste radio.</summary>
        private static void CreateBlackMarket(Transform interactables, ref int nextId)
        {
            Material tarp = CityMaterials.Plain("M_BlackMarketTarp", new Color(0.12f, 0.25f, 0.55f), 0.1f);
            Material wood = CityMaterials.Plain("M_StallWood", new Color(0.42f, 0.28f, 0.16f), 0.2f);
            Material pole = CityMaterials.Plain("M_StallPole", new Color(0.25f, 0.25f, 0.25f), 0.4f);

            var market = CreatePoint<BlackMarketPoint>(interactables, nextId++, BlackMarket.Name, BlackMarket.Position);
            market.transform.rotation = Quaternion.LookRotation(BlackMarket.Inward, Vector3.up);
            AddVisual(market.transform, PrimitiveType.Cube, new Vector3(0f, 2.5f, 1.5f), new Vector3(5f, 0.06f, 3.4f), tarp, false);
            foreach (float px in new[] { -2.4f, 2.4f })
            {
                foreach (float pz in new[] { 0f, 3f })
                    AddVisual(market.transform, PrimitiveType.Cylinder, new Vector3(px, 1.25f, pz), new Vector3(0.06f, 1.25f, 0.06f), pole, false);
            }
            for (int i = -1; i <= 1; i++)
                AddVisual(market.transform, PrimitiveType.Cube, new Vector3(i * 1.6f, 0.4f, 1.8f), new Vector3(1.4f, 0.8f, 0.9f), wood, true);

            // Marchandises Poly Haven réalistes sur et autour des étals.
            PropLibrary.Place("boombox", market.transform, new Vector3(-1.6f, 0.8f, 1.8f), 170f, 0.7f, false, 0.01f, false);
            PropLibrary.Place("rusted_wheel_rim_01", market.transform, new Vector3(0f, 0.88f, 1.8f), 0f, 1.0f, false, 0.01f, false, 90f);
            PropLibrary.Place("cardboard_box_01", market.transform, new Vector3(1.6f, 0.8f, 1.8f), 20f, 0.9f, false, 0.01f, false);
            PropLibrary.Place("old_tyre", market.transform, new Vector3(2.9f, 0.08f, 0.6f), 10f, 1.0f, true, 0.015f, pitch: 90f);
            PropLibrary.Place("old_tyre", market.transform, new Vector3(2.9f, 0.25f, 0.6f), 40f, 1.0f, false, 0.015f, pitch: 90f);
            PropLibrary.Place("plastic_jerrycan", market.transform, new Vector3(-2.8f, 0f, 0.7f), 25f, 1.0f, true, 0.015f);
            PropLibrary.Place("small_lpg_tank", market.transform, new Vector3(-2.3f, 0f, 0.5f), 0f, 1.0f, true, 0.015f);
            PropLibrary.Place("wooden_crate_01", market.transform, new Vector3(0f, 0f, 0.6f), 0f, 1.0f, true, 0.015f);
        }

        /// <summary>
        /// Armes blanches au marché black d'Adjamé (spécification : machette et couteau, 20 000 F), à côté des
        /// étals de Djassa. Créé en dernier : les identifiants des autres points ne bougent pas.
        /// </summary>
        private static void CreateBladeVendor(Transform interactables, ref int nextId)
        {
            Vector3 along = Vector3.Cross(Vector3.up, BlackMarket.Inward).normalized;
            Vector3 position = BlackMarket.Position + along * 5.6f;
            var store = CreatePoint<StorePoint>(interactables, nextId++, "Armes blanches du marché black", position);
            store.transform.rotation = Quaternion.LookRotation(BlackMarket.Inward, Vector3.up);
            store.EditorSetupStore("Le Vieux", "Machettes, couteaux… Pas de question, pas de reçu.", new[]
            {
                Product(Gameplay.Combat.Weapons.Machete, Gameplay.Combat.Weapons.Price, 0, 0),
                Product(Gameplay.Combat.Weapons.Knife, Gameplay.Combat.Weapons.Price, 0, 0),
            });
            AddKiosk(store.transform, s_pendingMeshes);
            AddVendor(store.transform, new Vector3(0f, 0f, 1.75f), 180f, s_vendorIndex++, true);
            PropLibrary.Place("machete", store.transform, new Vector3(0.45f, 1.08f, 1.18f), 55f, 1f, false, 0.02f, pitch: 90f);
            PropLibrary.Place("machete", store.transform, new Vector3(-0.35f, 1.08f, 1.2f), 120f, 1f, false, 0.02f, pitch: 90f);
        }

        private static ShopSpot[] CashPoints => new[]
        {
            Shop("Banque du Plateau", "Plateau", 0),
            Shop("Guichet Pingoin d'Adjamé", "Adjamé", 14),
            Shop("Guichet Pingoin de Treichville", "Treichville", 12),
            Shop("Guichet Pingoin de Yopougon", "Yopougon", 10),
            Shop("Guichet Pingoin de Cocody", "Cocody", 22),
            Shop("Guichet Pingoin de Marcory", "Marcory", 15),
        };

        /// <summary>Banque et guichets Pingoin : retrait et dépôt d'espèces.</summary>
        private static void CreateCashPoints(Transform interactables, ref int nextId)
        {
            Material white = CityMaterials.Plain("M_ShowroomWhite", new Color(0.94f, 0.95f, 0.96f), 0.4f);
            Material pingoin = CityMaterials.Plain("M_PingoinBlue", new Color(0.10f, 0.45f, 0.85f), 0.3f);
            Material bank = CityMaterials.Plain("M_BankGreen", new Color(0.10f, 0.40f, 0.25f), 0.3f);
            Material dark = CityMaterials.Plain("M_AtmDark", new Color(0.08f, 0.08f, 0.10f), 0.6f);
            foreach (ShopSpot spot in CashPoints)
            {
                bool isBank = spot.Name.StartsWith("Banque");
                var point = CreatePoint<CashPointPoint>(interactables, nextId++, spot.Name, spot.Position);
                point.EditorSetupCashPoint(spot.Name);
                point.transform.rotation = Quaternion.LookRotation(spot.Inward, Vector3.up);
                Material brand = isBank ? bank : pingoin;
                AddVisual(point.transform, PrimitiveType.Cube, new Vector3(0f, 1.2f, 1.3f), new Vector3(2.2f, 2.4f, 1.2f), white, true);
                AddVisual(point.transform, PrimitiveType.Cube, new Vector3(0f, 1.3f, 0.68f), new Vector3(1.2f, 0.8f, 0.04f), dark, false);
                AddVisual(point.transform, PrimitiveType.Cube, new Vector3(0f, 2.55f, 1.0f), new Vector3(2.6f, 0.12f, 1.8f), brand, false);
                AddVisual(point.transform, PrimitiveType.Cube, new Vector3(0f, 2.9f, 0.7f), new Vector3(2.0f, 0.5f, 0.06f), brand, false);
                if (isBank)
                {
                    PropLibrary.Place("planter_box_01", point.transform, new Vector3(-1.7f, 0f, 1.0f), 0f, 1.0f, true, 0.015f);
                    PropLibrary.Place("calathea_orbifolia_01", point.transform, new Vector3(-1.7f, 0.34f, 1.0f), 0f, 0.36f, false, 0.015f, false);
                }
            }
        }

        private static void CreateClothingShops(Transform interactables, ref int nextId)
        {
            Material wood = CityMaterials.Plain("M_StallWood", new Color(0.42f, 0.28f, 0.16f), 0.2f);
            Material pole = CityMaterials.Plain("M_StallPole", new Color(0.25f, 0.25f, 0.25f), 0.4f);
            Material awning = CityMaterials.Plain("M_ClothingAwning", new Color(0.70f, 0.15f, 0.45f), 0.1f);
            Material[] fabrics =
            {
                CityMaterials.Plain("M_Fabric_Wax", new Color(0.95f, 0.55f, 0.10f), 0.1f),
                CityMaterials.Plain("M_Fabric_Blue", new Color(0.15f, 0.35f, 0.75f), 0.1f),
                CityMaterials.Plain("M_Fabric_Green", new Color(0.10f, 0.55f, 0.30f), 0.1f),
            };
            Material skin = CityMaterials.Plain("M_Mannequin", new Color(0.35f, 0.24f, 0.17f), 0.3f);

            foreach (ShopSpot spot in ClothingShops)
            {
                var store = CreatePoint<ClothingStorePoint>(interactables, nextId++, spot.Name, spot.Position);
                store.EditorSetupStore(spot.Name);
                store.transform.rotation = Quaternion.LookRotation(spot.Inward, Vector3.up);

                AddVisual(store.transform, PrimitiveType.Cube, new Vector3(0f, 0.5f, 1.0f), new Vector3(2.4f, 1f, 0.8f), wood, true);
                AddVisual(store.transform, PrimitiveType.Cube, new Vector3(0f, 2.6f, 0.9f), new Vector3(3.4f, 0.08f, 2.2f), awning, false);
                foreach (float px in new[] { -1.6f, 1.6f })
                {
                    foreach (float pz in new[] { -0.1f, 1.9f })
                        AddVisual(store.transform, PrimitiveType.Cylinder, new Vector3(px, 1.3f, pz), new Vector3(0.06f, 1.3f, 0.06f), pole, false);
                }
                AddVisual(store.transform, PrimitiveType.Cube, new Vector3(0f, 1.9f, 1.7f), new Vector3(2.6f, 0.05f, 0.05f), pole, false);
                for (int i = 0; i < 5; i++)
                    AddVisual(store.transform, PrimitiveType.Cube, new Vector3(-1.0f + i * 0.5f, 1.45f, 1.7f), new Vector3(0.38f, 0.85f, 0.06f), fabrics[i % fabrics.Length], false);
                for (int side = -1; side <= 1; side += 2)
                {
                    var origin = new Vector3(side * 1.9f, 0f, 0.4f);
                    AddVisual(store.transform, PrimitiveType.Cylinder, origin + new Vector3(0f, 0.55f, 0f), new Vector3(0.06f, 0.55f, 0.06f), pole, false);
                    AddVisual(store.transform, PrimitiveType.Cube, origin + new Vector3(0f, 1.35f, 0f), new Vector3(0.5f, 0.75f, 0.28f), fabrics[(side + 1) / 2], false);
                    AddVisual(store.transform, PrimitiveType.Sphere, origin + new Vector3(0f, 1.9f, 0f), new Vector3(0.24f, 0.28f, 0.24f), skin, false);
                }
                PropLibrary.Place("CashRegister_01", store.transform, new Vector3(0.75f, 1.0f, 1.0f), 180f, 0.6f, false, 0.01f, false);
                PropLibrary.Place("wooden_display_shelves_01", store.transform, new Vector3(-2.3f, 0f, 1.6f), 20f, 0.9f, true, 0.015f);
                PropLibrary.Place("standing_chalkboard_01", store.transform, new Vector3(2.3f, 0f, 0.2f), 165f, 0.95f, false, 0.018f, false);
            }
        }

        private static void CreateCommerces(Transform interactables, ref int nextId)
        {
            Material wood = CityMaterials.Plain("M_CommerceWood", new Color(0.40f, 0.25f, 0.14f), 0.2f);
            Material pole = CityMaterials.Plain("M_CommercePole", new Color(0.22f, 0.22f, 0.24f), 0.45f);
            Material[] awnings =
            {
                CityMaterials.Plain("M_Commerce_Maquis", new Color(0.84f, 0.38f, 0.10f), 0.1f),
                CityMaterials.Plain("M_Commerce_Resto", new Color(0.58f, 0.12f, 0.16f), 0.1f),
                CityMaterials.Plain("M_Commerce_FastFood", new Color(0.92f, 0.62f, 0.08f), 0.1f),
                CityMaterials.Plain("M_Commerce_Boutique", new Color(0.14f, 0.45f, 0.72f), 0.1f),
                CityMaterials.Plain("M_Commerce_Pharmacie", new Color(0.12f, 0.64f, 0.36f), 0.1f),
            };

            foreach (Gameplay.Economy.CommerceDefinition def in Gameplay.Economy.CommerceCatalog.All)
            {
                Gameplay.World.CityLayout.Place place = Gameplay.Economy.CommerceCatalog.GetPlace(def);
                var commerce = CreatePoint<CommercePoint>(interactables, nextId++, def.Name, place.Position);
                commerce.EditorSetupCommerce(def.Id);
                commerce.transform.rotation = Quaternion.LookRotation(-place.ToRoad, Vector3.up);

                Material awning = awnings[Mathf.Clamp((int)def.Kind, 0, awnings.Length - 1)];
                AddVisual(commerce.transform, PrimitiveType.Cube, new Vector3(0f, 0.52f, 1.05f), new Vector3(2.8f, 1.04f, 0.85f), wood, true);
                AddVisual(commerce.transform, PrimitiveType.Cube, new Vector3(0f, 2.65f, 0.95f), new Vector3(3.8f, 0.10f, 2.4f), awning, false);
                foreach (float px in new[] { -1.75f, 1.75f })
                {
                    foreach (float pz in new[] { -0.15f, 2.0f })
                        AddVisual(commerce.transform, PrimitiveType.Cylinder, new Vector3(px, 1.32f, pz), new Vector3(0.06f, 1.32f, 0.06f), pole, false);
                }

                PropLibrary.Place("CashRegister_01", commerce.transform, new Vector3(0.85f, 1.04f, 1.05f), 180f, 0.6f, false, 0.01f, false);
                PropLibrary.Place("standing_chalkboard_01", commerce.transform, new Vector3(-2.15f, 0f, 0.3f), 170f, 0.95f, false, 0.018f, false);
                if (def.Kind == Gameplay.Economy.CommerceKind.Maquis || def.Kind == Gameplay.Economy.CommerceKind.Restaurant)
                    CityDecor.AddMaquis(commerce.transform, new Vector3(0f, 0f, 1.2f), Vector3.right, Vector3.forward, new System.Random(def.Id * 997));
            }
        }
    }
}
