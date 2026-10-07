using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public enum ShrineState
    {
        Dormant,
        Awakened,
        Completed
    }

    // Elementar-Schrein außerhalb der Arena. Wird vom ShrineManager zu Wellenbeginn erweckt:
    // Der Player muss RequiredTime Sekunden im Kreis (CaptureRadius, XZ) stehen, während Angreifer kommen.
    // Gegner im Kreis → umkämpft (Fortschritt pausiert). Player draußen → Fortschritt verfällt langsam.
    // Erfolg: neuer Element-Zauber (PlayerAbilities.UnlockElementAbility) + passiver Schadensbonus für Buddies des Elements.
    // Endet die Welle vorher → gescheitert, zurück auf Dormant (ShrineManager plant ihn 2 Wellen später neu ein).
    // Mehrspieler: Nur der Server entscheidet (Einnahme, Angreifer, Aura, Bonus). Im Kreis zählt jede lebende Figur.
    // Belohnung: der Element-Zauber für ALLE Spielfiguren (auf jedem Rechner, ApplyUnlockForTeam) + Team-Bonus
    // (ShrineBonuses, Wert wird mit dem Zustand synchronisiert). Zustand/Fortschritt per NetGame an die Clients.
    public class Shrine : MonoBehaviour
    {
        private static readonly List<Shrine> _all = new List<Shrine>();
        public static IReadOnlyList<Shrine> All => _all;

        public static event System.Action<Shrine> OnAnyShrineAwakened;
        public static event System.Action<Shrine> OnAnyShrineCompleted;
        public static event System.Action<Shrine> OnAnyShrineFailed;

        [Header("Element")]
        [Tooltip("0 Feuer, 1 Eis, 2 Erde, 3 Licht")]
        [Range(0, 3)] public int ElementIndex;
        [Tooltip("Leer → automatisch „Feuer-Schrein“ usw.")]
        public string DisplayName = "";

        [Header("Capture")]
        [Tooltip("Mittelpunkt des Kreises (Fallback: dieses Transform). Schrein-Angreifer laufen hierhin.")]
        public Transform Center;
        public float CaptureRadius = 4f;
        [Tooltip("Sekunden, die der Player (unumkämpft) im Kreis stehen muss.")]
        public float RequiredTime = 30f;
        [Tooltip("Fortschrittsverlust in Sekunden pro Sekunde, solange der Player draußen ist (nie unter 0).")]
        public float DecayPerSecond = 0.5f;
        [Tooltip("Fortschritts-Tempo, solange Gegner im Kreis stehen (0 = Stillstand, 1 = voll).")]
        [Range(0f, 1f)] public float ContestedProgressRate = 0.4f;
        [Tooltip("Schaden pro Sekunde, den die Schrein-Aura Gegnern im Kreis zufügt (nur solange erwacht).")]
        public float AuraDps = 12f;
        [Tooltip("Angreifer erscheinen erst, wenn der Spieler den Kreis zum ersten Mal betritt (+ Vorlauf).")]
        public float EngageDelay = 2f;

        [Header("Angreifer")]
        [Tooltip("Gegnertypen (zufällig). Leer → erster Gegnertyp der aktuellen Welle.")]
        public EnemyConfigSO[] AttackerTypes;
        [Tooltip("Angreifer, die beim Erwachen gespawnt werden.")]
        public int AttackersPerWave = 4;
        [Tooltip("Zeitraum, über den die erste Gruppe gespawnt wird (s).")]
        public float AttackerSpawnDuration = 4f;
        public float SpawnRingMin = 10f;
        public float SpawnRingMax = 14f;
        [Tooltip("Nachschub alle X Sekunden, solange der Schrein erwacht ist (0 = aus).")]
        public float ReinforceInterval = 18f;
        public int ReinforceCount = 2;
        [Tooltip("Zählen die Angreifer zur Welle? (Welle endet erst, wenn sie tot sind.)")]
        public bool AttackersCountTowardWave = true;

        [Header("Belohnung")]
        [Tooltip("Passiver Schadensbonus für Buddies dieses Elements (0.1 = +10 %).")]
        public float BuddyDamageBonus = 0.1f;

        [Header("Visuals (optional)")]
        public GameObject DormantVisuals;
        public GameObject AwakenedVisuals;
        public GameObject CompletedVisuals;
        [Tooltip("Optional: wird auf Durchmesser = 2 × CaptureRadius (XZ) skaliert, z. B. ein Kreis-Quad/Decal.")]
        public Transform RadiusVisual;
        [Tooltip("Runen mit Emission (_EmissionColor) – pulsieren, solange der Schrein erwacht ist.")]
        public Renderer[] RuneRenderers;
        public bool UseElementColor = true;
        public Color RuneColor = Color.white;
        public float RuneIntensityAwakened = 3f;
        public float RuneIntensityCompleted = 1.2f;
        public float RunePulseSpeed = 3f;

        public ShrineState State { get; private set; } = ShrineState.Dormant;
        public float ProgressSeconds { get; private set; }
        public float Progress01 => RequiredTime > 0f ? Mathf.Clamp01(ProgressSeconds / RequiredTime) : 1f;
        public bool PlayerInside { get; private set; }
        public bool Contested { get; private set; }
        public bool IsAwakened => State == ShrineState.Awakened;
        public bool IsCompleted => State == ShrineState.Completed;
        // Ergebnis von PlayerAbilities.UnlockElementAbility beim Abschluss
        public bool AbilityUnlocked { get; private set; }

        public event System.Action<Shrine> OnStateChanged;

        public Vector3 CenterPosition => (Center != null ? Center : transform).position;
        public Transform CenterTransform => Center != null ? Center : transform;

        private readonly List<PlayerAvatar> _inside = new List<PlayerAvatar>();
        private float _syncTimer;
        private readonly List<EnemyBrain> _attackers = new List<EnemyBrain>();
        private readonly Collider[] _overlap = new Collider[64];
        private float _reinforceTimer;
        private bool _engaged;
        private readonly HashSet<IDamageable> _auraHits = new HashSet<IDamageable>();
        private Coroutine _spawnRoutine;
        private readonly List<Material> _runeMaterials = new List<Material>();
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            OnAnyShrineAwakened = null;
            OnAnyShrineCompleted = null;
            OnAnyShrineFailed = null;
        }

        void OnValidate()
        {
            ElementIndex = Mathf.Clamp(ElementIndex, 0, 3);
            // Auto-Name nachziehen, solange kein eigener Name vergeben wurde
            if (string.IsNullOrEmpty(DisplayName) || System.Array.IndexOf(ElementInfo.ShrineNames, DisplayName) >= 0)
                DisplayName = ElementInfo.ShrineName(ElementIndex);
        }

        void Awake()
        {
            if (string.IsNullOrEmpty(DisplayName)) DisplayName = ElementInfo.ShrineName(ElementIndex);

            if (RuneRenderers != null)
            {
                foreach (var r in RuneRenderers)
                {
                    if (r == null) continue;
                    foreach (var m in r.materials) // Instanzen, damit das Material-Asset unverändert bleibt
                    {
                        m.EnableKeyword("_EMISSION");
                        _runeMaterials.Add(m);
                    }
                }
            }

            if (RadiusVisual != null)
            {
                Vector3 s = RadiusVisual.localScale;
                Vector3 parentScale = RadiusVisual.parent != null ? RadiusVisual.parent.lossyScale : Vector3.one;
                float d = CaptureRadius * 2f;
                RadiusVisual.localScale = new Vector3(d / Mathf.Max(0.0001f, parentScale.x), s.y, d / Mathf.Max(0.0001f, parentScale.z));
            }

            ApplyVisuals();
        }

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
        }

        void OnDisable()
        {
            _all.Remove(this);
        }

        void Start()
        {
            if (WaveManager.Instance != null) WaveManager.Instance.OnWaveEnd += HandleWaveEnd;
        }

        void OnDestroy()
        {
            if (WaveManager.Instance != null) WaveManager.Instance.OnWaveEnd -= HandleWaveEnd;
            foreach (var m in _runeMaterials) if (m != null) Destroy(m);
        }

        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.IsGameOver;

        // Schrein eines Elements in der Szene (Netz-Id = ElementIndex)
        public static Shrine Find(int elementIndex)
        {
            foreach (var s in _all)
                if (s != null && s.ElementIndex == elementIndex) return s;
            return null;
        }

        // ---------------- Zustände (Server) ----------------

        public bool Awaken()
        {
            if (!Net.IsServer || State != ShrineState.Dormant || IsGameOver) return false;

            ProgressSeconds = 0f;
            PlayerInside = false;
            Contested = false;
            _reinforceTimer = 0f;
            SetState(ShrineState.Awakened);

            _engaged = false;
            if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);

            Debug.Log($"Shrine: {DisplayName} awakened.");
            OnAnyShrineAwakened?.Invoke(this);
            return true;
        }

        private void Complete()
        {
            if (!Net.IsServer || State != ShrineState.Awakened) return;

            ProgressSeconds = RequiredTime;
            StopSpawning();
            ReleaseAttackers();

            AbilityUnlocked = ApplyUnlockForTeam();
            ShrineBonuses.AddDamageBonus(ElementIndex, BuddyDamageBonus);

            SetState(ShrineState.Completed);
            Debug.Log($"Shrine: {DisplayName} completed (ability unlocked: {AbilityUnlocked}).");
            OnAnyShrineCompleted?.Invoke(this);
        }

        // Öffentlich, damit Manager/Debug einen Fehlschlag erzwingen können
        public void Fail()
        {
            if (!Net.IsServer || State != ShrineState.Awakened) return;

            StopSpawning();
            ReleaseAttackers();
            ProgressSeconds = 0f;
            PlayerInside = false;
            Contested = false;

            SetState(ShrineState.Dormant);
            Debug.Log($"Shrine: {DisplayName} failed – back to dormant.");
            OnAnyShrineFailed?.Invoke(this);
        }

        private void HandleWaveEnd()
        {
            if (!Net.IsServer) return;
            if (State == ShrineState.Awakened) Fail();
        }

        private void SetState(ShrineState s)
        {
            State = s;
            ApplyVisuals();
            if (Net.IsServer) NetGame.SendShrineState(ElementIndex, (int)s, ShrineBonuses.GetDamageBonus(ElementIndex));
            OnStateChanged?.Invoke(this);
        }

        // Alle Rechner: Element-Zauber für jede Spielfigur freischalten (und für später gespawnte Figuren merken).
        // Ergebnis: true, wenn die lokale Figur ihn neu gelernt hat (für den Toast).
        private bool ApplyUnlockForTeam()
        {
            if (ShrineManager.Instance != null) ShrineManager.Instance.MarkElementUnlocked(ElementIndex);
            bool localNew = false;
            bool anyNew = false;
            foreach (var a in PlayerAvatar.All)
            {
                if (a == null || a.Abilities == null) continue;
                bool fresh = a.Abilities.UnlockElementAbility(ElementIndex);
                anyNew |= fresh;
                if (a.IsLocal) localNew = fresh;
            }
            if (PlayerAvatar.All.Count == 0 && PlayerAbilities.Instance != null)
                return PlayerAbilities.Instance.UnlockElementAbility(ElementIndex); // ohne Netzwerk-Figuren
            return PlayerAvatar.Local != null ? localNew : anyNew;
        }

        // ---------------- Netz-Abbild (Clients) ----------------

        // Client: Zustandswechsel vom Server nachspielen (Optik + dieselben Ereignisse wie auf dem Server)
        public void NetApplyState(ShrineState s)
        {
            if (Net.IsServer || s == State) return;
            var old = State;
            if (s == ShrineState.Awakened)
            {
                ProgressSeconds = 0f;
                PlayerInside = false;
                Contested = false;
                SetState(s);
                OnAnyShrineAwakened?.Invoke(this);
            }
            else if (s == ShrineState.Completed)
            {
                ProgressSeconds = RequiredTime;
                AbilityUnlocked = ApplyUnlockForTeam();
                SetState(s);
                OnAnyShrineCompleted?.Invoke(this);
            }
            else
            {
                ProgressSeconds = 0f;
                PlayerInside = false;
                Contested = false;
                SetState(s);
                if (old == ShrineState.Awakened) OnAnyShrineFailed?.Invoke(this);
            }
        }

        // Client: Fortschritt vom Server
        public void NetApplyProgress(float progressSeconds, bool inside, bool contested)
        {
            if (Net.IsServer || State != ShrineState.Awakened) return;
            ProgressSeconds = progressSeconds;
            PlayerInside = inside;
            Contested = contested;
        }

        private void ApplyVisuals()
        {
            if (DormantVisuals != null) DormantVisuals.SetActive(State == ShrineState.Dormant);
            if (AwakenedVisuals != null) AwakenedVisuals.SetActive(State == ShrineState.Awakened);
            if (CompletedVisuals != null) CompletedVisuals.SetActive(State == ShrineState.Completed);
            if (RadiusVisual != null) RadiusVisual.gameObject.SetActive(State == ShrineState.Awakened);
            UpdateRunes();
        }

        // ---------------- Update ----------------

        void Update()
        {
            if (State == ShrineState.Awakened) UpdateRunes();
            if (State != ShrineState.Awakened || IsGameOver) return;
            // Ab hier entscheidet nur der Server
            if (!Net.IsServer) return;

            Vector3 c = CenterPosition;
            PlayerInside = FindPlayersInside(c);
            Contested = CheckContested(c);
            DamageEnemiesInCircle(c);

            // Erst beim ersten Betreten rücken die Angreifer an – vorher ist der Schrein frei
            if (PlayerInside && !_engaged)
            {
                _engaged = true;
                _reinforceTimer = 0f;
                if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
                _spawnRoutine = StartCoroutine(SpawnAttackersRoutine(AttackersPerWave, AttackerSpawnDuration, EngageDelay));
            }

            if (PlayerInside)
            {
                ProgressSeconds += Time.deltaTime * (Contested ? ContestedProgressRate : 1f);
            }
            else
            {
                ProgressSeconds = Mathf.Max(0f, ProgressSeconds - DecayPerSecond * Time.deltaTime);
            }

            if (ProgressSeconds >= RequiredTime)
            {
                Complete();
                return;
            }

            _syncTimer -= Time.unscaledDeltaTime;
            if (_syncTimer <= 0f)
            {
                _syncTimer = 0.1f;
                NetGame.SendCaptureProgress(true, ElementIndex, ProgressSeconds, PlayerInside, Contested);
            }

            if (_engaged && ReinforceInterval > 0f && ReinforceCount > 0)
            {
                _reinforceTimer += Time.deltaTime;
                if (_reinforceTimer >= ReinforceInterval)
                {
                    _reinforceTimer = 0f;
                    if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
                    _spawnRoutine = StartCoroutine(SpawnAttackersRoutine(ReinforceCount, 1.5f));
                }
            }
        }

        // Irgendeine lebende Spielfigur im Kreis? (ohne Netzwerk-Figuren: Fallback auf das Player-Tag)
        private bool FindPlayersInside(Vector3 c)
        {
            if (PlayerAvatar.All.Count > 0)
            {
                PlayerAvatar.InRadius(c, CaptureRadius, _inside);
                return _inside.Count > 0;
            }
            var p = GameObject.FindGameObjectWithTag("Player");
            return p != null && XZDistance(p.transform.position, c) <= CaptureRadius;
        }

        private static float XZDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Schrein-Aura: Gegner im Kreis nehmen Schaden (jeder Gegner nur einmal pro Frame)
        private void DamageEnemiesInCircle(Vector3 c)
        {
            if (AuraDps <= 0f) return;
            _auraHits.Clear();
            int n = Physics.OverlapCapsuleNonAlloc(c + Vector3.down * 5f, c + Vector3.up * 5f, CaptureRadius, _overlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = _overlap[i];
                if (col == null || !col.CompareTag("Enemy")) continue;
                if (XZDistance(col.transform.position, c) > CaptureRadius) continue;
                var dmg = col.GetComponentInParent<IDamageable>();
                if (dmg != null && _auraHits.Add(dmg)) dmg.TakeDamage(AuraDps * Time.deltaTime);
            }
        }

        private bool CheckContested(Vector3 c)
        {
            Vector3 bottom = c + Vector3.down * 5f;
            Vector3 top = c + Vector3.up * 5f;
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, CaptureRadius, _overlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = _overlap[i];
                if (col == null || !col.CompareTag("Enemy")) continue;
                if (XZDistance(col.transform.position, c) <= CaptureRadius) return true;
            }
            return false;
        }

        private void UpdateRunes()
        {
            if (_runeMaterials.Count == 0) return;

            Color baseColor = UseElementColor ? ElementInfo.GetColor(ElementIndex) : RuneColor;
            float intensity;
            switch (State)
            {
                case ShrineState.Awakened:
                    intensity = RuneIntensityAwakened * (0.55f + 0.45f * Mathf.Sin(Time.time * RunePulseSpeed));
                    break;
                case ShrineState.Completed:
                    intensity = RuneIntensityCompleted;
                    break;
                default:
                    intensity = 0f;
                    break;
            }

            Color e = baseColor * intensity;
            foreach (var m in _runeMaterials)
                if (m != null) m.SetColor(EmissionColorId, e);
        }

        // ---------------- Angreifer ----------------

        private IEnumerator SpawnAttackersRoutine(int count, float duration, float initialDelay)
        {
            if (initialDelay > 0f) yield return new WaitForSeconds(initialDelay);
            yield return SpawnAttackersRoutine(count, duration);
        }

        private IEnumerator SpawnAttackersRoutine(int count, float duration)
        {
            float interval = count > 1 ? duration / (count - 1) : 0f;
            for (int i = 0; i < count; i++)
            {
                if (State != ShrineState.Awakened || IsGameOver) yield break;
                SpawnAttacker();
                if (interval > 0f) yield return new WaitForSeconds(interval);
            }
            _spawnRoutine = null;
        }

        private void SpawnAttacker()
        {
            var wm = WaveManager.Instance;
            if (wm == null || !wm.IsWaveActive) return;

            EnemyConfigSO config = null;
            if (AttackerTypes != null && AttackerTypes.Length > 0)
                config = AttackerTypes[Random.Range(0, AttackerTypes.Length)];
            if (config == null) config = wm.GetDefaultEnemyType();
            if (config == null) return;

            if (!TryGetRingPoint(out Vector3 pos)) return;

            var brain = wm.SpawnEnemyAt(config, pos, -1f, AttackersCountTowardWave);
            if (brain != null)
            {
                brain.ForcedTarget = CenterTransform;
                _attackers.Add(brain);
            }
        }

        private bool TryGetRingPoint(out Vector3 pos)
        {
            Vector3 c = CenterPosition;
            float min = Mathf.Max(0f, Mathf.Min(SpawnRingMin, SpawnRingMax));
            float max = Mathf.Max(SpawnRingMin, SpawnRingMax);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(min, max);
                Vector3 p = c + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)
                    && XZDistance(hit.position, c) > CaptureRadius + 1f)
                {
                    pos = hit.position;
                    return true;
                }
            }

            if (NavMesh.SamplePosition(c, out NavMeshHit fallback, max, NavMesh.AllAreas))
            {
                pos = fallback.position;
                return true;
            }

            Debug.LogWarning($"Shrine: {DisplayName} found no NavMesh point for an attacker.");
            pos = c;
            return false;
        }

        private void StopSpawning()
        {
            if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        // Überlebende Angreifer ziehen danach normal zum Nexus weiter
        private void ReleaseAttackers()
        {
            foreach (var a in _attackers)
                if (a != null && a.ForcedTarget == CenterTransform) a.ForcedTarget = null;
            _attackers.Clear();
        }

        void OnDrawGizmosSelected()
        {
            Vector3 c = (Center != null ? Center : transform).position;
            Gizmos.color = ElementInfo.GetColor(ElementIndex);
            DrawCircle(c, CaptureRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
            DrawCircle(c, SpawnRingMin);
            DrawCircle(c, SpawnRingMax);
        }

        private static void DrawCircle(Vector3 c, float r)
        {
            const int seg = 48;
            Vector3 prev = c + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
