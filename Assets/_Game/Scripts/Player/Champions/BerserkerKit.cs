using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Berserker: Axthieb-Kombo (LMB), Ansturm (RMB), Glutsturm / Eisbrecher / Bergsturz / Lichtrausch (R/F/C/V).
    // Ressource Wut statt Mana (ResourceProfile in PlayerMana): entsteht auf dem Server durch Treffer und erlittenen
    // Schaden und geht per Kit-Ereignis (SendKitEvent → KitEventRpc → OnNetEvent) an den Besitzer, der sie verbucht.
    // Überleben: +25 Max-LP, Lebensraub im Lichtrausch, 40 % Schadensreduktion im Ansturm – keine Unverwundbarkeit.
    public class BerserkerKit : ChampionKit
    {
        [Header("Wut (Ressource statt Mana)")]
        public ResourceProfile Rage = new ResourceProfile
        {
            Name = "Wut", Color = new Color(0.66f, 0.2f, 0.16f), Cap = 100f, StartValue = 0f, WaveStartMinimum = 25f,
            RegenIn = 0f, RegenOut = 0f, DecayDelay = 4f, DecayRate = 8f, CapBonusScale = 0.5f,
        };
        [Tooltip("Wut pro getroffenem Gegner beim Axthieb.")]
        public float RagePerHit = 3f;
        [Tooltip("Höchstens so viele Gegner zählen pro Hieb für die Wut.")]
        public int MaxRageHitsPerSwing = 3;
        [Tooltip("Wut pro getroffenem Gegner beim Ansturm.")]
        public float RagePerChargeHit = 3f;
        [Tooltip("Wut pro erlittenem Lebenspunkt (nach Reduktionen).")]
        public float RagePerDamageTaken = 0.5f;
        [Tooltip("Ab dieser Wut: Raserei (Axthieb schneller, Axt glüht).")]
        public float FrenzyThreshold = 75f;
        [Tooltip("Raserei: Axthieb-Tempo +x (0.15 = +15 %).")]
        public float FrenzySpeedBonus = 0.15f;
        [Tooltip("Glut-Partikel am Spieler während der Raserei (Schleife, optional).")]
        public GameObject RageGlowPrefab;

        [Header("Leben")]
        public float HealthBonus = 25f;

        [Header("Axthieb (LMB, 3er-Kombo)")]
        public float CleaveDamage = 26f;
        public float FinisherDamage = 48f;
        public float CleaveRange = 2.9f;
        [Tooltip("Volle Öffnung des Schlagbogens in Grad.")]
        public float CleaveArc = 150f;
        [Tooltip("Abklingzeit zwischen Hieb 1→2→3.")]
        public float CleaveInterval = 0.6f;
        [Tooltip("Abklingzeit nach dem dritten Hieb.")]
        public float FinisherRecovery = 1.0f;
        public float ComboWindow = 1.2f;
        public float FinisherKnockback = 1.5f;
        [Tooltip("Ausholen bis zum Treffer (Hieb 1/2).")]
        public float CleaveHitDelay = 0.18f;
        [Tooltip("Ausholen bis zum Treffer beim Überkopf-Spalter.")]
        public float FinisherHitDelay = 0.32f;
        public float HitHeight = 1.0f;
        [Tooltip("SlashArcFx-Prefab (Axt-Bogen).")]
        public GameObject SlashFxPrefab;
        public GameObject HitFxPrefab;
        [ColorUsage(true, true)] public Color CleaveColor = new Color(1.9f, 0.6f, 0.3f, 1f);
        [ColorUsage(true, true)] public Color FinisherColor = new Color(2.2f, 0.35f, 0.15f, 1f);

        [Header("Ansturm (RMB)")]
        public float ChargeDistance = 6f;
        public float ChargeDuration = 0.3f;
        [Tooltip("Breite der Bahn, auf der Gegner getroffen werden.")]
        public float ChargeWidth = 1.2f;
        public float ChargeDamage = 15f;
        public float ChargeKnockback = 1f;
        [Range(0f, 0.9f)] public float ChargeDamageReduction = 0.4f;
        public float ChargeCooldown = 6f;
        public int ChargeCharges = 1;
        public float ChargeLockout = 0.4f;
        public GameObject ChargeDustPrefab;
        public GameObject ChargeImpactPrefab;

        [Header("Element-Fähigkeiten (freischaltbar)")]
        public EmberStormSpell EmberStorm = new EmberStormSpell(); // R
        public IceBreakerSpell IceBreaker = new IceBreakerSpell(); // F
        public LandslideSpell Landslide = new LandslideSpell();    // C
        public LightFrenzySpell LightFrenzy = new LightFrenzySpell(); // V

        [Header("Sounds")]
        public ChampionSfx SwingSfx = new ChampionSfx();
        public ChampionSfx FinisherSfx = new ChampionSfx();
        public ChampionSfx ChargeSfx = new ChampionSfx();
        public ChampionSfx ChargeImpactSfx = new ChampionSfx();

        // Zustand (öffentlich lesbar für UI/Tests)
        public int ComboStep { get; private set; }
        public bool IsCharging { get; private set; }
        public bool IsLeaping { get; private set; }
        public bool IsSpinning => Time.time < _spinUntil;
        public bool LightFrenzyActive => Time.time < _frenzyUntil;
        public float CurrentRage => Owner != null && Owner.Mana != null ? Owner.Mana.CurrentMana : 0f;
        public bool InRaserei => CurrentRage >= FrenzyThreshold;
        public float LifestealHealedThisCast => _lifestealHealed;

        private float _comboExpires;
        private Coroutine _charge, _leap, _spin;
        private float _spinUntil;
        private float _spinMoveMultiplier = 1f;
        private float _frenzyUntil, _lifestealPct, _lifestealCap, _lifestealHealed, _frenzySpeedBonus;
        private GameObject _frenzyAura, _rageGlow;
        private bool _rageWasFull = true; // erst nach einem Wert unter voll erneut melden (kein Ton beim Aktivieren)
        private float _lastMarkSent = -999f;

        public override ChampionClass Class => ChampionClass.Berserker;
        public override ResourceProfile Resource => Rage;
        public override float MaxHealthBonus => HealthBonus;

        // ---------------- Effektive Werte (inkl. Händlerkarten) ----------------

        public float CleaveRangeEff => Mod(AbilityId.AxeCleave, AbilityStat.Range, CleaveRange);
        public float CleaveArcEff => Mathf.Min(360f, Mod(AbilityId.AxeCleave, AbilityStat.Angle, CleaveArc));
        // Tempo: Karten × Raserei × Lichtrausch
        public float CleaveSpeedFactor => ModFactor(AbilityId.AxeCleave, AbilityStat.Speed)
            * (InRaserei ? 1f + FrenzySpeedBonus : 1f) * (LightFrenzyActive ? 1f + _frenzySpeedBonus : 1f);
        public float CleaveIntervalEff => CleaveInterval / CleaveSpeedFactor;
        public float FinisherRecoveryEff => FinisherRecovery / CleaveSpeedFactor;
        public float ChargeDistanceEff => Mod(AbilityId.BerserkCharge, AbilityStat.Range, ChargeDistance) * (Owner != null ? Owner.MobilityMultiplier : 1f);
        public int ChargeChargesEff => Mathf.Max(1, Owner != null ? Owner.Mods.ApplyInt(AbilityId.BerserkCharge, AbilityStat.Charges, ChargeCharges) : ChargeCharges);

        public override AbilityId GetAbility(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Primary: return AbilityId.AxeCleave;
                case AbilitySlot.Secondary: return AbilityId.BerserkCharge;
                case AbilitySlot.Fire: return AbilityId.EmberStorm;
                case AbilitySlot.Ice: return AbilityId.IceBreaker;
                case AbilitySlot.Earth: return AbilityId.Landslide;
                default: return AbilityId.LightFrenzy;
            }
        }

        public override ElementSpell GetSpell(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.EmberStorm: return EmberStorm;
                case AbilityId.IceBreaker: return IceBreaker;
                case AbilityId.Landslide: return Landslide;
                case AbilityId.LightFrenzy: return LightFrenzy;
                default: return null;
            }
        }

        public override float GetCooldown(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.AxeCleave: return CleaveIntervalEff;
                case AbilityId.BerserkCharge: return ChargeCooldown;
                default: { var s = GetSpell(id); return s != null ? s.Cooldown : 0f; }
            }
        }

        // Wutkosten (Händlerkarten „Wutkosten“ = AbilityStat.ManaCost)
        public override float GetManaCost(AbilityId id)
        {
            var s = GetSpell(id);
            return s != null ? Mod(id, AbilityStat.ManaCost, s.ManaCost) : 0f;
        }

        public override int GetMaxCharges(AbilityId id) => id == AbilityId.BerserkCharge ? ChargeChargesEff : 1;
        public override float GetChargeLockout(AbilityId id) => id == AbilityId.BerserkCharge ? ChargeLockout : 0.3f;
        public override bool AutoRepeatPrimary => true;
        public override bool IsActive(AbilityId id) =>
            (id == AbilityId.BerserkCharge && IsCharging) || (id == AbilityId.EmberStorm && IsSpinning) || (id == AbilityId.LightFrenzy && LightFrenzyActive);
        public override float MoveSpeedMultiplier => IsSpinning ? _spinMoveMultiplier : 1f;

        public override bool CanCast(AbilityId id)
        {
            if (IsCharging || IsLeaping) return false;
            // Im Glutsturm keine Hiebe/Sprünge (Lichtrausch geht)
            if (IsSpinning && id != AbilityId.LightFrenzy) return false;
            return true;
        }

        public override string GetCostLine(AbilityId id)
        {
            if (id == AbilityId.AxeCleave)
                return $"{CostColor}+{Fmt(RagePerHit)} Wut pro Treffer</color>   •   {Fmt(Owner != null ? Owner.GetCooldownDuration(id) : CleaveIntervalEff)} s zwischen Hieben";
            if (id == AbilityId.BerserkCharge)
                return $"{CostColor}+{Fmt(RagePerChargeHit)} Wut pro Treffer</color>   •   {ChargeChargesEff} × je {Fmt(Owner != null ? Owner.GetCooldownDuration(id) : ChargeCooldown)} s";
            return base.GetCostLine(id);
        }

        // ---------------- Ausführen ----------------

        public override void Cast(AbilityId id, SpellCastContext ctx)
        {
            switch (id)
            {
                case AbilityId.AxeCleave:
                    DoCleave(ctx);
                    break;
                case AbilityId.BerserkCharge:
                    DoCharge(ctx);
                    break;
                default:
                    var spell = GetModdedSpell(id);
                    if (spell != null) spell.Cast(ctx);
                    break;
            }
        }

        // Kombo-Schritt für andere Rechner
        public override int GetCastVariant(AbilityId id, SpellCastContext ctx) => id == AbilityId.AxeCleave ? ComboStep : 0;

        // Nächster Kombo-Schritt (1..3): im Fenster weiter, sonst neu
        public static int NextComboStep(int step, bool inWindow) => inWindow && step >= 1 && step < 3 ? step + 1 : 1;

        private void DoCleave(SpellCastContext ctx)
        {
            int step = ctx.IsRemote && ctx.Variant >= 1 && ctx.Variant <= 3
                ? ctx.Variant
                : NextComboStep(ComboStep, Time.time <= _comboExpires);
            ComboStep = step;
            bool finisher = step == 3;
            _comboExpires = Time.time + ComboWindow;
            if (finisher && Owner != null) Owner.StartCooldown(AbilityId.AxeCleave, FinisherRecoveryEff * Owner.GetCooldownFactor(AbilityId.AxeCleave));
            float delay = finisher ? FinisherHitDelay : CleaveHitDelay;
            if (delay > 0f && isActiveAndEnabled) StartCoroutine(CleaveAfter(delay, step, ctx));
            else CleaveHit(step, ctx);
        }

        private IEnumerator CleaveAfter(float delay, int step, SpellCastContext ctx)
        {
            yield return new WaitForSeconds(delay);
            if (Owner == null || Owner.ActiveKit != this || IsCharging || IsLeaping) yield break;
            CleaveHit(step, ctx);
        }

        private void CleaveHit(int step, SpellCastContext ctx)
        {
            bool finisher = step == 3;
            float range = CleaveRangeEff;
            float arcDeg = CleaveArcEff;
            float damage = (finisher ? FinisherDamage : CleaveDamage) * ctx.DamageMultiplier;
            Vector3 origin = transform.position;
            Vector3 dir = ctx.AimDirection;

            int hits = 0;
            foreach (var enemy in CombatUtil.FindEnemiesInCone(origin, dir, range + 0.4f, arcDeg))
            {
                if (enemy == null) continue;
                if (CombatUtil.HorizontalDistance(origin, enemy.transform.position) > range + CombatUtil.EnemyRadius(enemy)) continue;
                // Wucht-Treffer (HitVfx-Tabelle, sonst HitFxPrefab) auf allen Rechnern
                HitVfx.Show(enemy, AbilityId.AxeCleave, dir, finisher ? 1.25f : 1f, HitFxPrefab);
                if (!Net.IsServer) continue; // Treffer entscheidet der Server
                if (finisher && FinisherKnockback > 0f)
                    using (EnemyBrain.PlayerSource()) enemy.Knockback(CombatUtil.FlatDirection(origin, enemy.transform.position, dir), FinisherKnockback, 0.25f);
                DealDamage(enemy, damage, AbilityId.AxeCleave, hitFx: false);
                hits++;
            }
            if (hits > 0) ServerGrantRage(RageForHits(hits, RagePerHit, MaxRageHitsPerSwing));

            // Bogen: 1 diagonal, 2 Rückhand, 3 Überkopf (breiter, dunkler rot)
            Vector3 fxPos = Ground(origin) + Vector3.up * HitHeight;
            var arc = SlashArcFx.Spawn(SlashFxPrefab, fxPos, dir, range, finisher ? Mathf.Min(360f, arcDeg + 20f) : arcDeg,
                finisher ? FinisherColor : CleaveColor, step != 1, step == 1 ? 18f : (step == 2 ? -8f : 0f));
            if (arc != null) arc.Width = finisher ? 1.4f : 1.1f;
            (finisher ? FinisherSfx : SwingSfx).Play(origin);
        }

        // ---------------- Ansturm ----------------

        private void DoCharge(SpellCastContext ctx)
        {
            Vector3 dir = ctx.AimDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            dir.Normalize();
            float dist = ChargeDistanceEff;
            // Steinwall hält den Ansturm auf (wie Blink); Wände/Gebäude stoppt der CharacterController
            Vector3 from = ctx.Origin + Vector3.up;
            int mask = StoneWall.BlockMask;
            if (mask != 0 && Physics.Raycast(from, dir, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0f, hit.distance - 0.6f);
            if (_charge != null) StopCoroutine(_charge);
            _charge = StartCoroutine(ChargeRoutine(ctx, dir, dist, Mathf.Max(0.05f, ChargeDuration)));
        }

        private IEnumerator ChargeRoutine(SpellCastContext ctx, Vector3 dir, float distance, float duration)
        {
            IsCharging = true;
            ComboStep = 0;
            if (Controller != null) { Controller.MovementLocked = true; Controller.RotationLocked = true; }
            if (IsLocalControl) transform.rotation = Quaternion.LookRotation(dir);
            if (ChargeDustPrefab != null) CombatUtil.SpawnFx(ChargeDustPrefab, Ground(transform.position), Quaternion.LookRotation(dir), 2f);
            ChargeSfx.Play(transform.position);

            // Bahn: vom Besitzer festgelegt (Origin + Richtung), auf dem Server unabhängig von der nachhinkenden Position
            var hitSet = new HashSet<EnemyBrain>();
            float damage = ChargeDamage * ctx.DamageMultiplier;
            // Durch die Gegner hindurch sprinten (sie werden zur Seite gestoßen) – sonst stoppt der erste Gegner auf der Bahn
            // den CharacterController nach ~1 m; Steinwall/Gelände blocken weiter
            SetPassThroughEnemies(true);
            float t = 0f, done = 0f;
            while (t < duration)
            {
                float dt = Mathf.Min(Time.deltaTime, duration - t);
                t += dt;
                float k = t / duration;
                if (IsLocalControl && Character != null && Character.enabled) Character.Move(dir * (distance / duration) * dt);
                if (Net.IsServer) ChargeHits(ctx.Origin, dir, distance * done, distance * k, damage, hitSet);
                done = k;
                yield return null;
            }
            EndCharge(dir);
        }

        private void ChargeHits(Vector3 origin, Vector3 dir, float fromDist, float toDist, float damage, HashSet<EnemyBrain> hitSet)
        {
            int hits = 0;
            Vector3 start = origin + dir * fromDist;
            foreach (var enemy in CombatUtil.FindEnemiesOnLine(start, dir, Mathf.Max(0.3f, toDist - fromDist) + 0.6f, ChargeWidth * 0.5f))
            {
                if (enemy == null || !hitSet.Add(enemy)) continue;
                // Rückstoß zur Seite (die Seite, auf der der Gegner steht)
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                if (Vector3.Dot(enemy.transform.position - origin, side) < 0f) side = -side;
                if (ChargeKnockback > 0f) using (EnemyBrain.PlayerSource()) enemy.Knockback(side, ChargeKnockback, 0.2f);
                DealDamage(enemy, damage, AbilityId.BerserkCharge); // Treffer-Effekt über den Server (HitVfx, auch auf Clients)
                hits++;
            }
            if (hits > 0)
            {
                ServerGrantRage(hits * RagePerChargeHit);
                ChargeImpactSfx.Play(start);
            }
        }

        private int _excludeBefore = -1;

        private void SetPassThroughEnemies(bool on)
        {
            if (Character == null) return;
            int enemy = LayerMask.NameToLayer("Enemy");
            if (enemy < 0) return;
            if (on)
            {
                if (_excludeBefore < 0) _excludeBefore = Character.excludeLayers.value;
                Character.excludeLayers = _excludeBefore | (1 << enemy);
            }
            else if (_excludeBefore >= 0)
            {
                Character.excludeLayers = _excludeBefore;
                _excludeBefore = -1;
            }
        }

        private void EndCharge(Vector3 dir)
        {
            SetPassThroughEnemies(false);
            IsCharging = false;
            _charge = null;
            if (Controller != null) { Controller.MovementLocked = false; Controller.RotationLocked = false; }
            if (ChargeImpactPrefab != null) CombatUtil.SpawnFx(ChargeImpactPrefab, Ground(transform.position), Quaternion.LookRotation(dir), 2f);
        }

        // ---------------- Bergsturz-Sprung ----------------

        // Sprung über die Strecke offset (CharacterController), Modell hebt in einem Bogen ab
        public void Leap(Vector3 offset, float duration, float height, System.Action onLand)
        {
            if (_leap != null) StopCoroutine(_leap);
            _leap = StartCoroutine(LeapRoutine(offset, Mathf.Max(0.05f, duration), height, onLand));
        }

        private IEnumerator LeapRoutine(Vector3 offset, float duration, float height, System.Action onLand)
        {
            IsLeaping = true;
            if (Controller != null) { Controller.MovementLocked = true; Controller.RotationLocked = true; }
            if (IsLocalControl && offset.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(new Vector3(offset.x, 0f, offset.z));
            Transform visual = Owner != null && Owner.ActiveVisual != null ? Owner.ActiveVisual.transform : null;
            Vector3 visualBase = visual != null ? visual.localPosition : Vector3.zero;
            float t = 0f, done = 0f;
            while (t < duration)
            {
                t = Mathf.Min(duration, t + Time.deltaTime);
                float k = t / duration;
                float step = k - done;
                done = k;
                if (IsLocalControl && Character != null && Character.enabled) Character.Move(offset * step);
                if (visual != null) visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(k * Mathf.PI) * height);
                yield return null;
            }
            if (visual != null) visual.localPosition = visualBase;
            if (Controller != null) { Controller.MovementLocked = false; Controller.RotationLocked = false; }
            IsLeaping = false;
            _leap = null;
            if (onLand != null) onLand();
        }

        // ---------------- Glutsturm (Kanal) ----------------

        // Wirbel für duration Sekunden; onTick alle interval Sekunden (auf jedem Rechner – Schaden prüft der Zauber selbst)
        public void BeginSpin(float duration, float interval, float moveMultiplier, GameObject loopFx, System.Action<Vector3> onTick)
        {
            if (_spin != null) StopCoroutine(_spin);
            _spin = StartCoroutine(SpinRoutine(duration, Mathf.Max(0.1f, interval), moveMultiplier, loopFx, onTick));
        }

        private IEnumerator SpinRoutine(float duration, float interval, float moveMultiplier, GameObject loopFx, System.Action<Vector3> onTick)
        {
            _spinUntil = Time.time + duration;
            _spinMoveMultiplier = moveMultiplier;
            ComboStep = 0;
            GameObject fx = null;
            if (loopFx != null)
            {
                fx = Instantiate(loopFx, transform);
                fx.transform.localPosition = Vector3.zero;
                fx.transform.localRotation = Quaternion.identity;
                Destroy(fx, duration + 0.5f);
            }
            float next = 0f;
            while (Time.time < _spinUntil)
            {
                if (Time.time >= next)
                {
                    next = Time.time + interval;
                    if (onTick != null) onTick(Ground(transform.position));
                }
                yield return null;
            }
            StopLoopFx(fx);
            _spin = null;
        }

        private static void StopLoopFx(GameObject fx)
        {
            if (fx == null) return;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // ---------------- Lichtrausch ----------------

        public void BeginFrenzy(float duration, float lifestealPct, float lifestealCap, float speedBonus, GameObject auraPrefab)
        {
            _frenzyUntil = Time.time + duration;
            _lifestealPct = lifestealPct;
            _lifestealCap = lifestealCap;
            _lifestealHealed = 0f;
            _frenzySpeedBonus = speedBonus;
            if (auraPrefab != null)
            {
                if (_frenzyAura != null) Destroy(_frenzyAura);
                _frenzyAura = Instantiate(auraPrefab, transform);
                _frenzyAura.transform.localPosition = Vector3.zero;
                Destroy(_frenzyAura, duration);
            }
        }

        // ---------------- Server: Schaden, Lebensraub, Wut ----------------

        // Schaden an einem Gegner (nur Server) inkl. Lebensraub. Rückgabe = tatsächlich verursachter Schaden.
        // ability/hitFx: Treffer-Effekt je Fähigkeit (EnemyBrain.DealPlayerDamage)
        public float DealDamage(EnemyBrain enemy, float amount, AbilityId ability, bool hitFx = true)
        {
            if (!Net.IsServer || enemy == null || amount <= 0f) return 0f;
            float before = enemy.CurrentHP;
            EnemyBrain.DealPlayerDamage(enemy, amount, ability, hitFx);
            float dealt = Mathf.Max(0f, before - Mathf.Max(0f, enemy.CurrentHP));
            if (LightFrenzyActive && Stats != null && !Stats.IsDead)
            {
                float heal = LifestealHeal(dealt, _lifestealPct, _lifestealHealed, _lifestealCap);
                heal = Mathf.Min(heal, Mathf.Max(0f, Stats.MaxHP - Stats.CurrentHP)); // volle LP verbrauchen das Limit nicht
                if (heal > 0f)
                {
                    Stats.Heal(heal);
                    _lifestealHealed += heal;
                }
            }
            return dealt;
        }

        // Wut für den Besitzer (nur Server). amount 0 = nur Kampf markieren (Verfall aufschieben, gedrosselt).
        public void ServerGrantRage(float amount)
        {
            if (!Net.IsServer) return;
            var mana = Owner != null ? Owner.Mana : null;
            if (IsLocalControl)
            {
                if (mana != null) mana.Gain(amount);
                return;
            }
            if (amount <= 0f && Time.time - _lastMarkSent < 1f) return;
            _lastMarkSent = Time.time;
            // Gespiegelten Wert sofort mitziehen (verhindert doppelte Kosten-Prüfungen nicht – nur Anzeige)
            if (mana != null) mana.MarkCombat();
            SendKitEvent(NetEvtRage, new Vector3(amount, 0f, 0f));
        }

        public const int NetEvtRage = 1;

        public override void OnNetEvent(int evt, Vector3 point)
        {
            // Nur der Besitzer verbucht die Wut (andere bekommen den Wert über PlayerMana/PlayerAvatar)
            if (evt == NetEvtRage && IsLocalControl && Owner != null && Owner.Mana != null) Owner.Mana.Gain(point.x);
        }

        // Läuft nur auf dem Server (PlayerStats.TakeDamage): Ansturm-Reduktion, danach Wut aus erlittenem Schaden
        public override float ModifyIncomingDamage(float amount, Vector3 sourcePosition, bool hasSource)
        {
            if (IsCharging) amount *= 1f - ChargeDamageReduction;
            if (amount > 0f) ServerGrantRage(RageFromDamageTaken(amount, RagePerDamageTaken));
            return amount;
        }

        // ---------------- Rechen-Helfer (statisch, testbar) ----------------

        public static float RageForHits(int hits, float perHit, int maxHits) => Mathf.Min(Mathf.Max(0, hits), Mathf.Max(0, maxHits)) * perHit;
        public static float RageFromDamageTaken(float damage, float perHp) => Mathf.Max(0f, damage) * perHp;
        public static float LifestealHeal(float dealt, float pct, float healedSoFar, float cap) =>
            Mathf.Clamp(dealt * pct, 0f, Mathf.Max(0f, cap - healedSoFar));

        // ---------------- Laufender Zustand ----------------

        void Update()
        {
            // Raserei-Glut (alle Rechner, Wut-Wert gespiegelt)
            bool glow = InRaserei && isActiveAndEnabled;
            if (glow && _rageGlow == null && RageGlowPrefab != null)
            {
                _rageGlow = Instantiate(RageGlowPrefab, transform);
                _rageGlow.transform.localPosition = Vector3.zero;
            }
            if (_rageGlow != null && _rageGlow.activeSelf != glow) _rageGlow.SetActive(glow);

            // Volle Wut: kurzer Puls nur für den eigenen Spieler (Hinweis „jetzt ausgeben“)
            if (IsLocalControl && Owner != null && Owner.Mana != null)
            {
                bool full = Owner.Mana.MaxMana > 0f && CurrentRage >= Owner.Mana.MaxMana - 0.01f;
                if (full && !_rageWasFull) GameAudio.Play(SfxId.RageFull, transform.position);
                _rageWasFull = full;
            }
        }

        // ---------------- Animation ----------------

        public override string GetAnimTrigger(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.AxeCleave: return "Cleave" + Mathf.Clamp(ComboStep, 1, 3);
                case AbilityId.BerserkCharge: return "Charge";
                case AbilityId.IceBreaker: return "IceBreak";
                case AbilityId.Landslide: return "LeapSlam";
                case AbilityId.LightFrenzy: return "Roar";
                default: return null; // Glutsturm läuft über das Bool "Spinning"
            }
        }

        public override void UpdateAnimator(Animator animator, ChampionVisual visual)
        {
            PlayerVisualAnimator.SetBoolSafe(animator, "Spinning", IsSpinning);
        }

        public override void OnDeactivated()
        {
            if (_charge != null) StopCoroutine(_charge);
            if (_leap != null) StopCoroutine(_leap);
            if (_spin != null) StopCoroutine(_spin);
            _charge = _leap = _spin = null;
            SetPassThroughEnemies(false);
            if (IsCharging || IsLeaping)
            {
                IsCharging = false;
                IsLeaping = false;
                if (Controller != null) { Controller.MovementLocked = false; Controller.RotationLocked = false; }
            }
            _spinUntil = 0f;
            _frenzyUntil = 0f;
            if (_frenzyAura != null) Destroy(_frenzyAura);
            if (_rageGlow != null) Destroy(_rageGlow);
        }

        void OnDisable()
        {
            if (_rageGlow != null) Destroy(_rageGlow);
        }

        // ---------------- Texte ----------------

        public override string GetName(AbilityId id)
        {
            foreach (var a in Abilities)
                if (a != null && a.Id == id && !string.IsNullOrEmpty(a.Name)) return a.Name;
            switch (id)
            {
                case AbilityId.AxeCleave: return "Axthieb";
                case AbilityId.BerserkCharge: return "Ansturm";
                case AbilityId.EmberStorm: return "Glutsturm";
                case AbilityId.IceBreaker: return "Eisbrecher";
                case AbilityId.Landslide: return "Bergsturz";
                case AbilityId.LightFrenzy: return "Lichtrausch";
                default: return base.GetName(id);
            }
        }

        public override bool TryGetStatValue(AbilityId id, AbilityStat stat, out float value, out string unit, out string label)
        {
            value = 0f;
            unit = "";
            label = "";
            if (id == AbilityId.AxeCleave)
            {
                switch (stat)
                {
                    case AbilityStat.Damage: value = CleaveDamage * DamageMult(id); label = "Schaden"; return true;
                    case AbilityStat.Range: value = CleaveRangeEff; unit = " m"; label = "Reichweite"; return true;
                    case AbilityStat.Angle: value = CleaveArcEff; unit = "°"; label = "Schlagbogen"; return true;
                    case AbilityStat.Speed: value = Owner != null ? Owner.GetCooldownDuration(id) : CleaveIntervalEff; unit = " s"; label = "Zeit zwischen Hieben"; return true;
                }
            }
            else if (id == AbilityId.BerserkCharge)
            {
                switch (stat)
                {
                    case AbilityStat.Damage: value = ChargeDamage * DamageMult(id); label = "Schaden"; return true;
                    case AbilityStat.Range: value = ChargeDistanceEff; unit = " m"; label = "Distanz"; return true;
                    case AbilityStat.Charges: value = ChargeChargesEff; label = "Aufladungen"; return true;
                    case AbilityStat.Cooldown: value = Owner != null ? Owner.GetCooldownDuration(id) : ChargeCooldown; unit = " s"; label = "Abklingzeit"; return true;
                }
            }
            else if (stat == AbilityStat.ManaCost && GetSpell(id) != null)
            {
                value = GetManaCost(id);
                unit = " Wut";
                label = "Wutkosten";
                return true;
            }
            return base.TryGetStatValue(id, stat, out value, out unit, out label);
        }

        public override string Describe(AbilityId id, float dm)
        {
            switch (id)
            {
                case AbilityId.AxeCleave:
                    return $"Wuchtiger Axthieb im {Hi(CleaveArcEff)}°-Bogen vor dir ({Hi(CleaveRangeEff)} m), trifft alle Gegner darin. 3er-Kombo: zwei Hiebe mit {Hi(CleaveDamage * dm)} Schaden, der Überkopf-Spalter mit {Hi(FinisherDamage * dm)} Schaden und {Hi(FinisherKnockback)} m Rückstoß. Jeder Treffer gibt {Hi(RagePerHit)} Wut (bis {Hi(MaxRageHitsPerSwing)} Gegner pro Hieb). Ab {Hi(FrenzyThreshold)} Wut: Raserei, Hiebe {Hi(FrenzySpeedBonus * 100f)} % schneller.";
                case AbilityId.BerserkCharge:
                    return $"Stürmt {Hi(ChargeDistanceEff)} m Richtung Mauszeiger. Gegner auf der Bahn erleiden {Hi(ChargeDamage * dm)} Schaden, werden zur Seite gestoßen und geben je {Hi(RagePerChargeHit)} Wut. Während des Ansturms nimmst du {Hi(ChargeDamageReduction * 100f)} % weniger Schaden. Steinwälle halten dich auf.";
                case AbilityId.EmberStorm:
                {
                    var s = Modded(EmberStorm, id);
                    return $"Du wirbelst {Hi(s.Duration)} s lang mit glühender Axt umher (Tempo {Hi(s.MoveMultiplier * 100f)} %). Alle {Hi(s.TickInterval)} s erleiden Gegner im Umkreis von {Hi(s.Radius)} m {Hi(s.TickDamage * dm)} Schaden und brennen mit {Hi(s.BurnDps * dm)} Schaden pro Sekunde.";
                }
                case AbilityId.IceBreaker:
                {
                    var s = Modded(IceBreaker, id);
                    return $"Die Axt schlägt in den Boden, eine Linie aus Eisstacheln bricht {Hi(s.Length)} m nach vorne auf ({Hi(s.Width)} m breit): {Hi(s.Damage * dm)} Schaden und {Hi(s.FreezeDuration)} s eingefroren. Zerschmettern: schon eingefrorene oder verlangsamte Gegner nehmen {Hi(s.ShatterMultiplier * 100f)} % Schaden.";
                }
                case AbilityId.Landslide:
                {
                    var s = Modded(Landslide, id);
                    return $"Weitsprung bis zu {Hi(s.LeapDistance)} m zum Mauszeiger. Bei der Landung ({Hi(s.Radius)} m): {Hi(s.Damage * dm)} Schaden, {Hi(s.StunDuration)} s Betäubung und {Hi(s.Knockback)} m Rückstoß.";
                }
                case AbilityId.LightFrenzy:
                {
                    var s = Modded(LightFrenzy, id);
                    return $"Kampfschrei: {Hi(s.Duration)} s lang heilt dich {Hi(s.Lifesteal * 100f)} % deines Schadens (höchstens {Hi(s.LifestealCap)} LP), Axthiebe sind {Hi(s.AttackSpeedBonus * 100f)} % schneller. Buddies im Umkreis von {Hi(s.BuddyRadius)} m verursachen {Hi(s.BuddyDamageBonus * 100f)} % mehr Schaden, Gegner im Umkreis von {Hi(s.BlindRadius)} m sind {Hi(s.BlindDuration)} s geblendet.";
                }
                default:
                    return "";
            }
        }
    }
}
