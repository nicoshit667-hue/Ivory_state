using System;
using System.Collections.Generic;
using IvoryState.Gameplay.Combat;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using IvoryState.Gameplay.World;
using UnityEngine;

namespace IvoryState.Client.UI
{
    internal enum TaxiCategory
    {
        All = 0,
        District = 1,
        Landmark = 2,
        Commerce = 3,
        Health = 4
    }

    internal struct TaxiDestination
    {
        public string Name;
        public string Badge;
        public TaxiCategory Category;
        public Vector3 Position;
        public bool PriorityHome;
        public float Distance;
    }

    internal struct TaxiTrackingSnapshot
    {
        public PlayerServices.TaxiState State;
        public string StatusTitle;
        public string StatusDetail;
        public Color AccentColor;
        public bool CanCancel;
    }

    /// <summary>
    /// Modèle et logique métier de l'application Taxi (destinations d'Abidjan, calcul du tarif,
    /// suivi en temps réel de l'approche du chauffeur et de la course en cours).
    /// </summary>
    internal static class TaxiRideModel
    {
        public const float MinCallDistance = 30f;
        private const float EstimatedSpeedMetersPerSec = 9.5f;

        public static TaxiTrackingSnapshot BuildTrackingSnapshot(PlayerServices services, Vector3 playerPos)
        {
            PlayerServices.TaxiState state = services != null ? services.Taxi : PlayerServices.TaxiState.None;
            switch (state)
            {
                case PlayerServices.TaxiState.Searching:
                    return new TaxiTrackingSnapshot
                    {
                        State = state,
                        StatusTitle = "Recherche d'un chauffeur…",
                        StatusDetail = services.TaxiFare > 0
                            ? "Tarif estimé : " + Money.Format(services.TaxiFare) + " · En attente d'acceptation."
                            : "Nous contactons les taxis disponibles à proximité.",
                        AccentColor = new Color(1f, 0.86f, 0.40f, 1f),
                        CanCancel = true,
                    };

                case PlayerServices.TaxiState.Coming:
                {
                    string driver = string.IsNullOrEmpty(services.TaxiDriverName) ? "Chauffeur assigné" : services.TaxiDriverName;
                    string eta = TryFindVehiclePosition(services.TaxiVehicleId, out Vector3 taxiPos)
                        ? FormatLiveEta(HorizontalDistance(playerPos, taxiPos))
                        : "En approche";
                    string fare = services.TaxiFare > 0 ? "  •  " + Money.Format(services.TaxiFare) : string.Empty;
                    return new TaxiTrackingSnapshot
                    {
                        State = state,
                        StatusTitle = "Chauffeur en route : " + driver,
                        StatusDetail = eta + fare + " — Reste sur le trottoir.",
                        AccentColor = new Color(0.45f, 1f, 0.62f, 1f),
                        CanCancel = true,
                    };
                }

                case PlayerServices.TaxiState.Riding:
                {
                    string driver = string.IsNullOrEmpty(services.TaxiDriverName) ? "Chauffeur" : services.TaxiDriverName;
                    float remaining = HorizontalDistance(playerPos, services.TaxiDestination);
                    string fare = services.TaxiFare > 0 ? Money.Format(services.TaxiFare) : "compteur";
                    return new TaxiTrackingSnapshot
                    {
                        State = state,
                        StatusTitle = "Course en cours (" + driver + ")",
                        StatusDetail = "Reste " + FormatDistance(remaining) + " (~" + FormatSecondsEta(remaining) + ")  •  " + fare,
                        AccentColor = new Color(0.50f, 0.88f, 1f, 1f),
                        CanCancel = false,
                    };
                }

                default:
                    return new TaxiTrackingSnapshot
                    {
                        State = PlayerServices.TaxiState.None,
                        StatusTitle = FormatNearestFreeTaxiHint(),
                        StatusDetail = "Choisis une destination ci-dessous ou marque un point sur la carte GPS.",
                        AccentColor = new Color(1f, 1f, 1f, 0.90f),
                        CanCancel = false,
                    };
            }
        }

