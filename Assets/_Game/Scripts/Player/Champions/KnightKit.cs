using System.Collections;
using UnityEngine;

namespace ElementalBuddies
{
    // Schwertkämpfer: Schwerthieb-Kombo (LMB), Schildblock halten (RMB),
    // Flammenwirbel / Frostschlag / Erdbeben / Lichtschwur (R/F/C/V)
    public class KnightKit : ChampionKit
    {
        [Header("Schwerthieb (LMB, 3er-Kombo)")]
        public float SlashDamage = 20f;
        [Tooltip("Dritter Schlag der Kombo.")]
        public float FinisherDamage = 36f;
        public float SlashRange = 2.6f;
        [Tooltip("Volle Öffnung des Schlagbogens in Grad.")]
        public float SlashArc = 120f;
        [Tooltip("Abklingzeit zwischen Schlag 1→2→3.")]
        public float SlashInterval = 0.45f;
        [Tooltip("Abklingzeit nach dem dritten Schlag.")]
        public float FinisherRecovery = 0.8f;
        [Tooltip("So lange nach einem Schlag geht die Kombo weiter.")]
        public float ComboWindow = 1.0f;
        public float FinisherKnockback = 2.5f;
        [Tooltip("Höhe des Schlagbogens / Treffer-Mittelpunkts über dem Boden.")]
        public float HitHeight = 1.0f;
        [Tooltip("SlashArcFx-Prefab (Schwert-Bogen).")]
        public GameObject SlashFxPrefab;
        [Tooltip("Funken am getroffenen Gegner.")]
        public GameObject HitFxPrefab;
        [ColorUsage(true, true)] public Color SlashColor = new Color(1.3f, 1.45f, 1.8f, 1f);
        [ColorUsage(true, true)] public Color FinisherColor = new Color(2f, 1.5f, 0.45f, 1f);

        [Header("Schildblock (RMB halten)")]
        [Tooltip("Volle Öffnung des geblockten Bereichs vor dir in Grad.")]
        public float BlockAngle = 110f;
        [Range(0f, 1f)] public float BlockReduction = 1f;
        [Tooltip("Mana pro geblocktem Schadenspunkt.")]
        public float ManaPerBlockedDamage = 0.5f;
        [Tooltip("Mindest-Mana, um den Block zu beginnen.")]
        public float BlockMinMana = 5f;
        [Range(0.1f, 1f)] public float BlockMoveMultiplier = 0.5f;
        [Tooltip("Sperre, nachdem der Block mangels Mana bricht.")]
        public float BlockBreakCooldown = 3f;
        [Tooltip("Abklingzeit nach dem Loslassen.")]
        public float BlockReleaseCooldown = 0.3f;
        [Tooltip("Funken am Schild bei geblocktem Treffer.")]
        public GameObject BlockSparkPrefab;
        [Tooltip("Leuchten am Schild, solange der Block gehalten wird (optional, wird an-/ausgeschaltet).")]
        public GameObject BlockGlowPrefab;
        public float BlockSparkInterval = 0.25f;
        [Tooltip("Position des Schildes relativ zum Spieler (für Funken).")]
        public Vector3 ShieldOffset = new Vector3(0f, 1.1f, 0.6f);

        [Header("Element-Fähigkeiten (freischaltbar)")]
        public FlameWhirlSpell FlameWhirl = new FlameWhirlSpell();    // R
        public FrostStrikeSpell FrostStrike = new FrostStrikeSpell(); // F
        public EarthquakeSpell Earthquake = new EarthquakeSpell();    // C
        public LightOathSpell LightOath = new LightOathSpell();       // V

        [Header("Sounds")]
        public ChampionSfx SwingSfx = new ChampionSfx();
        public ChampionSfx FinisherSfx = new ChampionSfx();
        public ChampionSfx BlockHitSfx = new ChampionSfx { MinInterval = 0.2f };
        public ChampionSfx BlockBreakSfx = new ChampionSfx();

