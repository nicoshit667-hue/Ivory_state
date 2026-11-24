using IvoryState.Client.Settings;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.World
{
    /// <summary>
    /// Affichage des intérieurs (client) : seul le logement où se trouve le joueur local est rendu
    /// (studio de colocation ou bien immobilier), les autres restent masqués (leurs colliders, eux,
    /// restent actifs pour la physique). À l'intérieur, la caméra ne voit pas plus loin que le logement :
    /// la ville n'est plus dessinée.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StudioVisibility : MonoBehaviour
    {
        private const float IndoorFarClip = 65f; // hôpital agrandi et jardins compris

        // Index : studios (n° - 1), puis biens immobiliers (StudioLayout.Count + n° - 1).
        [SerializeField] private GameObject[] _visuals;

        private int _shown = -1;
        private Camera _camera;

        private void Awake()
        {
            for (int i = 0; i < _visuals.Length; i++)
            {
                if (_visuals[i] != null)
                    _visuals[i].SetActive(false);
            }
        }

        private static int IndexAt(Vector3 position)
        {
            int studio = StudioLayout.StudioAt(position);
            if (studio != 0)
                return studio - 1;
            int property = PropertyCatalog.PropertyAt(position);
            if (property != 0)
                return StudioLayout.Count + property - 1;
            int hospital = Gameplay.Combat.HospitalCatalog.HospitalAt(position);
            return hospital >= 0 ? StudioLayout.Count + PropertyCatalog.Count + hospital : -1;
        }

        private void LateUpdate()
        {
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            int index = local != null ? IndexAt(local.transform.position) : -1;
            if (index == _shown)
                return;

            SetVisible(_shown, false);
            SetVisible(index, true);
            _shown = index;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;
            if (index >= 0)
            {
                _camera.farClipPlane = IndoorFarClip;
            }
            else
            {
                _camera.farClipPlane = GameSettings.OutdoorFarClip;
            }
        }

        private void SetVisible(int index, bool visible)
        {
            if (index >= 0 && index < _visuals.Length && _visuals[index] != null)
                _visuals[index].SetActive(visible);
        }

#if UNITY_EDITOR
        public void EditorSetVisuals(GameObject[] visuals) => _visuals = visuals;
#endif
    }
}
