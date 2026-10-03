using UnityEngine;

namespace ElementalBuddies
{
    // Element-Fähigkeiten des Bogenschützen (R/F/C/V). Werte im Inspector des ArcherKit.

    // Feuerpfeil-Regen (R): Pfeile regnen über RainDuration aufs Zielgebiet am Mauszeiger, Schaden in Wellen + Brand
    [System.Serializable]
    public class FireArrowRainSpell : ElementSpell
    {
        [Header("Feuerpfeil-Regen")]
        public float MaxCastRange = 16f;
        public float Radius = 3.5f;
        public float RainDuration = 1.5f;
        [Tooltip("Anzahl der Schadens-Wellen über die Dauer.")]
        public int Waves = 6;
        public float DamagePerWave = 10f;
        public float BurnDps = 8f;
        public float BurnDuration = 3f;
        public GameObject BurnVfxPrefab;
        [Tooltip("Fallender Feuerpfeil (nur Optik, wird pro Welle mehrfach gespawnt).")]
        public GameObject FallingArrowPrefab;
        [Tooltip("Einschlag-Funken eines Pfeils.")]
        public GameObject ImpactFxPrefab;
        public int ArrowsPerWave = 5;

        public FireArrowRainSpell()
        {
            ManaCost = 40f;
            Cooldown = 9f;
        }

        public Vector3 TargetPoint(SpellCastContext ctx)
        {
            Vector3 target = ctx.HasAimPoint ? ctx.AimPoint : ctx.Origin + ctx.AimDirection * (MaxCastRange * 0.5f);
            Vector3 to = target - ctx.Origin;
            to.y = 0f;
            if (to.magnitude > MaxCastRange) target = ctx.Origin + to.normalized * MaxCastRange;
            if (ctx.Caster != null) target.y = ctx.Caster.GetGroundHeight(target, ctx.Origin.y);
            return target;
        }

        public override void Cast(SpellCastContext ctx)
        {
            Vector3 target = TargetPoint(ctx);
            SpawnEffect(target, Quaternion.identity, Radius);
            var go = new GameObject("ArrowRain");
            go.transform.position = target;
            var area = go.AddComponent<ArrowRainArea>();
            area.Setup(this, ctx.DamageMultiplier);
        }
    }

    // Frostpfeil (F): durchschlagender Pfeil, friert jeden getroffenen Gegner ein
    [System.Serializable]
    public class FrostArrowSpell : ElementSpell
    {
        [Header("Frostpfeil")]
        public float Damage = 35f;
        public float FreezeDuration = 2f;
        public float Speed = 30f;
        public float Range = 24f;
        public GameObject ArrowPrefab;
        public GameObject FrozenVfxPrefab;

        public FrostArrowSpell()
        {
            ManaCost = 30f;
            Cooldown = 8f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as ArcherKit : null;
            Vector3 pos = kit != null ? kit.MuzzlePosition : ctx.Origin + Vector3.up * 1.2f;
            var arrow = ArcherKit.LaunchArrow(ArrowPrefab, pos, ctx.AimDirection, Speed, Damage * ctx.DamageMultiplier, Range, true);
            if (arrow != null)
            {
                float freeze = FreezeDuration;
                GameObject vfx = FrozenVfxPrefab;
                arrow.OnHitEnemy = e => { if (freeze > 0f) e.Freeze(freeze, vfx); };
            }
            SpawnEffect(pos, Quaternion.LookRotation(ctx.AimDirection), 1f);
            GameAudio.Play(SfxId.FrostNova, pos);
        }
    }

    // Dornenfalle (C): Falle am Mauszeiger; die ersten Gegner, die hineinlaufen, werden festgehalten + verletzt
    [System.Serializable]
    public class ThornTrapSpell : ElementSpell
    {
        [Header("Dornenfalle")]
        public float MaxCastRange = 10f;
        public float TriggerRadius = 2.2f;
        public float ArmTime = 0.4f;
        public float Lifetime = 20f;
        public float Damage = 40f;
        public float RootDuration = 2.5f;
        [Tooltip("So viele Gegner werden beim Auslösen höchstens erfasst.")]
        public int MaxTargets = 6;
        [Tooltip("Optik der Falle (Wurzel-/Dornenring). Wird beim Auslösen per Animator-losem Skalieren 'zugeschnappt'.")]
        public GameObject TrapPrefab;
        [Tooltip("Dornen an den Füßen festgehaltener Gegner.")]
        public GameObject RootVfxPrefab;
        [Tooltip("Ausbruch beim Zuschnappen.")]
        public GameObject SnapFxPrefab;

