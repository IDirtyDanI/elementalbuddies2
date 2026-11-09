using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Element-Fähigkeiten des Schamanen (R/F/C/V). Werte im Inspector des ShamanKit. Cast läuft auf allen Rechnern:
    // Totems/Geist/Optik überall, Schaden, Heilung, Schild und Spieler-Buffs nur mit Net.IsServer (Spieler-Buffs gehen
    // über PlayerAbilities.ServerGrantBuff an den Besitzer). Buddy-Feuerrate läuft wie der Lichtrausch überall mit.

    // Kriegstotem (R): Totem am Mauspunkt, Buddies +Feuerrate, Spieler +Angriffstempo (Grundangriff). Nur eines pro Schamane.
    [System.Serializable]
    public class WarTotemSpell : ElementSpell
    {
        [Header("Kriegstotem")]
        public float MaxCastRange = 10f;
        public float Duration = 10f;
        public float Radius = 7f;
        [Tooltip("Buddies: Feuerrate +x (0.25 = +25 %).")]
        public float BuddyFireRateBonus = 0.25f;
        [Tooltip("Spieler: Angriffstempo des Grundangriffs +x (0.25 = +25 %).")]
        public float PlayerAttackSpeedBonus = 0.25f;
        [Tooltip("Abstand der Auffrischungen (wer den Radius verlässt, verliert den Buff nach BuffLinger s).")]
        public float PulseInterval = 0.5f;
        public float BuffLinger = 1f;
        [Tooltip("Trommelschlag (SfxId.WarDrum) alle x s.")]
        public float DrumInterval = 2f;
        [Tooltip("Totem-Modell (Totem_War, optional mit ShamanTotem). Leer = Platzhalter.")]
        public GameObject TotemPrefab;
        [Tooltip("Roter Bodenring mit Trommelpuls (VFX_WarTotemAura), Schleife für die Lebensdauer.")]
        public GameObject AuraFxPrefab;
        [Tooltip("Radius (m), für den die Aura gebaut ist.")]
        public float AuraAuthoredSize = 7f;
        [Tooltip("Rot-oranges Glimmen an gestärkten Buddies/Spielern (VFX_WarBuff).")]
        public GameObject BuffFxPrefab;
        [Tooltip("Zeit vom Cast bis zum Aufschlag des Stabs (Clip Shaman_PlantTotem: 0,33 s) – dann erscheint das Totem.")]
        public float PlantDelay = 0.33f;

        public WarTotemSpell()
        {
            ManaCost = 40f;
            Cooldown = 16f;
        }

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            BuddyFireRateBonus = m.Apply(id, AbilityStat.Speed, BuddyFireRateBonus);
            PlayerAttackSpeedBonus = m.Apply(id, AbilityStat.Speed, PlayerAttackSpeedBonus);
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            Duration = m.Apply(id, AbilityStat.Duration, Duration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Speed: return Stat(BuddyFireRateBonus * 100f, " %", "Buff-Stärke", out value, out unit, out label);
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(Duration, " s", "Dauer", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as ShamanKit : null;
            Vector3 target = ShamanKit.TotemPoint(ctx, MaxCastRange);
            ShamanKit.After(kit, PlantDelay, () => Plant(ctx, kit, target));
        }

        private void Plant(SpellCastContext ctx, ShamanKit kit, Vector3 target)
        {
            SpawnEffect(target, Quaternion.identity, Radius);
            GameAudio.PlayOr(SfxId.TotemPlant, target);
            int pulsesPerDrum = Mathf.Max(1, Mathf.RoundToInt(DrumInterval / Mathf.Max(0.1f, PulseInterval)));
            var totem = ShamanTotem.Spawn(TotemPrefab, AuraFxPrefab, AuraAuthoredSize, target, Quaternion.LookRotation(ctx.AimDirection),
                ShamanTotem.TotemKind.War, kit, Radius, Duration, PulseInterval, t =>
                {
                    if (t.PulseCount > 1 && (t.PulseCount - 1) % pulsesPerDrum == 0) GameAudio.PlayOr(SfxId.WarDrum, t.transform.position);
                    Pulse(t, ctx.Caster);
                });
            if (kit != null) kit.SetTotem(totem);
        }

        private static readonly List<PlayerAbilities> _players = new List<PlayerAbilities>();

        private void Pulse(ShamanTotem t, PlayerAbilities caster)
        {
            Vector3 c = t.transform.position;
            float linger = PulseInterval + BuffLinger;
            // Buddies: Feuerrate auf allen Rechnern (Anzeige gleich, geschossen wird ohnehin auf dem Server)
            var buddies = ElementalBuddy.Active;
            for (int i = buddies.Count - 1; i >= 0; i--)
            {
                var b = buddies[i];
                if (b == null || b.IsDead || !t.InRange(b.transform.position)) continue;
                b.AddTimedFireRateBuff(BuddyFireRateBonus, linger, ElementalBuddy.FireRateSourceTotem);
                t.ShowTargetFx(b, BuffFxPrefab, 1.6f, 1.5f);
            }
            // Spieler (inkl. Schamane): Angriffstempo – entscheidet der Server, Glimmen überall
            ShamanKit.CollectPlayers(c, Radius, caster, _players);
            foreach (var p in _players)
            {
                if (p == null) continue;
                if (Net.IsServer) p.ServerGrantBuff(SupportBuffKind.AttackSpeed, PlayerAttackSpeedBonus, linger);
                t.ShowTargetFx(p, BuffFxPrefab, 1.6f, 1.5f);
            }
        }
    }

    // Heilende Woge (F): Wasserwelle im Kegel – Gegner Schaden + Verlangsamung, Verbündete im Kegel und du selbst Heilung
    [System.Serializable]
    public class HealingTideSpell : ElementSpell
    {
        [Header("Heilende Woge")]
        public float ConeAngle = 75f;
        public float Range = 8f;
        public float Damage = 30f;
        [Tooltip("EnemyBrain.ApplySlow: Tempo = Basis × (1 − Wert).")]
        [Range(0f, 1f)] public float Slow = 0.4f;
        public float SlowDuration = 3f;
        [Tooltip("Heilung für Spieler und Buddies im Kegel und den Schamanen selbst.")]
        public float Heal = 30f;
        [Tooltip("Funkeln an geheilten Zielen (vorhandenes HealSparkle).")]
        public GameObject HealFxPrefab;
        [Tooltip("Zeit vom Cast bis zur Woge (Clip Shaman_Wave: Schwung bei 0,23 s).")]
        public float WaveDelay = 0.23f;

        public HealingTideSpell()
        {
            ManaCost = 35f;
            Cooldown = 9f;
        }

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            Heal = m.Apply(id, AbilityStat.Heal, Heal);
            Range = m.Apply(id, AbilityStat.Range, Range);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Heal: return Stat(Heal, " LP", "Heilung", out value, out unit, out label);
                case AbilityStat.Damage: return Stat(Damage * dm, "", "Schaden", out value, out unit, out label);
                case AbilityStat.Range: return Stat(Range, " m", "Reichweite", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        private static readonly List<PlayerAbilities> _players = new List<PlayerAbilities>();

        // Liegt die Position im waagerechten Kegel (volle Öffnung angle) vor origin? Ziele direkt am Ursprung zählen.
        public static bool InCone(Vector3 origin, Vector3 dir, float range, float angle, Vector3 position)
        {
            Vector3 d = position - origin;
            d.y = 0f;
            if (d.magnitude > range) return false;
            if (d.sqrMagnitude < 0.25f) return true;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(dir, d) <= angle * 0.5f;
        }

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as ShamanKit : null;
            if (WaveDelay <= 0f || kit == null || !kit.isActiveAndEnabled) { Wave(ctx); return; }
            // Woge startet dort, wo die Figur beim Schwung steht (sie kann während des Ausholens weiterlaufen)
            Vector3 offset = ctx.Origin - kit.transform.position;
            ShamanKit.After(kit, WaveDelay, () =>
            {
                if (kit != null) ctx.Origin = kit.transform.position + offset;
                Wave(ctx);
            });
        }

        private void Wave(SpellCastContext ctx)
        {
            Vector3 dir = ctx.AimDirection;
            SpawnEffect(ctx.Origin, Quaternion.LookRotation(dir), Range);
            GameAudio.PlayOr(SfxId.TideWave, ctx.Origin);
            bool server = Net.IsServer;

            // Gegner: Schaden + Verlangsamung (Server)
            if (server)
                foreach (var enemy in CombatUtil.FindEnemiesInCone(ctx.Origin, dir, Range, ConeAngle))
                {
                    if (enemy == null || enemy.IsDead) continue;
                    if (Slow > 0f && SlowDuration > 0f) using (EnemyBrain.PlayerSource()) enemy.ApplySlow(Slow, SlowDuration);
                    EnemyBrain.DealPlayerDamage(enemy, Damage * ctx.DamageMultiplier, AbilityId.HealingTide);
                }

            // Spieler im Kegel und der Schamane selbst
            ShamanKit.CollectPlayers(ctx.Origin, Range, ctx.Caster, _players);
            foreach (var p in _players)
            {
                if (p == null) continue;
                bool self = p == ctx.Caster;
                if (!self && !InCone(ctx.Origin, dir, Range, ConeAngle, p.transform.position)) continue;
                var stats = p.GetComponent<PlayerStats>();
                if (stats == null || stats.IsDead) continue;
                bool hurt = stats.CurrentHP < stats.MaxHP;
                if (server) stats.Heal(Heal);
                if (hurt) Sparkle(stats.transform.position);
            }

            // Buddies im Kegel
            var buddies = ElementalBuddy.Active;
            for (int i = buddies.Count - 1; i >= 0; i--)
            {
                var b = buddies[i];
                if (b == null || b.IsDead || !InCone(ctx.Origin, dir, Range, ConeAngle, b.transform.position)) continue;
                bool hurt = b.CurrentHP < b.MaxHP;
                if (server) b.Heal(Heal);
                if (hurt) Sparkle(b.transform.position);
            }
        }

        private void Sparkle(Vector3 position)
        {
            if (HealFxPrefab != null) CombatUtil.SpawnFx(HealFxPrefab, position + Vector3.up * 0.8f, Quaternion.identity, 2f);
        }
    }

    // Steinhaut-Totem (C): Totem am Mauspunkt. Beim Aufstellen Schaden + Rückstoß in der Nähe; alle PulseInterval s
    // Buddies Schild, Spieler Schadensreduktion (solange sie im Radius stehen).
    [System.Serializable]
    public class StoneskinTotemSpell : ElementSpell
    {
        [Header("Steinhaut-Totem")]
        public float MaxCastRange = 10f;
        public float Duration = 8f;
        public float Radius = 6f;
        [Header("Aufstellen")]
        public float PlantRadius = 2.5f;
        public float PlantDamage = 20f;
        public float PlantKnockback = 2f;
        [Header("Schutz")]
        public float PulseInterval = 2f;
        public float BuddyShield = 30f;
        public float ShieldDuration = 4f;
        [Tooltip("Spieler: erlittener Schaden −x (0.3 = −30 %), solange im Radius.")]
        [Range(0f, 0.75f)] public float PlayerDamageReduction = 0.3f;
        [Tooltip("Totem-Modell (Totem_Stone, optional mit ShamanTotem). Leer = Platzhalter.")]
        public GameObject TotemPrefab;
        [Tooltip("Grüner Runenring (VFX_StoneskinAura), Schleife für die Lebensdauer.")]
        public GameObject AuraFxPrefab;
        [Tooltip("Radius (m), für den die Aura gebaut ist.")]
        public float AuraAuthoredSize = 6f;
        [Tooltip("Steinschimmer am geschützten Ziel (VFX_StoneskinShield).")]
        public GameObject ShieldFxPrefab;
        [Tooltip("Zeit vom Cast bis zum Aufschlag des Stabs (Clip Shaman_PlantTotem: 0,33 s) – dann erscheint das Totem.")]
        public float PlantDelay = 0.33f;

        public StoneskinTotemSpell()
        {
            ManaCost = 40f;
            Cooldown = 18f;
        }

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            BuddyShield = m.Apply(id, AbilityStat.Heal, BuddyShield);
            PlayerDamageReduction = Mathf.Min(SupportBuffRules.MaxDamageReduction, m.Apply(id, AbilityStat.Heal, PlayerDamageReduction));
            Radius = m.Apply(id, AbilityStat.Area, Radius);
            Duration = m.Apply(id, AbilityStat.Duration, Duration);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Heal: return Stat(BuddyShield, "", "Schild", out value, out unit, out label);
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(Duration, " s", "Dauer", out value, out unit, out label);
                case AbilityStat.Damage: return Stat(PlantDamage * dm, "", "Schaden", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        private static readonly List<PlayerAbilities> _players = new List<PlayerAbilities>();

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as ShamanKit : null;
            Vector3 target = ShamanKit.TotemPoint(ctx, MaxCastRange);
            ShamanKit.After(kit, PlantDelay, () => Plant(ctx, kit, target));
        }

        private void Plant(SpellCastContext ctx, ShamanKit kit, Vector3 target)
        {
            SpawnEffect(target, Quaternion.identity, PlantRadius);
            GameAudio.PlayOr(SfxId.TotemPlant, target);

            // Aufstellen: Schaden + Rückstoß (Server)
            if (Net.IsServer)
                foreach (var enemy in CombatUtil.FindEnemies(target, PlantRadius))
                {
                    if (enemy == null || enemy.IsDead) continue;
                    if (PlantKnockback > 0f)
                        using (EnemyBrain.PlayerSource()) enemy.Knockback(CombatUtil.FlatDirection(target, enemy.transform.position, ctx.AimDirection), PlantKnockback, 0.3f);
                    EnemyBrain.DealPlayerDamage(enemy, PlantDamage * ctx.DamageMultiplier, AbilityId.StoneskinTotem);
                }

            var totem = ShamanTotem.Spawn(TotemPrefab, AuraFxPrefab, AuraAuthoredSize, target, Quaternion.LookRotation(ctx.AimDirection),
                ShamanTotem.TotemKind.Stoneskin, kit, Radius, Duration, PulseInterval, t => Pulse(t, ctx.Caster));
            if (kit != null) kit.SetTotem(totem);
        }

        private void Pulse(ShamanTotem t, PlayerAbilities caster)
        {
            bool server = Net.IsServer;
            bool any = false;
            var buddies = ElementalBuddy.Active;
            for (int i = buddies.Count - 1; i >= 0; i--)
            {
                var b = buddies[i];
                if (b == null || b.IsDead || !t.InRange(b.transform.position)) continue;
                if (server) b.AddShield(BuddyShield, ShieldDuration);
                t.ShowTargetFx(b, ShieldFxPrefab, Mathf.Min(ShieldDuration, 2f), PulseInterval - 0.05f);
                any = true;
            }
            ShamanKit.CollectPlayers(t.transform.position, Radius, caster, _players);
            foreach (var p in _players)
            {
                if (p == null) continue;
                // etwas länger als ein Puls-Abstand: durchgehend geschützt, solange man im Radius bleibt
                if (server) p.ServerGrantBuff(SupportBuffKind.DamageReduction, PlayerDamageReduction, PulseInterval + 0.3f);
                t.ShowTargetFx(p, ShieldFxPrefab, Mathf.Min(PulseInterval, 2f), PulseInterval - 0.05f);
                any = true;
            }
            if (any) GameAudio.PlayOr(SfxId.StoneSkin, t.transform.position);
        }
    }

    // Ahnengeist (V): Geisterwolf begleitet den Schamanen, heilt Spieler/Buddies im Radius über Zeit, Nexus gedrosselt.
    // Beim Rufen: Gegner im Umkreis geblendet.
    [System.Serializable]
    public class AncestralSpiritSpell : ElementSpell
    {
        [Header("Ahnengeist")]
        public float Duration = 8f;
        public float Radius = 8f;
        public float HealPerSecond = 7f;
        public float TickInterval = 1f;
        [Tooltip("Nexus: Anteil der Max-LP je NexusInterval (0.01 = 1 %, wie der Licht-Buddy).")]
        [Range(0f, 0.1f)] public float NexusHealPercent = 0.01f;
        public float NexusInterval = 2f;
        [Header("Blenden beim Rufen")]
        public float BlindRadius = 5f;
        public float BlindDuration = 2f;
        [Tooltip("EnemyBrain.ApplySlow: Tempo = Basis × (1 − Wert).")]
        [Range(0f, 1f)] public float BlindSlow = 0.5f;
        public GameObject BlindVfxPrefab;
        [Header("Optik")]
        [Tooltip("Geisterwolf (Spirit_Wolf + VFX_AncestralSpirit, optional mit AncestralSpiritFollower). Leer = Platzhalter.")]
        public GameObject SpiritPrefab;
        [Tooltip("Funkeln an geheilten Zielen (vorhandenes HealSparkle).")]
        public GameObject HealFxPrefab;
        [Tooltip("Zeit vom Cast bis zum Ruf (Clip Shaman_Summon: Stab oben bei 0,40 s) – dann Blendung und Geist.")]
        public float CallDelay = 0.4f;

        public AncestralSpiritSpell()
        {
            ManaCost = 50f;
            Cooldown = 20f;
        }

        protected override void ApplyMods(AbilityMods m, AbilityId id)
        {
            HealPerSecond = m.Apply(id, AbilityStat.Heal, HealPerSecond);
            Duration = m.Apply(id, AbilityStat.Duration, Duration);
            Radius = m.Apply(id, AbilityStat.Area, Radius);
        }

        public override bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            switch (stat)
            {
                case AbilityStat.Heal: return Stat(HealPerSecond, " LP/s", "Heilung", out value, out unit, out label);
                case AbilityStat.Duration: return Stat(Duration, " s", "Dauer", out value, out unit, out label);
                case AbilityStat.Area: return Stat(Radius, " m", "Radius", out value, out unit, out label);
            }
            return base.TryGetStat(stat, dm, out value, out unit, out label);
        }

        public override void Cast(SpellCastContext ctx)
        {
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as ShamanKit : null;
            if (CallDelay <= 0f || kit == null || !kit.isActiveAndEnabled) { Call(ctx); return; }
            Vector3 offset = ctx.Origin - kit.transform.position;
            ShamanKit.After(kit, CallDelay, () =>
            {
                if (kit != null) ctx.Origin = kit.transform.position + offset;
                Call(ctx);
            });
        }

        private void Call(SpellCastContext ctx)
        {
            SpawnEffect(ctx.Origin, Quaternion.identity, BlindRadius);
            GameAudio.PlayOr(SfxId.AncestorHowl, ctx.Origin);
            if (BlindDuration > 0f)
                foreach (var enemy in CombatUtil.FindEnemies(ctx.Origin, BlindRadius))
                {
                    if (enemy == null || enemy.IsDead) continue;
                    if (Net.IsServer) using (EnemyBrain.PlayerSource()) enemy.ApplySlow(BlindSlow, BlindDuration);
                    BlindEffect.Apply(enemy.gameObject, BlindDuration, BlindVfxPrefab);
                }
            var kit = ctx.Caster != null ? ctx.Caster.ActiveKit as ShamanKit : null;
            if (kit != null) kit.SummonSpirit(this);
        }
    }
}
