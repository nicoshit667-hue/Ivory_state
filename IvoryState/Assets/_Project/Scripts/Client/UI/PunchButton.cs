using IvoryState.Gameplay.Combat;
using UnityEngine;
using UnityEngine.EventSystems;

namespace IvoryState.Client.UI
{
    /// <summary>Bouton « FRAPPER » : un appui = un coup sur le joueur visé (le serveur décide).</summary>
    [DisallowMultipleComponent]
    public sealed class PunchButton : MonoBehaviour, IPointerDownHandler
    {
        public void OnPointerDown(PointerEventData eventData) => PlayerCombat.Local?.Punch();
    }
}
