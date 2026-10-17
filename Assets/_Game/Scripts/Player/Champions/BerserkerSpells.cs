using UnityEngine;

namespace ElementalBuddies
{
    // Element-Fähigkeiten des Berserkers (R/F/C/V). Werte im Inspector des BerserkerKit. Element-Fähigkeiten bauen keine
    // Wut auf (sonst Endlosschleife), halten sie aber durch Treffer am Leben (Verfall startet später).

    // Glutsturm (R): kanalisierter Wirbel, Ticks im Radius mit Schaden + Brand
    [System.Serializable]
    public class EmberStormSpell : ElementSpell
    {
        [Header("Glutsturm")]
        public float Duration = 2.5f;
        public float TickInterval = 0.5f;
        public float Radius = 3f;
        public float TickDamage = 14f;
        public float BurnDps = 6f;
        public float BurnDuration = 2f;
        [Range(0.1f, 1f)] public float MoveMultiplier = 0.7f;
        [Tooltip("Brand-VFX am Gegner.")]
        public GameObject BurnVfxPrefab;
        [Tooltip("Feuer-Tornado als Schleife am Spieler (für die Dauer des Wirbels).")]
        public GameObject LoopFxPrefab;
        [Tooltip("SlashArcFx-Prefab für den Feuerbogen je Tick.")]
        public GameObject ArcFxPrefab;
        [ColorUsage(true, true)] public Color ArcColor = new Color(2f, 0.5f, 0.12f, 1f);

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            Duration = m.Apply(id, AbilityStat.Duration, Duration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return Stat(TickDamage * dm, "", "Schaden pro Treffer", out value, out unit, out label);
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(Duration, " s", "Wirbeldauer", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public EmberStormSpell()
        {
            ManaCost = 35f;
            Cooldown = 9f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as BerserkerKit : null;
            float dm = ctx.DamageMultiplier;
            Vector3 aim = ctx.AimDirection;
            if (kit == null) { Tick(null, ctx.Origin, aim, dm); return; }
            GameObject loop = LoopFxPrefab;
            kit.BeginSpin(Duration, TickInterval, MoveMultiplier, null, center => Tick(kit, center, aim, dm));
            if (loop != null)
            {
                // Schleife am Spieler, mit dem Radius skaliert
                var fx = SpawnEffect(kit.transform.position, Quaternion.identity, Radius);
                if (fx != null)
                {
                    fx.transform.SetParent(kit.transform, true);
                    Object.Destroy(fx, Duration + 0.6f);
                    kit.StartCoroutine(StopAfter(fx, Duration));
                }
            }
        }

        private static System.Collections.IEnumerator StopAfter(GameObject fx, float t)
        {
            yield return new WaitForSeconds(t);
            if (fx == null) yield break;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // Ein Tick um center: Optik überall, Treffer nur auf dem Server
        public void Tick(BerserkerKit kit, Vector3 center, Vector3 aim, float dm)
        {
            var arc = SlashArcFx.Spawn(ArcFxPrefab, center + Vector3.up * 0.9f, aim, Radius, 360f, ArcColor, true);
            GameAudio.Play(SfxId.EmberSpin, center); // eine Umdrehung je Tick
            if (arc != null) { arc.SweepTime = 0.2f; arc.FadeTime = 0.25f; arc.TailLength = 0.5f; arc.Width = 1.2f; }
            if (!Net.IsServer) return;
            int hits = 0;
            foreach (var enemy in FindEnemies(center, Radius))
            {
                if (enemy == null) continue;
                if (BurnDps > 0f && BurnDuration > 0f) BurnEffect.Apply(enemy.gameObject, BurnDps * dm, BurnDuration, BurnVfxPrefab, fromChampion: true);
                if (kit != null) kit.DealDamage(enemy, TickDamage * dm);
                else EnemyBrain.DealPlayerDamage(enemy, TickDamage * dm);
                hits++;
            }
            if (hits > 0 && kit != null) kit.ServerGrantRage(0f);
        }
    }

    // Eisbrecher (F): Eisstachel-Linie nach vorne, Einfrieren; Zerschmettern gegen eingefrorene/verlangsamte Gegner
    [System.Serializable]
    public class IceBreakerSpell : ElementSpell
    {
        [Header("Eisbrecher")]
        public float Length = 7f;
        public float Width = 2f;
        public float Damage = 35f;
        public float FreezeDuration = 1.5f;
        [Tooltip("Schadens-Faktor gegen schon eingefrorene oder verlangsamte Gegner.")]
        public float ShatterMultiplier = 1.5f;
        [Tooltip("Zeit vom Cast bis zum Aufschlag der Axt (Clip Berserker_GroundSlam: Treffer-Frame 13 bei 30 fps ≈ 0,43 s).")]
        public float HitDelay = 0.43f;
        public GameObject FrozenVfxPrefab;
        [Tooltip("Splitter am zerschmetterten Gegner (optional).")]
        public GameObject ShatterFxPrefab;

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Length = m.Apply(id, AbilityStat.Range, Length);
            FreezeDuration = m.Apply(id, AbilityStat.Duration, FreezeDuration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return Stat(Damage * dm, "", "Schaden", out value, out unit, out label);
                case AbilityStat.Range: return Stat(Length, " m", "Länge", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(FreezeDuration, " s", "Einfrierdauer", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public IceBreakerSpell()
        {
            ManaCost = 30f;
            Cooldown = 9f;
        }

        // Schaden mit Zerschmettern (statisch, testbar)
        public static float DamageFor(float damage, bool shatter, float shatterMultiplier) => shatter ? damage * shatterMultiplier : damage;

        public override void Cast(SpellCastContext ctx)
        {
            // Eislinie erst beim Aufschlag der Axt (Animation), nicht schon beim Ausholen
            var caster = ctx.Caster != null ? ctx.Caster.ActiveKit as BerserkerKit : null;
            if (HitDelay > 0f && caster != null && caster.isActiveAndEnabled) caster.StartCoroutine(HitAfter(ctx, caster.transform));
            else Hit(ctx);
        }

        private System.Collections.IEnumerator HitAfter(SpellCastContext ctx, Transform caster)
        {
            Vector3 offset = ctx.Origin - caster.position;
            yield return new WaitForSeconds(HitDelay);
            // Linie startet dort, wo die Figur beim Aufschlag steht (sie kann während des Ausholens weiterlaufen)
            if (caster != null) ctx.Origin = caster.position + offset;
            Hit(ctx);
        }

        private void Hit(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.LookRotation(ctx.AimDirection), Length);
            GameAudio.Play(SfxId.IceBreak, ctx.Origin);
            if (!Net.IsServer) return; // Treffer/Einfrieren nur auf dem Server
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as BerserkerKit : null;
            int hits = 0;
            bool anyShatter = false;
            foreach (var enemy in CombatUtil.FindEnemiesOnLine(ctx.Origin, ctx.AimDirection, Length, Width * 0.5f))
            {
                if (enemy == null) continue;
                bool shatter = enemy.IsFrozen || enemy.IsSlowed;
                float dmg = DamageFor(Damage * ctx.DamageMultiplier, shatter, ShatterMultiplier);
                if (shatter)
                {
                    anyShatter = true;
                    if (ShatterFxPrefab != null) CombatUtil.SpawnFx(ShatterFxPrefab, enemy.transform.position + Vector3.up, Quaternion.identity, 2f);
                }
                if (FreezeDuration > 0f) using (EnemyBrain.PlayerSource()) enemy.Freeze(FreezeDuration, FrozenVfxPrefab);
                if (kit != null) kit.DealDamage(enemy, dmg);
                else EnemyBrain.DealPlayerDamage(enemy, dmg);
                hits++;
            }
            if (anyShatter) GameAudio.Play(SfxId.FrostShatter, ctx.Origin);
            if (hits > 0 && kit != null) kit.ServerGrantRage(0f);
        }
    }

    // Bergsturz (C): Weitsprung zum Mauspunkt (Warnring am Ziel), Landung: Schaden, Betäubung, Rückstoß
    [System.Serializable]
    public class LandslideSpell : ElementSpell
    {
        [Header("Bergsturz")]
        public float LeapDistance = 8f;
        public float LeapDuration = 0.5f;
        public float LeapHeight = 2f;
        public float Radius = 3.5f;
        public float Damage = 55f;
        public float StunDuration = 1f;
        public float Knockback = 2f;
        public GameObject StunVfxPrefab;
        [Tooltip("Warnring am Landepunkt (lebt LeapDuration).")]
        public GameObject WarnFxPrefab;

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            StunDuration = m.Apply(id, AbilityStat.Duration, StunDuration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return Stat(Damage * dm, "", "Schaden", out value, out unit, out label);
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(StunDuration, " s", "Betäubung", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public LandslideSpell()
        {
            ManaCost = 40f;
            Cooldown = 12f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as BerserkerKit : null;
            float dist = LeapDistance * (ctx.Caster != null ? ctx.Caster.MobilityMultiplier : 1f);
            if (ctx.HasAimPoint)
            {
                Vector3 to = ctx.AimPoint - ctx.Origin;
                to.y = 0f;
                dist = Mathf.Min(dist, to.magnitude);
            }
            Vector3 landing = ctx.Origin + ctx.AimDirection * dist;
            if (ctx.Caster != null) landing.y = ctx.Caster.GetGroundHeight(landing, landing.y);
            if (WarnFxPrefab != null)
            {
                var warn = CombatUtil.SpawnFx(WarnFxPrefab, landing + Vector3.up * 0.05f, Quaternion.identity, LeapDuration + 0.1f);
                if (warn != null) CombatUtil.ScaleFx(warn, WarnFxPrefab.transform.localScale, Radius / 3.5f, false);
            }
            if (kit != null) kit.Leap(ctx.AimDirection * dist, LeapDuration, LeapHeight, () =>
            {
                // Abbild: Position hinkt per NetworkTransform nach → vorhergesagten Landepunkt nehmen
                Vector3 c = ctx.IsRemote ? landing : kit.transform.position;
                SlamAt(ctx, kit, c);
            });
            else SlamAt(ctx, null, ctx.Origin);
        }

        private void SlamAt(SpellCastContext ctx, BerserkerKit kit, Vector3 center)
        {
            if (ctx.Caster != null) center.y = ctx.Caster.GetGroundHeight(center, center.y);
            SpawnEffect(center, Quaternion.LookRotation(ctx.AimDirection), Radius);
            GameAudio.Play(SfxId.LeapLand, center);
            if (!Net.IsServer) return;
            int hits = 0;
            foreach (var enemy in FindEnemies(center, Radius))
            {
                if (enemy == null) continue;
                if (StunDuration > 0f) using (EnemyBrain.PlayerSource()) enemy.Stun(StunDuration, StunVfxPrefab);
                if (Knockback > 0f) using (EnemyBrain.PlayerSource()) enemy.Knockback(CombatUtil.FlatDirection(center, enemy.transform.position, ctx.AimDirection), Knockback, 0.3f);
                if (kit != null) kit.DealDamage(enemy, Damage * ctx.DamageMultiplier);
                else EnemyBrain.DealPlayerDamage(enemy, Damage * ctx.DamageMultiplier);
                hits++;
            }
            if (hits > 0 && kit != null) kit.ServerGrantRage(0f);
        }
    }

    // Lichtrausch (V): Kampfschrei – Lebensraub, schnellere Hiebe, Buddy-Schadensbuff, Gegner geblendet
    [System.Serializable]
    public class LightFrenzySpell : ElementSpell
    {
        [Header("Lichtrausch")]
        public float Duration = 8f;
        [Range(0f, 1f)] public float Lifesteal = 0.15f;
        [Tooltip("Höchstens so viele LP heilt der Lebensraub pro Einsatz.")]
        public float LifestealCap = 60f;
        [Tooltip("Axthieb-Tempo +x (0.25 = +25 %).")]
        public float AttackSpeedBonus = 0.25f;
        public float BuddyRadius = 8f;
        public float BuddyDamageBonus = 0.15f;
        public float BlindRadius = 5f;
        public float BlindDuration = 1.5f;
        [Tooltip("EnemyBrain.ApplySlow: Tempo = Basis × (1 − Wert).")]
        [Range(0f, 1f)] public float BlindSlow = 0.5f;
        public GameObject BlindVfxPrefab;
        [Tooltip("Gold-rote Körper-Aura für die Dauer (am Spieler).")]
        public GameObject AuraPrefab;
        [Tooltip("Kurzer Glanz am gestärkten Buddy (optional).")]
        public GameObject BuddyBuffFxPrefab;

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Duration = m.Apply(id, AbilityStat.Duration, Duration);
            Lifesteal = m.Apply(id, AbilityStat.Heal, Lifesteal);
            LifestealCap = m.Apply(id, AbilityStat.Heal, LifestealCap);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Duration: return Stat(Duration, " s", "Dauer", out value, out unit, out label);
                case AbilityStat.Heal: return Stat(Lifesteal * 100f, " %", "Lebensraub", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public LightFrenzySpell()
        {
            ManaCost = 50f;
            Cooldown = 18f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.identity, BlindRadius);
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as BerserkerKit : null;
            if (kit != null) kit.BeginFrenzy(Duration, Lifesteal, LifestealCap, AttackSpeedBonus, AuraPrefab);

            // Buddy-Buff auf allen Rechnern (zählt auf dem Server, Anzeige überall gleich)
            var buddies = ElementalBuddy.Active;
            for (int i = buddies.Count - 1; i >= 0; i--)
            {
                var b = buddies[i];
                if (b == null || HorizontalDistance(b.transform.position, ctx.Origin) > BuddyRadius) continue;
                b.AddTimedDamageBuff(BuddyDamageBonus, Duration);
                if (BuddyBuffFxPrefab != null) CombatUtil.SpawnFx(BuddyBuffFxPrefab, b.transform.position, Quaternion.identity, 2f);
            }

            // Blenden: Verlangsamung entscheidet der Server, die Sterne sind Optik
            if (BlindDuration > 0f)
                foreach (var enemy in FindEnemies(ctx.Origin, BlindRadius))
                {
                    if (enemy == null) continue;
                    if (Net.IsServer) using (EnemyBrain.PlayerSource()) enemy.ApplySlow(BlindSlow, BlindDuration);
                    BlindEffect.Apply(enemy.gameObject, BlindDuration, BlindVfxPrefab);
                }
            GameAudio.Play(SfxId.WarRoar, ctx.Origin);
        }
    }
}