        // Zustand (öffentlich lesbar für UI/Tests)
        public bool IsBlocking { get; private set; }
        public int ComboStep { get; private set; }       // zuletzt ausgeführter Schlag 1..3 (0 = keiner)
        public float BlockedDamageTotal { get; private set; }
        public bool IsLeaping { get; private set; }
        public bool OathActive => Time.time < _oathUntil;

        private float _comboExpires;
        private float _lastSpark = -999f;
        private float _oathUntil;
        private float _oathReduction;
        private GameObject _oathAura;
        private GameObject _blockGlow;
        private Coroutine _leap;

        public override ChampionClass Class => ChampionClass.Knight;

        public override AbilityId GetAbility(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Primary: return AbilityId.SwordSlash;
                case AbilitySlot.Secondary: return AbilityId.ShieldBlock;
                case AbilitySlot.Fire: return AbilityId.FlameWhirl;
                case AbilitySlot.Ice: return AbilityId.FrostStrike;
                case AbilitySlot.Earth: return AbilityId.Earthquake;
                default: return AbilityId.LightOath;
            }
        }

        public ElementSpell GetSpell(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.FlameWhirl: return FlameWhirl;
                case AbilityId.FrostStrike: return FrostStrike;
                case AbilityId.Earthquake: return Earthquake;
                case AbilityId.LightOath: return LightOath;
                default: return null;
            }
        }

        public override float GetCooldown(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.SwordSlash: return SlashInterval;
                case AbilityId.ShieldBlock: return 0f;
                default: { var s = GetSpell(id); return s != null ? s.Cooldown : 0f; }
            }
        }

        public override float GetManaCost(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.SwordSlash:
                case AbilityId.ShieldBlock:
                    return 0f;
                default: { var s = GetSpell(id); return s != null ? s.ManaCost : 0f; }
            }
        }

        public override float GetRequiredMana(AbilityId id) => id == AbilityId.ShieldBlock ? BlockMinMana : GetManaCost(id);

        public override bool AutoRepeatPrimary => true;
        public override bool IsHoldAbility(AbilityId id) => id == AbilityId.ShieldBlock;
        public override bool IsActive(AbilityId id) => (id == AbilityId.ShieldBlock && IsBlocking) || (id == AbilityId.LightOath && OathActive);
        public override float MoveSpeedMultiplier => IsBlocking ? BlockMoveMultiplier : 1f;

        public override bool CanCast(AbilityId id)
        {
            if (IsLeaping) return false;
            // Kein Angriff, solange der Schild oben ist
            if (id == AbilityId.SwordSlash && IsBlocking) return false;
            return true;
        }

        public override string GetManaLabel(AbilityId id)
        {
            if (id == AbilityId.ShieldBlock) return Fmt(ManaPerBlockedDamage) + "/LP";
            return base.GetManaLabel(id);
        }

        public override string GetCostLine(AbilityId id)
        {
            if (id == AbilityId.ShieldBlock) return $"{ManaColor}{Fmt(ManaPerBlockedDamage)} Mana pro geblocktem LP</color>   •   halten";
            if (id == AbilityId.SwordSlash) return $"{ManaColor}kein Mana</color>   •   {Fmt(SlashInterval)} s zwischen Schlägen";
            return base.GetCostLine(id);
        }

        // ---------------- Ausführen ----------------

        public override void Cast(AbilityId id, SpellCastContext ctx)
        {
            switch (id)
            {
                case AbilityId.SwordSlash:
                    DoSlash(ctx);
                    break;
                case AbilityId.ShieldBlock:
                    SetBlocking(true);
                    break;
                default:
                    var spell = GetSpell(id);
                    if (spell != null) spell.Cast(ctx);
                    break;
            }
        }

        private void DoSlash(SpellCastContext ctx)
        {
            // Kombo fortsetzen oder neu beginnen
            int step = (Time.time <= _comboExpires && ComboStep < 3) ? ComboStep + 1 : 1;
            ComboStep = step;
            bool finisher = step == 3;
            _comboExpires = Time.time + ComboWindow;
            if (finisher) Owner.StartCooldown(AbilityId.SwordSlash, FinisherRecovery * Mathf.Max(0.1f, Owner.CooldownMultiplier));

            float damage = (finisher ? FinisherDamage : SlashDamage) * ctx.DamageMultiplier;
            Vector3 origin = transform.position;
            Vector3 dir = ctx.AimDirection;

            foreach (var enemy in CombatUtil.FindEnemiesInCone(origin, dir, SlashRange + 0.4f, SlashArc))
            {
                if (enemy == null) continue;
                if (CombatUtil.HorizontalDistance(origin, enemy.transform.position) > SlashRange + CombatUtil.EnemyRadius(enemy)) continue;
                if (HitFxPrefab != null)
                    CombatUtil.SpawnFx(HitFxPrefab, enemy.transform.position + Vector3.up * HitHeight, Quaternion.LookRotation(dir), 1.5f);
                if (finisher && FinisherKnockback > 0f)
                    enemy.Knockback(CombatUtil.FlatDirection(origin, enemy.transform.position, dir), FinisherKnockback, 0.25f);
                enemy.TakeDamage(damage);
            }

            // Bogen: Schlag 1 von rechts, 2 von links, 3 breiter + golden
            Vector3 fxPos = Ground(origin) + Vector3.up * HitHeight;
            var arc = SlashArcFx.Spawn(SlashFxPrefab, fxPos, dir, SlashRange, finisher ? SlashArc + 30f : SlashArc,
                finisher ? FinisherColor : SlashColor, step != 2, step == 1 ? -12f : (step == 2 ? 12f : 0f));
            if (arc != null && finisher) arc.Width = 1.2f;

            (finisher ? FinisherSfx : SwingSfx).Play(origin);
        }

        // ---------------- Schildblock ----------------

        public override void SetHeld(AbilityId id, bool held)
        {
            if (id == AbilityId.ShieldBlock && !held && IsBlocking)
            {
                SetBlocking(false);
                Owner.StartCooldown(AbilityId.ShieldBlock, BlockReleaseCooldown);
            }
        }

        private void SetBlocking(bool on)
        {
            IsBlocking = on;
            if (on) ComboStep = 0;
            if (BlockGlowPrefab != null)
            {
                if (_blockGlow == null && on)
                {
                    _blockGlow = Instantiate(BlockGlowPrefab, transform);
                    _blockGlow.transform.localPosition = ShieldOffset;
                    _blockGlow.transform.localRotation = Quaternion.identity;
                }
                if (_blockGlow != null) _blockGlow.SetActive(on);
            }
        }

        private void BreakBlock()
        {
            SetBlocking(false);
            Owner.StartCooldown(AbilityId.ShieldBlock, BlockBreakCooldown);
            BlockBreakSfx.Play(transform.position);
            if (BlockSparkPrefab != null)
                CombatUtil.SpawnFx(BlockSparkPrefab, transform.TransformPoint(ShieldOffset), transform.rotation, 1.5f, 1.6f);
        }

        // Liegt die Quelle im Block-Winkel vor dem Spieler?
        public bool IsInBlockArc(Vector3 sourcePosition)
        {
            Vector3 to = sourcePosition - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return true;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            return Vector3.Angle(fwd, to) <= BlockAngle * 0.5f;
        }

        public override float ModifyIncomingDamage(float amount, Vector3 sourcePosition, bool hasSource)
        {
            if (IsBlocking && hasSource && IsInBlockArc(sourcePosition))
            {
                float blocked = amount * BlockReduction;
                float cost = blocked * ManaPerBlockedDamage;
                var eco = EconomyManager.Instance;
                float mana = eco != null ? eco.CurrentMana : 0f;
                if (cost > 0f && mana < cost)
                {
                    // Mana reicht nur für einen Teil: Rest geht durch, Block bricht
                    float part = mana / cost;
                    if (eco != null && mana > 0f) eco.TrySpendMana(mana);
                    blocked *= part;
                    BlockedDamageTotal += blocked;
                    amount -= blocked;
                    BreakBlock();
                }
                else
                {
                    if (eco != null && cost > 0f) eco.TrySpendMana(cost);
                    BlockedDamageTotal += blocked;
                    amount -= blocked;
                    if (Time.time - _lastSpark >= BlockSparkInterval)
                    {
                        _lastSpark = Time.time;
                        Vector3 shield = transform.TransformPoint(ShieldOffset);
                        if (BlockSparkPrefab != null)
                            CombatUtil.SpawnFx(BlockSparkPrefab, shield, Quaternion.LookRotation(CombatUtil.FlatDirection(transform.position, sourcePosition, transform.forward)), 1.5f);
                        BlockHitSfx.Play(shield);
                    }
                }
            }

            if (OathActive && _oathReduction > 0f) amount *= 1f - _oathReduction;
            return amount;
        }

        // ---------------- Erdbeben-Sprung ----------------

        // Sprung über die Strecke offset (CharacterController, nicht teleportiert); Modell hebt in einem Bogen ab
        public void Leap(Vector3 offset, float duration, float height, System.Action onLand)
        {
            if (_leap != null) StopCoroutine(_leap);
            _leap = StartCoroutine(LeapRoutine(offset, Mathf.Max(0.05f, duration), height, onLand));
        }

        private IEnumerator LeapRoutine(Vector3 offset, float duration, float height, System.Action onLand)
        {
            IsLeaping = true;
            if (IsBlocking) SetBlocking(false);
            Controller.MovementLocked = true;
            Controller.RotationLocked = true;
            Transform visual = Owner.ActiveVisual != null ? Owner.ActiveVisual.transform : null;
            Vector3 visualBase = visual != null ? visual.localPosition : Vector3.zero;

            float t = 0f;
            float done = 0f;
            while (t < duration)
            {
                t = Mathf.Min(duration, t + Time.deltaTime);
                float k = t / duration;
                float step = k - done;
                done = k;
                if (Character != null && Character.enabled) Character.Move(offset * step);
                if (visual != null) visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(k * Mathf.PI) * height);
                yield return null;
            }

            if (visual != null) visual.localPosition = visualBase;
            Controller.MovementLocked = false;
            Controller.RotationLocked = false;
            IsLeaping = false;
            _leap = null;
            if (onLand != null) onLand();
        }

        // ---------------- Lichtschwur ----------------

        public void BeginOath(float duration, float reduction, GameObject auraPrefab)
        {
            _oathUntil = Time.time + duration;
            _oathReduction = reduction;
            if (auraPrefab != null)
            {
                if (_oathAura != null) Destroy(_oathAura);
                _oathAura = Instantiate(auraPrefab, transform);
                _oathAura.transform.localPosition = Vector3.zero;
                Destroy(_oathAura, duration);
            }
        }

        // ---------------- Animation ----------------

        public override string GetAnimTrigger(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.SwordSlash: return "Slash" + Mathf.Clamp(ComboStep, 1, 3);
                case AbilityId.FlameWhirl: return "FlameWhirl";
                case AbilityId.FrostStrike: return "FrostStrike";
                case AbilityId.Earthquake: return "Earthquake";
                case AbilityId.LightOath: return "LightOath";
                default: return null; // Block läuft über das Bool "Block"
            }
        }

        public override void UpdateAnimator(Animator animator, ChampionVisual visual)
        {
            PlayerVisualAnimator.SetBoolSafe(animator, "Block", IsBlocking);
        }

        public override void OnDeactivated()
        {
            if (IsBlocking) SetBlocking(false);
            if (_leap != null)
            {
                StopCoroutine(_leap);
                _leap = null;
                IsLeaping = false;
                if (Controller != null) { Controller.MovementLocked = false; Controller.RotationLocked = false; }
            }
            if (_oathAura != null) Destroy(_oathAura);
            _oathUntil = 0f;
        }

        void OnDisable()
        {
            if (IsBlocking) SetBlocking(false);
        }

        // ---------------- Texte ----------------

        public override string GetName(AbilityId id)
        {
            foreach (var a in Abilities)
                if (a != null && a.Id == id && !string.IsNullOrEmpty(a.Name)) return a.Name;
            switch (id)
            {
                case AbilityId.SwordSlash: return "Schwerthieb";
                case AbilityId.ShieldBlock: return "Schildblock";
                case AbilityId.FlameWhirl: return "Flammenwirbel";
                case AbilityId.FrostStrike: return "Frostschlag";
                case AbilityId.Earthquake: return "Erdbeben";
                case AbilityId.LightOath: return "Lichtschwur";
                default: return base.GetName(id);
            }
        }

        public override string Describe(AbilityId id, float dm)
        {
            switch (id)
            {
                case AbilityId.SwordSlash:
                    return $"Schwungvoller Hieb im {Hi(SlashArc)}°-Bogen vor dir ({Hi(SlashRange)} m), trifft alle Gegner darin. 3er-Kombo: zwei Hiebe mit {Hi(SlashDamage * dm)} Schaden, der dritte mit {Hi(FinisherDamage * dm)} Schaden und {Hi(FinisherKnockback)} m Rückstoß. Gedrückt halten schlägt weiter.";
                case AbilityId.ShieldBlock:
                    return $"Halten: Du hebst den Schild und blockst {Hi(BlockReduction * 100f)} % des Schadens von vorne ({Hi(BlockAngle)}°). Jeder geblockte Lebenspunkt kostet {Hi(ManaPerBlockedDamage)} Mana – ist das Mana leer, bricht der Block ({Hi(BlockBreakCooldown)} s Sperre). Beim Blocken läufst du mit {Hi(BlockMoveMultiplier * 100f)} % Tempo und kannst nicht zuschlagen.";
                case AbilityId.FlameWhirl:
                {
                    var s = FlameWhirl;
                    return $"Feuriger Drehschlag um dich herum ({Hi(s.Radius)} m). Er verursacht {Hi(s.Damage * dm)} Schaden, stößt Gegner leicht zurück und setzt sie in Brand: {Hi(s.BurnDps * dm)} Schaden pro Sekunde für {Hi(s.BurnDuration)} s.";
                }
                case AbilityId.FrostStrike:
                {
                    var s = FrostStrike;
                    return $"Ein Schwerthieb in den Boden schickt eine eisige Schockwelle nach vorne ({Hi(s.ConeAngle)}°, {Hi(s.Range)} m). Sie verursacht {Hi(s.Damage * dm)} Schaden und friert Gegner {Hi(s.FreezeDuration)} s komplett ein.";
                }
                case AbilityId.Earthquake:
                {
                    var s = Earthquake;
                    return $"Sprung bis zu {Hi(s.LeapDistance)} m Richtung Mauszeiger, beim Aufprall bebt die Erde ({Hi(s.Radius)} m): {Hi(s.Damage * dm)} Schaden, {Hi(s.Knockback)} m Rückstoß und {Hi(s.StunDuration)} s Betäubung.";
                }
                case AbilityId.LightOath:
                {
                    var s = LightOath;
                    return $"Dein Schild erstrahlt: Alle Gegner im Umkreis von {Hi(s.Radius)} m greifen {Hi(s.TauntDuration)} s lang nur dich an, während du {Hi(s.DamageReduction * 100f)} % weniger Schaden nimmst. Heilt dich um {Hi(s.PlayerHeal)} und Buddies in der Nähe um {Hi(s.BuddyHeal)} LP.";
                }
                default:
                    return "";
            }
        }
    }
}
