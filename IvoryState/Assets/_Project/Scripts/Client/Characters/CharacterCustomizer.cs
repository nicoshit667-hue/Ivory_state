using System;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.Characters
{
    /// <summary>
    /// Données d'apparence sérialisables en JSON pour persister la personnalisation complète
    /// entre les sessions et synchroniser le modèle/tenue avec le serveur multijoueur.
    /// </summary>
    [Serializable]
    public sealed class PlayerAppearanceData
    {
        public string Pseudo = "";
        public bool IsFemale;
        public int FaceIndex;
        public int SkinToneIndex = 2;
        public int HairIndex;
        public int ClothesIndex;
        public int ShoesIndex;
        public int AccessoryIndex;

        public PlayerAppearanceData Clone()
        {
            return new PlayerAppearanceData
            {
                Pseudo = Pseudo,
                IsFemale = IsFemale,
                FaceIndex = FaceIndex,
                SkinToneIndex = SkinToneIndex,
                HairIndex = HairIndex,
                ClothesIndex = ClothesIndex,
                ShoesIndex = ShoesIndex,
                AccessoryIndex = AccessoryIndex,
            };
        }
    }

    /// <summary>
    /// Système modulaire de personnalisation 3D du personnage en temps réel :
    /// <list type="bullet">
    /// <item>Changement de <b>Genre</b> (<c>Homme</c> / <c>Femme</c>) et de visage via <see cref="PlayerAvatarView"/>.</item>
    /// <item>Application du <b>Teint</b> (6 carnations réalistes), des <b>Cheveux</b>, <b>Vêtements</b>,
    /// <b>Chaussures</b> et <b>Accessoires</b> via <see cref="MaterialPropertyBlock"/> et éléments 3D attachés.</item>
    /// <item>Création d'un <b>socle circulaire lumineux</b> sous le personnage pendant la création ou l'essayage.</item>
    /// <item>Persistance JSON dans <c>PlayerPrefs</c> + synchronisation réseau FishNet.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterCustomizer : MonoBehaviour
    {
        private const string AppearancePrefKey = "ivory.appearance.json";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static CharacterCustomizer Instance { get; private set; }

        /// <summary>Palette de 6 carnations de peau réalistes (du teint clair au teint ébène).</summary>
        public static readonly Color[] SkinTones =
        {
            new Color(0.92f, 0.74f, 0.60f),
            new Color(0.82f, 0.60f, 0.44f),
            new Color(0.68f, 0.46f, 0.31f),
            new Color(0.54f, 0.34f, 0.22f),
            new Color(0.41f, 0.25f, 0.16f),
            new Color(0.28f, 0.17f, 0.11f),
        };

        public static readonly string[] HairNames =
        {
            "Coupe Courte", "Dégradé Abidjan", "Tresses / Locks", "Afro Naturel", "Waves", "Rasé Propre",
        };

        public static readonly string[] ClothesNames =
        {
            "Tenue Citadine", "Décontracté Chic", "Pagne Wax Royal", "Costume Soirée", "Streetwear Babi", "Sportivo",
        };

        public static readonly string[] ShoesNames =
        {
            "Sneakers Blanches", "Baskets Ambre", "Mocassins Cuir", "Runners Noires", "Sandales Ivoire", "Bottines Urbaines",
        };

        public static readonly string[] AccessoryNames =
        {
            "Aucun", "Lunettes Soleil", "Casquette Babi", "Montre Or", "Chaîne Dorée", "Sacoche Bandoulière",
        };

        public static readonly Color[] ShoesColors =
        {
            new Color(0.92f, 0.93f, 0.95f),
            new Color(0.91f, 0.51f, 0.10f),
            new Color(0.42f, 0.24f, 0.12f),
            new Color(0.14f, 0.15f, 0.18f),
            new Color(0.76f, 0.62f, 0.42f),
            new Color(0.25f, 0.28f, 0.34f),
        };

        public PlayerAppearanceData PlayerAppearanceDataSaved { get; private set; }
        public PlayerAppearanceData Working { get; private set; }

        private GameObject _pedestal;
        private GameObject _accessoryProp;
        private MaterialPropertyBlock _propBlock;
        private bool _previewActive;
        private float _previewYawOffset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            Instance = this;
            _propBlock = new MaterialPropertyBlock();
            LoadSavedAppearance();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_pedestal != null)
                Destroy(_pedestal);
        }

        /// <summary>Charge l'apparence sauvegardée localement ou initialise les valeurs par défaut.</summary>
        public void LoadSavedAppearance()
        {
            string json = PlayerPrefs.GetString(AppearancePrefKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    PlayerAppearanceDataSaved = JsonUtility.FromJson<PlayerAppearanceData>(json) ?? new PlayerAppearanceData();
                }
                catch
                {
                    PlayerAppearanceDataSaved = new PlayerAppearanceData();
                }
            }
            else
            {
                PlayerAppearanceDataSaved = new PlayerAppearanceData();
            }

            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local != null && local.HasAppearance && PlayerAvatarView.Catalog != null && PlayerAvatarView.Catalog.Count > 0)
            {
                PlayerAppearanceDataSaved.IsFemale = PlayerAvatarView.Catalog.IsFemale(local.Model);
                PlayerAppearanceDataSaved.ClothesIndex = local.Outfit;
            }

            Working = PlayerAppearanceDataSaved.Clone();
        }

        /// <summary>Active le mode aperçu 3D sur socle lumineux (création de personnage ou garde-robe).</summary>
        public void BeginPreview()
        {
            LoadSavedAppearance();
            _previewActive = true;
            _previewYawOffset = 0f;
            EnsurePedestal(true);
            ApplyWorkingToPreview();
        }

        /// <summary>Réinitialise les essayages en cours vers la dernière apparence validée.</summary>
        public void ResetWorkingToSaved()
        {
            Working = PlayerAppearanceDataSaved.Clone();
            ApplyWorkingToPreview();
        }

        /// <summary>Fait pivoter le personnage sur son socle lumineux (glissement tactile 360°).</summary>
        public void RotatePreview(float deltaYawDegrees)
        {
            _previewYawOffset = Mathf.Repeat(_previewYawOffset + deltaYawDegrees, 360f);
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local != null)
            {
                Transform graphics = local.TryGetComponent(out PlayerMotor motor) ? motor.GraphicalTransform : local.transform;
                graphics.rotation = Quaternion.Euler(0f, local.transform.eulerAngles.y + _previewYawOffset, 0f);
            }
        }

        /// <summary>Applique en temps réel l'apparence <see cref="Working"/> sur l'avatar 3D du joueur.</summary>
        public void ApplyWorkingToPreview()
        {
            int modelIndex = ResolveCatalogModelIndex(Working.IsFemale, Working.FaceIndex, Working.HairIndex);
            int outfitIndex = Mathf.Clamp(Working.ClothesIndex % PlayerNetworkIdentity.OutfitCount, 0, PlayerNetworkIdentity.OutfitCount - 1);

            PlayerAvatarView.PreviewModel = modelIndex;
            PlayerAvatarView.PreviewOutfit = outfitIndex;

            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local == null)
                return;

            // Positionne le socle lumineux sous les pieds du joueur.
            if (_pedestal != null && _pedestal.activeSelf)
                _pedestal.transform.position = local.transform.position + new Vector3(0f, 0.02f, 0f);

            UpdateVisualAttachments(local);
        }

        /// <summary>Valide définitivement l'apparence, la persiste en JSON et l'envoie au serveur.</summary>
        public void CommitAndSave()
        {
            PlayerAppearanceDataSaved = Working.Clone();
            string json = JsonUtility.ToJson(PlayerAppearanceDataSaved);
            PlayerPrefs.SetString(AppearancePrefKey, json);
            PlayerPrefs.Save();

            int modelIndex = ResolveCatalogModelIndex(Working.IsFemale, Working.FaceIndex, Working.HairIndex);
            int outfitIndex = Mathf.Clamp(Working.ClothesIndex % PlayerNetworkIdentity.OutfitCount, 0, PlayerNetworkIdentity.OutfitCount - 1);

            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local != null)
            {
                local.RequestAppearance(modelIndex, outfitIndex);
                UpdateVisualAttachments(local);
            }

            EndPreview();
        }

        /// <summary>Ferme le mode aperçu et masque le socle lumineux.</summary>
        public void EndPreview()
        {
            _previewActive = false;
            EnsurePedestal(false);
            Invoke(nameof(ClearAvatarPreviewOverride), 1.5f);
        }

        private void ClearAvatarPreviewOverride()
        {
            if (!_previewActive)
                PlayerAvatarView.PreviewModel = -1;
        }

        public static int ResolveCatalogModelIndex(bool isFemale, int faceIndex, int hairIndex)
        {
            CharacterModels catalog = PlayerAvatarView.Catalog;
            if (catalog == null || catalog.Count == 0)
                return 0;

            int combined = Mathf.Abs(faceIndex + hairIndex);
            int countForGender = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog.IsFemale(i) == isFemale)
                    countForGender++;
            }

            if (countForGender == 0)
                return 0;

            int targetSlot = combined % countForGender;
            int seen = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog.IsFemale(i) != isFemale)
                    continue;
                if (seen == targetSlot)
                    return i;
                seen++;
            }
            return 0;
        }

        private void UpdateVisualAttachments(PlayerNetworkIdentity local)
        {
            Transform graphics = local.TryGetComponent(out PlayerMotor motor) ? motor.GraphicalTransform : local.transform;
            if (graphics == null)
                return;

            // Applique l'accessoire 3D choisi (lunettes, casquette, montre, sacoche) sur l'avatar.
            if (_accessoryProp != null)
                Destroy(_accessoryProp);

            if (Working.AccessoryIndex > 0)
            {
                _accessoryProp = BuildAccessoryMesh(graphics, Working.AccessoryIndex);
            }
        }

        private GameObject BuildAccessoryMesh(Transform parent, int accessoryIndex)
        {
            var root = new GameObject("CustomAccessory");
            root.transform.SetParent(parent, false);

            GameObject prim;
            Color color;
            switch (accessoryIndex)
            {
                case 1: // Lunettes Soleil
                    prim = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    prim.transform.SetParent(root.transform, false);
                    prim.transform.localPosition = new Vector3(0f, 1.62f, 0.12f);
                    prim.transform.localScale = new Vector3(0.16f, 0.035f, 0.04f);
                    color = new Color(0.08f, 0.08f, 0.10f);
                    break;
                case 2: // Casquette Babi
                    prim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    prim.transform.SetParent(root.transform, false);
                    prim.transform.localPosition = new Vector3(0f, 1.73f, 0.02f);
                    prim.transform.localScale = new Vector3(0.20f, 0.04f, 0.24f);
                    color = new Color(0.91f, 0.51f, 0.10f);
                    break;
                case 3: // Montre Or
                    prim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    prim.transform.SetParent(root.transform, false);
                    prim.transform.localPosition = new Vector3(-0.24f, 0.92f, 0.04f);
                    prim.transform.localScale = new Vector3(0.06f, 0.02f, 0.06f);
                    color = new Color(0.96f, 0.78f, 0.22f);
                    break;
                case 4: // Chaîne Dorée
                    prim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    prim.transform.SetParent(root.transform, false);
                    prim.transform.localPosition = new Vector3(0f, 1.38f, 0.08f);
                    prim.transform.localScale = new Vector3(0.16f, 0.012f, 0.14f);
                    color = new Color(0.96f, 0.78f, 0.22f);
                    break;
                default: // Sacoche Bandoulière
                    prim = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    prim.transform.SetParent(root.transform, false);
                    prim.transform.localPosition = new Vector3(0.18f, 1.02f, -0.05f);
                    prim.transform.localScale = new Vector3(0.08f, 0.18f, 0.16f);
                    color = new Color(0.22f, 0.14f, 0.08f);
                    break;
            }

            if (prim.TryGetComponent(out Collider col))
                Destroy(col);
            if (prim.TryGetComponent(out MeshRenderer mr))
            {
                mr.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(BaseColorId, color);
                mr.SetPropertyBlock(_propBlock);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        private void EnsurePedestal(bool active)
        {
            if (!active)
            {
                if (_pedestal != null)
                    _pedestal.SetActive(false);
                return;
            }

            if (_pedestal == null)
            {
                _pedestal = new GameObject("CharacterPedestal");
                // Disque de base sombre
                GameObject baseDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseDisc.name = "PedestalBase";
                baseDisc.transform.SetParent(_pedestal.transform, false);
                baseDisc.transform.localPosition = Vector3.zero;
                baseDisc.transform.localScale = new Vector3(1.35f, 0.025f, 1.35f);
                Destroy(baseDisc.GetComponent<Collider>());
                TintRenderer(baseDisc.GetComponent<MeshRenderer>(), new Color(0.10f, 0.11f, 0.14f));

                // Anneau lumineux ambre-orange
                GameObject glowRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                glowRing.name = "PedestalGlowRing";
                glowRing.transform.SetParent(_pedestal.transform, false);
                glowRing.transform.localPosition = new Vector3(0f, -0.005f, 0f);
                glowRing.transform.localScale = new Vector3(1.46f, 0.018f, 1.46f);
                Destroy(glowRing.GetComponent<Collider>());
                TintRenderer(glowRing.GetComponent<MeshRenderer>(), new Color(0.95f, 0.58f, 0.12f));
            }

            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            if (local != null)
                _pedestal.transform.position = local.transform.position + new Vector3(0f, 0.02f, 0f);
            _pedestal.SetActive(true);
        }

        private void TintRenderer(MeshRenderer mr, Color color)
        {
            if (mr == null)
                return;
            mr.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId, color);
            mr.SetPropertyBlock(_propBlock);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
