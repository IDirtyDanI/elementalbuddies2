using UnityEngine;

namespace ElementalBuddies
{
    // Flammenwelle (R): cone in aim direction, instant damage + burn over time
    [System.Serializable]
    public class FireWaveSpell : ElementSpell
    {
        [Header("Flammenwelle")]
        public float Range = 7f;
        [Tooltip("Full cone angle in degrees.")]
        public float ConeAngle = 60f;
        public float Damage = 40f;
        public float BurnDps = 8f;
        public float BurnDuration = 3f;
        [Tooltip("Looping flame effect attached to burning enemies (optional).")]
        public GameObject BurnVfxPrefab;

        public FireWaveSpell()
        {
            ManaCost = 35f;
            Cooldown = 6f;
        }

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Range = m.Apply(id, AbilityStat.Range, Range);
            ConeAngle = Mathf.Min(360f, m.Apply(id, AbilityStat.Area, ConeAngle));
            BurnDuration = m.Apply(id, AbilityStat.Duration, BurnDuration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return Stat(Damage * dm, "", "Schaden", out value, out unit, out label);
                case AbilityStat.Range: return Stat(Range, " m", "Reichweite", out value, out unit, out label);
                case AbilityStat.Area: return Stat(ConeAngle, "°", "Kegel", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(BurnDuration, " s", "Brenndauer", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public override void Cast(SpellCastContext ctx)
        {
            Quaternion rot = Quaternion.LookRotation(ctx.AimDirection);
            SpawnEffect(ctx.Origin, rot, Range);

            float halfAngle = ConeAngle * 0.5f;
            foreach (var enemy in FindEnemies(ctx.Origin, Range))
            {
                if (enemy == null) continue;

                Vector3 to = enemy.transform.position - ctx.Origin;
                to.y = 0f;
                // Enemies standing right on top of the player always count
                if (to.sqrMagnitude > 0.25f && Vector3.Angle(ctx.AimDirection, to) > halfAngle) continue;

                if (BurnDps > 0f && BurnDuration > 0f)
                    BurnEffect.Apply(enemy.gameObject, BurnDps * ctx.DamageMultiplier, BurnDuration, BurnVfxPrefab);
                enemy.TakeDamage(Damage * ctx.DamageMultiplier);
            }
        }
    }
}
