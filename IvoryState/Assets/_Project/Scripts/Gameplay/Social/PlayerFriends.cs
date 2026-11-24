using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Gameplay.Social
{
    /// <summary>
    /// Amis et messages privés.
    /// <list type="bullet">
    /// <item><b>Demande d'ami</b> : seulement à un joueur à moins de <see cref="RequestRange"/> m ; il
    /// accepte ou refuse. Liste enregistrée des deux côtés et sauvegardée dans le profil.</item>
    /// <item><b>Appels</b> : réservés aux amis (vérifié par le standard du serveur).</item>
    /// <item><b>Messages privés</b> : entre amis, remis au seul destinataire (personne d'autre ne les
    /// voit), s'il est connecté.</item>
    /// </list>
    /// Un ami est identifié par son compte (identifiant Nakama), ou par son pseudo en mode hors-ligne.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFriends : NetworkBehaviour
    {
        public const float RequestRange = 8f;
        public const int MaxFriends = 50;
        public const int MaxMessageLength = 200;

        private struct Friend
        {
            public string Key;
            public string Name;
        }

        private readonly List<Friend> _friends = new List<Friend>();
        private readonly Dictionary<int, float> _pendingFrom = new Dictionary<int, float>(); // ObjectId → heure de la demande
        private float _nextAction;

        public static PlayerFriends Local { get; private set; }

        /// <summary>Client : liste des amis (clés, pseudos, identifiant réseau si connecté, sinon -1).</summary>
        public static event Action<string[], string[], int[]> LocalFriendsReceived;

        /// <summary>Client : demande d'ami reçue (identifiant réseau et pseudo de l'auteur).</summary>
        public static event Action<int, string> LocalFriendRequest;

        /// <summary>Client : message privé (clé de l'ami, pseudo, texte, envoyé par moi ?).</summary>
        public static event Action<string, string, string, bool> LocalMessage;

        /// <summary>Serveur : la liste d'amis a changé (sauvegarde du profil).</summary>
        public static event Action<PlayerFriends> ServerFriendsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalFriendsReceived = null;
            LocalFriendRequest = null;
            LocalMessage = null;
            ServerFriendsChanged = null;
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

        #region Identité

        /// <summary>Serveur : clé stable du joueur (compte Nakama, ou pseudo hors-ligne).</summary>
        public string Key
        {
            get
            {
                string userId = TryGetComponent(out PlayerAccount account) ? account.ServerUserId : null;
                return userId ?? "nom:" + DisplayName;
            }
        }

        private string DisplayName => TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?";

        private static PlayerFriends FindOnline(string key)
        {
            var players = PlayerNetworkIdentity.ServerInstances;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].Owner.IsValid && players[i].TryGetComponent(out PlayerFriends friends) && friends.Key == key)
                    return friends;
            }
            return null;
        }

        private static PlayerFriends FindByObject(int objectId)
        {
            var players = PlayerNetworkIdentity.ServerInstances;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].ObjectId == objectId && players[i].TryGetComponent(out PlayerFriends friends))
                    return friends;
            }
            return null;
        }

        /// <summary>Serveur : vrai si <paramref name="other"/> est un ami.</summary>
        [Server]
        public bool ServerIsFriend(PlayerFriends other)
        {
            if (other == null)
                return false;
            string key = other.Key;
            foreach (Friend friend in _friends)
            {
                if (friend.Key == key)
                    return true;
            }
            return false;
        }

        private bool Throttle()
        {
            if (Time.unscaledTime < _nextAction)
                return true;
            _nextAction = Time.unscaledTime + 0.5f;
            return false;
        }

        private void Notify(string message)
        {
            if (TryGetComponent(out PlayerEconomy economy))
                economy.ServerNotify(message);
        }

        #endregion

        #region Liste d'amis

        public void RequestList() { if (IsOwner) ServerList(); }

        [ServerRpc]
        private void ServerList()
        {
            if (!Throttle())
                ServerSendList();
        }

        [Server]
        private void ServerSendList()
        {
            if (!Owner.IsValid)
                return;
            var keys = new string[_friends.Count];
            var names = new string[_friends.Count];
            var online = new int[_friends.Count];
            for (int i = 0; i < _friends.Count; i++)
            {
                keys[i] = _friends[i].Key;
                names[i] = _friends[i].Name;
                PlayerFriends friend = FindOnline(_friends[i].Key);
                online[i] = friend != null ? friend.ObjectId : -1;
            }
            TargetList(Owner, keys, names, online);
        }

        [TargetRpc]
        private void TargetList(NetworkConnection connection, string[] keys, string[] names, int[] online)
        {
            LocalFriendsReceived?.Invoke(keys, names, online);
        }

        #endregion

        #region Demandes

        /// <summary>Client : demande d'ami au joueur d'identifiant réseau donné (à proximité).</summary>
        public void SendRequest(int targetObjectId) { if (IsOwner) ServerRequest(targetObjectId); }

        [ServerRpc]
        private void ServerRequest(int targetObjectId)
        {
            if (Throttle())
                return;
            PlayerFriends target = FindByObject(targetObjectId);
            if (target == null || target == this || !target.Owner.IsValid)
            {
                Notify("Ce joueur n'est pas joignable.");
                return;
            }
            if ((target.transform.position - transform.position).sqrMagnitude > RequestRange * RequestRange)
            {
                Notify("Rapproche-toi de " + target.DisplayName + " pour l'ajouter en ami.");
                return;
            }
            if (ServerIsFriend(target))
            {
                Notify(target.DisplayName + " est déjà ton ami.");
                return;
            }
            if (_friends.Count >= MaxFriends || target._friends.Count >= MaxFriends)
            {
                Notify("Liste d'amis pleine (" + MaxFriends + ").");
                return;
            }

            target._pendingFrom[ObjectId] = Time.unscaledTime;
            target.TargetRequest(target.Owner, ObjectId, DisplayName);
            Notify("Demande d'ami envoyée à " + target.DisplayName + ".");
        }

        [TargetRpc]
        private void TargetRequest(NetworkConnection connection, int fromObjectId, string fromName)
        {
            LocalFriendRequest?.Invoke(fromObjectId, fromName);
        }

        /// <summary>Client : accepte ou refuse une demande reçue.</summary>
        public void Respond(int fromObjectId, bool accept) { if (IsOwner) ServerRespond(fromObjectId, accept); }

        [ServerRpc]
        private void ServerRespond(int fromObjectId, bool accept)
        {
            if (!_pendingFrom.TryGetValue(fromObjectId, out float at) || Time.unscaledTime - at > 120f)
                return;
            _pendingFrom.Remove(fromObjectId);
            PlayerFriends requester = FindByObject(fromObjectId);
            if (requester == null)
                return;

            if (!accept)
            {
                requester.Notify(DisplayName + " a refusé ta demande d'ami.");
                return;
            }

            ServerAdd(requester);
            requester.ServerAdd(this);
            Notify(requester.DisplayName + " est maintenant ton ami.");
            requester.Notify(DisplayName + " a accepté : vous êtes amis !");
            ServerSendList();
            requester.ServerSendList();
        }

        [Server]
        private void ServerAdd(PlayerFriends other)
        {
            if (ServerIsFriend(other))
                return;
            _friends.Add(new Friend { Key = other.Key, Name = other.DisplayName });
            ServerFriendsChanged?.Invoke(this);
        }

        #endregion

        #region Messages privés

        /// <summary>Client : message privé à un ami.</summary>
        public void SendPrivateMessage(string friendKey, string text) { if (IsOwner) ServerMessage(friendKey ?? "", text ?? ""); }

        [ServerRpc]
        private void ServerMessage(string friendKey, string text)
        {
            if (Throttle())
                return;
            text = text.Trim();
            if (text.Length == 0)
                return;
            if (text.Length > MaxMessageLength)
                text = text.Substring(0, MaxMessageLength);
            text = text.Replace('<', '‹').Replace('>', '›'); // pas de balises

            Friend? friend = null;
            foreach (Friend f in _friends)
            {
                if (f.Key == friendKey)
                    friend = f;
            }
            if (friend == null)
                return;

            PlayerFriends recipient = FindOnline(friendKey);
            if (recipient == null)
            {
                Notify(friend.Value.Name + " n'est pas connecté : message non remis.");
                return;
            }
            recipient.TargetMessage(recipient.Owner, Key, DisplayName, text, false);
            TargetMessage(Owner, friendKey, friend.Value.Name, text, true);
        }

        [TargetRpc]
        private void TargetMessage(NetworkConnection connection, string friendKey, string name, string text, bool outgoing)
        {
            LocalMessage?.Invoke(friendKey, name, text, outgoing);
        }

        #endregion

        #region Sauvegarde

        /// <summary>Texte compact : « clé|pseudo;… ».</summary>
        [Server]
        public string ServerSerialize()
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < _friends.Count; i++)
            {
                if (i > 0)
                    text.Append(';');
                text.Append(_friends[i].Key).Append('|').Append(_friends[i].Name.Replace('|', ' ').Replace(';', ' '));
            }
            return text.ToString();
        }

        [Server]
        public void ServerLoad(string data)
        {
            _friends.Clear();
            if (string.IsNullOrEmpty(data))
                return;
            foreach (string entry in data.Split(';'))
            {
                string[] parts = entry.Split('|');
                if (parts.Length == 2 && parts[0].Length > 0 && _friends.Count < MaxFriends)
                    _friends.Add(new Friend { Key = parts[0], Name = parts[1] });
            }
        }

        #endregion
    }
}
