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
    // Mehrspieler: läuft nur auf dem Server. Clients bekommen Warnflächen (AoeTelegraph → NetGame), Animator-Trigger und
    // die Optik der Wirkung (EnemyNet.BossFxRpc → PlayRemoteFx) gespiegelt.
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
        private EnemyNet _net;
        private NavMeshAgent _agent;
        private readonly List<PlayerStats> _players = new List<PlayerStats>();
        private Animator _animator;
        private readonly HashSet<string> _triggers = new HashSet<string>();
        private float _nextCheck, _nextCastTime;
        private Coroutine _casting;
        private AoeTelegraph _telegraph;
        // Regen mit mehreren Flächen (BossAbility.RainAreas > 1): weitere Flächen neben der ersten (_telegraph)
        private readonly List<AoeShape> _extraShapes = new List<AoeShape>();
        private readonly List<AoeTelegraph> _extraTelegraphs = new List<AoeTelegraph>();
        private static readonly Vector3[] _rainCenters = new Vector3[8];
        // Wurf (BossAbilityKind.Throw): Ziel, dessen Laufrichtung beim Zielen beobachtet wird
        private Transform _throwTarget;
        private float _lastThrowSfx = -1f;
        private const float MaxLeadSpeed = 7f; // Blinzeln/Rückstoß sollen das Vorhalten nicht ins Leere schicken

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
            _net = GetComponent<EnemyNet>();
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
                foreach (var p in _animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger) _triggers.Add(p.name);

            if (!Net.IsServer) return; // Clients: keine Boss-KI
            float now = Time.time;
            foreach (var a in Abilities)
                if (a != null) a.ReadyTime = now + a.Cooldown * Random.Range(InitialCooldownFactor.x, InitialCooldownFactor.y);
            _nextCastTime = now + 1f;
        }

        void OnDisable()
        {
            if (_telegraph != null) _telegraph.Cancel();
            _telegraph = null;
            CancelExtraTelegraphs();
            _casting = null;
        }

        private void CancelExtraTelegraphs()
        {
            foreach (var t in _extraTelegraphs) if (t != null) t.Cancel();
            _extraTelegraphs.Clear();
            _extraShapes.Clear();
        }

        void Update()
        {
            if (!Net.IsServer) return;
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
                // Kein Zauber mitten im Ausholen des Grundangriffs (Nahkampf-Hieb des Knochenfürsten); LockForCast bricht ihn sonst ab
                return !_brain.IsStunned && !_brain.IsFrozen && !_brain.IsCasting && !_brain.IsMeleeSwinging && (_agent == null || _agent.isOnNavMesh);
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
                case BossAbilityKind.Throw:
                    return FindThrowTarget(a.MinTriggerRange, range, out targetPos);
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
            // Alle lebenden Spielfiguren
            BossCombat.CollectPlayers(_players);
            foreach (var player in _players)
            {
                if (!BossCombat.IsAlive(player)) continue;
                float d = CombatUtil.HorizontalDistance(me, player.transform.position);
                if (d <= range && d < best) { best = d; pos = player.transform.position; found = true; }
            }
            _players.Clear();
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

        // Wurfziel: die am weitesten entfernte lebende Spielfigur zwischen min und max (wer auf Abstand bleibt, wird
        // beworfen – im Koop der Fernkämpfer, nicht der Nahkämpfer am Boss). Kein Wurf durch einen Steinwall.
        private bool FindThrowTarget(float min, float max, out Vector3 pos)
        {
            pos = transform.position;
            _throwTarget = null;
            Vector3 me = transform.position;
            float best = -1f;
            BossCombat.CollectPlayers(_players);
            foreach (var player in _players)
            {
                if (!BossCombat.IsAlive(player)) continue;
                float d = CombatUtil.HorizontalDistance(me, player.transform.position);
                if (d < min || d > max || d <= best) continue;
                if (BossCombat.WallBetween(me, player.transform.position)) continue;
                best = d;
                pos = player.transform.position;
                _throwTarget = player.transform;
            }
            _players.Clear();
            return _throwTarget != null;
        }

        private static bool IsUnit(Transform t)
        {
            var ps = t.GetComponent<PlayerStats>();
            if (ps != null) return BossCombat.IsAlive(ps);
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
            if (!string.IsNullOrEmpty(a.AnimatorTrigger) && _triggers.Contains(a.AnimatorTrigger) && _animator.isActiveAndEnabled)
                _animator.SetTrigger(a.AnimatorTrigger);
            if (!string.IsNullOrEmpty(a.AnimatorTrigger) && _net != null) _net.ServerAnimTrigger(Animator.StringToHash(a.AnimatorTrigger));

            // Wurf: erst kurz zielen (Laufrichtung des Ziels beobachten), dann mit Vorhalt festlegen – erst jetzt erscheint
            // die Warnlinie. Wer nur stur im Kreis läuft, läuft hinein; wer die Richtung wechselt oder ausweicht, entkommt.
            float t = 0f;
            _extraShapes.Clear();
            _extraTelegraphs.Clear();
            if (a.Kind == BossAbilityKind.Throw && a.ThrowAimTime > 0f && _throwTarget != null)
            {
                Transform tt = _throwTarget;
                Vector3 p0 = tt.position;
                float aim = Mathf.Min(a.ThrowAimTime, windup * 0.5f);
                while (t < aim)
                {
                    if (_brain == null || _brain.IsDead || _brain.IsStunned || _brain.IsFrozen) { Cancel(a); yield break; }
                    t += Time.deltaTime;
                    yield return null;
                }
                if (tt != null && t > 0f)
                {
                    Vector3 p1 = tt.position;
                    targetPos = DimensionEventRules.ThrowAimPoint(me, p0, p1, t, windup - t, a.ThrowSpeed, a.ThrowLead, MaxLeadSpeed);
                    dir = CombatUtil.FlatDirection(me, targetPos, dir);
                    transform.rotation = Quaternion.LookRotation(dir);
                }
            }

            AoeShape shape = BuildShape(a, me, dir, targetPos, out Vector3 landing);
            _telegraph = AoeTelegraph.Spawn(shape, ThemeColor, windup - t + leap);
            if (a.Kind == BossAbilityKind.Throw && a.ThrowCount > 1)
            {
                // Fächer: weitere Linien links/rechts der ersten
                int n = Mathf.Min(a.ThrowCount, 7);
                for (int i = 1; i < n; i++)
                {
                    float angle = DimensionEventRules.FanAngle(i, a.ThrowSpreadAngle);
                    Vector3 d = Quaternion.Euler(0f, angle, 0f) * dir;
                    _extraShapes.Add(AoeShape.Line(me, d, BeamLength(a, me, d), a.Width));
                }
                foreach (var extra in _extraShapes) _extraTelegraphs.Add(AoeTelegraph.Spawn(extra, ThemeColor, windup - t + leap));
            }
            if (a.Kind == BossAbilityKind.Rain && a.RainAreas > 1)
            {
                BuildExtraRainShapes(a, shape, targetPos);
                foreach (var extra in _extraShapes) _extraTelegraphs.Add(AoeTelegraph.Spawn(extra, ThemeColor, windup + leap));
            }

            // Ausholen (Blickrichtung bleibt fest)
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
            CancelExtraTelegraphs();
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
                case BossAbilityKind.Throw:
                    return AoeShape.Line(me, dir, BeamLength(a, me, dir), a.Width);
                default:
                    return AoeShape.Circle(me, a.Radius);
            }
        }

        // Weitere Regenflächen: zuerst auf anderen lebenden Spielern in Wurfweite, sonst zufällig RainSpread m um die erste
        // Fläche; nie näher als ein Radius an einer schon gesetzten Fläche (DimensionEventRules.RainCenters). Punkte
        // außerhalb des begehbaren Bodens rücken auf den NavMesh-Rand (kein Regen ins Leere).
        private void BuildExtraRainShapes(BossAbility a, AoeShape first, Vector3 targetPos)
        {
            Vector3 me = transform.position;
            BossCombat.CollectPlayers(_players);
            var others = new List<Vector3>();
            foreach (var player in _players)
            {
                if (!BossCombat.IsAlive(player)) continue;
                Vector3 p = player.transform.position;
                if (CombatUtil.HorizontalDistance(p, first.Origin) < 0.5f) continue; // das Ziel selbst
                if (CombatUtil.HorizontalDistance(me, p) > a.MaxCastRange) continue;
                others.Add(p);
            }
            _players.Clear();
            int want = Mathf.Clamp(a.RainAreas, 1, _rainCenters.Length);
            int n = DimensionEventRules.RainCenters(first.Origin, others.ToArray(), want, a.Radius, a.RainSpread.x, a.RainSpread.y,
                () => Random.value, _rainCenters);
            Vector3 inward = CombatUtil.FlatDirection(first.Origin, me, transform.forward); // vom Ziel Richtung Boss = ins Feld
            for (int i = 1; i < n; i++)
            {
                Vector3 c = _rainCenters[i];
                // Liegt die Mitte nicht auf begehbarem Boden (Ziel steht an der Mauer): erst gespiegelt, dann Richtung Boss
                // gedreht versuchen – so fällt der Regen nie ins Leere
                if (!OnWalkable(ref c))
                {
                    float d = Mathf.Max(a.Radius, CombatUtil.HorizontalDistance(c, first.Origin));
                    Vector3 mirrored = first.Origin + (first.Origin - c);
                    Vector3 side = Quaternion.Euler(0f, i % 2 == 0 ? 55f : -55f, 0f) * inward;
                    Vector3 toBoss = first.Origin + side * d;
                    Vector3 straight = first.Origin + inward * d;
                    if (OnWalkable(ref mirrored) && FarFromOthers(mirrored, a.Radius)) c = mirrored;
                    else if (OnWalkable(ref toBoss) && FarFromOthers(toBoss, a.Radius)) c = toBoss;
                    else if (OnWalkable(ref straight)) c = straight;
                    else continue; // kein Platz: diese Fläche entfällt
                }
                c.y = AoeTelegraph.GroundY(c);
                _extraShapes.Add(AoeShape.Circle(c, a.Radius));
            }
        }

        private static bool OnWalkable(ref Vector3 p)
        {
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) return false;
            p = hit.position;
            return true;
        }

        private bool FarFromOthers(Vector3 p, float minDist)
        {
            foreach (var s in _extraShapes)
                if (CombatUtil.HorizontalDistance(s.Origin, p) < minDist) return false;
            return true;
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

        // Wände (ObstacleLayer) kürzen den Strahl – wie beim Lichtpfeil des Bogenschützen.
        // Steinwall (eigener Layer) kürzt immer, geprüft in fester Höhe über dem Boden (Pivot schwebt).
        private float BeamLength(BossAbility a, Vector3 me, Vector3 dir)
        {
            float length = a.Length;
            if (a.ObstacleLayer.value != 0)
            {
                Vector3 from = me + Vector3.up * BeamHeight;
                foreach (var h in Physics.RaycastAll(from, dir, a.Length, a.ObstacleLayer, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.GetComponentInParent<EnemyBrain>() != null || h.collider.GetComponentInParent<PlayerStats>() != null
                        || h.collider.GetComponentInParent<ElementalBuddy>() != null) continue;
                    length = Mathf.Min(length, Mathf.Max(1f, h.distance));
                }
            }
            if (StoneWall.AnyActive)
            {
                Vector3 chest = BossCombat.WallCheckPoint(me);
                if (StoneWall.Blocks(chest, chest + dir * length, out RaycastHit wall))
                    length = Mathf.Min(length, Mathf.Max(1f, wall.distance));
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
            if (a.Kind == BossAbilityKind.Slam) shape.Origin = me; // tatsächlicher Landepunkt
            // Strahl: Länge erst beim Auslösen festlegen – ein während des Ausholens errichteter Steinwall kürzt ihn
            // (Wirkung und Optik, auch auf den Clients)
            if (a.Kind == BossAbilityKind.Beam)
                shape = AoeShape.Line(shape.Origin, shape.Forward, BeamLength(a, shape.Origin, shape.Forward), shape.Width);

            // Wurf: je Linie ein Geschoss; es übernimmt die Warnlinie und beendet sie am Flugende. Die Länge wird wie beim
            // Strahl erst jetzt festgelegt (Steinwall während des Ausholens).
            if (a.Kind == BossAbilityKind.Throw)
            {
                int index = Abilities.IndexOf(a);
                for (int i = -1; i < _extraShapes.Count; i++)
                {
                    AoeShape line = i < 0 ? shape : _extraShapes[i];
                    line = AoeShape.Line(line.Origin, line.Forward, BeamLength(a, line.Origin, line.Forward), line.Width);
                    var tele = i < 0 ? telegraph : (i < _extraTelegraphs.Count ? _extraTelegraphs[i] : null);
                    BossThrowProjectile.Spawn(line, a, tele, a.Damage * DamageMultiplier, ThemeColor, false);
                    if (_net != null)
                        _net.ServerBossFx(index, new BossFxData { Shape = line, Me = me, Dir = line.Forward, Theme = ThemeColor, ScaleY = transform.lossyScale.y });
                }
                _extraShapes.Clear();
                _extraTelegraphs.Clear();
                if (a.PlaySfx) GameAudio.Play(a.ImpactSfx, me);
                return;
            }

            // Optik lokal und auf den Clients
            var fx = new BossFxData { Shape = shape, Me = me, Dir = dir, Theme = ThemeColor, ScaleY = transform.lossyScale.y };
            PlayFx(a, fx, false);
            if (_net != null) _net.ServerBossFx(Abilities.IndexOf(a), fx);

            // Regen mit mehreren Flächen: jede weitere Fläche ist ein eigener Regen (eigene Optik-Nachricht an die
            // Clients – BossFxData bleibt unverändert –, eigene Warnfläche, eigene Pulse)
            if (a.Kind == BossAbilityKind.Rain)
            {
                for (int i = 0; i < _extraShapes.Count; i++)
                {
                    var efx = fx;
                    efx.Shape = _extraShapes[i];
                    PlayFx(a, efx, false);
                    if (_net != null) _net.ServerBossFx(Abilities.IndexOf(a), efx);
                    var ego = new GameObject("BossRain");
                    ego.AddComponent<BossRainArea>().Setup(_extraShapes[i], a, i < _extraTelegraphs.Count ? _extraTelegraphs[i] : null, DamageMultiplier);
                }
                _extraShapes.Clear();
                _extraTelegraphs.Clear();
            }

            // Wirkung (nur Server)
            switch (a.Kind)
            {
                case BossAbilityKind.Rain:
                {
                    // Pulse laufen im eigenen Objekt; Warnfläche bleibt bis zum letzten Puls
                    var go = new GameObject("BossRain");
                    go.AddComponent<BossRainArea>().Setup(shape, a, telegraph, DamageMultiplier);
                    return;
                }
                case BossAbilityKind.AllyHeal:
                    HealAllies(a);
                    break;
                default:
                    BossCombat.Apply(shape, a, me, a.Damage * DamageMultiplier);
                    break;
            }
            if (telegraph != null) telegraph.Finish();
        }

        // Optik einer ausgelösten Fähigkeit (Strahl, Bogen, Einschlag, Ton). remote = Client-Kopie: Pfeilhagel als reine Optik.
        private void PlayFx(BossAbility a, BossFxData fx, bool remote)
        {
            AoeShape shape = fx.Shape;
            Vector3 me = fx.Me;
            switch (a.Kind)
            {
                case BossAbilityKind.Throw:
                    // nur Clients (der Server fliegt das echte Geschoss): Optik-Kopie, Wurf-Ton einmal je Wurf
                    if (remote)
                    {
                        BossThrowProjectile.Spawn(shape, a, null, 0f, fx.Theme, true);
                        if (a.PlaySfx && Time.time - _lastThrowSfx > 0.2f) { _lastThrowSfx = Time.time; GameAudio.Play(a.ImpactSfx, me); }
                    }
                    return;
                case BossAbilityKind.Rain:
                    if (remote)
                    {
                        var go = new GameObject("BossRain (Optik)");
                        go.AddComponent<BossRainArea>().SetupVisual(shape, a);
                    }
                    BossCombat.SpawnEffect(a, a.ImpactEffectPrefab, shape.Origin, Quaternion.identity);
                    return;
                case BossAbilityKind.Beam:
                {
                    Vector3 from = me + Vector3.up * BeamHeight;
                    var beam = BeamFx.Spawn(a.BeamPrefab, from, from + shape.Forward * shape.Length);
                    if (beam != null) beam.Color = fx.Theme;
                    break;
                }
                case BossAbilityKind.Whirl:
                {
                    var arc = SlashArcFx.Spawn(a.ArcFxPrefab, new Vector3(me.x, AoeTelegraph.GroundY(me), me.z) + Vector3.up * (0.6f * fx.ScaleY), fx.Dir, a.Radius, 360f, fx.Theme, true);
                    if (arc != null) { arc.SweepTime = 0.22f; arc.FadeTime = 0.3f; arc.TailLength = 0.6f; arc.Width = 1.3f; }
                    break;
                }
            }

            Quaternion rot = Quaternion.LookRotation(shape.Forward);
            BossCombat.SpawnEffect(a, a.ImpactEffectPrefab, a.Kind == BossAbilityKind.Slam ? me : shape.Origin, rot);
            if (a.PlaySfx) GameAudio.Play(a.ImpactSfx, shape.Origin);
            CameraFollow.AddTraumaAt(0.4f, shape.Origin, 22f);
        }

        // Client: Optik einer vom Server ausgelösten Fähigkeit (EnemyNet)
        // Server: frühestens in seconds wieder eine Fähigkeit beginnen (laufende Würfe bleiben unberührt)
        public void DelayCasts(float seconds)
        {
            _nextCastTime = Mathf.Max(_nextCastTime, Time.time + Mathf.Max(0f, seconds));
        }

        public void PlayRemoteFx(int abilityIndex, BossFxData fx)
        {
            if (Net.IsServer || Abilities == null || abilityIndex < 0 || abilityIndex >= Abilities.Count) return;
            var a = Abilities[abilityIndex];
            if (a != null) PlayFx(a, fx, true);
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