        public ThornTrapSpell()
        {
            ManaCost = 30f;
            Cooldown = 12f;
        }

        public Vector3 TargetPoint(SpellCastContext ctx)
        {
            Vector3 target = ctx.HasAimPoint ? ctx.AimPoint : ctx.Origin + ctx.AimDirection * 3f;
            Vector3 to = target - ctx.Origin;
            to.y = 0f;
            if (to.magnitude > MaxCastRange) target = ctx.Origin + to.normalized * MaxCastRange;
            if (to.magnitude < 1.5f) target = ctx.Origin + ctx.AimDirection * 1.5f; // nicht unter die eigenen Füße
            if (ctx.Caster != null) target.y = ctx.Caster.GetGroundHeight(target, ctx.Origin.y);
            return target;
        }

        public override void Cast(SpellCastContext ctx)
        {
            Vector3 target = TargetPoint(ctx);
            SpawnEffect(target, Quaternion.identity, TriggerRadius);
            var go = TrapPrefab != null ? Object.Instantiate(TrapPrefab, target, Quaternion.LookRotation(ctx.AimDirection)) : new GameObject("ThornTrap");
            go.transform.position = target;
            var trap = go.GetComponent<ThornTrap>();
            if (trap == null) trap = go.AddComponent<ThornTrap>();
            trap.Setup(this, ctx.DamageMultiplier);
            GameAudio.Play(SfxId.StoneWall, target);
        }
    }

    // Lichtpfeil (V): sofortiger Lichtstrahl durch alle Gegner in Blickrichtung, blendet
    [System.Serializable]
    public class LightArrowSpell : ElementSpell
    {
        [Header("Lichtpfeil")]
        public float Length = 25f;
        public float Width = 1.2f;
        public float Damage = 55f;
        public float BlindDuration = 3f;
        [Tooltip("EnemyBrain.ApplySlow-Semantik: Tempo = Basis × (1 − Wert).")]
        [Range(0f, 1f)] public float BlindSlow = 0.5f;
        public GameObject BlindVfxPrefab;
        [Tooltip("Strahl-Optik (BeamFx).")]
        public GameObject BeamPrefab;
        public GameObject HitFxPrefab;
        public LayerMask ObstacleLayer = 1;

        public LightArrowSpell()
        {
            ManaCost = 45f;
            Cooldown = 14f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as ArcherKit : null;
            Vector3 from = kit != null ? kit.MuzzlePosition : ctx.Origin + Vector3.up * 1.2f;
            Vector3 dir = ctx.AimDirection;

            // Wände stoppen den Strahl
            float length = Length;
            foreach (var h in Physics.RaycastAll(from, dir, Length, ObstacleLayer, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<EnemyBrain>() != null || h.collider.GetComponentInParent<PlayerStats>() != null
                    || h.collider.GetComponentInParent<ElementalBuddy>() != null) continue;
                length = Mathf.Min(length, Mathf.Max(1f, h.distance));
            }

            foreach (var enemy in CombatUtil.FindEnemiesOnLine(ctx.Origin, dir, length, Width * 0.5f))
            {
                if (enemy == null) continue;
                if (HitFxPrefab != null) CombatUtil.SpawnFx(HitFxPrefab, enemy.transform.position + Vector3.up, Quaternion.LookRotation(-dir), 1.5f);
                if (BlindDuration > 0f)
                {
                    enemy.ApplySlow(BlindSlow, BlindDuration);
                    BlindEffect.Apply(enemy.gameObject, BlindDuration, BlindVfxPrefab);
                }
                enemy.TakeDamage(Damage * ctx.DamageMultiplier);
            }

            BeamFx.Spawn(BeamPrefab, from, from + dir * length);
            SpawnEffect(from, Quaternion.LookRotation(dir), 1f);
            GameAudio.Play(SfxId.HolyCircle, from);
        }
    }
}
