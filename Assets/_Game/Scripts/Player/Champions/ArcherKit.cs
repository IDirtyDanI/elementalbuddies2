using System.Collections;
using UnityEngine;

namespace ElementalBuddies
{
    // Bogenschütze: Pfeilschuss (LMB), Rolle mit 2 Aufladungen (RMB),
    // Feuerpfeil-Regen / Frostpfeil / Dornenfalle / Lichtpfeil (R/F/C/V)
    public class ArcherKit : ChampionKit
    {
        [Header("Abschuss")]
        [Tooltip("Abschusspunkt der Pfeile (Kind des Player-Roots, nicht am Knochen). Leer = MuzzleOffset.")]
        public Transform ProjectileSpawn;
        public Vector3 MuzzleOffset = new Vector3(0.15f, 1.25f, 0.7f);

        [Header("Pfeilschuss (LMB)")]
        public GameObject ArrowPrefab;
        public float ArrowDamage = 22f;
        public float ArrowSpeed = 34f;
        public float ArrowRange = 24f;
        public float ArrowCooldown = 0.6f;
        public GameObject ArrowHitFxPrefab;

        [Header("Rolle (RMB, Aufladungen)")]
        public float RollDistance = 4.5f;
        public float RollDuration = 0.3f;
        [Tooltip("Unverwundbar ab Rollenbeginn.")]
        public float RollInvulnerability = 0.35f;
        public int RollCharges = 2;
        [Tooltip("Wiederaufladezeit pro Ladung (eine nach der anderen).")]
        public float RollRecharge = 4f;
        [Tooltip("Mindestabstand zwischen zwei Rollen.")]
        public float RollLockout = 0.35f;
        public GameObject RollDustPrefab;

        [Header("Element-Fähigkeiten (freischaltbar)")]
        public FireArrowRainSpell FireArrowRain = new FireArrowRainSpell(); // R
        public FrostArrowSpell FrostArrow = new FrostArrowSpell();          // F
        public ThornTrapSpell ThornTrap = new ThornTrapSpell();             // C
        public LightArrowSpell LightArrow = new LightArrowSpell();          // V

        [Header("Sounds")]
        public ChampionSfx ShootSfx = new ChampionSfx();
        public ChampionSfx ArrowHitSfx = new ChampionSfx();
        public ChampionSfx RollSfx = new ChampionSfx();

        public bool IsRolling { get; private set; }

        private Coroutine _roll;
        private float _invulnUntil;

        public override ChampionClass Class => ChampionClass.Archer;

        public Vector3 MuzzlePosition => ProjectileSpawn != null ? ProjectileSpawn.position : transform.TransformPoint(MuzzleOffset);
        public float EffectiveRollDistance => Mod(AbilityId.Roll, AbilityStat.Range, RollDistance) * (Owner != null ? Owner.MobilityMultiplier : 1f);

        // ---------------- Effektive Werte (inkl. Händlerkarten) ----------------

        public float ArrowRangeEff => Mod(AbilityId.ArrowShot, AbilityStat.Range, ArrowRange);
        // Feuerrate: kürzere Abklingzeit zwischen zwei Schüssen
        public float ArrowCooldownEff => ArrowCooldown / ModFactor(AbilityId.ArrowShot, AbilityStat.Speed);
        public int ArrowPierceEff => Owner != null ? Mathf.Max(0, Mathf.RoundToInt(Owner.Mods.Sum(AbilityId.ArrowShot, AbilityStat.Pierce))) : 0;
        public int RollChargesEff => Mathf.Max(1, Owner != null ? Owner.Mods.ApplyInt(AbilityId.Roll, AbilityStat.Charges, RollCharges) : RollCharges);

        public override AbilityId GetAbility(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Primary: return AbilityId.ArrowShot;
                case AbilitySlot.Secondary: return AbilityId.Roll;
                case AbilitySlot.Fire: return AbilityId.FireArrowRain;
                case AbilitySlot.Ice: return AbilityId.FrostArrow;
                case AbilitySlot.Earth: return AbilityId.ThornTrap;
                default: return AbilityId.LightArrow;
            }
        }

