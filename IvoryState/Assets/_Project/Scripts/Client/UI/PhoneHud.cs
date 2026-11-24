using System;
using System.Collections.Generic;
using IvoryState.Client.Input;
using IvoryState.Client.Phone;
using IvoryState.Client.Settings;
using IvoryState.Client.World;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Le téléphone du joueur : un vrai smartphone à l'écran, avec un écran d'accueil et des applications.
    /// <list type="bullet">
    /// <item><b>Contacts</b> : joueurs connectés, appel vocal (sonnerie, Décrocher / Refuser, durée, micro).</item>
    /// <item><b>Pingoin</b> : porte-monnaie mobile. Solde, choix du destinataire (les joueurs à côté en
    /// premier), montant au pavé numérique ou rapide, envoi. Le serveur valide tout.</item>
    /// <item><b>Paramètres</b> : volume, sensibilité de la caméra, sonnerie, effets visuels (sauvegardés).</item>
    /// </list>
    /// L'interface est construite en code au démarrage, sous le canvas du HUD (aucun élément à régénérer
    /// dans la scène). Les sonneries sont synthétisées (aucun fichier audio).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class PhoneHud : MonoBehaviour
    {
        public static PhoneHud Instance { get; private set; }

        /// <summary>Le joueur vient d'ouvrir son téléphone (tutoriel).</summary>
        public static event Action PhoneOpened;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPhoneStatics()
        {
            Instance = null;
            PhoneOpened = null;
        }

        public enum Page { Home, Contacts, Pingoin, PingoinAmount, Settings, Bag, AddFriend, Messages, Conversation, History, Orders, Jobs, Housing, RealEstate, Lottery, Wassup, Garage, Djassa, Taxi, Profile, Commerces }

        // Amis (liste reçue du serveur) et conversations privées (gardées pendant la session).
        private string[] _friendKeys = new string[0];
        private string[] _friendNames = new string[0];
        private int[] _friendOnline = new int[0];
        private readonly string[] _contactKeys = new string[ListSlots];
        private readonly Button[] _messageButtons = new Button[ListSlots];
        private readonly Text[] _messageLabels = new Text[ListSlots];
        private readonly string[] _messageKeys = new string[ListSlots];
        private Text _messagesHint;
        private static readonly Dictionary<string, List<(string text, bool outgoing)>> s_conversations =
            new Dictionary<string, List<(string, bool)>>();
        private static readonly Dictionary<string, string> s_conversationNames = new Dictionary<string, string>();
        private int _unread;
        private string _openConversation;
        private const int ConversationLines = 9;
        private Text _conversationTitle;
        private readonly Text[] _conversationLines = new Text[ConversationLines];
        private InputField _conversationInput;

        // Ajout d'ami à proximité.
        private const int NearbySlots = 6;
        private readonly Button[] _nearbyButtons = new Button[NearbySlots];
        private readonly Text[] _nearbyLabels = new Text[NearbySlots];
        private readonly int[] _nearbyIds = new int[NearbySlots];
        private Text _nearbyHint;

        // Demande d'ami reçue (bandeau, même téléphone fermé).
        private RectTransform _requestPanel;
        private Text _requestText;
        private int _requestFrom = -1;

        // Historique Pingoin.
        private const int HistorySlots = 9;
        private readonly Text[] _historyDates = new Text[HistorySlots];
        private readonly Text[] _historyReasons = new Text[HistorySlots];
        private readonly Text[] _historyAmounts = new Text[HistorySlots];
        private Text _historyHint;

        // Commandes et emplois.
        private Text _taxiInfo;

        private const int ListSlots = 7;
        private const int PayeeSlots = 5;
        private const int MaxAmountDigits = 6;
        private const float PhoneWidth = 477f, PhoneHeight = 960f;
        private const float ScreenLogicalWidth = 472f;
        private static readonly long[] Presets = { 500, 1000, 2000, 5000 };

        private static readonly Color Green = new Color(0.15f, 0.6f, 0.3f, 0.95f);
        private static readonly Color Red = new Color(0.75f, 0.18f, 0.15f, 0.95f);
        private static readonly Color Dark = new Color(0.12f, 0.12f, 0.14f, 0.92f);
        private static readonly Color Row = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color PingoinBlue = new Color(0.07f, 0.36f, 0.85f, 1f);
        private static readonly Color PingoinDeep = new Color(0.03f, 0.13f, 0.36f, 1f);
        private static readonly Color Wallpaper = new Color(0.07f, 0.20f, 0.30f, 1f);

        private const string PrefVolume = "ivory.volume";
        private const string PrefSensitivity = "ivory.sensitivity";
        private const string PrefRingtone = "ivory.ringtone";
        private const string PrefEffects = "ivory.effects";

        [SerializeField] private Sprite _rounded;

        private Font _font;
        private Button _phoneButton;

        // Le téléphone et ses pages.
        private RectTransform _phone;
        private Text _clock;
        private readonly Dictionary<Page, RectTransform> _pages = new Dictionary<Page, RectTransform>();
        private readonly Dictionary<Page, PhoneScrollView> _pageScrolls = new Dictionary<Page, PhoneScrollView>();
        private Page _page;

        // Animation de glissement lors de la navigation entre pages.
        private const float PageSlideDuration = 0.20f;
        private RectTransform _slidePrevPage;
        private RectTransform _slideCurrPage;
        private float _slideTimer;
        private int _slideDirection;

        // Annuaire partagé par Pingoin.
        private int[] _directoryIds = new int[0];
        private string[] _directoryNames = new string[0];

        // Pingoin.
        private Text _walletBalance;
        private Text _walletHint;
        private readonly Button[] _payeeButtons = new Button[PayeeSlots];
        private readonly Text[] _payeeLabels = new Text[PayeeSlots];
        private readonly int[] _payeeIds = new int[PayeeSlots];
        private readonly string[] _payeeNames = new string[PayeeSlots];
        private int _recipientId = -1;
        private string _recipientName = string.Empty;
        private string _amountDigits = string.Empty;
        private Text _amountTitle;
        private Text _amountText;
        private Text _amountBalance;
        private Button _amountSend;
        private long _shownBalance = long.MinValue;
        private bool _balanceHidden;

        // Statuts / métiers officiels (carte d'identité)
        private readonly Button[] _jobStatusButtons = new Button[6];
        private readonly Image[] _jobStatusButtonBg = new Image[6];
        private readonly Text[] _jobStatusButtonLabel = new Text[6];

        // Appel (panneau en haut de l'écran, visible même téléphone fermé).
        private RectTransform _callPanel;
        private Text _callTitle;
        private Text _callInfo;
        private Button _acceptButton;
        private Button _hangUpButton;
        private Text _hangUpLabel;
        private Button _muteButton;
        private Text _muteLabel;
        private RectTransform _levelFill;

        private AudioSource _ringer;
        private AudioClip _ringtone;
        private AudioClip _ringback;

        private CallState _state;
        private float _stateSince;
        private string _notice;
        private float _noticeUntil;
        private int _shownSeconds = -1;
        private string _shownClock;

        private void Awake()
        {
            Instance = this;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            LoadSettings();
            BuildUi();
            _ringer = gameObject.AddComponent<AudioSource>();
            _ringer.loop = true;
            _ringer.spatialBlend = 0f;
            _ringer.playOnAwake = false;
            _ringtone = BuildTone("Sonnerie", new[] { 523f, 659f }, 0.12f, 2f, 1.2f);
            _ringback = BuildTone("Tonalité", new[] { 440f }, 1.0f, 4f, 1.0f);

            if (GetComponent<VoiceChat>() == null)
                gameObject.AddComponent<VoiceChat>();

            // Menus des boutiques, du marché et des gares de gbaka (même canvas, plein écran).
            var menuObject = new GameObject("ChoiceMenu", typeof(RectTransform));
            menuObject.transform.SetParent(transform, false);
            var menuRect = (RectTransform)menuObject.transform;
            menuRect.anchorMin = Vector2.zero;
            menuRect.anchorMax = Vector2.one;
            menuRect.offsetMin = menuRect.offsetMax = Vector2.zero;
            menuObject.AddComponent<ChoiceMenuHud>().Initialize(_rounded, _font);

            // Compte et création du personnage, au-dessus de tout le reste.
            var accountObject = new GameObject("Compte", typeof(RectTransform));
            accountObject.transform.SetParent(transform, false);
            var accountRect = (RectTransform)accountObject.transform;
            accountRect.anchorMin = Vector2.zero;
            accountRect.anchorMax = Vector2.one;
            accountRect.offsetMin = accountRect.offsetMax = Vector2.zero;
            accountObject.AddComponent<AccountPanel>().Initialize(_rounded, _font);
        }

        private void Start()
        {
            ApplySettings(); // les caméras existent maintenant (effets visuels)
        }

        private void OnEnable()
        {
            PlayerPhone.LocalDirectoryReceived += OnDirectory;
            PlayerPhone.LocalCallStateChanged += OnCallState;
            PlayerPhone.LocalProfileDirectoryReceived += OnProfileDirectoryReceived;
            PlayerPhone.LocalProfileReceived += OnProfileReceived;
            PlayerNetworkIdentity.LocalXpChanged += OnLocalXpChanged;
            PlayerNetworkIdentity.LocalProfileMetaChanged += OnLocalProfileMetaChanged;
            PlayerInteractor.LocalProfileInspectRequested += OnProfileInspectRequested;
            PlayerInventory.LocalChanged += OnBagChanged;
            PlayerFriends.LocalFriendsReceived += OnFriends;
            PlayerFriends.LocalFriendRequest += OnFriendRequest;
            Gameplay.Vehicles.PlayerDriving.LocalBoardingRequest += OnBoardingRequest;
            PlayerFriends.LocalMessage += OnPrivateMessage;
            PlayerEconomy.LocalHistoryReceived += OnHistory;
            PlayerEconomy.LocalCommercesChanged += OnCommercesUpdated;
            CommerceRegistryClient.RegistryUpdated += OnCommercesUpdated;
            Gameplay.Homes.PlayerHome.LocalChanged += OnHomeChanged;
            Gameplay.Homes.PlayerHome.LocalResidenceMenu += OnResidenceMenu;
            Gameplay.Homes.PlayerHome.LocalPropertyList += OnPropertyList;
            Gameplay.Homes.PlayerHome.LocalMoveInOffer += OnMoveInOffer;
            Gameplay.Economy.PlayerLottery.LocalStatus += OnLotteryStatus;
            Gameplay.Social.PlayerWassup.LocalFeed += OnWassupFeed;
            Gameplay.Vehicles.PlayerGarage.LocalChanged += OnGarageChanged;
            Gameplay.Economy.PlayerMarket.LocalAds += OnDjassaAds;
            PlayerInteractor.LocalMenuRequested += OnMenuRequested;
            MapHud.WaypointChanged += OnWaypointChangedForTaxi;
            GameSettings.Changed += RefreshSettingsTexts;
            MobilePlayerControls.RegisterBlockingRect(_residencePanel);
            MobilePlayerControls.RegisterBlockingRect(_requestPanel);
            MobilePlayerControls.RegisterBlockingRect((RectTransform)_phoneButton.transform);
            MobilePlayerControls.RegisterBlockingRect(_phone);
            MobilePlayerControls.RegisterBlockingRect(_callPanel);
        }

        private void OnDisable()
        {
            PlayerPhone.LocalDirectoryReceived -= OnDirectory;
            PlayerPhone.LocalCallStateChanged -= OnCallState;
            PlayerPhone.LocalProfileDirectoryReceived -= OnProfileDirectoryReceived;
            PlayerPhone.LocalProfileReceived -= OnProfileReceived;
            PlayerNetworkIdentity.LocalXpChanged -= OnLocalXpChanged;
            PlayerNetworkIdentity.LocalProfileMetaChanged -= OnLocalProfileMetaChanged;
            PlayerInteractor.LocalProfileInspectRequested -= OnProfileInspectRequested;
            PlayerInventory.LocalChanged -= OnBagChanged;
            PlayerFriends.LocalFriendsReceived -= OnFriends;
            PlayerFriends.LocalFriendRequest -= OnFriendRequest;
            Gameplay.Vehicles.PlayerDriving.LocalBoardingRequest -= OnBoardingRequest;
            PlayerFriends.LocalMessage -= OnPrivateMessage;
            PlayerEconomy.LocalHistoryReceived -= OnHistory;
            PlayerEconomy.LocalCommercesChanged -= OnCommercesUpdated;
            CommerceRegistryClient.RegistryUpdated -= OnCommercesUpdated;
            Gameplay.Homes.PlayerHome.LocalChanged -= OnHomeChanged;
            Gameplay.Homes.PlayerHome.LocalResidenceMenu -= OnResidenceMenu;
            Gameplay.Homes.PlayerHome.LocalPropertyList -= OnPropertyList;
            Gameplay.Homes.PlayerHome.LocalMoveInOffer -= OnMoveInOffer;
            Gameplay.Economy.PlayerLottery.LocalStatus -= OnLotteryStatus;
            Gameplay.Social.PlayerWassup.LocalFeed -= OnWassupFeed;
            Gameplay.Vehicles.PlayerGarage.LocalChanged -= OnGarageChanged;
            Gameplay.Economy.PlayerMarket.LocalAds -= OnDjassaAds;
            PlayerInteractor.LocalMenuRequested -= OnMenuRequested;
            MapHud.WaypointChanged -= OnWaypointChangedForTaxi;
            GameSettings.Changed -= RefreshSettingsTexts;
            MobilePlayerControls.UnregisterBlockingRect(_residencePanel);
            MobilePlayerControls.UnregisterBlockingRect(_requestPanel);
            MobilePlayerControls.UnregisterBlockingRect((RectTransform)_phoneButton.transform);
            MobilePlayerControls.UnregisterBlockingRect(_phone);
            MobilePlayerControls.UnregisterBlockingRect(_callPanel);
        }

        private void Update()
        {
            UpdatePageSlideTransition();

            bool connected = PlayerPhone.Local != null;
            if (_phoneButton.gameObject.activeSelf)
                _phoneButton.gameObject.SetActive(false);
            if (!connected)
            {
                if (_phone.gameObject.activeSelf && _page != Page.Settings)
                    _phone.gameObject.SetActive(false);
                if (_state != CallState.Idle) OnCallState(CallState.Idle, -1, string.Empty, string.Empty);
                return;
            }

            if (_phone.gameObject.activeSelf)
            {
                RefreshPhoneScreen();
                if (_page == Page.Taxi && Time.unscaledTime >= _nextTaxiStatusRefresh)
                {
                    _nextTaxiStatusRefresh = Time.unscaledTime + 0.5f;
                    RefreshTaxiPageStatus();
                }
            }

            bool showCall = _state != CallState.Idle || Time.unscaledTime < _noticeUntil;
            if (_callPanel.gameObject.activeSelf != showCall)
                _callPanel.gameObject.SetActive(showCall);
            if (!showCall)
                return;

            if (_state == CallState.Connected)
            {
                int seconds = (int)(Time.unscaledTime - _stateSince);
                if (seconds != _shownSeconds)
                {
                    _shownSeconds = seconds;
                    _callInfo.text = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
                }
                float level = VoiceChat.Muted ? 0f : VoiceChat.InputLevel;
                _levelFill.anchorMax = new Vector2(Mathf.Lerp(_levelFill.anchorMax.x, level, 0.3f), 1f);
            }
        }

        /// <summary>Horloge de la barre d'état et solde Pingoin (mis à jour seulement s'ils changent).</summary>
        private void RefreshPhoneScreen()
        {
            string clock = DayNightCycle.ClockText;
            if (clock != _shownClock)
            {
                _shownClock = clock;
                _clock.text = clock;
            }

            PlayerEconomy economy = PlayerTransfers.Local != null ? PlayerTransfers.Local.Economy : null;
            long balance = economy != null ? economy.Balance : 0;
            if (balance != _shownBalance)
            {
                _shownBalance = balance;
                RefreshBalanceTexts();
            }
        }

        private void RefreshBalanceTexts()
        {
            string formatted = _balanceHidden ? "•••••• FCFA" : Money.Format(_shownBalance);
            _walletBalance.text = formatted;
            _amountBalance.text = "Solde : " + formatted;
        }

        #region Navigation

        public void TogglePhone()
        {
            if (PlayerPhone.Local == null)
                return;
            bool open = !_phone.gameObject.activeSelf;
            _phone.gameObject.SetActive(open);
            if (open)
            {
                _phone.SetAsLastSibling();
                PhoneOpened?.Invoke();
                _shownBalance = long.MinValue;
                _shownClock = null;
                ShowPage(Page.Home, animate: false);
            }
        }

        public void OpenBag()
        {
            if (PlayerPhone.Local == null)
                return;
            if (_phone.gameObject.activeSelf && _page == Page.Bag)
            {
                _phone.gameObject.SetActive(false);
                return;
            }
            _phone.SetAsLastSibling();
            _phone.gameObject.SetActive(true);
            PhoneOpened?.Invoke();
            _shownBalance = long.MinValue;
            _shownClock = null;
            ShowPage(Page.Bag, animate: false);
        }

        public void OpenSettings()
        {
            if (_phone.gameObject.activeSelf && _page == Page.Settings)
            {
                _phone.gameObject.SetActive(false);
                return;
            }
            _phone.SetAsLastSibling();
            _phone.gameObject.SetActive(true);
            PhoneOpened?.Invoke();
            _shownBalance = long.MinValue;
            _shownClock = null;
            ShowPage(Page.Settings, animate: false);
        }

        private void ShowPage(Page page, bool animate = true, int slideDir = 0)
        {
            Page previous = _page;
            _page = page;

            int dir = slideDir != 0 ? slideDir : (page == Page.Home ? -1 : 1);
            bool shouldAnimate = animate && _phone != null && _phone.gameObject.activeInHierarchy && previous != page;

            FinishSlideTransition();

            foreach (KeyValuePair<Page, RectTransform> entry in _pages)
            {
                if (entry.Key == page)
                    entry.Value.gameObject.SetActive(true);
                else if (!shouldAnimate || entry.Key != previous)
                    entry.Value.gameObject.SetActive(false);
            }

            if (shouldAnimate && _pages.TryGetValue(previous, out RectTransform prevRect) && _pages.TryGetValue(page, out RectTransform currRect))
            {
                _slidePrevPage = prevRect;
                _slideCurrPage = currRect;
                _slideDirection = dir;
                _slideTimer = 0f;
                prevRect.SetAsFirstSibling();
                currRect.SetAsLastSibling();
                SetPageSlideOffset(currRect, dir * ScreenLogicalWidth);
            }
            else if (_pages.TryGetValue(page, out RectTransform currRectNoAnim))
            {
                SetPageSlideOffset(currRectNoAnim, 0f);
            }

            if (_pageScrolls.TryGetValue(page, out PhoneScrollView scroll))
                scroll.ResetToTopAnimated(true);

            switch (page)
            {
                case Page.Profile:
                    RequestProfileSnapshot(_requestedProfileId);
                    break;
                case Page.Contacts:
                    _contactsHint.text = "Chargement de tes contacts…";
                    PlayerFriends.Local?.RequestList();
                    PlayerPhone.Local?.RequestProfileDirectory();
                    RefreshContactsView();
                    break;
                case Page.Messages:
                    PlayerFriends.Local?.RequestList();
                    FillMessages();
                    break;
                case Page.Jobs:
                    RefreshJobStatusCards();
                    break;
                case Page.Commerces:
                    OpenCommercesPage();
                    break;
                case Page.AddFriend:
                    FillNearby();
                    break;
                case Page.Conversation:
                    FillConversation();
                    break;
                case Page.History:
                    _historyHint.text = "Chargement de l'historique…";
                    for (int i = 0; i < HistorySlots; i++)
                        _historyDates[i].transform.parent.gameObject.SetActive(false);
                    PlayerTransfers.Local?.Economy?.RequestHistory();
                    break;
                case Page.Orders:
                    RefreshTaxiInfo();
                    break;
                case Page.Pingoin:
                    _walletHint.text = "Recherche des destinataires…";
                    for (int i = 0; i < PayeeSlots; i++)
                        _payeeButtons[i].gameObject.SetActive(false);
                    PlayerPhone.Local?.RequestDirectory();
                    break;
                case Page.PingoinAmount:
                    _amountDigits = string.Empty;
                    _amountTitle.text = "Envoyer à " + _recipientName;
                    RefreshAmount();
                    break;
                case Page.Settings:
                    RefreshSettingsTexts();
                    break;
                case Page.Bag:
                    FillBag();
                    break;
                case Page.Housing:
                    FillHousing();
                    break;
                case Page.Garage:
                    FillGarage();
                    break;
                case Page.Djassa:
                    OpenDjassaPage();
                    break;
                case Page.Taxi:
                    OpenTaxiPage();
                    break;
                case Page.Wassup:
                    _wassupHint.text = "Chargement…";
                    Gameplay.Social.PlayerWassup.Local?.RequestFeed();
                    break;
                case Page.Lottery:
                    Gameplay.Economy.PlayerLottery.Local?.RequestStatus();
                    FillLottery();
                    break;
                case Page.RealEstate:
                    _estateHint.text = "Chargement des annonces…";
                    Gameplay.Homes.PlayerHome.Local?.RequestPropertyList();
                    FillRealEstate();
                    break;
            }
        }

        // --- Loto : tirage chaque heure, 1 chance sur 10, gain ×8 ---
        private static readonly long[] LotteryStakes = { 500, 1000, 5000, 10000, 50000, 100000 };
        private Text _lotteryNext, _lotteryTickets;
        private long _lotteryDrawAt;
        private long[] _lotteryMine = new long[0];

        private void OnLotteryStatus(long nextDraw, long[] stakes)
        {
            _lotteryDrawAt = nextDraw;
            _lotteryMine = stakes ?? new long[0];
            if (_phone.gameObject.activeSelf && _page == Page.Lottery)
                FillLottery();
        }

        private void FillLottery()
        {
            if (_lotteryDrawAt <= 0)
            {
                _lotteryNext.text = "Chargement…";
            }
            else
            {
                long left = Math.Max(0, _lotteryDrawAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                _lotteryNext.text = "Prochain tirage à " + DateTimeOffset.FromUnixTimeSeconds(_lotteryDrawAt).ToLocalTime().ToString("HH:mm") +
                                    "  (dans " + (left / 60) + " min)";
            }
            if (_lotteryMine.Length == 0)
            {
                _lotteryTickets.text = "Aucun ticket pour ce tirage.";
                return;
            }
            var text = new System.Text.StringBuilder("Mes tickets :");
            foreach (long stake in _lotteryMine)
                text.Append("\n• ").Append(Money.Format(stake)).Append("  →  gain possible ").Append(Money.Format(Gameplay.Economy.PlayerLottery.Rules.Payout(stake)));
            _lotteryTickets.text = text.ToString();
        }

        private void BuildLottery(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Lottery, screen);
            page.GetComponent<Image>().color = new Color(0.10f, 0.06f, 0.14f, 1f);
            var accent = new Color(0.55f, 0.20f, 0.70f, 1f);
            MakeHeader(page, "Loto", accent);
            MakeText("Regles", page, "Un tirage chaque heure. Chaque ticket a 1 chance sur 10 de gagner 8 fois sa mise.", 19,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 56f), new Vector2(0f, -84f)).horizontalOverflow = HorizontalWrapMode.Wrap;
            _lotteryNext = MakeText("Prochain", page, string.Empty, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 36f), new Vector2(0f, -144f));
            _lotteryNext.color = new Color(1f, 0.85f, 0.4f);
            MakeText("Choix", page, "Choisis ta mise :", 20, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(430f, 30f), new Vector2(0f, -192f));
            for (int i = 0; i < LotteryStakes.Length; i++)
            {
                long stake = LotteryStakes[i];
                int column = i % 2, row = i / 2;
                Button button = MakeButton("Mise_" + stake, page, Money.Format(stake), 22, accent, new Vector2(0.5f, 1f),
                    new Vector2(205f, 62f), new Vector2(column == 0 ? -110f : 110f, -232f - row * 72f));
                button.onClick.AddListener(() => Gameplay.Economy.PlayerLottery.Local?.Buy(stake));
            }
            _lotteryTickets = MakeText("Tickets", page, string.Empty, 19, TextAnchor.UpperLeft, new Vector2(0.5f, 1f), new Vector2(430f, 200f), new Vector2(0f, -460f));
            _lotteryTickets.horizontalOverflow = HorizontalWrapMode.Wrap;
            MakeText("Max", page, "5 tickets au plus par tirage. Jeu de hasard : mise seulement ce que tu peux perdre.", 15,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(440f, 44f), new Vector2(0f, 10f)).color = new Color(1f, 1f, 1f, 0.6f);
        }

        // --- Loterie : 3 tirages, 5 numéros chacun ---
        private const int EstateRows = 5;
        private readonly RectTransform[] _estateRows = new RectTransform[EstateRows];
        private readonly Text[] _estateTitles = new Text[EstateRows];
        private readonly Text[] _estateDetails = new Text[EstateRows];
        private readonly Text[] _estatePrices = new Text[EstateRows];
        private readonly Button[] _estateActions = new Button[EstateRows];
        private readonly Text[] _estateActionLabels = new Text[EstateRows];
        private readonly int[] _estateIds = new int[EstateRows];
        private string[] _estateOccupants;
        private Text _estateHint, _estatePageLabel;
        private Button _estateRentTab, _estateBuyTab;
        private bool _estateBuyMode;
        private int _estatePage;
        private int _estateConfirmId;
        private float _estateConfirmUntil;
        private readonly List<int> _estateFiltered = new List<int>();

        private void OnPropertyList(int[] ids, string[] occupants)
        {
            _estateOccupants = new string[Gameplay.Homes.PropertyCatalog.Count + 1];
            for (int i = 0; i < ids.Length && i < occupants.Length; i++)
            {
                if (Gameplay.Homes.PropertyCatalog.IsValid(ids[i]))
                    _estateOccupants[ids[i]] = occupants[i];
            }
            if (_phone.gameObject.activeSelf && _page == Page.RealEstate)
                FillRealEstate();
        }

        private void FillRealEstate()
        {
            _estateFiltered.Clear();
            foreach (var property in Gameplay.Homes.PropertyCatalog.Properties)
            {
                if (Gameplay.Homes.PropertyCatalog.IsForSale(property.Kind) == _estateBuyMode)
                    _estateFiltered.Add(property.Id);
            }
            int pages = Mathf.Max(1, (_estateFiltered.Count + EstateRows - 1) / EstateRows);
            _estatePage = Mathf.Clamp(_estatePage, 0, pages - 1);
            _estatePageLabel.text = (_estatePage + 1) + " / " + pages;
            _estateRentTab.GetComponent<Image>().color = _estateBuyMode ? Row : new Color(0.20f, 0.55f, 0.75f, 1f);
            _estateBuyTab.GetComponent<Image>().color = _estateBuyMode ? new Color(0.20f, 0.55f, 0.75f, 1f) : Row;

            Gameplay.Homes.PlayerHome home = Gameplay.Homes.PlayerHome.Local;
            for (int row = 0; row < EstateRows; row++)
            {
                int index = _estatePage * EstateRows + row;
                bool used = index < _estateFiltered.Count;
                _estateRows[row].gameObject.SetActive(used);
                if (!used)
                    continue;

                int id = _estateFiltered[index];
                _estateIds[row] = id;
                var property = Gameplay.Homes.PropertyCatalog.Get(id);
                string occupant = _estateOccupants != null ? _estateOccupants[id] : "…";
                bool mine = home != null && home.LivesIn(id);
                bool free = occupant != null && occupant.Length == 0;

                _estateTitles[row].text = property.Label + " — " + Gameplay.Homes.PropertyCatalog.KindName(property.Kind);
                _estateDetails[row].text = Gameplay.Homes.PropertyCatalog.Entrances[property.EntranceIndex].Name +
                                           (property.Chic ? "  •  quartier chic" : string.Empty) +
                                           (Gameplay.Homes.PropertyCatalog.HasGarden(property.Kind) ? "  •  jardin" : string.Empty) +
                                           "\n" + (mine ? "<color=#8CFF9A>Chez toi</color>"
                                               : free ? "<color=#8CFF9A>Libre</color>"
                                               : occupant == "…" || occupant == null ? "…" : "<color=#FF8A7A>Occupé par " + occupant + "</color>");
                _estatePrices[row].text = _estateBuyMode
                    ? Money.Format(property.Price)
                    : Money.Format(property.Price) + "/sem.";
                bool canTake = free && home != null && home.CanTakeProperty;
                _estateActions[row].gameObject.SetActive(canTake);
                if (canTake)
                    _estateActionLabels[row].text = _estateConfirmId == id && Time.unscaledTime <= _estateConfirmUntil
                        ? "Confirmer ?"
                        : _estateBuyMode ? "Acheter" : "Louer";
            }

            _estateHint.text = _estateOccupants == null ? "Chargement des annonces…"
                : home != null && !home.CanTakeProperty ? "Tu as déjà 2 logements (le maximum). Revends-en un pour en reprendre un autre."
                : _estateBuyMode ? "Maison achetée : impôt de 0,5 % du prix chaque dimanche."
                : "Location : 1re semaine payée à la signature, puis chaque dimanche.";
        }

        private void OnEstateAction(int row)
        {
            int id = _estateIds[row];
            if (_estateConfirmId != id || Time.unscaledTime > _estateConfirmUntil)
            {
                _estateConfirmId = id;
                _estateConfirmUntil = Time.unscaledTime + 4f;
                FillRealEstate();
                return;
            }
            _estateConfirmId = 0;
            Gameplay.Homes.PlayerHome.Local?.RequestTakeProperty(id);
            Gameplay.Homes.PlayerHome.Local?.RequestPropertyList();
        }

        private void BuildRealEstate(RectTransform screen)
        {
            RectTransform page = MakePage(Page.RealEstate, screen);
            page.GetComponent<Image>().color = new Color(0.06f, 0.10f, 0.14f, 1f);
            MakeHeader(page, "Immobilier", new Color(0.20f, 0.55f, 0.75f, 1f));

            _estateRentTab = MakeButton("Location", page, "À louer", 21, Row, new Vector2(0.5f, 1f), new Vector2(210f, 50f), new Vector2(-110f, -86f));
            _estateRentTab.onClick.AddListener(() => { _estateBuyMode = false; _estatePage = 0; FillRealEstate(); });
            _estateBuyTab = MakeButton("Achat", page, "À vendre", 21, Row, new Vector2(0.5f, 1f), new Vector2(210f, 50f), new Vector2(110f, -86f));
            _estateBuyTab.onClick.AddListener(() => { _estateBuyMode = true; _estatePage = 0; FillRealEstate(); });

            for (int i = 0; i < EstateRows; i++)
            {
                int row = i;
                RectTransform card = MakeImage("Annonce_" + i, page, Row, new Vector2(0.5f, 1f), new Vector2(440f, 104f), new Vector2(0f, -146f - i * 110f));
                _estateRows[i] = card;
                _estateTitles[i] = MakeText("Titre", card, string.Empty, 18, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(420f, 26f), new Vector2(12f, -6f));
                _estateDetails[i] = MakeText("Details", card, string.Empty, 15, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(300f, 44f), new Vector2(12f, -34f));
                _estateDetails[i].supportRichText = true;
                _estateDetails[i].color = new Color(1f, 1f, 1f, 0.8f);
                _estatePrices[i] = MakeText("Prix", card, string.Empty, 18, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(220f, 26f), new Vector2(12f, 4f));
                _estatePrices[i].color = new Color(1f, 0.8f, 0.4f);
                Button gps = MakeButton("Gps", card, "GPS", 16, Dark, new Vector2(1f, 0f), new Vector2(64f, 40f), new Vector2(-122f, 8f));
                gps.onClick.AddListener(() =>
                {
                    MapHud.SuggestWaypoint(Gameplay.Homes.PropertyCatalog.EntrancePosition(_estateIds[row]));
                    _phone.gameObject.SetActive(false);
                });
                _estateActions[i] = MakeButton("Prendre", card, "Louer", 16, Green, new Vector2(1f, 0f), new Vector2(104f, 40f), new Vector2(-10f, 8f));
                _estateActionLabels[i] = _estateActions[i].GetComponentInChildren<Text>();
                _estateActions[i].onClick.AddListener(() => OnEstateAction(row));
            }

            Button previous = MakeButton("Precedent", page, "‹", 30, Dark, new Vector2(0.5f, 0f), new Vector2(80f, 46f), new Vector2(-150f, 64f));
            previous.onClick.AddListener(() => { _estatePage--; FillRealEstate(); });
            _estatePageLabel = MakeText("Page", page, "1 / 1", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(120f, 46f), new Vector2(0f, 64f));
            Button next = MakeButton("Suivant", page, "›", 30, Dark, new Vector2(0.5f, 0f), new Vector2(80f, 46f), new Vector2(150f, 64f));
            next.onClick.AddListener(() => { _estatePage++; FillRealEstate(); });
            _estateHint = MakeText("Info", page, string.Empty, 16, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(440f, 50f), new Vector2(0f, 8f));
            _estateHint.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // --- Logement ---
        private Text _housingTitle, _housingRoommate, _housingRent, _housingDebt;
        private Button _payDebtButton, _aidButton, _doorButton;
        private Text _doorLabel;

        // --- Menu de la résidence (dans le cercle d'entrée, s'il y a des studios ouverts) ---
        private const int ResidenceSlots = 9;
        private RectTransform _residencePanel;
        private readonly Button[] _residenceButtons = new Button[ResidenceSlots];
        private readonly Text[] _residenceLabels = new Text[ResidenceSlots];
        private readonly int[] _residenceStudios = new int[ResidenceSlots];

        private void OnResidenceMenu(int[] studios, string[] names)
        {
            for (int i = 0; i < ResidenceSlots; i++)
            {
                bool used = i < studios.Length && (studios[i] > 0);
                _residenceButtons[i].gameObject.SetActive(used);
                if (!used)
                    continue;
                _residenceStudios[i] = studios[i];
                int key = studios[i];
                string place = key >= 1000 && Gameplay.Homes.PropertyCatalog.IsValid(key - 1000)
                    ? Gameplay.Homes.PropertyCatalog.Get(key - 1000).Label
                    : "Studio n° " + key;
                _residenceLabels[i].text = names[i] == "Chez moi"
                    ? "Chez moi (" + place + ")"
                    : place + " — chez " + names[i];
            }
            _residencePanel.gameObject.SetActive(true);
        }

        private void BuildResidencePanel(RectTransform root)
        {
            _residencePanel = MakeImage("Residence", root, new Color(0.08f, 0.06f, 0.05f, 0.95f), new Vector2(0.5f, 0.5f), new Vector2(620f, 760f), Vector2.zero);
            _residencePanel.GetComponent<Image>().raycastTarget = true;
            MakeText("Titre", _residencePanel, "Où veux-tu aller ?", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(580f, 50f), new Vector2(0f, -20f)).color = new Color(1f, 0.8f, 0.35f);
            MakeText("Info", _residencePanel, "Les logements ouverts accueillent des visiteurs.", 19, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(580f, 40f), new Vector2(0f, -68f)).horizontalOverflow = HorizontalWrapMode.Wrap;
            for (int i = 0; i < ResidenceSlots; i++)
            {
                int slot = i;
                Button button = MakeButton("Studio_" + i, _residencePanel, string.Empty, 21, i == 0 ? Green : Row, new Vector2(0.5f, 1f), new Vector2(560f, 58f), new Vector2(0f, -118f - i * 64f));
                _residenceButtons[i] = button;
                _residenceLabels[i] = button.GetComponentInChildren<Text>();
                button.onClick.AddListener(() =>
                {
                    Gameplay.Homes.PlayerHome.Local?.RequestEnterStudio(_residenceStudios[slot]);
                    _residencePanel.gameObject.SetActive(false);
                });
            }
            Button close = MakeButton("Fermer", _residencePanel, "Rester dehors", 22, Dark, new Vector2(0.5f, 0f), new Vector2(300f, 60f), new Vector2(0f, 18f));
            close.onClick.AddListener(() => _residencePanel.gameObject.SetActive(false));
            _residencePanel.gameObject.SetActive(false);
        }

        private void OnHomeChanged()
        {
            if (!_phone.gameObject.activeSelf)
                return;
            if (_page == Page.Housing)
                FillHousing();
            else if (_page == Page.Djassa)
                FillDjassaSell();
        }

        private Text _propertyInfo;
        private readonly Button[] _leavePropertyButtons = new Button[Gameplay.Homes.PlayerHome.MaxProperties];
        private readonly Text[] _leavePropertyLabels = new Text[Gameplay.Homes.PlayerHome.MaxProperties];
        private Button _leaveColocButton, _moveInButton, _primaryButton;
        private Text _primaryLabel;

        private static string HomeName(int key)
        {
            if (key >= Gameplay.Homes.PlayerHome.PropertyKeyBase && Gameplay.Homes.PropertyCatalog.IsValid(key - Gameplay.Homes.PlayerHome.PropertyKeyBase))
                return Gameplay.Homes.PropertyCatalog.Get(key - Gameplay.Homes.PlayerHome.PropertyKeyBase).Label;
            return key > 0 ? "studio n° " + key : "aucun";
        }

        /// <summary>Passe au domicile suivant (studio, puis mes biens) comme domicile principal.</summary>
        private void CyclePrimaryHome()
        {
            var home = Gameplay.Homes.PlayerHome.Local;
            if (home == null)
                return;
            var keys = new List<int>();
            if (home.HasHome)
                keys.Add(home.Studio);
            for (int slot = 0; slot < Gameplay.Homes.PlayerHome.MaxProperties; slot++)
            {
                int id = home.PropertyAt(slot);
                if (Gameplay.Homes.PropertyCatalog.IsValid(id))
                    keys.Add(Gameplay.Homes.PlayerHome.PropertyKeyBase + id);
            }
            if (keys.Count < 2)
                return;
            int next = keys[(keys.IndexOf(home.PrimaryKey) + 1) % keys.Count];
            home.RequestSetPrimary(next);
        }
        private float _leaveConfirmUntil;
        private int _leaveConfirmSlot = -1;

        private void FillHousing()
        {
            Gameplay.Homes.PlayerHome home = Gameplay.Homes.PlayerHome.Local;
            if (home == null)
            {
                _housingTitle.text = "Logement indisponible (hors ligne).";
                _housingRoommate.text = _housingRent.text = _housingDebt.text = _propertyInfo.text = string.Empty;
                _payDebtButton.gameObject.SetActive(false);
                _aidButton.gameObject.SetActive(false);
                foreach (Button leave in _leavePropertyButtons)
                    leave.gameObject.SetActive(false);
                _leaveColocButton.gameObject.SetActive(false);
                _moveInButton.gameObject.SetActive(false);
                return;
            }

            if (home.IsHomeless)
            {
                _housingTitle.text = "Tu es SDF";
                _housingRoommate.text = "Tu as perdu ton logement : loyer impayé.";
                _housingRent.text = "Paie ta dette pour redevenir colocataire.";
            }
            else if (home.HasHome)
            {
                _housingTitle.text = "Studio n° " + home.Studio + " (colocation)\nRésidence Les Flamboyants, Treichville";
                _housingRoommate.text = "Colocataire : " + (string.IsNullOrEmpty(home.Roommate) ? "personne pour l'instant" : home.Roommate);
                _housingRent.text = "Loyer : " + Money.Format(Gameplay.Homes.RentRules.StudioRentPerRoommate) + " par semaine";
            }
            else
            {
                _housingTitle.text = home.HasProperty ? "Plus de colocation" : "Pas encore de logement";
                _housingRoommate.text = home.HasProperty ? "Tu vis dans ton logement ci-dessous." : "Un studio va t'être attribué.";
                _housingRent.text = string.Empty;
            }
            if (home.RentDueAt > 0 && !home.IsHomeless)
                _housingRent.text += (_housingRent.text.Length > 0 ? "\n" : string.Empty) + "Prochain prélèvement : " + Gameplay.Homes.RentRules.Format(home.RentDueAt);

            // Biens immobiliers (2 au plus) : une ligne et un bouton « quitter / revendre » chacun.
            var info = new System.Text.StringBuilder();
            bool ownsOne = false;
            for (int slot = 0; slot < Gameplay.Homes.PlayerHome.MaxProperties; slot++)
            {
                int id = home.PropertyAt(slot);
                bool has = Gameplay.Homes.PropertyCatalog.IsValid(id);
                _leavePropertyButtons[slot].gameObject.SetActive(has);
                if (!has)
                    continue;
                var property = Gameplay.Homes.PropertyCatalog.Get(id);
                bool owned = Gameplay.Homes.PropertyCatalog.IsForSale(property.Kind);
                bool mine = home.OwnsPropertyAt(slot);
                ownsOne |= mine;
                if (info.Length > 0)
                    info.Append('\n');
                info.Append(slot + 1).Append(". ").Append(property.Label).Append(" — ")
                    .Append(Gameplay.Homes.PropertyCatalog.Entrances[property.EntranceIndex].Name).Append("  •  ")
                    .Append(mine ? (owned ? "impôt " : "loyer ") + Money.Format(Gameplay.Homes.PropertyCatalog.WeeklyCost(property)) + "/sem." : "invité");
                if (!(_leaveConfirmSlot == slot && Time.unscaledTime <= _leaveConfirmUntil))
                    _leavePropertyLabels[slot].text = (slot + 1) + ". " + (mine ? (owned ? "Revendre à l'État (½)" : "Résilier le bail") : "Quitter");
            }
            _propertyInfo.text = info.Length > 0
                ? info.ToString()
                : "Envie de plus grand ? Ouvre l'application Immobilier (2 logements au plus).";
            _leaveColocButton.gameObject.SetActive(home.HasHome && home.HasProperty);
            _moveInButton.gameObject.SetActive(ownsOne);

            _doorButton.gameObject.SetActive(home.HasHome || home.HasProperty);
            _doorLabel.text = home.DoorOpen ? "Porte ouverte • Fermer" : "Porte fermée • Ouvrir";
            _primaryButton.gameObject.SetActive(home.HomeCount >= 2);
            _primaryLabel.text = "Domicile principal : " + HomeName(home.PrimaryKey) + "   ▸ changer";

            bool inDebt = home.RentDebt > 0;
            _housingDebt.text = !inDebt ? "Aucune dette."
                : home.IsHomeless ? "Dette : " + Money.Format(home.RentDebt)
                : "Dette : " + Money.Format(home.RentDebt) + "\nÀ payer avant " + Gameplay.Homes.RentRules.Format(home.RentDueAt) + ", sinon tu seras à la rue.";
            _housingDebt.color = inDebt ? new Color(1f, 0.45f, 0.40f) : new Color(0.55f, 1f, 0.6f);
            _payDebtButton.gameObject.SetActive(inDebt);
            _aidButton.gameObject.SetActive(inDebt);
        }

        private void BuildHousing(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Housing, screen);
            page.GetComponent<Image>().color = new Color(0.12f, 0.08f, 0.06f, 1f);
            var accent = new Color(0.75f, 0.30f, 0.20f, 1f);
            MakeHeader(page, "Logement", accent);

            PhoneScrollView scroll = PhoneScrollView.Create(page, "HousingScroll", 78f, 142f);
            RegisterPageScroll(Page.Housing, scroll);
            RectTransform body = scroll.Content;

            RectTransform card = MakeImage("Carte", body, Row, new Vector2(0.5f, 1f), new Vector2(430f, 300f), new Vector2(0f, -10f));
            _housingTitle = MakeText("Titre", card, string.Empty, 21, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(400f, 60f), new Vector2(16f, -10f));
            _housingRoommate = MakeText("Colocataire", card, string.Empty, 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(400f, 30f), new Vector2(16f, -74f));
            _housingRent = MakeText("Loyer", card, string.Empty, 18, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(400f, 52f), new Vector2(16f, -108f));
            _housingRent.color = new Color(1f, 1f, 1f, 0.8f);
            _housingDebt = MakeText("Dette", card, string.Empty, 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(400f, 100f), new Vector2(16f, -166f));
            foreach (Text text in new[] { _housingTitle, _housingRoommate, _housingRent, _housingDebt })
                text.horizontalOverflow = HorizontalWrapMode.Wrap;

            _payDebtButton = MakeButton("PayerDette", body, "Payer ma dette", 19, Green, new Vector2(0.5f, 1f), new Vector2(210f, 58f), new Vector2(-110f, -318f));
            _payDebtButton.onClick.AddListener(() => Gameplay.Homes.PlayerHome.Local?.RequestPayDebt());
            _aidButton = MakeButton("Aide", body, "Aide publique", 19, new Color(0.25f, 0.45f, 0.75f, 1f), new Vector2(0.5f, 1f), new Vector2(210f, 58f), new Vector2(110f, -318f));
            _aidButton.onClick.AddListener(() => Gameplay.Homes.PlayerHome.Local?.RequestPublicAid());

            _propertyInfo = MakeText("Bien", body, string.Empty, 16, TextAnchor.UpperLeft, new Vector2(0.5f, 1f), new Vector2(430f, 62f), new Vector2(0f, -382f));
            _propertyInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            _propertyInfo.color = new Color(1f, 0.85f, 0.6f);
            for (int i = 0; i < Gameplay.Homes.PlayerHome.MaxProperties; i++)
            {
                int slot = i;
                Button leave = MakeButton("QuitterBien_" + i, body, string.Empty, 16, Red, new Vector2(0.5f, 1f), new Vector2(210f, 50f), new Vector2(i == 0 ? -110f : 110f, -450f));
                _leavePropertyButtons[i] = leave;
                _leavePropertyLabels[i] = leave.GetComponentInChildren<Text>();
                leave.onClick.AddListener(() =>
                {
                    var home = Gameplay.Homes.PlayerHome.Local;
                    if (home == null)
                        return;
                    // Deux appuis : on confirme (revente à moitié prix, bail résilié…).
                    if (_leaveConfirmSlot != slot || Time.unscaledTime > _leaveConfirmUntil)
                    {
                        _leaveConfirmSlot = slot;
                        _leaveConfirmUntil = Time.unscaledTime + 3f;
                        _leavePropertyLabels[slot].text = "Sûr ? Touche encore";
                        return;
                    }
                    _leaveConfirmUntil = 0f;
                    home.RequestLeaveProperty(home.PropertyAt(slot));
                });
            }
            _leaveColocButton = MakeButton("QuitterColoc", body, "Quitter la colocation", 16, Dark, new Vector2(0.5f, 1f), new Vector2(210f, 50f), new Vector2(-110f, -506f));
            _leaveColocButton.onClick.AddListener(() => Gameplay.Homes.PlayerHome.Local?.RequestLeaveColocation());
            _moveInButton = MakeButton("Emmenager", body, "Faire emménager quelqu'un", 16, new Color(0.25f, 0.45f, 0.30f, 1f),
                new Vector2(0.5f, 1f), new Vector2(210f, 50f), new Vector2(110f, -506f));
            _moveInButton.onClick.AddListener(() => { _nearbyForMoveIn = true; ShowPage(Page.AddFriend); });
            scroll.SetContentHeight(576f);

            // Domicile principal (dès 2 domiciles) : on fait défiler ses logements.
            _primaryButton = MakeButton("Principal", page, string.Empty, 17, new Color(0.55f, 0.42f, 0.15f, 1f), new Vector2(0.5f, 0f), new Vector2(430f, 56f), new Vector2(0f, 78f));
            _primaryLabel = _primaryButton.GetComponentInChildren<Text>();
            _primaryButton.onClick.AddListener(CyclePrimaryHome);

            _doorButton = MakeButton("Porte", page, string.Empty, 17, new Color(0.45f, 0.30f, 0.18f, 1f), new Vector2(0.5f, 0f), new Vector2(210f, 60f), new Vector2(-110f, 10f));
            _doorLabel = _doorButton.GetComponentInChildren<Text>();
            _doorButton.onClick.AddListener(() => Gameplay.Homes.PlayerHome.Local?.RequestToggleDoor());

            Button gps = MakeButton("Gps", page, "GPS domicile", 18, Dark, new Vector2(0.5f, 0f), new Vector2(210f, 60f), new Vector2(110f, 10f));
            gps.onClick.AddListener(() =>
            {
                Gameplay.Homes.PlayerHome home = Gameplay.Homes.PlayerHome.Local;
                int key = home != null ? home.PrimaryKey : 0;
                if (key >= Gameplay.Homes.PlayerHome.PropertyKeyBase)
                    MapHud.SuggestWaypoint(Gameplay.Homes.PropertyCatalog.EntrancePosition(key - Gameplay.Homes.PlayerHome.PropertyKeyBase));
                else if (Gameplay.Homes.HomeEntrance.Residence != null)
                    MapHud.SuggestWaypoint(Gameplay.Homes.HomeEntrance.Residence.transform.position);
                _phone.gameObject.SetActive(false);
            });
        }

        private void OnBagChanged()
        {
            if (!_phone.gameObject.activeSelf)
                return;
            if (_page == Page.Bag)
                FillBag();
            else if (_page == Page.Djassa)
                FillDjassaSell();
        }

        private void OnHomeBarClicked()
        {
            if (PlayerPhone.Local == null)
            {
                _phone.gameObject.SetActive(false);
                return;
            }
            ShowPage(Page.Home, _page != Page.Home, -1);
        }

        private void RegisterPageScroll(Page page, PhoneScrollView scroll)
        {
            if (scroll != null)
                _pageScrolls[page] = scroll;
        }

        private static void SetPageSlideOffset(RectTransform rect, float xOffset)
        {
            if (rect == null)
                return;
            rect.offsetMin = new Vector2(xOffset, 56f);
            rect.offsetMax = new Vector2(xOffset, -66f);
        }

        private void FinishSlideTransition()
        {
            if (_slidePrevPage != null)
            {
                SetPageSlideOffset(_slidePrevPage, 0f);
                if (!_pages.TryGetValue(_page, out RectTransform activeRect) || _slidePrevPage != activeRect)
                    _slidePrevPage.gameObject.SetActive(false);
                _slidePrevPage = null;
            }
            if (_slideCurrPage != null)
            {
                SetPageSlideOffset(_slideCurrPage, 0f);
                _slideCurrPage = null;
            }
        }

        private void UpdatePageSlideTransition()
        {
            if (_slideCurrPage == null)
                return;
            _slideTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_slideTimer / PageSlideDuration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            float currX = Mathf.Lerp(_slideDirection * ScreenLogicalWidth, 0f, ease);
            SetPageSlideOffset(_slideCurrPage, currX);
            if (_slidePrevPage != null)
                SetPageSlideOffset(_slidePrevPage, Mathf.Lerp(0f, -_slideDirection * ScreenLogicalWidth, ease));
            if (t >= 1f)
                FinishSlideTransition();
        }

        private void Back()
        {
            switch (_page)
            {
                case Page.Settings:
                    if (PlayerPhone.Local == null)
                        _phone.gameObject.SetActive(false);
                    else
                        ShowPage(Page.Home, true, -1);
                    break;
                case Page.Profile:
                {
                    Page target = _profileReturnPage != Page.Profile ? _profileReturnPage : Page.Home;
                    _profileReturnPage = Page.Home;
                    ShowPage(target, true, -1);
                    break;
                }
                case Page.PingoinAmount: case Page.History: ShowPage(Page.Pingoin, true, -1); break;
                case Page.AddFriend: ShowPage(_nearbyForMoveIn ? Page.Housing : Page.Contacts, true, -1); break;
                case Page.Conversation: ShowPage(Page.Messages, true, -1); break;
                default: ShowPage(Page.Home, true, -1); break;
            }
        }

        #endregion

        #region Événements

        private void OnDirectory(int[] ids, string[] names)
        {
            _directoryIds = ids ?? new int[0];
            _directoryNames = names ?? new string[0];
            FillPayees();
        }

        #region Amis et messages

        private void OnFriends(string[] keys, string[] names, int[] online)
        {
            _friendKeys = keys ?? new string[0];
            _friendNames = names ?? new string[0];
            _friendOnline = online ?? new int[0];
            for (int i = 0; i < _friendKeys.Length; i++)
                s_conversationNames[_friendKeys[i]] = _friendNames[i];
            RefreshContactsView();
            FillMessages();
        }

        private void FillMessages()
        {
            int count = Mathf.Min(_friendKeys.Length, ListSlots);
            for (int i = 0; i < ListSlots; i++)
            {
                bool used = i < count;
                _messageButtons[i].gameObject.SetActive(used);
                if (!used)
                    continue;
                _messageKeys[i] = _friendKeys[i];
                string last = s_conversations.TryGetValue(_friendKeys[i], out var lines) && lines.Count > 0
                    ? lines[lines.Count - 1].text : "Aucun message";
                if (last.Length > 34)
                    last = last.Substring(0, 34) + "…";
                _messageLabels[i].text = _friendNames[i] + "\n<size=17><color=#bbb>" + Escape(last) + "</color></size>";
            }
            _messagesHint.text = _friendKeys.Length == 0 ? "Ajoute des amis pour leur écrire." : "Messages privés : seul ton ami les voit.";
        }

        private static string Escape(string text) => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "‹").Replace(">", "›");

        private void OpenConversation(int slot)
        {
            _openConversation = _messageKeys[slot];
            ShowPage(Page.Conversation);
        }

        private void FillConversation()
        {
            if (_openConversation == null)
                return;
            _conversationTitle.text = s_conversationNames.TryGetValue(_openConversation, out string name) ? name : "Conversation";
            s_conversations.TryGetValue(_openConversation, out var lines);
            int count = lines?.Count ?? 0;
            for (int i = 0; i < ConversationLines; i++)
            {
                int index = count - ConversationLines + i;
                bool used = index >= 0 && index < count;
                _conversationLines[i].gameObject.SetActive(used);
                if (!used)
                    continue;
                (string text, bool outgoing) = lines[index];
                _conversationLines[i].alignment = outgoing ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                _conversationLines[i].color = outgoing ? new Color(0.65f, 0.95f, 0.70f) : Color.white;
                _conversationLines[i].text = Escape(text);
            }
            _unread = 0;
            RefreshPhoneBadge();
        }

        private void SendConversationMessage()
        {
            string text = _conversationInput.text;
            if (_openConversation == null || string.IsNullOrWhiteSpace(text))
                return;
            PlayerFriends.Local?.SendPrivateMessage(_openConversation, text);
            _conversationInput.text = string.Empty;
        }

        private void OnPrivateMessage(string key, string name, string text, bool outgoing)
        {
            if (!s_conversations.TryGetValue(key, out var lines))
                s_conversations[key] = lines = new List<(string, bool)>();
            lines.Add((text, outgoing));
            if (lines.Count > 40)
                lines.RemoveAt(0);
            s_conversationNames[key] = name;

            bool visible = _phone.gameObject.activeSelf && _page == Page.Conversation && _openConversation == key;
            if (visible)
                FillConversation();
            else if (!outgoing)
            {
                _unread++;
                RefreshPhoneBadge();
                ShowBanner(name + " t'écrit : « " + (text.Length > 40 ? text.Substring(0, 40) + "…" : text) + " »", -1);
            }
            if (_phone.gameObject.activeSelf && _page == Page.Messages)
                FillMessages();
        }

        private void RefreshPhoneBadge()
        {
            _phoneButton.GetComponentInChildren<Text>().text = _unread > 0 ? "Téléphone (" + _unread + ")" : "Téléphone";
        }

        // La page « joueurs proches » sert aussi à faire emménager quelqu'un (depuis Logement).
        private bool _nearbyForMoveIn;

        /// <summary>Joueurs à proximité : demande d'ami, ou proposition d'emménager chez moi.</summary>
        private void FillNearby()
        {
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            int count = 0;
            var players = PlayerNetworkIdentity.ClientInstances;
            for (int i = 0; i < players.Count && count < NearbySlots && local != null; i++)
            {
                PlayerNetworkIdentity other = players[i];
                if (other == null || other == local || !other.Owner.IsValid)
                    continue;
                if ((other.transform.position - local.transform.position).sqrMagnitude > PlayerFriends.RequestRange * PlayerFriends.RequestRange)
                    continue;
                _nearbyIds[count] = other.ObjectId;
                _nearbyLabels[count].text = other.DisplayName;
                count++;
            }
            for (int i = 0; i < NearbySlots; i++)
                _nearbyButtons[i].gameObject.SetActive(i < count);
            _nearbyHint.text = count == 0
                ? "Personne à moins de " + Mathf.RoundToInt(PlayerFriends.RequestRange) + " m. Approche-toi d'un joueur."
                : _nearbyForMoveIn
                    ? "Touche un joueur pour lui proposer d'emménager chez toi."
                    : "Touche un joueur pour lui envoyer une demande d'ami.";
        }

        private void OnFriendRequest(int fromObjectId, string fromName)
        {
            ShowBanner(fromName + " veut devenir ton ami.", fromObjectId);
            _requestIsBoarding = false;
            _requestIsMoveIn = false;
        }

        private bool _requestIsBoarding;

        private void OnBoardingRequest(int fromObjectId, string fromName)
        {
            ShowBanner(fromName + " veut monter dans ton taxi.", fromObjectId);
            _requestIsBoarding = true;
            _requestIsMoveIn = false;
        }

        private bool _requestIsMoveIn;

        private void OnMoveInOffer(int fromObjectId, string fromName, string place)
        {
            ShowBanner(fromName + " te propose d'emménager chez lui : " + place + ".", fromObjectId);
            _requestIsBoarding = false;
            _requestIsMoveIn = true;
        }

        /// <summary>Bandeau en haut de l'écran : demande d'ami (Accepter / Refuser) ou nouveau message.</summary>
        private void ShowBanner(string text, int requestFrom)
        {
            _requestText.text = text;
            _requestFrom = requestFrom;
            bool isRequest = requestFrom >= 0;
            _requestPanel.Find("Accepter").gameObject.SetActive(isRequest);
            _requestPanel.Find("Refuser").gameObject.SetActive(isRequest);
            _requestPanel.Find("Ok").gameObject.SetActive(!isRequest);
            _requestPanel.gameObject.SetActive(true);
        }

        private void AnswerRequest(bool accept)
        {
            if (_requestFrom >= 0)
            {
                if (_requestIsBoarding)
                    Gameplay.Vehicles.PlayerDriving.Local?.RespondBoarding(_requestFrom, accept);
                else if (_requestIsMoveIn)
                    Gameplay.Homes.PlayerHome.Local?.RespondInvite(_requestFrom, accept);
                else
                    PlayerFriends.Local?.Respond(_requestFrom, accept);
            }
            _requestFrom = -1;
            _requestIsBoarding = false;
            _requestIsMoveIn = false;
            _requestPanel.gameObject.SetActive(false);
        }

        #endregion

        #region Historique, commandes, emplois

        private void OnHistory(long[] times, long[] deltas, string[] reasons)
        {
            int count = Mathf.Min(times.Length, HistorySlots);
            for (int i = 0; i < HistorySlots; i++)
            {
                bool used = i < count;
                _historyDates[i].transform.parent.gameObject.SetActive(used);
                if (!used)
                    continue;
                DateTime date = DateTimeOffset.FromUnixTimeSeconds(times[i]).ToLocalTime().DateTime;
                _historyDates[i].text = date.ToString("dd/MM HH:mm");
                _historyReasons[i].text = reasons[i];
                bool income = deltas[i] > 0;
                _historyAmounts[i].text = (income ? "+" : "−") + Money.Format(Math.Abs(deltas[i]));
                _historyAmounts[i].color = income ? new Color(0.45f, 0.95f, 0.55f) : new Color(1f, 0.55f, 0.50f);
            }
            _historyHint.text = times.Length == 0 ? "Aucune opération pour l'instant." : "Dernières opérations (les plus récentes en haut).";
        }

        /// <summary>
        /// Taxi : état de ma commande (et bouton Annuler), sinon le mode d'emploi et le taxi libre le plus
        /// proche (calcul local, gratuit).
        /// </summary>
        private void RefreshTaxiInfo()
        {
            PlayerServices services = PlayerServices.Local;
            PlayerServices.TaxiState state = services != null ? services.Taxi : PlayerServices.TaxiState.None;
            bool pending = state == PlayerServices.TaxiState.Searching || state == PlayerServices.TaxiState.Coming;
            if (_taxiCancel.gameObject.activeSelf != pending)
                _taxiCancel.gameObject.SetActive(pending);
            switch (state)
            {
                case PlayerServices.TaxiState.Searching:
                    _taxiInfo.text = "Recherche d'un chauffeur…";
                    return;
                case PlayerServices.TaxiState.Coming:
                    _taxiInfo.text = "Ton taxi arrive ! Reste où tu es.";
                    return;
                case PlayerServices.TaxiState.Riding:
                    _taxiInfo.text = "En route. Tu paies à l'arrivée.";
                    return;
            }

            const string howTo = "Commander : utilise l'appli Taxi ou marque ta destination sur la carte.";
            _taxiInfo.text = TaxiRideModel.TryDescribeNearestFreeTaxi(out int meters, out string direction)
                ? howTo + "\nTaxi libre à " + meters + " m " + direction + " (à conduire toi-même)."
                : howTo;
        }

        private Button _taxiCancel;

        #endregion

        /// <summary>Destinataires Pingoin : les joueurs tout proches d'abord (marqués « à côté »).</summary>
        private void FillPayees()
        {
            var order = new List<int>(_directoryIds.Length);
            var near = new List<bool>(_directoryIds.Length);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < _directoryIds.Length; i++)
                {
                    bool isNear = IsNearby(_directoryIds[i]);
                    if (isNear == (pass == 0))
                    {
                        order.Add(i);
                        near.Add(isNear);
                    }
                }
            }

            int count = Mathf.Min(order.Count, PayeeSlots);
            for (int slot = 0; slot < PayeeSlots; slot++)
            {
                bool used = slot < count;
                _payeeButtons[slot].gameObject.SetActive(used);
                if (!used)
                    continue;
                int index = order[slot];
                _payeeIds[slot] = _directoryIds[index];
                _payeeNames[slot] = _directoryNames[index];
                _payeeLabels[slot].text = near[slot] ? _directoryNames[index] + "   • à côté" : _directoryNames[index];
            }
            _walletHint.text = order.Count == 0 ? "Aucun autre joueur connecté." : "Envoyer de l'argent à :";
        }

        private static bool IsNearby(int objectId)
        {
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local == null)
                return false;
            var players = PlayerNetworkIdentity.ClientInstances;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerNetworkIdentity other = players[i];
                if (other != null && other.ObjectId == objectId)
                    return (other.transform.position - local.transform.position).sqrMagnitude < 8f * 8f;
            }
            return false;
        }

        private void OnCallState(CallState state, int peer, string peerName, string reason)
        {
            _state = state;
            _stateSince = Time.unscaledTime;
            _shownSeconds = -1;
            if (!string.IsNullOrEmpty(reason))
            {
                _notice = reason;
                _noticeUntil = Time.unscaledTime + 4f;
            }

            switch (state)
            {
                case CallState.Calling:
                    SetCall("Appel de " + peerName + "…", "Ça sonne.", false, "Raccrocher", false);
                    PlayTone(_ringback);
                    break;
                case CallState.Ringing:
                    SetCall(peerName + " t'appelle", "Appel entrant", true, "Refuser", false);
                    PlayTone(_ringtoneOn ? _ringtone : null);
                    break;
                case CallState.Connected:
                    SetCall("En ligne avec " + peerName, "00:00", false, "Raccrocher", true);
                    PlayTone(null);
                    break;
                default:
                    SetCall(_notice ?? string.Empty, string.Empty, false, null, false);
                    PlayTone(null);
                    VoiceChat.Muted = false;
                    break;
            }
        }

        #endregion

        #region Actions

        private void CallContact(int slot)
        {
            if (_contactIds[slot] < 0)
            {
                _contactsHint.text = "Cet ami n'est pas connecté pour l'instant.";
                return;
            }
            PlayerPhone.Local?.Call(_contactIds[slot]);
            _phone.gameObject.SetActive(false);
        }

        private void MessageContact(int slot)
        {
            _openConversation = _contactKeys[slot];
            ShowPage(Page.Conversation);
        }

        private void ChoosePayee(int slot)
        {
            _recipientId = _payeeIds[slot];
            _recipientName = _payeeNames[slot];
            ShowPage(Page.PingoinAmount);
        }

        private void PressKey(string key)
        {
            if (key == "<")
            {
                if (_amountDigits.Length > 0)
                    _amountDigits = _amountDigits.Substring(0, _amountDigits.Length - 1);
            }
            else if (_amountDigits.Length + key.Length <= MaxAmountDigits && !(_amountDigits.Length == 0 && key[0] == '0'))
            {
                _amountDigits += key;
            }
            RefreshAmount();
        }

        private void SetPreset(long amount)
        {
            _amountDigits = amount.ToString();
            RefreshAmount();
        }

        private long TypedAmount => long.TryParse(_amountDigits, out long amount) ? amount : 0;

        private void RefreshAmount()
        {
            long amount = TypedAmount;
            _amountText.text = amount > 0 ? Money.Format(amount) : "0 FCFA";
            bool valid = amount >= PlayerTransfers.MinAmount && amount <= PlayerTransfers.MaxAmount;
            _amountSend.interactable = valid;
        }

        private void SendAmount()
        {
            long amount = TypedAmount;
            if (PlayerTransfers.Local == null || _recipientId < 0 || amount <= 0)
                return;
            PlayerTransfers.Local.RequestSend(_recipientId, amount);
            _amountDigits = string.Empty;
            ShowPage(Page.Pingoin);
            _walletHint.text = "Envoi de " + Money.Format(amount) + " à " + _recipientName + "…";
        }

        private void Accept() => PlayerPhone.Local?.Answer(true);

        private void HangUpOrRefuse()
        {
            PlayerPhone phone = PlayerPhone.Local;
            if (phone == null)
                return;
            if (_state == CallState.Ringing)
                phone.Answer(false);
            else
                phone.HangUp();
        }

        private void ToggleMute()
        {
            VoiceChat.Muted = !VoiceChat.Muted;
            _muteLabel.text = VoiceChat.Muted ? "Micro coupé" : "Micro";
        }

        #endregion

        #region Construction de l'interface

        private void SetCall(string title, string info, bool showAccept, string hangUpLabel, bool connected)
        {
            _callTitle.text = title;
            _callInfo.text = info;
            _acceptButton.gameObject.SetActive(showAccept);
            _hangUpButton.gameObject.SetActive(hangUpLabel != null);
            if (hangUpLabel != null)
                _hangUpLabel.text = hangUpLabel;
            _muteButton.gameObject.SetActive(connected);
            _levelFill.parent.gameObject.SetActive(connected);
            _muteLabel.text = VoiceChat.Muted ? "Micro coupé" : "Micro";
        }

        private void BuildUi()
        {
            var root = (RectTransform)transform;

            // Bouton sous « Chat », en haut à gauche.
            _phoneButton = MakeButton("PhoneButton", root, "Téléphone", 26, Dark, new Vector2(0f, 1f), new Vector2(170f, 70f), new Vector2(330f, -120f));
            _phoneButton.onClick.AddListener(TogglePhone);

            BuildPhone(root);
            BuildCallPanel(root);
            BuildRequestPanel(root);
            BuildResidencePanel(root);
        }

        private void BuildPhone(RectTransform root)
        {
            HudIconLibrary.EnsureInitialized();
            Sprite frameSprite = HudIconLibrary.PhoneFrame;
            bool hasFrame = frameSprite != null;

            // Châssis du téléphone (phone_icone.png, ratio exact 246:495) : à droite, à côté des boutons d'action.
            _phone = MakeImage("Smartphone", root, hasFrame ? Color.white : new Color(0.05f, 0.05f, 0.06f, 1f),
                new Vector2(1f, 0.5f), new Vector2(PhoneWidth, PhoneHeight), new Vector2(-260f, 0f));
            Image phoneImg = _phone.GetComponent<Image>();
            phoneImg.raycastTarget = true;
            if (hasFrame)
            {
                phoneImg.sprite = frameSprite;
                phoneImg.type = Image.Type.Simple;
                phoneImg.preserveAspect = true;
            }

            RectTransform screen;
            if (hasFrame)
            {
                // Zone écran exacte à l'intérieur du châssis phone_icone.png (246x495 : x=18..228, y=15..479).
                RectTransform clip = MakeImage("EcranClip", _phone, Color.white, Vector2.zero, Vector2.zero, Vector2.zero);
                clip.anchorMin = new Vector2(18f / 246f, 15f / 495f);
                clip.anchorMax = new Vector2(229f / 246f, 480f / 495f);
                clip.pivot = new Vector2(0.5f, 0.5f);
                clip.offsetMin = Vector2.zero;
                clip.offsetMax = Vector2.zero;
                Image clipImg = clip.GetComponent<Image>();
                clipImg.sprite = HudIconLibrary.RoundedBox ?? _rounded;
                clipImg.type = Image.Type.Sliced;
                clipImg.pixelsPerUnitMultiplier = 0.35f;
                var mask = clip.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = false;

                float visualScreenWidth = PhoneWidth * (211f / 246f);
                float visualScreenHeight = PhoneHeight * (465f / 495f);
                float screenScale = visualScreenWidth / ScreenLogicalWidth;
                float logicalHeight = visualScreenHeight / screenScale;

                screen = MakeImage("Ecran", clip, new Color(0f, 0f, 0f, 0f),
                    new Vector2(0.5f, 0.5f), new Vector2(ScreenLogicalWidth, logicalHeight), Vector2.zero);
                screen.localScale = new Vector3(screenScale, screenScale, 1f);
                screen.GetComponent<Image>().raycastTarget = true;
            }
            else
            {
                screen = MakeImage("Ecran", _phone, Wallpaper,
                    new Vector2(0.5f, 0.5f), new Vector2(ScreenLogicalWidth, PhoneHeight - 28f), Vector2.zero);
                screen.GetComponent<Image>().raycastTarget = true;
                MakeImage("Encoche", screen, new Color(0.05f, 0.05f, 0.06f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(120f, 26f), new Vector2(0f, -6f));
            }

            // Barre d'état alignée de part et d'autre de la Dynamic Island (heure à gauche, réseau/wifi/batterie à droite).
            _clock = MakeText("Heure", screen, "10:15", 22, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(120f, 36f), new Vector2(22f, -14f));
            _clock.fontStyle = FontStyle.Bold;
            DrawStatusBarIcons(screen);

            BuildHome(screen);
            BuildProfile(screen);
            BuildContacts(screen);
            BuildPingoin(screen);
            BuildPingoinAmount(screen);
            BuildSettings(screen);
            BuildBag(screen);
            BuildAddFriend(screen);
            BuildMessages(screen);
            BuildConversation(screen);
            BuildHistory(screen);
            BuildOrders(screen);
            BuildJobs(screen);
            BuildHousing(screen);
            BuildRealEstate(screen);
            BuildLottery(screen);
            BuildWassup(screen);
            BuildGarage(screen);
            BuildDjassa(screen);
            BuildTaxi(screen);
            BuildCommerces(screen);

            // Bouton « accueil » en bas de l'écran.
            Button home = MakeButton("Accueil", screen, string.Empty, 20, new Color(1f, 1f, 1f, 0.35f), new Vector2(0.5f, 0f), new Vector2(150f, 12f), new Vector2(0f, 16f));
            home.onClick.AddListener(OnHomeBarClicked);
            var homeHitbox = MakeButton("AccueilZone", screen, string.Empty, 20, new Color(0f, 0f, 0f, 0f), new Vector2(0.5f, 0f), new Vector2(240f, 50f), new Vector2(0f, 0f));
            homeHitbox.onClick.AddListener(OnHomeBarClicked);

            _phone.gameObject.SetActive(false);
        }

        private RectTransform MakePage(Page page, RectTransform screen)
        {
            RectTransform rect = MakeImage(page.ToString(), screen, new Color(0f, 0f, 0f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(0f, 56f);   // bouton accueil
            rect.offsetMax = new Vector2(0f, -66f);  // barre d'état + Dynamic Island
            _pages[page] = rect;
            return rect;
        }

        /// <summary>En-tête d'application : bouton retour et titre.</summary>
        private void MakeHeader(RectTransform page, string title, Color color)
        {
            RectTransform header = MakeImage("EnTete", page, color, new Vector2(0.5f, 1f), new Vector2(ScreenLogicalWidth, 76f), Vector2.zero);
            MakeText("Titre", header, title, 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(300f, 70f), Vector2.zero);
            Button back = MakeButton("Retour", header, "‹", 48, new Color(0f, 0f, 0f, 0f), new Vector2(0f, 0.5f), new Vector2(80f, 76f), new Vector2(0f, 0f));
            back.onClick.AddListener(Back);
            Button close = MakeButton("Fermer", header, "×", 40, new Color(0f, 0f, 0f, 0f), new Vector2(1f, 0.5f), new Vector2(80f, 76f), new Vector2(0f, 0f));
            close.onClick.AddListener(() => _phone.gameObject.SetActive(false));
        }

        private void DrawStatusBarIcons(RectTransform screen)
        {
            // Barres de réseau cellulaire
            for (int i = 0; i < 4; i++)
            {
                float h = 6f + i * 3.5f;
                MakeImage("Sig_" + i, screen, Color.white, new Vector2(1f, 1f), new Vector2(4f, h), new Vector2(-96f + i * 6.5f, -38f + h * 0.5f));
            }
            // Icône Wi-Fi
            MakeImage("WifiTop", screen, Color.white, new Vector2(1f, 1f), new Vector2(16f, 4f), new Vector2(-58f, -24f));
            MakeImage("WifiMid", screen, Color.white, new Vector2(1f, 1f), new Vector2(11f, 4f), new Vector2(-58f, -29f));
            MakeImage("WifiDot", screen, Color.white, new Vector2(1f, 1f), new Vector2(5f, 5f), new Vector2(-58f, -34f));
            // Icône Batterie
            RectTransform bat = MakeImage("Battery", screen, new Color(1f, 1f, 1f, 0.5f), new Vector2(1f, 1f), new Vector2(26f, 13f), new Vector2(-28f, -29f));
            MakeImage("BatFill", bat, Color.white, new Vector2(0.5f, 0.5f), new Vector2(20f, 9f), Vector2.zero);
            MakeImage("BatTip", screen, Color.white, new Vector2(1f, 1f), new Vector2(3f, 6f), new Vector2(-13f, -29f));
        }

        private void BuildHome(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Home, screen);
            HudIconLibrary.EnsureInitialized();

            PhoneScrollView scroll = PhoneScrollView.Create(page, "HomeScroll", 0f, 0f);
            RegisterPageScroll(Page.Home, scroll);
            RectTransform grid = scroll.Content;

            // Grille de 3 colonnes dans un défilement fluide (16 applications).
            MakeAppPngIcon(grid, 0, "Profil", HudIconLibrary.AppProfil, () => OpenPlayerProfile(-1, Page.Home));
            MakeAppPngIcon(grid, 1, "Contacts", HudIconLibrary.AppContacts, () => ShowPage(Page.Contacts));
            MakeAppPngIcon(grid, 2, "Messages", HudIconLibrary.AppMessages, () => ShowPage(Page.Messages));

            MakeAppPngIcon(grid, 3, "Wassup?", HudIconLibrary.AppWassup, () => ShowPage(Page.Wassup));
            MakeAppPngIcon(grid, 4, "Pingoin", HudIconLibrary.AppPingoin, () => ShowPage(Page.Pingoin), cutoutSprite: true);
            MakeAppPngIcon(grid, 5, "Emplois", HudIconLibrary.AppEmploi, () => ShowPage(Page.Jobs));

            MakeAppPngIcon(grid, 6, "Taxi", HudIconLibrary.AppTaxi, () => ShowPage(Page.Taxi));
            MakeAppPngIcon(grid, 7, "Logement", HudIconLibrary.AppLogement, () => ShowPage(Page.Housing));
            MakeAppPngIcon(grid, 8, "Immobilier", HudIconLibrary.AppImmobilier, () => ShowPage(Page.RealEstate));

            MakeAppPngIcon(grid, 9, "Djassa", HudIconLibrary.AppDjassa, () => ShowPage(Page.Djassa));
            MakeAppPngIcon(grid, 10, "Loto", HudIconLibrary.AppLoto, () => ShowPage(Page.Lottery));
            MakeAppPngIcon(grid, 11, "Garage", HudIconLibrary.AppGarage, () => ShowPage(Page.Garage));

            MakeAppPngIcon(grid, 12, "Sac", HudIconLibrary.Backpack, () => ShowPage(Page.Bag), cutoutSprite: true,
                cutoutBg: new Color(0.76f, 0.42f, 0.14f, 1f));
            MakeAppPngIcon(grid, 13, "Commerces", HudIconLibrary.AppCommerces, () => ShowPage(Page.Commerces), cutoutSprite: true,
                cutoutBg: new Color(0.82f, 0.34f, 0.14f, 1f));
            MakeAppPngIcon(grid, 14, "Paramètres", HudIconLibrary.AppParametres, () => ShowPage(Page.Settings));
            MakeAppPngIcon(grid, 15, "Déconnexion", HudIconLibrary.AppLogout, LogoutFromPhone);

            scroll.SetContentHeight(6 * 190f + 56f);
        }

        /// <summary>Ferme le téléphone et déconnecte la session du compte joueur actif.</summary>
        public void LogoutFromPhone()
        {
            if (_phone != null)
                _phone.gameObject.SetActive(false);
            AuthFlowController.Instance?.Logout();
        }

        /// <summary>Silhouette (tête et épaules) pour les avatars de la liste Contacts.</summary>
        private void DrawPerson(RectTransform parent, Color color)
        {
            float s = parent.sizeDelta.x / 104f;
            MakeImage("Tete", parent, color, new Vector2(0.5f, 0.5f), new Vector2(34f, 34f) * s, new Vector2(0f, 16f) * s);
            MakeImage("Epaules", parent, color, new Vector2(0.5f, 0.5f), new Vector2(62f, 30f) * s, new Vector2(0f, -24f) * s);
        }

        private RectTransform MakeAppPngIcon(RectTransform page, int index, string appName, Sprite sprite, UnityEngine.Events.UnityAction onClick, bool cutoutSprite = false, Color? cutoutBg = null)
        {
            const float cellWidth = 144f;
            const float rowHeight = 190f;
            int column = index % 3, row = index / 3;
            var position = new Vector2((column - 1f) * cellWidth, -42f - row * rowHeight);

            RectTransform slot = MakeImage("App_" + appName, page, new Color(0f, 0f, 0f, 0f), new Vector2(0.5f, 1f), new Vector2(cellWidth, 160f), position);
            Color tileColor = cutoutSprite ? (cutoutBg ?? new Color(1f, 1f, 1f, 0.004f)) : Color.white;
            Button btn = MakeButton("Icone", slot, string.Empty, 20, tileColor, new Vector2(0.5f, 1f), new Vector2(110f, 110f), Vector2.zero);
            Image btnImg = btn.GetComponent<Image>();
            if (!cutoutSprite)
            {
                btnImg.sprite = sprite;
                btnImg.type = Image.Type.Simple;
                btnImg.preserveAspect = true;
            }
            else
            {
                if (!cutoutBg.HasValue)
                    btnImg.sprite = null;
                else if (btnImg.sprite == null)
                    btnImg.sprite = HudIconLibrary.RoundedBox;
                btnImg.type = cutoutBg.HasValue && btnImg.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                float iconSide = cutoutBg.HasValue ? 74f : 106f;
                RectTransform child = MakeImage("Sprite", btn.transform, Color.white, new Vector2(0.5f, 0.5f), new Vector2(iconSide, iconSide), Vector2.zero);
                Image childImg = child.GetComponent<Image>();
                childImg.sprite = sprite;
                childImg.type = Image.Type.Simple;
                childImg.preserveAspect = true;
            }
            btn.onClick.AddListener(onClick);
            MakeText("Nom", slot, appName, 21, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(cellWidth, 32f), new Vector2(0f, -116f));
            return slot;
        }

        private void BuildAddFriend(RectTransform screen)
        {
            RectTransform page = MakePage(Page.AddFriend, screen);
            MakeHeader(page, "Ajouter un ami", new Color(0.12f, 0.45f, 0.25f, 1f));
            _nearbyHint = MakeText("Info", page, string.Empty, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 50f), new Vector2(0f, -86f));
            PhoneScrollView scroll = PhoneScrollView.Create(page, "NearbyScroll", 138f, 6f);
            RegisterPageScroll(Page.AddFriend, scroll);
            for (int i = 0; i < NearbySlots; i++)
            {
                int slot = i;
                Button button = MakeButton("Proche_" + i, scroll.Content, "…", 26, Row, new Vector2(0.5f, 1f), new Vector2(430f, 70f), new Vector2(0f, -8f - i * 80f));
                Text label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(24f, 0f);
                MakeText("Demande", button.transform, "Demander", 20, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(120f, 60f), new Vector2(-14f, 0f)).color = new Color(0.4f, 1f, 0.5f);
                button.onClick.AddListener(() =>
                {
                    if (_nearbyForMoveIn)
                        Gameplay.Homes.PlayerHome.Local?.RequestInvite(_nearbyIds[slot]);
                    else
                        PlayerFriends.Local?.SendRequest(_nearbyIds[slot]);
                    _nearbyHint.text = (_nearbyForMoveIn ? "Proposition envoyée à " : "Demande envoyée à ") + _nearbyLabels[slot].text + ".";
                });
                _nearbyButtons[i] = button;
                _nearbyLabels[i] = label;
                button.gameObject.SetActive(false);
            }
            scroll.SetContentHeight(NearbySlots * 80f + 16f);
        }

        private void BuildMessages(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Messages, screen);
            MakeHeader(page, "Messages", new Color(0.15f, 0.40f, 0.75f, 1f));
            _messagesHint = MakeText("Info", page, string.Empty, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 40f), new Vector2(0f, -86f));
            for (int i = 0; i < ListSlots; i++)
            {
                int slot = i;
                Button button = MakeButton("Fil_" + i, page, "…", 24, Row, new Vector2(0.5f, 1f), new Vector2(430f, 76f), new Vector2(0f, -136f - i * 84f));
                Text label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.supportRichText = true; // dernier message échappé (Escape)
                label.rectTransform.offsetMin = new Vector2(24f, 0f);
                button.onClick.AddListener(() => OpenConversation(slot));
                _messageButtons[i] = button;
                _messageLabels[i] = label;
                button.gameObject.SetActive(false);
            }
        }

        private void BuildConversation(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Conversation, screen);
            MakeHeader(page, "Conversation", new Color(0.15f, 0.40f, 0.75f, 1f));
            _conversationTitle = page.Find("EnTete/Titre").GetComponent<Text>();
            for (int i = 0; i < ConversationLines; i++)
            {
                _conversationLines[i] = MakeText("Ligne_" + i, page, string.Empty, 21, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(430f, 58f), new Vector2(0f, -90f - i * 62f));
                _conversationLines[i].gameObject.SetActive(false);
            }

            RectTransform box = MakeImage("Saisie", page, new Color(1f, 1f, 1f, 0.12f), new Vector2(0f, 0f), new Vector2(320f, 64f), new Vector2(10f, 6f));
            box.GetComponent<Image>().raycastTarget = true;
            Text text = MakeText("Texte", box, string.Empty, 22, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(300f, 60f), Vector2.zero);
            Text hint = MakeText("Indice", box, "Ton message…", 22, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(300f, 60f), Vector2.zero);
            hint.color = new Color(1f, 1f, 1f, 0.4f);
            _conversationInput = box.gameObject.AddComponent<InputField>();
            _conversationInput.targetGraphic = box.GetComponent<Image>();
            _conversationInput.textComponent = text;
            _conversationInput.placeholder = hint;
            _conversationInput.characterLimit = PlayerFriends.MaxMessageLength;
            Button send = MakeButton("Envoyer", page, "Envoyer", 22, new Color(0.15f, 0.40f, 0.75f, 1f), new Vector2(1f, 0f), new Vector2(118f, 64f), new Vector2(-10f, 6f));
            send.onClick.AddListener(SendConversationMessage);
        }

        // --- Djassa déplacé dans PhoneHud.Djassa.cs ---

        // --- Garage : permis, voitures achetées, location ---
        private Text _garageLicense, _garageHint;
        private readonly Button[] _garageCars = new Button[Gameplay.Vehicles.PlayerGarage.Rules.MaxCars + 1];
        private readonly Text[] _garageCarLabels = new Text[Gameplay.Vehicles.PlayerGarage.Rules.MaxCars + 1];
        private readonly int[] _garageCarIndex = new int[Gameplay.Vehicles.PlayerGarage.Rules.MaxCars + 1];
        private static readonly Color GarageColor = new Color(0.30f, 0.32f, 0.38f, 1f);

        private void OnGarageChanged()
        {
            if (!_phone.gameObject.activeSelf)
                return;
            if (_page == Page.Garage)
                FillGarage();
            else if (_page == Page.Djassa)
                FillDjassaSell();
        }

        private void FillGarage()
        {
            var garage = Gameplay.Vehicles.PlayerGarage.Local;
            if (garage == null)
            {
                _garageLicense.text = "Garage indisponible (hors ligne).";
                foreach (Button button in _garageCars)
                    button.gameObject.SetActive(false);
                return;
            }
            _garageLicense.text = garage.HasLicense
                ? "<color=#8CFF9A>Permis de conduire : OK</color>"
                : "<color=#FF8A7A>Pas de permis</color> : passe-le à l'auto-école (" + Money.Format(Gameplay.Vehicles.PlayerGarage.Rules.LicensePrice) + ").";

            int row = 0;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (garage.RentalUntil > now)
            {
                long hours = (garage.RentalUntil - now) / 3600, minutes = (garage.RentalUntil - now) / 60 % 60;
                _garageCarIndex[row] = Gameplay.Vehicles.PlayerGarage.RentalIndex;
                _garageCarLabels[row].text = "Location : " + Gameplay.Vehicles.VehicleMotor.PaintNames[garage.RentalPaint] +
                                             " (" + hours + " h " + minutes + " min)  •  Faire venir";
                _garageCars[row++].gameObject.SetActive(true);
            }
            var cars = garage.Cars;
            for (int i = 0; i < cars.Count && row < _garageCars.Length; i++)
            {
                _garageCarIndex[row] = i;
                _garageCarLabels[row].text = Gameplay.Vehicles.VehicleMotor.PaintNames[cars[i]] + "  •  Faire venir";
                _garageCars[row++].gameObject.SetActive(true);
            }
            for (int i = row; i < _garageCars.Length; i++)
                _garageCars[i].gameObject.SetActive(false);
            _garageHint.text = row == 0
                ? "Aucune voiture. Achète ou loue une berline chez un concessionnaire."
                : "Touche une voiture pour la faire venir près de toi.";
        }

        /// <summary>Point du monde le plus proche d'un type donné (auto-école, concessionnaire…).</summary>
        private static bool TryFindNearest<T>(out Vector3 position) where T : Gameplay.Interaction.WorldInteractable
        {
            position = default;
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            float best = float.MaxValue;
            var all = Gameplay.Interaction.WorldInteractable.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is T))
                    continue;
                float sqr = local != null ? (all[i].transform.position - local.transform.position).sqrMagnitude : 0f;
                if (sqr < best)
                {
                    best = sqr;
                    position = all[i].transform.position;
                }
            }
            return best < float.MaxValue;
        }

        private void BuildGarage(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Garage, screen);
            page.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.11f, 1f);
            MakeHeader(page, "Garage", GarageColor);
            _garageLicense = MakeText("Permis", page, string.Empty, 19, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(430f, 56f), new Vector2(0f, -86f));
            _garageLicense.supportRichText = true;
            _garageLicense.horizontalOverflow = HorizontalWrapMode.Wrap;
            PhoneScrollView scroll = PhoneScrollView.Create(page, "GarageScroll", 142f, 198f);
            RegisterPageScroll(Page.Garage, scroll);
            for (int i = 0; i < _garageCars.Length; i++)
            {
                int row = i;
                Button button = MakeButton("Voiture_" + i, scroll.Content, string.Empty, 19, Row, new Vector2(0.5f, 1f), new Vector2(430f, 56f), new Vector2(0f, -8f - i * 62f));
                _garageCarLabels[i] = button.GetComponentInChildren<Text>();
                button.onClick.AddListener(() =>
                {
                    Gameplay.Vehicles.PlayerGarage.Local?.RequestSpawn(_garageCarIndex[row]);
                    _phone.gameObject.SetActive(false);
                });
                _garageCars[i] = button;
                button.gameObject.SetActive(false);
            }
            _garageHint = MakeText("Info", scroll.Content, string.Empty, 17, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(430f, 44f), new Vector2(0f, -8f - _garageCars.Length * 62f));
            _garageHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            scroll.SetContentHeight(_garageCars.Length * 62f + 64f);
            Button store = MakeButton("Ranger", page, "Ranger ma voiture", 20, Dark, new Vector2(0.5f, 0f), new Vector2(430f, 56f), new Vector2(0f, 136f));
            store.onClick.AddListener(() => Gameplay.Vehicles.PlayerGarage.Local?.RequestStore());
            Button school = MakeButton("GpsEcole", page, "GPS auto-école", 18, GarageColor, new Vector2(0.5f, 0f), new Vector2(210f, 56f), new Vector2(-110f, 72f));
            school.onClick.AddListener(() =>
            {
                if (TryFindNearest<Gameplay.Interaction.DrivingSchoolPoint>(out Vector3 position))
                    MapHud.SuggestWaypoint(position);
                _phone.gameObject.SetActive(false);
            });
            Button dealer = MakeButton("GpsConcession", page, "GPS concessionnaire", 18, GarageColor, new Vector2(0.5f, 0f), new Vector2(210f, 56f), new Vector2(110f, 72f));
            dealer.onClick.AddListener(() =>
            {
                if (TryFindNearest<Gameplay.Interaction.DealershipPoint>(out Vector3 position))
                    MapHud.SuggestWaypoint(position);
                _phone.gameObject.SetActive(false);
            });
        }

        // --- Wassup? : fil des publications (temporaires, 24 h) ---
        private const int WassupRows = 6;
        private readonly RectTransform[] _wassupRows = new RectTransform[WassupRows];
        private readonly Text[] _wassupAuthors = new Text[WassupRows];
        private readonly Text[] _wassupTexts = new Text[WassupRows];
        private readonly Text[] _wassupLikeLabels = new Text[WassupRows];
        private readonly int[] _wassupIds = new int[WassupRows];
        private InputField _wassupInput;
        private Text _wassupHint;
        private static readonly Color WassupColor = new Color(0.95f, 0.45f, 0.20f, 1f);

        private void OnWassupFeed(int[] ids, string[] authors, string[] texts, long[] times, int[] likes, bool[] liked)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            for (int i = 0; i < WassupRows; i++)
            {
                bool used = i < ids.Length;
                _wassupRows[i].gameObject.SetActive(used);
                if (!used)
                    continue;
                _wassupIds[i] = ids[i];
                long minutes = Math.Max(0, now - times[i]) / 60;
                string age = minutes < 1 ? "à l'instant" : minutes < 60 ? "il y a " + minutes + " min" : "il y a " + (minutes / 60) + " h";
                _wassupAuthors[i].text = authors[i] + "  •  " + age;
                _wassupTexts[i].text = texts[i];
                // « ♥ » rouge si j'aime déjà, gris sinon (le cœur vide n'existe pas dans toutes les polices).
                _wassupLikeLabels[i].text = "♥ " + likes[i];
                _wassupLikeLabels[i].color = liked[i] ? new Color(1f, 0.35f, 0.35f) : new Color(1f, 1f, 1f, 0.6f);
            }
            _wassupHint.text = ids.Length == 0 ? "Rien pour l'instant. Sois le premier à publier !" : string.Empty;
        }

        private void PublishWassup()
        {
            string text = _wassupInput.text;
            if (string.IsNullOrWhiteSpace(text))
                return;
            Gameplay.Social.PlayerWassup.Local?.Post(text);
            _wassupInput.text = string.Empty;
        }

        private void BuildWassup(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Wassup, screen);
            page.GetComponent<Image>().color = new Color(0.10f, 0.07f, 0.05f, 1f);
            MakeHeader(page, "Wassup?", WassupColor);
            _wassupHint = MakeText("Info", page, string.Empty, 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 30f), new Vector2(0f, -82f));

            for (int i = 0; i < WassupRows; i++)
            {
                int row = i;
                RectTransform card = MakeImage("Post_" + i, page, Row, new Vector2(0.5f, 1f), new Vector2(440f, 92f), new Vector2(0f, -116f - i * 98f));
                _wassupRows[i] = card;
                _wassupAuthors[i] = MakeText("Auteur", card, string.Empty, 15, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(330f, 22f), new Vector2(12f, -6f));
                _wassupAuthors[i].color = new Color(1f, 0.75f, 0.5f);
                _wassupTexts[i] = MakeText("Texte", card, string.Empty, 17, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(330f, 62f), new Vector2(12f, -28f));
                _wassupTexts[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                Button like = MakeButton("Aime", card, "♥ 0", 18, new Color(1f, 1f, 1f, 0.08f), new Vector2(1f, 0.5f), new Vector2(84f, 56f), new Vector2(-8f, 0f));
                _wassupLikeLabels[i] = like.GetComponentInChildren<Text>();
                like.onClick.AddListener(() => Gameplay.Social.PlayerWassup.Local?.Like(_wassupIds[row]));
                card.gameObject.SetActive(false);
            }

            RectTransform box = MakeImage("Saisie", page, new Color(1f, 1f, 1f, 0.12f), new Vector2(0f, 0f), new Vector2(320f, 64f), new Vector2(10f, 6f));
            box.GetComponent<Image>().raycastTarget = true;
            Text text = MakeText("Texte", box, string.Empty, 20, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(300f, 60f), Vector2.zero);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            Text hint = MakeText("Indice", box, "Quoi de neuf ? (24 h)", 20, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(300f, 60f), Vector2.zero);
            hint.color = new Color(1f, 1f, 1f, 0.4f);
            _wassupInput = box.gameObject.AddComponent<InputField>();
            _wassupInput.targetGraphic = box.GetComponent<Image>();
            _wassupInput.textComponent = text;
            _wassupInput.placeholder = hint;
            _wassupInput.characterLimit = Gameplay.Social.PlayerWassup.MaxLength;
            Button publish = MakeButton("Publier", page, "Publier", 22, WassupColor, new Vector2(1f, 0f), new Vector2(118f, 64f), new Vector2(-10f, 6f));
            publish.onClick.AddListener(PublishWassup);
        }

        private void BuildHistory(RectTransform screen)
        {
            RectTransform page = MakePage(Page.History, screen);
            page.GetComponent<Image>().color = PingoinDeep;
            MakeHeader(page, "Historique", PingoinBlue);
            _historyHint = MakeText("Info", page, string.Empty, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 40f), new Vector2(0f, -86f));
            PhoneScrollView scroll = PhoneScrollView.Create(page, "HistoryScroll", 128f, 6f);
            RegisterPageScroll(Page.History, scroll);
            for (int i = 0; i < HistorySlots; i++)
            {
                RectTransform row = MakeImage("Op_" + i, scroll.Content, new Color(1f, 1f, 1f, 0.07f), new Vector2(0.5f, 1f), new Vector2(430f, 62f), new Vector2(0f, -6f - i * 68f));
                _historyDates[i] = MakeText("Date", row, string.Empty, 16, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(200f, 22f), new Vector2(14f, -6f));
                _historyDates[i].color = new Color(1f, 1f, 1f, 0.6f);
                _historyReasons[i] = MakeText("Motif", row, string.Empty, 19, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(280f, 30f), new Vector2(14f, 6f));
                _historyAmounts[i] = MakeText("Montant", row, string.Empty, 22, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(150f, 50f), new Vector2(-14f, 0f));
                row.gameObject.SetActive(false);
            }
            scroll.SetContentHeight(HistorySlots * 68f + 16f);
        }

        private void BuildOrders(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Orders, screen);
            page.GetComponent<Image>().color = new Color(0.16f, 0.09f, 0.06f, 1f);
            MakeHeader(page, "Commandes", new Color(0.85f, 0.35f, 0.15f, 1f));
            MakeText("Info", page, "Repas livrés dans ton sac (+" + Money.Format(PlayerServices.Catalog.DeliveryFee) + " de livraison)", 18,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 40f), new Vector2(0f, -84f));
            var dishes = PlayerServices.Catalog.Dishes;
            for (int i = 0; i < dishes.Length; i++)
            {
                int index = i;
                Button button = MakeButton("Plat_" + i, page, string.Empty, 20, Row, new Vector2(0.5f, 1f), new Vector2(430f, 66f), new Vector2(0f, -128f - i * 72f));
                MakeText("Nom", button.transform, dishes[i].Name, 22, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(290f, 32f), new Vector2(16f, 10f));
                MakeText("Resto", button.transform, dishes[i].Restaurant, 16, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(290f, 24f), new Vector2(16f, -17f)).color = new Color(1f, 1f, 1f, 0.6f);
                MakeText("Prix", button.transform, Money.Format(dishes[i].Price), 20, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(130f, 60f), new Vector2(-12f, 0f)).color = new Color(1f, 0.75f, 0.35f);
                button.onClick.AddListener(() => PlayerServices.Local?.OrderFood(index));
            }
            RectTransform taxi = MakeImage("Taxi", page, new Color(0.95f, 0.75f, 0.10f, 0.20f), new Vector2(0.5f, 0f), new Vector2(430f, 120f), new Vector2(0f, 8f));
            MakeText("Titre", taxi, "TAXI", 20, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(200f, 26f), new Vector2(14f, -8f)).color = new Color(1f, 0.85f, 0.3f);
            _taxiInfo = MakeText("Infos", taxi, string.Empty, 17, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(400f, 76f), new Vector2(0f, -14f));
            _taxiInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            _taxiCancel = MakeButton("Annuler", taxi, "Annuler", 18, Red, new Vector2(1f, 1f), new Vector2(120f, 34f), new Vector2(-8f, -6f));
            _taxiCancel.onClick.AddListener(() => PlayerServices.Local?.CancelTaxi());
            _taxiCancel.gameObject.SetActive(false);
        }

        private void BuildJobs(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Jobs, screen);
            page.GetComponent<Image>().color = new Color(0.07f, 0.12f, 0.10f, 1f);
            MakeHeader(page, "Emplois & Statuts", new Color(0.20f, 0.55f, 0.40f, 1f));
            PhoneScrollView scroll = PhoneScrollView.Create(page, "JobsScroll", 78f, 6f);
            RegisterPageScroll(Page.Jobs, scroll);

            float y = -12f;
            var jobs = PlayerServices.Catalog.Jobs;
            for (int i = 0; i < jobs.Length; i++)
            {
                int index = i;
                RectTransform card = MakeImage("Offre_" + i, scroll.Content, Row, new Vector2(0.5f, 1f), new Vector2(430f, 176f), new Vector2(0f, y));
                MakeText("Titre", card, jobs[i].Title, 24, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(400f, 32f), new Vector2(16f, -12f));
                Text description = MakeText("Description", card, jobs[i].Description, 17, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(400f, 64f), new Vector2(16f, -46f));
                description.color = new Color(1f, 1f, 1f, 0.75f);
                description.horizontalOverflow = HorizontalWrapMode.Wrap;
                Button accept = MakeButton("Accepter", card, jobs[i].Available ? "Accepter" : "Bientôt", 20,
                    jobs[i].Available ? new Color(0.20f, 0.65f, 0.40f, 1f) : new Color(0.35f, 0.35f, 0.38f, 1f),
                    new Vector2(1f, 0f), new Vector2(150f, 48f), new Vector2(-14f, 12f));
                accept.interactable = jobs[i].Available;
                accept.onClick.AddListener(() =>
                {
                    PlayerServices.Local?.AcceptJob(index);
                    _phone.gameObject.SetActive(false);
                });
                y -= 188f;
            }

            // Section Statut & Métier officiel (carte d'identité dynamique)
            MakeText("TitreStatuts", scroll.Content, "STATUT OFFICIEL & CARTE D'IDENTITÉ", 14, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(430f, 28f), new Vector2(0f, y - 6f)).color = new Color(0.65f, 0.95f, 0.80f, 0.95f);
            y -= 40f;

            var statuses = PlayerProfileMetaCatalog.Statuses;
            for (int i = 0; i < statuses.Length; i++)
            {
                ProfileStatusVisual meta = statuses[i];
                RectTransform card = MakeImage("Statut_" + meta.Status, scroll.Content, Row,
                    new Vector2(0.5f, 1f), new Vector2(430f, 76f), new Vector2(0f, y));

                RectTransform chip = MakeImage("IconBg", card, meta.PillBgColor,
                    new Vector2(0f, 0.5f), new Vector2(48f, 48f), new Vector2(12f, 0f));
                RectTransform ic = MakeImage("Icon", chip, Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(30f, 30f), Vector2.zero);
                Image icImg = ic.GetComponent<Image>();
                icImg.sprite = HudIconLibrary.GetProfileIcon(meta.IconKey);
                icImg.type = Image.Type.Simple;
                icImg.preserveAspect = true;

                MakeText("Nom", card, meta.Label, 18, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(236f, 26f), new Vector2(70f, -12f)).fontStyle = FontStyle.Bold;
                Text sub = MakeText("Role", card, meta.RoleSummary, 14, TextAnchor.LowerLeft,
                    new Vector2(0f, 0f), new Vector2(236f, 24f), new Vector2(70f, 12f));
                sub.color = new Color(1f, 1f, 1f, 0.68f);

                Button btn = MakeButton("Activer", card, "Choisir", 14, meta.PillBgColor,
                    new Vector2(1f, 0.5f), new Vector2(104f, 42f), new Vector2(-12f, 0f));
                PlayerJobStatus captured = meta.Status;
                btn.onClick.AddListener(() =>
                {
                    PlayerNetworkIdentity.Local?.RequestSetJobStatus(captured);
                    RefreshJobStatusCards(captured);
                });

                _jobStatusButtons[i] = btn;
                _jobStatusButtonBg[i] = btn.GetComponent<Image>();
                _jobStatusButtonLabel[i] = btn.GetComponentInChildren<Text>();
                y -= 84f;
            }

            scroll.SetContentHeight(-y + 18f);
        }

        private void RefreshJobStatusCards(PlayerJobStatus? optimistic = null)
        {
            PlayerJobStatus active = optimistic ?? (PlayerNetworkIdentity.Local != null
                ? PlayerNetworkIdentity.Local.JobStatus
                : PlayerJobStatus.Civil);

            var statuses = PlayerProfileMetaCatalog.Statuses;
            for (int i = 0; i < statuses.Length && i < _jobStatusButtons.Length; i++)
            {
                if (_jobStatusButtons[i] == null)
                    continue;
                bool isCurrent = statuses[i].Status == active;
                _jobStatusButtons[i].interactable = !isCurrent;
                _jobStatusButtonBg[i].color = isCurrent ? new Color(0.18f, 0.62f, 0.36f, 1f) : statuses[i].PillBgColor;
                _jobStatusButtonLabel[i].text = isCurrent ? "Actif" : "Choisir";
            }
        }

        /// <summary>Bandeau de demande d'ami / nouveau message, en haut de l'écran (visible téléphone fermé).</summary>
        private void BuildRequestPanel(RectTransform root)
        {
            _requestPanel = MakeImage("Notification", root, new Color(0.05f, 0.06f, 0.09f, 0.94f), new Vector2(0.5f, 1f), new Vector2(720f, 150f), new Vector2(0f, -150f));
            _requestPanel.GetComponent<Image>().raycastTarget = true;
            _requestText = MakeText("Texte", _requestPanel, string.Empty, 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(680f, 60f), new Vector2(0f, -10f));
            _requestText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Button accept = MakeButton("Accepter", _requestPanel, "Accepter", 24, Green, new Vector2(0f, 0f), new Vector2(240f, 62f), new Vector2(40f, 14f));
            accept.onClick.AddListener(() => AnswerRequest(true));
            Button refuse = MakeButton("Refuser", _requestPanel, "Refuser", 24, Red, new Vector2(1f, 0f), new Vector2(240f, 62f), new Vector2(-40f, 14f));
            refuse.onClick.AddListener(() => AnswerRequest(false));
            Button ok = MakeButton("Ok", _requestPanel, "OK", 24, Dark, new Vector2(0.5f, 0f), new Vector2(200f, 62f), new Vector2(0f, 14f));
            ok.onClick.AddListener(() => _requestPanel.gameObject.SetActive(false));
            _requestPanel.gameObject.SetActive(false);
        }

        private void BuildPingoin(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Pingoin, screen);
            page.GetComponent<Image>().color = PingoinDeep;
            MakeHeader(page, "Pingoin", PingoinBlue);

            // Carte du solde (touchée : masquer / afficher).
            Button card = MakeButton("Solde", page, string.Empty, 20, PingoinBlue, new Vector2(0.5f, 1f), new Vector2(430f, 150f), new Vector2(0f, -92f));
            RectTransform logo = MakeImage("Logo", card.transform, Color.white, new Vector2(1f, 0.5f), new Vector2(92f, 92f), new Vector2(-12f, 0f));
            Image logoImg = logo.GetComponent<Image>();
            logoImg.sprite = HudIconLibrary.AppPingoin;
            logoImg.type = Image.Type.Simple;
            logoImg.preserveAspect = true;
            MakeText("Libelle", card.transform, "Mon solde  (toucher pour masquer)", 18, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(320f, 30f), new Vector2(20f, -16f));
            _walletBalance = MakeText("Montant", card.transform, string.Empty, 40, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(330f, 60f), new Vector2(20f, -6f));
            MakeText("Frais", card.transform, "Envoi gratuit, partout dans la ville", 16, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(330f, 26f), new Vector2(20f, 10f)).color = new Color(1f, 1f, 1f, 0.75f);
            card.onClick.AddListener(() => { _balanceHidden = !_balanceHidden; RefreshBalanceTexts(); });
            Button history = MakeButton("Historique", page, "Historique des dépenses et revenus", 20, PingoinBlue, new Vector2(0.5f, 0f), new Vector2(430f, 56f), new Vector2(0f, 6f));
            history.onClick.AddListener(() => ShowPage(Page.History));

            _walletHint = MakeText("Info", page, string.Empty, 22, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(430f, 40f), new Vector2(0f, -252f));
            for (int i = 0; i < PayeeSlots; i++) // la carte du solde prend la place de deux lignes
            {
                int slot = i;
                Button button = MakeButton("Destinataire_" + i, page, "…", 26, new Color(1f, 1f, 1f, 0.12f), new Vector2(0.5f, 1f), new Vector2(430f, 66f), new Vector2(0f, -298f - i * 74f));
                Text label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(24f, 0f);
                MakeText("Fleche", button.transform, "›", 40, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(50f, 60f), new Vector2(-14f, 0f));
                button.onClick.AddListener(() => ChoosePayee(slot));
                _payeeButtons[i] = button;
                _payeeLabels[i] = label;
                button.gameObject.SetActive(false);
            }
        }

        private void BuildPingoinAmount(RectTransform screen)
        {
            RectTransform page = MakePage(Page.PingoinAmount, screen);
            page.GetComponent<Image>().color = PingoinDeep;
            MakeHeader(page, "Envoyer", PingoinBlue);

            _amountTitle = MakeText("Destinataire", page, string.Empty, 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 40f), new Vector2(0f, -86f));
            _amountText = MakeText("Montant", page, "0 FCFA", 52, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 70f), new Vector2(0f, -128f));
            _amountBalance = MakeText("Solde", page, string.Empty, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 30f), new Vector2(0f, -198f));
            _amountBalance.color = new Color(1f, 1f, 1f, 0.7f);

            // Montants rapides.
            for (int i = 0; i < Presets.Length; i++)
            {
                long amount = Presets[i];
                Button preset = MakeButton("Rapide_" + amount, page, amount >= 1000 ? (amount / 1000) + " 000" : amount.ToString(), 22,
                    new Color(1f, 1f, 1f, 0.15f), new Vector2(0.5f, 1f), new Vector2(100f, 50f), new Vector2((i - 1.5f) * 108f, -236f));
                preset.onClick.AddListener(() => SetPreset(amount));
            }

            // Pavé numérique.
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "00", "0", "<" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                int column = i % 3, row = i / 3;
                Button button = MakeButton("Touche_" + key, page, key == "<" ? "←" : key, 34, new Color(1f, 1f, 1f, 0.08f),
                    new Vector2(0.5f, 1f), new Vector2(136f, 66f), new Vector2((column - 1) * 144f, -306f - row * 74f));
                button.onClick.AddListener(() => PressKey(key));
            }

            _amountSend = MakeButton("Envoyer", page, "Envoyer", 30, new Color(0.15f, 0.75f, 0.95f, 1f), new Vector2(0.5f, 0f), new Vector2(430f, 76f), new Vector2(0f, 8f));
            _amountSend.onClick.AddListener(SendAmount);
        }

        private void BuildCallPanel(RectTransform root)
        {
            // Appel (entrant, sortant, en cours) : en haut au centre, sous la barre de chat.
            _callPanel = MakeImage("Call", root, new Color(0f, 0f, 0f, 0.8f), new Vector2(0.5f, 1f), new Vector2(660f, 210f), new Vector2(0f, -300f));
            _callPanel.GetComponent<Image>().raycastTarget = true;
            _callTitle = MakeText("Title", _callPanel, string.Empty, 32, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(640f, 46f), new Vector2(0f, -14f));
            _callInfo = MakeText("Info", _callPanel, string.Empty, 24, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(640f, 34f), new Vector2(0f, -62f));
            _acceptButton = MakeButton("Accept", _callPanel, "Décrocher", 28, Green, new Vector2(0f, 0f), new Vector2(280f, 76f), new Vector2(30f, 20f));
            _acceptButton.onClick.AddListener(Accept);
            _hangUpButton = MakeButton("HangUp", _callPanel, "Raccrocher", 28, Red, new Vector2(1f, 0f), new Vector2(280f, 76f), new Vector2(-30f, 20f));
            _hangUpButton.onClick.AddListener(HangUpOrRefuse);
            _hangUpLabel = _hangUpButton.GetComponentInChildren<Text>();
            _muteButton = MakeButton("Mute", _callPanel, "Micro", 26, Dark, new Vector2(0f, 0f), new Vector2(220f, 76f), new Vector2(30f, 20f));
            _muteButton.onClick.AddListener(ToggleMute);
            _muteLabel = _muteButton.GetComponentInChildren<Text>();
            RectTransform level = MakeImage("Level", _callPanel, new Color(1f, 1f, 1f, 0.15f), new Vector2(0.5f, 0f), new Vector2(110f, 14f), new Vector2(-55f, 50f));
            _levelFill = MakeImage("Fill", level, new Color(0.4f, 1f, 0.5f, 0.9f), Vector2.zero, Vector2.zero, Vector2.zero);
            _levelFill.anchorMin = Vector2.zero;
            _levelFill.anchorMax = new Vector2(0f, 1f);
            _levelFill.offsetMin = _levelFill.offsetMax = Vector2.zero;
            _callPanel.gameObject.SetActive(false);
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
            text.supportRichText = false; // les pseudos viennent d'autres joueurs
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
            Text text = MakeText("Label", rect, label, size, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), box, Vector2.zero);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        #endregion

        #region Sonneries

        private void PlayTone(AudioClip clip)
        {
            if (_ringer.clip == clip && _ringer.isPlaying)
                return;
            _ringer.Stop();
            _ringer.clip = clip;
            if (clip != null)
                _ringer.Play();
        }

        /// <summary>
        /// Sonnerie synthétisée : notes alternées de <paramref name="noteLength"/> s pendant
        /// <paramref name="onSeconds"/> s, puis silence jusqu'à <paramref name="period"/> s.
        /// </summary>
        private static AudioClip BuildTone(string name, float[] notes, float noteLength, float period, float onSeconds)
        {
            const int rate = 22050;
            int length = (int)(rate * period);
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                if (t > onSeconds)
                    break;
                float frequency = notes[(int)(t / noteLength) % notes.Length];
                float envelope = Mathf.Clamp01((onSeconds - t) * 20f) * Mathf.Clamp01(t * 50f);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.25f * envelope;
            }
            AudioClip clip = AudioClip.Create(name, length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        #endregion
    }

    /// <summary>Grande horloge de l'écran d'accueil (heure du jeu).</summary>
    internal sealed class PhoneClockLabel : MonoBehaviour
    {
        private Text _text;
        private string _shown;

        private void Awake() => _text = GetComponent<Text>();

        private void Update()
        {
            string clock = DayNightCycle.ClockText;
            if (clock != _shown)
            {
                _shown = clock;
                _text.text = clock;
            }
        }
    }
}
