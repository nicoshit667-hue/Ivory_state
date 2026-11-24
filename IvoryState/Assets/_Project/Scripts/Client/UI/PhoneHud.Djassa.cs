using System.Collections.Generic;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    public sealed partial class PhoneHud
    {
        private enum DjassaCategory
        {
            Vehicle = 0,
            House = 1,
            Food = 2,
            Medicine = 3,
            Other = 4
        }

        private sealed class DjassaCardView
        {
            public RectTransform Root;
            public Image Icon;
            public Text Title;
            public Text Subtitle;
            public Text Price;
            public Button ActionButton;
            public Image ActionBg;
            public Text ActionLabel;
        }

        private struct DjassaDisplayEntry
        {
            public bool IsStore;
            public bool IsMedicine;
            public int StoreIndex;
            public int AdId;
            public bool Mine;
        }

        private static readonly Color DjassaColor = new Color(0.10f, 0.55f, 0.45f, 1f);
        private static readonly Color DjassaTabIdle = new Color(1f, 1f, 1f, 0.11f);
        private static readonly Color DjassaStoreColor = new Color(0.14f, 0.52f, 0.72f, 1f);
        private const float DjassaScrollTopMargin = 190f;
        private const float DjassaScrollBottomSell = 192f;
        private const float DjassaScrollBottomStore = 10f;

        private DjassaCategory _djassaCategory = DjassaCategory.Vehicle;
        private readonly Button[] _djassaTabButtons = new Button[5];
        private readonly Image[] _djassaTabBackgrounds = new Image[5];
        private readonly Text[] _djassaTabLabels = new Text[5];

        private PhoneScrollView _djassaScroll;
        private RectTransform _djassaScrollRect;
        private readonly List<DjassaCardView> _djassaCards = new List<DjassaCardView>();
        private readonly List<DjassaDisplayEntry> _djassaEntries = new List<DjassaDisplayEntry>();

        private Text _djassaHint;
        private RectTransform _djassaSellPanel;
        private Text _djassaSellLabel;
        private InputField _djassaPrice;
        private Button _djassaPublishButton;
        private Button _djassaStateButton;

        // Dernier instantané reçu du serveur pour filtrage instantané par catégorie.
        private int[] _djassaAllIds = System.Array.Empty<int>();
        private string[] _djassaAllSellers = System.Array.Empty<string>();
        private string[] _djassaAllKinds = System.Array.Empty<string>();
        private int[] _djassaAllItems = System.Array.Empty<int>();
        private string[] _djassaAllExtras = System.Array.Empty<string>();
        private long[] _djassaAllPrices = System.Array.Empty<long>();
        private bool[] _djassaAllMine = System.Array.Empty<bool>();

        private readonly List<int> _sellableHouses = new List<int>();
        private int _djassaChoice;
        private int _djassaConfirmId;
        private float _djassaConfirmUntil;

        private void OpenDjassaPage()
        {
            _djassaConfirmId = 0;
            ApplyDjassaCategoryLayout();
            RefreshDjassaList();
            FillDjassaSell();
            _djassaScroll?.ResetToTopAnimated(true);
            if (!IsStoreCategory(_djassaCategory))
                _djassaHint.text = "Chargement des annonces…";
            PlayerMarket.Local?.Send(PlayerMarket.Request.List);
        }

        private void OnDjassaAds(int[] ids, string[] sellers, string[] kinds, int[] items, string[] extras, long[] prices, bool[] mine)
        {
            _djassaAllIds = ids ?? System.Array.Empty<int>();
            _djassaAllSellers = sellers ?? System.Array.Empty<string>();
            _djassaAllKinds = kinds ?? System.Array.Empty<string>();
            _djassaAllItems = items ?? System.Array.Empty<int>();
            _djassaAllExtras = extras ?? System.Array.Empty<string>();
            _djassaAllPrices = prices ?? System.Array.Empty<long>();
            _djassaAllMine = mine ?? System.Array.Empty<bool>();
            _djassaConfirmId = 0;
            RefreshDjassaList();
        }

        /// <summary>Marché black d'Adjamé : ouvre directement Djassa.</summary>
        private void OnMenuRequested(MenuInteractable menu)
        {
            if (!(menu is BlackMarketPoint))
                return;
            if (!_phone.gameObject.activeSelf)
                _phone.gameObject.SetActive(true);
            ShowPage(Page.Djassa);
        }

        private static bool IsStoreCategory(DjassaCategory category) =>
            category == DjassaCategory.Food || category == DjassaCategory.Medicine;

        private void SelectDjassaCategory(DjassaCategory category)
        {
            if (_djassaCategory == category)
                return;
            _djassaCategory = category;
            _djassaChoice = 0;
            _djassaConfirmId = 0;
            ApplyDjassaCategoryLayout();
            RefreshDjassaList();
            FillDjassaSell();
            _djassaScroll?.ResetToTopAnimated(false);
        }

        private void ApplyDjassaCategoryLayout()
        {
            for (int i = 0; i < _djassaTabBackgrounds.Length; i++)
            {
                bool active = i == (int)_djassaCategory;
                if (_djassaTabBackgrounds[i] != null)
                    _djassaTabBackgrounds[i].color = active ? DjassaColor : DjassaTabIdle;
                if (_djassaTabLabels[i] != null)
                    _djassaTabLabels[i].color = active ? Color.white : new Color(1f, 1f, 1f, 0.78f);
            }

            bool isStore = IsStoreCategory(_djassaCategory);
            if (_djassaSellPanel != null && _djassaSellPanel.gameObject.activeSelf == isStore)
                _djassaSellPanel.gameObject.SetActive(!isStore);

            if (_djassaScrollRect != null)
                _djassaScrollRect.offsetMin = new Vector2(0f, isStore ? DjassaScrollBottomStore : DjassaScrollBottomSell);

            bool isVehicle = _djassaCategory == DjassaCategory.Vehicle;
            if (_djassaStateButton != null && _djassaStateButton.gameObject.activeSelf != isVehicle)
                _djassaStateButton.gameObject.SetActive(isVehicle);

            if (_djassaPublishButton != null)
            {
                var pubRect = (RectTransform)_djassaPublishButton.transform;
                pubRect.sizeDelta = new Vector2(isVehicle ? 215f : 440f, 50f);
                pubRect.anchoredPosition = new Vector2(isVehicle ? -112f : 0f, 26f);
            }
        }

        private void RefreshDjassaList()
        {
            if (_djassaScroll == null)
                return;

            _djassaEntries.Clear();
            bool isStore = IsStoreCategory(_djassaCategory);
            int entryCount = 0;

            if (isStore)
            {
                bool medicine = _djassaCategory == DjassaCategory.Medicine;
                DjassaStoreCatalog.StoreItem[] storeItems = medicine ? DjassaStoreCatalog.Medicines : DjassaStoreCatalog.Foods;
                for (int i = 0; i < storeItems.Length; i++)
                {
                    DjassaStoreCatalog.StoreItem item = storeItems[i];
                    DjassaCardView card = GetOrCreateDjassaCard(entryCount);
                    card.Root.gameObject.SetActive(true);
                    SetDjassaCardIcon(card, HudIconLibrary.GetConsumableSprite(item.Name));
                    card.Title.text = item.Name;
                    card.Subtitle.text = item.StoreName + FormatStoreEffects(item.Satiety, item.Hydration, item.Health);
                    card.Subtitle.color = new Color(0.65f, 0.92f, 0.85f, 0.95f);
                    card.Price.text = Money.Format(item.Price);
                    card.ActionLabel.text = "Commander";
                    card.ActionBg.color = DjassaStoreColor;
                    _djassaEntries.Add(new DjassaDisplayEntry
                    {
                        IsStore = true,
                        IsMedicine = medicine,
                        StoreIndex = i
                    });
                    entryCount++;
                }
                _djassaHint.text = medicine
                    ? "Pharmacies du jeu · Utilisable depuis Mon sac pour te soigner."
                    : "Magasins et maquis du jeu · Livraison directe dans Mon sac.";
            }
            else
            {
                string expectedKind = CategoryToKind(_djassaCategory);
                for (int i = 0; i < _djassaAllIds.Length; i++)
                {
                    string kind = i < _djassaAllKinds.Length ? _djassaAllKinds[i] : string.Empty;
                    if (kind != expectedKind)
                        continue;

                    int item = i < _djassaAllItems.Length ? _djassaAllItems[i] : 0;
                    string seller = i < _djassaAllSellers.Length ? _djassaAllSellers[i] : "?";
                    string extra = i < _djassaAllExtras.Length ? _djassaAllExtras[i] : string.Empty;
                    long price = i < _djassaAllPrices.Length ? _djassaAllPrices[i] : 0;
                    bool mine = i < _djassaAllMine.Length && _djassaAllMine[i];

                    FormatAdText(kind, item, extra, out string title, out string detail);

                    DjassaCardView card = GetOrCreateDjassaCard(entryCount);
                    card.Root.gameObject.SetActive(true);
                    Sprite adSprite = kind == PlayerMarket.KindItem
                        ? HudIconLibrary.GetConsumableSprite(title)
                        : (kind == PlayerMarket.KindCar ? HudIconLibrary.AppGarage : HudIconLibrary.AppImmobilier);
                    SetDjassaCardIcon(card, adSprite);
                    card.Title.text = Escape(title);
                    card.Subtitle.text = "Vendu par " + Escape(seller) + detail;
                    card.Subtitle.color = new Color(1f, 1f, 1f, 0.72f);
                    card.Price.text = Money.Format(price);

                    bool confirming = !mine && _djassaConfirmId == _djassaAllIds[i] && Time.unscaledTime <= _djassaConfirmUntil;
                    card.ActionLabel.text = mine ? "Retirer" : (confirming ? "Confirmer ?" : "Acheter");
                    card.ActionBg.color = mine ? Red : DjassaColor;

                    _djassaEntries.Add(new DjassaDisplayEntry
                    {
                        IsStore = false,
                        AdId = _djassaAllIds[i],
                        Mine = mine
                    });
                    entryCount++;
                }

                _djassaHint.text = entryCount == 0
                    ? "Aucune annonce de joueur ici. Mets un bien en vente ci-dessous."
                    : "Annonces entre joueurs · Appuie 2 fois pour acheter.";
            }

            const float cardHeight = 88f;
            const float spacing = 8f;
            const float topPad = 8f;
            for (int i = 0; i < entryCount; i++)
                _djassaCards[i].Root.anchoredPosition = new Vector2(0f, -(topPad + i * (cardHeight + spacing)));
            for (int i = entryCount; i < _djassaCards.Count; i++)
                _djassaCards[i].Root.gameObject.SetActive(false);

            float totalHeight = entryCount == 0 ? 120f : topPad * 2f + entryCount * (cardHeight + spacing) - spacing;
            _djassaScroll.SetContentHeight(totalHeight);
        }

        private static void SetDjassaCardIcon(DjassaCardView card, Sprite sprite)
        {
            if (card.Icon == null)
                return;
            bool has = sprite != null;
            card.Icon.gameObject.SetActive(has);
            card.Icon.sprite = sprite;
        }

        private DjassaCardView GetOrCreateDjassaCard(int index)
        {
            while (_djassaCards.Count <= index)
            {
                int slot = _djassaCards.Count;
                RectTransform root = MakeImage("DjassaCard_" + slot, _djassaScroll.Content, Row,
                    new Vector2(0.5f, 1f), new Vector2(440f, 88f), Vector2.zero);

                RectTransform iconRect = MakeImage("Icone", root, Color.white,
                    new Vector2(0f, 0.5f), new Vector2(52f, 52f), new Vector2(10f, 0f));
                Image iconImg = iconRect.GetComponent<Image>();
                iconImg.type = Image.Type.Simple;
                iconImg.preserveAspect = true;

                Text title = MakeText("Titre", root, string.Empty, 16, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(240f, 38f), new Vector2(70f, -6f));
                title.horizontalOverflow = HorizontalWrapMode.Wrap;

                Text subtitle = MakeText("Detail", root, string.Empty, 13, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(240f, 24f), new Vector2(70f, -40f));
                subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;

                Text price = MakeText("Prix", root, string.Empty, 17, TextAnchor.LowerLeft,
                    new Vector2(0f, 0f), new Vector2(220f, 26f), new Vector2(70f, 5f));
                price.color = new Color(1f, 0.82f, 0.42f);

                Button action = MakeButton("Action", root, "Acheter", 16, DjassaColor,
                    new Vector2(1f, 0.5f), new Vector2(114f, 52f), new Vector2(-10f, 0f));
                int capturedIndex = slot;
                action.onClick.AddListener(() => OnDjassaCardAction(capturedIndex));

                _djassaCards.Add(new DjassaCardView
                {
                    Root = root,
                    Icon = iconImg,
                    Title = title,
                    Subtitle = subtitle,
                    Price = price,
                    ActionButton = action,
                    ActionBg = action.GetComponent<Image>(),
                    ActionLabel = action.GetComponentInChildren<Text>()
                });
            }
            return _djassaCards[index];
        }

        private void OnDjassaCardAction(int index)
        {
            if (index < 0 || index >= _djassaEntries.Count)
                return;
            DjassaDisplayEntry entry = _djassaEntries[index];
            PlayerMarket market = PlayerMarket.Local;
            if (market == null)
                return;

            if (entry.IsStore)
            {
                market.Send(entry.IsMedicine ? PlayerMarket.Request.BuyStoreMedicine : PlayerMarket.Request.BuyStoreFood,
                    entry.StoreIndex);
                return;
            }

            if (entry.Mine)
            {
                market.Send(PlayerMarket.Request.Cancel, entry.AdId);
                return;
            }

            if (_djassaConfirmId != entry.AdId || Time.unscaledTime > _djassaConfirmUntil)
            {
                _djassaConfirmId = entry.AdId;
                _djassaConfirmUntil = Time.unscaledTime + 4f;
                if (index < _djassaCards.Count)
                    _djassaCards[index].ActionLabel.text = "Confirmer ?";
                return;
            }

            _djassaConfirmId = 0;
            market.Send(PlayerMarket.Request.Buy, entry.AdId);
        }

        private static string CategoryToKind(DjassaCategory category)
        {
            switch (category)
            {
                case DjassaCategory.Vehicle: return PlayerMarket.KindCar;
                case DjassaCategory.House: return PlayerMarket.KindHouse;
                case DjassaCategory.Other: return PlayerMarket.KindItem;
                default: return string.Empty;
            }
        }

        private static string FormatStoreEffects(byte satiety, byte hydration, byte health)
        {
            string effects = string.Empty;
            if (health > 0) effects += "  •  Santé +" + health;
            if (satiety > 0) effects += "  •  Faim +" + satiety;
            if (hydration > 0) effects += "  •  Soif +" + hydration;
            return effects;
        }

        private static void FormatAdText(string kind, int item, string extra, out string title, out string detail)
        {
            if (kind == PlayerMarket.KindCar)
            {
                title = item > 0 && item < VehicleMotor.PaintNames.Length
                    ? VehicleMotor.PaintNames[item]
                    : "Véhicule";
                detail = item > 0 && item < PlayerGarage.Rules.CarPrices.Length
                    ? "  •  Neuf " + Money.Format(PlayerGarage.Rules.CarPrices[item])
                    : string.Empty;
                return;
            }
            if (kind == PlayerMarket.KindHouse)
            {
                if (PropertyCatalog.IsValid(item))
                {
                    var prop = PropertyCatalog.Get(item);
                    title = PropertyCatalog.KindName(prop.Kind) + " — " + PropertyCatalog.Entrances[prop.EntranceIndex].Name;
                    detail = string.Empty;
                }
                else
                {
                    title = "Maison";
                    detail = string.Empty;
                }
                return;
            }
            if (kind == PlayerMarket.KindItem)
            {
                PlayerMarket.TryUnpackItemExtra(extra, out string itemName, out byte satiety, out byte hydration, out byte health);
                title = itemName;
                detail = FormatStoreEffects(satiety, hydration, health);
                return;
            }
            title = "?";
            detail = string.Empty;
        }

        /// <summary>Met à jour le sélecteur de vente selon la catégorie active (Véhicule, Maison ou Autres).</summary>
        private void FillDjassaSell()
        {
            if (_djassaSellLabel == null || IsStoreCategory(_djassaCategory))
                return;

            if (_djassaCategory == DjassaCategory.Vehicle)
            {
                PlayerGarage garage = PlayerGarage.Local;
                int cars = garage != null ? garage.Cars.Count : 0;
                if (cars == 0)
                {
                    _djassaSellLabel.text = "Aucune voiture dans ton Garage.";
                    return;
                }
                _djassaChoice = (_djassaChoice % cars + cars) % cars;
                _djassaSellLabel.text = "À vendre : " + VehicleMotor.PaintNames[garage.Cars[_djassaChoice]] + "   ▸ changer";
                return;
            }

            if (_djassaCategory == DjassaCategory.House)
            {
                CollectSellableHouses(PlayerHome.Local);
                int count = _sellableHouses.Count;
                if (count == 0)
                {
                    _djassaSellLabel.text = "Aucune maison achetée à vendre.";
                    return;
                }
                _djassaChoice = (_djassaChoice % count + count) % count;
                _djassaSellLabel.text = "À vendre : " + PropertyCatalog.Get(_sellableHouses[_djassaChoice]).Label + "   ▸ changer";
                return;
            }

            PlayerInventory bag = PlayerInventory.Local;
            int bagCount = bag != null ? bag.Count : 0;
            if (bagCount == 0)
            {
                _djassaSellLabel.text = "Ton sac est vide (aucun objet à vendre).";
                return;
            }
            _djassaChoice = (_djassaChoice % bagCount + bagCount) % bagCount;
            InventoryItem bagItem = bag[_djassaChoice];
            _djassaSellLabel.text = "À vendre : " + bagItem.Name + " (×" + bagItem.Count + ")   ▸ changer";
        }

        /// <summary>Mes maisons achetées (vendables sur Djassa).</summary>
        private void CollectSellableHouses(PlayerHome home)
        {
            _sellableHouses.Clear();
            if (home == null)
                return;
            for (int slot = 0; slot < PlayerHome.MaxProperties; slot++)
            {
                int id = home.PropertyAt(slot);
                if (PropertyCatalog.IsValid(id) && home.OwnsPropertyAt(slot) &&
                    PropertyCatalog.IsForSale(PropertyCatalog.Get(id).Kind))
                    _sellableHouses.Add(id);
            }
        }

        private void PublishDjassa()
        {
            PlayerMarket market = PlayerMarket.Local;
            if (market == null || !long.TryParse(_djassaPrice.text, out long price))
            {
                _djassaHint.text = "Indique un prix en chiffres.";
                return;
            }

            if (_djassaCategory == DjassaCategory.Vehicle)
            {
                PlayerGarage garage = PlayerGarage.Local;
                int cars = garage != null ? garage.Cars.Count : 0;
                if (cars == 0)
                {
                    _djassaHint.text = "Aucune voiture à vendre.";
                    return;
                }
                int slot = (_djassaChoice % cars + cars) % cars;
                market.Send(PlayerMarket.Request.SellCar, slot, price);
            }
            else if (_djassaCategory == DjassaCategory.House)
            {
                CollectSellableHouses(PlayerHome.Local);
                if (_sellableHouses.Count == 0)
                {
                    _djassaHint.text = "Aucune maison achetée à vendre.";
                    return;
                }
                int idx = (_djassaChoice % _sellableHouses.Count + _sellableHouses.Count) % _sellableHouses.Count;
                market.Send(PlayerMarket.Request.SellHouse, _sellableHouses[idx], price);
            }
            else if (_djassaCategory == DjassaCategory.Other)
            {
                PlayerInventory bag = PlayerInventory.Local;
                int bagCount = bag != null ? bag.Count : 0;
                if (bagCount == 0)
                {
                    _djassaHint.text = "Ton sac est vide.";
                    return;
                }
                int slot = (_djassaChoice % bagCount + bagCount) % bagCount;
                market.Send(PlayerMarket.Request.SellItem, slot, price);
            }
            _djassaPrice.text = string.Empty;
        }

        private void SellToStateDjassa()
        {
            PlayerGarage garage = PlayerGarage.Local;
            if (_djassaCategory != DjassaCategory.Vehicle || garage == null || garage.Cars.Count == 0)
            {
                _djassaHint.text = "Sélectionne un véhicule dans ton Garage.";
                return;
            }
            int slot = (_djassaChoice % garage.Cars.Count + garage.Cars.Count) % garage.Cars.Count;
            PlayerMarket.Local?.Send(PlayerMarket.Request.SellCarToState, slot);
        }

        private void BuildDjassa(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Djassa, screen);
            page.GetComponent<Image>().color = new Color(0.05f, 0.11f, 0.10f, 1f);
            MakeHeader(page, "Djassa", DjassaColor);

            // 5 catégories demandées (2 rangées nettes sous l'en-tête).
            string[] labels = { "Véhicule", "Maison", "Nourriture & boisson", "Médicaments", "Autres" };
            float[] xs = { -154f, -22f, 131f, -112f, 112f };
            float[] ys = { -100f, -100f, -100f, -138f, -138f };
            float[] ws = { 132f, 118f, 176f, 216f, 216f };
            int[] sizes = { 14, 14, 13, 14, 14 };

            for (int i = 0; i < 5; i++)
            {
                DjassaCategory category = (DjassaCategory)i;
                Button tab = MakeButton("Cat_" + category, page, labels[i], sizes[i], DjassaTabIdle,
                    new Vector2(0.5f, 1f), new Vector2(ws[i], 34f), new Vector2(xs[i], ys[i]));
                tab.onClick.AddListener(() => SelectDjassaCategory(category));
                _djassaTabButtons[i] = tab;
                _djassaTabBackgrounds[i] = tab.GetComponent<Image>();
                _djassaTabLabels[i] = tab.GetComponentInChildren<Text>();
            }

            _djassaHint = MakeText("Info", page, string.Empty, 14, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(444f, 26f), new Vector2(0f, -162f));
            _djassaHint.color = new Color(1f, 1f, 1f, 0.78f);

            _djassaScroll = PhoneScrollView.Create(page, "DjassaScroll", DjassaScrollTopMargin, DjassaScrollBottomSell);
            _djassaScrollRect = (RectTransform)_djassaScroll.ScrollRect.transform;
            RegisterPageScroll(Page.Djassa, _djassaScroll);

            // Panneau de mise en vente (uniquement pour Véhicule, Maison, Autres).
            _djassaSellPanel = MakeImage("VentePanel", page, new Color(0.03f, 0.08f, 0.07f, 0.96f),
                new Vector2(0.5f, 0f), new Vector2(ScreenLogicalWidth, 184f), Vector2.zero);

            Button choose = MakeButton("Choisir", _djassaSellPanel, string.Empty, 15, new Color(1f, 1f, 1f, 0.10f),
                new Vector2(0.5f, 0f), new Vector2(440f, 42f), new Vector2(0f, 134f));
            _djassaSellLabel = choose.GetComponentInChildren<Text>();
            choose.onClick.AddListener(() => { _djassaChoice++; FillDjassaSell(); });

            RectTransform box = MakeImage("Prix", _djassaSellPanel, new Color(1f, 1f, 1f, 0.12f),
                new Vector2(0.5f, 0f), new Vector2(440f, 46f), new Vector2(0f, 82f));
            box.GetComponent<Image>().raycastTarget = true;
            Text text = MakeText("Texte", box, string.Empty, 20, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(416f, 42f), Vector2.zero);
            Text hint = MakeText("Indice", box, "Prix de vente en FCFA", 18, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(416f, 42f), Vector2.zero);
            hint.color = new Color(1f, 1f, 1f, 0.45f);
            _djassaPrice = box.gameObject.AddComponent<InputField>();
            _djassaPrice.targetGraphic = box.GetComponent<Image>();
            _djassaPrice.textComponent = text;
            _djassaPrice.placeholder = hint;
            _djassaPrice.contentType = InputField.ContentType.IntegerNumber;
            _djassaPrice.characterLimit = 9;

            _djassaPublishButton = MakeButton("Publier", _djassaSellPanel, "Publier l'annonce", 17, DjassaColor,
                new Vector2(0.5f, 0f), new Vector2(215f, 50f), new Vector2(-112f, 26f));
            _djassaPublishButton.onClick.AddListener(PublishDjassa);

            _djassaStateButton = MakeButton("Etat", _djassaSellPanel, "Vendre à l'État (½)", 16, Dark,
                new Vector2(0.5f, 0f), new Vector2(215f, 50f), new Vector2(112f, 26f));
            _djassaStateButton.onClick.AddListener(SellToStateDjassa);

            Text marketNote = MakeText("Marche", _djassaSellPanel,
                "Véhicule / Maison / Autres : ventes entre joueurs · Nourriture & Médicaments : magasins du jeu.",
                12, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(448f, 22f), new Vector2(0f, 2f));
            marketNote.color = new Color(1f, 1f, 1f, 0.55f);

            ApplyDjassaCategoryLayout();
        }
    }
}
