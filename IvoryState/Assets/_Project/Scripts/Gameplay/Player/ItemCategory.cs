namespace IvoryState.Gameplay.Player
{
    /// <summary>Compartiment du sac où se range un objet.</summary>
    public enum ItemCategory
    {
        /// <summary>Nourriture, boissons, soins : tout ce qui remonte la faim, la soif ou la santé.</summary>
        Consumable,
        /// <summary>Armes (machette, couteau, armes à feu) et leurs munitions.</summary>
        Weapon,
        /// <summary>Le reste : papiers, objets divers.</summary>
        Other,
    }

    public static class ItemCategories
    {
        /// <summary>Compartiment d'un objet du sac, déduit de son nom et de ses effets.</summary>
        public static ItemCategory Of(InventoryItem item)
        {
            if (Combat.Weapons.IsWeapon(item.Name) || Combat.Weapons.IsAmmo(item.Name))
                return ItemCategory.Weapon;
            if (item.Satiety > 0 || item.Hydration > 0 || item.Health > 0)
                return ItemCategory.Consumable;
            return ItemCategory.Other;
        }

        public static string Label(ItemCategory category) => category switch
        {
            ItemCategory.Consumable => "Consommables",
            ItemCategory.Weapon => "Armes",
            _ => "Autres",
        };
    }
}
