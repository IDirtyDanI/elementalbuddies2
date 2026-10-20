using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElementalBuddies
{
    // Langes Drücken auf ein UI-Element (Paket U, Touch-Ersatz für Hover): nach MobileUiMath.LongPressSeconds (0,4 s)
    // ohne Verrutschen meldet OnLongPress(true), beim Loslassen/Verlassen OnLongPress(false). Wurde lange gedrückt, löst
    // der Knopf beim Loslassen NICHT aus (eligibleForClick = false – das Eingabemodul prüft das nach PointerUp).
    // Muss auf demselben GameObject wie der Button liegen (dorthin gehen PointerDown/Up). Implementiert bewusst keine
    // Drag-Schnittstellen, damit Bildlauf in Listen (ScrollRect) weiter funktioniert. Funktioniert auch mit der Maus.
    public class MobileUiLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public event Action<bool> OnLongPress;

        private PointerEventData _ped;
        private Vector2 _start;
        private float _downAt = -1f;
        private bool _fired;

        public bool IsHeld => _fired;

        public static MobileUiLongPress Attach(GameObject go)
        {
            if (go == null) return null;
            var lp = go.GetComponent<MobileUiLongPress>();
            if (lp == null) lp = go.AddComponent<MobileUiLongPress>();
            return lp;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_ped != null) return; // zweiter Finger ignorieren
            _ped = eventData;
            _start = eventData.position;
            _downAt = Time.unscaledTime;
            _fired = false;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != _ped) return;
            if (_fired) eventData.eligibleForClick = false; // kein Klick nach langem Drücken
            End();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Touch: Verlassen kommt nach dem Loslassen; Maus: Zeiger vom Knopf gezogen
            if (eventData == _ped && !_fired) Cancel();
        }

        void Update()
        {
            if (_ped == null || _fired) return;
            if (_ped.dragging || MobileUiMath.MovedTooFar((_ped.position - _start).magnitude, MobilePlatform.Dpi)) { Cancel(); return; }
            if (Time.unscaledTime - _downAt < MobileUiMath.LongPressSeconds) return;
            _fired = true;
            OnLongPress?.Invoke(true);
        }

        void OnDisable() => End();

        private void Cancel()
        {
            _ped = null;
            _downAt = -1f;
            _fired = false;
        }

        private void End()
        {
            bool was = _fired;
            Cancel();
            if (was) OnLongPress?.Invoke(false);
        }
    }
}
