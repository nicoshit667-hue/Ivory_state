using System;
using System.Collections.Generic;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    public sealed partial class PhoneHud
    {
        private static readonly Color ContactsAccent = new Color(0.12f, 0.45f, 0.25f, 1f);
        private static readonly Color ContactsTabIdle = new Color(1f, 1f, 1f, 0.12f);

        private int _contactsTab; // 0 = Amis, 1 = Joueurs en ligne (annuaire)
        private Button _contactsFriendsTabBtn;
        private Button _contactsPlayersTabBtn;
        private InputField _contactsSearchInput;
        private Text _contactsHint;
        private PhoneScrollView _contactsScroll;
        private Button _contactsAddFriendBtn;

        private readonly Button[] _contactButtons = new Button[ListSlots];
        private readonly Button[] _contactProfileButtons = new Button[ListSlots];
        private readonly Text[] _contactLabels = new Text[ListSlots];
        private readonly int[] _contactIds = new int[ListSlots];

        private int[] _dirProfileIds = Array.Empty<int>();
        private string[] _dirProfileNames = Array.Empty<string>();
        private int[] _dirProfileLevels = Array.Empty<int>();
        private bool[] _dirProfileHomeless = Array.Empty<bool>();

        private sealed class DirectoryRowUi
        {
            public Button Button;
            public Image AvatarBg;
            public Text AvatarInitial;
            public Text NameText;
            public Text LevelText;
            public int ObjectId;
        }
        private readonly List<DirectoryRowUi> _directoryRows = new List<DirectoryRowUi>();

        private void BuildContacts(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Contacts, screen);
            MakeHeader(page, "Contacts", ContactsAccent);

            // Barre de recherche (recherche de joueurs / amis déplacée ici depuis Profil).
            RectTransform searchBox = MakeImage("RechercheContacts", page, new Color(1f, 1f, 1f, 0.12f),
                new Vector2(0.5f, 1f), new Vector2(436f, 44f), new Vector2(0f, -84f));
            searchBox.GetComponent<Image>().raycastTarget = true;
            Text searchText = MakeText("Texte", searchBox, string.Empty, 18, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(408f, 38f), Vector2.zero);
            Text searchPh = MakeText("Indice", searchBox, "Rechercher un ami ou joueur…", 17, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(408f, 38f), Vector2.zero);
            searchPh.color = new Color(1f, 1f, 1f, 0.45f);
            _contactsSearchInput = searchBox.gameObject.AddComponent<InputField>();
            _contactsSearchInput.targetGraphic = searchBox.GetComponent<Image>();
            _contactsSearchInput.textComponent = searchText;
            _contactsSearchInput.placeholder = searchPh;
            _contactsSearchInput.characterLimit = 24;
            _contactsSearchInput.onValueChanged.AddListener(_ => RefreshContactsView());

            // Onglets Amis | Joueurs en ligne
            _contactsFriendsTabBtn = MakeButton("TabAmis", page, "Mes amis", 17, ContactsAccent,
                new Vector2(0.5f, 1f), new Vector2(214f, 38f), new Vector2(-111f, -134f));
            _contactsFriendsTabBtn.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            _contactsFriendsTabBtn.onClick.AddListener(() => SelectContactsTab(0));

            _contactsPlayersTabBtn = MakeButton("TabJoueurs", page, "Joueurs en ligne", 17, ContactsTabIdle,
                new Vector2(0.5f, 1f), new Vector2(214f, 38f), new Vector2(111f, -134f));
            _contactsPlayersTabBtn.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            _contactsPlayersTabBtn.onClick.AddListener(() => SelectContactsTab(1));

            _contactsHint = MakeText("Info", page, string.Empty, 16, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(436f, 28f), new Vector2(0f, -178f));
            _contactsHint.color = new Color(1f, 1f, 1f, 0.78f);

            _contactsScroll = PhoneScrollView.Create(page, "ContactsScroll", 210f, 74f);
            RegisterPageScroll(Page.Contacts, _contactsScroll);

            for (int i = 0; i < ListSlots; i++)
            {
                int slot = i;
                Button button = MakeButton("Contact_" + i, _contactsScroll.Content, "…", 24, Row,
                    new Vector2(0.5f, 1f), new Vector2(430f, 70f), new Vector2(0f, -6f - i * 78f));
                Text label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.supportRichText = true;
                label.rectTransform.offsetMin = new Vector2(78f, 0f);
                label.rectTransform.offsetMax = new Vector2(-202f, 0f);
                RectTransform avatar = MakeImage("Avatar", button.transform, new Color(0.25f, 0.6f, 0.4f, 1f),
                    new Vector2(0f, 0.5f), new Vector2(50f, 50f), new Vector2(14f, 0f));
                DrawPerson(avatar, new Color(1f, 1f, 1f, 0.9f));

                Button profileBtn = MakeButton("Profil", button.transform, "Profil", 17,
                    new Color(0.14f, 0.42f, 0.72f, 1f), new Vector2(1f, 0.5f), new Vector2(80f, 50f), new Vector2(-114f, 0f));
                profileBtn.onClick.AddListener(() =>
                {
                    if (_contactIds[slot] >= 0)
                        OpenPlayerProfile(_contactIds[slot], Page.Contacts);
                });

                Button message = MakeButton("Message", button.transform, "Message", 18,
                    new Color(0.15f, 0.40f, 0.75f, 1f), new Vector2(1f, 0.5f), new Vector2(98f, 50f), new Vector2(-8f, 0f));
                message.onClick.AddListener(() => MessageContact(slot));

                button.onClick.AddListener(() => CallContact(slot));
                _contactButtons[i] = button;
                _contactProfileButtons[i] = profileBtn;
                _contactLabels[i] = label;
                button.gameObject.SetActive(false);
            }

            _contactsAddFriendBtn = MakeButton("Ajouter", page, "+ Ajouter un ami à proximité", 21,
                new Color(0.18f, 0.70f, 0.35f, 1f), new Vector2(0.5f, 0f), new Vector2(430f, 58f), new Vector2(0f, 8f));
            _contactsAddFriendBtn.onClick.AddListener(() => { _nearbyForMoveIn = false; ShowPage(Page.AddFriend); });
        }

        private void SelectContactsTab(int tab)
        {
            _contactsTab = tab;
            if (_contactsFriendsTabBtn != null && _contactsFriendsTabBtn.targetGraphic is Image fImg)
                fImg.color = tab == 0 ? ContactsAccent : ContactsTabIdle;
            if (_contactsPlayersTabBtn != null && _contactsPlayersTabBtn.targetGraphic is Image pImg)
                pImg.color = tab == 1 ? ContactsAccent : ContactsTabIdle;

            if (tab == 1)
                PlayerPhone.Local?.RequestProfileDirectory();

            RefreshContactsView();
            _contactsScroll?.ResetToTopAnimated(false);
        }

        private void OnProfileDirectoryReceived(int[] ids, string[] names, int[] levels, bool[] homeless)
        {
            _dirProfileIds = ids ?? Array.Empty<int>();
            _dirProfileNames = names ?? Array.Empty<string>();
            _dirProfileLevels = levels ?? Array.Empty<int>();
            _dirProfileHomeless = homeless ?? Array.Empty<bool>();

            if (_phone != null && _phone.gameObject.activeSelf && _page == Page.Contacts && _contactsTab == 1)
                RefreshContactsView();
        }

        private void RefreshContactsView()
        {
            if (_contactsTab == 0)
                FillContacts();
            else
                FillContactsDirectory();
        }

        private void FillContacts()
        {
            for (int i = 0; i < _directoryRows.Count; i++)
                _directoryRows[i].Button.gameObject.SetActive(false);

            string query = _contactsSearchInput != null ? (_contactsSearchInput.text ?? string.Empty).Trim() : string.Empty;
            int shown = 0;
            float y = -6f;

            for (int i = 0; i < _friendKeys.Length && shown < ListSlots; i++)
            {
                string name = _friendNames[i] ?? string.Empty;
                if (query.Length > 0 && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool online = _friendOnline[i] >= 0;
                _contactIds[shown] = _friendOnline[i];
                _contactKeys[shown] = _friendKeys[i];
                _contactButtons[shown].gameObject.SetActive(true);
                ((RectTransform)_contactButtons[shown].transform).anchoredPosition = new Vector2(0f, y);
                _contactLabels[shown].text = Escape(name) + (online ? "\n<size=15><color=#6f6>● en ligne</color></size>" : "\n<size=15><color=#999>○ hors ligne</color></size>");
                if (_contactProfileButtons[shown] != null)
                    _contactProfileButtons[shown].gameObject.SetActive(online);
                y -= 78f;
                shown++;
            }

            for (int i = shown; i < ListSlots; i++)
                _contactButtons[i].gameObject.SetActive(false);

            _contactsScroll?.SetContentHeight(Mathf.Max(220f, shown * 78f + 16f));
            if (_contactsHint != null)
            {
                if (_friendKeys.Length == 0)
                    _contactsHint.text = "Pas encore d'amis. Consulte « Joueurs en ligne » ou approche-toi d'un joueur.";
                else if (shown == 0 && query.Length > 0)
                    _contactsHint.text = "Aucun ami trouvé pour « " + query + " ».";
                else
                    _contactsHint.text = "Touche un ami pour l'appeler ou voir son profil.";
            }
        }

        private void FillContactsDirectory()
        {
            for (int i = 0; i < ListSlots; i++)
                _contactButtons[i].gameObject.SetActive(false);

            if (_contactsScroll == null)
                return;

            string query = _contactsSearchInput != null ? (_contactsSearchInput.text ?? string.Empty).Trim() : string.Empty;
            var entries = new List<(int id, string name, int level, bool homeless, bool nearby, float dist)>();

            for (int i = 0; i < _dirProfileIds.Length; i++)
            {
                string name = _dirProfileNames[i] ?? string.Empty;
                if (query.Length > 0 && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                int level = i < _dirProfileLevels.Length ? _dirProfileLevels[i] : 1;
                bool homeless = i < _dirProfileHomeless.Length && _dirProfileHomeless[i];
                bool nearby = IsPlayerNearby(_dirProfileIds[i], out float dist);
                entries.Add((_dirProfileIds[i], name, level, homeless, nearby, dist));
            }

            entries.Sort((a, b) =>
            {
                if (a.nearby != b.nearby)
                    return a.nearby ? -1 : 1;
                if (a.nearby && b.nearby)
                    return a.dist.CompareTo(b.dist);
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });

            EnsureDirectoryRowPool(entries.Count);
            float y = -6f;
            for (int i = 0; i < _directoryRows.Count; i++)
            {
                bool active = i < entries.Count;
                _directoryRows[i].Button.gameObject.SetActive(active);
                if (!active)
                    continue;

                var e = entries[i];
                _directoryRows[i].ObjectId = e.id;
                _directoryRows[i].AvatarBg.color = AvatarColorFor(e.name);
                _directoryRows[i].AvatarInitial.text = string.IsNullOrEmpty(e.name) ? "?" : e.name.Substring(0, 1).ToUpperInvariant();
                string status = e.nearby
                    ? $"<color=#7CFFA0>● À proximité ({Mathf.RoundToInt(e.dist)} m)</color>"
                    : "<color=#9FD0FF>● En ligne</color>";
                if (e.homeless)
                    status += "  <color=#FF7A6B>SDF</color>";
                _directoryRows[i].NameText.text = Escape(e.name) + "\n<size=14>" + status + "</size>";
                _directoryRows[i].LevelText.text = "Niv. " + e.level;

                var rowRect = (RectTransform)_directoryRows[i].Button.transform;
                rowRect.anchoredPosition = new Vector2(0f, y);
                y -= 74f;
            }

            _contactsScroll.SetContentHeight(Mathf.Max(220f, -y + 10f));
            if (_contactsHint != null)
            {
                _contactsHint.text = entries.Count == 0
                    ? (query.Length > 0 ? "Aucun joueur trouvé pour « " + query + " »." : "Aucun autre joueur connecté pour l'instant.")
                    : entries.Count + (entries.Count > 1 ? " joueurs en ligne — Touche pour voir le profil" : " joueur en ligne — Touche pour voir le profil");
            }
        }

        private void EnsureDirectoryRowPool(int needed)
        {
            while (_directoryRows.Count < needed)
            {
                int idx = _directoryRows.Count;
                Button btn = MakeButton("Joueur_" + idx, _contactsScroll.Content, string.Empty, 20, Row,
                    new Vector2(0.5f, 1f), new Vector2(436f, 68f), Vector2.zero);

                RectTransform av = MakeImage("Avatar", btn.transform, ProfileAccent,
                    new Vector2(0f, 0.5f), new Vector2(46f, 46f), new Vector2(12f, 0f));
                Text init = MakeText("Init", av, "?", 22, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(46f, 46f), Vector2.zero);
                init.fontStyle = FontStyle.Bold;

                Text nameTxt = MakeText("Nom", btn.transform, string.Empty, 19, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.5f), new Vector2(260f, 56f), new Vector2(70f, 0f));
                nameTxt.supportRichText = true;

                RectTransform lvlBg = MakeImage("LvlBg", btn.transform, new Color(0.18f, 0.44f, 0.72f, 0.9f),
                    new Vector2(1f, 0.5f), new Vector2(88f, 34f), new Vector2(-12f, 0f));
                Text lvlTxt = MakeText("Lvl", lvlBg, "Niv. 1", 15, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(84f, 30f), Vector2.zero);
                lvlTxt.fontStyle = FontStyle.Bold;

                var row = new DirectoryRowUi
                {
                    Button = btn,
                    AvatarBg = av.GetComponent<Image>(),
                    AvatarInitial = init,
                    NameText = nameTxt,
                    LevelText = lvlTxt,
                };
                btn.onClick.AddListener(() => OpenPlayerProfile(row.ObjectId, Page.Contacts));
                _directoryRows.Add(row);
            }
        }
    }
}
