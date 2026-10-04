using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Vulkan-Titan (Feuer + Eis + Erde): schwerer Belagerer. Ruft alle ~3,2 s einen Meteor auf die dichteste Gegnergruppe in Reichweite:
    // fällt in FallTime vom Himmel (Boden-Telegraph), Einschlag = EffectiveDamage im ImpactRadius + Betäubung + kleiner Rückstoß,
    // danach ein Krater (LavaPuddle: Verlangsamung + Schaden über Zeit). Nimmt weniger Schaden (DamageReduction).
    public class VolcanoTitanBuddy : SuperBuddy
    {
        [Header("Meteor")]
        public float ImpactRadius = 3.5f;
        public float FallTime = 0.9f;
        public float FallHeight = 16f;
        public float StunDuration = 0.8f;
        public float KnockbackDistance = 1.2f;

        [Header("Krater")]
        public float CraterRadius = 3f;
        public float CraterLifetime = 5f;
        public float CraterDps = 10f;
        [Range(0f, 1f)] public float CraterSlow = 0.4f;
        public float CraterSlowDuration = 0.6f;

        [Header("Optik")]
        public GameObject MeteorPrefab;       // optional; leer = Laufzeit-Kugel
        public GameObject CraterPrefab;       // optional (Radius 1, wird skaliert); leer = Laufzeit-Scheibe
        public GameObject ImpactVfxPrefab;    // optional, 2.5 s
        public GameObject TelegraphPrefab;    // optional (Radius 1, wird skaliert), lebt FallTime; leer = Laufzeit-Ring
        public GameObject StunVfxPrefab;      // optional, an EnemyBrain.Stun übergeben
        public float RuntimeMeteorSize = 1.1f;
        public Color TelegraphColor = new Color(1f, 0.4f, 0.1f, 0.9f);

        private static readonly List<EnemyBrain> _impactBuffer = new List<EnemyBrain>();

        protected override FusionElement DefaultElement => FusionElement.VolcanoTitan;

        protected override void ApplyClassDefaults()
        {
            BaseMaxHP = 600f;
            DamageReduction = 0.2f;
        }

        protected override bool TryPerformAction()
        {
            var best = FindDensestEnemy(transform.position, EffectiveRange, ImpactRadius, out _);
            if (best == null) return false;
            CurrentTarget = best.transform;
            LaunchMeteor(GroundPoint(best.transform.position));
            return true;
        }

        // Öffentlich für Tests
        public void LaunchMeteor(Vector3 ground)
        {
            float damage = EffectiveDamage;
            float radius = ImpactRadius, stun = StunDuration, knock = KnockbackDistance;
            float craterR = CraterRadius, craterLife = CraterLifetime, craterDps = CraterDps, craterSlow = CraterSlow, craterSlowDur = CraterSlowDuration;
            GameObject craterPrefab = CraterPrefab, impactVfx = ImpactVfxPrefab, stunVfx = StunVfxPrefab;

            // Telegraph am Boden
            if (TelegraphPrefab != null)
            {
                var t = Instantiate(TelegraphPrefab, ground + Vector3.up * 0.05f, Quaternion.identity);
                t.transform.localScale = Vector3.Scale(t.transform.localScale, new Vector3(radius, 1f, radius));
                Destroy(t, FallTime);
            }
            else
            {
                FusionLineFx.SpawnRing(ground + Vector3.up * 0.08f, radius, radius * 0.9f, null, TelegraphColor, 0.12f, FallTime, "MeteorTelegraph");
            }

            Vector3 from = ground + Vector3.up * FallHeight + (ground - transform.position).normalized * 3f;
            LavaBall.Launch(from, ground, FallTime, 0f, MeteorPrefab, RuntimeMeteorSize, point =>
            {
                SpawnVfx(impactVfx, point, 2.5f);
                _impactBuffer.Clear();
                LavaPuddle.CollectEnemies(point, radius, _impactBuffer);
                foreach (var e in _impactBuffer)
                {
                    if (e == null || e.IsDead) continue;
                    Vector3 away = e.transform.position - point;
                    e.Stun(stun, stunVfx);
                    if (knock > 0f) e.Knockback(away.sqrMagnitude > 0.01f ? away : e.PathBackDirection, knock, 0.2f);
                    e.TakeDamage(damage);
                }
                LavaPuddle.Spawn(point, craterR, craterLife, craterDps, craterSlow, craterSlowDur, craterPrefab);
                GameAudio.Play(SfxId.StoneWall, point);
            });
        }
    }
}
