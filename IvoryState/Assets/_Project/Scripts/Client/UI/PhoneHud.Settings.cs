using System;
using IvoryState.Client.Input;
using IvoryState.Client.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    public sealed partial class PhoneHud
    {
        // Catégories des paramètres : 0 = Général, 1 = Graphisme, 2 = Commande, 3 = Son.
        private const int SettingsCategoryCount = 4;
        private static readonly string[] SettingsCategoryNames = { "Général", "Graphisme", "Commande", "Son" };
        private static readonly Color SettingsTabActive = new Color(0.18f, 0.54f, 0.85f, 1f);
        private static readonly Color SettingsTabIdle = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color SettingsAccentBlue = new Color(0.16f, 0.50f, 0.82f, 1f);

        private int _settingsCategory;
        private PhoneScrollView _settingsScroll;
        private readonly Button[] _settingsTabs = new Button[SettingsCategoryCount];
        private readonly RectTransform[] _settingsContainers = new RectTransform[SettingsCategoryCount];
        private readonly float[] _settingsCategoryHeights = new float[SettingsCategoryCount];

        // Valeurs des réglages :
        // 0..6 = Graphisme (Qualité, Résolution, Distance, Ombres, Effets, FPS, Passants)
        // 7..8 = Commande (Sensibilité vue, Joystick)
        // 9..10 = Général (Taille interface, Noms joueurs)
        // 11..12 = Son (Volume, Sonnerie)
        private readonly Text[] _settingValues = new Text[13];
        private Text _hudLayoutStatusText;
        private Text _resetControlsLabel;
        private float _resetControlsConfirmUntil;
        private Text _resetAllLabel;
        private float _resetAllConfirmUntil;

        private bool _ringtoneOn => GameSettings.RingtoneIndex == 0;

        private static readonly int[] FpsChoices = { 30, 45, 60, 90, 120 };
        private static readonly int[] PedChoices = { 0, 6, 12, 20, 30 };

        private void LoadSettings()
        {
            GameSettings.Load();
        }

        private void BuildSettings(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Settings, screen);
            page.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 1f);
            MakeHeader(page, "Paramètres", new Color(0.28f, 0.32f, 0.36f, 1f));

            // Barre d'onglets de catégories : Général | Graphisme | Commande | Son
            float tabWidth = 104f;
            float spacing = 6f;
            float totalWidth = SettingsCategoryCount * tabWidth + (SettingsCategoryCount - 1) * spacing;
            float startX = -totalWidth * 0.5f + tabWidth * 0.5f;

            for (int i = 0; i < SettingsCategoryCount; i++)
            {
                int cat = i;
                float x = startX + i * (tabWidth + spacing);
                Button tab = MakeButton("Cat_" + SettingsCategoryNames[i], page, SettingsCategoryNames[i], 16,
                    SettingsTabIdle, new Vector2(0.5f, 1f), new Vector2(tabWidth, 42f), new Vector2(x, -84f));
                tab.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
                tab.onClick.AddListener(() => SelectSettingsCategory(cat));
                _settingsTabs[i] = tab;
            }

            _settingsScroll = PhoneScrollView.Create(page, "SettingsScroll", 134f, 6f);
            RegisterPageScroll(Page.Settings, _settingsScroll);

            for (int i = 0; i < SettingsCategoryCount; i++)
            {
                RectTransform container = MakeImage("CatPanel_" + i, _settingsScroll.Content, new Color(0f, 0f, 0f, 0f),
                    new Vector2(0.5f, 1f), new Vector2(ScreenLogicalWidth, 600f), Vector2.zero);
                container.anchorMin = new Vector2(0f, 1f);
                container.anchorMax = new Vector2(1f, 1f);
                container.pivot = new Vector2(0.5f, 1f);
                container.offsetMin = new Vector2(0f, -600f);
                container.offsetMax = new Vector2(0f, 0f);
                _settingsContainers[i] = container;
            }

            BuildSettingsGeneralCategory(_settingsContainers[0]);
            BuildSettingsGraphicsCategory(_settingsContainers[1]);
            BuildSettingsControlsCategory(_settingsContainers[2]);
            BuildSettingsAudioCategory(_settingsContainers[3]);

            SelectSettingsCategory(0);
            ApplySettings();
        }

        private void BuildSettingsGeneralCategory(RectTransform parent)
        {
            float y = -12f;
            y = AddSectionTitle(parent, "Interface & Compte", y);
            _settingValues[9] = MakeSettingRowAt(parent, 9, "Taille interface", true, y);
            y -= 60f;
            _settingValues[10] = MakeSettingRowAt(parent, 10, "Noms des joueurs", false, y);
            y -= 66f;

            Button resetAll = MakeButton("BtnResetAll", parent, "Réinitialiser tous les réglages", 18, Dark,
                new Vector2(0.5f, 1f), new Vector2(440f, 48f), new Vector2(0f, y));
            _resetAllLabel = resetAll.GetComponentInChildren<Text>();
            resetAll.onClick.AddListener(OnResetAllSettingsClicked);
            y -= 58f;

            Button logoutBtn = MakeButton("BtnDeconnexion", parent, "Se déconnecter du compte", 19, Red,
                new Vector2(0.5f, 1f), new Vector2(440f, 48f), new Vector2(0f, y));
            logoutBtn.onClick.AddListener(LogoutFromPhone);
            y -= 64f;

            Text credits = MakeText("Credits", parent,
                "Plan d'Abidjan : © contributeurs OpenStreetMap (ODbL).\nAssets : Poly Haven (CC0), MakeHuman/MPFB (CC0), Microsoft Rocketbox (MIT).\nTaser jaune : Richard Speight, Sketchfab (CC BY 4.0). Armes : we3d1234, janxo (CGTrader).\nVéhicules (Sketchfab, CC BY 4.0) : Comrade1280, mk2design, Gest.lt, e-restrepo1114, Brout.",
                13, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(436f, 116f), new Vector2(0f, y));
            credits.color = new Color(1f, 1f, 1f, 0.55f);
            credits.horizontalOverflow = HorizontalWrapMode.Wrap;
            y -= 128f;

            SetCategoryHeight(0, parent, -y + 20f);
        }

        private void BuildSettingsGraphicsCategory(RectTransform parent)
        {
            float y = -12f;
            y = AddSectionTitle(parent, "Qualité visuelle & Performances", y);
            (int id, string label, bool stepper)[] rows =
            {
                (0, "Qualité globale", true),
                (1, "Résolution 3D", true),
                (2, "Distance vue", true),
                (3, "Ombres", true),
                (4, "Effets visuels", false),
                (5, "Limite FPS", true),
                (6, "Passants (foule)", true),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                _settingValues[rows[i].id] = MakeSettingRowAt(parent, rows[i].id, rows[i].label, rows[i].stepper, y);
                y -= 60f;
            }
            SetCategoryHeight(1, parent, -y + 20f);
        }

        private void BuildSettingsControlsCategory(RectTransform parent)
        {
            float y = -12f;
            y = AddSectionTitle(parent, "Caméra & Joystick", y);
            _settingValues[7] = MakeSettingRowAt(parent, 7, "Sensibilité vue", true, y);
            y -= 60f;
            _settingValues[8] = MakeSettingRowAt(parent, 8, "Joystick", false, y);
            y -= 70f;

            y = AddSectionTitle(parent, "Disposition des touches", y);

            RectTransform layoutCard = MakeImage("TouchesCard", parent, Row, new Vector2(0.5f, 1f), new Vector2(440f, 128f), new Vector2(0f, y));
            Text title = MakeText("Titre", layoutCard, "Déplacer les touches à l'écran", 19, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(416f, 28f), new Vector2(14f, -10f));
            title.fontStyle = FontStyle.Bold;

            _hudLayoutStatusText = MakeText("Etat", layoutCard, "Disposition par défaut", 15, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(416f, 28f), new Vector2(14f, -40f));
            _hudLayoutStatusText.color = new Color(0.75f, 0.88f, 1f, 0.9f);

            Button editBtn = MakeButton("ModifierTouches", layoutCard, "Déplacer les touches", 18, SettingsAccentBlue,
                new Vector2(0f, 0f), new Vector2(236f, 44f), new Vector2(12f, 12f));
            editBtn.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            editBtn.onClick.AddListener(OpenTouchLayoutEditor);

            Button resetBtn = MakeButton("ResetTouches", layoutCard, "Par défaut", 17, Dark,
                new Vector2(1f, 0f), new Vector2(170f, 44f), new Vector2(-12f, 12f));
            _resetControlsLabel = resetBtn.GetComponentInChildren<Text>();
            resetBtn.onClick.AddListener(OnResetControlsClicked);
            y -= 142f;

            Text tip = MakeText("Tip", parent,
                "Vous pouvez déplacer et redimensionner chaque bouton (à pied et en véhicule) librement.",
                14, TextAnchor.UpperLeft, new Vector2(0.5f, 1f), new Vector2(436f, 42f), new Vector2(0f, y));
            tip.color = new Color(1f, 1f, 1f, 0.6f);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            y -= 50f;

            SetCategoryHeight(2, parent, -y + 20f);
        }

        private void BuildSettingsAudioCategory(RectTransform parent)
        {
            float y = -12f;
            y = AddSectionTitle(parent, "Audio du jeu & Téléphone", y);
            _settingValues[11] = MakeSettingRowAt(parent, 11, "Volume sonore", true, y);
            y -= 60f;
            _settingValues[12] = MakeSettingRowAt(parent, 12, "Sonnerie", false, y);
            y -= 70f;
            SetCategoryHeight(3, parent, -y + 20f);
        }

        private float AddSectionTitle(RectTransform parent, string title, float y)
        {
            Text txt = MakeText("Sec_" + title, parent, title.ToUpperInvariant(), 14, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(436f, 24f), new Vector2(0f, y));
            txt.color = new Color(0.65f, 0.80f, 0.96f, 0.85f);
            txt.fontStyle = FontStyle.Bold;
            return y - 28f;
        }

        private void SetCategoryHeight(int cat, RectTransform container, float height)
        {
            _settingsCategoryHeights[cat] = height;
            container.sizeDelta = new Vector2(0f, height);
            container.offsetMin = new Vector2(0f, -height);
            container.offsetMax = new Vector2(0f, 0f);
        }

        private void SelectSettingsCategory(int cat)
        {
            _settingsCategory = Mathf.Clamp(cat, 0, SettingsCategoryCount - 1);
            for (int i = 0; i < SettingsCategoryCount; i++)
            {
                bool active = i == _settingsCategory;
                if (_settingsContainers[i] != null)
                    _settingsContainers[i].gameObject.SetActive(active);
                if (_settingsTabs[i] != null && _settingsTabs[i].targetGraphic is Image img)
                    img.color = active ? SettingsTabActive : SettingsTabIdle;
            }

            if (_settingsScroll != null)
            {
                _settingsScroll.SetContentHeight(_settingsCategoryHeights[_settingsCategory]);
                _settingsScroll.ResetToTopAnimated(true);
            }
            RefreshSettingsTexts();
        }

        /// <summary>Ligne de réglage compacte : libellé, valeur, et boutons − / + (ou bascule).</summary>
        private Text MakeSettingRowAt(RectTransform parent, int setting, string label, bool stepper, float y)
        {
            RectTransform row = MakeImage("Reglage_" + setting, parent, Row, new Vector2(0.5f, 1f), new Vector2(440f, 52f), new Vector2(0f, y));
            MakeText("Libelle", row, label, 19, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(210f, 46f), new Vector2(14f, 0f));
            if (stepper)
            {
                Button minus = MakeButton("Moins", row, "−", 26, Dark, new Vector2(1f, 0.5f), new Vector2(44f, 40f), new Vector2(-156f, 0f));
                minus.onClick.AddListener(() => ChangeSetting(setting, -1));
                Button plus = MakeButton("Plus", row, "+", 26, Dark, new Vector2(1f, 0.5f), new Vector2(44f, 40f), new Vector2(-8f, 0f));
                plus.onClick.AddListener(() => ChangeSetting(setting, 1));
                return MakeText("Valeur", row, string.Empty, 18, TextAnchor.MiddleCenter, new Vector2(1f, 0.5f), new Vector2(100f, 40f), new Vector2(-54f, 0f));
            }

            Button toggle = MakeButton("Bascule", row, string.Empty, 18, Dark, new Vector2(1f, 0.5f), new Vector2(140f, 40f), new Vector2(-8f, 0f));
            toggle.onClick.AddListener(() => ChangeSetting(setting, 0));
            return toggle.GetComponentInChildren<Text>();
        }

        private void ChangeSetting(int setting, int direction)
        {
            switch (setting)
            {
                case 0: // Préréglage Qualité
                {
                    int cur = GameSettings.Preset == QualityPreset.VeryLow ? 0
                            : GameSettings.Preset == QualityPreset.High ? 2 : 1;
                    int next = (cur + (direction == 0 ? 1 : direction) + 3) % 3;
                    GameSettings.ApplyPreset(next == 0 ? QualityPreset.VeryLow : next == 2 ? QualityPreset.High : QualityPreset.Balanced);
                    RefreshSettingsTexts();
                    return;
                }
                case 1: // Résolution (RenderScale 50%..100%)
                    GameSettings.SetRenderScale(Mathf.Round((GameSettings.RenderScale + direction * 0.1f) * 10f) / 10f);
                    break;
                case 2: // Distance d'affichage (120m..600m)
                    GameSettings.SetDrawDistance(Mathf.Round((GameSettings.DrawDistance + direction * 60f) / 30f) * 30f);
                    break;
                case 3: // Ombres (0, 1, 2)
                {
                    int next = (GameSettings.ShadowMode + (direction == 0 ? 1 : direction) + 3) % 3;
                    GameSettings.SetShadowMode(next);
                    break;
                }
                case 4: // Effets visuels
                    GameSettings.SetPostProcessing(!GameSettings.PostProcessing);
                    break;
                case 5: // FPS cible
                {
                    int idx = Array.IndexOf(FpsChoices, GameSettings.TargetFps);
                    if (idx < 0) idx = 2;
                    idx = (idx + (direction == 0 ? 1 : direction) + FpsChoices.Length) % FpsChoices.Length;
                    GameSettings.SetTargetFps(FpsChoices[idx]);
                    break;
                }
                case 6: // Passants
                {
                    int idx = Array.IndexOf(PedChoices, GameSettings.PedestrianCount);
                    if (idx < 0) idx = 2;
                    idx = (idx + (direction == 0 ? 1 : direction) + PedChoices.Length) % PedChoices.Length;
                    GameSettings.SetPedestrianCount(PedChoices[idx]);
                    break;
                }
                case 7: // Sensibilité caméra
                    GameSettings.SetCameraSensitivity(Mathf.Round((GameSettings.CameraSensitivity + direction * 0.1f) * 10f) / 10f);
                    break;
                case 8: // Joystick fixe / flottant
                    GameSettings.SetFixedJoystick(!GameSettings.FixedJoystick);
                    break;
                case 9: // Taille HUD
                    GameSettings.SetHudScale(Mathf.Round((GameSettings.HudScale + direction * 0.1f) * 10f) / 10f);
                    break;
                case 10: // Afficher noms des joueurs
                    GameSettings.SetShowNameTags(!GameSettings.ShowNameTags);
                    break;
                case 11: // Volume
                    GameSettings.SetMasterVolume(Mathf.Round((GameSettings.MasterVolume + direction * 0.1f) * 10f) / 10f);
                    break;
                case 12: // Sonnerie
                    GameSettings.SetRingtoneIndex(GameSettings.RingtoneIndex == 0 ? 1 : 0);
                    break;
            }

            GameSettings.Apply();
            GameSettings.Save();
            RefreshSettingsTexts();
        }

        private void ApplySettings()
        {
            GameSettings.Apply();
            MobilePlayerControls.LookSensitivity = GameSettings.CameraSensitivity;
            RefreshSettingsTexts();
        }

        private void RefreshSettingsTexts()
        {
            if (_settingValues[0] == null)
                return;

            _settingValues[0].text = GameSettings.Preset switch
            {
                QualityPreset.VeryLow => "Très bas",
                QualityPreset.Balanced => "Équilibré",
                QualityPreset.High => "Élevé",
                _ => "Perso",
            };
            _settingValues[1].text = Mathf.RoundToInt(GameSettings.RenderScale * 100f) + " %";
            _settingValues[2].text = Mathf.RoundToInt(GameSettings.DrawDistance) + " m";
            _settingValues[3].text = GameSettings.ShadowMode switch { 0 => "Non", 1 => "Basses", _ => "Normales" };
            _settingValues[4].text = GameSettings.PostProcessing ? "Oui" : "Non";
            _settingValues[5].text = GameSettings.TargetFps + " FPS";
            _settingValues[6].text = GameSettings.PedestrianCount.ToString();
            _settingValues[7].text = "× " + GameSettings.CameraSensitivity.ToString("0.0");
            _settingValues[8].text = GameSettings.FixedJoystick ? "Fixe" : "Flottant";
            _settingValues[9].text = Mathf.RoundToInt(GameSettings.HudScale * 100f) + " %";
            _settingValues[10].text = GameSettings.ShowNameTags ? "Affichés" : "Masqués";
            _settingValues[11].text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + " %";
            _settingValues[12].text = GameSettings.RingtoneIndex == 0 ? "Oui" : "Non";

            if (_hudLayoutStatusText != null)
            {
                int customCount = GameSettings.CustomHudControlCount;
                _hudLayoutStatusText.text = customCount == 0
                    ? "Disposition par défaut"
                    : customCount + (customCount > 1 ? " touches personnalisées" : " touche personnalisée");
            }
            if (_resetControlsLabel != null && Time.unscaledTime > _resetControlsConfirmUntil)
                _resetControlsLabel.text = "Par défaut";
            if (_resetAllLabel != null && Time.unscaledTime > _resetAllConfirmUntil)
                _resetAllLabel.text = "Réinitialiser tous les réglages";
        }

        private void OnResetControlsClicked()
        {
            if (Time.unscaledTime > _resetControlsConfirmUntil)
            {
                _resetControlsConfirmUntil = Time.unscaledTime + 3f;
                if (_resetControlsLabel != null)
                    _resetControlsLabel.text = "Confirmer ?";
                return;
            }
            _resetControlsConfirmUntil = 0f;
            GameSettings.ResetHudLayout();
            RefreshSettingsTexts();
        }

        private void OnResetAllSettingsClicked()
        {
            if (Time.unscaledTime > _resetAllConfirmUntil)
            {
                _resetAllConfirmUntil = Time.unscaledTime + 3f;
                if (_resetAllLabel != null)
                    _resetAllLabel.text = "Confirmer la réinitialisation ?";
                return;
            }
            _resetAllConfirmUntil = 0f;
            GameSettings.ResetAllSettings();
            RefreshSettingsTexts();
        }

        private void OpenTouchLayoutEditor()
        {
            HudLayoutEditor editor = HudLayoutEditor.GetOrCreate();
            if (editor == null)
                return;

            _phone.gameObject.SetActive(false);
            editor.Open(() =>
            {
                if (_phone != null)
                {
                    _phone.SetAsLastSibling();
                    _phone.gameObject.SetActive(true);
                    ShowPage(Page.Settings, animate: false);
                    SelectSettingsCategory(2);
                }
            });
        }
    }
}
