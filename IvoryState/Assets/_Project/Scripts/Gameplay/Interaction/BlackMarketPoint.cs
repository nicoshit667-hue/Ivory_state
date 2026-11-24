namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Marché black d'Adjamé : le lieu physique de Djassa. Toucher le bouton d'action ouvre directement
    /// les annonces (application Djassa du téléphone) ; les ventes passent par le même service.
    /// </summary>
    public sealed class BlackMarketPoint : MenuInteractable
    {
        public override InteractionKind Kind => InteractionKind.BlackMarket;
        public override string Prompt => "Marché black : voir les annonces";
        public override string MenuGreeting => "Ici on achète et on vend de tout : voitures, maisons…";
        public override int OptionCount => 0;
        public override string OptionLabel(int index) => string.Empty;
        public override long OptionPrice(int index) => 0;
    }
}
