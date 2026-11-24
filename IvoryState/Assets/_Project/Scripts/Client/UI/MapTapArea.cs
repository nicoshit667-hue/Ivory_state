using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Gestes sur la carte plein écran : tap (coordonnées locales de la carte, en pixels), glisser (déplacement,
    /// en pixels d'écran) et molette (zoom autour du curseur).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapTapArea : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public event Action<Vector2> Tapped;
        /// <summary>Déplacement du doigt ou de la souris depuis le dernier appel (pixels d'écran).</summary>
        public event Action<Vector2> Dragged;
        /// <summary>Molette : position d'écran du curseur, crans (positif = zoom avant).</summary>
        public event Action<Vector2, float> Scrolled;

        /// <summary>Un glisser est en cours (le tap qui le termine ne marque pas de point).</summary>
        public bool IsDragging { get; private set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging)
                return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, eventData.position,
                    eventData.pressEventCamera, out Vector2 local))
                Tapped?.Invoke(local);
        }

        public void OnBeginDrag(PointerEventData eventData) => IsDragging = true;

        public void OnDrag(PointerEventData eventData) => Dragged?.Invoke(eventData.delta);

        public void OnEndDrag(PointerEventData eventData) => IsDragging = false;

        public void OnScroll(PointerEventData eventData) => Scrolled?.Invoke(eventData.position, eventData.scrollDelta.y);
    }
}
