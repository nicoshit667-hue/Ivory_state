using System;
using System.Collections.Generic;
using System.Text;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace IvoryState.Gameplay.Social
{
    /// <summary>
    /// Chat de proximité : on parle aux joueurs autour de soi, comme dans la rue.
    /// <list type="bullet">
    /// <item><b>Client</b> : <see cref="Send"/> envoie le texte au serveur.</item>
    /// <item><b>Serveur</b> : longueur bornée, cadence limitée (anti-spam), balises supprimées, puis
    /// diffusion par le service de chat (assembly Server) aux joueurs à portée de voix.</item>
    /// <item><b>Réception</b> : le message arrive sur le PlayerChat <i>du destinataire</i>, qui existe
    /// toujours chez lui. Même si l'émetteur n'est pas affiché chez lui (plafond de joueurs visibles),
    /// le message apparaît dans l'historique.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerChat : NetworkBehaviour
    {
        public const int MaxLength = 120;

        // Anti-spam : 3 messages d'affilée maximum, puis 1 par seconde.
        private const float TokensPerSecond = 1f;
        private const float MaxTokens = 3f;

        private static readonly List<PlayerChat> s_serverInstances = new List<PlayerChat>(128);
        private static readonly StringBuilder s_sanitizer = new StringBuilder(MaxLength);

        private float _tokens = MaxTokens;
        private float _lastTokenTime;

        /// <summary>Message reçu par le joueur local : (id d'objet de l'émetteur, nom, texte).</summary>
        public static event Action<int, string, string> LocalMessageReceived;

        /// <summary>Gestionnaire serveur (diffusion), enregistré par l'assembly Server.</summary>
        public static Action<PlayerChat, string> ServerHandler { get; set; }

        public static PlayerChat Local { get; private set; }
        public static IReadOnlyList<PlayerChat> ServerInstances => s_serverInstances;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LocalMessageReceived = null;
            ServerHandler = null;
            Local = null;
            s_serverInstances.Clear();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            s_serverInstances.Add(this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            s_serverInstances.Remove(this);
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

        /// <summary>Envoie un message (joueur local uniquement).</summary>
        public void Send(string text)
        {
            if (!IsOwner || string.IsNullOrWhiteSpace(text))
                return;

            text = text.Trim();
            if (text.Length > MaxLength)
                text = text.Substring(0, MaxLength);

            ServerSend(text);
        }

        [ServerRpc]
        private void ServerSend(string text)
        {
            if (!TryConsumeToken())
                return;

            string clean = Sanitize(text);
            if (clean.Length == 0)
                return;

            ServerHandler?.Invoke(this, clean);
        }

        /// <summary>Remet le message au client propriétaire de ce PlayerChat (le destinataire).</summary>
        [Server]
        public void ServerDeliver(int speakerObjectId, string speakerName, string text)
        {
            if (Owner.IsValid)
                TargetReceive(Owner, speakerObjectId, speakerName, text);
        }

        [TargetRpc]
        private void TargetReceive(NetworkConnection connection, int speakerObjectId, string speakerName, string text)
        {
            LocalMessageReceived?.Invoke(speakerObjectId, speakerName, text);
        }

        private bool TryConsumeToken()
        {
            float now = Time.unscaledTime;
            _tokens = Mathf.Min(MaxTokens, _tokens + (now - _lastTokenTime) * TokensPerSecond);
            _lastTokenTime = now;

            if (_tokens < 1f)
                return false;

            _tokens -= 1f;
            return true;
        }

        /// <summary>
        /// Nettoyage serveur : coupe à la longueur max, retire les caractères de contrôle et les chevrons
        /// (pas de balises de mise en forme injectées chez les autres joueurs), et compacte les espaces.
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            s_sanitizer.Clear();
            bool lastWasSpace = true;
            for (int i = 0; i < text.Length && s_sanitizer.Length < MaxLength; i++)
            {
                char c = text[i];

                // Balise de mise en forme (<b>, </color>…) : retirée en entier si elle se referme vite.
                if (c == '<')
                {
                    int close = text.IndexOf('>', i + 1, Mathf.Min(24, text.Length - i - 1));
                    if (close > 0)
                        i = close;
                    continue;
                }

                if (char.IsControl(c) || c == '>')
                    continue;

                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace && lastWasSpace)
                    continue;

                s_sanitizer.Append(isSpace ? ' ' : c);
                lastWasSpace = isSpace;
            }

            return s_sanitizer.ToString().TrimEnd();
        }
    }
}
