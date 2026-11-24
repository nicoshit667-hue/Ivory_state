using System.Collections;
using System.Collections.Generic;
using IvoryState.Client.Settings;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace IvoryState.Client.World
{
    /// <summary>
    /// Passants d'ambiance (figurants) : la ville paraît habitée même avec peu de joueurs connectés.
    /// <para>
    /// <b>Entièrement local, zéro réseau :</b> chaque téléphone simule ses propres figurants, et
    /// seulement autour de la caméra. Ils naissent entre 25 et 70 m, disparaissent au-delà de 90 m et
    /// marchent sur le NavMesh vers des destinations aléatoires. Les joueurs les traversent (aucun
    /// collider), et le serveur ne les connaît pas. Ils n'ont aucun effet sur le jeu.
    /// </para>
    /// <para>
    /// Coût mobile maîtrisé : un pool fixe étalé au démarrage, aucune ombre portée, 8 matériaux de tenue
    /// (wax, pagnes) partagés et compatibles SRP Batcher, et un évitement léger.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientPedestrians : MonoBehaviour
    {
        private const float UpdateInterval = 0.5f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // 20 avatars animés : compromis entre une rue vivante et le coût du skinning sur un téléphone.
        [SerializeField, Range(0, 80)] private int _maxPedestrians = 20;
        [SerializeField] private float _spawnMinDistance = 25f;
        [SerializeField] private float _spawnMaxDistance = 70f;
        [SerializeField] private float _despawnDistance = 90f;
        [SerializeField] private float _wanderRadius = 40f;
        [SerializeField] private Material _material;
        [SerializeField] private Characters.CharacterModels _models;

        // Couleurs de tenues : wax et pagnes vifs.
        private static readonly Color[] Outfits =
        {
            new Color(0.90f, 0.35f, 0.10f), new Color(0.15f, 0.55f, 0.30f), new Color(0.95f, 0.75f, 0.10f),
            new Color(0.20f, 0.35f, 0.75f), new Color(0.75f, 0.15f, 0.35f), new Color(0.95f, 0.95f, 0.92f),
            new Color(0.45f, 0.25f, 0.55f), new Color(0.10f, 0.10f, 0.12f),
        };

        private readonly List<NavMeshAgent> _pool = new List<NavMeshAgent>();
        private readonly Dictionary<GameObject, Animator> _animators = new Dictionary<GameObject, Animator>();
        private Transform _camera;
        private float _nextUpdate;
        private System.Random _random;
        private Material[] _outfitMaterials;
        private bool _cliOverride;
        private bool _spawning;

        private void OnEnable()
        {
            GameSettings.Changed += OnSettingsChanged;
        }

        private void OnDisable()
        {
            GameSettings.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged()
        {
            if (_cliOverride || _outfitMaterials == null)
                return;
            int targetCount = Mathf.Clamp(GameSettings.PedestrianCount, 0, 80);
            _maxPedestrians = targetCount;
            for (int i = targetCount; i < _pool.Count; i++)
            {
                if (_pool[i] != null && _pool[i].gameObject.activeSelf)
                {
                    _pool[i].enabled = false;
                    _pool[i].gameObject.SetActive(false);
                }
            }
            if (_pool.Count < targetCount && !_spawning)
                StartCoroutine(SpawnPoolCoroutine());
        }

        private void Start()
        {
            // Rien à afficher sur un serveur headless.
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            _random = new System.Random();

            // Réglage de test de performance : -pedestrians n (voir PerfProbe).
            if (Debugging.PerfProbe.TryGetFloat(System.Environment.GetCommandLineArgs(), "-pedestrians", out float count))
            {
                _maxPedestrians = Mathf.Clamp(Mathf.RoundToInt(count), 0, 80);
                _cliOverride = true;
            }
            else
            {
                _maxPedestrians = Mathf.Clamp(GameSettings.PedestrianCount, 0, 80);
            }

            // Un matériau par couleur (et non un MaterialPropertyBlock) : compatible avec le SRP Batcher
            // d'URP, donc les passants de même couleur sont dessinés en lot.
            _outfitMaterials = new Material[Outfits.Length];
            for (int i = 0; i < Outfits.Length; i++)
            {
                _outfitMaterials[i] = _material != null ? new Material(_material) : null;
                if (_outfitMaterials[i] != null)
                    _outfitMaterials[i].SetColor(BaseColorId, Outfits[i]);
            }

            // Création étalée sur plusieurs frames pour éviter tout freeze au chargement.
            StartCoroutine(SpawnPoolCoroutine());
        }

        private IEnumerator SpawnPoolCoroutine()
        {
            _spawning = true;
            while (_pool.Count < _maxPedestrians)
            {
                int i = _pool.Count;
                _pool.Add(CreatePedestrian(i, _outfitMaterials[i % _outfitMaterials.Length]));
                yield return null; // 1 avatar par frame : aucune saccade perceptible
            }
            _spawning = false;
        }

        private NavMeshAgent CreatePedestrian(int index, Material material)
        {
            // L'agent est porté par un parent : le visuel (pivot aux pieds) en est un enfant.
            var root = new GameObject("Passant_" + index + "_Agent");
            root.transform.SetParent(transform, false);
            float speed = 1.1f + (float)_random.NextDouble() * 0.6f;

            GameObject model = _models != null ? _models.Get(index) : null;
            if (model != null)
            {
                // Avatar réaliste animé (marche). La vitesse est fixe : un seul réglage, à la création.
                GameObject avatar = Instantiate(model, root.transform, false);
                avatar.name = "Passant_" + index;
                var animator = avatar.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.cullingMode = AnimatorCullingMode.CullCompletely;
                    animator.keepAnimatorStateOnDisable = true; // garde vitesse et décalage entre deux apparitions
                    _animators[root] = animator;
                }

                foreach (Renderer r in avatar.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }
            else
            {
                // Repli : capsule colorée (catalogue de personnages absent).
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Passant_" + index;
                Destroy(body.GetComponent<Collider>());
                var renderer = body.GetComponent<MeshRenderer>();
                if (material != null)
                    renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                body.transform.localScale = new Vector3(0.55f, 0.85f, 0.55f);
            }

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 1.7f;
            agent.speed = speed;
            agent.angularSpeed = 360f;
            agent.acceleration = 4f;
            agent.autoBraking = false;
            // Évitement léger : les passants se contournent entre eux et contournent joueurs et voitures
            // (NavMeshObstacle sur leurs prefabs). Rien ne se traverse.
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 60;
            agent.enabled = false;

            root.SetActive(false);
            return agent;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextUpdate)
                return;
            _nextUpdate = Time.unscaledTime + UpdateInterval;

            if (_camera == null)
            {
                UnityEngine.Camera main = UnityEngine.Camera.main;
                if (main == null)
                    return;
                _camera = main.transform;
            }

            Vector3 center = _camera.position;
            center.y = 0f;
            int spawnBudget = 2; // étale les apparitions : pas de pic de calcul de chemins
            int activeLimit = Mathf.Min(_pool.Count, _maxPedestrians);

            for (int i = 0; i < activeLimit; i++)
            {
                NavMeshAgent agent = _pool[i];
                GameObject go = agent.gameObject;

                if (go.activeSelf)
                {
                    Vector3 offset = agent.transform.position - center;
                    offset.y = 0f;
                    if (offset.sqrMagnitude > _despawnDistance * _despawnDistance)
                    {
                        agent.enabled = false;
                        go.SetActive(false);
                    }
                    else if (!agent.pathPending && agent.remainingDistance < 1.5f)
                    {
                        SetRandomDestination(agent);
                    }
                }
                else if (spawnBudget > 0 && TrySpawn(agent, center))
                {
                    spawnBudget--;
                }
            }
        }

        private bool TrySpawn(NavMeshAgent agent, Vector3 center)
        {
            float angle = (float)(_random.NextDouble() * Mathf.PI * 2.0);
            float distance = Mathf.Lerp(_spawnMinDistance, _spawnMaxDistance, (float)_random.NextDouble());
            Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                return false;

            GameObject go = agent.gameObject;
            go.transform.position = hit.position;
            go.SetActive(true);
            agent.enabled = true;
            agent.Warp(hit.position);
            SetRandomDestination(agent);

            // Animation de marche à la vitesse de l'agent (constante : réglée une fois par apparition).
            if (_animators.TryGetValue(go, out Animator animator))
            {
                Characters.CharacterAnimation.Drive(animator, agent.speed);
                animator.SetFloat(Characters.CharacterAnimation.CycleOffsetId, (float)_random.NextDouble());
            }
            return true;
        }

        private void SetRandomDestination(NavMeshAgent agent)
        {
            float angle = (float)(_random.NextDouble() * Mathf.PI * 2.0);
            float distance = _wanderRadius * Mathf.Sqrt((float)_random.NextDouble());
            Vector3 target = agent.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }
    }
}
