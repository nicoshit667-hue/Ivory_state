using System;
using System.Collections.Generic;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    public sealed partial class PhoneHud
    {
        private sealed class TaxiCardView
        {
            public RectTransform Root;
            public Text Title;
            public Text Subtitle;
            public Button OrderButton;
            public Image OrderBg;
            public Text OrderLabel;
        }

        private static readonly Color TaxiHeaderColor = new Color(0.84f, 0.56f, 0.05f, 1f);
        private static readonly Color TaxiActionColor = new Color(0.88f, 0.60f, 0.06f, 1f);
        private static readonly Color TaxiDisabledColor = new Color(1f, 1f, 1f, 0.14f);

        private TaxiCategory _taxiCategory = TaxiCategory.All;
        private readonly Image[] _taxiTabBg = new Image[5];
        private readonly Text[] _taxiTabText = new Text[5];

        private Text _taxiPageStatus;
        private Text _taxiPageDetail;
        private Button _taxiPageCancelButton;
        private Text _taxiWaypointLabel;
        private Button _taxiWaypointGoButton;
        private InputField _taxiSearchInput;
        private PhoneScrollView _taxiScroll;

        private readonly List<TaxiDestination> _taxiDestinations = new List<TaxiDestination>();
        private readonly List<TaxiDestination> _taxiVisible = new List<TaxiDestination>();
        private readonly List<TaxiCardView> _taxiCards = new List<TaxiCardView>();
        private PlayerServices.TaxiState _lastTaxiPageState;
        private float _nextTaxiStatusRefresh;

        private void OpenTaxiPage()
        {
            _nextTaxiStatusRefresh = 0f;
            RefreshTaxiTabsVisual();
            RefreshTaxiPageStatus();
            RefreshTaxiDestinations();
            _taxiScroll?.ResetToTopAnimated(true);
        }

        private void OnWaypointChangedForTaxi()
        {
            if (_phone != null && _phone.gameObject.activeSelf && _page == Page.Taxi)
            {
                RefreshTaxiPageStatus();
                RefreshTaxiDestinations();
            }
        }

        private void SelectTaxiCategory(TaxiCategory category)
        {
            if (_taxiCategory == category)
                return;
            _taxiCategory = category;
            RefreshTaxiTabsVisual();
            ApplyTaxiFilter();
            _taxiScroll?.ResetToTopAnimated(false);
        }

        private void RefreshTaxiTabsVisual()
        {
            for (int i = 0; i < _taxiTabBg.Length; i++)
            {
                bool active = i == (int)_taxiCategory;
                if (_taxiTabBg[i] != null)
                    _taxiTabBg[i].color = active ? TaxiActionColor : TaxiDisabledColor;
                if (_taxiTabText[i] != null)
                    _taxiTabText[i].color = active ? new Color(0.08f, 0.06f, 0.02f, 1f) : new Color(1f, 1f, 1f, 0.85f);
            }
        }

        private void RefreshTaxiPageStatus()
        {
            if (_taxiPageStatus == null)
                return;

            PlayerServices services = PlayerServices.Local;
            Vector3 origin = LocalPlayerPosition();
            TaxiTrackingSnapshot tracking = TaxiRideModel.BuildTrackingSnapshot(services, origin);

            bool stateChanged = tracking.State != _lastTaxiPageState;
            _lastTaxiPageState = tracking.State;

            if (_taxiPageCancelButton != null && _taxiPageCancelButton.gameObject.activeSelf != tracking.CanCancel)
                _taxiPageCancelButton.gameObject.SetActive(tracking.CanCancel);

            _taxiPageStatus.text = tracking.StatusTitle;
            _taxiPageStatus.color = tracking.AccentColor;
            if (_taxiPageDetail != null)
                _taxiPageDetail.text = tracking.StatusDetail;

            if (_taxiWaypointLabel != null && _taxiWaypointGoButton != null)
            {
                if (!MapHud.HasWaypoint)
                {
                    _taxiWaypointLabel.text = "Repère carte : aucun point marqué.";
                    _taxiWaypointGoButton.gameObject.SetActive(false);
                }
                else
                {
                    float dist = TaxiRideModel.HorizontalDistance(origin, MapHud.Waypoint);
                    long fare = PlayerServices.Catalog.TaxiFare(origin, MapHud.Waypoint);
                    _taxiWaypointLabel.text = "Repère carte : " + TaxiRideModel.FormatDistance(dist) + "  •  " + Money.Format(fare);
                    _taxiWaypointGoButton.gameObject.SetActive(true);
                    bool canGo = tracking.State == PlayerServices.TaxiState.None && dist >= TaxiRideModel.MinCallDistance;
                    _taxiWaypointGoButton.interactable = canGo;
                    Image bg = _taxiWaypointGoButton.GetComponent<Image>();
                    if (bg != null)
                        bg.color = canGo ? TaxiActionColor : TaxiDisabledColor;
                }
            }

            if (stateChanged && _taxiDestinations.Count > 0)
                ApplyTaxiFilter();
        }

        private void RefreshTaxiDestinations()
        {
            TaxiRideModel.CollectDestinations(LocalPlayerPosition(), _taxiDestinations);
            ApplyTaxiFilter();
        }

        private void ApplyTaxiFilter()
        {
            if (_taxiScroll == null)
                return;

            string search = _taxiSearchInput != null ? _taxiSearchInput.text.Trim() : string.Empty;
            bool hasSearch = search.Length > 0;
            Vector3 origin = LocalPlayerPosition();
            PlayerServices services = PlayerServices.Local;
            bool busy = services != null && services.Taxi != PlayerServices.TaxiState.None;

            _taxiVisible.Clear();
            int cardIndex = 0;

            for (int i = 0; i < _taxiDestinations.Count; i++)
            {
                TaxiDestination dest = _taxiDestinations[i];
                if (_taxiCategory != TaxiCategory.All && dest.Category != _taxiCategory)
                    continue;
                if (hasSearch &&
                    dest.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    dest.Badge.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                TaxiCardView card = GetOrCreateTaxiCard(cardIndex);
                card.Root.gameObject.SetActive(true);
                float dist = TaxiRideModel.HorizontalDistance(origin, dest.Position);
                long fare = PlayerServices.Catalog.TaxiFare(origin, dest.Position);

                card.Title.text = dest.Name;
                card.Subtitle.text = dest.Badge + "  •  " + TaxiRideModel.FormatDistance(dist) + "  •  " + Money.Format(fare);

                bool tooClose = dist < TaxiRideModel.MinCallDistance;
                bool canOrder = !busy && !tooClose;
                card.OrderButton.interactable = canOrder;
                card.OrderBg.color = canOrder ? TaxiActionColor : TaxiDisabledColor;
                card.OrderLabel.text = tooClose ? "< 30 m" : (busy ? "En cours" : "Aller");
                card.OrderLabel.color = canOrder ? new Color(0.08f, 0.06f, 0.02f, 1f) : new Color(1f, 1f, 1f, 0.55f);

                _taxiVisible.Add(dest);
                cardIndex++;
            }

            const float cardHeight = 72f;
            const float spacing = 8f;
            const float topPad = 8f;
            for (int i = 0; i < cardIndex; i++)
                _taxiCards[i].Root.anchoredPosition = new Vector2(0f, -(topPad + i * (cardHeight + spacing)));
            for (int i = cardIndex; i < _taxiCards.Count; i++)
                _taxiCards[i].Root.gameObject.SetActive(false);

            float totalHeight = cardIndex == 0 ? 120f : topPad * 2f + cardIndex * (cardHeight + spacing) - spacing;
            _taxiScroll.SetContentHeight(totalHeight);
        }

        private TaxiCardView GetOrCreateTaxiCard(int index)
        {
            while (_taxiCards.Count <= index)
            {
                int slot = _taxiCards.Count;
                RectTransform root = MakeImage("TaxiCard_" + slot, _taxiScroll.Content, Row,
                    new Vector2(0.5f, 1f), new Vector2(442f, 72f), Vector2.zero);

                Text title = MakeText("Lieu", root, string.Empty, 16, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(316f, 34f), new Vector2(12f, -6f));
                title.horizontalOverflow = HorizontalWrapMode.Wrap;

                Text sub = MakeText("Tarif", root, string.Empty, 14, TextAnchor.LowerLeft,
                    new Vector2(0f, 0f), new Vector2(316f, 24f), new Vector2(12f, 6f));
                sub.color = new Color(1f, 0.85f, 0.45f, 0.95f);

                Button btn = MakeButton("Commander", root, "Aller", 16, TaxiActionColor,
                    new Vector2(1f, 0.5f), new Vector2(96f, 48f), new Vector2(-10f, 0f));
                int captured = slot;
                btn.onClick.AddListener(() => OrderTaxiToSlot(captured));

                _taxiCards.Add(new TaxiCardView
                {
                    Root = root,
                    Title = title,
                    Subtitle = sub,
                    OrderButton = btn,
                    OrderBg = btn.GetComponent<Image>(),
                    OrderLabel = btn.GetComponentInChildren<Text>()
                });
            }
            return _taxiCards[index];
        }

        private void OrderTaxiToSlot(int index)
        {
            if (index < 0 || index >= _taxiVisible.Count)
                return;
            OrderTaxiAt(_taxiVisible[index].Position);
        }

        private void OrderTaxiAt(Vector3 destination)
        {
            PlayerServices services = PlayerServices.Local;
            if (services == null)
            {
                _taxiPageStatus.text = "Service taxi indisponible.";
                return;
            }
            if (services.Taxi != PlayerServices.TaxiState.None)
            {
                _taxiPageStatus.text = "Une course est déjà en cours.";
                return;
            }
            if (TaxiRideModel.HorizontalDistance(LocalPlayerPosition(), destination) < TaxiRideModel.MinCallDistance)
            {
                _taxiPageStatus.text = "Destination trop proche (minimum 30 m).";
                return;
            }
            services.CallTaxi(destination);
            RefreshTaxiPageStatus();
            ApplyTaxiFilter();
        }

        private static Vector3 LocalPlayerPosition()
        {
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            return local != null ? local.transform.position : Vector3.zero;
        }

        private void BuildTaxi(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Taxi, screen);
            page.GetComponent<Image>().color = new Color(0.11f, 0.09f, 0.04f, 1f);
            MakeHeader(page, "Taxi", TaxiHeaderColor);

            // Carte de suivi de course en direct avec icône Taxi + bouton Annuler.
            RectTransform statusCard = MakeImage("EtatTaxi", page, Row,
                new Vector2(0.5f, 1f), new Vector2(444f, 76f), new Vector2(0f, -84f));

            RectTransform taxiLogoRect = MakeImage("LogoTaxi", statusCard, Color.white,
                new Vector2(0f, 0.5f), new Vector2(50f, 50f), new Vector2(10f, 0f));
            Image taxiLogo = taxiLogoRect.GetComponent<Image>();
            taxiLogo.sprite = HudIconLibrary.AppTaxi;
            taxiLogo.type = Image.Type.Simple;
            taxiLogo.preserveAspect = true;

            _taxiPageStatus = MakeText("Statut", statusCard, string.Empty, 16, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(268f, 28f), new Vector2(68f, -10f));
            _taxiPageStatus.fontStyle = FontStyle.Bold;
            _taxiPageStatus.horizontalOverflow = HorizontalWrapMode.Wrap;

            _taxiPageDetail = MakeText("Detail", statusCard, string.Empty, 13, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(268f, 34f), new Vector2(68f, -36f));
            _taxiPageDetail.color = new Color(1f, 1f, 1f, 0.72f);
            _taxiPageDetail.horizontalOverflow = HorizontalWrapMode.Wrap;

            _taxiPageCancelButton = MakeButton("Annuler", statusCard, "Annuler", 14, Red,
                new Vector2(1f, 0.5f), new Vector2(88f, 44f), new Vector2(-10f, 0f));
            _taxiPageCancelButton.onClick.AddListener(() =>
            {
                PlayerServices.Local?.CancelTaxi();
                RefreshTaxiPageStatus();
                ApplyTaxiFilter();
            });
            _taxiPageCancelButton.gameObject.SetActive(false);

            // Carte repère GPS / ouverture carte plein écran.
            RectTransform gpsCard = MakeImage("GpsTaxi", page, new Color(1f, 0.80f, 0.25f, 0.10f),
                new Vector2(0.5f, 1f), new Vector2(444f, 64f), new Vector2(0f, -166f));
            _taxiWaypointLabel = MakeText("GpsInfo", gpsCard, string.Empty, 15, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(228f, 52f), new Vector2(12f, 0f));
            _taxiWaypointLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            _taxiWaypointGoButton = MakeButton("AllerGps", gpsCard, "Aller", 15, TaxiActionColor,
                new Vector2(1f, 0.5f), new Vector2(82f, 42f), new Vector2(-114f, 0f));
            _taxiWaypointGoButton.GetComponentInChildren<Text>().color = new Color(0.08f, 0.06f, 0.02f, 1f);
            _taxiWaypointGoButton.onClick.AddListener(() =>
            {
                if (MapHud.HasWaypoint)
                    OrderTaxiAt(MapHud.Waypoint);
            });

            Button mapBtn = MakeButton("OuvrirCarte", gpsCard, "Carte GPS", 14, Dark,
                new Vector2(1f, 0.5f), new Vector2(98f, 42f), new Vector2(-10f, 0f));
            mapBtn.onClick.AddListener(() =>
            {
                _phone.gameObject.SetActive(false);
                MapHud.OpenFullMap();
            });

            // Barre de recherche de destination.
            RectTransform searchBox = MakeImage("RechercheTaxi", page, new Color(1f, 1f, 1f, 0.11f),
                new Vector2(0.5f, 1f), new Vector2(444f, 40f), new Vector2(0f, -236f));
            searchBox.GetComponent<Image>().raycastTarget = true;
            Text searchText = MakeText("Texte", searchBox, string.Empty, 17, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(420f, 36f), Vector2.zero);
            Text searchHint = MakeText("Indice", searchBox, "Rechercher un quartier, commerce, hôpital…", 16, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(420f, 36f), Vector2.zero);
            searchHint.color = new Color(1f, 1f, 1f, 0.45f);
            _taxiSearchInput = searchBox.gameObject.AddComponent<InputField>();
            _taxiSearchInput.targetGraphic = searchBox.GetComponent<Image>();
            _taxiSearchInput.textComponent = searchText;
            _taxiSearchInput.placeholder = searchHint;
            _taxiSearchInput.characterLimit = 40;
            _taxiSearchInput.onValueChanged.AddListener(_ => ApplyTaxiFilter());

            // Onglets de filtre de destinations.
            string[] tabNames = { "Tout", "Quartiers", "Lieux", "Commerces", "Santé" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                TaxiCategory cat = (TaxiCategory)i;
                float x = (i - 2) * 90f;
                Button tab = MakeButton("Tab_" + cat, page, tabNames[i], 13, TaxiDisabledColor,
                    new Vector2(0.5f, 1f), new Vector2(86f, 34f), new Vector2(x, -282f));
                tab.onClick.AddListener(() => SelectTaxiCategory(cat));
                _taxiTabBg[i] = tab.GetComponent<Image>();
                _taxiTabText[i] = tab.GetComponentInChildren<Text>();
            }

            _taxiScroll = PhoneScrollView.Create(page, "TaxiScroll", 324f, 6f);
            RegisterPageScroll(Page.Taxi, _taxiScroll);
            RefreshTaxiTabsVisual();
        }
    }
}
