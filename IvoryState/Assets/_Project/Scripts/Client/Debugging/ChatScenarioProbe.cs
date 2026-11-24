#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Social;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Test de bout en bout du chat (builds de dev) :
    /// <list type="bullet">
    /// <item><c>-e2eChat</c> : écrit chaque message reçu dans le log (<c>[E2E-CHAT] reçu …</c>).</item>
    /// <item><c>-e2eChatSay</c> : en plus, envoie quelques messages 8 s après l'apparition, dont un
    /// message piégé (balises, espaces, longueur excessive) pour vérifier le nettoyage serveur.</item>
    /// </list>
    /// </summary>
    internal sealed class ChatScenarioProbe : MonoBehaviour
    {
        private const string LogTag = "E2E-CHAT";

        private bool _say;
        private float _sendAt = -1f;
        private int _sent;

        private static readonly string[] Messages =
        {
            "On dit quoi ?",
            "  <b>balise</b>   et    espaces   ",
            new string('x', 300),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool listen = Array.IndexOf(args, "-e2eChat") >= 0;
            bool say = Array.IndexOf(args, "-e2eChatSay") >= 0;
            if (!listen && !say)
                return;

            var go = new GameObject(nameof(ChatScenarioProbe));
            DontDestroyOnLoad(go);
            go.AddComponent<ChatScenarioProbe>()._say = say;
        }

        private void OnEnable()
        {
            PlayerChat.LocalMessageReceived += OnMessage;
        }

        private void OnDisable()
        {
            PlayerChat.LocalMessageReceived -= OnMessage;
        }

        private static void OnMessage(int speakerObjectId, string speakerName, string text)
        {
            GameLog.Info(LogTag, "reçu de " + speakerName + " (" + text.Length + " car.) : " + text);
        }

        private void Update()
        {
            if (!_say || PlayerChat.Local == null || _sent >= Messages.Length)
                return;

            if (_sendAt < 0f)
                _sendAt = Time.time + 8f;

            if (Time.time >= _sendAt)
            {
                PlayerChat.Local.Send(Messages[_sent]);
                GameLog.Info(LogTag, "envoyé n°" + (_sent + 1));
                _sent++;
                _sendAt = Time.time + 0.5f; // dans la limite de cadence (3 d'affilée)
            }
        }
    }
}
#endif
