using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Randpfeile (Spieltest 2026-10-18 Paket R): Zielauswahl nach Priorität/Abstand, Rand-Projektion, Entzerrung
    public class EdgeArrowLayoutTests
    {
        private static readonly Vector2 C = Vector2.zero;
        private const float HalfW = 900f, HalfDown = 480f, HalfUp = 400f; // 1920×1080 minus Rand, oben TopBar

        // ---------------- Auswahl ----------------

        [Test]
        public void Auswahl_PrioritaetVorAbstand()
        {
            int[] prio = { EdgeArrowLayout.PriorityMerchant, EdgeArrowLayout.PriorityThreat, EdgeArrowLayout.PriorityShrine, EdgeArrowLayout.PriorityPortal };
            float[] dist = { 5f, 90f, 10f, 50f };
            var res = new int[4];
            int n = EdgeArrowLayout.SelectByPriority(prio, dist, 4, 8, res);
            Assert.AreEqual(4, n);
            CollectionAssert.AreEqual(new[] { 1, 3, 2, 0 }, res);
        }

        [Test]
        public void Auswahl_GleichePrioritaet_NaehereZuerst()
        {
            int[] prio = { 0, 0, 0 };
            float[] dist = { 30f, 10f, 20f };
            var res = new int[3];
            EdgeArrowLayout.SelectByPriority(prio, dist, 3, 8, res);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, res);
        }

        [Test]
        public void Auswahl_Obergrenze_SchneidetUnwichtigsteAb()
        {
            // 9 Bedrohungen + Händler, Obergrenze 8 → Händler und fernste Bedrohung fallen weg
            int[] prio = new int[10];
            float[] dist = new float[10];
            for (int i = 0; i < 9; i++) { prio[i] = EdgeArrowLayout.PriorityThreat; dist[i] = 10f + i; }
            prio[9] = EdgeArrowLayout.PriorityMerchant; dist[9] = 1f;
            var res = new int[10];
            int n = EdgeArrowLayout.SelectByPriority(prio, dist, 10, EdgeArrowLayout.DefaultMaxArrows, res);
            Assert.AreEqual(8, n);
            for (int k = 0; k < n; k++)
            {
                Assert.AreNotEqual(9, res[k], "Händler fällt weg");
                Assert.AreNotEqual(8, res[k], "fernste Bedrohung fällt weg");
            }
        }

        [Test]
        public void Auswahl_Leer()
        {
            Assert.AreEqual(0, EdgeArrowLayout.SelectByPriority(new int[0], new float[0], 0, 8, new int[8]));
        }

        // ---------------- Rand-Projektion ----------------

        [Test]
        public void Projektion_TrifftKanten()
        {
            Vector2 r = EdgeArrowLayout.ProjectToEdge(C, Vector2.right, HalfW, HalfDown, HalfUp);
            Assert.AreEqual(HalfW, r.x, 1e-3f); Assert.AreEqual(0f, r.y, 1e-3f);
            Vector2 u = EdgeArrowLayout.ProjectToEdge(C, Vector2.up, HalfW, HalfDown, HalfUp);
            Assert.AreEqual(HalfUp, u.y, 1e-3f, "oben nur bis unter die TopBar");
            Vector2 d = EdgeArrowLayout.ProjectToEdge(C, Vector2.down, HalfW, HalfDown, HalfUp);
            Assert.AreEqual(-HalfDown, d.y, 1e-3f);
            Vector2 z = EdgeArrowLayout.ProjectToEdge(C, Vector2.zero, HalfW, HalfDown, HalfUp);
            Assert.AreEqual(-HalfDown, z.y, 1e-3f, "ohne Richtung nach unten");
        }

        [Test]
        public void Projektion_Diagonal_LiegtAufRand()
        {
            Vector2 p = EdgeArrowLayout.ProjectToEdge(new Vector2(10f, 20f), new Vector2(1f, 1f), HalfW, HalfDown, HalfUp);
            Assert.AreEqual(20f + HalfUp, p.y, 1e-3f, "Oberkante wird zuerst erreicht");
            Assert.AreEqual(10f + HalfUp, p.x, 1e-3f);
        }

        [Test]
        public void Umfang_HinUndZurueck()
        {
            Vector2[] pts =
            {
                new Vector2(-300f, -HalfDown), new Vector2(HalfW, 100f), new Vector2(200f, HalfUp), new Vector2(-HalfW, -50f),
                new Vector2(HalfW, HalfUp),
            };
            foreach (var p in pts)
            {
                float s = EdgeArrowLayout.ToPerimeter(p, C, HalfW, HalfDown, HalfUp);
                Vector2 q = EdgeArrowLayout.FromPerimeter(s, C, HalfW, HalfDown, HalfUp);
                Assert.AreEqual(p.x, q.x, 1e-2f, p.ToString());
                Assert.AreEqual(p.y, q.y, 1e-2f, p.ToString());
            }
        }

        // ---------------- Entzerrung ----------------

        [Test]
        public void Entzerrung_WeitAuseinander_BleibtUnveraendert()
        {
            float[] s = { 100f, 500f, 1200f };
            EdgeArrowLayout.Spread(s, 3, 80f, 5000f);
            CollectionAssert.AreEqual(new[] { 100f, 500f, 1200f }, s);
        }

        [Test]
        public void Entzerrung_GleicheStelle_SymmetrischMitMindestabstand()
        {
            float[] s = { 1000f, 1000f, 1000f };
            EdgeArrowLayout.Spread(s, 3, 80f, 5000f);
            System.Array.Sort(s);
            Assert.AreEqual(920f, s[0], 1e-3f);
            Assert.AreEqual(1000f, s[1], 1e-3f);
            Assert.AreEqual(1080f, s[2], 1e-3f);
        }

        [Test]
        public void Entzerrung_ReihenfolgeBleibt()
        {
            float[] s = { 1010f, 1000f, 1020f };
            EdgeArrowLayout.Spread(s, 3, 80f, 5000f);
            Assert.Less(s[1], s[0]);
            Assert.Less(s[0], s[2]);
            Assert.AreEqual(80f, s[0] - s[1], 1e-3f);
            Assert.AreEqual(80f, s[2] - s[0], 1e-3f);
        }

        [Test]
        public void Entzerrung_UeberDenNullpunkt()
        {
            // Umfang 1000: 990 und 10 liegen nebeneinander (Ringschluss)
            float[] s = { 990f, 10f };
            EdgeArrowLayout.Spread(s, 2, 80f, 1000f);
            float gap = Mathf.Repeat(s[1] - s[0], 1000f);
            Assert.AreEqual(80f, gap, 1e-3f);
            Assert.AreEqual(960f, s[0], 1e-3f);
            Assert.AreEqual(40f, s[1], 1e-3f);
        }

        [Test]
        public void Entzerrung_ZuVielePfeile_TeilenUmfangGleichmaessig()
        {
            float[] s = { 0f, 1f, 2f, 3f };
            EdgeArrowLayout.Spread(s, 4, 500f, 1000f); // braucht 2000, hat 1000 → Abstand 250
            float[] sorted = (float[])s.Clone();
            System.Array.Sort(sorted);
            for (int i = 0; i < 4; i++)
            {
                float next = i + 1 < 4 ? sorted[i + 1] : sorted[0] + 1000f;
                Assert.GreaterOrEqual(next - sorted[i], 250f - 1e-2f);
            }
        }

        [Test]
        public void Entzerrung_AlleMindestabstand_Zufall()
        {
            var rnd = new System.Random(7);
            const float P = 5560f, Gap = 80f;
            for (int run = 0; run < 200; run++)
            {
                int n = 2 + rnd.Next(7);
                var s = new float[n];
                for (int i = 0; i < n; i++) s[i] = (float)(rnd.NextDouble() * 400.0) + 2000f; // gehäuft
                EdgeArrowLayout.Spread(s, n, Gap, P);
                var sorted = (float[])s.Clone();
                System.Array.Sort(sorted);
                for (int i = 0; i < n; i++)
                {
                    Assert.That(sorted[i], Is.InRange(0f, P));
                    float next = i + 1 < n ? sorted[i + 1] : sorted[0] + P;
                    Assert.GreaterOrEqual(next - sorted[i], Gap - 1e-2f, $"Lauf {run}");
                }
            }
        }

        [Test]
        public void EntzerrungAmRand_UmDieEcke()
        {
            // Zwei Pfeile genau in der Ecke oben rechts → einer rutscht nach unten, einer nach links, beide auf dem Rand
            var pts = new[] { new Vector2(HalfW, HalfUp), new Vector2(HalfW, HalfUp) };
            EdgeArrowLayout.SpreadOnEdge(pts, 2, C, HalfW, HalfDown, HalfUp, 80f);
            Assert.Greater((pts[0] - pts[1]).magnitude, 50f);
            foreach (var p in pts)
            {
                bool onEdge = Mathf.Abs(p.x - HalfW) < 1e-2f || Mathf.Abs(p.y - HalfUp) < 1e-2f;
                Assert.IsTrue(onEdge, p.ToString());
            }
        }
    }
}
