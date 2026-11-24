using FishNet.Managing;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Networking;
using UnityEngine;

namespace IvoryState.Server.Networking
{
    /// <summary>
    /// Recalcule toutes les 0,5 s les voisins visibles de chaque client (<see cref="PlayerRelevance"/>).
    /// Rythme aligné sur la réévaluation des conditions d'observation par FishNet (≈ 0,5 s).
    /// Coût : N × M distances (100 joueurs × 100 clients = 10 000 par passe), sans allocation.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class PlayerRelevanceUpdater : MonoBehaviour
    {
        private const float Interval = 0.5f;

        private NetworkManager _networkManager;
        private float _nextRebuild;

        public void Initialize(NetworkManager networkManager, int maxVisiblePlayers)
        {
            _networkManager = networkManager;
            PlayerRelevance.MaxVisiblePlayers = maxVisiblePlayers;
            _nextRebuild = 0f;

            GameLog.Info("Relevance", maxVisiblePlayers > 0
                ? "Plafond : " + maxVisiblePlayers + " joueurs visibles par client."
                : "Pas de plafond de joueurs visibles.");
        }

        private void OnDestroy()
        {
            PlayerRelevance.MaxVisiblePlayers = 0;
        }

        private void Update()
        {
            if (_networkManager == null || Time.unscaledTime < _nextRebuild)
                return;

            _nextRebuild = Time.unscaledTime + Interval;
            PlayerRelevance.Rebuild(_networkManager.ServerManager.Clients);
        }
    }
}
