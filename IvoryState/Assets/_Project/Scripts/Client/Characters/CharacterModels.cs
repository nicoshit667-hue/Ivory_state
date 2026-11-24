using UnityEngine;

namespace IvoryState.Client.Characters
{
    /// <summary>
    /// Catalogue des apparences de personnages (avatars Rocketbox préparés par l'éditeur : maillage
    /// réaliste, Animator marche / course / attente). Partagé par les joueurs et les passants.
    /// <para>
    /// Tenues : pour chaque avatar, le matériau du corps d'origine (tenue n° 0) et des variantes de
    /// couleur générées par l'éditeur, où seuls les vêtements changent (la peau est préservée).
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Ivory State/Character Models", fileName = "CharacterModels")]
    public sealed class CharacterModels : ScriptableObject
    {
        /// <summary>Variantes de tenue par avatar, en plus de la tenue d'origine.</summary>
        public const int OutfitVariants = 3;

        [SerializeField] private GameObject[] _models = new GameObject[0];
        [SerializeField] private Material[] _bodyMaterials = new Material[0];
        [SerializeField] private Material[] _outfitMaterials = new Material[0];

        public int Count => _models.Length;

        /// <summary>Apparence n° <paramref name="index"/> (modulo le nombre de modèles), ou null si vide.</summary>
        public GameObject Get(int index)
        {
            if (_models.Length == 0)
                return null;
            return _models[Wrap(index)];
        }

        private int Wrap(int index) => ((index % _models.Length) + _models.Length) % _models.Length;

        /// <summary>Vrai si l'avatar n° <paramref name="index"/> est une femme (d'après le nom du modèle).</summary>
        public bool IsFemale(int index)
        {
            GameObject model = Get(index);
            return model != null && model.name.Contains("Female");
        }

        /// <summary>
        /// Matériau du corps d'origine et matériau de la tenue choisie (0 = d'origine) pour un avatar.
        /// Renvoie faux s'il n'y a rien à remplacer.
        /// </summary>
        public bool TryGetOutfit(int model, int outfit, out Material original, out Material replacement)
        {
            original = null;
            replacement = null;
            if (_models.Length == 0 || outfit <= 0)
                return false;
            int m = Wrap(model);
            int slot = m * OutfitVariants + Mathf.Min(outfit, OutfitVariants) - 1;
            if (m >= _bodyMaterials.Length || slot >= _outfitMaterials.Length)
                return false;
            original = _bodyMaterials[m];
            replacement = _outfitMaterials[slot];
            return original != null && replacement != null;
        }

        /// <summary>Couleur indicative d'une tenue (pastilles de l'écran de création).</summary>
        public static Color OutfitSwatch(int outfit)
        {
            switch (outfit)
            {
                case 1: return new Color(0.20f, 0.55f, 0.35f);
                case 2: return new Color(0.25f, 0.35f, 0.75f);
                case 3: return new Color(0.15f, 0.15f, 0.17f);
                default: return new Color(0.85f, 0.75f, 0.55f);
            }
        }

#if UNITY_EDITOR
        public void EditorSetModels(GameObject[] models)
        {
            _models = models;
        }

        public void EditorSetOutfits(Material[] bodyMaterials, Material[] outfitMaterials)
        {
            _bodyMaterials = bodyMaterials;
            _outfitMaterials = outfitMaterials;
        }
#endif
    }
}
