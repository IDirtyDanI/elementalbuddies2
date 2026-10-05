using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Boss-Fähigkeiten neben dem EnemyBrain: prüft alle CheckInterval s die Abilities-Liste (Reihenfolge = Priorität),
    // wählt die erste bereite mit erfüllter Bedingung, sperrt den EnemyBrain (LockForCast), zeigt eine Warnfläche
    // (AoeTelegraph, Farbe = Config.ThemeColor) und löst nach der Ausholzeit den Effekt aus.
    // Betäubung / Einfrieren / Tod während des Ausholens bricht ab (kürzere Abklingzeit).
    [RequireComponent(typeof(EnemyBrain))]
    public class BossBrain : MonoBehaviour
    {
        public List<BossAbility> Abilities = new List<BossAbility>();
        [Tooltip("Mindestabstand zwischen zwei Fähigkeiten (s).")]
        public float GlobalCooldown = 1.5f;
        public float CheckInterval = 0.25f;
        [Tooltip("Startabklingzeit = Cooldown × Zufall in diesem Bereich (nicht alles direkt beim Spawn).")]
        public Vector2 InitialCooldownFactor = new Vector2(0.3f, 0.7f);
        [Tooltip("Abgebrochene Fähigkeit: Abklingzeit × diesen Faktor.")]
        public float CancelCooldownFactor = 0.35f;
        [Tooltip("Höhe des Strahl-Ursprungs (Beam) über dem Boden.")]
        public float BeamHeight = 1.3f;
        [Tooltip("Optional: Farbe der Warnflächen statt Config.ThemeColor (Alpha 0 = ThemeColor).")]
        public Color TelegraphColorOverride = new Color(0f, 0f, 0f, 0f);

        public bool IsCastingAbility => _casting != null;
        // Fähigkeitsschaden × Gegnerschaden-Multiplikator des Bosses (Wellen-Rampe × Schwierigkeit)
        private float DamageMultiplier => _brain != null ? _brain.DamageMultiplier : 1f;
        public BossAbility CurrentAbility { get; private set; }

        private EnemyBrain _brain;
        private NavMeshAgent _agent;
        private Animator _animator;
        private readonly HashSet<string> _triggers = new HashSet<string>();
        private float _nextCheck, _nextCastTime;
        private Coroutine _casting;
        private AoeTelegraph _telegraph;

        public Color ThemeColor
        {
            get
            {
                if (TelegraphColorOverride.a > 0f) return TelegraphColorOverride;
                return _brain != null && _brain.Config != null ? _brain.Config.ThemeColor : Color.white;
            }
        }

        void Start()
        {
            _brain = GetComponent<EnemyBrain>();
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
                foreach (var p in _animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger) _triggers.Add(p.name);

            float now = Time.time;
            foreach (var a in Abilities)
                if (a != null) a.ReadyTime = now + a.Cooldown * Random.Range(InitialCooldownFactor.x, InitialCooldownFactor.y);
            _nextCastTime = now + 1f;
        }

        void OnDisable()
        {
            if (_telegraph != null) _telegraph.Cancel();
            _telegraph = null;
            _casting = null;
        }

        void Update()
        {
            if (_brain == null || _brain.IsDead || _casting != null) return;
            if (Time.time < _nextCheck || Time.time < _nextCastTime) return;
            _nextCheck = Time.time + CheckInterval;
            if (!CanAct) return;

            foreach (var a in Abilities)
            {
                if (a == null || !a.Enabled || Time.time < a.ReadyTime) continue;
                if (!TryGetTarget(a, out Vector3 targetPos)) continue;
                _casting = StartCoroutine(CastRoutine(a, targetPos));
                return;
            }
        }

        private bool CanAct
        {
            get
            {
                if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return false;
                return !_brain.IsStunned && !_brain.IsFrozen && !_brain.IsCasting && (_agent == null || _agent.isOnNavMesh);
            }
        }

        // ---------------- Bedingungen ----------------

        private float Range(BossAbility a) => a.TriggerRange > 0f ? a.TriggerRange : a.AreaSize;

        // Zielpunkt der Fähigkeit; false = Bedingung nicht erfüllt
        public bool TryGetTarget(BossAbility a, out Vector3 targetPos)
        {
            targetPos = transform.position;
            float range = Range(a);
            switch (a.Kind)
            {
                case BossAbilityKind.Whirl:
                case BossAbilityKind.Nova:
                    return FindUnitInRange(range, false, out targetPos);
                case BossAbilityKind.AllyHeal:
                    return CountWoundedAllies(a.Radius, a.HealThreshold) >= Mathf.Max(1, a.MinAllies);
                default:
                    return FindUnitInRange(range, true, out targetPos);
            }
        }

        // Aktuelles Ziel des EnemyBrain, wenn Spieler/Buddy in Reichweite; sonst nächster Spieler/Buddy in Reichweite
        private bool FindUnitInRange(float range, bool preferCurrent, out Vector3 pos)
        {
            pos = transform.position;
            Vector3 me = transform.position;
            if (preferCurrent)
            {
                Transform t = _brain.CurrentTarget;
                if (t != null && IsUnit(t) && CombatUtil.HorizontalDistance(me, t.position) <= range)
                {
                    pos = t.position;
                    return true;
                }
            }

            float best = float.MaxValue;
            bool found = false;
            var player = BossCombat.Player;
            if (player != null && player.isActiveAndEnabled && player.CurrentHP > 0f)
            {
                float d = CombatUtil.HorizontalDistance(me, player.transform.position);
                if (d <= range) { best = d; pos = player.transform.position; found = true; }
            }
            var buddies = ElementalBuddy.Active;
            for (int i = 0; i < buddies.Count; i++)
            {
                var b = buddies[i];
                if (b == null || b.IsDead || !b.isActiveAndEnabled) continue;
                Vector3 p = b.GetClosestPoint(me);
                float d = CombatUtil.HorizontalDistance(me, p);
                if (d <= range && d < best) { best = d; pos = b.transform.position; found = true; }
            }
            return found;
        }

        private static bool IsUnit(Transform t)
        {
            if (t.GetComponent<PlayerStats>() != null) return true;
            var b = t.GetComponent<ElementalBuddy>();
            return b != null && !b.IsDead;
        }

        private int CountWoundedAllies(float radius, float threshold)
        {
            int n = 0;
            foreach (var e in CombatUtil.FindEnemies(transform.position, radius))
            {
                if (e == null || e == _brain || e.IsDead || e.MaxHP <= 0f) continue;
                if (CombatUtil.HorizontalDistance(e.transform.position, transform.position) > radius) continue;
                if (e.CurrentHP / e.MaxHP < threshold) n++;
            }
            return n;
        }

        // ---------------- Ablauf ----------------

        private IEnumerator CastRoutine(BossAbility a, Vector3 targetPos)
        {
            CurrentAbility = a;
            float start = Time.time;
            a.ReadyTime = start + a.Cooldown;

            Vector3 me = transform.position;
            Vector3 dir = CombatUtil.FlatDirection(me, targetPos, transform.forward);
            float leap = a.Kind == BossAbilityKind.Slam ? Mathf.Max(0f, a.LeapDuration) : 0f;
            float windup = Mathf.Max(0.05f, a.Windup);
            _brain.LockForCast(windup + leap + Mathf.Max(0f, a.Recovery));
            transform.rotation = Quaternion.LookRotation(dir);

            AoeShape shape = BuildShape(a, me, dir, targetPos, out Vector3 landing);
            _telegraph = AoeTelegraph.Spawn(shape, ThemeColor, windup + leap);
            if (!string.IsNullOrEmpty(a.AnimatorTrigger) && _triggers.Contains(a.AnimatorTrigger) && _animator.isActiveAndEnabled)
                _animator.SetTrigger(a.AnimatorTrigger);

            // Ausholen (Blickrichtung bleibt fest)
            float t = 0f;
            while (t < windup)
            {
                if (_brain == null || _brain.IsDead || _brain.IsStunned || _brain.IsFrozen) { Cancel(a); yield break; }
                transform.rotation = Quaternion.LookRotation(dir);
                t += Time.deltaTime;
                yield return null;
            }

            if (a.Kind == BossAbilityKind.Slam && leap > 0f)
                yield return LeapRoutine(landing, leap, a.LeapHeight);

            Execute(a, shape, dir);
            _nextCastTime = Time.time + Mathf.Max(0f, a.Recovery) + GlobalCooldown;
            _casting = null;
            CurrentAbility = null;
        }

        private void Cancel(BossAbility a)
        {
            if (_telegraph != null) _telegraph.Cancel();
            _telegraph = null;
            a.ReadyTime = Time.time + a.Cooldown * CancelCooldownFactor;
            _nextCastTime = Time.time + GlobalCooldown;
            if (_brain != null) _brain.EndCast();
            _casting = null;
            CurrentAbility = null;
        }

        private AoeShape BuildShape(BossAbility a, Vector3 me, Vector3 dir, Vector3 targetPos, out Vector3 landing)
        {
            landing = me;
            switch (a.Kind)
            {
                case BossAbilityKind.Slam:
                    landing = LeapTarget(me, targetPos, a.LeapDistance);
                    return AoeShape.Circle(landing, a.Radius);
                case BossAbilityKind.Cone:
                    return AoeShape.Cone(me, dir, a.Length, a.ConeAngle);
                case BossAbilityKind.Rain:
                {
                    Vector3 to = targetPos - me;
                    to.y = 0f;
                    if (to.magnitude > a.MaxCastRange) targetPos = me + to.normalized * a.MaxCastRange;
                    targetPos.y = AoeTelegraph.GroundY(targetPos);
                    return AoeShape.Circle(targetPos, a.Radius);
                }
                case BossAbilityKind.Beam:
                    return AoeShape.Line(me, dir, BeamLength(a, me, dir), a.Width);
                default:
                    return AoeShape.Circle(me, a.Radius);
            }
        }

        // Landepunkt auf dem NavMesh: Richtung Ziel, höchstens maxDist, an NavMesh-Kanten gestoppt
        private static Vector3 LeapTarget(Vector3 from, Vector3 target, float maxDist)
        {
            Vector3 to = target - from;
            to.y = 0f;
            Vector3 desired = to.magnitude > maxDist ? from + to.normalized * maxDist : from + to;
            desired.y = target.y;
            if (NavMesh.SamplePosition(desired, out NavMeshHit sample, 2f, NavMesh.AllAreas)) desired = sample.position;
            if (NavMesh.Raycast(from, desired, out NavMeshHit hit, NavMesh.AllAreas)) desired = hit.position;
            return desired;
        }

        // Wände (ObstacleLayer) kürzen den Strahl – wie beim Lichtpfeil des Bogenschützen
        private float BeamLength(BossAbility a, Vector3 me, Vector3 dir)
        {
            float length = a.Length;
            if (a.ObstacleLayer.value == 0) return length;
            Vector3 from = me + Vector3.up * BeamHeight;
            foreach (var h in Physics.RaycastAll(from, dir, a.Length, a.ObstacleLayer, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<EnemyBrain>() != null || h.collider.GetComponentInParent<PlayerStats>() != null
                    || h.collider.GetComponentInParent<ElementalBuddy>() != null) continue;
                length = Mathf.Min(length, Mathf.Max(1f, h.distance));
            }
            return length;
        }

        // Sprung über den NavMesh (agent.Move), Bogen über baseOffset
        private IEnumerator LeapRoutine(Vector3 landing, float duration, float height)
        {
            if (_agent == null || !_agent.isOnNavMesh) yield break;
            Vector3 start = transform.position;
            float baseOffset = _agent.baseOffset;
            float t = 0f;
            while (t < duration)
            {
                if (_brain == null || _brain.IsDead) yield break;
                t = Mathf.Min(duration, t + Time.deltaTime);
                float k = t / duration;
                Vector3 p = Vector3.Lerp(start, landing, k);
                Vector3 step = p - transform.position;
                step.y = 0f;
                if (_agent.isOnNavMesh) _agent.Move(step);
                _agent.baseOffset = baseOffset + Mathf.Sin(k * Mathf.PI) * height / Mathf.Max(0.01f, transform.lossyScale.y);
                yield return null;
            }
            _agent.baseOffset = baseOffset;
        }

        // ---------------- Wirkung ----------------

        private void Execute(BossAbility a, AoeShape shape, Vector3 dir)
        {
            Vector3 me = transform.position;
            var telegraph = _telegraph;
            _telegraph = null;

            switch (a.Kind)
            {
                case BossAbilityKind.Rain:
                {
                    // Pulse laufen im eigenen Objekt; Warnfläche bleibt bis zum letzten Puls
                    var go = new GameObject("BossRain");
                    go.AddComponent<BossRainArea>().Setup(shape, a, telegraph, DamageMultiplier);
                    BossCombat.SpawnEffect(a, a.ImpactEffectPrefab, shape.Origin, Quaternion.identity);
                    return;
                }
                case BossAbilityKind.AllyHeal:
                    HealAllies(a);
                    break;
                case BossAbilityKind.Slam:
                    shape.Origin = me; // tatsächlicher Landepunkt
                    BossCombat.Apply(shape, a, me, a.Damage * DamageMultiplier);
                    break;
                case BossAbilityKind.Beam:
                {
                    Vector3 from = me + Vector3.up * BeamHeight;
                    var beam = BeamFx.Spawn(a.BeamPrefab, from, from + shape.Forward * shape.Length);
                    if (beam != null) beam.Color = ThemeColor;
                    BossCombat.Apply(shape, a, me, a.Damage * DamageMultiplier);
                    break;
                }
                case BossAbilityKind.Whirl:
                {
                    var arc = SlashArcFx.Spawn(a.ArcFxPrefab, new Vector3(me.x, AoeTelegraph.GroundY(me), me.z) + Vector3.up * (0.6f * transform.lossyScale.y), dir, a.Radius, 360f, ThemeColor, true);
                    if (arc != null) { arc.SweepTime = 0.22f; arc.FadeTime = 0.3f; arc.TailLength = 0.6f; arc.Width = 1.3f; }
                    BossCombat.Apply(shape, a, me, a.Damage * DamageMultiplier);
                    break;
                }
                default:
                    BossCombat.Apply(shape, a, me, a.Damage * DamageMultiplier);
                    break;
            }

            Quaternion rot = Quaternion.LookRotation(shape.Forward);
            BossCombat.SpawnEffect(a, a.ImpactEffectPrefab, a.Kind == BossAbilityKind.Slam ? me : shape.Origin, rot);
            if (a.PlaySfx) GameAudio.Play(a.ImpactSfx, shape.Origin);
            if (telegraph != null) telegraph.Finish();
        }

        private void HealAllies(BossAbility a)
        {
            foreach (var e in CombatUtil.FindEnemies(transform.position, a.Radius))
            {
                if (e == null || e == _brain || e.IsDead) continue;
                if (CombatUtil.HorizontalDistance(e.transform.position, transform.position) > a.Radius) continue;
                e.Heal(e.MaxHP * a.HealPercent);
            }
            if (a.SelfHealPercent > 0f) _brain.Heal(_brain.MaxHP * a.SelfHealPercent);
        }
    }
}
