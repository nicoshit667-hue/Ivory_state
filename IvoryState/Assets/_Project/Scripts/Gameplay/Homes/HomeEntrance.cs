using System.Collections.Generic;
using UnityEngine;

namespace IvoryState.Gameplay.Homes
{
    /// <summary>
    /// Entrée d'une résidence dans la rue : un cercle au sol. Le joueur qui y entre est conduit dans son
    /// logement (le serveur décide). Placé par le générateur de la ville.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeEntrance : MonoBehaviour
    {
        [SerializeField] private string _displayName = "Résidence";
        [SerializeField] private float _radius = 1.3f;
        [Tooltip("Où l'on ressort (dans le repère de l'entrée), hors du cercle.")]
        [SerializeField] private Vector3 _exitOffset = new Vector3(0f, 0f, 3.5f);
        [Tooltip("-1 : résidence des studios en colocation ; sinon index dans PropertyCatalog.Entrances.")]
        [SerializeField] private int _entranceIndex = -1;

        private static readonly List<HomeEntrance> s_all = new List<HomeEntrance>();

        public static IReadOnlyList<HomeEntrance> All => s_all;

        /// <summary>Entrée de la résidence des studios en colocation (null si absente).</summary>
        public static HomeEntrance Residence
        {
            get
            {
                for (int i = 0; i < s_all.Count; i++)
                {
                    if (s_all[i].IsResidence)
                        return s_all[i];
                }
                return null;
            }
        }

        public bool IsResidence => _entranceIndex < 0;

        /// <summary>Index de l'entrée dans le catalogue immobilier (-1 pour la résidence des studios).</summary>
        public int EntranceIndex => _entranceIndex;

        public string DisplayName => _displayName;
        public float Radius => _radius;
        public Vector3 ExitPosition => transform.TransformPoint(_exitOffset);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_all.Clear();
        }

        private void OnEnable()
        {
            if (!s_all.Contains(this))
                s_all.Add(this);
        }

        private void OnDisable()
        {
            s_all.Remove(this);
        }

        /// <summary>Le joueur est-il dans le cercle (à plat, tolérance de hauteur) ?</summary>
        public bool Contains(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            if (Mathf.Abs(offset.y) > 2f)
                return false;
            offset.y = 0f;
            return offset.sqrMagnitude <= _radius * _radius;
        }

#if UNITY_EDITOR
        public void EditorSetup(string displayName, float radius, Vector3 exitOffset, int entranceIndex = -1)
        {
            _displayName = displayName;
            _radius = radius;
            _exitOffset = exitOffset;
            _entranceIndex = entranceIndex;
        }
#endif
    }
}
