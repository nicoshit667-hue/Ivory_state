using System;
using System.Collections.Generic;
using System.Text;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    public sealed partial class PhoneHud
    {
        private static readonly Color ProfileAccent = new Color(0.14f, 0.42f, 0.72f, 1f);
        private static readonly Color ProfileBg = new Color(0.945f, 0.953f, 0.969f, 1f);
        private static readonly Color ProfileHeaderBg = Color.white;
        private static readonly Color ProfileHeaderBorder = new Color(0.86f, 0.89f, 0.94f, 1f);
        private static readonly Color ProfileCardBg = Color.white;
        private static readonly Color ProfileCardShadow = new Color(0.06f, 0.09f, 0.16f, 0.08f);
        private static readonly Color ProfileDarkText = new Color(0.07f, 0.10f, 0.18f, 1f);
        private static readonly Color ProfileBodyText = new Color(0.20f, 0.25f, 0.35f, 1f);
        private static readonly Color ProfileMutedText = new Color(0.45f, 0.51f, 0.62f, 1f);

        private int _requestedProfileId = -1;
        private int _viewedProfileId = -1;
        private string _viewedProfileName = string.Empty;
        private Page _profileReturnPage = Page.Home;
        private PlayerProfileSnapshot _lastProfileSnapshot;

        private Text _profileHeaderTitle;
        private PhoneScrollView _profileScroll;
        private Text _profileHintText;

        // Carte d'identité dynamique (haut de l'écran)
        private RectTransform _profileHeroShadow;
        private RectTransform _profileHeroCard;
        private Image _profileCardBgImage;
        private Image _profileStatusBg;
        private Image _profileStatusIcon;
        private Text _profileStatusLabel;
        private Image _profileViewsIcon;
        private Text _profileViewsText;

        private Image _profilePortraitBorder;
        private Image _profileAvatarBg;
        private Image _profilePortraitImage;
        private Text _profileAvatarInitial;
        private Text _profileNameText;
        private Text _profileRankText;
        private Text _profileBadgeText;
        private Image _profileLevelPillBg;
        private Text _profileLevelPill;
        private RectTransform _profileXpFill;
        private Text _profileXpLabel;

        // Actions sociales (uniquement sur le profil d'un autre joueur)
        private RectTransform _profileActionsRow;
        private Button _profileCallBtn;
        private Button _profilePayBtn;
        private Button _profileFriendBtn;

        private sealed class ProfileSectionCard
        {
            public ProfilePrivacyFlags PrivacyFlag;
            public RectTransform Shadow;
            public RectTransform Card;
            public Image IconChipBg;
            public Image Icon;
            public Text Title;
            public Button EyeButton;
            public Image EyeIcon;
            public Text EyeLabel;
            public RectTransform LockRow;
            public Image LockIcon;
            public Text LockText;
            public Text Body;
            public Button FooterAction;
            public Text FooterActionLabel;
        }

        private ProfileSectionCard _secResidence;
        private ProfileSectionCard _secProperties;
        private ProfileSectionCard _secVehicles;
        private ProfileSectionCard _secCommerces;

        private void BuildProfile(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Profile, screen);
            page.GetComponent<Image>().color = ProfileBg;

            // En-tête clair sobre (sans onglets ni barre de recherche)
            RectTransform header = MakeImage("EnTete", page, ProfileHeaderBg,
                new Vector2(0.5f, 1f), new Vector2(ScreenLogicalWidth, 76f), Vector2.zero);
            MakeImage("Separateur", header, ProfileHeaderBorder,
                new Vector2(0.5f, 0f), new Vector2(ScreenLogicalWidth, 2f), Vector2.zero);

            _profileHeaderTitle = MakeText("Titre", header, "Profil", 28, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(300f, 70f), Vector2.zero);
            _profileHeaderTitle.color = ProfileDarkText;
            _profileHeaderTitle.fontStyle = FontStyle.Bold;

            Button back = MakeButton("Retour", header, "‹", 48, new Color(0f, 0f, 0f, 0f),
                new Vector2(0f, 0.5f), new Vector2(80f, 76f), Vector2.zero);
            back.GetComponentInChildren<Text>().color = ProfileDarkText;
            back.onClick.AddListener(Back);

            Button close = MakeButton("Fermer", header, "×", 40, new Color(0f, 0f, 0f, 0f),
                new Vector2(1f, 0.5f), new Vector2(80f, 76f), Vector2.zero);
            close.GetComponentInChildren<Text>().color = ProfileDarkText;
            close.onClick.AddListener(() => _phone.gameObject.SetActive(false));

            _profileScroll = PhoneScrollView.Create(page, "ProfileScroll", 80f, 6f);
            RegisterPageScroll(Page.Profile, _profileScroll);
            RectTransform content = _profileScroll.Content;

            _profileHintText = MakeText("Hint", content, string.Empty, 16, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(430f, 30f), new Vector2(0f, -8f));
            _profileHintText.color = ProfileMutedText;

            BuildProfileHero(content);
            BuildProfileActionRow(content);

            _secResidence = CreateFloatingSection(content, ProfilePrivacyFlags.Residence, "Lieu de résidence",
                "icon_residence_pin", new Color(0.92f, 0.44f, 0.14f, 1f));
            _secProperties = CreateFloatingSection(content, ProfilePrivacyFlags.Properties, "Maisons & Biens",
                "icon_maison", new Color(0.08f, 0.56f, 0.62f, 1f));
            _secVehicles = CreateFloatingSection(content, ProfilePrivacyFlags.Vehicles, "Voitures",
                "icon_voiture", new Color(0.16f, 0.48f, 0.84f, 1f));
            _secCommerces = CreateFloatingSection(content, ProfilePrivacyFlags.Commerces, "Commerces",
                "icon_commerce", new Color(0.62f, 0.28f, 0.72f, 1f), withFooterAction: true);
        }

        private void BuildProfileHero(RectTransform parent)
        {
            _profileHeroShadow = MakeImage("HeroShadow", parent, new Color(0.05f, 0.08f, 0.16f, 0.16f),
                new Vector2(0.5f, 1f), new Vector2(442f, 218f), Vector2.zero);

            _profileHeroCard = MakeImage("HeroCard", parent, new Color(0.10f, 0.16f, 0.28f, 1f),
                new Vector2(0.5f, 1f), new Vector2(440f, 216f), new Vector2(0f, -12f));
            _profileHeroCard.gameObject.AddComponent<RectMask2D>();

            // Fond dynamique selon le métier/statut
            RectTransform bgRect = MakeImage("DynamicBg", _profileHeroCard, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(448f, 224f), Vector2.zero);
            _profileCardBgImage = bgRect.GetComponent<Image>();
            _profileCardBgImage.sprite = null;
            _profileCardBgImage.type = Image.Type.Simple;
            _profileCardBgImage.preserveAspect = false;

            // Voile sombre léger pour garantir la lisibilité sur toutes les textures JPG
            MakeImage("Scrim", _profileHeroCard, new Color(0.03f, 0.05f, 0.09f, 0.42f),
                new Vector2(0.5f, 0.5f), new Vector2(444f, 220f), Vector2.zero);

            // Barre supérieure : badge statut + icône métier à gauche, compteur de vues à droite
            RectTransform statusBg = MakeImage("StatutPill", _profileHeroCard, new Color(0.14f, 0.34f, 0.56f, 0.92f),
                new Vector2(0f, 1f), new Vector2(184f, 30f), new Vector2(14f, -12f));
            _profileStatusBg = statusBg.GetComponent<Image>();

            RectTransform stIconRect = MakeImage("IconeStatut", statusBg, Color.white,
                new Vector2(0f, 0.5f), new Vector2(20f, 20f), new Vector2(8f, 0f));
            _profileStatusIcon = stIconRect.GetComponent<Image>();
            _profileStatusIcon.type = Image.Type.Simple;
            _profileStatusIcon.preserveAspect = true;

            _profileStatusLabel = MakeText("LibelleStatut", statusBg, "CIVIL", 13, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(144f, 26f), new Vector2(34f, 0f));
            _profileStatusLabel.fontStyle = FontStyle.Bold;

            RectTransform viewsBg = MakeImage("VuesPill", _profileHeroCard, new Color(0f, 0f, 0f, 0.42f),
                new Vector2(1f, 1f), new Vector2(104f, 28f), new Vector2(-14f, -13f));
            RectTransform viewsIconRect = MakeImage("IconeVues", viewsBg, Color.white,
                new Vector2(0f, 0.5f), new Vector2(16f, 16f), new Vector2(8f, 0f));
            _profileViewsIcon = viewsIconRect.GetComponent<Image>();
            _profileViewsIcon.sprite = HudIconLibrary.GetProfileIcon("icon_eye_open");
            _profileViewsIcon.type = Image.Type.Simple;
            _profileViewsIcon.preserveAspect = true;

            _profileViewsText = MakeText("VuesTexte", viewsBg, "0 vue", 13, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(72f, 24f), new Vector2(28f, 0f));
            _profileViewsText.fontStyle = FontStyle.Bold;

            // Portrait / visage du personnage
            RectTransform portraitFrame = MakeImage("PortraitBorder", _profileHeroCard, Color.white,
                new Vector2(0f, 1f), new Vector2(88f, 88f), new Vector2(14f, -54f));
            _profilePortraitBorder = portraitFrame.GetComponent<Image>();

            RectTransform portraitBg = MakeImage("PortraitBg", portraitFrame, ProfileAccent,
                new Vector2(0.5f, 0.5f), new Vector2(82f, 82f), Vector2.zero);
            portraitBg.gameObject.AddComponent<RectMask2D>();
            _profileAvatarBg = portraitBg.GetComponent<Image>();

            _profileAvatarInitial = MakeText("Init", portraitBg, "?", 34, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(82f, 82f), Vector2.zero);
            _profileAvatarInitial.fontStyle = FontStyle.Bold;

            RectTransform portraitImgRect = MakeImage("Visage", portraitBg, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(82f, 82f), Vector2.zero);
            _profilePortraitImage = portraitImgRect.GetComponent<Image>();
            _profilePortraitImage.type = Image.Type.Simple;
            _profilePortraitImage.preserveAspect = true;
            _profilePortraitImage.gameObject.SetActive(false);

            // Pseudo, rang, badge niveau, statut en ligne
            _profileNameText = MakeText("Nom", _profileHeroCard, "Joueur", 23, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(224f, 30f), new Vector2(114f, -54f));
            _profileNameText.fontStyle = FontStyle.Bold;

            _profileRankText = MakeText("Rang", _profileHeroCard, "Nouveau venu", 15, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(224f, 22f), new Vector2(114f, -84f));
            _profileRankText.color = new Color(0.88f, 0.93f, 1f, 0.95f);

            _profileBadgeText = MakeText("Statut", _profileHeroCard, "● En ligne", 14, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(310f, 22f), new Vector2(114f, -108f));
            _profileBadgeText.supportRichText = true;

            RectTransform lvlBg = MakeImage("NivBadge", _profileHeroCard, new Color(0.96f, 0.72f, 0.16f, 1f),
                new Vector2(1f, 1f), new Vector2(82f, 32f), new Vector2(-14f, -56f));
            _profileLevelPillBg = lvlBg.GetComponent<Image>();
            _profileLevelPill = MakeText("NivText", lvlBg, "NIV. 1", 15, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(80f, 30f), Vector2.zero);
            _profileLevelPill.fontStyle = FontStyle.Bold;
            _profileLevelPill.color = new Color(0.08f, 0.08f, 0.10f, 1f);

            // Barre d'XP + texte XP en bas de la carte
            RectTransform xpTrack = MakeImage("XpTrack", _profileHeroCard, new Color(0f, 0f, 0f, 0.42f),
                new Vector2(0.5f, 0f), new Vector2(412f, 12f), new Vector2(0f, 34f));
            _profileXpFill = MakeImage("XpFill", xpTrack, new Color(0.25f, 0.84f, 0.50f, 1f),
                Vector2.zero, Vector2.zero, Vector2.zero);
            _profileXpFill.anchorMin = Vector2.zero;
            _profileXpFill.anchorMax = new Vector2(0.1f, 1f);
            _profileXpFill.offsetMin = _profileXpFill.offsetMax = Vector2.zero;

            _profileXpLabel = MakeText("XpLabel", _profileHeroCard, "0 / 100 XP", 13, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0f), new Vector2(412f, 22f), new Vector2(0f, 8f));
            _profileXpLabel.color = new Color(1f, 1f, 1f, 0.90f);
        }

        private void BuildProfileActionRow(RectTransform parent)
        {
            _profileActionsRow = MakeImage("Actions", parent, new Color(0f, 0f, 0f, 0f),
                new Vector2(0.5f, 1f), new Vector2(440f, 44f), Vector2.zero);

            _profileCallBtn = MakeButton("Appeler", _profileActionsRow, "Appeler", 15, Green,
                new Vector2(0f, 0.5f), new Vector2(140f, 40f), Vector2.zero);
            _profileCallBtn.onClick.AddListener(CallViewedProfile);

            _profilePayBtn = MakeButton("Envoyer", _profileActionsRow, "Pingoin", 15, PingoinBlue,
                new Vector2(0.5f, 0.5f), new Vector2(140f, 40f), Vector2.zero);
            _profilePayBtn.onClick.AddListener(PayViewedProfile);

            _profileFriendBtn = MakeButton("Ami", _profileActionsRow, "+ Ami", 15, ProfileAccent,
                new Vector2(1f, 0.5f), new Vector2(140f, 40f), Vector2.zero);
            _profileFriendBtn.onClick.AddListener(AddFriendViewedProfile);

            _profileActionsRow.gameObject.SetActive(false);
        }

        private ProfileSectionCard CreateFloatingSection(RectTransform parent, ProfilePrivacyFlags flag, string title,
            string iconKey, Color chipColor, bool withFooterAction = false)
        {
            RectTransform shadow = MakeImage("Shadow_" + title, parent, ProfileCardShadow,
                new Vector2(0.5f, 1f), new Vector2(442f, 100f), Vector2.zero);

            RectTransform card = MakeImage("Card_" + title, parent, ProfileCardBg,
                new Vector2(0.5f, 1f), new Vector2(438f, 96f), Vector2.zero);

            RectTransform chip = MakeImage("Chip", card, new Color(chipColor.r, chipColor.g, chipColor.b, 0.14f),
                new Vector2(0f, 1f), new Vector2(34f, 34f), new Vector2(14f, -12f));
            RectTransform iconRect = MakeImage("Icon", chip, chipColor,
                new Vector2(0.5f, 0.5f), new Vector2(22f, 22f), Vector2.zero);
            Image iconImg = iconRect.GetComponent<Image>();
            iconImg.sprite = HudIconLibrary.GetProfileIcon(iconKey);
            iconImg.type = Image.Type.Simple;
            iconImg.preserveAspect = true;

            Text header = MakeText("Titre", card, title, 17, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(246f, 34f), new Vector2(56f, -12f));
            header.color = ProfileDarkText;
            header.fontStyle = FontStyle.Bold;

            // Bouton œil (visible uniquement sur Mon profil)
            Button eyeBtn = MakeButton("PrivacyBtn", card, string.Empty, 12, new Color(0.93f, 0.95f, 0.98f, 1f),
                new Vector2(1f, 1f), new Vector2(106f, 30f), new Vector2(-12f, -14f));
            RectTransform eyeIconRect = MakeImage("EyeIcon", eyeBtn.transform, ProfileMutedText,
                new Vector2(0f, 0.5f), new Vector2(18f, 18f), new Vector2(8f, 0f));
            Image eyeIcon = eyeIconRect.GetComponent<Image>();
            eyeIcon.sprite = HudIconLibrary.GetProfileIcon("icon_eye_open");
            eyeIcon.type = Image.Type.Simple;
            eyeIcon.preserveAspect = true;
            Text eyeLabel = MakeText("EyeText", eyeBtn.transform, "Visible", 12, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(70f, 26f), new Vector2(30f, 0f));
            eyeLabel.color = ProfileMutedText;
            eyeLabel.fontStyle = FontStyle.Bold;
            eyeBtn.onClick.AddListener(() => ToggleProfileSectionPrivacy(flag));

            // État verrouillé (uniquement chez un autre joueur lorsque la section est masquée)
            RectTransform lockRow = MakeImage("Locked", card, new Color(0.95f, 0.96f, 0.98f, 1f),
                new Vector2(0.5f, 1f), new Vector2(410f, 44f), new Vector2(0f, -54f));
            RectTransform lockIconRect = MakeImage("LockIcon", lockRow, ProfileMutedText,
                new Vector2(0f, 0.5f), new Vector2(22f, 22f), new Vector2(12f, 0f));
            Image lockIcon = lockIconRect.GetComponent<Image>();
            lockIcon.sprite = HudIconLibrary.GetProfileIcon("icon_lock");
            lockIcon.type = Image.Type.Simple;
            lockIcon.preserveAspect = true;
            Text lockText = MakeText("LockMsg", lockRow, "Information masquée par le joueur", 15, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(354f, 32f), new Vector2(42f, 0f));
            lockText.color = ProfileMutedText;
            lockRow.gameObject.SetActive(false);

            Text body = MakeText("Contenu", card, "…", 15, TextAnchor.UpperLeft,
                new Vector2(0.5f, 1f), new Vector2(410f, 46f), new Vector2(0f, -54f));
            body.color = ProfileBodyText;
            body.supportRichText = true;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;

            Button footerBtn = null;
            Text footerLbl = null;
            if (withFooterAction)
            {
                footerBtn = MakeButton("GererCommerces", card, "Gérer mes commerces ›", 13,
                    new Color(0.94f, 0.96f, 0.99f, 1f), new Vector2(1f, 0f), new Vector2(190f, 28f), new Vector2(-14f, 10f));
                footerLbl = footerBtn.GetComponentInChildren<Text>();
                footerLbl.color = ProfileAccent;
                footerLbl.fontStyle = FontStyle.Bold;
                footerBtn.onClick.AddListener(() => ShowPage(Page.Commerces));
                footerBtn.gameObject.SetActive(false);
            }

            return new ProfileSectionCard
            {
                PrivacyFlag = flag,
                Shadow = shadow,
                Card = card,
                IconChipBg = chip.GetComponent<Image>(),
                Icon = iconImg,
                Title = header,
                EyeButton = eyeBtn,
                EyeIcon = eyeIcon,
                EyeLabel = eyeLabel,
                LockRow = lockRow,
                LockIcon = lockIcon,
                LockText = lockText,
                Body = body,
                FooterAction = footerBtn,
                FooterActionLabel = footerLbl,
            };
        }

        public void OpenPlayerProfile(int targetObjectId, Page returnPage = Page.Home)
        {
            if (PlayerPhone.Local == null)
                return;

            _profileReturnPage = returnPage != Page.Profile ? returnPage : Page.Home;
            _requestedProfileId = targetObjectId;

            if (!_phone.gameObject.activeSelf)
            {
                _phone.SetAsLastSibling();
                _phone.gameObject.SetActive(true);
                PhoneOpened?.Invoke();
            }

            if (_page != Page.Profile)
                ShowPage(Page.Profile);
            else
                RequestProfileSnapshot(targetObjectId);
        }

        private void RequestProfileSnapshot(int targetObjectId)
        {
            _requestedProfileId = targetObjectId;
            if (_profileHeaderTitle != null)
                _profileHeaderTitle.text = targetObjectId <= 0 ? "Mon profil" : "Profil";
            if (_profileHintText != null)
            {
                _profileHintText.gameObject.SetActive(true);
                _profileHintText.text = "Chargement du profil…";
            }
            LayoutProfileCards();
            PlayerPhone.Local?.RequestProfile(targetObjectId);
        }

        private void OnProfileReceived(PlayerProfileSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _lastProfileSnapshot = snapshot;
            if (!snapshot.Found)
            {
                if (_profileHintText != null)
                {
                    _profileHintText.gameObject.SetActive(true);
                    _profileHintText.text = "Ce joueur n'est plus connecté.";
                }
                LayoutProfileCards();
                return;
            }

            _viewedProfileId = snapshot.TargetObjectId;
            _viewedProfileName = snapshot.DisplayName ?? "?";

            if (_profileHeaderTitle != null)
                _profileHeaderTitle.text = snapshot.IsSelf ? "Mon profil" : "Profil";
            if (_profileHintText != null)
                _profileHintText.gameObject.SetActive(false);

            // Visuel dynamique de la carte d'identité selon le statut/métier
            ProfileStatusVisual statusMeta = PlayerProfileMetaCatalog.Get(snapshot.JobStatus);
            Sprite dynamicBg = HudIconLibrary.GetProfileBackground(snapshot.JobStatus);
            if (_profileCardBgImage != null)
            {
                _profileCardBgImage.sprite = dynamicBg;
                _profileCardBgImage.color = dynamicBg != null ? Color.white : statusMeta.PillBgColor;
            }
            if (_profileStatusBg != null)
                _profileStatusBg.color = statusMeta.PillBgColor;
            if (_profileStatusIcon != null)
                _profileStatusIcon.sprite = HudIconLibrary.GetProfileIcon(statusMeta.IconKey);
            if (_profileStatusLabel != null)
                _profileStatusLabel.text = statusMeta.Label.ToUpperInvariant();
            if (_profilePortraitBorder != null)
                _profilePortraitBorder.color = statusMeta.AccentColor;
            if (_profileLevelPillBg != null)
                _profileLevelPillBg.color = statusMeta.AccentColor;

            // Compteur de vues du profil
            if (_profileViewsText != null)
                _profileViewsText.text = snapshot.ProfileViews + (snapshot.ProfileViews > 1 ? " vues" : " vue");

            // Portrait 3D du personnage ou fallback initial
            string safeName = Escape(_viewedProfileName);
            _profileNameText.text = safeName;
            _profileAvatarBg.color = AvatarColorFor(_viewedProfileName);
            _profileAvatarInitial.text = string.IsNullOrEmpty(_viewedProfileName) ? "?" : _viewedProfileName.Substring(0, 1).ToUpperInvariant();

            Sprite portrait = PlayerPortraitRenderer.GetPortrait(snapshot.IsSelf ? -1 : snapshot.TargetObjectId, _viewedProfileName, snapshot.CurrentOutfit);
            bool hasPortrait = portrait != null;
            _profilePortraitImage.gameObject.SetActive(hasPortrait);
            _profileAvatarInitial.gameObject.SetActive(!hasPortrait);
            if (hasPortrait)
                _profilePortraitImage.sprite = portrait;

            _profileLevelPill.text = "NIV. " + snapshot.Level;
            _profileRankText.text = snapshot.RankTitle ?? PlayerProgression.GetRankTitle(snapshot.Level);

            PlayerProgression.GetLevelInfo(snapshot.Xp, out _, out int curXp, out int nextXp);
            float ratio = nextXp > 0 ? Mathf.Clamp01(curXp / (float)nextXp) : 1f;
            _profileXpFill.anchorMax = new Vector2(Mathf.Max(0.03f, ratio), 1f);
            _profileXpFill.GetComponent<Image>().color = statusMeta.AccentColor;
            _profileXpLabel.text = snapshot.Level >= PlayerProgression.MaxLevel
                ? $"{snapshot.Xp} XP (Niveau max)"
                : $"{curXp} / {nextXp} XP  •  Total : {snapshot.Xp} XP";

            float distMeters = 0f;
            bool nearby = !snapshot.IsSelf && IsPlayerNearby(snapshot.TargetObjectId, out distMeters);
            if (snapshot.IsSelf)
                _profileBadgeText.text = "<color=#B8FFD0>● Mon compte</color>  •  " + statusMeta.RoleSummary;
            else if (nearby)
                _profileBadgeText.text = $"<color=#A8FFB8>● À proximité ({Mathf.RoundToInt(distMeters)} m)</color>";
            else
                _profileBadgeText.text = "<color=#BFE0FF>● En ligne</color>";

            bool showActions = !snapshot.IsSelf;
            _profileActionsRow.gameObject.SetActive(showActions);
            if (showActions)
                _profileFriendBtn.gameObject.SetActive(nearby);

            // Mise à jour des 4 cartes flottantes avec gestion du masquage
            ApplySectionContent(_secResidence, snapshot, FormatProfileResidence(snapshot));
            ApplySectionContent(_secProperties, snapshot, FormatProfileProperties(snapshot));
            ApplySectionContent(_secVehicles, snapshot, FormatProfileVehicles(snapshot));
            ApplySectionContent(_secCommerces, snapshot, FormatProfileCommerces(snapshot));

            if (_secCommerces.FooterAction != null)
                _secCommerces.FooterAction.gameObject.SetActive(snapshot.IsSelf);

            LayoutProfileCards();
        }

        private void ApplySectionContent(ProfileSectionCard section, PlayerProfileSnapshot snapshot, string bodyText)
        {
            bool isMasked = (snapshot.PrivacyMask & (int)section.PrivacyFlag) != 0;

            section.EyeButton.gameObject.SetActive(snapshot.IsSelf);
            if (snapshot.IsSelf)
            {
                section.EyeIcon.sprite = HudIconLibrary.GetProfileIcon(isMasked ? "icon_eye_closed" : "icon_eye_open");
                Color tint = isMasked ? new Color(0.76f, 0.36f, 0.08f, 1f) : new Color(0.08f, 0.50f, 0.44f, 1f);
                section.EyeIcon.color = tint;
                section.EyeLabel.color = tint;
                section.EyeLabel.text = isMasked ? "Masqué" : "Visible";
                section.EyeButton.GetComponent<Image>().color = isMasked
                    ? new Color(0.99f, 0.94f, 0.88f, 1f)
                    : new Color(0.91f, 0.97f, 0.96f, 1f);
            }

            bool showLocked = !snapshot.IsSelf && isMasked;
            section.LockRow.gameObject.SetActive(showLocked);
            section.Body.gameObject.SetActive(!showLocked);
            if (!showLocked)
                section.Body.text = bodyText;
        }

        private void ToggleProfileSectionPrivacy(ProfilePrivacyFlags flag)
        {
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local == null)
                return;

            int current = _lastProfileSnapshot != null && _lastProfileSnapshot.IsSelf
                ? _lastProfileSnapshot.PrivacyMask
                : local.ProfilePrivacy;
            int updated = current ^ (int)flag;

            if (_lastProfileSnapshot != null && _lastProfileSnapshot.IsSelf)
            {
                _lastProfileSnapshot.PrivacyMask = updated;
                OnProfileReceived(_lastProfileSnapshot);
            }
            local.RequestSetProfilePrivacy(updated);
        }

        private string FormatProfileResidence(PlayerProfileSnapshot s)
        {
            var sb = new StringBuilder();
            if (s.IsHomeless)
            {
                sb.Append("<color=#B91C1C>Sans domicile fixe (SDF)</color>");
                return sb.ToString();
            }

            if (!string.IsNullOrEmpty(s.ResidenceDistrict))
                sb.Append("Commune / Résidence : <b>").Append(Escape(s.ResidenceDistrict)).Append("</b>");

            if (StudioLayout.IsValid(s.StudioId))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("Studio n° ").Append(s.StudioId).Append(" — Résidence Les Flamboyants");
            }

            if (!string.IsNullOrEmpty(s.Roommate))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("Colocataire : <b>").Append(Escape(s.Roommate)).Append("</b>");
            }

            if (sb.Length == 0)
                sb.Append("Aucun logement principal attribué");
            return sb.ToString();
        }

        private string FormatProfileProperties(PlayerProfileSnapshot s)
        {
            var sb = new StringBuilder();
            AppendPropertyLine(sb, s.Prop1Id, s.Prop1Owner);
            AppendPropertyLine(sb, s.Prop2Id, s.Prop2Owner);
            if (sb.Length == 0)
                sb.Append("Aucune maison ou appartement enregistré.");
            return sb.ToString();
        }

        private static void AppendPropertyLine(StringBuilder sb, int propId, bool isOwner)
        {
            if (!PropertyCatalog.IsValid(propId))
                return;
            PropertyCatalog.Property p = PropertyCatalog.Get(propId);
            string entrance = PropertyCatalog.Entrances[p.EntranceIndex].Name;
            bool forSale = PropertyCatalog.IsForSale(p.Kind);
            string role = !isOwner ? "Invité" : (forSale ? "Propriétaire" : "Locataire");
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("• <b>").Append(p.Label).Append("</b> (").Append(entrance).Append(") — <color=#0F766E>").Append(role).Append("</color>");
        }

        private static string FormatProfileVehicles(PlayerProfileSnapshot s)
        {
            var sb = new StringBuilder();
            sb.Append(s.HasLicense
                ? "<color=#15803D>Permis de conduire : Obtenu</color>"
                : "<color=#B45309>Permis de conduire : Non obtenu</color>");

            int carCount = 0;
            if (!string.IsNullOrEmpty(s.CarsCsv))
            {
                string[] parts = s.CarsCsv.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (int.TryParse(parts[i], out int paint) && paint > 0 && paint < VehicleMotor.PaintNames.Length)
                    {
                        carCount++;
                        sb.Append("\n• <b>").Append(VehicleMotor.PaintNames[paint]).Append("</b>");
                        if (paint < PlayerGarage.Rules.CarPrices.Length && PlayerGarage.Rules.CarPrices[paint] > 0)
                            sb.Append(" <color=#64748B>(").Append(Money.Format(PlayerGarage.Rules.CarPrices[paint])).Append(")</color>");
                    }
                }
            }
            if (s.HasRental && s.RentalPaint > 0 && s.RentalPaint < VehicleMotor.PaintNames.Length)
                sb.Append("\n• Location en cours : ").Append(VehicleMotor.PaintNames[s.RentalPaint]);
            else if (carCount == 0)
                sb.Append("\nAucun véhicule personnel");

            return sb.ToString();
        }

        private static string FormatProfileCommerces(PlayerProfileSnapshot s)
        {
            if (s.CommercesCount <= 0 || string.IsNullOrEmpty(s.CommercesSummary))
                return "Aucun commerce détenu ou géré pour l'instant.";

            string[] items = s.CommercesSummary.Split('|');
            var sb = new StringBuilder();
            for (int i = 0; i < items.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(items[i]))
                    continue;
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append("• ").Append(Escape(items[i]));
            }
            return sb.Length > 0 ? sb.ToString() : "Aucun commerce détenu ou géré pour l'instant.";
        }

        private void LayoutProfileCards()
        {
            float y = -12f;
            if (_profileHintText != null && _profileHintText.gameObject.activeSelf)
            {
                _profileHintText.rectTransform.anchoredPosition = new Vector2(0f, y);
                y -= 34f;
            }
            if (_profileHeroCard != null)
            {
                _profileHeroCard.anchoredPosition = new Vector2(0f, y);
                if (_profileHeroShadow != null)
                    _profileHeroShadow.anchoredPosition = new Vector2(0f, y - 4f);
                y -= _profileHeroCard.sizeDelta.y + 14f;
            }
            if (_profileActionsRow != null && _profileActionsRow.gameObject.activeSelf)
            {
                _profileActionsRow.anchoredPosition = new Vector2(0f, y);
                y -= _profileActionsRow.sizeDelta.y + 12f;
            }

            y = PositionFloatingSection(_secResidence, y);
            y = PositionFloatingSection(_secProperties, y);
            y = PositionFloatingSection(_secVehicles, y);
            y = PositionFloatingSection(_secCommerces, y);

            _profileScroll?.SetContentHeight(-y + 16f);
        }

        private static float PositionFloatingSection(ProfileSectionCard section, float y)
        {
            if (section == null || section.Card == null)
                return y;

            float contentH;
            if (section.LockRow.gameObject.activeSelf)
            {
                contentH = 44f;
            }
            else
            {
                Canvas.ForceUpdateCanvases();
                contentH = Mathf.Max(26f, section.Body.preferredHeight + 6f);
                section.Body.rectTransform.sizeDelta = new Vector2(410f, contentH);
            }

            bool hasFooter = section.FooterAction != null && section.FooterAction.gameObject.activeSelf;
            float cardH = 54f + contentH + (hasFooter ? 46f : 14f);
            section.Card.sizeDelta = new Vector2(438f, cardH);
            section.Card.anchoredPosition = new Vector2(0f, y);
            if (section.Shadow != null)
            {
                section.Shadow.sizeDelta = new Vector2(442f, cardH + 2f);
                section.Shadow.anchoredPosition = new Vector2(0f, y - 3f);
            }
            return y - cardH - 12f;
        }

        private bool IsPlayerNearby(int objectId, out float distanceMeters)
        {
            distanceMeters = 0f;
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local == null || objectId <= 0)
                return false;

            var visible = PlayerNetworkIdentity.ClientInstances;
            for (int i = 0; i < visible.Count; i++)
            {
                PlayerNetworkIdentity p = visible[i];
                if (p != null && p.ObjectId == objectId)
                {
                    distanceMeters = Vector3.Distance(local.transform.position, p.transform.position);
                    return true;
                }
            }
            return false;
        }

        private static Color AvatarColorFor(string name)
        {
            uint h = string.IsNullOrEmpty(name) ? 0u : unchecked((uint)name.GetHashCode());
            float hue = (h % 360u) / 360f;
            return Color.HSVToRGB(hue, 0.52f, 0.65f);
        }

        private void CallViewedProfile()
        {
            if (_viewedProfileId <= 0)
                return;
            PlayerPhone.Local?.Call(_viewedProfileId);
            _phone.gameObject.SetActive(false);
        }

        private void PayViewedProfile()
        {
            if (_viewedProfileId <= 0)
                return;
            _recipientId = _viewedProfileId;
            _recipientName = _viewedProfileName;
            ShowPage(Page.PingoinAmount);
        }

        private void AddFriendViewedProfile()
        {
            if (_viewedProfileId <= 0)
                return;
            PlayerFriends.Local?.SendRequest(_viewedProfileId);
            if (_profileHintText != null)
            {
                _profileHintText.gameObject.SetActive(true);
                _profileHintText.text = "Demande d'ami envoyée à " + _viewedProfileName + ".";
                LayoutProfileCards();
            }
        }

        private void OnLocalXpChanged(PlayerNetworkIdentity identity)
        {
            if (_phone != null && _phone.gameObject.activeSelf && _page == Page.Profile && _requestedProfileId <= 0)
                PlayerPhone.Local?.RequestProfile(-1);
        }

        private void OnLocalProfileMetaChanged(PlayerNetworkIdentity identity)
        {
            if (_phone == null || !_phone.gameObject.activeSelf)
                return;
            if (_page == Page.Profile && _requestedProfileId <= 0)
                PlayerPhone.Local?.RequestProfile(-1);
            else if (_page == Page.Jobs)
                RefreshJobStatusCards();
        }

        private void OnProfileInspectRequested(int targetObjectId)
        {
            OpenPlayerProfile(targetObjectId, Page.Home);
        }
    }
}
