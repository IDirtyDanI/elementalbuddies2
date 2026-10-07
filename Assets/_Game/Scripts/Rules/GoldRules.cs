using UnityEngine;

namespace ElementalBuddies
{
    // Reine Rechenregeln für Gold, Händlerpreise und Einnahme-Beteiligung. Liegt in einer eigenen Assembly
    // (ElementalBuddies.Rules), damit EditMode-Tests sie ohne Assembly-CSharp prüfen können.
    public static class GoldRules
    {
        // Einnahme-Bonus gleichmäßig auf alle Einnehmenden verteilen (60 Gold, 3 Spieler → je 20).
        // count <= 0 → 0 (niemand bekommt etwas).
        public static float SplitCaptureBonus(float total, int count)
        {
            if (count <= 0 || total <= 0f) return 0f;
            return total / count;
        }

        // Preis der nächsten Händlerkarte nach purchasesSoFar Käufen in diesem Besuch: 0, base, base+step, …
        // (Standard: gratis, 60, 90, 120 …)
        public static int CardPrice(int purchasesSoFar, int baseCost, int step)
        {
            if (purchasesSoFar <= 0) return 0;
            return Mathf.Max(0, baseCost + step * (purchasesSoFar - 1));
        }

        // Preis des nächsten Neu-Würfelns nach rerollsSoFar Würfen (Standard: 25, 40, 55 …)
        public static int RerollPrice(int rerollsSoFar, int baseCost, int step)
        {
            return Mathf.Max(0, baseCost + step * Mathf.Max(0, rerollsSoFar));
        }

        // Zählt ein Spieler zu den Einnehmenden? Wer beim Abschluss im Kreis steht, oder wer insgesamt
        // mindestens minSeconds im Kreis verbracht hat (kurzes Durchlaufen zählt nicht).
        public static bool IsCaptureContributor(float secondsInside, bool insideAtCompletion, float minSeconds)
        {
            return insideAtCompletion || secondsInside >= Mathf.Max(0f, minSeconds);
        }

        // Anzeige-Grund für die Wellen-Sperre, z. B. "Warte auf Kartenwahl (2/3)"
        public static string WaitReason(string what, int done, int total)
        {
            return $"Warte auf {what} ({Mathf.Clamp(done, 0, total)}/{total})";
        }
    }
}
