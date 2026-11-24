#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_SERVER || IVORY_SERVER
using IvoryState.Gameplay.Input;
using UnityEngine;

namespace IvoryState.Gameplay.Bots
{
    /// <summary>
    /// « Cerveau » de bot de test : marche vers des points tirés au hasard dans un disque, en suivant
    /// les rues (NavMesh), court parfois, saute de temps en temps. Partagé par les bots serveur et les
    /// clients <c>-bot</c>. Absent des builds clients de production.
    /// <para>
    /// Interrogé une fois par tick par <c>PlayerMotor</c> : aucune allocation, aucun Update.
    /// Le déplacement est exprimé en repère monde (lacet de vue = 0, donc avant = +Z).
    /// </para>
    /// </summary>
    public sealed class WanderBotBrain : IMovementInputProvider
    {
        private const float ArrivalDistance = 2f;
        private const float SprintChance = 0.3f;
        private const float JumpChancePerQuery = 0.004f; // ≈ un saut toutes les 8 s à 30 Hz
        private const int StuckQueries = 90;             // ≈ 3 s sans avancer : nouvelle destination

        private readonly Transform _body;
        private readonly Vector3 _center;
        private readonly float _radius;
        private readonly System.Random _random;
        private readonly NavPathFollower _follower = new NavPathFollower();

        private bool _sprintToTarget;
        private bool _hasTarget;
        private Vector3 _lastPosition;
        private int _queriesWithoutProgress;

        public WanderBotBrain(Transform body, Vector3 center, float radius, int seed)
        {
            _body = body;
            _center = center;
            _radius = Mathf.Max(3f, radius);
            _random = new System.Random(seed);
        }

        public bool Sprint => _sprintToTarget;
        public float ViewYaw => 0f;

        public Vector2 Move
        {
            get
            {
                if (_body == null)
                    return Vector2.zero;

                Vector3 position = _body.position;

                // Première requête, arrivée ou blocage : nouvelle destination.
                if (!_hasTarget ||
                    _follower.RemainingDistance(position) < ArrivalDistance ||
                    _queriesWithoutProgress > StuckQueries)
                {
                    PickTarget(position);
                }

                _queriesWithoutProgress = (position - _lastPosition).sqrMagnitude < 0.0004f ? _queriesWithoutProgress + 1 : 0;
                _lastPosition = position;

                return _follower.GetDirection(position);
            }
        }

        public bool ConsumeJump()
        {
            return _random.NextDouble() < JumpChancePerQuery;
        }

        private void PickTarget(Vector3 from)
        {
            // Racine carrée : répartition uniforme sur la surface du disque, pas concentrée au centre.
            float distance = _radius * Mathf.Sqrt((float)_random.NextDouble());
            float angle = (float)(_random.NextDouble() * Mathf.PI * 2f);
            Vector3 target = _center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

            _follower.SetDestination(from, target);
            _sprintToTarget = _random.NextDouble() < SprintChance;
            _queriesWithoutProgress = 0;
            _hasTarget = true;
        }
    }
}
#endif
