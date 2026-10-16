using System;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Rückruf der liegenden Seelensplitter am Wellenende (#461, 2026-10-16) – reine Rechenregel, damit EditMode-Tests
    // sie ohne Szene prüfen können. Alle noch liegenden (oder schon fliegenden) Drops werden beim Wellenende EINMAL
    // gutgeschrieben – nach Wellenbonus und Zinsen, vor der Berechnung des Kartenladen-Anteils. Danach fliegen sie nur
    // noch optisch zum Spieler; beim Eintreffen wird nichts mehr gutgeschrieben. Betrag pro Welle unverändert (die Zinsen
    // sahen die liegenden Drops schon vorher nicht), nur der Zeitpunkt ist früher.
    public static class ShardRecall
    {
        // Summe aller noch nicht gutgeschriebenen Drops mit Wert > 0; markiert sie als gutgeschrieben.
        public static float CreditAll<T>(IEnumerable<T> drops, Func<T, float> value, Func<T, bool> isCredited, Action<T> markCredited)
        {
            if (drops == null) return 0f;
            float sum = 0f;
            foreach (var d in drops)
            {
                if (d == null || isCredited(d)) continue;
                float v = value(d);
                if (v <= 0f) continue;
                sum += v;
                markCredited(d);
            }
            return sum;
        }

        // Gutschrift beim Eintreffen eines Drops: 0, wenn er schon beim Rückruf gutgeschrieben wurde
        public static float CreditOnCollect(float value, bool credited) => credited || value <= 0f ? 0f : value;
    }
}
