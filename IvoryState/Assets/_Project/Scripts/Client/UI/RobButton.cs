using IvoryState.Gameplay.Combat;
using UnityEngine;
using UnityEngine.EventSystems;

namespace IvoryState.Client.UI
{
    /// <summary>Bouton « RACKET » : menacer le joueur visé avec la machette ou le couteau du sac (le serveur décide).</summary>
    [DisallowMultipleComponent]
    public sealed class RobButton : MonoBehaviour, IPointerDownHandler
    {
        public void OnPointerDown(PointerEventData eventData) => PlayerCombat.Local?.Rob();
    }
}
