using UnityEngine;

namespace IvoryState.Client.World
{
    /// <summary>
    /// Positions des têtes de lampadaires de la ville, enregistrées par le générateur (éditeur).
    /// Lu par <see cref="StreetLightPool"/> pour placer de vraies lumières sur les plus proches.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityLamps : MonoBehaviour
    {
        /// <summary>xyz = position monde de l'ampoule, w = type (0 = LED blanche, 1 = sodium orangé).</summary>
        [SerializeField] private Vector4[] _lamps = new Vector4[0];

        public Vector4[] Lamps => _lamps;

#if UNITY_EDITOR
        public void EditorSetLamps(Vector4[] lamps)
        {
            _lamps = lamps;
        }
#endif
    }
}
