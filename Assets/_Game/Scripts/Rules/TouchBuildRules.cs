using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Reine Rechenregeln für Tippen-zum-Bauen (Mobil-Version, Paket B): Bildschirmpunkt des Bau-Geists beim Ziehen
    // (Versatz nach oben, damit der Finger ihn nicht verdeckt), Raster, Suchreihenfolge für den Start-Bauplatz, Wahl des
    // nächsten Buddys in der vergrößerten Trefferzone und Lage der ✓/✗-Knöpfe am Geist (im sicheren Bereich).
    // Eigene Assembly (ElementalBuddies.Rules) → EditMode-Tests (TouchBuildRulesTests). Längen in Pixeln bzw. Metern.
    public static class TouchBuildRules
    {
        // Versatz des Geists über dem Finger beim Ziehen (dp)
        public const float GhostLiftDp = 64f;
        // Trefferzone beim Antippen eines Buddys: nächster Buddy in diesem Radius (m, waagerecht) um den Bodenpunkt
        public const float PickRadius = 0.8f;
        // Kugel-Radius (m) des Sphere-Casts entlang des Tipp-Strahls (zweite Stufe nach dem genauen Raycast)
        public const float PickCastRadius = 0.35f;
        // ✓/✗-Knöpfe: Durchmesser, Abstand zueinander, Höhe über dem Fußpunkt des Geists (dp)
        public const float ConfirmButtonDp = 56f;
        public const float ConfirmGapDp = 14f;
        public const float ConfirmLiftDp = 84f;
        // Start-Bauplatz: so weit vor der Figur (m, in Blickrichtung der Kamera) und Suchradius in Rasterzellen
        public const float StartDistance = 2.5f;
        public const int StartSearchRadius = 4;

        // Bildschirmpunkt des Geists beim Ziehen: über dem Finger, im Bildschirm gehalten
        public static Vector2 GhostScreenPoint(Vector2 finger, float liftPx, Rect screen)
        {
            Vector2 p = finger + Vector2.up * Mathf.Max(0f, liftPx);
            p.x = Mathf.Clamp(p.x, screen.xMin, screen.xMax);
            p.y = Mathf.Clamp(p.y, screen.yMin, screen.yMax);
            return p;
        }

        // 1-m-Raster wie am PC (InteractionManager.UpdateGhost / ServerBuild): x/z runden, Höhe bleibt
        public static Vector3 SnapToGrid(Vector3 p) => new Vector3(Mathf.Round(p.x), p.y, Mathf.Round(p.z));

        // Rasterzellen um (0, 0) bis radius (Quadrat), nach Abstand sortiert; bei Gleichstand zuerst größeres y, dann
        // kleineres x (feste Reihenfolge). x/y = Welt-x/-z-Versatz in Metern. result wird geleert und gefüllt; Rückgabe = Anzahl.
        public static int SearchOrder(int radius, List<Vector2Int> result)
        {
            result.Clear();
            if (radius < 0) return 0;
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                    result.Add(new Vector2Int(x, y));
            result.Sort((a, b) =>
            {
                int d = (a.x * a.x + a.y * a.y).CompareTo(b.x * b.x + b.y * b.y);
                if (d != 0) return d;
                if (a.y != b.y) return b.y.CompareTo(a.y);
                return a.x.CompareTo(b.x);
            });
            return result.Count;
        }

        // Index des Kandidaten, der waagerecht (x/z) am nächsten an point liegt und höchstens radius entfernt ist; −1 = keiner
        public static int PickNearest(Vector3 point, IList<Vector3> candidates, float radius)
        {
            if (candidates == null) return -1;
            int best = -1;
            float bestSqr = radius * radius;
            for (int i = 0; i < candidates.Count; i++)
            {
                float dx = candidates[i].x - point.x, dz = candidates[i].z - point.z;
                float sqr = dx * dx + dz * dz;
                if (sqr <= bestSqr && (best < 0 || sqr < bestSqr))
                {
                    best = i;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        // Mittelpunkte der Knöpfe: ✗ links, ✓ rechts, liftPx über dem Fußpunkt des Geists. Oben kein Platz → unter den
        // Geist. Beide zusammen in den sicheren Bereich geschoben (Abstand zueinander bleibt).
        public static void ConfirmButtons(Vector2 ghostScreen, float liftPx, float sizePx, float gapPx, Rect safe,
            out Vector2 confirm, out Vector2 cancel)
        {
            float half = sizePx * 0.5f;
            float spacing = half + gapPx * 0.5f; // Abstand Knopfmitte → gemeinsame Mitte
            Vector2 c = ghostScreen + Vector2.up * liftPx;
            if (c.y + half > safe.yMax) c.y = ghostScreen.y - liftPx;
            c.x = ClampSafe(c.x, safe.xMin + spacing + half, safe.xMax - spacing - half);
            c.y = ClampSafe(c.y, safe.yMin + half, safe.yMax - half);
            confirm = c + Vector2.right * spacing;
            cancel = c - Vector2.right * spacing;
        }

        // Wie Mathf.Clamp; ist der Bereich zu klein (min > max), die Mitte
        private static float ClampSafe(float v, float min, float max)
        {
            if (min > max) return (min + max) * 0.5f;
            return Mathf.Clamp(v, min, max);
        }
    }
}
