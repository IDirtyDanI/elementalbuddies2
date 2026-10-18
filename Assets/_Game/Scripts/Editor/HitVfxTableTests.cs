using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools.Tests
{
    // Spieltest 2026-10-18, Paket V: jede schadenverursachende Fähigkeit hat genau einen Treffer-Effekt (HitVfxTable),
    // der Arkanball seine neue Optik. Liegt in Assembly-CSharp-Editor (sieht Spielklassen und AssetDatabase).
    public class HitVfxTableTests
    {
        private static HitVfxTableSO Table()
        {
            var t = AssetDatabase.LoadAssetAtPath<HitVfxTableSO>(ArcaneVfxBuilder.TablePath);
            Assert.IsNotNull(t, ArcaneVfxBuilder.TablePath + " (BuddyTD → Game Feel → Arkanball & Treffer-Effekte bauen)");
            return t;
        }

        [Test]
        public void Tabelle_LiegtInResources()
        {
            Assert.IsNotNull(Resources.Load<HitVfxTableSO>(HitVfx.ResourcePath), "HitVfx lädt die Tabelle zur Laufzeit aus Resources");
        }

        [Test]
        public void JedeSchadensFaehigkeit_GenauEinEintragMitPrefab()
        {
            var t = Table();
            foreach (var id in HitVfx.DamagingAbilities)
            {
                int n = 0;
                foreach (var e in t.Entries) if (e != null && e.Ability == id) n++;
                Assert.AreEqual(1, n, id + ": genau ein Tabelleneintrag");
                var entry = t.Find(id);
                Assert.IsNotNull(entry.Prefab, id + ": Prefab");
                Assert.Greater(entry.Scale, 0f, id + ": Größe");
                Assert.AreEqual(ArcaneVfxBuilder.HitPath(entry.Family), AssetDatabase.GetAssetPath(entry.Prefab), id + ": Prefab passt zur Familie");
            }
        }

        [Test]
        public void SchadensListe_DecktAlleChampionsAb()
        {
            // jede Klasse: Grundangriff + alle vier Element-Fähigkeiten außer reinen Heil-/Schutz-/Buff-Zaubern
            var none = new HashSet<AbilityId>
            {
                AbilityId.Blink, AbilityId.StoneWall, AbilityId.HolyCircle,
                AbilityId.ShieldBlock, AbilityId.LightOath, AbilityId.Roll, AbilityId.LightFrenzy
            };
            foreach (AbilityId id in System.Enum.GetValues(typeof(AbilityId)))
                Assert.AreEqual(!none.Contains(id), HitVfx.IsDamaging(id), id.ToString());
        }

        [Test]
        public void TrefferPrefabs_KurzUndOhneSchleife()
        {
            foreach (HitVfxFamily f in System.Enum.GetValues(typeof(HitVfxFamily)))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(ArcaneVfxBuilder.HitPath(f));
                Assert.IsNotNull(go, f.ToString());
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                Assert.IsNotEmpty(systems, f + ": Partikel");
                foreach (var ps in systems)
                {
                    Assert.IsFalse(ps.main.loop, f + "/" + ps.name + ": einmaliger Ausstoß");
                    Assert.LessOrEqual(ps.main.startLifetime.constantMax, 0.7f, f + "/" + ps.name + ": höchstens ~0,6 s");
                }
            }
        }

        [Test]
        public void Arkanball_NeueOptikUndEffekte()
        {
            var ball = AssetDatabase.LoadAssetAtPath<GameObject>(ArcaneVfxBuilder.BallPath);
            var ab = ball.GetComponent<ArcaneBall>();
            Assert.IsNotNull(ab.ImpactVfx, "VFX_ArcaneBurst");
            Assert.IsNotNull(ab.MuzzleVfx, "VFX_ArcaneMuzzle");
            var hum = ball.GetComponent<ArcaneBallHum>();
            Assert.IsNotNull(hum, "ArcaneBallHum am Root");
            Assert.IsNotNull(hum.Clip, "arcane_loop");
            Assert.IsNull(ball.transform.Find("Visual"), "alte Feuerball-Optik entfernt");
            Assert.IsEmpty(ball.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true), "kein FireballFlames mehr");
            var fx = ball.transform.Find("ArcaneFx");
            Assert.IsNotNull(fx);
            Assert.IsNotNull(fx.GetComponentInChildren<TrailRenderer>(true), "Schweif");
            Assert.AreEqual(2, fx.GetComponentsInChildren<VfxSpin>(true).Length, "zwei Runenringe");
            var spins = fx.GetComponentsInChildren<VfxSpin>(true);
            Assert.Less(spins[0].DegreesPerSecond * spins[1].DegreesPerSecond, 0f, "gegenläufig");
            Assert.IsNotNull(fx.GetComponentInChildren<Light>(true), "Punktlicht");
        }
    }
}
