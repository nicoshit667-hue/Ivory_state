using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Gameplay.Social
{
    /// <summary>
    /// Voix de proximité : ce que dit un joueur (micro activé par le bouton du HUD) est relayé par le
    /// serveur aux seuls joueurs à moins de <see cref="Range"/> m, et joué en 3D depuis son personnage.
    /// <list type="bullet">
    /// <item>Le client n'envoie que des trames compressées (ADPCM, 40 ms) ; le serveur choisit les
    /// auditeurs sur ses positions, jamais le client.</item>
    /// <item>Débit plafonné par joueur (anti-abus), trames sur canal non fiable (temps réel).</item>
    /// </list>
    /// La capture et la lecture sont côté client (VoiceChat, ProximitySpeaker).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVoice : NetworkBehaviour
    {
        public const float Range = 25f;
        public const int MaxFrame = 512;
        private const int MaxBytesPerSecond = 16 * 1024;

        public static PlayerVoice Local { get; private set; }

        /// <summary>Client : trame de voix entendue, prononcée par le joueur <c>speaker</c>.</summary>
        public static event Action<PlayerVoice, ArraySegment<byte>> FrameHeard;

        private int _budget;
        private float _budgetResetAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            FrameHeard = null;
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

        /// <summary>Client : envoie une trame de voix de proximité.</summary>
        public void SendFrame(ArraySegment<byte> frame)
        {
            if (IsOwner && frame.Count > 0 && frame.Count <= MaxFrame)
                ServerFrame(frame);
        }

        [ServerRpc]
        private void ServerFrame(ArraySegment<byte> frame, Channel channel = Channel.Unreliable)
        {
            if (frame.Count == 0 || frame.Count > MaxFrame)
                return;

            float now = Time.unscaledTime;
            if (now >= _budgetResetAt)
            {
                _budget = 0;
                _budgetResetAt = now + 1f;
            }
            _budget += frame.Count;
            if (_budget > MaxBytesPerSecond)
                return;

            Vector3 position = transform.position;
            var players = PlayerNetworkIdentity.ServerInstances;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerNetworkIdentity listener = players[i];
                if (listener == null || listener.gameObject == gameObject || !listener.Owner.IsValid)
                    continue;
                if ((listener.transform.position - position).sqrMagnitude > Range * Range)
                    continue;
                TargetFrame(listener.Owner, frame);
            }
        }

        [TargetRpc]
        private void TargetFrame(NetworkConnection connection, ArraySegment<byte> frame, Channel channel = Channel.Unreliable)
        {
            FrameHeard?.Invoke(this, frame);
        }
    }
}
