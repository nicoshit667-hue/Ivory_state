using UnityEngine;

namespace IvoryState.Gameplay.Homes
{
    /// <summary>
    /// Emplacement des studios de la Résidence (lieux à part du monde extérieur). Les intérieurs sont
    /// rangés en grille loin sous la ville : invisibles depuis la rue, hors du NavMesh, et chaque studio
    /// est fermé (murs et plafond), donc on n'y voit jamais le voisin. Partagé par l'éditeur (génération),
    /// le serveur (téléportations) et le client (affichage du seul studio occupé).
    /// </summary>
    public static class StudioLayout
    {
        /// <summary>Nombre de studios (2 colocataires chacun). Doit rester identique côté Nakama.</summary>
        public const int Count = 96;
        public const int Columns = 8;
        public const float Spacing = 40f;

        /// <summary>Studio n° 1 ; les suivants vers l'est puis vers le sud.</summary>
        public static readonly Vector3 Origin = new Vector3(-140f, -400f, -190f);

        /// <summary>Sous cette hauteur, le joueur est dans un intérieur.</summary>
        public const float IndoorBelowY = -100f;

        // Dimensions intérieures d'après le plan du studio (6 × 4,5 m, agrandi comme tous les plans).
        // Porte sur le mur ouest (-x).
        public const float Width = 6f * PropertyCatalog.PlanScale;
        public const float Depth = 4.5f * PropertyCatalog.PlanScale;
        public const float Height = 3.0f;
        public const float ExitRadius = 0.7f;
        private const float DoorZ = 0.55f * PropertyCatalog.PlanScale;

        public static bool IsValid(int studio) => studio >= 1 && studio <= Count;

        public static Vector3 Center(int studio)
        {
            int index = studio - 1;
            return Origin + new Vector3(index % Columns * Spacing, 0f, -(index / Columns) * Spacing);
        }

        /// <summary>Point d'arrivée : juste devant la porte (mur ouest), dans la pièce.</summary>
        public static Vector3 SpawnPoint(int studio) => Center(studio) + new Vector3(-Width * 0.5f + 1.7f, 0.05f, DoorZ);

        /// <summary>Cercle de sortie, au pied de la porte.</summary>
        public static Vector3 ExitCircle(int studio) => Center(studio) + new Vector3(-Width * 0.5f + 0.75f, 0f, DoorZ);

        public static bool IsIndoors(Vector3 position) => position.y < IndoorBelowY;

        /// <summary>Studio où se trouve cette position (0 = dehors).</summary>
        public static int StudioAt(Vector3 position)
        {
            if (!IsIndoors(position))
                return 0;
            Vector3 local = position - Origin;
            int column = Mathf.RoundToInt(local.x / Spacing);
            int row = Mathf.RoundToInt(-local.z / Spacing);
            if (column < 0 || column >= Columns || row < 0)
                return 0;
            int studio = row * Columns + column + 1;
            return IsValid(studio) ? studio : 0;
        }
    }
}