        public static void CollectDestinations(Vector3 origin, List<TaxiDestination> output)
        {
            output.Clear();

            // 1. Domiciles du joueur
            PlayerHome home = PlayerHome.Local;
            if (home != null)
            {
                if (home.HasHome)
                {
                    TryAdd(output, "Mon studio n° " + home.Studio + " (Treichville)", "Domicile", TaxiCategory.Landmark,
                        CityPlaces.Residence.Position, origin, priorityHome: true);
                }
                for (int slot = 0; slot < PlayerHome.MaxProperties; slot++)
                {
                    int propId = home.PropertyAt(slot);
                    if (!PropertyCatalog.IsValid(propId))
                        continue;
                    var prop = PropertyCatalog.Get(propId);
                    TryAdd(output, "Mon logement — " + prop.Label, "Domicile", TaxiCategory.Landmark,
                        PropertyCatalog.EntrancePosition(propId), origin, priorityHome: true);
                }
            }

            // 2. Hôpitaux / CHU
            for (int i = 0; i < HospitalCatalog.Hospitals.Length; i++)
            {
                HospitalCatalog.Hospital hospital = HospitalCatalog.Hospitals[i];
                TryAdd(output, hospital.Name, "Hôpital", TaxiCategory.Health, hospital.Entrance, origin);
            }

            // 3. Commerces d'Abidjan (Maquis, Restaurants, Fast-Foods, Boutiques, Pharmacies)
            for (int i = 0; i < CommerceCatalog.All.Length; i++)
            {
                CommerceDefinition comm = CommerceCatalog.All[i];
                TaxiCategory cat = comm.Kind == CommerceKind.Pharmacie ? TaxiCategory.Health : TaxiCategory.Commerce;
                TryAdd(output, comm.Name, CommerceCatalog.KindLabel(comm.Kind) + " · " + comm.District, cat,
                    CommerceCatalog.GetWorldPosition(comm), origin);
            }

            // 4. Quartiers d'Abidjan
            CityLayout.DistrictData[] districts = CityLayout.Layout?.districts;
            if (districts != null)
            {
                for (int i = 0; i < districts.Length; i++)
                {
                    CityLayout.DistrictData d = districts[i];
                    if (d == null || string.IsNullOrWhiteSpace(d.name))
                        continue;
                    TryAdd(output, "Quartier " + d.name, "Quartier", TaxiCategory.District,
                        new Vector3(d.x, origin.y, d.z), origin);
                }
            }

            // 5. Repères principaux de la carte
            MapLandmarks.Landmark[] landmarks = MapLandmarks.All;
            if (landmarks != null)
            {
                for (int i = 0; i < landmarks.Length; i++)
                {
                    MapLandmarks.Landmark lm = landmarks[i];
                    if (string.IsNullOrWhiteSpace(lm.Name) || lm.Kind == MapIconKind.Home)
                        continue;
                    TaxiCategory cat = lm.Kind == MapIconKind.Hospital ? TaxiCategory.Health
                        : (lm.Kind == MapIconKind.Dealership || lm.Kind == MapIconKind.Clothing ||
                           lm.Kind == MapIconKind.Market || lm.Kind == MapIconKind.BlackMarket ||
                           lm.Kind == MapIconKind.CashPoint) ? TaxiCategory.Commerce
                        : TaxiCategory.Landmark;
                    TryAdd(output, lm.Name, CategoryBadge(cat), cat, lm.Position, origin);
                }
            }

            // 6. Autres points d'interaction du monde
            var worldPoints = WorldInteractable.All;
            for (int i = 0; i < worldPoints.Count; i++)
            {
                WorldInteractable wp = worldPoints[i];
                if (wp == null || string.IsNullOrWhiteSpace(wp.DisplayName))
                    continue;
                if (!TryMapInteractableKind(wp.Kind, out TaxiCategory cat, out string badge))
                    continue;
                TryAdd(output, wp.DisplayName, badge, cat, wp.transform.position, origin);
            }

            output.Sort((a, b) =>
            {
                if (a.PriorityHome != b.PriorityHome)
                    return a.PriorityHome ? -1 : 1;
                return a.Distance.CompareTo(b.Distance);
            });
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static string FormatDistance(float meters)
        {
            return meters >= 1000f
                ? (meters / 1000f).ToString("0.0") + " km"
                : Mathf.RoundToInt(meters) + " m";
        }

        private static string FormatLiveEta(float meters)
        {
            return "À " + FormatDistance(meters) + " (~" + FormatSecondsEta(meters) + ")";
        }

        private static string FormatSecondsEta(float meters)
        {
            int seconds = Mathf.Max(5, Mathf.RoundToInt(meters / EstimatedSpeedMetersPerSec));
            return seconds >= 60 ? (seconds / 60) + " min " + (seconds % 60).ToString("00") + " s" : seconds + " s";
        }

        private static bool TryFindVehiclePosition(int objectId, out Vector3 position)
        {
            if (objectId >= 0)
            {
                var all = VehicleMotor.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && all[i].ObjectId == objectId)
                    {
                        position = all[i].transform.position;
                        return true;
                    }
                }
            }
            position = Vector3.zero;
            return false;
        }

