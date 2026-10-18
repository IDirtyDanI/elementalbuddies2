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
        public float ArcaneBallCooldown = 0.6f;
        public Transform SpawnPoint;

        [Header("Blink (RMB)")]
        public float BlinkManaCost = 0f;
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
        // Halten der linken Maustaste feuert weiter (wie Ritter, Bogen, Berserker)
        public override bool AutoRepeatPrimary => true;

        public float EffectiveBlinkRange => Mod(AbilityId.Blink, AbilityStat.Range, BlinkRange) * (Owner != null ? Owner.MobilityMultiplier : 1f);
        public float EffectiveInvulnerability => Mod(AbilityId.Blink, AbilityStat.Duration, InvulnerabilityDuration);
        // Arkanball: Größe (Skalierung von Modell + Trigger) und Flugtempo mit Händlerkarten
        public float ArcaneBallSizeFactor => ModFactor(AbilityId.ArcaneBall, AbilityStat.Area);
        public float ArcaneBallSpeed => Mod(AbilityId.ArcaneBall, AbilityStat.Speed, BaseArcaneBall != null ? BaseArcaneBall.Speed : 20f);
        public float ArcaneBallBaseDamage => BaseArcaneBall != null ? BaseArcaneBall.Damage : 30f;
        // Splitterschaden am Einschlag (Anteil des Treffer-Schadens, Radius wächst mit der Kugelgröße)
        public float ArcaneBallSplashFraction => BaseArcaneBall != null ? BaseArcaneBall.SplashFraction : 0.4f;
        public float ArcaneBallSplashRadius => (BaseArcaneBall != null ? BaseArcaneBall.SplashRadius : 1.6f) * ArcaneBallSizeFactor;
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
                    CastArcaneBall(ctx.DamageMultiplier, ctx.AimDirection);
                    break;
                case AbilityId.Blink:
                    StartCoroutine(PerformBlink(ctx));
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

        private void CastArcaneBall(float damageMultiplier, Vector3 aimDirection)
        {
            // Flugrichtung aus dem Cast-Kontext (auf fremden Rechnern hinkt die Drehung der Figur etwas hinterher)
            aimDirection.y = 0f;
            Quaternion rot = aimDirection.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(aimDirection.normalized) : transform.rotation;
            Vector3 spawnPos = SpawnPoint != null ? SpawnPoint.position : transform.position + rot * Vector3.forward + Vector3.up;
            GameObject ball = Instantiate(ArcaneBallPrefab, spawnPos, rot);
            var arcane = ball.GetComponent<ArcaneBall>();
            if (arcane != null)
            {
                arcane.Damage *= damageMultiplier; // global × Händlerkarten (SpellCastContext)
                arcane.Speed = ArcaneBallSpeed;
            }
            float size = ArcaneBallSizeFactor;
            if (!Mathf.Approximately(size, 1f)) ball.transform.localScale *= size; // Trigger wächst mit
            if (arcane != null) arcane.SizeFactor = size;
            // Wirk-Blitz an der Hand (Spieltest 2026-10-18, Paket V) – Cast läuft auf allen Rechnern
            if (arcane != null && arcane.MuzzleVfx != null) CombatUtil.SpawnFx(arcane.MuzzleVfx, spawnPos, rot, 0.6f, size);
            ArcaneBallCast?.Invoke();
        }

        private IEnumerator PerformBlink(SpellCastContext ctx)
        {
            // I-Frames auf allen Rechnern (der Server entscheidet über Treffer)
            Stats.IsInvulnerable = true;
            if (IsLocalControl) Teleport(BlinkTarget(ctx));

            yield return new WaitForSeconds(EffectiveInvulnerability);
            Stats.IsInvulnerable = false;
        }

        // Zielpunkt des Blinks (nur beim Besitzer berechnet, die Figur-Position kommt per NetworkTransform zu den anderen)
        private Vector3 BlinkTarget(SpellCastContext ctx)
        {
            // Richtung Mauszeiger (Rechtsklick); liegt der Zeiger näher als die Reichweite, landet man genau dort
            float range = EffectiveBlinkRange;
            Vector3 blinkDir = transform.forward;
            float distance = range;
            if (ctx.HasAimPoint)
            {
                Vector3 to = ctx.AimPoint - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.04f)
                {
                    blinkDir = to.normalized;
                    distance = Mathf.Min(range, to.magnitude);
                }
            }

            // Wall Check (inkl. Steinwall-Layer: nicht durch die eigene Wand blinzeln)
            Vector3 targetPos = transform.position + blinkDir * distance;
            if (Physics.Raycast(transform.position + Vector3.up, blinkDir, out RaycastHit hit, distance, ObstacleLayer | global::ElementalBuddies.StoneWall.BlockMask))
            {
                targetPos = hit.point - blinkDir * 0.5f; // Stop slightly before wall
                targetPos.y = transform.position.y;
            }
            return SafeLanding(targetPos);
        }

        // Landepunkt auf begehbaren Boden setzen: Höhe aus dem Boden (sonst steckt die Figur nach dem Teleport z. B. in
        // einem Schrein-Podest und fällt durch die Map) und nur auf dem NavMesh. Ohne gültigen Punkt bleibt man stehen.
        // In der Luft (Sprung): waagerecht auf aktueller Höhe, danach fällt die Figur normal weiter (BlinkRules).
        private Vector3 SafeLanding(Vector3 target)
        {
            Vector3 start = transform.position;
            float pivotAboveFeet = PivotAboveFeet();

            if (Controller != null && BlinkRules.UseAirBlink(Controller.IsGrounded, FeetAboveFloor(start, pivotAboveFeet)))
            {
                Vector3 landing = start;
                int step = BlinkRules.FirstValidStep(BlinkRules.RetreatSteps, i =>
                    TryAirLanding(Vector3.Lerp(target, start, BlinkRules.RetreatFraction(i, BlinkRules.RetreatSteps)), pivotAboveFeet, out landing));
                if (step >= 0) return landing;
                // kein Boden unter der Flugbahn: Suche wie am Boden (knapp über dem Boden findet sie noch einen Punkt)
            }

            for (int i = 0; i < BlinkRules.RetreatSteps; i++)
            {
                // vom Ziel schrittweise zurück Richtung Start, bis ein gültiger Punkt gefunden ist
                Vector3 p = Vector3.Lerp(target, start, BlinkRules.RetreatFraction(i, BlinkRules.RetreatSteps));
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit nav, 1.5f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    Vector3 ground = nav.position;
                    int floor = Controller != null ? Controller.FloorLayer.value : 0;
                    if (floor != 0 && Physics.Raycast(ground + Vector3.up * 3f, Vector3.down, out RaycastHit g, 6f, floor, QueryTriggerInteraction.Ignore))
                        ground.y = g.point.y;
                    return new Vector3(ground.x, ground.y + pivotAboveFeet, ground.z);
                }
            }
            return start;
        }

        // Luft-Blink: Der Wand-Strahl geht in der Luft über Palisade und niedrige Dächer hinweg (Füße am Scheitel ~3,3 m hoch),
        // man landete hinter dem Kartenrand (Abschlusstest 2026-10-18). Landepunkt muss deshalb vom Boden unter dem Start aus
        // auf kurzem Weg erreichbar sein – über Zäune/Requisiten springen bleibt erlaubt, durch die Palisade nicht.
        private bool AirLandingReachable(Vector3 groundAtTarget)
        {
            int mask = AirGroundMask;
            if (mask == 0 || !Physics.Raycast(transform.position, Vector3.down, out RaycastHit g0, 100f, mask, QueryTriggerInteraction.Ignore))
                return true; // kein Boden unter dem Start: nicht prüfbar, wie bisher
            const float sample = 1.5f;
            // nur der NavMesh der normalen Gegner (Agententyp 0) – die Ramme hat einen eigenen, der sonst mit gefunden wird
            var filter = new UnityEngine.AI.NavMeshQueryFilter
            {
                agentTypeID = UnityEngine.AI.NavMesh.GetSettingsByIndex(0).agentTypeID,
                areaMask = UnityEngine.AI.NavMesh.AllAreas
            };
            if (!UnityEngine.AI.NavMesh.SamplePosition(g0.point, out UnityEngine.AI.NavMeshHit from, sample, filter)
                || !UnityEngine.AI.NavMesh.SamplePosition(groundAtTarget, out UnityEngine.AI.NavMeshHit to, sample, filter))
                return false;
            if (_airPath == null) _airPath = new UnityEngine.AI.NavMeshPath();
            if (!UnityEngine.AI.NavMesh.CalculatePath(from.position, to.position, filter, _airPath)
                || _airPath.status != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                return false;
            var corners = _airPath.corners;
            float walk = 0f;
            for (int i = 1; i < corners.Length; i++) walk += Vector3.Distance(corners[i - 1], corners[i]);
            float straight = Vector3.Distance(from.position, to.position);
            return BlinkRules.AirPathShortEnough(walk, straight);
        }

        private UnityEngine.AI.NavMeshPath _airPath;

        // Abstand Drehpunkt → Fußpunkt der Figur (Kapselmitte)
        private float PivotAboveFeet()
        {
            return Character != null
                ? (Character.height * 0.5f - Character.center.y) * transform.lossyScale.y + Character.skinWidth
                : 1f;
        }

        // Boden für den Luft-Blink: Floor-Layer und Hindernis-Layer (Gelände/Bauten auf "Default"); begehbar entscheidet der NavMesh
        private int AirGroundMask => (Controller != null ? Controller.FloorLayer.value : 0) | ObstacleLayer.value;

        // Höhe der Füße über dem Boden (+∞ ohne Boden darunter)
        private float FeetAboveFloor(Vector3 pivot, float pivotAboveFeet)
        {
            int mask = AirGroundMask;
            if (mask != 0 && Physics.Raycast(pivot, Vector3.down, out RaycastHit hit, 100f, mask, QueryTriggerInteraction.Ignore))
                return hit.distance - pivotAboveFeet;
            return float.PositiveInfinity;
        }

        // Luft-Landepunkt auf Höhe von p: darunter muss begehbarer Boden (NavMesh) liegen, am Ziel kein Hindernis (Decke, Gebäude)
        private bool TryAirLanding(Vector3 p, float pivotAboveFeet, out Vector3 landing)
        {
            landing = p;
            int mask = AirGroundMask;
            if (mask == 0) return false;
            // von Kopfhöhe nach unten: erfasst auch Boden/Hindernisse zwischen Füßen und Kopf (Hang, Stufe, Mauerkrone)
            float headAbovePivot = Character != null ? Character.height * transform.lossyScale.y - pivotAboveFeet : 1f;
            if (!Physics.Raycast(p + Vector3.up * headAbovePivot, Vector3.down, out RaycastHit g, 100f, mask, QueryTriggerInteraction.Ignore))
                return false;
            if (!BlinkRules.AirFloorReachable(g.point.y, p.y - pivotAboveFeet))
                return false;
            if (!UnityEngine.AI.NavMesh.SamplePosition(g.point, out UnityEngine.AI.NavMeshHit _, 1.5f, UnityEngine.AI.NavMesh.AllAreas))
                return false;
            if (!AirLandingReachable(g.point))
                return false;
            landing.y = BlinkRules.AirPivotY(p.y, g.point.y, pivotAboveFeet);
            return !BodyBlocked(landing, pivotAboveFeet);
        }

        // Steckt die Figur am Punkt in einem Hindernis (Decke, Gebäude, Steinwall)? Eigene Kollider zählen nicht.
        private bool BodyBlocked(Vector3 pivot, float pivotAboveFeet)
        {
            float scale = transform.lossyScale.y;
            float radius = Character != null ? Character.radius * scale : 0.5f;
            float height = Character != null ? Character.height * scale : 2f;
            float feet = pivot.y - pivotAboveFeet;
            // Fuß-Kugel etwas angehoben: Boden direkt unter den Füßen (Gelände auf "Default") zählt nicht als Hindernis
            Vector3 bottom = new Vector3(pivot.x, feet + radius + 0.25f, pivot.z);
            Vector3 top = new Vector3(pivot.x, Mathf.Max(bottom.y, feet + height - radius), pivot.z);
            Transform self = Character != null ? Character.transform : transform;
            var hits = Physics.OverlapCapsule(bottom, top, radius * 0.9f, ObstacleLayer | global::ElementalBuddies.StoneWall.BlockMask, QueryTriggerInteraction.Ignore);
            foreach (var c in hits)
                if (c != null && !c.transform.IsChildOf(self)) return true;
            return false;
        }

        private void Teleport(Vector3 targetPos)
        {
            // Netz: über PlayerAvatar (NetworkTransform.Teleport, keine Interpolation bei den anderen)
            var av = Owner != null ? Owner.Avatar : null;
            if (av != null && av.IsSpawned)
            {
                av.TeleportLocal(targetPos, transform.rotation);
                return;
            }
            bool cc = Character != null && Character.enabled;
            if (cc) Character.enabled = false;
            transform.position = targetPos;
            if (cc) Character.enabled = true;
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
                    string splash = ArcaneBallSplashFraction > 0f
                        ? $" Arkane Splitter treffen alle anderen Gegner im Umkreis von {Hi(ArcaneBallSplashRadius)} m mit {Hi(ArcaneBallBaseDamage * ArcaneBallSplashFraction * dm)} Schaden."
                        : "";
                    return $"Schleudert eine arkane Kugel in Blickrichtung. Sie verursacht {Hi(ArcaneBallBaseDamage * dm)} Schaden am ersten getroffenen Gegner.{splash}{extra} Halten feuert weiter.";
                }
                case AbilityId.Blink:
                    return $"Teleportiert dich bis zu {Hi(EffectiveBlinkRange)} m in Richtung Mauszeiger. Kurz nach dem Sprung bist du {Hi(EffectiveInvulnerability)} s unverwundbar. Wände halten den Sprung auf. Klappt auch im Sprung: Du gleitest waagerecht und fällst danach weiter.";
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
                    return $"Heiliges Licht im Umkreis von {Hi(s.Radius)} m. Es heilt dich und Mitspieler im Kreis um {Hi(s.PlayerHeal)}, Buddies um {Hi(s.BuddyHeal)} und den Nexus um {Hi(s.NexusHeal)} LP. Gegner werden geblendet (Sterne über dem Kopf) und sind {Hi(s.BlindDuration)} s lang um {Hi(s.BlindSlow * 100f)} % verlangsamt.";
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
