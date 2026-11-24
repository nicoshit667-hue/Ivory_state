using IvoryState.Client.Input;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Menu des points à choix : produits d'une boutique ou d'un étal du marché, destinations d'une
    /// gare de gbaka. Les boutiques de vêtements (<see cref="ClothingStorePoint"/>) ouvrent désormais
    /// l'Écran 4 (<c>InGameWardrobePanel</c>) via <see cref="UIManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChoiceMenuHud : MonoBehaviour
    {
        private const int Slots = 16;
        private static readonly Color Panel = new Color(0.07f, 0.07f, 0.09f, 0.94f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color Accent = new Color(0.95f, 0.62f, 0.12f, 1f);

        private Sprite _rounded;
        private Font _font;
        private RectTransform _panel;
        private Text _title;
        private Text _greeting;
        private Text _balance;
        private PhoneScrollView _scroll;
        private readonly Button[] _rows = new Button[Slots];
        private readonly Image[] _icons = new Image[Slots];
        private readonly Text[] _labels = new Text[Slots];
        private readonly Text[] _details = new Text[Slots];
        private readonly Text[] _prices = new Text[Slots];

        private Image _fade;
        private Text _fadeText;
        private float _fadeStart = -10f;

        private MenuInteractable _menu;
        private long _shownBalance = long.MinValue;

        private Button _keepButton;
        private Text _keepLabel;
        private bool _keep;

        public void Initialize(Sprite rounded, Font font)
        {
            _rounded = rounded;
            _font = font;
            Build();
        }

        private void OnEnable()
        {
            PlayerInteractor.LocalMenuRequested += Open;
            PlayerInteractor.LocalTravelStarted += OnTravel;
        }

        private void OnDisable()
        {
            PlayerInteractor.LocalMenuRequested -= Open;
            PlayerInteractor.LocalTravelStarted -= OnTravel;
            if (_panel != null)
                MobilePlayerControls.UnregisterBlockingRect(_panel);
        }

        private void Update()
        {
            UpdateFade();
            if (_menu == null)
                return;

            PlayerInteractor local = PlayerInteractor.Local;
            float range = _menu.Range + 1.5f;
            if (local == null || _menu == null ||
                (local.transform.position - _menu.transform.position).sqrMagnitude > range * range)
            {
                Close();
                return;
            }

            long balance = local.Economy != null ? local.Economy.Balance : 0;
            if (balance != _shownBalance)
            {
                _shownBalance = balance;
                _balance.text = "Ton solde : " + Money.Format(balance);
            }
        }

        private void Open(MenuInteractable menu)
        {
            if (menu is BlackMarketPoint)
                return;
            if (menu is ClothingStorePoint clothingStore && UIManager.Instance != null)
            {
                UIManager.Instance.OpenInGameWardrobe(clothingStore);
                return;
            }
            if (UIManager.Instance != null && UIManager.Instance.IsMenuOpen)
                return;

            if (transform.parent != null)
                transform.parent.SetAsLastSibling();
            transform.SetAsLastSibling();

            _menu = menu;
            _shownBalance = long.MinValue;
            _title.text = menu.MenuTitle;
            _greeting.text = menu.MenuGreeting;
            bool supportsKeep = menu is StorePoint || menu is CommercePoint;
            _keepButton.gameObject.SetActive(supportsKeep);
            RefreshKeep();

            int count = Mathf.Min(menu.OptionCount, Slots);
            for (int i = 0; i < Slots; i++)
            {
                bool used = i < count;
                _rows[i].gameObject.SetActive(used);
                if (!used)
                    continue;
                _labels[i].text = menu.OptionLabel(i);
                string detail = menu.OptionDetail(i);
                _details[i].text = detail ?? string.Empty;
                long price = menu.OptionPrice(i);
                _prices[i].text = price > 0 ? Money.Format(price) : "Gratuit";

                Sprite icon = ResolveOptionSprite(menu, i);
                bool showIcon = icon != null && supportsKeep;
                if (_icons[i] != null)
                {
                    _icons[i].gameObject.SetActive(showIcon);
                    _icons[i].sprite = icon;
                }
                float textX = showIcon ? 78f : 22f;
                float textW = showIcon ? 410f : 470f;
                _labels[i].rectTransform.anchoredPosition = new Vector2(textX, 14f);
                _labels[i].rectTransform.sizeDelta = new Vector2(textW, 36f);
                _details[i].rectTransform.anchoredPosition = new Vector2(textX, -16f);
                _details[i].rectTransform.sizeDelta = new Vector2(textW, 28f);
            }

            int visibleRows = Mathf.Clamp(count, 1, 6);
            float rowAreaHeight = visibleRows * 84f + 8f;
            _panel.sizeDelta = new Vector2(_panel.sizeDelta.x, 212f + rowAreaHeight);
            _scroll?.SetContentHeight(Mathf.Max(84f, count * 84f + 8f));
            _scroll?.ResetToTopAnimated(false);
            _panel.gameObject.SetActive(true);
            MobilePlayerControls.RegisterBlockingRect(_panel);
        }

        private static Sprite ResolveOptionSprite(MenuInteractable menu, int index)
        {
            if (menu is CommercePoint commerce)
                return HudIconLibrary.GetConsumableSprite(commerce.OptionIconKey(index));
            if (menu is StorePoint store)
                return HudIconLibrary.GetConsumableSprite(store.OptionIconKey(index));
            return null;
        }

        public void CloseMenu() => Close();

        private void Close()
        {
            _menu = null;
            if (_panel != null)
            {
                _panel.gameObject.SetActive(false);
                MobilePlayerControls.UnregisterBlockingRect(_panel);
            }
        }

        private void Choose(int index)
        {
            bool supportsKeep = _menu is StorePoint || _menu is CommercePoint;
            if (_menu != null && PlayerInteractor.Local != null)
                PlayerInteractor.Local.RequestChoice(_menu, index, _keep && supportsKeep);
            if (_menu is GbakaStationPoint)
                Close();
        }

        private void OnTravel(string destination)
        {
            _fadeText.text = "En route vers " + destination + "…";
            _fadeStart = Time.unscaledTime;
            _fade.gameObject.SetActive(true);
        }

        private void UpdateFade()
        {
            if (!_fade.gameObject.activeSelf)
                return;
            float elapsed = Time.unscaledTime - _fadeStart;
            if (elapsed > 2.2f)
            {
                _fade.gameObject.SetActive(false);
                return;
            }
            float alpha = elapsed < 0.25f ? elapsed / 0.25f : elapsed < 1.3f ? 1f : 1f - (elapsed - 1.3f) / 0.9f;
            _fade.color = new Color(0.03f, 0.03f, 0.04f, alpha);
            _fadeText.color = new Color(1f, 0.9f, 0.65f, alpha);
        }

        private void RefreshKeep()
        {
            _keepLabel.text = _keep ? "Achat : mettre dans mon sac   ▸ changer" : "Achat : consommer tout de suite   ▸ changer";
            _keepButton.GetComponent<Image>().color = _keep ? new Color(0.80f, 0.45f, 0.15f, 0.85f) : new Color(0.20f, 0.50f, 0.30f, 0.85f);
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            _panel = MakeImage("Menu", root, Panel, new Vector2(0.5f, 0.5f), new Vector2(760f, 640f), Vector2.zero);
            _panel.GetComponent<Image>().raycastTarget = true;

            RectTransform header = MakeImage("EnTete", _panel, Accent, new Vector2(0.5f, 1f), new Vector2(760f, 76f), Vector2.zero);
            _title = MakeText("Titre", header, string.Empty, 32, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(640f, 70f), new Vector2(24f, 0f));
            _title.color = new Color(0.08f, 0.05f, 0.02f, 1f);
            Button close = MakeButton("Fermer", header, "×", 42, new Color(0f, 0f, 0f, 0.18f), new Vector2(1f, 0.5f), new Vector2(64f, 60f), new Vector2(-10f, 0f));
            close.onClick.AddListener(Close);

            _greeting = MakeText("Accueil", _panel, string.Empty, 20, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(710f, 56f), new Vector2(0f, -82f));
            _greeting.color = new Color(1f, 0.92f, 0.72f, 1f);
            _greeting.horizontalOverflow = HorizontalWrapMode.Wrap;
            _balance = MakeText("Solde", _panel, string.Empty, 20, TextAnchor.MiddleRight, new Vector2(0.5f, 1f), new Vector2(710f, 34f), new Vector2(0f, -142f));
            _balance.color = new Color(0.6f, 0.95f, 0.7f, 1f);

            _keepButton = MakeButton("Mode", _panel, string.Empty, 20, RowColor, new Vector2(0.5f, 1f), new Vector2(460f, 44f), new Vector2(-125f, -142f));
            _keepLabel = _keepButton.GetComponentInChildren<Text>();
            _keepButton.onClick.AddListener(() => { _keep = !_keep; RefreshKeep(); });

            _scroll = PhoneScrollView.Create(_panel, "MenuScroll", 192f, 14f);
            for (int i = 0; i < Slots; i++)
            {
                int index = i;
                Button row = MakeButton("Option_" + i, _scroll.Content, string.Empty, 24, RowColor, new Vector2(0.5f, 1f), new Vector2(710f, 76f), new Vector2(0f, -6f - i * 84f));
                RectTransform iconRect = MakeImage("Icone", row.transform, Color.white, new Vector2(0f, 0.5f), new Vector2(52f, 52f), new Vector2(14f, 0f));
                Image iconImg = iconRect.GetComponent<Image>();
                iconImg.sprite = null;
                iconImg.type = Image.Type.Simple;
                iconImg.preserveAspect = true;
                iconRect.gameObject.SetActive(false);
                _icons[i] = iconImg;

                _labels[i] = MakeText("Nom", row.transform, string.Empty, 25, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(470f, 36f), new Vector2(22f, 14f));
                _details[i] = MakeText("Detail", row.transform, string.Empty, 18, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(470f, 28f), new Vector2(22f, -16f));
                _details[i].color = new Color(1f, 1f, 1f, 0.65f);
                _prices[i] = MakeText("Prix", row.transform, string.Empty, 24, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(200f, 60f), new Vector2(-20f, 0f));
                _prices[i].color = new Color(1f, 0.80f, 0.35f, 1f);
                row.onClick.AddListener(() => Choose(index));
                _rows[i] = row;
            }
            _panel.gameObject.SetActive(false);

            RectTransform fadeRect = MakeImage("Voyage", root, new Color(0f, 0f, 0f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.offsetMin = fadeRect.offsetMax = Vector2.zero;
            _fade = fadeRect.GetComponent<Image>();
            _fadeText = MakeText("Destination", fadeRect, string.Empty, 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(1200f, 120f), Vector2.zero);
            fadeRect.gameObject.SetActive(false);
        }

        private RectTransform MakeImage(string name, Transform parent, Color color, Vector2 anchor, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = _rounded;
            image.type = _rounded != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private Text MakeText(string name, Transform parent, string value, int size, TextAnchor alignment, Vector2 anchor, Vector2 box, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = value;
            text.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = box;
            rect.anchoredPosition = position;
            return text;
        }

        private Button MakeButton(string name, Transform parent, string label, int size, Color color, Vector2 anchor, Vector2 box, Vector2 position)
        {
            RectTransform rect = MakeImage(name, parent, color, anchor, box, position);
            Image image = rect.GetComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (!string.IsNullOrEmpty(label))
            {
                Text text = MakeText("Label", rect, label, size, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), box, Vector2.zero);
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            }
            return button;
        }
    }
}
