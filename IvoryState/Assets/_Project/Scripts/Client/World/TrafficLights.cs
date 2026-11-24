using UnityEngine;

namespace IvoryState.Client.World
{
    /// <summary>
    /// Feux tricolores de la ville. Toutes les lampes « allumées » d'un même groupe (axe nord-sud ou
    /// est-ouest, couleur rouge / orange / vert) sont fusionnées en un seul maillage : changer de phase
    /// revient à activer ou masquer six objets, quel que soit le nombre de carrefours.
    /// <para>
    /// Cycle : vert 14 s, orange 3 s, rouge intégral 1 s, puis l'autre axe. La phase est calculée à
    /// partir de l'horloge (aucun réseau) : tous les feux d'un même client sont synchronisés.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrafficLights : MonoBehaviour
    {
        private const float Green = 14f, Amber = 3f, AllRed = 1f;
        private const float HalfCycle = Green + Amber + AllRed;

        // Ordre : rouge, orange, vert.
        [SerializeField] private Renderer[] _northSouth = new Renderer[3];
        [SerializeField] private Renderer[] _eastWest = new Renderer[3];

        private int _shownPhase = -1;

        public void EditorSetup(Renderer[] northSouth, Renderer[] eastWest)
        {
            _northSouth = northSouth;
            _eastWest = eastWest;
        }

        private void Update()
        {
            float t = Mathf.Repeat(Time.time, HalfCycle * 2f);
            bool northSouthTurn = t < HalfCycle;
            float local = northSouthTurn ? t : t - HalfCycle;
            int light = local < Green ? 2 : local < Green + Amber ? 1 : 0; // 2 vert, 1 orange, 0 rouge
            int phase = (northSouthTurn ? 0 : 3) + light;
            if (phase == _shownPhase)
                return;
            _shownPhase = phase;

            Apply(_northSouth, northSouthTurn ? light : 0);
            Apply(_eastWest, northSouthTurn ? 0 : light);
        }

        private static void Apply(Renderer[] group, int lit)
        {
            for (int i = 0; i < group.Length; i++)
            {
                if (group[i] != null)
                    group[i].enabled = i == lit;
            }
        }
    }
}
