using System.Collections.Generic;
using IvoryState.Gameplay.World;
using UnityEngine;

namespace IvoryState.Client.UI
{
    /// <summary>Types d'icônes de la carte (valeurs gardées dans les préférences : ajouter à la fin).</summary>
    public enum MapIconKind
    {
        Hospital, Church, Mosque, Market, Station, Dealership, DrivingSchool, Clothing, BlackMarket,
        Sport, Park, Monument, Home, CashPoint,
        /// <summary>Commerces (maquis, restaurants, boutiques, pharmacies), achetables par les joueurs.</summary>
        Commerce,
        /// <summary>Kiosques, superettes, étals de marché (menu d'achat).</summary>
        Kiosk,
        /// <summary>Étals de rue : à manger, à boire.</summary>
        Stall,
        /// <summary>Armurerie (armes à feu, port d'arme).</summary>
        Gunshop,
        /// <summary>Armes blanches : quincailleries, vendeur du marché black.</summary>
        Blades,
        /// <summary>Dépôt Express : livraisons (petit boulot).</summary>
        Depot,
        Police, TownHall, Taxi, Airport,
    }

    /// <summary>
    /// Lieux remarquables de la ville pour la carte : les lieux particuliers de la disposition
    /// (Resources/CityLayout.json) — églises, mosquées, marchés, terrains, parcs, monuments. Les autres
    /// icônes (gares, commerces, hôpitaux, logements) sont lues directement dans la scène et les catalogues.
    /// </summary>
    public static class MapLandmarks
    {
        public struct Landmark
        {
            public string Name;
            public MapIconKind Kind;
            public Vector3 Position;
        }

        private static Landmark[] s_all;

        public static Landmark[] All => s_all ??= Build();

        private static Landmark[] Build()
        {
            var list = new List<Landmark>();
            foreach (CityLayout.BlockData block in CityLayout.Layout.blocks)
            {
                MapIconKind? kind = block.special switch
                {
                    "Pyramide" or "HotelIvoire" or "PalaisCulture" => MapIconKind.Monument,
                    "Cathedrale" or "Eglise" => MapIconKind.Church,
                    "GrandeMosquee" or "Mosquee" => MapIconKind.Mosque,
                    "GrandMarche" or "MarcheCouvert" => MapIconKind.Market,
                    "Stade" or "Foot" or "Basket" => MapIconKind.Sport,
                    "ParcBasket" => MapIconKind.Park,
                    _ => null, // gares (lues dans la scène), place du Plateau
                };
                if (kind.HasValue)
                    list.Add(new Landmark { Name = block.name, Kind = kind.Value, Position = new Vector3(block.x, 0f, block.z) });
            }
            // Monuments modélisés (à leur vraie place, voir tools/citylayout) ; le CHU a déjà l'icône d'hôpital.
            foreach (CityLayout.LandmarkData landmark in CityLayout.Layout.landmarks)
            {
                MapIconKind? kind = landmark.key switch
                {
                    "PF_Cathedrale_Saint_Paul" => MapIconKind.Church,
                    "PF_Grande_Mosquee" => MapIconKind.Mosque,
                    "PF_Stade_FHB" => MapIconKind.Sport,
                    "PF_Commissariat_Central" => MapIconKind.Police,
                    "PF_Mairie_Plateau" => MapIconKind.TownHall,
                    "PF_Hopital_CHU" or "PF_Duplex_Cocody_01" => null,
                    _ => MapIconKind.Monument,
                };
                if (kind.HasValue)
                    list.Add(new Landmark { Name = landmark.name, Kind = kind.Value, Position = new Vector3(landmark.x, 0f, landmark.z) });
            }
            CityLayout.AirportData airport = CityLayout.Layout.airport;
            if (airport?.runways != null && airport.runways.Length > 0 && airport.runways[0].p.Length >= 4)
            {
                float[] p = airport.runways[0].p;
                list.Add(new Landmark
                {
                    Name = "Aéroport Félix Houphouët-Boigny", Kind = MapIconKind.Airport,
                    Position = new Vector3((p[0] + p[p.Length - 2]) * 0.5f, 0f, (p[1] + p[p.Length - 1]) * 0.5f),
                });
            }
            return list.ToArray();
        }
    }
}