        public override ElementSpell GetSpell(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.FireArrowRain: return FireArrowRain;
                case AbilityId.FrostArrow: return FrostArrow;
                case AbilityId.ThornTrap: return ThornTrap;
                case AbilityId.LightArrow: return LightArrow;
                default: return null;
            }
        }

        public override float GetCooldown(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArrowShot: return ArrowCooldownEff;
                case AbilityId.Roll: return RollRecharge;
                default: { var s = GetSpell(id); return s != null ? s.Cooldown : 0f; }
            }
        }

        public override float GetManaCost(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArrowShot:
                case AbilityId.Roll:
                    return 0f;
                default: { var s = GetSpell(id); return s != null ? s.ManaCost : 0f; }
            }
        }

        public override int GetMaxCharges(AbilityId id) => id == AbilityId.Roll ? RollChargesEff : 1;
        public override float GetChargeLockout(AbilityId id) => id == AbilityId.Roll ? RollLockout : 0.3f;
        public override bool AutoRepeatPrimary => true;
        public override bool IsActive(AbilityId id) => id == AbilityId.Roll && IsRolling;

        // Während der Rolle wird nicht geschossen
        public override bool CanCast(AbilityId id) => !IsRolling;

        public override string GetCostLine(AbilityId id)
        {
            if (id == AbilityId.Roll) return $"{ManaColor}kein Mana</color>   •   {RollChargesEff} Aufladungen, je {Fmt(Owner != null ? Owner.GetCooldownDuration(id) : RollRecharge)} s";
            return base.GetCostLine(id);
        }

        // ---------------- Ausführen ----------------

        public override void Cast(AbilityId id, SpellCastContext ctx)
        {
            switch (id)
            {
                case AbilityId.ArrowShot:
                {
                    Vector3 pos = MuzzlePosition;
                    var arrow = LaunchArrow(ArrowPrefab, pos, ctx.AimDirection, ArrowSpeed, ArrowDamage * ctx.DamageMultiplier, ArrowRangeEff, false);
                    if (arrow != null)
                    {
                        arrow.ExtraPierce = ArrowPierceEff;
                        if (arrow.HitFxPrefab == null) arrow.HitFxPrefab = ArrowHitFxPrefab;
                        arrow.HitSfx = ArrowHitSfx;
                    }
                    ShootSfx.Play(pos);
                    break;
                }
                case AbilityId.Roll:
                    Roll(ctx);
                    break;
                default:
                {
                    var spell = GetModdedSpell(id);
                    if (spell != null)
                    {
                        spell.Cast(ctx);
                        if (id == AbilityId.FrostArrow || id == AbilityId.LightArrow) ShootSfx.Play(MuzzlePosition);
                    }
                    break;
                }
            }
        }

        // Pfeil erzeugen (Prefab mit ArrowProjectile; ohne Prefab ein unsichtbarer Pfeil, damit die Fähigkeit trotzdem wirkt)
        public static ArrowProjectile LaunchArrow(GameObject prefab, Vector3 position, Vector3 direction, float speed, float damage, float range, bool pierce)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(direction.normalized);
            GameObject go = prefab != null ? Instantiate(prefab, position, rot) : new GameObject("Arrow");
            go.transform.SetPositionAndRotation(position, rot);
            var arrow = go.GetComponent<ArrowProjectile>();
            if (arrow == null) arrow = go.AddComponent<ArrowProjectile>();
            arrow.Speed = speed;
            arrow.Damage = damage;
            arrow.MaxDistance = range;
            arrow.Pierce = pierce;
            return arrow;
        }

        // ---------------- Rolle ----------------

        private void Roll(SpellCastContext ctx)
        {
            // Richtung: Laufrichtung (WASD, vom Besitzer im Cast-Kontext mitgeschickt), sonst Richtung Mauszeiger
            Vector3 dir = ctx.AimDirection;
            Vector3 move = ctx.MoveDirection;
            move.y = 0f;
            if (move.sqrMagnitude > 0.04f) dir = move.normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            if (_roll != null) StopCoroutine(_roll);
            _roll = StartCoroutine(RollRoutine(dir, EffectiveRollDistance, Mathf.Max(0.05f, RollDuration)));
        }

        private IEnumerator RollRoutine(Vector3 dir, float distance, float duration)
        {
            BeginRoll(dir);
            float t = 0f;
            while (t < duration)
            {
                float dt = Mathf.Min(Time.deltaTime, duration - t);
                t += dt;
                RollStep(dir, distance, duration, dt);
                yield return null;
            }
            EndRoll();
        }

        // Einzelne Schritte öffentlich, damit Tests die Rolle synchron simulieren können
        public void BeginRoll(Vector3 dir)
        {
            IsRolling = true;
            Controller.MovementLocked = true;
            Controller.RotationLocked = true;
            // Drehen nur auf der eigenen Figur (fremde: NetworkTransform)
            if (IsLocalControl) transform.rotation = Quaternion.LookRotation(dir);
            Stats.IsInvulnerable = true;
            _invulnUntil = Time.time + RollInvulnerability;
            if (RollDustPrefab != null) CombatUtil.SpawnFx(RollDustPrefab, Ground(transform.position), Quaternion.LookRotation(dir), 2f);
            RollSfx.Play(transform.position);
        }

        public void RollStep(Vector3 dir, float distance, float duration, float dt)
        {
            // Konstantes Tempo mit leichtem Auslaufen am Ende
            float speed = distance / duration;
            if (IsLocalControl && Character != null && Character.enabled) Character.Move(dir * speed * dt);
        }

        public void EndRoll()
        {
            IsRolling = false;
            _roll = null;
            Controller.MovementLocked = false;
            Controller.RotationLocked = false;
            if (Time.time >= _invulnUntil) Stats.IsInvulnerable = false;
            if (RollDustPrefab != null) CombatUtil.SpawnFx(RollDustPrefab, Ground(transform.position), transform.rotation, 2f, 0.7f);
        }

        void Update()
        {
            // I-Frames können die Rolle überdauern
            if (!IsRolling && Stats != null && Stats.IsInvulnerable && _invulnUntil > 0f && Time.time >= _invulnUntil)
            {
                Stats.IsInvulnerable = false;
                _invulnUntil = 0f;
            }
        }

        public override void OnDeactivated()
        {
            if (_roll != null) StopCoroutine(_roll);
            _roll = null;
            if (IsRolling)
            {
                IsRolling = false;
                if (Controller != null) { Controller.MovementLocked = false; Controller.RotationLocked = false; }
            }
            if (Stats != null && _invulnUntil > 0f) Stats.IsInvulnerable = false;
            _invulnUntil = 0f;
        }

        // ---------------- Animation ----------------

        public override string GetAnimTrigger(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArrowShot:
                case AbilityId.FrostArrow:
                case AbilityId.LightArrow:
                    return "Shoot";
                case AbilityId.Roll: return "Roll";
                case AbilityId.FireArrowRain: return "ArrowRain";
                case AbilityId.ThornTrap: return "PlaceTrap";
                default: return null;
            }
        }

        // ---------------- Texte ----------------

        public override string GetName(AbilityId id)
        {
            foreach (var a in Abilities)
                if (a != null && a.Id == id && !string.IsNullOrEmpty(a.Name)) return a.Name;
            switch (id)
            {
                case AbilityId.ArrowShot: return "Pfeilschuss";
                case AbilityId.Roll: return "Rolle";
                case AbilityId.FireArrowRain: return "Feuerpfeil-Regen";
                case AbilityId.FrostArrow: return "Frostpfeil";
                case AbilityId.ThornTrap: return "Dornenfalle";
                case AbilityId.LightArrow: return "Lichtpfeil";
                default: return base.GetName(id);
            }
        }

        public override bool TryGetStatValue(AbilityId id, AbilityStat stat, out float value, out string unit, out string label)
        {
            value = 0f;
            unit = "";
            label = "";
            if (id == AbilityId.ArrowShot)
            {
                switch (stat)
                {
                    case AbilityStat.Damage: value = ArrowDamage * DamageMult(id); label = "Schaden"; return true;
                    case AbilityStat.Range: value = ArrowRangeEff; unit = " m"; label = "Reichweite"; return true;
                    case AbilityStat.Pierce: value = ArrowPierceEff; label = "Durchschlag"; unit = " Gegner"; return true;
                    case AbilityStat.Speed: value = Owner != null ? Owner.GetCooldownDuration(id) : ArrowCooldownEff; unit = " s"; label = "Zeit zwischen Schüssen"; return true;
                }
            }
            else if (id == AbilityId.Roll)
            {
                switch (stat)
                {
                    case AbilityStat.Charges: value = RollChargesEff; label = "Aufladungen"; return true;
                    case AbilityStat.Range: value = EffectiveRollDistance; unit = " m"; label = "Rollweite"; return true;
                    case AbilityStat.Cooldown: value = Owner != null ? Owner.GetCooldownDuration(id) : RollRecharge; unit = " s"; label = "Aufladezeit"; return true;
                }
            }
            return base.TryGetStatValue(id, stat, out value, out unit, out label);
        }

        public override string Describe(AbilityId id, float dm)
        {
            switch (id)
            {
                case AbilityId.ArrowShot:
                {
                    int pierce = ArrowPierceEff;
                    string hit = pierce > 0 ? $"am ersten getroffenen Gegner und durchschlägt bis zu {Hi(pierce)} weitere" : "am ersten getroffenen Gegner";
                    return $"Schneller Pfeil in Blickrichtung (bis {Hi(ArrowRangeEff)} m). Er verursacht {Hi(ArrowDamage * dm)} Schaden {hit}. Gedrückt halten schießt weiter.";
                }
                case AbilityId.Roll:
                    return $"Hechtrolle über {Hi(EffectiveRollDistance)} m in Laufrichtung (ohne Eingabe Richtung Mauszeiger). {Hi(RollInvulnerability)} s unverwundbar. {Hi(RollChargesEff)} Aufladungen, die sich nacheinander in je {Hi(Owner != null ? Owner.GetCooldownDuration(id) : RollRecharge)} s wieder füllen.";
                case AbilityId.FireArrowRain:
                {
                    var s = Modded(FireArrowRain, id);
                    return $"Brennende Pfeile regnen {Hi(s.RainDuration)} s lang auf das Gebiet am Mauszeiger ({Hi(s.Radius)} m Radius, bis {Hi(s.MaxCastRange)} m entfernt): {Hi(s.Waves)} × {Hi(s.DamagePerWave * dm)} Schaden und Brand mit {Hi(s.BurnDps * dm)} Schaden pro Sekunde für {Hi(s.BurnDuration)} s.";
                }
                case AbilityId.FrostArrow:
                {
                    var s = Modded(FrostArrow, id);
                    return $"Eisiger Pfeil, der alle Gegner in seiner Bahn durchschlägt ({Hi(s.Range)} m). Jeder Getroffene erleidet {Hi(s.Damage * dm)} Schaden und wird {Hi(s.FreezeDuration)} s eingefroren.";
                }
                case AbilityId.ThornTrap:
                {
                    var s = Modded(ThornTrap, id);
                    return $"Legt am Mauszeiger (bis {Hi(s.MaxCastRange)} m) eine Dornenfalle ({Hi(s.TriggerRadius)} m). Läuft ein Gegner hinein, schnappt sie zu: bis zu {Hi(s.MaxTargets)} Gegner werden {Hi(s.RootDuration)} s festgehalten und erleiden {Hi(s.Damage * dm)} Schaden. Hält {Hi(s.Lifetime)} s.";
                }
                case AbilityId.LightArrow:
                {
                    var s = Modded(LightArrow, id);
                    return $"Ein Lichtstrahl schießt sofort {Hi(s.Length)} m weit durch alle Gegner in Blickrichtung: {Hi(s.Damage * dm)} Schaden, dazu {Hi(s.BlindDuration)} s geblendet und um {Hi(s.BlindSlow * 100f)} % verlangsamt.";
                }
                default:
                    return "";
            }
        }
    }
}
