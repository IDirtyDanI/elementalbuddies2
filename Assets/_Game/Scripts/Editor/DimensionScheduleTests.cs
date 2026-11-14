using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools.Tests
{
    // Dimensionsriss im echten Wellenplan (Managers.prefab): Termine 1–60 im Zusammenspiel mit Boss-, Ernte-, Händler-
    // und den normalen Ereignis-Wellen; Zusammensetzung der Event-Welle (kein regulärer Boss, nichts „nur Champion“).
    // Liegt in Assembly-CSharp-Editor (das Test-Assembly unter _Game/Tests sieht die Spielklassen nicht).
    public class DimensionScheduleTests
    {
        private const string ManagersPrefab = "Assets/_Game/Prefabs/Core/Managers.prefab";

        private static WaveManager Waves()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPrefab);
            var wm = prefab.GetComponentInChildren<WaveManager>(true);
            Assert.IsNotNull(wm);
            return wm;
        }

        // Boss-Welle aus den Zusatzgruppen (ohne Dimensionsriss)
        private static bool RegularBossWave(WaveManager wm, int w)
        {
            foreach (var e in wm.ExtraGroups)
                if (e != null && e.Config != null && e.Config.IsBoss && e.GetCount(w) > 0) return true;
            return false;
        }

        [Test]
        public void Schedule_1To60()
        {
            var wm = Waves();
            var mm = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPrefab).GetComponentInChildren<MerchantManager>(true);
            Assert.AreEqual(13, wm.EventFromWave, "normale Ereignisse ab Welle 13");
            Assert.AreEqual(20, wm.DimensionFromWave);
            Assert.AreEqual(5, wm.DimensionEvery);

            var sb = new StringBuilder("[DimensionSchedule] Welle | Ereignis | Boss | Ernte | Händler\n");
            var regular = new List<string>();
            for (int w = 1; w <= 60; w++)
            {
                bool dim = w >= 20 && (w - 20) % 5 == 0;
                var ev = wm.EventFor(w);
                Assert.AreEqual(dim, wm.IsDimensionWave(w), "Welle " + w);
                Assert.AreEqual(dim, ev == WaveEvent.Dimension, "Welle " + w + ": Vorrang des Dimensionsrisses");
                if (dim)
                {
                    Assert.IsTrue(wm.IsBossWave(w), w + ": zählt als Boss-Welle");
                    Assert.IsFalse(wm.IsHarvestWave(w), w + ": selbst keine Ernte-Welle");
                    Assert.IsTrue(wm.IsBossWave(w + 1) || wm.IsHarvestWave(w + 1), (w + 1) + ": danach Ernte (oder regulärer Boss)");
                    if (mm != null) Assert.IsFalse(mm.IsMerchantWave(w, wm), w + ": kein Händler");
                }
                else if (ev != WaveEvent.None)
                {
                    Assert.IsFalse(wm.IsBossWave(w) || wm.IsHarvestWave(w), w + ": normales Ereignis nie in Boss-/Ernte-Wellen");
                    regular.Add(w + " " + ev);
                }
                bool merchant = mm != null && mm.IsMerchantWave(w, wm);
                if (w >= 13)
                    sb.Append(w).Append(" | ").Append(ev == WaveEvent.None ? "–" : WaveEvents.Name(ev)).Append(" | ")
                      .Append(dim ? "Dimension" : RegularBossWave(wm, w) ? "ja" : "–").Append(" | ")
                      .Append(wm.IsHarvestWave(w) ? "ja" : "–").Append(" | ").Append(merchant ? "ja" : "–").Append('\n');
            }
            Debug.Log(sb.ToString());
            // Plan: 13 Blutmond, 16 Ramme – die Ramme kommt weiter vor dem Morgengrauen vor
            Assert.AreEqual("13 BloodMoon", regular[0]);
            Assert.AreEqual("16 Ram", regular[1]);
            Assert.AreEqual(WaveEvent.None, wm.EventFor(12));
        }

        [Test]
        public void EventWave_HasNoRegularBossAndNothingChampionOnly()
        {
            var wm = Waves();
            foreach (int w in new[] { 20, 25, 30, 35, 40, 50, 60 })
            {
                var wc = wm.BuildWave(w);
                int normal = 0;
                foreach (var g in wc.EnemiesToSpawn)
                {
                    if (g == null || g.EnemyType == null) continue;
                    Assert.IsFalse(g.EnemyType.IsBoss, $"Welle {w}: regulärer Boss {g.EnemyType.name}");
                    Assert.IsFalse(g.EnemyType.IsChampionThreat, $"Welle {w}: {g.EnemyType.name}");
                    Assert.IsFalse(g.EnemyType.WardShield, $"Welle {w}: Bannschild {g.EnemyType.name}");
                    normal += g.Count;
                }
                Assert.Greater(normal, 0, "Welle " + w + ": Eskorte auf der Karte");
                foreach (var e in wm.PlanElites(wc, w)) Assert.AreNotEqual(EliteAffix.Warded, e, "Welle " + w + ": keine Elite „Gebannt“");
            }
            // Welle 30 und 50 wären reguläre Boss-Wellen (6 + 4k) – der Boss entfällt
            Assert.IsTrue(RegularBossWave(wm, 30) && RegularBossWave(wm, 50));
        }

        [Test]
        public void EventWave_UsesBossEscortFactor()
        {
            var wm = Waves();
            // gleiche Rampe wie eine Boss-Welle: Normalgegner × BossWaveEscortFactor gegenüber derselben Welle ohne Event
            int withEvent = Count(wm.BuildWave(20));
            int from = wm.DimensionFromWave;
            try
            {
                wm.DimensionFromWave = 0; // Events aus
                wm.ThemeOverride = wm.ThemeOverride; // Cache leeren
                int without = 0;
                foreach (var g in wm.BuildWave(20).EnemiesToSpawn)
                    if (g != null && g.EnemyType != null && !g.EnemyType.IsBoss && !g.EnemyType.IsChampionThreat) without += g.Count;
                // (der Deckel MaxEnemiesPerWave kann die volle Welle kappen – deshalb nur die Größenordnung prüfen)
                Assert.Less(withEvent, without * 0.85f, $"Eskorte {withEvent} deutlich kleiner als die volle Welle {without}");
                Assert.Greater(withEvent, without * wm.BossWaveEscortFactor * 0.6f, "aber nicht leer");
            }
            finally
            {
                wm.DimensionFromWave = from;
                wm.ThemeOverride = wm.ThemeOverride;
            }
        }

        private static int Count(WaveConfigSO wc)
        {
            int n = 0;
            foreach (var g in wc.EnemiesToSpawn) if (g != null) n += g.Count;
            return n;
        }

        [Test]
        public void DimensionBoss_ResolvesOnBothThemes_AndHpFollowsTheRule()
        {
            var wm = Waves();
            try
            {
                foreach (var path in new[] { ThemeSetup.UndeadThemePath, ThemeSetup.ForestThemePath })
                {
                    wm.ThemeOverride = AssetDatabase.LoadAssetAtPath<EnemyThemeSO>(path);
                    var boss = wm.GetDimensionBoss(out bool fallback);
                    Assert.IsNotNull(boss, path);
                    Assert.IsTrue(boss.IsBoss && boss.Prefab != null, boss.name);
                    Assert.IsNotNull(boss.Prefab.GetComponent<BossBrain>(), boss.name + ": BossBrain");
                    if (!fallback) Assert.AreEqual(EnemyRole.DimensionBoss, boss.Role, boss.name + ": eigene Rolle, damit kein Theme sie tauscht");
                }
            }
            finally { wm.ThemeOverride = null; }
            // Leben: Ziel × 1,15^Event (1 Spieler, Stufe des Prefabs/der Sitzung herausgerechnet über das Verhältnis)
            Assert.AreEqual(1.15f, wm.DimensionBossHp(25) / wm.DimensionBossHp(20), 1e-3f);
            Assert.AreEqual(1.15f * 1.15f, wm.DimensionBossHp(30) / wm.DimensionBossHp(20), 1e-3f);
            Assert.AreEqual(1f, wm.DimensionBossHp(23) / wm.DimensionBossHp(20), 1e-3f, "erzwungenes Event zwischen den Terminen zählt wie das vorige");
            Assert.AreEqual(1f, wm.DimensionBossHp(8) / wm.DimensionBossHp(20), 1e-3f, "erzwungenes Event vor Welle 20 zählt wie das erste");
        }

        [Test]
        public void MapAssets_NameTheirDimension()
        {
            MapRegistry.Reload();
            var city = MapRegistry.Find(MapDefinitionSO.CityId);
            var forest = MapRegistry.Find("forest");
            Assert.IsNotNull(city);
            Assert.IsNotNull(forest);
            Assert.AreEqual("Totenreich", city.DimensionName);
            Assert.AreEqual("Kriegslager der Orks", forest.DimensionName);
            foreach (var m in new[] { city, forest })
            {
                Assert.IsNotNull(m.DimensionAtmosphere, m.Id);
                Assert.Greater(m.DimensionAtmosphere.FogDensity, 0.005f, m.Id + ": Nebel verdeckt die ferne Karte");
                Assert.Greater(m.DimensionAtmosphere.SunIntensity, 0.3f, m.Id);
                // Riss-Farbe hebt sich vom normalen Portal-Violett ab
                Color.RGBToHSV(m.DimensionColor, out float h, out _, out _);
                Assert.IsTrue(h < 0.62f || h > 0.9f, m.Id + ": Riss-Farbe zu nah am Portal-Violett");
            }
            Assert.AreNotEqual(city.DimensionAtmosphere.SunColor, forest.DimensionAtmosphere.SunColor);
        }
    }
}
