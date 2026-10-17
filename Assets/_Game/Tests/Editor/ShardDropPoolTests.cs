using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Spieltest 2026-10-17 (Paket A): Drops < 1 Splitter (Beschworene ×0,25, Schwarm, Spätspiel) erhöhten die abgerundete
    // Kassen-Anzeige beim Einsammeln oft nicht. Jetzt fallen nur ganzzahlige Kristalle ≥ 1, der Rest sammelt sich im Pool
    // und wird am Wellenende mit dem Rückruf verbucht – Gesamtbetrag unverändert.
    public class ShardDropPoolTests
    {
        // Werte aus den Assets (Normal, Solo): ShardsPerKill 6, M(w) = (1 + 0,05·(w−1))^1,2, DropFactor ab W11
        private static float Bounty(float bountyMultiplier, int wave, float factor)
        {
            double m = Math.Pow(1.0 + 0.05 * (wave - 1), 1.2);
            double drop = wave <= 10 ? 1.0 : Math.Max(0.25, 1.0 - 0.08 * (wave - 10));
            return (float)(6.0 * bountyMultiplier / m * drop * factor);
        }

        [Test]
        public void Kleine_Drops_spawnen_erst_ab_einem_ganzen_Splitter()
        {
            var pool = new ShardDropPool();
            Assert.AreEqual(0f, pool.Add(0.4f));
            Assert.AreEqual(0f, pool.Add(0.4f));
            Assert.AreEqual(1f, pool.Add(0.4f));        // 1,2 → Kristall 1, Rest 0,2
            Assert.AreEqual(0.2f, pool.Remainder, 0.0001f);
        }

        [Test]
        public void Grosse_Drops_fallen_ganzzahlig_Rest_bleibt_im_Pool()
        {
            var pool = new ShardDropPool();
            Assert.AreEqual(2f, pool.Add(2.98f));
            Assert.AreEqual(0.98f, pool.Remainder, 0.0001f);
            Assert.AreEqual(4f, pool.Add(3.1f));        // 0,98 + 3,1 = 4,08
            Assert.AreEqual(0.08f, pool.Remainder, 0.0001f);
            Assert.AreEqual(120f, pool.Add(120f));
        }

        [Test]
        public void Jeder_Kristall_ist_ganzzahlig_und_mindestens_eins()
        {
            var pool = new ShardDropPool();
            var rnd = new Random(17);
            for (int i = 0; i < 2000; i++)
            {
                float v = pool.Add((float)(rnd.NextDouble() * 5.0));
                if (v == 0f) continue;
                Assert.GreaterOrEqual(v, 1f);
                Assert.AreEqual(Math.Floor(v), v, 0.0);
            }
            Assert.That(pool.Remainder, Is.InRange(0f, 1f));
        }

        [Test]
        public void Gesamtbetrag_bleibt_mit_Rest_am_Wellenende_gleich()
        {
            // Goblin-Trommler W16: Beschworene abwechselnd Läufer (1) und Schwarm (0,35), je ×0,25
            float laeufer = Bounty(1f, 16, 0.25f);      // ≈ 0,40
            float schwarm = Bounty(0.35f, 16, 0.25f);   // ≈ 0,14
            Assert.Less(laeufer, 1f);
            Assert.Less(schwarm, 1f);

            var pool = new ShardDropPool();
            double soll = 0, kristalle = 0;
            int anzahl = 0;
            for (int i = 0; i < 60; i++)
            {
                float b = i % 2 == 0 ? laeufer : schwarm;
                soll += b;
                float v = pool.Add(b);
                if (v > 0f) { kristalle += v; anzahl++; }
            }
            double rueckruf = pool.TakeRemainder();     // EconomyManager.RecallShards
            Assert.AreEqual(soll, kristalle + rueckruf, 0.01);
            Assert.AreEqual(0f, pool.Remainder);
            Assert.Greater(anzahl, 10);                 // ≈ 16 Kristalle à 1 statt 60 Kristalle < 1
        }

        [Test]
        public void Ganze_Kristalle_erhoehen_die_abgerundete_Anzeige_immer()
        {
            // HUDManager zeigt ⌊Kasse⌋. Vorher: 110,7 + 0,14 → Anzeige bleibt 110 („zählt nicht“)
            float kasse = 110.7f;
            Assert.AreEqual(110, (int)Math.Floor(kasse + 0.14f));
            var pool = new ShardDropPool();
            for (int i = 0; i < 40; i++)
            {
                float v = pool.Add(0.14f);
                if (v <= 0f) continue;
                int vorher = (int)Math.Floor(kasse);
                kasse += v;
                Assert.AreEqual(vorher + (int)v, (int)Math.Floor(kasse));
            }
        }

        [Test]
        public void Float_Rundung_zehn_mal_ein_Zehntel_ergibt_einen_Kristall()
        {
            var pool = new ShardDropPool();
            float gespawnt = 0f;
            for (int i = 0; i < 10; i++) gespawnt += pool.Add(0.1f);
            Assert.AreEqual(1f, gespawnt);
            Assert.AreEqual(0f, pool.Remainder, 0.0001f);
        }

        [Test]
        public void Wertlose_Kopfgelder_aendern_nichts()
        {
            var pool = new ShardDropPool();
            pool.Add(0.5f);
            Assert.AreEqual(0f, pool.Add(0f));
            Assert.AreEqual(0f, pool.Add(-2f));
            Assert.AreEqual(0f, pool.Add(float.NaN));
            Assert.AreEqual(0f, pool.Add(float.PositiveInfinity));
            Assert.AreEqual(0.5f, pool.Remainder, 0.0001f);
            pool.Reset();
            Assert.AreEqual(0f, pool.TakeRemainder());
        }

        [Test]
        public void Rest_landet_mit_Rueckruf_im_Kartenanteil()
        {
            var pool = new ShardDropPool();
            pool.Add(0.4f); pool.Add(0.4f);             // 0,8 im Pool, kein Kristall
            float kasse = 150.5f;
            kasse += 0f + pool.TakeRemainder();         // RecallShards: liegende Drops (hier keine) + Rest
            var l = new DraftLedger();
            l.Open(kasse, 1);
            Assert.AreEqual(151, l.Share);              // ⌊150,5 + 0,8⌋ – ohne Rest wären es 150
        }

        [Test]
        public void Koop_Rest_wird_geteilt_wie_die_Kasse()
        {
            var pool = new ShardDropPool();
            var drops = new List<float>();
            foreach (var b in new[] { 0.9f, 0.9f, 0.9f }) { float v = pool.Add(b); if (v > 0f) drops.Add(v); }
            CollectionAssert.AreEqual(new[] { 1f, 1f }, drops);   // 0,9 → 1,8 (1, Rest 0,8) → 1,7 (1, Rest 0,7)
            float kasse = 200f + 2f + pool.TakeRemainder();       // 202,7
            var a = new DraftLedger(); a.Open(kasse, 2);
            Assert.AreEqual(GoldRules.DraftShare(kasse, 2), a.Share);
            Assert.AreEqual(101, a.Share);
        }
    }
}
