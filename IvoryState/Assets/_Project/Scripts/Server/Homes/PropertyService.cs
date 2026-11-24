using System;
using System.Collections.Generic;
using IvoryState.Core.Backend;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using IvoryState.Server.Backend;
using UnityEngine;

namespace IvoryState.Server.Homes
{
    /// <summary>
    /// Immobilier (serveur uniquement) : appartements à louer, maisons à acheter.
    /// <list type="bullet">
    /// <item>Registre en mémoire (autorité), chargé depuis Nakama au démarrage et réécrit à chaque
    /// changement. Un bien pris ne peut plus être pris par un autre ; dans un immeuble, chaque
    /// appartement a son propre locataire.</item>
    /// <item>Location : première semaine payée tout de suite, puis loyer chaque dimanche (service des
    /// loyers). Achat : prix payé en entier, puis impôt de 0,5 % du prix chaque dimanche.</item>
    /// <item>Un joueur a au plus 2 logements : son studio en colocation et un bien. Il peut quitter la
    /// colocation s'il a un bien. Quitter une maison achetée la revend à l'État (moitié du prix).</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class PropertyService : MonoBehaviour
    {
        private const string LogTag = "Immobilier";

        [Serializable] private class LoadRequest { public int count; }
        [Serializable] private class LoadedProperty { public int id; public string owner; public string owner_name; public string members; public string names; }
        [Serializable] private class LoadResponse { public LoadedProperty[] properties; }
        [Serializable] private class SaveRequest { public int id; public string owner; public string owner_name; public string members; public string names; }

        private sealed class Entry
        {
            public string Owner = string.Empty;
            public string OwnerName = string.Empty;
            public readonly List<string> Members = new List<string>();
            public readonly List<string> Names = new List<string>();
            public bool IsFree => string.IsNullOrEmpty(Owner);
        }

        private readonly Dictionary<int, Entry> _registry = new Dictionary<int, Entry>();
        private AccountService _accounts;
        private bool _loaded;

        public static PropertyService Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            for (int id = 1; id <= PropertyCatalog.Count; id++)
                _registry[id] = new Entry();
        }

        private void OnEnable()
        {
            PlayerHome.ServerPropertyHandler = HandleRequest;
            AccountService.PropertyLinked += OnLinked;
        }

        private void OnDisable()
        {
            PlayerHome.ServerPropertyHandler = null;
            AccountService.PropertyLinked -= OnLinked;
            if (Instance == this)
                Instance = null;
        }

        private async void Start()
        {
            _accounts = GetComponent<AccountService>();
            if (_accounts == null || !_accounts.IsEnabled)
            {
                _loaded = true; // hors-ligne : registre en mémoire seulement
                return;
            }

            try
            {
                var request = new LoadRequest { count = PropertyCatalog.Count };
                NakamaHttp.Result result = await NakamaHttp.Rpc(_accounts.BaseUrl, _accounts.HttpKey, "ivory_properties_load", JsonUtility.ToJson(request));
                if (!result.Ok)
                {
                    GameLog.Error(LogTag, "Registre des biens illisible (" + result.Status + ") : " + result.Error);
                    return; // pas de chargement : aucune vente possible (on ne revend pas un bien déjà pris)
                }
                // Une table Lua vide s'encode en objet : on la ramène à un tableau.
                string body = result.Body.Replace("\"properties\":{}", "\"properties\":[]");
                LoadResponse response = JsonUtility.FromJson<LoadResponse>(body);
                int taken = 0;
                if (response?.properties != null)
                {
                    foreach (LoadedProperty loaded in response.properties)
                    {
                        if (!PropertyCatalog.IsValid(loaded.id))
                            continue;
                        Entry entry = _registry[loaded.id];
                        entry.Owner = loaded.owner ?? string.Empty;
                        entry.OwnerName = loaded.owner_name ?? string.Empty;
                        Split(loaded.members, entry.Members);
                        Split(loaded.names, entry.Names);
                        if (!entry.IsFree)
                            taken++;
                    }
                }
                _loaded = true;
                GameLog.Info(LogTag, "Registre chargé : " + taken + " bien(s) occupé(s) sur " + PropertyCatalog.Count + ".");

                // Joueurs déjà connectés pendant le chargement.
                var homes = PlayerHome.ServerInstances;
                for (int i = 0; i < homes.Count; i++)
                {
                    if (homes[i] != null && homes[i].TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
                        OnLinked(account, account.ServerUserId);
                }
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Chargement du registre interrompu : " + exception);
            }
        }

        private static void Split(string text, List<string> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(text))
                return;
            foreach (string part in text.Split(','))
            {
                if (part.Length > 0)
                    into.Add(part);
            }
        }

