using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace IvoryState.Core.Diagnostics
{
    /// <summary>
    /// Point d'entrée unique pour les logs du jeu.
    /// <para>
    /// <see cref="Info"/> est retiré à la compilation des builds clients de production
    /// (attribut <see cref="ConditionalAttribute"/>) : ni l'appel ni la construction de la
    /// chaîne ne coûtent quoi que ce soit sur mobile. Les avertissements et erreurs restent actifs.
    /// </para>
    /// </summary>
    public static class GameLog
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"),
         Conditional("UNITY_SERVER"), Conditional("IVORY_SERVER")]
        public static void Info(string tag, string message)
        {
            Debug.Log("[" + tag + "] " + message);
        }

        public static void Warn(string tag, string message)
        {
            Debug.LogWarning("[" + tag + "] " + message);
        }

        public static void Error(string tag, string message)
        {
            Debug.LogError("[" + tag + "] " + message);
        }
    }
}
