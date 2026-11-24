using System;
using System.Collections.Generic;
using System.Text;
using FishNet.Object;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Combat;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Server.Social
{
    /// <summary>
    /// Standard téléphonique du serveur : toutes les règles d'appel sont ici (jamais côté client).
    /// <list type="bullet">
    /// <item>Annuaire : joueurs connectés (bots exclus), triés par pseudo.</item>
    /// <item>Un seul appel à la fois par joueur : un correspondant déjà en ligne est « occupé ».</item>
    /// <item>Sonnerie limitée à <see cref="RingTimeout"/> s, puis « pas de réponse ».</item>
    /// <item>Voix relayée au seul correspondant, avec un débit plafonné par joueur (anti-abus).</item>
    /// <item>Déconnexion : l'appel en cours se termine proprement chez l'autre.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class PhoneService : MonoBehaviour, IPhoneSwitchboard
    {
        private const string LogTag = "Phone";
        private const float RingTimeout = 25f;
        // 16 kHz ADPCM : ~8 Ko/s par sens. Marge pour l'en-tête et les variations : 16 Ko/s maximum.
        private const int MaxVoiceBytesPerSecond = 16 * 1024;

        private sealed class ActiveCall
        {
            public PlayerPhone Caller, Callee;
            public float StartedAt;
            public bool Connected;
        }

        private readonly Dictionary<PlayerPhone, ActiveCall> _calls = new Dictionary<PlayerPhone, ActiveCall>();
        private readonly Dictionary<PlayerPhone, int> _voiceBudget = new Dictionary<PlayerPhone, int>();
        private readonly Dictionary<long, float> _profileViewCooldowns = new Dictionary<long, float>();
        private readonly List<ActiveCall> _expired = new List<ActiveCall>();
        private float _budgetResetAt;
        private const float ProfileViewCooldownSeconds = 10f;

        private void OnEnable()
        {
            PlayerPhone.Switchboard = this;
        }

        private void OnDisable()
        {
            if (ReferenceEquals(PlayerPhone.Switchboard, this))
                PlayerPhone.Switchboard = null;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _budgetResetAt)
            {
                _voiceBudget.Clear();
                _budgetResetAt = now + 1f;
            }

            // Appels qui sonnent trop longtemps.
            _expired.Clear();
            foreach (ActiveCall call in _calls.Values)
            {
                if (!call.Connected && now - call.StartedAt > RingTimeout && !_expired.Contains(call))
                    _expired.Add(call);
            }
            foreach (ActiveCall call in _expired)
                End(call, "Pas de réponse.", "Appel manqué.");
        }

        public void Directory(PlayerPhone phone)
        {
            var entries = new List<(int id, string name)>();
            foreach (PlayerNetworkIdentity identity in PlayerNetworkIdentity.ServerInstances)
            {
                if (identity == null || !identity.Owner.IsValid || identity.gameObject == phone.gameObject)
                    continue;
                entries.Add((identity.ObjectId, identity.DisplayName));
            }
            entries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            var ids = new int[entries.Count];
            var names = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                ids[i] = entries[i].id;
                names[i] = entries[i].name;
            }
            phone.ServerSendDirectory(ids, names);
        }

        public void ProfileDirectory(PlayerPhone phone)
        {
            if (phone == null)
                return;

            var entries = new List<(int id, string name, int level, bool homeless)>();
            foreach (PlayerNetworkIdentity identity in PlayerNetworkIdentity.ServerInstances)
            {
                if (identity == null || !identity.Owner.IsValid || identity.gameObject == phone.gameObject)
                    continue;
                bool isHomeless = identity.TryGetComponent(out PlayerHome home) && home.IsHomeless;
                entries.Add((identity.ObjectId, identity.DisplayName, identity.Level, isHomeless));
            }
            entries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            var ids = new int[entries.Count];
            var names = new string[entries.Count];
            var levels = new int[entries.Count];
            var homeless = new bool[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                ids[i] = entries[i].id;
                names[i] = entries[i].name;
                levels[i] = entries[i].level;
                homeless[i] = entries[i].homeless;
            }
            phone.ServerSendProfileDirectory(ids, names, levels, homeless);
        }

        public void Profile(PlayerPhone requester, int targetObjectId)
        {
            if (requester == null)
                return;

            PlayerNetworkIdentity target = null;
            if (targetObjectId <= 0)
            {
                requester.TryGetComponent(out target);
            }
            else
            {
                foreach (PlayerNetworkIdentity candidate in PlayerNetworkIdentity.ServerInstances)
                {
                    if (candidate != null && candidate.ObjectId == targetObjectId)
                    {
                        target = candidate;
                        break;
                    }
                }
            }

            if (target == null || !target.IsSpawned)
            {
                requester.ServerSendProfile(false, targetObjectId, string.Empty, 0, false, false,
                    0, 1, false, string.Empty, 0, false,
                    0, string.Empty, 0, false, 0, false,
                    0, -1, -1, 0, string.Empty, 0, 0, 0, 0,
                    0, 0, 0, string.Empty, string.Empty, 0);
                return;
            }

            bool isSelf = target.gameObject == requester.gameObject;
            if (!isSelf)
            {
                long pairKey = ((long)requester.ObjectId << 32) | (uint)target.ObjectId;
                float now = Time.unscaledTime;
                if (!_profileViewCooldowns.TryGetValue(pairKey, out float nextAllowed) || now >= nextAllowed)
                {
                    _profileViewCooldowns[pairKey] = now + ProfileViewCooldownSeconds;
                    target.ServerIncrementProfileViews();
                }
            }

            int privacyMask = target.ProfilePrivacy;
            bool hideResidence = !isSelf && (privacyMask & (int)ProfilePrivacyFlags.Residence) != 0;
            bool hideProperties = !isSelf && (privacyMask & (int)ProfilePrivacyFlags.Properties) != 0;
            bool hideVehicles = !isSelf && (privacyMask & (int)ProfilePrivacyFlags.Vehicles) != 0;
            bool hideCommerces = !isSelf && (privacyMask & (int)ProfilePrivacyFlags.Commerces) != 0;

            long balance = -1;
            long cash = -1;
            long netWorth = 0;
            string commercesSummary = string.Empty;
            int commercesCount = 0;

            if (target.TryGetComponent(out PlayerEconomy economy))
            {
                netWorth += Math.Max(0L, economy.Balance) + Math.Max(0L, economy.Cash);
                if (isSelf)
                {
                    balance = economy.Balance;
                    cash = economy.Cash;
                }

                List<int> commerceIds = CommerceCatalog.ParseIds(economy.OwnedCommercesCsv);
                commercesCount = hideCommerces ? 0 : commerceIds.Count;
                for (int i = 0; i < commerceIds.Count; i++)
                {
                    if (!CommerceCatalog.TryGet(commerceIds[i], out CommerceDefinition comm))
                        continue;
                    netWorth += comm.PurchasePrice;
                    if (!hideCommerces)
                    {
                        if (commercesSummary.Length > 0)
                            commercesSummary += "|";
                        string linkedProp = CommerceCatalog.DescribeLinkedProperty(comm.LinkedPropertyId);
                        string openLabel = Economy.CommerceService.Instance == null || Economy.CommerceService.Instance.IsCommerceOpen(comm.Id)
                            ? "Ouvert" : "Fermé";
                        commercesSummary += comm.Name + " (" + CommerceCatalog.KindLabel(comm.Kind) + " · " + comm.District + " · " + openLabel +
                            (string.IsNullOrEmpty(linkedProp) ? string.Empty : " · Lié : " + linkedProp) + ")";
                    }
                }
            }

            bool hasLicense = false;
            string carsCsv = string.Empty;
            byte rentalPaint = 0;
            bool hasRental = false;
            if (target.TryGetComponent(out PlayerGarage garage))
            {
                IReadOnlyList<byte> cars = garage.Cars;
                for (int i = 0; i < cars.Count; i++)
                {
                    int paint = cars[i];
                    if (paint >= 0 && paint < PlayerGarage.Rules.CarPrices.Length)
                        netWorth += PlayerGarage.Rules.CarPrices[paint];
                }
                if (!hideVehicles)
                {
                    hasLicense = garage.HasLicense;
                    carsCsv = garage.CarsText ?? string.Empty;
                    long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    hasRental = garage.RentalUntil > nowUnix && garage.RentalPaint > 0;
                    rentalPaint = garage.RentalPaint;
                }
            }

            bool isHomeless = false;
            int studioId = 0;
            string roommate = string.Empty;
            string residenceDistrict = string.Empty;
            int prop1Id = 0;
            bool prop1Owner = false;
            int prop2Id = 0;
            bool prop2Owner = false;
            if (target.TryGetComponent(out PlayerHome home))
            {
                for (int slot = 0; slot < PlayerHome.MaxProperties; slot++)
                {
                    int propId = home.PropertyAt(slot);
                    if (PropertyCatalog.IsValid(propId) && home.OwnsPropertyAt(slot))
                    {
                        PropertyCatalog.Property prop = PropertyCatalog.Get(propId);
                        if (PropertyCatalog.IsForSale(prop.Kind))
                            netWorth += prop.Price;
                    }
                }

                if (!hideResidence)
                {
                    isHomeless = home.IsHomeless;
                    studioId = home.Studio;
                    roommate = home.Roommate ?? string.Empty;
                    residenceDistrict = ResolveResidenceDistrict(home);
                }
                if (!hideProperties)
                {
                    prop1Id = home.PropertyAt(0);
                    prop1Owner = home.OwnsPropertyAt(0);
                    prop2Id = home.PropertyAt(1);
                    prop2Owner = home.OwnsPropertyAt(1);
                }
            }

            int bagCount = 0;
            string bagSummary = string.Empty;
            if (target.TryGetComponent(out PlayerInventory bag))
            {
                var sb = isSelf ? new StringBuilder() : null;
                for (int i = 0; i < bag.Count; i++)
                {
                    InventoryItem item = bag[i];
                    bagCount += item.Count;
                    if (sb != null && item.Count > 0)
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(item.Name).Append(" ×").Append(item.Count);
                    }
                }
                if (sb != null)
                    bagSummary = sb.ToString();
            }

            int crimes = target.TryGetComponent(out PlayerCombat combat) ? combat.Crimes : 0;
            byte health = 0, satiety = 0, hydration = 0;
            if (isSelf && target.TryGetComponent(out PlayerNeeds needs))
            {
                health = needs.Health;
                satiety = needs.Satiety;
                hydration = needs.Hydration;
            }

            requester.ServerSendProfile(
                true, target.ObjectId, target.DisplayName, target.Xp, isSelf, isHomeless,
                target.Outfit, target.ServerOwnedOutfits, hasLicense, carsCsv, rentalPaint, hasRental,
                studioId, roommate, prop1Id, prop1Owner, prop2Id, prop2Owner,
                netWorth, balance, cash, bagCount, bagSummary,
                crimes, health, satiety, hydration,
                (byte)target.JobStatus, target.ProfileViews, privacyMask, residenceDistrict, commercesSummary, commercesCount);
        }

        private static string ResolveResidenceDistrict(PlayerHome home)
        {
            if (home == null || home.IsHomeless)
                return string.Empty;

            if (home.PrimaryKey >= PlayerHome.PropertyKeyBase)
            {
                int primaryPropId = home.PrimaryKey - PlayerHome.PropertyKeyBase;
                if (PropertyCatalog.IsValid(primaryPropId))
                {
                    PropertyCatalog.Property p = PropertyCatalog.Get(primaryPropId);
                    return PropertyCatalog.Entrances[p.EntranceIndex].Name;
                }
            }

            for (int slot = 0; slot < PlayerHome.MaxProperties; slot++)
            {
                int propId = home.PropertyAt(slot);
                if (PropertyCatalog.IsValid(propId))
                {
                    PropertyCatalog.Property p = PropertyCatalog.Get(propId);
                    return PropertyCatalog.Entrances[p.EntranceIndex].Name;
                }
            }

            return StudioLayout.IsValid(home.Studio) ? "Treichville — Résidence Les Flamboyants" : string.Empty;
        }

        public void Call(PlayerPhone caller, int targetObjectId)
        {
            if (_calls.ContainsKey(caller))
                return; // déjà en ligne : il faut raccrocher d'abord

            PlayerPhone callee = FindPhone(targetObjectId);
            string callerName = NameOf(caller);
            if (callee == null || callee == caller || !callee.Owner.IsValid)
            {
                caller.ServerSetState(CallState.Idle, -1, string.Empty, "Ce numéro n'est pas joignable.");
                return;
            }

            // On n'appelle que ses amis (ajoutés à proximité, voir PlayerFriends).
            if (caller.TryGetComponent(out PlayerFriends callerFriends) && callee.TryGetComponent(out PlayerFriends calleeFriends) &&
                !callerFriends.ServerIsFriend(calleeFriends))
            {
                caller.ServerSetState(CallState.Idle, -1, string.Empty, "Tu ne peux appeler que tes amis.");
                return;
            }

            if (_calls.ContainsKey(callee))
            {
                caller.ServerSetState(CallState.Idle, -1, string.Empty, NameOf(callee) + " est déjà en ligne.");
                return;
            }

            var call = new ActiveCall { Caller = caller, Callee = callee, StartedAt = Time.unscaledTime };
            _calls[caller] = call;
            _calls[callee] = call;
            caller.ServerSetState(CallState.Calling, callee.ObjectId, NameOf(callee), string.Empty);
            callee.ServerSetState(CallState.Ringing, caller.ObjectId, callerName, string.Empty);
            GameLog.Info(LogTag, callerName + " appelle " + NameOf(callee) + ".");
        }

        public void Answer(PlayerPhone phone, bool accept)
        {
            if (!_calls.TryGetValue(phone, out ActiveCall call) || call.Callee != phone || call.Connected)
                return;

            if (!accept)
            {
                End(call, NameOf(phone) + " a refusé l'appel.", string.Empty);
                return;
            }

            call.Connected = true;
            call.StartedAt = Time.unscaledTime;
            call.Caller.ServerSetState(CallState.Connected, call.Callee.ObjectId, NameOf(call.Callee), string.Empty);
            call.Callee.ServerSetState(CallState.Connected, call.Caller.ObjectId, NameOf(call.Caller), string.Empty);
            GameLog.Info(LogTag, NameOf(call.Caller) + " et " + NameOf(call.Callee) + " sont en ligne.");
        }

        public void HangUp(PlayerPhone phone)
        {
            if (!_calls.TryGetValue(phone, out ActiveCall call))
                return;

            string name = NameOf(phone);
            if (call.Connected)
                End(call, "Appel terminé.", "Appel terminé.");
            else if (call.Caller == phone)
                End(call, string.Empty, "Appel manqué de " + name + ".");
            else
                End(call, name + " a refusé l'appel.", string.Empty);
        }

        public void Voice(PlayerPhone phone, ArraySegment<byte> frame)
        {
            if (!_calls.TryGetValue(phone, out ActiveCall call) || !call.Connected)
                return;

            _voiceBudget.TryGetValue(phone, out int used);
            used += frame.Count;
            _voiceBudget[phone] = used;
            if (used > MaxVoiceBytesPerSecond)
                return;

            PlayerPhone peer = call.Caller == phone ? call.Callee : call.Caller;
            peer.ServerDeliverVoice(frame);
        }

        public void Disconnected(PlayerPhone phone)
        {
            if (_calls.TryGetValue(phone, out ActiveCall call))
                End(call, "Communication coupée.", "Communication coupée.", phone);
        }

        /// <summary>Termine un appel : chacun repasse au repos, avec un message (vide = aucun).</summary>
        private void End(ActiveCall call, string callerReason, string calleeReason, PlayerPhone gone = null)
        {
            _calls.Remove(call.Caller);
            _calls.Remove(call.Callee);
            if (call.Caller != gone && call.Caller != null && call.Caller.IsSpawned)
                call.Caller.ServerSetState(CallState.Idle, -1, string.Empty, callerReason);
            if (call.Callee != gone && call.Callee != null && call.Callee.IsSpawned)
                call.Callee.ServerSetState(CallState.Idle, -1, string.Empty, calleeReason);
        }

        private static PlayerPhone FindPhone(int objectId)
        {
            foreach (PlayerNetworkIdentity identity in PlayerNetworkIdentity.ServerInstances)
            {
                if (identity != null && identity.ObjectId == objectId && identity.TryGetComponent(out PlayerPhone phone))
                    return phone;
            }
            return null;
        }

        private static string NameOf(PlayerPhone phone)
        {
            return phone != null && phone.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?";
        }
    }
}