        private void OnLinked(PlayerAccount account, string userId)
        {
            if (account == null || !account.TryGetComponent(out PlayerHome home))
                return;
            home.ServerClearProperties();
            foreach (KeyValuePair<int, Entry> pair in _registry)
            {
                if (pair.Value.Members.Contains(userId) && home.CanTakeProperty)
                    home.ServerAddProperty(pair.Key, pair.Value.Owner == userId);
            }
        }

        /// <summary>Habitants connectés d'un bien.</summary>
        public static void CollectResidents(int id, List<PlayerHome> into)
        {
            into.Clear();
            var homes = PlayerHome.ServerInstances;
            for (int i = 0; i < homes.Count; i++)
            {
                if (homes[i] != null && homes[i].LivesIn(id))
                    into.Add(homes[i]);
            }
        }

        #region Demandes des joueurs

        private void HandleRequest(PlayerHome home, PlayerHome.PropertyRequest request, int id)
        {
            if (home == null || !home.TryGetComponent(out PlayerEconomy economy))
                return;

            switch (request)
            {
                case PlayerHome.PropertyRequest.List:
                    SendList(home);
                    break;
                case PlayerHome.PropertyRequest.Take:
                    Take(home, economy, id);
                    break;
                case PlayerHome.PropertyRequest.Leave:
                    Leave(home, economy, id);
                    break;
                case PlayerHome.PropertyRequest.LeaveColocation:
                    LeaveColocation(home, economy);
                    break;
                case PlayerHome.PropertyRequest.Invite:
                    Invite(home, economy, id);
                    break;
                case PlayerHome.PropertyRequest.AcceptInvite:
                case PlayerHome.PropertyRequest.DeclineInvite:
                    AnswerInvite(home, economy, id, request == PlayerHome.PropertyRequest.AcceptInvite);
                    break;
            }
        }

        #region Emménager chez quelqu'un

        private const float InviteRange = 8f;
        private const float InviteSeconds = 120f;

        private struct Offer
        {
            public PlayerHome From;
            public int PropertyId;
            public float Expires;
        }

        private readonly Dictionary<PlayerHome, Offer> _offers = new Dictionary<PlayerHome, Offer>();

        private static PlayerHome FindByObjectId(int objectId)
        {
            var homes = PlayerHome.ServerInstances;
            for (int i = 0; i < homes.Count; i++)
            {
                if (homes[i] != null && homes[i].ObjectId == objectId)
                    return homes[i];
            }
            return null;
        }

        private void Invite(PlayerHome owner, PlayerEconomy economy, int targetObjectId)
        {
            // Le bien où le titulaire se trouve, sinon son premier bien en titre.
            int id = PropertyCatalog.PropertyAt(owner.transform.position);
            if (!owner.IsOwnerOf(id))
                id = owner.IsOwnerOf(owner.PropertyAt(0)) ? owner.PropertyAt(0) : owner.PropertyAt(1);
            if (!owner.IsOwnerOf(id))
            {
                economy.ServerNotify("Seul le titulaire d'un logement peut y faire emménager quelqu'un.");
                return;
            }
            PlayerHome target = FindByObjectId(targetObjectId);
            if (target == null || target == owner)
                return;
            if ((target.transform.position - owner.transform.position).sqrMagnitude > InviteRange * InviteRange)
            {
                economy.ServerNotify("Approche-toi : la personne doit être à moins de " + (int)InviteRange + " m.");
                return;
            }
            PropertyCatalog.Property property = PropertyCatalog.Get(id);
            if (_registry[id].Members.Count >= PropertyCatalog.Capacity(property.Kind))
            {
                economy.ServerNotify("Ton logement est complet (" + PropertyCatalog.Capacity(property.Kind) + " personnes au plus : 2 par chambre).");
                return;
            }
            if (!target.CanTakeProperty || target.LivesIn(id))
            {
                economy.ServerNotify("Cette personne a déjà 2 logements (ou habite déjà ici).");
                return;
            }

            _offers[target] = new Offer { From = owner, PropertyId = id, Expires = Time.unscaledTime + InviteSeconds };
            string ownerName = owner.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "Quelqu'un";
            target.ServerSendMoveInOffer(owner.ObjectId, ownerName, property.Label + " — " + PropertyCatalog.Entrances[property.EntranceIndex].Name);
            economy.ServerNotify("Proposition envoyée.");
        }

