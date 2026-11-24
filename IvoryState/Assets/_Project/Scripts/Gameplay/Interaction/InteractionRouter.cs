using System;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Pont entre une demande d'interaction validée (Gameplay) et la logique métier (assembly Server).
    /// <para>
    /// Gameplay ne référence pas Server : c'est le module serveur qui s'enregistre ici au démarrage.
    /// Dans un build client, aucun gestionnaire n'existe, et la logique économique n'y est pas livrée.
    /// </para>
    /// </summary>
    public static class InteractionRouter
    {
        /// <summary>Gestionnaire serveur : (joueur, point) — déjà validé (distance, cadence).</summary>
        public static Action<PlayerInteractor, WorldInteractable> ServerHandler { get; set; }

        /// <summary>
        /// Gestionnaire serveur des menus : (joueur, point, option, dans le sac) — option déjà bornée.
        /// « Dans le sac » : l'achat va dans l'inventaire au lieu d'être consommé sur place.
        /// </summary>
        public static Action<PlayerInteractor, MenuInteractable, int, bool> ServerChoiceHandler { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ServerHandler = null;
            ServerChoiceHandler = null;
        }
    }
}
