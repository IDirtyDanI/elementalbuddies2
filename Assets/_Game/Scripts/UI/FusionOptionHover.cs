using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElementalBuddies
{
    // Hover-Helfer für die Fusions-Optionen im Buddy-Info-Panel (zeigt die Verbindung in der Welt)
    public class FusionOptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action OnEnter;
        public Action OnExit;

        private bool _hovered;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            OnEnter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_hovered) return;
            _hovered = false;
            OnExit?.Invoke();
        }

        // Panel geschlossen / Button entfernt, während er gehovert ist
        void OnDisable() => OnPointerExit(null);
    }
}
