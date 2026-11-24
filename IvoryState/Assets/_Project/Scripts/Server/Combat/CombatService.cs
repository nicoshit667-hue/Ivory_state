using System.Collections.Generic;
using IvoryState.Gameplay.Combat;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Server.Combat
{
    /// <summary>
    /// Bagarres (serveur uniquement) : chaque coup est revérifié (distance mesurée sur les positions
    /// serveur, cadence, zones protégées, joueurs à l'hôpital ou en véhicule). Un coup retire 8 à 12 de
    /// santé ; à 0, la victime est K.O. et se réveille à l'hôpital. L'attaquant reçoit alors un crime au
    /// casier, effacé au bout d'un jour réel s'il n'est pas arrêté (la police viendra plus tard).
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed partial class CombatService : MonoBehaviour
    {
        private const float ServerReachTolerance = 0.9f;
        private const int MinDamage = 8, MaxDamage = 12;
        private const long CrimeLifetimeSeconds = 24 * 3600;

        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<PlayerCombat, float> _nextWarning = new Dictionary<PlayerCombat, float>();
        private readonly Dictionary<PlayerCombat, List<long>> _crimes = new Dictionary<PlayerCombat, List<long>>();
        private readonly List<PlayerCombat> _stale = new List<PlayerCombat>();
        private float _nextPrune;

        private readonly Dictionary<PlayerCombat, float> _robbedAt = new Dictionary<PlayerCombat, float>();
        private readonly Dictionary<PlayerCombat, float> _nextRob = new Dictionary<PlayerCombat, float>();

        private void OnEnable()
        {
            PlayerCombat.ServerPunchHandler = OnPunch;
            PlayerCombat.ServerRobHandler = OnRob;
            PlayerCombat.ServerFireHandler = OnFire;
        }

        private void OnDisable()
        {
            PlayerCombat.ServerPunchHandler = null;
            PlayerCombat.ServerRobHandler = null;
            PlayerCombat.ServerFireHandler = null;
        }

        /// <summary>Racket à l'arme blanche : toutes les conditions de <see cref="RobRules"/> sont revérifiées ici.</summary>
        private void OnRob(PlayerCombat attacker, PlayerCombat victim)
        {
            if (attacker == null || victim == null || !victim.Owner.IsValid)
                return;
            if (!attacker.TryGetComponent(out PlayerEconomy attackerWallet) || !victim.TryGetComponent(out PlayerEconomy victimWallet))
                return;
            if (!attacker.TryGetComponent(out PlayerInventory attackerBag) || !attackerBag.HasWeapon)
            {
                Warn(attacker, "Il te faut une arme (machette, couteau à la quincaillerie) pour racketter.");
                return;
            }
            if (IsInVehicle(attacker) || IsInVehicle(victim))
                return;

            Vector3 offset = victim.transform.position - attacker.transform.position;
            offset.y = 0f;
            float reach = PlayerCombat.Reach + ServerReachTolerance;
            if (offset.sqrMagnitude > reach * reach)
                return;

            long now = RentRules.Now;
            if (attacker.RecoveringUntil > now || victim.RecoveringUntil > now)
                return;
            if (ProtectedZones.Contains(attacker.transform.position) || ProtectedZones.Contains(victim.transform.position))
            {
                Warn(attacker, "Zone protégée : pas de racket ici.");
                return;
            }
            if (_nextRob.TryGetValue(attacker, out float next) && Time.time < next)
            {
                Warn(attacker, "Attends un peu avant de recommencer.");
                return;
            }
            string victimName = victim.TryGetComponent(out PlayerNetworkIdentity victimIdentity) ? victimIdentity.DisplayName : "ce joueur";
            if (victim.TryGetComponent(out PlayerInventory victimBag) && victimBag.HasWeapon)
            {
                Warn(attacker, victimName + " est armé lui aussi : il ne se laisse pas faire.");
                return;
            }
            if (HasWitness(attacker, victim))
            {
                Warn(attacker, "Il y a du monde autour de " + victimName + " : impossible de le racketter.");
                return;
            }
            if (_robbedAt.TryGetValue(victim, out float robbed) && Time.time - robbed < RobRules.VictimCooldown)
            {
                Warn(attacker, victimName + " vient déjà de se faire racketter : il n'a plus rien sur lui.");
                return;
            }
            long cash = victimWallet.Cash;
            if (cash <= 0)
            {
                Warn(attacker, victimName + " n'a pas d'espèces sur lui.");
                return;
            }

            _nextRob[attacker] = Time.time + RobRules.AttackerCooldown;
            if (!victimWallet.ServerTrySpendCash(cash))
                return;
            attackerWallet.ServerAddCash(cash);
            _robbedAt[victim] = Time.time;
            AddCrime(attacker, now);

            string attackerName = attacker.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "Quelqu'un";
            attacker.ServerPlayRob(victim, attackerName, cash);
            victimWallet.ServerNotify(attackerName + " t'a racketté : -" + Money.Format(cash) + " en espèces. Garde ton argent sur Pingoin !");
            attackerWallet.ServerNotify("Racket : +" + Money.Format(cash) + " en espèces. C'est un crime, inscrit à ton casier pour 24 h.");
        }

        /// <summary>Un autre joueur (ni l'agresseur ni la victime) près de la victime : témoin ou protecteur.</summary>
        private static bool HasWitness(PlayerCombat attacker, PlayerCombat victim)
        {
            Vector3 center = victim.transform.position;
            float radius = RobRules.WitnessRadius;
            foreach (PlayerNetworkIdentity other in PlayerNetworkIdentity.ServerInstances)
            {
                if (other == null || other.gameObject == attacker.gameObject || other.gameObject == victim.gameObject)
                    continue;
                Vector3 offset = other.transform.position - center;
                if (offset.sqrMagnitude <= radius * radius)
                    return true;
            }
            return false;
        }

        private void OnPunch(PlayerCombat attacker, PlayerCombat target)
        {
            if (attacker == null || target == null || !target.Owner.IsValid)
                return;
            if (!attacker.TryGetComponent(out PlayerNeeds attackerNeeds) || !target.TryGetComponent(out PlayerNeeds targetNeeds))
                return;
            if (IsInVehicle(attacker) || IsInVehicle(target))
                return;

            Vector3 offset = target.transform.position - attacker.transform.position;
            offset.y = 0f;
            float reach = PlayerCombat.Reach + ServerReachTolerance;
            if (offset.sqrMagnitude > reach * reach)
                return;

            long now = RentRules.Now;
            if (attacker.RecoveringUntil > now || target.RecoveringUntil > now || attacker.IsStunned)
                return;
            if (ProtectedZones.Contains(attacker.transform.position) || ProtectedZones.Contains(target.transform.position))
            {
                Warn(attacker, "Zone protégée : on ne se bat pas ici.");
                return;
            }

            int damage = _random.Next(MinDamage, MaxDamage + 1);
            targetNeeds.ServerChangeHealth(-damage);
            bool knockout = targetNeeds.Health == 0;
            string attackerName = attacker.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "Quelqu'un";
            attacker.ServerPlayPunch(target, attackerName, knockout);

            if (!knockout)
                return;

            string victimName = target.TryGetComponent(out PlayerNetworkIdentity victimIdentity) ? victimIdentity.DisplayName : "ta victime";
            HospitalService.Instance?.Admit(targetNeeds, HospitalCatalog.KnockoutFee, "K.O. par " + attackerName);
            AddCrime(attacker, now);
            if (attacker.TryGetComponent(out PlayerEconomy economy))
                economy.ServerNotify("Tu as mis " + victimName + " K.O. : c'est un crime, inscrit à ton casier pour 24 h. La police peut t'arrêter.");
        }

        private static bool IsInVehicle(PlayerCombat player) =>
            player.TryGetComponent(out PlayerDriving driving) && driving.IsInVehicle;

        private void Warn(PlayerCombat player, string message)
        {
            if (_nextWarning.TryGetValue(player, out float next) && Time.time < next)
                return;
            _nextWarning[player] = Time.time + 3f;
            if (player.TryGetComponent(out PlayerEconomy economy))
                economy.ServerNotify(message);
        }

        private void AddCrime(PlayerCombat player, long now)
        {
            if (!_crimes.TryGetValue(player, out List<long> list))
            {
                list = new List<long>();
                _crimes[player] = list;
            }
            list.Add(now);
            player.ServerSetCrimes(list.Count);
        }

        private void Update()
        {
            if (Time.time < _nextPrune)
                return;
            _nextPrune = Time.time + 30f;

            long now = RentRules.Now;
            _stale.Clear();
            foreach (KeyValuePair<PlayerCombat, List<long>> pair in _crimes)
            {
                if (pair.Key == null)
                {
                    _stale.Add(pair.Key);
                    continue;
                }
                int before = pair.Value.Count;
                pair.Value.RemoveAll(time => now - time > CrimeLifetimeSeconds);
                if (pair.Value.Count != before)
                    pair.Key.ServerSetCrimes(pair.Value.Count);
            }
            foreach (PlayerCombat key in _stale)
            {
                _crimes.Remove(key);
                _nextWarning.Remove(key);
            }

            // Rackets : on oublie les joueurs partis et les délais écoulés.
            _stale.Clear();
            foreach (KeyValuePair<PlayerCombat, float> pair in _robbedAt)
            {
                if (pair.Key == null || Time.time - pair.Value > RobRules.VictimCooldown)
                    _stale.Add(pair.Key);
            }
            foreach (PlayerCombat key in _stale)
                _robbedAt.Remove(key);
            _stale.Clear();
            foreach (KeyValuePair<PlayerCombat, float> pair in _nextRob)
            {
                if (pair.Key == null || Time.time > pair.Value)
                    _stale.Add(pair.Key);
            }
            foreach (PlayerCombat key in _stale)
                _nextRob.Remove(key);
        }
    }
}
