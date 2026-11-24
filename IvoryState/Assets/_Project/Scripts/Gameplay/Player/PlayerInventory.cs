using System;
using System.Collections.Generic;
using System.Text;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    /// <summary>Un emplacement du sac : produit (nom et effets relevés par le serveur à l'achat) et quantité.</summary>
    public struct InventoryItem
    {
        public string Name;
        public byte Satiety;
        public byte Hydration;
        public byte Health;
        public byte Count;
    }

    /// <summary>
    /// Sac du joueur : on achète maintenant, on mange ou on boit plus tard.
    /// <list type="bullet">
    /// <item>Le serveur seul remplit le sac (achat « dans le sac » chez un vendeur) et applique les
    /// effets à l'usage. Le client demande seulement « utiliser l'emplacement n° i ».</item>
    /// <item>Contenu synchronisé au seul propriétaire (personne d'autre ne voit votre sac).</item>
    /// <item>Sauvegardé dans le profil Nakama (texte compact, voir <see cref="ServerSerialize"/>).</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : NetworkBehaviour
    {
        public const int MaxSlots = 8;
        public const int MaxStack = 10;
        private const float UseCooldown = 0.5f;

        private readonly SyncList<InventoryItem> _items =
            new SyncList<InventoryItem>(new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        private float _nextUse;
        private PlayerNeeds _needs;
        private PlayerEconomy _economy;

        public static PlayerInventory Local { get; private set; }

        /// <summary>Client : le contenu du sac local a changé (pour l'appli « Sac » du téléphone).</summary>
        public static event Action LocalChanged;

        public int Count => _items.Count;
        public InventoryItem this[int slot] => _items[slot];

        /// <summary>Nombre d'unités de cet objet dans le sac (tous emplacements confondus).</summary>
        public int CountOf(string name)
        {
            int total = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Name == name)
                    total += _items[i].Count;
            }
            return total;
        }

        public bool Has(string name) => CountOf(name) > 0;

        /// <summary>Retire une unité de cet objet (premier emplacement trouvé). Faux s'il n'y en a pas.</summary>
        [Server]
        public bool ServerTryRemoveOne(string name)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Name == name)
                    return ServerConsumeOne(i, out _);
            }
            return false;
        }

        /// <summary>Une arme (blanche ou à feu) dans le sac (serveur, ou client pour son propre sac).</summary>
        public bool HasWeapon
        {
            get
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (Combat.Weapons.IsWeapon(_items[i].Name))
                        return true;
                }
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalChanged = null;
        }

        private void Awake()
        {
            _needs = GetComponent<PlayerNeeds>();
            _economy = GetComponent<PlayerEconomy>();
            _items.OnChange += OnItemsChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
            {
                Local = this;
                LocalChanged?.Invoke();
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local == this)
                Local = null;
        }

        private void OnItemsChanged(SyncListOperation op, int index, InventoryItem oldItem, InventoryItem newItem, bool asServer)
        {
            if (!asServer && IsOwner)
                LocalChanged?.Invoke();
        }

        #region Serveur

        /// <summary>Indique si le sac peut encore accueillir une unité de ce produit.</summary>
        public bool ServerCanAdd(string name, int satiety = 0, int hydration = 0, int health = 0)
        {
            name = Clean(name);
            byte sat = (byte)Mathf.Clamp(satiety, 0, 100);
            byte hyd = (byte)Mathf.Clamp(hydration, 0, 100);
            byte hp = (byte)Mathf.Clamp(health, 0, 100);
            for (int i = 0; i < _items.Count; i++)
            {
                InventoryItem item = _items[i];
                if (item.Name == name && item.Satiety == sat && item.Hydration == hyd && item.Health == hp && item.Count < MaxStack)
                    return true;
            }
            return _items.Count < MaxSlots;
        }

        /// <summary>Ajoute un produit (empilé avec un produit identique). Faux si le sac est plein.</summary>
        [Server]
        public bool ServerTryAdd(string name, int satiety, int hydration, int health = 0)
        {
            name = Clean(name);
            byte sat = (byte)Mathf.Clamp(satiety, 0, 100);
            byte hyd = (byte)Mathf.Clamp(hydration, 0, 100);
            byte hp = (byte)Mathf.Clamp(health, 0, 100);
            for (int i = 0; i < _items.Count; i++)
            {
                InventoryItem item = _items[i];
                if (item.Name == name && item.Satiety == sat && item.Hydration == hyd && item.Health == hp && item.Count < MaxStack)
                {
                    item.Count++;
                    _items[i] = item;
                    return true;
                }
            }

            if (_items.Count >= MaxSlots)
                return false;

            _items.Add(new InventoryItem
            {
                Name = name,
                Satiety = sat,
                Hydration = hyd,
                Health = hp,
                Count = 1,
            });
            return true;
        }

        /// <summary>Retire une unité d'un emplacement du sac (pour mise en vente sur Djassa).</summary>
        [Server]
        public bool ServerConsumeOne(int slot, out InventoryItem consumed)
        {
            consumed = default;
            if (slot < 0 || slot >= _items.Count)
                return false;

            consumed = _items[slot];
            if (consumed.Count <= 1)
                _items.RemoveAt(slot);
            else
            {
                InventoryItem updated = consumed;
                updated.Count--;
                _items[slot] = updated;
            }
            consumed.Count = 1;
            return true;
        }

        /// <summary>Texte compact pour la sauvegarde : « nom|faim|soif|nombre[|sante];… ».</summary>
        [Server]
        public string ServerSerialize()
        {
            var text = new StringBuilder();
            for (int i = 0; i < _items.Count; i++)
            {
                InventoryItem item = _items[i];
                if (i > 0)
                    text.Append(';');
                text.Append(item.Name).Append('|').Append(item.Satiety).Append('|').Append(item.Hydration).Append('|').Append(item.Count);
                if (item.Health > 0)
                    text.Append('|').Append(item.Health);
            }
            return text.ToString();
        }

        /// <summary>Recharge le sac depuis la sauvegarde (entrées invalides ignorées).</summary>
        [Server]
        public void ServerLoad(string data)
        {
            _items.Clear();
            if (string.IsNullOrEmpty(data))
                return;

            foreach (string entry in data.Split(';'))
            {
                string[] parts = entry.Split('|');
                if ((parts.Length != 4 && parts.Length != 5) || _items.Count >= MaxSlots)
                    continue;
                if (!byte.TryParse(parts[1], out byte satiety) || !byte.TryParse(parts[2], out byte hydration) ||
                    !byte.TryParse(parts[3], out byte count) || count == 0)
                    continue;
                byte health = 0;
                if (parts.Length == 5 && byte.TryParse(parts[4], out byte parsedHealth))
                    health = (byte)Mathf.Min(parsedHealth, 100);
                _items.Add(new InventoryItem
                {
                    Name = Clean(parts[0]),
                    Satiety = (byte)Mathf.Min(satiety, 100),
                    Hydration = (byte)Mathf.Min(hydration, 100),
                    Health = health,
                    Count = (byte)Mathf.Min(count, MaxStack),
                });
            }
        }

        private static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Produit";
            name = name.Replace('|', ' ').Replace(';', ' ');
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }

        #endregion

        #region Utilisation

        /// <summary>Client : manger ou boire le contenu de l'emplacement.</summary>
        public void RequestUse(int slot)
        {
            if (IsOwner && slot >= 0 && slot < _items.Count)
                ServerUse(slot);
        }

        [ServerRpc]
        private void ServerUse(int slot)
        {
            if (Time.unscaledTime < _nextUse || slot < 0 || slot >= _items.Count || _needs == null)
                return;
            _nextUse = Time.unscaledTime + UseCooldown;

            InventoryItem item = _items[slot];
            if (Combat.Weapons.IsMelee(item.Name))
            {
                _economy?.ServerNotify(item.Name + " : une arme. Approche-toi d'un joueur seul et appuie sur RACKET.");
                return;
            }
            if (Combat.Firearms.ByName(item.Name) != null)
            {
                _economy?.ServerNotify(item.Name + " : touche ÉQUIPER dans l'onglet Armes du sac pour la prendre en main.");
                return;
            }
            Combat.FirearmDef ammoFor = Combat.Firearms.ByAmmo(item.Name);
            if (ammoFor != null)
            {
                _economy?.ServerNotify(item.Name + " : munitions du " + ammoFor.Name.ToLowerInvariant() + ". Équipe l'arme, puis appuie sur RECHARGER.");
                return;
            }
            if (item.Name == Combat.Weapons.Permit)
            {
                _economy?.ServerNotify("Port d'arme : il t'autorise à acheter des armes à feu à l'armurerie. Garde-le sur toi.");
                return;
            }
            bool feeds = item.Satiety > 0 && _needs.Satiety < PlayerNeeds.MaxSatiety;
            bool quenches = item.Hydration > 0 && _needs.Hydration < PlayerNeeds.MaxHydration;
            bool heals = item.Health > 0 && _needs.Health < PlayerNeeds.MaxHealth;
            if (!feeds && !quenches && !heals)
            {
                string msg = item.Health > 0 && item.Satiety == 0 && item.Hydration == 0
                    ? "Ta santé est déjà au maximum."
                    : item.Satiety > 0 ? "Tu n'as pas faim pour l'instant." : "Tu n'as pas soif pour l'instant.";
                _economy?.ServerNotify(msg);
                return;
            }

            _needs.ServerChangeSatiety(item.Satiety);
            _needs.ServerChangeHydration(item.Hydration);
            _needs.ServerChangeHealth(item.Health);
            item.Count--;
            if (item.Count == 0)
                _items.RemoveAt(slot);
            else
                _items[slot] = item;
            _economy?.ServerNotify("Sac : " + item.Name + (item.Count > 0 ? " (" + item.Count + " restant" + (item.Count > 1 ? "s" : "") + ")." : "."));
        }

        #endregion
    }
}
