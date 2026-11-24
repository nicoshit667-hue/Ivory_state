using System;
using FishNet.Object;
using UnityEngine;

namespace IvoryState.Gameplay.Economy
{
    /// <summary>
    /// Services du téléphone : commander un repas livré (il arrive dans le sac), accepter une offre
    /// d'emploi. Le client n'envoie que des numéros (plat, offre) ; les prix et les règles sont lus par
    /// le serveur dans <see cref="Catalog"/> et appliqués par le service économique (assembly Server).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerServices : NetworkBehaviour
    {
        public enum Request : byte { Food = 0, Job = 1 }

        public struct Dish
        {
            public string Name;
            public string Restaurant;
            public long Price;
            public int Satiety;
            public int Hydration;
        }

        public struct JobOffer
        {
            public string Title;
            public string Description;
            public bool Available;
        }

        /// <summary>Catalogue partagé par le client (affichage) et le serveur (règles).</summary>
        public static class Catalog
        {
            public const long DeliveryFee = 300;
            public const float DeliveryDelay = 25f;

            public static readonly Dish[] Dishes =
            {
                new Dish { Name = "Garba", Restaurant = "Chez Tantie Awa", Price = 1000, Satiety = 40 },
                new Dish { Name = "Attiéké poisson braisé", Restaurant = "Maquis du Plateau", Price = 1500, Satiety = 55 },
                new Dish { Name = "Alloco poulet", Restaurant = "Maquis du Plateau", Price = 1200, Satiety = 45 },
                new Dish { Name = "Riz sauce arachide", Restaurant = "Maman Adjoua", Price = 1000, Satiety = 45 },
                new Dish { Name = "Eau minérale 1,5 L", Restaurant = "Superette Express", Price = 400, Hydration = 60 },
                new Dish { Name = "Bissap bien glacé", Restaurant = "Superette Express", Price = 300, Satiety = 5, Hydration = 45 },
            };

            public const int DeliveryJob = 0;
            public const int TaxiJob = 1;
            public const int GbakaJob = 2;

            // Taxi commandé : prise en charge + prix au mètre (ligne droite × détour), plafonné.
            public const long TaxiBaseFare = 500;
            public const float TaxiFarePerMeter = 8f;
            public const float TaxiDetourFactor = 1.3f;
            public const long TaxiMaxFare = 10000;

            /// <summary>Prix d'une course commandée entre deux points (arrondi à 25 F, 10 000 F au plus).</summary>
            public static long TaxiFare(Vector3 from, Vector3 to)
            {
                Vector3 offset = to - from;
                offset.y = 0f;
                long raw = TaxiBaseFare + (long)(offset.magnitude * TaxiDetourFactor * TaxiFarePerMeter);
                raw = (raw + 12) / 25 * 25;
                return raw > TaxiMaxFare ? TaxiMaxFare : raw;
            }

            public static readonly JobOffer[] Jobs =
            {
                new JobOffer { Title = "Livreur express", Description = "Une adresse à livrer, payée selon la distance. Mission donnée tout de suite.", Available = true },
                new JobOffer { Title = "Chauffeur de taxi", Description = "Monte dans un taxi libre : les clients t'appellent tout seuls.", Available = true },
                new JobOffer { Title = "Apprenti de gbaka", Description = "Remplir le minibus à la gare. Bientôt disponible.", Available = false },
            };
        }

        /// <summary>Gestionnaire serveur : (joueur, type de demande, numéro).</summary>
        public static Action<PlayerServices, Request, int> ServerHandler { get; set; }

        /// <summary>Service des taxis (serveur) : commande vers une destination, ou annulation.</summary>
        public static Action<PlayerServices, Vector3> ServerTaxiCallHandler { get; set; }
        public static Action<PlayerServices> ServerTaxiCancelHandler { get; set; }

        public static PlayerServices Local { get; private set; }

        public enum TaxiState : byte { None = 0, Searching = 1, Coming = 2, Riding = 3 }

        /// <summary>Où en est ma commande de taxi (lisible par le seul propriétaire).</summary>
        private readonly FishNet.Object.Synchronizing.SyncVar<byte> _taxiState = new FishNet.Object.Synchronizing.SyncVar<byte>(0,
            new FishNet.Object.Synchronizing.SyncTypeSettings(FishNet.Object.Synchronizing.WritePermission.ServerOnly,
                FishNet.Object.Synchronizing.ReadPermission.OwnerOnly));

        private readonly FishNet.Object.Synchronizing.SyncVar<string> _taxiDriverName = new FishNet.Object.Synchronizing.SyncVar<string>(string.Empty,
            new FishNet.Object.Synchronizing.SyncTypeSettings(FishNet.Object.Synchronizing.WritePermission.ServerOnly,
                FishNet.Object.Synchronizing.ReadPermission.OwnerOnly));

        private readonly FishNet.Object.Synchronizing.SyncVar<int> _taxiVehicleId = new FishNet.Object.Synchronizing.SyncVar<int>(-1,
            new FishNet.Object.Synchronizing.SyncTypeSettings(FishNet.Object.Synchronizing.WritePermission.ServerOnly,
                FishNet.Object.Synchronizing.ReadPermission.OwnerOnly));

        private readonly FishNet.Object.Synchronizing.SyncVar<Vector3> _taxiDestination = new FishNet.Object.Synchronizing.SyncVar<Vector3>(Vector3.zero,
            new FishNet.Object.Synchronizing.SyncTypeSettings(FishNet.Object.Synchronizing.WritePermission.ServerOnly,
                FishNet.Object.Synchronizing.ReadPermission.OwnerOnly));

        private readonly FishNet.Object.Synchronizing.SyncVar<long> _taxiFare = new FishNet.Object.Synchronizing.SyncVar<long>(0,
            new FishNet.Object.Synchronizing.SyncTypeSettings(FishNet.Object.Synchronizing.WritePermission.ServerOnly,
                FishNet.Object.Synchronizing.ReadPermission.OwnerOnly));

        public TaxiState Taxi => (TaxiState)_taxiState.Value;
        public string TaxiDriverName => _taxiDriverName.Value ?? string.Empty;
        public int TaxiVehicleId => _taxiVehicleId.Value;
        public Vector3 TaxiDestination => _taxiDestination.Value;
        public long TaxiFare => _taxiFare.Value;

        /// <summary>Client : le serveur propose un point au GPS (client à aller chercher, destination…).</summary>
        public static event Action<Vector3> LocalWaypointSuggested;

        private float _nextRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ServerHandler = null;
            ServerTaxiCallHandler = null;
            ServerTaxiCancelHandler = null;
            LocalWaypointSuggested = null;
            Local = null;
        }

