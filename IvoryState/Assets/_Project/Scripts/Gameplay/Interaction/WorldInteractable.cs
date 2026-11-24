using System.Collections.Generic;
using IvoryState.Core.Diagnostics;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    public enum InteractionKind : byte
    {
        Shop = 0,
        JobBoard = 1,
        DeliveryPoint = 2,
        Store = 3,
        GbakaStation = 4,
        ClothingStore = 5,
        DrivingSchool = 6,
        Dealership = 7,
        BlackMarket = 8,
        CashPoint = 9,
        Commerce = 10,
        Door = 11,
    }

    /// <summary>
    /// Point d'interaction statique du monde : étal, dépôt, adresse de livraison…
    /// <para>
    /// Ce <b>n'est pas</b> un objet réseau. Le client et le serveur chargent la même scène, et
    /// chaque point y porte un identifiant stable (<see cref="Id"/>, attribué par l'outil de setup
    /// ou le générateur de ville). Le client envoie seulement « j'interagis avec n° 12 » ;
    /// le serveur utilise sa propre copie du point (prix, position) pour valider.
    /// Un client modifié ne peut donc changer ni les prix ni les règles.
    /// </para>
    /// <para>
    /// Coût réseau nul tant que personne n'interagit : une ville peut compter des milliers de commerces.
    /// </para>
    /// </summary>
    public abstract class WorldInteractable : MonoBehaviour
    {
        private const string LogTag = "Interaction";

        private static readonly Dictionary<int, WorldInteractable> s_byId = new Dictionary<int, WorldInteractable>();
        private static readonly List<WorldInteractable> s_all = new List<WorldInteractable>();

        [Tooltip("Identifiant unique dans la scène, identique côté client et serveur. Attribué par le setup.")]
        [SerializeField] private int _id = -1;

        [Tooltip("Distance d'interaction (m).")]
        [SerializeField, Range(0.5f, 6f)] private float _range = 2.5f;

        [Tooltip("Nom affiché au joueur (ex. « Garba de Tantie Awa »).")]
        [SerializeField] private string _displayName = "Point";

        public int Id => _id;
        public float Range => _range;
        public string DisplayName => _displayName;

        public abstract InteractionKind Kind { get; }

        /// <summary>Texte du bouton d'action (ex. « Acheter un garba — 1 000 FCFA »).</summary>
        public abstract string Prompt { get; }

        /// <summary>Tous les points chargés (lecture seule, ne pas modifier).</summary>
        public static IReadOnlyList<WorldInteractable> All => s_all;

        public static bool TryGet(int id, out WorldInteractable interactable)
        {
            return s_byId.TryGetValue(id, out interactable);
        }

        /// <summary>Utilisé par les outils d'édition pour attribuer les identifiants.</summary>
        public void EditorSetup(int id, string displayName)
        {
            _id = id;
            _displayName = displayName;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_byId.Clear();
            s_all.Clear();
        }

        protected virtual void OnEnable()
        {
            if (_id < 0)
            {
                GameLog.Error(LogTag, name + " n'a pas d'identifiant : relancer Ivory State › Setup.");
                return;
            }

            if (s_byId.TryGetValue(_id, out WorldInteractable other) && other != this)
            {
                GameLog.Error(LogTag, "Identifiant " + _id + " en double : " + name + " et " + other.name + ".");
                return;
            }

            s_byId[_id] = this;
            s_all.Add(this);
        }

        protected virtual void OnDisable()
        {
            if (s_byId.TryGetValue(_id, out WorldInteractable registered) && registered == this)
                s_byId.Remove(_id);
            s_all.Remove(this);
        }
    }
}
