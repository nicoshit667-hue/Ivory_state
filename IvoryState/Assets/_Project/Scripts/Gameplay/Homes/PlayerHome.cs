using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace IvoryState.Gameplay.Homes
{
    /// <summary>
    /// Logement du joueur : son studio dans la Résidence de Treichville et le pseudo de son colocataire.
    /// Écrit par le serveur (chargé depuis Nakama), lisible par le seul propriétaire.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHome : NetworkBehaviour
    {
        private readonly SyncVar<int> _studio = new SyncVar<int>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<string> _roommate = new SyncVar<string>(string.Empty,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        // Loyer : prochaine échéance et dette (propriétaire seulement) ; SDF visible de tous (nom).
        private readonly SyncVar<long> _rentDueAt = new SyncVar<long>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<long> _rentDebt = new SyncVar<long>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<bool> _homeless = new SyncVar<bool>(false);

        public static PlayerHome Local { get; private set; }

        /// <summary>Client : mon logement a changé (attribution, colocataire, loyer, dette).</summary>
        public static event Action LocalChanged;

        public enum HomeAction : byte { PayDebt = 0, PublicAid = 1, ToggleDoor = 2 }

        // Porte de mon studio (ouverte : les visiteurs peuvent entrer tant qu'un colocataire est connecté).
        private readonly SyncVar<bool> _doorOpen = new SyncVar<bool>(false,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        public bool DoorOpen => _doorOpen.Value;

        // --- Bien immobilier (appartement loué ou maison achetée), en plus du studio en colocation ---
        private readonly SyncVar<int> _property = new SyncVar<int>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<bool> _propertyOwner = new SyncVar<bool>(false,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        // Deuxième bien : un joueur peut en avoir 2 (achetés ou loués), en plus de sa colocation.
        private readonly SyncVar<int> _property2 = new SyncVar<int>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private readonly SyncVar<bool> _propertyOwner2 = new SyncVar<bool>(false,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        public const int MaxProperties = 2;

        // --- Domicile principal (réapparition après une longue absence, GPS, porte) ---
        /// <summary>Clé d'un logement : n° de studio (1…), ou 1000 + n° du bien immobilier.</summary>
        public const int PropertyKeyBase = 1000;

        private readonly SyncVar<int> _primaryKey = new SyncVar<int>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        public bool LivesInKey(int key) => key >= PropertyKeyBase ? LivesIn(key - PropertyKeyBase) : key > 0 && Studio == key;

        /// <summary>
        /// Domicile principal : celui choisi par le joueur s'il y habite toujours, sinon (par défaut) sa
        /// première maison achetée, sinon son premier bien, sinon son studio. 0 = aucun.
        /// </summary>
        public int PrimaryKey
        {
            get
            {
                if (_primaryKey.Value != 0 && LivesInKey(_primaryKey.Value))
                    return _primaryKey.Value;
                for (int slot = 0; slot < MaxProperties; slot++)
                {
                    int id = PropertyAt(slot);
                    if (PropertyCatalog.IsValid(id) && OwnsPropertyAt(slot) && PropertyCatalog.IsForSale(PropertyCatalog.Get(id).Kind))
                        return PropertyKeyBase + id;
                }
                if (HasProperty)
                    return PropertyKeyBase + Property;
                return HasHome ? Studio : 0;
            }
        }

        /// <summary>Nombre de domiciles (studio compris) : le choix du principal n'a de sens qu'à partir de 2.</summary>
        public int HomeCount => PropertyCount + (HasHome ? 1 : 0);

        /// <summary>Valeur enregistrée du choix (pour la sauvegarde).</summary>
        public int ServerPrimaryChoice => _primaryKey.Value;

        public void RequestSetPrimary(int key)
        {
            if (IsOwner)
                ServerSetPrimaryRpc(key);
        }

        [ServerRpc]
        private void ServerSetPrimaryRpc(int key)
        {
            if (Time.unscaledTime < _nextAction || !LivesInKey(key))
                return;
            _nextAction = Time.unscaledTime + 1f;
            _primaryKey.Value = key;
            ServerRentChanged?.Invoke(this); // sauvegarde du profil
        }

        [Server]
        public void ServerLoadPrimary(int key) => _primaryKey.Value = key;

        /// <summary>Bien principal habité (0 = aucun).</summary>
        public int Property => _property.Value;
        /// <summary>Titulaire du bail ou propriétaire du bien principal (sinon simple invité).</summary>
        public bool IsPropertyOwner => _propertyOwner.Value;
        public bool HasProperty => PropertyCatalog.IsValid(_property.Value);

        public int PropertyCount => (PropertyCatalog.IsValid(_property.Value) ? 1 : 0) + (PropertyCatalog.IsValid(_property2.Value) ? 1 : 0);
        public bool CanTakeProperty => PropertyCount < MaxProperties;

        /// <summary>Bien n° slot (0 ou 1), 0 si vide.</summary>
        public int PropertyAt(int slot) => slot == 0 ? _property.Value : slot == 1 ? _property2.Value : 0;
        public bool OwnsPropertyAt(int slot) => slot == 0 ? _propertyOwner.Value : slot == 1 && _propertyOwner2.Value;

        public bool LivesIn(int id) => PropertyCatalog.IsValid(id) && (_property.Value == id || _property2.Value == id);
        public bool IsOwnerOf(int id) => PropertyCatalog.IsValid(id) &&
                                         ((_property.Value == id && _propertyOwner.Value) || (_property2.Value == id && _propertyOwner2.Value));

        [Server]
        public void ServerAddProperty(int id, bool owner)
        {
            if (!PropertyCatalog.IsValid(id) || LivesIn(id))
                return;
            if (!PropertyCatalog.IsValid(_property.Value))
            {
                _property.Value = id;
                _propertyOwner.Value = owner;
            }
            else if (!PropertyCatalog.IsValid(_property2.Value))
            {
                _property2.Value = id;
                _propertyOwner2.Value = owner;
            }
        }

        [Server]
        public void ServerRemoveProperty(int id)
        {
            if (_property2.Value == id)
            {
                _property2.Value = 0;
                _propertyOwner2.Value = false;
            }
            if (_property.Value == id)
            {
                // Le deuxième bien devient le principal.
                _property.Value = _property2.Value;
                _propertyOwner.Value = _propertyOwner2.Value;
                _property2.Value = 0;
                _propertyOwner2.Value = false;
            }
        }

        [Server]
        public void ServerClearProperties()
        {
            _property.Value = 0;
            _propertyOwner.Value = false;
            _property2.Value = 0;
            _propertyOwner2.Value = false;
        }

        public enum PropertyRequest : byte
        {
            Take = 0, Leave = 1, LeaveColocation = 2, List = 3,
            Invite = 4, AcceptInvite = 5, DeclineInvite = 6, // emménager chez quelqu'un (id = objet du joueur)
        }

        /// <summary>Client : un propriétaire me propose d'emménager chez lui. (objet, pseudo, logement)</summary>
        public static event Action<int, string, string> LocalMoveInOffer;

        /// <summary>Propose à un joueur proche d'emménager dans mon logement (2 personnes par chambre).</summary>
        public void RequestInvite(int targetObjectId) => SendProperty(PropertyRequest.Invite, targetObjectId);

        public void RespondInvite(int fromObjectId, bool accept) =>
            SendProperty(accept ? PropertyRequest.AcceptInvite : PropertyRequest.DeclineInvite, fromObjectId);

        [Server]
        public void ServerSendMoveInOffer(int fromObjectId, string fromName, string place)
        {
            if (Owner.IsValid)
                TargetMoveInOffer(Owner, fromObjectId, fromName, place);
        }

        [TargetRpc]
        private void TargetMoveInOffer(FishNet.Connection.NetworkConnection connection, int fromObjectId, string fromName, string place)
        {
            LocalMoveInOffer?.Invoke(fromObjectId, fromName, place);
        }

        /// <summary>Serveur : louer ou acheter, quitter, lister (géré par le service immobilier).</summary>
        public static Action<PlayerHome, PropertyRequest, int> ServerPropertyHandler { get; set; }

        /// <summary>Client : état des biens (numéros, occupants « » = libre).</summary>
        public static event Action<int[], string[]> LocalPropertyList;

        public void RequestTakeProperty(int id) => SendProperty(PropertyRequest.Take, id);
        /// <summary>Quitter (résilier, revendre) un de mes biens.</summary>
        public void RequestLeaveProperty(int id) => SendProperty(PropertyRequest.Leave, id);
        public void RequestLeaveColocation() => SendProperty(PropertyRequest.LeaveColocation, 0);
        public void RequestPropertyList() => SendProperty(PropertyRequest.List, 0);

        private void SendProperty(PropertyRequest request, int id)
        {
            if (IsOwner)
                ServerPropertyRequest(request, id);
        }

        [ServerRpc]
        private void ServerPropertyRequest(PropertyRequest request, int id)
        {
            if (Time.unscaledTime < _nextAction)
                return;
            _nextAction = Time.unscaledTime + (request == PropertyRequest.List ? 0.5f : 2f);
            ServerPropertyHandler?.Invoke(this, request, id);
        }


        [Server]
        public void ServerSendPropertyList(int[] ids, string[] occupants)
        {
            if (Owner.IsValid)
                TargetPropertyList(Owner, ids, occupants);
        }

        [TargetRpc]
        private void TargetPropertyList(FishNet.Connection.NetworkConnection connection, int[] ids, string[] occupants)
        {
            LocalPropertyList?.Invoke(ids, occupants);
        }

        /// <summary>Serveur : le joueur choisit un studio dans le menu de la résidence. (joueur, studio)</summary>
        public static Action<PlayerHome, int> ServerEnterHandler { get; set; }

        /// <summary>Serveur : un colocataire ouvre ou ferme la porte de son studio.</summary>
        public static Action<PlayerHome> ServerDoorHandler { get; set; }

        /// <summary>Client : menu de la résidence (studios ouverts : numéros et habitants).</summary>
        public static event Action<int[], string[]> LocalResidenceMenu;

        public void RequestToggleDoor() => Request(HomeAction.ToggleDoor);

        public void RequestEnterStudio(int studio)
        {
            if (IsOwner)
                ServerEnterStudio(studio);
        }

        [ServerRpc]
        private void ServerEnterStudio(int studio)
        {
            if (Time.unscaledTime < _nextAction)
                return;
            _nextAction = Time.unscaledTime + 1f;
            // Clé : n° de studio, ou 1000 + n° de bien immobilier ; 0 = rien.
            if (StudioLayout.IsValid(studio) || PropertyCatalog.IsValid(studio - 1000))
                ServerEnterHandler?.Invoke(this, studio);
        }

        [Server]
        public void ServerSetDoorOpen(bool open) => _doorOpen.Value = open;

        /// <summary>Serveur : propose le menu de la résidence au joueur.</summary>
        [Server]
        public void ServerShowResidenceMenu(int[] studios, string[] names)
        {
            if (Owner.IsValid)
                TargetResidenceMenu(Owner, studios, names);
        }

        [TargetRpc]
        private void TargetResidenceMenu(FishNet.Connection.NetworkConnection connection, int[] studios, string[] names)
        {
            LocalResidenceMenu?.Invoke(studios, names);
        }

        /// <summary>Serveur : le joueur paie sa dette ou demande une aide (géré par le service des loyers).</summary>
        public static Action<PlayerHome, HomeAction> ServerActionHandler { get; set; }

        /// <summary>Serveur : loyer, dette ou logement modifiés (à sauvegarder).</summary>
        public static event Action<PlayerHome> ServerRentChanged;

        private static readonly System.Collections.Generic.List<PlayerHome> s_serverInstances =
            new System.Collections.Generic.List<PlayerHome>(128);

        public static System.Collections.Generic.IReadOnlyList<PlayerHome> ServerInstances => s_serverInstances;

        /// <summary>Numéro du studio (0 = pas de logement).</summary>
        public int Studio => _studio.Value;
        public bool HasHome => StudioLayout.IsValid(_studio.Value);
        public string Roommate => _roommate.Value;
        public long RentDueAt => _rentDueAt.Value;
        public long RentDebt => _rentDebt.Value;
        public bool IsHomeless => _homeless.Value;

        // --- État serveur (sauvegardé dans le profil) ---
        /// <summary>Loyer payé jusqu'à cet instant (secondes Unix ; 0 = jamais initialisé).</summary>
        public long ServerPaidUntil { get; private set; }
        public long ServerAidAt { get; private set; }
        /// <summary>Le profil (et donc le loyer) est chargé : le service des loyers peut agir.</summary>
        public bool ServerRentLoaded { get; private set; }

        private float _nextAction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalChanged = null;
            ServerActionHandler = null;
            ServerEnterHandler = null;
            ServerDoorHandler = null;
            ServerPropertyHandler = null;
            LocalPropertyList = null;
            LocalMoveInOffer = null;
            LocalResidenceMenu = null;
            ServerRentChanged = null;
            s_serverInstances.Clear();
        }

        private void Awake()
        {
            _studio.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _roommate.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _rentDueAt.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _rentDebt.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _homeless.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _doorOpen.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _property.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _propertyOwner.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _property2.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _primaryKey.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
            _propertyOwner2.OnChange += (_, _, asServer) => { if (!asServer) LocalChanged?.Invoke(); };
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

        public void RequestPayDebt() => Request(HomeAction.PayDebt);

        public void RequestPublicAid() => Request(HomeAction.PublicAid);

        private void Request(HomeAction action)
        {
            if (IsOwner)
                ServerRequest(action);
        }

        [ServerRpc]
        private void ServerRequest(HomeAction action)
        {
            if (Time.unscaledTime < _nextAction)
                return;
            _nextAction = Time.unscaledTime + 2f;
            if (action == HomeAction.ToggleDoor)
                ServerDoorHandler?.Invoke(this);
            else
                ServerActionHandler?.Invoke(this, action);
        }

        /// <summary>Serveur : état du loyer lu dans le profil.</summary>
        [Server]
        public void ServerLoadRent(long paidUntil, long debt, bool homeless, long aidAt)
        {
            ServerPaidUntil = paidUntil;
            ServerAidAt = aidAt;
            _rentDebt.Value = Math.Max(0, debt);
            _homeless.Value = homeless;
            _rentDueAt.Value = paidUntil > 0 ? RentRules.NextDue(paidUntil) : 0;
            ServerRentLoaded = true;
        }

        [Server]
        public void ServerSetRent(long paidUntil, long debt)
        {
            ServerPaidUntil = paidUntil;
            _rentDebt.Value = Math.Max(0, debt);
            _rentDueAt.Value = RentRules.NextDue(paidUntil);
            ServerRentChanged?.Invoke(this);
        }

        [Server]
        public void ServerSetHomeless(bool homeless)
        {
            _homeless.Value = homeless;
            ServerRentChanged?.Invoke(this);
        }

        [Server]
        public void ServerMarkAid(long when)
        {
            ServerAidAt = when;
            ServerRentChanged?.Invoke(this);
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

        [Server]
        public void ServerSetHome(int studio, string roommate)
        {
            _studio.Value = StudioLayout.IsValid(studio) ? studio : 0;
            _roommate.Value = roommate ?? string.Empty;
        }
    }
}
