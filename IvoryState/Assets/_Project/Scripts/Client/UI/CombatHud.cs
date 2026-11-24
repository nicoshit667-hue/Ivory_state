using IvoryState.Client.Input;
using IvoryState.Gameplay.Combat;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Interface de combat et notification de zone :
    /// <list type="bullet">
    /// <item><b>Bouton Poing</b> (à pied) et bouton contextuel <b>Racket</b> (si armé près d'une cible).</item>
    /// <item><b>Bannière temporaire de zone</b> : « Zone protégée » ne s'affiche que 3 secondes à l'entrée
    /// dans la zone puis s'estompe pour garder le centre supérieur de l'écran sobre.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatHud : MonoBehaviour
    {
        private const float ZoneBannerDuration = 3.0f;

        [SerializeField] private RectTransform _punchButton;
        [SerializeField] private Image _flash;
        [SerializeField] private Text _status;
        [SerializeField] private RectTransform _robButton;

        private float _flashAlpha;
        private float _nextScan;
        private string _shownStatus;
        private bool _wasInProtectedZone;
        private float _zoneBannerUntil;
        private float _stunnedUntil;
        private WeaponHud _weaponHud;

        private void Awake()
        {
            _flash.raycastTarget = false;
            _flash.color = new Color(0.8f, 0f, 0f, 0f);
            _punchButton.gameObject.SetActive(false);
            if (_robButton != null)
                _robButton.gameObject.SetActive(false);
            _status.text = string.Empty;

            // Armes à feu : boutons construits à l'exécution dans ce canevas.
            _weaponHud = gameObject.AddComponent<WeaponHud>();
            _weaponHud.Build(_status.font);
        }

        private void OnEnable()
        {
            PlayerCombat.LocalHit += OnHit;
            PlayerCombat.LocalRobbed += OnRobbed;
            PlayerCombat.LocalStunned += OnStunned;
            MobilePlayerControls.RegisterBlockingRect(_punchButton);
            if (_robButton != null)
                MobilePlayerControls.RegisterBlockingRect(_robButton);
        }

        private void OnDisable()
        {
            PlayerCombat.LocalHit -= OnHit;
            PlayerCombat.LocalRobbed -= OnRobbed;
            PlayerCombat.LocalStunned -= OnStunned;
            MobilePlayerControls.UnregisterBlockingRect(_punchButton);
            if (_robButton != null)
                MobilePlayerControls.UnregisterBlockingRect(_robButton);
        }

        private void OnStunned(string attacker)
        {
            _flashAlpha = 0.5f;
            _stunnedUntil = Time.unscaledTime + Firearms.StunSeconds;
            _nextScan = 0f;
        }

        private void OnRobbed(string attacker, long amount)
        {
            _flashAlpha = 0.5f;
        }

        private void OnHit(string attacker, bool knockout)
        {
            _flashAlpha = knockout ? 0.75f : 0.35f;
        }

        private void Update()
        {
            if (_flashAlpha > 0f)
            {
                _flashAlpha = Mathf.Max(0f, _flashAlpha - Time.deltaTime * 1.2f);
                _flash.color = new Color(0.8f, 0f, 0f, _flashAlpha);
            }

            if (Time.unscaledTime < _nextScan)
                return;
            _nextScan = Time.unscaledTime + 0.2f;

            PlayerCombat local = PlayerCombat.Local;
            PlayerNetworkIdentity identity = PlayerNetworkIdentity.Local;
            if (local == null || identity == null)
            {
                SetPunchVisible(false);
                if (_robButton != null && _robButton.gameObject.activeSelf)
                    _robButton.gameObject.SetActive(false);
                SetStatus(string.Empty);
                _wasInProtectedZone = false;
                return;
            }

            bool inVehicle = PlayerDriving.Local != null && PlayerDriving.Local.IsInVehicle;
            Vector3 position = identity.transform.position;
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool recovering = local.RecoveringUntil > now;
            bool protectedZone = ProtectedZones.Contains(position) && !Gameplay.Homes.StudioLayout.IsIndoors(position);

            if (protectedZone && !_wasInProtectedZone)
                _zoneBannerUntil = Time.unscaledTime + ZoneBannerDuration;
            _wasInProtectedZone = protectedZone;

            // Arme à feu en main : TIRER remplace FRAPPER.
            bool armed = local.EquippedFirearm != null;
            SetPunchVisible(!inVehicle && !recovering && !armed && !local.IsStunned);

            bool canFight = !inVehicle && !recovering && !protectedZone && local.FindTarget() != null;
            if (_robButton != null)
            {
                PlayerInventory bag = PlayerInventory.Local;
                bool canRob = canFight && bag != null && bag.HasWeapon;
                if (_robButton.gameObject.activeSelf != canRob)
                    _robButton.gameObject.SetActive(canRob);
            }

            bool showZoneBanner = protectedZone && Time.unscaledTime < _zoneBannerUntil;
            float stunLeft = local.IsStunned ? Mathf.Max(1f, _stunnedUntil - Time.unscaledTime) : 0f;
            SetStatus(recovering ? "À l'hôpital : soins encore " + (local.RecoveringUntil - now) + " s"
                : stunLeft > 0f ? "Au sol (taser) : encore " + Mathf.CeilToInt(stunLeft) + " s"
                : showZoneBanner ? "Zone protégée"
                : string.Empty);
        }

        private void SetPunchVisible(bool visible)
        {
            if (_punchButton.gameObject.activeSelf != visible)
                _punchButton.gameObject.SetActive(visible);
        }

        private void SetStatus(string text)
        {
            if (ReferenceEquals(text, _shownStatus) || text == _shownStatus)
                return;
            _shownStatus = text;
            _status.text = text;
        }

#if UNITY_EDITOR
        public void EditorSetup(RectTransform punchButton, Image flash, Text status, RectTransform robButton = null)
        {
            _robButton = robButton;
            _punchButton = punchButton;
            _flash = flash;
            _status = status;
        }
#endif
    }
}
