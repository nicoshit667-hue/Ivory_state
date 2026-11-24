using UnityEngine;

namespace IvoryState.Gameplay.Homes
{
    public enum PropertyKind : byte
    {
        Studio = 0,        // studio seul (location) : le locataire peut choisir son colocataire
        OneBedroom = 1,    // 1 chambre salon (location)
        TwoBedroom = 2,    // 2 chambres salon (location), jardin
        House3 = 3,        // 3 chambres salon (achat), jardin
        House4 = 4,        // 4 chambres salon standard (achat), jardin
        House4Luxe = 5,    // 4 chambres salon luxe (achat), jardin et piscine
    }

    /// <summary>
    /// Biens immobiliers de la ville : appartements à louer (plusieurs par immeuble, donc plusieurs
    /// locataires par bâtiment) et maisons à acheter (un seul propriétaire). Partagé par l'éditeur
    /// (entrées et intérieurs), le serveur (règles) et le client (application Immobilier).
    /// Prix en FCFA ; le loyer est prélevé chaque dimanche (voir <see cref="RentRules"/>), une maison
    /// achetée paie un impôt de 0,5 % de son prix au même rythme.
    /// </summary>
    public static class PropertyCatalog
    {
        public struct Entrance
        {
            public string Name;
            public Vector3 Position;
            public Vector3 ExitOffset;
            /// <summary>Du trottoir vers le bâtiment (le portail est de ce côté).</summary>
            public Vector3 Inward;
        }

        public struct Property
        {
            public int Id;
            public int EntranceIndex;
            public PropertyKind Kind;
            public bool Chic;
            /// <summary>Loyer hebdomadaire (location) ou prix d'achat.</summary>
            public long Price;
            public string Label;
        }

        public const int MaxHomesPerPlayer = 2;
        public const float TaxRate = 0.005f;

        /// <summary>
        /// Entrée sur un repère de trottoir de la disposition de la ville (voir World.CityPlaces) : on
        /// ressort à 3,6 m le long du trottoir.
        /// </summary>
        private static Entrance Spot(string name, string district, int rank)
        {
            World.CityLayout.Place place = World.CityLayout.Spot(district, rank);
            return new Entrance { Name = name, Position = place.Position, ExitOffset = place.Along * 3.6f, Inward = -place.ToRoad };
        }

        private static Entrance[] s_entrances;
        private static int s_entrancesVersion = -1;

        /// <summary>Entrées des logements (posées sur le relief ; recalculées si la disposition change).</summary>
        public static Entrance[] Entrances
        {
            get
            {
                if (s_entrances == null || s_entrancesVersion != World.CityLayout.Version)
                {
                    s_entrancesVersion = World.CityLayout.Version;
                    s_entrances = BuildEntrances();
                }
                return s_entrances;
            }
        }

