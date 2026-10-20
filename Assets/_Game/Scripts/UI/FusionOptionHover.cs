using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElementalBuddies
{
    // Hover-Helfer für die Fusions-Optionen im Buddy-Info-Panel (zeigt die Verbindung in der Welt)
    // Touch (Paket U): Ein Finger löst beim Aufsetzen schon „Enter“ aus – dort zeigt erst langes Drücken (≥ 0,4 s) die
    // Verbindung, Loslassen blendet sie aus, und der Knopf verschmilzt dann NICHT (kein Klick nach langem Drücken).
    // Kurzes Tippen verschmilzt wie ein Klick.
    public class FusionOptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Action OnEnter;
        public Action OnExit;

        private bool _hovered;

        // Touch
        private PointerEventData _press;
        private Vector2 _pressStart;
        private float _pressAt;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (MobilePlatform.TouchMode) return; // Touch: erst nach langem Drücken (Update)
            _hovered = true;
            OnEnter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (MobilePlatform.TouchMode && eventData != null && eventData == _press) return; // Touch: endet mit PointerUp
            if (!_hovered) return;
            _hovered = false;
            OnExit?.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!MobilePlatform.TouchMode || _press != null) return;
            _press = eventData;
            _pressStart = eventData.position;
            _pressAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!MobilePlatform.TouchMode || eventData != _press) return;
            _press = null;
            if (!_hovered) return;
            eventData.eligibleForClick = false; // langes Drücken = nur ansehen
            _hovered = false;
            OnExit?.Invoke();
        }

        void Update()
        {
            if (_press == null || _hovered) return;
            if (_press.dragging || MobileUiMath.MovedTooFar((_press.position - _pressStart).magnitude, MobilePlatform.Dpi)) { _press = null; return; }
            if (Time.unscaledTime - _pressAt < MobileUiMath.LongPressSeconds) return;
            _hovered = true;
            OnEnter?.Invoke();
        }

        // Panel geschlossen / Button entfernt, während er gehovert ist
        void OnDisable()
        {
            _press = null;
            OnPointerExit(null);
        }
    }
}
