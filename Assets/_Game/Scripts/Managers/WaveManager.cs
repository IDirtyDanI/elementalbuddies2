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
            return config != null && config.IsBoss ? BossHpMultiplier(wave) : HpMultiplier(wave);
        }

        // Gegnerschaden-Multiplikator inkl. Schwierigkeit
        public float DamageMultiplier(int wave)
        {
            float m = 1f + DamageGrowthPerWave * Mathf.Max(0, wave - 1);
            return m * (Difficulty != null ? Difficulty.DamageMultiplier : 1f);
        }

        // Spawn-Intervall-Faktor der Nicht-Boss-Gruppen (ohne Boss-Eskorte)
        public float IntervalFactor(int wave)
        {
            return Mathf.Max(MinIntervalFactor, Mathf.Pow(IntervalDecayPerWave, Mathf.Max(0, wave - 1)));
        }

        // Einkommens-Faktor der Stufe (Kill-Drops + Wellen-Bonus)
        public float IncomeMultiplier => Difficulty != null ? Difficulty.IncomeMultiplier : 1f;

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
        }

        void OnDestroy()
        {
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
                InstantiateEnemy(config, pos, rot, HpMultiplierFor(config, UpcomingWaveNumber));
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
        private EnemyBrain InstantiateEnemy(EnemyConfigSO config, Vector3 pos, Quaternion rot, float hpMultiplier)
        {
            if (!Net.IsServer) return null;
            GameObject go = Instantiate(config.Prefab, pos, rot);
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null)
            {
                brain.Config = config;
                brain.Initialize(hpMultiplier, DamageMultiplier(UpcomingWaveNumber));
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

        private void EndWave()
        {
            if (IsGameOver) return;

            Debug.Log("WaveManager: Wave Ended!");
            IsWaveActive = false;
            if (GameManager.Instance != null) GameManager.Instance.EndCombat();
            
            // Shard Bonus: WaveBonusShardsBase + WaveBonusShardsPerWave * (completedWave - 1),
            // plus the optional per-wave extra from the WaveConfig (endless: last config).
            float bonus = 0f;
            if (CurrentWaveIndex < Waves.Count)
                bonus = Waves[CurrentWaveIndex].EndBonusShards;
            else if (Waves.Count > 0)
                bonus = Waves[Waves.Count - 1].EndBonusShards;

            LastWaveBonus = 0f;
            if (EconomyManager.Instance != null)
            {
                var settings = EconomyManager.Instance.Settings;
                if (settings != null)
                    bonus += settings.WaveBonusShardsBase + settings.WaveBonusShardsPerWave * CurrentWaveIndex; // CurrentWaveIndex = completedWave - 1
                bonus *= IncomeMultiplier; // Schwierigkeit
                LastWaveBonus = bonus;
                EconomyManager.Instance.EarnShards(bonus);
            }

            CurrentWaveIndex++;
            if (NetGame.Ready)
            {
                NetGame.Instance.ServerClearReady();
                NetGame.Instance.ServerWaveEnded(CurrentWaveIndex);
            }
            OnWaveEnd?.Invoke();
            
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
            if (NetGame.Ready) NetGame.Instance.ServerSyncWaveState(CurrentWaveIndex, IsWaveActive, EnemiesRemaining);
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