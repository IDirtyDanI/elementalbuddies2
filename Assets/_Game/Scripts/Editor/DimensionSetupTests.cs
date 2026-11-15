using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools.Tests
{
    // Dimensionsriss, Paket I: Map-Assets, Boss-Prefabs/-Configs und Arena-Prefabs (Vertrag aus DimensionArena).
    // Liegt in Assembly-CSharp-Editor (das Test-Assembly unter _Game/Tests sieht die Spielklassen nicht).
    public class DimensionSetupTests
    {
        private static readonly string[] Maps = { "Assets/Resources/Maps/Map_City.asset", "Assets/Resources/Maps/Map_Forest.asset" };

        private static MapDefinitionSO Map(string path)
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(path);
            Assert.IsNotNull(map, path);
            return map;
        }

        [Test]
        public void Maps_ReferenceDimensionBossAndArena()
        {
            var seen = new HashSet<EnemyConfigSO>();
            var list = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkPrefabsList>("Assets/_Game/Net/NetworkPrefabs.asset");
            Assert.IsNotNull(list);
            foreach (var path in Maps)
            {
                var map = Map(path);
                var boss = map.DimensionBoss;
                Assert.IsNotNull(boss, path + ": DimensionBoss");
                Assert.IsTrue(seen.Add(boss), path + ": eigener Boss je Map");
                Assert.AreEqual(EnemyRole.DimensionBoss, boss.Role, boss.name);
                Assert.IsTrue(boss.IsBoss, boss.name);
                Assert.IsFalse(string.IsNullOrEmpty(boss.DisplayName), boss.name + ": DisplayName");
                Assert.IsNotNull(boss.Icon, boss.name + ": Icon");
                Assert.IsTrue(boss.SpawnSound, boss.name + ": Schrei");
                Assert.Greater(boss.DeathClipDuration, 0f, boss.name);
                Assert.IsNotNull(boss.Prefab, boss.name + ": Prefab");
                Assert.IsNotNull(boss.Prefab.GetComponent<EnemyBrain>(), boss.name);
                Assert.AreSame(boss, boss.Prefab.GetComponent<EnemyBrain>().Config, boss.name + ": Prefab zeigt auf die Config");
                Assert.IsNotNull(boss.Prefab.GetComponent<EnemyNet>(), boss.name);
                Assert.IsNotNull(boss.Prefab.GetComponent<Unity.Netcode.NetworkObject>(), boss.name);
                Assert.IsNotNull(boss.Prefab.GetComponent<BossBrain>(), boss.name);
                bool inList = false;
                foreach (var p in list.PrefabList) if (p != null && p.Prefab == boss.Prefab) inList = true;
                Assert.IsTrue(inList, boss.name + ": in der Netz-Prefab-Liste");
                Assert.IsNotNull(map.DimensionArenaPrefab, path + ": DimensionArenaPrefab");
            }
        }

        [Test]
        public void DimensionBosses_NotInThemes()
        {
            foreach (var themePath in new[] { ThemeSetup.ForestThemePath, ThemeSetup.UndeadThemePath })
            {
                var theme = AssetDatabase.LoadAssetAtPath<EnemyThemeSO>(themePath);
                if (theme == null) continue;
                var so = new SerializedObject(theme);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is EnemyConfigSO c)
                        Assert.AreNotEqual(EnemyRole.DimensionBoss, c.Role, themePath + ": " + c.name);
            }
        }

        // Ausholzeiten = Trefferzeiten der Clips (.boss.json); Trigger existieren im Controller; Reihenfolge wie im Builder
        [Test]
        public void BossAbilities_MatchSidecarHitTimes()
        {
            foreach (var spec in DimensionSetup.Bosses)
            {
                string sidePath = BossBuilder.ForestModelDir + spec.Model + ".boss.json";
                var side = JsonUtility.FromJson<ForestEnemyBuilder.Sidecar>(System.IO.File.ReadAllText(sidePath));
                var hits = new Dictionary<string, float>();
                foreach (var c in side.clips) hits[c.name] = c.hit;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
                Assert.IsNotNull(prefab, spec.PrefabPath);
                var cfg = prefab.GetComponent<EnemyBrain>().Config;
                Assert.AreEqual(hits["Attack"], cfg.AttackWindup, 1e-3f, spec.Model + ": AttackWindup");
                Assert.AreEqual(side.deathDuration, cfg.DeathClipDuration, 1e-3f, spec.Model + ": DeathClipDuration");
                Assert.AreEqual(side.visualScale, cfg.VisualScale, 1e-3f, spec.Model + ": VisualScale");

                var anim = prefab.GetComponentInChildren<Animator>(true);
                Assert.IsNotNull(anim, spec.Model + ": Animator");
                var ctrl = anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                Assert.IsNotNull(ctrl, spec.Model + ": Controller");
                var triggers = new HashSet<string>();
                foreach (var p in ctrl.parameters) if (p.type == AnimatorControllerParameterType.Trigger) triggers.Add(p.name);
                Assert.IsTrue(triggers.Contains("Attack") && triggers.Contains("Die"), spec.Model + ": Attack/Die");

                var abilities = prefab.GetComponent<BossBrain>().Abilities;
                var expected = spec.Abilities();
                Assert.AreEqual(expected.Count, abilities.Count, spec.Model);
                Assert.AreEqual(4, abilities.Count, spec.Model);
                Assert.AreEqual(1, abilities.FindAll(x => x.Kind == BossAbilityKind.Throw).Count, spec.Model + ": genau ein Wurf");
                for (int i = 0; i < abilities.Count; i++)
                {
                    var a = abilities[i];
                    Assert.AreEqual(JsonUtility.ToJson(expected[i]), JsonUtility.ToJson(a), spec.Model + "/" + a.Name + ": Prefab = Builder");
                    Assert.IsTrue(triggers.Contains(a.AnimatorTrigger), spec.Model + "/" + a.Name + ": Trigger " + a.AnimatorTrigger);
                    Assert.IsTrue(hits.ContainsKey(a.AnimatorTrigger), spec.Model + "/" + a.Name + ": Clip");
                    float hit = a.Windup + (a.Kind == BossAbilityKind.Slam ? a.LeapDuration : 0f);
                    Assert.AreEqual(hits[a.AnimatorTrigger], hit, 1e-3f, spec.Model + "/" + a.Name + ": Ausholzeit = Trefferzeit des Clips");
                    Assert.Greater(a.Cooldown, hit + a.Recovery, spec.Model + "/" + a.Name);
                    if (a.Kind == BossAbilityKind.Throw)
                    {
                        Assert.IsNotNull(a.ProjectilePrefab, spec.Model + "/" + a.Name + ": Geschoss-Modell");
                        Assert.Greater(a.MinTriggerRange, cfg.AttackRange, spec.Model + "/" + a.Name + ": nur auf Abstand");
                        // ausweichbar: sichtbare Warnzeit reicht, um mit Lauftempo 5 m/s die halbe Linienbreite + Figur zu verlassen
                        float warn = a.Windup - a.ThrowAimTime;
                        Assert.Greater(warn * 5f, a.Width * 0.5f + 0.4f + 1f, spec.Model + "/" + a.Name + ": Warnzeit");
                    }
                }
            }
        }

        // Vertrag aus DimensionArena: Anker, Boden nur auf der Scheibe (Layer/Tag Floor), geschlossener Ring, nur Grundkörper
        [Test]
        public void ArenaPrefabs_FulfilContract()
        {
            foreach (var path in Maps)
            {
                var prefab = Map(path).DimensionArenaPrefab;
                Assert.IsNotNull(prefab, path);
                string n = prefab.name;
                Transform entry = null, bossSpawn = null, ret = null;
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == DimensionArena.EntryName && entry == null) entry = t;
                    if (t.name == DimensionArena.BossSpawnName && bossSpawn == null) bossSpawn = t;
                    if (t.name == DimensionArena.ReturnPortalName && ret == null) ret = t;
                }
                Assert.IsNotNull(entry, n + ": Entry");
                Assert.IsNotNull(bossSpawn, n + ": BossSpawn");
                Assert.IsNotNull(ret, n + ": ReturnPortal");
                Vector3 c = bossSpawn.position;
                Assert.Less(new Vector2(c.x, c.z).magnitude, 0.01f, n + ": BossSpawn in der Mitte");
                Assert.Greater(Vector3.Dot(entry.forward, (c - entry.position).normalized), 0.9f, n + ": Entry blickt in die Arena");
                Assert.Greater(Vector3.Dot(ret.forward, (c - ret.position).normalized), 0.9f, n + ": Rückportal öffnet zur Mitte");
                Assert.GreaterOrEqual(Vector3.Distance(entry.position, ret.position), 5f, n + ": Abstand Entry–ReturnPortal");
                Vector3 trigger = ret.position + ret.forward * DimensionArena.ReturnTriggerForward;
                Assert.Less(new Vector2(trigger.x, trigger.z).magnitude + DimensionEventRules.EnterRadius, 16f, n + ": Auslösekreis liegt auf der Scheibe");
                Assert.Less(new Vector2(entry.position.x, entry.position.z).magnitude + 4f * 1.6f * 0.5f, 16f, n + ": Eingang auf der Scheibe");

                Assert.AreEqual(0, prefab.GetComponentsInChildren<Rigidbody>(true).Length, n);
                Assert.AreEqual(0, prefab.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true).Length, n);
                int realtime = 0;
                foreach (var l in prefab.GetComponentsInChildren<Light>(true))
                {
                    Assert.AreEqual(LightShadows.None, l.shadows, n + ": Licht ohne Schatten");
                    realtime++;
                }
                Assert.LessOrEqual(realtime, DimensionSetup.MaxArenaLights, n + ": Lichter");

                var floors = new List<BoxCollider>();
                var walls = new List<BoxCollider>();
                foreach (var col in prefab.GetComponentsInChildren<Collider>(true))
                {
                    Assert.IsFalse(col.isTrigger, n + "/" + col.name);
                    if (col is MeshCollider mc)
                    {
                        // nur lesbare Meshes ergeben im Build ein NavMesh
                        Assert.IsTrue(mc.sharedMesh != null && mc.sharedMesh.isReadable, n + "/" + col.name + ": MeshCollider braucht ein lesbares Mesh");
                    }
                    if (col.gameObject.layer == DimensionArena.FloorLayer)
                    {
                        Assert.AreEqual("Floor", col.gameObject.tag, n + "/" + col.name);
                        Assert.IsInstanceOf<BoxCollider>(col, n + "/" + col.name);
                        floors.Add((BoxCollider)col);
                    }
                    else
                    {
                        Assert.AreEqual(0, col.gameObject.layer, n + "/" + col.name + ": Layer Default");
                        if (col.name.StartsWith("Wall_")) walls.Add((BoxCollider)col);
                    }
                }
                Assert.Greater(floors.Count, 0, n + ": Boden-Collider");
                Assert.GreaterOrEqual(walls.Count, 24, n + ": Mauerring");

                // Boden: Oberseite y = 0; deckt die Scheibe bis unter den Ring, nirgends über den Ring hinaus
                foreach (var f in floors)
                {
                    Assert.AreEqual(0f, f.transform.TransformPoint(f.center + Vector3.up * f.size.y * 0.5f).y, 1e-3f, n + ": Boden-Oberseite");
                    Vector3 corner = f.transform.TransformPoint(f.center + new Vector3(f.size.x, 0f, f.size.z) * 0.5f);
                    Assert.Less(new Vector2(corner.x, corner.z).magnitude, 17.5f, n + ": kein Boden außerhalb des Rings");
                }
                for (int deg = 0; deg < 360; deg += 3)
                {
                    float a = deg * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    Assert.IsTrue(Inside(floors, dir * 16.6f + Vector3.down * 0.1f), n + $": Boden bei {deg}° bis 16,6 m");
                    Assert.IsFalse(Inside(floors, dir * 17.55f + Vector3.down * 0.1f), n + $": kein Boden bei {deg}° jenseits 17,5 m");
                    // Ring: in jeder Richtung steht bei 16,6–17,4 m eine Mauer von Boden bis ≥ 3 m (lückenlos)
                    bool low = false, high = false;
                    for (float r = 16.55f; r <= 17.45f; r += 0.1f)
                    {
                        low |= Inside(walls, dir * r + Vector3.up * 0.1f);
                        high |= Inside(walls, dir * r + Vector3.up * 3f);
                    }
                    Assert.IsTrue(low && high, n + $": Mauer bei {deg}°");
                    Assert.IsFalse(Inside(walls, dir * 16.3f + Vector3.up * 1f), n + $": Mauer ragt bei {deg}° nicht in die Scheibe");
                }
                foreach (var w in walls)
                {
                    Assert.GreaterOrEqual(w.size.y, 3f, n + ": Mauerhöhe");
                    Assert.GreaterOrEqual(w.size.z, 1f, n + ": Mauerdicke");
                }
            }
        }

        private static bool Inside(List<BoxCollider> boxes, Vector3 world)
        {
            foreach (var b in boxes)
            {
                Vector3 p = b.transform.InverseTransformPoint(world) - b.center;
                if (Mathf.Abs(p.x) <= b.size.x * 0.5f && Mathf.Abs(p.y) <= b.size.y * 0.5f && Mathf.Abs(p.z) <= b.size.z * 0.5f) return true;
            }
            return false;
        }

        [Test]
        public void Codex_HasDimensionBossesAndEvent()
        {
            var db = AssetDatabase.LoadAssetAtPath<CodexDatabaseSO>("Assets/Resources/CodexDatabase.asset");
            Assert.IsNotNull(db);
            foreach (var path in Maps)
            {
                var boss = Map(path).DimensionBoss;
                var e = db.Entries.Find(x => x != null && x.Key == Codex.EnemyKey(boss));
                Assert.IsNotNull(e, boss.name + ": Kodex-Eintrag");
                Assert.AreEqual(CodexDatabaseSO.Category.Boss, e.Category);
                Assert.IsNotNull(e.Icon, boss.name + ": Kodex-Symbol");
            }
            Assert.IsNotNull(db.Entries.Find(x => x != null && x.Key == Codex.EventKey(WaveEvent.Dimension)), "Ereignis-Eintrag");
        }
    }
}
