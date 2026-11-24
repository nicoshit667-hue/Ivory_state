using System;
using IvoryState.Gameplay.Combat;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Appli « Sac » : trois compartiments.
    /// <list type="bullet">
    /// <item><b>Consommables</b> : nourriture, boissons, soins (toucher pour utiliser).</item>
    /// <item><b>Armes</b> : machette, couteau, armes à feu (Équiper / Ranger) et munitions.</item>
    /// <item><b>Autres</b> : papiers (carte d'identité, permis de conduire) et objets divers.</item>
    /// </list>
    /// </summary>
    public sealed partial class PhoneHud
    {
        private static readonly Color BagBackground = new Color(0.16f, 0.11f, 0.07f, 1f);
        private static readonly Color BagAccent = new Color(0.80f, 0.45f, 0.15f, 1f);
        private static readonly Color BagTabIdle = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color BagActionUse = new Color(0.5f, 1f, 0.6f);
        private static readonly Color BagActionSee = new Color(0.55f, 0.8f, 1f);
        private static readonly Color BagActionMuted = new Color(1f, 1f, 1f, 0.45f);

        /// <summary>Emplacements du sac plus les deux papiers (carte d'identité, permis).</summary>
        private const int BagRows = PlayerInventory.MaxSlots + 2;
        private const float BagRowStep = 72f;

        private ItemCategory _bagTab = ItemCategory.Consumable;
        private Text _bagHint;
        private PhoneScrollView _bagScroll;
        private readonly Image[] _bagTabBackgrounds = new Image[3];
        private readonly Text[] _bagTabLabels = new Text[3];
        private readonly Button[] _bagButtons = new Button[BagRows];
        private readonly Image[] _bagIcons = new Image[BagRows];
        private readonly Text[] _bagLabels = new Text[BagRows];
        private readonly Text[] _bagDetails = new Text[BagRows];
        private readonly Text[] _bagActions = new Text[BagRows];
        private readonly Action[] _bagRowActions = new Action[BagRows];

        private void BuildBag(RectTransform screen)
        {
            RectTransform page = MakePage(Page.Bag, screen);
            page.GetComponent<Image>().color = BagBackground;
            MakeHeader(page, "Mon sac", BagAccent);

            // Onglets : Consommables | Armes | Autres, chacun avec son icône.
            Sprite[] tabIcons =
            {
                HudIconLibrary.Hunger,
                HudIconLibrary.LoadWeaponAimSprite() ?? HudIconLibrary.Fight,
                HudIconLibrary.GetProfileIcon("icon_civil"),
            };
            for (int i = 0; i < 3; i++)
            {
                var tab = (ItemCategory)i;
                Button button = MakeButton("Onglet_" + tab, page, ItemCategories.Label(tab), 14, BagTabIdle,
                    new Vector2(0.5f, 1f), new Vector2(142f, 54f), new Vector2(-148f + i * 148f, -88f));
                Text label = button.GetComponentInChildren<Text>();
                label.fontStyle = FontStyle.Bold;
                label.rectTransform.offsetMin = new Vector2(40f, 0f);
                RectTransform icon = MakeImage("Icone", button.transform, Color.white, new Vector2(0f, 0.5f), new Vector2(34f, 34f), new Vector2(8f, 0f));
                Image iconImg = icon.GetComponent<Image>();
                iconImg.sprite = tabIcons[i];
                iconImg.type = Image.Type.Simple;
                iconImg.preserveAspect = true;
                button.onClick.AddListener(() => SelectBagTab(tab));
                _bagTabBackgrounds[i] = (Image)button.targetGraphic;
                _bagTabLabels[i] = label;
            }

            _bagHint = MakeText("Info", page, string.Empty, 17, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(440f, 34f), new Vector2(0f, -148f));
            _bagHint.color = new Color(1f, 1f, 1f, 0.8f);

            _bagScroll = PhoneScrollView.Create(page, "BagScroll", 188f, 6f);
            RegisterPageScroll(Page.Bag, _bagScroll);

            for (int i = 0; i < BagRows; i++)
            {
                int row = i;
                Button button = MakeButton("Objet_" + i, _bagScroll.Content, string.Empty, 24, Row,
                    new Vector2(0.5f, 1f), new Vector2(430f, 66f), new Vector2(0f, -6f - i * BagRowStep));
                RectTransform iconRect = MakeImage("Icone", button.transform, Color.white, new Vector2(0f, 0.5f), new Vector2(46f, 46f), new Vector2(12f, 0f));
                Image iconImg = iconRect.GetComponent<Image>();
                iconImg.type = Image.Type.Simple;
                iconImg.preserveAspect = true;
                _bagIcons[i] = iconImg;

                _bagLabels[i] = MakeText("Nom", button.transform, string.Empty, 22, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(246f, 34f), new Vector2(68f, 10f));
                _bagDetails[i] = MakeText("Effet", button.transform, string.Empty, 16, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(246f, 24f), new Vector2(68f, -18f));
                _bagDetails[i].color = new Color(1f, 0.85f, 0.55f, 0.9f);
                _bagActions[i] = MakeText("Action", button.transform, string.Empty, 19, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(104f, 60f), new Vector2(-12f, 0f));
                button.onClick.AddListener(() => _bagRowActions[row]?.Invoke());
                _bagButtons[i] = button;
                button.gameObject.SetActive(false);
            }
        }

        private void SelectBagTab(ItemCategory tab)
        {
            if (_bagTab == tab)
                return;
            _bagTab = tab;
            _bagScroll?.ResetToTopAnimated(playEntrance: false);
            FillBag();
        }

        private void FillBag()
        {
            PlayerInventory bag = PlayerInventory.Local;
            int count = bag != null ? bag.Count : 0;

            // Nombre d'objets par compartiment (affiché sur les onglets).
            var perTab = new int[3];
            for (int i = 0; i < count; i++)
                perTab[(int)ItemCategories.Of(bag[i])] += bag[i].Count;
            perTab[(int)ItemCategory.Other] += 1; // carte d'identité, toujours sur soi
            PlayerGarage garage = PlayerGarage.Local;
            bool hasLicense = garage != null && garage.HasLicense;
            if (hasLicense)
                perTab[(int)ItemCategory.Other] += 1;

            for (int i = 0; i < 3; i++)
            {
                bool active = (int)_bagTab == i;
                _bagTabBackgrounds[i].color = active ? BagAccent : BagTabIdle;
                string name = ItemCategories.Label((ItemCategory)i);
                _bagTabLabels[i].text = perTab[i] > 0 ? name + "\n" + perTab[i] : name;
            }

            int row = 0;
            if (_bagTab == ItemCategory.Other)
            {
                SetBagRow(row++, HudIconLibrary.GetProfileIcon("icon_civil"), "Carte d'identité",
                    "Nom, statut et métier", "Voir", BagActionSee, () => OpenPlayerProfile(-1, Page.Bag));
                SetBagRow(row++, HudIconLibrary.GetProfileIcon("icon_voiture"), "Permis de conduire",
                    hasLicense ? "Valide" : "Pas encore obtenu : auto-école",
                    hasLicense ? "Voir" : "Garage", hasLicense ? BagActionSee : BagActionMuted,
                    () => ShowPage(Page.Garage));
            }

            for (int slot = 0; slot < count && row < BagRows; slot++)
            {
                InventoryItem item = bag[slot];
                if (ItemCategories.Of(item) != _bagTab)
                    continue;
                int target = slot;
                string label = item.Name + "  ×" + item.Count;
                switch (_bagTab)
                {
                    case ItemCategory.Consumable:
                        SetBagRow(row++, HudIconLibrary.GetConsumableSprite(item.Name), label, ConsumableEffects(item),
                            "Utiliser", BagActionUse, () => UseBagSlot(target));
                        break;
                    case ItemCategory.Weapon:
                        FillWeaponRow(row++, bag, item, label, target);
                        break;
                    default:
                        if (item.Name == Weapons.Permit)
                            SetBagRow(row++, HudIconLibrary.GetProfileIcon("icon_gouvernement"), label, "Document officiel : armes à feu autorisées",
                                "Infos", BagActionSee, () => UseBagSlot(target));
                        else
                            SetBagRow(row++, HudIconLibrary.Backpack, label, "Objet",
                                "Utiliser", BagActionUse, () => UseBagSlot(target));
                        break;
                }
            }

            for (int i = row; i < BagRows; i++)
            {
                _bagButtons[i].gameObject.SetActive(false);
                _bagRowActions[i] = null;
            }
            _bagScroll?.SetContentHeight(12f + row * BagRowStep);

            _bagHint.text = _bagTab switch
            {
                ItemCategory.Consumable => perTab[0] == 0
                    ? "Aucun consommable. Achète en boutique ou sur Djassa."
                    : "Touche un produit pour manger, boire ou te soigner.",
                ItemCategory.Weapon => perTab[1] == 0
                    ? "Aucune arme. Machettes, couteaux : marché black d'Adjamé, quincailleries. Armes à feu : Armurerie du Plateau (port d'arme). Voir la carte."
                    : "Équipe une arme à feu pour tirer. Une arme blanche sert au racket.",
                _ => "Tes papiers et tes autres objets.",
            };
        }

        /// <summary>Onglet Armes : arme à feu (Équiper / Ranger), munitions, ou arme blanche.</summary>
        private void FillWeaponRow(int row, PlayerInventory bag, InventoryItem item, string label, int slot)
        {
            FirearmDef gun = Firearms.ByName(item.Name);
            if (gun != null)
            {
                PlayerCombat combat = PlayerCombat.Local;
                bool inHand = combat != null && combat.EquippedFirearm == gun;
                int magazines = bag.CountOf(gun.Ammo);
                string detail = inHand
                    ? "En main : " + combat.Loaded + "/" + gun.MagazineSize + (combat.IsReloading ? " (recharge…)" : string.Empty)
                    : "Arme à feu : " + magazines + (gun.MagazineSize > 1 ? " chargeur" : " cartouche") + (magazines > 1 ? "s" : string.Empty);
                string gunName = item.Name;
                SetBagRow(row, HudIconLibrary.LoadWeaponFireSprite() ?? HudIconLibrary.Fight, label, detail,
                    inHand ? "Ranger" : "Équiper", inHand ? BagActionSee : BagActionUse, () =>
                    {
                        PlayerCombat.Local?.RequestEquipToggle(gunName);
                        StartCoroutine(RefreshBagSoon());
                    });
                return;
            }

            FirearmDef ammoFor = Firearms.ByAmmo(item.Name);
            if (ammoFor != null)
            {
                string content = ammoFor.MagazineSize > 1 ? ammoFor.MagazineSize + " balles" : "1 tir";
                SetBagRow(row, HudIconLibrary.LoadWeaponReloadSprite() ?? HudIconLibrary.Backpack, label,
                    "Munitions du " + ammoFor.Name.ToLowerInvariant() + " (" + content + ")", "Infos", BagActionSee, () => UseBagSlot(slot));
                return;
            }

            SetBagRow(row, HudIconLibrary.Fight, label, "Arme blanche : permet le racket",
                "Infos", BagActionSee, () => UseBagSlot(slot));
        }

        /// <summary>L'arme en main est synchronisée par le serveur : on relit le sac un instant après.</summary>
        private System.Collections.IEnumerator RefreshBagSoon()
        {
            yield return new WaitForSecondsRealtime(0.35f);
            if (_page == Page.Bag && _phone.gameObject.activeSelf)
                FillBag();
        }

        private void SetBagRow(int row, Sprite icon, string label, string detail, string action, Color actionColor, Action onClick)
        {
            _bagButtons[row].gameObject.SetActive(true);
            _bagIcons[row].gameObject.SetActive(icon != null);
            _bagIcons[row].sprite = icon;
            _bagLabels[row].text = label;
            _bagDetails[row].text = detail;
            _bagActions[row].text = action;
            _bagActions[row].color = actionColor;
            _bagRowActions[row] = onClick;
        }

        private static string ConsumableEffects(InventoryItem item) =>
            (item.Health > 0 ? "Santé +" + item.Health + "   " : string.Empty) +
            (item.Satiety > 0 ? "Faim +" + item.Satiety + "   " : string.Empty) +
            (item.Hydration > 0 ? "Soif +" + item.Hydration : string.Empty);

        private void UseBagSlot(int slot)
        {
            PlayerInventory.Local?.RequestUse(slot);
        }
    }
}