        private static string FormatNearestFreeTaxiHint()
        {
            return TryDescribeNearestFreeTaxi(out int meters, out string direction)
                ? "Prêt · Taxi libre à " + meters + " m " + direction
                : "Commander un taxi vers n'importe quel lieu";
        }

        public static bool TryDescribeNearestFreeTaxi(out int meters, out string direction)
        {
            meters = 0;
            direction = string.Empty;
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local == null)
                return false;

            VehicleMotor best = null;
            float bestSqr = float.MaxValue;
            var all = VehicleMotor.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || all[i].HasDriver)
                    continue;
                float sqr = (all[i].transform.position - local.transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = all[i];
                    bestSqr = sqr;
                }
            }
            if (best == null)
                return false;

            Vector3 offset = best.transform.position - local.transform.position;
            string[] directions = { "au nord", "au nord-est", "à l'est", "au sud-est", "au sud", "au sud-ouest", "à l'ouest", "au nord-ouest" };
            float angle = Mathf.Repeat(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 22.5f, 360f);
            meters = Mathf.RoundToInt(offset.magnitude);
            direction = directions[(int)(angle / 45f) % 8];
            return true;
        }

        private static bool TryMapInteractableKind(InteractionKind kind, out TaxiCategory category, out string badge)
        {
            switch (kind)
            {
                case InteractionKind.GbakaStation:
                    category = TaxiCategory.Landmark; badge = "Gare Gbaka"; return true;
                case InteractionKind.JobBoard:
                    category = TaxiCategory.Landmark; badge = "Emplois"; return true;
                case InteractionKind.DrivingSchool:
                    category = TaxiCategory.Landmark; badge = "Auto-école"; return true;
                case InteractionKind.Dealership:
                    category = TaxiCategory.Commerce; badge = "Concessionnaire"; return true;
                case InteractionKind.ClothingStore:
                    category = TaxiCategory.Commerce; badge = "Mode"; return true;
                case InteractionKind.BlackMarket:
                    category = TaxiCategory.Commerce; badge = "Marché"; return true;
                case InteractionKind.Commerce:
                case InteractionKind.Store:
                case InteractionKind.Shop:
                    category = TaxiCategory.Commerce; badge = "Commerce"; return true;
                case InteractionKind.CashPoint:
                    category = TaxiCategory.Commerce; badge = "Point Cash"; return true;
                default:
                    category = TaxiCategory.Landmark; badge = string.Empty; return false;
            }
        }

        private static string CategoryBadge(TaxiCategory category) => category switch
        {
            TaxiCategory.District => "Quartier",
            TaxiCategory.Health => "Santé",
            TaxiCategory.Commerce => "Commerce",
            _ => "Lieu",
        };

        private static void TryAdd(List<TaxiDestination> list, string name, string badge, TaxiCategory category, Vector3 pos, Vector3 origin, bool priorityHome = false)
        {
            for (int i = 0; i < list.Count; i++)
            {
                TaxiDestination existing = list[i];
                bool sameName = string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase);
                float gap = HorizontalDistance(existing.Position, pos);
                if ((sameName && gap < 60f) || (!priorityHome && gap < 14f && existing.Category == category))
                    return;
            }

            list.Add(new TaxiDestination
            {
                Name = name,
                Badge = badge,
                Category = category,
                Position = pos,
                PriorityHome = priorityHome,
                Distance = HorizontalDistance(origin, pos),
            });
        }
    }
}
