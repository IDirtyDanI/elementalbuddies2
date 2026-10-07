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

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            BurnDuration = m.Apply(id, AbilityStat.Duration, BurnDuration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return Stat(Damage * dm, "", "Schaden", out value, out unit, out label);
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(BurnDuration, " s", "Brenndauer", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

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

            // Treffer, Brand und Rückstoß entscheidet nur der Server
            if (Net.IsServer)
            foreach (var enemy in FindEnemies(ctx.Origin, Radius))
            {
                if (enemy == null) continue;
                if (BurnDps > 0f && BurnDuration > 0f)
                    BurnEffect.Apply(enemy.gameObject, BurnDps * ctx.DamageMultiplier, BurnDuration, BurnVfxPrefab);
                if (Knockback > 0f)
                    enemy.Knockback(CombatUtil.FlatDirection(ctx.Origin, enemy.transform.position, ctx.AimDirection), Knockback, 0.2f);
                EnemyBrain.DealPlayerDamage(enemy, Damage * ctx.DamageMultiplier); // Quelle Spieler (Telemetrie)
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

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Range = m.Apply(id, AbilityStat.Range, Range);
            ConeAngle = Mathf.Min(360f, m.Apply(id, AbilityStat.Area, ConeAngle));
            FreezeDuration = m.Apply(id, AbilityStat.Duration, FreezeDuration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return Stat(Damage * dm, "", "Schaden", out value, out unit, out label);
                case AbilityStat.Range: return Stat(Range, " m", "Reichweite", out value, out unit, out label);
                case AbilityStat.Area: return Stat(ConeAngle, "°", "Kegel", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(FreezeDuration, " s", "Einfrierdauer", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public FrostStrikeSpell()
        {
            ManaCost = 40f;
            Cooldown = 10f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.LookRotation(ctx.AimDirection), Range);
            if (Net.IsServer) // Treffer/Einfrieren nur auf dem Server
            foreach (var enemy in CombatUtil.FindEnemiesInCone(ctx.Origin, ctx.AimDirection, Range, ConeAngle))
            {
                if (enemy == null) continue;
                if (FreezeDuration > 0f) enemy.Freeze(FreezeDuration, FrozenVfxPrefab);
                EnemyBrain.DealPlayerDamage(enemy, Damage * ctx.DamageMultiplier); // Quelle Spieler (Telemetrie)
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

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            StunDuration = m.Apply(id, AbilityStat.Duration, StunDuration);
            LeapDistance = m.Apply(id, AbilityStat.Range, LeapDistance);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return Stat(Damage * dm, "", "Schaden", out value, out unit, out label);
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(StunDuration, " s", "Betäubung", out value, out unit, out label);
                case AbilityStat.Range: return Stat(LeapDistance, " m", "Sprungweite", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

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
            // Abbild (fremder Rechner): Figur-Position hinkt per NetworkTransform nach → Landepunkt vorhersagen
            Vector3 landing = ctx.Origin + ctx.AimDirection * dist;
            if (kit != null) kit.Leap(ctx.AimDirection * dist, LeapDuration, LeapHeight, () => { if (ctx.IsRemote) SlamAt(ctx, landing); else Slam(ctx); });
            else Slam(ctx);
        }

        // Aufprall (öffentlich für Tests)
        public void Slam(SpellCastContext ctx)
        {
            Vector3 center = ctx.Caster != null ? ctx.Caster.transform.position : ctx.Origin;
            SlamAt(ctx, center);
        }

        private void SlamAt(SpellCastContext ctx, Vector3 center)
        {
            if (ctx.Caster != null) center.y = ctx.Caster.GetGroundHeight(center, center.y);
            SpawnEffect(center, Quaternion.LookRotation(ctx.AimDirection), Radius);

            if (Net.IsServer) // Betäubung, Rückstoß, Schaden nur auf dem Server
            foreach (var enemy in FindEnemies(center, Radius))
            {
                if (enemy == null) continue;
                if (StunDuration > 0f) enemy.Stun(StunDuration, StunVfxPrefab);
                if (Knockback > 0f)
                    enemy.Knockback(CombatUtil.FlatDirection(center, enemy.transform.position, ctx.AimDirection), Knockback, 0.3f);
                EnemyBrain.DealPlayerDamage(enemy, Damage * ctx.DamageMultiplier); // Quelle Spieler (Telemetrie)
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

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            TauntDuration = m.Apply(id, AbilityStat.Duration, TauntDuration);
            PlayerHeal = m.Apply(id, AbilityStat.Heal, PlayerHeal);
            BuddyHeal = m.Apply(id, AbilityStat.Heal, BuddyHeal);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(TauntDuration, " s", "Spottdauer", out value, out unit, out label);
                case AbilityStat.Heal: return Stat(PlayerHeal, " LP", "Heilung", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public LightOathSpell()
        {
            ManaCost = 45f;
            Cooldown = 16f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.identity, Radius);
            Transform caster = ctx.Caster != null ? ctx.Caster.transform : null;

            // Heilung und Spott entscheidet der Server; Schwur-Aura/Schadensreduktion laufen überall (Reduktion wirkt auf dem Server)
            if (Net.IsServer && caster != null && PlayerHeal > 0f)
            {
                var stats = caster.GetComponent<PlayerStats>();
                if (stats != null) stats.Heal(PlayerHeal);
            }

            if (Net.IsServer && BuddyHeal > 0f)
            {
                var buddies = ElementalBuddy.Active;
                for (int i = buddies.Count - 1; i >= 0; i--)
                {
                    var b = buddies[i];
                    if (b == null) continue;
                    if (HorizontalDistance(b.transform.position, ctx.Origin) <= Radius) b.Heal(BuddyHeal);
                }
            }

            if (Net.IsServer && caster != null && TauntDuration > 0f)
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
