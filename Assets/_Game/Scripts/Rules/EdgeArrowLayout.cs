using UnityEngine;

namespace ElementalBuddies
{
    // Reine Rechenregeln der Randpfeile (OffscreenIndicatorUI, Spieltest 2026-10-18 Paket R): Auswahl der angezeigten
    // Ziele nach Priorität/Abstand, Projektion einer Richtung auf den Bildschirmrand und Entzerrung von Pfeilen, die
    // am Rand übereinander lägen. Eigene Assembly (ElementalBuddies.Rules), damit EditMode-Tests sie prüfen können.
    //
    // Rand-Rechteck: Mitte center, halfW nach links/rechts, halfDown nach unten, halfUp nach oben (oben ist es wegen
    // der TopBar meist kürzer). Umfangs-Koordinate s läuft gegen den Uhrzeigersinn ab der Ecke unten links:
    // untere Kante (links → rechts), rechte Kante (unten → oben), obere Kante (rechts → links), linke Kante (oben → unten).
    // Nicht threadsicher (statische Puffer, nur Hauptthread) – vermeidet Allokationen pro Frame.
    public static class EdgeArrowLayout
    {
        // Prioritäten: kleiner = wichtiger
        public const int PriorityThreat = 0;   // Champion-Bedrohung und Boss
        public const int PriorityPortal = 1;
        public const int PriorityShrine = 2;
        public const int PriorityMerchant = 3;

        public const int DefaultMaxArrows = 8;

        private static int[] _order = new int[16];
        private static float[] _t = new float[16];
        private static float[] _sum = new float[16];
        private static int[] _cnt = new int[16];
        private static float[] _s = new float[16];

        // Wählt höchstens max Ziele: zuerst nach Priorität, dann nach Abstand (bei Gleichstand der kleinere Index).
        // result erhält die Indizes in dieser Reihenfolge; Rückgabe = Anzahl.
        public static int SelectByPriority(int[] priority, float[] distance, int count, int max, int[] result)
        {
            if (priority == null || distance == null || result == null) return 0;
            count = Mathf.Min(count, Mathf.Min(priority.Length, distance.Length));
            if (count <= 0 || max <= 0) return 0;
            Ensure(ref _order, count);
            for (int i = 0; i < count; i++) _order[i] = i;
            // Einfügesortierung (wenige Ziele, keine Allokation)
            for (int i = 1; i < count; i++)
            {
                int v = _order[i];
                int j = i - 1;
                while (j >= 0 && Before(v, _order[j], priority, distance))
                {
                    _order[j + 1] = _order[j];
                    j--;
                }
                _order[j + 1] = v;
            }
            int n = Mathf.Min(count, Mathf.Min(max, result.Length));
            for (int i = 0; i < n; i++) result[i] = _order[i];
            return n;
        }

        private static bool Before(int a, int b, int[] priority, float[] distance)
        {
            if (priority[a] != priority[b]) return priority[a] < priority[b];
            if (distance[a] != distance[b]) return distance[a] < distance[b];
            return a < b;
        }

        // Richtung (von der Mitte aus) → Schnittpunkt mit dem Rand-Rechteck. Ohne Richtung → nach unten.
        public static Vector2 ProjectToEdge(Vector2 center, Vector2 dir, float halfW, float halfDown, float halfUp)
        {
            if (dir.sqrMagnitude < 1e-12f) dir = Vector2.down;
            dir.Normalize();
            halfW = Mathf.Max(1f, halfW);
            halfDown = Mathf.Max(1f, halfDown);
            halfUp = Mathf.Max(1f, halfUp);
            float tx = Mathf.Abs(dir.x) > 1e-5f ? halfW / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float ty = Mathf.Abs(dir.y) > 1e-5f ? (dir.y > 0f ? halfUp : halfDown) / Mathf.Abs(dir.y) : float.PositiveInfinity;
            return center + dir * Mathf.Min(tx, ty);
        }

        public static float Perimeter(float halfW, float halfDown, float halfUp) =>
            2f * (2f * Mathf.Max(0f, halfW) + Mathf.Max(0f, halfDown) + Mathf.Max(0f, halfUp));

        // Punkt (auf oder nahe dem Rand) → Umfangs-Koordinate der nächstgelegenen Kante
        public static float ToPerimeter(Vector2 p, Vector2 center, float halfW, float halfDown, float halfUp)
        {
            float xMin = center.x - halfW, xMax = center.x + halfW;
            float yMin = center.y - halfDown, yMax = center.y + halfUp;
            float w2 = 2f * halfW, h = halfDown + halfUp;
            float x = Mathf.Clamp(p.x, xMin, xMax), y = Mathf.Clamp(p.y, yMin, yMax);
            float dB = y - yMin, dR = xMax - x, dT = yMax - y, dL = x - xMin;
            float m = Mathf.Min(Mathf.Min(dB, dR), Mathf.Min(dT, dL));
            if (m == dB) return x - xMin;
            if (m == dR) return w2 + (y - yMin);
            if (m == dT) return w2 + h + (xMax - x);
            return 2f * w2 + h + (yMax - y);
        }

