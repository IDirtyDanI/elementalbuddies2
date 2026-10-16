using UnityEngine;

namespace ElementalBuddies
{
    // Reine Rechenregeln der Flieger (FlyingMotor, FlyerHurtbox). Eigene Assembly (ElementalBuddies.Rules), damit die
    // EditMode-Tests (FlyingMotorTests) sie ohne Assembly-CSharp prüfen können. Walddorf-Plan „Flieger-Mechanik“.
    public static class FlightMath
    {
        // Sollhöhe über einer Stelle: max(Boden + Flughöhe; Hindernisoberkante + Abstand).
        // groundY = Boden (Gelände/Straße), topY = höchste getroffene Oberfläche (Dach, Mauer, Baum, sonst = Boden).
        public static float RequiredAltitude(float groundY, float topY, float flyHeight, float clearance)
        {
            return Mathf.Max(groundY + flyHeight, topY + clearance);
        }

        // Höhe, die der Flieger JETZT haben muss, um eine Stelle in horizontaler Entfernung distance mit der Steigrate
        // climbRate rechtzeitig auf ihrer Sollhöhe required zu überfliegen (frühzeitig steigen statt in die Wand).
        // Ohne Tempo (steht) zählt nur die Stelle unter ihm (distance 0).
        public static float AnticipatedAltitude(float required, float distance, float speed, float climbRate)
        {
            if (distance <= 0f) return required;
            if (speed <= 0.01f || climbRate <= 0f) return float.NegativeInfinity;
            return required - distance / speed * climbRate;
        }

        // Abtastpunkte entlang der Flugrichtung (m) und Planungsregeln: Ein Hindernis an Abtastpunkt d kann schon direkt
        // hinter dem vorigen Punkt beginnen → mit d − SampleSpacing rechnen; geplant wird mit 70 % der Steigrate (Reserve
        // für das weiche Einregeln von StepAltitude). Damit liegt der Flieger an der Hauskante ≥ Dach + 1,3 m.
        public static readonly float[] LookAheadDistances = { 0f, 2f, 4f, 6f, 8f, 10f };
        public const float SampleSpacing = 2f;
        public const float PlanRateFactor = 0.7f;

        // Sollhöhe jetzt aus den Sollhöhen required[i] an den Abtastpunkten LookAheadDistances[i]
        // (count = Anzahl gültiger Einträge; ohne Tempo zählt nur Punkt 0)
        public static float PlanAltitude(float[] required, int count, float speed, float climbRate)
        {
            float best = float.NegativeInfinity;
            for (int i = 0; i < count && i < LookAheadDistances.Length; i++)
            {
                float d = Mathf.Max(0f, LookAheadDistances[i] - (i > 0 ? SampleSpacing : 0f));
                float need = i == 0 ? required[i] : AnticipatedAltitude(required[i], Mathf.Max(0.001f, d), speed, climbRate * PlanRateFactor);
                if (need > best) best = need;
            }
            return best;
        }

        // Ein Höhenschritt: proportional zur Abweichung (weiches Ankommen), Steig-/Sinkrate höchstens maxRate.
        public static float StepAltitude(float current, float target, float maxRate, float dt, float gain = 6f)
        {
            if (dt <= 0f) return current;
            float v = Mathf.Clamp((target - current) * gain, -maxRate, maxRate);
            float next = current + v * dt;
            // nicht überschießen
            if ((target - current) * (target - next) < 0f) next = target;
            return next;
        }

        // Horizontale Blickrichtung mit begrenzter Drehrate (Grad/s) zur Wunschrichtung drehen (beide y = 0).
        public static Vector3 TurnTowards(Vector3 heading, Vector3 desired, float maxDegPerSec, float dt)
        {
            heading.y = 0f;
            desired.y = 0f;
            if (desired.sqrMagnitude < 1e-6f) return heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.forward;
            desired.Normalize();
            if (heading.sqrMagnitude < 1e-6f) return desired;
            heading.Normalize();
            float maxRad = Mathf.Max(0f, maxDegPerSec) * Mathf.Deg2Rad * Mathf.Max(0f, dt);
            Vector3 r = Vector3.RotateTowards(heading, desired, maxRad, 0f);
            r.y = 0f;
            return r.sqrMagnitude > 1e-6f ? r.normalized : desired;
        }

        // Position (x/z) in ein Quadrat um center mit halber Kantenlänge half begrenzen (Kartengrenze ±62 m).
        public static Vector3 ClampToSquare(Vector3 p, Vector2 center, float half)
        {
            if (half <= 0f) return p;
            p.x = Mathf.Clamp(p.x, center.x - half, center.x + half);
            p.z = Mathf.Clamp(p.z, center.y - half, center.y + half);
            return p;
        }

        // Abstoßung von einem Nachbarn (horizontal): 0 ab radius, linear stärker bis 1 bei Abstand 0.
        // Liegen beide genau übereinander, wird fallback (Einheitsvektor) benutzt.
        public static Vector3 SeparationPush(Vector3 self, Vector3 other, float radius, Vector3 fallback)
        {
            Vector3 d = self - other;
            d.y = 0f;
            float dist = d.magnitude;
            if (radius <= 0f || dist >= radius) return Vector3.zero;
            Vector3 dir = dist > 1e-4f ? d / dist : fallback;
            dir.y = 0f;
            return dir * (1f - dist / radius);
        }

        // Trefferkapsel: von der Körperoberkante (bodyY + topOffset) bis minAboveGround über den Boden darunter.
        // Liefert Unterkante und Oberkante in Weltkoordinaten; nie kürzer als minHeight (ab der Oberkante nach unten).
        public static void HurtboxSpan(float bodyY, float topOffset, float groundY, float minAboveGround, float minHeight,
                                       out float bottom, out float top)
        {
            top = bodyY + Mathf.Max(0f, topOffset);
            bottom = groundY + Mathf.Max(0f, minAboveGround);
            if (top - bottom < minHeight) bottom = top - minHeight;
        }

        // Kreisbogen um einen Punkt (horizontal): Tangente + Rückführung auf den Wunschradius. Liefert Geschwindigkeit.
        // side = +1 gegen, −1 im Uhrzeigersinn (von oben gesehen).
        public static Vector3 OrbitVelocity(Vector3 pos, Vector3 center, float radius, float speed, float side)
        {
            Vector3 d = pos - center;
            d.y = 0f;
            float r = d.magnitude;
            Vector3 dir = r > 1e-4f ? d / r : Vector3.forward;
            Vector3 tangent = Vector3.Cross(Vector3.up, dir) * (side >= 0f ? 1f : -1f);
            float correction = Mathf.Clamp((radius - r) * 2f, -speed, speed);
            Vector3 v = tangent * speed + dir * correction;
            v.y = 0f;
            return v;
        }

        // Horizontaler Abstand
        public static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
