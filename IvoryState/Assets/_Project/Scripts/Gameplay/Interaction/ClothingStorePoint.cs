using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Boutique de vêtements : on achète une tenue une fois, elle reste à soi (garde-robe sauvegardée) ;
    /// on peut ensuite la remettre gratuitement dans n'importe quelle boutique. Le serveur relit le prix
    /// sur sa propre copie du point.
    /// </summary>
    public sealed class ClothingStorePoint : MenuInteractable
    {
        public static class Outfits
        {
            public static readonly string[] Names = { "Tenue de base", "Tenue décontractée", "Tenue en pagne wax", "Tenue de soirée" };
            public static readonly long[] Prices = { 0, 8000, 15000, 35000 };
            public static int Count => Names.Length;
        }

        [SerializeField] private string _shopName = "Boutique";
        private string _prompt;

        public override InteractionKind Kind => InteractionKind.ClothingStore;
        public override string Prompt => _prompt ??= "Entrer chez " + _shopName;
        public override string MenuTitle => _shopName;
        public override string MenuGreeting => "La vendeuse : « Bonne arrivée ! Une tenue achetée reste à toi : tu la remets gratuitement quand tu veux. »";
        public override int OptionCount => Outfits.Count;
        public override string OptionLabel(int index) => Outfits.Names[index];
        public override long OptionPrice(int index) => Outfits.Prices[index];
        public override string OptionDetail(int index) => index == 0 ? "Toujours gratuite" : "Gratuite si tu l'as déjà achetée";

#if UNITY_EDITOR
        public void EditorSetupStore(string shopName)
        {
            _shopName = shopName;
            _prompt = null;
        }
#endif
    }
}
