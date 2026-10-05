using System.Collections;
using UnityEngine;

namespace ElementalBuddies
{
    // Magier: Arkanball (LMB), Blink (RMB), Flammenwelle / Frostnova / Steinwall / Heiliger Kreis (R/F/C/V)
    public class MageKit : ChampionKit
    {
        [Header("Arkanball (LMB)")]
        public GameObject ArcaneBallPrefab;
        public float ArcaneBallManaCost = 0f;
        public float ArcaneBallCooldown = 1f;
        public Transform SpawnPoint;

        [Header("Blink (RMB)")]
        public float BlinkManaCost = 30f;
        public float BlinkCooldown = 8f;
        public float BlinkRange = 8f;
        public float InvulnerabilityDuration = 0.4f;
        public LayerMask ObstacleLayer = 1; // "Default" bzw. Wand-Layer

        [Header("Element-Zauber (freischaltbar)")]
        public FireWaveSpell FireWave = new FireWaveSpell();      // R
        public FrostNovaSpell FrostNova = new FrostNovaSpell();   // F
        public StoneWallSpell StoneWall = new StoneWallSpell();   // C
        public HolyCircleSpell HolyCircle = new HolyCircleSpell(); // V

        [Header("Animation")]
        [Tooltip("Blink ist Bewegung – standardmäßig keine Zauber-Animation.")]
        public bool CastAnimOnBlink = false;

        // Raised on a successful Arcane Ball cast (e.g. for animation)
        public event System.Action ArcaneBallCast;

        public override ChampionClass Class => ChampionClass.Mage;

        public float EffectiveBlinkRange => Mod(AbilityId.Blink, AbilityStat.Range, BlinkRange) * (Owner != null ? Owner.MobilityMultiplier : 1f);
        public float EffectiveInvulnerability => Mod(AbilityId.Blink, AbilityStat.Duration, InvulnerabilityDuration);
        // Arkanball: Größe (Skalierung von Modell + Trigger) und Flugtempo mit Händlerkarten
        public float ArcaneBallSizeFactor => ModFactor(AbilityId.ArcaneBall, AbilityStat.Area);
        public float ArcaneBallSpeed => Mod(AbilityId.ArcaneBall, AbilityStat.Speed, BaseArcaneBall != null ? BaseArcaneBall.Speed : 20f);
        public float ArcaneBallBaseDamage => BaseArcaneBall != null ? BaseArcaneBall.Damage : 35f;
        private ArcaneBall BaseArcaneBall => ArcaneBallPrefab != null ? ArcaneBallPrefab.GetComponent<ArcaneBall>() : null;

        public override AbilityId GetAbility(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Primary: return AbilityId.ArcaneBall;
                case AbilitySlot.Secondary: return AbilityId.Blink;
                case AbilitySlot.Fire: return AbilityId.FireWave;
                case AbilitySlot.Ice: return AbilityId.FrostNova;
                case AbilitySlot.Earth: return AbilityId.StoneWall;
                default: return AbilityId.HolyCircle;
            }
        }

