using UnityEngine;

namespace IvoryState.Gameplay.Vehicles
{
    /// <summary>
    /// Emplacement de taxi dans la ville (simple repère de scène, sans réseau). Au démarrage,
    /// le serveur fait apparaître un taxi sur chaque emplacement, orienté comme le repère.
    /// </summary>
    public sealed class TaxiStand : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, 0.8f, 0f), new Vector3(1.9f, 1.6f, 4.3f));
        }
    }
}
