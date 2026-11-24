using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Nom des joueurs proches, affiché au-dessus de leur tête (jusqu'à 20 m).
    /// <para>
    /// Pool fixe d'étiquettes (aucune instanciation en jeu). Le texte n'est réécrit que si le joueur
    /// suivi change. Les joueurs au volant ne sont pas étiquetés : leur personnage est masqué.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NameTagsHud : MonoBehaviour
    {
        private const float MaxDistance = 20f;
        private const float HeadHeight = 2.05f;

        [SerializeField] private Text[] _tags;

        private PlayerNetworkIdentity[] _shown;
        private string[] _shownNames;
        private bool[] _shownHomeless;
        private UnityEngine.Camera _camera;

        private void Awake()
        {
            _shown = new PlayerNetworkIdentity[_tags.Length];
            _shownNames = new string[_tags.Length];
            _shownHomeless = new bool[_tags.Length];
            for (int i = 0; i < _tags.Length; i++)
            {
                _tags[i].supportRichText = true;
                _tags[i].gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (!Settings.GameSettings.ShowNameTags)
            {
                for (int i = 0; i < _tags.Length; i++)
                {
                    if (_tags[i] != null && _tags[i].gameObject.activeSelf)
                    {
                        _tags[i].gameObject.SetActive(false);
                        _shown[i] = null;
                        _shownNames[i] = null;
                    }
                }
                return;
            }

            if (_camera == null)
                _camera = UnityEngine.Camera.main;
            if (_camera == null)
                return;

            Vector3 cameraPosition = _camera.transform.position;
            PlayerNetworkIdentity local = PlayerNetworkIdentity.Local;
            var players = PlayerNetworkIdentity.ClientInstances;
            int used = 0;

            for (int i = 0; i < players.Count && used < _tags.Length; i++)
            {
                PlayerNetworkIdentity player = players[i];
                if (player == null || player == local)
                    continue;

                if (player.TryGetComponent(out PlayerDriving driving) && driving.IsInVehicle)
                    continue;

                Transform anchor = player.TryGetComponent(out PlayerMotor motor) ? motor.GraphicalTransform : player.transform;
                Vector3 head = anchor.position + Vector3.up * HeadHeight;
                if ((head - cameraPosition).sqrMagnitude > MaxDistance * MaxDistance)
                    continue;

                Vector3 screen = _camera.WorldToScreenPoint(head);
                if (screen.z <= 0f)
                    continue;

                Text tag = _tags[used];
                // Le pseudo peut arriver après l'apparition (SyncVar) : on compare aussi la chaîne.
                // Joueur à la rue (loyer impayé) : « SDF » en rouge sous son nom.
                bool homeless = player.TryGetComponent(out Gameplay.Homes.PlayerHome home) && home.IsHomeless;
                if (_shown[used] != player || !ReferenceEquals(_shownNames[used], player.DisplayName) || _shownHomeless[used] != homeless)
                {
                    _shown[used] = player;
                    _shownNames[used] = player.DisplayName;
                    _shownHomeless[used] = homeless;
                    tag.text = homeless ? player.DisplayName + "\n<color=#FF6A55><b>SDF</b></color>" : player.DisplayName;
                }

                tag.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
                if (!tag.gameObject.activeSelf)
                    tag.gameObject.SetActive(true);
                used++;
            }

            for (int i = used; i < _tags.Length; i++)
            {
                if (_tags[i].gameObject.activeSelf)
                {
                    _tags[i].gameObject.SetActive(false);
                    _shown[i] = null;
                    _shownNames[i] = null;
                }
            }
        }
    }
}
