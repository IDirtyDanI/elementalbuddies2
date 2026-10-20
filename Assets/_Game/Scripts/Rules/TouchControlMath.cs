using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Reine Rechenregeln der Touch-Steuerung (Mobil-Version, Paket S): virtuelle Sticks, Zieh-Zielen von den
    // Fähigkeits-Knöpfen, Auto-Ziel, Zwei-Finger-Drehen und das Knopf-Layout. Eigene Assembly (ElementalBuddies.Rules),
    // damit EditMode-Tests sie ohne Assembly-CSharp prüfen können. Bildschirm-Größen in Pixeln (Umrechnung dp → px macht
    // der Aufrufer über MobilePlatform.DpToPixels), Welt-Größen in Metern.
    public static class TouchControlMath
    {
        // ---------------- Sticks ----------------

        // Stick-Wert aus dem Fingerversatz: Länge 0..1 (radius = voller Ausschlag), innerhalb der Totzone (Anteil des
        // Radius, 0..<1) zero. Außerhalb wird von der Totzone bis zum Rand neu auf 0..1 gestreckt, damit der Wert direkt
        // hinter der Totzone sanft bei 0 beginnt statt zu springen.
        public static Vector2 StickValue(Vector2 origin, Vector2 current, float radius, float deadzone)
        {
            if (radius <= 0f) return Vector2.zero;
            Vector2 d = current - origin;
            float mag = d.magnitude / radius;
            float dz = Mathf.Clamp(deadzone, 0f, 0.95f);
            if (mag <= dz || mag <= 0f) return Vector2.zero;
            float scaled = Mathf.Clamp01((mag - dz) / (1f - dz));
            return d / d.magnitude * scaled;
        }

        // Schwebender Stick: zieht der Finger über den Rand hinaus, wandert der Mittelpunkt hinterher (Abstand = radius)
        public static Vector2 FollowOrigin(Vector2 origin, Vector2 current, float radius)
        {
            Vector2 d = current - origin;
            float mag = d.magnitude;
            if (radius <= 0f || mag <= radius) return origin;
            return current - d / mag * radius;
        }

        // Kurzes Antippen: kurz genug und kaum bewegt (travel = größter Abstand zum Aufsetzpunkt)
        public static bool IsTap(float duration, float travel, float maxDuration, float maxTravel)
        {
            return duration >= 0f && duration <= maxDuration && travel <= maxTravel;
        }

        // Doppeltipp: zweiter Tipp innerhalb des Fensters nach dem ersten
        public static bool IsDoubleTap(float lastTapTime, float now, float window)
        {
            float dt = now - lastTapTime;
            return dt > 0f && dt <= window;
        }

        // ---------------- Zieh-Zielen (Knopf → Bodenpunkt) ----------------

        // Finger wurde vom Knopf weggezogen (offset = Finger − Knopfmitte). Ab diesem Abstand zählt es als Zielen.
        public static bool IsDragging(Vector2 offset, float startDistance) => offset.magnitude > startDistance;

        // Zurück auf den Knopf gezogen = abbrechen
        public static bool IsCancel(Vector2 offset, float cancelRadius) => offset.magnitude <= cancelRadius;

        // Fingerversatz vom Knopf → Richtung in der Welt (kamerabezogen wie das Laufen) und Entfernung in Metern.
        // deadPx: ab hier beginnt das Zielen (Entfernung minFraction × maxRange), fullPx: volle Reichweite.
        // false, solange der Finger in der Totzone liegt.
        public static bool DragToGround(Vector2 offset, float deadPx, float fullPx, float cameraYaw, float maxRange,
                                        float minFraction, out Vector3 direction, out float distance)
        {
            direction = Vector3.forward;
            distance = 0f;
            float mag = offset.magnitude;
            if (mag <= deadPx || mag <= 0f) return false;
            direction = CameraOrbitMath.InputToWorld(offset / mag, cameraYaw);
            if (direction.sqrMagnitude < 1e-8f) return false;
            direction.Normalize();
            float t = fullPx > deadPx ? Mathf.Clamp01((mag - deadPx) / (fullPx - deadPx)) : 1f;
            float range = Mathf.Max(0f, maxRange);
            distance = Mathf.Lerp(Mathf.Clamp01(minFraction) * range, range, t);
            return true;
        }

        // ---------------- Auto-Ziel ----------------

        // Suchradius fürs Auto-Ziel aus der Reichweite der Fähigkeit: Nahkampf (2–3 m) sucht trotzdem im Umkreis von
        // minRadius (Figur dreht sich zum Gegner), Fernkampf höchstens maxRadius (nicht quer über die Karte).
        // range ≤ 0 (unbekannt) → fallback.
        public static float AutoAimRadius(float range, float minRadius, float maxRadius, float fallback)
        {
            if (range <= 0f) return Mathf.Clamp(fallback, minRadius, maxRadius);
            return Mathf.Clamp(range + 1f, minRadius, maxRadius);
        }

        // Nächstes Ziel in Reichweite (waagerechte Entfernung). Gegner vor der Figur werden leicht bevorzugt:
        // Wertung = Entfernung × (1 + facingWeight × (1 − cos Winkel) / 2), d. h. direkt hinter der Figur zählt ein
        // Gegner wie (1 + facingWeight)-fach so weit weg. −1 = kein Ziel in Reichweite.
        public static int PickAutoTarget(Vector3 origin, Vector3 forward, IList<Vector3> candidates, float maxRange, float facingWeight)
        {
            if (candidates == null || candidates.Count == 0 || maxRange <= 0f) return -1;
            Vector3 f = new Vector3(forward.x, 0f, forward.z);
            bool hasForward = f.sqrMagnitude > 1e-6f;
            if (hasForward) f.Normalize();
            float maxSqr = maxRange * maxRange;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 to = candidates[i] - origin;
                to.y = 0f;
                float sqr = to.sqrMagnitude;
                if (sqr > maxSqr) continue;
                float dist = Mathf.Sqrt(sqr);
                float score = dist;
                if (hasForward && dist > 1e-4f)
                {
                    float cos = Vector3.Dot(f, to / dist);
                    score *= 1f + Mathf.Max(0f, facingWeight) * (1f - cos) * 0.5f;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        // ---------------- Zwei-Finger-Drehen ----------------

        // Drehwinkel (Grad) zwischen zwei Frames aus der Verbindungslinie zweier Finger. Positiv = gegen den Uhrzeigersinn
        // auf dem Bildschirm (y nach oben). Liegen die Finger zu dicht beieinander (< minSpan px), ist der Winkel zu
        // unruhig → 0.
        public static float TwistDelta(Vector2 prevA, Vector2 prevB, Vector2 curA, Vector2 curB, float minSpan)
        {
            Vector2 p = prevB - prevA;
            Vector2 c = curB - curA;
            float min = Mathf.Max(0f, minSpan);
            if (p.magnitude < min || c.magnitude < min || p.sqrMagnitude < 1e-6f || c.sqrMagnitude < 1e-6f) return 0f;
            float a0 = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            float a1 = Mathf.Atan2(c.y, c.x) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(a0, a1);
        }

        // Kamera-Gierwinkel aus der Fingerdrehung: die Welt dreht sich mit den Fingern (wie bei Karten-Apps). Dreht man
        // gegen den Uhrzeigersinn, muss die Kamera (Yaw positiv = im Uhrzeigersinn von oben) im Uhrzeigersinn schwenken.
        public static float TwistToYaw(float twistDegrees, float sensitivity) => twistDegrees * sensitivity;

        // ---------------- Layout ----------------

        // Punkt auf einem Bogen um center (Winkel in Grad, 0 = rechts, 90 = oben, gegen den Uhrzeigersinn)
        public static Vector2 ArcPoint(Vector2 center, float radius, float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        // Abstand der Mittelpunkte zweier Nachbarknöpfe auf dem Bogen (Sehne)
        public static float ArcSpacing(float radius, float stepDeg) => 2f * radius * Mathf.Sin(Mathf.Abs(stepDeg) * 0.5f * Mathf.Deg2Rad);

        // Skalierung, damit ein Cluster (Ausdehnung in dp) in seine Zone passt; nie größer als 1, nie kleiner als minScale
        // (Touch-Ziele dürfen nicht unter die Mindestgröße fallen).
        public static float FitScale(Vector2 clusterSize, Vector2 zoneSize, float minScale)
        {
            float s = 1f;
            if (clusterSize.x > 0f && zoneSize.x > 0f) s = Mathf.Min(s, zoneSize.x / clusterSize.x);
            if (clusterSize.y > 0f && zoneSize.y > 0f) s = Mathf.Min(s, zoneSize.y / clusterSize.y);
            return Mathf.Clamp(s, Mathf.Min(1f, Mathf.Max(0.01f, minScale)), 1f);
        }

        // Kreis-Treffer (Knöpfe)
        public static bool InCircle(Vector2 point, Vector2 center, float radius) => (point - center).sqrMagnitude <= radius * radius;
    }
}
