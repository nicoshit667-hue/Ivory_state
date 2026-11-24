using System;
using System.Collections.Generic;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;

namespace IvoryState.Server.Social
{
    /// <summary>
    /// Fil « Wassup? » (serveur uniquement) : 100 publications au plus, effacées au bout de 24 h, une
    /// publication par minute et par joueur, balises retirées. Le fil vit en mémoire : il repart à zéro
    /// au redémarrage du serveur (les publications sont temporaires de toute façon).
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class WassupService : MonoBehaviour
    {
        private const int MaxPosts = 100;

        private sealed class Post
        {
            public int Id;
            public string Author;
            public string Text;
            public long Time;
            public readonly HashSet<string> Likes = new HashSet<string>();
        }

        private readonly List<Post> _posts = new List<Post>();
        private readonly Dictionary<string, float> _lastPost = new Dictionary<string, float>();
        private int _nextId = 1;

        private void OnEnable()
        {
            PlayerWassup.ServerPostHandler = OnPost;
            PlayerWassup.ServerLikeHandler = OnLike;
            PlayerWassup.ServerFeedHandler = SendFeed;
        }

        private void OnDisable()
        {
            PlayerWassup.ServerPostHandler = null;
            PlayerWassup.ServerLikeHandler = null;
            PlayerWassup.ServerFeedHandler = null;
        }

        private static string KeyOf(PlayerWassup player) =>
            player.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null ? account.ServerUserId : "objet:" + player.ObjectId;

        private void OnPost(PlayerWassup player, string text)
        {
            if (player == null)
                return;
            text = (text ?? string.Empty).Replace("<", "‹").Replace(">", "›").Replace("\n", " ").Trim();
            if (text.Length == 0)
                return;
            if (text.Length > PlayerWassup.MaxLength)
                text = text.Substring(0, PlayerWassup.MaxLength);

            string key = KeyOf(player);
            if (_lastPost.TryGetValue(key, out float last) && Time.unscaledTime - last < PlayerWassup.PostCooldown)
            {
                if (player.TryGetComponent(out PlayerEconomy wait))
                    wait.ServerNotify("Doucement ! Une publication par minute sur Wassup?.");
                return;
            }
            _lastPost[key] = Time.unscaledTime;

            Prune();
            _posts.Add(new Post
            {
                Id = _nextId++,
                Author = player.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?",
                Text = text,
                Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            identity?.ServerAddXp(10, "wassup");
            if (_posts.Count > MaxPosts)
                _posts.RemoveAt(0);
            SendFeed(player);
        }

        private void OnLike(PlayerWassup player, int postId)
        {
            if (player == null)
                return;
            string key = KeyOf(player);
            foreach (Post post in _posts)
            {
                if (post.Id != postId)
                    continue;
                if (!post.Likes.Remove(key))
                    post.Likes.Add(key);
                break;
            }
            SendFeed(player);
        }

        private void SendFeed(PlayerWassup player)
        {
            if (player == null)
                return;
            Prune();
            string key = KeyOf(player);
            int count = Mathf.Min(PlayerWassup.FeedSize, _posts.Count);
            var ids = new int[count];
            var authors = new string[count];
            var texts = new string[count];
            var times = new long[count];
            var likes = new int[count];
            var liked = new bool[count];
            for (int i = 0; i < count; i++)
            {
                Post post = _posts[_posts.Count - 1 - i]; // les plus récentes d'abord
                ids[i] = post.Id;
                authors[i] = post.Author;
                texts[i] = post.Text;
                times[i] = post.Time;
                likes[i] = post.Likes.Count;
                liked[i] = post.Likes.Contains(key);
            }
            player.ServerSendFeed(ids, authors, texts, times, likes, liked);
        }

        private void Prune()
        {
            long limit = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - PlayerWassup.LifetimeSeconds;
            _posts.RemoveAll(post => post.Time < limit);
        }
    }
}
