using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Champion-Bedrohungen (P1): Bannschild, Ritualplatz-Wahl, Beschwörer-Eskalation, Bann-Zielwahl, Wellen-Stufen
    public class ChampionThreatRulesTests
    {
        [Test]
        public void Ward_BlocksOnlyNonChampionDamage()
        {
            Assert.IsTrue(ChampionThreatRules.WardBlocks(true, false));
            Assert.IsFalse(ChampionThreatRules.WardBlocks(true, true));
            Assert.IsFalse(ChampionThreatRules.WardBlocks(false, false));
        }

        [Test]
        public void Ward_BreaksAtHalfMaxHp()
        {
            Assert.IsFalse(ChampionThreatRules.WardBreaks(249f, 500f, 0.5f));
            Assert.IsTrue(ChampionThreatRules.WardBreaks(250f, 500f, 0.5f));
            Assert.IsTrue(ChampionThreatRules.WardBreaks(400f, 500f, 0.5f));
            // dauerhafter Schild (Beschwörer/Bannwirker) bricht nie
            Assert.IsFalse(ChampionThreatRules.WardBreaks(1e6f, 500f, 0f));
        }

        [Test]
        public void RitualSite_PicksFreeReachableFarthestFromBuddies()
        {
            var sites = new List<ChampionThreatRules.SiteCandidate>
            {
                new ChampionThreatRules.SiteCandidate(true, true, 12f, 10f),
                new ChampionThreatRules.SiteCandidate(false, true, 40f, 10f), // belegt
                new ChampionThreatRules.SiteCandidate(true, false, 50f, 10f), // nicht erreichbar
                new ChampionThreatRules.SiteCandidate(true, true, 25f, 30f),
            };
            Assert.AreEqual(3, ChampionThreatRules.PickRitualSite(sites));
        }

        [Test]
        public void RitualSite_TieAndNoBuddies_PrefersNearer()
        {
            float inf = float.PositiveInfinity;
            var sites = new List<ChampionThreatRules.SiteCandidate>
            {
                new ChampionThreatRules.SiteCandidate(true, true, inf, 30f),
                new ChampionThreatRules.SiteCandidate(true, true, inf, 12f),
            };
            Assert.AreEqual(1, ChampionThreatRules.PickRitualSite(sites));
            sites.Clear();
            Assert.AreEqual(-1, ChampionThreatRules.PickRitualSite(sites));
            Assert.AreEqual(5f, ChampionThreatRules.NearestDistance(Vector3.zero, new[] { new Vector3(3f, 9f, 4f), new Vector3(10f, 0f, 0f) }), 1e-4f);
        }

        [Test]
        public void SummonGroup_GrowsEverySecondSummon_CappedAt8()
        {
            int[] expected = { 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 8, 8 };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], ChampionThreatRules.SummonGroupSize(i), "Beschwörung " + (i + 1));
            Assert.AreEqual(3, ChampionThreatRules.ClampToAliveCap(5, 21, 24));
            Assert.AreEqual(0, ChampionThreatRules.ClampToAliveCap(5, 24, 24));
            Assert.AreEqual(5, ChampionThreatRules.ClampToAliveCap(5, 100, 0));
        }

        [Test]
        public void Ritual_CompletesAfter45s_Deals10PercentNexusHp()
        {
            Assert.AreEqual(0f, ChampionThreatRules.RitualProgress(0f, 45f), 1e-5f);
            Assert.AreEqual(0.5f, ChampionThreatRules.RitualProgress(22.5f, 45f), 1e-5f);
            Assert.AreEqual(1f, ChampionThreatRules.RitualProgress(60f, 45f), 1e-5f);
            Assert.IsFalse(ChampionThreatRules.RitualCompleted(44.9f, 45f));
            Assert.IsTrue(ChampionThreatRules.RitualCompleted(45f, 45f));
            Assert.IsFalse(ChampionThreatRules.RitualCompleted(100f, 0f)); // 0 = aus
            Assert.AreEqual(50f, ChampionThreatRules.RitualNexusDamage(500f, 0.1f), 1e-4f);
            Assert.AreEqual(0f, ChampionThreatRules.RitualNexusDamage(500f, -1f), 1e-4f);
        }

        [Test]
        public void HexTarget_PicksLargestCluster()
        {
            var buddies = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),      // einzeln
                new Vector3(20f, 0f, 0f),     // Gruppe aus 3
                new Vector3(23f, 0f, 1f),
                new Vector3(21f, 0f, -3f),
                new Vector3(40f, 0f, 0f),
            };
            Assert.AreEqual(1, ChampionThreatRules.PickHexTarget(buddies, null, 5f, new Vector3(-10f, 0f, 0f)));
            // Mitte der Gruppe ungültig (z. B. tot) → Rest der Gruppe gewinnt weiter
            var valid = new List<bool> { true, false, true, true, true };
            Assert.AreEqual(2, ChampionThreatRules.PickHexTarget(buddies, valid, 5f, new Vector3(30f, 0f, 0f)));
            Assert.AreEqual(-1, ChampionThreatRules.PickHexTarget(new List<Vector3>(), null, 5f, Vector3.zero));
        }

        [Test]
        public void HexVictims_NearestUpToFour()
        {
            var buddies = new List<Vector3>();
            for (int i = 0; i < 6; i++) buddies.Add(new Vector3(i, 0f, 0f));
            buddies.Add(new Vector3(30f, 0f, 0f));
            var v = ChampionThreatRules.SelectHexVictims(buddies, null, new Vector3(3f, 0f, 0f), 5f, 4);
            // Abstände 0 (3), 1 (2, 4), 2 (1, 5), 3 (0) – Gleichstand in Eingabe-Reihenfolge
            CollectionAssert.AreEqual(new[] { 3, 2, 4, 1 }, v);
            CollectionAssert.DoesNotContain(v, 6);
        }

        [Test]
        public void Hex_HoldsWhileAliveAndWithin18m()
        {
            Assert.IsTrue(ChampionThreatRules.HexHolds(true, 18f, 18f));
            Assert.IsFalse(ChampionThreatRules.HexHolds(true, 18.1f, 18f));
            Assert.IsFalse(ChampionThreatRules.HexHolds(false, 2f, 18f));
        }

        [Test]
        public void StepCount_MatchesPlanWaves()
        {
            // Werte nach dem Balancing-Modell (P4): Beschwörer ab W9 1, ab W18 2; Bannwirker ab W11 1, ab W20 2
            int[] summoner = { 9, 18 };
            int[] hexer = { 11, 20 };
            Assert.AreEqual(0, ChampionThreatRules.StepCount(summoner, 8));
            Assert.AreEqual(1, ChampionThreatRules.StepCount(summoner, 9));
            Assert.AreEqual(1, ChampionThreatRules.StepCount(summoner, 17));
            Assert.AreEqual(2, ChampionThreatRules.StepCount(summoner, 18));
            Assert.AreEqual(2, ChampionThreatRules.StepCount(summoner, 40));
            Assert.AreEqual(0, ChampionThreatRules.StepCount(hexer, 10));
            Assert.AreEqual(1, ChampionThreatRules.StepCount(hexer, 19));
            Assert.AreEqual(2, ChampionThreatRules.StepCount(hexer, 20));
            Assert.AreEqual(3, ChampionThreatRules.StepCount(new[] { 8, 14, 20 }, 23));
        }

        [Test]
        public void BuddyTarget_SkipsWardedAndHoldsFireWithoutOther()
        {
            Assert.IsTrue(ChampionThreatRules.BuddyMayTarget(false));
            Assert.IsFalse(ChampionThreatRules.BuddyMayTarget(true));
            // nächstes Ziel trägt den Schild → das nächste ungeschützte
            Assert.AreEqual(1, ChampionThreatRules.NearestBuddyTarget(new List<float> { 2f, 5f, 8f }, new List<bool> { true, false, false }));
            Assert.AreEqual(0, ChampionThreatRules.NearestBuddyTarget(new List<float> { 2f, 5f }, new List<bool> { false, false }));
            // nur Bannschild-Träger → Feuer halten
            Assert.AreEqual(-1, ChampionThreatRules.NearestBuddyTarget(new List<float> { 2f, 5f }, new List<bool> { true, true }));
            Assert.AreEqual(-1, ChampionThreatRules.NearestBuddyTarget(new List<float>(), new List<bool>()));
        }

        [Test]
        public void InterruptFlag_IsConsumedOnce()
        {
            var f = new ChampionThreatRules.InterruptFlag();
            Assert.IsFalse(f.Consume());
            f.Mark();
            f.Mark(); // mehrere blockierte Frames = eine Unterbrechung
            Assert.IsTrue(f.IsSet);
            Assert.IsTrue(f.Consume());
            Assert.IsFalse(f.Consume());
        }

        [Test]
        public void Rescue_OnlyWhenStuckLongAndUnreachable()
        {
            Assert.IsFalse(ChampionThreatRules.ShouldRescueThreat(19.9f, 20f, false));
            Assert.IsTrue(ChampionThreatRules.ShouldRescueThreat(20f, 20f, false));
            // erreichbar (z. B. Beschwörer im Ritual am Ritualplatz) → bleibt
            Assert.IsFalse(ChampionThreatRules.ShouldRescueThreat(120f, 20f, true));
            // abgeschaltet
            Assert.IsFalse(ChampionThreatRules.ShouldRescueThreat(120f, 0f, false));
        }
    }
}
