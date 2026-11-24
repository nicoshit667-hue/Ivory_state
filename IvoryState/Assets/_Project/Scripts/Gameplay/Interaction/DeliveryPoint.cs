namespace IvoryState.Gameplay.Interaction
{
    /// <summary>Adresse de livraison (boutique, maison, bureau…).</summary>
    public sealed class DeliveryPoint : WorldInteractable
    {
        public override InteractionKind Kind => InteractionKind.DeliveryPoint;
        public override string Prompt => "Livrer le colis";
    }
}
