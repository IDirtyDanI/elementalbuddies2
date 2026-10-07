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

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            FreezeDuration = m.Apply(id, AbilityStat.Duration, FreezeDuration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return Stat(Damage * dm, "", "Schaden", out value, out unit, out label);
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(FreezeDuration, " s", "Einfrierdauer", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.identity, Radius);
            if (!Net.IsServer) return; // Einfrieren/Schaden nur auf dem Server

            foreach (var enemy in FindEnemies(ctx.Origin, Radius))
            {
                if (enemy == null) continue;
                // Freeze first: damage may kill (destroy) the enemy
                if (FreezeDuration > 0f) enemy.Freeze(FreezeDuration, FrozenVfxPrefab);
                EnemyBrain.DealPlayerDamage(enemy, Damage * ctx.DamageMultiplier); // Quelle Spieler (Telemetrie)
            }
        }
    }
}
