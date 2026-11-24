#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Sonde du test de sauvegarde (<c>-e2ePersist</c>) : écrit dans le log le lien du compte puis chaque
    /// variation du solde (<c>[E2E-PERSIST] lié solde=…</c>, <c>[E2E-PERSIST] solde=…</c>). Le script
    /// tools/e2e-persist.ps1 compare le solde de fin de partie avec celui retrouvé à la reconnexion.
    /// </summary>
    internal sealed class PersistenceProbe : MonoBehaviour
    {
        private const string LogTag = "E2E-PERSIST";

        private bool _linkedLogged;
        private long _lastBalance = long.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-e2ePersist") < 0)
                return;

            var go = new GameObject(nameof(PersistenceProbe));
            DontDestroyOnLoad(go);
            go.AddComponent<PersistenceProbe>();
        }

        private void Update()
        {
            PlayerAccount account = PlayerAccount.Local;
            PlayerInteractor player = PlayerInteractor.Local;
            if (account == null || player == null || player.Economy == null || !account.IsLinked)
                return;

            long balance = player.Economy.Balance;
            if (!_linkedLogged)
            {
                _linkedLogged = true;
                _lastBalance = balance;
                GameLog.Info(LogTag, "lié solde=" + balance);
                return;
            }

            if (balance != _lastBalance)
            {
                _lastBalance = balance;
                GameLog.Info(LogTag, "solde=" + balance);
            }
        }
    }
}
#endif
