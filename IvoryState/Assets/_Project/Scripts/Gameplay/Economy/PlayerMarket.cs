using System;
using FishNet.Object;
using UnityEngine;

namespace IvoryState.Gameplay.Economy
{
    /// <summary>
    /// Djassa : vente de biens entre joueurs (voitures, maisons achetées), ou à l'État à moitié prix.
    /// Le bien mis en vente est mis de côté jusqu'à la vente ou au retrait de l'annonce. Le serveur
    /// (service Djassa) fait toutes les vérifications ; le client n'envoie que des numéros et un prix.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMarket : NetworkBehaviour
    {
        public enum Request : byte
        {
            List = 0,
            SellCar = 1,          // index : n° de la voiture dans le garage, prix
            SellHouse = 2,        // index : n° du bien (maison achetée du joueur), prix
            Buy = 3,              // index : n° de l'annonce
            Cancel = 4,           // index : n° de l'annonce
            SellCarToState = 5,   // index : n° de la voiture (moitié du prix neuf)
            SellItem = 6,         // index : emplacement dans le sac (0..7), prix
            BuyStoreFood = 7,     // index : index dans DjassaStoreCatalog.Foods
            BuyStoreMedicine = 8, // index : index dans DjassaStoreCatalog.Medicines
        }

        public const string KindCar = "car";
        public const string KindHouse = "house";
        public const string KindItem = "item";
        public const long MinPrice = 1000;
        public const long MaxPrice = 200000000;

        public static PlayerMarket Local { get; private set; }

        /// <summary>Serveur : (joueur, demande, numéro, prix).</summary>
        public static Action<PlayerMarket, Request, int, long> ServerHandler { get; set; }

        /// <summary>Client : annonces (n°, vendeurs, types, objets, libellés/extras, prix, miennes).</summary>
        public static event Action<int[], string[], string[], int[], string[], long[], bool[]> LocalAds;

        private float _nextRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            ServerHandler = null;
            LocalAds = null;
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

        public void Send(Request request, int index = 0, long price = 0)
        {
            if (IsOwner)
                ServerRequest(request, index, price);
        }

        [ServerRpc]
        private void ServerRequest(Request request, int index, long price)
        {
            if (Time.unscaledTime < _nextRequest)
                return;
            _nextRequest = Time.unscaledTime + (request == Request.List ? 0.5f : 1.5f);
            ServerHandler?.Invoke(this, request, index, price);
        }

        [Server]
        public void ServerSendAds(int[] ids, string[] sellers, string[] kinds, int[] items, string[] extras, long[] prices, bool[] mine)
        {
            if (Owner.IsValid)
                TargetAds(Owner, ids, sellers, kinds, items, extras, prices, mine);
        }

        [TargetRpc]
        private void TargetAds(FishNet.Connection.NetworkConnection connection, int[] ids, string[] sellers, string[] kinds,
            int[] items, string[] extras, long[] prices, bool[] mine)
        {
            LocalAds?.Invoke(ids, sellers, kinds, items, extras, prices, mine);
        }

        public static string PackItemExtra(string name, byte satiety, byte hydration, byte health)
        {
            string clean = (name ?? "Objet").Replace('|', ' ').Replace(';', ' ');
            return $"{clean}|{satiety}|{hydration}|{health}";
        }

        public static bool TryUnpackItemExtra(string extra, out string name, out byte satiety, out byte hydration, out byte health)
        {
            name = "Objet";
            satiety = hydration = health = 0;
            if (string.IsNullOrEmpty(extra))
                return false;
            string[] parts = extra.Split('|');
            if (parts.Length < 4 || string.IsNullOrEmpty(parts[0]))
                return false;
            name = parts[0];
            byte.TryParse(parts[1], out satiety);
            byte.TryParse(parts[2], out hydration);
            byte.TryParse(parts[3], out health);
            return true;
        }
    }
}
