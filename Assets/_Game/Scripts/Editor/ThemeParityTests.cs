using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools.Tests
{
    // Walddorf-Plan, Paket C-Theme: Gegner-Themes ändern nur die Optik, nie die Balance.
    //  - gleiche Werte je Rolle (alle serialisierten Felder außer der Optik-Liste)
    //  - BuildWave(1…40): gleiche Gruppen, Anzahlen, Intervalle und HP in jedem Theme
    //  - Stadt-Theme = Identität bis auf den Flieger; Ork-Bosse zählen als dieselben Boss-Arten
    // Liegt in Assembly-CSharp-Editor (das Test-Assembly unter _Game/Tests sieht die Spielklassen nicht).
    public class ThemeParityTests
    {
        private const string ManagersPrefab = "Assets/_Game/Prefabs/Core/Managers.prefab";

        // Felder, die je Theme abweichen dürfen (Optik/Ton/Text)
        private static readonly HashSet<string> OptikFields = new HashSet<string>
        {
            "m_Script", "m_Name", "Prefab", "VisualScale", "DisplayName", "PreviewHint", "Icon", "ThemeColor",
            "MeleeHitSfx", "MeleeHitSound", "ProjectilePrefab", "DeathClipDuration", "ProjectileSpawnHeight",
            "AttackSound", "AttackSfx", "DeathSound", "DeathSfx", "SpawnSound", "SpawnSfx", // Klänge (Walddorf Etappe 3)
            "DeathVfx", // Tod-Partikel je Gegnerart (#409)
        };

        private static EnemyThemeSO Forest => AssetDatabase.LoadAssetAtPath<EnemyThemeSO>(ThemeSetup.ForestThemePath);
        private static EnemyThemeSO Undead => AssetDatabase.LoadAssetAtPath<EnemyThemeSO>(ThemeSetup.UndeadThemePath);

        private static List<string> BalanceDiffs(EnemyConfigSO a, EnemyConfigSO b)
        {
            var diffs = new List<string>();
            var sa = new SerializedObject(a);
            var sb = new SerializedObject(b);
            var it = sa.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (OptikFields.Contains(it.name)) continue;
                var other = sb.FindProperty(it.propertyPath);
                if (other == null) { diffs.Add(it.propertyPath + " fehlt"); continue; }
                if (!SerializedProperty.DataEquals(it, other)) diffs.Add(it.propertyPath);
            }
            return diffs;
        }

        [Test]
        public void ForestConfigs_HaveSameBalanceValuesAsCanonical()
        {
            foreach (var v in ThemeSetup.Forest)
            {
                var canonical = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(v.CanonicalPath);
                var forest = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(v.ForestPath);
                Assert.IsNotNull(canonical, v.CanonicalPath);
                Assert.IsNotNull(forest, v.ForestPath);
                Assert.AreEqual(v.Role, canonical.Role, "Rolle kanonisch " + v.Canonical);
                Assert.AreEqual(v.Role, forest.Role, "Rolle Dorf " + v.Name);
                Assert.IsNotNull(forest.Prefab, "Prefab " + v.Name);
                var diffs = BalanceDiffs(canonical, forest);
                Assert.IsEmpty(diffs, $"{v.Name} weicht von {v.Canonical} ab: {string.Join(", ", diffs)}");
            }
        }

        [Test]
        public void Flyers_HaveSameBalanceValuesInBothThemes()
        {
            var bat = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ThemeSetup.FlyerBatPath);
            var wyvern = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ThemeSetup.FlyerWyvernPath);
            if (bat == null || wyvern == null) Assert.Inconclusive("Flieger-Configs fehlen noch (C-Flieger).");
            Assert.AreEqual(EnemyRole.Flyer, bat.Role);
            Assert.AreEqual(EnemyRole.Flyer, wyvern.Role);
            var diffs = BalanceDiffs(bat, wyvern);
            Assert.IsEmpty(diffs, "Flyer_Wyvern weicht von Flyer_Bat ab: " + string.Join(", ", diffs));
        }

        [Test]
        public void Themes_MapEveryRoleOnceWithMatchingRole()
        {
            var forest = Forest;
            Assert.IsNotNull(forest, ThemeSetup.ForestThemePath);
            var seen = new HashSet<EnemyRole>();
            foreach (var e in forest.Roles)
            {
                Assert.IsNotNull(e.Config, "Dorf-Rolle ohne Config: " + e.Role);
                Assert.AreEqual(e.Role, e.Config.Role, "Rolle der Config " + e.Config.name);
                Assert.IsTrue(seen.Add(e.Role), "Rolle doppelt: " + e.Role);
            }
            foreach (var v in ThemeSetup.Forest) Assert.IsTrue(seen.Contains(v.Role), "Dorf-Rolle fehlt: " + v.Role);

            var undead = Undead;
            Assert.IsNotNull(undead, ThemeSetup.UndeadThemePath);
            foreach (var e in undead.Roles)
            {
                // Stadt: nur der Flieger, alle Untoten-Rollen bleiben Identität (byte-gleiches Verhalten)
                Assert.AreEqual(EnemyRole.Flyer, e.Role, "Stadt-Theme darf nur den Flieger tauschen");
                Assert.IsNotNull(e.Config);
                Assert.AreEqual(EnemyRole.Flyer, e.Config.Role);
            }
            if (!seen.Contains(EnemyRole.Flyer) || undead.Roles.Count == 0)
                Assert.Inconclusive("Flieger noch nicht in beiden Themes eingetragen (Flyer_*.asset fehlt?).");
        }

        [Test]
        public void Resolve_IsIdempotentAndKeepsUnmappedRoles()
        {
            var forest = Forest;
            Assert.IsNotNull(forest);
            foreach (var v in ThemeSetup.Forest)
            {
                var canonical = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(v.CanonicalPath);
                var forestCfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(v.ForestPath);
                Assert.AreSame(forestCfg, forest.Resolve(canonical), v.Name);
                Assert.AreSame(forestCfg, forest.Resolve(forestCfg), v.Name + " (idempotent)");
                Assert.AreSame(canonical, Undead.Resolve(canonical), v.Canonical + " in der Stadt");
            }
            Assert.IsNull(forest.Resolve(null));
        }

        // Gruppen einer Welle je Theme: (Rolle, Anzahl, Intervall, HP, Boss)
        private static List<string> Signature(WaveManager wm, EnemyThemeSO theme, int wave, bool skipFlyer = false)
        {
            wm.ThemeOverride = theme;
            var wc = wm.BuildWave(wave);
            var sig = new List<string>();
            foreach (var g in wc.EnemiesToSpawn)
            {
                if (g == null || g.EnemyType == null) { sig.Add("null"); continue; }
                if (skipFlyer && g.EnemyType.Role == EnemyRole.Flyer) continue;
                float hp = g.EnemyType.BaseHP * wm.HpMultiplierFor(g.EnemyType, wave);
                sig.Add($"{g.EnemyType.Role} x{g.Count} @{g.SpawnInterval:0.####} hp{hp:0.###} boss{g.EnemyType.IsBoss} b{g.EnemyType.BountyMultiplier} s{g.EnemyType.Speed}");
            }
            return sig;
        }

        private static WaveManager LoadWaveManager()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPrefab);
            Assert.IsNotNull(prefab, ManagersPrefab);
            var wm = prefab.GetComponentInChildren<WaveManager>(true);
            Assert.IsNotNull(wm, "WaveManager im Managers-Prefab");
            return wm;
        }

        [Test]
        public void BuildWave_1To40_SameCountsAndHpPerTheme()
        {
            var wm = LoadWaveManager();
            var forest = Forest;
            var undead = Undead;
            Assert.IsNotNull(forest);
            Assert.IsNotNull(undead);
            try
            {
                for (int w = 1; w <= 40; w++)
                {
                    var a = Signature(wm, undead, w);
                    var b = Signature(wm, forest, w);
                    CollectionAssert.AreEqual(a, b, $"Welle {w}: Stadt [{string.Join("; ", a)}] ≠ Dorf [{string.Join("; ", b)}]");
                    Assert.AreEqual(Fingerprint(wm, undead, w), Fingerprint(wm, forest, w), $"Welle {w}: Boss/Ernte/Ereignis/Eliten");
                }
            }
            finally { wm.ThemeOverride = null; }
        }

        private static string Fingerprint(WaveManager wm, EnemyThemeSO theme, int w)
        {
            wm.ThemeOverride = theme;
            return $"{wm.IsBossWave(w)}|{wm.IsHarvestWave(w)}|{wm.EventFor(w)}|{string.Join(",", wm.PlanElites(w))}";
        }

        [Test]
        public void CityTheme_IsIdentityExceptFlyerGroup()
        {
            var wm = LoadWaveManager();
            var identity = ScriptableObject.CreateInstance<EnemyThemeSO>(); // leer = Identität, ohne Flieger
            try
            {
                for (int w = 1; w <= 40; w++)
                {
                    wm.ThemeOverride = identity;
                    var plain = wm.BuildWave(w);
                    wm.ThemeOverride = Undead;
                    var city = wm.BuildWave(w);
                    int n = plain.EnemiesToSpawn.Count;
                    // Ohne Flieger-Deckel (MaxEnemiesPerWave) sind alle bisherigen Gruppen unverändert; der Flieger kommt hinten dazu
                    Assert.GreaterOrEqual(city.EnemiesToSpawn.Count, n, "Welle " + w);
                    // Deckel: Bodengruppen + Flieger über MaxEnemiesPerWave → Gruppen werden anteilig gekürzt (Rundung)
                    int plainNormal = 0, flyers = 0;
                    foreach (var g in plain.EnemiesToSpawn) if (g.EnemyType != null && !g.EnemyType.IsBoss) plainNormal += g.Count;
                    foreach (var g in city.EnemiesToSpawn) if (g.EnemyType != null && g.EnemyType.Role == EnemyRole.Flyer) flyers += g.Count;
                    bool capped = wm.MaxEnemiesPerWave > 0 && plainNormal + flyers > wm.MaxEnemiesPerWave;
                    for (int i = 0; i < n; i++)
                    {
                        var p = plain.EnemiesToSpawn[i];
                        var c = city.EnemiesToSpawn[i];
                        Assert.AreSame(p.EnemyType, c.EnemyType, $"Welle {w} Gruppe {i}: Config");
                        Assert.AreEqual(p.SpawnInterval, c.SpawnInterval, $"Welle {w} Gruppe {i}: Intervall");
                        if (!capped) Assert.AreEqual(p.Count, c.Count, $"Welle {w} Gruppe {i}: Anzahl");
                    }
                    for (int i = n; i < city.EnemiesToSpawn.Count; i++)
                        Assert.AreEqual(EnemyRole.Flyer, city.EnemiesToSpawn[i].EnemyType.Role, $"Welle {w}: Zusatzgruppe {i} ist kein Flieger");
                    if (w < wm.FlyerGroup.StartWave) Assert.AreEqual(n, city.EnemiesToSpawn.Count, $"Welle {w}: Flieger vor W{wm.FlyerGroup.StartWave}");
                }
            }
            finally
            {
                wm.ThemeOverride = null;
                Object.DestroyImmediate(identity);
            }
        }

        // Champion-Bedrohungen: feste Anzahl laut Plan/Balancing (Stufen), Beschwörer nie in Ernte-Wellen und in Boss-Wellen
        // höchstens 1; keine Rampe, kein Deckel; nie Elite; Dorf = Stadt (über BuildWave_1To40 oben)
        [Test]
        public void ChampionThreats_FixedCountsPerWave()
        {
            var wm = LoadWaveManager();
            try
            {
                wm.ThemeOverride = Undead;
                int firstSummoner = -1, firstHexer = -1;
                for (int w = 1; w <= 40; w++)
                {
                    var wc = wm.BuildWave(w);
                    int s = 0, h = 0;
                    foreach (var g in wc.EnemiesToSpawn)
                    {
                        if (g == null || g.EnemyType == null) continue;
                        if (g.EnemyType.Role == EnemyRole.Summoner) s += g.Count;
                        if (g.EnemyType.Role == EnemyRole.Hexer) h += g.Count;
                    }
                    int expS = ChampionThreatRules.StepCount(ChampionThreatSetup.SummonerSteps, w);
                    if (wm.IsHarvestWave(w)) expS = 0;
                    else if (wm.IsBossWave(w)) expS = Mathf.Min(expS, 1);
                    int expH = ChampionThreatRules.StepCount(ChampionThreatSetup.HexerSteps, w);
                    Assert.AreEqual(expS, s, "Welle " + w + ": Beschwörer");
                    Assert.AreEqual(expH, h, "Welle " + w + ": Bannwirker");
                    if (s > 0 && firstSummoner < 0) firstSummoner = w;
                    if (h > 0 && firstHexer < 0) firstHexer = w;
                    foreach (var e in wm.PlanElites(w)) Assert.AreNotEqual(EliteAffix.None, e);
                }
                Assert.AreEqual(ChampionThreatSetup.SummonerSteps[0], firstSummoner, "erster Beschwörer");
                Assert.AreEqual(ChampionThreatSetup.HexerSteps[0], firstHexer, "erster Bannwirker");
                var summoner = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ChampionThreatSetup.SummonerConfigPath);
                Assert.IsTrue(summoner.WardShield && summoner.IsChampionThreat);
                // ohne Spät-Rampe (Balancing-Modell P4)
                Assert.AreEqual(wm.HpMultiplier(25) / wm.LateHpFactor(25), wm.HpMultiplierFor(summoner, 25), 1e-4f);
            }
            finally { wm.ThemeOverride = null; }
        }

        [Test]
        public void ForestBosses_CountAsSameBossKinds()
        {
            var db = ScriptableObject.CreateInstance<AchievementDatabaseSO>(); // Standard-BossKinds (Reihenfolge = Bits)
            try
            {
                string[] canonical = { "Bosses/BossBoneLord", "Bosses/BossNecromancer", "Bosses/BossDeathHunter" };
                string[] forest = { "Ork_Kriegsherr", "Goblin_Schamane", "Ork_Faehrtenmeister" };
                for (int i = 0; i < 3; i++)
                {
                    var c = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ThemeSetup.ConfigDir + canonical[i] + ".asset");
                    var f = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ThemeSetup.ForestConfigDir + forest[i] + ".asset");
                    Assert.AreEqual(i, db.GetBossKindIndex(c), canonical[i]);
                    Assert.AreEqual(i, db.GetBossKindIndex(f), forest[i]);
                }
                Assert.AreEqual(3, db.BossKinds.Count, "BossKinds unverändert (Bitmaske)");
                Assert.AreEqual(-1, db.GetBossKindIndex(AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ThemeSetup.ConfigDir + "Runner.asset")));
            }
            finally { Object.DestroyImmediate(db); }
        }

        [Test]
        public void ForestMap_PointsToForestTheme()
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(ThemeSetup.ForestMapPath);
            Assert.IsNotNull(map, ThemeSetup.ForestMapPath);
            Assert.AreEqual("forest", map.Id);
            Assert.AreEqual("Walddorf", map.SceneName);
            Assert.AreEqual("dawn", map.UnlockAchievementId);
            Assert.AreSame(Forest, map.Theme);
            Assert.AreEqual("vor der Westpalisade", map.LocationPhrase(new Vector3(-45f, 0f, 20f)));
        }
    }
}
