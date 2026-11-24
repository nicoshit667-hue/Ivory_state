using System;
using System.Reflection;
using IvoryState.Client.UI;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Editor.Debugging
{
    internal static class UiIntegrationVerifier
    {
        [MenuItem("Ivory State/Verification/Verifier icones, commerces, taxi et profil")]
        public static void VerifyAll()
        {
            CustomIconsImporter.SyncIconDatabase();

            var db = Resources.Load<UiIconDatabase>("UI/UiIconDatabase");
            if (db == null)
                throw new Exception("[Verify] UiIconDatabase introuvable dans Resources/UI/UiIconDatabase.");

            if (db.TaxiAppIcon == null)
                throw new Exception("[Verify] TaxiAppIcon manquant dans UiIconDatabase.");
            if (db.CommerceAppIcon == null)
                throw new Exception("[Verify] CommerceAppIcon manquant dans UiIconDatabase.");

            HudIconLibrary.EnsureInitialized();
            if (HudIconLibrary.AppTaxi != db.TaxiAppIcon)
                throw new Exception("[Verify] HudIconLibrary.AppTaxi n'utilise pas db.TaxiAppIcon.");

            // 1. Vérifier les 45 consommables
            for (int i = 0; i < ConsumableCatalog.All.Length; i++)
            {
                ConsumableDefinition item = ConsumableCatalog.All[i];
                if (!db.TryGetConsumable(item.IconKey, out Sprite direct) || direct == null)
                    throw new Exception("[Verify] Sprite consommable manquant dans UiIconDatabase : " + item.IconKey);
                Sprite resolved = HudIconLibrary.GetConsumableSprite(item.DisplayName);
                if (resolved != direct)
                    throw new Exception("[Verify] Resolution sprite incorrecte pour : " + item.DisplayName);
            }

            // 2. Vérifier les alias historiques des boutiques/étals
            string[] legacyNames =
            {
                "Garba", "un garba", "Attiéké poisson braisé", "Alloco poulet", "Eau minérale 1,5 L",
                "Kit de premiers secours", "Trousse médicale CHU", "Bananes (3)", "Noix de coco fraîche",
                "Café Touba", "Beignets (5)", "Maïs grillé", "Yaourt", "Pain + omelette"
            };
            for (int i = 0; i < legacyNames.Length; i++)
            {
                Sprite s = HudIconLibrary.GetConsumableSprite(legacyNames[i]);
                if (s == null || s == HudIconLibrary.Backpack)
                    throw new Exception("[Verify] Alias consommable non resolu vers une icone dediee : " + legacyNames[i]);
            }

            // 3. Vérifier les icônes et fonds dynamiques de tous les statuts/métiers
            foreach (ProfileStatusVisual meta in PlayerProfileMetaCatalog.Statuses)
            {
                if (!db.TryGetProfileIcon(meta.IconKey, out Sprite statusIcon) || statusIcon == null)
                    throw new Exception("[Verify] Icone statut manquante : " + meta.IconKey);
                if (!db.TryGetProfileBackground(meta.BackgroundKey, out Sprite statusBg) || statusBg == null)
                    throw new Exception("[Verify] Fond dynamique manquant : " + meta.BackgroundKey);
            }

            // 4. Vérifier les icônes de confidentialité & sections du profil
            string[] requiredProfileIcons =
            {
                "icon_residence_pin", "icon_maison", "icon_voiture", "icon_commerce",
                "icon_eye_open", "icon_eye_closed", "icon_lock"
            };
            for (int i = 0; i < requiredProfileIcons.Length; i++)
            {
                if (!db.TryGetProfileIcon(requiredProfileIcons[i], out Sprite pSprite) || pSprite == null)
                    throw new Exception("[Verify] Icone profil manquante : " + requiredProfileIcons[i]);
            }

            // 5. Vérifier le catalogue des commerces
            for (int i = 0; i < CommerceCatalog.All.Length; i++)
            {
                CommerceDefinition comm = CommerceCatalog.All[i];
                if (comm.LinkedPropertyId != 0 && !PropertyCatalog.IsValid(comm.LinkedPropertyId))
                    throw new Exception("[Verify] LinkedPropertyId invalide pour commerce " + comm.Name);
                if (comm.ConsumableIds.Length == 0)
                    throw new Exception("[Verify] Aucun consommable pour commerce " + comm.Name);
                for (int j = 0; j < comm.ConsumableIds.Length; j++)
                {
                    if (!ConsumableCatalog.TryGetById(comm.ConsumableIds[j], out _))
                        throw new Exception("[Verify] ConsumableId invalide '" + comm.ConsumableIds[j] + "' dans " + comm.Name);
                }
                Vector3 pos = CommerceCatalog.GetWorldPosition(comm);
                if (float.IsNaN(pos.x) || float.IsNaN(pos.z))
                    throw new Exception("[Verify] Position invalide pour commerce " + comm.Name);
            }

            // 6. Vérifier l'instanciation complète de l'UI PhoneHud + ChoiceMenuHud + Profil dynamique
            GameObject canvasGo = new GameObject("VerifyPhoneCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            try
            {
                var phoneHud = canvasGo.AddComponent<PhoneHud>();
                typeof(PhoneHud).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(phoneHud, null);
                Transform phoneRoot = canvasGo.transform.Find("Smartphone");
                if (phoneRoot == null)
                    throw new Exception("[Verify] Racine Smartphone introuvable apres construction PhoneHud.");

                Transform screen = phoneRoot.Find("EcranClip/Ecran") ?? phoneRoot.Find("Ecran");
                if (screen == null)
                    throw new Exception("[Verify] Ecran Smartphone introuvable.");

                string[] expectedPages = { "Home", "Profile", "Contacts", "Taxi", "Commerces", "Bag", "Djassa", "Jobs" };
                for (int i = 0; i < expectedPages.Length; i++)
                {
                    if (screen.Find(expectedPages[i]) == null)
                        throw new Exception("[Verify] Page manquante dans PhoneHud : " + expectedPages[i]);
                }

                Transform taxiIcon = screen.Find("Home/HomeScroll/Content/App_Taxi/Icone");
                if (taxiIcon == null || taxiIcon.GetComponent<Image>().sprite != db.TaxiAppIcon)
                    throw new Exception("[Verify] L'icone App_Taxi sur l'ecran d'accueil ne pointe pas sur Phone/taxi.png.");

                Transform profilePage = screen.Find("Profile");
                if (profilePage.Find("TabMonProfil") != null || profilePage.Find("JoueursVue") != null)
                    throw new Exception("[Verify] L'ecran Profil contient encore une barre d'onglets ou de recherche.");

                Transform contactsPage = screen.Find("Contacts");
                if (contactsPage.Find("RechercheContacts") == null || contactsPage.Find("TabJoueurs") == null)
                    throw new Exception("[Verify] Recherche/annuaire absent de Contacts.");

                Transform profileContent = profilePage.Find("ProfileScroll/Content");
                Transform heroCard = profileContent != null ? profileContent.Find("HeroCard") : null;
                if (heroCard == null)
                    throw new Exception("[Verify] HeroCard absente de Profile.");
                if (heroCard.Find("DynamicBg") == null || heroCard.Find("StatutPill") == null || heroCard.Find("VuesPill") == null)
                    throw new Exception("[Verify] Elements dynamiques manquants dans HeroCard.");

                // Tester le rendu dynamique par métier + masquage personnel + cadenas autre joueur
                MethodInfo onProfileReceived = typeof(PhoneHud).GetMethod("OnProfileReceived", BindingFlags.Instance | BindingFlags.NonPublic);
                if (onProfileReceived == null)
                    throw new Exception("[Verify] OnProfileReceived introuvable.");

                var selfSnapshot = new PlayerProfileSnapshot
                {
                    Found = true,
                    TargetObjectId = 10,
                    DisplayName = "Koffi",
                    Xp = 650,
                    Level = 4,
                    RankTitle = "Débrouillard",
                    IsSelf = true,
                    JobStatus = PlayerJobStatus.Medecin,
                    ProfileViews = 12,
                    PrivacyMask = (int)ProfilePrivacyFlags.Vehicles,
                    StudioId = 2,
                    ResidenceDistrict = "Treichville — Résidence Les Flamboyants",
                    Roommate = "Aya",
                    CarsCsv = "1,2",
                    HasLicense = true,
                    CommercesSummary = "Maquis Les Palmiers (Maquis · Yopougon · Ouvert)",
                    CommercesCount = 1,
                };
                onProfileReceived.Invoke(phoneHud, new object[] { selfSnapshot });

                Image dynBg = heroCard.Find("DynamicBg").GetComponent<Image>();
                if (dynBg.sprite != HudIconLibrary.GetProfileBackground(PlayerJobStatus.Medecin))
                    throw new Exception("[Verify] Le fond dynamique Medecin n'a pas ete applique.");

                Text viewsText = heroCard.Find("VuesPill/VuesTexte").GetComponent<Text>();
                if (viewsText.text != "12 vues")
                    throw new Exception("[Verify] Compteur de vues inattendu : " + viewsText.text);

                Transform vehiclesCard = profileContent.Find("Card_Voitures");
                if (!vehiclesCard.Find("PrivacyBtn").gameObject.activeSelf ||
                    vehiclesCard.Find("PrivacyBtn/EyeText").GetComponent<Text>().text != "Masqué")
                    throw new Exception("[Verify] Le bouton oeil de Voitures (Mon profil) devrait afficher 'Masqué'.");

                // Tester vue d'un autre joueur avec Gouvernement + sections masquées verrouillées
                var otherSnapshot = new PlayerProfileSnapshot
                {
                    Found = true,
                    TargetObjectId = 22,
                    DisplayName = "Awa",
                    Xp = 2500,
                    Level = 9,
                    RankTitle = "Citadin confirmé",
                    IsSelf = false,
                    JobStatus = PlayerJobStatus.Gouvernement,
                    ProfileViews = 41,
                    PrivacyMask = (int)(ProfilePrivacyFlags.Vehicles | ProfilePrivacyFlags.Commerces),
                    StudioId = 3,
                    ResidenceDistrict = "Plateau",
                };
                onProfileReceived.Invoke(phoneHud, new object[] { otherSnapshot });

                if (dynBg.sprite != HudIconLibrary.GetProfileBackground(PlayerJobStatus.Gouvernement))
                    throw new Exception("[Verify] Le fond dynamique Gouvernement n'a pas ete applique.");

                if (vehiclesCard.Find("PrivacyBtn").gameObject.activeSelf)
                    throw new Exception("[Verify] Le bouton oeil ne doit pas etre actif sur le profil d'un autre joueur.");

                if (!vehiclesCard.Find("Locked").gameObject.activeSelf || vehiclesCard.Find("Contenu").gameObject.activeSelf)
                    throw new Exception("[Verify] La section masquee Voitures d'un autre joueur doit afficher l'etat verrouille (cadenas).");

                Transform residenceCard = profileContent.Find("Card_Lieu de résidence");
                if (residenceCard.Find("Locked").gameObject.activeSelf || !residenceCard.Find("Contenu").gameObject.activeSelf)
                    throw new Exception("[Verify] La section non masquee Lieu de residence doit rester visible.");

                if (canvasGo.GetComponentInChildren<ChoiceMenuHud>(true) == null)
                    throw new Exception("[Verify] ChoiceMenuHud non cree par PhoneHud.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasGo);
            }

            Debug.Log("[Verify] SUCCES TOTAL : 45 consommables, 6 statuts/fonds dynamiques, " +
                      CommerceCatalog.Count + " commerces, Taxi, Contacts, et nouveau Profil verifies.");
        }
    }
}
