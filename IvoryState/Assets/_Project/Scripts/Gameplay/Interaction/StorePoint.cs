using System;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Commerce tenu par un vendeur : superette, boutique de quartier, étal du marché, maquis…
    /// Le joueur parle au vendeur et choisit dans la liste des produits. Chaque produit rend de la
    /// satiété et/ou de l'hydratation (la jauge va de 0 à 100).
    /// </summary>
    public sealed class StorePoint : MenuInteractable
    {
        [Serializable]
        public struct Product
        {
            public string Name;
            public long Price;
            [Range(0, 100)] public int Satiety;
            [Range(0, 100)] public int Hydration;
            [Range(0, 100)] public int Health;
            public string IconKey;
        }

        [SerializeField] private string _vendorName = "Tantie Awa";
        [SerializeField] private string _greeting = "Bonjour ! Qu'est-ce qu'on te sert ?";
        [SerializeField] private Product[] _products = new Product[0];

        private string _prompt;

        public override InteractionKind Kind => InteractionKind.Store;
        public string VendorName => _vendorName;
        public override string MenuGreeting => _vendorName + " : « " + _greeting + " »";
        public override string Prompt => _prompt ??= "Parler à " + _vendorName;
        public override int OptionCount => _products.Length;

        public bool TryGetProduct(int index, out Product product)
        {
            if (index >= 0 && index < _products.Length)
            {
                product = _products[index];
                return true;
            }
            product = default;
            return false;
        }

        public override string OptionLabel(int index) => _products[index].Name;
        public override long OptionPrice(int index) => _products[index].Price;

        public override string OptionDetail(int index)
        {
            Product p = _products[index];
            string text = p.Health > 0 ? "Santé +" + p.Health : string.Empty;
            if (p.Satiety > 0)
                text += (text.Length > 0 ? "   " : string.Empty) + "Faim +" + p.Satiety;
            if (p.Hydration > 0)
                text += (text.Length > 0 ? "   " : string.Empty) + "Soif +" + p.Hydration;
            return text.Length > 0 ? text : null;
        }

        public string OptionIconKey(int index)
        {
            if (index < 0 || index >= _products.Length)
                return string.Empty;
            Product p = _products[index];
            return !string.IsNullOrEmpty(p.IconKey)
                ? p.IconKey
                : Economy.ConsumableCatalog.ResolveIconKey(p.Name);
        }

        public void EditorSetupStore(string vendorName, string greeting, Product[] products)
        {
            _vendorName = vendorName;
            _greeting = greeting;
            _products = products;
            _prompt = null;
        }
    }
}
