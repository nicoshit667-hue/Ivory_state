using UnityEngine;

namespace IvoryState.Client.Characters
{
    /// <summary>
    /// Personnage non joueur immobile : vendeuse derrière son comptoir, marchand du marché, apprenti
    /// de gbaka, fidèle devant l'église… Purement visuel, créé seulement sur les clients qui affichent
    /// le jeu (rien sur le serveur headless).
    /// <para>
    /// Mobile : l'Animator ne calcule rien hors de l'écran (CullCompletely), et le personnage est
    /// désactivé au-delà de <see cref="VisibleDistance"/> m de la caméra.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VendorNpc : MonoBehaviour
    {
        private const float VisibleDistance = 90f;
        private const float CheckInterval = 0.5f;

        [SerializeField] private CharacterModels _models;
        [SerializeField] private int _modelIndex;

        [Tooltip("Petit balancement (marchand qui appelle les clients, apprenti qui fait signe).")]
        [SerializeField] private bool _lively;

        private GameObject _avatar;
        private Animator _animator;
        private float _nextCheck;
        private float _phase;

        public void EditorSetup(CharacterModels models, int modelIndex, bool lively)
        {
            _models = models;
            _modelIndex = modelIndex;
            _lively = lively;
        }

        private void Start()
        {
            if (Application.isBatchMode || _models == null || _models.Count == 0)
            {
                enabled = false;
                return;
            }

            GameObject prefab = _models.Get(_modelIndex);
            if (prefab == null)
            {
                enabled = false;
                return;
            }

            _avatar = Instantiate(prefab, transform, false);
            _avatar.transform.localPosition = Vector3.zero;
            _avatar.transform.localRotation = Quaternion.identity;
            _animator = _avatar.GetComponent<Animator>();
            if (_animator != null)
            {
                _animator.cullingMode = AnimatorCullingMode.CullCompletely;
                _animator.SetFloat(CharacterAnimation.CycleOffsetId, (_modelIndex * 0.37f) % 1f);
                CharacterAnimation.Drive(_animator, 0f);
            }

            // Les PNJ ne projettent pas d'ombre (comme les passants) : économie sur téléphone.
            foreach (Renderer renderer in _avatar.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _phase = (_modelIndex * 1.7f) % 6.28f;
            _nextCheck = Time.unscaledTime + Random.value * CheckInterval;
        }

        private void Update()
        {
            if (_avatar == null)
                return;

            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + CheckInterval;
                Camera camera = Camera.main;
                bool visible = camera == null ||
                               (camera.transform.position - transform.position).sqrMagnitude < VisibleDistance * VisibleDistance;
                if (_avatar.activeSelf != visible)
                    _avatar.SetActive(visible);
            }

            if (_lively && _avatar.activeSelf)
            {
                // Se tourne légèrement de gauche à droite, comme pour héler les passants.
                float yaw = Mathf.Sin(Time.time * 0.6f + _phase) * 25f;
                _avatar.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }
    }
}
