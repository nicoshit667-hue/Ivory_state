using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace IvoryState.Gameplay.Networking
{
    /// <summary>
    /// Plafond de joueurs visibles par client : chaque client ne reçoit que ses
    /// <see cref="MaxVisiblePlayers"/> voisins les plus proches (serveur uniquement).
    /// <para>
    /// Complète la grille d'intérêt : la grille limite la <b>distance</b>, ce plafond limite le
    /// <b>nombre</b>. C'est ce qui protège la bande passante mobile dans une foule (marché,
    /// concert, maquis) où tout le monde est dans la même cellule.
    /// </para>
    /// <para>
    /// Le calcul est fait toutes les 0,5 s par le serveur (<see cref="Rebuild"/>), pas à chaque
    /// évaluation de condition. Une marge (hystérésis) évite qu'un joueur à la limite du classement
    /// apparaisse et disparaisse en boucle : chaque apparition coûte un message de spawn complet.
    /// </para>
    /// </summary>
    public static class PlayerRelevance
    {
        /// <summary>0 = pas de plafond.</summary>
        public static int MaxVisiblePlayers { get; set; }

        private static readonly List<NetworkObject> s_players = new List<NetworkObject>(128);
        private static readonly HashSet<int> s_playerIds = new HashSet<int>();
        private static readonly Dictionary<int, HashSet<int>> s_relevantByClient = new Dictionary<int, HashSet<int>>();
        private static readonly List<int> s_staleClients = new List<int>();

        // Tampons réutilisés : aucune allocation en régime établi.
        private static HashSet<int> s_scratchSet = new HashSet<int>();
        private static float[] s_sqrDistances = new float[128];
        private static int[] s_objectIds = new int[128];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            MaxVisiblePlayers = 0;
            s_players.Clear();
            s_playerIds.Clear();
            s_relevantByClient.Clear();
        }

        public static void Register(NetworkObject player)
        {
            if (s_playerIds.Add(player.ObjectId))
                s_players.Add(player);
        }

        public static void Unregister(NetworkObject player)
        {
            if (s_playerIds.Remove(player.ObjectId))
                s_players.Remove(player);
        }

        /// <summary>Appelé par la condition d'observation, pour chaque paire (client, objet).</summary>
        public static bool IsRelevant(NetworkConnection connection, NetworkObject networkObject)
        {
            if (MaxVisiblePlayers <= 0)
                return true;

            // Le plafond ne concerne que les joueurs (pas les futurs véhicules, objets…).
            if (!s_playerIds.Contains(networkObject.ObjectId))
                return true;

            // Un client doit toujours observer son propre personnage.
            if (networkObject.Owner == connection)
                return true;

            // Pas encore calculé pour ce client (connexion récente) : la grille seule décide.
            if (!s_relevantByClient.TryGetValue(connection.ClientId, out HashSet<int> relevant))
                return true;

            return relevant.Contains(networkObject.ObjectId);
        }

        /// <summary>Recalcule les voisins de chaque client. Serveur uniquement.</summary>
        public static void Rebuild(Dictionary<int, NetworkConnection> clients)
        {
            int limit = MaxVisiblePlayers;
            if (limit <= 0)
                return;

            int hysteresisLimit = limit + Mathf.Max(4, limit / 4);
            EnsureCapacity(s_players.Count);

            foreach (NetworkConnection connection in clients.Values)
            {
                NetworkObject self = connection.FirstObject;
                if (self == null)
                {
                    s_relevantByClient.Remove(connection.ClientId);
                    continue;
                }

                // Distances au carré vers tous les autres joueurs.
                Vector3 origin = self.transform.position;
                int count = 0;
                for (int i = 0; i < s_players.Count; i++)
                {
                    NetworkObject other = s_players[i];
                    if (other == null || other == self)
                        continue;

                    s_sqrDistances[count] = (other.transform.position - origin).sqrMagnitude;
                    s_objectIds[count] = other.ObjectId;
                    count++;
                }

                Array.Sort(s_sqrDistances, s_objectIds, 0, count);

                s_relevantByClient.TryGetValue(connection.ClientId, out HashSet<int> previous);
                HashSet<int> next = s_scratchSet;
                next.Clear();

                for (int rank = 0; rank < count && rank < hysteresisLimit; rank++)
                {
                    int id = s_objectIds[rank];
                    // Les `limit` plus proches entrent ; ceux déjà visibles restent jusqu'à la marge.
                    if (rank < limit || (previous != null && previous.Contains(id)))
                        next.Add(id);
                }

                s_relevantByClient[connection.ClientId] = next;
                s_scratchSet = previous ?? new HashSet<int>();
            }

            // Nettoyage des clients déconnectés.
            s_staleClients.Clear();
            foreach (int clientId in s_relevantByClient.Keys)
            {
                if (!clients.ContainsKey(clientId))
                    s_staleClients.Add(clientId);
            }

            for (int i = 0; i < s_staleClients.Count; i++)
                s_relevantByClient.Remove(s_staleClients[i]);
        }

        private static void EnsureCapacity(int count)
        {
            if (s_sqrDistances.Length >= count)
                return;

            int size = Mathf.NextPowerOfTwo(count);
            s_sqrDistances = new float[size];
            s_objectIds = new int[size];
        }
    }
}
