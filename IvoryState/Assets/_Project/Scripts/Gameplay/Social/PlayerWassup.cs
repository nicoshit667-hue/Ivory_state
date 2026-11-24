using System;
using FishNet.Object;
using UnityEngine;

namespace IvoryState.Gameplay.Social
{
    /// <summary>
    /// « Wassup? » : le réseau social du téléphone. Publications courtes, visibles de tous les joueurs du
    /// serveur, temporaires (effacées au bout de 24 h). Le serveur garde le fil, filtre et limite.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWassup : NetworkBehaviour
    {
        public const int MaxLength = 140;
        public const int FeedSize = 15;
        public const float PostCooldown = 60f;
        public const long LifetimeSeconds = 24 * 3600;

        public static PlayerWassup Local { get; private set; }

        /// <summary>Serveur : publier (joueur, texte), aimer (joueur, n° de publication), lire le fil.</summary>
        public static Action<PlayerWassup, string> ServerPostHandler { get; set; }
        public static Action<PlayerWassup, int> ServerLikeHandler { get; set; }
        public static Action<PlayerWassup> ServerFeedHandler { get; set; }

        /// <summary>Client : fil reçu (n°, auteurs, textes, dates Unix, nombres de « j'aime », aimé par moi).</summary>
        public static event Action<int[], string[], string[], long[], int[], bool[]> LocalFeed;

        private float _nextPost, _nextRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            ServerPostHandler = null;
            ServerLikeHandler = null;
            ServerFeedHandler = null;
            LocalFeed = null;
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

        public void Post(string text)
        {
            if (!IsOwner || string.IsNullOrWhiteSpace(text))
                return;
            text = text.Trim();
            if (text.Length > MaxLength)
                text = text.Substring(0, MaxLength);
            ServerPost(text);
        }

        public void Like(int postId)
        {
            if (IsOwner)
                ServerLike(postId);
        }

        public void RequestFeed()
        {
            if (IsOwner)
                ServerFeed();
        }

        [ServerRpc]
        private void ServerPost(string text)
        {
            if (Time.unscaledTime < _nextPost || text == null)
                return;
            _nextPost = Time.unscaledTime + 2f; // le service applique la vraie limite (1 par minute)
            ServerPostHandler?.Invoke(this, text);
        }

        [ServerRpc]
        private void ServerLike(int postId)
        {
            if (Time.unscaledTime < _nextRequest)
                return;
            _nextRequest = Time.unscaledTime + 0.3f;
            ServerLikeHandler?.Invoke(this, postId);
        }

        [ServerRpc]
        private void ServerFeed()
        {
            if (Time.unscaledTime < _nextRequest)
                return;
            _nextRequest = Time.unscaledTime + 0.5f;
            ServerFeedHandler?.Invoke(this);
        }

        [Server]
        public void ServerSendFeed(int[] ids, string[] authors, string[] texts, long[] times, int[] likes, bool[] liked)
        {
            if (Owner.IsValid)
                TargetFeed(Owner, ids, authors, texts, times, likes, liked);
        }

        [TargetRpc]
        private void TargetFeed(FishNet.Connection.NetworkConnection connection, int[] ids, string[] authors, string[] texts,
            long[] times, int[] likes, bool[] liked)
        {
            LocalFeed?.Invoke(ids, authors, texts, times, likes, liked);
        }
    }
}
