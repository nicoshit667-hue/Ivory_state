using System;
using System.Collections.Generic;
using IvoryState.Gameplay.Economy;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Cache client des propriétaires et états d'ouverture des commerces (alimenté par <c>CommerceService</c>).
    /// </summary>
    public static class CommerceRegistryClient
    {
        public struct Entry
        {
            public int CommerceId;
            public string OwnerName;
            public bool IsMine;
            public bool IsOpen;
        }

        private static readonly Dictionary<int, Entry> s_entries = new Dictionary<int, Entry>();

        public static event Action RegistryUpdated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_entries.Clear();
            RegistryUpdated = null;
        }

        public static void ApplySnapshot(int[] ids, string[] ownerNames, bool[] mine, bool[] openStates)
        {
            s_entries.Clear();
            if (ids != null)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    s_entries[ids[i]] = new Entry
                    {
                        CommerceId = ids[i],
                        OwnerName = ownerNames != null && i < ownerNames.Length ? ownerNames[i] : string.Empty,
                        IsMine = mine != null && i < mine.Length && mine[i],
                        IsOpen = openStates == null || i >= openStates.Length || openStates[i],
                    };
                }
            }
            RegistryUpdated?.Invoke();
        }

        public static bool TryGet(int commerceId, out Entry entry) => s_entries.TryGetValue(commerceId, out entry);
    }

    /// <summary>
    /// Point d'interaction d'un commerce d'Abidjan (Maquis, Restaurant, Fast-Food, Boutique, Pharmacie).
    /// Utilise les données de <see cref="CommerceCatalog"/> et <see cref="ConsumableCatalog"/>.
    /// </summary>
    public sealed class CommercePoint : MenuInteractable
    {
        [SerializeField] private int _commerceId = 1;

        private string _prompt;

        public int CommerceId => _commerceId;
        public override InteractionKind Kind => InteractionKind.Commerce;

        public CommerceDefinition Definition => CommerceCatalog.TryGet(_commerceId, out CommerceDefinition def)
            ? def
            : CommerceCatalog.All[0];

        public override string MenuTitle => Definition.Name;

        public override string MenuGreeting
        {
            get
            {
                string ownerSuffix = string.Empty;
                if (CommerceRegistryClient.TryGet(_commerceId, out CommerceRegistryClient.Entry entry) && !string.IsNullOrEmpty(entry.OwnerName))
                    ownerSuffix = entry.IsOpen ? " [Géré par " + entry.OwnerName + "]" : " [FERMÉ — " + entry.OwnerName + "]";
                return CommerceCatalog.KindLabel(Definition.Kind) + " · " + Definition.Greeting + ownerSuffix;
            }
        }

        public override string Prompt => _prompt ??= "Entrer : " + Definition.Name;
        public override int OptionCount => Definition.ConsumableIds.Length;

        public bool TryGetConsumable(int index, out ConsumableDefinition consumable)
        {
            string[] ids = Definition.ConsumableIds;
            if (index >= 0 && index < ids.Length && ConsumableCatalog.TryGetById(ids[index], out consumable))
                return true;
            consumable = default;
            return false;
        }

        public override string OptionLabel(int index)
        {
            return TryGetConsumable(index, out ConsumableDefinition c) ? c.DisplayName : "Produit";
        }

        public override long OptionPrice(int index)
        {
            return TryGetConsumable(index, out ConsumableDefinition c) ? c.DefaultPrice : 0;
        }

        public override string OptionDetail(int index)
        {
            if (!TryGetConsumable(index, out ConsumableDefinition c))
                return null;

            string s = c.Health > 0 ? "Santé +" + c.Health : string.Empty;
            if (c.Satiety > 0)
                s += (s.Length > 0 ? "   " : string.Empty) + "Faim +" + c.Satiety;
            if (c.Hydration > 0)
                s += (s.Length > 0 ? "   " : string.Empty) + "Soif +" + c.Hydration;
            return s.Length > 0 ? s : null;
        }

        public string OptionIconKey(int index)
        {
            return TryGetConsumable(index, out ConsumableDefinition c) ? c.IconKey : string.Empty;
        }

        public void EditorSetupCommerce(int commerceId)
        {
            _commerceId = commerceId;
            _prompt = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureCommercesInScene()
        {
            // Ne crée rien dans une scène vide (ex. scène de test isolée).
            if (All.Count == 0)
                return;

            var existingIds = new HashSet<int>();
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] is CommercePoint cp)
                    existingIds.Add(cp.CommerceId);
            }

            if (existingIds.Count >= CommerceCatalog.Count)
                return;

            GameObject container = new GameObject("CommercesAbidjan_Runtime");
            for (int i = 0; i < CommerceCatalog.All.Length; i++)
            {
                CommerceDefinition def = CommerceCatalog.All[i];
                if (existingIds.Contains(def.Id))
                    continue;

                var place = CommerceCatalog.GetPlace(def);
                var go = new GameObject("Commerce_" + def.Id + "_" + def.Name);
                go.transform.SetParent(container.transform, false);
                go.transform.position = place.Position;
                go.transform.rotation = Quaternion.LookRotation(-place.ToRoad, Vector3.up);
                go.SetActive(false);

                var point = go.AddComponent<CommercePoint>();
                point.EditorSetup(9500 + def.Id, def.Name);
                point.EditorSetupCommerce(def.Id);
                go.SetActive(true);
            }
        }
    }
}
