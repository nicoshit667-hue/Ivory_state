using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>Auto-école : on y passe le permis de conduire, obligatoire pour prendre le volant.</summary>
    public sealed class DrivingSchoolPoint : MenuInteractable
    {
        public override InteractionKind Kind => InteractionKind.DrivingSchool;
        public override string Prompt => "Auto-école : passer le permis";
        public override string MenuGreeting => "Le moniteur : « Code de la route et conduite en ville, on t'accompagne jusqu'au permis ! »";
        public override int OptionCount => 1;
        public override string OptionLabel(int index) => "Permis de conduire (code + conduite)";
        public override long OptionPrice(int index) => PlayerGarage.Rules.LicensePrice;
        public override string OptionDetail(int index) => "Valable à vie, pour les taxis et ta propre voiture";
    }
}
