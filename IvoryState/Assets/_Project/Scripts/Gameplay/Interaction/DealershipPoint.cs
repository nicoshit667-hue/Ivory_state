using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Concessionnaire : achat d'une berline (à soi pour toujours) ou location pour 24 h. La voiture
    /// apparaît sur le parking (<see cref="DeliveryPosition"/>).
    /// </summary>
    public sealed class DealershipPoint : MenuInteractable
    {
        [SerializeField] private string _shopName = "Concessionnaire";
        [Tooltip("Place de parking où la voiture achetée ou louée apparaît (repère local).")]
        [SerializeField] private Vector3 _deliveryOffset = new Vector3(0f, 0f, -6f);

        private string _prompt;

        /// <summary>Options : les berlines à vendre (couleurs 1…5), puis la location.</summary>
        public int RentOption => VehicleMotor.Paints.Length - 1;

        public Vector3 DeliveryPosition => transform.TransformPoint(_deliveryOffset);
        public Quaternion DeliveryRotation => transform.rotation;

        public override InteractionKind Kind => InteractionKind.Dealership;
        public override string Prompt => _prompt ??= "Entrer chez " + _shopName;
        public override string MenuTitle => _shopName;
        public override string MenuGreeting => "Le vendeur : « Neuves, garanties, prêtes à rouler ! Il te faut le permis pour les conduire. »";
        public override int OptionCount => VehicleMotor.Paints.Length; // 5 couleurs + location
        public override string OptionLabel(int index) => index == RentOption ? "Location d'une berline (24 h)" : VehicleMotor.PaintNames[index + 1];
        public override long OptionPrice(int index) => index == RentOption ? PlayerGarage.Rules.RentPerDay : PlayerGarage.Rules.CarPrices[index + 1];
        public override string OptionDetail(int index) => index == RentOption
            ? "5 places, rendue automatiquement au bout de 24 h"
            : "5 places, à toi pour toujours (application Garage)";

        /// <summary>Couleur de la voiture pour une option.</summary>
        public byte PaintFor(int index) => (byte)(index == RentOption ? 5 : index + 1);

#if UNITY_EDITOR
        public void EditorSetupDealership(string shopName, Vector3 deliveryOffset)
        {
            _shopName = shopName;
            _deliveryOffset = deliveryOffset;
            _prompt = null;
        }
#endif
    }
}
