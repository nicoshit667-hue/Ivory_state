#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_SERVER || IVORY_SERVER
using UnityEngine;
using UnityEngine.AI;

namespace IvoryState.Gameplay.Bots
{
    /// <summary>
    /// Suit un chemin calculé sur le NavMesh de la ville et renvoie une direction de marche.
    /// <para>
    /// Ce n'est pas un NavMeshAgent : le personnage reste piloté par PlayerMotor, donc par la
    /// même simulation prédite que les vrais joueurs. Seule la direction (l'« entrée joystick »)
    /// vient du chemin. Aucune allocation après la construction : tampon de coins réutilisé.
    /// Si la scène n'a pas de NavMesh, le suivi se fait en ligne droite.
    /// </para>
    /// </summary>
    public sealed class NavPathFollower
    {
        private const float CornerReachedDistance = 1.2f;
        private const float SampleDistance = 8f;

        // Créé à la première utilisation : Unity interdit de construire un NavMeshPath pendant la
        // construction d'un MonoBehaviour (initialiseur de champ), où ce suiveur peut être déclaré.
        private NavMeshPath _path;
        private readonly Vector3[] _corners = new Vector3[64];
        private int _cornerCount;
        private int _cornerIndex;
        private Vector3 _destination;

        public Vector3 Destination => _destination;

        /// <summary>Coins du dernier chemin calculé (affichage du GPS).</summary>
        public int CornerCount => _cornerCount;

        public Vector3 GetCorner(int index) => _corners[index];

        /// <summary>
        /// Zone NavMesh des trottoirs et des places (blocs de la ville, voir CityGenerator). Les piétons
        /// la parcourent au coût normal ; un véhicule lui donne un coût élevé pour rester sur la chaussée
        /// au lieu de couper par un marché, une gare ou un parc.
        /// </summary>
        public const int SidewalkArea = 3;

        /// <summary>Coût des trottoirs et places pour ce suiveur (1 = piéton ; 20 conseillé pour un véhicule).</summary>
        public float SidewalkCost { get; set; } = 1f;

        /// <summary>
        /// Véhicule : distance (m) gardée entre les coins du chemin et le bord de la chaussée. Sans elle,
        /// le chemin le plus court longe la bordure et le véhicule roule à cheval sur le trottoir, contre
        /// les lampadaires. 0 = piéton (pas de décalage).
        /// </summary>
        public float EdgeClearance { get; set; }

        /// <summary>Calcule un chemin vers la destination. Renvoie faux si elle est inaccessible.</summary>
        public bool SetDestination(Vector3 from, Vector3 destination)
        {
            _destination = destination;
            _path ??= new NavMeshPath();

            var filter = new NavMeshQueryFilter { areaMask = NavMesh.AllAreas, agentTypeID = 0 };
            filter.SetAreaCost(SidewalkArea, SidewalkCost);

            if (NavMesh.SamplePosition(from, out NavMeshHit start, SampleDistance, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(destination, out NavMeshHit end, SampleDistance, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(start.position, end.position, filter, _path) &&
                _path.status == NavMeshPathStatus.PathComplete)
            {
                _cornerCount = _path.GetCornersNonAlloc(_corners);
                _cornerIndex = _cornerCount > 1 ? 1 : 0;
                if (EdgeClearance > 0f)
                    KeepCornersOffEdges();
                return true;
            }

            // Pas de NavMesh (ou destination isolée) : ligne droite.
            _corners[0] = destination;
            _cornerCount = 1;
            _cornerIndex = 0;
            return false;
        }

        /// <summary>
        /// Écarte les coins intermédiaires du bord de la chaussée (trottoirs exclus du masque : leur
        /// limite compte comme un bord). Le dernier coin (la destination) n'est pas déplacé.
        /// </summary>
        private void KeepCornersOffEdges()
        {
            int roadMask = NavMesh.AllAreas & ~(1 << SidewalkArea);
            for (int i = 1; i < _cornerCount - 1; i++)
            {
                Vector3 corner = _corners[i];
                if (!NavMesh.FindClosestEdge(corner, out NavMeshHit edge, roadMask))
                    continue;
                if (edge.distance >= EdgeClearance)
                    continue;
                Vector3 push = edge.normal;
                push.y = 0f;
                if (push.sqrMagnitude < 0.01f)
                    continue;
                _corners[i] = corner + push.normalized * (EdgeClearance - edge.distance);
            }
        }

        /// <summary>Direction de marche normalisée (repère monde XZ), ou zéro une fois arrivé.</summary>
        public Vector2 GetDirection(Vector3 position)
        {
            while (_cornerIndex < _cornerCount)
            {
                Vector3 toCorner = _corners[_cornerIndex] - position;
                toCorner.y = 0f;

                bool isLast = _cornerIndex == _cornerCount - 1;
                if (!isLast && toCorner.sqrMagnitude < CornerReachedDistance * CornerReachedDistance)
                {
                    _cornerIndex++;
                    continue;
                }

                return new Vector2(toCorner.x, toCorner.z).normalized;
            }

            return Vector2.zero;
        }

        public float RemainingDistance(Vector3 position)
        {
            Vector3 delta = _destination - position;
            delta.y = 0f;
            return delta.magnitude;
        }
    }
}
#endif
