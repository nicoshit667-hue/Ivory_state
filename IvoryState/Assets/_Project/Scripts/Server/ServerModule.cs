using IvoryState.Core.Networking;
using IvoryState.Server.Backend;
using IvoryState.Server.Diagnostics;
using IvoryState.Server.Economy;
using IvoryState.Server.Social;
using IvoryState.Server.World;
using IvoryState.Server.Networking;
using UnityEngine;

namespace IvoryState.Server
{
    /// <summary>
    /// Point d'entrée de l'assembly serveur.
    /// <para>
    /// Cette assembly n'est compilée que dans l'éditeur, les builds Dedicated Server
    /// (<c>UNITY_SERVER</c>) et les builds de test serveur (<c>IVORY_SERVER</c>).
    /// Elle s'enregistre d'elle-même au démarrage : aucune autre assembly ne la référence.
    /// </para>
    /// </summary>
    internal static class ServerModule
    {
        // BeforeSceneLoad s'exécute après la remise à zéro (SubsystemRegistration) de NetworkBootstrap.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            NetworkBootstrap.RegisterServerModule();
            NetworkBootstrap.ServerStarted += OnServerStarted;
            NetworkBootstrap.ServerStopped += OnServerStopped;
        }

        private static void OnServerStarted(NetworkBootstrap bootstrap)
        {
            // Les systèmes serveur sont ajoutés à l'exécution : la scène ne contient aucun
            // composant serveur, donc pas de "missing script" dans les builds clients.
            GameObject host = bootstrap.gameObject;

            GetOrAdd<ServerPlayerSpawner>(host).Initialize(bootstrap.NetworkManager, bootstrap.Config);
            GetOrAdd<AccountService>(host).Initialize();
            GetOrAdd<EconomyService>(host);
            GetOrAdd<CommerceService>(host);
            GetOrAdd<TaxiFareService>(host);
            GetOrAdd<Homes.HomeService>(host);
            GetOrAdd<Homes.RentService>(host);
            GetOrAdd<Homes.PropertyService>(host);
            GetOrAdd<LotteryService>(host);
            GetOrAdd<Combat.HospitalService>(host);
            GetOrAdd<Combat.CombatService>(host);
            GetOrAdd<Social.WassupService>(host);
            GetOrAdd<Vehicles.GarageService>(host);
            GetOrAdd<DjassaService>(host);
            GetOrAdd<TransferService>(host).Initialize(bootstrap.NetworkManager);
            GetOrAdd<ChatService>(host);
            GetOrAdd<PhoneService>(host);
            GetOrAdd<ServerVehicleSpawner>(host).Spawn(bootstrap.NetworkManager, bootstrap.Config);
            int maxVisible = bootstrap.Arguments.MaxVisiblePlayers >= 0
                ? bootstrap.Arguments.MaxVisiblePlayers
                : bootstrap.Config.MaxVisiblePlayers;
            GetOrAdd<PlayerRelevanceUpdater>(host).Initialize(bootstrap.NetworkManager, maxVisible);

            // Statistiques : toujours actives sur un serveur headless, à la demande ailleurs.
            if (Application.isBatchMode || bootstrap.Arguments.ServerBots > 0)
                GetOrAdd<ServerStatsReporter>(host).Initialize(bootstrap.NetworkManager);

            LaunchArguments args = bootstrap.Arguments;
            if (args.ServerBots > 0)
                GetOrAdd<ServerBotSpawner>(host).Spawn(bootstrap.NetworkManager, bootstrap.Config, args.ServerBots, args.BotSpread);
        }

        private static void OnServerStopped(NetworkBootstrap bootstrap)
        {
            if (bootstrap == null)
                return;

            DestroyIfPresent<ServerPlayerSpawner>(bootstrap.gameObject);
            DestroyIfPresent<AccountService>(bootstrap.gameObject);
            DestroyIfPresent<EconomyService>(bootstrap.gameObject);
            DestroyIfPresent<CommerceService>(bootstrap.gameObject);
            DestroyIfPresent<TaxiFareService>(bootstrap.gameObject);
            DestroyIfPresent<Homes.HomeService>(bootstrap.gameObject);
            DestroyIfPresent<Homes.RentService>(bootstrap.gameObject);
            DestroyIfPresent<Homes.PropertyService>(bootstrap.gameObject);
            DestroyIfPresent<LotteryService>(bootstrap.gameObject);
            DestroyIfPresent<Combat.HospitalService>(bootstrap.gameObject);
            DestroyIfPresent<Combat.CombatService>(bootstrap.gameObject);
            DestroyIfPresent<Social.WassupService>(bootstrap.gameObject);
            DestroyIfPresent<Vehicles.GarageService>(bootstrap.gameObject);
            DestroyIfPresent<DjassaService>(bootstrap.gameObject);
            DestroyIfPresent<TransferService>(bootstrap.gameObject);
            DestroyIfPresent<ChatService>(bootstrap.gameObject);
            DestroyIfPresent<PhoneService>(bootstrap.gameObject);
            DestroyIfPresent<ServerVehicleSpawner>(bootstrap.gameObject);
            DestroyIfPresent<PlayerRelevanceUpdater>(bootstrap.gameObject);
            DestroyIfPresent<ServerStatsReporter>(bootstrap.gameObject);
            DestroyIfPresent<ServerBotSpawner>(bootstrap.gameObject);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent(out T component) ? component : go.AddComponent<T>();
        }

        private static void DestroyIfPresent<T>(GameObject go) where T : Component
        {
            if (go.TryGetComponent(out T component))
                Object.Destroy(component);
        }
    }
}