        private void AnswerInvite(PlayerHome target, PlayerEconomy economy, int fromObjectId, bool accept)
        {
            if (!_offers.TryGetValue(target, out Offer offer) || offer.From == null || offer.From.ObjectId != fromObjectId)
                return;
            _offers.Remove(target);
            PlayerEconomy ownerEconomy = offer.From.TryGetComponent(out PlayerEconomy e) ? e : null;
            string targetName = target.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "La personne";
            if (!accept)
            {
                ownerEconomy?.ServerNotify(targetName + " a refusé d'emménager chez toi.");
                return;
            }
            int id = offer.PropertyId;
            Entry entry = _registry[id];
            bool stillValid = Time.unscaledTime <= offer.Expires && offer.From.IsOwnerOf(id) &&
                              entry.Members.Count < PropertyCatalog.Capacity(PropertyCatalog.Get(id).Kind) && target.CanTakeProperty && !target.LivesIn(id) &&
                              target.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null;
            if (!stillValid)
            {
                economy.ServerNotify("Cette proposition n'est plus valable.");
                return;
            }

            target.TryGetComponent(out PlayerAccount targetAccount);
            entry.Members.Add(targetAccount.ServerUserId);
            entry.Names.Add(targetName);
            Save(id);
            target.ServerAddProperty(id, false);
            economy.ServerNotify("Bienvenue dans ton nouveau logement ! Le titulaire paie le loyer ; tu peux partir quand tu veux (Logement).");
            ownerEconomy?.ServerNotify(targetName + " a emménagé chez toi.");
            if (target.TryGetComponent(out PlayerServices services))
                services.ServerSuggestWaypoint(PropertyCatalog.EntrancePosition(id));
        }

        #endregion

        private void SendList(PlayerHome home)
        {
            var ids = new int[PropertyCatalog.Count];
            var occupants = new string[PropertyCatalog.Count];
            for (int id = 1; id <= PropertyCatalog.Count; id++)
            {
                ids[id - 1] = id;
                Entry entry = _registry[id];
                occupants[id - 1] = !_loaded ? "…" : entry.IsFree ? string.Empty : entry.OwnerName;
            }
            home.ServerSendPropertyList(ids, occupants);
        }

        private void Take(PlayerHome home, PlayerEconomy economy, int id)
        {
            if (!PropertyCatalog.IsValid(id))
                return;
            if (!_loaded)
            {
                economy.ServerNotify("L'agence immobilière est fermée pour l'instant. Réessaie dans un moment.");
                return;
            }
            if (!home.TryGetComponent(out PlayerAccount account) || account.ServerUserId == null)
            {
                economy.ServerNotify("Il faut un compte pour louer ou acheter un logement.");
                return;
            }
            if (!home.CanTakeProperty)
            {
                economy.ServerNotify("Tu as déjà 2 logements (le maximum). Revends ou quitte l'un d'eux d'abord.");
                return;
            }

            Entry entry = _registry[id];
            if (!entry.IsFree)
            {
                economy.ServerNotify("Déjà pris par " + entry.OwnerName + ".");
                return;
            }

            PropertyCatalog.Property property = PropertyCatalog.Get(id);
            bool buy = PropertyCatalog.IsForSale(property.Kind);
            string what = PropertyCatalog.KindName(property.Kind) + " — " + PropertyCatalog.Entrances[property.EntranceIndex].Name;
            if (!economy.ServerTrySpend(property.Price, buy ? "achat : " + what : "loyer (1re semaine) : " + what))
            {
                economy.ServerNotify("Pas assez d'argent : il faut " + Money.Format(property.Price) + ".");
                return;
            }

            string pseudo = home.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?";
            entry.Owner = account.ServerUserId;
            entry.OwnerName = pseudo;
            entry.Members.Clear();
            entry.Names.Clear();
            entry.Members.Add(account.ServerUserId);
            entry.Names.Add(pseudo);
            Save(id);

            home.ServerAddProperty(id, true);
            if (home.TryGetComponent(out PlayerNetworkIdentity propIdentity))
                propIdentity.ServerAddXp(buy ? 120 : 45, buy ? "achat immobilier" : "location");
            // Le loyer suivant tombe dimanche prochain : la semaine en cours est payée.
            if (home.ServerPaidUntil <= 0)
                home.ServerSetRent(RentRules.NextDue(RentRules.Now), home.RentDebt);
            economy.ServerNotify(buy
                ? "Félicitations, tu es propriétaire : " + what + " ! Impôt : " + Money.Format(PropertyCatalog.WeeklyCost(property)) + " par semaine."
                : "Bail signé : " + what + ". Loyer : " + Money.Format(property.Price) + " par semaine, prélevé le dimanche.");
            if (home.TryGetComponent(out PlayerServices services))
                services.ServerSuggestWaypoint(PropertyCatalog.EntrancePosition(id));
            GameLog.Info(LogTag, pseudo + (buy ? " achète " : " loue ") + "le bien " + id + ".");
        }

