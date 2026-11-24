using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    /// <summary>
    /// Mesure de l'erreur de prédiction du joueur local : à chaque reconcile, écart entre la
    /// position que le client avait prédite pour ce tick et celle calculée par le serveur.
    /// <para>
    /// C'est la mesure objective des saccades du personnage : une erreur de 0 cm signifie
    /// que le client et le serveur sont parfaitement d'accord. Au-delà de quelques centimètres,
    /// la correction devient visible malgré le lissage.
    /// </para>
    /// Fenêtre glissante, lue et remise à zéro par qui affiche les statistiques.
    /// </summary>
    public static class PredictionDiagnostics
    {
        /// <summary>Seuil (m) au-delà duquel une correction est comptée comme « visible ».</summary>
        public const float VisibleErrorThreshold = 0.05f;

        public static int Samples { get; private set; }
        public static float SumError { get; private set; }
        public static float MaxError { get; private set; }
        public static int VisibleCorrections { get; private set; }

        public static float AverageError => Samples > 0 ? SumError / Samples : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Samples = 0;
            SumError = 0f;
            MaxError = 0f;
            VisibleCorrections = 0;
        }

        internal static void Record(float error)
        {
            Samples++;
            SumError += error;
            if (error > MaxError)
                MaxError = error;
            if (error > VisibleErrorThreshold)
                VisibleCorrections++;
        }
    }
}
