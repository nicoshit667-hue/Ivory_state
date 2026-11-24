using System;
using System.Collections.Generic;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.World;
using UnityEngine;

namespace IvoryState.Gameplay.Economy
{
    public enum CommerceKind : byte
    {
        Maquis = 0,
        Restaurant = 1,
        FastFood = 2,
        Boutique = 3,
        Pharmacie = 4,
    }

    public readonly struct CommerceDefinition
    {
        public readonly int Id;
        public readonly string Name;
        public readonly CommerceKind Kind;
        public readonly string District;
        public readonly int SpotRank;
        public readonly int LinkedPropertyId;
        public readonly long PurchasePrice;
        public readonly byte CommissionPercent;
        public readonly string Greeting;
        public readonly string[] ConsumableIds;

        public CommerceDefinition(int id, string name, CommerceKind kind, string district, int spotRank,
            int linkedPropertyId, long purchasePrice, byte commissionPercent, string greeting, params string[] consumableIds)
        {
            Id = id;
            Name = name;
            Kind = kind;
            District = district;
            SpotRank = spotRank;
            LinkedPropertyId = linkedPropertyId;
            PurchasePrice = purchasePrice;
            CommissionPercent = commissionPercent;
            Greeting = greeting;
            ConsumableIds = consumableIds ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Catalogue officiel des commerces d'Abidjan (maquis, restaurants, fast-foods, boutiques/pâtisseries
    /// et pharmacies), incluant les liaisons éventuelles avec les biens immobiliers et les menus liés
    /// aux 45 consommables de <see cref="ConsumableCatalog"/>.
    /// </summary>
    public static class CommerceCatalog
    {
        public const int MaxOwnedCommerces = 4;

        public static readonly CommerceDefinition[] All =
        {
            new CommerceDefinition(
                id: 1,
                name: "Maquis Les Palmiers",
                kind: CommerceKind.Maquis,
                district: "Yopougon",
                spotRank: 16,
                linkedPropertyId: 1, // Immeuble Les Palmiers (Yopougon)
                purchasePrice: 1800000,
                commissionPercent: 25,
                greeting: "Ici c'est chaud ! Garba, APF, porcodjo et bières bien glacées.",
                "garba", "apf", "alloco", "porcodjo", "foutou", "kope", "pain_chien", "biere_b66", "biere_bressos", "gbele", "koutoukou", "eau_dji"),

            new CommerceDefinition(
                id: 2,
                name: "Maquis Zone 4",
                kind: CommerceKind.Maquis,
                district: "Marcory",
                spotRank: 18,
                linkedPropertyId: 9, // Résidence Biétry (Marcory)
                purchasePrice: 2800000,
                commissionPercent: 25,
                greeting: "Ambiance Marcory ! Braisés, alloco et boissons fraîches toute la nuit.",
                "apf", "porcodjo", "alloco", "chips_alloco", "kope", "tchep_poisson", "biere_despey", "biere_b66", "liqueur_4_heures", "vin_saint_bouake", "eau_dji", "jus_sangya"),

            new CommerceDefinition(
                id: 3,
                name: "Chez Tantie Garba",
                kind: CommerceKind.Maquis,
                district: "Treichville",
                spotRank: 18,
                linkedPropertyId: 13, // Maison de Treichville
                purchasePrice: 1500000,
                commissionPercent: 25,
                greeting: "Garba rapide, maïs braisé et jus frais de Treichville !",
                "garba", "alloco", "chips_alloco", "mais_braise", "pain_chien", "gbofloto", "degue", "jus_sangya", "soda_fantou", "eau_dji"),

            new CommerceDefinition(
                id: 4,
                name: "Restaurant Le Plateau Gourmand",
                kind: CommerceKind.Restaurant,
                district: "Plateau",
                spotRank: 2,
                linkedPropertyId: 0,
                purchasePrice: 4500000,
                commissionPercent: 28,
                greeting: "Bienvenue à table : spécialités ivoiriennes, lasagnes et grands crus.",
                "tchep_poisson", "tchep_poulet", "apf", "foutou", "lasagnes", "spaghetti", "vin_divin", "vin_saint_bouake", "gateau_chocolat", "cafe_emporter", "eau_dji"),

            new CommerceDefinition(
                id: 5,
                name: "La Table des Deux-Plateaux",
                kind: CommerceKind.Restaurant,
                district: "Cocody",
                spotRank: 20,
                linkedPropertyId: 16, // Maison des Deux-Plateaux (Cocody)
                purchasePrice: 5200000,
                commissionPercent: 30,
                greeting: "Cuisine raffinée à Cocody : tchep, pâtes, pâtisseries et vins.",
                "tchep_poulet", "tchep_poisson", "kope", "porcodjo", "lasagnes", "spaghetti", "vin_divin", "vin_saint_bouake", "gateau_chocolat", "pain_au_chocolat", "eau_dji"),

            new CommerceDefinition(
                id: 6,
                name: "Abidjan Burger & Tacos",
                kind: CommerceKind.FastFood,
                district: "Cocody",
                spotRank: 21,
                linkedPropertyId: 17, // Villa Riviera (Cocody)
                purchasePrice: 3600000,
                commissionPercent: 26,
                greeting: "Menus chauds servis vite : burgers, pizzas, tacos et sodas glacés !",
                "fastfood_burger", "fastfood_pizza", "fastfood_tacos", "fastfood_tenders", "fastfood_club", "panini", "soda_fantou", "cafe_cola", "jus_sangya", "eau_dji"),

            new CommerceDefinition(
                id: 7,
                name: "Snack Express Plateau",
                kind: CommerceKind.FastFood,
                district: "Plateau",
                spotRank: 5,
                linkedPropertyId: 0,
                purchasePrice: 3000000,
                commissionPercent: 25,
                greeting: "Pause midi express : paninis, club sandwich, tenders et cafés.",
                "fastfood_club", "fastfood_tenders", "fastfood_burger", "panini", "spaghetti", "cafe_emporter", "cafe_cola", "soda_fantou", "barre_chocolat", "eau_dji"),

            new CommerceDefinition(
                id: 8,
                name: "Supérette du Marché",
                kind: CommerceKind.Boutique,
                district: "Adjamé",
                spotRank: 20,
                linkedPropertyId: 6, // Immeuble du Marché (Adjamé)
                purchasePrice: 2200000,
                commissionPercent: 22,
                greeting: "Fruits frais, pain chaud, boissons et douceurs à petit prix !",
                "banane", "orange", "goyave", "coco", "pain", "pain_au_chocolat", "gbofloto", "degue", "barre_chocolat", "eau_dji", "soda_fantou", "jus_sangya"),

            new CommerceDefinition(
                id: 9,
                name: "Boulangerie & Cave Biétry",
                kind: CommerceKind.Boutique,
                district: "Marcory",
                spotRank: 20,
                linkedPropertyId: 11, // Résidence Biétry (Marcory)
                purchasePrice: 3200000,
                commissionPercent: 25,
                greeting: "Pains, viennoiseries, gâteaux et belle cave de vins & bières.",
                "pain", "pain_au_chocolat", "gateau_chocolat", "barre_chocolat", "cafe_emporter", "vin_divin", "vin_saint_bouake", "biere_despey", "biere_bressos", "liqueur_4_heures", "eau_dji"),

            new CommerceDefinition(
                id: 10,
                name: "Pharmacie Centrale du Plateau",
                kind: CommerceKind.Pharmacie,
                district: "Plateau",
                spotRank: 8,
                linkedPropertyId: 0,
                purchasePrice: 4000000,
                commissionPercent: 28,
                greeting: "Santé & premiers secours : kits médicaux, réhydratation et soins CHU.",
                "kit_vert", "kit_bleu", "kit_rouge", "eau_dji", "orange", "jus_sangya"),

            new CommerceDefinition(
                id: 11,
                name: "Pharmacie de Cocody",
                kind: CommerceKind.Pharmacie,
                district: "Cocody",
                spotRank: 23,
                linkedPropertyId: 18, // Villa Ambassade (Cocody)
                purchasePrice: 4400000,
                commissionPercent: 28,
                greeting: "Trousses médicales complètes et boissons vitaminées.",
                "kit_vert", "kit_bleu", "kit_rouge", "eau_dji", "coco", "goyave"),

            new CommerceDefinition(
                id: 12,
                name: "Étal Gourmand de Koumassi",
                kind: CommerceKind.Maquis,
                district: "Koumassi",
                spotRank: 2,
                linkedPropertyId: 0,
                purchasePrice: 1400000,
                commissionPercent: 24,
                greeting: "Cuisine de quartier : attiéké, foutou, maïs braisé et rafraîchissements.",
                "apf", "garba", "foutou", "alloco", "mais_braise", "banane", "coco", "gbele", "biere_b66", "eau_dji"),
        };

        public static int Count => All.Length;

        public static bool IsValid(int id) => id >= 1 && id <= All.Length;

        public static bool TryGet(int id, out CommerceDefinition def)
        {
            if (id >= 1 && id <= All.Length)
            {
                def = All[id - 1];
                return true;
            }
            def = default;
            return false;
        }

        public static string KindLabel(CommerceKind kind) => kind switch
        {
            CommerceKind.Maquis => "Maquis",
            CommerceKind.Restaurant => "Restaurant",
            CommerceKind.FastFood => "Fast-Food",
            CommerceKind.Boutique => "Boutique",
            CommerceKind.Pharmacie => "Pharmacie",
            _ => "Commerce",
        };

        public static CityLayout.Place GetPlace(CommerceDefinition def)
        {
            return CityLayout.Spot(def.District, def.SpotRank);
        }

        public static Vector3 GetWorldPosition(CommerceDefinition def)
        {
            return GetPlace(def).Position;
        }

        public static string DescribeLinkedProperty(int propertyId)
        {
            if (!PropertyCatalog.IsValid(propertyId))
                return string.Empty;
            PropertyCatalog.Property p = PropertyCatalog.Get(propertyId);
            string entrance = PropertyCatalog.Entrances[p.EntranceIndex].Name;
            return p.Label + " (" + entrance + ")";
        }

        public static List<int> ParseIds(string csv)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(csv))
                return result;

            string[] parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i].Trim(), out int id) && IsValid(id) && !result.Contains(id))
                    result.Add(id);
            }
            return result;
        }

        public static string SerializeIds(IReadOnlyList<int> ids)
        {
            if (ids == null || ids.Count == 0)
                return string.Empty;
            return string.Join(",", ids);
        }
    }
}