        private void Leave(PlayerHome home, PlayerEconomy economy, int id)
        {
            if (!home.LivesIn(id))
                id = home.Property; // ancienne demande sans numéro : le bien principal
            if (!PropertyCatalog.IsValid(id))
            {
                economy.ServerNotify("Tu n'as pas de logement à quitter (en dehors de ta colocation).");
                return;
            }
            if (!home.IsOwnerOf(id))
            {
                RemoveMember(id, home);
                economy.ServerNotify("Tu as quitté ce logement.");
                return;
            }

            PropertyCatalog.Property property = PropertyCatalog.Get(id);
            if (PropertyCatalog.IsForSale(property.Kind))
            {
                long refund = property.Price / 2;
                economy.ServerAdd(refund, "revente à l'État : " + PropertyCatalog.KindName(property.Kind));
                economy.ServerNotify("Maison revendue à l'État : +" + Money.Format(refund) + " (moitié du prix).");
            }
            else
            {
                economy.ServerNotify("Bail résilié. Tu n'as plus de loyer à payer pour ce logement.");
            }
            Release(id, "Le propriétaire a quitté le logement : tu n'y habites plus.");
        }

        private void LeaveColocation(PlayerHome home, PlayerEconomy economy)
        {
            if (!home.HasHome)
            {
                economy.ServerNotify("Tu n'es pas en colocation.");
                return;
            }
            if (!home.HasProperty)
            {
                economy.ServerNotify("Garde ta colocation tant que tu n'as pas d'autre logement : sinon tu serais à la rue.");
                return;
            }
            if (home.TryGetComponent(out PlayerAccount account) && _accounts != null)
                _accounts.ServerReleaseHome(account);
            home.ServerSetHome(0, string.Empty);
            home.ServerSetRent(home.ServerPaidUntil, home.RentDebt); // sauvegarde
            economy.ServerNotify("Tu as quitté ta colocation : plus de loyer de studio à payer.");
        }

        #endregion

        /// <summary>
        /// Service des loyers : bail résilié pour impayé (location seulement ; une maison achetée n'est
        /// pas saisie, sa dette d'impôt reste due).
        /// </summary>
        public bool ServerEvictRented(PlayerHome home)
        {
            bool evicted = false;
            for (int slot = PlayerHome.MaxProperties - 1; slot >= 0; slot--)
            {
                int id = home.PropertyAt(slot);
                if (!PropertyCatalog.IsValid(id) || !home.OwnsPropertyAt(slot) || PropertyCatalog.IsForSale(PropertyCatalog.Get(id).Kind))
                    continue;
                Release(id, "Loyer impayé : le bail est résilié, tu n'habites plus ce logement.");
                evicted = true;
            }
            return evicted;
        }

        public bool IsLoaded => _loaded;

        public bool IsOwnedBy(int id, string userId) =>
            PropertyCatalog.IsValid(id) && !string.IsNullOrEmpty(userId) && _registry[id].Owner == userId;

