using System;
using FishNet.Object;
using UnityEngine;

namespace IvoryState.Gameplay.Economy
{
    /// <summary>
    /// Loto : un tirage chaque heure réelle (à l'heure pile). Le joueur choisit sa mise ; chaque ticket a
    /// 1 chance sur 10 de gagner, et rapporte alors un pourcentage de sa mise (×8). Les règles sont
    /// appliquées par le serveur (service du loto) ; le client n'envoie que la mise.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLottery : NetworkBehaviour
    {
        public static class Rules
        {
            public const int WinOneIn = 10;
            /// <summary>Gain : 800 % de la mise (la banque garde l'avantage : 0,8 rendu en moyenne).</summary>
            public const int PayoutPercent = 800;
            public const long MinStake = 100;
            public const long MaxStake = 100000;
            public const int MaxTicketsPerDraw = 5;

            public static long Payout(long stake) => stake * PayoutPercent / 100;

            /// <summary>Prochain tirage (heure pile suivante), en secondes Unix.</summary>
            public static long NextDraw(long unixSeconds) => (unixSeconds / 3600 + 1) * 3600;
        }

        /// <summary>Serveur : achat d'un ticket (joueur, mise).</summary>
        public static Action<PlayerLottery, long> ServerBuyHandler { get; set; }

        /// <summary>Serveur : demande de l'état (prochain tirage, mes tickets).</summary>
        public static Action<PlayerLottery> ServerStatusHandler { get; set; }

        /// <summary>Client : (prochain tirage en secondes Unix, mes mises pour ce tirage).</summary>
        public static event Action<long, long[]> LocalStatus;

        public static PlayerLottery Local { get; private set; }

        private float _nextRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ServerBuyHandler = null;
            ServerStatusHandler = null;
            LocalStatus = null;
            Local = null;
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

        public void Buy(long stake)
        {
            if (IsOwner && stake >= Rules.MinStake && stake <= Rules.MaxStake)
                ServerBuy(stake);
        }

        public void RequestStatus()
        {
            if (IsOwner)
                ServerStatus();
        }

        [ServerRpc]
        private void ServerBuy(long stake)
        {
            if (Time.unscaledTime < _nextRequest)
                return;
            _nextRequest = Time.unscaledTime + 0.5f;
            if (stake < Rules.MinStake || stake > Rules.MaxStake)
                return;
            ServerBuyHandler?.Invoke(this, stake);
        }

        [ServerRpc]
        private void ServerStatus()
        {
            ServerStatusHandler?.Invoke(this);
        }

        [Server]
        public void ServerSendStatus(long nextDraw, long[] stakes)
        {
            if (Owner.IsValid)
                TargetStatus(Owner, nextDraw, stakes);
        }

        [TargetRpc]
        private void TargetStatus(FishNet.Connection.NetworkConnection connection, long nextDraw, long[] stakes)
        {
            LocalStatus?.Invoke(nextDraw, stakes);
        }
    }
}
