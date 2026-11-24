using UnityEngine;

namespace IvoryState.Gameplay.Combat
{
    /// <summary>
    /// Hôpitaux de la ville : entrée dans la rue (cercle sous une croix rouge) et grande salle de soins
    /// (lieu à part, loin à l'est sous la ville). Un joueur mis K.O. (bagarre, malaise) se réveille dans
    /// l'hôpital le plus proche ; sa santé remonte pendant une minute avant qu'il puisse sortir.
    /// </summary>
    public static class HospitalCatalog
    {
        public struct Hospital
        {
            public string Name;
            public Vector3 Entrance;
            public Vector3 ExitOffset;
            /// <summary>Du trottoir vers le bâtiment.</summary>
            public Vector3 Inward;
        }

        private static Hospital At(string name, World.CityLayout.Place place) =>
            new Hospital { Name = name, Entrance = place.Position, ExitOffset = place.Along * 3.6f, Inward = -place.ToRoad };

        private static Hospital[] s_hospitals;
        private static int s_hospitalsVersion = -1;

        /// <summary>Les trois CHU (entrées sur des repères de trottoir, posées sur le relief).</summary>
        public static Hospital[] Hospitals
        {
            get
            {
                if (s_hospitals == null || s_hospitalsVersion != World.CityLayout.Version)
                {
                    s_hospitalsVersion = World.CityLayout.Version;
                    s_hospitals = new[]
                    {
                        At("CHU de Treichville", World.CityPlaces.HospitalTreichville),
                        At("CHU de Cocody", World.CityPlaces.HospitalCocody),
                        At("CHU de Yopougon", World.CityPlaces.HospitalYopougon),
                    };
                }
                return s_hospitals;
            }
        }
        public const float RecoverySeconds = 60f;
        /// <summary>Soins après une chute (K.O.) : frais médicaux.</summary>
        public const long KnockoutFee = 4000;
        /// <summary>Consultation volontaire (blessé qui entre de lui-même).</summary>
        public const long ConsultationFee = 2000;

        // Bâtiment agrandi d'après le plan (28 × 22 unités de plan × 1,5 = 42 × 33 m, plafond à 4,8 m) :
        // grand hall d'accueil et d'attente au sud, large couloir central est-ouest (6,75 m), urgences au
        // sud-ouest, consultations au nord-ouest, pharmacie et labo au nord-centre, hospitalisation (4 lits)
        // au nord-est, bureau du médecin et sanitaires au sud-est. 8 lits en tout.
        public static readonly Vector3 InteriorOrigin = new Vector3(6000f, -400f, 0f);
        public const float InteriorSpacing = 140f;
        public const float WardWidth = 28f * Homes.PropertyCatalog.PlanScale;
        public const float WardDepth = 22f * Homes.PropertyCatalog.PlanScale;
        public const int Beds = 8;

        // Lits en repère local (déjà agrandi) : 4 en hospitalisation (tête au nord), 2 en consultation
        // (tête au nord), 2 aux urgences (tête au sud). Réveil : sur le sol dégagé à côté du lit.
        private static readonly Vector3[] BedLocal =
        {
            new Vector3(10.8f, 0f, 15.1f), new Vector3(13.8f, 0f, 15.1f),
            new Vector3(16.8f, 0f, 15.1f), new Vector3(19.5f, 0f, 15.1f),
            new Vector3(-18.0f, 0f, 15.1f), new Vector3(-9.75f, 0f, 15.1f),
            new Vector3(-17.25f, 0f, -15.1f), new Vector3(-12.75f, 0f, -15.1f),
        };
        private static readonly Vector3[] WakeOffset =
        {
            new Vector3(0f, 0.05f, -1.75f), new Vector3(0f, 0.05f, -1.75f),
            new Vector3(0f, 0.05f, -1.75f), new Vector3(0f, 0.05f, -1.75f),
            new Vector3(1.15f, 0.05f, -0.4f), new Vector3(1.15f, 0.05f, -0.4f),
            new Vector3(0f, 0.05f, 1.75f), new Vector3(0f, 0.05f, 1.75f),
        };

        /// <summary>Le lit n° i a-t-il la tête au nord ou au sud (0 = nord, 2 = sud).</summary>
        public static int BedHead(int bed) => bed < 6 ? 0 : 2;

        public static int Count => Hospitals.Length;

        public static bool IsValid(int index) => index >= 0 && index < Hospitals.Length;

        public static Vector3 InteriorCenter(int index) => InteriorOrigin + new Vector3(index * InteriorSpacing, 0f, 0f);

        /// <summary>Lit n° i (0…7) : position du lit (le joueur se réveille à côté).</summary>
        public static Vector3 BedPosition(int index, int bed) => InteriorCenter(index) + BedLocal[Mathf.Clamp(bed, 0, Beds - 1)];

        public static Vector3 WakeUpPoint(int index, int bed) =>
            BedPosition(index, bed) + WakeOffset[Mathf.Clamp(bed, 0, Beds - 1)];

        public static Vector3 ExitCircle(int index) =>
            InteriorCenter(index) + new Vector3(0f, 0f, -WardDepth * 0.5f + 1.15f);

        public static Vector3 EntranceExit(int index) => Hospitals[index].Entrance + Hospitals[index].ExitOffset;

        /// <summary>Hôpital dont la salle contient cette position (-1 : aucun).</summary>
        public static int HospitalAt(Vector3 position)
        {
            if (!Homes.StudioLayout.IsIndoors(position) || position.x < InteriorOrigin.x - InteriorSpacing * 0.5f)
                return -1;
            int index = Mathf.RoundToInt((position.x - InteriorOrigin.x) / InteriorSpacing);
            if (!IsValid(index))
                return -1;
            Vector3 local = position - InteriorCenter(index);
            return Mathf.Abs(local.x) < InteriorSpacing * 0.5f && Mathf.Abs(local.z) < InteriorSpacing * 0.5f ? index : -1;
        }

        public static int Nearest(Vector3 position)
        {
            int best = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < Hospitals.Length; i++)
            {
                Vector3 offset = Hospitals[i].Entrance - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < bestSqr)
                {
                    best = i;
                    bestSqr = offset.sqrMagnitude;
                }
            }
            return best;
        }
    }
}
