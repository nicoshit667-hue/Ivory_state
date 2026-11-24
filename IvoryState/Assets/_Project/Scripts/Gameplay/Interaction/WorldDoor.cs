using System.Collections.Generic;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    public enum DoorMotion : byte
    {
        /// <summary>Battant : pivote autour de l'axe vertical de ses gonds.</summary>
        Hinged = 0,
        /// <summary>Coulissant : glisse le long du mur.</summary>
        Sliding = 1,
        /// <summary>Barrière levante : pivote vers le haut.</summary>
        Barrier = 2,
    }

    /// <summary>
    /// Porte ou portail qui s'ouvre et se ferme (bâtiments modélisés dans Blender, voir
    /// Editor/City/ArchitectureSetup). Le composant est posé sur une poignée au milieu du battant fermé ;
    /// <see cref="_leaf"/> est la partie mobile (maillage et colliders), animée entre deux poses locales.
    /// <para>
    /// L'état est décidé par le serveur (<see cref="DoorStates"/>), puis appliqué sur le serveur (colliders)
    /// et chez chaque client. Les portails des véhicules s'ouvrent aussi depuis une voiture (bouton du HUD
    /// de conduite, portée <see cref="VehicleRange"/>).
    /// </para>
    /// </summary>
    public sealed class WorldDoor : WorldInteractable
    {
        public const float VehicleRange = 14f;

        [SerializeField] private Transform _leaf;
        [SerializeField] private DoorMotion _motion;
        [SerializeField] private Vector3 _closedPosition, _openPosition;
        [SerializeField] private Quaternion _closedRotation = Quaternion.identity, _openRotation = Quaternion.identity;
        [Tooltip("Portail pour véhicules : s'ouvre depuis une voiture.")]
        [SerializeField] private bool _vehicleGate;
        [Tooltip("Ouvert au lancement du serveur (portes intérieures : pas de porte qui bloque inutilement).")]
        [SerializeField] private bool _openByDefault;
        [Tooltip("Durée d'ouverture (s).")]
        [SerializeField] private float _duration = 0.9f;

        private float _amount;      // 0 = fermé, 1 = ouvert (pose affichée)
        private bool _open;
        private bool _initialized;

        private static readonly List<WorldDoor> s_doors = new List<WorldDoor>();

        /// <summary>Toutes les portes chargées.</summary>
        public static IReadOnlyList<WorldDoor> Doors => s_doors;

        public bool IsOpen => _open;
        public bool VehicleGate => _vehicleGate;
        public bool OpenByDefault => _openByDefault;
        public override InteractionKind Kind => InteractionKind.Door;

        public override string Prompt => (_open ? "Fermer " : "Ouvrir ") + (_vehicleGate ? "le portail" : "la porte");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDoors() => s_doors.Clear();

        /// <summary>Outil d'édition : partie mobile, poses fermée et ouverte (locales à son parent).</summary>
        public void EditorSetupDoor(Transform leaf, DoorMotion motion, Vector3 closedPosition, Quaternion closedRotation,
            Vector3 openPosition, Quaternion openRotation, bool vehicleGate, bool openByDefault)
        {
            _leaf = leaf;
            _motion = motion;
            _closedPosition = closedPosition;
            _closedRotation = closedRotation;
            _openPosition = openPosition;
            _openRotation = openRotation;
            _vehicleGate = vehicleGate;
            _openByDefault = openByDefault;
            _duration = motion == DoorMotion.Sliding ? (vehicleGate ? 2.4f : 1.1f) : motion == DoorMotion.Barrier ? 1.6f : 0.9f;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            s_doors.Add(this);
            if (!_initialized)
            {
                _initialized = true;
                _open = _openByDefault;
                _amount = _open ? 1f : 0f;
                Apply();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            s_doors.Remove(this);
        }

        /// <summary>Ouvre ou ferme (animé, ou tout de suite à l'arrivée d'un joueur).</summary>
        public void SetOpen(bool open, bool instant = false)
        {
            _open = open;
            if (instant)
            {
                _amount = open ? 1f : 0f;
                Apply();
            }
        }

        private void Update()
        {
            float target = _open ? 1f : 0f;
            if (Mathf.Approximately(_amount, target))
                return;
            _amount = Mathf.MoveTowards(_amount, target, Time.deltaTime / Mathf.Max(0.1f, _duration));
            Apply();
        }

        private void Apply()
        {
            if (_leaf == null)
                return;
            float t = _amount * _amount * (3f - 2f * _amount);
            _leaf.localPosition = Vector3.LerpUnclamped(_closedPosition, _openPosition, t);
            _leaf.localRotation = Quaternion.Slerp(_closedRotation, _openRotation, t);
        }

        /// <summary>Portail pour véhicules le plus proche de <paramref name="position"/> (null au-delà de la portée).</summary>
        public static WorldDoor NearestVehicleGate(Vector3 position, float range = VehicleRange)
        {
            WorldDoor best = null;
            float bestSqr = range * range;
            for (int i = 0; i < s_doors.Count; i++)
            {
                WorldDoor door = s_doors[i];
                if (door == null || !door._vehicleGate)
                    continue;
                float sqr = (door.transform.position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = door;
                    bestSqr = sqr;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// État des portes côté serveur : ouvertures par rapport à l'état de départ, envoyées à chaque client
    /// (à son arrivée, puis à chaque changement) par <see cref="Player.PlayerInteractor"/>.
    /// </summary>
    public static class DoorStates
    {

        /// <summary>Serveur : portes dont l'état diffère de l'état de départ (pour un joueur qui arrive).</summary>
        public static void CollectChanged(List<int> ids, List<bool> states)
        {
            ids.Clear();
            states.Clear();
            IReadOnlyList<WorldDoor> doors = WorldDoor.Doors;
            for (int i = 0; i < doors.Count; i++)
            {
                if (doors[i] != null && doors[i].IsOpen != doors[i].OpenByDefault)
                {
                    ids.Add(doors[i].Id);
                    states.Add(doors[i].IsOpen);
                }
            }
        }
    }
}
