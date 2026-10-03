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
        [Tooltip("Gegner stehen so lange komplett still (keine Bewegung, keine Angriffe).")]
        public float FreezeDuration = 2.5f;
        [Tooltip("Eiskristalle/Frost am eingefrorenen Gegner.")]
        public GameObject FrozenVfxPrefab;

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
                // Freeze first: damage may kill (destroy) the enemy
                if (FreezeDuration > 0f) enemy.Freeze(FreezeDuration, FrozenVfxPrefab);
                enemy.TakeDamage(Damage * ctx.DamageMultiplier);
            }
        }
    }
}
