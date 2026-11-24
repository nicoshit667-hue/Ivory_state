using FishNet.Managing;
using FishNet.Object;
using IvoryState.Core.Diagnostics;
using IvoryState.Core.Networking;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Server.World
{
    /// <summary>
    /// Fait apparaître un taxi sur chaque <see cref="TaxiStand"/> de la scène au démarrage du serveur.
    /// Les taxis appartiennent au serveur tant que personne ne les conduit.
    /// À l'arrêt, sans entrée, ils ne génèrent quasiment aucun trafic réseau.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ServerVehicleSpawner : MonoBehaviour
    {
        private const string LogTag = "Vehicles";

        public void Spawn(NetworkManager networkManager, NetworkConfig config)
        {
            if (config.VehiclePrefab == null)
            {
                GameLog.Warn(LogTag, "Aucun VehiclePrefab dans la NetworkConfig : pas de taxis.");
                return;
            }

            TaxiStand[] stands = FindObjectsByType<TaxiStand>(FindObjectsSortMode.None);
            foreach (TaxiStand stand in stands)
            {
                NetworkObject taxi = networkManager.GetPooledInstantiated(
                    config.VehiclePrefab, stand.transform.position, stand.transform.rotation, true);
                networkManager.ServerManager.Spawn(taxi);
            }

            GameLog.Info(LogTag, stands.Length + " taxis en station.");
        }
    }
}
