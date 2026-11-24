using System.Collections.Generic;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Server.Homes
{
    /// <summary>
    /// Entrées, sorties et visites des logements (serveur uniquement) : studios en colocation de la
    /// Résidence et biens immobiliers (appartements, maisons).
    /// <list type="bullet">
    /// <item>Cercle devant une entrée : si le joueur n'a rien d'autre à choisir, il entre directement
    /// chez lui ; sinon un menu lui propose son logement et les logements ouverts de ce bâtiment.</item>
    /// <item>Cercle au pied de la porte d'un intérieur : retour dans la rue.</item>
    /// <item>Porte : fermée par défaut (seuls les habitants entrent). Un habitant peut l'ouvrir : tout le
    /// monde peut alors entrer, tant qu'un habitant est connecté. Quand plus aucun ne l'est, la porte se
    /// referme et les visiteurs sont raccompagnés dehors.</item>
    /// </list>
    /// Clé d'un logement : n° de studio (1…96), ou 1000 + n° du bien immobilier.
    /// Un joueur doit être sorti d'un cercle avant de pouvoir le déclencher à nouveau.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class HomeService : MonoBehaviour
    {
        public const int PropertyKeyBase = 1000;

        private const float CheckInterval = 0.2f;
        private const float DoorCheckInterval = 2f;
        private const float MenuValidRange = 4f;
        private const int MaxMenuEntries = 8;

        private readonly Dictionary<PlayerDriving, bool> _armed = new Dictionary<PlayerDriving, bool>();
        private readonly Dictionary<PlayerDriving, HomeEntrance> _cameFrom = new Dictionary<PlayerDriving, HomeEntrance>();
        private readonly HashSet<int> _openDoors = new HashSet<int>();
        private readonly List<PlayerDriving> _stale = new List<PlayerDriving>();
        private readonly List<int> _closing = new List<int>();
        private readonly List<int> _menuKeys = new List<int>();
        private readonly List<string> _menuNames = new List<string>();
        private float _nextCheck, _nextDoorCheck;

        private void OnEnable()
        {
            PlayerHome.ServerEnterHandler = OnEnterRequest;
            PlayerHome.ServerDoorHandler = OnToggleDoor;
        }

        private void OnDisable()
        {
            PlayerHome.ServerEnterHandler = null;
            PlayerHome.ServerDoorHandler = null;
        }

        private void Update()
        {
            if (Time.time >= _nextDoorCheck)
            {
                _nextDoorCheck = Time.time + DoorCheckInterval;
                CloseAbandonedDoors();
            }

            if (Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + CheckInterval;

            var players = PlayerDriving.ServerInstances;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerDriving player = players[i];
                if (player == null || player.IsInVehicle)
                    continue;
                Check(player);
            }

            // Joueurs déconnectés : on oublie leur état.
            _stale.Clear();
            foreach (PlayerDriving key in _armed.Keys)
            {
                if (key == null)
                    _stale.Add(key);
            }
            for (int i = 0; i < _stale.Count; i++)
            {
                _armed.Remove(_stale[i]);
                _cameFrom.Remove(_stale[i]);
            }
        }

        #region Clés

        private static bool IsProperty(int key) => key >= PropertyKeyBase;

        private static int KeyOf(PlayerHome home, bool property) =>
            property ? (home.HasProperty ? PropertyKeyBase + home.Property : 0) : (home.HasHome ? home.Studio : 0);

        /// <summary>Logement principal : le bien s'il y en a un, sinon le studio.</summary>
        private static int MainKey(PlayerHome home) => home.PrimaryKey;

        /// <summary>
        /// Où remettre un joueur qui se reconnecte (null : point d'apparition habituel).
        /// <list type="bullet">
        /// <item>Absent au moins un jour (et pas SDF) : dans son domicile principal.</item>
        /// <item>Sinon : là où il s'est déconnecté ; s'il était dans un intérieur qui n'est plus le sien
        /// (visite, hôpital), devant l'entrée de ce lieu.</item>
        /// </list>
        /// </summary>
        public static Vector3? ResolveReconnect(PlayerHome home, Vector3 last, bool longAbsence)
        {
            if (longAbsence && !home.IsHomeless)
            {
                int key = home.PrimaryKey;
                if (key != 0)
                    return SpawnPoint(key);
            }
            if (last == Vector3.zero)
                return null;
            if (!StudioLayout.IsIndoors(last))
            {
                // Position dehors encore valable (la carte a pu changer depuis) : dans la ville, pas sur
                // l'eau, près du sol praticable. Sinon : chez soi s'il a un logement, ou point d'apparition.
                bool valid = Gameplay.World.CityLayout.Bounds.Contains(new Vector2(last.x, last.z))
                             && !Gameplay.World.CityLayout.IsWater(last.x, last.z)
                             && UnityEngine.AI.NavMesh.SamplePosition(last, out _, 3f, UnityEngine.AI.NavMesh.AllAreas);
                if (valid)
                    return last + Vector3.up * 0.2f;
                int fallback = home.IsHomeless ? 0 : home.PrimaryKey;
                return fallback != 0 ? SpawnPoint(fallback) : (Vector3?)null;
            }
            int at = KeyAt(last);
            if (at != 0)
            {
                if (LivesIn(home, at))
                    return last;
                if (IsProperty(at))
                    return PropertyCatalog.EntranceExit(at - PropertyKeyBase);
                return HomeEntrance.Residence != null ? HomeEntrance.Residence.ExitPosition : (Vector3?)null;
            }
            int hospital = Gameplay.Combat.HospitalCatalog.HospitalAt(last);
            if (hospital >= 0)
                return Gameplay.Combat.HospitalCatalog.EntranceExit(hospital);
            return null;
        }

        private static bool LivesIn(PlayerHome home, int key) =>
            IsProperty(key) ? home.LivesIn(key - PropertyKeyBase) : home.Studio == key;

        private static int KeyAt(Vector3 position)
        {
            int studio = StudioLayout.StudioAt(position);
            if (studio != 0)
                return studio;
            int property = PropertyCatalog.PropertyAt(position);
            return property != 0 ? PropertyKeyBase + property : 0;
        }

        private static Vector3 SpawnPoint(int key) =>
            IsProperty(key) ? PropertyCatalog.SpawnPoint(key - PropertyKeyBase) : StudioLayout.SpawnPoint(key);

        private static Vector3 ExitCircle(int key) =>
            IsProperty(key) ? PropertyCatalog.ExitCircle(key - PropertyKeyBase) : StudioLayout.ExitCircle(key);

        private static string Describe(int key)
        {
            if (!IsProperty(key))
                return "Studio n° " + key;
            PropertyCatalog.Property property = PropertyCatalog.Get(key - PropertyKeyBase);
            return property.Label + " (" + PropertyCatalog.KindName(property.Kind) + ")";
        }

        #endregion

        private void Check(PlayerDriving player)
        {
            Vector3 position = player.transform.position;
            bool armed = _armed.TryGetValue(player, out bool value) && value;
            bool inCircle = false;

            if (StudioLayout.IsIndoors(position))
            {
                int key = KeyAt(position);
                if (key != 0)
                {
                    Vector3 offset = position - ExitCircle(key);
                    offset.y = 0f;
                    inCircle = offset.sqrMagnitude <= StudioLayout.ExitRadius * StudioLayout.ExitRadius;
                    if (inCircle && armed)
                        Leave(player, key);
                }
            }
            else
            {
                var entrances = HomeEntrance.All;
                for (int i = 0; i < entrances.Count; i++)
                {
                    if (!entrances[i].Contains(position))
                        continue;
                    inCircle = true;
                    if (armed)
                        Arrive(player, entrances[i]);
                    break;
                }
            }

            _armed[player] = !inCircle;
        }

        #region Entrée

        /// <summary>Joueur dans le cercle : chez lui directement, ou menu s'il y a d'autres choix.</summary>
        private void Arrive(PlayerDriving player, HomeEntrance entrance)
        {
            if (!player.TryGetComponent(out PlayerHome home))
                return;
            _cameFrom[player] = entrance;

            _menuKeys.Clear();
            _menuNames.Clear();
            int own = 0;
            if (entrance.IsResidence)
            {
                own = KeyOf(home, false);
                foreach (int key in _openDoors)
                    AddOpen(key, own, !IsProperty(key));
            }
            else
            {
                // Mes biens dans ce bâtiment (jusqu'à 2 appartements dans le même immeuble).
                int second = 0;
                for (int slot = 0; slot < PlayerHome.MaxProperties; slot++)
                {
                    int id = home.PropertyAt(slot);
                    if (!PropertyCatalog.IsValid(id) || PropertyCatalog.Get(id).EntranceIndex != entrance.EntranceIndex)
                        continue;
                    if (own == 0)
                        own = PropertyKeyBase + id;
                    else
                        second = PropertyKeyBase + id;
                }
                if (second != 0)
                {
                    _menuKeys.Add(second);
                    _menuNames.Add("Chez moi");
                }
                foreach (int key in _openDoors)
                    AddOpen(key, own, IsProperty(key) && !LivesIn(home, key) && PropertyCatalog.Get(key - PropertyKeyBase).EntranceIndex == entrance.EntranceIndex);
            }

            if (_menuKeys.Count == 0)
            {
                if (own != 0)
                    EnterOwn(player, home, own, entrance);
                else
                    ExplainClosed(player, home, entrance);
                return;
            }

            // Mon logement en premier (0 : pas de logement ici).
            _menuKeys.Insert(0, own);
            _menuNames.Insert(0, own != 0 ? "Chez moi" : string.Empty);
            home.ServerShowResidenceMenu(_menuKeys.ToArray(), _menuNames.ToArray());
        }

        private void AddOpen(int key, int own, bool sameBuilding)
        {
            if (!sameBuilding || key == own || _menuKeys.Count >= MaxMenuEntries)
                return;
            string residents = ResidentNames(key);
            if (residents.Length == 0)
                return;
            _menuKeys.Add(key);
            _menuNames.Add(residents);
        }

        private static void ExplainClosed(PlayerDriving player, PlayerHome home, HomeEntrance entrance)
        {
            PlayerEconomy economy = player.Economy;
            if (entrance.IsResidence)
            {
                economy?.ServerNotify(home.IsHomeless
                    ? "Tu es SDF : paie ta dette de loyer (téléphone → Logement) pour retrouver un studio."
                    : "Tu n'as pas de studio ici. Il se libère bientôt une place, reviens plus tard.");
                return;
            }
            economy?.ServerNotify(entrance.DisplayName + " : porte fermée. Pour louer ou acheter, ouvre l'application Immobilier du téléphone.");
        }

        private void OnEnterRequest(PlayerHome home, int key)
        {
            if (home == null || !home.TryGetComponent(out PlayerDriving player) || player.IsInVehicle)
                return;
            HomeEntrance entrance = NearestEntrance(home.transform.position);
            if (entrance == null)
                return; // trop loin de l'entrée : demande ignorée
            _cameFrom[player] = entrance;

            if (key != 0 && LivesIn(home, key))
            {
                EnterOwn(player, home, key, entrance);
                return;
            }

            PlayerEconomy economy = player.Economy;
            if (!_openDoors.Contains(key) || ResidentNames(key).Length == 0)
            {
                economy?.ServerNotify("Ce logement est fermé.");
                return;
            }
            if (!player.TryGetComponent(out PlayerInteractor interactor))
                return;

            interactor.ServerTravel(SpawnPoint(key), "En visite : " + Describe(key));
            string visitor = player.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "Quelqu'un";
            economy?.ServerNotify("Tu rends visite à " + ResidentNames(key) + ".");
            NotifyResidents(key, visitor + " est entré chez toi.");
        }

        private static void EnterOwn(PlayerDriving player, PlayerHome home, int key, HomeEntrance entrance)
        {
            if (!player.TryGetComponent(out PlayerInteractor interactor))
                return;
            interactor.ServerTravel(SpawnPoint(key), Describe(key) + " — " + entrance.DisplayName);
            PlayerEconomy economy = player.Economy;
            if (IsProperty(key))
            {
                economy?.ServerNotify("Bienvenue chez toi !");
                return;
            }
            economy?.ServerNotify(string.IsNullOrEmpty(home.Roommate)
                ? "Bienvenue chez toi ! Tu es seul pour l'instant : un colocataire arrivera bientôt."
                : "Bienvenue chez toi ! Ton colocataire : " + home.Roommate + ".");
        }

        private void Leave(PlayerDriving player, int key)
        {
            if (!player.TryGetComponent(out PlayerInteractor interactor))
                return;
            if (IsProperty(key))
            {
                int id = key - PropertyKeyBase;
                interactor.ServerTravel(PropertyCatalog.EntranceExit(id), PropertyCatalog.Entrances[PropertyCatalog.Get(id).EntranceIndex].Name);
                return;
            }
            HomeEntrance entrance = _cameFrom.TryGetValue(player, out HomeEntrance from) && from != null && from.IsResidence
                ? from
                : HomeEntrance.Residence;
            if (entrance != null)
                interactor.ServerTravel(entrance.ExitPosition, entrance.DisplayName);
        }

        private static HomeEntrance NearestEntrance(Vector3 position)
        {
            var entrances = HomeEntrance.All;
            for (int i = 0; i < entrances.Count; i++)
            {
                Vector3 offset = position - entrances[i].transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= MenuValidRange * MenuValidRange)
                    return entrances[i];
            }
            return null;
        }

        #endregion

        #region Porte

        /// <summary>Ouvre ou ferme la porte du logement principal (le bien s'il y en a un, sinon le studio).</summary>
        private void OnToggleDoor(PlayerHome home)
        {
            if (home == null || !home.TryGetComponent(out PlayerEconomy economy))
                return;
            // La porte du logement où l'on se trouve (s'il est à soi), sinon celle du logement principal.
            int key = KeyAt(home.transform.position);
            if (key == 0 || !LivesIn(home, key))
                key = MainKey(home);
            if (key == 0)
            {
                economy.ServerNotify("Tu n'as pas de logement.");
                return;
            }
            bool open = !_openDoors.Contains(key);
            if (open)
                _openDoors.Add(key);
            else
                _openDoors.Remove(key);
            SyncDoors();

            string who = home.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "Un habitant";
            economy.ServerNotify(open
                ? "Porte ouverte : tout le monde peut entrer chez toi tant que tu es connecté."
                : "Porte fermée : seuls les habitants peuvent entrer.");
            NotifyResidents(key, who + (open ? " a ouvert la porte." : " a fermé la porte."), home);
        }

        /// <summary>Plus aucun habitant connecté : porte refermée, visiteurs raccompagnés dehors.</summary>
        private void CloseAbandonedDoors()
        {
            _closing.Clear();
            foreach (int key in _openDoors)
            {
                if (ResidentNames(key).Length == 0)
                    _closing.Add(key);
            }
            for (int i = 0; i < _closing.Count; i++)
            {
                _openDoors.Remove(_closing[i]);
                ExpelVisitors(_closing[i], "Tes hôtes sont partis : tu sors.");
            }
            SyncDoors();
        }

        /// <summary>Chaque habitant voit l'état réel de la porte de son logement principal.</summary>
        private void SyncDoors()
        {
            var homes = PlayerHome.ServerInstances;
            for (int i = 0; i < homes.Count; i++)
            {
                PlayerHome home = homes[i];
                if (home == null)
                    continue;
                int key = MainKey(home);
                bool open = key != 0 && _openDoors.Contains(key);
                if (home.DoorOpen != open)
                    home.ServerSetDoorOpen(open);
            }
        }

        private void ExpelVisitors(int key, string message)
        {
            var homes = PlayerHome.ServerInstances;
            for (int i = 0; i < homes.Count; i++)
            {
                PlayerHome visitor = homes[i];
                if (visitor == null || LivesIn(visitor, key) || KeyAt(visitor.transform.position) != key)
                    continue;
                if (visitor.TryGetComponent(out PlayerDriving driving))
                    Leave(driving, key);
                if (visitor.TryGetComponent(out PlayerEconomy economy))
                    economy.ServerNotify(message);
            }
        }

        #endregion

        /// <summary>Pseudos des habitants connectés (vide : personne en ligne).</summary>
        private static string ResidentNames(int key)
        {
            string names = string.Empty;
            var homes = PlayerHome.ServerInstances;
            for (int i = 0; i < homes.Count; i++)
            {
                PlayerHome home = homes[i];
                if (home == null || !LivesIn(home, key))
                    continue;
                string name = home.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "?";
                names = names.Length == 0 ? name : names + " et " + name;
            }
            return names;
        }

        private static void NotifyResidents(int key, string message, PlayerHome except = null)
        {
            var homes = PlayerHome.ServerInstances;
            for (int i = 0; i < homes.Count; i++)
            {
                PlayerHome home = homes[i];
                if (home != null && home != except && LivesIn(home, key) && home.TryGetComponent(out PlayerEconomy economy))
                    economy.ServerNotify(message);
            }
        }
    }
}
