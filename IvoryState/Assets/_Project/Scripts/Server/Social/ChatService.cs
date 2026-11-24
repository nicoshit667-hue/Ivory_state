using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;

namespace IvoryState.Server.Social
{
    /// <summary>
    /// Diffusion du chat de proximité : un message est remis à chaque joueur connecté situé à moins de
    /// <see cref="HearingDistance"/> m de l'émetteur (lui compris, pour qu'il voie sa propre bulle).
    /// Le message est déjà nettoyé et limité en cadence par PlayerChat.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ChatService : MonoBehaviour
    {
        private const string LogTag = "Chat";
        private const float HearingDistance = 30f;

        private void OnEnable()
        {
            PlayerChat.ServerHandler = Broadcast;
        }

        private void OnDisable()
        {
            if (PlayerChat.ServerHandler == (System.Action<PlayerChat, string>)Broadcast)
                PlayerChat.ServerHandler = null;
        }

        private static void Broadcast(PlayerChat speaker, string text)
        {
            string speakerName = speaker.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?";
            Vector3 origin = speaker.transform.position;
            int recipients = 0;

            var all = PlayerChat.ServerInstances;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerChat listener = all[i];
                if (!listener.Owner.IsValid)
                    continue;

                if ((listener.transform.position - origin).sqrMagnitude <= HearingDistance * HearingDistance)
                {
                    listener.ServerDeliver(speaker.ObjectId, speakerName, text);
                    recipients++;
                }
            }

            GameLog.Info(LogTag, speakerName + " (" + recipients + " destinataires) : " + text);
        }
    }
}
