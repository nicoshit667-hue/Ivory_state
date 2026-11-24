using System;
using System.Collections.Generic;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    public sealed partial class PhoneHud
    {
        private enum CommerceFilter
        {
            All = 0,
            Maquis = 1,
            Restaurant = 2,
            FastFood = 3,
            Boutique = 4,
            Pharmacie = 5,
            Mine = 6,
        }

        private sealed class CommerceCardView
        {
            public RectTransform Root;
            public Text Title;
            public Text Subtitle;
            public Text OwnerInfo;
            public readonly Image[] PreviewIcons = new Image[5];
            public Button GpsButton;
            public Button TaxiButton;
            public Button PrimaryButton;
            public Image PrimaryBg;
            public Text PrimaryLabel;
            public Button SecondaryButton;
            public Text SecondaryLabel;
            public int CommerceId;
            public Vector3 Position;
        }

        private static readonly Color CommerceHeaderColor = new Color(0.76f, 0.34f, 0.14f, 1f);
        private static readonly Color CommerceTabIdle = new Color(1f, 1f, 1f, 0.11f);

        private CommerceFilter _commerceFilter = CommerceFilter.All;
        private readonly Image[] _commerceTabBg = new Image[7];
        private readonly Text[] _commerceTabText = new Text[7];
        private Text _commerceVaultText;
        private Button _commerceCollectBtn;
        private InputField _commerceSearchInput;
        private PhoneScrollView _commerceScroll;
        private readonly List<CommerceCardView> _commerceCards = new List<CommerceCardView>();

        private void OpenCommercesPage()
        {
            RefreshCommerceTabsVisual();
            RefreshCommercesList();
            PlayerEconomy.Local?.RequestCommerceDirectory();
            _commerceScroll?.ResetToTopAnimated(true);
        }

        private void OnCommercesUpdated()
        {
            if (_phone != null && _phone.gameObject.activeSelf && _page == Page.Commerces)
                RefreshCommercesList();
        }

        private void SelectCommerceFilter(CommerceFilter filter)
        {
            if (_commerceFilter == filter)
                return;
            _commerceFilter = filter;
            RefreshCommerceTabsVisual();
            RefreshCommercesList();
            _commerceScroll?.ResetToTopAnimated(false);
        }

        private void RefreshCommerceTabsVisual()
        {
            for (int i = 0; i < _commerceTabBg.Length; i++)
            {
                bool active = i == (int)_commerceFilter;
                if (_commerceTabBg[i] != null)
                    _commerceTabBg[i].color = active ? CommerceHeaderColor : CommerceTabIdle;
                if (_commerceTabText[i] != null)
                    _commerceTabText[i].color = active ? Color.white : new Color(1f, 1f, 1f, 0.78f);
            }
        }

        private void RefreshCommercesList()
        {
            if (_commerceScroll == null)
                return;

            PlayerEconomy economy = PlayerEconomy.Local;
            List<int> ownedIds = economy != null ? CommerceCatalog.ParseIds(economy.OwnedCommercesCsv) : new List<int>();
            long vault = economy != null ? economy.CommerceVault : 0;

            if (_commerceVaultText != null)
                _commerceVaultText.text = $"Mes commerces : {ownedIds.Count}/{CommerceCatalog.MaxOwnedCommerces}   •   Caisse : {Money.Format(vault)}";
            if (_commerceCollectBtn != null)
                _commerceCollectBtn.interactable = vault > 0;

            string query = _commerceSearchInput != null ? (_commerceSearchInput.text ?? string.Empty).Trim() : string.Empty;
            bool hasQuery = query.Length > 0;
            Vector3 playerPos = LocalPlayerPosition();

            var filtered = new List<CommerceDefinition>();
            for (int i = 0; i < CommerceCatalog.All.Length; i++)
            {
                CommerceDefinition def = CommerceCatalog.All[i];
                bool isMine = ownedIds.Contains(def.Id) ||
                              (CommerceRegistryClient.TryGet(def.Id, out var reg) && reg.IsMine);

                if (_commerceFilter == CommerceFilter.Mine && !isMine)
                    continue;
                if (_commerceFilter != CommerceFilter.All && _commerceFilter != CommerceFilter.Mine &&
                    (int)def.Kind != ((int)_commerceFilter - 1))
                    continue;

                if (hasQuery && !MatchesCommerceSearch(def, query))
                    continue;

                filtered.Add(def);
            }

            int count = filtered.Count;
            EnsureCommerceCardPool(count);

            const float cardHeight = 148f;
            const float spacing = 10f;
            const float topPad = 8f;

            for (int i = 0; i < _commerceCards.Count; i++)
            {
                CommerceCardView card = _commerceCards[i];
                bool active = i < count;
                card.Root.gameObject.SetActive(active);
                if (!active)
                    continue;

                CommerceDefinition def = filtered[i];
                Vector3 pos = CommerceCatalog.GetWorldPosition(def);
                float dist = TaxiRideModel.HorizontalDistance(playerPos, pos);
                bool hasReg = CommerceRegistryClient.TryGet(def.Id, out CommerceRegistryClient.Entry entry);
                bool isMine = ownedIds.Contains(def.Id) || (hasReg && entry.IsMine);
                bool isTaken = hasReg && !string.IsNullOrEmpty(entry.OwnerName);
                bool isOpen = !hasReg || entry.IsOpen;

                card.CommerceId = def.Id;
                card.Position = pos;
                card.Root.anchoredPosition = new Vector2(0f, -(topPad + i * (cardHeight + spacing)));

                string linked = CommerceCatalog.DescribeLinkedProperty(def.LinkedPropertyId);
                card.Title.text = def.Name;
                card.Subtitle.text = $"{CommerceCatalog.KindLabel(def.Kind)} · {def.District} · {TaxiRideModel.FormatDistance(dist)}" +
                                     (string.IsNullOrEmpty(linked) ? string.Empty : "\nLié au bien : " + linked);

                if (isMine)
                {
                    card.OwnerInfo.text = (isOpen ? "<color=#7CFFA0>● Ouvert (Votre commerce)</color>" : "<color=#FF9966>● Fermé (Votre commerce)</color>") +
                                          $"  •  Marge {def.CommissionPercent}%";
                    card.PrimaryButton.gameObject.SetActive(true);
                    card.PrimaryButton.interactable = true;
                    card.PrimaryBg.color = isOpen ? new Color(0.80f, 0.45f, 0.12f, 1f) : Green;
                    card.PrimaryLabel.text = isOpen ? "Fermer" : "Ouvrir";
                    card.SecondaryButton.gameObject.SetActive(true);
                    card.SecondaryLabel.text = "Vendre";
                }
                else if (isTaken)
                {
                    card.OwnerInfo.text = $"Géré par <color=#FFD97D>{Escape(entry.OwnerName)}</color> · " +
                                          (isOpen ? "<color=#7CFFA0>Ouvert</color>" : "<color=#FF8878>Fermé</color>");
                    card.PrimaryButton.gameObject.SetActive(false);
                    card.SecondaryButton.gameObject.SetActive(false);
                }
                else
                {
                    card.OwnerInfo.text = $"<color=#8BE0FF>Disponible à l'achat : {Money.Format(def.PurchasePrice)}</color>  •  {def.CommissionPercent}% / vente";
                    card.PrimaryButton.gameObject.SetActive(true);
                    card.PrimaryButton.interactable = ownedIds.Count < CommerceCatalog.MaxOwnedCommerces;
                    card.PrimaryBg.color = CommerceHeaderColor;
                    card.PrimaryLabel.text = "Acheter";
                    card.SecondaryButton.gameObject.SetActive(false);
                }

                for (int k = 0; k < card.PreviewIcons.Length; k++)
                {
                    Image icon = card.PreviewIcons[k];
                    bool show = k < def.ConsumableIds.Length;
                    icon.gameObject.SetActive(show);
                    if (show)
                        icon.sprite = HudIconLibrary.GetConsumableSprite(def.ConsumableIds[k]);
                }
            }

            float totalHeight = count == 0 ? 180f : topPad * 2f + count * (cardHeight + spacing) - spacing;
            _commerceScroll.SetContentHeight(totalHeight);
        }

        private static bool MatchesCommerceSearch(CommerceDefinition def, string query)
        {
            if (def.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                def.District.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                CommerceCatalog.KindLabel(def.Kind).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            for (int i = 0; i < def.ConsumableIds.Length; i++)
            {
                if (ConsumableCatalog.TryGetById(def.ConsumableIds[i], out ConsumableDefinition item) &&
                    item.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private void EnsureCommerceCardPool(int needed)
        {
            while (_commerceCards.Count < needed)
            {
                RectTransform root = MakeImage("CommerceCard_" + _commerceCards.Count, _commerceScroll.Content, Row,
                    new Vector2(0.5f, 1f), new Vector2(442f, 148f), Vector2.zero);

                Text title = MakeText("Nom", root, string.Empty, 17, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(418f, 24f), new Vector2(12f, -8f));
                title.fontStyle = FontStyle.Bold;

                Text sub = MakeText("Lieu", root, string.Empty, 13, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(418f, 34f), new Vector2(12f, -32f));
                sub.color = new Color(1f, 1f, 1f, 0.72f);
                sub.horizontalOverflow = HorizontalWrapMode.Wrap;

                Text owner = MakeText("Etat", root, string.Empty, 13, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(418f, 22f), new Vector2(12f, -68f));
                owner.supportRichText = true;

                var view = new CommerceCardView
                {
                    Root = root,
                    Title = title,
                    Subtitle = sub,
                    OwnerInfo = owner,
                };

                for (int k = 0; k < view.PreviewIcons.Length; k++)
                {
                    RectTransform ic = MakeImage("Prod_" + k, root, Color.white,
                        new Vector2(0f, 0f), new Vector2(28f, 28f), new Vector2(12f + k * 32f, 12f));
                    Image img = ic.GetComponent<Image>();
                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                    view.PreviewIcons[k] = img;
                }

                view.GpsButton = MakeButton("Gps", root, "GPS", 13, Dark,
                    new Vector2(1f, 0f), new Vector2(56f, 34f), new Vector2(-194f, 10f));
                view.GpsButton.onClick.AddListener(() =>
                {
                    MapHud.SuggestWaypoint(view.Position);
                    _phone.gameObject.SetActive(false);
                });

                view.TaxiButton = MakeButton("Taxi", root, "Taxi", 13, TaxiActionColor,
                    new Vector2(1f, 0f), new Vector2(60f, 34f), new Vector2(-128f, 10f));
                view.TaxiButton.GetComponentInChildren<Text>().color = new Color(0.08f, 0.06f, 0.02f, 1f);
                view.TaxiButton.onClick.AddListener(() =>
                {
                    if (PlayerServices.Local != null && PlayerServices.Local.Taxi == PlayerServices.TaxiState.None &&
                        TaxiRideModel.HorizontalDistance(LocalPlayerPosition(), view.Position) >= TaxiRideModel.MinCallDistance)
                    {
                        PlayerServices.Local.CallTaxi(view.Position);
                    }
                    ShowPage(Page.Taxi);
                });

                view.PrimaryButton = MakeButton("Action", root, "Acheter", 13, CommerceHeaderColor,
                    new Vector2(1f, 0f), new Vector2(108f, 34f), new Vector2(-12f, 10f));
                view.PrimaryBg = view.PrimaryButton.GetComponent<Image>();
                view.PrimaryLabel = view.PrimaryButton.GetComponentInChildren<Text>();
                view.PrimaryButton.onClick.AddListener(() => OnCommercePrimaryAction(view.CommerceId));

                view.SecondaryButton = MakeButton("Vendre", root, "Vendre", 12, Red,
                    new Vector2(1f, 1f), new Vector2(72f, 26f), new Vector2(-10f, -8f));
                view.SecondaryLabel = view.SecondaryButton.GetComponentInChildren<Text>();
                view.SecondaryButton.onClick.AddListener(() => PlayerEconomy.Local?.RequestSellCommerce(view.CommerceId));

                _commerceCards.Add(view);
            }
        }

        private static void OnCommercePrimaryAction(int commerceId)
        {
            PlayerEconomy economy = PlayerEconomy.Local;
            if (economy == null)
                return;
            List<int> owned = CommerceCatalog.ParseIds(economy.OwnedCommercesCsv);
            if (owned.Contains(commerceId))
                economy.RequestToggleCommerceOpen(commerceId);
            else
                economy.RequestBuyCommerce(commerceId);
        }

        private void BuildCommerces(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Commerces, screen);
            page.GetComponent<Image>().color = new Color(0.13f, 0.09f, 0.07f, 1f);
            MakeHeader(page, "Commerces", CommerceHeaderColor);

            RectTransform vaultCard = MakeImage("CaisseCommerces", page, Row,
                new Vector2(0.5f, 1f), new Vector2(444f, 48f), new Vector2(0f, -84f));
            _commerceVaultText = MakeText("Info", vaultCard, "Mes commerces : 0/4   •   Caisse : 0 FCFA", 14, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(316f, 40f), new Vector2(12f, 0f));
            _commerceCollectBtn = MakeButton("Encaisser", vaultCard, "Encaisser", 13, Green,
                new Vector2(1f, 0.5f), new Vector2(102f, 34f), new Vector2(-10f, 0f));
            _commerceCollectBtn.onClick.AddListener(() => PlayerEconomy.Local?.RequestCollectCommerceVault());

            RectTransform searchBox = MakeImage("RechercheCommerces", page, new Color(1f, 1f, 1f, 0.11f),
                new Vector2(0.5f, 1f), new Vector2(444f, 38f), new Vector2(0f, -138f));
            searchBox.GetComponent<Image>().raycastTarget = true;
            Text searchText = MakeText("Texte", searchBox, string.Empty, 16, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(420f, 34f), Vector2.zero);
            Text searchPh = MakeText("Indice", searchBox, "Rechercher un maquis, resto, plat…", 15, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(420f, 34f), Vector2.zero);
            searchPh.color = new Color(1f, 1f, 1f, 0.45f);
            _commerceSearchInput = searchBox.gameObject.AddComponent<InputField>();
            _commerceSearchInput.targetGraphic = searchBox.GetComponent<Image>();
            _commerceSearchInput.textComponent = searchText;
            _commerceSearchInput.placeholder = searchPh;
            _commerceSearchInput.characterLimit = 36;
            _commerceSearchInput.onValueChanged.AddListener(_ => RefreshCommercesList());

            string[] labels = { "Tous", "Maquis", "Restos", "Fast-Food", "Boutiques", "Pharmacies", "Mes biens" };
            float[] xs = { -164f, -55f, 54f, 163f, -148f, 0f, 148f };
            float[] ys = { -184f, -184f, -184f, -184f, -220f, -220f, -220f };
            float[] ws = { 104f, 104f, 104f, 104f, 142f, 142f, 142f };

            for (int i = 0; i < labels.Length; i++)
            {
                CommerceFilter filter = (CommerceFilter)i;
                Button tab = MakeButton("Tab_" + filter, page, labels[i], 13, CommerceTabIdle,
                    new Vector2(0.5f, 1f), new Vector2(ws[i], 30f), new Vector2(xs[i], ys[i]));
                tab.onClick.AddListener(() => SelectCommerceFilter(filter));
                _commerceTabBg[i] = tab.GetComponent<Image>();
                _commerceTabText[i] = tab.GetComponentInChildren<Text>();
            }

            _commerceScroll = PhoneScrollView.Create(page, "CommercesScroll", 258f, 6f);
            RegisterPageScroll(Page.Commerces, _commerceScroll);
            RefreshCommerceTabsVisual();
        }
    }
}
