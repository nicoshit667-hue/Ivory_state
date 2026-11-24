using IvoryState.Gameplay.Economy;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Étal : nourriture (garba, alloco, attiéké…) ou boisson (eau en sachet, bissap, gnamakoudji).
    /// Le joueur paie, sa faim et/ou sa soif diminuent. Le serveur lit le prix et les effets sur
    /// <b>sa</b> copie de la scène.
    /// </summary>
    public sealed class ShopPoint : WorldInteractable
    {
        [SerializeField] private string _itemName = "Garba";
        [SerializeField, Min(0)] private long _price = 1000;

        [Tooltip("Points de satiété rendus (la jauge va de 0 à 100).")]
        [SerializeField, Range(0, 100)] private int _satiety = 40;

        [Tooltip("Points d'hydratation rendus (la jauge va de 0 à 100).")]
        [SerializeField, Range(0, 100)] private int _hydration;

        private string _prompt;

        public override InteractionKind Kind => InteractionKind.Shop;
        public string ItemName => _itemName;
        public long Price => _price;
        public int Satiety => _satiety;
        public int Hydration => _hydration;

        /// <summary>Boisson pure (n'apaise que la soif).</summary>
        public bool IsDrink => _hydration > 0 && _satiety == 0;

        // Construit une fois : le bouton peut l'afficher à chaque frame sans allocation.
        public override string Prompt => _prompt ??= "Acheter " + _itemName + " — " + Money.Format(_price);

        public void EditorSetupShop(string itemName, long price, int satiety, int hydration = 0)
        {
            _itemName = itemName;
            _price = price;
            _satiety = satiety;
            _hydration = hydration;
            _prompt = null;
        }
    }
}
