using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace IvoryState.Client.UI
{
    /// <summary>Relaie l'appui et le relâchement d'un bouton du HUD construit à l'exécution.</summary>
    [DisallowMultipleComponent]
    public sealed class HudPressRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private Action _down;
        private Action _up;

        public void Init(Action down, Action up)
        {
            _down = down;
            _up = up;
        }

        public void OnPointerDown(PointerEventData eventData) => _down?.Invoke();
        public void OnPointerUp(PointerEventData eventData) => _up?.Invoke();
    }
}
