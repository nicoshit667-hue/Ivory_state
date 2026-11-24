using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace IvoryState.Gameplay.Vehicles
{
    /// <summary>
    /// Permis de conduire et voitures du joueur : voitures achetées chez un concessionnaire (gardées pour
    /// toujours) et voiture de location (24 h). Depuis le téléphone (Garage), le joueur sort une de ses
    /// voitures près de lui ou la range. Écrit par le serveur (service du garage), lisible par le seul
    /// propriétaire.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGarage : NetworkBehaviour
    {
        public static class Rules
        {
            public const long LicensePrice = 25000;
            public const long RentPerDay = 15000;
            public const long RentSeconds = 24 * 3600;
            public const int MaxCars = 5;

            /// <summary>Prix d'achat par couleur (index de VehicleMotor.Paints, 1…).</summary>
            public static readonly long[] CarPrices = { 0, 2500000, 3000000, 2800000, 2800000, 2400000 };

            /// <summary>Permis exigé pour conduire (désactivable pour les tests : -noLicense).</summary>
            public static bool LicenseRequired => Array.IndexOf(Environment.GetCommandLineArgs(), "-noLicense") < 0;
        }

        private readonly SyncVar<bool> _license = new SyncVar<bool>(false,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<string> _cars = new SyncVar<string>(string.Empty,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<long> _rentalUntil = new SyncVar<long>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<byte> _rentalPaint = new SyncVar<byte>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        public static PlayerGarage Local { get; private set; }

        /// <summary>Client : permis, voitures ou location modifiés.</summary>
        public static event Action LocalChanged;

        /// <summary>Serveur : sortir une voiture (index dans Cars, -1 = location) ou ranger (-2).</summary>
        public static Action<PlayerGarage, int> ServerRequestHandler { get; set; }

        /// <summary>Serveur : garage modifié (à sauvegarder).</summary>
        public static event Action<PlayerGarage> ServerChanged;

        public const int RentalIndex = -1;
        public const int StoreIndex = -2;

        public bool HasLicense => _license.Value;
        public long RentalUntil => _rentalUntil.Value;
        public byte RentalPaint => _rentalPaint.Value;
        public string CarsText => _cars.Value;

        private readonly List<byte> _parsed = new List<byte>();
        private string _parsedFrom;
        private float _nextRequest;

        /// <summary>Couleurs des voitures achetées (index de VehicleMotor.Paints).</summary>
        public IReadOnlyList<byte> Cars
        {
            get
            {
                if (!ReferenceEquals(_parsedFrom, _cars.Value))
                {
                    _parsedFrom = _cars.Value;
                    _parsed.Clear();
                    foreach (string part in (_cars.Value ?? string.Empty).Split(','))
                    {
                        if (byte.TryParse(part, out byte paint) && paint > 0 && paint < VehicleMotor.Paints.Length)
                            _parsed.Add(paint);
                    }
                }
                return _parsed;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalChanged = null;
            ServerRequestHandler = null;
            ServerChanged = null;
        }

        private void Awake()
        {
            _license.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _cars.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _rentalUntil.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
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

        public void RequestSpawn(int index) => Send(index);
        public void RequestStore() => Send(StoreIndex);

        private void Send(int index)
        {
            if (IsOwner)
                ServerRequest(index);
        }

        [ServerRpc]
        private void ServerRequest(int index)
        {
            if (Time.unscaledTime < _nextRequest)
                return;
            _nextRequest = Time.unscaledTime + 1.5f;
            ServerRequestHandler?.Invoke(this, index);
        }

        // --- Serveur ---

        [Server]
        public void ServerLoad(bool license, string cars, long rentalUntil, byte rentalPaint)
        {
            _license.Value = license;
            _cars.Value = cars ?? string.Empty;
            _rentalUntil.Value = rentalUntil;
            _rentalPaint.Value = rentalPaint;
        }

        [Server]
        public void ServerGrantLicense()
        {
            _license.Value = true;
            ServerChanged?.Invoke(this);
        }

        [Server]
        public bool ServerAddCar(byte paint)
        {
            if (Cars.Count >= Rules.MaxCars)
                return false;
            _cars.Value = string.IsNullOrEmpty(_cars.Value) ? paint.ToString() : _cars.Value + "," + paint;
            ServerChanged?.Invoke(this);
            return true;
        }

        /// <summary>Serveur : retire une voiture du garage (vente) ; renvoie sa couleur, 0 si absente.</summary>
        [Server]
        public byte ServerRemoveCarAt(int index)
        {
            var cars = Cars;
            if (index < 0 || index >= cars.Count)
                return 0;
            byte paint = cars[index];
            var kept = new List<string>();
            for (int i = 0; i < cars.Count; i++)
            {
                if (i != index)
                    kept.Add(cars[i].ToString());
            }
            _cars.Value = string.Join(",", kept);
            ServerChanged?.Invoke(this);
            return paint;
        }

        [Server]
        public void ServerSetRental(byte paint, long until)
        {
            _rentalPaint.Value = paint;
            _rentalUntil.Value = until;
            ServerChanged?.Invoke(this);
        }
    }
}