        /// <summary>Client : commande un taxi jusqu'à ce point (choisi sur la carte).</summary>
        public void CallTaxi(Vector3 destination)
        {
            if (IsOwner)
                ServerCallTaxi(destination);
        }

        public void CancelTaxi()
        {
            if (IsOwner)
                ServerCancelTaxi();
        }

        [ServerRpc]
        private void ServerCallTaxi(Vector3 destination)
        {
            if (Time.unscaledTime < _nextRequest)
                return;
            _nextRequest = Time.unscaledTime + 1f;
            if (float.IsNaN(destination.x) || float.IsNaN(destination.z) ||
                Mathf.Abs(destination.x) > 5000f || Mathf.Abs(destination.z) > 5000f)
                return;
            ServerTaxiCallHandler?.Invoke(this, destination);
        }

        [ServerRpc]
        private void ServerCancelTaxi()
        {
            ServerTaxiCancelHandler?.Invoke(this);
        }

        [Server]
        public void ServerSetTaxiState(TaxiState state)
        {
            _taxiState.Value = (byte)state;
            if (state == TaxiState.None)
                ServerClearTaxiTracking();
        }

        [Server]
        public void ServerSetTaxiCallInfo(Vector3 destination, long fare)
        {
            _taxiDestination.Value = destination;
            _taxiFare.Value = fare;
            _taxiDriverName.Value = string.Empty;
            _taxiVehicleId.Value = -1;
        }

        [Server]
        public void ServerSetTaxiDriver(string driverName, int vehicleObjectId)
        {
            _taxiDriverName.Value = driverName ?? string.Empty;
            _taxiVehicleId.Value = vehicleObjectId;
        }

        [Server]
        public void ServerClearTaxiTracking()
        {
            _taxiDriverName.Value = string.Empty;
            _taxiVehicleId.Value = -1;
            _taxiDestination.Value = Vector3.zero;
            _taxiFare.Value = 0;
        }

        /// <summary>Serveur : place un point sur le GPS du propriétaire.</summary>
        [Server]
        public void ServerSuggestWaypoint(Vector3 point)
        {
            if (Owner.IsValid)
                TargetSuggestWaypoint(Owner, point);
        }

        [TargetRpc]
        private void TargetSuggestWaypoint(FishNet.Connection.NetworkConnection connection, Vector3 point)
        {
            LocalWaypointSuggested?.Invoke(point);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
                Local = this;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local == this)
                Local = null;
        }

        public void OrderFood(int dish)
        {
            if (IsOwner && dish >= 0 && dish < Catalog.Dishes.Length)
                ServerRequest(Request.Food, dish);
        }

        public void AcceptJob(int job)
        {
            if (IsOwner && job >= 0 && job < Catalog.Jobs.Length)
                ServerRequest(Request.Job, job);
        }

        [ServerRpc]
        private void ServerRequest(Request kind, int index)
        {
            if (Time.unscaledTime < _nextRequest)
                return;
            _nextRequest = Time.unscaledTime + 1f;
            int count = kind == Request.Food ? Catalog.Dishes.Length : Catalog.Jobs.Length;
            if (index < 0 || index >= count)
                return;
            ServerHandler?.Invoke(this, kind, index);
        }
    }
}