        public override ElementSpell GetSpell(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.FireWave: return FireWave;
                case AbilityId.FrostNova: return FrostNova;
                case AbilityId.StoneWall: return StoneWall;
                case AbilityId.HolyCircle: return HolyCircle;
                default: return null;
            }
        }

        public override float GetCooldown(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall: return ArcaneBallCooldown;
                case AbilityId.Blink: return BlinkCooldown;
                default: { var s = GetSpell(id); return s != null ? s.Cooldown : 0f; }
            }
        }

        public override float GetManaCost(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall: return ArcaneBallManaCost;
                case AbilityId.Blink: return BlinkManaCost;
                default: { var s = GetSpell(id); return s != null ? s.ManaCost : 0f; }
            }
        }

        public override bool CanCast(AbilityId id)
        {
            if (id == AbilityId.ArcaneBall) return ArcaneBallPrefab != null;
            return true;
        }

        public override void Cast(AbilityId id, SpellCastContext ctx)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall:
                    CastArcaneBall(ctx.DamageMultiplier);
                    break;
                case AbilityId.Blink:
                    StartCoroutine(PerformBlink());
                    break;
                default:
                    var spell = GetModdedSpell(id);
                    if (spell != null) spell.Cast(ctx);
                    break;
            }
        }

        public override string GetAnimTrigger(AbilityId id)
        {
            if (id == AbilityId.Blink && !CastAnimOnBlink) return null;
            return "Cast";
        }

        private void CastArcaneBall(float damageMultiplier)
        {
            Vector3 spawnPos = SpawnPoint != null ? SpawnPoint.position : transform.position + transform.forward + Vector3.up;
            GameObject ball = Instantiate(ArcaneBallPrefab, spawnPos, transform.rotation);
            var arcane = ball.GetComponent<ArcaneBall>();
            if (arcane != null)
            {
                arcane.Damage *= damageMultiplier; // global × Händlerkarten (SpellCastContext)
                arcane.Speed = ArcaneBallSpeed;
            }
            float size = ArcaneBallSizeFactor;
            if (!Mathf.Approximately(size, 1f)) ball.transform.localScale *= size; // Trigger wächst mit
            if (arcane != null) arcane.SizeFactor = size;
            ArcaneBallCast?.Invoke();
        }

        private IEnumerator PerformBlink()
        {
            Stats.IsInvulnerable = true;

            // Richtung Mauszeiger (Rechtsklick); liegt der Zeiger näher als die Reichweite, landet man genau dort
            float range = EffectiveBlinkRange;
            Vector3 blinkDir = transform.forward;
            float distance = range;
            if (Controller.HasAimPoint)
            {
                Vector3 to = Controller.AimPoint - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.04f)
                {
                    blinkDir = to.normalized;
                    distance = Mathf.Min(range, to.magnitude);
                }
            }

            // Wall Check
            Vector3 targetPos = transform.position + blinkDir * distance;
            if (Physics.Raycast(transform.position + Vector3.up, blinkDir, out RaycastHit hit, distance, ObstacleLayer))
            {
                targetPos = hit.point - blinkDir * 0.5f; // Stop slightly before wall
                targetPos.y = transform.position.y;
            }

            Character.enabled = false;
            transform.position = targetPos;
            Character.enabled = true;

            yield return new WaitForSeconds(EffectiveInvulnerability);
            Stats.IsInvulnerable = false;
        }

        public override void OnDeactivated()
        {
            StopAllCoroutines();
            if (Stats != null) Stats.IsInvulnerable = false;
        }

        // ---------------- Texte ----------------

        public override string Describe(AbilityId id, float dm)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall:
                {
                    string extra = "";
                    float size = ArcaneBallSizeFactor;
                    if (!Mathf.Approximately(size, 1f)) extra += $" Kugelgröße {Hi(size * 100f)} %.";
                    if (BaseArcaneBall != null && !Mathf.Approximately(ArcaneBallSpeed, BaseArcaneBall.Speed)) extra += $" Flugtempo {Hi(ArcaneBallSpeed)} m/s.";
                    return $"Schleudert eine arkane Kugel in Blickrichtung. Sie verursacht {Hi(ArcaneBallBaseDamage * dm)} Schaden am ersten getroffenen Gegner.{extra}";
                }
                case AbilityId.Blink:
                    return $"Teleportiert dich bis zu {Hi(EffectiveBlinkRange)} m in Richtung Mauszeiger. Kurz nach dem Sprung bist du {Hi(EffectiveInvulnerability)} s unverwundbar. Wände halten den Sprung auf.";
                case AbilityId.FireWave:
                {
                    var s = Modded(FireWave, id);
                    return $"Eine Flammenwelle im Kegel vor dir ({Hi(s.ConeAngle)}°, {Hi(s.Range)} m). Sie verursacht {Hi(s.Damage * dm)} Schaden und setzt Gegner in Brand: {Hi(s.BurnDps * dm)} Schaden pro Sekunde für {Hi(s.BurnDuration)} s.";
                }
                case AbilityId.FrostNova:
                {
                    var s = Modded(FrostNova, id);
                    return $"Eisige Druckwelle um dich herum ({Hi(s.Radius)} m). Sie verursacht {Hi(s.Damage * dm)} Schaden und friert Gegner {Hi(s.FreezeDuration)} s komplett ein: Sie können sich weder bewegen noch angreifen.";
                }
                case AbilityId.StoneWall:
                {
                    var s = Modded(StoneWall, id);
                    return $"Lässt {Hi(s.Distance)} m vor dir eine {Hi(s.Length)} m breite Steinmauer quer zur Blickrichtung aufsteigen. Gegner müssen {Hi(s.Lifetime)} s lang außen herum laufen – ideal, um Engstellen zu sperren.";
                }
                case AbilityId.HolyCircle:
                {
                    var s = Modded(HolyCircle, id);
                    return $"Heiliges Licht im Umkreis von {Hi(s.Radius)} m. Es heilt dich um {Hi(s.PlayerHeal)}, Buddies um {Hi(s.BuddyHeal)} und den Nexus um {Hi(s.NexusHeal)} LP. Gegner werden geblendet (Sterne über dem Kopf) und sind {Hi(s.BlindDuration)} s lang um {Hi(s.BlindSlow * 100f)} % verlangsamt.";
                }
                default:
                    return "";
            }
        }

        public override bool TryGetStatValue(AbilityId id, AbilityStat stat, out float value, out string unit, out string label)
        {
            unit = "";
            label = "";
            value = 0f;
            if (id == AbilityId.ArcaneBall)
            {
                switch (stat)
                {
                    case AbilityStat.Damage: value = ArcaneBallBaseDamage * DamageMult(id); label = "Schaden"; return true;
                    case AbilityStat.Area: value = ArcaneBallSizeFactor * 100f; unit = " %"; label = "Kugelgröße"; return true;
                    case AbilityStat.Speed: value = ArcaneBallSpeed; unit = " m/s"; label = "Flugtempo"; return true;
                }
            }
            else if (id == AbilityId.Blink)
            {
                switch (stat)
                {
                    case AbilityStat.Range: value = EffectiveBlinkRange; unit = " m"; label = "Reichweite"; return true;
                    case AbilityStat.Duration: value = EffectiveInvulnerability; unit = " s"; label = "Unverwundbarkeit"; return true;
                }
            }
            return base.TryGetStatValue(id, stat, out value, out unit, out label);
        }

        public override string GetName(AbilityId id)
        {
            foreach (var a in Abilities)
                if (a != null && a.Id == id && !string.IsNullOrEmpty(a.Name)) return a.Name;
            switch (id)
            {
                case AbilityId.ArcaneBall: return "Arkanball";
                case AbilityId.Blink: return "Blinzeln";
                case AbilityId.FireWave: return "Flammenwelle";
                case AbilityId.FrostNova: return "Frostnova";
                case AbilityId.StoneWall: return "Steinwall";
                case AbilityId.HolyCircle: return "Heiliger Kreis";
                default: return base.GetName(id);
            }
        }
    }
}
