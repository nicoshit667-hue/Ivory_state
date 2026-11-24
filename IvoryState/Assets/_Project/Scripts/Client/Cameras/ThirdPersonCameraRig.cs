using IvoryState.Client.Input;
using IvoryState.Core;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

// "Cameras" (pluriel) pour ne pas masquer le type UnityEngine.Camera dans IvoryState.Client.*
namespace IvoryState.Client.Cameras
{
    /// <summary>
    /// Caméra à la troisième personne qui suit le joueur local.
    /// <list type="bullet">
    /// <item>Suit le visuel <b>lissé</b> du joueur, jamais la racine prédite, qui peut sauter
    /// légèrement lors d'un recalage serveur.</item>
    /// <item>Évite de traverser les murs : un SphereCast raccourcit la distance tout de suite,
    /// puis elle revient en douceur. Indispensable dans les rues étroites d'une ville.</item>
    /// </list>
    /// Tourne entièrement côté client : rien n'est synchronisé.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraRig : MonoBehaviour
    {
        [SerializeField] private MobilePlayerControls _controls;

        [Header("Cadrage")]
        [SerializeField, Range(1.5f, 12f)] private float _distance = 5f;
        [SerializeField, Range(0.3f, 3f)] private float _minDistance = 1f;
        [Tooltip("Hauteur du point visé au-dessus des pieds (≈ épaules).")]
        [SerializeField, Range(0.5f, 2.5f)] private float _pivotHeight = 1.6f;

        [Header("Collisions")]
        [SerializeField, Range(0.05f, 0.6f)] private float _collisionRadius = 0.25f;
        [Tooltip("Couches qui bloquent la caméra. Les joueurs sont exclus : la caméra ne doit pas " +
                 "zoomer à chaque passant dans une foule.")]
        [SerializeField] private LayerMask _collisionMask = GameLayers.WorldMask;
        [Tooltip("Vitesse de retour à la distance normale après un obstacle (m/s).")]
        [SerializeField, Range(1f, 20f)] private float _recoverSpeed = 6f;

        private const float VehicleDistanceFactor = 1.5f;
        // Visée (arme à feu) : caméra rapprochée, décalée sur l'épaule droite.
        private const float AimDistance = 2.1f;
        private const float AimShoulderOffset = 0.55f;
        private const float AimPivotDrop = 0.1f;
        private const float AimBlendSpeed = 6f;
        private float _aimBlend;
        private const float VehicleRecenterSpeed = 90f; // °/s

        private Transform _target;
        private float _currentDistance;

        private void OnEnable()
        {
            PlayerNetworkIdentity.LocalPlayerSpawned += OnLocalPlayerSpawned;
            if (PlayerNetworkIdentity.Local != null)
                OnLocalPlayerSpawned(PlayerNetworkIdentity.Local);
        }

        private void OnDisable()
        {
            PlayerNetworkIdentity.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        }

        private void OnLocalPlayerSpawned(PlayerNetworkIdentity player)
        {
            _target = player.TryGetComponent(out PlayerMotor motor) ? motor.GraphicalTransform : player.transform;
            _currentDistance = _distance;

            // Vue placée derrière le personnage à son apparition.
            if (_controls != null)
                _controls.SetYaw(player.transform.eulerAngles.y);
        }

        private void LateUpdate()
        {
            // Le test Unity "== null" couvre aussi un joueur détruit (déconnexion).
            if (_target == null || _controls == null)
                return;

            // Au volant : on suit le véhicule, plus haut et plus loin, et la vue revient doucement
            // derrière lui quand il roule (on peut toujours tourner la caméra du doigt).
            Transform followed = _target;
            float distance = _distance;
            float pivotHeight = _pivotHeight;
            PlayerDriving driving = PlayerDriving.Local;
            VehicleMotor vehicle = driving != null ? driving.Vehicle : null;
            if (vehicle != null)
            {
                followed = vehicle.GraphicalTransform;
                distance = _distance * VehicleDistanceFactor;
                pivotHeight = _pivotHeight + 0.6f;
                if (Mathf.Abs(vehicle.Speed) > 2f)
                    _controls.SetYaw(Mathf.MoveTowardsAngle(_controls.ViewYaw, vehicle.Yaw, VehicleRecenterSpeed * Time.deltaTime));
            }

            Quaternion rotation = Quaternion.Euler(_controls.Pitch, _controls.ViewYaw, 0f);
            bool aiming = vehicle == null && Gameplay.Combat.PlayerCombat.LocalAiming;
            _aimBlend = Mathf.MoveTowards(_aimBlend, aiming ? 1f : 0f, AimBlendSpeed * Time.deltaTime);
            distance = Mathf.Lerp(distance, AimDistance, _aimBlend);
            Vector3 pivot = followed.position + Vector3.up * (pivotHeight - AimPivotDrop * _aimBlend)
                            + rotation * Vector3.right * (AimShoulderOffset * _aimBlend);
            Vector3 back = rotation * Vector3.back;

            // Le pivot est à l'intérieur de la capsule du joueur, que le SphereCast ignore donc.
            float wanted = distance;
            if (Physics.SphereCast(pivot, _collisionRadius, back, out RaycastHit hit, distance,
                    _collisionMask, QueryTriggerInteraction.Ignore))
            {
                wanted = Mathf.Max(_minDistance, hit.distance);
            }

            // Rapprochement immédiat (ne jamais voir à travers un mur), éloignement progressif.
            _currentDistance = wanted < _currentDistance
                ? wanted
                : Mathf.MoveTowards(_currentDistance, wanted, _recoverSpeed * Time.deltaTime);

            transform.SetPositionAndRotation(pivot + back * _currentDistance, rotation);
        }
    }
}
