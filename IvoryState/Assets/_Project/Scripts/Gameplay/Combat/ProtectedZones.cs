using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Interaction;
using UnityEngine;

namespace IvoryState.Gameplay.Combat
{
    /// <summary>
    /// Zones protégées : on ne peut pas y frapper (ni y être frappé). Les nouveaux joueurs y sont
    /// tranquilles à leur arrivée.
    /// <list type="bullet">
    /// <item>La place du Plateau (point d'apparition).</item>
    /// <item>Tous les intérieurs : logements et hôpitaux.</item>
    /// <item>Les abords des hôpitaux (25 m), des entrées de logements (6 m) et des gares de gbaka (15 m).</item>
    /// </list>
    /// </summary>
    public static class ProtectedZones
    {
        private const float PlazaHalfSize = 40f;
        private const float HospitalRadius = 25f;
        private const float EntranceRadius = 6f;
        private const float StationRadius = 15f;

        public static bool Contains(Vector3 position)
        {
            if (StudioLayout.IsIndoors(position))
                return true;
            if (Mathf.Abs(position.x) <= PlazaHalfSize && Mathf.Abs(position.z) <= PlazaHalfSize)
                return true;

            foreach (HospitalCatalog.Hospital hospital in HospitalCatalog.Hospitals)
            {
                if (FlatSqr(hospital.Entrance, position) <= HospitalRadius * HospitalRadius)
                    return true;
            }
            var entrances = HomeEntrance.All;
            for (int i = 0; i < entrances.Count; i++)
            {
                if (FlatSqr(entrances[i].transform.position, position) <= EntranceRadius * EntranceRadius)
                    return true;
            }
            var points = WorldInteractable.All;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] is GbakaStationPoint && FlatSqr(points[i].transform.position, position) <= StationRadius * StationRadius)
                    return true;
            }
            return false;
        }

        private static float FlatSqr(Vector3 a, Vector3 b)
        {
            Vector3 offset = a - b;
            offset.y = 0f;
            return offset.sqrMagnitude;
        }
    }
}
