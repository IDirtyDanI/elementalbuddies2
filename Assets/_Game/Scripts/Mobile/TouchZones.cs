using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

namespace ElementalBuddies
{
    // Aufteilung der Finger zwischen Steuerung (Sticks, Fähigkeits-Knöpfe), Bauen/Auswählen und Kamera-Gesten.
    // Wer einen Finger übernimmt, meldet ihn mit Claim(fingerIndex, owner) an; andere Systeme fragen IsClaimed ab und
    // ignorieren ihn. Finger, die auf einem UI-Element (EventSystem) beginnen, gehören der UI.
    //
    // Zonen (in Anteilen des sicheren Bereichs): links unten = Bewegungs-Stick, rechts unten = Angriffs-Stick +
    // Fähigkeits-Knöpfe. Alles andere (Mitte, oben) = Welt: Tippen wählt/baut (InteractionManager), zwei Finger drehen
    // die Kamera.
    public static class TouchZones
    {
        public enum Owner { None, MoveStick, AttackStick, AbilityButton, Build, Camera, UI }

        // Bewegungs-Stick: linker Rand bis MoveZoneWidth, unten bis MoveZoneHeight
        public const float MoveZoneWidth = 0.38f;
        public const float MoveZoneHeight = 0.62f;
        // Angriffs-Cluster: ab 1 - AttackZoneWidth, unten bis AttackZoneHeight
        public const float AttackZoneWidth = 0.34f;
        public const float AttackZoneHeight = 0.62f;

        private static readonly Dictionary<int, Owner> _claims = new Dictionary<int, Owner>();
        private static bool _enabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _claims.Clear();
            _enabled = false;
        }

        // EnhancedTouch einschalten (idempotent). Nur im Touch-Modus aufrufen.
        public static void EnsureEnabled()
        {
            if (_enabled) return;
            _enabled = true;
            EnhancedTouchSupport.Enable();
        }

        // Normierte Position im sicheren Bereich (0..1)
        public static Vector2 Normalized(Vector2 screenPos)
        {
            Rect s = MobilePlatform.SafeArea;
            return new Vector2((screenPos.x - s.xMin) / Mathf.Max(1f, s.width), (screenPos.y - s.yMin) / Mathf.Max(1f, s.height));
        }

        public static bool InMoveZone(Vector2 screenPos)
        {
            Vector2 n = Normalized(screenPos);
            return n.x <= MoveZoneWidth && n.y <= MoveZoneHeight;
        }

        public static bool InAttackZone(Vector2 screenPos)
        {
            Vector2 n = Normalized(screenPos);
            return n.x >= 1f - AttackZoneWidth && n.y <= AttackZoneHeight;
        }

        public static bool InWorldZone(Vector2 screenPos) => !InMoveZone(screenPos) && !InAttackZone(screenPos);

        public static void Claim(int fingerIndex, Owner owner) => _claims[fingerIndex] = owner;

        public static void Release(int fingerIndex) => _claims.Remove(fingerIndex);

        public static Owner OwnerOf(int fingerIndex) => _claims.TryGetValue(fingerIndex, out var o) ? o : Owner.None;

        public static bool IsClaimed(int fingerIndex) => _claims.ContainsKey(fingerIndex);

        // Beendete Finger aufräumen (einmal pro Frame von einem beliebigen Nutzer aufrufbar)
        public static void PruneEnded()
        {
            if (_claims.Count == 0) return;
            _tmp.Clear();
            foreach (var kv in _claims)
            {
                bool active = false;
                foreach (var t in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
                    if (t.finger.index == kv.Key) { active = true; break; }
                if (!active) _tmp.Add(kv.Key);
            }
            for (int i = 0; i < _tmp.Count; i++) _claims.Remove(_tmp[i]);
        }

        private static readonly List<int> _tmp = new List<int>();
    }
}