        private static Entrance[] BuildEntrances() => new[]
        {
            Spot("Immeuble Les Palmiers (Yopougon)", "Yopougon", 1),
            Spot("Immeuble du Marché (Adjamé)", "Adjamé", 2),
            Spot("Résidence Biétry (Marcory)", "Marcory", 2),
            Spot("Maison de Treichville", "Treichville", 3),
            Spot("Maison de Yopougon", "Yopougon", 7),
            Spot("Maison de Marcory", "Marcory", 6),
            Spot("Maison des Deux-Plateaux (Cocody)", "Cocody", 1),
            Spot("Villa Riviera (Cocody)", "Cocody", 7),
            Spot("Villa Ambassade (Cocody)", "Cocody", 10),
            Spot("Villa Lagune (Cocody)", "Cocody", 13),
        };
        public static readonly Property[] Properties =
        {
            P(1, 0, PropertyKind.Studio, false, 15000, "Studio A1"),
            P(2, 0, PropertyKind.Studio, false, 20000, "Studio A2"),
            P(3, 0, PropertyKind.OneBedroom, false, 35000, "Appartement B1"),
            P(4, 0, PropertyKind.TwoBedroom, false, 60000, "Appartement C1"),
            P(5, 0, PropertyKind.TwoBedroom, false, 70000, "Appartement C2"),
            P(6, 1, PropertyKind.Studio, false, 25000, "Studio 1er étage"),
            P(7, 1, PropertyKind.OneBedroom, false, 40000, "Appartement 2e étage"),
            P(8, 1, PropertyKind.TwoBedroom, false, 75000, "Appartement 3e étage"),
            P(9, 2, PropertyKind.Studio, true, 50000, "Studio Biétry 1"),
            P(10, 2, PropertyKind.Studio, true, 55000, "Studio Biétry 2"),
            P(11, 2, PropertyKind.OneBedroom, true, 70000, "Appartement Biétry 3"),
            P(12, 2, PropertyKind.TwoBedroom, true, 100000, "Appartement Biétry 4"),
            P(13, 3, PropertyKind.House3, false, 10000000, "Maison 3 chambres"),
            P(14, 4, PropertyKind.House3, false, 10000000, "Maison 3 chambres"),
            P(15, 5, PropertyKind.House3, true, 15000000, "Maison 3 chambres"),
            P(16, 6, PropertyKind.House3, true, 15000000, "Maison 3 chambres"),
            P(17, 7, PropertyKind.House4, true, 50000000, "Villa 4 chambres"),
            P(18, 8, PropertyKind.House4Luxe, true, 70000000, "Villa 4 chambres de luxe"),
            P(19, 9, PropertyKind.House4Luxe, true, 70000000, "Villa 4 chambres de luxe"),
        };

        private static Property P(int id, int entrance, PropertyKind kind, bool chic, long price, string label) =>
            new Property { Id = id, EntranceIndex = entrance, Kind = kind, Chic = chic, Price = price, Label = label };

        public static int Count => Properties.Length;

        public static bool IsValid(int id) => id >= 1 && id <= Properties.Length;

        public static Property Get(int id) => Properties[id - 1];

        public static bool IsForSale(PropertyKind kind) => kind >= PropertyKind.House3;

        public static int Bedrooms(PropertyKind kind) => kind switch
        {
            PropertyKind.Studio => 0,
            PropertyKind.OneBedroom => 1,
            PropertyKind.TwoBedroom => 2,
            PropertyKind.House3 => 3,
            _ => 4,
        };

        /// <summary>Habitants au plus : 2 par chambre (2 pour un studio).</summary>
        public static int Capacity(PropertyKind kind) => Mathf.Max(1, Bedrooms(kind)) * 2;

        /// <summary>Cour ou jardin : les villas duplex (terrain clos avec portail).</summary>
        public static bool HasGarden(PropertyKind kind) => kind >= PropertyKind.House3;

        public static bool HasPool(PropertyKind kind) => kind == PropertyKind.House4Luxe;

        /// <summary>Ce que coûte le bien chaque semaine : loyer, ou impôt de 0,5 % du prix d'achat.</summary>
        public static long WeeklyCost(Property property) =>
            IsForSale(property.Kind) ? (long)(property.Price * TaxRate) : property.Price;

        public static string KindName(PropertyKind kind) => kind switch
        {
            PropertyKind.Studio => "Studio",
            PropertyKind.OneBedroom => "1 chambre salon",
            PropertyKind.TwoBedroom => "2 chambres salon",
            PropertyKind.House3 => "Maison 3 chambres salon",
            PropertyKind.House4 => "Villa 4 chambres salon",
            _ => "Villa 4 chambres salon, piscine",
        };

        // --- Intérieurs (loin à l'est, sous le niveau de la ville : rien au-dessus pour l'ombre) ---
        public static readonly Vector3 InteriorOrigin = new Vector3(4000f, -400f, 0f);
        public const float InteriorSpacing = 90f;
        public const int InteriorColumns = 5;

        /// <summary>
        /// Agrandissement des plans dans le jeu : les cotes des plans (pièces, murs, portes) sont multipliées
        /// par ce facteur, pas le mobilier (un lit garde 2 m). Avec la caméra à la 3e personne, des pièces à
        /// la taille réelle paraissent minuscules.
        /// </summary>
        public const float PlanScale = 1.5f;

