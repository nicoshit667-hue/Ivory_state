namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Dépôt où l'on prend une mission de livraison. Le serveur choisit l'adresse,
    /// le délai et la rémunération (voir EconomyService côté serveur).
    /// </summary>
    public sealed class JobBoardPoint : WorldInteractable
    {
        public override InteractionKind Kind => InteractionKind.JobBoard;
        public override string Prompt => "Prendre une livraison";
    }
}
