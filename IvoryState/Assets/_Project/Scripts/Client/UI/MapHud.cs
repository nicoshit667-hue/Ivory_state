using System.Collections.Generic;
using IvoryState.Client.Input;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Carte de la ville : mini-carte carrée (dans l'angle en haut à droite) qui tourne avec la caméra, et carte
    /// plein écran (nord en haut) ouverte d'un tap.
    /// <list type="bullet">
    /// <item>Carte plein écran : zoom (molette, pincement, boutons + et −), déplacement au doigt ou à la souris,
    /// bouton « Moi » pour revenir sur sa position.</item>
    /// <item>Noms en léger : communes, rues (en zoomant), monuments (à la place d'une icône).</item>
    /// <item>Légende : toucher une ligne marque le lieu de ce type le plus proche (GPS) ; la case à droite
    /// affiche ou masque ce type de lieu. Toucher une icône : itinéraire, ou masquer ce lieu seulement.
    /// Les choix restent d'une partie à l'autre.</item>
    /// <item>GPS : l'itinéraire suit les rues, au plus court (<see cref="RoadNetwork"/>).</item>
    /// </list>
    /// <para>
    /// Optimisé contre les freezes : les icônes hors du rayon de la mini-carte sont désactivées et les
    /// transformations UI ne sont mises à jour que lorsque la position, l'angle ou le zoom change réellement.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapHud : MonoBehaviour
    {
        [Header("Carte")]
        [SerializeField] private Texture _mapTexture;           // ville vue du ciel (générée par l'éditeur)
        [SerializeField] private float _worldExtent = 558f;     // demi-côté de la zone couverte par l'image (m)
        [SerializeField] private float _minimapMeters = 160f;   // diamètre visible sur la mini-carte (m)

        [Header("Mini-carte")]
        [SerializeField] private RectTransform _minimap;        // conteneur carré (masque)
        [SerializeField] private RectTransform _minimapMap;     // image de la ville, tournée et décalée
        [SerializeField] private RectTransform _minimapTarget;
        [SerializeField] private RectTransform _minimapNorth;
        [SerializeField] private Button _openButton;

        [Header("Carte plein écran")]
        [SerializeField] private GameObject _fullPanel;
        [SerializeField] private RectTransform _fullMap;
        [SerializeField] private RectTransform _fullPlayer;
        [SerializeField] private RectTransform _fullTarget;
        [SerializeField] private Button _closeButton;

        private Transform _camera;

        // --- Cache anti-thrash Canvas ---
        private float _lastMinimapPixelsPerMeter = -1f;
        private float _lastMinimapYaw = -999f;
        private Vector3 _lastMinimapPlayer = new Vector3(float.MinValue, 0f, float.MinValue);

        // --- GPS ---
        private const float RouteRefresh = 1f;          // recalcul du chemin (s)
        private const float RouteMoveThreshold = 6f;    // ou dès que le joueur a bougé de tant (m)
        private const float WaypointReachedRadius = 12f;
        private const float DirectRouteDistance = 30f;  // plus près : trait direct
        private static readonly Color RouteColor = new Color(0.25f, 0.65f, 1f, 0.95f);
        private static readonly Color WaypointColor = new Color(0.3f, 0.75f, 1f);

        /// <summary>Instance active du HUD carte sur le client.</summary>
        public static MapHud Instance { get; private set; }

        /// <summary>Point marqué par le joueur sur la carte (client uniquement).</summary>
        public static bool HasWaypoint { get; private set; }
        public static Vector3 Waypoint { get; private set; }

        /// <summary>Le point a été posé ou effacé.</summary>
        public static event System.Action WaypointChanged;

        private readonly Gameplay.Bots.NavPathFollower _navFallback = new Gameplay.Bots.NavPathFollower();
        private readonly List<Vector3> _routeWorld = new List<Vector3>(128);
        private readonly List<Vector2> _linePoints = new List<Vector2>(128);
        private MapPathLine _minimapLine, _fullLine;
        private RectTransform _minimapWaypoint, _fullWaypoint;
        private GameObject _clearButton, _taxiButton;
        private Text _hint;
        private string _hintOverride;
        private float _hintOverrideUntil;
        private float _nextRoute;
        private Vector3 _routeFrom, _routeTo;
        private bool _hasRoute;
        private float _minimapScale, _fullScale;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            HasWaypoint = false;
            Waypoint = default;
            WaypointChanged = null;
        }

        /// <summary>Ouvre la carte plein écran pour permettre de marquer un point (appelé par l'appli Taxi).</summary>
        public static void OpenFullMap()
        {
            if (Instance == null || Instance._fullPanel == null)
                return;
            Instance.OpenPanel();
        }

        #region Icônes et types de lieux

        private sealed class MapIcon
        {
            public MapIconKind Kind;
            public string Name;
            public Vector3 World;
            public RectTransform Full, Mini;
            public Graphic FullHouse, MiniHouse;
            public int Entrance = -2;
            public Color HouseColor;
            public string Key;
        }

        private static readonly Color HomeMine = Color.white;
        private static readonly Color HomeFree = new Color(0.25f, 0.85f, 0.35f);
        private static readonly Color HomeTaken = new Color(0.92f, 0.22f, 0.20f);
        private static readonly Color HomeUnknown = new Color(0.65f, 0.65f, 0.65f);
        private static readonly Color MonumentColor = new Color(0.98f, 0.84f, 0.45f);

        private readonly List<MapIcon> _icons = new List<MapIcon>();
        private bool _iconsBuilt;
        private Sprite _circle;
        private Font _font;
        private string[] _occupants;
        private float _iconScaleFull = -1f, _iconScaleMini = -1f;
        private float _nextListRequest;

        /// <summary>Types nombreux : seulement en zoomant sur la carte plein écran.</summary>
        private static bool IsDense(MapIconKind kind) => kind == MapIconKind.Stall || kind == MapIconKind.Kiosk;

        private static (Color background, string glyph) Style(MapIconKind kind) => kind switch
        {
            MapIconKind.Hospital => (Color.white, null),
            MapIconKind.Church => (new Color(0.45f, 0.25f, 0.65f), null),
            MapIconKind.Mosque => (new Color(0.05f, 0.50f, 0.45f), null),
            MapIconKind.Market => (new Color(0.95f, 0.55f, 0.10f), "M"),
            MapIconKind.Station => (new Color(0.95f, 0.80f, 0.10f), "G"),
            MapIconKind.Dealership => (new Color(0.15f, 0.35f, 0.75f), "V"),
            MapIconKind.DrivingSchool => (new Color(0.80f, 0.20f, 0.15f), "P"),
            MapIconKind.Clothing => (new Color(0.85f, 0.30f, 0.60f), "H"),
            MapIconKind.BlackMarket => (new Color(0.15f, 0.15f, 0.18f), "D"),
            MapIconKind.Sport => (new Color(0.20f, 0.50f, 0.20f), "S"),
            MapIconKind.Park => (new Color(0.30f, 0.72f, 0.30f), "♣"),
            MapIconKind.Monument => (MonumentColor, null),
            MapIconKind.CashPoint => (new Color(0.10f, 0.45f, 0.85f), "F"),
            MapIconKind.Commerce => (new Color(0.90f, 0.40f, 0.10f), "C"),
            MapIconKind.Kiosk => (new Color(0.55f, 0.35f, 0.20f), "K"),
            MapIconKind.Stall => (new Color(0.95f, 0.70f, 0.30f), "E"),
            MapIconKind.Gunshop => (new Color(0.35f, 0.38f, 0.30f), "A"),
            MapIconKind.Blades => (new Color(0.50f, 0.50f, 0.55f), "/"),
            MapIconKind.Depot => (new Color(0.20f, 0.45f, 0.95f), "L"),
            MapIconKind.Police => (new Color(0.10f, 0.20f, 0.55f), "P"),
            MapIconKind.TownHall => (new Color(0.55f, 0.15f, 0.25f), "M"),
            MapIconKind.Taxi => (new Color(0.98f, 0.75f, 0.05f), "T"),
            MapIconKind.Airport => (new Color(0.30f, 0.30f, 0.35f), "▲"),
            _ => (new Color(0f, 0f, 0f, 0.6f), null),
        };

        private static string Label(MapIconKind kind) => kind switch
        {
            MapIconKind.Hospital => "Hôpital",
            MapIconKind.Church => "Église",
            MapIconKind.Mosque => "Mosquée",
            MapIconKind.Market => "Marché",
            MapIconKind.Station => "Gare de gbaka",
            MapIconKind.Dealership => "Concessionnaire",
            MapIconKind.DrivingSchool => "Auto-école (permis)",
            MapIconKind.Clothing => "Boutique de vêtements",
            MapIconKind.BlackMarket => "Marché black (Djassa)",
            MapIconKind.Sport => "Stade, terrain",
            MapIconKind.Park => "Parc, jardin",
            MapIconKind.Monument => "Monuments (noms)",
            MapIconKind.CashPoint => "Banque, guichet Pingoin",
            MapIconKind.Commerce => "Commerces (maquis, resto…)",
            MapIconKind.Kiosk => "Kiosques, superettes",
            MapIconKind.Stall => "Étals de rue (manger, boire)",
            MapIconKind.Gunshop => "Armurerie (armes à feu)",
            MapIconKind.Blades => "Armes blanches",
            MapIconKind.Depot => "Dépôt de livraisons (job)",
            MapIconKind.Police => "Police",
            MapIconKind.TownHall => "Mairie",
            MapIconKind.Taxi => "Station de taxis",
            MapIconKind.Airport => "Aéroport",
            _ => "Logements (blanc : le mien, vert : libre)",
        };

        /// <summary>Ordre de la légende.</summary>
        private static readonly MapIconKind[] LegendOrder =
        {
            MapIconKind.Hospital, MapIconKind.Police, MapIconKind.TownHall, MapIconKind.CashPoint, MapIconKind.Depot,
            MapIconKind.Station, MapIconKind.Taxi, MapIconKind.Airport, MapIconKind.Commerce, MapIconKind.Market,
            MapIconKind.Kiosk, MapIconKind.Stall, MapIconKind.Clothing, MapIconKind.Dealership, MapIconKind.DrivingSchool,
            MapIconKind.Gunshop, MapIconKind.Blades, MapIconKind.BlackMarket, MapIconKind.Church, MapIconKind.Mosque,
            MapIconKind.Sport, MapIconKind.Park, MapIconKind.Monument, MapIconKind.Home,
        };

        private RectTransform CreateIcon(Transform parent, MapIconKind kind, float size, out Graphic house)
        {
            house = null;
            var root = new GameObject("Icone_" + kind, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            (Color background, string glyph) = Style(kind);
            if (kind == MapIconKind.Monument)
            {
                // Monument : un simple point doré, son nom est écrit à côté (pas d'icône).
                Image dot = AddImage(rect, "Point", _circle, background, size * 0.38f, Vector2.zero);
                dot.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
                return rect;
            }
            Image disk = AddImage(rect, "Fond", _circle, background, size, Vector2.zero);
            disk.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);

            switch (kind)
            {
                case MapIconKind.Hospital:
                    AddImage(rect, "CroixV", null, new Color(0.85f, 0.08f, 0.08f), size * 0.22f, Vector2.zero, size * 0.66f);
                    AddImage(rect, "CroixH", null, new Color(0.85f, 0.08f, 0.08f), size * 0.66f, Vector2.zero, size * 0.22f);
                    break;
                case MapIconKind.Church:
                    AddImage(rect, "CroixV", null, Color.white, size * 0.14f, new Vector2(0f, -size * 0.04f), size * 0.66f);
                    AddImage(rect, "CroixH", null, Color.white, size * 0.46f, new Vector2(0f, size * 0.12f), size * 0.14f);
                    break;
                case MapIconKind.Mosque:
                    AddImage(rect, "Dome", _circle, Color.white, size * 0.42f, new Vector2(0f, size * 0.02f));
                    AddImage(rect, "Base", null, Color.white, size * 0.50f, new Vector2(0f, -size * 0.16f), size * 0.18f);
                    AddImage(rect, "Minaret", null, Color.white, size * 0.08f, new Vector2(size * 0.30f, -size * 0.02f), size * 0.46f);
                    break;
                case MapIconKind.Home:
                    Image roof = AddImage(rect, "Toit", null, HomeUnknown, size * 0.44f, new Vector2(0f, size * 0.10f), size * 0.44f);
                    roof.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    Image body = AddImage(rect, "Murs", null, HomeUnknown, size * 0.52f, new Vector2(0f, -size * 0.12f), size * 0.36f);
                    house = body;
                    var link = body.gameObject.AddComponent<MapHouseTint>();
                    link.Roof = roof;
                    break;
                default:
                    Text text = AddText(rect, "Lettre", glyph, Mathf.RoundToInt(size * 0.62f), FontStyle.Bold,
                        kind == MapIconKind.Station || kind == MapIconKind.Taxi || kind == MapIconKind.Stall ? new Color(0.15f, 0.12f, 0.02f) : Color.white,
                        TextAnchor.MiddleCenter, new Vector2(size, size));
                    text.rectTransform.anchoredPosition = Vector2.zero;
                    break;
            }
            return rect;
        }

        private static Image AddImage(RectTransform parent, string name, Sprite sprite, Color color, float width, Vector2 position, float height = -1f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height < 0f ? width : height);
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text AddText(RectTransform parent, string name, string value, int fontSize, FontStyle style, Color color,
            TextAnchor alignment, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private void AddIcon(MapIconKind kind, Vector3 world, string name, int entrance = -2)
        {
            var icon = new MapIcon
            {
                Kind = kind, World = world, Name = name, Entrance = entrance,
                Key = kind + ":" + Mathf.RoundToInt(world.x) + ":" + Mathf.RoundToInt(world.z),
            };
            icon.Full = CreateIcon(_iconLayer, kind, 30f, out icon.FullHouse);
            icon.Mini = CreateIcon(_minimapMap, kind, 20f, out icon.MiniHouse);
            icon.Mini.SetSiblingIndex(1);
            if (kind == MapIconKind.Monument)
                AddLabel(_nameLayer, name, new Vector2(world.x, world.z), 1.2f, 17, FontStyle.Bold, MonumentColor, 0f, new Vector2(0f, -18f), icon);
            _icons.Add(icon);
        }

        private static MapIconKind KindOfStore(string name)
        {
            if (name.StartsWith("Armurerie", System.StringComparison.Ordinal))
                return MapIconKind.Gunshop;
            if (name.StartsWith("Quincaillerie", System.StringComparison.Ordinal) || name.StartsWith("Armes blanches", System.StringComparison.Ordinal))
                return MapIconKind.Blades;
            return MapIconKind.Kiosk;
        }

        private void BuildIcons()
        {
            _iconsBuilt = true;
            _circle = _fullTarget.GetComponent<Image>().sprite;
            Text reference = _closeButton.GetComponentInChildren<Text>();
            _font = reference != null ? reference.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            LoadPreferences();

            // Calques de la carte plein écran, de bas en haut : noms des communes et des rues, trait du GPS,
            // icônes, noms des monuments, puis les repères (joueur, destination, point marqué).
            _streetLayer = CreateLayer("Noms_Rues");
            _iconLayer = CreateLayer("Icones");
            _nameLayer = CreateLayer("Noms_Monuments");
            _streetLayer.SetSiblingIndex(0);
            _fullLine.transform.SetSiblingIndex(1);
            _iconLayer.SetSiblingIndex(2);
            _nameLayer.SetSiblingIndex(3);
            _fullTarget.SetAsLastSibling();
            _fullWaypoint.SetAsLastSibling();
            _fullPlayer.SetAsLastSibling();
            BuildStreetLabels();

            foreach (Gameplay.Combat.HospitalCatalog.Hospital hospital in Gameplay.Combat.HospitalCatalog.Hospitals)
                AddIcon(MapIconKind.Hospital, hospital.Entrance, hospital.Name);
            foreach (MapLandmarks.Landmark landmark in MapLandmarks.All)
                AddIcon(landmark.Kind, landmark.Position, landmark.Name);
            var points = WorldInteractable.All;
            for (int i = 0; i < points.Count; i++)
            {
                WorldInteractable point = points[i];
                Vector3 p = point.transform.position;
                switch (point)
                {
                    case GbakaStationPoint: AddIcon(MapIconKind.Station, p, point.DisplayName); break;
                    case DealershipPoint: AddIcon(MapIconKind.Dealership, p, point.DisplayName); break;
                    case DrivingSchoolPoint: AddIcon(MapIconKind.DrivingSchool, p, point.DisplayName); break;
                    case ClothingStorePoint: AddIcon(MapIconKind.Clothing, p, point.DisplayName); break;
                    case BlackMarketPoint: AddIcon(MapIconKind.BlackMarket, p, point.DisplayName); break;
                    case CashPointPoint: AddIcon(MapIconKind.CashPoint, p, point.DisplayName); break;
                    case CommercePoint: AddIcon(MapIconKind.Commerce, p, point.DisplayName); break;
                    case StorePoint: AddIcon(KindOfStore(point.DisplayName), p, point.DisplayName); break;
                    case ShopPoint: AddIcon(MapIconKind.Stall, p, point.DisplayName); break;
                    case JobBoardPoint: AddIcon(MapIconKind.Depot, p, point.DisplayName); break;
                }
            }
            foreach (Gameplay.Vehicles.TaxiStand stand in FindObjectsByType<Gameplay.Vehicles.TaxiStand>(FindObjectsSortMode.None))
                AddIcon(MapIconKind.Taxi, stand.transform.position, "Station de taxis");
            Gameplay.Homes.HomeEntrance residence = Gameplay.Homes.HomeEntrance.Residence;
            if (residence != null)
                AddIcon(MapIconKind.Home, residence.transform.position, "Résidence (studios)", -1);
            for (int i = 0; i < Gameplay.Homes.PropertyCatalog.Entrances.Length; i++)
                AddIcon(MapIconKind.Home, Gameplay.Homes.PropertyCatalog.Entrances[i].Position, "Logement", i);

            BuildLegend();
            RefreshHouseColors();
            RefreshVisibility();
            Gameplay.Homes.PlayerHome.Local?.RequestPropertyList();
        }

        private RectTransform CreateLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_fullMap, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        #endregion

        #region Noms sur la carte (communes, rues, monuments)

        private sealed class MapLabel
        {
            public RectTransform Rect;
            public Vector2 World;
            public Vector2 Offset;
            public float MinZoom;
            public MapIcon Owner;
        }

        private readonly List<MapLabel> _labels = new List<MapLabel>();
        private RectTransform _streetLayer, _iconLayer, _nameLayer;

        private void AddLabel(RectTransform layer, string value, Vector2 world, float minZoom, int fontSize, FontStyle style,
            Color color, float angle, Vector2 offset, MapIcon owner = null)
        {
            Text text = AddText(layer, "Nom", value, fontSize, style, color, TextAnchor.MiddleCenter, new Vector2(10f, 10f));
            text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, Mathf.Min(0.55f, color.a * 0.8f));
            text.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            _labels.Add(new MapLabel { Rect = text.rectTransform, World = world, Offset = offset, MinZoom = minZoom, Owner = owner });
        }

        /// <summary>Communes (gros, très léger) et rues (petit, léger, le long de la chaussée).</summary>
        private void BuildStreetLabels()
        {
            CityLayout.Data layout = CityLayout.Layout;
            foreach (CityLayout.DistrictData district in layout.districts)
            {
                AddLabel(_streetLayer, district.name.ToUpperInvariant(), new Vector2(district.x, district.z), 0f, 30, FontStyle.Bold,
                    new Color(1f, 1f, 1f, 0.34f), 0f, Vector2.zero);
            }

            var placed = new List<(Vector2 p, string name)>();
            var roads = new List<CityLayout.RoadData>(layout.roads);
            roads.Sort((a, b) => a.cls.CompareTo(b.cls));
            foreach (CityLayout.RoadData road in roads)
            {
                if (string.IsNullOrEmpty(road.name) || road.ring)
                    continue;
                float[] p = road.p;
                for (int i = 0; i + 3 < p.Length; i += 2)
                {
                    var a = new Vector2(p[i], p[i + 1]);
                    var b = new Vector2(p[i + 2], p[i + 3]);
                    float length = Vector2.Distance(a, b);
                    if (length < 45f)
                        continue;
                    Vector2 mid = (a + b) * 0.5f;
                    bool crowded = false;
                    foreach ((Vector2 q, string name) in placed)
                    {
                        float d = Vector2.Distance(q, mid);
                        if (d < 45f || (name == road.name && d < 280f))
                        {
                            crowded = true;
                            break;
                        }
                    }
                    if (crowded)
                        continue;
                    placed.Add((mid, road.name));
                    float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                    if (angle > 90f)
                        angle -= 180f;
                    else if (angle < -90f)
                        angle += 180f;
                    AddLabel(_streetLayer, road.name, mid, road.cls <= 1 ? 1.8f : 2.6f, road.cls <= 1 ? 14 : 12, FontStyle.Normal,
                        new Color(1f, 1f, 1f, 0.62f), angle, Vector2.zero);
                    if (placed.Count > 420)
                        return;
                }
            }
        }

        #endregion

        #region Légende, lieux masqués

        private const string HiddenKindsKey = "map.hiddenKinds";
        private const string HiddenPlacesKey = "map.hiddenPlaces";
        private readonly HashSet<MapIconKind> _hiddenKinds = new HashSet<MapIconKind>();
        private readonly HashSet<string> _hiddenPlaces = new HashSet<string>();
        private readonly Dictionary<MapIconKind, (Image box, Text label)> _legendRows = new Dictionary<MapIconKind, (Image, Text)>();
        private GameObject _legend;
        private Text _restoreLabel;

        private void LoadPreferences()
        {
            foreach (string part in PlayerPrefs.GetString(HiddenKindsKey, string.Empty).Split(','))
            {
                if (int.TryParse(part, out int value) && System.Enum.IsDefined(typeof(MapIconKind), value))
                    _hiddenKinds.Add((MapIconKind)value);
            }
            foreach (string part in PlayerPrefs.GetString(HiddenPlacesKey, string.Empty).Split('|'))
            {
                if (part.Length > 0)
                    _hiddenPlaces.Add(part);
            }
        }

        private void SavePreferences()
        {
            var kinds = new List<string>();
            foreach (MapIconKind kind in _hiddenKinds)
                kinds.Add(((int)kind).ToString());
            PlayerPrefs.SetString(HiddenKindsKey, string.Join(",", kinds));
            PlayerPrefs.SetString(HiddenPlacesKey, string.Join("|", _hiddenPlaces));
            PlayerPrefs.Save();
        }

        private bool IsShown(MapIcon icon) => !_hiddenKinds.Contains(icon.Kind) && !_hiddenPlaces.Contains(icon.Key);

        private void BuildLegend()
        {
            var present = new HashSet<MapIconKind>();
            foreach (MapIcon icon in _icons)
                present.Add(icon.Kind);
            var kinds = new List<MapIconKind>();
            foreach (MapIconKind kind in LegendOrder)
            {
                if (present.Contains(kind))
                    kinds.Add(kind);
            }

            const float rowHeight = 24f;
            _legend = new GameObject("Legende", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)_legend.transform;
            rect.SetParent(_fullPanel.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(30f, -200f);
            rect.sizeDelta = new Vector2(380f, (kinds.Count + 2) * rowHeight + 20f);
            var background = _legend.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.6f);
            background.raycastTarget = true;

            Text header = AddText(rect, "Titre", "Toucher un lieu : GPS        Afficher", 14, FontStyle.Italic, new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft, new Vector2(360f, rowHeight));
            header.rectTransform.anchorMin = header.rectTransform.anchorMax = header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(12f, -8f);

            int row = 1;
            foreach (MapIconKind kind in kinds)
            {
                float y = -8f - row * rowHeight;
                // Ligne entière : marque le plus proche lieu de ce type.
                var line = new GameObject("Ligne_" + kind, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                var lineRect = (RectTransform)line.transform;
                lineRect.SetParent(rect, false);
                lineRect.anchorMin = lineRect.anchorMax = lineRect.pivot = new Vector2(0f, 1f);
                lineRect.sizeDelta = new Vector2(316f, rowHeight - 2f);
                lineRect.anchoredPosition = new Vector2(6f, y);
                line.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.001f);
                MapIconKind captured = kind;
                line.GetComponent<Button>().onClick.AddListener(() => MarkNearest(captured));

                RectTransform icon = CreateIcon(lineRect, kind, 20f, out Graphic house);
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.anchoredPosition = new Vector2(16f, 0f);
                if (house != null)
                    house.GetComponent<MapHouseTint>().Apply(HomeFree);
                Text label = AddText(lineRect, "Libelle", Label(kind), 15, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft,
                    new Vector2(280f, rowHeight));
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0f, 0.5f);
                label.rectTransform.anchoredPosition = new Vector2(32f, 0f);

                // Case : affiche ou masque ce type de lieu.
                var toggle = new GameObject("Afficher_" + kind, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                var toggleRect = (RectTransform)toggle.transform;
                toggleRect.SetParent(rect, false);
                toggleRect.anchorMin = toggleRect.anchorMax = toggleRect.pivot = new Vector2(0f, 1f);
                toggleRect.sizeDelta = new Vector2(40f, rowHeight - 5f);
                toggleRect.anchoredPosition = new Vector2(330f, y - 2f);
                Image box = toggle.GetComponent<Image>();
                toggle.GetComponent<Button>().onClick.AddListener(() => ToggleKind(captured));
                _legendRows[kind] = (box, label);
                row++;
            }

            // Bas de la légende : réafficher les lieux masqués un par un.
            var restore = new GameObject("Reafficher", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var restoreRect = (RectTransform)restore.transform;
            restoreRect.SetParent(rect, false);
            restoreRect.anchorMin = restoreRect.anchorMax = restoreRect.pivot = new Vector2(0f, 1f);
            restoreRect.sizeDelta = new Vector2(368f, rowHeight - 2f);
            restoreRect.anchoredPosition = new Vector2(6f, -8f - row * rowHeight);
            restore.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            restore.GetComponent<Button>().onClick.AddListener(() =>
            {
                _hiddenPlaces.Clear();
                _hiddenKinds.Clear();
                SavePreferences();
                RefreshVisibility();
            });
            _restoreLabel = AddText(restoreRect, "Libelle", string.Empty, 15, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter,
                new Vector2(368f, rowHeight));
            _restoreLabel.rectTransform.anchoredPosition = Vector2.zero;
            RefreshLegend();
        }

        private void RefreshLegend()
        {
            foreach (var pair in _legendRows)
            {
                bool shown = !_hiddenKinds.Contains(pair.Key);
                pair.Value.box.color = shown ? new Color(0.25f, 0.8f, 0.35f, 0.95f) : new Color(0.35f, 0.35f, 0.35f, 0.9f);
                pair.Value.label.color = shown ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            }
            if (_restoreLabel != null)
            {
                int hidden = _hiddenPlaces.Count + _hiddenKinds.Count;
                _restoreLabel.text = hidden > 0 ? "Tout réafficher (" + hidden + " masqué" + (hidden > 1 ? "s" : string.Empty) + ")" : "Rien de masqué";
            }
        }

        private void ToggleKind(MapIconKind kind)
        {
            if (!_hiddenKinds.Remove(kind))
                _hiddenKinds.Add(kind);
            SavePreferences();
            RefreshVisibility();
        }

        /// <summary>Marque (GPS) le lieu de ce type le plus proche du joueur.</summary>
        private void MarkNearest(MapIconKind kind)
        {
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local == null)
                return;
            Vector3 from = local.transform.position;
            MapIcon best = null;
            float bestSqr = float.MaxValue;
            foreach (MapIcon icon in _icons)
            {
                if (icon.Kind != kind || _hiddenPlaces.Contains(icon.Key))
                    continue;
                // Logements : le sien d'abord, sinon le plus proche libre.
                if (kind == MapIconKind.Home && best != null && best.HouseColor == HomeMine && icon.HouseColor != HomeMine)
                    continue;
                if (kind == MapIconKind.Home && icon.HouseColor != HomeMine && icon.HouseColor != HomeFree)
                    continue;
                float sqr = (icon.World - from).sqrMagnitude;
                bool better = sqr < bestSqr || (kind == MapIconKind.Home && icon.HouseColor == HomeMine && (best == null || best.HouseColor != HomeMine));
                if (better)
                {
                    best = icon;
                    bestSqr = sqr;
                }
            }
            if (best == null)
            {
                ShowHint("Aucun lieu de ce type à marquer.");
                return;
            }
            GoTo(best);
        }

        private void GoTo(MapIcon icon)
        {
            SetWaypoint(SnapToGround(icon.World));
            CenterOn(new Vector2(icon.World.x, icon.World.z));
            ShowHint("GPS : " + icon.Name + " (" + Mathf.RoundToInt(Vector3.Distance(PlayerNetworkIdentity.Local != null
                ? PlayerNetworkIdentity.Local.transform.position : icon.World, icon.World)) + " m à vol d'oiseau)");
        }

        private void RefreshVisibility()
        {
            foreach (MapIcon icon in _icons)
            {
                bool shown = IsShown(icon);
                bool fullShown = shown && (!IsDense(icon.Kind) || _zoom >= 2.4f);
                if (icon.Full.gameObject.activeSelf != fullShown)
                    icon.Full.gameObject.SetActive(fullShown);
                if (!shown && icon.Mini.gameObject.activeSelf)
                    icon.Mini.gameObject.SetActive(false);
            }
            foreach (MapLabel label in _labels)
            {
                bool shown = _zoom >= label.MinZoom && (label.Owner == null || IsShown(label.Owner));
                if (label.Rect.gameObject.activeSelf != shown)
                    label.Rect.gameObject.SetActive(shown);
            }
            _iconScaleMini = -1f; // la mini-carte refait son tri
            RefreshLegend();
        }

        #endregion

        #region Fiche d'un lieu touché

        private GameObject _card;
        private Text _cardTitle;
        private MapIcon _selected;

        private void BuildCard()
        {
            _card = new GameObject("FicheLieu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)_card.transform;
            rect.SetParent(_fullPanel.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(760f, 170f);
            rect.anchoredPosition = new Vector2(0f, 90f);
            _card.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
            _card.GetComponent<Image>().raycastTarget = true;
            _cardTitle = AddText(rect, "Nom", string.Empty, 26, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, new Vector2(740f, 50f));
            _cardTitle.rectTransform.anchorMin = _cardTitle.rectTransform.anchorMax = _cardTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            _cardTitle.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            CardButton(rect, "Itinéraire", new Vector2(-250f, 16f), () => { if (_selected != null) GoTo(_selected); CloseCard(); });
            CardButton(rect, "Masquer ce lieu", new Vector2(0f, 16f), () =>
            {
                if (_selected != null)
                {
                    _hiddenPlaces.Add(_selected.Key);
                    SavePreferences();
                    RefreshVisibility();
                }
                CloseCard();
            });
            CardButton(rect, "Fermer", new Vector2(250f, 16f), CloseCard);
            _card.SetActive(false);
        }

        private void CardButton(RectTransform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            Button button = Instantiate(_closeButton, parent);
            button.name = label;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(230f, 80f);
            rect.anchoredPosition = position;
            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
                text.text = label;
        }

        private void Select(MapIcon icon)
        {
            _selected = icon;
            _cardTitle.text = icon.Name;
            _card.SetActive(true);
        }

        private void CloseCard()
        {
            _selected = null;
            if (_card != null)
                _card.SetActive(false);
        }

        #endregion

        #region Zoom et déplacement de la carte plein écran

        private const float BaseMapSize = 1000f;
        private const float MinZoom = 1f, MaxZoom = 10f;
        private float _zoom = 3f;
        private Vector2 _viewCenter;
        private Vector2 _mapBasePosition;
        private bool _viewCentered;
        private Canvas _canvas;
        private bool _pinching;
        private float _pinchStartDistance, _pinchStartZoom;

        private float FullPixelsPerMeter => BaseMapSize * _zoom / (_worldExtent * 2f);

        private void OpenPanel()
        {
            // Au-dessus du reste du HUD (argent, boutons du bas) tant qu'elle est ouverte.
            _fullPanel.transform.SetAsLastSibling();
            _fullPanel.SetActive(true);
            _nextListRequest = 0f;
            CloseCard();
            // À l'ouverture : centrée sur le joueur (ou sur le point marqué).
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local != null)
            {
                Vector3 p = HasWaypoint && !_viewCentered ? Waypoint : local.transform.position;
                if (Gameplay.Homes.PropertyCatalog.TryGetExterior(p, out Vector3 exterior))
                    p = exterior;
                _viewCenter = new Vector2(p.x, p.z);
            }
            _viewCentered = true;
            ApplyView();
        }

        private void ApplyView()
        {
            _zoom = Mathf.Clamp(_zoom, MinZoom, MaxZoom);
            _viewCenter.x = Mathf.Clamp(_viewCenter.x, -_worldExtent, _worldExtent);
            _viewCenter.y = Mathf.Clamp(_viewCenter.y, -_worldExtent, _worldExtent);
            float size = BaseMapSize * _zoom;
            if (!Mathf.Approximately(_fullMap.sizeDelta.x, size))
            {
                _fullMap.sizeDelta = new Vector2(size, size);
                RefreshVisibility();
            }
            _fullMap.anchoredPosition = _mapBasePosition - _viewCenter * FullPixelsPerMeter;
        }

        private void CenterOn(Vector2 world)
        {
            _viewCenter = world;
            ApplyView();
        }

        /// <summary>Zoom en gardant fixe le point du monde sous <paramref name="panelPoint"/> (repère du panneau).</summary>
        private void ZoomAt(Vector2 panelPoint, float zoom)
        {
            float ppm = FullPixelsPerMeter;
            Vector2 world = (panelPoint - _fullMap.anchoredPosition) / ppm;
            _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            float ppm2 = FullPixelsPerMeter;
            Vector2 origin = panelPoint - world * ppm2;
            _viewCenter = (_mapBasePosition - origin) / ppm2;
            ApplyView();
        }

        private bool ScreenToPanel(Vector2 screen, out Vector2 panel)
        {
            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_fullPanel.transform, screen, eventCamera, out panel);
        }

        private void OnMapDragged(Vector2 screenDelta)
        {
            if (_pinching)
                return;
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _viewCenter -= screenDelta / Mathf.Max(0.01f, scale) / FullPixelsPerMeter;
            ApplyView();
        }

        private void OnMapScrolled(Vector2 screen, float notches)
        {
            if (Mathf.Abs(notches) < 0.01f || !ScreenToPanel(screen, out Vector2 panel))
                return;
            ZoomAt(panel, _zoom * (notches > 0f ? 1.25f : 0.8f));
        }

        private void UpdatePinchAndKeys()
        {
            Touchscreen touchscreen = Touchscreen.current;
            int pressed = 0;
            Vector2 a = default, b = default;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.isPressed)
                        continue;
                    if (pressed == 0)
                        a = touch.position.ReadValue();
                    else if (pressed == 1)
                        b = touch.position.ReadValue();
                    pressed++;
                }
            }
            if (pressed >= 2)
            {
                float distance = Vector2.Distance(a, b);
                if (!_pinching)
                {
                    _pinching = true;
                    _pinchStartDistance = Mathf.Max(1f, distance);
                    _pinchStartZoom = _zoom;
                }
                else if (ScreenToPanel((a + b) * 0.5f, out Vector2 mid))
                {
                    ZoomAt(mid, _pinchStartZoom * distance / _pinchStartDistance);
                }
            }
            else
            {
                _pinching = false;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.numpadPlusKey.wasPressedThisFrame || keyboard.equalsKey.wasPressedThisFrame)
                    ZoomAt(Vector2.zero, _zoom * 1.4f);
                else if (keyboard.numpadMinusKey.wasPressedThisFrame || keyboard.minusKey.wasPressedThisFrame)
                    ZoomAt(Vector2.zero, _zoom / 1.4f);
            }
        }

        #endregion

        private void OnPropertyList(int[] ids, string[] occupants)
        {
            _occupants = new string[Gameplay.Homes.PropertyCatalog.Count + 1];
            for (int i = 0; i < ids.Length && i < occupants.Length; i++)
            {
                if (Gameplay.Homes.PropertyCatalog.IsValid(ids[i]))
                    _occupants[ids[i]] = occupants[i];
            }
            RefreshHouseColors();
        }

        private void RefreshHouseColors()
        {
            Gameplay.Homes.PlayerHome home = Gameplay.Homes.PlayerHome.Local;
            foreach (MapIcon icon in _icons)
            {
                if (icon.Kind != MapIconKind.Home)
                    continue;
                Color color;
                if (icon.Entrance == -1)
                {
                    color = home != null && home.HasHome ? HomeMine : HomeUnknown;
                }
                else
                {
                    bool mine = false, free = false, known = _occupants != null;
                    foreach (Gameplay.Homes.PropertyCatalog.Property property in Gameplay.Homes.PropertyCatalog.Properties)
                    {
                        if (property.EntranceIndex != icon.Entrance)
                            continue;
                        if (home != null && home.LivesIn(property.Id))
                            mine = true;
                        if (known && _occupants[property.Id] != null && _occupants[property.Id].Length == 0)
                            free = true;
                    }
                    color = mine ? HomeMine : !known ? HomeUnknown : free ? HomeFree : HomeTaken;
                }
                icon.HouseColor = color;
                icon.FullHouse.GetComponent<MapHouseTint>().Apply(color);
                icon.MiniHouse.GetComponent<MapHouseTint>().Apply(color);
            }
        }

        private void PlaceFullIcons(float pixelsPerMeter)
        {
            if (Mathf.Approximately(_iconScaleFull, pixelsPerMeter))
                return;
            _iconScaleFull = pixelsPerMeter;
            for (int i = 0; i < _icons.Count; i++)
            {
                MapIcon icon = _icons[i];
                icon.Full.anchoredPosition = new Vector2(icon.World.x, icon.World.z) * pixelsPerMeter;
            }
            for (int i = 0; i < _labels.Count; i++)
            {
                MapLabel label = _labels[i];
                label.Rect.anchoredPosition = label.World * pixelsPerMeter + label.Offset;
            }
        }

        private void PlaceMinimapIcons(Vector3 player, float pixelsPerMeter, float counterYaw, bool rotateOrMove)
        {
            bool rescale = !Mathf.Approximately(_iconScaleMini, pixelsPerMeter);
            _iconScaleMini = pixelsPerMeter;
            if (!rescale && !rotateOrMove)
                return;

            float cullDistSqr = (_minimapMeters * 0.78f) * (_minimapMeters * 0.78f);
            Quaternion upright = Quaternion.Euler(0f, 0f, counterYaw);

            for (int i = 0; i < _icons.Count; i++)
            {
                MapIcon icon = _icons[i];
                RectTransform rect = icon.Mini;
                if (rescale)
                    rect.anchoredPosition = new Vector2(icon.World.x, icon.World.z) * pixelsPerMeter;

                float dx = icon.World.x - player.x;
                float dz = icon.World.z - player.z;
                bool visible = dx * dx + dz * dz <= cullDistSqr && IsShown(icon);
                if (rect.gameObject.activeSelf != visible)
                    rect.gameObject.SetActive(visible);
                if (visible)
                    rect.localRotation = upright;
            }
        }

        public static void SuggestWaypoint(Vector3 point)
        {
            Waypoint = point;
            HasWaypoint = true;
            WaypointChanged?.Invoke();
        }

        public static void ClearWaypoint()
        {
            if (!HasWaypoint)
                return;
            HasWaypoint = false;
            WaypointChanged?.Invoke();
        }

        private void Awake()
        {
            Instance = this;
            // Style cadre mini-carte : bordure blanche fine et N blanc en haut à gauche (Images 1 & 2).
            if (_minimap != null && _minimap.parent != null && _minimap.parent.TryGetComponent(out Image frameImg))
                frameImg.color = new Color(1f, 1f, 1f, 0.85f);
            if (_minimapNorth != null && _minimapNorth.TryGetComponent(out Text northTxt))
                northTxt.color = Color.white;

            _canvas = GetComponentInParent<Canvas>();
            _mapBasePosition = _fullMap.anchoredPosition;
            _openButton.onClick.AddListener(OpenPanel);
            _closeButton.onClick.AddListener(() => _fullPanel.SetActive(false));
            _fullPanel.SetActive(false);
            _minimapMap.GetComponent<RawImage>().texture = _mapTexture;
            _fullMap.GetComponent<RawImage>().texture = _mapTexture;
            Text reference = _closeButton.GetComponentInChildren<Text>();
            _font = reference != null ? reference.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildGps();
            BuildCard();
        }

        private void BuildGps()
        {
            _minimapLine = CreateLine("Route", _minimapMap);
            _fullLine = CreateLine("Route", _fullMap);

            _minimapWaypoint = Instantiate(_minimapTarget, _minimapTarget.parent);
            _minimapWaypoint.name = "Waypoint";
            _fullWaypoint = Instantiate(_fullTarget, _fullTarget.parent);
            _fullWaypoint.name = "Waypoint";
            foreach (RectTransform marker in new[] { _minimapWaypoint, _fullWaypoint })
            {
                if (marker.TryGetComponent(out Graphic graphic))
                    graphic.color = WaypointColor;
                marker.gameObject.SetActive(false);
            }

            _fullMap.GetComponent<RawImage>().raycastTarget = true;
            var area = _fullMap.gameObject.AddComponent<MapTapArea>();
            area.Tapped += OnFullMapTapped;
            area.Dragged += OnMapDragged;
            area.Scrolled += OnMapScrolled;

            _clearButton = CloneButton("ClearWaypoint", "Effacer le point", new Vector2(-40f, -150f), new Vector2(300f, 90f), ClearWaypoint);
            _taxiButton = CloneButton("TaxiHere", "Taxi jusqu'ici", new Vector2(-40f, -260f), new Vector2(300f, 90f), () =>
            {
                if (HasWaypoint)
                    Gameplay.Economy.PlayerServices.Local?.CallTaxi(Waypoint);
                _fullPanel.SetActive(false);
            });
            // Zoom et recentrage (colonne de droite, sous les boutons du GPS).
            CloneButton("ZoomIn", "+", new Vector2(-40f, -380f), new Vector2(110f, 90f), () => ZoomAt(Vector2.zero, _zoom * 1.5f));
            CloneButton("ZoomOut", "−", new Vector2(-170f, -380f), new Vector2(110f, 90f), () => ZoomAt(Vector2.zero, _zoom / 1.5f));
            CloneButton("Me", "Moi", new Vector2(-40f, -490f), new Vector2(240f, 90f), () =>
            {
                PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
                if (local != null)
                    CenterOn(new Vector2(local.transform.position.x, local.transform.position.z));
            });
            CloneButton("Legend", "Légende", new Vector2(-40f, -600f), new Vector2(240f, 90f), () =>
            {
                if (_legend != null)
                    _legend.SetActive(!_legend.activeSelf);
            });

            Text title = _closeButton.GetComponentInChildren<Text>();
            var hintObject = new GameObject("Hint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            hintObject.transform.SetParent(_fullPanel.transform, false);
            _hint = hintObject.GetComponent<Text>();
            _hint.font = title != null ? title.font : null;
            _hint.fontSize = 28;
            _hint.color = new Color(1f, 1f, 1f, 0.85f);
            _hint.alignment = TextAnchor.LowerCenter;
            _hint.raycastTarget = false;
            var hintRect = (RectTransform)hintObject.transform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.sizeDelta = new Vector2(0f, 60f);
            hintRect.anchoredPosition = new Vector2(0f, 20f);
            RefreshButtons();
        }

        private static MapPathLine CreateLine(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(MapPathLine));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.SetSiblingIndex(0);
            var line = go.GetComponent<MapPathLine>();
            line.color = RouteColor;
            return line;
        }

        private GameObject CloneButton(string name, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            Button button = Instantiate(_closeButton, _closeButton.transform.parent);
            button.name = name;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            var rect = (RectTransform)button.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
                text.text = label;
            return button.gameObject;
        }

        private void OnFullMapTapped(Vector2 local)
        {
            float pixelsPerMeter = FullPixelsPerMeter;
            if (pixelsPerMeter <= 0f)
                return;

            // Une icône (ou un nom de monument) sous le doigt : sa fiche.
            MapIcon touched = null;
            float bestSqr = 26f * 26f;
            foreach (MapIcon icon in _icons)
            {
                if (!icon.Full.gameObject.activeInHierarchy)
                    continue;
                float sqr = (icon.Full.anchoredPosition - local).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    touched = icon;
                }
            }
            if (touched != null)
            {
                Select(touched);
                return;
            }

            CloseCard();
            Vector3 point = new Vector3(local.x / pixelsPerMeter, 0f, local.y / pixelsPerMeter);
            if (Mathf.Abs(point.x) > _worldExtent || Mathf.Abs(point.z) > _worldExtent)
                return;
            SetWaypoint(SnapToGround(point));
        }

        private static Vector3 SnapToGround(Vector3 point)
        {
            point.y = CityLayout.GroundHeight(point.x, point.z) + 0.5f;
            if (UnityEngine.AI.NavMesh.SamplePosition(point, out UnityEngine.AI.NavMeshHit hit, 25f, UnityEngine.AI.NavMesh.AllAreas))
                point = hit.position;
            return point;
        }

        private void SetWaypoint(Vector3 point)
        {
            Waypoint = point;
            HasWaypoint = true;
            _nextRoute = 0f;
            WaypointChanged?.Invoke();
        }

        private void ShowHint(string text)
        {
            _hintOverride = text;
            _hintOverrideUntil = Time.unscaledTime + 4f;
        }

        private long _shownFare = -1;

        private void RefreshButtons()
        {
            if (_clearButton.activeSelf != HasWaypoint)
                _clearButton.SetActive(HasWaypoint);
            var services = Gameplay.Economy.PlayerServices.Local;
            bool canCall = HasWaypoint && services != null && services.Taxi == Gameplay.Economy.PlayerServices.TaxiState.None &&
                           PlayerNetworkIdentity.Local != null;
            if (_taxiButton.activeSelf != canCall)
                _taxiButton.SetActive(canCall);
            if (canCall)
            {
                long fare = Gameplay.Economy.PlayerServices.Catalog.TaxiFare(PlayerNetworkIdentity.Local.transform.position, Waypoint);
                if (fare != _shownFare)
                {
                    _shownFare = fare;
                    Text label = _taxiButton.GetComponentInChildren<Text>();
                    if (label != null)
                        label.text = "Taxi jusqu'ici\n" + Gameplay.Economy.Money.Format(fare);
                }
            }
            if (_hintOverride != null && Time.unscaledTime < _hintOverrideUntil)
                _hint.text = _hintOverride;
            else
                _hint.text = HasWaypoint
                    ? "Trait bleu : ton GPS par les rues. Touche ailleurs pour déplacer le point."
                    : "Touche la carte pour marquer un point. Glisse pour te déplacer, pince ou molette pour zoomer.";
        }

        private void UpdateRoute(Vector3 player, bool hasTarget, Vector3 target)
        {
            if (HasWaypoint)
            {
                Vector3 flat = Waypoint - player;
                flat.y = 0f;
                if (flat.sqrMagnitude <= WaypointReachedRadius * WaypointReachedRadius)
                    ClearWaypoint();
            }

            bool hasDestination = HasWaypoint || hasTarget;
            Vector3 destination = HasWaypoint ? Waypoint : target;
            if (!hasDestination)
            {
                if (_hasRoute)
                {
                    _hasRoute = false;
                    _minimapLine.Clear();
                    _fullLine.Clear();
                }
                return;
            }

            bool moved = (player - _routeFrom).sqrMagnitude > RouteMoveThreshold * RouteMoveThreshold;
            bool retarget = (destination - _routeTo).sqrMagnitude > 1f;
            if (_hasRoute && !retarget && Time.unscaledTime < _nextRoute && !moved)
                return;

            _nextRoute = Time.unscaledTime + RouteRefresh;
            _routeFrom = player;
            _routeTo = destination;
            _hasRoute = true;

            // Itinéraire par les rues (le plus court) ; tout près, un trait direct ; sans rue proche, le NavMesh.
            Vector3 flatDelta = destination - player;
            flatDelta.y = 0f;
            if (flatDelta.sqrMagnitude < DirectRouteDistance * DirectRouteDistance || !RoadNetwork.TryRoute(player, destination, _routeWorld))
            {
                _routeWorld.Clear();
                _routeWorld.Add(player);
                if (flatDelta.sqrMagnitude >= DirectRouteDistance * DirectRouteDistance)
                {
                    _navFallback.SetDestination(player, destination);
                    for (int i = 0; i < _navFallback.CornerCount; i++)
                        _routeWorld.Add(_navFallback.GetCorner(i));
                }
                if (_routeWorld.Count < 2)
                    _routeWorld.Add(destination);
            }
            _minimapScale = _fullScale = -1f;
        }

        private void DrawRoute(MapPathLine line, float pixelsPerMeter, ref float drawnScale, float width)
        {
            if (!_hasRoute || Mathf.Approximately(drawnScale, pixelsPerMeter))
                return;
            drawnScale = pixelsPerMeter;
            _linePoints.Clear();
            for (int i = 0; i < _routeWorld.Count; i++)
                _linePoints.Add(new Vector2(_routeWorld[i].x, _routeWorld[i].z) * pixelsPerMeter);
            line.SetPoints(_linePoints, width);
        }

        private void OnEnable()
        {
            Gameplay.Economy.PlayerServices.LocalWaypointSuggested += SetWaypoint;
            Gameplay.Homes.PlayerHome.LocalPropertyList += OnPropertyList;
            Gameplay.Homes.PlayerHome.LocalChanged += RefreshHouseColors;
            MobilePlayerControls.RegisterBlockingRect(_minimap);
            MobilePlayerControls.RegisterBlockingRect((RectTransform)_fullPanel.transform);
        }

        private void OnDisable()
        {
            Gameplay.Economy.PlayerServices.LocalWaypointSuggested -= SetWaypoint;
            Gameplay.Homes.PlayerHome.LocalPropertyList -= OnPropertyList;
            Gameplay.Homes.PlayerHome.LocalChanged -= RefreshHouseColors;
            MobilePlayerControls.UnregisterBlockingRect(_minimap);
            MobilePlayerControls.UnregisterBlockingRect((RectTransform)_fullPanel.transform);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float _testOpenAt;
#endif

        private void LateUpdate()
        {
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            bool visible = local != null;
            Transform minimapRoot = _minimap.parent != null && _minimap.parent.name == "MinimapFrame" ? _minimap.parent : _minimap;
            if (minimapRoot.gameObject.activeSelf != visible)
                minimapRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                if (_fullPanel.activeSelf)
                    _fullPanel.SetActive(false);
                return;
            }

            if (_camera == null)
            {
                UnityEngine.Camera main = UnityEngine.Camera.main;
                if (main == null)
                    return;
                _camera = main.transform;
            }

            Vector3 player = local.transform.position;
            if (Gameplay.Homes.PropertyCatalog.TryGetExterior(player, out Vector3 exterior))
                player = exterior;
            Vector3 forward = _camera.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

            if (!_iconsBuilt)
            {
                BuildIcons();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Test : « -openMap [zoom] » ouvre la carte plein écran au démarrage (captures d'écran).
                string[] args = System.Environment.GetCommandLineArgs();
                int open = System.Array.IndexOf(args, "-openMap");
                if (open >= 0)
                {
                    if (open + 1 < args.Length && float.TryParse(args[open + 1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float zoom))
                        _zoom = zoom;
                    _testOpenAt = Time.realtimeSinceStartup + 10f; // après l'écran de connexion
                }
#endif
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_testOpenAt > 0f && Time.realtimeSinceStartup >= _testOpenAt)
            {
                _testOpenAt = 0f;
                OpenPanel();
                MarkNearest(MapIconKind.Hospital);
            }
#endif

            bool hasTarget = TryGetTarget(local, out Vector3 target);
            UpdateRoute(player, hasTarget, target);

            UpdateMinimap(player, yaw, hasTarget, target);
            if (_fullPanel.activeSelf)
            {
                UpdatePinchAndKeys();
                UpdateFullMap(player, hasTarget, target);
                RefreshButtons();
            }
        }

        private void UpdateMinimap(Vector3 player, float yaw, bool hasTarget, Vector3 target)
        {
            float radius = _minimap.rect.width * 0.5f;
            float pixelsPerMeter = radius * 2f / _minimapMeters;

            if (!Mathf.Approximately(pixelsPerMeter, _lastMinimapPixelsPerMeter))
            {
                _lastMinimapPixelsPerMeter = pixelsPerMeter;
                _minimapMap.sizeDelta = new Vector2(_worldExtent * 2f * pixelsPerMeter, _worldExtent * 2f * pixelsPerMeter);
            }

            bool yawChanged = Mathf.Abs(Mathf.DeltaAngle(yaw, _lastMinimapYaw)) > 0.2f;
            bool posChanged = (player - _lastMinimapPlayer).sqrMagnitude > 0.04f;

            Quaternion rotation = Quaternion.Euler(0f, 0f, yaw);
            if (yawChanged || posChanged)
            {
                _lastMinimapYaw = yaw;
                _lastMinimapPlayer = player;
                Vector2 offset = new Vector2(-player.x, -player.z) * pixelsPerMeter;
                _minimapMap.localRotation = rotation;
                _minimapMap.anchoredPosition = rotation * offset;

                // N fixe dans l'angle supérieur gauche de la mini-carte (Images 1 & 2).
                _minimapNorth.anchoredPosition = new Vector2(-radius + 22f, radius - 22f);
            }

            PlaceMinimapIcons(player, pixelsPerMeter, -yaw, yawChanged || posChanged);
            PlaceMinimapMarker(_minimapTarget, hasTarget, target, player, rotation, pixelsPerMeter, radius);
            PlaceMinimapMarker(_minimapWaypoint, HasWaypoint, Waypoint, player, rotation, pixelsPerMeter, radius);
            DrawRoute(_minimapLine, pixelsPerMeter, ref _minimapScale, 5f);
        }

        private static void PlaceMinimapMarker(RectTransform marker, bool visible, Vector3 world, Vector3 player,
            Quaternion rotation, float pixelsPerMeter, float radius)
        {
            if (marker.gameObject.activeSelf != visible)
                marker.gameObject.SetActive(visible);
            if (!visible)
                return;
            Vector2 delta = new Vector2(world.x - player.x, world.z - player.z) * pixelsPerMeter;
            Vector2 onScreen = rotation * delta;
            float max = radius - 14f;
            if (onScreen.sqrMagnitude > max * max)
                onScreen = onScreen.normalized * max;
            marker.anchoredPosition = onScreen;
        }

        private void UpdateFullMap(Vector3 player, bool hasTarget, Vector3 target)
        {
            float pixelsPerMeter = FullPixelsPerMeter;
            _fullPlayer.anchoredPosition = new Vector2(player.x, player.z) * pixelsPerMeter;
            Vector3 forward = _camera.forward;
            _fullPlayer.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);

            _fullTarget.gameObject.SetActive(hasTarget);
            if (hasTarget)
                _fullTarget.anchoredPosition = new Vector2(target.x, target.z) * pixelsPerMeter;

            if (_fullWaypoint.gameObject.activeSelf != HasWaypoint)
                _fullWaypoint.gameObject.SetActive(HasWaypoint);
            if (HasWaypoint)
                _fullWaypoint.anchoredPosition = new Vector2(Waypoint.x, Waypoint.z) * pixelsPerMeter;
            DrawRoute(_fullLine, pixelsPerMeter, ref _fullScale, 6f);
            PlaceFullIcons(pixelsPerMeter);
            if (Time.unscaledTime >= _nextListRequest)
            {
                _nextListRequest = Time.unscaledTime + 20f;
                Gameplay.Homes.PlayerHome.Local?.RequestPropertyList();
            }
        }

        private static bool TryGetTarget(PlayerNetworkIdentity local, out Vector3 position)
        {
            position = default;
            if (!local.TryGetComponent(out PlayerEconomy economy) || !economy.HasDelivery)
                return false;
            if (!WorldInteractable.TryGet(economy.DeliveryTargetId, out WorldInteractable target))
                return false;
            position = target.transform.position;
            return true;
        }
    }
}
