using System;
using IvoryState.Client.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IvoryState.Client.World
{
    /// <summary>
    /// Réglages de rendu propres aux téléphones, appliqués au lancement (sans modifier les assets URP :
    /// changer par script les réglages qui touchent aux mots-clés de shader casse le build Android).
    /// <para>
    /// Délègue désormais à <see cref="GameSettings"/> pour que l'utilisateur puisse ajuster la résolution,
    /// la distance d'affichage, les ombres et le post-traitement depuis les paramètres du jeu.
    /// </para>
    /// </summary>
    public static class MobileRenderQuality
    {
        /// <summary>Vrai si le post-traitement a été coupé (lu par les outils de diagnostic).</summary>
        public static bool PostProcessingDisabled => !GameSettings.PostProcessing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Apply()
        {
            if (Application.isBatchMode)
                return;

            GameSettings.Load();

            string[] args = Environment.GetCommandLineArgs();
            int quality = Array.IndexOf(args, "-quality");
            if (quality >= 0 && quality + 1 < args.Length && args[quality + 1] == "high")
            {
                GameSettings.SetPostProcessing(true);
                GameSettings.Apply();
            }
        }
    }
}
