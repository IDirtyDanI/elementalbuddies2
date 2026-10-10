using UnityEngine;

namespace ElementalBuddies
{
    public class TankBuddy : ElementalBuddy
    {
        [Header("Tank Stats")]
        public float TauntRadius = 4f;
        public float TauntDuration = 2f;
        public float DamageReduction = 0.15f;
        public float AuraDps = 8f;

        [Header("Stufe 4 – Steinhaut (Bergkönig)")]
        [Tooltip("Andere Buddies im Radius nehmen so viel weniger Schaden (stapelt nicht).")]
        [Range(0f, 0.9f)] public float Stage4StoneSkinReduction = 0.2f;
        [Tooltip("Faktor auf den Spott-/Aura-Radius ab Stufe 4.")]
        public float Stage4TauntRadiusFactor = 1.25f;

        private float _lastAuraTime;

        // Schadens-Multiplikator für einen Buddy durch die stärkste Steinhaut-Aura (nicht für den Tank selbst, kein Stapeln)
        public static float GetDamageTakenMultiplier(ElementalBuddy buddy)
        {
            if (buddy == null) return 1f;
            float best = 1f;
            Vector3 pos = buddy.transform.position;
            var active = Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (!(active[i] is TankBuddy tank) || tank == buddy || !tank.HasPerk || tank.IsStunned || !tank.isActiveAndEnabled) continue;
                float r = tank.EffectiveRange;
                Vector3 d = tank.transform.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude <= r * r) best = Mathf.Min(best, 1f - tank.Stage4StoneSkinReduction);
            }
            return best;
        }

        // Earth-Prefab hat kein serialisiertes BaseMaxHP -> Tank-Standard hier (Config.BuddyMaxHP hat Vorrang)
        protected override float DefaultMaxHP => 100f;

        // Tank: "Damage" = Aura-DPS, "Range" = Taunt-/Aura-Radius, "FireRate" = Taunt-Rate (Config, z. B. 0.16 = alle ~6 s)
        protected override float GetBaseDamageAtLevel(int level) => AuraDps * LevelMultiplier(DamageBonusPerLevel, level);
        public override float GetRangeAtLevel(int level) =>
            TauntRadius * LevelMultiplier(RangeBonusPerLevel, level) * (level >= PerkLevel ? Stage4TauntRadiusFactor : 1f);

        public override void TakeDamage(float amount)
        {
            float reduced = amount * (1f - DamageReduction);
            base.TakeDamage(reduced);
        }

        protected override bool TryPerformAction()
        {
            // Taunt logic (controlled by EffectiveFireRate, Config.FireRate ~1/6 for 6s CD)
            Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveRange);
            bool tauntedAny = false;
            foreach (var hit in hits)
            {
                if (hit.CompareTag("Enemy"))
                {
                    var tauntable = hit.GetComponent<ITauntable>();
                    if (tauntable != null)
                    {
                        if (Net.IsServer) tauntable.Taunt(transform, TauntDuration); // Spott nur auf dem Server
                        tauntedAny = true;
                    }
                }
            }
            return tauntedAny;
        }

        protected override void Update()
        {
            base.Update();
            if (IsStunned) return;

            // Aura Logic (1 tick per second) – Schaden nur auf dem Server
            if (Net.IsServer && Time.time >= _lastAuraTime + 1f)
            {
                _lastAuraTime = Time.time;
                Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveRange);
                float auraDps = EffectiveDamage;
                float slow = CardEffects.EarthSlow; // Wellenkarte „Schwere Erde“
                var prevSource = EnemyBrain.DamageSource;
                EnemyBrain.DamageSource = this; // Kartensynergien und Run-Statistik
                try
                {
                    foreach (var hit in hits)
                    {
                        if (hit.CompareTag("Enemy"))
                        {
                            var dmg = hit.GetComponent<IDamageable>();
                            if (dmg != null) dmg.TakeDamage(auraDps);
                            if (slow > 0f)
                            {
                                var slowable = hit.GetComponent<ISlowable>();
                                if (slowable != null) slowable.ApplySlow(slow, 1.3f);
                            }
                        }
                    }
                }
                finally { EnemyBrain.DamageSource = prevSource; }
            }
        }
    }
}