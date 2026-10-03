using UnityEngine;

namespace ElementalBuddies
{
    // Heiliger Kreis (V): heals player, buddies and Nexus in range, blinds (slows) enemies
    [System.Serializable]
    public class HolyCircleSpell : ElementSpell
    {
        [Header("Heiliger Kreis")]
        public float Radius = 6f;
        public float PlayerHeal = 25f;
        public float BuddyHeal = 25f;
        public float NexusHeal = 15f;
        [Tooltip("EnemyBrain.ApplySlow semantics: speed = base * (1 - value).")]
        [Range(0f, 1f)] public float BlindSlow = 0.6f;
        public float BlindDuration = 3f;

        public HolyCircleSpell()
        {
            ManaCost = 45f;
            Cooldown = 15f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.identity, Radius);

            // Player
            if (ctx.Caster != null && PlayerHeal > 0f)
            {
                var stats = ctx.Caster.GetComponent<PlayerStats>();
                if (stats != null) stats.Heal(PlayerHeal);
            }

            // Buddies
            if (BuddyHeal > 0f)
            {
                var buddies = ElementalBuddy.Active;
                for (int i = buddies.Count - 1; i >= 0; i--)
                {
                    var b = buddies[i];
                    if (b == null) continue;
                    if (HorizontalDistance(b.transform.position, ctx.Origin) <= Radius) b.Heal(BuddyHeal);
                }
            }

            // Nexus (measured to its surface, it is big)
            if (NexusHeal > 0f && Nexus.Instance != null && Nexus.Instance.GetDistanceFrom(ctx.Origin) <= Radius)
                Nexus.Instance.Heal(NexusHeal);

            // Blind enemies
            if (BlindDuration > 0f)
            {
                foreach (var enemy in FindEnemies(ctx.Origin, Radius))
                {
                    if (enemy != null) enemy.ApplySlow(BlindSlow, BlindDuration);
                }
            }
        }
    }
}
