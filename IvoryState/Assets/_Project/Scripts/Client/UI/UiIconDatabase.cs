using System;
using System.Collections.Generic;
using UnityEngine;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Base de référence runtime des icônes et fonds importés sous <c>Assets/Art/UI/Icons/</c>
    /// (<c>Phone/</c>, <c>Consumables/</c>, <c>Profile/</c>).
    /// Synchronisée automatiquement par <c>CustomIconsImporter</c> dans <c>Resources/UI/UiIconDatabase.asset</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "UiIconDatabase", menuName = "Ivory State/UI/Icon Database")]
    public sealed class UiIconDatabase : ScriptableObject
    {
        [Serializable]
        public struct NamedSprite
        {
            public string Key;
            public Sprite Sprite;
        }

        [Header("Phone Apps")]
        public Sprite TaxiAppIcon;
        public Sprite CommerceAppIcon;

        [Header("Consumables (Assets/Art/UI/Icons/Consumables)")]
        public NamedSprite[] Consumables = Array.Empty<NamedSprite>();

        [Header("Profile Icons & Card Backgrounds (Assets/Art/UI/Icons/Profile)")]
        public NamedSprite[] ProfileIcons = Array.Empty<NamedSprite>();
        public NamedSprite[] ProfileBackgrounds = Array.Empty<NamedSprite>();

        private Dictionary<string, Sprite> _consumableMap;
        private Dictionary<string, Sprite> _profileIconMap;
        private Dictionary<string, Sprite> _profileBgMap;

        private void OnEnable()
        {
            _consumableMap = null;
            _profileIconMap = null;
            _profileBgMap = null;
        }

        public bool TryGetConsumable(string key, out Sprite sprite)
        {
            EnsureMap(ref _consumableMap, Consumables);
            return TryLookup(_consumableMap, key, out sprite);
        }

        public bool TryGetProfileIcon(string key, out Sprite sprite)
        {
            EnsureMap(ref _profileIconMap, ProfileIcons);
            return TryLookup(_profileIconMap, key, out sprite);
        }

        public bool TryGetProfileBackground(string key, out Sprite sprite)
        {
            EnsureMap(ref _profileBgMap, ProfileBackgrounds);
            return TryLookup(_profileBgMap, key, out sprite);
        }

        private static void EnsureMap(ref Dictionary<string, Sprite> map, NamedSprite[] entries)
        {
            if (map != null)
                return;
            map = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
                return;
            for (int i = 0; i < entries.Length; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].Key) && entries[i].Sprite != null)
                    map[entries[i].Key] = entries[i].Sprite;
            }
        }

        private static bool TryLookup(Dictionary<string, Sprite> map, string key, out Sprite sprite)
        {
            if (string.IsNullOrEmpty(key) || map == null)
            {
                sprite = null;
                return false;
            }
            return map.TryGetValue(key, out sprite) && sprite != null;
        }
    }
}
