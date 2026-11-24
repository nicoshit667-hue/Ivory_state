using System.Collections.Generic;
using IvoryState.Client.Input;
using IvoryState.Client.Settings;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Interface de conduite en véhicule (architecture mobile open-world) :
    /// <list type="bullet">
    /// <item><b>Bas-gauche</b> : deux boutons circulaires de direction (◀ / ▶) à contraste élevé et
    /// icônes vectorielles SDF.</item>
    /// <item><b>Bas-droite</b> : compteur de vitesse circulaire sur fond sombre contrasté (km/h,
    /// badge D/R cliquable, rapport 1..5, bandeau carburant + état mécanique bien séparé),
    /// frein à main <c>((P))</c>, pédale d'accélérateur striée, klaxon vectoriel et phares <c>≡D</c>.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DrivingHud : MonoBehaviour
    {
        public static DrivingHud Instance { get; private set; }

        private static readonly int DayIntensityId = Shader.PropertyToID("_DayIntensity");
        private static readonly int NightIntensityId = Shader.PropertyToID("_NightIntensity");

        // Fond sombre semi-transparent : garantit un contraste parfait même face au soleil ou aux façades claires.
        private static readonly Color ControlBg = new Color(0.07f, 0.09f, 0.13f, 0.58f);
        private static readonly Color ControlPressed = new Color(0.22f, 0.28f, 0.36f, 0.78f);
        private static readonly Color ControlActive = new Color(0.14f, 0.48f, 0.72f, 0.76f);
        private static readonly Color BorderCrisp = new Color(1f, 1f, 1f, 0.86f);
        private static readonly Color DialBg = new Color(0.05f, 0.07f, 0.10f, 0.68f);

        private RectTransform _root;
        private HoldButton _leftSteer;
        private HoldButton _rightSteer;
        private HoldButton _gazPedal;
        private HoldButton _brakePedal;
        private HoldButton _klaxonBtn;
        private Button _pharesBtn;
        private Image _pharesBg;
        private Button _gateBtn;
        private Text _gateLabel;
        private Button _gearBadgeBtn;
        private RectTransform _speedometerRect;

        private Text _speedText;
        private Text _gearModeText;
        private Text _gearNumberText;
        private Text _fuelText;
        private Text _conditionText;
        private Image _speedArcFill;

        private bool _reverseMode;
        private float _brakeHeldTime;
        private bool _headlightsOn;
        private float _fuelPercent = 60f;
        private int _shownSpeed = -1;
        private int _shownGearNum = -1;
        private string _shownGearMode;
        private int _shownFuel = -1;

        private AudioSource _hornSource;
        private AudioClip _hornClip;

        private VehicleMotor _litVehicle;
        private Light _headlightSpot;
        private MaterialPropertyBlock _lightBlock;
        private readonly List<RectTransform> _blockingRects = new List<RectTransform>(8);

        private Font _font;

        /// <summary>Direction (-1 = gauche, 0 = neutre, +1 = droite).</summary>
        public float Steer { get; private set; }

        /// <summary>Accélérateur / frein (-1 à +1).</summary>
        public float Throttle { get; private set; }

        /// <summary>Vrai si le frein (P) est maintenu.</summary>
        public bool Brake { get; private set; }

        /// <summary>Vrai si un contrôle tactile de conduite est maintenu.</summary>
        public bool HasInput =>
            (_leftSteer != null && _leftSteer.IsHeld) ||
            (_rightSteer != null && _rightSteer.IsHeld) ||
            (_gazPedal != null && _gazPedal.IsHeld) ||
            (_brakePedal != null && _brakePedal.IsHeld);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (Application.isBatchMode || Instance != null)
                return;
            if (FindFirstObjectByType<DrivingHud>() != null)
                return;

            PlayerHud playerHud = FindFirstObjectByType<PlayerHud>();
            if (playerHud != null)
            {
                playerHud.gameObject.AddComponent<DrivingHud>();
                return;
            }

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
                canvas.gameObject.AddComponent<DrivingHud>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            HudIconLibrary.EnsureInitialized();
            BuildUi();
            SetupHornAudio();
            ApplyCustomLayout();
            SetDrivingVisible(false);
        }

        private void OnEnable()
        {
            GameSettings.Changed += ApplyCustomLayout;
            ApplyCustomLayout();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            UnregisterAllBlocking();
        }

        private void OnDisable()
        {
            GameSettings.Changed -= ApplyCustomLayout;
            Steer = 0f;
            Throttle = 0f;
            Brake = false;
            if (_hornSource != null && _hornSource.isPlaying)
                _hornSource.Stop();
        }

        private void Update()
        {
            PlayerDriving driving = PlayerDriving.Local;
            bool isDriving = driving != null && driving.IsDriving;

            if (_root.gameObject.activeSelf != isDriving)
            {
                SetDrivingVisible(isDriving);
                if (isDriving)
                    ApplyCustomLayout();
                if (!isDriving)
                {
                    Steer = 0f;
                    Throttle = 0f;
                    Brake = false;
                    _reverseMode = false;
                    _brakeHeldTime = 0f;
                    if (_hornSource != null && _hornSource.isPlaying)
                        _hornSource.Stop();
                    return;
                }
            }

            if (!isDriving)
                return;

            VehicleMotor vehicle = driving.Vehicle;
            float speedMs = vehicle != null ? vehicle.Speed : 0f;
            UpdateGateButton();

            // --- Direction ---
            float left = _leftSteer.IsHeld ? 1f : 0f;
            float right = _rightSteer.IsHeld ? 1f : 0f;
            Steer = right - left;

            // --- Accélérateur & Frein (P) ---
            Brake = _brakePedal.IsHeld;
            if (_gazPedal.IsHeld)
            {
                _brakeHeldTime = 0f;
                Throttle = _reverseMode ? -1f : 1f;
            }
            else if (_brakePedal.IsHeld)
            {
                if (speedMs > 0.4f)
                {
                    Throttle = -1f;
                    _brakeHeldTime = 0f;
                }
                else
                {
                    _brakeHeldTime += Time.unscaledDeltaTime;
                    Throttle = (_reverseMode || _brakeHeldTime > 0.35f) ? -1f : 0f;
                }
            }
            else
            {
                _brakeHeldTime = 0f;
                Throttle = 0f;
            }

            // --- Klaxon ---
            if (_klaxonBtn.IsHeld)
            {
                if (!_hornSource.isPlaying)
                    _hornSource.Play();
            }
            else if (_hornSource.isPlaying)
            {
                _hornSource.Stop();
            }

            // --- Phares ---
            if (_litVehicle != vehicle)
            {
                _litVehicle = vehicle;
                if (_headlightsOn && vehicle != null)
                    ApplyHeadlights(vehicle, true);
            }

            // --- Compteur de vitesse ---
            float absSpeedKmh = Mathf.Abs(speedMs) * 3.6f;
            if (absSpeedKmh > 1f)
                _fuelPercent = Mathf.Max(5f, _fuelPercent - Time.unscaledDeltaTime * 0.015f);

            int kmh = Mathf.RoundToInt(absSpeedKmh);
            if (kmh != _shownSpeed)
            {
                _shownSpeed = kmh;
                _speedText.text = kmh.ToString();
                _speedArcFill.fillAmount = Mathf.Clamp01(absSpeedKmh / 110f) * 0.74f;
            }

            string gearMode = (_reverseMode || speedMs < -0.3f) ? "R" : "D";
            if (!ReferenceEquals(gearMode, _shownGearMode))
            {
                _shownGearMode = gearMode;
                _gearModeText.text = gearMode;
            }

            int gearNum = speedMs < -0.3f ? 1
                : kmh < 18 ? 1
                : kmh < 35 ? 2
                : kmh < 52 ? 3
                : kmh < 75 ? 4 : 5;
            if (gearNum != _shownGearNum)
            {
                _shownGearNum = gearNum;
                _gearNumberText.text = gearNum.ToString();
            }

            int fuelInt = Mathf.RoundToInt(_fuelPercent);
            if (fuelInt != _shownFuel)
            {
                _shownFuel = fuelInt;
                _fuelText.text = fuelInt + "%";
            }
        }

        private void SetDrivingVisible(bool visible)
        {
            if (_root != null)
                _root.gameObject.SetActive(visible);
        }

        private void ToggleHeadlights()
        {
            _headlightsOn = !_headlightsOn;
            _pharesBg.color = _headlightsOn ? ControlActive : ControlBg;
            if (PlayerDriving.Local != null && PlayerDriving.Local.Vehicle != null)
                ApplyHeadlights(PlayerDriving.Local.Vehicle, _headlightsOn);
        }

        private void ApplyHeadlights(VehicleMotor vehicle, bool on)
        {
            if (vehicle == null)
                return;

            _lightBlock ??= new MaterialPropertyBlock();
            Transform graphics = vehicle.GraphicalTransform;
            foreach (Renderer r in graphics.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Phares" || r.name == "Feux" || r.name == "Lumignon")
                {
                    r.GetPropertyBlock(_lightBlock);
                    float intensity = on ? (r.name == "Phares" ? 6f : 3.5f) : 0.15f;
                    _lightBlock.SetFloat(DayIntensityId, intensity);
                    _lightBlock.SetFloat(NightIntensityId, on ? intensity : 2.5f);
                    r.SetPropertyBlock(_lightBlock);
                }
            }

            Transform spotTr = graphics.Find("HeadlightSpot");
            if (spotTr == null && on)
            {
                var go = new GameObject("HeadlightSpot");
                go.transform.SetParent(graphics, false);
                go.transform.localPosition = new Vector3(0f, 0.85f, 2.25f);
                go.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
                _headlightSpot = go.AddComponent<Light>();
                _headlightSpot.type = LightType.Spot;
                _headlightSpot.range = 38f;
                _headlightSpot.spotAngle = 72f;
                _headlightSpot.innerSpotAngle = 45f;
                _headlightSpot.intensity = 4.5f;
                _headlightSpot.color = new Color(1f, 0.96f, 0.84f);
                _headlightSpot.shadows = LightShadows.None;
            }
            else if (spotTr != null)
            {
                _headlightSpot = spotTr.GetComponent<Light>();
            }

            if (_headlightSpot != null)
                _headlightSpot.enabled = on;
        }

        private void ToggleGearMode()
        {
            _reverseMode = !_reverseMode;
            _shownGearMode = null;
        }

        #region Construction UI

        private void BuildUi()
        {
            var rootGo = new GameObject("DrivingControls", typeof(RectTransform));
            _root = (RectTransform)rootGo.transform;
            _root.SetParent(transform, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            // --- BAS-GAUCHE : Boutons de direction ◀ et ▶ bien espacés ---
            _leftSteer = CreateSteerButton("SteerLeft", new Vector2(165f, 160f), true);
            _rightSteer = CreateSteerButton("SteerRight", new Vector2(365f, 160f), false);

            // --- BAS-DROITE : Pédale d'accélérateur, Frein (P), Compteur, Klaxon, Phares ---
            _gazPedal = CreateGazPedal(new Vector2(-102f, 168f));
            _brakePedal = CreateBrakeButton(new Vector2(-262f, 120f));
            BuildSpeedometer(new Vector2(-490f, 154f));
            _klaxonBtn = CreateKlaxonButton(new Vector2(-262f, 294f));
            CreatePharesButton(new Vector2(-102f, 412f));
            CreateGateButton(new Vector2(-262f, 468f));
        }

        private HoldButton CreateSteerButton(string name, Vector2 anchoredPos, bool isLeft)
        {
            RectTransform btn = CreateCircleControl(name, new Vector2(0f, 0f), new Vector2(164f, 164f), anchoredPos, out Image fill);
            RectTransform arrow = MakeImage("Arrow", btn, HudIconLibrary.SteerArrow, Color.white, new Vector2(74f, 74f), new Vector2(isLeft ? -4f : 4f, 0f));
            arrow.localRotation = Quaternion.Euler(0f, 0f, isLeft ? 90f : -90f);

            var hold = btn.gameObject.AddComponent<HoldButton>();
            hold.Initialize(fill, ControlBg, ControlPressed);
            RegisterBlocking(btn);
            return hold;
        }

        private HoldButton CreateGazPedal(Vector2 anchoredPos)
        {
            var container = new GameObject("GazPedal", typeof(RectTransform));
            var rect = (RectTransform)container.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(118f, 250f);
            rect.anchoredPosition = anchoredPos;

            // Tige supérieure de la pédale
            RectTransform neck = MakeSliced("Neck", rect, ControlBg, new Vector2(58f, 64f), new Vector2(0f, 88f));
            MakeOutline(neck, BorderCrisp);

            // Semelle principale de la pédale avec stries vectorielles antidérapantes
            RectTransform body = MakeSliced("Body", rect, ControlBg, new Vector2(112f, 192f), new Vector2(0f, -24f));
            body.GetComponent<Image>().raycastTarget = true;
            MakeOutline(body, BorderCrisp);

            MakeImage("Grooves", body, HudIconLibrary.PedalGrooves, new Color(1f, 1f, 1f, 0.65f), new Vector2(88f, 128f), new Vector2(0f, 12f));
            Text label = MakeText("Label", body, "GAZ", 20, TextAnchor.MiddleCenter, new Vector2(96f, 28f), new Vector2(0f, -64f));
            label.fontStyle = FontStyle.Bold;
            label.color = new Color(1f, 1f, 1f, 0.9f);

            var hold = container.AddComponent<HoldButton>();
            hold.Initialize(body.GetComponent<Image>(), ControlBg, ControlPressed);
            RegisterBlocking(rect);
            return hold;
        }

        private HoldButton CreateBrakeButton(Vector2 anchoredPos)
        {
            RectTransform rect = MakeSliced("BrakeButton", _root, ControlBg, new Vector2(134f, 140f), anchoredPos);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Image bg = rect.GetComponent<Image>();
            bg.raycastTarget = true;
            MakeOutline(rect, BorderCrisp);

            // Cercle central + P (symbole frein à main ISO)
            MakeImage("Ring", rect, HudIconLibrary.Ring, Color.white, new Vector2(68f, 68f), new Vector2(0f, 8f));
            Text pText = MakeText("P", rect, "P", 32, TextAnchor.MiddleCenter, new Vector2(68f, 68f), new Vector2(0f, 8f));
            pText.fontStyle = FontStyle.Bold;
            Text sub = MakeText("Sub", rect, "FREIN", 16, TextAnchor.MiddleCenter, new Vector2(110f, 24f), new Vector2(0f, -44f));
            sub.fontStyle = FontStyle.Bold;
            sub.color = new Color(1f, 1f, 1f, 0.85f);

            var hold = rect.gameObject.AddComponent<HoldButton>();
            hold.Initialize(bg, ControlBg, ControlPressed);
            RegisterBlocking(rect);
            return hold;
        }

        private HoldButton CreateKlaxonButton(Vector2 anchoredPos)
        {
            RectTransform btn = CreateCircleControl("KlaxonButton", new Vector2(1f, 0f), new Vector2(132f, 132f), anchoredPos, out Image fill);
            MakeImage("Icon", btn, HudIconLibrary.Horn, Color.white, new Vector2(68f, 68f), new Vector2(0f, 10f));
            Text lbl = MakeText("Label", btn, "Klaxon", 18, TextAnchor.MiddleCenter, new Vector2(106f, 26f), new Vector2(0f, -34f));
            lbl.fontStyle = FontStyle.Bold;

            var hold = btn.gameObject.AddComponent<HoldButton>();
            hold.Initialize(fill, ControlBg, ControlPressed);
            RegisterBlocking(btn);
            return hold;
        }

        private void CreatePharesButton(Vector2 anchoredPos)
        {
            RectTransform btn = CreateCircleControl("PharesButton", new Vector2(1f, 0f), new Vector2(132f, 132f), anchoredPos, out _pharesBg);
            MakeImage("Icon", btn, HudIconLibrary.Headlight, Color.white, new Vector2(68f, 68f), new Vector2(0f, 10f));
            Text lbl = MakeText("Label", btn, "Phares", 18, TextAnchor.MiddleCenter, new Vector2(106f, 26f), new Vector2(0f, -34f));
            lbl.fontStyle = FontStyle.Bold;

            _pharesBtn = btn.gameObject.AddComponent<Button>();
            _pharesBtn.targetGraphic = _pharesBg;
            _pharesBtn.onClick.AddListener(ToggleHeadlights);
            RegisterBlocking(btn);
        }

        /// <summary>Portail à portée de la voiture : ouvrir ou fermer sans descendre (caché sinon).</summary>
        private void CreateGateButton(Vector2 anchoredPos)
        {
            RectTransform btn = CreateCircleControl("PortailButton", new Vector2(1f, 0f), new Vector2(132f, 132f), anchoredPos, out Image bg);
            _gateLabel = MakeText("Label", btn, "Ouvrir\nportail", 18, TextAnchor.MiddleCenter, new Vector2(116f, 60f), Vector2.zero);
            _gateLabel.fontStyle = FontStyle.Bold;
            _gateBtn = btn.gameObject.AddComponent<Button>();
            _gateBtn.targetGraphic = bg;
            _gateBtn.onClick.AddListener(() => IvoryState.Gameplay.Player.PlayerInteractor.Local?.RequestToggleGate());
            RegisterBlocking(btn);
            btn.gameObject.SetActive(false);
        }

        private void UpdateGateButton()
        {
            if (_gateBtn == null)
                return;
            IvoryState.Gameplay.Interaction.WorldDoor gate = IvoryState.Gameplay.Player.PlayerInteractor.Local != null ? IvoryState.Gameplay.Player.PlayerInteractor.Local.CurrentGate : null;
            bool show = gate != null;
            if (_gateBtn.gameObject.activeSelf != show)
                _gateBtn.gameObject.SetActive(show);
            if (show)
                _gateLabel.text = gate.IsOpen ? "Fermer\nportail" : "Ouvrir\nportail";
        }

        private void BuildSpeedometer(Vector2 anchoredPos)
        {
            var container = new GameObject("Speedometer", typeof(RectTransform));
            var rect = (RectTransform)container.transform;
            _speedometerRect = rect;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(224f, 224f);
            rect.anchoredPosition = anchoredPos;

            // Fond circulaire sombre contrasté
            MakeImage("Bg", rect, HudIconLibrary.Circle, DialBg, new Vector2(210f, 210f), Vector2.zero);

            // Piste d'arc
            RectTransform trackArc = MakeImage("ArcTrack", rect, HudIconLibrary.Ring, new Color(1f, 1f, 1f, 0.30f), new Vector2(210f, 210f), Vector2.zero);
            Image trackImg = trackArc.GetComponent<Image>();
            trackImg.type = Image.Type.Filled;
            trackImg.fillMethod = Image.FillMethod.Radial360;
            trackImg.fillOrigin = (int)Image.Origin360.Bottom;
            trackImg.fillClockwise = true;
            trackImg.fillAmount = 0.74f;
            trackArc.localRotation = Quaternion.Euler(0f, 0f, 45f);

            // Arc de vitesse
            RectTransform fillArc = MakeImage("ArcFill", rect, HudIconLibrary.Ring, Color.white, new Vector2(210f, 210f), Vector2.zero);
            _speedArcFill = fillArc.GetComponent<Image>();
            _speedArcFill.type = Image.Type.Filled;
            _speedArcFill.fillMethod = Image.FillMethod.Radial360;
            _speedArcFill.fillOrigin = (int)Image.Origin360.Bottom;
            _speedArcFill.fillClockwise = true;
            _speedArcFill.fillAmount = 0.02f;
            fillArc.localRotation = Quaternion.Euler(0f, 0f, 45f);

            // Vitesse km/h au centre
            _speedText = MakeText("Speed", rect, "0", 60, TextAnchor.MiddleCenter, new Vector2(150f, 70f), new Vector2(0f, 22f));
            _speedText.fontStyle = FontStyle.Bold;
            MakeText("Unit", rect, "km/h", 20, TextAnchor.MiddleCenter, new Vector2(96f, 26f), new Vector2(0f, -22f)).color = new Color(0.88f, 0.92f, 0.96f, 0.9f);

            // Rapport engagé (1..5)
            _gearNumberText = MakeText("GearNumber", rect, "1", 26, TextAnchor.MiddleCenter, new Vector2(44f, 32f), new Vector2(0f, -62f));
            _gearNumberText.fontStyle = FontStyle.Bold;

            // Badge D / R cliquable à droite dans le cadran
            RectTransform gearBadge = MakeImage("GearBadge", rect, HudIconLibrary.Circle, new Color(0.18f, 0.24f, 0.32f, 0.92f), new Vector2(44f, 44f), new Vector2(54f, -24f));
            MakeImage("GearRing", gearBadge, HudIconLibrary.ThinRing, BorderCrisp, new Vector2(44f, 44f), Vector2.zero);
            gearBadge.GetComponent<Image>().raycastTarget = true;
            _gearBadgeBtn = gearBadge.gameObject.AddComponent<Button>();
            _gearBadgeBtn.targetGraphic = gearBadge.GetComponent<Image>();
            _gearBadgeBtn.onClick.AddListener(ToggleGearMode);
            _gearModeText = MakeText("GearMode", gearBadge, "D", 23, TextAnchor.MiddleCenter, new Vector2(44f, 44f), Vector2.zero);
            _gearModeText.fontStyle = FontStyle.Bold;
            RegisterBlocking(gearBadge);

            // Bandeau inférieur (Carburant + État mécanique) dans une pastille sombre sous le cadran
            RectTransform statusBar = MakeSliced("StatusPill", rect, new Color(0.05f, 0.07f, 0.10f, 0.65f), new Vector2(216f, 32f), new Vector2(0f, -110f));
            MakeImage("FuelIcon", statusBar, HudIconLibrary.FuelPump, new Color(1f, 0.85f, 0.38f), new Vector2(22f, 22f), new Vector2(-82f, 0f));
            _fuelText = MakeText("FuelText", statusBar, "60%", 18, TextAnchor.MiddleLeft, new Vector2(64f, 26f), new Vector2(-34f, 0f));

            MakeImage("WrenchIcon", statusBar, HudIconLibrary.Wrench, new Color(0.65f, 0.88f, 1f), new Vector2(20f, 20f), new Vector2(24f, 0f));
            _conditionText = MakeText("ConditionText", statusBar, "100%", 18, TextAnchor.MiddleLeft, new Vector2(72f, 26f), new Vector2(74f, 0f));
        }

        private RectTransform CreateCircleControl(string name, Vector2 anchor, Vector2 size, Vector2 anchoredPos, out Image fillImage)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            fillImage = go.GetComponent<Image>();
            fillImage.sprite = HudIconLibrary.Circle;
            fillImage.color = ControlBg;
            fillImage.raycastTarget = true;

            MakeImage("Border", rect, HudIconLibrary.Ring, BorderCrisp, size, Vector2.zero);
            return rect;
        }

        private RectTransform MakeImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return rect;
        }

        private RectTransform MakeSliced(string name, Transform parent, Color color, Vector2 size, Vector2 pos)
        {
            RectTransform rect = MakeImage(name, parent, HudIconLibrary.RoundedBox, color, size, pos);
            rect.GetComponent<Image>().type = Image.Type.Sliced;
            return rect;
        }

        private static void MakeOutline(RectTransform rect, Color color)
        {
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(1.5f, 1.5f);
        }

        private Text MakeText(string name, Transform parent, string content, int fontSize, TextAnchor align, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;
            var txt = go.GetComponent<Text>();
            txt.font = _font;
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = Color.white;
            txt.text = content;
            txt.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            return txt;
        }

        private void ApplyCustomLayout()
        {
            if (_root == null)
                return;
            RectTransform canvasRect = transform as RectTransform;
            GameSettings.ApplyRectLayout(_leftSteer != null ? _leftSteer.transform as RectTransform : null,
                canvasRect, HudControlId.SteerLeft, new Vector2(165f, 160f));
            GameSettings.ApplyRectLayout(_rightSteer != null ? _rightSteer.transform as RectTransform : null,
                canvasRect, HudControlId.SteerRight, new Vector2(365f, 160f));
            GameSettings.ApplyRectLayout(_gazPedal != null ? _gazPedal.transform as RectTransform : null,
                canvasRect, HudControlId.GazPedal, new Vector2(-102f, 168f));
            GameSettings.ApplyRectLayout(_brakePedal != null ? _brakePedal.transform as RectTransform : null,
                canvasRect, HudControlId.BrakeButton, new Vector2(-262f, 120f));
            GameSettings.ApplyRectLayout(_klaxonBtn != null ? _klaxonBtn.transform as RectTransform : null,
                canvasRect, HudControlId.KlaxonButton, new Vector2(-262f, 294f));
            GameSettings.ApplyRectLayout(_pharesBtn != null ? _pharesBtn.transform as RectTransform : null,
                canvasRect, HudControlId.PharesButton, new Vector2(-102f, 412f));
            GameSettings.ApplyRectLayout(_speedometerRect,
                canvasRect, HudControlId.Speedometer, new Vector2(-490f, 154f));
        }

        private void RegisterBlocking(RectTransform rect)
        {
            _blockingRects.Add(rect);
            MobilePlayerControls.RegisterBlockingRect(rect);
        }

        private void UnregisterAllBlocking()
        {
            for (int i = 0; i < _blockingRects.Count; i++)
                MobilePlayerControls.UnregisterBlockingRect(_blockingRects[i]);
            _blockingRects.Clear();
        }

        #endregion

        #region Audio Klaxon

        private void SetupHornAudio()
        {
            _hornSource = gameObject.AddComponent<AudioSource>();
            _hornSource.loop = true;
            _hornSource.spatialBlend = 0f;
            _hornSource.playOnAwake = false;
            _hornSource.volume = 0.35f;

            const int sampleRate = 22050;
            int samples = sampleRate / 2;
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float w1 = Mathf.Sin(2f * Mathf.PI * 415.3f * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * 415.3f * t);
                float w2 = Mathf.Sin(2f * Mathf.PI * 523.25f * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * 523.25f * t);
                data[i] = Mathf.Clamp((w1 + w2) * 0.25f, -0.8f, 0.8f);
            }
            _hornClip = AudioClip.Create("TaxiHorn", samples, 1, sampleRate, false);
            _hornClip.SetData(data, 0);
            _hornSource.clip = _hornClip;
        }

        #endregion
    }

    /// <summary>Bouton tactile maintenu (direction, gaz, frein, klaxon).</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private Image _graphic;
        private Color _normalColor;
        private Color _pressedColor;

        public bool IsHeld { get; private set; }

        public void Initialize(Image graphic, Color normalColor, Color pressedColor)
        {
            _graphic = graphic;
            _normalColor = normalColor;
            _pressedColor = pressedColor;
        }

        private void OnDisable()
        {
            IsHeld = false;
            if (_graphic != null)
                _graphic.color = _normalColor;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsHeld = true;
            if (_graphic != null)
                _graphic.color = _pressedColor;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsHeld = false;
            if (_graphic != null)
                _graphic.color = _normalColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            IsHeld = false;
            if (_graphic != null)
                _graphic.color = _normalColor;
        }
    }
}
