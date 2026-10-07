using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public enum MerchantKind
    {
        Waffen, // Primär- (LMB) und Sekundärfähigkeit (RMB)
        Feuer,  // R
        Eis,    // F
        Erde,   // C
        Licht   // V
    }

    public enum MerchantState
    {
        Geschlossen, // Stand zu, Plane über den Waren
        Aktiv,       // Händler da, Kreis muss eingenommen werden
        Eingenommen  // Karte gewählt bzw. Auswahl offen; schließt zum Wellenende
    }

    public static class MerchantInfo
    {
        public const int Count = 5;
        public static readonly string[] Names = { "Waffenhändler", "Feuerhändler", "Eishändler", "Erdhändler", "Lichthändler" };
        // Dateischlüssel (portrait_merchant_<key>.png, Haendler_<Typ>)
        public static readonly string[] Keys = { "waffen", "feuer", "eis", "erde", "licht" };
        public static readonly Color[] Colors =
        {
            new Color(0.9f, 0.25f, 0.18f), // Waffen: Rot (mit Gold-Akzent im Modell)
            new Color(1f, 0.45f, 0.15f),   // Feuer
            new Color(0.45f, 0.8f, 1f),    // Eis
            new Color(0.45f, 0.78f, 0.3f), // Erde: Grün
            new Color(1f, 0.92f, 0.5f),    // Licht
        };

        private static int Clamp(MerchantKind k) => Mathf.Clamp((int)k, 0, Count - 1);
        public static string Name(MerchantKind k) => Names[Clamp(k)];
        public static string Key(MerchantKind k) => Keys[Clamp(k)];
        public static Color GetColor(MerchantKind k) => Colors[Clamp(k)];

        // Element-Index (0 Feuer … 3 Licht), -1 für den Waffenhändler
        public static int ElementOf(MerchantKind k) => (int)k - 1;

        public static MerchantKind KindOfSlot(AbilitySlot slot)
        {
            int e = AbilitySlots.ElementOf(slot);
            return e < 0 ? MerchantKind.Waffen : (MerchantKind)(e + 1);
        }

        // Verkauft der Händler Karten für diese Taste?
        public static bool Sells(MerchantKind k, AbilitySlot slot) => KindOfSlot(slot) == k;

        // Was der Händler verbessert (Toasts, Ziel-Panel)
        public static string Goods(MerchantKind k)
        {
            if (k == MerchantKind.Waffen) return "Grund- und Zweitangriff";
            int e = ElementOf(k);
            var pa = PlayerAbilities.Instance;
            return pa != null ? pa.GetElementAbilityName(e) : ElementInfo.AbilityName(e);
        }
    }

    // Händlerstand in der Stadt. Wird vom MerchantManager zu Wellenbeginn geöffnet (Aktiv): Der Spieler muss
    // RequiredTime Sekunden im Kreis (CaptureRadius, XZ) stehen – gleiche Regeln wie beim Schrein (Shrine.cs):
    // Angreifer erst beim ersten Betreten, Gegner im Kreis = umkämpft (40 % Tempo), draußen Verfall.
    // Erfolg → Eingenommen, der MerchantManager öffnet die Kartenauswahl; zum Wellenende schließt der Stand wieder.
    // Endet die Welle vorher → Fehlschlag (Händler kommt zurück in den Beutel).
    // Mehrspieler: Nur der Server entscheidet (Einnahme, Angreifer, Aura). Im Kreis zählt jede lebende Figur
    // (PlayerAvatar.InRadius). Einnehmende = wer beim Abschluss im Kreis steht oder insgesamt mindestens
    // MerchantManager.MinCaptureContributionSeconds drin war (GoldRules.IsCaptureContributor). Zustand und Fortschritt
    // gehen per NetGame.SendMerchantState / SendCaptureProgress an die Clients (Optik, Objective-UI, Toasts).
    //
    // Optik-Vertrag (Kinder, vom Editor-Setup angelegt, Modelle austauschbar):
    //   Stand      – Marktstand (Front = lokales +Z, Richtung Kreis)
    //   Abdeckung  – Plane über den Waren, nur im Zustand Geschlossen sichtbar
    //   Figur      – Händler hinter der Theke (Animator optional, Trigger „Winken"/„Jubel"), nur offen sichtbar
    //   Kreis      – Einnahme-Kreis + Leuchtsäule (Partikel werden in Händlerfarbe getönt), nur Aktiv sichtbar
    //   Lampe      – Punktlicht für die Nacht (über SiegeAtmosphere.Lanterns gesteuert)
    public class Merchant : MonoBehaviour
    {
        private static readonly List<Merchant> _all = new List<Merchant>();
        public static IReadOnlyList<Merchant> All => _all;

        public static event System.Action<Merchant> OnAnyMerchantActivated;
        public static event System.Action<Merchant> OnAnyMerchantCaptured;
        public static event System.Action<Merchant> OnAnyMerchantFailed;

        [Header("Händler")]
        public MerchantKind Kind;
        [Tooltip("Leer → automatisch „Waffenhändler“ usw.")]
        public string DisplayName = "";

        [Header("Einnahme (wie Schrein)")]
        [Tooltip("Mittelpunkt des Kreises (Fallback: dieses Transform). Angreifer laufen hierhin.")]
        public Transform Center;
        public float CaptureRadius = 3.5f;
        [Tooltip("Sekunden, die der Spieler (unumkämpft) im Kreis stehen muss.")]
        public float RequiredTime = 15f;
        [Tooltip("Fortschrittsverlust pro Sekunde außerhalb des Kreises.")]
        public float DecayPerSecond = 0.5f;
        [Range(0f, 1f)] public float ContestedProgressRate = 0.4f;
        [Tooltip("Schaden pro Sekunde für Gegner im Kreis (0 = keine Aura).")]
        public float AuraDps = 0f;
        public float EngageDelay = 2f;

        [Header("Angreifer")]
        [Tooltip("Gegnertypen (zufällig). Leer → erster Gegnertyp der aktuellen Welle.")]
        public EnemyConfigSO[] AttackerTypes;
        public int AttackersPerWave = 4;
        public float AttackerSpawnDuration = 4f;
        public float SpawnRingMin = 10f;
        public float SpawnRingMax = 14f;
        public float ReinforceInterval = 18f;
        public int ReinforceCount = 2;
        public bool AttackersCountTowardWave = true;

        [Header("Optik (Kinder)")]
        public GameObject Stand;
        public GameObject Abdeckung;
        public GameObject Figur;
        public GameObject Kreis;
        public Light Lampe;
        [Tooltip("Optional: Animator der Figur (Trigger werden nur gesetzt, wenn der Controller sie kennt).")]
        public Animator FigureAnimator;
        public string WaveTrigger = "Winken";
        public string CheerTrigger = "Jubel";
        [Tooltip("Abstand zwischen zwei Wink-Animationen, solange der Stand aktiv ist (s).")]
        public float WaveInterval = 6f;
        [Tooltip("Partikel/Lichter im Kreis in Händlerfarbe tönen.")]
        public bool TintCircle = true;

        public MerchantState State { get; private set; } = MerchantState.Geschlossen;
        public float ProgressSeconds { get; private set; }
        public float Progress01 => RequiredTime > 0f ? Mathf.Clamp01(ProgressSeconds / RequiredTime) : 1f;
        public bool PlayerInside { get; private set; }
        public bool Contested { get; private set; }
        public bool IsActive => State == MerchantState.Aktiv;
        public bool IsCaptured => State == MerchantState.Eingenommen;
        public Color Color => MerchantInfo.GetColor(Kind);

        public event System.Action<Merchant> OnStateChanged;

        public Vector3 CenterPosition => (Center != null ? Center : transform).position;
        public Transform CenterTransform => Center != null ? Center : transform;

        private readonly List<PlayerAvatar> _inside = new List<PlayerAvatar>();
        // Server: Sekunden im Kreis pro Client-Id während der aktuellen Einnahme
        private readonly Dictionary<ulong, float> _presence = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> _insideAtEnd = new HashSet<ulong>();
        private float _syncTimer;
        private readonly List<EnemyBrain> _attackers = new List<EnemyBrain>();
        private readonly Collider[] _overlap = new Collider[64];
        private readonly HashSet<IDamageable> _auraHits = new HashSet<IDamageable>();
        private float _reinforceTimer;
        private float _waveTimer;
        private bool _engaged;
        private Coroutine _spawnRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            OnAnyMerchantActivated = null;
            OnAnyMerchantCaptured = null;
            OnAnyMerchantFailed = null;
        }

        void OnValidate()
        {
            if (string.IsNullOrEmpty(DisplayName) || System.Array.IndexOf(MerchantInfo.Names, DisplayName) >= 0)
                DisplayName = MerchantInfo.Name(Kind);
        }

        void Awake()
        {
            if (string.IsNullOrEmpty(DisplayName)) DisplayName = MerchantInfo.Name(Kind);
            if (TintCircle) TintCircleVisuals();
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
        }

        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.IsGameOver;

        // Händler einer Art in der Szene (Netz-Id = MerchantKind)
        public static Merchant Find(MerchantKind kind)
        {
            foreach (var m in _all)
                if (m != null && m.Kind == kind) return m;
            return null;
        }

        // ---------------- Zustände (Server) ----------------

        public bool Activate()
        {
            if (!Net.IsServer || State != MerchantState.Geschlossen || IsGameOver) return false;

            _presence.Clear();
            _insideAtEnd.Clear();
            ProgressSeconds = 0f;
            PlayerInside = false;
            Contested = false;
            _reinforceTimer = 0f;
            _waveTimer = 0f;
            _engaged = false;
            if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;

            SetState(MerchantState.Aktiv);
            Animate(WaveTrigger);
            Debug.Log($"Merchant: {DisplayName} opened.");
            OnAnyMerchantActivated?.Invoke(this);
            return true;
        }

        // Öffentlich für DevTools/Tests: Einnahme sofort abschließen
        public void Complete()
        {
            if (!Net.IsServer || State != MerchantState.Aktiv) return;
            ProgressSeconds = RequiredTime;
            _insideAtEnd.Clear();
            foreach (var a in _inside) if (a != null) _insideAtEnd.Add(a.OwnerClientId);
            StopSpawning();
            ReleaseAttackers();
            SetState(MerchantState.Eingenommen);
            Animate(CheerTrigger);
            Debug.Log($"Merchant: {DisplayName} captured.");
            OnAnyMerchantCaptured?.Invoke(this);
        }

        public void Fail()
        {
            if (!Net.IsServer || State != MerchantState.Aktiv) return;
            StopSpawning();
            ReleaseAttackers();
            ProgressSeconds = 0f;
            PlayerInside = false;
            Contested = false;
            SetState(MerchantState.Geschlossen);
            Debug.Log($"Merchant: {DisplayName} failed – closed again.");
            OnAnyMerchantFailed?.Invoke(this);
        }

        // Nach dem Kauf: zum Wellenende schließen
        public void Close()
        {
            if (!Net.IsServer) return;
            if (State == MerchantState.Aktiv) { Fail(); return; }
            if (State == MerchantState.Geschlossen) return;
            SetState(MerchantState.Geschlossen);
        }

        private void HandleWaveEnd()
        {
            if (!Net.IsServer) return;
            if (State == MerchantState.Aktiv) Fail();
            else if (State == MerchantState.Eingenommen) Close();
        }

        private void SetState(MerchantState s)
        {
            State = s;
            ApplyVisuals();
            if (Net.IsServer) NetGame.SendMerchantState((int)Kind, (int)s);
            OnStateChanged?.Invoke(this);
        }

        // Server: Client-Ids der Einnehmenden der letzten Einnahme (gültig ab Complete)
        public void GetCapturers(List<ulong> result, float minSeconds)
        {
            result.Clear();
            foreach (var kv in _presence)
                if (GoldRules.IsCaptureContributor(kv.Value, _insideAtEnd.Contains(kv.Key), minSeconds)) result.Add(kv.Key);
            foreach (ulong id in _insideAtEnd)
                if (!result.Contains(id)) result.Add(id);
        }

        // ---------------- Netz-Abbild (Clients) ----------------

        // Client: Zustandswechsel vom Server nachspielen (Optik + dieselben Ereignisse wie auf dem Server)
        public void NetApplyState(MerchantState s)
        {
            if (Net.IsServer || s == State) return;
            var old = State;
            if (s == MerchantState.Aktiv)
            {
                ProgressSeconds = 0f;
                PlayerInside = false;
                Contested = false;
                _waveTimer = 0f;
                SetState(s);
                Animate(WaveTrigger);
                OnAnyMerchantActivated?.Invoke(this);
            }
            else if (s == MerchantState.Eingenommen)
            {
                ProgressSeconds = RequiredTime;
                SetState(s);
                Animate(CheerTrigger);
                OnAnyMerchantCaptured?.Invoke(this);
            }
            else
            {
                ProgressSeconds = 0f;
                PlayerInside = false;
                Contested = false;
                SetState(s);
                if (old == MerchantState.Aktiv) OnAnyMerchantFailed?.Invoke(this);
            }
        }

        // Client: Fortschritt vom Server
        public void NetApplyProgress(float progressSeconds, bool inside, bool contested)
        {
            if (Net.IsServer || State != MerchantState.Aktiv) return;
            ProgressSeconds = progressSeconds;
            PlayerInside = inside;
            Contested = contested;
        }

        public void ApplyVisuals()
        {
            bool open = State != MerchantState.Geschlossen;
            if (Abdeckung != null) Abdeckung.SetActive(!open);
            if (Figur != null) Figur.SetActive(open);
            if (Kreis != null) Kreis.SetActive(State == MerchantState.Aktiv);
        }

        private void Animate(string trigger)
        {
            if (FigureAnimator == null || string.IsNullOrEmpty(trigger) || !FigureAnimator.isActiveAndEnabled) return;
            PlayerVisualAnimator.SetTriggerSafe(FigureAnimator, trigger);
        }

        // Partikel-Startfarben und Lichter im Kreis auf die Händlerfarbe setzen (Alpha bleibt)
        private void TintCircleVisuals()
        {
            if (Kreis == null) return;
            Color c = Color;
            foreach (var ps in Kreis.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var g = main.startColor;
                float a = g.mode == ParticleSystemGradientMode.Color ? g.color.a : 1f;
                main.startColor = new Color(c.r, c.g, c.b, a);
            }
            foreach (var l in Kreis.GetComponentsInChildren<Light>(true)) l.color = c;
        }

        // ---------------- Update ----------------

        void Update()
        {
            if (State != MerchantState.Aktiv || IsGameOver) return;

            if (WaveInterval > 0f)
            {
                _waveTimer += Time.deltaTime;
                if (_waveTimer >= WaveInterval && !PlayerInside)
                {
                    _waveTimer = 0f;
                    Animate(WaveTrigger);
                }
            }

            // Ab hier entscheidet nur der Server
            if (!Net.IsServer) return;

            Vector3 c = CenterPosition;
            PlayerInside = FindPlayersInside(c);
            foreach (var a in _inside)
            {
                if (a == null) continue;
                _presence.TryGetValue(a.OwnerClientId, out float t);
                _presence[a.OwnerClientId] = t + Time.deltaTime;
            }
            Contested = CheckContested(c);
            DamageEnemiesInCircle(c);

            // Erst beim ersten Betreten rücken die Angreifer an
            if (PlayerInside && !_engaged)
            {
                _engaged = true;
                _reinforceTimer = 0f;
                if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
                _spawnRoutine = StartCoroutine(SpawnAttackersRoutine(AttackersPerWave, AttackerSpawnDuration, EngageDelay));
            }

            if (PlayerInside) ProgressSeconds += Time.deltaTime * (Contested ? ContestedProgressRate : 1f);
            else ProgressSeconds = Mathf.Max(0f, ProgressSeconds - DecayPerSecond * Time.deltaTime);

            if (ProgressSeconds >= RequiredTime)
            {
                Complete();
                return;
            }

            _syncTimer -= Time.unscaledDeltaTime;
            if (_syncTimer <= 0f)
            {
                _syncTimer = 0.1f;
                NetGame.SendCaptureProgress(false, (int)Kind, ProgressSeconds, PlayerInside, Contested);
            }

            if (_engaged && ReinforceInterval > 0f && ReinforceCount > 0)
            {
                _reinforceTimer += Time.deltaTime;
                if (_reinforceTimer >= ReinforceInterval)
                {
                    _reinforceTimer = 0f;
                    if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
                    _spawnRoutine = StartCoroutine(SpawnAttackersRoutine(ReinforceCount, 1.5f, 0f));
                }
            }
        }

        // Lebende Spielfiguren im Kreis (ohne Netzwerk-Figuren: Fallback auf das Player-Tag, lokaler Spieler)
        private bool FindPlayersInside(Vector3 c)
        {
            if (PlayerAvatar.All.Count > 0)
            {
                PlayerAvatar.InRadius(c, CaptureRadius, _inside);
                return _inside.Count > 0;
            }
            _inside.Clear();
            var p = GameObject.FindGameObjectWithTag("Player");
            bool inside = p != null && XZDistance(p.transform.position, c) <= CaptureRadius;
            if (inside)
            {
                _presence.TryGetValue(Net.LocalClientId, out float t);
                _presence[Net.LocalClientId] = t + Time.deltaTime;
            }
            return inside;
        }

        private static float XZDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

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
            int n = Physics.OverlapCapsuleNonAlloc(c + Vector3.down * 5f, c + Vector3.up * 5f, CaptureRadius, _overlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = _overlap[i];
                if (col == null || !col.CompareTag("Enemy")) continue;
                if (XZDistance(col.transform.position, c) <= CaptureRadius) return true;
            }
            return false;
        }

        // ---------------- Angreifer (wie Schrein) ----------------

        public int AliveAttackers
        {
            get
            {
                int n = 0;
                foreach (var a in _attackers) if (a != null) n++;
                return n;
            }
        }

        private IEnumerator SpawnAttackersRoutine(int count, float duration, float initialDelay)
        {
            if (initialDelay > 0f) yield return new WaitForSeconds(initialDelay);
            float interval = count > 1 ? duration / (count - 1) : 0f;
            for (int i = 0; i < count; i++)
            {
                if (State != MerchantState.Aktiv || IsGameOver) yield break;
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
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(min, max);
                Vector3 p = c + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                // In der Stadt: nur Punkte, von denen ein vollständiger Weg zum Kreis führt (nicht in eingezäunten Gärten)
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)
                    && XZDistance(hit.position, c) > CaptureRadius + 1f && HasPath(hit.position, c))
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

            Debug.LogWarning($"Merchant: {DisplayName} found no NavMesh point for an attacker.");
            pos = c;
            return false;
        }

        private static NavMeshPath _path;
        private static bool HasPath(Vector3 from, Vector3 to)
        {
            if (_path == null) _path = new NavMeshPath();
            if (!NavMesh.SamplePosition(to, out NavMeshHit target, 3f, NavMesh.AllAreas)) return true;
            return NavMesh.CalculatePath(from, target.position, NavMesh.AllAreas, _path) && _path.status == NavMeshPathStatus.PathComplete;
        }

        private void StopSpawning()
        {
            if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        private void ReleaseAttackers()
        {
            foreach (var a in _attackers)
                if (a != null && a.ForcedTarget == CenterTransform) a.ForcedTarget = null;
            _attackers.Clear();
        }

        void OnDrawGizmosSelected()
        {
            Vector3 c = (Center != null ? Center : transform).position;
            Gizmos.color = MerchantInfo.GetColor(Kind);
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
