using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Zusätzliche Gegnergruppe, die ab einer Welle in jede Welle (definiert oder endlos) gemischt wird
    [System.Serializable]
    public class ExtraEnemyGroup
    {
        public EnemyConfigSO Config;
        [Tooltip("Erste Welle (1-basiert), in der die Gruppe auftaucht.")]
        public int StartWave = 1;
        [Tooltip("Anzahl in der Startwelle.")]
        public int BaseCount = 1;
        [Tooltip("Zusätzliche Gegner pro Welle nach StartWave (abgerundet).")]
        public float PerWave;
        public float SpawnInterval = 1f;
        [Tooltip("0 = jede Welle ab StartWave; n > 0 = nur jede n-te Welle (StartWave, StartWave + n, …), z. B. Bosse.")]
        public int EveryNthWave = 0;

        public int GetCount(int waveNumber)
        {
            if (Config == null || waveNumber < StartWave) return 0;
            if (EveryNthWave > 0 && (waveNumber - StartWave) % EveryNthWave != 0) return 0;
            return Mathf.Max(0, BaseCount + Mathf.FloorToInt(PerWave * (waveNumber - StartWave)));
        }
    }

    // Mehrspieler: Wellen-Logik (Spawns, Zählung, Wellenende, Bonus) läuft nur auf dem Server. Clients spiegeln
    // CurrentWaveIndex/IsWaveActive/EnemiesRemaining über NetGame.Waves und feuern OnWaveStart/OnWaveEnd/OnPortalOpened lokal.
    // Wellenstart per Bereit-Abstimmung: RequestStartWave() auf jedem Rechner; der Server startet, wenn alle bereit sind
    // und kein WaveGate blockiert (Einzelspieler: sofort).
    public class WaveManager : MonoBehaviour
    {
        public static WaveManager Instance { get; private set; }

        public List<WaveConfigSO> Waves;
        public List<Transform> SpawnPoints;

        [Header("Schrein-Wellen")]
        [Tooltip("Ist in einer Welle ein Schrein erwacht, dauert die Spawn-Phase mindestens Schrein-Dauer + dieser Puffer (Weg zum Schrein, Umkämpft-Verlangsamung). Fehlende Zeit wird mit zusätzlichen Gegnern aufgefüllt.")]
        public float ShrineWaveExtraTime = 25f;

        [Header("Gemischte Wellen")]
        [Tooltip("Zusatzgruppen (z. B. Schwarm, Ritter), die ab StartWave an jede Welle angehängt werden.")]
        public List<ExtraEnemyGroup> ExtraGroups = new List<ExtraEnemyGroup>();
        [Tooltip("Alle Gruppen einer Welle spawnen parallel; Gruppe i startet i × diesen Versatz (s) nach dem StartDelay.")]
        public float GroupStartOffset = 2f;

        [Header("Balancing – Gegner-HP")]
        [Tooltip("HP Normalgegner: BaseHP · (1 + HpLinearPerWave·(w−1)) · HpGrowthPerWave^(w−1). Linearer Anteil.")]
        public float HpLinearPerWave = 0.02f;
        [Tooltip("Exponentieller Anteil der HP-Kurve (Faktor pro Welle).")]
        public float HpGrowthPerWave = 1.031f;
        [Tooltip("Boss-HP: BaseHP · w · min(1, w/BossRampWave)² – Anlauf für frühe Bosse (0 = kein Anlauf).")]
        public float BossRampWave = 12f;

        [Header("Balancing – Gegner-Anzahl und Spawn-Tempo")]
        [Tooltip("Anzahl-Multiplikator M(w) = (1 + CountGrowth·(w−1))^CountExponent auf alle Nicht-Boss-Gruppen.")]
        public float CountGrowth = 0.05f;
        public float CountExponent = 1.2f;
        [Tooltip("Höchstens so viele Normalgegner pro Welle (ohne Schrein-Verlängerung und Angreifer), Performance-Deckel.")]
        public int MaxEnemiesPerWave = 260;
        [Tooltip("Spawn-Intervall aller Nicht-Boss-Gruppen × IntervalDecayPerWave^(w−1) …")]
        public float IntervalDecayPerWave = 0.98f;
        [Tooltip("… aber nie unter diesem Anteil des Ausgangsintervalls.")]
        public float MinIntervalFactor = 0.5f;
        [Tooltip("Boss-Wellen: Normalgruppen × diesen Faktor, Intervall ÷ Faktor (gleiche Spawn-Dauer, weniger dicht).")]
        public float BossWaveEscortFactor = 0.6f;

        [Header("Balancing – Gegnerschaden")]
        [Tooltip("Gegnerschaden × (1 + DamageGrowthPerWave·(w−1)) – Nahkampf, Fernkampf, Kontakt und Boss-Fähigkeiten.")]
        public float DamageGrowthPerWave = 0.04f;

        [Header("Sägezahn – Ernte-Welle (Plan Fesselung C3)")]
        [Tooltip("Die Welle direkt nach einer Boss-Welle ist eine Ernte-Welle: weniger Gegner, mehr Wellenbonus – der Spieler spürt seine neue Stärke.")]
        public bool HarvestAfterBoss = true;
        [Tooltip("Ernte-Welle: Normalgegner-Anzahl × Faktor (Kopfgeld pro Kill bleibt gleich).")]
        public float HarvestCountFactor = 0.75f;
        [Tooltip("Ernte-Welle: Wellenbonus × Faktor.")]
        public float HarvestBonusFactor = 1.5f;

        [Header("Früher Wellenstart (Plan Fesselung C2)")]
        [Tooltip("Höchstbonus für einen sofortigen Start = Anteil des Wellenbonus der kommenden Welle (ab Welle 2).")]
        public float EarlyCallMaxFraction = 0.3f;
        [Tooltip("In so vielen Sekunden freier Bauphase sinkt der Bonus linear auf 0 (Draft/Laden/Pause zählen nicht).")]
        public float EarlyCallWindow = 25f;

        [Header("Elite-Gegner (Plan Fesselung C4)")]
        [Tooltip("Ab dieser Welle können Normalgegner Elite werden (nicht in Ernte-Wellen).")]
        public int EliteFromWave = 12;
        [Tooltip("Anteil Eliten an den Normalgegnern: Start + pro Welle, gedeckelt.")]
        public float EliteShareStart = 0.025f;
        public float EliteSharePerWave = 0.003f;
        public float EliteShareMax = 0.06f;
        [Tooltip("Höchstens so viele Eliten pro Welle.")]
        public int MaxElitesPerWave = 8;
        [Tooltip("Aura-Partikel unter einer Elite (wird pro Eigenschaft eingefärbt).")]
        public GameObject EliteAuraPrefab;
        [Tooltip("Symbole der Eigenschaften in EliteAffix-Reihenfolge ohne None (Feuerfest, Frostgepanzert, Flink, Schildträger, Splitterdieb).")]
        public Sprite[] EliteIcons;

        [Header("Wellen-Ereignisse (Plan Fesselung C5)")]
        [Tooltip("Erstes Ereignis frühestens in dieser Welle; danach alle EventEvery Wellen (Boss-/Ernte-Wellen werden übersprungen).")]
        public int EventFromWave = 16;
        public int EventEvery = 3;
        [Tooltip("Blutmond: Tempo × Faktor, Kopfgeld × Faktor.")]
        public float BloodMoonSpeed = 1.2f;
        public float BloodMoonBounty = 1.5f;
        [Tooltip("Seelensturm: Kopfgeld × Faktor, Splitter-Magnet-Radius × Faktor (selbst einsammeln).")]
        public float SoulStormBounty = 2f;
        public float SoulStormMagnet = 0.3f;

        [Header("Mehrspieler – Skalierung nach Spielerzahl n")]
        [Tooltip("Gegner-Anzahl × (1 + CountPerExtraPlayer·(n−1)). Geht über CountMultiplier auch in die Kopfgeld-Division ein (Teamkasse wächst nicht mit n).")]
        public float CountPerExtraPlayer = 0.5f;
        [Tooltip("HP der Normalgegner × (1 + HpPerExtraPlayer·(n−1)).")]
        public float HpPerExtraPlayer = 0.25f;
        [Tooltip("Boss-HP × (1 + BossHpPerExtraPlayer·(n−1)).")]
        public float BossHpPerExtraPlayer = 0.6f;

        // Spielerzahl-Faktor: 1 + perPlayer·(n−1)
        private static float PlayerFactor(float perPlayer) => 1f + Mathf.Max(0f, perPlayer) * Mathf.Max(0, Net.PlayerCount - 1);

        public int CurrentWaveIndex { get; private set; } = 0;
        public bool IsWaveActive { get; private set; } = false;
        public int EnemiesRemaining { get; private set; }

        public event System.Action OnWaveStart;
        public event System.Action OnWaveEnd;

        // Feuert für jedes Portal, das sich vor einem Wellenstart neu öffnet (nicht für die beim Spielstart offenen)
        public static event System.Action<SpawnPortal> OnPortalOpened;

        // 1-basierte Nummer der laufenden Welle bzw. (zwischen den Wellen) der nächsten Welle
        public int UpcomingWaveNumber => CurrentWaveIndex + 1;

        // Schwierigkeitsstufe dieses Spiels (beim Start aus GameSession gelesen)
        public DifficultySO Difficulty { get; private set; }

        // Wellen-Bonus der zuletzt abgeschlossenen Welle (inkl. Einkommens-Faktor), z. B. für die Telemetrie
        public float LastWaveBonus { get; private set; }
        // Früh-Start-Bonus beim letzten Wellenstart (0 = regulär gestartet)
        public float LastEarlyCallBonus { get; private set; }

        // Neuer Wellengegner (Welle, Schrein-/Händler-Angreifer, Dev-Boss) – nach Initialize
        public static event System.Action<EnemyBrain> OnEnemySpawned;

        // ---------------- Balancing-Kurven (w = 1-basierte Wellennummer) ----------------

        // Anzahl-Multiplikator M(w) inkl. Schwierigkeit – auch für das Kopfgeld (mehr Gegner ≠ mehr Splitter)
        public float CountMultiplier(int wave)
        {
            float m = Mathf.Pow(1f + CountGrowth * Mathf.Max(0, wave - 1), CountExponent);
            return m * (Difficulty != null ? Difficulty.CountMultiplier : 1f) * PlayerFactor(CountPerExtraPlayer);
        }

        // HP-Multiplikator der Normalgegner inkl. Schwierigkeit
        public float HpMultiplier(int wave)
        {
            int n = Mathf.Max(0, wave - 1);
            float m = (1f + HpLinearPerWave * n) * Mathf.Pow(HpGrowthPerWave, n);
            return m * (Difficulty != null ? Difficulty.HpMultiplier : 1f) * PlayerFactor(HpPerExtraPlayer);
        }

        // HP-Multiplikator der Bosse: w · min(1, w/BossRampWave)² inkl. Schwierigkeit
        public float BossHpMultiplier(int wave)
        {
            float w = Mathf.Max(1, wave);
            float ramp = BossRampWave > 0f ? Mathf.Min(1f, w / BossRampWave) : 1f;
            return w * ramp * ramp * (Difficulty != null ? Difficulty.BossHpMultiplier : 1f) * PlayerFactor(BossHpPerExtraPlayer);
        }

        // HP-Multiplikator für einen Gegnertyp (Boss-Zweig über Config.IsBoss)
        public float HpMultiplierFor(EnemyConfigSO config, int wave)
        {
            return config != null && config.IsBoss ? BossHpMultiplier(wave) * SiegeLevels.BossHpFactor : HpMultiplier(wave);
        }

        // Gegnerschaden-Multiplikator inkl. Schwierigkeit
        public float DamageMultiplier(int wave)
        {
            float m = 1f + DamageGrowthPerWave * Mathf.Max(0, wave - 1);
            return m * (Difficulty != null ? Difficulty.DamageMultiplier : 1f) * SiegeLevels.EnemyDamageFactor;
        }

        // Spawn-Intervall-Faktor der Nicht-Boss-Gruppen (ohne Boss-Eskorte)
        public float IntervalFactor(int wave)
        {
            return Mathf.Max(MinIntervalFactor, Mathf.Pow(IntervalDecayPerWave, Mathf.Max(0, wave - 1)));
        }

        // Einkommens-Faktor der Stufe (Kill-Drops + Wellen-Bonus)
        public float IncomeMultiplier => Difficulty != null ? Difficulty.IncomeMultiplier : 1f;

        // ---------------- Wellen-Typen (Boss, Ernte) ----------------

        // Basiswelle (Asset oder Endless-Vorlage) mit Zusatzgruppen, ohne Rampe – nur zum Prüfen der Zusammensetzung
        private WaveConfigSO UnscaledWave(int waveNumber)
        {
            if (Waves == null || Waves.Count == 0 || waveNumber < 1) return null;
            WaveConfigSO wave = waveNumber <= Waves.Count ? Waves[waveNumber - 1] : Waves[Waves.Count - 1];
            return AddExtraGroups(wave, waveNumber);
        }

        // Welle enthält einen Boss (Basis- oder Zusatzgruppe); gecacht – AddExtraGroups legt Kopien an
        private readonly Dictionary<int, bool> _bossWaveCache = new Dictionary<int, bool>();
        public bool IsBossWave(int waveNumber)
        {
            if (_bossWaveCache.TryGetValue(waveNumber, out bool cached)) return cached;
            bool boss = false;
            var wave = UnscaledWave(waveNumber);
            if (wave != null && wave.EnemiesToSpawn != null)
                foreach (var g in wave.EnemiesToSpawn)
                    if (g != null && g.Count > 0 && g.EnemyType != null && g.EnemyType.IsBoss) { boss = true; break; }
            _bossWaveCache[waveNumber] = boss;
            return boss;
        }

        // Ernte-Welle: direkt nach einer Boss-Welle (selbst keine)
        public bool IsHarvestWave(int waveNumber) =>
            HarvestAfterBoss && waveNumber > 1 && IsBossWave(waveNumber - 1) && !IsBossWave(waveNumber);

        // Wellenbonus für das Ende von Welle waveNumber (Formel aus GlobalSettings + WaveConfig-Extra, Schwierigkeit, Ernte)
        public float WaveEndBonus(int waveNumber)
        {
            float bonus = 0f;
            if (Waves != null && Waves.Count > 0)
                bonus = Waves[Mathf.Clamp(waveNumber - 1, 0, Waves.Count - 1)].EndBonusShards;
            var eco = EconomyManager.Instance;
            var settings = eco != null ? eco.Settings : null;
            if (settings != null) bonus += settings.WaveBonusShardsBase + settings.WaveBonusShardsPerWave * Mathf.Max(0, waveNumber - 1);
            bonus *= IncomeMultiplier * SiegeLevels.WaveBonusFactor;
            if (IsHarvestWave(waveNumber)) bonus *= HarvestBonusFactor;
            return bonus;
        }

        // ---------------- Früher Wellenstart ----------------

        private float _buildElapsed;

        // Aktueller Bonus für einen sofortigen Start der nächsten Welle (Server rechnet, Clients lesen NetGame)
        public float EarlyCallBonus
        {
            get
            {
                if (!Net.IsServer) return NetGame.EarlyCallBonus;
                if (IsWaveActive || IsGameOver || UpcomingWaveNumber <= 1 || EarlyCallWindow <= 0f) return 0f;
                float baseBonus = WaveEndBonus(UpcomingWaveNumber);
                if (IsHarvestWave(UpcomingWaveNumber)) baseBonus /= Mathf.Max(0.01f, HarvestBonusFactor);
                float left = 1f - Mathf.Clamp01(_buildElapsed / EarlyCallWindow);
                return Mathf.Floor(baseBonus * EarlyCallMaxFraction * left);
            }
        }

        // Server: freie Bauphase mitzählen (Draft, Laden, Pause und Wellengate halten die Uhr an)
        private void TickBuildPhase()
        {
            if (IsWaveActive || IsGameOver) return;
            if (Net.CanPauseTime && PauseManager.IsPaused) return;
            if (WaveGate.IsBlocked()) return;
            _buildElapsed += Time.deltaTime;
        }

        private int _portalCursor;

        // Sicherheitsnetz Wellenende: laufende Spawn-Coroutines und Zeitpunkt der nächsten Lebend-Zählung
        private int _spawnRoutinesRunning;
        private bool _spawnPhaseStarted;
        private float _nextAliveCheck;

        // Schrein-Verlängerung: startet erst, wenn die übrigen Gruppen durch sind (Versatz relativ zum StartDelay)
        private EnemySpawnInfo _shrineExtension;
        private float _shrineExtensionDelay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnPortalOpened = null;
            OnEnemySpawned = null;
        }

        void Awake()
        {
            Instance = this;
            Difficulty = GameSession.Difficulty;
            Debug.Log($"WaveManager: Schwierigkeit {Difficulty.DisplayName} (HP ×{Difficulty.HpMultiplier}, Anzahl ×{Difficulty.CountMultiplier}, Schaden ×{Difficulty.DamageMultiplier}, Einkommen ×{Difficulty.IncomeMultiplier}, Boss ×{Difficulty.BossHpMultiplier}).");
        }

        // Stufe zur Laufzeit wechseln (Tests / Dev); gilt ab dem nächsten Spawn bzw. Kill
        public void SetDifficulty(DifficultySO difficulty)
        {
            Difficulty = difficulty != null ? difficulty : DifficultySO.Normal;
        }

        void Start()
        {
            EnemyBrain.OnEnemyDeath += HandleEnemyDeath;
            EnemyBrain.OnBossSpawned += HandleBossSpawned;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;
            NetGame.OnReadyChanged += RaiseReadyChanged;
            NetPlayer.OnPlayerJoined += HandlePlayerListChanged;
            NetPlayer.OnPlayerLeft += HandlePlayerListChanged;

            // Portale für die erste Welle schon beim Spielstart sichtbar öffnen (ohne Meldung)
            UpdatePortals(false);
            // Morgengrauen: bis zur Entscheidung „Weiter / Beenden“ keine neue Welle
            WaveGate.Register(this, () => DawnPending, () => "Morgengrauen – Entscheidung des Hosts");
        }

        void OnDestroy()
        {
            WaveGate.Unregister(this);
            EnemyBrain.OnEnemyDeath -= HandleEnemyDeath;
            EnemyBrain.OnBossSpawned -= HandleBossSpawned;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
            NetGame.OnReadyChanged -= RaiseReadyChanged;
            NetPlayer.OnPlayerJoined -= HandlePlayerListChanged;
            NetPlayer.OnPlayerLeft -= HandlePlayerListChanged;
            if (Instance == this) Instance = null;
        }

        // Boss-Ankündigung
        private void HandleBossSpawned(EnemyBrain boss)
        {
            if (boss == null) return;
            ToastUI.Show($"{boss.DisplayName} ist erschienen!");
            GameAudio.Play(GameAudio.Has(SfxId.BossSpawn) ? SfxId.BossSpawn : SfxId.StoneWall);
        }

        private bool IsGameOver => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;

        // ---------------- Dev-Modus ----------------

        // Springt (nur zwischen den Wellen) direkt zu einer Welle; Portale werden entsprechend geöffnet
        public void DevSetStartWave(int waveNumber)
        {
            if (!Net.IsServer || IsWaveActive || waveNumber < 1) return;
            CurrentWaveIndex = waveNumber - 1;
            SyncNet();
            UpdatePortals(false);
        }

        // Tötet alle lebenden Gegner (zählt normal als Kill → Welle endet regulär)
        public void DevKillAllEnemies()
        {
            if (!Net.IsServer) return;
            foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                if (e != null) e.TakeDamage(999999f);
        }

        private void HandleGameOver(string reason)
        {
            // Stop spawning immediately; IsWaveActive stays as-is so the HUD still shows the wave the player died in
            StopAllCoroutines();
        }

        // ---------------- Wellenstart: Bereit-Abstimmung ----------------

        // Anzahl Bereit-Stimmen (verbundene Spieler) / benötigte Stimmen (alle verbundenen Spieler, lebend oder tot)
        public int ReadyCount => NetGame.Ready ? NetGame.Instance.ReadyVotes : 0;
        public int ReadyNeeded => Mathf.Max(1, NetPlayer.All.Count);
        public bool IsLocalReady => NetGame.Ready && NetGame.Instance.IsClientReady(Net.LocalClientId);
        // Warum die nächste Welle (trotz Stimmen) noch wartet, z. B. "Warte auf Kartenwahl …"; leer = frei
        public string WaitReason
        {
            get
            {
                if (Net.IsServer) return WaveGate.IsBlocked(out string reason) ? reason ?? "" : "";
                return NetGame.WaitReason;
            }
        }
        // Stimmen oder Spielerliste geändert
        public event System.Action OnReadyChanged;

        private void RaiseReadyChanged() => OnReadyChanged?.Invoke();
        private void HandlePlayerListChanged(NetPlayer p) => OnReadyChanged?.Invoke();

        // Auf jedem Rechner aufrufbar (HUD-Button): Einzelspieler startet sofort, im Koop setzt bzw. zieht der lokale
        // Spieler seine Bereit-Stimme (nochmal drücken = zurückziehen). Der Server startet, wenn alle bereit sind.
        public void RequestStartWave()
        {
            if (IsWaveActive || IsGameOver) return;
            if (!Net.IsMultiplayer || !NetGame.Ready)
            {
                if (!Net.IsServer) return;
                if (WaveGate.IsBlocked(out string reason))
                {
                    if (!string.IsNullOrEmpty(reason)) ToastUI.Show(reason);
                    return;
                }
                StartNextWave();
                return;
            }
            NetGame.Instance.RequestReady(!IsLocalReady);
        }

        // Server: alle bereit und nichts blockiert → Welle starten
        private void ServerTickReady()
        {
            if (!NetGame.Ready) return;
            var ng = NetGame.Instance;
            ng.ServerSetWaitReason(WaitReason);
            if (IsWaveActive || IsGameOver) return;
            int needed = ReadyNeeded;
            if (needed <= 1 || ReadyCount < needed || WaveGate.IsBlocked()) return;
            StartNextWave();
        }

        // Server: Welle starten (Solo-Button, Bereit-Abstimmung, Tests). Clients: ohne Wirkung.
        public void StartNextWave()
        {
            if (!Net.IsServer) return;
            if (IsWaveActive || IsGameOver || (Net.CanPauseTime && PauseManager.IsPaused)) return;

            WaveConfigSO waveToSpawn;

            if (CurrentWaveIndex < Waves.Count)
            {
                // Normal defined wave
                waveToSpawn = Waves[CurrentWaveIndex];
            }
            else
            {
                // Endless Mode! Use last wave as template
                if (Waves.Count > 0)
                {
                    Debug.Log("WaveManager: Entering Endless Mode generation...");
                    waveToSpawn = CreateProceduralWave(Waves[Waves.Count - 1], CurrentWaveIndex - Waves.Count + 1);
                }
                else
                {
                    Debug.LogWarning("WaveManager: No WaveConfigs assigned! Cannot start.");
                    return;
                }
            }

            waveToSpawn = AddExtraGroups(waveToSpawn, UpcomingWaveNumber);
            waveToSpawn = ScaleWave(waveToSpawn, UpcomingWaveNumber);
            PrepareElites(waveToSpawn, UpcomingWaveNumber);
            ActiveEvent = EventFor(UpcomingWaveNumber);

            // Früher Start: Splitter-Bonus (sinkt mit der Dauer der Bauphase)
            float early = EarlyCallBonus;
            _buildElapsed = 0f;
            if (early >= 1f && EconomyManager.Instance != null)
            {
                EconomyManager.Instance.EarnShards(early);
                LastEarlyCallBonus = early;
                ToastUI.Show($"Früher Start: +{early:0} Seelensplitter");
            }
            else LastEarlyCallBonus = 0f;
            if (IsHarvestWave(UpcomingWaveNumber)) ToastUI.Show($"Ernte-Welle! Weniger Gegner, +{(HarvestBonusFactor - 1f) * 100f:0} % Wellenbonus");

            if (NetGame.Ready) NetGame.Instance.ServerClearReady();
            UpdatePortals(true);
            StartCoroutine(SpawnWaveRoutine(waveToSpawn));
        }

        // ---------------- Portale ----------------

        // Öffnet alle Portale mit OpenFromWave <= UpcomingWaveNumber
        private void UpdatePortals(bool fireEvents)
        {
            var portals = SpawnPortal.All;
            // Kopie, falls ein Event-Handler die Registry verändert
            var toOpen = new List<SpawnPortal>();
            for (int i = 0; i < portals.Count; i++)
            {
                var p = portals[i];
                if (p != null && !p.IsOpen && p.OpenFromWave <= UpcomingWaveNumber) toOpen.Add(p);
            }
            foreach (var p in toOpen)
            {
                p.SetOpen(true);
                Debug.Log($"WaveManager: Portal '{p.DisplayName}' opened (wave {UpcomingWaveNumber}).");
                if (NetGame.Ready) NetGame.Instance.ServerPortalOpened(p.transform.position, fireEvents);
                if (fireEvents) OnPortalOpened?.Invoke(p);
            }
        }

        public bool HasPortals => SpawnPortal.All.Count > 0;

        // Nächstes offenes Portal (Round-Robin); null, wenn keins offen ist
        private SpawnPortal NextOpenPortal()
        {
            var portals = SpawnPortal.All;
            int n = portals.Count;
            for (int k = 0; k < n; k++)
            {
                var p = portals[(_portalCursor + k) % n];
                if (p != null && p.IsOpen)
                {
                    _portalCursor = (_portalCursor + k + 1) % n;
                    return p;
                }
            }
            return null;
        }

        // Spawn-Position + Rotation für den nächsten Wellengegner. false, wenn es keinen Spawnpunkt gibt.
        private bool TryGetSpawnPose(out Vector3 pos, out Quaternion rot)
        {
            if (HasPortals)
            {
                var portal = NextOpenPortal();
                if (portal == null)
                {
                    // Sicherheitsnetz: Portale existieren, aber keins ist offen (OpenFromWave falsch gesetzt) → erstes öffnen
                    Debug.LogWarning("WaveManager: No open SpawnPortal – opening the first one.");
                    portal = SpawnPortal.All[0];
                    portal.SetOpen(true);
                    if (NetGame.Ready) NetGame.Instance.ServerPortalOpened(portal.transform.position, true);
                    OnPortalOpened?.Invoke(portal);
                }
                pos = portal.GetSpawnPosition();
                rot = portal.SpawnTransform.rotation;
                return true;
            }

            if (SpawnPoints != null && SpawnPoints.Count > 0)
            {
                Transform sp = SpawnPoints[Random.Range(0, SpawnPoints.Count)];
                pos = sp.position;
                rot = sp.rotation;
                return true;
            }

            pos = Vector3.zero;
            rot = Quaternion.identity;
            return false;
        }

        // Helper to create a harder version of a wave on the fly
        private WaveConfigSO CreateProceduralWave(WaveConfigSO template, int endlessDepth)
        {
            WaveConfigSO newWave = ScriptableObject.CreateInstance<WaveConfigSO>();
            newWave.StartDelay = template.StartDelay;
            newWave.EndBonusShards = template.EndBonusShards; // Extra stays constant; the GlobalSettings formula scales with wave number
            newWave.EnemiesToSpawn = new List<EnemySpawnInfo>();

            float multiplier = 1f + (endlessDepth * 0.2f); // +20% count per endless wave

            foreach (var group in template.EnemiesToSpawn)
            {
                EnemySpawnInfo newGroup = new EnemySpawnInfo();
                newGroup.EnemyType = group.EnemyType;
                newGroup.SpawnInterval = Mathf.Max(0.2f, group.SpawnInterval * 0.9f); // Faster spawns (capped at 0.2s)
                newGroup.Count = Mathf.CeilToInt(group.Count * multiplier);
                newWave.EnemiesToSpawn.Add(newGroup);
            }
            return newWave;
        }

        // Kopie der Welle (Assets bleiben unverändert) – mit gleicher Gruppenliste
        private static WaveConfigSO CopyWave(WaveConfigSO wave)
        {
            var copy = ScriptableObject.CreateInstance<WaveConfigSO>();
            copy.StartDelay = wave.StartDelay;
            copy.EndBonusShards = wave.EndBonusShards;
            copy.EnemiesToSpawn = wave.EnemiesToSpawn != null ? new List<EnemySpawnInfo>(wave.EnemiesToSpawn) : new List<EnemySpawnInfo>();
            return copy;
        }

        // Hängt alle Zusatzgruppen mit StartWave <= waveNumber an (auf einer Kopie)
        private WaveConfigSO AddExtraGroups(WaveConfigSO wave, int waveNumber)
        {
            if (ExtraGroups == null || ExtraGroups.Count == 0) return wave;

            WaveConfigSO copy = null;
            foreach (var extra in ExtraGroups)
            {
                if (extra == null) continue;
                int count = extra.GetCount(waveNumber);
                if (count <= 0) continue;
                if (copy == null) copy = CopyWave(wave);
                copy.EnemiesToSpawn.Add(new EnemySpawnInfo { EnemyType = extra.Config, Count = count, SpawnInterval = Mathf.Max(0.05f, extra.SpawnInterval) });
            }
            return copy != null ? copy : wave;
        }

        // Wellen-Rampe auf alle Nicht-Boss-Gruppen (Basiswelle, Endless und Zusatzgruppen): Anzahl × M(w) (aufgerundet),
        // Intervall × IntervalFactor(w); Boss-Wellen nur mit Eskorte (Anzahl × BossWaveEscortFactor, Intervall ÷ Faktor).
        // Danach Deckel MaxEnemiesPerWave (anteilig abgerundet, je Gruppe mind. 1). Wie balance_model.py wave_groups().
        // Arbeitet auf neuen EnemySpawnInfo-Objekten – die WaveConfig-Assets bleiben unverändert.
        public WaveConfigSO ScaleWave(WaveConfigSO wave, int waveNumber)
        {
            if (wave == null || wave.EnemiesToSpawn == null) return wave;

            bool bossWave = false;
            foreach (var g in wave.EnemiesToSpawn)
                if (g != null && g.Count > 0 && g.EnemyType != null && g.EnemyType.IsBoss) bossWave = true;

            float m = CountMultiplier(waveNumber);
            float f = IntervalFactor(waveNumber);
            if (bossWave && BossWaveEscortFactor > 0f && !Mathf.Approximately(BossWaveEscortFactor, 1f))
            {
                m *= BossWaveEscortFactor;
                f /= BossWaveEscortFactor;
            }
            if (!bossWave && IsHarvestWave(waveNumber)) m *= Mathf.Max(0.05f, HarvestCountFactor); // Ernte-Welle

            var scaled = ScriptableObject.CreateInstance<WaveConfigSO>();
            scaled.StartDelay = wave.StartDelay;
            scaled.EndBonusShards = wave.EndBonusShards;
            scaled.EnemiesToSpawn = new List<EnemySpawnInfo>();
            int normal = 0;
            foreach (var g in wave.EnemiesToSpawn)
            {
                if (g == null) continue;
                var copy = new EnemySpawnInfo { EnemyType = g.EnemyType, Count = g.Count, SpawnInterval = g.SpawnInterval };
                bool boss = g.EnemyType != null && g.EnemyType.IsBoss;
                if (!boss && g.Count > 0)
                {
                    copy.Count = Mathf.Max(1, Mathf.CeilToInt(g.Count * m - 1e-4f));
                    copy.SpawnInterval = Mathf.Max(0.05f, g.SpawnInterval * f);
                    normal += copy.Count;
                }
                scaled.EnemiesToSpawn.Add(copy);
            }

            if (MaxEnemiesPerWave > 0 && normal > MaxEnemiesPerWave)
            {
                float s = (float)MaxEnemiesPerWave / normal;
                foreach (var g in scaled.EnemiesToSpawn)
                    if (g.Count > 0 && (g.EnemyType == null || !g.EnemyType.IsBoss))
                        g.Count = Mathf.Max(1, Mathf.FloorToInt(g.Count * s));
            }
            return scaled;
        }

        // Komplette Gruppenliste einer Welle wie beim Start (Basis/Endless + Zusatzgruppen + Rampe), ohne Schrein-
        // Verlängerung und Angreifer – für Tests/Telemetrie/Modellvergleich
        public WaveConfigSO BuildWave(int waveNumber)
        {
            if (Waves == null || Waves.Count == 0 || waveNumber < 1) return null;
            WaveConfigSO wave = waveNumber <= Waves.Count
                ? Waves[waveNumber - 1]
                : CreateProceduralWave(Waves[Waves.Count - 1], waveNumber - Waves.Count);
            return ScaleWave(AddExtraGroups(wave, waveNumber), waveNumber);
        }

        // Startversatz einer Gruppe relativ zum StartDelay (Gruppen laufen parallel)
        private float GetGroupDelay(int index, EnemySpawnInfo group)
        {
            if (group != null && group == _shrineExtension) return _shrineExtensionDelay;
            return index * Mathf.Max(0f, GroupStartOffset);
        }

        // Ende der Spawn-Phase relativ zum StartDelay: die am längsten laufende Gruppe
        private float GetSpawnDuration(WaveConfigSO wave)
        {
            float longest = 0f;
            for (int i = 0; i < wave.EnemiesToSpawn.Count; i++)
            {
                var g = wave.EnemiesToSpawn[i];
                if (g == null || g.Count <= 0) continue;
                longest = Mathf.Max(longest, GetGroupDelay(i, g) + g.Count * g.SpawnInterval);
            }
            return longest;
        }

        // Schrein-Welle: so viele Gegner anhängen, dass die Spawn-Phase lang genug für die Einnahme ist.
        // Vorlage ist die erste Gruppe (Basis-Läufer); die Verlängerung startet, wenn die längste Gruppe fertig ist.
        // Arbeitet auf einer Kopie, die WaveConfig-Assets bleiben unverändert.
        private WaveConfigSO ExtendForShrine(WaveConfigSO wave)
        {
            // Längste Einnahme-Dauer dieser Welle: erwachter Schrein und/oder geöffneter Händler
            float required = 0f;
            string label = null;
            var sm = ShrineManager.Instance;
            Shrine shrine = sm != null ? sm.ActiveShrine : null;
            if (shrine != null && shrine.IsAwakened)
            {
                required = shrine.RequiredTime;
                label = shrine.DisplayName;
            }
            var mm = MerchantManager.Instance;
            Merchant merchant = mm != null ? mm.ActiveMerchant : null;
            if (merchant != null && merchant.IsActive && merchant.RequiredTime > required)
            {
                required = merchant.RequiredTime;
                label = merchant.DisplayName;
            }
            if (label == null || wave.EnemiesToSpawn == null || wave.EnemiesToSpawn.Count == 0) return wave;

            float groupsDuration = GetSpawnDuration(wave);
            float spawnTime = wave.StartDelay + groupsDuration;

            float target = required + ShrineWaveExtraTime;
            EnemySpawnInfo first = wave.EnemiesToSpawn[0];
            float interval = Mathf.Max(0.2f, first.SpawnInterval);
            int extra = Mathf.CeilToInt((target - spawnTime) / interval);
            if (extra <= 0) return wave;

            var copy = CopyWave(wave);
            _shrineExtension = new EnemySpawnInfo { EnemyType = first.EnemyType, Count = extra, SpawnInterval = interval };
            _shrineExtensionDelay = groupsDuration;
            copy.EnemiesToSpawn.Add(_shrineExtension);
            EnemiesRemaining += extra;
            Debug.Log($"WaveManager: Einnahme-Welle ({label}) – +{extra} Gegner, Spawn-Phase {spawnTime:0}s → {target:0}s.");
            return copy;
        }

        private IEnumerator SpawnWaveRoutine(WaveConfigSO wave)
        {
            IsWaveActive = true;
            _shrineExtension = null;
            _spawnPhaseStarted = false;
            _spawnRoutinesRunning = 0;

            // Calculate total enemies BEFORE OnWaveStart, so extra enemies spawned by listeners
            // (e.g. shrine attackers via SpawnEnemyAt) are added on top and not overwritten.
            EnemiesRemaining = 0;
            foreach (var group in wave.EnemiesToSpawn) EnemiesRemaining += group.Count;
            Debug.Log($"WaveManager: Expecting {EnemiesRemaining} enemies.");

            if (NetGame.Ready) NetGame.Instance.ServerWaveStarted(CurrentWaveIndex, EnemiesRemaining);
            OnWaveStart?.Invoke();
            SpawnEventEnemies();
            wave = ExtendForShrine(wave); // Schrein erwacht erst in OnWaveStart
            if (GameManager.Instance != null) GameManager.Instance.StartCombat();
            Debug.Log($"WaveManager: Wave {CurrentWaveIndex + 1} Started!");

            yield return new WaitForSeconds(wave.StartDelay);

            // Alle Gruppen parallel, jeweils mit Startversatz → gemischte Wellen. Das Wellenende hängt nur an
            // EnemiesRemaining (oben vorab für alle Gruppen gezählt), nicht am Ende dieser Coroutines.
            for (int g = 0; g < wave.EnemiesToSpawn.Count; g++)
            {
                var group = wave.EnemiesToSpawn[g];
                if (group == null || group.Count <= 0) continue;
                _spawnRoutinesRunning++;
                StartCoroutine(SpawnGroupRoutine(group, GetGroupDelay(g, group)));
            }
            _spawnPhaseStarted = true;

            // If it was a procedural instance, we might want to clean it up, but Unity GC handles ScriptableObject instances eventually or on scene change.
        }

        private IEnumerator SpawnGroupRoutine(EnemySpawnInfo group, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            for (int i = 0; i < group.Count; i++)
            {
                if (IsGameOver) yield break;
                SpawnEnemy(group.EnemyType);
                // Nach dem letzten Gegner nicht mehr warten: sonst läuft die Routine über das Wellenende hinaus und
                // zählt _spawnRoutinesRunning der NÄCHSTEN Welle herunter → Sicherheitsnetz beendet sie zu früh.
                if (i < group.Count - 1) yield return new WaitForSeconds(group.SpawnInterval);
            }
            _spawnRoutinesRunning--;
        }

        void Update()
        {
            if (!Net.IsServer) return;
            TickBuildPhase();
            SyncNet();
            ServerTickReady();

            // Sicherheitsnetz: Sind alle Gruppen gespawnt und lebt kein Gegner mehr, die Zählung aber > 0
            // (z. B. Gegner ohne Tod-Meldung zerstört), endet die Welle trotzdem.
            if (!IsWaveActive || IsGameOver || !_spawnPhaseStarted || _spawnRoutinesRunning > 0) return;
            if (Time.time < _nextAliveCheck) return;
            _nextAliveCheck = Time.time + 1f;

            if (EnemiesRemaining > 0 && FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Length == 0)
            {
                Debug.LogWarning($"WaveManager: Zählung {EnemiesRemaining}, aber kein Gegner lebt – Welle wird beendet.");
                EnemiesRemaining = 0;
                CheckWaveEnd();
            }
        }

        // Rettungspunkt für festhängende Gegner (EnemyBrain): ein offenes Portal bzw. ein Spawnpunkt
        public bool TryGetRescuePosition(out Vector3 pos)
        {
            return TryGetSpawnPose(out pos, out _);
        }

        private void SpawnEnemy(EnemyConfigSO config)
        {
            if (!TryGetSpawnPose(out Vector3 pos, out Quaternion rot))
            {
                Debug.LogError("WaveManager: No SpawnPortal and no SpawnPoints – cannot spawn!");
                EnemiesRemaining--; // keep the count consistent (same as the missing-prefab case)
                CheckWaveEnd();
                return;
            }

            if (config != null && config.Prefab != null)
            {
                InstantiateEnemy(config, pos, rot, HpMultiplierFor(config, UpcomingWaveNumber), NextEliteFor(config));
            }
            else
            {
                Debug.LogError("WaveManager: Config or Prefab missing for enemy spawn!");
                // If spawn fails, we MUST reduce count, otherwise wave never ends!
                EnemiesRemaining--; 
                CheckWaveEnd();
            }
        }

        // Gemeinsamer Spawn-Pfad: HP × hpMultiplier, Schaden × Multiplikator der laufenden (bzw. nächsten) Welle
        // Mehrspieler: nur auf dem Server; Initialize vor dem Netz-Spawn (HP gehen mit der Spawn-Nachricht raus)
        private EnemyBrain InstantiateEnemy(EnemyConfigSO config, Vector3 pos, Quaternion rot, float hpMultiplier, EliteAffix elite = EliteAffix.None)
        {
            if (!Net.IsServer) return null;
            GameObject go = Instantiate(config.Prefab, pos, rot);
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null)
            {
                brain.Config = config;
                brain.Initialize(hpMultiplier, DamageMultiplier(UpcomingWaveNumber));
                brain.SetSpeedFactor(SiegeLevels.EnemySpeedFactor * (ActiveEvent == WaveEvent.BloodMoon ? BloodMoonSpeed : 1f));
                if (elite != EliteAffix.None) brain.MakeElite(elite); // vor dem Netz-Spawn (HP, Eigenschaft)
            }
            // HP-NetworkVariables setzt EnemyNet.OnNetworkSpawn (vor dem Spawn geschrieben warnt Netcode)
            var no = go.GetComponent<Unity.Netcode.NetworkObject>();
            if (no != null && Net.IsRunning) no.Spawn(true);
            if (brain != null) OnEnemySpawned?.Invoke(brain);
            return brain;
        }

        // Zusatz-Gegner außerhalb der Wellenliste (z. B. Schrein-Angreifer, Dev-Boss F9). Gleicher Spawn-Pfad wie
        // Wellengegner (Config + Initialize). countTowardWave: während einer aktiven Welle wird EnemiesRemaining erhöht,
        // d. h. die Welle endet erst, wenn auch diese Gegner tot sind. hpMultiplier < 0 → Kurve der aktuellen Welle
        // (Bosse: Boss-Kurve).
        // Mehrspieler: nur auf dem Server (Clients: null)
        public EnemyBrain SpawnEnemyAt(EnemyConfigSO config, Vector3 position, float hpMultiplier = -1f, bool countTowardWave = true)
        {
            if (!Net.IsServer || IsGameOver) return null;
            if (config == null || config.Prefab == null)
            {
                Debug.LogError("WaveManager.SpawnEnemyAt: Config or Prefab missing!");
                return null;
            }

            Quaternion rot = Quaternion.identity;
            if (Nexus.Instance != null)
            {
                Vector3 dir = Nexus.Instance.transform.position - position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(dir);
            }

            var brain = InstantiateEnemy(config, position, rot, hpMultiplier < 0f ? HpMultiplierFor(config, UpcomingWaveNumber) : hpMultiplier);
            if (countTowardWave && IsWaveActive) EnemiesRemaining++;
            return brain;
        }

        // Fallback-Gegnertyp für Zusatz-Spawns: erster Typ der aktuellen (bzw. letzten) Welle
        public EnemyConfigSO GetDefaultEnemyType()
        {
            if (Waves == null || Waves.Count == 0) return null;
            var wave = Waves[Mathf.Clamp(CurrentWaveIndex, 0, Waves.Count - 1)];
            if (wave == null || wave.EnemiesToSpawn == null) return null;
            foreach (var g in wave.EnemiesToSpawn)
                if (g != null && g.EnemyType != null && g.EnemyType.Prefab != null) return g.EnemyType;
            return null;
        }

        private void HandleEnemyDeath()
        {
            if (!Net.IsServer || !IsWaveActive || IsGameOver) return;

            EnemiesRemaining--;
            Debug.Log($"WaveManager: Enemy died. Remaining: {EnemiesRemaining}");
            CheckWaveEnd();
        }

        // Welle endet, sobald alle (vorab gezählten) Gegner tot oder fehlgeschlagen sind
        private void CheckWaveEnd()
        {
            if (IsWaveActive && !IsGameOver && EnemiesRemaining <= 0) EndWave();
        }

        public int LastInterest { get; private set; }

        // ---------------- Elite-Gegner ----------------

        private readonly List<EliteAffix> _elitePlan = new List<EliteAffix>();
        private int _eliteNext, _eliteNormalTotal, _normalSpawned;

        // Eliten einer Welle – deterministisch aus Wellennummer und Zusammensetzung (Vorschau auf allen Rechnern gleich)
        public List<EliteAffix> PlanElites(int waveNumber) => PlanElites(BuildWave(waveNumber), waveNumber);

        public List<EliteAffix> PlanElites(WaveConfigSO wave, int waveNumber)
        {
            var plan = new List<EliteAffix>();
            int from = SiegeLevels.EliteFromWave(EliteFromWave);
            if (wave == null || waveNumber < from || IsHarvestWave(waveNumber)) return plan;
            int normal = 0;
            foreach (var g in wave.EnemiesToSpawn)
                if (g != null && g.EnemyType != null && !g.EnemyType.IsBoss) normal += Mathf.Max(0, g.Count);
            float share = Mathf.Min(EliteShareMax, EliteShareStart + EliteSharePerWave * Mathf.Max(0, waveNumber - EliteFromWave))
                          * SiegeLevels.EliteShareFactor;
            var rng = new System.Random(waveNumber * 7919 + 101);
            float exact = normal * share;
            int n = Mathf.FloorToInt(exact) + (rng.NextDouble() < exact - Mathf.Floor(exact) ? 1 : 0);
            n = Mathf.Clamp(n, waveNumber == from ? 1 : 0, MaxElitesPerWave);
            bool thief = false;
            for (int i = 0; i < n; i++)
            {
                var a = (EliteAffix)(1 + rng.Next(EliteInfo.Count));
                if (a == EliteAffix.ShardThief && thief) a = EliteAffix.Swift;
                if (a == EliteAffix.ShardThief) thief = true;
                plan.Add(a);
            }
            return plan;
        }

        // Server, Wellenstart: Plan merken; Eliten werden gleichmäßig über die Normal-Spawns verteilt
        private void PrepareElites(WaveConfigSO wave, int waveNumber)
        {
            _elitePlan.Clear();
            _elitePlan.AddRange(PlanElites(wave, waveNumber));
            // Fluch „Elitenruf“: zusätzliche Eliten mit zufälliger Eigenschaft
            for (int i = 0; i < _curseElites; i++) _elitePlan.Add((EliteAffix)(1 + Random.Range(0, EliteInfo.Count)));
            _curseElites = 0;
            _eliteNext = 0;
            _normalSpawned = 0;
            _eliteNormalTotal = 0;
            foreach (var g in wave.EnemiesToSpawn)
                if (g != null && g.EnemyType != null && !g.EnemyType.IsBoss) _eliteNormalTotal += Mathf.Max(0, g.Count);
        }

        // Händler-Flüche (D7): zusätzliche Eliten bzw. halbierter Bonus für die nächste Welle (Server)
        private int _curseElites;
        private float _bonusDebt = 1f;
        public int PendingCurseElites => _curseElites;
        public bool HasBonusDebt => _bonusDebt < 1f;
        public void AddCurseElites(int n) => _curseElites += Mathf.Max(0, n);
        public void AddBonusDebt(float factor) => _bonusDebt *= Mathf.Clamp01(factor);

        private EliteAffix NextEliteFor(EnemyConfigSO config)
        {
            if (config == null || config.IsBoss || _eliteNext >= _elitePlan.Count) return EliteAffix.None;
            int k = Mathf.Max(1, _eliteNormalTotal / Mathf.Max(1, _elitePlan.Count));
            int i = _normalSpawned++;
            if (i % k != k / 2) return EliteAffix.None;
            return _elitePlan[_eliteNext++];
        }

        // ---------------- Wellen-Ereignisse (C5) ----------------

        // Ereignis der laufenden Welle (Server und Clients: aus EventFor beim Wellenstart bzw. für die Anzeige)
        public WaveEvent ActiveEvent { get; private set; }

        // Ereignis einer Welle – deterministisch (Vorschau auf allen Rechnern gleich): ab EventFromWave alle EventEvery
        // Wellen; fällt der Termin auf eine Boss- oder Ernte-Welle, rückt er auf die nächste freie. Typen im Wechsel.
        public WaveEvent EventFor(int waveNumber)
        {
            if (EventFromWave <= 0 || waveNumber < EventFromWave) return WaveEvent.None;
            int next = EventFromWave, k = 0;
            while (next <= waveNumber)
            {
                int c = next;
                while (IsBossWave(c) || IsHarvestWave(c)) c++;
                if (c == waveNumber) return WaveEvents.Cycle[k % WaveEvents.Cycle.Length];
                if (c > waveNumber) return WaveEvent.None;
                next = c + Mathf.Max(1, EventEvery);
                k++;
            }
            return WaveEvent.None;
        }

        // Kopfgeld-Faktor des laufenden Ereignisses (EconomyManager)
        public float EventBountyFactor => !IsWaveActive ? 1f
            : ActiveEvent == WaveEvent.BloodMoon ? BloodMoonBounty
            : ActiveEvent == WaveEvent.SoulStorm ? SoulStormBounty : 1f;

        // Magnet-Radius-Faktor (ShardPickup) – Seelensturm: selbst einsammeln
        public float ShardMagnetFactor => IsWaveActive && ActiveEvent == WaveEvent.SoulStorm ? SoulStormMagnet : 1f;

        // Server, Wellenstart (OnWaveStart): Ramme an einem offenen Portal
        private void SpawnEventEnemies()
        {
            if (!Net.IsServer || ActiveEvent != WaveEvent.Ram) return;
            var cfg = RamConfig != null ? RamConfig : GetDefaultEnemyType();
            if (cfg == null || !TryGetSpawnPose(out Vector3 pos, out _)) return;
            var brain = SpawnEnemyAt(cfg, pos);
            if (brain != null) brain.MakeElite(EliteAffix.Ram);
        }

        [Tooltip("Gegnertyp der Belagerungsramme (Wellen-Ereignis); leer = erster Typ der Welle.")]
        public EnemyConfigSO RamConfig;

        // ---------------- Morgengrauen (E5) ----------------

        // Morgengrauen erreicht (Welle SiegeLevels.DawnWave überstanden); danach Endlos-Modus
        public bool DawnReached { get; private set; }
        // Server: Entscheidung „Weiter / Beenden“ steht aus (blockiert Kartenwahl und nächste Welle)
        public bool DawnPending { get; private set; }
        // Alle Rechner: Morgengrauen (Server direkt, Clients über NetGame)
        public event System.Action OnDawn;

        // Alle Rechner (NetGame.BroadcastDawn): Morgengrauen anzeigen
        public void ApplyDawn()
        {
            if (DawnReached) return;
            DawnReached = true;
            if (AchievementManager.Instance != null) AchievementManager.Instance.ReportDawn();
            var am = AchievementManager.Instance;
            if (am != null && am.AchievementsAllowed) SiegeLevels.RecordDawn(SiegeLevels.Current);
            OnDawn?.Invoke();
        }

        // Alle Rechner: Host hat „Weiter (Endlos)“ gewählt (Siegbildschirm schließen)
        public event System.Action OnDawnContinued;
        public void RaiseDawnContinued() => OnDawnContinued?.Invoke();

        // Server: Spieler wählt „Weiter (Endlos)“ → Kartenwahl wie nach jeder Welle
        public void ServerContinueAfterDawn()
        {
            if (!Net.IsServer || !DawnPending) return;
            DawnPending = false;
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.PresentUpgrades();
        }

        private void EndWave()
        {
            if (IsGameOver) return;

            Debug.Log("WaveManager: Wave Ended!");
            IsWaveActive = false;
            if (GameManager.Instance != null) GameManager.Instance.EndCombat();
            
            // Splitter-Bonus: WaveBonusShardsBase + WaveBonusShardsPerWave * (completedWave - 1) + WaveConfig-Extra
            // (endless: letzte Config), × Schwierigkeit, Ernte-Welle × HarvestBonusFactor
            LastWaveBonus = 0f;
            if (EconomyManager.Instance != null && EconomyManager.Instance.Settings != null)
            {
                float bonus = WaveEndBonus(CurrentWaveIndex + 1) * _bonusDebt;
                if (_bonusDebt < 1f) ToastUI.Show("Splitterschuld: Wellenbonus halbiert");
                _bonusDebt = 1f;
                LastWaveBonus = bonus;
                EconomyManager.Instance.EarnShards(bonus);
            }
            // Zinsen-Karte: Anteil des Kontostands nach dem Wellenbonus
            LastInterest = 0;
            if (EconomyManager.Instance != null)
            {
                LastInterest = CardEffects.InterestFor(EconomyManager.Instance.CurrentShards);
                if (LastInterest > 0)
                {
                    EconomyManager.Instance.EarnShards(LastInterest);
                    ToastUI.Show($"Zinsen: +{LastInterest} Seelensplitter");
                }
            }
            _buildElapsed = 0f;

            CurrentWaveIndex++;
            if (NetGame.Ready)
            {
                NetGame.Instance.ServerClearReady();
                NetGame.Instance.ServerWaveEnded(CurrentWaveIndex);
            }
            OnWaveEnd?.Invoke();
            ActiveEvent = WaveEvent.None;

            // Morgengrauen: Die Nacht ist überstanden → Siegbildschirm; Kartenwahl erst nach „Weiter (Endlos)“
            if (!DawnReached && CurrentWaveIndex == SiegeLevels.DawnWave)
            {
                DawnPending = true;
                NetGame.BroadcastDawn();
                return;
            }

            // Trigger Upgrade Phase
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.PresentUpgrades();
            }
            
            // No Victory - Infinite War!
        }
    
        // ---------------- Mehrspieler: Spiegel auf Clients ----------------

        // Server: Zustand an NetGame (schreibt nur bei Änderung)
        private void SyncNet()
        {
            if (NetGame.Ready)
            {
                NetGame.Instance.ServerSyncWaveState(CurrentWaveIndex, IsWaveActive, EnemiesRemaining);
                NetGame.Instance.ServerSetEarlyCallBonus(EarlyCallBonus);
            }
        }

        // Client: Werte aus NetGame übernehmen (ohne Ereignisse)
        public void ApplyRemoteWaveState(int waveIndex, bool waveActive, int enemiesRemaining)
        {
            if (Net.IsServer) return;
            CurrentWaveIndex = waveIndex;
            IsWaveActive = waveActive;
            EnemiesRemaining = enemiesRemaining;
        }

        // Client: Server hat eine Welle gestartet
        public void HandleRemoteWaveStart(int waveIndex, int enemiesRemaining)
        {
            if (Net.IsServer) return;
            CurrentWaveIndex = waveIndex;
            IsWaveActive = true;
            EnemiesRemaining = enemiesRemaining;
            if (GameManager.Instance != null) GameManager.Instance.ApplyRemoteState(GameState.Combat);
            ActiveEvent = EventFor(waveIndex + 1);
            OnWaveStart?.Invoke();
        }

        // Client: Server hat die Welle beendet (CurrentWaveIndex bereits erhöht – wie auf dem Server vor OnWaveEnd)
        public void HandleRemoteWaveEnd(int newWaveIndex)
        {
            if (Net.IsServer) return;
            CurrentWaveIndex = newWaveIndex;
            IsWaveActive = false;
            EnemiesRemaining = 0;
            if (GameManager.Instance != null) GameManager.Instance.ApplyRemoteState(GameState.Building);
            OnWaveEnd?.Invoke();
            ActiveEvent = WaveEvent.None;
        }

        // Client: Portal geöffnet (Zuordnung über die Position – Registry-Reihenfolge kann abweichen)
        public void HandleRemotePortalOpened(Vector3 position, bool fireEvents)
        {
            if (Net.IsServer) return;
            SpawnPortal best = null;
            float bestSqr = float.MaxValue;
            foreach (var p in SpawnPortal.All)
            {
                if (p == null) continue;
                float d = (p.transform.position - position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }
            if (best == null) return;
            best.SetOpen(true);
            if (fireEvents) OnPortalOpened?.Invoke(best);
        }
    }
}