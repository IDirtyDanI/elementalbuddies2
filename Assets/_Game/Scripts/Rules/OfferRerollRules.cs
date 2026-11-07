using System;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Neu würfeln im Händlerladen und in der Kartenwahl nach der Welle (2026-11-02, Nutzerwunsch): IMMER alle Plätze
    // neu belegen – auch schon gekaufte (vorher wurden nur offene ersetzt; nach zwei Käufen kam so immer nur eine neue
    // Karte). Neu belegte Plätze sind wieder kaufbar; die Preisstaffel des Besuchs läuft weiter.
    public static class OfferRerollRules
    {
        // Zu ersetzende Plätze: alle. Leeres Angebot → emptyCount neue Plätze (z. B. Karten pro Kartenwahl).
        public static List<int> SlotsToReroll(int offerCount, int emptyCount)
        {
            int n = offerCount > 0 ? offerCount : Math.Max(0, emptyCount);
            var slots = new List<int>(n);
            for (int i = 0; i < n; i++) slots.Add(i);
            return slots;
        }

        // count verschiedene Karten aus pool ziehen. Bisher sichtbare Karten (current) werden gemieden, solange ohne sie
        // genug übrig bleiben. randomRange(n) liefert 0 … n−1. Weniger als count → Pool zu klein.
        public static List<int> DrawFresh(IList<int> current, IList<int> pool, int count, Func<int, int> randomRange)
        {
            var source = new List<int>(pool);
            if (current != null)
            {
                var fresh = new List<int>(source);
                foreach (int c in current) fresh.Remove(c);
                if (fresh.Count >= count) source = fresh;
            }
            var result = new List<int>(count);
            while (result.Count < count && source.Count > 0)
            {
                int idx = randomRange(source.Count);
                result.Add(source[idx]);
                source.RemoveAt(idx);
            }
            return result;
        }
    }
}
