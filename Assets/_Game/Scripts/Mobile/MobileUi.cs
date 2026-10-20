using UnityEngine;
using UnityEngine.InputSystem;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace ElementalBuddies
{
    // Gemeinsame Helfer der Mobil-UI (Paket U): aktuelle Skalierungs-Verstärkung, Touch-Texte statt Tasten-/Maus-Hinweisen,
    // gehaltene Finger (langes Drücken) und dp-Umrechnung. Nur im Touch-Modus wirksam (MobilePlatform.TouchMode);
    // auf dem PC liefern alle Abfragen die neutralen Werte (Verstärkung 1, Texte unverändert).
    public static class MobileUi
    {
        // ---------------- Skalierung ----------------

        private static int _boostFrame = -1;
        private static float _boost = 1f;
        private static Vector2Int _lastScreen;
        private static Rect _lastSafe;
        private static float _lastDpi;

        // Verstärkung der Canvas-Skalierung (1 = wie PC), aus MobileUiMath.Boost; neu berechnet bei Auflösungs-,
        // Orientierungs- oder Safe-Area-Wechsel
        public static float Boost
        {
            get
            {
                if (!MobilePlatform.TouchMode) return 1f;
                if (_boostFrame == Time.frameCount) return _boost;
                _boostFrame = Time.frameCount;
                var screen = new Vector2Int(Screen.width, Screen.height);
                Rect safe = MobilePlatform.SafeArea;
                float dpi = MobilePlatform.Dpi;
                if (screen != _lastScreen || safe != _lastSafe || !Mathf.Approximately(dpi, _lastDpi))
                {
                    _lastScreen = screen;
                    _lastSafe = safe;
                    _lastDpi = dpi;
                    float sw = safe.width > 1f ? safe.width : screen.x;
                    float sh = safe.height > 1f ? safe.height : screen.y;
                    _boost = MobileUiMath.Boost(screen.x, screen.y, sw, sh, dpi);
                }
                return _boost;
            }
        }

        // Pixel je Einheit eines Canvas (Overlay: lossyScale der Wurzel); Fallback 1
        public static float PixelsPerUnit(Transform t)
        {
            if (t == null) return 1f;
            float s = Mathf.Abs(t.lossyScale.x);
            return s > 0.0001f ? s : 1f;
        }

        // Mindest-Touch-Größe (48 dp) in lokalen Einheiten von t
        public static float MinTouchUnits(Transform t) =>
            MobilePlatform.DpToPixels(MobilePlatform.MinTouchTargetDp) / PixelsPerUnit(t);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _boostFrame = -1;
            _boost = 1f;
            _lastScreen = Vector2Int.zero;
            _lastSafe = default;
            _lastDpi = 0f;
            _mouseDownAt = -1f;
        }

        // ---------------- Texte ----------------

        // Maus-Formulierungen in Fähigkeits-/Champion-Beschreibungen und Hinweisen durch Touch-Formulierungen ersetzen.
        // Auf dem PC unverändert.
        public static string TouchText(string s)
        {
            if (!MobilePlatform.TouchMode || string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf("Maus", System.StringComparison.Ordinal) < 0 && s.IndexOf("lick", System.StringComparison.Ordinal) < 0) return s;
            s = s.Replace("in Richtung Mauszeiger", "in Zielrichtung")
                 .Replace("Richtung Mauszeiger", "in Zielrichtung")
                 .Replace("zum Mauszeiger", "zum Zielpunkt")
                 .Replace("am Mauszeiger", "am Zielpunkt")
                 .Replace("auf den Mauszeiger", "auf den Zielpunkt")
                 .Replace("Mauszeiger", "Zielpunkt")
                 .Replace("Linksklick", "Antippen")
                 .Replace("Rechtsklick", "Abbrechen-Knopf")
                 .Replace("anklicken", "antippen")
                 .Replace("Anklicken", "Antippen")
                 .Replace("klicken", "tippen")
                 .Replace("Klicken", "Tippen")
                 .Replace("klicke", "tippe")
                 .Replace("Klicke", "Tippe")
                 .Replace("Klick ", "Tipp ")
                 .Replace("klick ", "tipp ");
            return s;
        }

        // ---------------- Gehaltene Finger (langes Drücken ohne Raycast-Ziel) ----------------

        private static float _mouseDownAt = -1f;
        private static Vector2 _mouseDownPos;

        // Sucht einen Finger, der in einem Bereich aufgesetzt wurde (startsInside) und dort mindestens minHold Sekunden
        // fast still gehalten wird. Fällt ohne Touchscreen auf die linke Maustaste zurück (Editor mit erzwungenem
        // Touch-Modus). position = aktuelle Bildschirmposition.
        public static bool TryGetHeldPointer(System.Predicate<Vector2> startsInside, float minHold, out Vector2 position)
        {
            position = default;
            if (startsInside == null) return false;
            float dpi = MobilePlatform.Dpi;
            if (Touchscreen.current != null)
            {
                TouchZones.EnsureEnabled();
                double now = Time.realtimeSinceStartupAsDouble;
                foreach (var t in ETouch.activeTouches)
                {
                    if (!t.isInProgress) continue;
                    if (!startsInside(t.startScreenPosition)) continue;
                    float held = (float)(now - t.startTime);
                    float moved = (t.screenPosition - t.startScreenPosition).magnitude;
                    if (MobileUiMath.MovedTooFar(moved, dpi) || held < minHold) continue;
                    position = t.screenPosition;
                    return true;
                }
                return false;
            }

            var mouse = Mouse.current;
            if (mouse == null) return false;
            Vector2 pos = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame) { _mouseDownAt = Time.unscaledTime; _mouseDownPos = pos; }
            if (!mouse.leftButton.isPressed) { _mouseDownAt = -1f; return false; }
            if (_mouseDownAt < 0f || !startsInside(_mouseDownPos)) return false;
            if (MobileUiMath.MovedTooFar((pos - _mouseDownPos).magnitude, dpi)) return false;
            if (Time.unscaledTime - _mouseDownAt < minHold) return false;
            position = pos;
            return true;
        }

        // Neu aufgesetzter Finger (bzw. Mausklick) in diesem Frame – für Klick-Sounds
        public static bool PointerPressedThisFrame(out Vector2 position)
        {
            position = default;
            var ts = Touchscreen.current;
            if (ts != null)
            {
                TouchZones.EnsureEnabled();
                foreach (var t in ETouch.activeTouches)
                    if (t.phase == UnityEngine.InputSystem.TouchPhase.Began) { position = t.screenPosition; return true; }
                return false;
            }
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;
            position = mouse.position.ReadValue();
            return true;
        }

        // ---------------- Hierarchie ----------------

        // Kind mit Namen (rekursiv, auch inaktive)
        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
