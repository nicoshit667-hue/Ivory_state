using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using IvoryState.Core.Diagnostics;
using IvoryState.Core.Networking;
using IvoryState.Gameplay.Bots;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Server.Diagnostics
{
    /// <summary>
    /// Test de charge : fait apparaître N personnages pilotés par le serveur (<c>-serverBots N</c>).
    /// <para>
    /// Ils utilisent le même prefab et le même <see cref="PlayerMotor"/> que les vrais joueurs :
    /// le coût CPU de simulation et le trafic envoyé aux clients sont donc représentatifs.
    /// Seule la réception des entrées clientes n'est pas simulée (négligeable : 5 octets/tick).
    /// Cela permet de tester 100 joueurs sur un PC qui ne ferait pas tourner 100 clients Unity.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ServerBotSpawner : MonoBehaviour
    {
        private const string LogTag = "Bots";
        private const float CrowdRadius = 6f;

        private readonly List<NetworkObject> _bots = new List<NetworkObject>();

        public int Count => _bots.Count;

        public void Spawn(NetworkManager networkManager, NetworkConfig config, int count, float spread)
        {
            if (config.PlayerPrefab == null || count <= 0)
                return;

            float radius = spread > 0f ? spread : CrowdRadius;
            var random = new System.Random(12345); // reproductible d'un test à l'autre

            for (int i = 0; i < count; i++)
            {
                float distance = radius * Mathf.Sqrt((float)random.NextDouble());
                float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
                Vector3 position = config.SpawnCenter +
                                   new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                NetworkObject bot = networkManager.GetPooledInstantiated(config.PlayerPrefab, position, Quaternion.identity, true);
                networkManager.ServerManager.Spawn(bot); // sans propriétaire : le serveur est le contrôleur

                if (bot.TryGetComponent(out PlayerMotor motor))
                    motor.SetServerInput(new WanderBotBrain(bot.transform, config.SpawnCenter, radius, i + 1));

                _bots.Add(bot);
            }

            GameLog.Info(LogTag, count + " bots serveur créés (rayon " + radius + " m).");
        }
    }
}