        /// <summary>
        /// Djassa : la maison change de propriétaire. Les anciens habitants connectés la quittent ; les
        /// absents la perdent à leur prochaine connexion (le registre ne les contient plus).
        /// </summary>
        public void ServerTransfer(int id, PlayerHome buyer, string buyerUserId, string buyerName)
        {
            if (!PropertyCatalog.IsValid(id))
                return;
            CollectResidents(id, _residents);
            foreach (PlayerHome resident in _residents)
            {
                ExpelIfInside(resident, id);
                resident.ServerRemoveProperty(id);
                if (resident.TryGetComponent(out PlayerEconomy economy))
                    economy.ServerNotify("Ta maison a été vendue : tu n'y habites plus.");
            }
            Entry entry = _registry[id];
            entry.Owner = buyerUserId;
            entry.OwnerName = buyerName;
            entry.Members.Clear();
            entry.Names.Clear();
            entry.Members.Add(buyerUserId);
            entry.Names.Add(buyerName);
            Save(id);
            buyer.ServerAddProperty(id, true);
        }

        private readonly List<PlayerHome> _residents = new List<PlayerHome>();

        private void Release(int id, string message)
        {
            Entry entry = _registry[id];
            entry.Owner = string.Empty;
            entry.OwnerName = string.Empty;
            entry.Members.Clear();
            entry.Names.Clear();
            Save(id);

            CollectResidents(id, _residents);
            foreach (PlayerHome resident in _residents)
            {
                ExpelIfInside(resident, id);
                resident.ServerRemoveProperty(id);
                if (resident.TryGetComponent(out PlayerEconomy economy))
                    economy.ServerNotify(message);
            }
        }

        private void RemoveMember(int id, PlayerHome home)
        {
            Entry entry = _registry[id];
            if (home.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null)
            {
                int index = entry.Members.IndexOf(account.ServerUserId);
                if (index >= 0)
                {
                    entry.Members.RemoveAt(index);
                    if (index < entry.Names.Count)
                        entry.Names.RemoveAt(index);
                    Save(id);
                }
            }
            ExpelIfInside(home, id);
            home.ServerRemoveProperty(id);
        }

        private static void ExpelIfInside(PlayerHome home, int id)
        {
            if (PropertyCatalog.PropertyAt(home.transform.position) == id && home.TryGetComponent(out PlayerInteractor interactor))
                interactor.ServerTravel(PropertyCatalog.EntranceExit(id), PropertyCatalog.Entrances[PropertyCatalog.Get(id).EntranceIndex].Name);
        }

        private const float RetryInterval = 10f;
        private readonly HashSet<int> _inFlightSaves = new HashSet<int>();
        private readonly HashSet<int> _dirtySaves = new HashSet<int>();
        private readonly HashSet<int> _pendingSaves = new HashSet<int>();
        private readonly List<int> _retryBuffer = new List<int>();
        private float _nextRetry;

        private void Update()
        {
            if (_pendingSaves.Count == 0 || Time.unscaledTime < _nextRetry)
                return;
            _nextRetry = Time.unscaledTime + RetryInterval;
            _retryBuffer.Clear();
            _retryBuffer.AddRange(_pendingSaves);
            _pendingSaves.Clear();
            for (int i = 0; i < _retryBuffer.Count; i++)
                Save(_retryBuffer[i]);
        }

        private void Save(int id)
        {
            if (_accounts == null || !_accounts.IsEnabled)
                return;
            if (_inFlightSaves.Contains(id))
            {
                _dirtySaves.Add(id);
                return;
            }
            _pendingSaves.Remove(id);
            SendSave(id);
        }

        private async void SendSave(int id)
        {
            _inFlightSaves.Add(id);
            Entry entry = _registry[id];
            var request = new SaveRequest
            {
                id = id, owner = entry.Owner, owner_name = entry.OwnerName,
                members = string.Join(",", entry.Members), names = string.Join(",", entry.Names),
            };
            bool ok = false;
            try
            {
                NakamaHttp.Result result = await NakamaHttp.Rpc(_accounts.BaseUrl, _accounts.HttpKey, "ivory_property_save", JsonUtility.ToJson(request));
                ok = result.Ok;
                if (!ok)
                    GameLog.Warn(LogTag, "Bien " + id + " non sauvegardé (" + result.Status + ") : " + result.Error + " — nouvel essai dans " + RetryInterval + " s.");
            }
            catch (Exception exception)
            {
                GameLog.Error(LogTag, "Sauvegarde du bien " + id + " interrompue : " + exception);
            }
            finally
            {
                _inFlightSaves.Remove(id);
            }

            if (_dirtySaves.Remove(id))
            {
                SendSave(id);
                return;
            }
            if (!ok)
                _pendingSaves.Add(id);
        }
    }
}
