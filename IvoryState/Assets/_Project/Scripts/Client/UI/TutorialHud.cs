using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Tutoriel des nouveaux joueurs : un objectif à la fois, dans un bandeau doré en haut de l'écran.
    /// Les étapes se valident toutes seules (détection locale) ; la progression est gardée sur l'appareil
    /// et un bouton « Passer » arrête le tutoriel.
    /// <list type="number">
    /// <item>Ouvrir le téléphone.</item>
    /// <item>Rejoindre son studio (GPS).</item>
    /// <item>Manger ou boire.</item>
    /// <item>Prendre un premier travail.</item>
    /// <item>Passer le permis (facultatif, dernière étape).</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialHud : MonoBehaviour
    {
        private const string PrefKey = "ivory.tutorial.step";
        private const int Done = 99;

        private static readonly string[] Steps =
        {
            "Bienvenue à Abidjan ! Ouvre ton téléphone (bouton en bas de l'écran).",
            "Rejoins ton studio à Treichville : suis le trait bleu du GPS sur la carte.",
            "Mange ou bois quelque chose : étals de rue, boutiques, ou ton sac.",
            "Gagne ton premier argent : téléphone → Emplois → Livreur express.",
            "Pour conduire, passe ton permis : téléphone → Garage → GPS auto-école.",
        };

        private RectTransform _panel;
        private Text _text;
        private int _step;
        private int _lastFood = -1;
        private float _finishedAt = -1f;
        private float _nextCheck;

        private void Awake()
        {
            _step = ReadStep();
            BuildPanel();
            PhoneHud.PhoneOpened += OnPhoneOpened;
            Refresh();
        }

        private void OnDestroy() => PhoneHud.PhoneOpened -= OnPhoneOpened;

        private static int ReadStep()
        {
            try { return PlayerPrefs.GetInt(PrefKey, 0); }
            catch { return 0; }
        }

        private void SaveStep()
        {
            try { PlayerPrefs.SetInt(PrefKey, _step); PlayerPrefs.Save(); }
            catch { /* stockage indisponible : la progression reste en mémoire */ }
        }

        private void OnPhoneOpened()
        {
            if (_step == 0)
                Advance();
        }

        private void Update()
        {
            if (_finishedAt > 0f && Time.unscaledTime - _finishedAt > 8f)
            {
                _finishedAt = -1f;
                _step = Done;
                SaveStep();
                Refresh();
            }
            if (_step >= Steps.Length || Time.unscaledTime < _nextCheck)
                return;
            _nextCheck = Time.unscaledTime + 0.5f;

            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local == null)
            {
                SetVisible(false);
                return;
            }
            SetVisible(true);

            switch (_step)
            {
                case 1:
                    PlayerHome home = PlayerHome.Local;
                    if (home == null || !home.HasHome || StudioLayout.StudioAt(local.transform.position) == home.Studio)
                        Advance();
                    break;
                case 2:
                    if (local.TryGetComponent(out PlayerNeeds needs))
                    {
                        int food = needs.Satiety + needs.Hydration;
                        if (_lastFood >= 0 && food >= _lastFood + 5)
                            Advance();
                        _lastFood = _lastFood < 0 ? food : Mathf.Min(_lastFood, food);
                    }
                    break;
                case 3:
                    if (local.TryGetComponent(out PlayerEconomy economy) && economy.HasDelivery)
                        Advance();
                    break;
                case 4:
                    if (PlayerGarage.Local != null && PlayerGarage.Local.HasLicense)
                        Advance();
                    break;
            }
        }

        private void Advance()
        {
            _step++;
            SaveStep();
            if (_step >= Steps.Length)
                _finishedAt = Time.unscaledTime;
            Refresh();
        }

        private void Refresh()
        {
            if (_step == Done)
            {
                _panel.gameObject.SetActive(false);
                return;
            }
            _text.text = _step >= Steps.Length
                ? "Bravo, tu es prêt ! Explore Abidjan, trouve ta place… et bonne arrivée !"
                : "Objectif " + (_step + 1) + "/" + Steps.Length + " — " + Steps[_step];
        }

        private void SetVisible(bool visible)
        {
            bool show = visible && _step != Done;
            if (_panel.gameObject.activeSelf != show)
                _panel.gameObject.SetActive(show);
        }

        private void BuildPanel()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var panelObject = new GameObject("Tutoriel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _panel = (RectTransform)panelObject.transform;
            _panel.SetParent(transform, false);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 1f);
            _panel.sizeDelta = new Vector2(900f, 84f);
            _panel.anchoredPosition = new Vector2(0f, -196f);
            var background = panelObject.GetComponent<Image>();
            background.color = new Color(0.06f, 0.05f, 0.03f, 0.82f);
            panelObject.AddComponent<Outline>().effectColor = new Color(0.85f, 0.68f, 0.30f, 0.9f);

            var textObject = new GameObject("Texte", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(_panel, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 6f);
            textRect.offsetMax = new Vector2(-130f, -6f);
            _text = textObject.GetComponent<Text>();
            _text.font = font;
            _text.fontSize = 24;
            _text.color = new Color(1f, 0.92f, 0.70f);
            _text.alignment = TextAnchor.MiddleLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.raycastTarget = false;

            var skipObject = new GameObject("Passer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var skipRect = (RectTransform)skipObject.transform;
            skipRect.SetParent(_panel, false);
            skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = new Vector2(1f, 0.5f);
            skipRect.sizeDelta = new Vector2(110f, 54f);
            skipRect.anchoredPosition = new Vector2(-10f, 0f);
            skipObject.GetComponent<Image>().color = new Color(0.85f, 0.68f, 0.30f, 0.9f);
            skipObject.GetComponent<Button>().onClick.AddListener(() =>
            {
                _step = Done;
                SaveStep();
                Refresh();
            });
            var labelObject = new GameObject("Libelle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(skipRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var label = labelObject.GetComponent<Text>();
            label.font = font;
            label.fontSize = 22;
            label.color = new Color(0.08f, 0.06f, 0.03f);
            label.alignment = TextAnchor.MiddleCenter;
            label.text = "Passer";
            label.raycastTarget = false;

            Input.MobilePlayerControls.RegisterBlockingRect(_panel);
        }
    }
}
