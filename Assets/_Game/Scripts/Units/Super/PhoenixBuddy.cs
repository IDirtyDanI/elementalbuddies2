using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Phönix (Feuer + Erde + Licht): Unterstützer. Sturzflug alle ~5 s durch die dichteste Gegnerlinie (Schaden + Brand + Feuerspur),
    // Aura +Schaden für andere Buddies, und einmal pro Welle Wiedergeburt eines zerstörten Buddys in der Nähe (oder seiner selbst).
    // Der Buddy-Root (Collider, Slot) bleibt auf seiner Stange; nur das Kind "Visual" fliegt.
    public class PhoenixBuddy : SuperBuddy
    {
        [Header("Sturzflug")]
        public float DiveLength = 12f;
        public float DiveDuration = 1.2f;
        public float DiveHitRadius = 1.25f;
        public float DiveHeight = 3f;      // Flughöhe auf dem Hin-/Rückweg; tiefster Punkt über der Linie ~0.6 m
        public float DiveBurnDps = 15f;
        public float DiveBurnDuration = 4f;
        public float ClusterRadius = 2.5f;

        [Header("Feuerspur")]
        public float TrailLifetime = 4f;
        public float TrailDps = 8f;

        [Header("Aura")]
        public float AuraRadius = 8f;
        public float DamageBonus = 0.3f;   // +30 % Schaden für andere Buddies (stapelt nicht, nicht für Phönixe)

        [Header("Wiedergeburt")]
        public float RebirthRadius = 10f;
        public float RebirthDelay = 3f;
        public float SelfRebirthDelay = 5f;
        [Range(0.05f, 1f)] public float RebirthHealthFraction = 0.5f;

        [Header("Optik")]
        public GameObject FireTrailPrefab;    // optional (Länge 1 entlang Z, Breite 1); leer = Laufzeit-Streifen
        public GameObject RebirthEffectPrefab;// optional, beim Wiedererscheinen (3 s)
        public GameObject RebirthWaitPrefab;  // optional, Glut/Ei während der Wartezeit; leer = Laufzeit-Kugel
        public GameObject DiveHitVfxPrefab;   // optional, an jedem getroffenen Gegner (1 s)
        public GameObject BurnVfxPrefab;      // optional, an BurnEffect übergeben

        private bool _rebirthUsed;
        private WaveManager _waves;
        private Coroutine _dive;
        private static ElementalBuddy _lastClaimed;
        private readonly List<EnemyBrain> _diveBuffer = new List<EnemyBrain>();
        private readonly HashSet<EnemyBrain> _diveHit = new HashSet<EnemyBrain>();

        public bool RebirthUsed => _rebirthUsed;
        public bool IsDiving => _dive != null;
        public int LastDiveHits { get; private set; }

        protected override FusionElement DefaultElement => FusionElement.Phoenix;

        protected override void ApplyClassDefaults()
        {
            BaseMaxHP = 260f;
        }

        protected override void Start()
        {
            base.Start();
            OnBuddyDestroyed += HandleBuddyDestroyed;
            _waves = WaveManager.Instance;
            if (_waves != null) _waves.OnWaveStart += ResetRebirth;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            OnBuddyDestroyed -= HandleBuddyDestroyed;
            if (_waves != null) _waves.OnWaveStart -= ResetRebirth;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EndDive();
        }

        private void ResetRebirth() => _rebirthUsed = false;
        public void MarkRebirthUsed() => _rebirthUsed = true;

        // ---------------- Aura ----------------

        // Schadens-Multiplikator für einen Buddy: stärkste abdeckende Phönix-Aura (kein Stapeln), Phönixe profitieren nicht
        public static float GetDamageMultiplier(ElementalBuddy buddy)
        {
            if (buddy == null || buddy is PhoenixBuddy) return 1f;
            float best = 1f;
            Vector3 pos = buddy.transform.position;
            var active = Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (!(active[i] is PhoenixBuddy p) || p == null || !p.isActiveAndEnabled || p.IsStunned) continue;
                Vector3 d = p.transform.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude <= p.AuraRadius * p.AuraRadius) best = Mathf.Max(best, 1f + p.DamageBonus);
            }
            return best;
        }

        // ---------------- Sturzflug ----------------

        protected override bool TryPerformAction()
        {
            if (_dive != null) return false;
            var best = FindDensestEnemy(transform.position, EffectiveRange, ClusterRadius, out _);
            if (best == null) return false;
            CurrentTarget = best.transform;
            Dive(best.transform.position);
            return true;
        }

        // Linie durch den Zielpunkt (Richtung vom Phönix weg), DiveLength lang; Öffentlich für Tests
        public void Dive(Vector3 targetPos)
        {
            Vector3 perch = transform.position;
            Vector3 dir = CombatUtil.FlatDirection(perch, targetPos, transform.forward);
            float toTarget = CombatUtil.HorizontalDistance(perch, targetPos);
            float startDist = Mathf.Max(0.5f, toTarget - DiveLength * 0.5f);
            Vector3 a = SuperBuddy.GroundPoint(perch + dir * startDist);
            Vector3 b = SuperBuddy.GroundPoint(perch + dir * (startDist + DiveLength));
            _diveHit.Clear();
            LastDiveHits = 0;
            if (_dive != null) StopCoroutine(_dive);
            _dive = StartCoroutine(DiveRoutine(a, b, EffectiveDamage));
        }

        private IEnumerator DiveRoutine(Vector3 a, Vector3 b, float damage)
        {
            Transform v = Visual;
            Vector3 localPos = v != null ? v.localPosition : Vector3.zero;
            Quaternion localRot = v != null ? v.localRotation : Quaternion.identity;
            Vector3 start = v != null ? v.position : transform.position + Vector3.up * 2f;

            float half = Mathf.Max(0.05f, DiveDuration * 0.5f);
            float t = 0f;
            // Hinweg: Stange -> Linienanfang (Sinkflug) -> Linienende tief über dem Boden; Treffer entlang der Linie
            while (t < half)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / half);
                Vector3 ground = Vector3.Lerp(a, b, k);
                float h = Mathf.Lerp(0.6f, DiveHeight * 0.5f, Mathf.Abs(k - 0.5f) * 2f);
                if (v != null)
                {
                    Vector3 p = k < 0.1f ? Vector3.Lerp(start, ground + Vector3.up * h, k / 0.1f) : ground + Vector3.up * h;
                    Face(v, p - v.position);
                    v.position = p;
                }
                HitAlong(a, ground, damage);
                yield return null;
            }
            HitAlong(a, b, damage);
            FireTrail.Spawn(a, b, DiveHitRadius, TrailLifetime, TrailDps, FireTrailPrefab);

            // Rückweg: hoher Bogen zurück zur Stange
            t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / half);
                if (v != null)
                {
                    Vector3 home = transform.TransformPoint(localPos);
                    Vector3 p = Vector3.Lerp(b + Vector3.up * 0.6f, home, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * DiveHeight);
                    Face(v, p - v.position);
                    v.position = p;
                }
                yield return null;
            }
            EndDive();
        }

        private static void Face(Transform v, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) v.rotation = Quaternion.LookRotation(dir);
        }

        // Visual zurück auf die Stange
        private void EndDive()
        {
            if (_dive != null) StopCoroutine(_dive);
            _dive = null;
            if (Visual != null && _visualHome.HasValue)
            {
                Visual.localPosition = _visualHome.Value.pos;
                Visual.localRotation = _visualHome.Value.rot;
            }
        }

        private (Vector3 pos, Quaternion rot)? _visualHome;

        protected override void Awake()
        {
            base.Awake();
            if (Visual != null) _visualHome = (Visual.localPosition, Visual.localRotation);
        }

        private void HitAlong(Vector3 a, Vector3 b, float damage)
        {
            _diveBuffer.Clear();
            FireTrail.CollectOnSegment(a, b, DiveHitRadius, _diveBuffer);
            foreach (var e in _diveBuffer)
            {
                if (e == null || e.IsDead || !_diveHit.Add(e)) continue;
                LastDiveHits++;
                BurnEffect.Apply(e.gameObject, DiveBurnDps, DiveBurnDuration, BurnVfxPrefab);
                SpawnVfx(DiveHitVfxPrefab, e.transform.position + Vector3.up * 0.9f, 1f);
                e.TakeDamage(damage);
            }
        }

        // ---------------- Wiedergeburt ----------------

        private void HandleBuddyDestroyed(ElementalBuddy dead)
        {
            if (dead == null || _rebirthUsed || _lastClaimed == dead) return;
            bool self = dead == this;
            if (!self)
            {
                if (IsDead || !isActiveAndEnabled) return;
                Vector3 d = dead.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > RebirthRadius * RebirthRadius) return;
            }

            var snap = BuddySnapshot.Capture(dead);
            if (snap == null) return;
            if (self) snap.MarkPhoenixUsed = true; // eigene Wiedergeburt verbraucht die Welle auch für den neuen Phönix
            _rebirthUsed = true;
            _lastClaimed = dead;
            PhoenixRebirth.Schedule(snap, self ? SelfRebirthDelay : RebirthDelay, RebirthHealthFraction, RebirthEffectPrefab, RebirthWaitPrefab);
        }
    }

    // Alles, was zum Wiederherstellen eines Buddys nötig ist (vor der Zerstörung festgehalten)
    public class BuddySnapshot
    {
        public GameObject Prefab;
        public UnitConfigSO Config;
        public int Level;
        public float PaidCost;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsFusion;
        public FusionElement Element;
        public int ParentA = -1, ParentB = -1, ParentC = -1;
        public bool MarkPhoenixUsed;

        public static BuddySnapshot Capture(ElementalBuddy b)
        {
            if (b == null || b.Config == null) return null;
            var s = new BuddySnapshot
            {
                Prefab = b.Config.Prefab,
                Config = b.Config,
                Level = b.Level,
                PaidCost = b.PaidCost,
                Position = b.transform.position,
                Rotation = b.transform.rotation,
            };
            if (b is FusionBuddy f)
            {
                s.IsFusion = true;
                s.Element = f.Element;
                s.ParentA = f.ParentElementA;
                s.ParentB = f.ParentElementB;
                if (f is SuperBuddy sb) s.ParentC = sb.ParentElementC;
                if (b is PhoenixBuddy p && p.RebirthUsed) s.MarkPhoenixUsed = true;
            }
            // Ohne Prefab nur Super-Buddies (Laufzeit-Platzhalter)
            if (s.Prefab == null && !(b is SuperBuddy)) return null;
            return s;
        }

        public ElementalBuddy Spawn()
        {
            ElementalBuddy buddy;
            GameObject go;
            if (Prefab != null)
            {
                go = Object.Instantiate(Prefab, Position, Rotation);
                buddy = go.GetComponentInChildren<ElementalBuddy>();
            }
            else
            {
                var sb = SuperBuddy.CreateRuntime(Element, Position);
                go = sb.gameObject;
                buddy = sb;
            }
            if (buddy == null)
            {
                Object.Destroy(go);
                return null;
            }
            buddy.Config = Config;
            buddy.PaidCost = PaidCost;
            if (IsFusion && buddy is FusionBuddy f)
            {
                f.Element = Element;
                f.ParentElementA = ParentA;
                f.ParentElementB = ParentB;
                if (f is SuperBuddy sb2) sb2.ParentElementC = ParentC;
            }
            if (MarkPhoenixUsed && buddy is PhoenixBuddy p) p.MarkRebirthUsed();
            if (!go.activeSelf) go.SetActive(true);
            buddy.RestoreLevel(Level);
            return buddy;
        }
    }

    // Wartet die Wiedergeburts-Zeit ab (unabhängig vom Phönix, der inzwischen sterben kann) und erzeugt den Buddy neu
    public class PhoenixRebirth : MonoBehaviour
    {
        private BuddySnapshot _snap;
        private float _delay, _hpFraction, _t;
        private GameObject _effect;
        private bool _pulse;
        private bool _reserved;

        public static event System.Action<ElementalBuddy> OnReborn;

        // Wartende Wiedergeburten belegen ihren Buddy-Slot bis zum Wiedererscheinen
        private static readonly List<PhoenixRebirth> _pending = new List<PhoenixRebirth>();
        public static int PendingCount => _pending.Count;
        public static event System.Action OnPendingChanged;

        // Reset bei deaktiviertem Domain Reload
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            _pending.Clear();
            OnReborn = null;
            OnPendingChanged = null;
        }

        private void Reserve()
        {
            if (_reserved) return;
            _reserved = true;
            _pending.Add(this);
            OnPendingChanged?.Invoke();
        }

        // Slot freigeben (vor dem Spawn, damit der neue Buddy ihn übernimmt)
        private void Release()
        {
            if (!_reserved) return;
            _reserved = false;
            _pending.Remove(this);
            OnPendingChanged?.Invoke();
        }

        void OnDestroy() => Release();

        public static PhoenixRebirth Schedule(BuddySnapshot snap, float delay, float hpFraction, GameObject effectPrefab, GameObject waitPrefab)
        {
            GameObject go;
            if (waitPrefab != null) go = Instantiate(waitPrefab, snap.Position, Quaternion.identity);
            else
            {
                go = SuperBuddy.CreatePrimitive(PrimitiveType.Sphere, "PhoenixEmber", new Color(1f, 0.55f, 0.1f));
                go.transform.position = snap.Position + Vector3.up * 0.5f;
                go.transform.localScale = Vector3.one * 0.5f;
            }
            bool pulse = waitPrefab == null;
            go.name = "PhoenixRebirth";
            var r = go.AddComponent<PhoenixRebirth>();
            r._snap = snap;
            r._delay = delay;
            r._hpFraction = hpFraction;
            r._effect = effectPrefab;
            r._pulse = pulse;
            r.Reserve();
            return r;
        }

        void Update()
        {
            _t += Time.deltaTime;
            if (_pulse) transform.localScale = Vector3.one * (0.4f + 0.15f * Mathf.Sin(_t * 8f)); // Laufzeit-Glut pulsiert
            if (_t < _delay) return;
            Release();
            if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.GameOver)
            {
                var buddy = _snap.Spawn();
                if (buddy != null)
                {
                    buddy.SetStartHealthFraction(_hpFraction);
                    if (_effect != null) Destroy(Instantiate(_effect, _snap.Position, Quaternion.identity), 3f);
                    GameAudio.Play(SfxId.Fusion, _snap.Position);
                    ToastUI.Show($"{buddy.StageName} ist wiedergeboren!");
                    OnReborn?.Invoke(buddy);
                }
            }
            Destroy(gameObject);
        }
    }
}