        /// <summary>
        /// Emprise d'un intérieur (plans × PlanScale) : studio 6 × 4,5 ; F2 8 × 6 ; F3 12 × 8 ; villas duplex
        /// sur un terrain de 16 × 21 (maison 12 × 9 au nord, cour et portail au sud).
        /// </summary>
        public static Vector2 InteriorSize(PropertyKind kind) => PlanScale * kind switch
        {
            PropertyKind.Studio => new Vector2(6f, 4.5f),
            PropertyKind.OneBedroom => new Vector2(8f, 6f),
            PropertyKind.TwoBedroom => new Vector2(12f, 8f),
            _ => new Vector2(16f, 21f),
        };

        /// <summary>Porte d'entrée (ou portail) en repère local (déjà agrandie), et direction vers l'intérieur.</summary>
        public static (Vector3 door, Vector3 inward) InteriorDoor(PropertyKind kind) => kind switch
        {
            PropertyKind.Studio => (new Vector3(-3f, 0f, 0.55f) * PlanScale, Vector3.right),
            PropertyKind.OneBedroom => (new Vector3(-4f, 0f, 0.8f) * PlanScale, Vector3.right),
            PropertyKind.TwoBedroom => (new Vector3(-6f, 0f, 0.6f) * PlanScale, Vector3.right),
            _ => (new Vector3(0f, 0f, -11f) * PlanScale, Vector3.forward),
        };

        public static float GardenWidth(PropertyKind kind) => kind switch
        {
            PropertyKind.TwoBedroom => 8f,
            PropertyKind.House3 => 12f,
            PropertyKind.House4 => 14f,
            PropertyKind.House4Luxe => 16f,
            _ => 0f,
        };

        public static Vector3 InteriorCenter(int id)
        {
            int index = id - 1;
            return InteriorOrigin + new Vector3(index % InteriorColumns * InteriorSpacing, 0f, -(index / InteriorColumns) * InteriorSpacing);
        }

        public static Vector3 SpawnPoint(int id)
        {
            (Vector3 door, Vector3 inward) = InteriorDoor(Get(id).Kind);
            return InteriorCenter(id) + door + inward * 1.7f + new Vector3(0f, 0.05f, 0f);
        }

        public static Vector3 ExitCircle(int id)
        {
            (Vector3 door, Vector3 inward) = InteriorDoor(Get(id).Kind);
            return InteriorCenter(id) + door + inward * 0.75f;
        }

        /// <summary>Bien dont l'intérieur contient cette position (0 = aucun).</summary>
        public static int PropertyAt(Vector3 position)
        {
            if (!StudioLayout.IsIndoors(position) || position.x < InteriorOrigin.x - InteriorSpacing * 0.5f)
                return 0;
            Vector3 local = position - InteriorOrigin;
            int column = Mathf.RoundToInt(local.x / InteriorSpacing);
            int row = Mathf.RoundToInt(-local.z / InteriorSpacing);
            if (column < 0 || column >= InteriorColumns || row < 0)
                return 0;
            int id = row * InteriorColumns + column + 1;
            return IsValid(id) ? id : 0;
        }

        public static Vector3 EntrancePosition(int id) => Entrances[Get(id).EntranceIndex].Position;

        public static Vector3 EntranceExit(int id)
        {
            Entrance entrance = Entrances[Get(id).EntranceIndex];
            return entrance.Position + entrance.ExitOffset;
        }

        /// <summary>Position dans la ville correspondant à un intérieur (carte, GPS).</summary>
        public static bool TryGetExterior(Vector3 position, out Vector3 exterior)
        {
            exterior = position;
            if (!StudioLayout.IsIndoors(position))
                return false;
            int property = PropertyAt(position);
            if (property != 0)
            {
                exterior = EntrancePosition(property);
                return true;
            }
            int hospital = Combat.HospitalCatalog.HospitalAt(position);
            if (hospital >= 0)
            {
                exterior = Combat.HospitalCatalog.Hospitals[hospital].Entrance;
                return true;
            }
            HomeEntrance residence = HomeEntrance.Residence;
            if (residence != null)
            {
                exterior = residence.transform.position;
                return true;
            }
            return false;
        }
    }
}