        // Umfangs-Koordinate → Punkt auf dem Rand (s wird modulo Umfang genommen)
        public static Vector2 FromPerimeter(float s, Vector2 center, float halfW, float halfDown, float halfUp)
        {
            float xMin = center.x - halfW, xMax = center.x + halfW;
            float yMin = center.y - halfDown, yMax = center.y + halfUp;
            float w2 = 2f * halfW, h = halfDown + halfUp;
            float p = 2f * (w2 + h);
            if (p <= 0f) return center;
            s = Mathf.Repeat(s, p);
            if (s <= w2) return new Vector2(xMin + s, yMin);
            s -= w2;
            if (s <= h) return new Vector2(xMax, yMin + s);
            s -= h;
            if (s <= w2) return new Vector2(xMax - s, yMax);
            s -= w2;
            return new Vector2(xMin, yMax - Mathf.Min(s, h));
        }

        // Umfangs-Koordinaten (ringförmig, Länge perimeter) so verschieben, dass benachbarte Werte mindestens minGap
        // auseinanderliegen; Reihenfolge bleibt erhalten, Gruppen weichen symmetrisch um ihren Schwerpunkt aus
        // (kleinste Quadrate). Passen nicht alle hinein, wird der Abstand auf perimeter / count verkleinert.
        public static void Spread(float[] s, int count, float minGap, float perimeter)
        {
            if (s == null) return;
            count = Mathf.Min(count, s.Length);
            if (count < 1 || perimeter <= 0f) return;
            for (int i = 0; i < count; i++) s[i] = Mathf.Repeat(s[i], perimeter);
            if (count < 2 || minGap <= 0f) return;
            float gap = Mathf.Min(minGap, perimeter / count);

            Ensure(ref _order, count); Ensure(ref _t, count); Ensure(ref _sum, count); Ensure(ref _cnt, count);
            for (int i = 0; i < count; i++) _order[i] = i;
            for (int i = 1; i < count; i++)
            {
                int v = _order[i];
                int j = i - 1;
                while (j >= 0 && s[_order[j]] > s[v]) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = v;
            }

            // Am größten Zwischenraum aufschneiden → lineare Folge ab 0
            int start = 0;
            float biggest = -1f;
            for (int k = 0; k < count; k++)
            {
                float a = s[_order[k]];
                float b = k + 1 < count ? s[_order[k + 1]] : s[_order[0]] + perimeter;
                if (b - a > biggest) { biggest = b - a; start = (k + 1) % count; }
            }
            float origin = s[_order[start]];
            for (int j = 0; j < count; j++)
                _t[j] = Mathf.Repeat(s[_order[(start + j) % count]] - origin, perimeter);

            // Gruppen zusammenlegen, solange sie sich überlappen (Pool Adjacent Violators)
            int groups = 0;
            for (int j = 0; j < count; j++)
            {
                _sum[groups] = _t[j];
                _cnt[groups] = 1;
                groups++;
                while (groups >= 2 && First(groups - 1, gap) < First(groups - 2, gap) + _cnt[groups - 2] * gap)
                {
                    _sum[groups - 2] += _sum[groups - 1];
                    _cnt[groups - 2] += _cnt[groups - 1];
                    groups--;
                }
            }
            int idx = 0;
            for (int g = 0; g < groups; g++)
            {
                float f = First(g, gap);
                for (int k = 0; k < _cnt[g]; k++) _t[idx++] = f + k * gap;
            }

            // Ringschluss: letzter und (umlaufend) erster Pfeil brauchen ebenfalls minGap
            float span = _t[count - 1] - _t[0];
            float maxSpan = perimeter - gap;
            if (span > maxSpan && span > 1e-6f)
            {
                float f = maxSpan / span, t0 = _t[0];
                for (int j = 0; j < count; j++) _t[j] = t0 + (_t[j] - t0) * f;
            }

            for (int j = 0; j < count; j++)
                s[_order[(start + j) % count]] = Mathf.Repeat(origin + _t[j], perimeter);
        }

        private static float First(int g, float gap) => _sum[g] / _cnt[g] - gap * (_cnt[g] - 1) * 0.5f;

        // Bequem: Randpunkte (z. B. aus ProjectToEdge) in place entlang des Rands entzerren
        public static void SpreadOnEdge(Vector2[] points, int count, Vector2 center, float halfW, float halfDown, float halfUp, float minGap)
        {
            if (points == null) return;
            count = Mathf.Min(count, points.Length);
            if (count < 2) return;
            float p = Perimeter(halfW, halfDown, halfUp);
            if (p <= 0f) return;
            Ensure(ref _s, count);
            for (int i = 0; i < count; i++) _s[i] = ToPerimeter(points[i], center, halfW, halfDown, halfUp);
            Spread(_s, count, minGap, p);
            for (int i = 0; i < count; i++) points[i] = FromPerimeter(_s[i], center, halfW, halfDown, halfUp);
        }

        private static void Ensure<T>(ref T[] a, int n)
        {
            if (a.Length < n) a = new T[Mathf.Max(n, a.Length * 2)];
        }
    }
}
