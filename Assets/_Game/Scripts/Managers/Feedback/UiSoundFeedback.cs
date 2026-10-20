using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Klick- und Hover-Sounds für alle interaktiven UI-Elemente (Touch: nur Klick, siehe UpdateTouch) (auch zur Laufzeit erzeugte Karten/Buttons),
    // ohne jeden Button einzeln zu verdrahten: Raycast unter dem Mauszeiger. Wird von GameAudio angehängt.
    public class UiSoundFeedback : MonoBehaviour
    {
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private PointerEventData _ped;
        private EventSystem _pedSystem;
        private Selectable _hovered;

        void Update()
        {
            if (MobilePlatform.TouchMode) { UpdateTouch(); return; }
            var es = EventSystem.current;
            var mouse = Mouse.current;
            if (es == null || mouse == null) return;

            if (_ped == null || _pedSystem != es) { _ped = new PointerEventData(es); _pedSystem = es; }
            _ped.position = mouse.position.ReadValue();
            _hits.Clear();
            es.RaycastAll(_ped, _hits);

            Selectable sel = null;
            foreach (var h in _hits)
            {
                if (h.gameObject == null) continue;
                sel = h.gameObject.GetComponentInParent<Selectable>();
                break; // nur das oberste Element zählt
            }
            if (sel != null && (!sel.IsInteractable() || !sel.isActiveAndEnabled)) sel = null;

            if (sel != _hovered)
            {
                _hovered = sel;
                if (sel != null) GameAudio.Play(SfxId.UiHover);
            }
            if (sel != null && mouse.leftButton.wasPressedThisFrame) GameAudio.Play(SfxId.UiClick);
        }

        // Touch (Paket U): kein Hover – ein Finger, der über die Knöpfe streicht, spielt keine Hover-Sounds.
        // Nur der Klick-Sound beim Aufsetzen auf ein bedienbares Element.
        private void UpdateTouch()
        {
            _hovered = null;
            var es = EventSystem.current;
            if (es == null || !MobileUi.PointerPressedThisFrame(out var pos)) return;
            if (_ped == null || _pedSystem != es) { _ped = new PointerEventData(es); _pedSystem = es; }
            _ped.position = pos;
            _hits.Clear();
            es.RaycastAll(_ped, _hits);
            foreach (var h in _hits)
            {
                if (h.gameObject == null) continue;
                var sel = h.gameObject.GetComponentInParent<Selectable>();
                if (sel != null && sel.IsInteractable() && sel.isActiveAndEnabled) GameAudio.Play(SfxId.UiClick);
                break;
            }
        }
    }
}
