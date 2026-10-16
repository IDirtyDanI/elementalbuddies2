using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Flieger-Bewegung (Walddorf-Plan „Flieger-Mechanik“): horizontal direkt aufs Ziel (kein NavMesh-Weg), vertikal
    // Geländefolge – Sollhöhe max(Boden + FlyHeight; Hindernis + ObstacleClearance) aus Abtastungen unter und vor dem
    // Flieger, Steig-/Sinkrate ≤ ClimbRate, leichtes Auf-und-ab. Angriff: Herabstoßen (BeginDive) während des Ausholens,
    // danach Aufsteigen und kurzer Kreisbogen (EndDive). Frost/Betäubung: schwebt auf der Stelle und sinkt auf 1,5 m.
    // Simuliert nur auf dem Server; Clients bekommen die Position (inkl. Höhe) per NetworkTransform.
    // Der NavMeshAgent (RequireComponent des EnemyBrain) bleibt am Objekt, ist aber aus.
    [DisallowMultipleComponent]
    public class FlyingMotor : MonoBehaviour, IEnemyMotor
    {
        // Höchstzahl gleichzeitiger Flieger (Lesbarkeit/Leistung) – der Spawner (WaveManager) fragt CanSpawn ab
        public const int MaxConcurrent = 24;
        private static readonly List<FlyingMotor> _active = new List<FlyingMotor>();
        public static IReadOnlyList<FlyingMotor> Active => _active;
        public static int ActiveCount => _active.Count;
        public static bool CanSpawn => _active.Count < MaxConcurrent;

        [Header("Höhe (aus der Config überschrieben, siehe EnemyConfigSO)")]
        public float FlyHeight = 2f;
        public float ObstacleClearance = 1.5f;
        public float DiveHeight = 0.8f;
        [Tooltip("Frost/Betäubung: sinkt auf diese Höhe über dem Untergrund.")]
        public float HeldHeight = 1.5f;

        [Header("Flug")]
        public float ClimbRate = 4f;
        public float DiveRate = 6f;
        public float TurnRate = 240f;
        public float Acceleration = 8f;
        [Tooltip("Körperradius (lokal, × Skalierung) – Trefferbreite und Abstand.")]
        public float BodyRadius = 0.45f;
        public float SeparationRadius = 1.2f;
        public float SeparationSpeed = 2.5f;
        [Tooltip("Kartengrenze: Position auf ±MapHalfSize um die Kartenmitte (MapContext) begrenzt.")]
        public float MapHalfSize = 62f;
        public float BobAmplitude = 0.15f;
        public float BobPeriod = 1.6f;

        [Header("Angriff")]
        [Tooltip("Herabstoßen: horizontal bis auf diesen Abstand an den Zielpunkt (vom Beginn des Ausholens).")]
        public float LungeStopDistance = 1.0f;
        public float RecoverDuration = 1.0f;
        public float RecoverRadiusMin = 1.2f;
        public float RecoverRadiusMax = 2.0f;

        // Abtastung
        private const float ProbeInterval = 0.1f;
        private readonly float[] _required = new float[8];
        private static readonly RaycastHit[] _hits = new RaycastHit[16];
        private const float ProbeRadius = 0.9f; // ≈ Körper/Flügel: auch seitlich vorbeistreifende Kanten zählen
        private const float ProbeTopAbove = 40f;
        private const float ProbeLength = 120f;
        private static int _obstacleMask = -1, _groundMask = -1;

        private EnemyBrain _brain;
        private bool _simulate = true;

        private Vector3 _dest;
        private bool _hasDest;
        private bool _stopped;
        private float _speedMax = 4.2f;
        private float _curSpeed;
        private Vector3 _heading;
        private Vector3 _velocity;

        private float _cruiseAlt;
        private float _surfaceY;     // Oberfläche direkt darunter (Boden/Dach)
        private float _groundY;      // Boden (Floor) direkt darunter
        private float _nextProbe;
        private bool _probed;
        private float _bobPhase;
        private float _side = 1f;

        private bool _diving;
        private Vector3 _diveAim;
        private float _diveY;
        private float _lungeSpeed;
        private float _recoverUntil;
        private Transform _recoverTarget;
        private bool _recoverNexus;
        private float _recoverRadius;

        public bool IsDiving => _diving;
        public bool IsRecovering => Time.time < _recoverUntil;
        // Höhe über der Oberfläche darunter (Tests/Logging)
        public float HeightAboveSurface => transform.position.y - _surfaceY;
        public float SurfaceY => _surfaceY;
        public float CruiseAltitude => _cruiseAlt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active.Clear();
            _obstacleMask = -1;
            _groundMask = -1;
        }

        private static int ObstacleMask
        {
            get
            {
                // Gebäude, Gelände, Bäume (Default), Boden (Floor), Steinwall (SpellWall), Buddies (überfliegen)
                if (_obstacleMask == -1) _obstacleMask = LayerMask.GetMask("Default", "Floor", "SpellWall", "Buddy");
                return _obstacleMask;
            }
        }
        // Untergrund für Trefferkapsel und Schatten (ohne Buddies)
        public static int SurfaceMask => ObstacleMask & ~LayerMask.GetMask("Buddy");
        private static int GroundMask
        {
            get
            {
                if (_groundMask == -1) _groundMask = LayerMask.GetMask("Floor");
                return _groundMask;
            }
        }

        // EnemyBrain: Werte aus der Config übernehmen
        public void Init(EnemyBrain brain, EnemyConfigSO config)
        {
            _brain = brain;
            if (config != null)
            {
                if (config.FlyHeight > 0f) FlyHeight = config.FlyHeight;
                if (config.ObstacleClearance > 0f) ObstacleClearance = config.ObstacleClearance;
                if (config.DiveHeight > 0f) DiveHeight = config.DiveHeight;
            }
            _heading = transform.forward;
            _heading.y = 0f;
            _bobPhase = Random.value * Mathf.PI * 2f;
            _side = Random.value < 0.5f ? -1f : 1f;
            _nextProbe = Time.time + Random.value * ProbeInterval;
            Probe(transform.position, true);
        }

        void OnEnable()
        {
            if (!_active.Contains(this)) _active.Add(this);
        }

        void OnDisable()
        {
            _active.Remove(this);
        }

        // ---------------- IEnemyMotor ----------------

        public bool IsReady => isActiveAndEnabled && _simulate;
        public bool IsStopped
        {
            get => _stopped;
            set => _stopped = value;
        }
        public Vector3 Velocity
        {
            get => _velocity;
            set
            {
                Vector3 h = value;
                h.y = 0f;
                _curSpeed = h.magnitude;
                if (_curSpeed > 0.001f) _heading = h / _curSpeed;
                _velocity = value;
            }
        }
        public float Speed
        {
            get => _speedMax;
            set => _speedMax = Mathf.Max(0f, value);
        }
        public bool HasPath => _hasDest;
        public bool PathPending => false;
        public Vector3 SteeringTarget => _hasDest ? _dest : transform.position;
        public float Radius => BodyRadius * Mathf.Abs(transform.lossyScale.x);

        public bool SetDestination(Vector3 destination)
        {
            _dest = destination;
            _hasDest = true;
            return true;
        }

        public void ResetPath()
        {
            _hasDest = false;
        }

        // Ziel ist ein Bodenpunkt (Portal/NavMesh) → auf Flughöhe darüber
        public bool Warp(Vector3 position)
        {
            Probe(position, true);
            position.y = Mathf.Max(position.y + FlyHeight, _cruiseAlt);
            transform.position = position;
            _velocity = Vector3.zero;
            _curSpeed = 0f;
            _hasDest = false;
            return true;
        }

        // Rückstoß: nur horizontal (Höhe regelt die Geländefolge)
        public void Move(Vector3 offset)
        {
            offset.y = 0f;
            transform.position = ClampToMap(transform.position + offset);
        }

        public void DisableSimulation()
        {
            _simulate = false;
        }

        // ---------------- Angriff ----------------

        // Ausholen: herabstoßen auf max(Untergrund + DiveHeight; aimY) und horizontal bis LungeStopDistance an aim
        public void BeginDive(Vector3 aim, float aimY, float windup)
        {
            _diving = true;
            _recoverUntil = 0f;
            _diveAim = aim;
            float y = transform.position.y;
            _diveY = Mathf.Min(y, Mathf.Max(_surfaceY + DiveHeight, aimY));
            float dist = FlightMath.Horizontal(transform.position, aim) - LungeStopDistance;
            _lungeSpeed = Mathf.Max(_speedMax * 1.5f, windup > 0.01f ? Mathf.Max(0f, dist) / windup : 0f);
        }

        // Treffer (oder Fehlschlag): aufsteigen und kurzer Kreisbogen um das Ziel
        public void EndDive(Transform target, bool nexus)
        {
            _diving = false;
            if (target == null || RecoverDuration <= 0f) return;
            _recoverTarget = target;
            _recoverNexus = nexus;
            _recoverUntil = Time.time + RecoverDuration;
            float r = FlightMath.Horizontal(transform.position, RecoverCenter());
            _recoverRadius = Mathf.Clamp(r, RecoverRadiusMin, RecoverRadiusMax);
        }

        // Abbruch (Frost, Betäubung, Zielwechsel …): wieder steigen, kein Kreisbogen
        public void CancelDive()
        {
            _diving = false;
            _recoverUntil = 0f;
        }

        private Vector3 RecoverCenter()
        {
            if (_recoverNexus && Nexus.Instance != null) return Nexus.Instance.GetClosestPoint(transform.position);
            return _recoverTarget != null ? _recoverTarget.position : transform.position;
        }

        // ---------------- Simulation ----------------

        void Update()
        {
            if (!_simulate || Net.IsClientOnly) return;
            if (_brain == null || _brain.IsDead) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 pos = transform.position;
            bool held = _brain.IsFrozen || _brain.IsStunned;
            bool gameOver = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;

            // --- horizontal ---
            Vector3 vel = Vector3.zero;
            if (held || gameOver)
            {
                _curSpeed = 0f;
            }
            else if (_diving)
            {
                Vector3 d = _diveAim - pos;
                d.y = 0f;
                float dist = d.magnitude - LungeStopDistance;
                if (dist > 0.01f)
                {
                    float step = Mathf.Min(dist, _lungeSpeed * dt);
                    vel = d.normalized * (step / dt);
                    _heading = d.normalized;
                }
                _curSpeed = 0f;
            }
            else if (Time.time < _recoverUntil && !_stopped && _recoverTarget != null && _recoverTarget.gameObject.activeInHierarchy)
            {
                vel = FlightMath.OrbitVelocity(pos, RecoverCenter(), _recoverRadius, _speedMax * 0.8f, _side);
                _curSpeed = vel.magnitude;
                if (_curSpeed > 0.01f) _heading = FlightMath.TurnTowards(_heading, vel, TurnRate * 2f, dt);
            }
            else if (_hasDest && !_stopped)
            {
                Vector3 desired = _dest - pos;
                desired.y = 0f;
                float dist = desired.magnitude;
                if (dist < 0.1f)
                {
                    _hasDest = false;
                    _curSpeed = 0f;
                }
                else
                {
                    _heading = FlightMath.TurnTowards(_heading, desired, TurnRate, dt);
                    // bremsen vor dem Ziel (wie autoBraking), in scharfen Kurven langsamer
                    float align = Mathf.Clamp01(Vector3.Dot(_heading, desired / dist) * 0.5f + 0.5f);
                    float targetSpeed = Mathf.Min(_speedMax, Mathf.Sqrt(2f * Acceleration * dist)) * Mathf.Lerp(0.35f, 1f, align);
                    // weit unter der Sollhöhe (z. B. Start am Portal direkt vor einem Torturm): erst steigen, dann fliegen
                    float climbNeed = _cruiseAlt - pos.y;
                    if (climbNeed > 1.5f) targetSpeed *= Mathf.Clamp(1f - (climbNeed - 1.5f) / 4f, 0.25f, 1f);
                    _curSpeed = Mathf.MoveTowards(_curSpeed, targetSpeed, Acceleration * dt);
                    vel = _heading * Mathf.Min(_curSpeed, dist / dt);
                }
            }
            else
            {
                _curSpeed = 0f;
            }

            // Abstand zu anderen Fliegern
            if (!held && SeparationRadius > 0f)
            {
                Vector3 push = Vector3.zero;
                for (int i = 0; i < _active.Count; i++)
                {
                    var o = _active[i];
                    if (o == this || o == null) continue;
                    push += FlightMath.SeparationPush(pos, o.transform.position, SeparationRadius,
                        (i & 1) == 0 ? transform.right : -transform.right);
                }
                vel += push * SeparationSpeed;
            }

            Vector3 next = ClampToMap(pos + vel * dt);

            // --- vertikal ---
            if (Time.time >= _nextProbe)
            {
                _nextProbe = Time.time + ProbeInterval;
                Probe(next, false);
            }
            float targetY;
            float rate = ClimbRate;
            if (held) targetY = Mathf.Min(pos.y, _surfaceY + HeldHeight);
            else if (_diving)
            {
                targetY = _diveY;
                rate = DiveRate;
            }
            else
            {
                float bob = BobPeriod > 0f ? Mathf.Sin(Time.time * (Mathf.PI * 2f / BobPeriod) + _bobPhase) * BobAmplitude : 0f;
                targetY = _cruiseAlt + bob;
            }
            next.y = FlightMath.StepAltitude(pos.y, targetY, rate, dt);
            // nie in den Boden (Körperradius). Bewusst nicht an Dach/Krone: ein spät erkanntes schmales Hindernis
            // (Baumkrone, Laterne) würde sonst einen Höhensprung erzwingen – dann lieber kurz hindurch.
            next.y = Mathf.Max(next.y, _groundY + Radius);

            transform.position = next;
            _velocity = (next - pos) / dt;

            // Blickrichtung = Flugrichtung (im Angriff/Stand dreht der EnemyBrain zum Ziel)
            if (!held && !_diving && vel.sqrMagnitude > 0.04f)
            {
                Vector3 look = vel;
                look.y = 0f;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 540f * dt);
            }
        }

        private Vector3 ClampToMap(Vector3 p)
        {
            Vector2 c = MapContext.Current != null ? MapContext.Current.MinimapCenter : Vector2.zero;
            return FlightMath.ClampToSquare(p, c, MapHalfSize);
        }

        // Untergrund und Sollhöhe abtasten: unter dem Flieger und entlang der Flugrichtung (frühzeitig steigen)
        private void Probe(Vector3 pos, bool immediate)
        {
            bool moving = !_stopped && (_hasDest || IsRecovering);
            float speed = moving ? Mathf.Max(_curSpeed, _speedMax * 0.5f) : 0f;
            var dists = FlightMath.LookAheadDistances;
            int count = speed > 0.01f ? dists.Length : 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = pos + _heading * dists[i];
                SampleSurface(p, out float ground, out float top);
                if (i == 0)
                {
                    _surfaceY = top;
                    _groundY = ground;
                }
                _required[i] = FlightMath.RequiredAltitude(ground, top, FlyHeight, ObstacleClearance);
            }
            _cruiseAlt = FlightMath.PlanAltitude(_required, count, speed, ClimbRate);
            _probed = true;
            if (immediate) _nextProbe = Time.time + ProbeInterval;
        }

        // ground = Boden (Floor), top = höchste Oberfläche (Dach, Mauer, Stamm, Buddy); ohne Treffer: Flieger-Höhe − FlyHeight
        private void SampleSurface(Vector3 p, out float ground, out float top)
        {
            Vector3 origin = new Vector3(p.x, p.y + ProbeTopAbove, p.z);
            float fallback = _probed ? _surfaceY : p.y - FlyHeight;
            top = fallback;
            // höchste Oberfläche ohne den Nexus (den umkreist/pickt der Flieger seitlich statt ihn zu überfliegen)
            bool hitTop = false;
            int n = Physics.SphereCastNonAlloc(origin, ProbeRadius, Vector3.down, _hits, ProbeLength, ObstacleMask, QueryTriggerInteraction.Ignore);
            Transform nexus = Nexus.Instance != null ? Nexus.Instance.transform : null;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c == null || _hits[i].distance <= 0f) continue;
                if (nexus != null && c.transform.IsChildOf(nexus)) continue;
                float y = _hits[i].point.y;
                if (!hitTop || y > top) top = y;
                hitTop = true;
            }
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit g, ProbeLength, GroundMask, QueryTriggerInteraction.Ignore))
                ground = g.point.y;
            else ground = top;
            if (!hitTop) top = ground;
            if (top < ground) top = ground;
        }
    }
}
