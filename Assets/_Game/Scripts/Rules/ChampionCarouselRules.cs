using System;

namespace ElementalBuddies
{
    // Reine Regeln der Champion-Auswahl im Hauptmenü (Karte mit Pfeilen, 2026-11-09): endloses Blättern, Richtung eines
    // Wechsels, Wisch-Erkennung und Positionsanzeige. Eigene Assembly (ElementalBuddies.Rules) für die EditMode-Tests.
    // Die Anzahl der Klassen kommt immer als Parameter (Resources/ChampionRoster), nichts ist auf 5 festgelegt.
    public static class ChampionCarouselRules
    {
        // Bis zu so vielen Klassen zeigt die Karte Punkte (●●●○○), darüber „3 / 12“
        public const int MaxDots = 8;

        // Wischen: höchstens so lange (s) und deutlich waagerecht (|dx| ≥ Faktor · |dy|)
        public const float SwipeMaxSeconds = 0.8f;
        public const float SwipeHorizontalRatio = 1.4f;

        // Index endlos auf 0 … count−1 abbilden (−1 → letzte Klasse, count → erste)
        public static int Wrap(int index, int count)
        {
            if (count <= 0) return 0;
            int m = index % count;
            return m < 0 ? m + count : m;
        }

        // Nächster Index beim Blättern um delta (±1), mit Umlauf
        public static int Step(int index, int delta, int count) => Wrap(index + delta, count);

        // Blätterrichtung von from nach to auf dem kürzeren Weg im Kreis: +1 vorwärts, −1 rückwärts, 0 = gleich.
        // Gleich weit (z. B. zwei Klassen) zählt als vorwärts.
        public static int Direction(int from, int to, int count)
        {
            if (count <= 1) return 0;
            from = Wrap(from, count);
            to = Wrap(to, count);
            if (from == to) return 0;
            int forward = Wrap(to - from, count);
            return forward <= count - forward ? 1 : -1;
        }

        // Wischgeste (Bildschirm-Pixel, y nach oben): +1 = nächste Klasse (nach links gewischt, wie Blättern),
        // −1 = vorige (nach rechts gewischt), 0 = keine Wischgeste (zu kurz, zu langsam oder zu steil)
        public static int SwipeStep(float dx, float dy, float seconds, float minDistance)
        {
            if (seconds < 0f || seconds > SwipeMaxSeconds) return 0;
            float ax = Math.Abs(dx), ay = Math.Abs(dy);
            if (ax < Math.Max(1f, minDistance)) return 0;
            if (ax < SwipeHorizontalRatio * ay) return 0;
            return dx < 0f ? 1 : -1;
        }

        // Punkte statt Zahl?
        public static bool UseDots(int count) => count > 1 && count <= MaxDots;

        // Positionsanzeige als Text, z. B. „3 / 5“ (index 0-basiert)
        public static string PositionText(int index, int count)
        {
            if (count <= 0) return "";
            return (Wrap(index, count) + 1) + " / " + count;
        }

        // Wechsel-Animation: Fortschritt 0 … 1 weich ausrollen (ease-out kubisch) bzw. anlaufen (ease-in kubisch)
        public static float EaseOut(float t)
        {
            t = Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        public static float EaseIn(float t)
        {
            t = Clamp01(t);
            return t * t * t;
        }

        private static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
    }
}
