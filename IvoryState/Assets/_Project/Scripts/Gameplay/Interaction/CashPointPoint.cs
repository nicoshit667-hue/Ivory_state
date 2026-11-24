using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Guichet Pingoin ou banque : retirer des espèces (débitées du compte Pingoin) ou déposer ses
    /// espèces sur le compte. Les petits services de rue et le gbaka se paient en espèces.
    /// </summary>
    public sealed class CashPointPoint : MenuInteractable
    {
        public static readonly long[] Withdrawals = { 1000, 5000, 20000, 100000 };

        [SerializeField] private string _pointName = "Guichet Pingoin";
        private string _prompt;

        /// <summary>Option « déposer toutes mes espèces » (après les retraits).</summary>
        public int DepositOption => Withdrawals.Length;

        public override InteractionKind Kind => InteractionKind.CashPoint;
        public override string Prompt => _prompt ??= _pointName + " : retrait, dépôt";
        public override string MenuTitle => _pointName;
        public override string MenuGreeting => "Retire des espèces pour les petits achats (étals, gbaka…), ou dépose-les en sécurité sur ton compte.";
        public override int OptionCount => Withdrawals.Length + 1;
        public override string OptionLabel(int index) => index == DepositOption ? "Déposer toutes mes espèces" : "Retirer " + Economy.Money.Format(Withdrawals[index]);
        public override long OptionPrice(int index) => index == DepositOption ? 0 : Withdrawals[index];
        public override string OptionDetail(int index) => index == DepositOption
            ? "Les espèces peuvent être volées, pas ton compte"
            : "Débité de ton compte Pingoin";

#if UNITY_EDITOR
        public void EditorSetupCashPoint(string pointName)
        {
            _pointName = pointName;
            _prompt = null;
        }
#endif
    }
}
