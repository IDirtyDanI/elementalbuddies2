using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Dimensionsriss: Termin, Arena-Lage, Verfalls-Uhr, Boss-Leben/-Schaden, „wer ist drin“, Positionen
    public class DimensionEventRulesTests
    {
        [Test]
        public void Schedule_Wave20ThenEvery5()
        {
            for (int w = 1; w <= 60; w++)
            {
                bool expected = w == 20 || (w > 20 && (w - 20) % 5 == 0);
                Assert.AreEqual(expected, DimensionEventRules.IsEventWave(w), "Welle " + w);
            }
            Assert.AreEqual(0, DimensionEventRules.EventIndex(20));
            Assert.AreEqual(1, DimensionEventRules.EventIndex(25));
            Assert.AreEqual(4, DimensionEventRules.EventIndex(40));
            Assert.AreEqual(-1, DimensionEventRules.EventIndex(21));
            Assert.AreEqual(-1, DimensionEventRules.EventIndex(19));
        }

        [Test]
        public void Schedule_CustomAndOff()
        {
            Assert.IsFalse(DimensionEventRules.IsEventWave(20, 0, 5), "0 = aus");
            Assert.IsTrue(DimensionEventRules.IsEventWave(3, 3, 2));
            Assert.IsTrue(DimensionEventRules.IsEventWave(7, 3, 2));
            Assert.IsFalse(DimensionEventRules.IsEventWave(6, 3, 2));
            Assert.IsTrue(DimensionEventRules.IsEventWave(4, 3, 0), "every 0 zählt wie 1");
        }

        [Test]
        public void NextEventWave_FindsFirstAtOrAfter()
        {
            Assert.AreEqual(20, DimensionEventRules.NextEventWave(1));
            Assert.AreEqual(20, DimensionEventRules.NextEventWave(20));
            Assert.AreEqual(25, DimensionEventRules.NextEventWave(21));
            Assert.AreEqual(25, DimensionEventRules.NextEventWave(25));
            Assert.AreEqual(30, DimensionEventRules.NextEventWave(26));
            Assert.AreEqual(-1, DimensionEventRules.NextEventWave(5, 0, 5));
            for (int w = 1; w <= 80; w++)
            {
                int n = DimensionEventRules.NextEventWave(w);
                Assert.IsTrue(n >= w && DimensionEventRules.IsEventWave(n), "ab " + w);
                for (int k = w; k < n; k++) Assert.IsFalse(DimensionEventRules.IsEventWave(k));
            }
        }

        [Test]
        public void Arena_IsFarFromEveryMap()
        {
            Assert.IsTrue(DimensionEventRules.InArena(DimensionEventRules.ArenaCenter));
            Assert.IsTrue(DimensionEventRules.InArena(DimensionEventRules.ArenaCenter + Vector3.left * DimensionEventRules.FloorRadius));
            Assert.IsFalse(DimensionEventRules.InArena(Vector3.zero));
            Assert.IsFalse(DimensionEventRules.InArena(new Vector3(80f, 0f, 80f)));   // Kartenrand
            Assert.IsFalse(DimensionEventRules.InArena(new Vector3(-44f, 0f, 0f)));   // Portal West
            Assert.IsTrue(DimensionEventRules.SameDimension(new Vector3(390f, 0f, 3f), new Vector3(410f, 0f, -9f)));
            Assert.IsFalse(DimensionEventRules.SameDimension(new Vector3(390f, 0f, 3f), new Vector3(10f, 0f, -9f)));
            Assert.Less(DimensionEventRules.WalkRadius, DimensionEventRules.FloorRadius);
            Assert.Greater(DimensionEventRules.LeashRadius, 2f * DimensionEventRules.FloorRadius, "Leine deckt die ganze Arena");
            Assert.Greater(DimensionEventRules.MinimapRadius, DimensionEventRules.FloorRadius);
        }

        [Test]
        public void Drain_GraceThenTicksEveryInterval()
        {
            var c = DimensionEventRules.DrainClock.Start(100.0);
            Assert.AreEqual(DimensionEventRules.DrainPhase.Grace, c.Phase);
            int ticks = 0;
            double firstTick = -1, lastTick = -1;
            for (double t = 100.0; t <= 161.0; t += 0.1)
            {
                int n = c.Step(t, true, false);
                if (n > 0)
                {
                    if (firstTick < 0) firstTick = t;
                    else Assert.AreEqual(DimensionEventRules.DrainInterval, t - lastTick, 0.11, "Takt");
                    lastTick = t;
                    ticks += n;
                }
            }
            Assert.AreEqual(100.0 + DimensionEventRules.DrainGrace, firstTick, 0.11, "erster Takt nach der Schonfrist");
            Assert.AreEqual(7, ticks, "30 s Schonfrist, dann alle 5 s: 130, 135 … 160");
            Assert.AreEqual(DimensionEventRules.DrainPhase.Draining, c.Phase);
        }

        [Test]
        public void Drain_HeldWhileSomeoneInside_ThenRegrace()
        {
            var c = DimensionEventRules.DrainClock.Start(0.0);
            // Verfall läuft schon
            Assert.AreEqual(0, c.Step(29.9, true, false));
            Assert.AreEqual(1, c.Step(30.0, true, false));
            // Spieler geht hinein: kein Takt, auch nicht nach langer Zeit
            for (double t = 31.0; t < 90.0; t += 1.0) Assert.AreEqual(0, c.Step(t, true, true));
            Assert.AreEqual(DimensionEventRules.DrainPhase.Held, c.Phase);
            // Spieler stirbt bei t = 90: neue Schonfrist (DrainRegrace = 25 s: Wiederbelebung + Weg zurück, #561)
            double re = DimensionEventRules.DrainRegrace, iv = DimensionEventRules.DrainInterval;
            Assert.AreEqual(25.0, re, 1e-6);
            Assert.AreEqual(0, c.Step(90.0, true, false));
            Assert.AreEqual(DimensionEventRules.DrainPhase.Grace, c.Phase);
            Assert.AreEqual(90.0 + re, c.NextTick, 1e-6);
            Assert.AreEqual(0, c.Step(90.0 + re - 0.1, true, false));
            Assert.AreEqual(1, c.Step(90.0 + re, true, false));
            Assert.AreEqual(0, c.Step(90.0 + re + iv - 0.1, true, false));
            Assert.AreEqual(1, c.Step(90.0 + re + iv, true, false));
        }

        [Test]
        public void Drain_RegraceNeverShortensInitialGrace()
        {
            var c = DimensionEventRules.DrainClock.Start(0.0);
            Assert.AreEqual(0, c.Step(2.0, true, true));   // sofort hinein
            Assert.AreEqual(0, c.Step(3.0, true, false));  // nach 3 s tot: die neue Schonfrist (25 s) wäre kürzer als der Rest der 30 s
            Assert.AreEqual(30.0, c.NextTick, 1e-6);
            Assert.AreEqual(0, c.Step(29.0, true, false));
            Assert.AreEqual(1, c.Step(30.0, true, false));
        }

        [Test]
        public void Drain_StopsForGoodWhenBossDead_AndNoCatchUpAfterHitch()
        {
            var c = DimensionEventRules.DrainClock.Start(0.0);
            Assert.AreEqual(1, c.Step(100.0, true, false), "nach einem Hänger nur ein Takt");
            Assert.AreEqual(0, c.Step(100.5, true, false));
            Assert.AreEqual(0, c.Step(200.0, false, false));
            Assert.AreEqual(DimensionEventRules.DrainPhase.Off, c.Phase);
            Assert.AreEqual(0, c.Step(300.0, true, false), "bleibt aus");
        }

        [Test]
        public void Drain_ThreePercentPerTick_KillsFullNexusIn34Ticks()
        {
            Assert.AreEqual(15f, DimensionEventRules.DrainDamage(500f), 1e-4f);
            Assert.AreEqual(0f, DimensionEventRules.DrainDamage(500f, 0f));
            Assert.AreEqual(34, DimensionEventRules.DrainTicksToKill());
            float hp = 500f;
            int ticks = 0;
            while (hp > 0f) { hp -= DimensionEventRules.DrainDamage(500f); ticks++; }
            Assert.AreEqual(34, ticks);
        }

        [Test]
        public void BossHp_ScalesWithPlayersEventsDifficultySiege()
        {
            float t = DimensionEventRules.TargetBossHp;
            Assert.AreEqual(t, DimensionEventRules.BossHp(t, 0, 1), 0.01f);
            Assert.AreEqual(t * 1.6f, DimensionEventRules.BossHp(t, 0, 2), 0.01f);
            Assert.AreEqual(t * 2.8f, DimensionEventRules.BossHp(t, 0, 4), 0.01f);
            Assert.AreEqual(t * 1.15f, DimensionEventRules.BossHp(t, 1, 1), 0.01f);
            Assert.AreEqual(t * 1.15f * 1.15f, DimensionEventRules.BossHp(t, 2, 1), 0.01f);
            Assert.AreEqual(t * 1.3f * 1.2f, DimensionEventRules.BossHp(t, 0, 1, 1.3f, 1.2f), 0.01f);
            Assert.AreEqual(t, DimensionEventRules.BossHp(t, -3, 0), 0.01f, "ungültige Eingaben werden gedeckelt");
            // Ziel 60–75 s bei ≈ 100 DPS (gemessen 2026-11-14 mit Welle-20-Karten: Magier ≈ 120, Schwertkämpfer ≈ 87)
            float seconds = DimensionEventRules.BossHp(t, 0, 1) / 100f;
            Assert.IsTrue(seconds >= 60f && seconds <= 75f, "Kampfdauer " + seconds);
        }

        [Test]
        public void BossHpMultiplier_IsIndependentOfBaseHpAndArmor()
        {
            // BaseHP 560, Rüstung 0,4 (Knochenfürst): Roh-Leben 2400 = effektiv 4000
            float m = DimensionEventRules.BossHpMultiplier(560f, 0.4f, 4000f);
            Assert.AreEqual(2400f, 560f * m, 0.5f);
            Assert.AreEqual(4000f, 560f * m / (1f - 0.4f), 0.5f);
            Assert.AreEqual(4000f, 2000f * DimensionEventRules.BossHpMultiplier(2000f, 0f, 4000f), 0.5f);
        }

        [Test]
        public void BossDamage_TakesHalfOfTheWaveRamp()
        {
            Assert.AreEqual(1.38f, DimensionEventRules.BossDamageMultiplier(1.76f), 1e-4f);   // Welle 20, Normal
            Assert.AreEqual(1f, DimensionEventRules.BossDamageMultiplier(1f), 1e-4f);
            Assert.AreEqual(0.8f, DimensionEventRules.BossDamageMultiplier(0.8f), 1e-4f, "unter 1 unverändert (leichte Stufe)");
            Assert.AreEqual(1.76f, DimensionEventRules.BossDamageMultiplier(1.76f, 1f), 1e-4f);
            // härtester Treffer des Rückfall-Bosses (Aufschlag 45) tötet einen 100-LP-Champion nicht in einem Schlag
            Assert.Less(45f * DimensionEventRules.BossDamageMultiplier(1.76f), 100f);
        }

        [Test]
        public void Mask_TracksClientIds()
        {
            ulong m = 0UL;
            m = DimensionEventRules.MaskSet(m, 0UL, true);
            m = DimensionEventRules.MaskSet(m, 3UL, true);
            Assert.IsTrue(DimensionEventRules.MaskHas(m, 0UL));
            Assert.IsTrue(DimensionEventRules.MaskHas(m, 3UL));
            Assert.IsFalse(DimensionEventRules.MaskHas(m, 1UL));
            Assert.AreEqual(2, DimensionEventRules.MaskCount(m));
            m = DimensionEventRules.MaskSet(m, 0UL, false);
            Assert.IsFalse(DimensionEventRules.MaskHas(m, 0UL));
            Assert.AreEqual(1, DimensionEventRules.MaskCount(m));
            m = DimensionEventRules.MaskSet(m, 3UL, false);
            Assert.AreEqual(0UL, m);
            // hohe Client-Ids (nach vielen Verbindungen) laufen um statt zu überlaufen
            m = DimensionEventRules.MaskSet(0UL, 70UL, true);
            Assert.IsTrue(DimensionEventRules.MaskHas(m, 70UL));
            Assert.AreEqual(1, DimensionEventRules.MaskCount(m));
            m = DimensionEventRules.MaskSet(0UL, 63UL, true);
            Assert.IsTrue(DimensionEventRules.MaskHas(m, 63UL));
        }

        [Test]
        public void EntryPositions_SpreadSideways()
        {
            Vector3 entry = new Vector3(400f, 0f, -10f);
            var seen = new System.Collections.Generic.List<Vector3>();
            for (int s = 0; s < 4; s++)
            {
                Vector3 p = DimensionEventRules.EntryPosition(entry, Vector3.forward, s);
                Assert.AreEqual(entry.z, p.z, 1e-4f, "quer zur Blickrichtung");
                foreach (var o in seen) Assert.Greater(Vector3.Distance(o, p), 1.5f);
                seen.Add(p);
            }
            Assert.AreEqual(entry, seen[0]);
        }

        [Test]
        public void ReturnPosition_IsInwardOfThePortalAndOutsideItsTrigger()
        {
            Vector3 spawn = new Vector3(-41f, 0f, 0f); // Portal West, Spawnpunkt
            for (int s = 0; s < 4; s++)
            {
                Vector3 p = DimensionEventRules.ReturnPosition(spawn, Vector3.zero, s);
                Assert.Less(Mathf.Abs(p.x), Mathf.Abs(spawn.x), "Richtung Kartenmitte");
                Assert.Greater(Vector3.Distance(p, spawn), DimensionEventRules.EnterRadius + 1f, "nicht wieder im Auslösekreis");
                Assert.AreEqual(spawn.y, p.y, 1e-4f);
            }
            // Portal genau in der Mitte: kein NaN
            Vector3 q = DimensionEventRules.ReturnPosition(Vector3.zero, Vector3.zero, 0);
            Assert.IsFalse(float.IsNaN(q.x) || float.IsNaN(q.z));
        }

        // ---------------- Halten im Portal-Kreis (#569) ----------------

        [Test]
        public void Hold_FiresOnceAfterRequiredTime()
        {
            var h = new DimensionEventRules.HoldTimer();
            int fired = 0;
            float t = 0f;
            for (int i = 0; i < 40; i++) // 4 s in 0,1-s-Schritten
            {
                if (h.Step(true, 0.1f, DimensionEventRules.EnterHold)) { fired++; t = (i + 1) * 0.1f; }
            }
            Assert.AreEqual(1, fired, "genau eine Anfrage je Aufenthalt");
            Assert.AreEqual(DimensionEventRules.EnterHold, t, 0.11f);
            Assert.AreEqual(1f, h.Progress(DimensionEventRules.EnterHold), 1e-4f);
        }

        [Test]
        public void Hold_LeavingCircleCancels()
        {
            var h = new DimensionEventRules.HoldTimer();
            for (int i = 0; i < 10; i++) Assert.IsFalse(h.Step(true, 0.1f, 1.2f)); // 1,0 s – noch nicht
            Assert.AreEqual(1f / 1.2f, h.Progress(1.2f), 0.01f);
            Assert.IsFalse(h.Step(false, 0.1f, 1.2f), "Verlassen löst nie aus");
            Assert.AreEqual(0f, h.Progress(1.2f), 1e-5f, "Fortschritt weg");
            for (int i = 0; i < 11; i++) Assert.IsFalse(h.Step(true, 0.1f, 1.2f), "beginnt von vorn");
            Assert.IsTrue(h.Step(true, 0.1f, 1.2f));
        }

        [Test]
        public void Hold_ResetAllowsRetry_AndZeroIsImmediate()
        {
            var h = new DimensionEventRules.HoldTimer();
            Assert.IsTrue(h.Step(true, 0.016f, 0f), "Haltezeit 0 = sofort");
            Assert.IsFalse(h.Step(true, 0.016f, 0f));
            h.Reset(); // abgelehnte Anfrage
            Assert.IsTrue(h.Step(true, 0.016f, 0f));
            var g = new DimensionEventRules.HoldTimer();
            Assert.IsFalse(g.Step(true, -5f, 1f), "negative Zeit zählt nicht");
            Assert.AreEqual(0f, g.Elapsed, 1e-6f);
            Assert.Less(DimensionEventRules.ReturnHold, DimensionEventRules.EnterHold);
        }

        // ---------------- Regen mit mehreren Flächen (#570) ----------------

        private static System.Func<float> Seeded(int seed)
        {
            var r = new System.Random(seed);
            return () => (float)r.NextDouble();
        }

        [Test]
        public void RainCenters_RandomOffsetsInRingAndApart()
        {
            var result = new Vector3[8];
            for (int seed = 0; seed < 50; seed++)
            {
                Vector3 first = new Vector3(400f + seed, 0f, -3f);
                int n = DimensionEventRules.RainCenters(first, null, 3, 4f, 4f, 7f, Seeded(seed), result);
                Assert.AreEqual(3, n, "Seed " + seed);
                Assert.AreEqual(first, result[0]);
                for (int i = 1; i < n; i++)
                {
                    float d = Vector3.Distance(first, result[i]);
                    Assert.GreaterOrEqual(d, 4f - 1e-3f, "Mindestabstand zur ersten Fläche");
                    Assert.LessOrEqual(d, 7f + 1e-3f);
                    for (int j = 0; j < i; j++)
                        Assert.GreaterOrEqual(Vector3.Distance(result[i], result[j]), 4f - 1e-3f, "Flächen überlappen höchstens zur Hälfte");
                }
            }
        }

        [Test]
        public void RainCenters_PrefersOtherPlayers()
        {
            var result = new Vector3[8];
            Vector3 first = new Vector3(400f, 0f, 0f);
            var others = new[] { new Vector3(401f, 2f, 0f) /* zu nah am Ziel */, new Vector3(410f, 1f, 3f) };
            int n = DimensionEventRules.RainCenters(first, others, 3, 4f, 4f, 7f, Seeded(7), result);
            Assert.AreEqual(3, n);
            Assert.AreEqual(new Vector3(410f, 0f, 3f), result[1], "zweiter Spieler bekommt eine Fläche (Höhe der ersten)");
            Assert.LessOrEqual(Vector3.Distance(first, result[2]), 7f + 1e-3f, "Rest zufällig um die erste Fläche");
        }

        [Test]
        public void RainCenters_CountAndBufferLimits()
        {
            var result = new Vector3[2];
            Assert.AreEqual(1, DimensionEventRules.RainCenters(Vector3.zero, null, 1, 4f, 4f, 7f, Seeded(1), result));
            Assert.AreEqual(2, DimensionEventRules.RainCenters(Vector3.zero, null, 5, 4f, 4f, 7f, Seeded(1), result), "nie mehr als der Puffer");
            Assert.AreEqual(0, DimensionEventRules.RainCenters(Vector3.zero, null, 0, 4f, 4f, 7f, Seeded(1), result));
            Assert.AreEqual(2, DimensionEventRules.RainCenters(Vector3.zero, null, 2, 4f, 4f, 7f, null, result), "ohne Zufall weiter nutzbar");
        }
    }
}
