using UnityEngine;

namespace ElementalBuddies
{
    // Frostnova (F): damage + freeze around the player
    [System.Serializable]
    public class FrostNovaSpell : ElementSpell
    {
        [Header("Frostnova")]
        public float Radius = 6f;
        public float Damage = 20f;
        [Tooltip("EnemyBrain.ApplySlow semantics: speed = base * (1 - value). 1 = full stop.")]
        [Range(0f, 1f)] public float FreezeSlow = 1f;
        public float FreezeDuration = 2.5f;

        public FrostNovaSpell()
        {
            ManaCost = 40f;
            Cooldown = 10f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.identity, Radius);

            foreach (var enemy in FindEnemies(ctx.Origin, Radius))
            {
                if (enemy == null) continue;
                // Slow first: damage may kill (destroy) the enemy
                if (FreezeDuration > 0f) enemy.ApplySlow(FreezeSlow, FreezeDuration);
                enemy.TakeDamage(Damage * ctx.DamageMultiplier);
            }
        }
    }
}
