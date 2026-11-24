using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IvoryState.Gameplay.Economy
{
    public enum ConsumableCategory : byte
    {
        IvorianDish = 0,
        FastFood = 1,
        BakerySnack = 2,
        Fruit = 3,
        SoftDrink = 4,
        AlcoholicDrink = 5,
        MedicalKit = 6,
    }

    public readonly struct ConsumableDefinition
    {
        public readonly string Id;
        public readonly string IconKey;
        public readonly string DisplayName;
        public readonly ConsumableCategory Category;
        public readonly long DefaultPrice;
        public readonly byte Satiety;
        public readonly byte Hydration;
        public readonly byte Health;

        public ConsumableDefinition(string id, string iconKey, string displayName, ConsumableCategory category,
            long defaultPrice, byte satiety = 0, byte hydration = 0, byte health = 0)
        {
            Id = id;
            IconKey = iconKey;
            DisplayName = displayName;
            Category = category;
            DefaultPrice = defaultPrice;
            Satiety = satiety;
            Hydration = hydration;
            Health = health;
        }
    }

    /// <summary>
    /// Référentiel central des 45 consommables du jeu (nourriture locale, fast-food, snacks, fruits,
    /// boissons, vins/spiritueux et kits médicaux), lié 1-à-1 aux icônes sous <c>Assets/Art/UI/Icons/Consumables/</c>.
    /// </summary>
    public static class ConsumableCatalog
    {
        public static readonly ConsumableDefinition[] All =
        {
            // --- Plats ivoiriens & maquis ---
            new ConsumableDefinition("apf", "apf_attieke_poisson_frit", "Attiéké poisson frit (APF)", ConsumableCategory.IvorianDish, 1500, satiety: 55),
            new ConsumableDefinition("garba", "garba", "Garba", ConsumableCategory.IvorianDish, 1000, satiety: 40),
            new ConsumableDefinition("alloco", "alloco", "Alloco", ConsumableCategory.IvorianDish, 1000, satiety: 40),
            new ConsumableDefinition("chips_alloco", "chips_alloco", "Chips d'alloco", ConsumableCategory.IvorianDish, 400, satiety: 18),
            new ConsumableDefinition("foutou", "foutou", "Foutou banane", ConsumableCategory.IvorianDish, 1500, satiety: 55, hydration: 5),
            new ConsumableDefinition("kope", "kope", "Kôpê sauce graine", ConsumableCategory.IvorianDish, 1400, satiety: 52, hydration: 5),
            new ConsumableDefinition("porcodjo", "porcodjo", "Porcodjo braisé", ConsumableCategory.IvorianDish, 1600, satiety: 55),
            new ConsumableDefinition("tchep_poisson", "tchep_poisson", "Tchep poisson", ConsumableCategory.IvorianDish, 1500, satiety: 55),
            new ConsumableDefinition("tchep_poulet", "tchep_poulet", "Tchep poulet", ConsumableCategory.IvorianDish, 1500, satiety: 55),
            new ConsumableDefinition("mais_braise", "mais_braise", "Maïs braisé", ConsumableCategory.IvorianDish, 250, satiety: 15),
            new ConsumableDefinition("pain_chien", "pain_chien", "Pain chien (brochette)", ConsumableCategory.IvorianDish, 700, satiety: 35),

            // --- Fast-food & plats occidentaux ---
            new ConsumableDefinition("fastfood_burger", "fastfood_burger_menu", "Menu Cheeseburger", ConsumableCategory.FastFood, 2200, satiety: 65, hydration: 20),
            new ConsumableDefinition("fastfood_pizza", "fastfood_pizza_menu", "Menu Pizza Pepperoni", ConsumableCategory.FastFood, 2500, satiety: 70, hydration: 20),
            new ConsumableDefinition("fastfood_tenders", "fastfood_tenders_menu", "Menu Tenders Poulet", ConsumableCategory.FastFood, 2000, satiety: 60, hydration: 20),
            new ConsumableDefinition("fastfood_club", "fastfood_club_menu", "Menu Club Sandwich", ConsumableCategory.FastFood, 1800, satiety: 55, hydration: 20),
            new ConsumableDefinition("fastfood_tacos", "fastfood_tacos_menu", "Menu Tacos Grillé", ConsumableCategory.FastFood, 2100, satiety: 62, hydration: 20),
            new ConsumableDefinition("panini", "panini", "Panini grillé", ConsumableCategory.FastFood, 1100, satiety: 40),
            new ConsumableDefinition("lasagnes", "lasagnes", "Lasagnes gratinées", ConsumableCategory.FastFood, 1800, satiety: 55),
            new ConsumableDefinition("spaghetti", "spaghetti", "Spaghetti bolognaise", ConsumableCategory.FastFood, 1200, satiety: 45),

            // --- Boulangerie, beignets & snacks ---
            new ConsumableDefinition("gbofloto", "gbofloto", "Gbofloto (Beignets)", ConsumableCategory.BakerySnack, 250, satiety: 16),
            new ConsumableDefinition("degue", "degue_yaourt", "Dèguè au mil", ConsumableCategory.BakerySnack, 350, satiety: 14, hydration: 14),
            new ConsumableDefinition("pain", "pain", "Pain baguette", ConsumableCategory.BakerySnack, 200, satiety: 20),
            new ConsumableDefinition("pain_au_chocolat", "pain_au_chocolat", "Pain au chocolat", ConsumableCategory.BakerySnack, 400, satiety: 22),
            new ConsumableDefinition("gateau_chocolat", "gateau_chocolat", "Gâteau au chocolat", ConsumableCategory.BakerySnack, 650, satiety: 28),
            new ConsumableDefinition("barre_chocolat", "barre_chocolat", "Barre chocolatée", ConsumableCategory.BakerySnack, 300, satiety: 14),

            // --- Fruits frais ---
            new ConsumableDefinition("banane", "banane", "Banane douce", ConsumableCategory.Fruit, 200, satiety: 12, hydration: 6),
            new ConsumableDefinition("orange", "orange", "Orange fraîche", ConsumableCategory.Fruit, 200, satiety: 10, hydration: 14),
            new ConsumableDefinition("goyave", "goyave", "Goyave", ConsumableCategory.Fruit, 250, satiety: 12, hydration: 12),
            new ConsumableDefinition("coco", "coco", "Noix de coco fraîche", ConsumableCategory.Fruit, 350, satiety: 10, hydration: 50),

            // --- Boissons sans alcool & café ---
            new ConsumableDefinition("eau_dji", "eau_dji", "Eau minérale Dji", ConsumableCategory.SoftDrink, 400, hydration: 60),
            new ConsumableDefinition("soda_fantou", "soda_fantou", "Soda Fantou", ConsumableCategory.SoftDrink, 450, satiety: 5, hydration: 40),
            new ConsumableDefinition("cafe_cola", "cafe_cola", "Café Cola", ConsumableCategory.SoftDrink, 450, satiety: 5, hydration: 40),
            new ConsumableDefinition("jus_sangya", "jus_sangya", "Jus Sangya", ConsumableCategory.SoftDrink, 500, satiety: 8, hydration: 45),
            new ConsumableDefinition("cafe_emporter", "cafe_emporter", "Café à emporter", ConsumableCategory.SoftDrink, 350, satiety: 4, hydration: 25),

            // --- Bières, vins & spiritueux ---
            new ConsumableDefinition("biere_b66", "biere_b66", "Bière B66", ConsumableCategory.AlcoholicDrink, 800, satiety: 5, hydration: 30),
            new ConsumableDefinition("biere_bressos", "biere_bressos", "Bière Bressos", ConsumableCategory.AlcoholicDrink, 900, satiety: 5, hydration: 30),
            new ConsumableDefinition("biere_despey", "biere_despey", "Bière Despey", ConsumableCategory.AlcoholicDrink, 1000, satiety: 6, hydration: 32),
            new ConsumableDefinition("vin_divin", "vin_divin", "Vin Divin", ConsumableCategory.AlcoholicDrink, 3500, satiety: 8, hydration: 28),
            new ConsumableDefinition("vin_saint_bouake", "vin_saint_bouake", "Vin Saint-Bouaké", ConsumableCategory.AlcoholicDrink, 2800, satiety: 8, hydration: 28),
            new ConsumableDefinition("liqueur_4_heures", "liqueur_4_heures", "Liqueur 4 Heures", ConsumableCategory.AlcoholicDrink, 1800, satiety: 4, hydration: 20),
            new ConsumableDefinition("gbele", "gbele", "Gbêlê", ConsumableCategory.AlcoholicDrink, 700, satiety: 4, hydration: 22),
            new ConsumableDefinition("koutoukou", "koutoukou", "Koutoukou artisanal", ConsumableCategory.AlcoholicDrink, 600, satiety: 4, hydration: 20),

            // --- Kits médicaux & soins ---
            new ConsumableDefinition("kit_vert", "kit_medical_vert", "Kit premiers secours vert", ConsumableCategory.MedicalKit, 1500, health: 45),
            new ConsumableDefinition("kit_bleu", "kit_medical_bleu", "Kit médical bleu", ConsumableCategory.MedicalKit, 2000, hydration: 10, health: 60),
            new ConsumableDefinition("kit_rouge", "kit_medical_rouge", "Trousse urgence CHU", ConsumableCategory.MedicalKit, 2800, health: 85),
        };

        private static Dictionary<string, ConsumableDefinition> s_byId;

        private static readonly (string needle, string iconKey)[] AliasRules =
        {
            ("chips", "chips_alloco"),
            ("alloco", "alloco"),
            ("apf", "apf_attieke_poisson_frit"),
            ("attieke", "apf_attieke_poisson_frit"),
            ("garba", "garba"),
            ("foutou", "foutou"),
            ("placali", "kope"),
            ("kope", "kope"),
            ("porcodjo", "porcodjo"),
            ("tchep poulet", "tchep_poulet"),
            ("tchep", "tchep_poisson"),
            ("brochette", "pain_chien"),
            ("pain chien", "pain_chien"),
            ("mais", "mais_braise"),
            ("cheeseburger", "fastfood_burger_menu"),
            ("burger", "fastfood_burger_menu"),
            ("pizza", "fastfood_pizza_menu"),
            ("tenders", "fastfood_tenders_menu"),
            ("club", "fastfood_club_menu"),
            ("tacos", "fastfood_tacos_menu"),
            ("panini", "panini"),
            ("lasagne", "lasagnes"),
            ("spaghetti", "spaghetti"),
            ("riz sauce", "tchep_poulet"),
            ("gbofloto", "gbofloto"),
            ("beignet", "gbofloto"),
            ("degue", "degue_yaourt"),
            ("yaourt", "degue_yaourt"),
            ("pain au chocolat", "pain_au_chocolat"),
            ("gateau", "gateau_chocolat"),
            ("biscuit", "barre_chocolat"),
            ("chocolat", "barre_chocolat"),
            ("omelette", "pain_chien"),
            ("sardine", "pain"),
            ("pain", "pain"),
            ("banane", "banane"),
            ("orange", "orange"),
            ("goyave", "goyave"),
            ("mangue", "goyave"),
            ("papaye", "goyave"),
            ("ananas", "orange"),
            ("coco", "coco"),
            ("dji", "eau_dji"),
            ("eau", "eau_dji"),
            ("fantou", "soda_fantou"),
            ("sucrerie", "soda_fantou"),
            ("cafe cola", "cafe_cola"),
            ("cola", "cafe_cola"),
            ("sangya", "jus_sangya"),
            ("bissap", "jus_sangya"),
            ("gnamakoudji", "jus_sangya"),
            ("gingembre", "jus_sangya"),
            ("cafe", "cafe_emporter"),
            ("b66", "biere_b66"),
            ("bressos", "biere_bressos"),
            ("despey", "biere_despey"),
            ("divin", "vin_divin"),
            ("saint bouake", "vin_saint_bouake"),
            ("4 heures", "liqueur_4_heures"),
            ("gbele", "gbele"),
            ("koutoukou", "koutoukou"),
            ("trousse", "kit_medical_rouge"),
            ("chu", "kit_medical_rouge"),
            ("rouge", "kit_medical_rouge"),
            ("bleu", "kit_medical_bleu"),
            ("anti-palud", "kit_medical_bleu"),
            ("sirop", "kit_medical_bleu"),
            ("vitamine", "kit_medical_vert"),
            ("paracetamol", "kit_medical_vert"),
            ("secours", "kit_medical_vert"),
            ("kit", "kit_medical_vert"),
        };

        public static bool TryGetById(string id, out ConsumableDefinition def)
        {
            if (s_byId == null)
            {
                s_byId = new Dictionary<string, ConsumableDefinition>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < All.Length; i++)
                    s_byId[All[i].Id] = All[i];
            }
            if (string.IsNullOrEmpty(id))
            {
                def = default;
                return false;
            }
            return s_byId.TryGetValue(id, out def);
        }

        /// <summary>
        /// Associe n'importe quel nom d'objet (sac, Djassa, commerce, étal de rue) à sa clé d'icône
        /// sous <c>Assets/Art/UI/Icons/Consumables/</c>.
        /// </summary>
        public static string ResolveIconKey(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId))
                return string.Empty;

            if (TryGetById(nameOrId.Trim(), out ConsumableDefinition exact))
                return exact.IconKey;

            string norm = Normalize(nameOrId);
            for (int i = 0; i < All.Length; i++)
            {
                if (norm == Normalize(All[i].IconKey) || norm == Normalize(All[i].DisplayName))
                    return All[i].IconKey;
            }

            for (int i = 0; i < AliasRules.Length; i++)
            {
                if (norm.Contains(AliasRules[i].needle))
                    return AliasRules[i].iconKey;
            }

            return string.Empty;
        }

        private static string Normalize(string value)
        {
            string decomposed = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char c = decomposed[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (c == '\'' || c == '’' || c == '-' || c == '_')
                    sb.Append(' ');
                else
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
