using UnityEngine;

namespace ElementalBuddies
{
    // Element-Fähigkeiten des Schwertkämpfers (R/F/C/V). Werte im Inspector des KnightKit.

    // Flammenwirbel (R): Drehschlag um dich herum, Schaden + Brand
    [System.Serializable]
    public class FlameWhirlSpell : ElementSpell
    {
        [Header("Flammenwirbel")]
        public float Radius = 3.5f;
        public float Damage = 45f;
        public float BurnDps = 10f;
        public float BurnDuration = 3f;
        public float Knockback = 1f;
        [Tooltip("Brand-VFX am Gegner (wie Flammenwelle).")]
        public GameObject BurnVfxPrefab;
        [Tooltip("SlashArcFx-Prefab für den 360°-Feuerbogen.")]
        public GameObject ArcFxPrefab;
        [ColorUsage(true, true)] public Color ArcColor = new Color(1.6f, 0.65f, 0.15f, 1f);

        public FlameWhirlSpell()
        {
            ManaCost = 35f;
            Cooldown = 7f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.LookRotation(ctx.AimDirection), Radius);
            var arc = SlashArcFx.Spawn(ArcFxPrefab, ctx.Origin + Vector3.up * 0.9f, ctx.AimDirection, Radius, 360f, ArcColor, true);
            if (arc != null) { arc.SweepTime = 0.22f; arc.FadeTime = 0.3f; arc.TailLength = 0.6f; arc.Width = 1.3f; }

            foreach (var enemy in FindEnemies(ctx.Origin, Radius))
            {
                if (enemy == null) continue;
                if (BurnDps > 0f && BurnDuration > 0f)
                    BurnEffect.Apply(enemy.gameObject, BurnDps * ctx.DamageMultiplier, BurnDuration, BurnVfxPrefab);
                if (Knockback > 0f)
                    enemy.Knockback(CombatUtil.FlatDirection(ctx.Origin, enemy.transform.position, ctx.AimDirection), Knockback, 0.2f);
                enemy.TakeDamage(Damage * ctx.DamageMultiplier);
            }
            GameAudio.Play(SfxId.FireWave, ctx.Origin);
        }
    }

    // Frostschlag (F): Schockwelle im Kegel nach vorne, Schaden + Einfrieren
    [System.Serializable]
    public class FrostStrikeSpell : ElementSpell
    {
        [Header("Frostschlag")]
        public float Range = 7f;
        [Tooltip("Volle Kegelöffnung in Grad.")]
        public float ConeAngle = 70f;
        public float Damage = 30f;
        public float FreezeDuration = 2.5f;
        [Tooltip("Eiskristalle am eingefrorenen Gegner (wie Frostnova).")]
        public GameObject FrozenVfxPrefab;

        public FrostStrikeSpell()
        {
            ManaCost = 40f;
            Cooldown = 10f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.LookRotation(ctx.AimDirection), Range);
            foreach (var enemy in CombatUtil.FindEnemiesInCone(ctx.Origin, ctx.AimDirection, Range, ConeAngle))
            {
                if (enemy == null) continue;
                if (FreezeDuration > 0f) enemy.Freeze(FreezeDuration, FrozenVfxPrefab);
                enemy.TakeDamage(Damage * ctx.DamageMultiplier);
            }
            GameAudio.Play(SfxId.FrostNova, ctx.Origin);
        }
    }

    // Erdbeben (C): kurzer Sprung in Blickrichtung, beim Aufsetzen Stampfer: Schaden, Betäubung, Rückstoß
    [System.Serializable]
    public class EarthquakeSpell : ElementSpell
    {
        [Header("Erdbeben")]
        [Tooltip("Maximale Sprungweite (Richtung Mauszeiger, liegt er näher, landet man dort).")]
        public float LeapDistance = 3.5f;
        public float LeapDuration = 0.35f;
        public float LeapHeight = 1.2f;
        public float Radius = 4f;
        public float Damage = 50f;
        public float StunDuration = 1.5f;
        public float Knockback = 2.5f;
        [Tooltip("Sterne über dem Kopf betäubter Gegner (VFX_Blinded).")]
        public GameObject StunVfxPrefab;

        public EarthquakeSpell()
        {
            ManaCost = 35f;
            Cooldown = 11f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as KnightKit : null;
            float dist = LeapDistance;
            if (ctx.HasAimPoint)
            {
                Vector3 to = ctx.AimPoint - ctx.Origin;
                to.y = 0f;
                dist = Mathf.Min(LeapDistance, to.magnitude);
            }
            if (kit != null) kit.Leap(ctx.AimDirection * dist, LeapDuration, LeapHeight, () => Slam(ctx));
            else Slam(ctx);
        }

        // Aufprall (öffentlich für Tests)
        public void Slam(SpellCastContext ctx)
        {
            Vector3 center = ctx.Caster != null ? ctx.Caster.transform.position : ctx.Origin;
            if (ctx.Caster != null) center.y = ctx.Caster.GetGroundHeight(center, center.y);
            SpawnEffect(center, Quaternion.LookRotation(ctx.AimDirection), Radius);

            foreach (var enemy in FindEnemies(center, Radius))
            {
                if (enemy == null) continue;
                if (StunDuration > 0f) enemy.Stun(StunDuration, StunVfxPrefab);
                if (Knockback > 0f)
                    enemy.Knockback(CombatUtil.FlatDirection(center, enemy.transform.position, ctx.AimDirection), Knockback, 0.3f);
                enemy.TakeDamage(Damage * ctx.DamageMultiplier);
            }
            GameAudio.Play(SfxId.StoneWall, center);
        }
    }

    // Lichtschwur (V): Schild leuchtet, verspottet Gegner, heilt dich + Buddies, kurz weniger Schaden
    [System.Serializable]
    public class LightOathSpell : ElementSpell
    {
        [Header("Lichtschwur")]
        public float Radius = 7f;
        public float TauntDuration = 4f;
        public float PlayerHeal = 30f;
        public float BuddyHeal = 25f;
        [Tooltip("Schadensreduktion des Schwertkämpfers, solange der Spott läuft.")]
        [Range(0f, 0.9f)] public float DamageReduction = 0.3f;
        [Tooltip("Leuchten am Spieler für die Dauer des Schwurs (optional).")]
        public GameObject AuraPrefab;

        public LightOathSpell()
        {
            ManaCost = 45f;
            Cooldown = 16f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.identity, Radius);
            Transform caster = ctx.Caster != null ? ctx.Caster.transform : null;

            if (caster != null && PlayerHeal > 0f)
            {
                var stats = caster.GetComponent<PlayerStats>();
                if (stats != null) stats.Heal(PlayerHeal);
            }

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

            if (caster != null && TauntDuration > 0f)
            {
                foreach (var enemy in FindEnemies(ctx.Origin, Radius))
                    if (enemy != null) enemy.Taunt(caster, TauntDuration);
            }

            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as KnightKit : null;
            if (kit != null) kit.BeginOath(TauntDuration, DamageReduction, AuraPrefab);
            GameAudio.Play(SfxId.HolyCircle, ctx.Origin);
        }
    }
}
