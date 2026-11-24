namespace IvoryState.Gameplay.Economy
{
    /// <summary>
    /// Catalogue officiel des produits proposés par les magasins et pharmacies du jeu sur Djassa
    /// (catégories « Nourriture et boisson » et « Médicaments »).
    /// Partagé entre le serveur (validation, prix, effets) et le client (affichage boutique).
    /// </summary>
    public static class DjassaStoreCatalog
    {
        public struct StoreItem
        {
            public string Name;
            public string StoreName;
            public long Price;
            public byte Satiety;
            public byte Hydration;
            public byte Health;
        }

        public static readonly StoreItem[] Foods =
        {
            new StoreItem { Name = "Garba", StoreName = "Chez Tantie Garba", Price = 1000, Satiety = 40 },
            new StoreItem { Name = "Attiéké poisson braisé", StoreName = "Maquis de Marcory", Price = 1500, Satiety = 55 },
            new StoreItem { Name = "Alloco poulet", StoreName = "Coin Alloco Cocody", Price = 1200, Satiety = 45 },
            new StoreItem { Name = "Porcodjo braisé", StoreName = "Maquis Zone 4", Price = 1600, Satiety = 55 },
            new StoreItem { Name = "Foutou banane", StoreName = "Maquis Les Palmiers", Price = 1500, Satiety = 55, Hydration = 5 },
            new StoreItem { Name = "Tchep poisson", StoreName = "Restaurant du Plateau", Price = 1500, Satiety = 55 },
            new StoreItem { Name = "Menu Cheeseburger", StoreName = "Abidjan Burger & Tacos", Price = 2200, Satiety = 65, Hydration = 20 },
            new StoreItem { Name = "Menu Pizza Pepperoni", StoreName = "Abidjan Burger & Tacos", Price = 2500, Satiety = 70, Hydration = 20 },
            new StoreItem { Name = "Pain au chocolat", StoreName = "Boulangerie Biétry", Price = 400, Satiety = 22 },
            new StoreItem { Name = "Gbofloto (Beignets)", StoreName = "Kiosque du quartier", Price = 250, Satiety = 16 },
            new StoreItem { Name = "Dèguè au mil", StoreName = "Supérette du Plateau", Price = 350, Satiety = 14, Hydration = 14 },
            new StoreItem { Name = "Eau minérale Dji", StoreName = "Supérette du Plateau", Price = 400, Hydration = 60 },
            new StoreItem { Name = "Soda Fantou", StoreName = "Supérette du Plateau", Price = 450, Satiety = 5, Hydration = 40 },
            new StoreItem { Name = "Jus Sangya", StoreName = "Vendeuse du carrefour", Price = 500, Satiety = 8, Hydration = 45 },
            new StoreItem { Name = "Noix de coco fraîche", StoreName = "Grand Marché d'Adjamé", Price = 350, Satiety = 10, Hydration = 50 },
        };

        public static readonly StoreItem[] Medicines =
        {
            new StoreItem { Name = "Paracétamol 500 mg", StoreName = "Pharmacie du Plateau", Price = 500, Health = 20 },
            new StoreItem { Name = "Sirop contre la toux", StoreName = "Pharmacie de Cocody", Price = 800, Hydration = 10, Health = 25 },
            new StoreItem { Name = "Kit premiers secours vert", StoreName = "Pharmacie de Treichville", Price = 1500, Health = 45 },
            new StoreItem { Name = "Kit médical bleu", StoreName = "Pharmacie de Marcory", Price = 2000, Hydration = 10, Health = 60 },
            new StoreItem { Name = "Trousse urgence CHU", StoreName = "Pharmacie du CHU", Price = 2800, Health = 85 },
        };
    }
}
