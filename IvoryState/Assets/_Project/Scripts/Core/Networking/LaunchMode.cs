namespace IvoryState.Core.Networking
{
    /// <summary>Rôle réseau de l'instance en cours d'exécution.</summary>
    public enum LaunchMode : byte
    {
        /// <summary>Aucun démarrage automatique : le panneau de dev permet de choisir.</summary>
        None = 0,

        /// <summary>Client mobile (cas de production).</summary>
        Client = 1,

        /// <summary>Serveur dédié qui fait autorité (headless en production).</summary>
        Server = 2,

        /// <summary>Serveur + client dans le même processus. Réservé au développement.</summary>
        Host = 3,
    }
}
