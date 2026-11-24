using System;
using IvoryState.Client.Cameras;
using IvoryState.Client.Characters;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Point d'entrée UI des 4 écrans principaux (<see cref="UIManager"/>, <see cref="AuthFlowController"/>,
    /// <see cref="CharacterCustomizer"/>, <see cref="TurntableCameraController"/>, <see cref="WardrobeShopManager"/>).
    /// Instancié automatiquement par <see cref="PhoneHud"/> au démarrage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AccountPanel : MonoBehaviour
    {
        private UIManager _uiManager;
        private AuthFlowController _authFlow;
        private WardrobeShopManager _wardrobeShop;

        public void Initialize(Sprite rounded, Font font)
        {
            if (GetComponent<CharacterCustomizer>() == null)
                gameObject.AddComponent<CharacterCustomizer>();
            if (GetComponent<TurntableCameraController>() == null)
                gameObject.AddComponent<TurntableCameraController>();

            _uiManager = gameObject.AddComponent<UIManager>();
            _authFlow = gameObject.AddComponent<AuthFlowController>();
            _wardrobeShop = gameObject.AddComponent<WardrobeShopManager>();

            var root = (RectTransform)transform;
            _authFlow.Initialize(root, font);
            _wardrobeShop.Initialize(root, font);

            if (!IsAutomatedClient())
                _uiManager.TransitionTo(UIScreenState.MainMenu);
        }

        private void OnEnable()
        {
            PlayerNetworkIdentity.LocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        private void OnDisable()
        {
            PlayerNetworkIdentity.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        }

        /// <summary>Bots et tests automatisés : identifiant d'appareil et pseudo en ligne de commande.</summary>
        public static bool IsAutomatedClient()
        {
            if (Application.isBatchMode)
                return true;
            string[] args = Environment.GetCommandLineArgs();
            return Array.IndexOf(args, "-deviceId") >= 0 || Array.IndexOf(args, "-pseudo") >= 0 || Array.IndexOf(args, "-bot") >= 0;
        }

        private void OnLocalPlayerSpawned(PlayerNetworkIdentity player)
        {
            if (IsAutomatedClient())
                return;

            if (_authFlow != null)
                _authFlow.OnLocalPlayerSpawned(player);
            else
                _uiManager?.TransitionTo(UIScreenState.MainMenu);
        }
    }
}
