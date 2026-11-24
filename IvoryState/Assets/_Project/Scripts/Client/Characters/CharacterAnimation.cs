using UnityEngine;

namespace IvoryState.Client.Characters
{
    /// <summary>Paramètres communs aux Animator des personnages.</summary>
    public static class CharacterAnimation
    {
        /// <summary>Vitesse au sol (m/s) : 0 = attente, ~1,5 = marche, ~5 = course (arbre de mélange).</summary>
        public static readonly int SpeedId = Animator.StringToHash("Speed");

        /// <summary>Décalage de cycle, pour que des passants voisins ne marchent pas au pas.</summary>
        public static readonly int CycleOffsetId = Animator.StringToHash("CycleOffset");

        // Vitesses (m/s) auxquelles les clips Rocketbox avancent sans glisser (seuils de l'arbre de mélange,
        // voir CharacterSetup).
        public const float WalkClipSpeed = 1.45f;
        public const float RunClipSpeed = 4.8f;

        /// <summary>
        /// Anime un personnage qui se déplace à <paramref name="speed"/> m/s, sans glissement des pieds.
        /// <list type="bullet">
        /// <item>Jusqu'à la marche du clip : mélange attente / marche, cadence normale.</item>
        /// <item>Entre marche et course : le mélange penche d'abord vers la marche (une marche rapide
        /// reste une marche, au lieu d'un mélange mou à moitié couru), et la cadence est accélérée
        /// pour que la foulée couvre exactement la distance parcourue.</item>
        /// <item>Au-delà de la course du clip (sprint) : course pure, cadence accélérée d'autant.</item>
        /// </list>
        /// </summary>
        public static void Drive(Animator animator, float speed)
        {
            float blend, playback;
            if (speed <= WalkClipSpeed)
            {
                blend = speed;
                playback = 1f;
            }
            else if (speed < RunClipSpeed)
            {
                float t = (speed - WalkClipSpeed) / (RunClipSpeed - WalkClipSpeed);
                blend = WalkClipSpeed + (RunClipSpeed - WalkClipSpeed) * Mathf.Pow(t, 1.5f);
                // Avec la synchronisation des cycles, la foulée du mélange avance à la vitesse du seuil.
                playback = speed / blend;
            }
            else
            {
                blend = RunClipSpeed;
                playback = speed / RunClipSpeed;
            }

            animator.SetFloat(SpeedId, blend);
            animator.speed = Mathf.Clamp(playback, 0.6f, 1.5f);
        }
    }
}
