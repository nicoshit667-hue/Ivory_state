using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using IvoryState.Core.Diagnostics;
using IvoryState.Core.Networking;
using UnityEngine;

namespace IvoryState.Server
{
    /// <summary>
    /// Fait apparaître un avatar pour chaque client, côté serveur uniquement.
    /// <para>
    /// FishNet détruit (despawn) automatiquement les objets d'un client quand il se déconnecte.
    /// L'instanciation passe par le pool de FishNet pour éviter les allocations à chaque connexion.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ServerPlayerSpawner : MonoBehaviour
    {
        private const string LogTag = "Spawner";

        // Angle d'or : répartit les points d'apparition en spirale régulière, sans aléatoire.
        private const float GoldenAngle = 2.39996323f;

        private NetworkManager _networkManager;
        private NetworkConfig _config;

        public void Initialize(NetworkManager networkManager, NetworkConfig config)
        {
            Unsubscribe();

            _networkManager = networkManager;
            _config = config;

            if (_config.PlayerPrefab == null)
                GameLog.Error(LogTag, "Aucun PlayerPrefab dans la NetworkConfig : aucun joueur ne sera créé.");

            _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_networkManager == null)
                return;

            _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
            _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
                GameLog.Info(LogTag, "Client " + connection.ClientId + " connecté.");
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
                GameLog.Info(LogTag, "Client " + connection.ClientId + " déconnecté.");
        }

        // Appelé quand le client a chargé les scènes du serveur : il est prêt à recevoir des objets.
        private void OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer || _config.PlayerPrefab == null)
                return;

            Vector3 position = GetSpawnPosition(connection.ClientId);
            NetworkObject player = _networkManager.GetPooledInstantiated(
                _config.PlayerPrefab, position, Quaternion.identity, true);

            _networkManager.ServerManager.Spawn(player, connection);
            _networkManager.SceneManager.AddOwnerToDefaultScene(player);

            GameLog.Info(LogTag, "Joueur créé pour le client " + connection.ClientId + ".");
        }

        private Vector3 GetSpawnPosition(int clientId)
        {
            // Spirale de Vogel : chaque nouvel identifiant tombe à une place libre de la zone.
            int index = clientId % 64;
            float radius = _config.SpawnRadius * Mathf.Sqrt((index + 0.5f) / 64f);
            float angle = index * GoldenAngle;

            return _config.SpawnCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }
}
