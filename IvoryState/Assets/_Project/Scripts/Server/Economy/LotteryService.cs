using System.Collections.Generic;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Player;
using IvoryState.Server.Backend;
using UnityEngine;

namespace IvoryState.Server.Economy
{
    /// <summary>
    /// Loto (serveur uniquement) : tickets de l'heure en cours, tirage à l'heure pile. Chaque ticket a
    /// 1 chance sur 10 de gagner 800 % de sa mise. Un gagnant déconnecté est quand même payé (portefeuille
    /// Nakama). Les tickets ne survivent pas à un redémarrage du serveur : ils sont alors remboursés
    /// aux joueurs encore connectés.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class LotteryService : MonoBehaviour
    {
        private const string LogTag = "Loto";

        private sealed class Ticket
        {
            public PlayerLottery Player;
            public PlayerEconomy Economy;
            public string UserId;
            public string Name;
            public long Stake;
        }

        private readonly List<Ticket> _tickets = new List<Ticket>();
        private readonly System.Random _random = new System.Random();
        private long _drawAt;

        private void OnEnable()
        {
            PlayerLottery.ServerBuyHandler = Buy;
            PlayerLottery.ServerStatusHandler = SendStatus;
            _drawAt = PlayerLottery.Rules.NextDraw(Gameplay.Homes.RentRules.Now);
        }

        private void OnDisable()
        {
            PlayerLottery.ServerBuyHandler = null;
            PlayerLottery.ServerStatusHandler = null;
            // Arrêt du serveur avant le tirage : remboursement de ceux qui sont encore là.
            foreach (Ticket ticket in _tickets)
            {
                if (ticket.Economy != null)
                    ticket.Economy.ServerAdd(ticket.Stake, "loto : remboursement (tirage annulé)");
            }
            _tickets.Clear();
        }

        private void Update()
        {
            if (Gameplay.Homes.RentRules.Now < _drawAt)
                return;
            Draw();
            _drawAt = PlayerLottery.Rules.NextDraw(Gameplay.Homes.RentRules.Now);
        }

        private int CountTickets(PlayerLottery player)
        {
            int count = 0;
            foreach (Ticket ticket in _tickets)
            {
                if (ticket.Player == player)
                    count++;
            }
            return count;
        }

        private void Buy(PlayerLottery player, long stake)
        {
            if (player == null || !player.TryGetComponent(out PlayerEconomy economy))
                return;
            if (CountTickets(player) >= PlayerLottery.Rules.MaxTicketsPerDraw)
            {
                economy.ServerNotify("Tu as déjà " + PlayerLottery.Rules.MaxTicketsPerDraw + " tickets pour ce tirage.");
                return;
            }
            if (!economy.ServerTrySpend(stake, "ticket de loto"))
            {
                economy.ServerNotify("Pas assez d'argent pour cette mise.");
                return;
            }

            _tickets.Add(new Ticket
            {
                Player = player,
                Economy = economy,
                UserId = player.TryGetComponent(out PlayerAccount account) ? account.ServerUserId : null,
                Name = player.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?",
                Stake = stake,
            });
            economy.ServerNotify("Ticket validé : " + Money.Format(stake) + ". Tirage à " + FormatHour(_drawAt) +
                                 " — gain possible : " + Money.Format(PlayerLottery.Rules.Payout(stake)) + ".");
            SendStatus(player);
        }

        private void SendStatus(PlayerLottery player)
        {
            if (player == null)
                return;
            var stakes = new List<long>();
            foreach (Ticket ticket in _tickets)
            {
                if (ticket.Player == player)
                    stakes.Add(ticket.Stake);
            }
            player.ServerSendStatus(_drawAt, stakes.ToArray());
        }

        private void Draw()
        {
            if (_tickets.Count == 0)
                return;

            int winners = 0;
            AccountService accounts = GetComponent<AccountService>();
            foreach (Ticket ticket in _tickets)
            {
                bool win = _random.Next(PlayerLottery.Rules.WinOneIn) == 0;
                long prize = PlayerLottery.Rules.Payout(ticket.Stake);
                bool online = ticket.Economy != null;
                if (win)
                {
                    winners++;
                    if (online)
                    {
                        ticket.Economy.ServerAdd(prize, "gain au loto");
                        ticket.Economy.ServerNotify("LOTO : tu as GAGNÉ " + Money.Format(prize) + " ! (mise " + Money.Format(ticket.Stake) + ")");
                    }
                    else if (ticket.UserId != null && accounts != null)
                    {
                        accounts.ServerCreditOffline(ticket.UserId, prize, "gain au loto");
                    }
                }
                else if (online)
                {
                    ticket.Economy.ServerNotify("Loto de " + FormatHour(_drawAt) + " : pas de chance cette fois (mise " + Money.Format(ticket.Stake) + ").");
                }
            }
            GameLog.Info(LogTag, "Tirage : " + _tickets.Count + " ticket(s), " + winners + " gagnant(s).");
            _tickets.Clear();
        }

        private static string FormatHour(long unixSeconds) =>
            System.DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("HH:mm");
    }
}
