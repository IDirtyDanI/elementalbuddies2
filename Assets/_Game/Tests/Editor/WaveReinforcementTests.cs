using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Wellen-Dichte-Plan A/B (2026-10-16): Verstärkungs-Stöße in Einnahme-Wellen und Schwarm in Rudeln
    // (WaveReinforcement). Dummy-Gegnertyp statt EnemyConfigSO: Die Test-Assembly sieht nur ElementalBuddies.Rules.
    public class WaveReinforcementTests
    {
        private class Cfg
        {
            public string Name; public bool Boss; public bool Flyer;
            public override string ToString() => Name;
        }

        private static readonly Cfg Runner = new Cfg { Name = "Läufer" };
        private static readonly Cfg Swarm = new Cfg { Name = "Schwarm" };
        private static readonly Cfg Knight = new Cfg { Name = "Ritter" };
        private static readonly Cfg Archer = new Cfg { Name = "Bogenschütze" };
        private static readonly Cfg Spider = new Cfg { Name = "Grabspinne" };
        private static readonly Cfg Boss = new Cfg { Name = "Boss", Boss = true };
        private static readonly Cfg Bat = new Cfg { Name = "Flieger", Flyer = true };

        private static WaveReinforcement.Settings Defaults => new WaveReinforcement.Settings
        {
            Density = 0.6f, MaxShare = 0.75f, PulseInterval = 7f, Crescendo = 1f, PackSpacing = 0.3f,
        };

        private static List<WaveReinforcement.Group<Cfg>> Groups(out int main, params (Cfg, int)[] g)
        {
            var types = new List<Cfg>(); var counts = new List<int>();
            foreach (var (c, n) in g) { types.Add(c); counts.Add(n); }
            return WaveReinforcement.NormalGroups(types, counts, c => c.Boss || c.Flyer, out main);
        }

        // W13-Händler (Ist-Werte aus dem Plan): 87 Gegner in 27 s, Lücke 33 s, Läufer-Intervall 1,18 s
        private static WaveReinforcement.Plan<Cfg> W13(out int main, out List<WaveReinforcement.Group<Cfg>> groups)
        {
            groups = Groups(out main, (Runner, 30), (Swarm, 41), (Knight, 8), (Archer, 6), (Spider, 2), (Bat, 5));
            return WaveReinforcement.Build(groups, 27f, main, 33f, 1.18f, Defaults);
        }

        private static int Legacy(float gap, float interval) => (int)Math.Ceiling(gap / interval);

        private static void AssertPlanShape(WaveReinforcement.Plan<Cfg> plan, float gap)
        {
            int sum = 0;
            double prevStart = double.NegativeInfinity;
            int prevSize = 0;
            foreach (var p in plan.Pulses)
            {
                Assert.GreaterOrEqual(p.Enemies.Count, 1, "jeder Stoß mind. 1 Gegner");
                Assert.GreaterOrEqual(p.Enemies.Count, prevSize, "Stoßgrößen nicht fallend");
                Assert.GreaterOrEqual(p.Start, 0f);
                Assert.GreaterOrEqual(p.Start, prevStart + 0.5 - 1e-4, "Stöße mind. 0,5 s auseinander");
                Assert.GreaterOrEqual(p.Spacing, 0.08f - 1e-6f);
                Assert.LessOrEqual(p.Spacing, 0.3f + 1e-6f);
                prevStart = p.Start;
                prevSize = p.Enemies.Count;
                sum += p.Enemies.Count;
            }
            Assert.AreEqual(plan.Total, sum, "Summe der Stöße = N");
            var last = plan.Pulses[plan.Pulses.Count - 1];
            Assert.LessOrEqual(last.Start + (last.Enemies.Count - 1) * last.Spacing, gap + 1e-3, "letzter Stoß endet in der Lücke");
        }

        [Test]
        public void W13_Menge_Stoesse_und_Grenzen()
        {
            var plan = W13(out int main, out _);
            Assert.AreEqual(87, main, "Flieger zählen nicht zur Hauptphase");
            // N = ⌈33 · 87/27 · 0,6⌉ = 64; Deckel ⌈0,75 · 87⌉ = 66; alt ⌈33/1,18⌉ = 28
            Assert.AreEqual(64, plan.Total);
            Assert.GreaterOrEqual(plan.Total, Legacy(33f, 1.18f));
            Assert.LessOrEqual(plan.Total, Math.Max(Legacy(33f, 1.18f), (int)Math.Ceiling(0.75 * main)));
            Assert.AreEqual(5, plan.Pulses.Count, "K = round(33/7) = 5");
            AssertPlanShape(plan, 33f);
            // Crescendo: letzter Stoß deutlich größer als der erste
            Assert.Greater(plan.Pulses[4].Enemies.Count, plan.Pulses[0].Enemies.Count);
        }

        [Test]
        public void Mischung_anteilig_ohne_Bosse_und_Flieger()
        {
            var plan = W13(out int main, out var groups);
            var got = new Dictionary<Cfg, int>();
            foreach (var p in plan.Pulses)
                foreach (var e in p.Enemies)
                {
                    Assert.IsFalse(e.Boss || e.Flyer, "keine Bosse/Flieger in der Verstärkung");
                    got[e] = got.TryGetValue(e, out int n) ? n + 1 : 1;
                }
            foreach (var g in groups)
            {
                double soll = plan.Total * (double)g.Count / main;
                got.TryGetValue(g.Config, out int ist);
                Assert.LessOrEqual(Math.Abs(ist - soll), 1.0, $"{g.Config}: {ist} statt ≈{soll:0.0}");
            }
            // Gemischt: schon im ersten Stoß mehrere Typen
            var first = new HashSet<Cfg>(plan.Pulses[0].Enemies);
            Assert.GreaterOrEqual(first.Count, 2);
        }

        [Test]
        public void Bosse_und_Flieger_werden_ausgefiltert()
        {
            var groups = Groups(out int main, (Boss, 1), (Runner, 10), (Bat, 4), (Knight, 2));
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(12, main);
            var plan = WaveReinforcement.Build(groups, 15f, main, 20f, 1.4f, Defaults);
            foreach (var p in plan.Pulses)
                foreach (var e in p.Enemies) Assert.IsTrue(e == Runner || e == Knight);
        }

        [Test]
        public void Untergrenze_alte_Laeuferfuellung()
        {
            // dünne Hauptphase: N = max(min(⌈30·0,5·0,6⌉ = 9, ⌈0,75·5⌉ = 4), ⌈30/1⌉ = 30) = 30
            var groups = Groups(out int main, (Runner, 5));
            var plan = WaveReinforcement.Build(groups, 10f, main, 30f, 1f, Defaults);
            Assert.AreEqual(30, plan.Total);
            AssertPlanShape(plan, 30f);
        }

        [Test]
        public void Deckel_MaxShare()
        {
            // dichte Hauptphase, lange Lücke: ⌈40 · 10 · 0,6⌉ = 240 → Deckel ⌈0,75 · 100⌉ = 75 (alt ⌈40/1,2⌉ = 34)
            var groups = Groups(out int main, (Runner, 60), (Swarm, 40));
            var plan = WaveReinforcement.Build(groups, 10f, main, 40f, 1.2f, Defaults);
            Assert.AreEqual(75, plan.Total);
            AssertPlanShape(plan, 40f);
        }

        [Test]
        public void Ohne_Crescendo_gleich_grosse_Stoesse()
        {
            var s = Defaults; s.Crescendo = 0f;
            var groups = Groups(out int main, (Runner, 30), (Knight, 10));
            var plan = WaveReinforcement.Build(groups, 20f, main, 28f, 1.3f, s);
            AssertPlanShape(plan, 28f);
            int min = int.MaxValue, max = 0;
            foreach (var p in plan.Pulses) { min = Math.Min(min, p.Enemies.Count); max = Math.Max(max, p.Enemies.Count); }
            Assert.LessOrEqual(max - min, 1);
        }

        [Test]
        public void Kurze_Luecke_mindestens_zwei_Stoesse()
        {
            var groups = Groups(out int main, (Runner, 100), (Swarm, 79));
            var plan = WaveReinforcement.Build(groups, 46f, main, 14f, 1f, Defaults);   // W21-Händler
            Assert.AreEqual(2, plan.Pulses.Count, "K = max(2, round(14/7))");
            AssertPlanShape(plan, 14f);
        }

        [Test]
        public void Leere_Eingaben_ergeben_leeren_Plan()
        {
            var groups = Groups(out int main, (Runner, 10));
            Assert.AreEqual(0, WaveReinforcement.Build(groups, 10f, main, 0f, 1f, Defaults).Total);
            Assert.AreEqual(0, WaveReinforcement.Build(groups, 10f, main, -5f, 1f, Defaults).Pulses.Count);
            var none = Groups(out int m2, (Boss, 1), (Bat, 3));
            Assert.AreEqual(0, m2);
            Assert.AreEqual(0, WaveReinforcement.Build(none, 10f, m2, 20f, 1f, Defaults).Total);
            Assert.AreEqual(0, WaveReinforcement.Build<Cfg>(null, 10f, 0, 20f, 1f, Defaults).Total);
        }

        [Test]
        public void Plan_ist_deterministisch()
        {
            var a = W13(out _, out _);
            var b = W13(out _, out _);
            Assert.AreEqual(a.Pulses.Count, b.Pulses.Count);
            for (int k = 0; k < a.Pulses.Count; k++)
            {
                Assert.AreEqual(a.Pulses[k].Start, b.Pulses[k].Start);
                CollectionAssert.AreEqual(a.Pulses[k].Enemies, b.Pulses[k].Enemies);
            }
        }

        // ---------------- B: Schwarm in Rudeln ----------------

        [Test]
        public void Rudel_decken_die_Hauptphase_ab()
        {
            // Schwarm: Gruppe 1 (Start 2 s), 41 × 0,25 s; übrige Gruppen enden bei 25 s
            var packs = WaveReinforcement.PackSchedule(2f, 41, 0.25f, 6, 25f);
            Assert.AreEqual(7, packs.Count, "⌈41/6⌉");
            int sum = 0;
            foreach (var p in packs) sum += p.Count;
            Assert.AreEqual(41, sum, "Rudel-Summe = Count");
            Assert.AreEqual(2f, packs[0].Start, 1e-4f);
            var last = packs[packs.Count - 1];
            Assert.AreEqual(5, last.Count);
            Assert.AreEqual(25f, last.Start + (last.Count - 1) * 0.25f, 1e-3f, "letztes Rudel endet mit den übrigen Gruppen");
            for (int j = 1; j < packs.Count; j++)
            {
                var prev = packs[j - 1];
                Assert.Greater(packs[j].Start, prev.Start + (prev.Count - 1) * 0.25f, "Rudel überlappen nicht");
            }
        }

        [Test]
        public void Rudel_ohne_laengere_Gruppen_bleiben_zusammenhaengend()
        {
            // übrige Gruppen enden früher → Spanne = eigenes Ende, Rudel lückenlos im Gruppen-Takt
            var packs = WaveReinforcement.PackSchedule(0f, 18, 0.25f, 6, 1f);
            Assert.AreEqual(3, packs.Count);
            Assert.AreEqual(0f, packs[0].Start, 1e-4f);
            Assert.AreEqual(1.5f, packs[1].Start, 1e-4f);
            Assert.AreEqual(3f, packs[2].Start, 1e-4f);
            // keine anderen Gruppen (−∞)
            var solo = WaveReinforcement.PackSchedule(4f, 7, 0.2f, 6, float.NegativeInfinity);
            Assert.AreEqual(2, solo.Count);
            Assert.AreEqual(4f + 6 * 0.2f, solo[1].Start, 1e-4f);
        }

        [Test]
        public void Rudel_nur_fuer_schnelle_grosse_Gruppen()
        {
            Assert.IsTrue(WaveReinforcement.UsesPacks(0.25f, 41, 0.5f, 6));
            Assert.IsFalse(WaveReinforcement.UsesPacks(0.25f, 6, 0.5f, 6), "Count ≤ Rudelgröße");
            Assert.IsFalse(WaveReinforcement.UsesPacks(0.5f, 41, 0.5f, 6), "Intervall nicht unter Schwelle");
            Assert.IsFalse(WaveReinforcement.UsesPacks(0.25f, 41, 0.5f, 0), "0 = keine Rudel");
        }
    }
}
