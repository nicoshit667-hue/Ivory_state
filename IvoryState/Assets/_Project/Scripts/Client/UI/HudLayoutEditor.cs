using System;
using System.Collections.Generic;
using IvoryState.Client.Input;
using IvoryState.Client.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Éditeur visuel de disposition des touches tactiles (« Déplacer les touches »).
    /// Permet de glisser-déposer et redimensionner chaque contrôle à pied et en véhicule,
    /// puis de sauvegarder la disposition dans <see cref="GameSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class HudLayoutEditor : MonoBehaviour
    {
        private struct ControlSpec
        {
            public HudControlId Id;
            public string Label;
            public Vector2 Size;
            public Sprite Icon;
            public bool Circle;
            public bool Vehicle;
            public float IconRotation;
        }

        private sealed class HandleDrag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
        {
            public HudControlId Id;
            public HudLayoutEditor Owner;

            public void OnPointerDown(PointerEventData eventData) => Owner?.SelectControl(Id);
            public void OnBeginDrag(PointerEventData eventData) => Owner?.SelectControl(Id);
            public void OnDrag(PointerEventData eventData) => Owner?.DragControl(Id, eventData);
        }

        private static readonly Color BackdropColor = new Color(0.04f, 0.06f, 0.10f, 0.84f);
        private static readonly Color BarBg = new Color(0.09f, 0.12f, 0.18f, 0.96f);
        private static readonly Color AccentCyan = new Color(0.18f, 0.66f, 0.96f, 1f);
        private static readonly Color AccentGreen = new Color(0.16f, 0.65f, 0.34f, 1f);
        private static readonly Color AccentRed = new Color(0.75f, 0.20f, 0.18f, 1f);
        private static readonly Color HandleIdle = new Color(0.14f, 0.18f, 0.26f, 0.82f);
        private static readonly Color HandleSelected = new Color(0.12f, 0.36f, 0.56f, 0.92f);

        private RectTransform _root;
        private RectTransform _stage;
        private Font _font;
        private Action _onClosed;
        private bool _vehicleMode;
        private HudControlId _selected = HudControlId.Jump;

        private Button _onFootTab;
        private Button _vehicleTab;
        private Text _selectedText;
        private Text _scaleText;

        private readonly Dictionary<HudControlId, HudControlLayout> _working = new Dictionary<HudControlId, HudControlLayout>();
        private readonly Dictionary<HudControlId, RectTransform> _handles = new Dictionary<HudControlId, RectTransform>();
        private readonly Dictionary<HudControlId, Image> _handleBgs = new Dictionary<HudControlId, Image>();
        private readonly Dictionary<HudControlId, Outline> _handleOutlines = new Dictionary<HudControlId, Outline>();
        private ControlSpec[] _specs;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public static HudLayoutEditor GetOrCreate()
        {
            HudLayoutEditor existing = FindFirstObjectByType<HudLayoutEditor>(FindObjectsInactive.Include);
            if (existing != null)
                return existing;

            Canvas targetCanvas = null;
            PlayerHud playerHud = FindFirstObjectByType<PlayerHud>(FindObjectsInactive.Include);
            if (playerHud != null)
                targetCanvas = playerHud.GetComponent<Canvas>();
            if (targetCanvas == null)
                targetCanvas = FindFirstObjectByType<Canvas>();
            if (targetCanvas == null)
                return null;

            return targetCanvas.gameObject.AddComponent<HudLayoutEditor>();
        }

        public void Open(Action onClosed)
        {
            _onClosed = onClosed;
            HudIconLibrary.EnsureInitialized();
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_root == null)
                BuildUi();

            LoadWorkingCopy();
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            MobilePlayerControls.RegisterBlockingRect(_root);

            bool inVehicle = Gameplay.Vehicles.PlayerDriving.Local != null && Gameplay.Vehicles.PlayerDriving.Local.IsDriving;
            SetMode(inVehicle);
        }

        public void Close(bool save)
        {
            if (_root == null)
                return;

            if (save)
            {
                foreach (KeyValuePair<HudControlId, HudControlLayout> kv in _working)
                {
                    if (kv.Value.Customized)
                        GameSettings.SetHudLayout(kv.Key, kv.Value.NormX, kv.Value.NormY, kv.Value.Scale);
                    else
                        GameSettings.ClearHudLayout(kv.Key);
                }
                GameSettings.Apply();
                GameSettings.Save();
            }

            MobilePlayerControls.UnregisterBlockingRect(_root);
            _root.gameObject.SetActive(false);
            Action cb = _onClosed;
            _onClosed = null;
            cb?.Invoke();
        }

        private void OnDestroy()
        {
            if (_root != null)
                MobilePlayerControls.UnregisterBlockingRect(_root);
        }

        private void BuildUi()
        {
            var rootGo = new GameObject("HudLayoutEditor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _root = (RectTransform)rootGo.transform;
            _root.SetParent(transform, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            var bg = rootGo.GetComponent<Image>();
            bg.color = BackdropColor;
            bg.raycastTarget = true;

            _stage = new GameObject("Stage", typeof(RectTransform)).GetComponent<RectTransform>();
            _stage.SetParent(_root, false);
            _stage.anchorMin = Vector2.zero;
            _stage.anchorMax = Vector2.one;
            _stage.offsetMin = Vector2.zero;
            _stage.offsetMax = Vector2.zero;

            BuildGuideGrid();
            BuildHandleSpecs();
            BuildAllHandles();
            BuildToolbar();
            _root.gameObject.SetActive(false);
        }

        private void BuildGuideGrid()
        {
            Color lineColor = new Color(1f, 1f, 1f, 0.08f);
            float[] guides = { 0.25f, 0.5f, 0.75f };
            for (int i = 0; i < guides.Length; i++)
            {
                float g = guides[i];
                CreateGuideLine("V_" + i, new Vector2(g, 0f), new Vector2(g, 1f), new Vector2(2f, 0f), lineColor);
                CreateGuideLine("H_" + i, new Vector2(0f, g), new Vector2(1f, g), new Vector2(0f, 2f), lineColor);
            }
        }

        private void CreateGuideLine(string name, Vector2 min, Vector2 max, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var r = (RectTransform)go.transform;
            r.SetParent(_stage, false);
            r.anchorMin = min;
            r.anchorMax = max;
            r.sizeDelta = sizeDelta;
            r.anchoredPosition = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        private void BuildHandleSpecs()
        {
            _specs = new[]
            {
                new ControlSpec { Id = HudControlId.Joystick,     Label = "Joystick",     Size = new Vector2(180f, 180f), Icon = HudIconLibrary.Circle,       Circle = true,  Vehicle = false },
                new ControlSpec { Id = HudControlId.Jump,         Label = "Sauter",       Size = new Vector2(136f, 136f), Icon = HudIconLibrary.Jump,         Circle = true,  Vehicle = false },
                new ControlSpec { Id = HudControlId.Punch,        Label = "Frapper",      Size = new Vector2(136f, 136f), Icon = HudIconLibrary.Fight,        Circle = true,  Vehicle = false },
                new ControlSpec { Id = HudControlId.Mic,          Label = "Micro",        Size = new Vector2(136f, 136f), Icon = HudIconLibrary.Microphone,   Circle = true,  Vehicle = false },
                new ControlSpec { Id = HudControlId.Rob,          Label = "Voler",        Size = new Vector2(120f, 120f), Icon = HudIconLibrary.Fight,        Circle = true,  Vehicle = false },
                new ControlSpec { Id = HudControlId.Interact,     Label = "Interagir",    Size = new Vector2(460f, 72f),  Icon = HudIconLibrary.RoundedBox,   Circle = false, Vehicle = false },
                new ControlSpec { Id = HudControlId.BottomDock,   Label = "Barre bas",    Size = new Vector2(340f, 96f),  Icon = HudIconLibrary.Smartphone,   Circle = false, Vehicle = false },

                new ControlSpec { Id = HudControlId.SteerLeft,    Label = "Gauche",       Size = new Vector2(164f, 164f), Icon = HudIconLibrary.SteerArrow,   Circle = true,  Vehicle = true, IconRotation = 90f },
                new ControlSpec { Id = HudControlId.SteerRight,   Label = "Droite",       Size = new Vector2(164f, 164f), Icon = HudIconLibrary.SteerArrow,   Circle = true,  Vehicle = true, IconRotation = -90f },
                new ControlSpec { Id = HudControlId.GazPedal,     Label = "Accélérer",    Size = new Vector2(118f, 250f), Icon = HudIconLibrary.PedalGrooves, Circle = false, Vehicle = true },
                new ControlSpec { Id = HudControlId.BrakeButton,  Label = "Frein",        Size = new Vector2(134f, 140f), Icon = HudIconLibrary.Ring,         Circle = false, Vehicle = true },
                new ControlSpec { Id = HudControlId.KlaxonButton, Label = "Klaxon",       Size = new Vector2(132f, 132f), Icon = HudIconLibrary.Horn,         Circle = true,  Vehicle = true },
                new ControlSpec { Id = HudControlId.PharesButton, Label = "Phares",       Size = new Vector2(132f, 132f), Icon = HudIconLibrary.Headlight,    Circle = true,  Vehicle = true },
                new ControlSpec { Id = HudControlId.Speedometer,  Label = "Compteur",     Size = new Vector2(224f, 224f), Icon = HudIconLibrary.ThinRing,     Circle = true,  Vehicle = true },
            };
        }

        private void BuildAllHandles()
        {
            for (int i = 0; i < _specs.Length; i++)
            {
                ControlSpec spec = _specs[i];
                var go = new GameObject("Handle_" + spec.Id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(HandleDrag));
                var rect = (RectTransform)go.transform;
                rect.SetParent(_stage, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = spec.Size;

                var bg = go.GetComponent<Image>();
                bg.sprite = spec.Circle ? HudIconLibrary.Circle : HudIconLibrary.RoundedBox;
                bg.type = spec.Circle ? Image.Type.Simple : Image.Type.Sliced;
                bg.color = HandleIdle;
                bg.raycastTarget = true;

                var outline = go.GetComponent<Outline>();
                outline.effectColor = new Color(1f, 1f, 1f, 0.7f);
                outline.effectDistance = new Vector2(2f, 2f);

                if (spec.Icon != null && spec.Id != HudControlId.Joystick && spec.Id != HudControlId.Interact)
                {
                    var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    var iconRect = (RectTransform)iconGo.transform;
                    iconRect.SetParent(rect, false);
                    iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
                    float iconSide = Mathf.Min(spec.Size.x, spec.Size.y) * 0.46f;
                    iconRect.sizeDelta = new Vector2(iconSide, iconSide);
                    iconRect.anchoredPosition = new Vector2(0f, 10f);
                    iconRect.localRotation = Quaternion.Euler(0f, 0f, spec.IconRotation);
                    var iconImg = iconGo.GetComponent<Image>();
                    iconImg.sprite = spec.Icon;
                    iconImg.preserveAspect = true;
                    iconImg.color = new Color(1f, 1f, 1f, 0.92f);
                    iconImg.raycastTarget = false;
                }

                Text lbl = CreateText("Label", rect, spec.Label, 20, TextAnchor.MiddleCenter,
                    new Vector2(spec.Size.x - 10f, 34f), new Vector2(0f, spec.Size.y > 100f ? -spec.Size.y * 0.22f : 0f));
                lbl.fontStyle = FontStyle.Bold;

                var drag = go.GetComponent<HandleDrag>();
                drag.Id = spec.Id;
                drag.Owner = this;

                _handles[spec.Id] = rect;
                _handleBgs[spec.Id] = bg;
                _handleOutlines[spec.Id] = outline;
            }
        }

        private void BuildToolbar()
        {
            var barGo = new GameObject("Toolbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            var bar = (RectTransform)barGo.transform;
            bar.SetParent(_root, false);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(1060f, 96f);
            bar.anchoredPosition = new Vector2(0f, -18f);

            var bg = barGo.GetComponent<Image>();
            bg.sprite = HudIconLibrary.RoundedBox;
            bg.type = Image.Type.Sliced;
            bg.color = BarBg;
            bg.raycastTarget = true;

            var outline = barGo.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.25f);
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            CreateText("Hint", bar, "Glissez les touches pour les déplacer", 18, TextAnchor.MiddleLeft,
                new Vector2(360f, 26f), new Vector2(-326f, 24f)).color = new Color(0.8f, 0.88f, 0.96f);

            _onFootTab = CreateToolbarButton(bar, "À pied", new Vector2(-436f, -14f), new Vector2(130f, 38f),
                AccentCyan, () => SetMode(false));
            _vehicleTab = CreateToolbarButton(bar, "En véhicule", new Vector2(-286f, -14f), new Vector2(150f, 38f),
                new Color(0.22f, 0.26f, 0.34f, 1f), () => SetMode(true));

            _selectedText = CreateText("Selected", bar, "Touche : Sauter", 19, TextAnchor.MiddleCenter,
                new Vector2(220f, 28f), new Vector2(-50f, 22f));
            _selectedText.fontStyle = FontStyle.Bold;

            CreateToolbarButton(bar, "−", new Vector2(-120f, -14f), new Vector2(46f, 38f),
                new Color(0.24f, 0.28f, 0.36f, 1f), () => AdjustSelectedScale(-0.05f));
            _scaleText = CreateText("Scale", bar, "100 %", 20, TextAnchor.MiddleCenter,
                new Vector2(84f, 36f), new Vector2(-50f, -14f));
            _scaleText.fontStyle = FontStyle.Bold;
            CreateToolbarButton(bar, "+", new Vector2(20f, -14f), new Vector2(46f, 38f),
                new Color(0.24f, 0.28f, 0.36f, 1f), () => AdjustSelectedScale(0.05f));

            CreateToolbarButton(bar, "Réinit.", new Vector2(160f, 0f), new Vector2(136f, 52f),
                new Color(0.35f, 0.35f, 0.40f, 1f), ResetActiveMode);
            CreateToolbarButton(bar, "Annuler", new Vector2(308f, 0f), new Vector2(136f, 52f),
                AccentRed, () => Close(false));
            CreateToolbarButton(bar, "Sauver", new Vector2(456f, 0f), new Vector2(136f, 52f),
                AccentGreen, () => Close(true));
        }

        private Button CreateToolbarButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Color color, Action onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = size;
            r.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.sprite = HudIconLibrary.RoundedBox;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = true;

            Text t = CreateText("Label", r, label, 19, TextAnchor.MiddleCenter, size - new Vector2(8f, 4f), Vector2.zero);
            t.fontStyle = FontStyle.Bold;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            return btn;
        }

        private Text CreateText(string name, RectTransform parent, string content, int size, TextAnchor align, Vector2 box, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = box;
            r.anchoredPosition = pos;

            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.text = content;
            t.raycastTarget = false;
            return t;
        }

        private void LoadWorkingCopy()
        {
            _working.Clear();
            Canvas.ForceUpdateCanvases();
            float cw = _stage != null && _stage.rect.width > 1f ? _stage.rect.width : 1920f;
            float ch = _stage != null && _stage.rect.height > 1f ? _stage.rect.height : 1080f;

            for (int i = 0; i < _specs.Length; i++)
            {
                HudControlId id = _specs[i].Id;
                if (GameSettings.TryGetHudLayout(id, out HudControlLayout saved) && saved.Customized)
                {
                    _working[id] = saved;
                }
                else
                {
                    Vector2 def = GameSettings.GetDefaultNormPosition(id, cw, ch);
                    _working[id] = new HudControlLayout
                    {
                        Customized = true,
                        NormX = def.x,
                        NormY = def.y,
                        Scale = 1f,
                    };
                }
            }
        }

        private void SetMode(bool vehicle)
        {
            _vehicleMode = vehicle;
            if (_onFootTab != null && _onFootTab.targetGraphic is Image onImg)
                onImg.color = !vehicle ? AccentCyan : new Color(0.22f, 0.26f, 0.34f, 1f);
            if (_vehicleTab != null && _vehicleTab.targetGraphic is Image vehImg)
                vehImg.color = vehicle ? AccentCyan : new Color(0.22f, 0.26f, 0.34f, 1f);

            HudControlId firstMatch = vehicle ? HudControlId.SteerLeft : HudControlId.Jump;
            for (int i = 0; i < _specs.Length; i++)
            {
                ControlSpec spec = _specs[i];
                if (_handles.TryGetValue(spec.Id, out RectTransform handle))
                    handle.gameObject.SetActive(spec.Vehicle == vehicle);
            }

            SelectControl(firstMatch);
            RefreshHandleVisuals();
        }

        private void SelectControl(HudControlId id)
        {
            _selected = id;
            RefreshSelectionUi();
        }

        private void DragControl(HudControlId id, PointerEventData eventData)
        {
            if (_stage == null || !_working.TryGetValue(id, out HudControlLayout layout))
                return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_stage, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return;

            float cw = Mathf.Max(1f, _stage.rect.width);
            float ch = Mathf.Max(1f, _stage.rect.height);
            layout.NormX = Mathf.Clamp((local.x / cw) + 0.5f, 0.04f, 0.96f);
            layout.NormY = Mathf.Clamp((local.y / ch) + 0.5f, 0.04f, 0.96f);
            layout.Customized = true;
            _working[id] = layout;
            RefreshHandleVisuals();
        }

        private void AdjustSelectedScale(float delta)
        {
            if (!_working.TryGetValue(_selected, out HudControlLayout layout))
                return;
            layout.Scale = Mathf.Clamp(layout.Scale + delta, 0.7f, 1.5f);
            layout.Customized = true;
            _working[_selected] = layout;
            RefreshHandleVisuals();
            RefreshSelectionUi();
        }

        private void ResetActiveMode()
        {
            float cw = _stage != null && _stage.rect.width > 1f ? _stage.rect.width : 1920f;
            float ch = _stage != null && _stage.rect.height > 1f ? _stage.rect.height : 1080f;
            for (int i = 0; i < _specs.Length; i++)
            {
                if (_specs[i].Vehicle != _vehicleMode)
                    continue;
                HudControlId id = _specs[i].Id;
                Vector2 def = GameSettings.GetDefaultNormPosition(id, cw, ch);
                _working[id] = new HudControlLayout
                {
                    Customized = false,
                    NormX = def.x,
                    NormY = def.y,
                    Scale = 1f,
                };
            }
            RefreshHandleVisuals();
            RefreshSelectionUi();
        }

        private void RefreshHandleVisuals()
        {
            if (_stage == null)
                return;
            float cw = Mathf.Max(1f, _stage.rect.width);
            float ch = Mathf.Max(1f, _stage.rect.height);

            foreach (KeyValuePair<HudControlId, RectTransform> kv in _handles)
            {
                if (!_working.TryGetValue(kv.Key, out HudControlLayout layout))
                    continue;
                kv.Value.anchoredPosition = new Vector2((layout.NormX - 0.5f) * cw, (layout.NormY - 0.5f) * ch);
                kv.Value.localScale = new Vector3(layout.Scale, layout.Scale, 1f);
            }
            RefreshSelectionUi();
        }

        private void RefreshSelectionUi()
        {
            string label = _selected.ToString();
            for (int i = 0; i < _specs.Length; i++)
            {
                if (_specs[i].Id == _selected)
                {
                    label = _specs[i].Label;
                    break;
                }
            }

            if (_selectedText != null)
                _selectedText.text = "Touche : " + label;
            if (_scaleText != null && _working.TryGetValue(_selected, out HudControlLayout layout))
                _scaleText.text = Mathf.RoundToInt(layout.Scale * 100f) + " %";

            foreach (KeyValuePair<HudControlId, Image> kv in _handleBgs)
            {
                bool active = kv.Key == _selected;
                kv.Value.color = active ? HandleSelected : HandleIdle;
                if (_handleOutlines.TryGetValue(kv.Key, out Outline outline))
                {
                    outline.effectColor = active ? AccentCyan : new Color(1f, 1f, 1f, 0.7f);
                    outline.effectDistance = active ? new Vector2(3.5f, 3.5f) : new Vector2(2f, 2f);
                }
            }
        }
    }
}
