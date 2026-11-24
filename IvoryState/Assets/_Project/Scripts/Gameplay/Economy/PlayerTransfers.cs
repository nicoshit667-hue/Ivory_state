using System;
using FishNet.Object;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Gameplay.Economy
{
    /// <summary>
    /// Transfert d'argent entre joueurs par l'application Pingoin du téléphone (porte-monnaie mobile) :
    /// le destinataire peut être n'importe où dans la ville.
    /// <para>
    /// Le client envoie seulement « je donne N FCFA au joueur n° X ». Toutes les règles (distance,
    /// solde, bornes, cadence) sont appliquées par le service de transferts de l'assembly Server, absent des
    /// builds clients.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerTransfers : NetworkBehaviour
    {
        public const long MinAmount = 100;
        public const long MaxAmount = 100000;

        /// <summary>Distance maximale entre les deux joueurs (tolérance serveur comprise côté service).</summary>
        public const float Range = 4f;

        /// <summary>Gestionnaire serveur : (émetteur, identifiant d'objet du destinataire, montant).</summary>
        public static Action<PlayerTransfers, int, long> ServerHandler { get; set; }

        public static PlayerTransfers Local { get; private set; }

        public PlayerEconomy Economy { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ServerHandler = null;
            Local = null;
        }

        private void Awake()
        {
            Economy = GetComponent<PlayerEconomy>();
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

        /// <summary>Client : demande d'envoi au joueur indiqué.</summary>
        public void RequestSend(PlayerNetworkIdentity recipient, long amount)
        {
            if (recipient != null)
                RequestSend(recipient.ObjectId, amount);
        }

        /// <summary>Client (application Pingoin) : envoi au joueur d'identifiant réseau donné, où qu'il soit.</summary>
        public void RequestSend(int recipientObjectId, long amount)
        {
            if (IsOwner && recipientObjectId != ObjectId)
                ServerSend(recipientObjectId, amount);
        }

        [ServerRpc]
        private void ServerSend(int recipientObjectId, long amount)
        {
            ServerHandler?.Invoke(this, recipientObjectId, amount);
        }
    }
}
