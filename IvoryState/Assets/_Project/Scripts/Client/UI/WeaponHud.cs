using IvoryState.Client.Input;
using IvoryState.Gameplay.Combat;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Commandes d'arme à feu, visibles seulement avec une arme en main (équipée depuis le sac) :
    /// <list type="bullet">
    /// <item><b>TIRER</b> (à la place de FRAPPER) : un appui = un tir ; la kalach tire en rafale tant qu'on appuie.</item>
    /// <item><b>RECHARGER</b> : un chargeur du sac. Le chargeur vide se recharge aussi tout seul au tir suivant.</item>
    /// <item><b>VISER</b> : caméra rapprochée par-dessus l'épaule, réticule resserré, aide à la visée réduite.</item>
    /// <item>Compteur de balles et réticule au centre de l'écran.</item>
    /// </list>
    /// Construit à l'exécution par <see cref="CombatHud"/> dans le même canevas (pas de scène à régénérer).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponHud : MonoBehaviour
    {
        private static readonly Color Gold = new Color(0.85f, 0.68f, 0.30f, 0.90f);
        private static readonly Color Red = new Color(0.80f, 0.20f, 0.15f, 0.92f);
        private static readonly Color Taser = new Color(0.95f, 0.80f, 0.10f, 0.92f);
        private static readonly Color Dark = new Color(0.05f, 0.06f, 0.09f, 0.80f);

        private RectTransform _root;
        private RectTransform _fire;
        private RectTransform _reload;
        private RectTransform _aim;
        private Image _fireRing;
        private Image _aimRing;
        private Text _fireLabel;
        private Text _ammo;
        private RectTransform _crosshair;
        private Font _font;

        private bool _held;
        private bool _pressedThisFrame;
        private bool _visible;

        public bool Visible => _visible;

        public void Build(Font font)
        {
            _font = font;
            HudIconLibrary.EnsureInitialized();

            _root = new GameObject("WeaponHud", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(transform, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;

            _crosshair = MakeIcon("Reticule", _root, HudIconLibrary.LoadWeaponAimSprite(), new Vector2(0.5f, 0.5f), new Vector2(64f, 64f), Vector2.zero);
            if (_crosshair.GetComponent<Image>().sprite == null)
                _crosshair.GetComponent<Image>().sprite = HudIconLibrary.ThinRing;

            _fire = MakeButton("BoutonTirer", new Vector2(-570f, 190f), 170f, Red, HudIconLibrary.LoadWeaponFireSprite(), out _fireRing);
            _fireLabel = MakeLabel(_fire, "TIRER", -52f);
            _fire.Find("Fond").gameObject.AddComponent<HudPressRelay>().Init(() => { _held = true; _pressedThisFrame = true; }, () => _held = false);

            _reload = MakeButton("BoutonRecharger", new Vector2(-770f, 150f), 116f, Gold, HudIconLibrary.LoadWeaponReloadSprite(), out _);
            MakeLabel(_reload, "RECHARGER", -40f, 15);
            _reload.Find("Fond").gameObject.AddComponent<HudPressRelay>().Init(() => PlayerCombat.Local?.RequestReload(), null);

            _aim = MakeButton("BoutonViser", new Vector2(-770f, 300f), 116f, Gold, HudIconLibrary.LoadWeaponAimSprite(), out _aimRing);
            MakeLabel(_aim, "VISER", -40f, 15);
            _aim.Find("Fond").gameObject.AddComponent<HudPressRelay>().Init(() => PlayerCombat.LocalAiming = !PlayerCombat.LocalAiming, null);

            _ammo = MakeText("Munitions", _root, 30, TextAnchor.MiddleCenter, new Vector2(1f, 0f), new Vector2(300f, 44f), new Vector2(-570f, 305f));
            _ammo.gameObject.AddComponent<Outline>().effectColor = Color.black;

            MobilePlayerControls.RegisterBlockingRect(_fire);
            MobilePlayerControls.RegisterBlockingRect(_reload);
            MobilePlayerControls.RegisterBlockingRect(_aim);
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_fire == null)
                return;
            MobilePlayerControls.UnregisterBlockingRect(_fire);
            MobilePlayerControls.UnregisterBlockingRect(_reload);
            MobilePlayerControls.UnregisterBlockingRect(_aim);
        }

        private void Update()
        {
            if (_root == null)
                return;

            PlayerCombat combat = PlayerCombat.Local;
            FirearmDef def = combat != null ? combat.EquippedFirearm : null;
            bool inVehicle = PlayerDriving.Local != null && PlayerDriving.Local.IsInVehicle;
            bool show = def != null && !inVehicle && !combat.IsStunned;
            SetVisible(show);
            if (!show)
            {
                _held = _pressedThisFrame = false;
                PlayerCombat.LocalAiming = false;
                return;
            }

            // Tir : au coup par coup (un appui = un tir), en rafale pour une arme automatique.
            if (_pressedThisFrame || (_held && def.Automatic))
                combat.TryFire();
            _pressedThisFrame = false;

            bool aiming = PlayerCombat.LocalAiming;
            _crosshair.sizeDelta = Vector2.one * (aiming ? 40f : 64f);
            _aimRing.color = aiming ? Color.white : Gold;
            _fireRing.color = def.Stun ? Taser : Red;
            _fireLabel.text = def.Stun ? "TASER" : "TIRER";

            PlayerInventory bag = PlayerInventory.Local;
            int magazines = bag != null ? bag.CountOf(def.Ammo) : 0;
            _ammo.text = combat.IsReloading
                ? "Recharge…"
                : combat.Loaded + " / " + def.MagazineSize + "   ·   " + magazines + (def.MagazineSize > 1 ? " chargeur" : " cartouche") + (magazines > 1 ? "s" : string.Empty);
            _ammo.color = combat.Loaded == 0 ? new Color(1f, 0.45f, 0.4f) : Color.white;
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible && _root.gameObject.activeSelf == visible)
                return;
            _visible = visible;
            _root.gameObject.SetActive(visible);
        }

        #region Construction

        private RectTransform MakeButton(string name, Vector2 position, float size, Color ring, Sprite icon, out Image ringImage)
        {
            RectTransform button = MakeImage(name, _root, HudIconLibrary.Circle, ring, new Vector2(1f, 0f), new Vector2(size, size), position);
            ringImage = button.GetComponent<Image>();
            RectTransform inner = MakeImage("Fond", button, HudIconLibrary.Circle, Dark, new Vector2(0.5f, 0.5f), new Vector2(size - 12f, size - 12f), Vector2.zero);
            inner.GetComponent<Image>().raycastTarget = true;
            if (icon != null)
                MakeIcon("Icone", button, icon, new Vector2(0.5f, 0.5f), new Vector2(size * 0.5f, size * 0.5f), new Vector2(0f, size * 0.08f));
            return button;
        }

        private Text MakeLabel(RectTransform button, string label, float y, int size = 20)
        {
            Text text = MakeText("Libelle", button, size, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(150f, 28f), new Vector2(0f, y));
            text.text = label;
            text.color = new Color(1f, 0.92f, 0.85f, 1f);
            return text;
        }

        private static RectTransform MakeIcon(string name, Transform parent, Sprite sprite, Vector2 anchor, Vector2 size, Vector2 position)
        {
            RectTransform rect = MakeImage(name, parent, sprite, Color.white, anchor, size, position);
            rect.GetComponent<Image>().preserveAspect = true;
            return rect;
        }

        private static RectTransform MakeImage(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private Text MakeText(string name, Transform parent, int size, TextAnchor alignment, Vector2 anchor, Vector2 box, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = box;
            rect.anchoredPosition = position;
            return text;
        }

        #endregion
    }
}
