using System;
using IvoryState.Client.Input;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Choix du pseudo.
    /// <list type="bullet">
    /// <item>Premier lancement : panneau « Choisis ton pseudo ».</item>
    /// <item>Lancements suivants : le pseudo mémorisé sur l'appareil est renvoyé automatiquement.</item>
    /// <item>Test/automatisation : <c>-pseudo Nom</c> en ligne de commande.</item>
    /// </list>
    /// Le serveur reste maître : il nettoie le pseudo et ajoute un suffixe s'il est déjà pris.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NameEntryPanel : MonoBehaviour
    {
        private const string PrefsKey = "ivory.pseudo";

        [SerializeField] private RectTransform _panel;
        [SerializeField] private InputField _input;
        [SerializeField] private Button _confirm;
        [SerializeField] private Text _hint;

        private void Awake()
        {
            _input.characterLimit = PlayerNames.MaxLength;
            _confirm.onClick.AddListener(Confirm);
            _input.onEndEdit.AddListener(_ =>
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && keyboard.enterKey.wasPressedThisFrame)
                    Confirm();
            });
            _panel.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            PlayerNetworkIdentity.LocalPlayerSpawned += OnLocalPlayerSpawned;
            MobilePlayerControls.RegisterBlockingRect(_panel);
        }

        private void OnDisable()
        {
            PlayerNetworkIdentity.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            MobilePlayerControls.UnregisterBlockingRect(_panel);
        }

        private void OnLocalPlayerSpawned(PlayerNetworkIdentity player)
        {
            // Les joueurs choisissent leur pseudo en créant leur compte (AccountPanel) : ce panneau ne sert
            // plus qu'aux clients automatiques (-pseudo en ligne de commande). Il reste disponible pour
            // un usage manuel (ShowManual) mais ne s'ouvre plus de lui-même.
            string fromArgs = ReadArgument("-pseudo");
            if (fromArgs != null && PlayerNames.Sanitize(fromArgs) != null)
                player.RequestName(fromArgs);
        }

        /// <summary>Ouvre le panneau de pseudo (outil de développement).</summary>
        public void ShowManual()
        {
            _hint.text = PlayerNames.MinLength + " à " + PlayerNames.MaxLength + " caractères : lettres, chiffres, espace, tiret.";
            _panel.gameObject.SetActive(true);
            _input.ActivateInputField();
        }

        private void Confirm()
        {
            string name = PlayerNames.Sanitize(_input.text);
            if (name == null)
            {
                _hint.text = "Pseudo invalide : " + PlayerNames.MinLength + " à " + PlayerNames.MaxLength +
                             " caractères, lettres, chiffres, espace ou tiret.";
                return;
            }

            PlayerPrefs.SetString(PrefsKey, name);
            PlayerPrefs.Save();
            PlayerNetworkIdentity.Local?.RequestName(name);
            _panel.gameObject.SetActive(false);
        }

        private static string ReadArgument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
