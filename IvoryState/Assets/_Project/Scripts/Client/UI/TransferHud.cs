using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Ancien bouton « Payer [pseudo] » de proximité, remplacé par l'application Pingoin du téléphone
    /// (voir <see cref="PhoneHud"/>). Le composant reste dans la scène tant qu'elle n'est pas régénérée :
    /// il masque simplement ses éléments au démarrage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TransferHud : MonoBehaviour
    {
        [SerializeField] private Button _payButton;
        [SerializeField] private Text _payLabel;
        [SerializeField] private RectTransform _panel;
        [SerializeField] private Text _title;
        [SerializeField] private Button[] _presetButtons;
        [SerializeField] private InputField _amountInput;
        [SerializeField] private Button _sendButton;
        [SerializeField] private Button _cancelButton;

        private void Awake()
        {
            if (_payButton != null)
                _payButton.gameObject.SetActive(false);
            if (_panel != null)
                _panel.gameObject.SetActive(false);
            enabled = false;
        }
    }
}
