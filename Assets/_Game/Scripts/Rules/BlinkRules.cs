using System;

namespace ElementalBuddies
{
    // Entscheidungsregeln des Magier-Blinks (MageKit.BlinkTarget/SafeLanding) – reine Rechenregeln, damit EditMode-Tests
    // (BlinkRulesTests) sie ohne Szene prüfen können. Spieltest 2026-10-18, Paket M (Punkt 4 „Blink in der Luft“).
    //
    // Problem: SafeLanding suchte den NavMesh mit Radius 1,5 m um den Zielpunkt auf Flughöhe. In der Luft liegt der Boden
    // zu tief, alle Versuche scheiterten, die Figur blieb stehen. Regel: In der Luft wird waagerecht auf der aktuellen Höhe
    // teleportiert (danach fällt die Figur normal weiter), sofern unter dem Zielpunkt begehbarer Boden liegt.
    public static class BlinkRules
    {
        // Ab dieser Höhe der Füße über dem Boden gilt die Figur als „in der Luft“ (darunter: Landung wie am Boden,
        // fängt auch das kurze Flackern von isGrounded an Stufen/Hängen ab)
        public const float MinAirHeight = 0.4f;

        // Anzahl Rückschritte vom Ziel Richtung Start (wie bisher: 100 %, 75 %, 50 %, 25 % der Strecke)
        public const int RetreatSteps = 4;

        // Luft-Blink nur, wenn nicht am Boden UND die Füße deutlich über dem Boden sind
        // (feetAboveFloor = +∞, wenn unter der Figur gar kein Boden gefunden wurde)
        public static bool UseAirBlink(bool grounded, float feetAboveFloor)
        {
            return !grounded && feetAboveFloor >= MinAirHeight; // NaN → false (Boden-Verhalten)
        }

        // Anteil der Strecke Ziel → Start für Rückschritt step (0 = Ziel, steps−1 = kurz vor dem Start)
        public static float RetreatFraction(int step, int steps)
        {
            if (steps <= 0) return 0f;
            return Math.Max(0f, Math.Min(1f, step / (float)steps));
        }

        // Erster gültiger Rückschritt (0 = volle Strecke), −1 = keiner gültig → Figur bleibt stehen
        public static int FirstValidStep(int steps, Func<int, bool> isValid)
        {
            if (isValid == null) return -1;
            for (int i = 0; i < steps; i++)
                if (isValid(i)) return i;
            return -1;
        }

        // Höchstens so weit darf der Boden am Luft-Ziel über den Füßen liegen (Hang, Stufe). Höher (Mauerkrone, Dach)
        // → ungültig, sonst ließe sich per Sprung-Blink auf Kanten klettern
        public const float MaxAirStepUp = 0.5f;

        public static bool AirFloorReachable(float floorY, float feetY)
        {
            return floorY <= feetY + MaxAirStepUp;
        }

        // Drehpunkt-Höhe am Luft-Ziel: aktuelle Höhe halten; liegt der Boden dort höher als die Füße (Hang, Stufe),
        // wird die Figur auf den Boden gesetzt statt hineinzustecken
        public static float AirPivotY(float currentPivotY, float floorY, float pivotAboveFeet)
        {
            return Math.Max(currentPivotY, floorY + pivotAboveFeet);
        }

        // Luft-Blink: Fußweg vom Start zum Landepunkt höchstens so viel länger als die Luftlinie – sonst liegt eine Barriere
        // (Palisade, Kartenrand) dazwischen, die man nur durch die Luft überwinden würde
        public const float MaxPathFactor = 2.5f;
        public const float MaxPathDetour = 6f;

        public static bool AirPathShortEnough(float walkDistance, float straightDistance)
        {
            return walkDistance <= Math.Max(straightDistance * MaxPathFactor, straightDistance + MaxPathDetour);
        }
    }
}
