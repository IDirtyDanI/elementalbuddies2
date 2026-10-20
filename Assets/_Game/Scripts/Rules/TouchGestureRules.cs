using UnityEngine;

namespace ElementalBuddies
{
    // Reine Gesten-Regeln für einen Finger in der Welt-Zone (Mobil-Version, Paket B „Bauen“): Tippen vs. Ziehen vs.
    // langes Drücken. Eigene Assembly (ElementalBuddies.Rules), damit EditMode-Tests (TouchGestureRulesTests) sie ohne
    // Szene prüfen können. Schwellen in dp bzw. Sekunden; der Aufrufer rechnet dp mit MobilePlatform.DpToPixels in Pixel um.
    //
    // Ablauf: Begin beim Aufsetzen → Pending. Bewegt sich der Finger weiter als slop vom Startpunkt → Drag (endgültig, ein
    // langes Drücken ist dann nicht mehr möglich). Bleibt er longPress Sekunden innerhalb von slop → LongPress (einmalig).
    // Loslassen im Zustand Pending (kurz, kaum bewegt) = Tap. Cancel (z. B. zweiter Finger → Kamera-Geste) beendet alles.
    public enum TouchGestureState { None, Pending, Drag, LongPress, Cancelled }

    // Ereignis, das in diesem Schritt neu eingetreten ist
    public enum TouchGestureEvent { None, Tap, DragStarted, LongPress }

    public struct TouchGesture
    {
        // Bis zu diesem Weg (dp) gilt der Finger noch als ruhend (Tippen / langes Drücken)
        public const float TapSlopDp = 12f;
        // Ab dieser Haltedauer (s) ohne Bewegung: langes Drücken (Ping)
        public const float LongPressSeconds = 0.5f;

        public Vector2 StartPos;
        public float StartTime;
        public Vector2 LastPos;
        public TouchGestureState State;

        public bool Active => State == TouchGestureState.Pending || State == TouchGestureState.Drag || State == TouchGestureState.LongPress;

        public static TouchGesture Begin(Vector2 pos, float time)
        {
            return new TouchGesture { StartPos = pos, StartTime = time, LastPos = pos, State = TouchGestureState.Pending };
        }

        // Pro Frame, solange der Finger liegt. Liefert das neu eingetretene Ereignis (DragStarted / LongPress) oder None.
        public TouchGestureEvent Update(Vector2 pos, float time, float slopPx, float longPressSeconds)
        {
            LastPos = pos;
            if (State != TouchGestureState.Pending) return TouchGestureEvent.None;
            if ((pos - StartPos).sqrMagnitude > slopPx * slopPx)
            {
                State = TouchGestureState.Drag;
                return TouchGestureEvent.DragStarted;
            }
            if (time - StartTime >= longPressSeconds)
            {
                State = TouchGestureState.LongPress;
                return TouchGestureEvent.LongPress;
            }
            return TouchGestureEvent.None;
        }

        // Finger losgelassen. Tap, wenn noch Pending; ein verpasstes Ziehen/langes Drücken (Frame-Ruckler) wird nachgeholt.
        public TouchGestureEvent End(Vector2 pos, float time, float slopPx, float longPressSeconds)
        {
            bool wasPending = State == TouchGestureState.Pending;
            var e = Update(pos, time, slopPx, longPressSeconds);
            State = TouchGestureState.None;
            if (!wasPending) return TouchGestureEvent.None;
            return e == TouchGestureEvent.None ? TouchGestureEvent.Tap : e;
        }

        public void Cancel() => State = TouchGestureState.Cancelled;
    }
}
