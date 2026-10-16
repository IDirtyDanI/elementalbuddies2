using UnityEngine;
using UnityEngine.AI;
using System.Collections;

namespace ElementalBuddies
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyBrain : MonoBehaviour, IDamageable, ISlowable, ITauntable, IHealthBarTarget
    {
        public EnemyConfigSO Config;

        [Header("Targeting")]
        [Tooltip("Enemies switch from the Nexus to the player while the player is within this radius.")]
        public float PlayerAggroRadius = 6f;
        [Tooltip("Attack range against units (player / taunt target), measured center to center.")]
        public float AttackRange = 1.5f;
        [Tooltip("Attack range against the Nexus, measured to the closest point of its collider.")]
        public float NexusAttackRange = 2.5f;

        [Header("Forced Target (z. B. Schrein-Angreifer)")]
        [Tooltip("Optional: statt zum Nexus läuft der Gegner hierhin und bleibt dort. Taunt und Player-Aggro haben weiter Vorrang.")]
        public Transform ForcedTarget;
        [Tooltip("Player-Aggro-Radius, solange ein ForcedTarget gesetzt ist (Schrein-Angreifer reagieren früher auf den Player).")]
        public float ForcedTargetPlayerAggroRadius = 10f;
        [Tooltip("Ab dieser Distanz zum ForcedTarget gilt der Gegner als angekommen (kein Anti-Cheese-Stuck).")]
        public float ForcedTargetArriveDistance = 2.5f;
        [Tooltip("Kommt ein Gegner so lange (s) nicht voran (< 1,5 m, kein Angriff), wird er an ein offenes Portal zurückgesetzt – z. B. auf einer abgeschnittenen NavMesh-Insel.")]
        public float StragglerRescueTime = 15f;

        [Header("HP-Bar")]
        [Tooltip("World-Space-HP-Bar (mit EnemyHealthBar), erscheint erst nach dem ersten Treffer.")]
        public EnemyHealthBar HealthBarPrefab;

        // Clients: aus den synchronisierten Werten (EnemyNet → ApplyRemoteHealth)
        public float CurrentHP => _currentHP;
        public float MaxHP => _maxHP;
        bool IHealthBarTarget.HealthBarVisible => true;

        // Status-Effekte (für Synergien, z. B. Blitz-Bonus auf nasse Gegner)
        // Komponenten können auch direkt per XyzEffect.Apply angehängt werden → bei Bedarf nachschlagen
        public bool IsWet
        {
            get
            {
                if (IsRemote) return HasRemoteStatus(EnemyNet.StatusWet);
                if (_wet == null) _wet = GetComponent<WetEffect>();
                return _wet != null && _wet.IsActive;
            }
        }
        public bool IsCursed
        {
            get
            {
                if (IsRemote) return HasRemoteStatus(EnemyNet.StatusCursed);
                if (_curse == null) _curse = GetComponent<CurseEffect>();
                return _curse != null && _curse.IsActive;
            }
        }
        public bool IsBurning
        {
            get
            {
                if (IsRemote) return HasRemoteStatus(EnemyNet.StatusBurning);
                if (_burn == null) _burn = GetComponent<BurnEffect>();
                return _burn != null && _burn.IsActive;
            }
        }
        // ---------------- Elite (Plan „Fesselung“ C4) ----------------
        public EliteAffix Elite { get; private set; }
        public bool IsElite => Elite != EliteAffix.None;
        private static readonly System.Collections.Generic.List<EnemyBrain> _shieldbearers = new System.Collections.Generic.List<EnemyBrain>();
        private float _speedFactor = 1f;

        // Server, vor Start: Tempo-Faktor (Belagerungsstufe, Blutmond); multipliziert sich mit Elite-Tempo
        public void SetSpeedFactor(float factor)
        {
            _speedFactor *= Mathf.Max(0.1f, factor);
        }
        private bool _started;
        // Server: eine Elite ist erschienen (für Hinweis-Toast und Sound)
        public static event System.Action<EnemyBrain> OnEliteSpawned;

        // Server, direkt nach Initialize und vor dem Netz-Spawn: Gegner wird zur Elite (mehr Leben, Eigenschaft)
        public void MakeElite(EliteAffix affix)
        {
            if (!Net.IsServer || affix == EliteAffix.None || IsBoss || IsElite) return;
            Elite = affix;
            _currentHP *= EliteInfo.Hp(affix);
            _maxHP = _currentHP;
            _speedFactor *= EliteInfo.SpeedFactor(affix);
            DamageMultiplier *= EliteInfo.Damage(affix);
            if (affix == EliteAffix.Shieldbearer && !_shieldbearers.Contains(this)) _shieldbearers.Add(this);
            SyncHealth();
            if (_started) ApplyEliteLook();
            OnEliteSpawned?.Invoke(this);
        }

        // Client: Eigenschaft vom Server (EnemyNet) – nur Optik
        public void ApplyRemoteElite(EliteAffix affix)
        {
            if (affix == EliteAffix.None || IsElite) return;
            Elite = affix;
            if (_started) ApplyEliteLook();
        }

        private void ApplyEliteLook()
        {
            Codex.Discover(Elite == EliteAffix.Ram ? Codex.EventKey(WaveEvent.Ram) : Codex.EliteKey(Elite));
            transform.localScale *= EliteInfo.Scale(Elite);
            EliteVisual.Attach(this, Elite);
        }

        // Feuerfest gegen Feuer-Buddies, Schildträger in der Nähe
        private float EliteDamageTakenFactor(ElementalBuddy source)
        {
            float f = 1f;
            if (Elite == EliteAffix.Fireproof && source != null && !source.IsFusion && source.ElementIndex == 0) f *= EliteInfo.FireproofTaken;
            if (_shieldbearers.Count > 0)
            {
                float r2 = EliteInfo.ShieldRadius * EliteInfo.ShieldRadius;
                foreach (var sb in _shieldbearers)
                {
                    if (sb == null || sb == this || sb._isDead) continue;
                    if ((sb.transform.position - transform.position).sqrMagnitude <= r2) { f *= EliteInfo.ShieldTaken; break; }
                }
            }
            return f;
        }

        // Gespottet (Erd-Buddy) bzw. verlangsamt – für Kartensynergien (nur Server-Zustand)
        public bool IsTaunted => _tauntTarget != null;
        public bool IsSlowed => IsRemote ? HasRemoteStatus(EnemyNet.StatusSlowed) : Time.time < _slowUntil && _slowPercent > 0f;
        // Schadensreduktion 0..0.9 aus der Config (Fluch ignoriert sie)
        public float Armor => Config != null ? Mathf.Clamp(Config.Armor, 0f, 0.9f) : 0f;

        private IEnemyMotor _motor;
        private FlyingMotor _flyer; // nur Flieger (Config.IsFlyer)
        private float _maxHP;
        private float _slowPercent;
        private float _slowUntil;
        private FreezeEffect _freeze;
        private bool _wasFrozen;
        private WetEffect _wet;
        private CurseEffect _curse;
        private BurnEffect _burn;
        private Coroutine _knockbackRoutine;
        private bool _knockedBack;
        private Transform _tauntTarget;
        private float _currentHP;
        private float _baseSpeed;

        // Anti-Cheese
        private float _stuckTimer;
        private Vector3 _progressPos;
        private float _noProgressTimer;
        private bool _blockedByBuddy;

        // Buddy-Ziel (gecacht, alle BuddyRescanInterval s neu bewertet)
        private const float BuddyRescanInterval = 0.4f;
        private ElementalBuddy _buddyTarget;
        private float _nextBuddyScan;
        private ElementalBuddy _destBuddy;
        private Vector3 _buddyDest;
        private float _nextDestUpdate;
        private Transform _lookupTransform;
        private ElementalBuddy _lookupBuddy;

        // Patt-Schutz: kommt ein Angreifer bei einem Buddy nicht voran (z. B. Fernkampf-Boss gegen einen sich
        // selbst heilenden Licht-Buddy außer Turmreichweite), wird dieser Buddy eine Weile ignoriert → weiter zum Nexus
        private const float BuddyStallTime = 10f;     // so lange ohne neuen Tiefstwert der Ziel-HP → aufgeben
        private const float BuddyStallProgress = 0.05f; // Fortschritt = Ziel-HP mindestens 5 % (vom Max) unter dem bisherigen Tiefstwert
        private const float BuddyIgnoreTime = 20f;
        private ElementalBuddy _stallBuddy;
        private float _stallLowestFraction;
        private float _stallSince;
        private ElementalBuddy _ignoredBuddy;
        private float _ignoreBuddyUntil;

        // Fernkampf / Angriffs-Animation
        private float _nextAttackTime;
        private bool _shotPending;
        private float _shotFireTime;
        private Transform _shotTarget;

        // Nahkampf (2026-10-12): einzelne, angekündigte Hiebe statt Dauerschaden. Ausholen (Config.AttackWindup) → ein Treffer.
        // AttackDamage bleibt ein DPS-Wert: Schaden pro Hieb = AttackDamage × AttackInterval (× DamageMultiplier, gegen Buddies
        // × BuddyDamageMultiplier) → gleicher DPS wie früher, solange das Ziel in Reichweite bleibt.
        private const float MeleeHitTolerance = 0.4f;   // beim Treffer: Reichweite + Toleranz (Ziel darf leicht zurückweichen)
        private const float SwingCancelCooldown = 0.3f; // abgebrochener Hieb → kurze Pause bis zum nächsten
        private const float SwingRecovery = 0.35f;      // nach dem Treffer noch so lange stehen bleiben (höchstens 0,6 × Intervall)
        private bool _swingPending;
        private bool _swingVsBlocker;                   // Anti-Cheese-Hieb gegen einen blockierenden Buddy (nicht das Ziel)
        private float _swingHitTime;
        private float _swingHoldUntil;
        private Transform _swingTarget;
        private Collider _swingBlocker;
        private ElementalBuddy _swingBuddy;
        private PlayerStats _swingPlayer;
        private float _attackSfxTime = -1f;             // Fernkampf-Abschusston (Config.AttackSound) nach AttackWindup
        private float _remoteSwingSfxTime = -1f;        // Client: Trefferton nach dem Ausholen (aus dem Angriffs-RPC)
        // Trefferton global begrenzt (50 Schwarm-Skelette sollen nicht 50-mal gleichzeitig klappern)
        private const int MeleeSfxPerWindow = 3;
        private const float MeleeSfxWindow = 0.1f;
        private static float _meleeSfxWindowStart = -1f;
        private static int _meleeSfxCount;

        // Animator (optional, Parameter nur wenn vorhanden)
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MovingHash = Animator.StringToHash("Moving");
        private Animator _animator;
        private bool _hasAttackTrigger, _hasSpeedFloat, _hasMovingBool;

        // Mehrspieler: Netzwerk-Anker; IsRemote = reiner Client (nur Abbild, keine KI, kein Zustand ändern)
        private EnemyNet _net;
        private Vector3 _prefabScale = Vector3.one;
        private bool IsRemote => Net.IsClientOnly;
        private bool HasRemoteStatus(byte flag) => _net != null && _net.IsSpawned && _net.HasStatus(flag);
        private bool _remoteFrozenAnim;
        private float _remoteAnimSpeed = 1f;

        private bool IsRanged => Config != null && Config.ProjectilePrefab != null;
        // Einzelhiebe: alle Nahkämpfer außer der Belagerungsramme (die macht weiter Dauerschaden am Nexus, Takt über RamVisual)
        private bool UsesMeleeSwings => !IsRanged && Elite != EliteAffix.Ram;
        private float MeleeWindup => Config != null ? Mathf.Max(0f, Config.AttackWindup) : 0.35f;
        // Server: holt gerade zu einem Nahkampf-Hieb aus (BossBrain startet dann keinen Zauber)
        public bool IsMeleeSwinging => _swingPending;
        // Reichweite gegen Einheiten (Spieler, Buddies, Spott-Ziel)
        private float UnitAttackRange => Config != null && Config.AttackRange > 0f ? Config.AttackRange : AttackRange;
        private float NexusRange => Config != null && Config.AttackRange > 0f ? Mathf.Max(NexusAttackRange, Config.AttackRange) : NexusAttackRange;
        private float BuddyDamageMultiplier => Config != null ? Config.BuddyDamageMultiplier : 1f;
        
        public static event System.Action OnEnemyDeath;
        // Wie OnEnemyDeath, aber mit dem getöteten Gegner (z. B. Kopfgeld je Gegnertyp)
        public static event System.Action<EnemyBrain> OnEnemyKilled;
        // Boss ist erschienen (in Start, nur Config.IsBoss); Boss-Tod läuft über OnEnemyKilled
        public static event System.Action<EnemyBrain> OnBossSpawned;
        // Gegner steht zum ersten Mal in Angriffsreichweite des Nexus (Telemetrie „durchgekommen“)
        public static event System.Action<EnemyBrain> OnReachedNexus;
        // Tatsächlich abgezogene HP (höchstens Rest-HP) – Ziel, Menge, vom Spieler/Champion (sonst Türme/Rest)
        public static event System.Action<EnemyBrain, float, bool> OnDamageDealt;
        // Treffer-Rückmeldung auf JEDEM Rechner (Server: echte Treffer; Clients: aus der HP-Synchronisation, Quelle unbekannt = false)
        // – Ziel, Menge, vom Champion, tödlich. Nur für Optik/Sound (Schadenszahlen, Aufblitzen, Treffer-Stopp).
        public static event System.Action<EnemyBrain, float, bool, bool> OnLocalHit;
        // Server: Buddy, der gerade Schaden austeilt (ElementalBuddy.Update / BuddyProjectile) – für die Run-Statistik
        // („bester Buddy“), auszuwerten in OnDamageDealt. Nicht zugeordnet: DoTs, Fusions-Geschosse, Schrein-Auren.
        public static ElementalBuddy DamageSource;

        // Schadensmultiplikator dieses Gegners (Wellen-Rampe × Schwierigkeit, aus Initialize): Nahkampf, Fernkampf,
        // Kontaktschaden an Buddies und Boss-Fähigkeiten (BossBrain)
        public float DamageMultiplier { get; private set; } = 1f;
        public bool ReachedNexus { get; private set; }

        // Spieler-Treffer (Champion-Angriffe/-Zauber) laufen über DealPlayerDamage → Quelle für die Telemetrie
        private static int _playerSourceDepth;
        public static void DealPlayerDamage(IDamageable target, float amount)
        {
            if (target == null || !Net.IsServer) return; // Treffer entscheidet nur der Server
            _playerSourceDepth++;
            try { target.TakeDamage(amount); }
            finally { _playerSourceDepth--; }
        }
        private bool _isDead;
        public bool IsDead => _isDead;

        // Lebende Bosse in Spawn-Reihenfolge (für die Boss-HP-Leiste)
        private static readonly System.Collections.Generic.List<EnemyBrain> _activeBosses = new System.Collections.Generic.List<EnemyBrain>();
        public static System.Collections.Generic.IReadOnlyList<EnemyBrain> ActiveBosses => _activeBosses;
        // Alle aktiven Gegner (Server und Clients) – für die Minimap, ohne FindObjectsByType pro Takt
        private static readonly System.Collections.Generic.List<EnemyBrain> _all = new System.Collections.Generic.List<EnemyBrain>();
        public static System.Collections.Generic.IReadOnlyList<EnemyBrain> All => _all;
        public bool IsBoss => Config != null && Config.IsBoss;
        public string DisplayName => Config != null && !string.IsNullOrEmpty(Config.DisplayName) ? Config.DisplayName : name;

        // Kontroll-Resistenz (Bosse): 1 = volle Dauer
        private float ControlFactor => Config != null ? 1f - Mathf.Clamp(Config.ControlResistance, 0f, 0.9f) : 1f;

        // Zauber-Sperre (BossBrain): steht still, kein Grundangriff
        private float _castUntil;
        public bool IsCasting => !_isDead && (IsRemote ? HasRemoteStatus(EnemyNet.StatusCasting) : Time.time < _castUntil);

        // Aktuelles Ziel (Taunt > Buddy-Jäger > Spieler > Buddy > ForcedTarget > Nexus), pro Frame gecacht
        private Transform _cachedTarget;
        private int _cachedTargetFrame = -1;
        public Transform CurrentTarget
        {
            get
            {
                if (_cachedTargetFrame != Time.frameCount)
                {
                    _cachedTargetFrame = Time.frameCount;
                    _cachedTarget = GetCurrentTarget();
                }
                return _cachedTarget;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnEnemyDeath = null;
            OnEnemyKilled = null;
            OnBossSpawned = null;
            OnReachedNexus = null;
            OnDamageDealt = null;
            OnLocalHit = null;
            DamageSource = null;
            OnEliteSpawned = null;
            _shieldbearers.Clear();
            _playerSourceDepth = 0;
            _activeBosses.Clear();
            _all.Clear();
            _meleeSfxWindowStart = -1f;
            _meleeSfxCount = 0;
        }

        void Awake()
        {
            _net = GetComponent<EnemyNet>();
            // Prefab-Größe merken: VisualScale wird absolut gesetzt (Clients bekommen evtl. schon skalierte Werte)
            _prefabScale = transform.localScale;
        }

        // ---------------- Bewegung (Motor) ----------------

        // Flieger (Walddorf-Plan): FlyingMotor statt NavMesh, Trefferkapsel, Schatten, horizontale Reichweiten
        public bool IsFlyer => Config != null && Config.IsFlyer;
        // Bewegungsbaustein: NavAgentMotor (bisheriger NavMeshAgent, Bodengegner) oder FlyingMotor
        public IEnemyMotor Motor
        {
            get
            {
                EnsureMotor();
                return _motor;
            }
        }
        public FlyingMotor Flyer
        {
            get
            {
                EnsureMotor();
                return _flyer;
            }
        }
        // Körperradius in Weltmetern (Trefferbreite, z. B. Linien-Fähigkeiten)
        public float BodyRadius => Motor != null ? Motor.Radius : 0.4f;

        // Sofort anhalten (Festhalten, z. B. Weltenbaum-Wurzeln): Tempo 0 bis zum nächsten UpdateSpeed
        public void HaltMotor()
        {
            var m = Motor;
            if (m == null || !m.IsReady) return;
            m.Speed = 0f;
            m.Velocity = Vector3.zero;
        }

        // Motor einmalig wählen (Start; vorher z. B. aus Freeze/Stun). Config muss gesetzt sein (WaveManager setzt sie
        // direkt nach Instantiate, Clients haben sie aus dem Prefab).
        private void EnsureMotor()
        {
            if (_motor != null) return;
            var agent = GetComponent<NavMeshAgent>();
            if (IsFlyer)
            {
                if (agent != null && agent.enabled) agent.enabled = false; // bleibt als Komponente (RequireComponent)
                _flyer = GetComponent<FlyingMotor>();
                if (_flyer == null) _flyer = gameObject.AddComponent<FlyingMotor>();
                _flyer.Init(this, Config);
                _motor = _flyer;
                if (GetComponent<FlyerHurtbox>() == null) gameObject.AddComponent<FlyerHurtbox>();
                if (GetComponent<FlyerShadow>() == null) gameObject.AddComponent<FlyerShadow>();
            }
            else _motor = new NavAgentMotor(agent);
        }

        // Abstand für Aggro/Reichweite: Flieger horizontal (schweben über dem Ziel), Bodengegner wie bisher 3D
        private float UnitDistance(Vector3 a, Vector3 b) => IsFlyer ? HorizontalDistance(a, b) : Vector3.Distance(a, b);

        void Start()
        {
            EnsureMotor();

            if (Config != null)
            {
                if (_currentHP <= 0) _currentHP = Config.BaseHP;
                _motor.Speed = Config.Speed * _speedFactor;
                _baseSpeed = Config.Speed * _speedFactor;
            }
            else
            {
                if (_currentHP <= 0) _currentHP = 60f;
                _motor.Speed = 3.5f;
                _baseSpeed = 3.5f;
            }
            if (_maxHP <= 0f) _maxHP = _currentHP;
            if (IsRemote) _motor.DisableSimulation(); // Clients: Position kommt vom Server

            // Gegnertyp-Größe (vor der HP-Bar, die ihre Höhe beim Binden misst); Skalierung wird nicht synchronisiert
            if (Config != null && Config.VisualScale > 0f && !Mathf.Approximately(Config.VisualScale, 1f))
                transform.localScale = _prefabScale * Config.VisualScale;

            _started = true;
            if (IsElite) ApplyEliteLook(); // vor der HP-Leiste (misst die Höhe)
            // Ramme hat keinen eigenen Gegner-Eintrag – sie wird als Ereignis entdeckt (ApplyEliteLook)
            if (Elite != EliteAffix.Ram) Codex.Discover(Codex.EnemyKey(Config));

            if (HealthBarPrefab != null)
                Instantiate(HealthBarPrefab).Bind(this);

            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.nameHash == AttackHash && p.type == AnimatorControllerParameterType.Trigger) _hasAttackTrigger = true;
                    else if (p.nameHash == SpeedHash && p.type == AnimatorControllerParameterType.Float) _hasSpeedFloat = true;
                    else if (p.nameHash == MovingHash && p.type == AnimatorControllerParameterType.Bool) _hasMovingBool = true;
                }
            }

            if (IsBoss && !_isDead)
            {
                if (!_activeBosses.Contains(this)) _activeBosses.Add(this);
                OnBossSpawned?.Invoke(this);
            }
            // Ruf beim Erscheinen (Walddorf: Wolfsheulen, Flieger-Schrei) – lokal auf allen Rechnern, Drosselung über GameAudio
            if (Config != null && Config.SpawnSound && !_isDead) GameAudio.Play(Config.SpawnSfx, transform.position);
        }

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
        }

        void OnDisable()
        {
            _all.Remove(this);
        }

        void OnDestroy()
        {
            _all.Remove(this);
            _activeBosses.Remove(this);
            _shieldbearers.Remove(this);
        }

        // Wellen-Skalierung (WaveManager): HP = BaseHP × hpMultiplier (Bosse: Boss-Kurve, siehe WaveManager.HpMultiplierFor),
        // Schaden × damageMultiplier
        public void Initialize(float hpMultiplier, float damageMultiplier = 1f)
        {
             float baseHp = Config != null ? Config.BaseHP : 60f;
             _currentHP = Mathf.Max(1f, baseHp * Mathf.Max(0f, hpMultiplier));
             _maxHP = _currentHP;
             DamageMultiplier = Mathf.Max(0f, damageMultiplier);
             SyncHealth();
        }

        // ---------------- Mehrspieler ----------------

        // Server: HP an die Clients
        private void SyncHealth()
        {
            if (_net != null && Net.IsServer) _net.ServerSetHealth(_currentHP, _maxHP);
        }

        // Client: synchronisierte HP übernehmen (EnemyNet)
        public void ApplyRemoteHealth(float current, float max)
        {
            if (!IsRemote) return;
            float before = _currentHP, beforeMax = _maxHP;
            _currentHP = current;
            if (max > 0f) _maxHP = max;
            // Nur echte Treffer melden (nicht die Erst-Synchronisation, bei der sich auch das Max-Leben setzt)
            if (current < before && before > 0f && Mathf.Approximately(beforeMax, _maxHP) && OnLocalHit != null) OnLocalHit(this, before - current, false, current <= 0f);
        }

        // Client: Server meldet den Tod (vor dem Despawn) → Ereignisse lokal feuern (HUD, Erfolge, Boss-Leiste)
        public void HandleRemoteKilled()
        {
            if (!IsRemote || _isDead) return;
            _isDead = true;
            _currentHP = 0f;
            _activeBosses.Remove(this);
            PlayDeathVisual();
            OnEnemyDeath?.Invoke();
            OnEnemyKilled?.Invoke(this);
            PlayDeathSfx();
        }

        // Todeslaut: eigener Ton der Config (Walddorf) oder wie bisher der Knochenbruch
        private void PlayDeathSfx()
        {
            if (Config != null && Config.DeathSound) GameAudio.PlayPitched(Config.DeathSfx, transform.position, Random.Range(0.95f, 1.05f));
            else GameAudio.Play(SfxId.EnemyDeath, transform.position);
        }

        // Optionaler Tod-Clip (Config.DeathClipDuration > 0 und Animator-Trigger „Die“ an einem Kind-Modell): Modell
        // lösen, Clip als reine Optik abspielen (EnemyCorpse), Flieger stürzen ab. Der Gegner verschwindet wie bisher sofort.
        private static readonly int DieHash = Animator.StringToHash("Die");
        private void PlayDeathVisual()
        {
            if (Config == null || Config.DeathClipDuration <= 0f || _animator == null || !_animator.isActiveAndEnabled) return;
            Transform model = _animator.transform;
            if (model == transform) return;
            bool hasDie = false;
            foreach (var p in _animator.parameters)
                if (p.nameHash == DieHash && p.type == AnimatorControllerParameterType.Trigger) { hasDie = true; break; }
            if (!hasDie) return;
            model.SetParent(null, true);
            _animator.speed = 1f;
            _animator.SetTrigger(DieHash);
            model.gameObject.AddComponent<EnemyCorpse>().Init(Config.DeathClipDuration, IsFlyer);
        }

        // Angriffs-Takt für Optik ohne Animator (z. B. RamVisual: Rammbalken) – Server und Clients, unabhängig vom Animator
        public event System.Action OnAttackVisual;

        // Angriffs-Animation (Server direkt, Clients per RPC im Server-Takt)
        public void PlayAttackTrigger()
        {
            OnAttackVisual?.Invoke();
            // Angriffston der Config (Walddorf): Fernkampf beim Abschuss (nach AttackWindup), sonst sofort (Ausholen/Sturzflug)
            if (Config != null && Config.AttackSound)
            {
                if (IsRanged && Config.AttackWindup > 0f) _attackSfxTime = Time.time + Config.AttackWindup;
                else GameAudio.PlayPitched(Config.AttackSfx, transform.position, Random.Range(0.95f, 1.05f));
            }
            // Client: Trefferton des Nahkampf-Hiebs nach dem Ausholen nachspielen (Server spielt ihn in ResolveSwing).
            // Kein eigenes RPC für den Treffer – die Clients kennen AttackWindup aus der Config.
            if (IsRemote && UsesMeleeSwings && Config != null && Config.MeleeHitSound) _remoteSwingSfxTime = Time.time + MeleeWindup;
            if (_animator == null || !_hasAttackTrigger || !_animator.isActiveAndEnabled) return;
            _animator.SetTrigger(AttackHash);
        }

        // Server: Angriffs-Takt auslösen, wenn ihn jemand anzeigt (Attack-Trigger am Animator oder OnAttackVisual)
        private bool HasAttackVisual => (_hasAttackTrigger && _animator.isActiveAndEnabled) || OnAttackVisual != null;

        // Client: beliebiger Animator-Trigger (Boss-Zauber); unbekannte Parameter werden ignoriert
        public void PlayAnimTrigger(int hash)
        {
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            foreach (var p in _animator.parameters)
            {
                if (p.nameHash != hash || p.type != AnimatorControllerParameterType.Trigger) continue;
                _animator.SetTrigger(hash);
                return;
            }
        }

        // Server: Status-Flags für die Clients (Anzeige)
        private void SyncStatus()
        {
            if (_net == null || !_net.IsSpawned) return;
            byte f = 0;
            if (IsFrozen) f |= EnemyNet.StatusFrozen;
            if (IsStunned) f |= EnemyNet.StatusStunned;
            if (Time.time < _slowUntil && _slowPercent > 0f) f |= EnemyNet.StatusSlowed;
            if (IsWet) f |= EnemyNet.StatusWet;
            if (IsCursed) f |= EnemyNet.StatusCursed;
            if (IsBurning) f |= EnemyNet.StatusBurning;
            if (IsCasting) f |= EnemyNet.StatusCasting;
            _net.ServerSetStatus(f);
        }

        // Client: Animation aus den synchronisierten Werten (Laufgeschwindigkeit, eingefroren, verlangsamt)
        private void RemoteUpdate()
        {
            if (_isDead) return;
            bool frozen = IsFrozen;
            if (_remoteSwingSfxTime >= 0f)
            {
                // Betäubt/eingefroren/zaubert → der Server hat den Hieb abgebrochen (kein Ton); die Animation läuft einfach aus
                if (frozen || IsStunned || IsCasting) _remoteSwingSfxTime = -1f;
                else if (Time.time >= _remoteSwingSfxTime)
                {
                    _remoteSwingSfxTime = -1f;
                    PlayMeleeHitSfx();
                }
            }
            float speed = frozen || IsStunned ? 0f : (_net != null && _net.IsSpawned ? _net.MoveSpeed.Value : 0f);
            UpdateAnimator(speed);
            if (_animator == null) return;
            // Eingefroren: Animation steht; verlangsamt: etwas träger
            float animSpeed = frozen ? 0f : (HasRemoteStatus(EnemyNet.StatusSlowed) ? 0.7f : 1f);
            if (!Mathf.Approximately(animSpeed, _remoteAnimSpeed) || frozen != _remoteFrozenAnim)
            {
                _remoteAnimSpeed = animSpeed;
                _remoteFrozenAnim = frozen;
                _animator.speed = animSpeed;
            }
        }

        // Nächste lebende Spielfigur (Mehrspieler); ohne Netz-Figuren Fallback auf das Player-Tag
        private Transform NearestPlayer()
        {
            if (PlayerAvatar.All.Count > 0)
            {
                var a = PlayerAvatar.Nearest(transform.position);
                return a != null ? a.transform : null;
            }
            if (_fallbackPlayer == null && Time.time >= _nextFallbackPlayerLookup)
            {
                _nextFallbackPlayerLookup = Time.time + 1f;
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) _fallbackPlayer = go.transform;
            }
            return _fallbackPlayer;
        }
        private Transform _fallbackPlayer;
        private float _nextFallbackPlayerLookup;

        void Update()
        {
            if (_attackSfxTime >= 0f && Time.time >= _attackSfxTime)
            {
                _attackSfxTime = -1f;
                if (!_isDead && !IsFrozen && !IsStunned && Config != null) GameAudio.PlayPitched(Config.AttackSfx, transform.position, Random.Range(0.95f, 1.05f));
            }
            if (IsRemote)
            {
                RemoteUpdate();
                return;
            }
            ServerUpdate();
            SyncStatus();
        }

        private void ServerUpdate()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver)
            {
                if (_motor.IsReady && !_motor.IsStopped) _motor.IsStopped = true;
                _shotPending = false;
                CancelSwing();
                UpdateAnimator(0f);
                return;
            }

            if (IsFrozen || IsStunned)
            {
                _shotPending = false; // laufender Schuss bricht ab
                CancelSwing();        // ebenso ein laufender Nahkampf-Hieb
                UpdateAnimator(0f);
                _wasFrozen = true;
                if (_motor.IsReady) { _motor.IsStopped = true; _motor.Velocity = Vector3.zero; }
                return;
            }
            if (_wasFrozen)
            {
                _wasFrozen = false;
                _stuckTimer = 0f;
                if (_motor.IsReady && !_knockedBack) _motor.IsStopped = false;
            }

            // Boss-Zauber: steht still, kein Grundangriff (Neustart des Agents über _wasFrozen)
            if (IsCasting)
            {
                _shotPending = false;
                CancelSwing();
                _wasFrozen = true;
                _stuckTimer = 0f;
                _noProgressTimer = 0f;
                UpdateAnimator(0f);
                if (_motor.IsReady) { _motor.IsStopped = true; _motor.Velocity = Vector3.zero; }
                return;
            }

            // Rückstoß: Bewegung und Angriff kurz unterbrochen
            if (_knockedBack)
            {
                if (_motor.IsReady) _motor.IsStopped = true;
                _stuckTimer = 0f;
                _shotPending = false;
                CancelSwing();
                UpdateAnimator(0f);
                return;
            }

            UpdateSpeed();
            HandleMovement();
            HandleAntiCheese();
            HandleStraggler();
            HandleAttack();
            UpdateAnimator(_motor.Velocity.magnitude);
        }

        private void UpdateAnimator(float speed)
        {
            if (_net != null && !IsRemote) _net.ServerSetMoveSpeed(speed);
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            if (_hasSpeedFloat) _animator.SetFloat(SpeedHash, speed);
            if (_hasMovingBool) _animator.SetBool(MovingHash, speed > 0.1f);
        }

        // Nachzügler-Rettung: Ein Gegner, der lange weder läuft noch angreift, kann die Welle sonst endlos blockieren
        private void HandleStraggler()
        {
            if (StragglerRescueTime <= 0f) return;
            bool blocked = _blockedByBuddy;
            _blockedByBuddy = false;
            if (blocked || InAttackPosition(GetCurrentTarget()) || (transform.position - _progressPos).sqrMagnitude > 2.25f)
            {
                _progressPos = transform.position;
                _noProgressTimer = 0f;
                return;
            }

            _noProgressTimer += Time.deltaTime;
            if (_noProgressTimer < StragglerRescueTime) return;

            _noProgressTimer = 0f;
            var wm = WaveManager.Instance;
            if (wm == null || !wm.TryGetRescuePosition(out Vector3 pos)) return;
            Vector3 rescue;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas)) rescue = hit.position;
            else if (IsFlyer) rescue = pos; // Flieger brauchen kein NavMesh
            else return;

            Debug.LogWarning($"EnemyBrain: {name} hing bei {transform.position} fest – zurück an ein Portal.");
            ForcedTarget = null;
            _motor.Warp(rescue);
            _progressPos = IsFlyer ? transform.position : rescue;
            _stuckTimer = 0f;
        }

        // Priority: Taunt > Buddy (Jäger) > Player (within aggro radius) > Buddy > ForcedTarget > Nexus > Player (fallback if no Nexus)
        private Transform GetCurrentTarget()
        {
            // Belagerungsramme: nur der Nexus zählt (kein Spott, keine Spieler/Buddies)
            if (Elite == EliteAffix.Ram && Nexus.Instance != null) return Nexus.Instance.transform;
            if (_tauntTarget != null && _tauntTarget.gameObject.activeInHierarchy) return _tauntTarget;

            // Flieger ignorieren Buddies (überfliegen sie); Spott wirkt weiter (oben)
            ElementalBuddy buddy = IsFlyer ? null : GetBuddyTarget();
            bool hunter = Config != null && Config.HuntsBuddies;
            if (hunter && buddy != null) return buddy.transform;

            // Mehrspieler: nächste lebende Spielfigur
            Transform player = NearestPlayer();
            if (player != null)
            {
                float aggro = ForcedTarget != null ? Mathf.Max(PlayerAggroRadius, ForcedTargetPlayerAggroRadius) : PlayerAggroRadius;
                float playerDist = UnitDistance(transform.position, player.position);
                if (playerDist <= aggro) return player;
            }

            if (buddy != null) return buddy.transform;

            if (ForcedTarget != null) return ForcedTarget;

            if (Nexus.Instance != null) return Nexus.Instance.transform;

            return player;
        }

        private bool IsNexus(Transform target)
        {
            return Nexus.Instance != null && target == Nexus.Instance.transform;
        }

        private static bool IsValidBuddy(ElementalBuddy b) =>
            b != null && b.isActiveAndEnabled && !b.IsDead && b.CurrentHP > 0f;

        // Nächster lebender Buddy im BuddyAggroRadius (gecacht; zerstörte Ziele fallen sofort weg)
        private ElementalBuddy GetBuddyTarget()
        {
            float radius = Config != null ? Config.BuddyAggroRadius : 0f;
            if (radius <= 0f) return null;

            if ((object)_buddyTarget != null && !IsValidBuddy(_buddyTarget))
            {
                _buddyTarget = null;
                _nextBuddyScan = 0f;
            }
            if (Time.time >= _nextBuddyScan)
            {
                _nextBuddyScan = Time.time + BuddyRescanInterval;
                _buddyTarget = FindBuddyTarget(radius);
            }
            TrackBuddyStall();
            return _buddyTarget;
        }

        // Kein Fortschritt am aktuellen Buddy-Ziel → Ziel für BuddyIgnoreTime s ignorieren
        private void TrackBuddyStall()
        {
            var b = _buddyTarget;
            if (b == null)
            {
                _stallBuddy = null;
                return;
            }
            float frac = b.MaxHP > 0f ? b.CurrentHP / b.MaxHP : 1f;
            if (b != _stallBuddy)
            {
                _stallBuddy = b;
                _stallLowestFraction = frac;
                _stallSince = Time.time;
                return;
            }
            if (frac <= _stallLowestFraction - BuddyStallProgress)
            {
                _stallLowestFraction = frac;
                _stallSince = Time.time;
            }
            else if (Time.time - _stallSince >= BuddyStallTime)
            {
                _ignoredBuddy = b;
                _ignoreBuddyUntil = Time.time + BuddyIgnoreTime;
                _stallBuddy = null;
                _buddyTarget = null;
                _nextBuddyScan = 0f;
            }
        }

        private ElementalBuddy FindBuddyTarget(float radius)
        {
            ElementalBuddy best = null;
            float bestDist = float.MaxValue;
            Vector3 pos = transform.position;
            var active = ElementalBuddy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var b = active[i];
                if (!IsValidBuddy(b)) continue;
                if (b == _ignoredBuddy && Time.time < _ignoreBuddyUntil) continue;
                Vector3 d = b.transform.position - pos;
                d.y = 0f;
                float dist = d.magnitude;
                // Hysterese: aktuelles Ziel bleibt etwas länger und wird bevorzugt (kein Flackern)
                bool current = b == _buddyTarget;
                if (dist > (current ? radius * 1.2f : radius)) continue;
                if (current) dist -= 1.5f;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = b;
                }
            }
            return best;
        }

        // Buddy hinter einem Ziel-Transform (Buddy-Ziel oder Spott durch Erde/Kristall); null = kein Buddy
        private ElementalBuddy BuddyOf(Transform target)
        {
            if (target == null) return null;
            if (_buddyTarget != null && target == _buddyTarget.transform) return _buddyTarget;
            if (target != _lookupTransform)
            {
                _lookupTransform = target;
                _lookupBuddy = target.GetComponent<ElementalBuddy>();
            }
            return _lookupBuddy;
        }

        // Erreichbarer NavMesh-Punkt am Buddy (Collider-Rand), alle 0,5 s neu
        private Vector3 BuddyDestination(ElementalBuddy b)
        {
            if (b != _destBuddy || Time.time >= _nextDestUpdate)
            {
                _destBuddy = b;
                _nextDestUpdate = Time.time + 0.5f;
                Vector3 p = b.GetClosestPoint(transform.position);
                p.y = b.transform.position.y;
                _buddyDest = NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : b.transform.position;
            }
            return _buddyDest;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // extra: zusätzliche Reichweite (Treffer-Toleranz der Nahkampf-Hiebe); gilt nicht fürs ForcedTarget
        private bool IsInAttackRange(Transform target, float extra = 0f)
        {
            if (target == null) return false;

            if (IsNexus(target))
                return Nexus.Instance.GetDistanceFrom(transform.position) <= NexusRange + extra;

            if (target == ForcedTarget)
            {
                Vector3 d = target.position - transform.position;
                d.y = 0f;
                return d.magnitude <= ForcedTargetArriveDistance;
            }

            // Buddies: bis zum Collider-Rand (große Buddies wie der Kristall)
            var buddy = BuddyOf(target);
            if (buddy != null)
                return HorizontalDistance(transform.position, buddy.GetClosestPoint(transform.position)) <= UnitAttackRange + extra;

            return UnitDistance(transform.position, target.position) < UnitAttackRange + extra;
        }

        // Fernkampf: freie Schusslinie zum Ziel? Prüft nur den Steinwall-Layer (Linecast vom Abschusspunkt zum
        // Zielpunkt wie FireShot), je Gegner 0,2 s gecacht; ohne Wand sofort frei.
        private const float LineOfSightInterval = 0.2f;
        private Transform _losTarget;
        private float _losNextCheck;
        private bool _losClear = true;

        private bool HasLineOfSight(Transform target)
        {
            if (target == null || target == ForcedTarget || Config == null || !StoneWall.AnyActive) return true;
            if (target == _losTarget && Time.time < _losNextCheck) return _losClear;
            _losTarget = target;
            _losNextCheck = Time.time + LineOfSightInterval;
            // Gleiche Höhe wie der echte Abschuss (Projektil fliegt ungefähr gerade dorthin)
            Vector3 from = transform.position + Vector3.up * Config.ProjectileSpawnHeight;
            Vector3 to = IsNexus(target) ? Nexus.Instance.GetClosestPoint(from) : target.position + Vector3.up * 0.9f;
            _losClear = !StoneWall.Blocks(from, to);
            return _losClear;
        }

        // Fernkämpfer: in Reichweite UND freie Schusslinie
        private bool CanShoot(Transform target) => IsInAttackRange(target) && HasLineOfSight(target);

        // „Steht zu Recht still“ (Anti-Cheese/Nachzügler): Fernkämpfer hinter einer Wand zählen nicht als angreifend
        private bool InAttackPosition(Transform target) => IsRanged ? CanShoot(target) : IsInAttackRange(target);

        private void HandleMovement()
        {
            Transform target = GetCurrentTarget();
            if (target == null) return;

            // Nahkampf-Hieb: während Ausholen + kurzer Erholung stehen bleiben (kein Rutschen in den Treffer).
            // Anti-Cheese-Hiebe gegen einen Blocker nicht – der Gegner steckt dort ohnehin fest.
            if ((_swingPending && !_swingVsBlocker) || Time.time < _swingHoldUntil)
            {
                if (_motor.IsReady && _motor.HasPath) _motor.ResetPath();
                return;
            }

            if (IsFlyer && target != ForcedTarget && IsInAttackRange(target))
            {
                // Flieger: in Reichweite schweben (nicht auf das Ziel setzen); Kreisbogen nach dem Treffer fliegt der Motor
                if (_motor.HasPath) _motor.ResetPath();
                return;
            }

            if (target == ForcedTarget && IsInAttackRange(target))
            {
                // Angekommen: am Ziel stehen bleiben (z. B. im Schrein-Kreis → blockiert den Fortschritt)
                if (_motor.IsReady && _motor.HasPath) _motor.ResetPath();
                return;
            }

            if (IsRanged && CanShoot(target))
            {
                // Fernkämpfer: in Reichweite und freier Schusslinie stehen bleiben und schießen
                // (Steinwall dazwischen → weiterlaufen, die Wand ist aus dem NavMesh geschnitten)
                if (_motor.IsReady && _motor.HasPath) _motor.ResetPath();
                return;
            }

            if (Elite == EliteAffix.Ram && IsInAttackRange(target))
            {
                // Ramme (lang, Pivot in der Mitte): vor dem Nexus anhalten statt sich hineinzuschieben, Bug zum Nexus
                if (_motor.IsReady && _motor.HasPath) _motor.ResetPath();
                FaceTowards(target);
                return;
            }

            if (IsNexus(target))
            {
                // Walk to the Nexus surface instead of its pivot (the Nexus is big / may carve the NavMesh)
                RequestDestination(Nexus.Instance.GetClosestPoint(transform.position));
            }
            else
            {
                var buddy = BuddyOf(target);
                RequestDestination(buddy != null ? BuddyDestination(buddy) : target.position);
            }
        }

        // Gleiches Ziel nicht jeden Frame neu setzen: bei unerreichbarem Ziel (Teil-Pfad, z. B. Buddy auf einer
        // NavMesh-Insel) verwirft SetDestination den fertigen Teil-Pfad und rechnet neu -> Gegner stand dauerhaft still
        private Vector3 _requestedDest;
        private void RequestDestination(Vector3 dest)
        {
            if ((_motor.PathPending || _motor.HasPath) && (dest - _requestedDest).sqrMagnitude < 0.25f) return;
            _requestedDest = dest;
            _motor.SetDestination(dest);
        }

        // Anti-Cheese-Suchradius: 2,5 m, bei großen Gegnern (Ramme, 4,6 m lang) Collider-Halbausdehnung + 1,2 m.
        // Gecacht je Root-Skalierung (Elite-Look skaliert erst nach dem Spawn); Normalgegner bleiben bei genau 2,5 m.
        private const float AntiCheeseRadius = 2.5f;
        private const float AntiCheeseMargin = 1.2f;
        private float _antiCheeseRadius = -1f;
        private float _antiCheeseScale;

        private float GetAntiCheeseRadius()
        {
            float scale = Mathf.Abs(transform.lossyScale.x);
            if (_antiCheeseRadius > 0f && Mathf.Approximately(scale, _antiCheeseScale)) return _antiCheeseRadius;
            _antiCheeseScale = scale;
            var col = GetComponent<Collider>();
            float half = col != null ? BodyFootprint.LocalHalfExtent(col) * scale : 0f;
            _antiCheeseRadius = Mathf.Max(AntiCheeseRadius, half + AntiCheeseMargin);
            return _antiCheeseRadius;
        }

        private void HandleAntiCheese()
        {
            // Flieger können nicht blockiert werden (überfliegen Buddies und Steinwall) – kein Anti-Cheese
            if (IsFlyer)
            {
                _stuckTimer = 0f;
                return;
            }

            // Laufender Anti-Cheese-Hieb: Zähler bleibt stehen (nach dem Hieb sofort der nächste, solange er feststeckt)
            if (_swingPending && _swingVsBlocker) return;

            // Standing still while attacking our target is not "stuck" (auch nicht während Hieb + Erholung)
            if (_swingPending || Time.time < _swingHoldUntil || InAttackPosition(GetCurrentTarget()))
            {
                _stuckTimer = 0;
                return;
            }

            if (_motor.Velocity.magnitude < 0.1f && !_motor.PathPending && (_motor.HasPath || NearestPlayer() != null || Nexus.Instance != null))
            {
                _stuckTimer += Time.deltaTime;
            }
            else
            {
                _stuckTimer = 0;
            }

            if (_stuckTimer > 0.6f)
            {
                Collider[] hits = Physics.OverlapSphere(transform.position, GetAntiCheeseRadius(), LayerMask.GetMask("Buddy"));
                foreach (var hit in hits)
                {
                     var buddy = hit.GetComponent<IDamageable>();
                     if (buddy != null)
                     {
                         _blockedByBuddy = true; // blockiert = kein Nachzügler
                         if (UsesMeleeSwings)
                         {
                             // Einzelhiebe wie im Nahkampf: alle AttackInterval s ein Hieb mit AttackDamage × AttackInterval
                             if (Time.time >= _nextAttackTime) StartSwing(hit.transform, hit);
                             return;
                         }
                         // Fernkämpfer und Ramme: weiter kontinuierlich
                         float dmg = (Config != null ? Config.AttackDamage : 10f) * DamageMultiplier * Time.deltaTime;
                         buddy.TakeDamage(dmg);
                         return; 
                     }
                }
            }
        }
        
        private void HandleAttack()
        {
             Transform target = GetCurrentTarget();
             if (!ReachedNexus && target != null && IsNexus(target) && IsInAttackRange(target))
             {
                 ReachedNexus = true;
                 OnReachedNexus?.Invoke(this);
             }
             if (IsRanged)
             {
                 HandleRangedAttack(target);
                 return;
             }
             if (UsesMeleeSwings)
             {
                 HandleMeleeAttack(target);
                 return;
             }
             HandleContactAttack(target);
        }

        // Dauerschaden (AttackDamage × deltaTime) – nur noch Belagerungsramme am Nexus und ForcedTarget (Schrein/Händler)
        private void HandleContactAttack(Transform target)
        {
             if (target != null && IsInAttackRange(target))
             {
                 float amount = (Config != null ? Config.AttackDamage : 10f) * DamageMultiplier * Time.deltaTime;
                 if (BuddyOf(target) != null) amount *= BuddyDamageMultiplier;
                 // Optik: Angriffs-Animation im Takt von AttackInterval (Schaden bleibt kontinuierlich)
                 if (target != ForcedTarget && Time.time >= _nextAttackTime && HasAttackVisual)
                 {
                     _nextAttackTime = Time.time + AttackInterval;
                     PlayAttackTrigger();
                     if (_net != null) _net.ServerAttackTrigger();
                 }
                 DealMeleeDamage(target, amount);
             }
        }

        // Spieler bekommt die Angriffsrichtung mit (Schildblock blockt nur frontal), sonst IDamageable
        private void DealMeleeDamage(Transform target, float amount)
        {
             var directional = target.GetComponent<IDirectionalDamageable>();
             if (directional != null)
             {
                 directional.TakeDamage(amount, transform.position);
                 return;
             }
             var dmg = target.GetComponent<IDamageable>();
             if (dmg != null)
             {
                 dmg.TakeDamage(amount);
             }
        }

        // ---------------- Nahkampf-Hiebe ----------------

        // In Reichweite: stehen bleiben, zum Ziel drehen, Attack-Trigger (Server + Clients), nach AttackWindup ein Treffer.
        // Nächster Hieb AttackInterval s nach Beginn des vorigen. ForcedTarget (Schrein/Händler): Dauerschaden wie bisher.
        private void HandleMeleeAttack(Transform target)
        {
            if (_swingPending)
            {
                // Ziel gewechselt oder tot → Hieb abbrechen (kein Schaden); Anti-Cheese-Hiebe hängen nur am Blocker
                Transform t = _swingTarget;
                if (!IsSwingTargetValid() || (!_swingVsBlocker && t != target)) CancelSwing();
                else
                {
                    if (!_swingVsBlocker) FaceTowards(t);
                    if (Time.time >= _swingHitTime) ResolveSwing();
                    return;
                }
            }

            if (target == null) return;
            if (target == ForcedTarget)
            {
                HandleContactAttack(target);
                return;
            }
            if (!IsInAttackRange(target)) return;
            // Im Stand dreht der Agent nicht selbst
            if (Time.time < _swingHoldUntil || _motor.Velocity.sqrMagnitude < 0.04f) FaceTowards(target);
            if (Time.time < _nextAttackTime) return;
            StartSwing(target, null);
        }

        // blocker != null: Anti-Cheese-Hieb gegen diesen Collider (Reichweite = Anti-Cheese-Radius, kein Stehenbleiben)
        private void StartSwing(Transform target, Collider blocker)
        {
            if (target == null) return;
            float now = Time.time;
            float windup = MeleeWindup;
            _swingPending = true;
            _swingVsBlocker = blocker != null;
            _swingBlocker = blocker;
            _swingTarget = target;
            _swingBuddy = BuddyOf(target);
            _swingPlayer = _swingBuddy == null ? target.GetComponent<PlayerStats>() : null;
            _swingHitTime = now + windup;
            _nextAttackTime = now + AttackInterval;
            if (!_swingVsBlocker && _flyer != null)
            {
                // Flieger: Herabstoßen während des Ausholens, nach dem Treffer fliegt der Motor einen Kreisbogen
                _swingHoldUntil = now + windup;
                bool nexus = IsNexus(target);
                Vector3 aim = nexus ? Nexus.Instance.GetClosestPoint(transform.position) : target.position;
                _flyer.BeginDive(aim, nexus ? float.NegativeInfinity : target.position.y, windup);
                if (_motor.HasPath) _motor.ResetPath();
            }
            else if (!_swingVsBlocker)
            {
                // stehen bleiben bis kurz nach dem Treffer: min(0,6 × Intervall; Windup + 0,35 s), mindestens bis zum Treffer
                _swingHoldUntil = now + Mathf.Max(windup, Mathf.Min(AttackInterval * 0.6f, windup + SwingRecovery));
                if (_motor.IsReady)
                {
                    if (_motor.HasPath) _motor.ResetPath();
                    _motor.Velocity = Vector3.zero;
                }
            }
            // Angriffs-Animation immer (Clients per RPC; dort startet auch der Trefferton nach AttackWindup)
            PlayAttackTrigger();
            if (_net != null) _net.ServerAttackTrigger();
            if (windup <= 0f) ResolveSwing();
        }

        // Ziel noch gültig? (lebender Buddy / Spieler, aktives Objekt, Blocker-Collider noch an)
        private bool IsSwingTargetValid()
        {
            if (_swingTarget == null || !_swingTarget.gameObject.activeInHierarchy) return false;
            if (_swingVsBlocker && (_swingBlocker == null || !_swingBlocker.enabled)) return false;
            if (_swingBuddy != null) return IsValidBuddy(_swingBuddy);
            if (_swingPlayer != null) return !_swingPlayer.IsDead;
            return true;
        }

        // Trefferzeitpunkt: Ton (auch bei Fehlschlag), Schaden nur, wenn das Ziel noch in Reichweite + Toleranz ist
        private void ResolveSwing()
        {
            Transform t = _swingTarget;
            bool blocker = _swingVsBlocker;
            Collider col = _swingBlocker;
            bool valid = IsSwingTargetValid();
            _swingPending = false;
            _swingVsBlocker = false;
            _swingTarget = null;
            _swingBlocker = null;
            _swingBuddy = null;
            _swingPlayer = null;
            if (_flyer != null && !blocker) _flyer.EndDive(valid ? t : null, valid && IsNexus(t));
            if (!valid) return;

            PlayMeleeHitSfx();
            bool inRange = blocker
                ? Vector3.Distance(transform.position, col.bounds.ClosestPoint(transform.position)) <= GetAntiCheeseRadius() + MeleeHitTolerance
                : IsInAttackRange(t, MeleeHitTolerance);
            if (!inRange) return; // ausgewichen

            // AttackDamage ist DPS: ein Hieb = AttackDamage × AttackInterval
            float amount = (Config != null ? Config.AttackDamage : 10f) * AttackInterval * DamageMultiplier;
            if (blocker)
            {
                // wie der frühere Blocker-Dauerschaden: ohne BuddyDamageMultiplier
                var d = col.GetComponent<IDamageable>();
                if (d != null) d.TakeDamage(amount);
                return;
            }
            if (BuddyOf(t) != null) amount *= BuddyDamageMultiplier;
            DealMeleeDamage(t, amount);
        }

        // Laufenden Hieb verwerfen (Betäubung, Frost, Rückstoß, Zauber, Zielwechsel, Game Over) – kurze Pause bis zum nächsten
        private void CancelSwing()
        {
            if (!_swingPending) return;
            if (_flyer != null) _flyer.CancelDive();
            _swingPending = false;
            _swingVsBlocker = false;
            _swingTarget = null;
            _swingBlocker = null;
            _swingBuddy = null;
            _swingPlayer = null;
            _swingHoldUntil = 0f;
            _nextAttackTime = Time.time + SwingCancelCooldown;
        }

        // Trefferton je Gegnertyp (Config.MeleeHitSfx), Server und Clients; global höchstens MeleeSfxPerWindow je 0,1 s
        private void PlayMeleeHitSfx()
        {
            if (Config == null || !Config.MeleeHitSound) return;
            float now = Time.unscaledTime;
            if (now - _meleeSfxWindowStart >= MeleeSfxWindow)
            {
                _meleeSfxWindowStart = now;
                _meleeSfxCount = 0;
            }
            if (_meleeSfxCount >= MeleeSfxPerWindow) return;
            _meleeSfxCount++;
            GameAudio.PlayPitched(Config.MeleeHitSfx, transform.position, Random.Range(0.94f, 1.06f));
        }

        private float AttackInterval => Config != null && Config.AttackInterval > 0f ? Config.AttackInterval : 1.5f;

        // Fernkampf: in Reichweite zum Ziel drehen, alle AttackInterval s Attack-Trigger + Projektil nach AttackWindup.
        // ForcedTarget (Schrein) wird nicht beschossen – dort nur stehen.
        private void HandleRangedAttack(Transform target)
        {
            if (_shotPending)
            {
                if (_shotTarget == null || !_shotTarget.gameObject.activeInHierarchy) _shotPending = false;
                else
                {
                    FaceTowards(_shotTarget);
                    if (Time.time >= _shotFireTime) FireShot();
                    return;
                }
            }

            if (target == null || target == ForcedTarget || !CanShoot(target)) return;
            FaceTowards(target);
            if (Time.time < _nextAttackTime) return;

            _nextAttackTime = Time.time + AttackInterval;
            if (HasAttackVisual)
            {
                PlayAttackTrigger();
                if (_net != null) _net.ServerAttackTrigger();
            }
            _shotTarget = target;
            _shotPending = true;
            _shotFireTime = Time.time + Mathf.Max(0f, Config.AttackWindup);
            if (Config.AttackWindup <= 0f) FireShot();
        }

        private void FireShot()
        {
            _shotPending = false;
            if (Config == null || Config.ProjectilePrefab == null || _shotTarget == null) return;

            bool nexus = IsNexus(_shotTarget);
            Vector3 aimOffset = nexus ? Vector3.zero : Vector3.up * 0.9f;
            Vector3 spawn = transform.position + Vector3.up * Config.ProjectileSpawnHeight + transform.forward * 0.5f;
            Vector3 aim = nexus ? Nexus.Instance.GetClosestPoint(spawn) : _shotTarget.position + aimOffset;
            Vector3 dir = aim - spawn;
            var go = Instantiate(Config.ProjectilePrefab, spawn, dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : transform.rotation);
            var proj = go.GetComponent<EnemyProjectile>();
            if (proj == null) proj = go.AddComponent<EnemyProjectile>();

            float damage = Config.AttackDamage * DamageMultiplier;
            if (BuddyOf(_shotTarget) != null) damage *= BuddyDamageMultiplier;
            proj.Init(_shotTarget, aimOffset, damage, Config.ProjectileSpeed, transform.position);
            // Clients: kosmetische Kopie (kein Schaden)
            if (_net != null) _net.ServerShot(spawn, go.transform.rotation, _shotTarget, aimOffset, nexus, aim);
        }

        // Weich zum Ziel drehen (Agent dreht im Stand nicht selbst)
        private void FaceTowards(Transform target)
        {
            Vector3 p = IsNexus(target) ? Nexus.Instance.GetClosestPoint(transform.position) : target.position;
            Vector3 d = p - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 540f * Time.deltaTime);
        }

        // Schadens-Pipeline: verflucht → Bonus-Schaden, Rüstung ignoriert; sonst Rüstung reduziert
        public void TakeDamage(float amount)
        {
            if (!Net.IsServer || _isDead || amount <= 0f) return;
            if (IsCursed) amount *= 1f + _curse.DamageTakenBonus;
            else amount *= 1f - Armor;
            // Kartensynergien (Spottmal, Dampfschock …) und Elite-Eigenschaften (Feuerfest, Schildträger)
            amount *= CardEffects.DamageTakenFactor(this, DamageSource);
            amount *= EliteDamageTakenFactor(DamageSource);
            TakeTrueDamage(amount);
        }

        // Schaden ohne Rüstung/Fluch-Modifikatoren
        public void TakeTrueDamage(float amount)
        {
            if (!Net.IsServer || _isDead) return;
            float dealt = Mathf.Min(amount, Mathf.Max(0f, _currentHP));
            if (amount > 0f && OnDamageDealt != null) OnDamageDealt(this, dealt, _playerSourceDepth > 0);
            _currentHP -= amount;
            if (amount > 0f && OnLocalHit != null) OnLocalHit(this, dealt, _playerSourceDepth > 0, _currentHP <= 0f);
            if (_currentHP <= 0)
            {
                // Guard: several hits in one frame must not report the death twice (bounty / wave count)
                _isDead = true;
                _currentHP = 0f;
                _activeBosses.Remove(this);
                _swingPending = false;
                bool shatter = IsFrozen;
                // Clients zuerst informieren (RPC kommt zuverlässig vor der Despawn-Nachricht an)
                if (_net != null) _net.ServerKilled();
                PlayDeathVisual();
                OnEnemyDeath?.Invoke();
                OnEnemyKilled?.Invoke(this);
                PlayDeathSfx();
                if (shatter) CardEffects.Shatter(this); // Splitterfrost-Karte
                Despawn();
            }
            else SyncHealth();
        }

        // Server: Gegner entfernen (im Netz Despawn, sonst Destroy)
        private void Despawn()
        {
            var no = _net != null ? _net.NetworkObject : null;
            if (no != null && no.IsSpawned) no.Despawn(true);
            else Destroy(gameObject);
        }

        // Mehrere Slows überschreiben sich nicht mehr: es gilt der stärkste noch laufende
        public void ApplySlow(float percentage, float duration)
        {
            if (!Net.IsServer || _isDead || Elite == EliteAffix.Frostguard) return;
            percentage = Mathf.Clamp01(percentage);
            duration *= ControlFactor;
            bool active = Time.time < _slowUntil;
            if (!active || percentage > _slowPercent)
            {
                _slowPercent = percentage;
                _slowUntil = Time.time + duration;
            }
            else if (Mathf.Approximately(percentage, _slowPercent))
            {
                _slowUntil = Mathf.Max(_slowUntil, Time.time + duration);
            }
            // schwächerer Slow während eines stärkeren: ignoriert
        }

        // Komplett einfrieren (Frostnova): steht still, greift nicht an, Animation pausiert
        public void Freeze(float duration, GameObject vfxPrefab = null)
        {
            if (!Net.IsServer || _isDead || duration <= 0f || Elite == EliteAffix.Frostguard) return;
            if (IsWet) duration *= 2f; // Nass + Frost: friert doppelt so lange ein
            duration *= ControlFactor;
            _freeze = FreezeEffect.Apply(gameObject, duration, vfxPrefab);
            if (_motor != null && _motor.IsReady)
            {
                _motor.IsStopped = true;
                _motor.Velocity = Vector3.zero;
            }
        }

        public bool IsFrozen => IsRemote ? HasRemoteStatus(EnemyNet.StatusFrozen) : _freeze != null && _freeze.IsActive;

        // Betäubt (Erdbeben): steht still und greift nicht an, Animation läuft weiter. Optik: kreisende Sterne (BlindEffect).
        public void Stun(float duration, GameObject vfxPrefab = null)
        {
            if (!Net.IsServer || _isDead || duration <= 0f) return;
            duration *= ControlFactor;
            _stunUntil = Mathf.Max(_stunUntil, Time.time + duration);
            if (vfxPrefab != null) BlindEffect.Apply(gameObject, duration, vfxPrefab);
            EnsureMotor();
            if (_motor != null && _motor.IsReady)
            {
                _motor.IsStopped = true;
                _motor.Velocity = Vector3.zero;
            }
        }

        public bool IsStunned => IsRemote ? HasRemoteStatus(EnemyNet.StatusStunned) : Time.time < _stunUntil;
        private float _stunUntil;

        // Nass machen (löscht einen laufenden Brand, siehe WetEffect.Apply)
        public void ApplyWet(float duration, GameObject vfxPrefab = null)
        {
            if (!Net.IsServer || _isDead || duration <= 0f) return;
            var wet = WetEffect.Apply(gameObject, duration, vfxPrefab);
            if (wet != null) _wet = wet;
        }

        // Verfluchen: DoT + mehr erlittener Schaden, Rüstung ignoriert
        public void ApplyCurse(float dps, float duration, float damageTakenBonus, GameObject vfxPrefab = null)
        {
            if (!Net.IsServer || _isDead || duration <= 0f) return;
            var curse = CurseEffect.Apply(gameObject, dps, duration, damageTakenBonus, vfxPrefab);
            if (curse != null) _curse = curse;
        }

        // Zusätzlich zur Kontroll-Resistenz der Ramme (0,4 → ControlFactor 0,6): 0,6 × 0,4 = 0,24 ≈ 25 % Rückstoß gesamt
        private const float RamKnockbackFactor = 0.4f;

        // Rückstoß entlang des NavMesh (agent.Move, verlässt das NavMesh nicht); unterbricht kurz die Bewegung
        public void Knockback(Vector3 direction, float distance, float duration = 0.25f)
        {
            if (!Net.IsServer) return;
            distance *= ControlFactor;
            if (Elite == EliteAffix.Ram) distance *= RamKnockbackFactor; // 4,6-m-Belagerungsramme lässt sich kaum schieben
            if (_isDead || distance <= 0f) return;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            EnsureMotor();
            if (_knockbackRoutine != null) StopCoroutine(_knockbackRoutine);
            _knockbackRoutine = StartCoroutine(KnockbackRoutine(direction.normalized, distance, Mathf.Max(0.01f, duration)));
        }

        private IEnumerator KnockbackRoutine(Vector3 dir, float distance, float duration)
        {
            _knockedBack = true;
            if (_motor.IsReady) { _motor.IsStopped = true; _motor.Velocity = Vector3.zero; }

            float t = 0f;
            float done = 0f;
            while (t < duration)
            {
                t = Mathf.Min(duration, t + Time.deltaTime);
                float k = t / duration;
                float eased = 1f - (1f - k) * (1f - k); // ease-out: schneller Stoß, weiches Abbremsen
                float step = (eased - done) * distance;
                done = eased;
                if (_motor.IsReady) _motor.Move(dir * step);
                yield return null;
            }

            _knockedBack = false;
            _knockbackRoutine = null;
            _stuckTimer = 0f;
            if (_motor.IsReady && !IsFrozen && !IsStunned && !IsCasting) _motor.IsStopped = false;
        }

        // Horizontale Richtung entgegen der aktuellen Laufrichtung (z. B. für Rückstoß „den Weg zurück“)
        public Vector3 PathBackDirection
        {
            get
            {
                Vector3 v = Vector3.zero;
                if (_motor != null)
                {
                    v = _motor.Velocity;
                    v.y = 0f;
                    if (v.sqrMagnitude < 0.01f && _motor.IsReady && _motor.HasPath)
                    {
                        v = _motor.SteeringTarget - transform.position;
                        v.y = 0f;
                    }
                }
                if (v.sqrMagnitude < 0.01f && Nexus.Instance != null)
                {
                    v = Nexus.Instance.GetClosestPoint(transform.position) - transform.position;
                    v.y = 0f;
                }
                if (v.sqrMagnitude < 0.0001f) v = transform.forward;
                v.y = 0f;
                return v.sqrMagnitude > 0.0001f ? -v.normalized : Vector3.back;
            }
        }

        private void UpdateSpeed()
        {
            float slow = Time.time < _slowUntil ? _slowPercent : 0f;
            _motor.Speed = _baseSpeed * (1f - slow);
        }

        public void Taunt(Transform target, float duration)
        {
            if (!Net.IsServer || _isDead || !isActiveAndEnabled) return;
            StartCoroutine(TauntRoutine(target, duration * ControlFactor));
        }

        // Heilung (z. B. Totenkreis des Nekromanten), auf MaxHP begrenzt; gibt die geheilte Menge zurück
        public float Heal(float amount)
        {
            if (!Net.IsServer || _isDead || amount <= 0f || _currentHP >= _maxHP) return 0f;
            float before = _currentHP;
            _currentHP = Mathf.Min(_maxHP, _currentHP + amount);
            SyncHealth();
            return _currentHP - before;
        }

        // Steht für duration s still und greift nicht an (Boss-Fähigkeit: Ausholen + Erholung)
        public void LockForCast(float duration)
        {
            if (!Net.IsServer || _isDead) return;
            _castUntil = Mathf.Max(_castUntil, Time.time + Mathf.Max(0f, duration));
            _shotPending = false;
            CancelSwing(); // kein Grundangriff-Hieb während des Zaubers
            EnsureMotor();
            if (_motor != null && _motor.IsReady) { _motor.IsStopped = true; _motor.Velocity = Vector3.zero; }
        }

        // Sperre vorzeitig aufheben (abgebrochener Zauber)
        public void EndCast()
        {
            _castUntil = 0f;
        }

        private IEnumerator TauntRoutine(Transform target, float duration)
        {
            _tauntTarget = target;
            yield return new WaitForSeconds(duration);
            _tauntTarget = null;
        }
    }
}