using UnityEngine;

namespace ElementalBuddies
{
    // Steinwall (C): temporary wall perpendicular to the aim direction that blocks enemies (NavMesh carving)
    [System.Serializable]
    public class StoneWallSpell : ElementSpell
    {
        [Header("Steinwall")]
        [Tooltip("Optional visual. Instantiated as child of the wall root (pivot = bottom center, local X = along the wall). " +
                 "Without prefab a stone-colored cube is created.")]
        public GameObject WallPrefab;
        [Tooltip("If true, the prefab is authored as a 1x1x1 m unit and gets scaled to Length x Height x Thickness. " +
                 "If false, it is used as-is (author it for Length x Height x Thickness).")]
        public bool WallPrefabIsUnitSize = false;

        public float Length = 6f;
        public float Height = 2.5f;
        public float Thickness = 1f;
        [Tooltip("Distance from the player to the wall center along the aim direction.")]
        public float Distance = 4f;
        public float Lifetime = 7f;
        public float RiseTime = 0.25f;
        public float SinkTime = 0.35f;

        public StoneWallSpell()
        {
            ManaCost = 30f;
            Cooldown = 12f;
        }

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Length = m.Apply(id, AbilityStat.Length, Length);
            Lifetime = m.Apply(id, AbilityStat.Duration, Lifetime);
            Distance = m.Apply(id, AbilityStat.Range, Distance);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Length: return Stat(Length, " m", "Mauerlänge", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(Lifetime, " s", "Standzeit", out value, out unit, out label);
                case AbilityStat.Range: return Stat(Distance, " m", "Abstand", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public override void Cast(SpellCastContext ctx)
        {
            Vector3 pos = ctx.Origin + ctx.AimDirection * Distance;
            pos.y = ctx.Caster != null ? ctx.Caster.GetGroundHeight(pos, ctx.Origin.y) : ctx.Origin.y;
            // Wall faces the player: local Z = aim direction, local X = along the wall
            Quaternion rot = Quaternion.LookRotation(ctx.AimDirection);

            StoneWall.Spawn(this, pos, rot);
            SpawnEffect(pos, rot, Length);
        }
    }
}
