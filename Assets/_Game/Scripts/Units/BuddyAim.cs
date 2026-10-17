using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Ausrichtung eines Buddys zu seinem Ziel (rein kosmetisch, läuft auf allen Rechnern, kein Netz).
    // Dreht nur das aktive Visual (Stage1Visual bzw. Humanoid-"Visual") um die Hochachse – der Root (Collider, NetworkObject)
    // bleibt fest. Feuerpunkte/Glow (Root-Kinder) werden um dieselbe Achse mitgedreht, damit Schüsse vorne am Modell starten.
    // Ziel: CurrentTarget des Buddys; Erd-Buddy/Kristall (Flächen-Spott ohne Ziel) schauen zum nächsten Gegner in Reichweite.
    // Ohne gültiges Ziel für IdleDelay Sekunden dreht das Visual langsam in die Prefab-Blickrichtung (zur Kamera) zurück.
    // Wird von ElementalBuddy.OnEnable automatisch angehängt (kein Prefab-Eintrag nötig).
    [DisallowMultipleComponent]
    public class BuddyAim : MonoBehaviour
    {
        [Tooltip("Drehtempo zum Ziel (Grad pro Sekunde).")]
        public float TurnSpeed = 420f;
        [Tooltip("Drehtempo zurück in die Ruhe-Blickrichtung (Grad pro Sekunde).")]
        public float ReturnSpeed = 120f;
        [Tooltip("So lange ohne gültiges Ziel, bevor das Visual in die Ruhe-Blickrichtung zurückdreht (s).")]
        public float IdleDelay = 2f;
        [Tooltip("Ziel gilt als verloren, wenn es weiter als Reichweite × Faktor entfernt ist.")]
        public float LoseRangeFactor = 1.5f;
        [Tooltip("Suchintervall für den nächsten Gegner (nur Erd-Buddy/Kristall).")]
        public float ScanInterval = 0.25f;

        private ElementalBuddy _buddy;
        private BuddyEvolution _evo;
        private PhoenixBuddy _phoenix;
        private bool _scanNearest;

        // Ruhe-Rotation (lokal) je Visual – Gier-Versatz 0 = Prefab-Blickrichtung
        private readonly Dictionary<Transform, Quaternion> _home = new Dictionary<Transform, Quaternion>();
        private readonly List<Transform> _muzzles = new List<Transform>();
        private readonly List<Vector3> _muzzleHomePos = new List<Vector3>();
        private readonly List<Quaternion> _muzzleHomeRot = new List<Quaternion>();
        private Vector3 _center; // Drehachse der Feuerpunkte (lokal im Root, nur XZ)

        private float _yaw; // aktueller Gier-Versatz in Grad (-180..180) relativ zur Ruhe-Blickrichtung
        private float _lastValidTime = -999f;
        private FloatingCreature _lastCreature;

        private Transform _target;
        private EnemyBrain _targetBrain;
        private Transform _scanTarget;
        private float _nextScan;
        private static readonly Collider[] _scanBuffer = new Collider[64];

        public float CurrentYaw => _yaw;
        public IReadOnlyList<Transform> Muzzles => _muzzles;

        void Awake()
        {
            Setup();
        }

        // Pivots + Feuerpunkte einsammeln (idempotent; für EditMode-Tests öffentlich)
        public void Setup()
        {
            _buddy = GetComponent<ElementalBuddy>();
            _evo = GetComponent<BuddyEvolution>();
            _phoenix = _buddy as PhoenixBuddy;
            _scanNearest = _buddy is TankBuddy || _buddy is CrystalBuddy;

            _home.Clear();
            if (_evo != null)
            {
                if (_evo.Stage1Visual != null) CaptureHome(_evo.Stage1Visual.transform);
                if (_evo.HumanoidVisual != null) CaptureHome(_evo.HumanoidVisual.transform);
            }
            Transform main = FallbackVisual();
            if (main != null) CaptureHome(main);

            Transform centerRef = main != null ? main : (_evo != null && _evo.HumanoidVisual != null ? _evo.HumanoidVisual.transform : null);
            _center = centerRef != null && centerRef.parent == transform ? new Vector3(centerRef.localPosition.x, 0f, centerRef.localPosition.z) : Vector3.zero;

            // Ruhe-Stellung bereits gedrehter Feuerpunkte wiederherstellen, bevor neu eingesammelt wird
            ApplyMuzzles(0f);
            _muzzles.Clear();
            _muzzleHomePos.Clear();
            _muzzleHomeRot.Clear();
            foreach (Transform c in transform)
                if (c.name.StartsWith("FirePoint") || c.name == "Glow") AddMuzzle(c);
            if (_buddy is ShooterBuddy s) AddMuzzle(s.FirePoint);
            else if (_buddy is HealerBuddy h) AddMuzzle(h.FirePoint);
            else if (_buddy is FusionBuddy f) AddMuzzle(f.FirePoint);
            if (_evo != null && _evo.StageFirePoints != null)
                foreach (var fp in _evo.StageFirePoints) AddMuzzle(fp);

            _yaw = 0f;
        }

        private void CaptureHome(Transform t)
        {
            if (t == null || _home.ContainsKey(t)) return;
            var creature = t.GetComponent<FloatingCreature>();
            _home[t] = creature != null ? creature.BaseLocalRotation : t.localRotation;
        }

        // Nur Root-Kinder, die nicht schon unter einem Visual hängen (die drehen ohnehin mit)
        private void AddMuzzle(Transform t)
        {
            if (t == null || t.parent != transform || _muzzles.Contains(t)) return;
            _muzzles.Add(t);
            _muzzleHomePos.Add(t.localPosition);
            _muzzleHomeRot.Add(t.localRotation);
        }

        private Transform FallbackVisual()
        {
            if (_buddy is SuperBuddy sb && sb.Visual != null) return sb.Visual;
            return transform.Find("Visual");
        }

        // Aktuell sichtbares Visual (nach Stufenwechsel von BuddyEvolution)
        public Transform ActivePivot
        {
            get
            {
                if (_evo != null)
                {
                    if (_evo.Stage1Visual != null && _evo.Stage1Visual.activeInHierarchy) return _evo.Stage1Visual.transform;
                    if (_evo.HumanoidVisual != null && _evo.HumanoidVisual.activeInHierarchy) return _evo.HumanoidVisual.transform;
                }
                Transform v = FallbackVisual();
                return v != null && v.gameObject.activeInHierarchy ? v : null;
            }
        }

        void LateUpdate()
        {
            if (_buddy == null || _buddy.IsDead) return;
            Transform pivot = ActivePivot;
            if (pivot == null) return;
            if (_phoenix != null && _phoenix.IsDiving) return; // Sturzflug steuert das Visual selbst

            if (!_buddy.IsStunned)
            {
                Transform target = ResolveTarget();
                float desired;
                float speed;
                if (target != null)
                {
                    _lastValidTime = Time.time;
                    desired = YawTowards(pivot, target.position);
                    speed = TurnSpeed;
                }
                else if (Time.time - _lastValidTime >= IdleDelay)
                {
                    desired = 0f;
                    speed = ReturnSpeed;
                }
                else
                {
                    desired = _yaw;
                    speed = 0f;
                }
                _yaw = Mathf.DeltaAngle(0f, Mathf.MoveTowardsAngle(_yaw, desired, speed * Time.deltaTime));
                SetAiming(pivot, target != null);
            }
            Apply(pivot);
        }

        // Gier-Versatz (Grad), bei dem das Visual waagerecht zum Weltpunkt schaut
        public float YawTowards(Transform pivot, Vector3 worldPoint)
        {
            if (pivot == null) return _yaw;
            Transform space = pivot.parent != null ? pivot.parent : transform;
            Vector3 dir = space.InverseTransformDirection(worldPoint - pivot.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return _yaw;
            Vector3 homeFwd = HomeRotation(pivot) * Vector3.forward;
            homeFwd.y = 0f;
            if (homeFwd.sqrMagnitude < 0.0001f) return _yaw;
            return Vector3.SignedAngle(homeFwd, dir, Vector3.up);
        }

        // Sofort auf einen Weltpunkt ausrichten (Tests/Werkzeuge)
        public void SnapTo(Vector3 worldPoint)
        {
            Transform pivot = ActivePivot;
            if (pivot == null) return;
            _yaw = YawTowards(pivot, worldPoint);
            Apply(pivot);
        }

        private Quaternion HomeRotation(Transform pivot)
        {
            if (!_home.TryGetValue(pivot, out var rot))
            {
                CaptureHome(pivot);
                rot = _home[pivot];
            }
            return rot;
        }

        // Visual + Feuerpunkte auf den aktuellen Versatz setzen
        private void Apply(Transform pivot)
        {
            Quaternion offset = Quaternion.AngleAxis(_yaw, Vector3.up);
            var creature = pivot.GetComponent<FloatingCreature>();
            if (creature != null && creature.isActiveAndEnabled)
            {
                creature.AimYaw = _yaw; // FloatingCreature schreibt die Rotation selbst jeden Frame
                // Nicht im Play-Mode (EditMode-Test): Update läuft nicht -> direkt setzen
                if (!Application.isPlaying) pivot.localRotation = offset * HomeRotation(pivot);
            }
            else pivot.localRotation = offset * HomeRotation(pivot);

            // Wechsel weg von einem FloatingCreature-Visual: dessen Zielmodus beenden
            if (_lastCreature != null && _lastCreature != creature) _lastCreature.Aiming = false;
            _lastCreature = creature;

            ApplyMuzzles(_yaw);
        }

        private void ApplyMuzzles(float yaw)
        {
            Quaternion offset = Quaternion.AngleAxis(yaw, Vector3.up);
            for (int i = 0; i < _muzzles.Count; i++)
            {
                Transform m = _muzzles[i];
                if (m == null) continue;
                m.localPosition = _center + offset * (_muzzleHomePos[i] - _center);
                m.localRotation = offset * _muzzleHomeRot[i];
            }
        }

        private void SetAiming(Transform pivot, bool aiming)
        {
            var creature = pivot.GetComponent<FloatingCreature>();
            if (creature != null) creature.Aiming = aiming;
        }

        // ---------------- Ziel ----------------

        private Transform ResolveTarget()
        {
            if (_scanNearest) return ScanNearest();

            Transform t = _buddy.CurrentTarget;
            if (t != _target)
            {
                _target = t;
                _targetBrain = t != null ? t.GetComponentInParent<EnemyBrain>() : null;
            }
            if (t == null || !t.gameObject.activeInHierarchy) return null;
            if (_targetBrain != null && _targetBrain.IsDead) return null;
            float range = _buddy.EffectiveRange * LoseRangeFactor;
            Vector3 d = t.position - transform.position;
            d.y = 0f;
            if (range > 0f && d.sqrMagnitude > range * range) return null;
            return t;
        }

        // Erd-Buddy/Kristall: nächster Gegner in Reichweite (gedrosselt)
        private Transform ScanNearest()
        {
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + ScanInterval;
                _scanTarget = null;
                float range = _buddy.EffectiveRange;
                if (range > 0f)
                {
                    Vector3 c = transform.position;
                    int n = Physics.OverlapSphereNonAlloc(c, range, _scanBuffer);
                    float best = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        var hit = _scanBuffer[i];
                        if (hit == null || !hit.CompareTag("Enemy")) continue;
                        var e = hit.GetComponentInParent<EnemyBrain>();
                        if (e == null || e.IsDead || !FusionBuddy.IsTargetable(e) || !ElementalBuddy.CanTarget(e)) continue;
                        Vector3 d = e.transform.position - c;
                        d.y = 0f;
                        if (d.sqrMagnitude < best)
                        {
                            best = d.sqrMagnitude;
                            _scanTarget = e.transform;
                        }
                    }
                }
            }
            if (_scanTarget != null && !_scanTarget.gameObject.activeInHierarchy) _scanTarget = null;
            return _scanTarget;
        }

        void OnDisable()
        {
            if (_lastCreature != null) _lastCreature.Aiming = false;
        }
    }
}
