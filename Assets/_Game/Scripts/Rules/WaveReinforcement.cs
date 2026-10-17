using System;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Wellen-Dichte (Plan Design/Wellen-Dichte-Plan, 2026-10-16) – reine Rechenregeln, damit EditMode-Tests sie ohne
    // Szene prüfen können (und balance_model.py sie 1:1 nachbaut). Generisch über den Gegnertyp T, weil die Test-
    // Assembly nur ElementalBuddies.Rules sieht (EnemyConfigSO liegt in Assembly-CSharp); der WaveManager nutzt
    // T = EnemyConfigSO.
    //  A  Verstärkungs-Stöße: In Einnahme-Wellen (Schrein/Händler) wird die Lücke bis zur Einnahme-Dauer nicht mehr mit
    //     Läufern im Läufer-Takt aufgefüllt, sondern mit gemischten, anwachsenden Stößen (Crescendo).
    //  B  Schwarm in Rudeln: Gruppen mit sehr kurzem Intervall spawnen in Rudeln, verteilt über die Hauptphase.
    public static class WaveReinforcement
    {
        // Stellschrauben der Verstärkung (Felder am WaveManager)
        public struct Settings
        {
            public float Density;       // Anteil der Hauptphasen-Dichte
            public float MaxShare;      // Deckel: Anteil der Normalgegner der Hauptphase
            public float PulseInterval; // Ziel-Abstand der Stöße (s)
            public float Crescendo;     // letzter Stoß ≈ (1 + Crescendo) × erster
            public float PackSpacing;   // Abstand im Stoß (s), Obergrenze
        }

        // Normalgruppe der Hauptphase (ohne Bosse, ohne Flieger)
        public struct Group<T>
        {
            public T Config;
            public int Count;
            public Group(T config, int count) { Config = config; Count = count; }
        }

        // Ein Stoß: Startzeit relativ zum Beginn der Verstärkung (= Ende der Hauptphase), Abstand im Stoß, Gegner
        public class Pulse<T>
        {
            public float Start;
            public float Spacing;
            public List<T> Enemies = new List<T>();
        }

        public class Plan<T>
        {
            public List<Pulse<T>> Pulses = new List<Pulse<T>>();
            public int Total;
        }

        // Unterer Stoß-Abstand und Mindestpause zwischen zwei Stoß-Starts (s)
        public const double MinSpacing = 0.08;
        public const double MinPulseGap = 0.5;

        // Normalgruppen einer Welle: Count > 0, nicht ausgeschlossen (Boss/Flieger); mainNormalCount = Summe der Counts
        public static List<Group<T>> NormalGroups<T>(IList<T> types, IList<int> counts, Func<T, bool> isExcluded, out int mainNormalCount)
        {
            var list = new List<Group<T>>();
            mainNormalCount = 0;
            if (types == null || counts == null) return list;
            for (int i = 0; i < types.Count && i < counts.Count; i++)
            {
                T t = types[i];
                if (t == null || counts[i] <= 0) continue;
                if (isExcluded != null && isExcluded(t)) continue;
                list.Add(new Group<T>(t, counts[i]));
                mainNormalCount += counts[i];
            }
            return list;
        }

        // Verstärkungsplan für die Lücke gap (s) nach der Hauptphase. legacyInterval = bisheriges Läufer-Intervall
        // (max(0,2; erste Gruppe)) – die Verstärkung bringt mindestens so viele Gegner wie die alte Läufer-Füllung.
        public static Plan<T> Build<T>(IList<Group<T>> groups, float mainDuration, int mainNormalCount, float gap, float legacyInterval, Settings s)
        {
            var plan = new Plan<T>();
            if (gap <= 0f || groups == null || groups.Count == 0) return plan;
            int total = 0;
            foreach (var g in groups) total += Math.Max(0, g.Count);
            if (total <= 0) return plan;

            double G = gap;
            // Menge: Dichte der Hauptphase × Density, gedeckelt, mindestens die alte Läufer-Füllung
            double rate = mainNormalCount / Math.Max(1.0, mainDuration);
            int legacy = (int)Math.Ceiling(G / Math.Max(1e-3, (double)legacyInterval));
            int n = (int)Math.Ceiling(G * rate * s.Density);
            n = Math.Min(n, (int)Math.Ceiling(s.MaxShare * (double)mainNormalCount));
            n = Math.Max(n, legacy);
            if (n <= 0) return plan;

            // Typ-Aufteilung anteilig zu den Gruppen-Counts (Largest-Remainder, Gleichstand: frühere Gruppe)
            int gc = groups.Count;
            var weights = new double[gc];
            for (int i = 0; i < gc; i++) weights[i] = Math.Max(0, groups[i].Count);
            int[] alloc = LargestRemainder(weights, n, false);

            // Gemischte Reihenfolge: jeweils der Typ mit dem größten Rückstand auf seinen Soll-Anteil.
            // Ganzzahlig verglichen (alloc·(i+1) − gewählt·N ≙ (alloc·(i+1)/N − gewählt)·N) → exakte Gleichstände.
            var chosen = new int[gc];
            var sequence = new List<T>(n);
            for (int i = 0; i < n; i++)
            {
                int best = -1;
                long bestScore = long.MinValue;
                for (int t = 0; t < gc; t++)
                {
                    long score = (long)alloc[t] * (i + 1) - (long)chosen[t] * n;
                    if (score > bestScore) { bestScore = score; best = t; }
                }
                chosen[best]++;
                sequence.Add(groups[best].Config);
            }

            // Stöße: Anzahl ~ gap / PulseInterval, Größen wachsen linear (Crescendo)
            int k = Math.Max(2, (int)Math.Round(G / Math.Max(1e-3, (double)s.PulseInterval), MidpointRounding.AwayFromZero));
            k = Math.Min(k, n);
            var w = new double[k];
            for (int j = 0; j < k; j++) w[j] = k > 1 ? 1.0 + s.Crescendo * j / (k - 1) : 1.0;
            int[] sizes = LargestRemainder(w, n, true);
            EnsureAtLeastOne(sizes);

            // Reihenfolge in Stöße zerschneiden, Startzeiten so, dass Stoß k bei (k+1)·gap/K endet
            int cursor = 0;
            double prev = double.NegativeInfinity;
            for (int j = 0; j < k; j++)
            {
                var pulse = new Pulse<T>();
                int size = sizes[j];
                for (int e = 0; e < size; e++) pulse.Enemies.Add(sequence[cursor++]);
                double spacing = Math.Min(s.PackSpacing, 0.6 * (G / k) / Math.Max(1, size - 1));
                spacing = Math.Max(MinSpacing, Math.Min(s.PackSpacing, spacing));
                double start = Math.Max(prev + MinPulseGap, (j + 1) * G / k - (size - 1) * spacing);
                start = Math.Max(0.0, start);
                prev = start;
                pulse.Start = (float)start;
                pulse.Spacing = (float)spacing;
                plan.Pulses.Add(pulse);
            }
            plan.Total = n;
            return plan;
        }

        // Largest-Remainder: total auf die Gewichte verteilen. Gleichstand der Reste: frühere (laterFirst = false)
        // bzw. spätere (laterFirst = true) Position zuerst.
        public static int[] LargestRemainder(double[] weights, int total, bool laterFirst)
        {
            int c = weights.Length;
            var result = new int[c];
            if (c == 0 || total <= 0) return result;
            double sum = 0;
            foreach (var x in weights) sum += Math.Max(0, x);
            if (sum <= 0) { result[laterFirst ? c - 1 : 0] = total; return result; }
            var rest = new double[c];
            int given = 0;
            for (int i = 0; i < c; i++)
            {
                double q = total * Math.Max(0, weights[i]) / sum;
                result[i] = (int)Math.Floor(q);
                rest[i] = q - result[i];
                given += result[i];
            }
            var order = new List<int>(c);
            for (int i = 0; i < c; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int cmp = rest[b].CompareTo(rest[a]);
                if (cmp != 0) return cmp;
                return laterFirst ? b.CompareTo(a) : a.CompareTo(b);
            });
            for (int r = 0; r < total - given && r < c; r++) result[order[r]]++;
            return result;
        }

        // Jeder Stoß mind. 1 Gegner: leere Stöße bekommen einen vom größten (bei Gleichstand dem frühesten der
        // größten – so bleiben die Größen nicht fallend). Voraussetzung: Summe ≥ Anzahl Stöße.
        private static void EnsureAtLeastOne(int[] sizes)
        {
            for (int i = 0; i < sizes.Length; i++)
            {
                if (sizes[i] >= 1) continue;
                int big = 0;
                for (int j = 1; j < sizes.Length; j++) if (sizes[j] > sizes[big]) big = j;
                if (sizes[big] <= 1) break;
                sizes[big]--;
                sizes[i]++;
            }
        }

        // ---------------- B: Schwarm in Rudeln ----------------

        // Ein Rudel: Startzeit (relativ zum StartDelay der Welle) und Größe; innerhalb im Gruppen-Intervall
        public struct Pack
        {
            public float Start;
            public int Count;
            public Pack(float start, int count) { Start = start; Count = count; }
        }

        // Gruppe spawnt in Rudeln: sehr kurzes Intervall und mehr Gegner als ein Rudel
        public static bool UsesPacks(float interval, int count, float intervalThreshold, int packSize) =>
            packSize > 0 && count > packSize && interval < intervalThreshold;

        // Rudel einer Gruppe: packs = ⌈count / packSize⌉ (volle Rudel, das letzte nimmt den Rest). Startzeiten gleichmäßig
        // zwischen delay und spanEnd = max(eigenes Ende ohne Rudel, othersEnd) − Dauer des letzten Rudels.
        // othersEnd = Ende der übrigen Nicht-Rudel-Gruppen (Delay + (Count−1)·Interval; keine → −∞).
        public static List<Pack> PackSchedule(float delay, int count, float interval, int packSize, float othersEnd)
        {
            var packs = new List<Pack>();
            if (count <= 0) return packs;
            if (packSize <= 0) packSize = count;
            int n = (count + packSize - 1) / packSize;
            int last = count - (n - 1) * packSize;
            double ownEnd = delay + (count - 1) * (double)interval;
            double spanEnd = Math.Max(ownEnd, othersEnd) - (last - 1) * (double)interval;
            for (int j = 0; j < n; j++)
            {
                double start = n > 1 ? delay + j * (spanEnd - delay) / (n - 1) : delay;
                packs.Add(new Pack((float)start, j < n - 1 ? packSize : last));
            }
            return packs;
        }
    }
}
