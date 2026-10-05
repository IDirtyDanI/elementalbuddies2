using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Magma (Feuer + Erde): kurze Reichweite, Flächenschaden. Wirft eine Lavakugel auf die Stelle mit den meisten Gegnern:
    // Einschlag = EffectiveDamage im ImpactRadius + Brand, danach bleibt eine Lavapfütze (Schaden über Zeit + leichte Verlangsamung).
    public class MagmaBuddy : FusionBuddy
    {
        [Header("Lavakugel")]
        public float ImpactRadius = 2.2f;
        public float FlightTime = 0.55f;
        public float ArcHeight = 2.5f;
        public float BurnDps = 6f;
        public float BurnDuration = 3f;

        [Header("Lavapfütze")]
        public float PuddleRadius = 1.8f;
        public float PuddleLifetime = 4f;
        [Tooltip("Pfützen-Schaden pro Sekunde als Anteil von EffectiveDamage")]
        public float PuddleDamageFactor = 0.5f;
        [Range(0f, 1f)] public float PuddleSlow = 0.15f;
        public float PuddleSlowDuration = 0.6f;
        public int MaxPuddles = 3;

        [Header("Optik")]
        public GameObject LavaBallPrefab;   // optional; leer = Laufzeit-Kugel
        public GameObject PuddlePrefab;     // optional (Radius 1, wird skaliert); leer = Laufzeit-Scheibe
        public GameObject ImpactVfxPrefab;  // optional, 2 s
        public GameObject BurnVfxPrefab;    // optional, an BurnEffect übergeben
        public float RuntimeBallSize = 0.45f;

        private readonly List<EnemyBrain> _inRange = new List<EnemyBrain>();
        private readonly List<EnemyBrain> _nearby = new List<EnemyBrain>();
        private readonly List<LavaPuddle> _puddles = new List<LavaPuddle>();
        private static readonly List<EnemyBrain> _impactBuffer = new List<EnemyBrain>();

        protected override FusionElement DefaultElement => FusionElement.Magma;

        public float PuddleDps => EffectiveDamage * PuddleDamageFactor;

        // Werte eines Wurfs (beim Abwurf festgehalten, damit der Einschlag ohne den Buddy funktioniert)
        private struct ImpactData
        {
            public MagmaBuddy Owner;
            public float Damage, Radius, BurnDps, BurnDuration, BossMultiplier;
            public float PuddleRadius, PuddleLifetime, PuddleDps, PuddleSlow, PuddleSlowDuration;
            public GameObject PuddlePrefab, ImpactVfx, BurnVfx;
        }

        protected override bool TryPerformAction()
        {
            _inRange.Clear();
            _inRange.AddRange(FindEnemies(transform.position, EffectiveRange));
            if (_inRange.Count == 0) return false;

            // Kandidaten = Gegner-Positionen; gewählt wird die mit den meisten Gegnern im ImpactRadius
            _nearby.Clear();
            _nearby.AddRange(FindEnemies(transform.position, EffectiveRange + ImpactRadius));
            EnemyBrain best = null;
            int bestCount = -1;
            float r2 = ImpactRadius * ImpactRadius;
            foreach (var c in _inRange)
            {
                Vector3 cp = c.transform.position;
                int count = 0;
                foreach (var o in _nearby)
                {
                    Vector3 d = o.transform.position - cp;
                    d.y = 0f;
                    if (d.sqrMagnitude <= r2) count++;
                }
                if (count > bestCount)
                {
                    bestCount = count;
                    best = c;
                }
            }
            if (best == null) return false;

            CurrentTarget = best.transform;
            var data = new ImpactData
            {
                Owner = this,
                Damage = EffectiveDamage,
                Radius = ImpactRadius,
                BurnDps = BurnDps,
                BurnDuration = BurnDuration,
                BossMultiplier = BossDamageMultiplier,
                PuddleRadius = PuddleRadius,
                PuddleLifetime = PuddleLifetime,
                PuddleDps = PuddleDps,
                PuddleSlow = PuddleSlow,
                PuddleSlowDuration = PuddleSlowDuration,
                PuddlePrefab = PuddlePrefab,
                ImpactVfx = ImpactVfxPrefab,
                BurnVfx = BurnVfxPrefab,
            };
            LavaBall.Launch(FirePosition, best.transform.position, FlightTime, ArcHeight, LavaBallPrefab, RuntimeBallSize,
                point => Impact(point, data));
            return true;
        }

        private static void Impact(Vector3 point, ImpactData data)
        {
            SpawnVfx(data.ImpactVfx, point, 2f);

            // Schaden + Brand (Gegner, die inzwischen gestorben sind, fallen über die Abfrage heraus)
            _impactBuffer.Clear();
            LavaPuddle.CollectEnemies(point, data.Radius, _impactBuffer);
            foreach (var e in _impactBuffer)
            {
                if (e == null || e.CurrentHP <= 0f) continue;
                float boss = e.IsBoss ? data.BossMultiplier : 1f;
                BurnEffect.Apply(e.gameObject, data.BurnDps * boss, data.BurnDuration, data.BurnVfx);
                e.TakeDamage(data.Damage * boss);
            }

            var puddle = LavaPuddle.Spawn(GroundPoint(point), data.PuddleRadius, data.PuddleLifetime, data.PuddleDps,
                data.PuddleSlow, data.PuddleSlowDuration, data.PuddlePrefab, data.BossMultiplier);
            if (data.Owner != null) data.Owner.RegisterPuddle(puddle);
        }

        // Pfützen-Limit pro Buddy: älteste klingt aus
        private void RegisterPuddle(LavaPuddle puddle)
        {
            _puddles.RemoveAll(p => p == null || p.IsExpired);
            _puddles.Add(puddle);
            while (_puddles.Count > Mathf.Max(1, MaxPuddles))
            {
                _puddles[0].Expire();
                _puddles.RemoveAt(0);
            }
        }

        // Boden unter dem Einschlag (Gegner, Buddies, Spieler und Trigger ignorieren); Fallback = Einschlaghöhe
        private static Vector3 GroundPoint(Vector3 point)
        {
            var hits = Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            Vector3 result = point;
            foreach (var h in hits)
            {
                if (h.collider.CompareTag("Enemy") || h.collider.CompareTag("Player")) continue;
                if (h.collider.GetComponentInParent<ElementalBuddy>() != null) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    result = h.point;
                }
            }
            return result;
        }
    }
}
