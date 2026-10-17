using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Champion-Bedrohungen (Design/Champion-Bedrohungen-Plan, Paket P1) – reine Rechenregeln, damit EditMode-Tests sie
    // ohne Szene prüfen können (Test-Assembly sieht nur ElementalBuddies.Rules):
    //  - Bannschild: welcher Treffer durchkommt, wann der Schild einer „Gebannt“-Elite bricht
    //  - Beschwörer: Ritualplatz-Wahl (am weitesten von allen Buddies), Gruppengröße (Eskalation)
    //  - Bannwirker: Zielwahl (größte Buddy-Gruppe), Opfer (nächste zum Zentrum), Halten des Banns
    //  - Unterbrechungs-Flag, Notbremse unerreichbarer Bedrohungen, Buddy-Zielfilter (Bannschild)
    //  - Zusatzgruppen mit festen Stufen (ab W8: 1, ab W14: 2 …)
    public static class ChampionThreatRules
    {
        // ---------------- Bannschild ----------------

        // Aktiver Schild lässt nur Champion-Treffer durch (EnemyBrain.DealPlayerDamage)
        public static bool WardBlocks(bool wardActive, bool championSource) => wardActive && !championSource;

        // „Gebannt“-Elite: Schild bricht, sobald der Champion-Schaden mindestens fraction × Max-HP erreicht.
        // fraction <= 0 = bricht nie (dauerhafter Schild von Beschwörer/Bannwirker).
        public static bool WardBreaks(float championDamage, float maxHp, float fraction)
        {
            if (fraction <= 0f || maxHp <= 0f) return false;
            return championDamage >= maxHp * fraction - 1e-4f;
        }

        // ---------------- Beschwörer ----------------

        // Ritualplatz-Kandidat: Platz frei/erreichbar, Abstand zum nächsten lebenden Buddy (∞ ohne Buddies),
        // Abstand zum Beschwörer (Gleichstand → näherer Platz)
        public struct SiteCandidate
        {
            public bool Free;
            public bool Reachable;
            public float NearestBuddyDistance;
            public float DistanceFromSelf;
            public SiteCandidate(bool free, bool reachable, float nearestBuddy, float fromSelf)
            { Free = free; Reachable = reachable; NearestBuddyDistance = nearestBuddy; DistanceFromSelf = fromSelf; }
        }

        // Kleinster Abstand von p zu einer der Positionen (horizontal); keine Positionen → +∞
        public static float NearestDistance(Vector3 p, IList<Vector3> others)
        {
            float best = float.PositiveInfinity;
            if (others == null) return best;
            for (int i = 0; i < others.Count; i++)
            {
                float dx = others[i].x - p.x, dz = others[i].z - p.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < best) best = d;
            }
            return best;
        }

        // Index des besten Platzes (frei, erreichbar, maximaler Buddy-Abstand; Gleichstand ±0,5 m → näher am Beschwörer)
        // oder −1, wenn keiner taugt
        public static int PickRitualSite(IList<SiteCandidate> sites)
        {
            int best = -1;
            if (sites == null) return best;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                if (!s.Free || !s.Reachable) continue;
                if (best < 0) { best = i; continue; }
                var b = sites[best];
                float da = Cap(s.NearestBuddyDistance), db = Cap(b.NearestBuddyDistance);
                if (da > db + 0.5f || (Mathf.Abs(da - db) <= 0.5f && s.DistanceFromSelf < b.DistanceFromSelf)) best = i;
            }
            return best;
        }

        // ∞ (keine Buddies) vergleichbar machen
        private static float Cap(float d) => float.IsInfinity(d) || float.IsNaN(d) ? 1e6f : d;

        // Ritual-Vollendung (P4): Fortschritt 0..1 der Ritual-Uhr; bei 1 verliert der Nexus fraction × Max-HP, dann
        // beginnt die Uhr von vorn
        public static float RitualProgress(float elapsed, float completeTime) =>
            completeTime <= 0f ? 0f : Mathf.Clamp01(elapsed / completeTime);

        public static bool RitualCompleted(float elapsed, float completeTime) => completeTime > 0f && elapsed >= completeTime;

        public static float RitualNexusDamage(float nexusMaxHp, float fraction) => Mathf.Max(0f, nexusMaxHp) * Mathf.Max(0f, fraction);

        // Gruppengröße der summonIndex-ten Beschwörung (0-basiert): baseSize, +1 je growEvery Beschwörungen, höchstens cap
        // (Standard 2, 2, 3, 3, 4 … bis 8)
        public static int SummonGroupSize(int summonIndex, int baseSize = 2, int growEvery = 2, int cap = 8)
        {
            int n = baseSize + (growEvery > 0 ? Math.Max(0, summonIndex) / growEvery : 0);
            return Math.Max(0, Math.Min(cap, n));
        }

        // Gruppe zusätzlich durch den Deckel lebender Beschworener begrenzen
        public static int ClampToAliveCap(int groupSize, int alive, int maxAlive)
        {
            if (maxAlive <= 0) return Math.Max(0, groupSize);
            return Math.Max(0, Math.Min(groupSize, maxAlive - alive));
        }

        // ---------------- Bannwirker ----------------

        // Ziel-Buddy: der mit den meisten (gültigen) Buddies im Radius (inkl. sich selbst); Gleichstand → näher an from.
        // −1, wenn kein gültiger Buddy da ist.
        public static int PickHexTarget(IList<Vector3> buddies, IList<bool> valid, float radius, Vector3 from)
        {
            int best = -1, bestCount = 0;
            float bestDist = float.MaxValue;
            if (buddies == null) return best;
            float r2 = radius * radius;
            for (int i = 0; i < buddies.Count; i++)
            {
                if (valid != null && i < valid.Count && !valid[i]) continue;
                int count = 0;
                for (int j = 0; j < buddies.Count; j++)
                {
                    if (valid != null && j < valid.Count && !valid[j]) continue;
                    float dx = buddies[j].x - buddies[i].x, dz = buddies[j].z - buddies[i].z;
                    if (dx * dx + dz * dz <= r2) count++;
                }
                float fx = buddies[i].x - from.x, fz = buddies[i].z - from.z;
                float dist = fx * fx + fz * fz;
                if (count > bestCount || (count == bestCount && dist < bestDist))
                {
                    best = i;
                    bestCount = count;
                    bestDist = dist;
                }
            }
            return best;
        }

        // Opfer des Banns: bis zu max gültige Buddies im Radius um center, die nächsten zuerst
        public static List<int> SelectHexVictims(IList<Vector3> buddies, IList<bool> valid, Vector3 center, float radius, int max)
        {
            var list = new List<int>();
            if (buddies == null || max <= 0) return list;
            float r2 = radius * radius;
            var dists = new List<float>();
            for (int i = 0; i < buddies.Count; i++)
            {
                if (valid != null && i < valid.Count && !valid[i]) continue;
                float dx = buddies[i].x - center.x, dz = buddies[i].z - center.z;
                float d = dx * dx + dz * dz;
                if (d > r2) continue;
                list.Add(i);
                dists.Add(d);
            }
            // stabile Sortierung nach Abstand (wenige Elemente → Einfügesortierung)
            for (int i = 1; i < list.Count; i++)
            {
                int idx = list[i];
                float d = dists[i];
                int j = i - 1;
                while (j >= 0 && dists[j] > d)
                {
                    list[j + 1] = list[j];
                    dists[j + 1] = dists[j];
                    j--;
                }
                list[j + 1] = idx;
                dists[j + 1] = d;
            }
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        // Bann hält, solange der Bannwirker lebt und höchstens maxDistance (horizontal) entfernt ist
        public static bool HexHolds(bool hexerAlive, float distance, float maxDistance) =>
            hexerAlive && distance <= maxDistance;

        // ---------------- Unterbrechung, Notbremse, Buddy-Zielwahl ----------------

        // Unterbrechung des Wirkens (Frost, Betäubung, Zauber-Sperre, Rückstoß): EnemyBrain setzt das Flag in jedem
        // blockierten Frame, der nächste Bedrohungs-Schritt liest und löscht es (keine Zeitlücken-Heuristik mehr)
        public struct InterruptFlag
        {
            private bool _set;
            public bool IsSet => _set;
            public void Mark() => _set = true;
            public bool Consume()
            {
                bool was = _set;
                _set = false;
                return was;
            }
        }

        // Notbremse: Bedrohung ohne Fortschritt seit mindestens limit s, deren Position vom Nexus aus nicht vollständig
        // erreichbar ist → zurück an ein Portal. limit <= 0 = aus.
        public static bool ShouldRescueThreat(float noProgressTime, float limit, bool reachableFromNexus) =>
            limit > 0f && noProgressTime >= limit && !reachableFromNexus;

        // Buddy-Zielwahl: Gegner mit aktivem Bannschild nicht anvisieren (nur Champion-Schaden kommt durch); Flächen-
        // wirkung trifft sie trotzdem (prallt ab)
        public static bool BuddyMayTarget(bool warded) => !warded;

        // Index des nächsten anvisierbaren Ziels (−1 = keins → Feuer halten)
        public static int NearestBuddyTarget(IList<float> distances, IList<bool> warded)
        {
            int best = -1;
            if (distances == null) return best;
            float bestDist = float.MaxValue;
            for (int i = 0; i < distances.Count; i++)
            {
                if (warded != null && i < warded.Count && !BuddyMayTarget(warded[i])) continue;
                if (distances[i] < bestDist)
                {
                    bestDist = distances[i];
                    best = i;
                }
            }
            return best;
        }

        // ---------------- Zusatzgruppen mit Stufen ----------------

        // Anzahl = Zahl der Stufen-Wellen <= wave (z. B. {8, 14, 20}: W8 1, W14 2, W20 3)
        public static int StepCount(IList<int> steps, int wave)
        {
            if (steps == null) return 0;
            int n = 0;
            for (int i = 0; i < steps.Count; i++)
                if (steps[i] > 0 && wave >= steps[i]) n++;
            return n;
        }
    }
}
