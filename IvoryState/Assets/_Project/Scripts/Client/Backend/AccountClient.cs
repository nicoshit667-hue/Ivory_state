using System;
using IvoryState.Core.Backend;
using IvoryState.Core.Diagnostics;
using IvoryState.Core.Networking;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.Backend
{
    /// <summary>
    /// Côté téléphone : transmet l'identifiant de l'installation au serveur de jeu, qui ouvre la session
    /// Nakama (en local) et charge le profil sauvegardé.
    /// <para>
    /// Identifiant d'installation : un GUID tiré au premier lancement et mémorisé dans les PlayerPrefs.
    /// C'est plus respectueux de la vie privée qu'un identifiant matériel et stable tant que l'app reste
    /// installée. Une connexion avec un compte (Google, téléphone…) pourra s'y ajouter plus tard.
    /// Tests : <c>-deviceId xxx</c> force l'identifiant (simuler le même joueur qui revient).
    /// </para>
    /// Si Nakama est injoignable, le jeu continue sans sauvegarde.
    /// </summary>
    internal sealed class AccountClient : MonoBehaviour
    {
        private const string LogTag = "Account";
        private const string DeviceKey = "ivory.device";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject(nameof(AccountClient));
            DontDestroyOnLoad(go);
            go.AddComponent<AccountClient>();
        }

        private void OnEnable()
        {
            PlayerNetworkIdentity.LocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        private void OnDisable()
        {
            PlayerNetworkIdentity.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        }

        // async void imposé par l'événement : exceptions rattrapées ici (sinon perdues hors des journaux).
        private async void OnLocalPlayerSpawned(PlayerNetworkIdentity player)
        {
            try
            {
                await LinkAccount();
            }
            catch (OperationCanceledException)
            {
                // Application en cours de fermeture : rien à faire.
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Liaison du compte interrompue : " + exception);
            }
        }

        private async Awaitable LinkAccount()
        {
            // Joueurs : compte e-mail (écran d'accueil, voir AccountPanel). Identifiant d'appareil réservé
            // aux clients automatiques (bots, tests de bout en bout).
            if (!UI.AccountPanel.IsAutomatedClient())
                return;

            // Le PlayerAccount local peut s'initialiser juste après l'identité : on attend qu'il existe.
            for (int i = 0; i < 60 && PlayerAccount.Local == null; i++)
                await Awaitable.NextFrameAsync(destroyCancellationToken);

            // Le serveur de jeu ouvre la session Nakama lui-même (Nakama reste privé, sur le serveur) :
            // un seul port à ouvrir pour jouer en ligne, celui du jeu.
            // Local est remis à null dans OnStopClient (vrai null, pas un objet détruit) : ?. est sûr ici.
            PlayerAccount.Local?.SendDeviceId(GetDeviceId());
        }

        private static string GetDeviceId()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-deviceId");
            if (index >= 0 && index + 1 < args.Length)
                return "test-" + args[index + 1];

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Plusieurs clients automatiques sur un même PC : un compte jetable chacun, sinon ils
            // partageraient le même compte et le serveur refuserait le deuxième.
            if (Application.isBatchMode || Application.isEditor)
                return "dev-" + Guid.NewGuid().ToString("N");
#endif

            string id = PlayerPrefs.GetString(DeviceKey, string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                id = "app-" + Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceKey, id);
                PlayerPrefs.Save();
            }

            return id;
        }
    }
}
