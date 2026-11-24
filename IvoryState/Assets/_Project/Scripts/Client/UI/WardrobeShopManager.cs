using System;
using IvoryState.Client.Cameras;
using IvoryState.Client.Characters;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Gestionnaire de personnalisation et de la boutique de garde-robe (Écrans 3 et 4) :
    /// <list type="bullet">
    /// <item><b>Écran 3 (<c>CharacterCreationPanel</c>)</b> : encart <c>PSEUDO</c> en haut à gauche,
    /// zone de rotation tactile 360° sur socle lumineux, barre de 6 onglets (<c>Genre</c>, <c>Teint</c>,
    /// <c>Cheveux</c>, <c>Vêtements</c>, <c>Chaussures</c>, <c>Accessoires</c>) et bouton ambre
    /// <c>CONFIRMER ET JOUER</c>.</item>
    /// <item><b>Écran 4 (<c>InGameWardrobePanel</c>)</b> : barre supérieure (<c>RETOUR AU JEU</c>,
    /// <c>CHANGEMENT D'APPARENCE</c>, solde <c>FCFA</c> en temps réel), boutons de zoom caméra
    /// (<b>Corps entier</b> / <b>Visage</b>), 5 onglets boutique (<c>Cheveux</c>, <c>Vêtements</c>,
    /// <c>Chaussures</c>, <c>Accessoires</c>, <c>Tenues</c>) avec badges <c>Possédé</c> ou prix
    /// <c>🔒 FCFA</c>, et boutons <c>RÉINITIALISER</c> / <c>APPLIQUER</c>.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WardrobeShopManager : MonoBehaviour
    {
        public static WardrobeShopManager Instance { get; private set; }

        private const string OwnedMaskPrefKey = "ivory.wardrobe.owned";
        private const int GridSlots = 6;

        // Charte Dark Glassmorphism + Ambre
        private static readonly Color GlassPanel = new Color(0.11f, 0.12f, 0.14f, 0.86f);
        private static readonly Color GlassCard = new Color(0.15f, 0.16f, 0.19f, 0.90f);
        private static readonly Color GlassDark = new Color(0.07f, 0.08f, 0.10f, 0.92f);
        private static readonly Color Amber = new Color(0.91f, 0.51f, 0.10f, 1f);
        private static readonly Color BorderSubtle = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color Emerald = new Color(0.22f, 0.82f, 0.44f, 1f);

        private static readonly string[] CreatorTabs = { "Genre", "Teint", "Cheveux", "Vêtements", "Chaussures", "Accessoires" };
        private static readonly string[] WardrobeTabs = { "Cheveux", "Vêtements", "Chaussures", "Accessoires", "Tenues" };

        private static readonly long[] ItemPrices = { 0, 5000, 8000, 15000, 25000, 35000 };

        private Font _font;
        private RectTransform _characterCreationPanel;
        private RectTransform _inGameWardrobePanel;

        // Écran 3 : CharacterCreationPanel
        private InputField _creatorPseudoInput;
        private Text _creatorErrorText;
        private readonly Button[] _creatorTabBtns = new Button[6];
        private RectTransform _creatorGenderGroup;
        private Button _creatorMaleBtn;
        private Button _creatorFemaleBtn;
        private Button _creatorPrevFaceBtn;
        private Button _creatorNextFaceBtn;
        private Text _creatorFaceLabel;
        private RectTransform _creatorSkinGroup;
        private readonly Button[] _creatorSkinSwatches = new Button[6];
        private RectTransform _creatorGridGroup;
        private readonly Button[] _creatorItemCards = new Button[GridSlots];
        private readonly Text[] _creatorItemTitles = new Text[GridSlots];
        private readonly Image[] _creatorItemDots = new Image[GridSlots];
        private int _activeCreatorTab;

        // Écran 4 : InGameWardrobePanel
        private Text _wardrobeBalanceText;
        private Text _wardrobeFeedbackText;
        private Button _camFullBodyBtn;
        private Button _camFaceZoomBtn;
        private readonly Button[] _wardrobeTabBtns = new Button[5];
        private readonly Button[] _wardrobeCards = new Button[GridSlots];
        private readonly Text[] _wardrobeCardTitles = new Text[GridSlots];
        private readonly Text[] _wardrobeCardBadges = new Text[GridSlots];
        private readonly Image[] _wardrobeCardOutlines = new Image[GridSlots];
        private Text _applyBtnLabel;
        private int _activeWardrobeTab = 1;
        private long _shownBalance = long.MinValue;
        private int _ownedMask = 1; // Bitmask local des articles débloqués (index 0 toujours possédé)
        private ClothingStorePoint _activeStore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            Instance = this;
            _ownedMask = PlayerPrefs.GetInt(OwnedMaskPrefKey, 1) | 1;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Initialize(RectTransform root, Font font)
        {
            _font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            HudIconLibrary.EnsureInitialized();

            BuildCharacterCreationPanel(root);
            BuildInGameWardrobePanel(root);

            UIManager.Instance?.RegisterPanel(UIScreenState.CharacterCreation, _characterCreationPanel);
            UIManager.Instance?.RegisterPanel(UIScreenState.InGameWardrobe, _inGameWardrobePanel);
        }

        private void Update()
        {
            if (_inGameWardrobePanel != null && _inGameWardrobePanel.gameObject.activeSelf)
            {
                PlayerInteractor local = PlayerInteractor.Local;
                long balance = local != null && local.Economy != null ? local.Economy.Balance : 0;
                if (balance != _shownBalance)
                {
                    _shownBalance = balance;
                    _wardrobeBalanceText.text = Money.Format(balance);
                }
            }
        }

        #region Logique Écran 3 : Création de Personnage

        public void PrepareCharacterCreation()
        {
            CharacterCustomizer.Instance?.BeginPreview();
            _creatorErrorText.text = string.Empty;
            if (_creatorPseudoInput != null && string.IsNullOrEmpty(_creatorPseudoInput.text) && PlayerNetworkIdentity.Local != null)
            {
                string serverName = PlayerNames.ValidateAccountPseudo(PlayerNetworkIdentity.Local.DisplayName);
                if (!string.IsNullOrEmpty(serverName))
                    _creatorPseudoInput.text = serverName;
            }
            SelectCreatorTab(0);
        }

        private void SelectCreatorTab(int tabIndex)
        {
            _activeCreatorTab = Mathf.Clamp(tabIndex, 0, CreatorTabs.Length - 1);
            for (int i = 0; i < _creatorTabBtns.Length; i++)
            {
                bool active = i == _activeCreatorTab;
                _creatorTabBtns[i].GetComponent<Image>().color = active ? Amber : GlassDark;
                _creatorTabBtns[i].GetComponentInChildren<Text>().color = active ? Color.black : Color.white;
            }

            _creatorGenderGroup.gameObject.SetActive(_activeCreatorTab == 0);
            _creatorSkinGroup.gameObject.SetActive(_activeCreatorTab == 1);
            _creatorGridGroup.gameObject.SetActive(_activeCreatorTab >= 2);

            // Zoom caméra automatique sur le visage pour Teint/Cheveux, Corps entier pour le reste.
            TurntableCameraController.Instance?.SetViewMode(
                (_activeCreatorTab == 1 || _activeCreatorTab == 2) ? TurntableViewMode.FaceZoom : TurntableViewMode.FullBody);

            RefreshCreatorView();
        }

        private void RefreshCreatorView()
        {
            CharacterCustomizer customizer = CharacterCustomizer.Instance;
            if (customizer == null)
                return;
            PlayerAppearanceData data = customizer.Working;

            // Genre & Visage
            _creatorMaleBtn.GetComponent<Image>().color = !data.IsFemale ? Amber : GlassDark;
            _creatorMaleBtn.GetComponentInChildren<Text>().color = !data.IsFemale ? Color.black : Color.white;
            _creatorFemaleBtn.GetComponent<Image>().color = data.IsFemale ? Amber : GlassDark;
            _creatorFemaleBtn.GetComponentInChildren<Text>().color = data.IsFemale ? Color.black : Color.white;
            _creatorFaceLabel.text = "Visage n° " + (data.FaceIndex + 1);

            // Teint
            for (int i = 0; i < _creatorSkinSwatches.Length; i++)
            {
                _creatorSkinSwatches[i].transform.localScale = i == data.SkinToneIndex ? new Vector3(1.18f, 1.18f, 1f) : Vector3.one;
            }

            // Grille (Cheveux, Vêtements, Chaussures, Accessoires)
            if (_activeCreatorTab >= 2)
            {
                string[] names = GetCategoryNames(_activeCreatorTab - 2);
                int currentSelection = GetCategoryValue(data, _activeCreatorTab - 2);
                for (int i = 0; i < GridSlots; i++)
                {
                    bool used = i < names.Length;
                    _creatorItemCards[i].gameObject.SetActive(used);
                    if (!used)
                        continue;
                    bool selected = i == currentSelection;
                    _creatorItemTitles[i].text = names[i];
                    _creatorItemCards[i].GetComponent<Image>().color = selected ? new Color(0.91f, 0.51f, 0.10f, 0.26f) : GlassCard;
                    _creatorItemDots[i].color = selected ? Amber : new Color(1f, 1f, 1f, 0.2f);
                }
            }

            customizer.ApplyWorkingToPreview();
        }

        private void OnCreatorItemClicked(int itemIndex)
        {
            CharacterCustomizer customizer = CharacterCustomizer.Instance;
            if (customizer == null)
                return;
            SetCategoryValue(customizer.Working, _activeCreatorTab - 2, itemIndex);
            RefreshCreatorView();
        }

        private void OnConfirmAndPlayClicked()
        {
            string rawPseudo = _creatorPseudoInput.text != null ? _creatorPseudoInput.text.Trim() : string.Empty;
            string validated = PlayerNames.ValidateAccountPseudo(rawPseudo);
            if (validated == null)
            {
                _creatorErrorText.text = "Pseudo requis : 3 à 16 lettres (sans chiffre ni espace).";
                return;
            }

            _creatorErrorText.text = "Enregistrement du citoyen…";
            if (CharacterCustomizer.Instance != null)
                CharacterCustomizer.Instance.Working.Pseudo = validated;

            if (AuthFlowController.Instance != null)
            {
                AuthFlowController.Instance.CompleteRegistrationFromCreator(validated, (err) =>
                {
                    _creatorErrorText.text = err;
                });
            }
            else
            {
                CharacterCustomizer.Instance?.CommitAndSave();
                UIManager.Instance?.TransitionTo(UIScreenState.InGame);
            }
        }

        #endregion

        #region Logique Écran 4 : Changement d'Apparence en Jeu (Garde-robe)

        public void OpenForStore(ClothingStorePoint store)
        {
            _activeStore = store;
            _shownBalance = long.MinValue;
            _wardrobeFeedbackText.text = string.Empty;
            CharacterCustomizer.Instance?.BeginPreview();
            SelectWardrobeTab(1); // Onglet Vêtements par défaut
            SetCameraZoom(TurntableViewMode.FullBody);
        }

        private void SetCameraZoom(TurntableViewMode mode)
        {
            TurntableCameraController.Instance?.SetViewMode(mode);
            bool full = mode == TurntableViewMode.FullBody;
            _camFullBodyBtn.GetComponent<Image>().color = full ? Amber : GlassPanel;
            _camFullBodyBtn.GetComponentInChildren<Text>().color = full ? Color.black : Color.white;
            _camFaceZoomBtn.GetComponent<Image>().color = !full ? Amber : GlassPanel;
            _camFaceZoomBtn.GetComponentInChildren<Text>().color = !full ? Color.black : Color.white;
        }

        private void SelectWardrobeTab(int tabIndex)
        {
            _activeWardrobeTab = Mathf.Clamp(tabIndex, 0, WardrobeTabs.Length - 1);
            for (int i = 0; i < _wardrobeTabBtns.Length; i++)
            {
                bool active = i == _activeWardrobeTab;
                _wardrobeTabBtns[i].GetComponent<Image>().color = active ? Amber : GlassDark;
                _wardrobeTabBtns[i].GetComponentInChildren<Text>().color = active ? Color.black : Color.white;
            }

            if (_activeWardrobeTab == 0)
                SetCameraZoom(TurntableViewMode.FaceZoom);
            else if (_activeWardrobeTab == 1 || _activeWardrobeTab == 2 || _activeWardrobeTab == 4)
                SetCameraZoom(TurntableViewMode.FullBody);

            RefreshWardrobeGrid();
        }

        private void RefreshWardrobeGrid()
        {
            CharacterCustomizer customizer = CharacterCustomizer.Instance;
            if (customizer == null)
                return;

            PlayerAppearanceData data = customizer.Working;
            int cat = _activeWardrobeTab == 4 ? 1 : _activeWardrobeTab; // Tenues = ensembles complets basés sur Vêtements
            string[] names = GetCategoryNames(cat);
            int currentSelection = GetCategoryValue(data, cat);

            for (int i = 0; i < GridSlots; i++)
            {
                bool used = i < names.Length;
                _wardrobeCards[i].gameObject.SetActive(used);
                if (!used)
                    continue;

                bool selected = i == currentSelection;
                bool owned = IsItemOwned(cat, i);
                long price = GetItemPrice(cat, i);

                _wardrobeCardTitles[i].text = _activeWardrobeTab == 4 ? ("Ensemble " + names[i]) : names[i];
                _wardrobeCards[i].GetComponent<Image>().color = selected ? new Color(0.91f, 0.51f, 0.10f, 0.25f) : GlassCard;
                _wardrobeCardOutlines[i].color = selected ? Amber : BorderSubtle;

                if (owned || price <= 0)
                {
                    _wardrobeCardBadges[i].text = "✓ Possédé";
                    _wardrobeCardBadges[i].color = Emerald;
                }
                else
                {
                    _wardrobeCardBadges[i].text = "🔒 " + Money.Format(price);
                    _wardrobeCardBadges[i].color = Amber;
                }
            }

            long cartTotal = CalculateCartTotal();
            _applyBtnLabel.text = cartTotal > 0 ? ("APPLIQUER (" + Money.Format(cartTotal) + ")") : "APPLIQUER";
            customizer.ApplyWorkingToPreview();
        }

        private void OnWardrobeCardClicked(int index)
        {
            CharacterCustomizer customizer = CharacterCustomizer.Instance;
            if (customizer == null)
                return;

            int cat = _activeWardrobeTab == 4 ? 1 : _activeWardrobeTab;
            SetCategoryValue(customizer.Working, cat, index);
            if (_activeWardrobeTab == 4)
            {
                // Un ensemble assortit aussi chaussures et coiffure.
                customizer.Working.ShoesIndex = index % CharacterCustomizer.ShoesNames.Length;
            }
            _wardrobeFeedbackText.text = string.Empty;
            RefreshWardrobeGrid();
        }

        private void OnResetWardrobeClicked()
        {
            CharacterCustomizer.Instance?.ResetWorkingToSaved();
            _wardrobeFeedbackText.text = "Essayage réinitialisé.";
            _wardrobeFeedbackText.color = new Color(0.8f, 0.85f, 0.9f);
            RefreshWardrobeGrid();
        }

        private void OnApplyWardrobeClicked()
        {
            CharacterCustomizer customizer = CharacterCustomizer.Instance;
            if (customizer == null)
                return;

            long totalCost = CalculateCartTotal();
            PlayerInteractor local = PlayerInteractor.Local;
            long balance = local != null && local.Economy != null ? local.Economy.Balance : 0;

            if (totalCost > 0 && balance < totalCost)
            {
                _wardrobeFeedbackText.text = "Solde insuffisant (" + Money.Format(totalCost) + " requis).";
                _wardrobeFeedbackText.color = new Color(1f, 0.45f, 0.40f);
                return;
            }

            // Si achat d'une tenue officielle en boutique, déclenche l'achat serveur.
            int outfitIdx = Mathf.Clamp(customizer.Working.ClothesIndex, 0, ClothingStorePoint.Outfits.Count - 1);
            if (_activeStore != null && local != null)
                local.RequestChoice(_activeStore, outfitIdx, false);

            // Débloque localement tous les articles équipés.
            for (int cat = 0; cat < 4; cat++)
            {
                int val = GetCategoryValue(customizer.Working, cat);
                MarkItemOwned(cat, val);
            }
            PlayerPrefs.SetInt(OwnedMaskPrefKey, _ownedMask);
            PlayerPrefs.Save();

            customizer.CommitAndSave();
            UIManager.Instance?.TransitionTo(UIScreenState.InGame);
        }

        private long CalculateCartTotal()
        {
            CharacterCustomizer customizer = CharacterCustomizer.Instance;
            if (customizer == null)
                return 0;
            long total = 0;
            for (int cat = 0; cat < 4; cat++)
            {
                int val = GetCategoryValue(customizer.Working, cat);
                if (!IsItemOwned(cat, val))
                    total += GetItemPrice(cat, val);
            }
            return total;
        }

        private bool IsItemOwned(int category, int index)
        {
            if (index == 0)
                return true;
            if (category == 1 && PlayerNetworkIdentity.Local != null && index < ClothingStorePoint.Outfits.Count)
            {
                if (PlayerNetworkIdentity.Local.Outfit == index || PlayerNetworkIdentity.Local.ServerOwnsOutfit(index))
                    return true;
            }
            int bit = (category * 6) + index;
            return (_ownedMask & (1 << bit)) != 0;
        }

        private void MarkItemOwned(int category, int index)
        {
            int bit = (category * 6) + index;
            _ownedMask |= (1 << bit);
        }

        private static long GetItemPrice(int category, int index)
        {
            if (index <= 0)
                return 0;
            if (category == 1 && index < ClothingStorePoint.Outfits.Count)
                return ClothingStorePoint.Outfits.Prices[index];
            return ItemPrices[Mathf.Clamp(index, 0, ItemPrices.Length - 1)];
        }

        private static string[] GetCategoryNames(int category) => category switch
        {
            0 => CharacterCustomizer.HairNames,
            1 => CharacterCustomizer.ClothesNames,
            2 => CharacterCustomizer.ShoesNames,
            _ => CharacterCustomizer.AccessoryNames,
        };

        private static int GetCategoryValue(PlayerAppearanceData data, int category) => category switch
        {
            0 => data.HairIndex,
            1 => data.ClothesIndex,
            2 => data.ShoesIndex,
            _ => data.AccessoryIndex,
        };

        private static void SetCategoryValue(PlayerAppearanceData data, int category, int value)
        {
            switch (category)
            {
                case 0: data.HairIndex = value; break;
                case 1: data.ClothesIndex = value; break;
                case 2: data.ShoesIndex = value; break;
                default: data.AccessoryIndex = value; break;
            }
        }

        #endregion

        #region Construction Écran 3 : CharacterCreationPanel

        private void BuildCharacterCreationPanel(RectTransform root)
        {
            _characterCreationPanel = MakePanel("CharacterCreationPanel", root, new Color(0.02f, 0.03f, 0.05f, 0.18f));

            // Zone tactile de rotation 360° à gauche/centre
            BuildTurntableDragZone(_characterCreationPanel);

            // --- Haut-Gauche : Logo IVORY STATE + Encart PSEUDO ---
            Sprite creationLogo = HudIconLibrary.IvoryLogo;
            if (creationLogo != null)
            {
                MakeImg("TopLogo", _characterCreationPanel, creationLogo, Color.white,
                    new Vector2(0f, 1f), new Vector2(240f, 48f), new Vector2(56f, -36f));
            }
            else
            {
                Text logo = MakeText("TopLogo", _characterCreationPanel, "IVORY <color=#F59E0B>STATE</color>", 36, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(360f, 48f), new Vector2(56f, -36f));
                logo.supportRichText = true;
                logo.fontStyle = FontStyle.Bold;
            }

            RectTransform pseudoCard = MakeRounded("PseudoCard", _characterCreationPanel, GlassPanel,
                new Vector2(0f, 1f), new Vector2(440f, 126f), new Vector2(56f, -98f));
            AddBorder(pseudoCard, BorderSubtle);
            MakeText("PseudoTitle", pseudoCard, "PSEUDO DU CITOYEN", 16, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(400f, 24f), new Vector2(20f, -14f)).color = Amber;

            RectTransform pseudoBox = MakeRounded("PseudoBox", pseudoCard, GlassDark,
                new Vector2(0.5f, 0f), new Vector2(400f, 58f), new Vector2(0f, 18f));
            AddBorder(pseudoBox, BorderSubtle);
            pseudoBox.GetComponent<Image>().raycastTarget = true;

            Text pText = MakeText("Text", pseudoBox, string.Empty, 24, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(368f, 50f), new Vector2(16f, 0f));
            Text pHint = MakeText("Placeholder", pseudoBox, "Entrez votre pseudo...", 22, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(368f, 50f), new Vector2(16f, 0f));
            pHint.color = new Color(1f, 1f, 1f, 0.35f);
            pHint.fontStyle = FontStyle.Italic;

            _creatorPseudoInput = pseudoBox.gameObject.AddComponent<InputField>();
            _creatorPseudoInput.targetGraphic = pseudoBox.GetComponent<Image>();
            _creatorPseudoInput.textComponent = pText;
            _creatorPseudoInput.placeholder = pHint;
            _creatorPseudoInput.characterLimit = PlayerNames.MaxLength;

            _creatorErrorText = MakeText("CreatorError", _characterCreationPanel, string.Empty, 18, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(440f, 44f), new Vector2(60f, -234f));
            _creatorErrorText.color = new Color(1f, 0.50f, 0.45f);

            // Indication rotation 360° sous le socle
            MakeText("RotateHint", _characterCreationPanel, "⟲   Glisser horizontalement pour tourner à 360°", 18, TextAnchor.MiddleCenter,
                new Vector2(0.28f, 0f), new Vector2(460f, 36f), new Vector2(0f, 44f)).color = new Color(1f, 1f, 1f, 0.72f);

            // --- Panneau Droit : CustomizationPanel ---
            RectTransform rightPanel = MakeRounded("CustomizationPanel", _characterCreationPanel, GlassPanel,
                new Vector2(1f, 0.5f), new Vector2(820f, 920f), new Vector2(-56f, 0f));
            AddBorder(rightPanel, BorderSubtle);
            MakeBox("TopAccent", rightPanel, Amber, new Vector2(0.5f, 1f), new Vector2(820f, 6f), Vector2.zero);

            MakeText("PanelTitle", rightPanel, "CRÉATION D'APPARENCE", 28, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(520f, 42f), new Vector2(28f, -24f)).fontStyle = FontStyle.Bold;

            // Barre de 6 onglets horizontale
            for (int i = 0; i < CreatorTabs.Length; i++)
            {
                int tab = i;
                Button tabBtn = MakeBtn("Tab_" + CreatorTabs[i], rightPanel, CreatorTabs[i].ToUpperInvariant(), 15, GlassDark, Color.white,
                    new Vector2(0f, 1f), new Vector2(122f, 48f), new Vector2(26f + i * 129f, -82f));
                AddBorder((RectTransform)tabBtn.transform, BorderSubtle);
                tabBtn.onClick.AddListener(() => SelectCreatorTab(tab));
                _creatorTabBtns[i] = tabBtn;
            }

            // Contenu Onglet 0 : Genre & Visage
            _creatorGenderGroup = MakeBox("GenderGroup", rightPanel, new Color(0f, 0f, 0f, 0f),
                new Vector2(0.5f, 1f), new Vector2(760f, 580f), new Vector2(0f, -156f));
            MakeText("LblGender", _creatorGenderGroup, "SÉLECTION DU GENRE", 18, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(400f, 28f), new Vector2(10f, -10f)).color = Amber;

            _creatorMaleBtn = MakeBtn("BtnMale", _creatorGenderGroup, "♂   HOMME", 24, Amber, Color.black,
                new Vector2(0f, 1f), new Vector2(360f, 84f), new Vector2(10f, -50f));
            _creatorMaleBtn.onClick.AddListener(() =>
            {
                if (CharacterCustomizer.Instance != null)
                {
                    CharacterCustomizer.Instance.Working.IsFemale = false;
                    CharacterCustomizer.Instance.Working.FaceIndex = 0;
                    RefreshCreatorView();
                }
            });

            _creatorFemaleBtn = MakeBtn("BtnFemale", _creatorGenderGroup, "♀   FEMME", 24, GlassDark, Color.white,
                new Vector2(1f, 1f), new Vector2(360f, 84f), new Vector2(-10f, -50f));
            _creatorFemaleBtn.onClick.AddListener(() =>
            {
                if (CharacterCustomizer.Instance != null)
                {
                    CharacterCustomizer.Instance.Working.IsFemale = true;
                    CharacterCustomizer.Instance.Working.FaceIndex = 0;
                    RefreshCreatorView();
                }
            });

            MakeText("LblFace", _creatorGenderGroup, "MORPHOLOGIE DU VISAGE", 18, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(400f, 28f), new Vector2(10f, -176f)).color = Amber;
            _creatorPrevFaceBtn = MakeBtn("PrevFace", _creatorGenderGroup, "‹", 36, GlassDark, Color.white,
                new Vector2(0f, 1f), new Vector2(96f, 74f), new Vector2(10f, -216f));
            _creatorPrevFaceBtn.onClick.AddListener(() =>
            {
                if (CharacterCustomizer.Instance != null)
                {
                    CharacterCustomizer.Instance.Working.FaceIndex = (CharacterCustomizer.Instance.Working.FaceIndex + 5) % 6;
                    RefreshCreatorView();
                }
            });
            _creatorFaceLabel = MakeText("FaceLabel", _creatorGenderGroup, "Visage n° 1", 24, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(520f, 74f), new Vector2(0f, -216f));
            _creatorNextFaceBtn = MakeBtn("NextFace", _creatorGenderGroup, "›", 36, GlassDark, Color.white,
                new Vector2(1f, 1f), new Vector2(96f, 74f), new Vector2(-10f, -216f));
            _creatorNextFaceBtn.onClick.AddListener(() =>
            {
                if (CharacterCustomizer.Instance != null)
                {
                    CharacterCustomizer.Instance.Working.FaceIndex = (CharacterCustomizer.Instance.Working.FaceIndex + 1) % 6;
                    RefreshCreatorView();
                }
            });

            // Contenu Onglet 1 : Palette de Teints (pastilles circulaires)
            _creatorSkinGroup = MakeBox("SkinGroup", rightPanel, new Color(0f, 0f, 0f, 0f),
                new Vector2(0.5f, 1f), new Vector2(760f, 580f), new Vector2(0f, -156f));
            MakeText("LblSkin", _creatorSkinGroup, "CARNATION DE PEAU", 18, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(400f, 28f), new Vector2(10f, -10f)).color = Amber;

            for (int i = 0; i < CharacterCustomizer.SkinTones.Length; i++)
            {
                int skinIdx = i;
                RectTransform swatch = MakeImg("Skin_" + i, _creatorSkinGroup, HudIconLibrary.Circle, CharacterCustomizer.SkinTones[i],
                    new Vector2(0f, 1f), new Vector2(96f, 96f), new Vector2(24f + i * 124f, -68f));
                swatch.GetComponent<Image>().raycastTarget = true;
                MakeImg("Ring", swatch, HudIconLibrary.ThinRing, Color.white, new Vector2(0.5f, 0.5f), new Vector2(96f, 96f), Vector2.zero);
                var btn = swatch.gameObject.AddComponent<Button>();
                btn.targetGraphic = swatch.GetComponent<Image>();
                btn.onClick.AddListener(() =>
                {
                    if (CharacterCustomizer.Instance != null)
                    {
                        CharacterCustomizer.Instance.Working.SkinToneIndex = skinIdx;
                        RefreshCreatorView();
                    }
                });
                _creatorSkinSwatches[i] = btn;
            }

            // Contenu Onglets 2..5 : Grille 2×3 (Cheveux, Vêtements, Chaussures, Accessoires)
            _creatorGridGroup = MakeBox("GridGroup", rightPanel, new Color(0f, 0f, 0f, 0f),
                new Vector2(0.5f, 1f), new Vector2(760f, 580f), new Vector2(0f, -156f));
            for (int i = 0; i < GridSlots; i++)
            {
                int slot = i;
                int col = i % 2, row = i / 2;
                RectTransform card = MakeRounded("ItemCard_" + i, _creatorGridGroup, GlassCard,
                    new Vector2(0f, 1f), new Vector2(364f, 152f), new Vector2(10f + col * 378f, -10f - row * 172f));
                AddBorder(card, BorderSubtle);
                card.GetComponent<Image>().raycastTarget = true;
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card.GetComponent<Image>();
                btn.onClick.AddListener(() => OnCreatorItemClicked(slot));

                RectTransform dot = MakeImg("StatusDot", card, HudIconLibrary.Circle, Amber,
                    new Vector2(1f, 1f), new Vector2(18f, 18f), new Vector2(-18f, -18f));
                _creatorItemDots[i] = dot.GetComponent<Image>();

                _creatorItemTitles[i] = MakeText("Title", card, "Option", 22, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.5f), new Vector2(310f, 56f), new Vector2(22f, 10f));
                _creatorItemTitles[i].fontStyle = FontStyle.Bold;

                MakeText("Sub", card, "Inclus dans la garde-robe de départ", 15, TextAnchor.LowerLeft,
                    new Vector2(0f, 0f), new Vector2(320f, 26f), new Vector2(22f, 16f)).color = Emerald;

                _creatorItemCards[i] = btn;
            }

            // Bouton principal en bas à droite : CONFIRMER ET JOUER
            Button confirmBtn = MakeBtn("BtnConfirmAndPlay", rightPanel, "CONFIRMER ET JOUER", 26, Amber, Color.black,
                new Vector2(0.5f, 0f), new Vector2(764f, 78f), new Vector2(0f, 26f));
            confirmBtn.onClick.AddListener(OnConfirmAndPlayClicked);
        }

        #endregion

        #region Construction Écran 4 : InGameWardrobePanel

        private void BuildInGameWardrobePanel(RectTransform root)
        {
            _inGameWardrobePanel = MakePanel("InGameWardrobePanel", root, new Color(0.02f, 0.03f, 0.05f, 0.22f));

            // Zone tactile de rotation 360° à gauche/centre
            BuildTurntableDragZone(_inGameWardrobePanel);

            // --- Barre Supérieure ---
            RectTransform topBar = MakeRounded("TopHeaderBar", _inGameWardrobePanel, GlassPanel,
                new Vector2(0.5f, 1f), new Vector2(1824f, 76f), new Vector2(0f, -24f));
            AddBorder(topBar, BorderSubtle);

            // Haut-Gauche : RETOUR AU JEU
            Button backToGameBtn = MakeBtn("BtnReturnToGame", topBar, "‹   RETOUR AU JEU", 20, GlassDark, Color.white,
                new Vector2(0f, 0.5f), new Vector2(230f, 52f), new Vector2(16f, 0f));
            AddBorder((RectTransform)backToGameBtn.transform, BorderSubtle);
            backToGameBtn.onClick.AddListener(() =>
            {
                CharacterCustomizer.Instance?.ResetWorkingToSaved();
                CharacterCustomizer.Instance?.EndPreview();
                UIManager.Instance?.TransitionTo(UIScreenState.InGame);
            });

            // Haut-Centre : Titre CHANGEMENT D'APPARENCE
            Text headerTitle = MakeText("HeaderTitle", topBar, "CHANGEMENT D'APPARENCE", 28, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(560f, 52f), Vector2.zero);
            headerTitle.fontStyle = FontStyle.Bold;

            // Haut-Droite : Solde en FCFA + Bouton Paramètres
            RectTransform walletPill = MakeRounded("WalletPill", topBar, GlassDark,
                new Vector2(1f, 0.5f), new Vector2(280f, 52f), new Vector2(-80f, 0f));
            AddBorder(walletPill, Amber);
            MakeImg("Diamond", walletPill, HudIconLibrary.Cash, Color.white, new Vector2(0f, 0.5f), new Vector2(30f, 30f), new Vector2(18f, 0f));
            _wardrobeBalanceText = MakeText("BalanceText", walletPill, "250 000 FCFA", 22, TextAnchor.MiddleRight,
                new Vector2(1f, 0.5f), new Vector2(215f, 44f), new Vector2(-16f, 0f));
            _wardrobeBalanceText.fontStyle = FontStyle.Bold;
            _wardrobeBalanceText.color = Amber;

            RectTransform gearBtn = MakeRounded("WardrobeSettingsBtn", topBar, GlassDark,
                new Vector2(1f, 0.5f), new Vector2(52f, 52f), new Vector2(-16f, 0f));
            AddBorder(gearBtn, BorderSubtle);
            gearBtn.GetComponent<Image>().raycastTarget = true;
            var gBtn = gearBtn.gameObject.AddComponent<Button>();
            gBtn.targetGraphic = gearBtn.GetComponent<Image>();
            gBtn.onClick.AddListener(() => PhoneHud.Instance?.OpenSettings());
            MakePaddedIcon("Icon", gearBtn, HudIconLibrary.ParameterGear, Color.white, 0.22f);

            // --- Zone Personnage 3D : 2 boutons de caméra de part et d'autre du socle ---
            _camFullBodyBtn = MakeBtn("BtnCamFullBody", _inGameWardrobePanel, "🧍 CORPS ENTIER", 18, Amber, Color.black,
                new Vector2(0f, 0f), new Vector2(210f, 56f), new Vector2(96f, 48f));
            AddBorder((RectTransform)_camFullBodyBtn.transform, BorderSubtle);
            _camFullBodyBtn.onClick.AddListener(() => SetCameraZoom(TurntableViewMode.FullBody));

            _camFaceZoomBtn = MakeBtn("BtnCamFaceZoom", _inGameWardrobePanel, "👤 VISAGE / HAUT", 18, GlassPanel, Color.white,
                new Vector2(0f, 0f), new Vector2(210f, 56f), new Vector2(730f, 48f));
            AddBorder((RectTransform)_camFaceZoomBtn.transform, BorderSubtle);
            _camFaceZoomBtn.onClick.AddListener(() => SetCameraZoom(TurntableViewMode.FaceZoom));

            MakeText("WardrobeRotateHint", _inGameWardrobePanel, "⟲ Glisser pour pivoter à 360°", 17, TextAnchor.MiddleCenter,
                new Vector2(0f, 0f), new Vector2(360f, 40f), new Vector2(335f, 56f)).color = new Color(1f, 1f, 1f, 0.72f);

            // --- Panneau Droit : WardrobeShopPanel ---
            RectTransform shopPanel = MakeRounded("WardrobeShopPanel", _inGameWardrobePanel, GlassPanel,
                new Vector2(1f, 0f), new Vector2(860f, 920f), new Vector2(-48f, 36f));
            AddBorder(shopPanel, BorderSubtle);
            MakeBox("TopAccent", shopPanel, Amber, new Vector2(0.5f, 1f), new Vector2(860f, 6f), Vector2.zero);

            // 5 Onglets : Cheveux, Vêtements, Chaussures, Accessoires, Tenues
            for (int i = 0; i < WardrobeTabs.Length; i++)
            {
                int tab = i;
                Button tabBtn = MakeBtn("ShopTab_" + WardrobeTabs[i], shopPanel, WardrobeTabs[i].ToUpperInvariant(), 16, GlassDark, Color.white,
                    new Vector2(0f, 1f), new Vector2(156f, 52f), new Vector2(24f + i * 164f, -26f));
                AddBorder((RectTransform)tabBtn.transform, BorderSubtle);
                tabBtn.onClick.AddListener(() => SelectWardrobeTab(tab));
                _wardrobeTabBtns[i] = tabBtn;
            }

            // Grille d'articles 2×3 avec badge Possédé ou Prix FCFA + cadenas
            RectTransform gridRoot = MakeBox("ShopGrid", shopPanel, new Color(0f, 0f, 0f, 0f),
                new Vector2(0.5f, 1f), new Vector2(812f, 660f), new Vector2(0f, -104f));

            for (int i = 0; i < GridSlots; i++)
            {
                int slot = i;
                int col = i % 2, row = i / 2;
                RectTransform card = MakeRounded("ShopCard_" + i, gridRoot, GlassCard,
                    new Vector2(0f, 1f), new Vector2(394f, 196f), new Vector2(col * 416f, -row * 214f));
                card.GetComponent<Image>().raycastTarget = true;

                RectTransform borderBox = MakeRounded("CardBorder", card, new Color(0f, 0f, 0f, 0f),
                    new Vector2(0.5f, 0.5f), new Vector2(394f, 196f), Vector2.zero);
                AddBorder(borderBox, BorderSubtle);
                _wardrobeCardOutlines[i] = borderBox.GetComponent<Image>();

                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card.GetComponent<Image>();
                btn.onClick.AddListener(() => OnWardrobeCardClicked(slot));

                _wardrobeCardTitles[i] = MakeText("ItemTitle", card, "Article", 24, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(340f, 60f), new Vector2(24f, -22f));
                _wardrobeCardTitles[i].fontStyle = FontStyle.Bold;

                MakeText("ItemCategory", card, "Collection Abidjan Prestige", 15, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(340f, 26f), new Vector2(24f, -72f)).color = new Color(1f, 1f, 1f, 0.5f);

                RectTransform badgePill = MakeRounded("BadgePill", card, GlassDark,
                    new Vector2(0f, 0f), new Vector2(200f, 42f), new Vector2(22f, 18f));
                _wardrobeCardBadges[i] = MakeText("BadgeText", badgePill, "✓ Possédé", 18, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(188f, 36f), Vector2.zero);
                _wardrobeCardBadges[i].fontStyle = FontStyle.Bold;

                _wardrobeCards[i] = btn;
            }

            _wardrobeFeedbackText = MakeText("FeedbackText", shopPanel, string.Empty, 18, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(812f, 32f), new Vector2(26f, 108f));

            // Deux boutons en bas à droite : RÉINITIALISER et APPLIQUER
            Button resetBtn = MakeBtn("BtnResetWardrobe", shopPanel, "RÉINITIALISER", 22, GlassDark, Color.white,
                new Vector2(0f, 0f), new Vector2(270f, 74f), new Vector2(24f, 24f));
            AddBorder((RectTransform)resetBtn.transform, BorderSubtle);
            resetBtn.onClick.AddListener(OnResetWardrobeClicked);

            Button applyBtn = MakeBtn("BtnApplyWardrobe", shopPanel, "APPLIQUER", 24, Amber, Color.black,
                new Vector2(1f, 0f), new Vector2(522f, 74f), new Vector2(-24f, 24f));
            _applyBtnLabel = applyBtn.GetComponentInChildren<Text>();
            applyBtn.onClick.AddListener(OnApplyWardrobeClicked);
        }

        private static void BuildTurntableDragZone(RectTransform parent)
        {
            var zoneGo = new GameObject("CharacterTurntableTouchZone", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)zoneGo.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0.54f, 0.88f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = zoneGo.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.01f); // Transparent mais capte le glissement tactile
            img.raycastTarget = true;

            zoneGo.AddComponent<TurntableDragForwarder>();
        }

        #endregion

        #region Helpers UI

        private static RectTransform MakePanel(string name, RectTransform parent, Color bgColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;
            return rect;
        }

        private static RectTransform MakeBox(string name, Transform parent, Color color, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rect;
        }

        private static RectTransform MakeRounded(string name, Transform parent, Color color, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            RectTransform rect = MakeBox(name, parent, color, anchor, size, pos);
            var img = rect.GetComponent<Image>();
            img.sprite = HudIconLibrary.RoundedBox;
            img.type = Image.Type.Sliced;
            return rect;
        }

        private static RectTransform MakeImg(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            RectTransform rect = MakeBox(name, parent, color, anchor, size, pos);
            Image img = rect.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            return rect;
        }

        private static RectTransform MakePaddedIcon(string name, Transform parent, Sprite sprite, Color color, float padding)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(padding, padding);
            rect.anchorMax = new Vector2(1f - padding, 1f - padding);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return rect;
        }

        private static void AddBorder(RectTransform rect, Color borderColor)
        {
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(1.2f, 1.2f);
        }

        private Text MakeText(string name, Transform parent, string value, int size, TextAnchor align, Vector2 anchor, Vector2 box, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = box;
            rect.anchoredPosition = pos;
            var txt = go.GetComponent<Text>();
            txt.font = _font;
            txt.fontSize = size;
            txt.alignment = align;
            txt.color = Color.white;
            txt.text = value;
            txt.raycastTarget = false;
            return txt;
        }

        private Button MakeBtn(string name, Transform parent, string label, int fontSize, Color bgColor, Color textColor, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            RectTransform rect = MakeRounded(name, parent, bgColor, anchor, size, pos);
            Image img = rect.GetComponent<Image>();
            img.raycastTarget = true;
            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            Text txt = MakeText("Label", rect, label, fontSize, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), size, Vector2.zero);
            txt.color = textColor;
            txt.fontStyle = FontStyle.Bold;
            return btn;
        }

        #endregion
    }

    /// <summary>Relais tactile redirigeant le glissement d'une zone UI vers <see cref="TurntableCameraController"/>.</summary>
    internal sealed class TurntableDragForwarder : MonoBehaviour, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IPointerDownHandler
    {
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
        {
            TurntableCameraController.Instance?.OnPointerDown(eventData);
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData)
        {
            TurntableCameraController.Instance?.OnDrag(eventData);
        }
    }
}
