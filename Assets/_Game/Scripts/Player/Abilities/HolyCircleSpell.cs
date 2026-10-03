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
        [Tooltip("Kreisende Sterne über dem Kopf geblendeter Gegner.")]
        public GameObject BlindVfxPrefab;

        public HolyCircleSpell()
        {
            ManaCost = 45f;
            Cooldown = 15f;
        }

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            BlindDuration = m.Apply(id, AbilityStat.Duration, BlindDuration);
            PlayerHeal = m.Apply(id, AbilityStat.Heal, PlayerHeal);
            BuddyHeal = m.Apply(id, AbilityStat.Heal, BuddyHeal);
            NexusHeal = m.Apply(id, AbilityStat.Heal, NexusHeal);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(BlindDuration, " s", "Blenddauer", out value, out unit, out label);
                case AbilityStat.Heal: return Stat(PlayerHeal, " LP", "Heilung", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
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
                    if (enemy == null) continue;
                    enemy.ApplySlow(BlindSlow, BlindDuration);
                    BlindEffect.Apply(enemy.gameObject, BlindDuration, BlindVfxPrefab);
                }
            }
        }
    }
}
