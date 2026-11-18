using System.Collections.Generic;
using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Bestenliste der eigenen Läufe (2026-11-18): Rang, Begrenzung, JSON-Rundlauf, Markierung „per Video wiederbelebt“
    public class RunHistoryRulesTests
    {
        private static RunRecord Run(string id, int wave, float seconds = 600f, long time = 1000, bool continued = false, string map = "city")
            => new RunRecord { Id = id, Wave = wave, Seconds = seconds, Time = time, Continued = continued, Map = map };

        [Test]
        public void Rang_Welle_absteigend_dann_kuerzer_dann_neuer()
        {
            var runs = new List<RunRecord>
            {
                Run("a", 10, 900f, 1), Run("b", 25, 1500f, 2), Run("c", 10, 700f, 3), Run("d", 10, 700f, 4), Run("e", 3, 100f, 5),
            };
            var r = RunHistoryRules.Ranked(runs);
            CollectionAssert.AreEqual(new[] { "b", "d", "c", "a", "e" }, r.ConvertAll(x => x.Id));
            Assert.AreEqual(2, RunHistoryRules.RankOf(runs, "d"));
            Assert.AreEqual(0, RunHistoryRules.RankOf(runs, "fehlt"));
        }

        [Test]
        public void Filter_nach_Map()
        {
            var runs = new List<RunRecord> { Run("a", 5, map: "city"), Run("b", 9, map: "forest"), Run("c", 7, map: "city") };
            CollectionAssert.AreEqual(new[] { "c", "a" }, RunHistoryRules.Ranked(runs, "city").ConvertAll(x => x.Id));
            Assert.AreEqual(3, RunHistoryRules.Ranked(runs, null).Count);
        }

        [Test]
        public void Upsert_ersetzt_gleiche_Id()
        {
            var runs = new List<RunRecord>();
            RunHistoryRules.Upsert(runs, Run("lauf", 20, 1200f, 10));        // Morgengrauen
            RunHistoryRules.Upsert(runs, Run("lauf", 24, 1500f, 20));        // späteres Game Over desselben Laufs
            RunHistoryRules.Upsert(runs, Run("anderer", 8, 300f, 30));
            Assert.AreEqual(2, runs.Count);
            Assert.AreEqual(24, runs[0].Wave);
        }

        [Test]
        public void Upsert_derselbe_Lauf_wird_nie_schlechter()
        {
            var runs = new List<RunRecord>();
            var dawn = Run("lauf", 20, 1200f, 10, continued: true);
            dawn.Dawn = true;
            RunHistoryRules.Upsert(runs, dawn);
            RunHistoryRules.Upsert(runs, Run("lauf", 12, 1300f, 20));
            Assert.AreEqual(1, runs.Count);
            Assert.AreEqual(20, runs[0].Wave);
            Assert.IsTrue(runs[0].Dawn);
            Assert.IsTrue(runs[0].Continued);
        }

        [Test]
        public void Begrenzung_behaelt_Beste_und_die_neuesten()
        {
            var runs = new List<RunRecord>();
            // 60 alte Läufe mit Welle 1..60, danach 10 schwache neue (Welle 1)
            for (int i = 1; i <= 60; i++) RunHistoryRules.Upsert(runs, Run("alt" + i, i, 600f, i));
            for (int i = 0; i < RunHistoryRules.KeepRecent; i++) RunHistoryRules.Upsert(runs, Run("neu" + i, 1, 600f, 1000 + i));
            Assert.AreEqual(RunHistoryRules.MaxEntries, runs.Count);
            var ids = runs.ConvertAll(x => x.Id);
            for (int i = 0; i < RunHistoryRules.KeepRecent; i++) Assert.Contains("neu" + i, ids);
            Assert.Contains("alt60", ids);
            Assert.Contains("alt21", ids);              // 40 beste alte (21..60) + 10 neue = 50
            Assert.IsFalse(ids.Contains("alt20"));
            Assert.AreEqual("alt60", runs[0].Id);       // gespeichert in Rang-Reihenfolge
        }

        [Test]
        public void Begrenzung_schwacher_neuer_Lauf_bleibt_vorerst()
        {
            var runs = new List<RunRecord>();
            for (int i = 0; i < RunHistoryRules.MaxEntries; i++) RunHistoryRules.Upsert(runs, Run("gut" + i, 30, 600f, i));
            RunHistoryRules.Upsert(runs, Run("schwach", 2, 600f, 999));
            Assert.AreEqual(RunHistoryRules.MaxEntries, runs.Count);
            Assert.IsTrue(runs.Exists(x => x.Id == "schwach"));
        }

        [Test]
        public void Json_Rundlauf_mit_Markierung()
        {
            var runs = new List<RunRecord>
            {
                new RunRecord { Id = "x", Time = 1763470000, Map = "forest", MapName = "Walddorf", Champion = 3, ChampionName = "Berserker",
                    Difficulty = "schwer", DifficultyName = "Schwer", Siege = 2, Wave = 21, Seconds = 1834.5f, Reason = "Der Nexus ist gefallen",
                    Dawn = true, Coop = true, Continued = true, Kills = 1234, TeamWipes = 2 },
                Run("y", 4),
            };
            var back = RunHistoryRules.FromJson(RunHistoryRules.ToJson(runs));
            Assert.AreEqual(2, back.Count);
            var a = back[0];
            Assert.AreEqual("x", a.Id);
            Assert.AreEqual(1763470000, a.Time);
            Assert.AreEqual("Walddorf", a.MapName);
            Assert.AreEqual(3, a.Champion);
            Assert.AreEqual(2, a.Siege);
            Assert.AreEqual(21, a.Wave);
            Assert.AreEqual(1834.5f, a.Seconds, 0.001f);
            Assert.IsTrue(a.Dawn && a.Coop && a.Continued);
            Assert.AreEqual(1234, a.Kills);
            Assert.AreEqual(2, a.TeamWipes);
            Assert.IsFalse(back[1].Continued);
            Assert.AreEqual(RunHistoryRules.ContinuedMark, RunHistoryRules.Marks(a));
            Assert.AreEqual("", RunHistoryRules.Marks(back[1]));
        }

        [Test]
        public void Json_kaputt_oder_leer_gibt_leere_Liste()
        {
            Assert.AreEqual(0, RunHistoryRules.FromJson(null).Count);
            Assert.AreEqual(0, RunHistoryRules.FromJson("").Count);
            Assert.AreEqual(0, RunHistoryRules.FromJson("{kaputt").Count);
            Assert.AreEqual(0, RunHistoryRules.FromJson("{}").Count);
        }

        [Test]
        public void Nur_echte_Laeufe_ohne_Cheats()
        {
            Assert.IsTrue(RunHistoryRules.ShouldRecord(1, false));
            Assert.IsFalse(RunHistoryRules.ShouldRecord(0, false));
            Assert.IsFalse(RunHistoryRules.ShouldRecord(25, true));
        }

        [Test]
        public void Dauer_und_Zeit_formatieren()
        {
            Assert.AreEqual("0:00", RunHistoryRules.FormatDuration(-3f));
            Assert.AreEqual("12:34", RunHistoryRules.FormatDuration(754.9f));
            Assert.AreEqual("1:02:03", RunHistoryRules.FormatDuration(3723f));
            var t = new System.DateTime(2026, 11, 18, 12, 0, 0, System.DateTimeKind.Utc);
            Assert.AreEqual(t, RunHistoryRules.FromUnix(RunHistoryRules.ToUnix(t)));
        }
    }
}
