using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Server.Economy
{
    /// <summary>
    /// Règles des transferts entre joueurs (serveur uniquement).
    /// <list type="bullet">
    /// <item>Destinataire : un vrai joueur connecté (pas un bot), différent de l'émetteur.</item>
    /// <item>Aucune distance minimale : l'envoi se fait par l'application Pingoin du téléphone.</item>
    /// <item>Montant entier entre 100 et 100 000 FCFA. L'émetteur doit avoir le solde.</item>
    /// <item>Un transfert toutes les 2 s par joueur (anti-spam, anti-erreur de double tap).</item>
    /// </list>
    /// Le débit et le crédit passent par PlayerEconomy, donc par la sauvegarde Nakama si elle est active,
    /// avec la raison « transfert vers / de ».
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class TransferService : MonoBehaviour
    {
        private const float Cooldown = 2f;

        private readonly Dictionary<int, float> _nextAllowed = new Dictionary<int, float>();
        private NetworkManager _networkManager;

        public void Initialize(NetworkManager networkManager)
        {
            _networkManager = networkManager;
            PlayerTransfers.ServerHandler = Handle;
        }

        private void OnDestroy()
        {
            if (PlayerTransfers.ServerHandler == (System.Action<PlayerTransfers, int, long>)Handle)
                PlayerTransfers.ServerHandler = null;
        }

        private void Handle(PlayerTransfers sender, int recipientObjectId, long amount)
        {
            PlayerEconomy senderEconomy = sender.Economy;
            if (senderEconomy == null)
                return;

            float now = Time.unscaledTime;
            if (_nextAllowed.TryGetValue(sender.ObjectId, out float allowedAt) && now < allowedAt)
                return; // double tap ou spam : ignoré sans message
            _nextAllowed[sender.ObjectId] = now + Cooldown;

            if (amount < PlayerTransfers.MinAmount || amount > PlayerTransfers.MaxAmount)
            {
                senderEconomy.ServerNotify("Montant invalide : entre " + Money.Format(PlayerTransfers.MinAmount) +
                                           " et " + Money.Format(PlayerTransfers.MaxAmount) + ".");
                return;
            }

            if (recipientObjectId == sender.ObjectId ||
                !_networkManager.ServerManager.Objects.Spawned.TryGetValue(recipientObjectId, out NetworkObject recipientObject) ||
                !recipientObject.Owner.IsValid ||
                !recipientObject.TryGetComponent(out PlayerEconomy recipientEconomy))
            {
                senderEconomy.ServerNotify("Ce joueur ne peut pas recevoir d'argent.");
                return;
            }

            // Pingoin est un porte-monnaie mobile : aucune condition de distance.
            string senderName = NameOf(sender);
            string recipientName = NameOf(recipientObject);

            if (!senderEconomy.ServerTrySpend(amount, "transfert vers " + recipientName))
            {
                senderEconomy.ServerNotify("Solde insuffisant pour envoyer " + Money.Format(amount) + ".");
                return;
            }

            recipientEconomy.ServerAdd(amount, "transfert de " + senderName);

            senderEconomy.ServerNotify("Pingoin : tu as envoyé " + Money.Format(amount) + " à " + recipientName + ".");
            recipientEconomy.ServerNotify("Pingoin : " + senderName + " t'a envoyé " + Money.Format(amount) + ".");
            GameLog.Info("Transfer", senderName + " -> " + recipientName + " : " + amount + " FCFA");
        }

        private static string NameOf(Component player)
        {
            return player.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?";
        }
    }
}
