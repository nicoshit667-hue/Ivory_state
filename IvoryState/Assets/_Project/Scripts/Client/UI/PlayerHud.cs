using IvoryState.Client.Input;
using IvoryState.Client.Settings;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// HUD principal du joueur, structuré selon la grille d'ancrage mobile open-world :
    /// <list type="bullet">
    /// <item><b>Haut-gauche</b> : 3 pastilles sombres contrastées avec icônes vectorielles SDF
    /// (<c>ECO</c>, <c>PINGOIN</c>, <c>ESPECE</c>), strictement isolées du panneau debug.</item>
    /// <item><b>Haut-droite</b> : bouton roue dentée ⚙ contrasté, mini-carte carrée, et 3 jauges fines
    /// (<c>SANTE</c>, <c>FAIM</c>, <c>SOIF</c>) avec icônes SDF.</item>
    /// <item><b>Bas-centre</b> : dock de 3 boutons carrés — <b>Téléphone</b>, <b>Sac</b>, et
    /// <b>Animation / Emote</b> (à pied, à côté du sac) ou <b>Sortir du véhicule</b> (en voiture).</item>
    /// <item><b>Bas-droite (à pied)</b> : cluster de 3 boutons circulaires contrastés — <b>Micro</b> (haut-gauche),
    /// <b>Saut</b> (à droite) et <b>Frapper</b> (bas-gauche).</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHud : MonoBehaviour
    {
        private const float RefreshInterval = 0.2f;
        private const float ToastDuration = 3.5f;
        private const byte LowSatiety = 20;

        [Header("Statut")]
        [SerializeField] private Text _moneyText;
        [SerializeField] private Text _clockText;
        [SerializeField] private Image _satietyFill;
        [SerializeField] private Image _hydrationFill;
        [SerializeField] private Image _healthFill;

        [Header("Action")]
        [SerializeField] private GameObject _interactButton;
        [SerializeField] private Text _interactLabel;

        [Header("Messages et mission")]
        [SerializeField] private Text _toastText;
        [SerializeField] private Text _deliveryText;

        [Tooltip("Matériau de la balise plantée sur l'adresse de livraison.")]
        [SerializeField] private Material _beaconMaterial;

        private float _nextRefresh;
        private float _toastHideAt;
        private long _shownBalance = long.MinValue;
        private long _shownCash = long.MinValue;
        private long _shownEco = long.MinValue;
        private int _shownSatiety = -1;
        private int _shownHydration = -1;
        private int _shownHealth = -1;
        private string _shownPrompt;
        private WorldInteractable _shownDeliveryTarget;
        private MissionKind _shownDeliveryMission;
        private int _shownDeliveryDistance = -1;
        private int _shownDeliverySeconds = -1;
        private Transform _beacon;

        private RectTransform _topLeftPanel;
        private Text _ecoLineText;
        private Text _pingoinLineText;
        private Text _especeLineText;
        private Text _healthLabelText;
        private Text _satietyLabelText;
        private Text _hydrationLabelText;

        private RectTransform _settingsGearBtn;
        private RectTransform _bottomDock;
        private GameObject _dockAnimIcon;
        private GameObject _dockExitIcon;
        private RectTransform _emotePopup;

        private RectTransform _micRoot;
        private RectTransform _punchBtn;
        private RectTransform _robBtn;
        private RectTransform _rightJumpBtn;
        private RectTransform _hudRect;
        private bool _lastInVehicle;
        private int _emoteIndex;
        private CanvasScaler _scaler;
        private Font _font;

        private static readonly string[] EmoteNames =
        {
            "👋 Salut d'Abidjan",
            "💃 Pas de Coupé-Décalé",
            "👏 Applaudissements",
            "🙌 Bras croisés (Relax)",
        };

        private static readonly Color HealthColor = new Color(0.24f, 0.82f, 0.36f);
        private static readonly Color SatietyOk = new Color(0.94f, 0.56f, 0.20f);
        private static readonly Color HydrationOk = new Color(0.24f, 0.68f, 0.94f);
        private static readonly Color NeedLow = new Color(0.95f, 0.20f, 0.15f);

        private static readonly Color StatPillBg = new Color(0.05f, 0.07f, 0.11f, 0.54f);
        private static readonly Color DockColor = new Color(0.08f, 0.10f, 0.14f, 0.86f);
        private static readonly Color ActionCircleBg = new Color(0.07f, 0.09f, 0.13f, 0.58f);
        private static readonly Color ActionCirclePressed = new Color(0.22f, 0.28f, 0.36f, 0.78f);
        private static readonly Color BorderCrisp = new Color(1f, 1f, 1f, 0.86f);
        private static readonly Color AmberAccent = new Color(0.91f, 0.51f, 0.10f, 1f);

        private void OnEnable()
        {
            PlayerEconomy.LocalNotification += ShowToast;
            GameSettings.Changed += ApplyHudScale;
        }

        private void OnDisable()
        {
            PlayerEconomy.LocalNotification -= ShowToast;
            GameSettings.Changed -= ApplyHudScale;
        }

        private void Start()
        {
            _font = _moneyText != null && _moneyText.font != null
                ? _moneyText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _scaler = GetComponent<CanvasScaler>();
            _hudRect = transform as RectTransform;
            HudIconLibrary.EnsureInitialized();

            _beacon = CreateBeacon();
            BuildModernHudLayout();
            ApplyHudScale();

            if (GetComponent<DrivingHud>() == null && DrivingHud.Instance == null)
                gameObject.AddComponent<DrivingHud>();

            _interactButton.SetActive(false);
            _toastText.gameObject.SetActive(false);
            _deliveryText.gameObject.SetActive(false);
            SetStatusVisible(false);
        }

        private void ApplyHudScale()
        {
            if (_scaler == null)
                return;
            float scale = Mathf.Clamp(GameSettings.HudScale, 0.8f, 1.3f);
            _scaler.referenceResolution = new Vector2(1920f / scale, 1080f / scale);
            Canvas.ForceUpdateCanvases();
            ApplyCustomHudLayout();
        }

        private void ApplyCustomHudLayout()
        {
            if (_hudRect == null)
                return;
            GameSettings.ApplyRectLayout(_rightJumpBtn, _hudRect, HudControlId.Jump, new Vector2(-112f, 216f));
            GameSettings.ApplyRectLayout(_punchBtn, _hudRect, HudControlId.Punch, new Vector2(-276f, 128f));
            GameSettings.ApplyRectLayout(_micRoot, _hudRect, HudControlId.Mic, new Vector2(-276f, 305f));
            GameSettings.ApplyRectLayout(_robBtn, _hudRect, HudControlId.Rob, new Vector2(-570f, 360f));
            GameSettings.ApplyRectLayout(_interactButton != null ? _interactButton.transform as RectTransform : null,
                _hudRect, HudControlId.Interact, new Vector2(0f, 134f));
            GameSettings.ApplyRectLayout(_bottomDock, _hudRect, HudControlId.BottomDock, new Vector2(0f, 24f));
        }

        private void ShowToast(string message)
        {
            _toastText.text = message;
            _toastText.gameObject.SetActive(true);
            _toastHideAt = Time.unscaledTime + ToastDuration;
        }

        private void Update()
        {
            if (_toastText.gameObject.activeSelf && Time.unscaledTime >= _toastHideAt)
                _toastText.gameObject.SetActive(false);

            PlayerDriving driving = PlayerDriving.Local;
            bool inVehicle = driving != null && driving.IsInVehicle;
            if (inVehicle != _lastInVehicle)
            {
                _lastInVehicle = inVehicle;
                UpdateVehicleModeVis(inVehicle);
            }

            if (Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;

            string clock = World.DayNightCycle.ClockText;
            if (_clockText != null && !ReferenceEquals(_clockText.text, clock))
                _clockText.text = clock;

            PlayerInteractor local = PlayerInteractor.Local;
            if (local == null)
            {
                SetStatusVisible(false);
                SetPrompt(null);
                SetDelivery(null, null);
                return;
            }

            SetStatusVisible(true);
            RefreshStatus(local);
            SetPrompt(inVehicle ? null : local.CurrentPrompt);
            SetDelivery(local, local.Economy);
        }

        private void UpdateVehicleModeVis(bool inVehicle)
        {
            if (_dockAnimIcon != null)
                _dockAnimIcon.SetActive(!inVehicle);
            if (_dockExitIcon != null)
                _dockExitIcon.SetActive(inVehicle);
            if (inVehicle && _emotePopup != null)
                _emotePopup.gameObject.SetActive(false);
            if (_rightJumpBtn != null)
                _rightJumpBtn.gameObject.SetActive(!inVehicle && PlayerInteractor.Local != null);
            if (_micRoot != null)
                _micRoot.gameObject.SetActive(!inVehicle && PlayerInteractor.Local != null);
        }

        private void RefreshStatus(PlayerInteractor local)
        {
            if (local.Economy != null)
            {
                long balance = local.Economy.Balance;
                long cash = local.Economy.Cash;
                long eco = 4200 + Mathf.Max(0, (int)((balance + cash) / 25000));

                if (eco != _shownEco)
                {
                    _shownEco = eco;
                    if (_ecoLineText != null)
                        _ecoLineText.text = "ECO: " + _shownEco;
                }
                if (balance != _shownBalance)
                {
                    _shownBalance = balance;
                    if (_moneyText != null)
                        _moneyText.text = Money.Format(_shownBalance);
                    if (_pingoinLineText != null)
                        _pingoinLineText.text = "PINGOIN: " + Money.Format(_shownBalance);
                }
                if (cash != _shownCash)
                {
                    _shownCash = cash;
                    if (_especeLineText != null)
                        _especeLineText.text = "ESPECE: " + Money.Format(_shownCash);
                }
            }

            PlayerNeeds needs = local.Needs;
            if (needs == null)
                return;

            if (_healthFill != null && needs.Health != _shownHealth)
            {
                _shownHealth = needs.Health;
                float ratio = _shownHealth / (float)PlayerNeeds.MaxHealth;
                SetGauge(_healthFill, ratio, HealthColor);
                if (_healthLabelText != null)
                    _healthLabelText.text = "SANTE " + Mathf.RoundToInt(ratio * 100f) + "%";
            }

            if (needs.Satiety != _shownSatiety)
            {
                _shownSatiety = needs.Satiety;
                float ratio = _shownSatiety / (float)PlayerNeeds.MaxSatiety;
                SetGauge(_satietyFill, ratio, _shownSatiety <= LowSatiety ? NeedLow : SatietyOk);
                if (_satietyLabelText != null)
                    _satietyLabelText.text = "FAIM " + Mathf.RoundToInt(ratio * 100f) + "%";
            }

            if (_hydrationFill != null && needs.Hydration != _shownHydration)
            {
                _shownHydration = needs.Hydration;
                float ratio = _shownHydration / (float)PlayerNeeds.MaxHydration;
                SetGauge(_hydrationFill, ratio, _shownHydration <= LowSatiety ? NeedLow : HydrationOk);
                if (_hydrationLabelText != null)
                    _hydrationLabelText.text = "SOIF " + Mathf.RoundToInt(ratio * 100f) + "%";
            }
        }

        private static void SetGauge(Image fill, float value, Color color)
        {
            value = Mathf.Clamp01(value);
            fill.color = color;
            if (fill.type == Image.Type.Filled)
            {
                fill.fillAmount = value;
                return;
            }

            var rect = (RectTransform)fill.transform;
            bool visible = value > 0.005f;
            if (fill.enabled != visible)
                fill.enabled = visible;
            rect.anchorMax = new Vector2(Mathf.Max(value, 0.06f), 1f);
            Text percent = fill.GetComponentInChildren<Text>(true);
            if (percent != null)
                percent.text = string.Empty;
        }

        private void SetPrompt(string prompt)
        {
            if (ReferenceEquals(prompt, _shownPrompt))
                return;

            _shownPrompt = prompt;
            _interactButton.SetActive(prompt != null);
            if (prompt != null)
                _interactLabel.text = prompt;
        }

        private void SetDelivery(PlayerInteractor local, PlayerEconomy economy)
        {
            if (economy == null || !economy.HasDelivery ||
                !WorldInteractable.TryGet(economy.DeliveryTargetId, out WorldInteractable target))
            {
                if (_deliveryText.gameObject.activeSelf)
                    _deliveryText.gameObject.SetActive(false);
                _shownDeliveryTarget = null;
                if (_beacon != null && _beacon.gameObject.activeSelf)
                    _beacon.gameObject.SetActive(false);
                return;
            }

            float distance = Vector3.Distance(local.transform.position, target.transform.position);
            float remaining = Mathf.Max(0f, economy.DeliveryTimeLimit - (Time.time - economy.DeliveryReceivedAt));
            int minutes = (int)(remaining / 60f);
            int seconds = (int)(remaining % 60f);

            int roundedDistance = Mathf.RoundToInt(distance);
            int totalSeconds = minutes * 60 + seconds;
            if (target != _shownDeliveryTarget || economy.Mission != _shownDeliveryMission ||
                roundedDistance != _shownDeliveryDistance || totalSeconds != _shownDeliverySeconds)
            {
                _shownDeliveryTarget = target;
                _shownDeliveryMission = economy.Mission;
                _shownDeliveryDistance = roundedDistance;
                _shownDeliverySeconds = totalSeconds;
                string label = economy.Mission == MissionKind.TaxiPickup ? "Client à prendre : "
                    : economy.Mission == MissionKind.TaxiDropoff ? "Déposer le client : "
                    : "Livraison : ";
                _deliveryText.text = label + target.DisplayName + "  •  " + roundedDistance +
                                     " m  •  " + minutes + ":" + seconds.ToString("00");
            }
            if (!_deliveryText.gameObject.activeSelf)
                _deliveryText.gameObject.SetActive(true);

            if (_beacon != null)
            {
                _beacon.position = target.transform.position + Vector3.up * _beacon.localScale.y;
                _beacon.gameObject.SetActive(true);
            }
        }

        private void SetStatusVisible(bool visible)
        {
            if (_topLeftPanel != null && _topLeftPanel.gameObject.activeSelf != visible)
                _topLeftPanel.gameObject.SetActive(visible);
            if (_settingsGearBtn != null && _settingsGearBtn.gameObject.activeSelf != visible)
                _settingsGearBtn.gameObject.SetActive(visible);
            if (_bottomDock != null && _bottomDock.gameObject.activeSelf != visible)
                _bottomDock.gameObject.SetActive(visible);

            bool inVehicle = PlayerDriving.Local != null && PlayerDriving.Local.IsInVehicle;
            if (_rightJumpBtn != null && _rightJumpBtn.gameObject.activeSelf != (visible && !inVehicle))
                _rightJumpBtn.gameObject.SetActive(visible && !inVehicle);
            if (_micRoot != null && _micRoot.gameObject.activeSelf != (visible && !inVehicle))
                _micRoot.gameObject.SetActive(visible && !inVehicle);

            SetBarVisible(_satietyFill, visible);
            SetBarVisible(_hydrationFill, visible);
            SetBarVisible(_healthFill, visible);
            if (_clockText != null && _clockText.gameObject.activeSelf)
                _clockText.gameObject.SetActive(false);
        }

        private static void SetBarVisible(Image fill, bool visible)
        {
            if (fill == null)
                return;
            Transform root = fill.transform.parent;
            if (root.name == "Piste" && root.parent != null)
                root = root.parent;
            if (root.gameObject.activeSelf != visible)
                root.gameObject.SetActive(visible);
        }

        #region Construction et restylage du HUD

        private void BuildModernHudLayout()
        {
            if (_moneyText != null)
            {
                Transform moneyRoot = _moneyText.transform;
                if (moneyRoot.parent != null && moneyRoot.parent.name == "Fond" && moneyRoot.parent.parent != null)
                    moneyRoot = moneyRoot.parent.parent;
                moneyRoot.gameObject.SetActive(false);
            }

            Transform oldJump = transform.Find("JumpButton");
            if (oldJump != null)
                oldJump.gameObject.SetActive(false);

            if (_interactButton != null)
            {
                var iRect = (RectTransform)_interactButton.transform;
                iRect.anchorMin = iRect.anchorMax = new Vector2(0.5f, 0f);
                iRect.pivot = new Vector2(0.5f, 0f);
                iRect.sizeDelta = new Vector2(460f, 72f);
                iRect.anchoredPosition = new Vector2(0f, 134f);
            }

            BuildTopLeftEconomy();
            BuildSettingsGearButton();
            RestyleNeedBar(_healthFill, "SANTE 100%", Color.white, HudIconLibrary.Health, out _healthLabelText);
            RestyleNeedBar(_satietyFill, "FAIM 100%", Color.white, HudIconLibrary.Hunger, out _satietyLabelText);
            RestyleNeedBar(_hydrationFill, "SOIF 100%", Color.white, HudIconLibrary.Thirst, out _hydrationLabelText);
            BuildBottomCenterDock();
            RestyleOnFootActionButtons();
        }

        private void BuildTopLeftEconomy()
        {
            var go = new GameObject("TopLeftStats", typeof(RectTransform));
            _topLeftPanel = (RectTransform)go.transform;
            _topLeftPanel.SetParent(transform, false);
            _topLeftPanel.anchorMin = _topLeftPanel.anchorMax = _topLeftPanel.pivot = new Vector2(0f, 1f);
            _topLeftPanel.sizeDelta = new Vector2(420f, 156f);
            _topLeftPanel.anchoredPosition = new Vector2(24f, -20f);

            CreateStatRow("RowEco", 0f, "ECO: 4200", HudIconLibrary.Eco, Color.white, out _ecoLineText);
            CreateStatRow("RowPingoin", -50f, "PINGOIN: 0 FCFA", HudIconLibrary.PingoinCoin, Color.white, out _pingoinLineText);
            CreateStatRow("RowEspece", -100f, "ESPECE: 0 FCFA", HudIconLibrary.Cash, Color.white, out _especeLineText);
        }

        private void CreateStatRow(string name, float y, string initialText, Sprite iconSprite, Color iconTint, out Text label)
        {
            RectTransform row = MakeImg(name, _topLeftPanel, HudIconLibrary.Pill, StatPillBg, new Vector2(390f, 42f), new Vector2(0f, y));
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 1f);
            var rowImg = row.GetComponent<Image>();
            rowImg.type = Image.Type.Sliced;
            rowImg.preserveAspect = false;

            RectTransform icon = MakeImg("Icon", row, iconSprite, iconTint, new Vector2(32f, 32f), new Vector2(24f, 0f));
            icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);

            label = MakeText("Text", row, initialText, 25, TextAnchor.MiddleLeft, new Vector2(330f, 38f), new Vector2(52f, 0f));
            label.fontStyle = FontStyle.Bold;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(52f, 0f);
        }

        private void BuildSettingsGearButton()
        {
            var go = new GameObject("SettingsGearButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            _settingsGearBtn = (RectTransform)go.transform;
            _settingsGearBtn.SetParent(transform, false);
            _settingsGearBtn.anchorMin = _settingsGearBtn.anchorMax = new Vector2(1f, 1f);
            _settingsGearBtn.pivot = new Vector2(0.5f, 0.5f);
            _settingsGearBtn.sizeDelta = new Vector2(56f, 56f);
            _settingsGearBtn.anchoredPosition = new Vector2(-280f, -52f);

            var bgImg = go.GetComponent<Image>();
            bgImg.sprite = HudIconLibrary.Circle;
            bgImg.color = ActionCircleBg;
            bgImg.raycastTarget = true;

            MakeImg("Border", _settingsGearBtn, HudIconLibrary.ThinRing, BorderCrisp, new Vector2(56f, 56f), Vector2.zero);
            MakePaddedIcon("GearIcon", _settingsGearBtn, HudIconLibrary.ParameterGear, Color.white, 0.22f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bgImg;
            btn.onClick.AddListener(() => PhoneHud.Instance?.OpenSettings());
            MobilePlayerControls.RegisterBlockingRect(_settingsGearBtn);
        }

        private void RestyleNeedBar(Image fill, string initialLabel, Color iconColor, Sprite iconSprite, out Text labelOut)
        {
            labelOut = null;
            if (fill == null)
                return;

            Transform track = fill.transform.parent;
            Transform group = track != null && track.parent != null ? track.parent : track;
            if (group == null)
                return;

            var groupRect = (RectTransform)group;
            groupRect.sizeDelta = new Vector2(226f, 36f);

            if (track != null)
            {
                var trackRect = (RectTransform)track;
                trackRect.anchorMin = new Vector2(0f, 0f);
                trackRect.anchorMax = new Vector2(1f, 0f);
                trackRect.offsetMin = new Vector2(36f, 2f);
                trackRect.offsetMax = new Vector2(0f, 13f);
                if (track.TryGetComponent(out Image trackImg))
                    trackImg.color = new Color(0.06f, 0.08f, 0.12f, 0.72f);
            }

            var fillRect = (RectTransform)fill.transform;
            fillRect.offsetMin = new Vector2(1.5f, 1.5f);
            fillRect.offsetMax = new Vector2(-1.5f, -1.5f);

            Transform lblTr = group.Find("Label");
            if (lblTr != null && lblTr.TryGetComponent(out Text lbl))
            {
                lbl.alignment = TextAnchor.UpperRight;
                lbl.fontSize = 15;
                lbl.fontStyle = FontStyle.Bold;
                lbl.color = Color.white;
                lbl.text = initialLabel;
                var lRect = (RectTransform)lblTr;
                lRect.offsetMin = new Vector2(36f, 0f);
                lRect.offsetMax = new Vector2(0f, 0f);
                labelOut = lbl;
            }

            Transform oldIcon = group.Find("NeedIcon");
            if (oldIcon != null)
                Destroy(oldIcon.gameObject);

            RectTransform iconRect = MakeImg("NeedIcon", group, iconSprite, iconColor, new Vector2(28f, 28f), new Vector2(14f, 0f));
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.45f);
        }

        private void BuildBottomCenterDock()
        {
            var go = new GameObject("BottomDock", typeof(RectTransform));
            _bottomDock = (RectTransform)go.transform;
            _bottomDock.SetParent(transform, false);
            _bottomDock.anchorMin = _bottomDock.anchorMax = new Vector2(0.5f, 0f);
            _bottomDock.pivot = new Vector2(0.5f, 0f);
            _bottomDock.sizeDelta = new Vector2(340f, 96f);
            _bottomDock.anchoredPosition = new Vector2(0f, 24f);

            // 1. Téléphone (Gauche) — PNG smartphone_icone avec padding ~22 %
            RectTransform phoneDock = CreateDockButton("DockPhone", new Vector2(-108f, 48f), () => PhoneHud.Instance?.TogglePhone());
            MakePaddedIcon("Icon", phoneDock, HudIconLibrary.Smartphone, Color.white, 0.22f);

            // 2. Sac à dos (Centre) — PNG bag_icone avec padding ~22 %
            RectTransform bagDock = CreateDockButton("DockBag", new Vector2(0f, 48f), () => PhoneHud.Instance?.OpenBag());
            MakePaddedIcon("Icon", bagDock, HudIconLibrary.Backpack, Color.white, 0.22f);

            // 3. Animation / Emote à pied (à côté de l'icône de sac) OU Sortir en véhicule (Droite)
            RectTransform actionDock = CreateDockButton("DockAction", new Vector2(108f, 48f), OnDockThirdButtonClicked);
            _dockAnimIcon = MakePaddedIcon("AnimIcon", actionDock, HudIconLibrary.AnimationEmote, Color.white, 0.22f).gameObject;
            _dockExitIcon = MakePaddedIcon("ExitIcon", actionDock, HudIconLibrary.ExitDoor, Color.white, 0.22f).gameObject;
            _dockExitIcon.SetActive(false);

            BuildEmotePopup();
        }

        private void BuildEmotePopup()
        {
            _emotePopup = MakeImg("EmotePopup", _bottomDock, HudIconLibrary.RoundedBox, new Color(0.10f, 0.11f, 0.14f, 0.92f),
                new Vector2(320f, 240f), new Vector2(108f, 224f));
            var popupImg = _emotePopup.GetComponent<Image>();
            popupImg.type = Image.Type.Sliced;
            popupImg.preserveAspect = false;
            popupImg.raycastTarget = true;
            var outline = _emotePopup.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.18f);

            Text title = MakeText("Title", _emotePopup, "ANIMATIONS", 18, TextAnchor.MiddleCenter, new Vector2(280f, 28f), new Vector2(0f, 92f));
            title.fontStyle = FontStyle.Bold;
            title.color = AmberAccent;

            for (int i = 0; i < EmoteNames.Length; i++)
            {
                int idx = i;
                RectTransform btnRect = MakeImg("Emote_" + i, _emotePopup, HudIconLibrary.RoundedBox,
                    new Color(1f, 1f, 1f, 0.08f), new Vector2(288f, 40f), new Vector2(0f, 48f - i * 44f));
                var img = btnRect.GetComponent<Image>();
                img.type = Image.Type.Sliced;
                img.preserveAspect = false;
                img.raycastTarget = true;
                var btn = btnRect.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => PlayEmote(idx));

                Text lbl = MakeText("Label", btnRect, EmoteNames[i], 19, TextAnchor.MiddleLeft, new Vector2(260f, 36f), new Vector2(8f, 0f));
                lbl.fontStyle = FontStyle.Normal;
            }

            MobilePlayerControls.RegisterBlockingRect(_emotePopup);
            _emotePopup.gameObject.SetActive(false);
        }

        private void PlayEmote(int idx)
        {
            _emoteIndex = idx;
            if (_emotePopup != null)
                _emotePopup.gameObject.SetActive(false);
            ShowToast("Animation : " + EmoteNames[idx]);
        }

        private void OnDockThirdButtonClicked()
        {
            PlayerDriving driving = PlayerDriving.Local;
            if (driving != null && driving.IsInVehicle)
            {
                driving.RequestExit();
                return;
            }

            if (_emotePopup != null)
                _emotePopup.gameObject.SetActive(!_emotePopup.gameObject.activeSelf);
            else
                PlayEmote((_emoteIndex + 1) % EmoteNames.Length);
        }

        private RectTransform CreateDockButton(string name, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_bottomDock, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(88f, 88f);
            rect.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.sprite = HudIconLibrary.RoundedBox;
            img.type = Image.Type.Sliced;
            img.color = DockColor;
            img.raycastTarget = true;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.35f);
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            MobilePlayerControls.RegisterBlockingRect(rect);
            return rect;
        }

        private void RestyleOnFootActionButtons()
        {
            // 1. Micro (haut-gauche du cluster bas-droite)
            Transform micTr = transform.Find("MicButton");
            if (micTr != null)
            {
                _micRoot = (RectTransform)micTr;
                _micRoot.anchorMin = _micRoot.anchorMax = new Vector2(1f, 0f);
                _micRoot.pivot = new Vector2(0.5f, 0.5f);
                _micRoot.sizeDelta = new Vector2(136f, 136f);
                _micRoot.anchoredPosition = new Vector2(-276f, 305f);
                StyleGlassCircle(_micRoot);
            }

            // 2. Frapper / Poing (bas-gauche du cluster bas-droite)
            Transform punchTr = transform.Find("PunchButton");
            if (punchTr != null)
            {
                _punchBtn = (RectTransform)punchTr;
                _punchBtn.anchorMin = _punchBtn.anchorMax = new Vector2(1f, 0f);
                _punchBtn.pivot = new Vector2(0.5f, 0.5f);
                _punchBtn.sizeDelta = new Vector2(136f, 136f);
                _punchBtn.anchoredPosition = new Vector2(-276f, 128f);
                StyleGlassCircle(_punchBtn);

                Transform lbl = _punchBtn.Find("Libelle");
                if (lbl != null)
                    lbl.gameObject.SetActive(false);

                Transform oldFist = _punchBtn.Find("FistIcon");
                if (oldFist != null)
                    Destroy(oldFist.gameObject);
                MakePaddedIcon("FistIcon", _punchBtn, HudIconLibrary.Fight, Color.white, 0.22f);
            }

            _robBtn = transform.Find("RobButton") as RectTransform;

            // 3. SAUT (à droite du cluster bas-droite — déclenche le saut du personnage)
            var jumpGo = new GameObject("RightJumpButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _rightJumpBtn = (RectTransform)jumpGo.transform;
            _rightJumpBtn.SetParent(transform, false);
            _rightJumpBtn.anchorMin = _rightJumpBtn.anchorMax = new Vector2(1f, 0f);
            _rightJumpBtn.pivot = new Vector2(0.5f, 0.5f);
            _rightJumpBtn.sizeDelta = new Vector2(136f, 136f);
            _rightJumpBtn.anchoredPosition = new Vector2(-112f, 216f);

            var jumpImg = jumpGo.GetComponent<Image>();
            jumpImg.sprite = HudIconLibrary.Circle;
            jumpImg.color = ActionCircleBg;
            jumpImg.raycastTarget = true;
            MakeImg("Border", _rightJumpBtn, HudIconLibrary.Ring, BorderCrisp, new Vector2(136f, 136f), Vector2.zero);
            MakePaddedIcon("JumpIcon", _rightJumpBtn, HudIconLibrary.Jump, Color.white, 0.22f);

            var jumpHandler = jumpGo.AddComponent<RightJumpButtonHandler>();
            jumpHandler.Initialize(jumpImg, ActionCircleBg, ActionCirclePressed);
            MobilePlayerControls.RegisterBlockingRect(_rightJumpBtn);
        }

        private static void StyleGlassCircle(RectTransform circleRoot)
        {
            if (circleRoot.TryGetComponent(out Image outerImg))
            {
                outerImg.sprite = HudIconLibrary.Ring;
                outerImg.color = BorderCrisp;
            }
            Transform inner = circleRoot.Find("Fond");
            if (inner != null && inner.TryGetComponent(out Image innerImg))
            {
                innerImg.sprite = HudIconLibrary.Circle;
                innerImg.color = ActionCircleBg;
            }
        }

        private RectTransform MakePaddedIcon(string name, Transform parent, Sprite sprite, Color color, float paddingRatio)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(paddingRatio, paddingRatio);
            rect.anchorMax = new Vector2(1f - paddingRatio, 1f - paddingRatio);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.color = color;
            img.raycastTarget = false;
            return rect;
        }

        private RectTransform MakeImg(string name, Transform parent, Sprite sprite, Color color, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.color = color;
            img.raycastTarget = false;
            return rect;
        }

        private Text MakeText(string name, Transform parent, string content, int size, TextAnchor align, Vector2 box, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = box;
            rect.anchoredPosition = pos;
            var txt = go.GetComponent<Text>();
            txt.font = _font;
            txt.fontSize = size;
            txt.alignment = align;
            txt.color = Color.white;
            txt.text = content;
            txt.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            return txt;
        }

        #endregion

        private Transform CreateBeacon()
        {
            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "DeliveryBeacon";
            Destroy(beacon.GetComponent<Collider>());
            beacon.transform.localScale = new Vector3(0.6f, 30f, 0.6f);

            var renderer = beacon.GetComponent<MeshRenderer>();
            if (_beaconMaterial != null)
                renderer.sharedMaterial = _beaconMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            beacon.SetActive(false);
            return beacon.transform;
        }
    }

    /// <summary>Bouton tactile de Saut placé à droite du cluster d'actions à pied.</summary>
    internal sealed class RightJumpButtonHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private Image _graphic;
        private Color _normal;
        private Color _pressed;

        public void Initialize(Image graphic, Color normal, Color pressed)
        {
            _graphic = graphic;
            _normal = normal;
            _pressed = pressed;
        }

        private void OnDisable()
        {
            if (_graphic != null)
                _graphic.color = _normal;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            MobilePlayerControls.Instance?.QueueJump();
            if (_graphic != null)
                _graphic.color = _pressed;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_graphic != null)
                _graphic.color = _normal;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_graphic != null)
                _graphic.color = _normal;
        }
    }
}
