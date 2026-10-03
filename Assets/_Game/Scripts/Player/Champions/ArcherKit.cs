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
        public float EffectiveRollDistance => RollDistance * (Owner != null ? Owner.MobilityMultiplier : 1f);

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

        public ElementSpell GetSpell(AbilityId id)
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
                case AbilityId.ArrowShot: return ArrowCooldown;
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

        public override int GetMaxCharges(AbilityId id) => id == AbilityId.Roll ? Mathf.Max(1, RollCharges) : 1;
        public override float GetChargeLockout(AbilityId id) => id == AbilityId.Roll ? RollLockout : 0.3f;
        public override bool AutoRepeatPrimary => true;
        public override bool IsActive(AbilityId id) => id == AbilityId.Roll && IsRolling;

        // Während der Rolle wird nicht geschossen
        public override bool CanCast(AbilityId id) => !IsRolling;

        public override string GetCostLine(AbilityId id)
        {
            if (id == AbilityId.Roll) return $"{ManaColor}kein Mana</color>   •   {RollCharges} Aufladungen, je {Fmt(Owner != null ? Owner.GetCooldownDuration(id) : RollRecharge)} s";
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
                    var arrow = LaunchArrow(ArrowPrefab, pos, ctx.AimDirection, ArrowSpeed, ArrowDamage * ctx.DamageMultiplier, ArrowRange, false);
                    if (arrow != null)
                    {
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
                    var spell = GetSpell(id);
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
            // Richtung: Laufrichtung (WASD), sonst Richtung Mauszeiger
            Vector3 dir = ctx.AimDirection;
            if (Controller != null && Controller.MoveAction != null)
            {
                Vector2 input = Controller.MoveAction.ReadValue<Vector2>();
                if (input.sqrMagnitude > 0.04f) dir = new Vector3(input.x, 0f, input.y).normalized;
            }
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
            transform.rotation = Quaternion.LookRotation(dir);
            Stats.IsInvulnerable = true;
            _invulnUntil = Time.time + RollInvulnerability;
            if (RollDustPrefab != null) CombatUtil.SpawnFx(RollDustPrefab, Ground(transform.position), Quaternion.LookRotation(dir), 2f);
            RollSfx.Play(transform.position);
        }

        public void RollStep(Vector3 dir, float distance, float duration, float dt)
        {
            // Konstantes Tempo mit leichtem Auslaufen am Ende
            float speed = distance / duration;
            if (Character != null && Character.enabled) Character.Move(dir * speed * dt);
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

        public override string Describe(AbilityId id, float dm)
        {
            switch (id)
            {
                case AbilityId.ArrowShot:
                    return $"Schneller Pfeil in Blickrichtung (bis {Hi(ArrowRange)} m). Er verursacht {Hi(ArrowDamage * dm)} Schaden am ersten getroffenen Gegner. Gedrückt halten schießt weiter.";
                case AbilityId.Roll:
                    return $"Hechtrolle über {Hi(EffectiveRollDistance)} m in Laufrichtung (ohne Eingabe Richtung Mauszeiger). {Hi(RollInvulnerability)} s unverwundbar. {Hi(RollCharges)} Aufladungen, die sich nacheinander in je {Hi(RollRecharge)} s wieder füllen.";
                case AbilityId.FireArrowRain:
                {
                    var s = FireArrowRain;
                    return $"Brennende Pfeile regnen {Hi(s.RainDuration)} s lang auf das Gebiet am Mauszeiger ({Hi(s.Radius)} m Radius, bis {Hi(s.MaxCastRange)} m entfernt): {Hi(s.Waves)} × {Hi(s.DamagePerWave * dm)} Schaden und Brand mit {Hi(s.BurnDps * dm)} Schaden pro Sekunde für {Hi(s.BurnDuration)} s.";
                }
                case AbilityId.FrostArrow:
                {
                    var s = FrostArrow;
                    return $"Eisiger Pfeil, der alle Gegner in seiner Bahn durchschlägt ({Hi(s.Range)} m). Jeder Getroffene erleidet {Hi(s.Damage * dm)} Schaden und wird {Hi(s.FreezeDuration)} s eingefroren.";
                }
                case AbilityId.ThornTrap:
                {
                    var s = ThornTrap;
                    return $"Legt am Mauszeiger (bis {Hi(s.MaxCastRange)} m) eine Dornenfalle ({Hi(s.TriggerRadius)} m). Läuft ein Gegner hinein, schnappt sie zu: bis zu {Hi(s.MaxTargets)} Gegner werden {Hi(s.RootDuration)} s festgehalten und erleiden {Hi(s.Damage * dm)} Schaden. Hält {Hi(s.Lifetime)} s.";
                }
                case AbilityId.LightArrow:
                {
                    var s = LightArrow;
                    return $"Ein Lichtstrahl schießt sofort {Hi(s.Length)} m weit durch alle Gegner in Blickrichtung: {Hi(s.Damage * dm)} Schaden, dazu {Hi(s.BlindDuration)} s geblendet und um {Hi(s.BlindSlow * 100f)} % verlangsamt.";
                }
                default:
                    return "";
            }
        }
    }
}
