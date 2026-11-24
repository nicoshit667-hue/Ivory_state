using FishNet.Object;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.Characters
{
    /// <summary>
    /// Apparence réaliste d'un joueur : remplace la capsule par un avatar animé.
    /// <para>
    /// <b>Apparence stable :</b> l'avatar est choisi d'après le pseudo (hachage stable), et non d'après
    /// l'identifiant réseau qui change à chaque connexion. Même pseudo = même personnage, à chaque
    /// relance et sur tous les écrans, sans aucun message réseau. Si le pseudo change, l'avatar suit.
    /// </para>
    /// <para>
    /// L'avatar est placé sous le « Graphics » lissé par FishNet ; l'animation suit
    /// <see cref="PlayerMotor.PlanarSpeed"/>, lissée pour absorber les recalages de la prédiction.
    /// Rien n'est créé sur un serveur headless ni sur un bot.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)] // après le lissage de FishNet (Graphics)
    public sealed class PlayerAvatarView : MonoBehaviour
    {
        // Pseudo provisoire donné par le serveur avant le choix du joueur : on attend un peu le vrai.
        private const string ProvisionalPrefix = "Joueur_";
        private const float ProvisionalWait = 3f;

        // Étale l'instanciation des avatars distants (au plus 1 par frame) pour éviter les freezes
        // quand plusieurs joueurs entrent en même temps dans la zone d'intérêt réseau.
        private static int s_lastRemoteBuildFrame = -1;

        [SerializeField] private CharacterModels _models;
        [SerializeField] private Transform _graphics;
        [SerializeField] private Renderer[] _placeholders = new Renderer[0];
        [SerializeField, Range(1f, 30f)] private float _speedSharpness = 10f;

        [Tooltip("Temps de lissage de l'orientation visible (s). La simulation tourne vite (720 °/s) ; le corps affiché suit en douceur, sans à-coups gauche-droite.")]
        [SerializeField, Range(0f, 0.3f)] private float _turnSmoothTime = 0.09f;

        private float _visualYaw;
        private float _yawVelocity;

        private PlayerMotor _motor;
        private NetworkObject _networkObject;
        private PlayerNetworkIdentity _identity;
        private Animator _animator;
        private GameObject _avatar;
        private string _builtForName;
        private int _builtModel = -1;
        private float _spawnTime = -1f;
        private float _speed;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _networkObject = GetComponent<NetworkObject>();
            _identity = GetComponent<PlayerNetworkIdentity>();
            if (_models != null)
                Catalog = _models;
            if (Application.isBatchMode)
                enabled = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PreviewModel = -1;
            PreviewOutfit = 0;
            Catalog = null;
            s_lastRemoteBuildFrame = -1;
        }

        private void Update()
        {
            if (_networkObject == null || !_networkObject.IsSpawned)
                return;
            if (_spawnTime < 0f)
                _spawnTime = Time.unscaledTime;

            // Apparence choisie (création du personnage), aperçu local pendant la création, ou à défaut
            // avatar tiré du pseudo (bots, anciens comptes).
            bool local = _identity != null && _identity == PlayerNetworkIdentity.Local;
            if (local && PreviewModel >= 0)
            {
                if (PreviewModel != _builtModel || PreviewOutfit != _builtOutfit)
                    Build(PreviewModel, PreviewOutfit);
            }
            else if (_identity != null && _identity.HasAppearance)
            {
                if (_identity.Model != _builtModel || _identity.Outfit != _builtOutfit)
                {
                    if (local || CanBuildRemoteThisFrame())
                        Build(_identity.Model, _identity.Outfit);
                }
            }
            else
            {
                // Comparaison de références : aucune allocation par image.
                string name = _identity != null ? _identity.DisplayName : null;
                if (!ReferenceEquals(name, _builtForName) && IsUsableName(name))
                {
                    if (local || CanBuildRemoteThisFrame())
                        Rebuild(name);
                }
            }

            if (_animator == null || _motor == null)
                return;

            // Vitesse réellement parcourue par le modèle affiché (et non la vitesse demandée) : contre un
            // mur, le personnage ne court plus sur place, et les joueurs distants suivent leur vrai
            // déplacement. Plafonnée par la vitesse demandée, pour ignorer les recalages de prédiction.
            float target = Mathf.Min(MeasuredSpeed(), _motor.PlanarSpeed + 0.5f);
            _speed = Mathf.Lerp(_speed, target, 1f - Mathf.Exp(-_speedSharpness * Time.deltaTime));
            CharacterAnimation.Drive(_animator, _speed);
        }

        private static bool CanBuildRemoteThisFrame()
        {
            int frame = Time.frameCount;
            if (s_lastRemoteBuildFrame == frame)
                return false;
            s_lastRemoteBuildFrame = frame;
            return true;
        }

        private Vector3 _lastGraphicsPosition;
        private bool _hasLastPosition;

        private float MeasuredSpeed()
        {
            Vector3 position = _graphics != null ? _graphics.position : transform.position;
            float delta = Time.deltaTime;
            float speed = 0f;
            if (_hasLastPosition && delta > 0f)
            {
                Vector3 moved = position - _lastGraphicsPosition;
                moved.y = 0f;
                float distance = moved.magnitude;
                // Téléportation (descente de véhicule, réapparition) : on ne la compte pas comme une course.
                speed = distance > 3f ? 0f : distance / delta;
            }
            _lastGraphicsPosition = position;
            _hasLastPosition = true;
            return speed;
        }

        /// <summary>Orientation affichée lissée (purement visuelle : la simulation réseau n'est pas touchée).</summary>
        private void LateUpdate()
        {
            if (_avatar == null || _graphics == null)
                return;
            float target = _graphics.eulerAngles.y;
            _visualYaw = Mathf.SmoothDampAngle(_visualYaw, target, ref _yawVelocity, _turnSmoothTime, Mathf.Infinity, Time.deltaTime);
            _avatar.transform.rotation = Quaternion.Euler(0f, _visualYaw, 0f);
        }

        private bool IsUsableName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return Time.unscaledTime - _spawnTime > ProvisionalWait && _avatar == null;
            if (name.StartsWith(ProvisionalPrefix, System.StringComparison.Ordinal))
                return Time.unscaledTime - _spawnTime > ProvisionalWait && _avatar == null;
            return true;
        }

        /// <summary>Aperçu local (écran de création du personnage) : -1 = aucun.</summary>
        public static int PreviewModel { get; set; } = -1;
        public static int PreviewOutfit { get; set; }

        /// <summary>Catalogue des avatars (lu par l'écran de création du personnage).</summary>
        public static CharacterModels Catalog { get; private set; }

        private int _builtOutfit = -1;

        private void Rebuild(string name)
        {
            _builtForName = name;
            if (_models == null || _models.Count == 0 || _graphics == null)
                return;

            int model = ModelIndexFor(name, _models.Count);
            if (model == _builtModel && _avatar != null)
                return;
            Build(model, 0);
        }

        private void Build(int model, int outfit)
        {
            if (_models == null || _models.Count == 0 || _graphics == null)
                return;
            Catalog = _models;

            if (_avatar != null)
                Destroy(_avatar);
            _builtOutfit = outfit;

            _avatar = Instantiate(_models.Get(model), _graphics, false);
            _avatar.transform.localPosition = Vector3.zero;
            _avatar.transform.localRotation = Quaternion.identity;
            _builtModel = model;
            _visualYaw = _graphics.eulerAngles.y;
            _yawVelocity = 0f;

            int layer = gameObject.layer;
            foreach (Transform t in _avatar.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            _animator = _avatar.GetComponent<Animator>();
            if (_animator != null)
                _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            foreach (Renderer placeholder in _placeholders)
            {
                if (placeholder != null)
                    placeholder.enabled = false;
            }

            // Tenue : remplace le matériau du corps d'origine par sa variante de couleur.
            if (_models.TryGetOutfit(model, outfit, out Material original, out Material replacement))
            {
                foreach (Renderer r in _avatar.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == original)
                        {
                            materials[i] = replacement;
                            changed = true;
                        }
                    }
                    if (changed)
                        r.sharedMaterials = materials;
                }
            }
        }

        /// <summary>
        /// Index d'avatar pour un pseudo : hachage FNV-1a insensible à la casse, identique sur toutes
        /// les plateformes (string.GetHashCode varie d'un processus à l'autre).
        /// </summary>
        public static int ModelIndexFor(string name, int count)
        {
            if (count <= 0)
                return 0;
            uint hash = 2166136261;
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < name.Length; i++)
                {
                    hash ^= char.ToLowerInvariant(name[i]);
                    hash *= 16777619;
                }
            }
            return (int)(hash % (uint)count);
        }
    }
}
