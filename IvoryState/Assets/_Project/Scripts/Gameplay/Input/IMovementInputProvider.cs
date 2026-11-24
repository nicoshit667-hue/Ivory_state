using UnityEngine;

namespace IvoryState.Gameplay.Input
{
    /// <summary>
    /// Source des entrées de déplacement du joueur local.
    /// <para>
    /// Le moteur de déplacement (Gameplay) ne connaît que cette interface. L'implémentation
    /// concrète (contrôles tactiles, clavier, bot de test) vit côté Client, ce qui garde
    /// Gameplay indépendant de l'UI et compilable sur le serveur dédié.
    /// </para>
    /// </summary>
    public interface IMovementInputProvider
    {
        /// <summary>Direction voulue, repère caméra. Magnitude 0..1 (analogique).</summary>
        Vector2 Move { get; }

        /// <summary>Vrai si le joueur veut courir.</summary>
        bool Sprint { get; }

        /// <summary>Lacet (yaw) de la caméra en degrés : le déplacement est relatif à la vue.</summary>
        float ViewYaw { get; }

        /// <summary>
        /// Renvoie vrai si un saut a été demandé depuis le dernier appel, puis réinitialise.
        /// Les entrées ponctuelles sont accumulées entre deux ticks pour ne jamais être perdues
        /// quand le framerate est supérieur au tick réseau.
        /// </summary>
        bool ConsumeJump();
    }

    /// <summary>Actions ponctuelles du joueur local (hors déplacement).</summary>
    public interface IActionInputProvider
    {
        /// <summary>Vrai si le bouton d'action a été pressé depuis le dernier appel.</summary>
        bool ConsumeInteract();
    }

    /// <summary>Point d'enregistrement unique des sources d'entrées locales.</summary>
    public static class PlayerInputRegistry
    {
        public static IMovementInputProvider Provider { get; private set; }
        public static IActionInputProvider Actions { get; private set; }

        public static void Register(IMovementInputProvider provider)
        {
            Provider = provider;
            if (provider is IActionInputProvider actions)
                Actions = actions;
        }

        public static void Unregister(IMovementInputProvider provider)
        {
            if (Provider == provider)
                Provider = null;
            if (ReferenceEquals(Actions, provider))
                Actions = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Provider = null;
            Actions = null;
        }
    }
}
