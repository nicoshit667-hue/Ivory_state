using FishNet.Component.Observing;
using FishNet.Component.Transforming.Beta;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Observing;
using FishNet.Object;
using IvoryState.Client.Cameras;
using IvoryState.Client.Debugging;
using IvoryState.Client.Input;
using IvoryState.Client.UI;
using IvoryState.Gameplay.Interaction;
using IvoryState.Core;
using IvoryState.Core.Networking;
using IvoryState.Gameplay.Networking;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using IvoryState.Gameplay.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Editor
{
    /// <summary>
    /// Génère les assets de base : NetworkConfig, prefab joueur (prédit, avec visuel lissé),
    /// scène Bootstrap avec HUD tactile, caméra à la troisième personne et arène de test.
    /// <para>
    /// Le prefab joueur et la scène sont <b>régénérés</b> à chaque exécution (GUID conservés,
    /// les références restent valides). La NetworkConfig existante est conservée.
    /// </para>
    /// <para>Ligne de commande : <c>-executeMethod IvoryState.Editor.IvoryProjectSetup.SetupAll</c></para>
    /// </summary>
    public static class IvoryProjectSetup
    {
        private const string ProjectRoot = "Assets/_Project";
        private const string ConfigPath = ProjectRoot + "/Settings/Network/NetworkConfig.asset";
        private const string PlayerPrefabPath = ProjectRoot + "/Prefabs/Characters/PF_Player.prefab";
        private const string TaxiPrefabPath = ProjectRoot + "/Prefabs/Vehicles/PF_Taxi.prefab";
        private const string BootstrapScenePath = ProjectRoot + "/Scenes/Bootstrap.unity";
        private const string GridConditionPath = ProjectRoot + "/Settings/Network/GridCondition.asset";
        private const string NearestPlayersConditionPath = ProjectRoot + "/Settings/Network/NearestPlayersCondition.asset";

        // Taille d'une cellule de la grille d'intérêt (m). Avec les 3 × 3 cellules voisines, un joueur
        // voit les autres jusqu'à 50-100 m : cohérent avec une ville où les immeubles bloquent la vue.
        private const int InterestCellSize = 50;

        // Gabarit du personnage : pivot aux pieds, 1,80 m.
        private const float PlayerHeight = 1.8f;
        private const float PlayerRadius = 0.35f;

        [MenuItem("Ivory State/Setup/Générer le bootstrap réseau", priority = 0)]
        public static void SetupAll()
        {
            bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath) != null;
            if (sceneExists && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Ivory State",
                    "La scène Bootstrap et le prefab PF_Player vont être régénérés. " +
                    "Les modifications faites à la main dessus seront perdues.", "Régénérer", "Annuler"))
                return;

            // Personnages réalistes (Rocketbox), avant le prefab joueur qui les référence.
            CustomIconsImporter.SyncIconDatabase();
            Characters.CharacterSetup.Setup();

            NetworkObject playerPrefab = BuildPlayerPrefab();
            NetworkObject taxiPrefab = BuildTaxiPrefab();
            GetOrCreateConfig();
            DefaultPrefabObjects prefabs = RegisterSpawnablePrefab(playerPrefab, taxiPrefab);

            // Le rafraîchissement de FishNet réimporte des assets : les références C# obtenues
            // avant deviennent invalides. On les recharge depuis le disque.
            playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
            taxiPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(TaxiPrefabPath);
            NetworkConfig config = AssetDatabase.LoadAssetAtPath<NetworkConfig>(ConfigPath);

            SetProperty(config, "_playerPrefab", playerPrefab);
            SetProperty(config, "_vehiclePrefab", taxiPrefab);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            CreateBootstrapScene(prefabs);

            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Bootstrap réseau prêt : " + BootstrapScenePath);
        }

        /// <summary>
        /// Ouvre la scène Bootstrap. Ligne de commande (éditeur interactif) :
        /// <c>Unity.exe -projectPath IvoryState -executeMethod IvoryState.Editor.IvoryProjectSetup.OpenBootstrapScene</c>
        /// </summary>
        [MenuItem("Ivory State/Ouvrir la scène Bootstrap", priority = -100)]
        public static void OpenBootstrapScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath) == null)
            {
                Debug.LogWarning("[Setup] Bootstrap.unity n'existe pas encore : lance Ivory State › Setup.");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// Reconstruit uniquement le prefab <c>PF_Taxi</c> (en conservant son GUID) à partir du modèle Blender
        /// <c>Art/Models/Vehicles/VH_Taxi_Corolla.fbx</c>, sans régénérer toute la ville.
        /// Ligne de commande : <c>-executeMethod IvoryState.Editor.IvoryProjectSetup.RebuildTaxiPrefab</c>
        /// </summary>
        [MenuItem("Ivory State/Setup/Mettre à jour le prefab Taxi", priority = 5)]
        public static void RebuildTaxiPrefab()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            NetworkObject taxiPrefab = BuildTaxiPrefab();
            RegisterSpawnablePrefab(taxiPrefab);

            taxiPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(TaxiPrefabPath);
            NetworkConfig config = AssetDatabase.LoadAssetAtPath<NetworkConfig>(ConfigPath);
            if (config != null && taxiPrefab != null)
            {
                SetProperty(config, "_vehiclePrefab", taxiPrefab);
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Prefab PF_Taxi mis à jour : " + TaxiPrefabPath);
        }

        #region Assets

        private static GridCondition GetOrCreateGridCondition()
        {
            return GetOrCreateCondition<GridCondition>(GridConditionPath);
        }

        private static T GetOrCreateCondition<T>(string path) where T : ScriptableObject
        {
            var condition = AssetDatabase.LoadAssetAtPath<T>(path);
            if (condition != null)
                return condition;

            EnsureFolder(ProjectRoot + "/Settings/Network");
            condition = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(condition, path);
            return condition;
        }

        private static NetworkConfig GetOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<NetworkConfig>(ConfigPath);
            if (config != null)
                return config;

            EnsureFolder(ProjectRoot + "/Settings/Network");
            config = ScriptableObject.CreateInstance<NetworkConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        /// <summary>
        /// Structure du prefab :
        /// <code>
        /// PF_Player           CharacterController, NetworkObject (prédiction), PlayerNetworkIdentity, PlayerMotor
        /// └─ Graphics         NetworkTickSmoother : suit la racine en douceur (c'est lui que la caméra suit)
        ///    ├─ Body          placeholder capsule, à remplacer par le vrai modèle
        ///    └─ Facing        petit repère indiquant l'avant du personnage
        /// </code>
        /// La racine se « téléporte » lors des recalages serveur ; seul Graphics est affiché.
        /// </summary>
        private static NetworkObject BuildPlayerPrefab()
        {
            EnsureFolder(ProjectRoot + "/Prefabs/Characters");

            var root = new GameObject("PF_Player");

            var controller = root.AddComponent<CharacterController>();
            controller.height = PlayerHeight;
            controller.radius = PlayerRadius;
            controller.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);
            controller.stepOffset = 0.35f; // trottoirs et marches
            controller.slopeLimit = 45f;
            controller.skinWidth = 0.04f;
            controller.minMoveDistance = 0f; // indispensable avec la prédiction : aucun petit pas ignoré

            // Les passants (NavMeshAgent locaux) contournent les joueurs au lieu de les traverser.
            var obstacle = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Capsule;
            obstacle.radius = PlayerRadius;
            obstacle.height = PlayerHeight;
            obstacle.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);
            obstacle.carving = false;

            var networkObject = root.AddComponent<NetworkObject>();
            var identity = root.AddComponent<PlayerNetworkIdentity>();
            var motor = root.AddComponent<PlayerMotor>();
            root.AddComponent<PlayerNeeds>();
            root.AddComponent<PlayerEconomy>();
            root.AddComponent<PlayerInteractor>();
            root.AddComponent<PlayerChat>();
            root.AddComponent<PlayerPhone>();
            root.AddComponent<PlayerDriving>();
            root.AddComponent<IvoryState.Gameplay.Accounts.PlayerAccount>();
            root.AddComponent<PlayerInventory>();
            root.AddComponent<IvoryState.Gameplay.Social.PlayerVoice>();
            root.AddComponent<IvoryState.Gameplay.Social.PlayerFriends>();
            root.AddComponent<IvoryState.Gameplay.Economy.PlayerServices>();
            root.AddComponent<IvoryState.Gameplay.Homes.PlayerHome>();
            root.AddComponent<IvoryState.Gameplay.Economy.PlayerLottery>();
            root.AddComponent<IvoryState.Gameplay.Combat.PlayerCombat>();
            root.AddComponent<IvoryState.Gameplay.Social.PlayerWassup>();
            root.AddComponent<IvoryState.Gameplay.Vehicles.PlayerGarage>();
            root.AddComponent<IvoryState.Gameplay.Economy.PlayerMarket>();
            root.AddComponent<IvoryState.Gameplay.Economy.PlayerTransfers>();

            var graphics = new GameObject("Graphics").transform;
            graphics.SetParent(root.transform, false);

            Transform body = CreateVisualPart(PrimitiveType.Capsule, "Body", graphics,
                new Vector3(0f, PlayerHeight * 0.5f, 0f), new Vector3(PlayerRadius * 2f, PlayerHeight * 0.5f, PlayerRadius * 2f));
            Transform facing = CreateVisualPart(PrimitiveType.Cube, "Facing", graphics,
                new Vector3(0f, 1.45f, PlayerRadius), new Vector3(0.2f, 0.12f, 0.25f));

            // Avatar réaliste choisi d'après l'identifiant réseau ; la capsule n'est qu'un repli.
            var avatarView = root.AddComponent<IvoryState.Client.Characters.PlayerAvatarView>();
            SetProperty(avatarView, "_models",
                AssetDatabase.LoadAssetAtPath<IvoryState.Client.Characters.CharacterModels>(Characters.CharacterSetup.ModelsPath));
            SetProperty(avatarView, "_graphics", graphics);
            var placeholders = new SerializedObject(avatarView);
            SetArray(placeholders.FindProperty("_placeholders"),
                new Object[] { body.GetComponent<MeshRenderer>(), facing.GetComponent<MeshRenderer>() });
            placeholders.ApplyModifiedPropertiesWithoutUndo();

            var smoother = graphics.gameObject.AddComponent<NetworkTickSmoother>();

            SetProperty(networkObject, "_enablePrediction", true);
            SetProperty(networkObject, "_predictionType", 0); // Other : mouvement par CharacterController
            SetProperty(networkObject, "_enableStateForwarding", true); // les autres joueurs sont aussi simulés
            SetProperty(smoother, "_initializationSettings.TargetTransform", root.transform);
            SetProperty(motor, "_graphicalTransform", graphics);
            SetProperty(identity, "_bodyRenderer", body.GetComponent<MeshRenderer>());

            // Joueurs distants : un tick de tampon d'interpolation en plus (+33 ms de retard visuel,
            // invisible pour un passant) absorbe les paquets irréguliers du réseau mobile.
            SetProperty(smoother, "_spectatorMovementSettings.InterpolationValue", 3);

            // Couche « Player » : les joueurs se traversent, et la caméra et le sol les ignorent.
            EnsureLayerName(GameLayers.Player, GameLayers.PlayerName);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = GameLayers.Player;

            // Écrase le prefab existant en conservant son GUID : les références restent valides.
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<NetworkObject>();
        }

        private static Transform CreateVisualPart(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>()); // seul le CharacterController collisionne
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            return part.transform;
        }

        /// <summary>Bouton « Payer [pseudo] » (en haut, à côté du chat) et panneau de choix du montant.</summary>
        private static void CreateTransferUi(Transform hud, Sprite rounded)
        {
            var root = new GameObject("Transfer", typeof(RectTransform));
            root.transform.SetParent(hud, false);
            Stretch((RectTransform)root.transform, 0f);

            Button pay = CreateButton("PayButton", root.transform, rounded, "Payer", 28);
            pay.GetComponent<Image>().color = new Color(0.10f, 0.45f, 0.25f, 0.92f);
            var payRect = (RectTransform)pay.transform;
            payRect.anchorMin = payRect.anchorMax = payRect.pivot = new Vector2(0f, 1f);
            payRect.sizeDelta = new Vector2(330f, 80f);
            payRect.anchoredPosition = new Vector2(330f, -200f); // sous « Chat » et « Téléphone »

            RectTransform panel = CreateHudImage("Panel", root.transform, rounded, new Color(0f, 0f, 0f, 0.82f));
            panel.GetComponent<Image>().type = Image.Type.Sliced;
            panel.GetComponent<Image>().raycastTarget = true;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(820f, 470f);

            Text title = CreateHudText("Title", panel, 36, TextAnchor.UpperCenter, Color.white);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(780f, 56f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);

            var presets = new Button[4];
            for (int i = 0; i < presets.Length; i++)
            {
                Button preset = CreateButton("Preset_" + i, panel, rounded, "…", 30);
                var rect = (RectTransform)preset.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(180f, 90f);
                rect.anchoredPosition = new Vector2(-285f + i * 190f, -110f);
                presets[i] = preset;
            }

            InputField amount = CreateInputField(panel, rounded);
            var amountRect = (RectTransform)amount.transform;
            amountRect.anchorMin = amountRect.anchorMax = amountRect.pivot = new Vector2(0.5f, 1f);
            amountRect.sizeDelta = new Vector2(420f, 72f);
            amountRect.anchoredPosition = new Vector2(-140f, -240f);
            ((Text)amount.placeholder).text = "Autre montant (FCFA)";

            Button send = CreateButton("Send", panel, rounded, "Envoyer", 30);
            var sendRect = (RectTransform)send.transform;
            sendRect.anchorMin = sendRect.anchorMax = sendRect.pivot = new Vector2(0.5f, 1f);
            sendRect.sizeDelta = new Vector2(260f, 72f);
            sendRect.anchoredPosition = new Vector2(225f, -240f);

            Button cancel = CreateButton("Cancel", panel, rounded, "Annuler", 28);
            var cancelRect = (RectTransform)cancel.transform;
            cancelRect.anchorMin = cancelRect.anchorMax = cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.sizeDelta = new Vector2(240f, 70f);
            cancelRect.anchoredPosition = new Vector2(0f, 26f);

            var transferHud = root.AddComponent<TransferHud>();
            var so = new SerializedObject(transferHud);
            so.FindProperty("_payButton").objectReferenceValue = pay;
            so.FindProperty("_payLabel").objectReferenceValue = pay.GetComponentInChildren<Text>();
            so.FindProperty("_panel").objectReferenceValue = panel;
            so.FindProperty("_title").objectReferenceValue = title;
            so.FindProperty("_amountInput").objectReferenceValue = amount;
            so.FindProperty("_sendButton").objectReferenceValue = send;
            so.FindProperty("_cancelButton").objectReferenceValue = cancel;
            SetArray(so.FindProperty("_presetButtons"), presets);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Panneau central « Choisis ton pseudo » (affiché au premier lancement seulement).</summary>
        private static void CreateNameEntryPanel(Transform hud, Sprite rounded)
        {
            var root = new GameObject("NameEntry", typeof(RectTransform));
            root.transform.SetParent(hud, false);
            Stretch((RectTransform)root.transform, 0f);

            RectTransform panel = CreateHudImage("Panel", root.transform, rounded, new Color(0f, 0f, 0f, 0.82f));
            panel.GetComponent<Image>().type = Image.Type.Sliced;
            panel.GetComponent<Image>().raycastTarget = true;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(760f, 380f);
            panel.anchoredPosition = Vector2.zero;

            Text title = CreateHudText("Title", panel, 42, TextAnchor.UpperCenter, Color.white);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(700f, 60f);
            titleRect.anchoredPosition = new Vector2(0f, -30f);
            title.text = "Bienvenue à Ivory State ! Choisis ton pseudo";
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.resizeTextForBestFit = true;
            title.resizeTextMaxSize = 42;

            InputField input = CreateInputField(panel, rounded);
            var inputRect = (RectTransform)input.transform;
            inputRect.anchorMin = inputRect.anchorMax = inputRect.pivot = new Vector2(0.5f, 0.5f);
            inputRect.sizeDelta = new Vector2(520f, 72f);
            inputRect.anchoredPosition = new Vector2(0f, 20f);
            ((Text)input.placeholder).text = "Ton pseudo…";

            Text hint = CreateHudText("Hint", panel, 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(0.5f, 0.5f);
            hintRect.sizeDelta = new Vector2(700f, 40f);
            hintRect.anchoredPosition = new Vector2(0f, -45f);

            Button confirm = CreateButton("Confirm", panel, rounded, "Valider", 32);
            var confirmRect = (RectTransform)confirm.transform;
            confirmRect.anchorMin = confirmRect.anchorMax = confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.sizeDelta = new Vector2(260f, 80f);
            confirmRect.anchoredPosition = new Vector2(0f, 30f);

            var entry = root.AddComponent<NameEntryPanel>();
            var so = new SerializedObject(entry);
            so.FindProperty("_panel").objectReferenceValue = panel;
            so.FindProperty("_input").objectReferenceValue = input;
            so.FindProperty("_confirm").objectReferenceValue = confirm;
            so.FindProperty("_hint").objectReferenceValue = hint;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Étiquettes de nom au-dessus des joueurs proches (pool de 12).</summary>
        private static void CreateNameTags(Transform hud)
        {
            var root = new GameObject("NameTags", typeof(RectTransform));
            root.transform.SetParent(hud, false);
            Stretch((RectTransform)root.transform, 0f);

            var tags = new Text[12];
            for (int i = 0; i < tags.Length; i++)
            {
                Text tag = CreateHudText("Tag_" + i, root.transform, 24, TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.95f));
                tag.supportRichText = false;
                tag.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
                var rect = (RectTransform)tag.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(300f, 34f);
                tags[i] = tag;
            }

            var nameTags = root.AddComponent<NameTagsHud>();
            var so = new SerializedObject(nameTags);
            SetArray(so.FindProperty("_tags"), tags);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// FishNet ne peut instancier sur le réseau que les prefabs listés dans DefaultPrefabObjects.
        /// Son générateur le fait automatiquement ; on force un rafraîchissement par sécurité.
        /// </summary>
        private static DefaultPrefabObjects RegisterSpawnablePrefab(params NetworkObject[] networkPrefabs)
        {
            EditorApplication.ExecuteMenuItem("Tools/Fish-Networking/Utility/Refresh Default Prefabs");

            DefaultPrefabObjects prefabs = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(DefaultPrefabObjects)))
            {
                prefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefabs != null)
                    break;
            }

            if (prefabs == null)
            {
                Debug.LogWarning("[Setup] DefaultPrefabObjects introuvable : FishNet le créera à l'ouverture de la scène.");
                return null;
            }

            foreach (NetworkObject prefab in networkPrefabs)
                prefabs.AddObject(prefab, checkForDuplicates: true);
            EditorUtility.SetDirty(prefabs);
            return prefabs;
        }

        /// <summary>
        /// Taxi orange (couleur des taxis compteurs d'Abidjan), en volumes simples :
        /// <code>
        /// PF_Taxi            CharacterController, NetworkObject (prédiction), VehicleMotor — couche Vehicle
        /// └─ Graphics        NetworkTickSmoother : carrosserie, habitacle, enseigne, roues
        /// </code>
        /// </summary>
        private static NetworkObject BuildTaxiPrefab()
        {
            EnsureFolder(ProjectRoot + "/Prefabs/Vehicles");

            var root = new GameObject("PF_Taxi");
            var controller = root.AddComponent<CharacterController>();
            controller.radius = 1.1f;
            controller.height = 2.2f;
            controller.center = new Vector3(0f, 1.1f, 0f);
            controller.stepOffset = 0.3f;   // monte sur un trottoir (0,15 m), pas sur un muret
            controller.slopeLimit = 30f;
            controller.skinWidth = 0.05f;
            controller.minMoveDistance = 0f;

            var networkObject = root.AddComponent<NetworkObject>();
            var motor = root.AddComponent<VehicleMotor>();

            var graphics = new GameObject("Graphics").transform;
            graphics.SetParent(root.transform, false);

            // Berline orange modélisée en code : caisse lissée, vitres, phares et feux de nuit, lumignon.
            Vehicles.TaxiModelBuilder.Build(graphics);

            var smoother = graphics.gameObject.AddComponent<NetworkTickSmoother>();
            SetProperty(networkObject, "_enablePrediction", true);
            SetProperty(networkObject, "_predictionType", 0);
            SetProperty(networkObject, "_enableStateForwarding", true);
            // Si le conducteur se déconnecte, le taxi reste dans la rue au lieu d'être détruit avec lui.
            SetProperty(networkObject, "_preventDespawnOnDisconnect", true);
            SetProperty(smoother, "_initializationSettings.TargetTransform", root.transform);
            SetProperty(smoother, "_spectatorMovementSettings.InterpolationValue", 3);
            SetProperty(motor, "_graphicalTransform", graphics);

            EnsureLayerName(GameLayers.Vehicle, GameLayers.VehicleName);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = GameLayers.Vehicle;

            // Carrosserie solide aux dimensions de la berline : les joueurs ne la traversent plus, et
            // VehicleMotor la balaie pour s'arrêter contre les obstacles (la capsule ne couvre pas l'avant).
            EnsureLayerName(GameLayers.VehicleBody, GameLayers.VehicleBodyName);
            var bodyCollider = new GameObject("CarrosserieSolide");
            bodyCollider.transform.SetParent(root.transform, false);
            bodyCollider.layer = GameLayers.VehicleBody;
            var bodyBox = bodyCollider.AddComponent<BoxCollider>();
            bodyBox.center = new Vector3(0f, 0.8f, 0f);
            bodyBox.size = new Vector3(1.8f, 1.1f, 4.4f);
            var carObstacle = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            carObstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            carObstacle.center = new Vector3(0f, 0.7f, 0f);
            carObstacle.size = new Vector3(1.8f, 1.4f, 4.4f);
            carObstacle.carving = false;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, TaxiPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<NetworkObject>();
        }

        private static Transform AddPart(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.transform;
        }

        #endregion

        #region Scène

        private static void CreateBootstrapScene(DefaultPrefabObjects prefabs)
        {
            EnsureFolder(ProjectRoot + "/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Réseau ---
            var networkGo = new GameObject("NetworkManager");
            var networkManager = networkGo.AddComponent<NetworkManager>();
            if (prefabs != null)
                networkManager.SpawnablePrefabs = prefabs;

            var bootstrap = networkGo.AddComponent<NetworkBootstrap>();
            networkGo.AddComponent<DevConnectionPanel>();

            // --- Gestion de l'intérêt ---
            // Grille de hachage sur le plan XZ : chaque client ne reçoit que les objets des cellules
            // voisines de la sienne (3 × 3 cellules), soit un rayon de vue d'environ 1 à 2 cellules.
            var hashGrid = networkGo.AddComponent<HashGrid>();
            SetProperty(hashGrid, "_gridAxes", 2); // GridAxes.XZ
            SetProperty(hashGrid, "_accuracy", InterestCellSize);
            var observerManager = networkGo.AddComponent<ObserverManager>();

            // --- Contrôles et caméra ---
            MobilePlayerControls controls = CreateHud();

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 1000f; // couvre la Tour F (412 m) et la silhouette du Plateau
            cameraGo.AddComponent<AudioListener>();
            cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 14f, -14f), Quaternion.Euler(45f, 0f, 0f));
            var rig = cameraGo.AddComponent<ThirdPersonCameraRig>();
            SetProperty(rig, "_controls", controls);

            // --- Décor ---
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Vie de la ville, côté client uniquement : jour/nuit et passants d'ambiance.
            var world = new GameObject("WorldAmbience");
            var dayNight = world.AddComponent<IvoryState.Client.World.DayNightCycle>();
            SetProperty(dayNight, "_sun", light);
            var pedestrians = world.AddComponent<IvoryState.Client.World.AmbientPedestrians>();
            SetProperty(pedestrians, "_material", GetOrCreateMaterial("M_Pedestrian", Color.white));
            SetProperty(pedestrians, "_models",
                AssetDatabase.LoadAssetAtPath<IvoryState.Client.Characters.CharacterModels>(Characters.CharacterSetup.ModelsPath));

            // Ville procédurale : quartiers, lagune, ponts, commerces, livraisons, décor et NavMesh.
            City.CityGenerator.Generate(null);

            // Ciel, ambiance, brume (qui masque aussi la limite de rendu), post-traitement, ombres.
            City.CityLighting.Setup(light, camera);

            // Carte : la ville vue du ciel, pour la mini-carte et la carte plein écran.
            Texture2D map = City.MapBaker.Bake(MapTexturePath, City.MapBaker.MapExtent);
            var mapHud = Object.FindFirstObjectByType<MapHud>(FindObjectsInactive.Include);
            if (mapHud != null)
                City.MapBaker.Assign(mapHud, map);

            // Ajouter un NetworkManager peut déclencher un rafraîchissement d'assets côté FishNet,
            // qui invalide les références chargées plus tôt : on charge la config au dernier moment.
            var config = AssetDatabase.LoadAssetAtPath<NetworkConfig>(ConfigPath);
            SetProperty(bootstrap, "_config", config);
            if (bootstrap.Config == null)
                Debug.LogError("[Setup] Impossible d'assigner la NetworkConfig sur NetworkBootstrap.");
            else
            {
                // Point d'apparition : la place du Plateau de la disposition d'Abidjan.
                var spawn = new SerializedObject(config);
                spawn.FindProperty("_spawnCenter").vector3Value = IvoryState.Gameplay.World.CityLayout.PlazaCenter + Vector3.up * 0.85f;
                spawn.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
            }

            // Même précaution pour la condition d'observation (appliquée par défaut à tous les objets).
            var observers = new SerializedObject(observerManager);
            SerializedProperty conditions = FindRequired(observers, "_defaultConditions");
            if (conditions != null)
            {
                // Grille = limite de distance ; plus proches voisins = limite de nombre (foules).
                conditions.arraySize = 2;
                conditions.GetArrayElementAtIndex(0).objectReferenceValue = GetOrCreateGridCondition();
                conditions.GetArrayElementAtIndex(1).objectReferenceValue =
                    GetOrCreateCondition<NearestPlayersCondition>(NearestPlayersConditionPath);
                observers.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.SaveScene(scene, BootstrapScenePath);

            // Reflet du ciel (vitres, lagune) : nécessite une scène déjà enregistrée.
            City.CityLighting.BakeEnvironment();
            EditorSceneManager.SaveScene(scene, BootstrapScenePath);

            // Bootstrap en scène 0 : c'est la première chargée par le client et le serveur.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapScenePath, true) };
        }

        /// <summary>
        /// HUD tactile en Screen Space - Overlay : joystick flottant (masqué tant qu'aucun doigt
        /// n'est posé) et bouton de saut. Aucun EventSystem : les doigts sont lus directement.
        /// </summary>
        private static MobilePlayerControls CreateHud()
        {
            var hud = new GameObject("HUD", typeof(RectTransform));
            var canvas = hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = hud.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Raycaster + EventSystem : nécessaires aux boutons de menu (chat). Le joystick et la caméra
            // continuent de lire les doigts directement ; les zones de menu leur sont signalées.
            hud.AddComponent<GraphicRaycaster>();
            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            // Joystick : socle sombre cerclé d'or, manette dorée (anneau = disque doré derrière un disque sombre).
            RectTransform joystickBase = CreateHudImage("JoystickBase", hud.transform, circle, new Color(0.85f, 0.68f, 0.30f, 0.55f));
            RectTransform joystickInner = CreateHudImage("Fond", joystickBase, circle, new Color(0.05f, 0.06f, 0.09f, 0.55f));
            Stretch(joystickInner, 5f);
            RectTransform joystickKnob = CreateHudImage("JoystickKnob", hud.transform, circle, new Color(0.95f, 0.78f, 0.38f, 0.92f));
            RectTransform knobCore = CreateHudImage("Coeur", joystickKnob, circle, new Color(0.10f, 0.10f, 0.12f, 0.55f));
            knobCore.anchorMin = knobCore.anchorMax = knobCore.pivot = new Vector2(0.5f, 0.5f);
            knobCore.sizeDelta = new Vector2(26f, 26f);

            // Saut : disque sombre cerclé d'or, flèche vers le haut et libellé.
            RectTransform jumpButton = CreateHudImage("JumpButton", hud.transform, circle, new Color(0.85f, 0.68f, 0.30f, 0.85f));
            jumpButton.anchorMin = jumpButton.anchorMax = new Vector2(1f, 0f);
            jumpButton.pivot = new Vector2(0.5f, 0.5f);
            jumpButton.sizeDelta = new Vector2(190f, 190f);
            jumpButton.anchoredPosition = new Vector2(-200f, 200f);
            RectTransform jumpInner = CreateHudImage("Fond", jumpButton, circle, new Color(0.05f, 0.06f, 0.09f, 0.80f));
            Stretch(jumpInner, 6f);
            RectTransform jumpArrow = CreateHudImage("Fleche", jumpButton, GetOrCreateArrowSprite(), new Color(0.95f, 0.78f, 0.38f, 1f));
            jumpArrow.anchorMin = jumpArrow.anchorMax = jumpArrow.pivot = new Vector2(0.5f, 0.5f);
            jumpArrow.sizeDelta = new Vector2(70f, 70f);
            jumpArrow.anchoredPosition = new Vector2(0f, 14f);
            Text jumpLabel = CreateHudText("Libelle", jumpButton, 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f));
            var jumpLabelRect = (RectTransform)jumpLabel.transform;
            jumpLabelRect.anchorMin = jumpLabelRect.anchorMax = jumpLabelRect.pivot = new Vector2(0.5f, 0.5f);
            jumpLabelRect.sizeDelta = new Vector2(150f, 30f);
            jumpLabelRect.anchoredPosition = new Vector2(0f, -42f);
            jumpLabel.text = "SAUT";

            // Micro de proximité : parler aux joueurs autour de soi (appui pour activer / couper).
            RectTransform mic = CreateHudImage("MicButton", hud.transform, circle, new Color(0.85f, 0.68f, 0.30f, 0.85f));
            mic.anchorMin = mic.anchorMax = new Vector2(1f, 0f);
            mic.pivot = new Vector2(0.5f, 0.5f);
            mic.sizeDelta = new Vector2(120f, 120f);
            mic.anchoredPosition = new Vector2(-400f, 140f);
            RectTransform micInner = CreateHudImage("Fond", mic, circle, new Color(0.05f, 0.06f, 0.09f, 0.80f));
            Stretch(micInner, 5f);
            micInner.GetComponent<Image>().raycastTarget = true;
            micInner.gameObject.AddComponent<ProximityMicButton>();

            // Frapper : disque sombre cerclé de rouge (visible seulement quand un joueur est à portée).
            RectTransform punch = CreateHudImage("PunchButton", hud.transform, circle, new Color(0.80f, 0.20f, 0.15f, 0.90f));
            punch.anchorMin = punch.anchorMax = new Vector2(1f, 0f);
            punch.pivot = new Vector2(0.5f, 0.5f);
            punch.sizeDelta = new Vector2(140f, 140f);
            punch.anchoredPosition = new Vector2(-570f, 190f);
            RectTransform punchInner = CreateHudImage("Fond", punch, circle, new Color(0.05f, 0.06f, 0.09f, 0.80f));
            Stretch(punchInner, 6f);
            punchInner.GetComponent<Image>().raycastTarget = true;
            punchInner.gameObject.AddComponent<PunchButton>();
            Text punchLabel = CreateHudText("Libelle", punch, 22, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.8f, 1f));
            var punchLabelRect = (RectTransform)punchLabel.transform;
            punchLabelRect.anchorMin = punchLabelRect.anchorMax = punchLabelRect.pivot = new Vector2(0.5f, 0.5f);
            punchLabelRect.sizeDelta = new Vector2(130f, 30f);
            punchLabel.text = "FRAPPER";
            punchLabel.raycastTarget = false;

            // Voile rouge (coup reçu) et bandeau d'état (zone protégée, soins à l'hôpital).
            RectTransform flash = CreateHudImage("CombatFlash", hud.transform, null, new Color(0.8f, 0f, 0f, 0f));
            Stretch(flash, 0f);
            Text combatStatus = CreateHudText("CombatStatus", hud.transform, 26, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f, 1f));
            combatStatus.gameObject.AddComponent<Outline>().effectColor = Color.black;
            var combatStatusRect = (RectTransform)combatStatus.transform;
            combatStatusRect.anchorMin = combatStatusRect.anchorMax = combatStatusRect.pivot = new Vector2(0.5f, 1f);
            combatStatusRect.sizeDelta = new Vector2(700f, 40f);
            combatStatusRect.anchoredPosition = new Vector2(0f, -150f);
            // Racket : au-dessus de FRAPPER, visible avec une machette ou un couteau dans le sac.
            RectTransform rob = CreateHudImage("RobButton", hud.transform, circle, new Color(0.85f, 0.55f, 0.10f, 0.90f));
            rob.anchorMin = rob.anchorMax = new Vector2(1f, 0f);
            rob.pivot = new Vector2(0.5f, 0.5f);
            rob.sizeDelta = new Vector2(120f, 120f);
            rob.anchoredPosition = new Vector2(-570f, 360f);
            RectTransform robInner = CreateHudImage("Fond", rob, circle, new Color(0.05f, 0.06f, 0.09f, 0.80f));
            Stretch(robInner, 6f);
            robInner.GetComponent<Image>().raycastTarget = true;
            robInner.gameObject.AddComponent<RobButton>();
            Text robLabel = CreateHudText("Libelle", rob, 20, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.7f, 1f));
            var robLabelRect = (RectTransform)robLabel.transform;
            robLabelRect.anchorMin = robLabelRect.anchorMax = robLabelRect.pivot = new Vector2(0.5f, 0.5f);
            robLabelRect.sizeDelta = new Vector2(110f, 30f);
            robLabel.text = "RACKET";
            robLabel.raycastTarget = false;

            hud.gameObject.AddComponent<CombatHud>().EditorSetup(punch, flash.GetComponent<Image>(), combatStatus, rob);
            hud.gameObject.AddComponent<TutorialHud>();

            // --- Bouton d'action contextuel (au-dessus du saut) ---
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            RectTransform interactButton = CreateHudImage("InteractButton", hud.transform, rounded, new Color(0.05f, 0.06f, 0.09f, 0.82f));
            interactButton.GetComponent<Image>().type = Image.Type.Sliced;
            interactButton.gameObject.AddComponent<Outline>().effectColor = new Color(0.85f, 0.68f, 0.30f, 0.9f);
            interactButton.anchorMin = interactButton.anchorMax = new Vector2(1f, 0f);
            interactButton.pivot = new Vector2(1f, 0.5f);
            interactButton.sizeDelta = new Vector2(560f, 110f);
            interactButton.anchoredPosition = new Vector2(-60f, 400f);
            Text interactLabel = CreateHudText("Label", interactButton, 34, TextAnchor.MiddleCenter, Color.white);
            Stretch((RectTransform)interactLabel.transform, 16f);

            // --- Statut : argent et satiété (en haut à droite) ---
            // Colonne de droite, de haut en bas : carte carrée (300 px, voir CreateMapUi), puis santé,
            // soif et faim, puis l'argent et l'heure.
            // Jauges : libellé au-dessus, piste en pastille, remplissage arrondi avec le pourcentage.
            // Sous la carte (220 px) : jauges, puis l'heure.
            Image healthFill = CreateNeedBar(hud.transform, rounded, "HealthBar", "SANTÉ", -258f, new Color(0.89f, 0.27f, 0.36f));
            Image satietyFill = CreateNeedBar(hud.transform, rounded, "HungerBar", "FAIM", -296f, new Color(0.93f, 0.64f, 0.29f));
            Image hydrationFill = CreateNeedBar(hud.transform, rounded, "ThirstBar", "SOIF", -334f, new Color(0.49f, 0.67f, 0.87f));

            // --- Argent : en haut à gauche, cadre doré (liseré, fond vitré sombre, pièce) ---
            RectTransform purse = CreateHudImage("Bourse", hud.transform, rounded, new Color(0.85f, 0.68f, 0.30f, 0.95f));
            purse.GetComponent<Image>().type = Image.Type.Sliced;
            purse.anchorMin = purse.anchorMax = purse.pivot = new Vector2(0f, 1f);
            purse.sizeDelta = new Vector2(300f, 86f);
            purse.anchoredPosition = new Vector2(24f, -24f);
            RectTransform purseInner = CreateHudImage("Fond", purse, rounded, new Color(0.05f, 0.06f, 0.09f, 0.88f));
            purseInner.GetComponent<Image>().type = Image.Type.Sliced;
            Stretch(purseInner, 3f);
            RectTransform coin = CreateHudImage("Piece", purseInner, circle, new Color(0.95f, 0.76f, 0.30f, 1f));
            coin.anchorMin = coin.anchorMax = coin.pivot = new Vector2(0f, 0.5f);
            coin.sizeDelta = new Vector2(52f, 52f);
            coin.anchoredPosition = new Vector2(14f, 0f);
            RectTransform coinCore = CreateHudImage("Coeur", coin, circle, new Color(0.80f, 0.58f, 0.18f, 1f));
            Stretch(coinCore, 7f);
            Text coinLetter = CreateHudText("F", coin, 26, TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.7f));
            Stretch((RectTransform)coinLetter.transform, 0f);
            coinLetter.text = "F";
            Text currency = CreateHudText("Devise", purseInner, 15, TextAnchor.UpperLeft, new Color(0.85f, 0.68f, 0.30f, 1f));
            var currencyRect = (RectTransform)currency.transform;
            currencyRect.anchorMin = currencyRect.anchorMax = currencyRect.pivot = new Vector2(0f, 1f);
            currencyRect.sizeDelta = new Vector2(200f, 20f);
            currencyRect.anchoredPosition = new Vector2(80f, -8f);
            currency.text = "MON ARGENT";
            Text money = CreateHudText("Money", purseInner, 34, TextAnchor.MiddleLeft, new Color(1f, 0.92f, 0.70f));
            var moneyRect = (RectTransform)money.transform;
            moneyRect.anchorMin = moneyRect.anchorMax = moneyRect.pivot = new Vector2(0f, 0.5f);
            moneyRect.sizeDelta = new Vector2(205f, 44f);
            moneyRect.anchoredPosition = new Vector2(80f, -8f);
            money.resizeTextForBestFit = true;
            money.resizeTextMinSize = 20;
            money.resizeTextMaxSize = 34;

            // --- Messages du serveur et mission (en haut au centre) ---
            Text toast = CreateHudText("Toast", hud.transform, 36, TextAnchor.MiddleCenter, Color.white);
            var toastRect = (RectTransform)toast.transform;
            toastRect.anchorMin = toastRect.anchorMax = toastRect.pivot = new Vector2(0.5f, 1f);
            toastRect.sizeDelta = new Vector2(1200f, 60f);
            toastRect.anchoredPosition = new Vector2(0f, -260f);
            toast.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

            Text delivery = CreateHudText("Delivery", hud.transform, 32, TextAnchor.MiddleCenter, new Color(0.5f, 1f, 0.6f));
            var deliveryRect = (RectTransform)delivery.transform;
            deliveryRect.anchorMin = deliveryRect.anchorMax = deliveryRect.pivot = new Vector2(0.5f, 1f);
            deliveryRect.sizeDelta = new Vector2(1200f, 50f);
            deliveryRect.anchoredPosition = new Vector2(0f, -200f);
            delivery.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

            var controls = hud.AddComponent<MobilePlayerControls>();
            SetProperty(controls, "_jumpButton", jumpButton);
            SetProperty(controls, "_interactButton", interactButton);

            var joystickView = hud.AddComponent<VirtualJoystickView>();
            SetProperty(joystickView, "_controls", controls);
            SetProperty(joystickView, "_canvas", canvas);
            SetProperty(joystickView, "_base", joystickBase);
            SetProperty(joystickView, "_knob", joystickKnob);

            CreateChatUi(hud.transform, rounded);
            CreateCompass(hud.transform);
            CreateMapUi(hud.transform, rounded, circle);
            CreateNameTags(hud.transform);
            CreateNameEntryPanel(hud.transform, rounded);
            CreateTransferUi(hud.transform, rounded);

            // Téléphone : l'interface se construit en code au démarrage (PhoneHud).
            var phone = new GameObject("Phone", typeof(RectTransform));
            phone.transform.SetParent(hud.transform, false);
            Stretch((RectTransform)phone.transform, 0f);
            SetProperty(phone.AddComponent<PhoneHud>(), "_rounded", rounded);

            var playerHud = hud.AddComponent<PlayerHud>();
            SetProperty(playerHud, "_moneyText", money);

            // Horloge du jeu, sous la jauge de satiété.
            Text clock = CreateHudText("Clock", hud.transform, 32, TextAnchor.UpperRight, Color.white);
            clock.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            var clockRect = (RectTransform)clock.transform;
            clockRect.anchorMin = clockRect.anchorMax = clockRect.pivot = new Vector2(1f, 1f);
            clockRect.sizeDelta = new Vector2(200f, 44f);
            clockRect.anchoredPosition = new Vector2(-24f, -376f);
            SetProperty(playerHud, "_clockText", clock);
            SetProperty(playerHud, "_satietyFill", satietyFill);
            SetProperty(playerHud, "_hydrationFill", hydrationFill);
            SetProperty(playerHud, "_healthFill", healthFill);
            SetProperty(playerHud, "_interactButton", interactButton.gameObject);
            SetProperty(playerHud, "_interactLabel", interactLabel);
            SetProperty(playerHud, "_toastText", toast);
            SetProperty(playerHud, "_deliveryText", delivery);
            SetProperty(playerHud, "_beaconMaterial", GetOrCreateMaterial("M_Beacon", new Color(1f, 0.85f, 0.1f), emissive: true));
            hud.AddComponent<DrivingHud>();

            return controls;
        }

        /// <summary>
        /// Chat : bouton (en haut, à droite du panneau de dev), panneau de phrases rapides + saisie libre,
        /// historique (à gauche) et bulles au-dessus des têtes.
        /// </summary>
        private static void CreateChatUi(Transform hud, Sprite rounded)
        {
            var root = new GameObject("Chat", typeof(RectTransform));
            root.transform.SetParent(hud, false);
            Stretch((RectTransform)root.transform, 0f);

            Button chatButton = CreateButton("ChatButton", root.transform, rounded, "Chat", 30);
            var chatRect = (RectTransform)chatButton.transform;
            chatRect.anchorMin = chatRect.anchorMax = chatRect.pivot = new Vector2(0f, 1f);
            chatRect.sizeDelta = new Vector2(170f, 80f);
            chatRect.anchoredPosition = new Vector2(330f, -30f);

            RectTransform panel = CreateHudImage("Panel", root.transform, rounded, new Color(0f, 0f, 0f, 0.7f));
            panel.GetComponent<Image>().type = Image.Type.Sliced;
            panel.GetComponent<Image>().raycastTarget = true; // bloque les clics « à travers » le panneau
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            panel.sizeDelta = new Vector2(620f, 640f);
            panel.anchoredPosition = new Vector2(330f, -125f);

            var phraseButtons = new Button[ChatHud.QuickPhrases.Length];
            for (int i = 0; i < phraseButtons.Length; i++)
            {
                Button button = CreateButton("Phrase_" + i, panel, rounded, "…", 28);
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(580f, 58f);
                rect.anchoredPosition = new Vector2(0f, -20f - i * 66f);
                phraseButtons[i] = button;
            }

            InputField input = CreateInputField(panel, rounded);
            var inputRect = (RectTransform)input.transform;
            inputRect.anchorMin = inputRect.anchorMax = inputRect.pivot = new Vector2(0f, 0f);
            inputRect.sizeDelta = new Vector2(430f, 64f);
            inputRect.anchoredPosition = new Vector2(20f, 20f);

            Button send = CreateButton("Send", panel, rounded, "Envoyer", 26);
            var sendRect = (RectTransform)send.transform;
            sendRect.anchorMin = sendRect.anchorMax = sendRect.pivot = new Vector2(1f, 0f);
            sendRect.sizeDelta = new Vector2(140f, 64f);
            sendRect.anchoredPosition = new Vector2(-20f, 20f);

            // Barre du haut : les 3 derniers messages, sous la boussole (décalée à droite des boutons
            // Chat et Payer, à gauche de l'argent).
            RectTransform logBar = CreateHudImage("LogBar", root.transform, rounded, new Color(0f, 0f, 0f, 0.45f));
            logBar.GetComponent<Image>().type = Image.Type.Sliced;
            logBar.anchorMin = logBar.anchorMax = logBar.pivot = new Vector2(0.5f, 1f);
            logBar.sizeDelta = new Vector2(620f, 112f);
            logBar.anchoredPosition = new Vector2(0f, -66f); // centrée sous la boussole

            Text log = CreateHudText("Log", logBar, 23, TextAnchor.LowerLeft, Color.white);
            log.horizontalOverflow = HorizontalWrapMode.Wrap;
            log.verticalOverflow = VerticalWrapMode.Truncate;
            log.supportRichText = false; // jamais de balises venant d'autres joueurs
            log.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            Stretch((RectTransform)log.transform, 10f);

            var bubbles = new Text[6];
            for (int i = 0; i < bubbles.Length; i++)
            {
                Text bubble = CreateHudText("Bubble_" + i, root.transform, 28, TextAnchor.LowerCenter, Color.white);
                bubble.supportRichText = false;
                bubble.horizontalOverflow = HorizontalWrapMode.Wrap;
                bubble.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                var rect = (RectTransform)bubble.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(420f, 120f);
                bubbles[i] = bubble;
            }

            var chatHud = root.AddComponent<ChatHud>();
            var so = new SerializedObject(chatHud);
            so.FindProperty("_chatButton").objectReferenceValue = chatButton;
            so.FindProperty("_panel").objectReferenceValue = panel;
            so.FindProperty("_input").objectReferenceValue = input;
            so.FindProperty("_sendButton").objectReferenceValue = send;
            so.FindProperty("_logText").objectReferenceValue = log;
            so.FindProperty("_logBar").objectReferenceValue = logBar.gameObject;
            SetArray(so.FindProperty("_phraseButtons"), phraseButtons);
            SetArray(so.FindProperty("_bubbles"), bubbles);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Jauge de la colonne de droite, sous la carte : libellé en majuscules au-dessus, piste en
        /// pastille claire, remplissage arrondi (sa largeur suit la valeur : l'extrémité reste ronde) et
        /// pourcentage à l'intérieur, à droite. PlayerHud règle la largeur et le texte.
        /// </summary>
        private static Image CreateNeedBar(Transform hud, Sprite rounded, string name, string label, float y, Color color)
        {
            var group = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            group.SetParent(hud, false);
            group.anchorMin = group.anchorMax = group.pivot = new Vector2(1f, 1f);
            group.sizeDelta = new Vector2(300f, 36f);
            group.anchoredPosition = new Vector2(-24f, y);

            Text title = CreateHudText("Label", group, 15, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.9f));
            title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(-8f, 16f);
            titleRect.anchoredPosition = new Vector2(8f, 0f);
            title.text = label;

            RectTransform track = CreateHudImage("Piste", group, rounded, new Color(0.92f, 0.92f, 0.92f, 0.45f));
            track.GetComponent<Image>().type = Image.Type.Sliced;
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.sizeDelta = new Vector2(0f, 20f);
            track.anchoredPosition = Vector2.zero;

            RectTransform fillRect = CreateHudImage("Fill", track, rounded, color);
            var fill = fillRect.GetComponent<Image>();
            fill.type = Image.Type.Sliced;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);

            Text value = CreateHudText("Pourcent", fillRect, 14, TextAnchor.MiddleRight, Color.white);
            Stretch((RectTransform)value.transform, 0f);
            ((RectTransform)value.transform).offsetMax = new Vector2(-8f, 0f);
            value.text = "100%";
            return fill;
        }

        /// <summary>Boussole : bandeau en haut, au-dessus de la barre de chat.</summary>
        private static void CreateCompass(Transform hud)
        {
            var strip = new GameObject("Compass", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            strip.SetParent(hud, false);
            strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0.5f, 1f);
            strip.sizeDelta = new Vector2(440f, 44f);
            strip.anchoredPosition = new Vector2(0f, -14f); // centré

            // Repère central (cap actuel).
            RectTransform tick = CreateHudImage("Tick", strip, null, new Color(1f, 1f, 1f, 0.9f));
            tick.anchorMin = tick.anchorMax = tick.pivot = new Vector2(0.5f, 0f);
            tick.sizeDelta = new Vector2(3f, 10f);
            tick.anchoredPosition = Vector2.zero;

            Text template = CreateHudText("Label", strip, 30, TextAnchor.MiddleCenter, Color.white);
            template.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            var templateRect = (RectTransform)template.transform;
            templateRect.anchorMin = templateRect.anchorMax = templateRect.pivot = new Vector2(0.5f, 0.5f);
            templateRect.sizeDelta = new Vector2(60f, 40f);

            var compass = strip.gameObject.AddComponent<CompassHud>();
            SetProperty(compass, "_strip", strip);
            SetProperty(compass, "_labelTemplate", template);
        }

        public const string MapTexturePath = ProjectRoot + "/Art/Generated/T_CityMap.png";
        private const string ArrowSpritePath = ProjectRoot + "/Art/Generated/T_MapArrow.png";

        /// <summary>
        /// Mini-carte carrée dans l'angle en haut à droite et carte plein écran ouverte d'un tap.
        /// L'image de la ville est rendue plus tard dans la génération (<see cref="City.MapBaker"/>).
        /// </summary>
        private static void CreateMapUi(Transform hud, Sprite rounded, Sprite circle)
        {
            Sprite arrow = GetOrCreateArrowSprite();
            var root = new GameObject("Map", typeof(RectTransform));
            root.transform.SetParent(hud, false);
            Stretch((RectTransform)root.transform, 0f);

            // --- Mini-carte ---
            // Carte carrée (220 px), dans l'angle en haut à droite, liseré doré comme le cadre de l'argent.
            RectTransform ring = CreateHudImage("MinimapFrame", root.transform, rounded, new Color(0.85f, 0.68f, 0.30f, 0.95f));
            ring.GetComponent<Image>().type = Image.Type.Sliced;
            ring.anchorMin = ring.anchorMax = ring.pivot = new Vector2(1f, 1f);
            ring.sizeDelta = new Vector2(220f, 220f);
            ring.anchoredPosition = new Vector2(-24f, -24f);

            RectTransform minimap = CreateHudImage("Minimap", ring, null, Color.white);
            minimap.anchorMin = minimap.anchorMax = minimap.pivot = new Vector2(0.5f, 0.5f);
            minimap.sizeDelta = new Vector2(212f, 212f);
            minimap.anchoredPosition = Vector2.zero;
            minimap.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            minimap.GetComponent<Image>().raycastTarget = true;
            var openButton = minimap.gameObject.AddComponent<Button>();
            openButton.targetGraphic = minimap.GetComponent<Image>();

            RectTransform minimapMap = CreateRawImage("CityMap", minimap);
            RectTransform target = CreateHudImage("Target", minimap, circle, new Color(1f, 0.85f, 0.1f));
            target.anchorMin = target.anchorMax = target.pivot = new Vector2(0.5f, 0.5f);
            target.sizeDelta = new Vector2(22f, 22f);
            Text north = CreateHudText("North", minimap, 24, TextAnchor.MiddleCenter, new Color(1f, 0.45f, 0.3f));
            north.text = "N";
            north.gameObject.AddComponent<Outline>().effectColor = Color.black;
            var northRect = (RectTransform)north.transform;
            northRect.anchorMin = northRect.anchorMax = northRect.pivot = new Vector2(0.5f, 0.5f);
            northRect.sizeDelta = new Vector2(30f, 30f);
            RectTransform player = CreateHudImage("Player", minimap, arrow, Color.white);
            player.anchorMin = player.anchorMax = player.pivot = new Vector2(0.5f, 0.5f);
            player.sizeDelta = new Vector2(30f, 30f);
            player.anchoredPosition = Vector2.zero;

            // --- Carte plein écran ---
            RectTransform full = CreateHudImage("FullMap", root.transform, null, new Color(0f, 0f, 0f, 0.85f));
            full.GetComponent<Image>().raycastTarget = true; // bloque les touches vers le jeu
            Stretch(full, 0f);
            RectTransform fullMap = CreateRawImage("CityMap", full);
            fullMap.sizeDelta = new Vector2(1000f, 1000f);
            fullMap.anchoredPosition = new Vector2(0f, -10f);
            RectTransform fullTarget = CreateHudImage("Target", fullMap, circle, new Color(1f, 0.85f, 0.1f));
            fullTarget.anchorMin = fullTarget.anchorMax = fullTarget.pivot = new Vector2(0.5f, 0.5f);
            fullTarget.sizeDelta = new Vector2(30f, 30f);
            RectTransform fullPlayer = CreateHudImage("Player", fullMap, arrow, Color.white);
            fullPlayer.anchorMin = fullPlayer.anchorMax = fullPlayer.pivot = new Vector2(0.5f, 0.5f);
            fullPlayer.sizeDelta = new Vector2(40f, 40f);
            Text title = CreateHudText("Title", full, 34, TextAnchor.UpperLeft, Color.white);
            title.text = "Ivory State  •  nord en haut\nPoint jaune : destination";
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(420f, 120f);
            titleRect.anchoredPosition = new Vector2(40f, -40f);
            Button close = CreateButton("Close", full, rounded, "Fermer", 30);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(220f, 90f);
            closeRect.anchoredPosition = new Vector2(-40f, -40f);

            var mapHud = root.AddComponent<MapHud>();
            SetProperty(mapHud, "_mapTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(MapTexturePath));
            SetProperty(mapHud, "_minimap", minimap);
            SetProperty(mapHud, "_minimapMap", minimapMap);
            SetProperty(mapHud, "_minimapTarget", target);
            SetProperty(mapHud, "_minimapNorth", northRect);
            SetProperty(mapHud, "_openButton", openButton);
            SetProperty(mapHud, "_fullPanel", full.gameObject);
            SetProperty(mapHud, "_fullMap", fullMap);
            SetProperty(mapHud, "_fullPlayer", fullPlayer);
            SetProperty(mapHud, "_fullTarget", fullTarget);
            SetProperty(mapHud, "_closeButton", close);
        }

        private static RectTransform CreateRawImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            go.GetComponent<RawImage>().raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        /// <summary>Flèche du joueur sur la carte (triangle blanc bordé de noir), générée une fois.</summary>
        private static Sprite GetOrCreateArrowSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
            if (existing != null)
                return existing;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var tip = new Vector2(32f, 60f);
            var left = new Vector2(8f, 6f);
            var right = new Vector2(56f, 6f);
            var notch = new Vector2(32f, 18f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right);
                    bool border = !inside && (InTriangle(p, tip + new Vector2(0f, 3f), left + new Vector2(-3f, -2f), notch - new Vector2(0f, 3f)) ||
                                              InTriangle(p, tip + new Vector2(0f, 3f), notch - new Vector2(0f, 3f), right + new Vector2(3f, -2f)));
                    texture.SetPixel(x, y, inside ? Color.white : border ? Color.black : Color.clear);
                }
            }
            texture.Apply();
            EnsureFolder(ProjectRoot + "/Art/Generated");
            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(ArrowSpritePath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(ArrowSpritePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(ArrowSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        private static void SetArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static Button CreateButton(string name, Transform parent, Sprite sprite, string label, int fontSize)
        {
            RectTransform rect = CreateHudImage(name, parent, sprite, new Color(0.15f, 0.15f, 0.18f, 0.9f));
            var image = rect.GetComponent<Image>();
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateHudText("Label", rect, fontSize, TextAnchor.MiddleCenter, Color.white);
            Stretch((RectTransform)text.transform, 8f);
            text.text = label;
            return button;
        }

        private static InputField CreateInputField(Transform parent, Sprite sprite)
        {
            RectTransform rect = CreateHudImage("Input", parent, sprite, new Color(1f, 1f, 1f, 0.95f));
            var image = rect.GetComponent<Image>();
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            Text text = CreateHudText("Text", rect, 26, TextAnchor.MiddleLeft, Color.black);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch((RectTransform)text.transform, 12f);

            Text placeholder = CreateHudText("Placeholder", rect, 26, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.45f));
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.text = "Écrire un message…";
            Stretch((RectTransform)placeholder.transform, 12f);

            var input = rect.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Text CreateHudText(string name, Transform parent, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>Matériau URP Lit uni (placeholder en attendant la direction artistique).</summary>
        private static Material GetOrCreateMaterial(string name, Color color, bool emissive = false)
        {
            string folder = ProjectRoot + "/Art/Materials";
            string path = folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EnsureFolder(folder);
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.5f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static RectTransform CreateHudImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false; // pas d'EventSystem : économise le raycast UI

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = new Vector2(200f, 200f);
            return rect;
        }

        #endregion

        #region Utilitaires

        private static void SetProperty(Object target, string path, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = FindRequired(so, path);
            if (property == null)
                return;

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetProperty(Object target, string path, bool value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = FindRequired(so, path);
            if (property == null)
                return;

            property.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetProperty(Object target, string path, int value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = FindRequired(so, path);
            if (property == null)
                return;

            property.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Donne un nom à une couche dans ProjectSettings/TagManager.asset (lisibilité dans l'éditeur).</summary>
        private static void EnsureLayerName(int index, string name)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets.Length == 0)
                return;

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = FindRequired(tagManager, "layers");
            if (layers == null || index >= layers.arraySize)
                return;

            SerializedProperty layer = layers.GetArrayElementAtIndex(index);
            if (layer.stringValue == name)
                return;

            layer.stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty FindRequired(SerializedObject so, string path)
        {
            SerializedProperty property = so.FindProperty(path);
            if (property == null)
                Debug.LogError("[Setup] Champ « " + path + " » introuvable sur " + so.targetObject.GetType().Name +
                               " (API FishNet modifiée ?).");
            return property;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        #endregion
    }
}
