using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Schamane (Support): Geisterblitz mit Kettensprung (LMB), Windschritt (RMB), Kriegstotem / Heilende Woge /
    // Steinhaut-Totem / Ahnengeist (R/F/C/V), Passiv „Geistverbindung“ (Buddies in der Nähe feuern schneller).
    // Mehrspieler: Cast auf allen Rechnern; Schaden/Heilung/Schild/Spieler-Buffs nur auf dem Server (Spieler-Buffs per
    // PlayerAbilities.ServerGrantBuff → PlayerAvatar.BuffRpc an den Besitzer), Bewegung nur bei IsLocalControl.
    public class ShamanKit : ChampionKit
    {
        [Header("Abschuss")]
        [Tooltip("Abschusspunkt des Geisterblitzes (Kind des Player-Roots, z. B. am Stab-Stein). Leer = MuzzleOffset.")]
        public Transform ProjectileSpawn;
        public Vector3 MuzzleOffset = new Vector3(0.15f, 1.3f, 0.7f);

        [Header("Geisterblitz (LMB)")]
        [Tooltip("Geschoss-Optik (VFX_SpiritBolt, optional mit ArrowProjectile). Leer = türkise Platzhalter-Kugel.")]
        public GameObject SpiritBoltPrefab;
        public float BoltDamage = 24f;
        public float BoltSpeed = 28f;
        public float BoltRange = 20f;
        public float BoltCooldown = 0.6f;
        [Tooltip("Sprünge auf weitere Gegner beim Treffer (Händlerkarte „+1 Sprung“ = AbilityStat.Pierce).")]
        public int ChainJumps = 2;
        public float ChainRadius = 6f;
        [Tooltip("Schaden je Sprung als Anteil des Blitz-Schadens.")]
        [Range(0f, 1f)] public float ChainDamageFactor = 0.6f;
        [Tooltip("Blitzbogen zum nächsten Ziel (VFX_SpiritChain, am besten mit BeamFx/LineRenderer).")]
        public GameObject ChainFxPrefab;
        public float ChainFxLifetime = 0.5f;
        [Tooltip("Treffer-Effekt, falls die HitVfx-Tabelle keinen Eintrag hat (optional).")]
        public GameObject BoltHitFxPrefab;
        [Tooltip("Kurzer Blitz am Abschusspunkt (optional).")]
        public GameObject MuzzleFxPrefab;

        [Header("Windschritt (RMB)")]
        public float WindStepDistance = 5f;
        public float WindStepDuration = 0.25f;
        public float WindStepCooldown = 7f;
        public int WindStepCharges = 1;
        public float WindStepLockout = 0.3f;
        [Tooltip("Lauftempo +x danach (0.3 = +30 %) für dich und Spieler im Umkreis.")]
        public float HasteBonus = 0.3f;
        public float HasteDuration = 3f;
        public float HasteRadius = 6f;
        [Tooltip("Windspur (VFX_WindStep).")]
        public GameObject WindStepFxPrefab;
        [Tooltip("Wirbel an den Füßen gebuffter Spieler (VFX_WindBuff), lebt die Tempo-Dauer.")]
        public GameObject WindBuffFxPrefab;

        [Header("Passiv: Geistverbindung")]
        public float SpiritLinkRadius = 8f;
        [Tooltip("Buddies im Umkreis feuern +x schneller (0.1 = +10 %).")]
        public float SpiritLinkBonus = 0.1f;
        public float SpiritLinkInterval = 0.5f;
        [Tooltip("Dezenter Faden zu Buddies in der Aura, nur für den eigenen Spieler (VFX_SpiritLink, mit LineRenderer).")]
        public GameObject SpiritLinkFxPrefab;
        [Tooltip("Fäden zu den Buddies in der Aura anzeigen (aus: Nutzerwunsch 2026-11-09, die Strahlen wirkten störend).")]
        public bool ShowSpiritLinks = false;

        [Header("Element-Fähigkeiten (freischaltbar)")]
        public WarTotemSpell WarTotem = new WarTotemSpell();                    // R
        public HealingTideSpell HealingTide = new HealingTideSpell();           // F
        public StoneskinTotemSpell StoneskinTotem = new StoneskinTotemSpell();  // C
        public AncestralSpiritSpell AncestralSpirit = new AncestralSpiritSpell(); // V

        public bool IsDashing { get; private set; }
        public ShamanTotem ActiveWarTotem => _warTotem;
        public ShamanTotem ActiveStoneTotem => _stoneTotem;
        public AncestralSpiritFollower ActiveSpirit => _spirit;
        public PlayerAbilities OwnerAbilities => Owner;
        public PlayerStats OwnerStats => Stats;

        private Coroutine _dash;
        private ShamanTotem _warTotem, _stoneTotem;
        private AncestralSpiritFollower _spirit;
        private float _nextLink;
        private readonly Dictionary<ElementalBuddy, LineRenderer> _links = new Dictionary<ElementalBuddy, LineRenderer>();
        private static readonly List<PlayerAbilities> _players = new List<PlayerAbilities>();
        private static readonly List<PlayerAvatar> _avatars = new List<PlayerAvatar>();

        public override ChampionClass Class => ChampionClass.Shaman;
        public override bool AutoRepeatPrimary => true;
        public override string DescribePassive() =>
            $"Geistverbindung: Buddies im Umkreis von {Hi(SpiritLinkRadius)} m feuern {Hi(SpiritLinkBonus * 100f)} % schneller.";

        // ---------------- Effektive Werte (inkl. Händlerkarten) ----------------

        public Vector3 MuzzlePosition => ProjectileSpawn != null ? ProjectileSpawn.position : transform.TransformPoint(MuzzleOffset);
        public float BoltRangeEff => Mod(AbilityId.SpiritBolt, AbilityStat.Range, BoltRange);
        public int ChainJumpsEff => Mathf.Max(0, ChainJumps + (Owner != null ? Mathf.RoundToInt(Owner.Mods.Sum(AbilityId.SpiritBolt, AbilityStat.Pierce)) : 0));
        public float WindStepDistanceEff => Mod(AbilityId.WindStep, AbilityStat.Range, WindStepDistance) * (Owner != null ? Owner.MobilityMultiplier : 1f);
        public float HasteDurationEff => Mod(AbilityId.WindStep, AbilityStat.Duration, HasteDuration);
        public int WindStepChargesEff => Mathf.Max(1, Owner != null ? Owner.Mods.ApplyInt(AbilityId.WindStep, AbilityStat.Charges, WindStepCharges) : WindStepCharges);

        public override AbilityId GetAbility(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Primary: return AbilityId.SpiritBolt;
                case AbilitySlot.Secondary: return AbilityId.WindStep;
                case AbilitySlot.Fire: return AbilityId.WarTotem;
                case AbilitySlot.Ice: return AbilityId.HealingTide;
                case AbilitySlot.Earth: return AbilityId.StoneskinTotem;
                default: return AbilityId.AncestralSpirit;
            }
        }

        public override ElementSpell GetSpell(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.WarTotem: return WarTotem;
                case AbilityId.HealingTide: return HealingTide;
                case AbilityId.StoneskinTotem: return StoneskinTotem;
                case AbilityId.AncestralSpirit: return AncestralSpirit;
                default: return null;
            }
        }

        public override float GetCooldown(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.SpiritBolt: return BoltCooldown; // Händlerkarte „Tempo“ = AbilityStat.Cooldown (über GetCooldownFactor)
                case AbilityId.WindStep: return WindStepCooldown;
                default: { var s = GetSpell(id); return s != null ? s.Cooldown : 0f; }
            }
        }

        // Manakosten inkl. Händlerkarte „Manakosten“ (Ahnengeist)
        public override float GetManaCost(AbilityId id)
        {
            var s = GetSpell(id);
            return s != null ? Mod(id, AbilityStat.ManaCost, s.ManaCost) : 0f;
        }

        public override int GetMaxCharges(AbilityId id) => id == AbilityId.WindStep ? WindStepChargesEff : 1;
        public override float GetChargeLockout(AbilityId id) => id == AbilityId.WindStep ? WindStepLockout : 0.3f;
        public override bool IsActive(AbilityId id) =>
            (id == AbilityId.WindStep && IsDashing) || (id == AbilityId.WarTotem && _warTotem != null && !_warTotem.Expired)
            || (id == AbilityId.StoneskinTotem && _stoneTotem != null && !_stoneTotem.Expired) || (id == AbilityId.AncestralSpirit && _spirit != null && _spirit.Remaining > 0f);

        // Während des Windschritts kein Angriff/Zauber
        public override bool CanCast(AbilityId id) => !IsDashing;

        public override string GetCostLine(AbilityId id)
        {
            if (id == AbilityId.WindStep && WindStepChargesEff > 1)
                return $"{ManaColor}kein Mana</color>   •   {WindStepChargesEff} Aufladungen, je {Fmt(Owner != null ? Owner.GetCooldownDuration(id) : WindStepCooldown)} s";
            return base.GetCostLine(id);
        }

        // ---------------- Ausführen ----------------

        public override void Cast(AbilityId id, SpellCastContext ctx)
        {
            switch (id)
            {
                case AbilityId.SpiritBolt:
                    FireBolt(ctx);
                    break;
                case AbilityId.WindStep:
                    WindStep(ctx);
                    break;
                default:
                    var spell = GetModdedSpell(id);
                    if (spell != null) spell.Cast(ctx);
                    break;
            }
        }

        // ---------------- Geisterblitz ----------------

        private void FireBolt(SpellCastContext ctx)
        {
            Vector3 pos = MuzzlePosition;
            float damage = BoltDamage * ctx.DamageMultiplier;
            var bolt = ArcherKit.LaunchArrow(SpiritBoltPrefab, pos, ctx.AimDirection, BoltSpeed, damage, BoltRangeEff, false);
            if (bolt == null) return;
            if (SpiritBoltPrefab == null) BuildBoltPlaceholder(bolt.transform);
            bolt.Ability = AbilityId.SpiritBolt;
            bolt.ExtraPierce = 0;
            if (bolt.HitFxPrefab == null) bolt.HitFxPrefab = BoltHitFxPrefab;
            int jumps = ChainJumpsEff;
            bolt.OnHitAny = (enemy, point) => Chain(enemy, damage * ChainDamageFactor, jumps);
            if (MuzzleFxPrefab != null) CombatUtil.SpawnFx(MuzzleFxPrefab, pos, bolt.transform.rotation, 0.6f);
            GameAudio.PlayOr(SfxId.SpiritBolt, pos);
        }

        private static void BuildBoltPlaceholder(Transform bolt)
        {
            var orb = SuperBuddy.CreatePrimitive(PrimitiveType.Sphere, "SpiritOrb", new Color(0.25f, 0.95f, 0.85f), bolt);
            orb.transform.localScale = Vector3.one * 0.3f;
        }

        // Kettensprung vom getroffenen Gegner: jeweils der nächste noch nicht getroffene Gegner im ChainRadius.
        // Optik auf allen Rechnern (Ziele können leicht abweichen), Schaden nur auf dem Server.
        private void Chain(EnemyBrain first, float damage, int jumps)
        {
            if (first == null || jumps <= 0) return;
            var hit = new HashSet<EnemyBrain> { first };
            EnemyBrain from = first;
            for (int j = 0; j < jumps; j++)
            {
                EnemyBrain next = NearestChainTarget(from.transform.position, hit);
                if (next == null) break;
                hit.Add(next);
                Vector3 a = from.transform.position + Vector3.up;
                Vector3 b = next.transform.position + Vector3.up;
                SpawnChainFx(a, b);
                GameAudio.PlayOr(SfxId.SpiritZap, b);
                HitVfx.Show(next, AbilityId.SpiritBolt, (b - a).normalized, 0.8f, BoltHitFxPrefab);
                if (Net.IsServer) EnemyBrain.DealPlayerDamage(next, damage, AbilityId.SpiritBolt, hitFx: false);
                from = next;
            }
        }

        private EnemyBrain NearestChainTarget(Vector3 from, HashSet<EnemyBrain> exclude)
        {
            EnemyBrain best = null;
            float bestD = float.MaxValue;
            foreach (var e in CombatUtil.FindEnemies(from, ChainRadius))
            {
                if (e == null || e.IsDead || exclude.Contains(e)) continue;
                float d = CombatUtil.HorizontalDistance(from, e.transform.position);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        private void SpawnChainFx(Vector3 from, Vector3 to)
        {
            if (ChainFxPrefab == null) return;
            Vector3 dir = to - from;
            var go = Instantiate(ChainFxPrefab, from, dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity);
            var beam = go.GetComponent<BeamFx>();
            if (beam != null) beam.SetPoints(from, to);
            else
            {
                var line = go.GetComponent<LineRenderer>();
                if (line != null)
                {
                    line.useWorldSpace = true;
                    line.positionCount = 2;
                    line.SetPosition(0, from);
                    line.SetPosition(1, to);
                }
            }
            Destroy(go, Mathf.Max(0.1f, ChainFxLifetime) + 0.5f);
        }

        // ---------------- Windschritt ----------------

        private void WindStep(SpellCastContext ctx)
        {
            // Richtung: Laufrichtung (WASD), sonst Richtung Mauszeiger (wie die Rolle)
            Vector3 dir = ctx.AimDirection;
            Vector3 move = ctx.MoveDirection;
            move.y = 0f;
            if (move.sqrMagnitude > 0.04f) dir = move.normalized;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            dir.Normalize();
            float dist = WindStepDistanceEff;
            // Steinwall hält den Sprint auf (wie Blink/Ansturm); Wände stoppt der CharacterController
            int mask = StoneWall.BlockMask;
            if (mask != 0 && Physics.Raycast(ctx.Origin + Vector3.up, dir, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0f, hit.distance - 0.6f);
            if (_dash != null) StopCoroutine(_dash);
            _dash = StartCoroutine(DashRoutine(ctx, dir, dist, Mathf.Max(0.05f, WindStepDuration)));
        }

        private IEnumerator DashRoutine(SpellCastContext ctx, Vector3 dir, float distance, float duration)
        {
            IsDashing = true;
            if (Controller != null) { Controller.MovementLocked = true; Controller.RotationLocked = true; }
            if (IsLocalControl) transform.rotation = Quaternion.LookRotation(dir);
            if (WindStepFxPrefab != null) CombatUtil.SpawnFx(WindStepFxPrefab, Ground(transform.position), Quaternion.LookRotation(dir), 1.5f);
            GameAudio.PlayOr(SfxId.WindStep, transform.position);
            float t = 0f;
            while (t < duration)
            {
                float dt = Mathf.Min(Time.deltaTime, duration - t);
                t += dt;
                if (IsLocalControl && Character != null && Character.enabled) Character.Move(dir * (distance / duration) * dt);
                yield return null;
            }
            EndDash();
            // Lauftempo für dich und Spieler im Umkreis (Mittelpunkt: vorhergesagter Endpunkt, auf dem Server unabhängig
            // von der nachhinkenden Position der fremden Figur)
            Vector3 center = IsLocalControl ? transform.position : ctx.Origin + dir * distance;
            GrantHaste(center);
        }

        private void EndDash()
        {
            IsDashing = false;
            _dash = null;
            if (Controller != null) { Controller.MovementLocked = false; Controller.RotationLocked = false; }
        }

        private void GrantHaste(Vector3 center)
        {
            float dur = HasteDurationEff;
            CollectPlayers(center, HasteRadius, Owner, _players);
            foreach (var p in _players)
            {
                if (p == null) continue;
                if (Net.IsServer) p.ServerGrantBuff(SupportBuffKind.MoveSpeed, HasteBonus, dur);
                if (WindBuffFxPrefab != null)
                {
                    var fx = Instantiate(WindBuffFxPrefab, p.transform.position, Quaternion.identity, p.transform);
                    Destroy(fx, dur);
                }
            }
        }

        // ---------------- Totems / Geist ----------------

        // Neues Totem merken; nur ein Kriegstotem (und ein Steinhaut-Totem) gleichzeitig pro Schamane
        public void SetTotem(ShamanTotem totem)
        {
            if (totem == null) return;
            if (totem.Kind == ShamanTotem.TotemKind.War)
            {
                if (_warTotem != null && _warTotem != totem) _warTotem.End();
                _warTotem = totem;
            }
            else
            {
                if (_stoneTotem != null && _stoneTotem != totem) _stoneTotem.End();
                _stoneTotem = totem;
            }
        }

        public void SummonSpirit(AncestralSpiritSpell spell)
        {
            if (_spirit != null) _spirit.End();
            _spirit = AncestralSpiritFollower.Spawn(spell.SpiritPrefab, this, spell, transform.position + transform.rotation * new Vector3(1.1f, 0.6f, -0.9f));
        }

        // ---------------- Gemeinsame Helfer (auch für die Zauber) ----------------

        // Aktion nach delay Sekunden (Treffer-Zeitpunkt der Animation); ohne aktives Kit sofort
        public static void After(ShamanKit kit, float delay, System.Action action)
        {
            if (action == null) return;
            if (delay <= 0f || kit == null || !kit.isActiveAndEnabled) { action(); return; }
            kit.StartCoroutine(kit.AfterRoutine(delay, action));
        }

        private IEnumerator AfterRoutine(float delay, System.Action action)
        {
            yield return new WaitForSeconds(delay);
            action();
        }

        // Spieler im XZ-Radius (lebend). Ohne Netz gibt es keine PlayerAvatar-Liste → der Wirkende zählt, wenn er im Radius steht.
        public static void CollectPlayers(Vector3 center, float radius, PlayerAbilities caster, List<PlayerAbilities> result)
        {
            result.Clear();
            PlayerAvatar.InRadius(center, radius, _avatars);
            foreach (var a in _avatars)
                if (a != null && a.Abilities != null && !result.Contains(a.Abilities)) result.Add(a.Abilities);
            if (caster != null && !result.Contains(caster))
            {
                var stats = caster.GetComponent<PlayerStats>();
                if ((stats == null || !stats.IsDead) && CombatUtil.HorizontalDistance(center, caster.transform.position) <= radius)
                    result.Add(caster);
            }
        }

        // Totem-Punkt: Mauspunkt, höchstens maxRange entfernt, nicht unter den eigenen Füßen, auf dem Boden
        public static Vector3 TotemPoint(SpellCastContext ctx, float maxRange)
        {
            Vector3 target = ctx.HasAimPoint ? ctx.AimPoint : ctx.Origin + ctx.AimDirection * 3f;
            Vector3 to = target - ctx.Origin;
            to.y = 0f;
            if (to.magnitude > maxRange) target = ctx.Origin + to.normalized * maxRange;
            if (to.magnitude < 1f) target = ctx.Origin + ctx.AimDirection * 1f;
            if (ctx.Caster != null) target.y = ctx.Caster.GetGroundHeight(target, ctx.Origin.y);
            return target;
        }

        // ---------------- Passiv: Geistverbindung ----------------

        void Update()
        {
            if (Owner == null) return;
            bool alive = Stats == null || !Stats.IsDead;
            if (alive && Time.time >= _nextLink)
            {
                _nextLink = Time.time + Mathf.Max(0.1f, SpiritLinkInterval);
                // Auf allen Rechnern (wie der Buddy-Buff des Lichtrauschs): Anzeige überall gleich
                var buddies = ElementalBuddy.Active;
                for (int i = buddies.Count - 1; i >= 0; i--)
                {
                    var b = buddies[i];
                    if (b == null || b.IsDead || CombatUtil.HorizontalDistance(transform.position, b.transform.position) > SpiritLinkRadius) continue;
                    b.AddTimedFireRateBuff(SpiritLinkBonus, SpiritLinkInterval + 0.5f, ElementalBuddy.FireRateSourceSpiritLink);
                }
            }
            UpdateLinks(alive && IsLocalControl);
        }

        // Fäden zu Buddies in der Aura – nur für den eigenen Spieler
        private readonly List<ElementalBuddy> _stale = new List<ElementalBuddy>();
        private void UpdateLinks(bool show)
        {
            show &= ShowSpiritLinks; // 2026-11-09: Nutzer fand die Fäden störend → standardmäßig aus (vorhandene werden entfernt)
            if (SpiritLinkFxPrefab == null || SpiritLinkFxPrefab.GetComponent<LineRenderer>() == null) return; // Fäden brauchen einen LineRenderer
            _stale.Clear();
            foreach (var kv in _links)
            {
                var b = kv.Key;
                bool keep = show && b != null && !b.IsDead && CombatUtil.HorizontalDistance(transform.position, b.transform.position) <= SpiritLinkRadius;
                if (!keep) _stale.Add(b);
            }
            foreach (var b in _stale)
            {
                if (_links.TryGetValue(b, out var lr) && lr != null) Destroy(lr.gameObject);
                _links.Remove(b);
            }
            if (!show) return;
            var buddies = ElementalBuddy.Active;
            for (int i = 0; i < buddies.Count; i++)
            {
                var b = buddies[i];
                if (b == null || b.IsDead || _links.ContainsKey(b)) continue;
                if (CombatUtil.HorizontalDistance(transform.position, b.transform.position) > SpiritLinkRadius) continue;
                var go = Instantiate(SpiritLinkFxPrefab);
                var lr = go.GetComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                _links[b] = lr;
            }
            foreach (var kv in _links)
            {
                if (kv.Key == null || kv.Value == null) continue;
                kv.Value.SetPosition(0, transform.position + Vector3.up * 1.2f);
                kv.Value.SetPosition(1, kv.Key.transform.position + Vector3.up * 0.8f);
            }
        }

        private void ClearLinks()
        {
            foreach (var kv in _links) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _links.Clear();
        }

        // ---------------- Animation ----------------

        public override string GetAnimTrigger(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.SpiritBolt: return "Cast";
                case AbilityId.WindStep: return "Dash";
                case AbilityId.WarTotem:
                case AbilityId.StoneskinTotem: return "PlantTotem";
                case AbilityId.HealingTide: return "Wave";
                case AbilityId.AncestralSpirit: return "Summon";
                default: return null;
            }
        }

        public override void OnDeactivated()
        {
            if (_dash != null) StopCoroutine(_dash);
            if (IsDashing) EndDash();
            _dash = null;
            // Totems bleiben stehen (ortsgebunden, eigene Lebensdauer); der Geist folgt nur einem lebenden Schamanen
            if (_spirit != null) _spirit.End();
            _spirit = null;
            ClearLinks();
        }

        void OnDisable()
        {
            ClearLinks();
        }

        // ---------------- Texte ----------------

        public override string GetName(AbilityId id)
        {
            foreach (var a in Abilities)
                if (a != null && a.Id == id && !string.IsNullOrEmpty(a.Name)) return a.Name;
            switch (id)
            {
                case AbilityId.SpiritBolt: return "Geisterblitz";
                case AbilityId.WindStep: return "Windschritt";
                case AbilityId.WarTotem: return "Kriegstotem";
                case AbilityId.HealingTide: return "Heilende Woge";
                case AbilityId.StoneskinTotem: return "Steinhaut-Totem";
                case AbilityId.AncestralSpirit: return "Ahnengeist";
                default: return base.GetName(id);
            }
        }

        public override bool TryGetStatValue(AbilityId id, AbilityStat stat, out float value, out string unit, out string label)
        {
            value = 0f;
            unit = "";
            label = "";
            if (id == AbilityId.SpiritBolt)
            {
                switch (stat)
                {
                    case AbilityStat.Damage: value = BoltDamage * DamageMult(id); label = "Schaden"; return true;
                    case AbilityStat.Pierce: value = ChainJumpsEff; label = "Sprünge"; return true;
                    case AbilityStat.Range: value = BoltRangeEff; unit = " m"; label = "Reichweite"; return true;
                    case AbilityStat.Cooldown: value = Owner != null ? Owner.GetCooldownDuration(id) : BoltCooldown; unit = " s"; label = "Zeit zwischen Blitzen"; return true;
                }
            }
            else if (id == AbilityId.WindStep)
            {
                switch (stat)
                {
                    case AbilityStat.Range: value = WindStepDistanceEff; unit = " m"; label = "Distanz"; return true;
                    case AbilityStat.Duration: value = HasteDurationEff; unit = " s"; label = "Tempo-Dauer"; return true;
                    case AbilityStat.Charges: value = WindStepChargesEff; label = "Aufladungen"; return true;
                }
            }
            else if (stat == AbilityStat.ManaCost && GetSpell(id) != null)
            {
                value = GetManaCost(id);
                unit = " Mana";
                label = "Manakosten";
                return true;
            }
            return base.TryGetStatValue(id, stat, out value, out unit, out label);
        }

        public override string Describe(AbilityId id, float dm)
        {
            switch (id)
            {
                case AbilityId.SpiritBolt:
                    return $"Schleudert einen Geisterblitz ({Hi(BoltRangeEff)} m), der {Hi(BoltDamage * dm)} Schaden verursacht. Beim Treffer springt er auf bis zu {Hi(ChainJumpsEff)} weitere Gegner im Umkreis von {Hi(ChainRadius)} m über (je {Hi(BoltDamage * ChainDamageFactor * dm)} Schaden). Halten feuert weiter.";
                case AbilityId.WindStep:
                    return $"Sprintet {Hi(WindStepDistanceEff)} m weit. Danach laufen du und Mitspieler im Umkreis von {Hi(HasteRadius)} m {Hi(HasteDurationEff)} s lang {Hi(HasteBonus * 100f)} % schneller. Steinwälle halten dich auf.";
                case AbilityId.WarTotem:
                {
                    var s = Modded(WarTotem, id);
                    return $"Rammt am Mauszeiger (bis {Hi(s.MaxCastRange)} m) ein Kriegstotem in den Boden. {Hi(s.Duration)} s lang feuern Buddies im Umkreis von {Hi(s.Radius)} m {Hi(s.BuddyFireRateBonus * 100f)} % schneller, Spieler greifen mit dem Grundangriff {Hi(s.PlayerAttackSpeedBonus * 100f)} % schneller an. Nur ein Kriegstotem gleichzeitig.";
                }
                case AbilityId.HealingTide:
                {
                    var s = Modded(HealingTide, id);
                    return $"Eine Wasserwelle im Kegel vor dir ({Hi(s.ConeAngle)}°, {Hi(s.Range)} m). Gegner erleiden {Hi(s.Damage * dm)} Schaden und sind {Hi(s.SlowDuration)} s lang {Hi(s.Slow * 100f)} % langsamer. Du und Verbündete im Kegel werden um {Hi(s.Heal)} LP geheilt.";
                }
                case AbilityId.StoneskinTotem:
                {
                    var s = Modded(StoneskinTotem, id);
                    return $"Stellt am Mauszeiger (bis {Hi(s.MaxCastRange)} m) ein Steinhaut-Totem auf: Gegner im Umkreis von {Hi(s.PlantRadius)} m erleiden {Hi(s.PlantDamage * dm)} Schaden und werden {Hi(s.PlantKnockback)} m zurückgestoßen. {Hi(s.Duration)} s lang bekommen Buddies im Umkreis von {Hi(s.Radius)} m alle {Hi(s.PulseInterval)} s einen Schild von {Hi(s.BuddyShield)} ({Hi(s.ShieldDuration)} s), Spieler darin nehmen {Hi(s.PlayerDamageReduction * 100f)} % weniger Schaden.";
                }
                case AbilityId.AncestralSpirit:
                {
                    var s = Modded(AncestralSpirit, id);
                    return $"Ruft einen Geisterwolf, der dich {Hi(s.Duration)} s lang begleitet. Er heilt Spieler und Buddies im Umkreis von {Hi(s.Radius)} m um {Hi(s.HealPerSecond)} LP pro Sekunde, den Nexus um {Hi(s.NexusHealPercent * 100f)} % alle {Hi(s.NexusInterval)} s. Beim Rufen sind Gegner im Umkreis von {Hi(s.BlindRadius)} m {Hi(s.BlindDuration)} s geblendet.";
                }
                default:
                    return "";
            }
        }
    }
}
