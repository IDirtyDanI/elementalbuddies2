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

        public int GetCount(int waveNumber)
        {
            if (Config == null || waveNumber < StartWave) return 0;
            return Mathf.Max(0, BaseCount + Mathf.FloorToInt(PerWave * (waveNumber - StartWave)));
        }
    }

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

        public int CurrentWaveIndex { get; private set; } = 0;
        public bool IsWaveActive { get; private set; } = false;
        public int EnemiesRemaining { get; private set; }

        public event System.Action OnWaveStart;
        public event System.Action OnWaveEnd;

        // Feuert für jedes Portal, das sich vor einem Wellenstart neu öffnet (nicht für die beim Spielstart offenen)
        public static event System.Action<SpawnPortal> OnPortalOpened;

        // 1-basierte Nummer der laufenden Welle bzw. (zwischen den Wellen) der nächsten Welle
        public int UpcomingWaveNumber => CurrentWaveIndex + 1;

        // HP-Bonus für Gegner der aktuellen Welle (+8 HP pro Welle)
        public float CurrentHpBonus => CurrentWaveIndex * 8f;

        private int _portalCursor;

        // Schrein-Verlängerung: startet erst, wenn die übrigen Gruppen durch sind (Versatz relativ zum StartDelay)
        private EnemySpawnInfo _shrineExtension;
        private float _shrineExtensionDelay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnPortalOpened = null;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            EnemyBrain.OnEnemyDeath += HandleEnemyDeath;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;

            // Portale für die erste Welle schon beim Spielstart sichtbar öffnen (ohne Meldung)
            UpdatePortals(false);
        }

        void OnDestroy()
        {
            EnemyBrain.OnEnemyDeath -= HandleEnemyDeath;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
        }

        private bool IsGameOver => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;

        // ---------------- Dev-Modus ----------------

        // Springt (nur zwischen den Wellen) direkt zu einer Welle; Portale werden entsprechend geöffnet
        public void DevSetStartWave(int waveNumber)
        {
            if (IsWaveActive || waveNumber < 1) return;
            CurrentWaveIndex = waveNumber - 1;
            UpdatePortals(false);
        }

        // Tötet alle lebenden Gegner (zählt normal als Kill → Welle endet regulär)
        public void DevKillAllEnemies()
        {
            foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                if (e != null) e.TakeDamage(999999f);
        }

        private void HandleGameOver(string reason)
        {
            // Stop spawning immediately; IsWaveActive stays as-is so the HUD still shows the wave the player died in
            StopAllCoroutines();
        }

        public void StartNextWave()
        {
            if (IsWaveActive || IsGameOver || PauseManager.IsPaused) return;

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
            var sm = ShrineManager.Instance;
            Shrine shrine = sm != null ? sm.ActiveShrine : null;
            if (shrine == null || !shrine.IsAwakened || wave.EnemiesToSpawn == null || wave.EnemiesToSpawn.Count == 0) return wave;

            float groupsDuration = GetSpawnDuration(wave);
            float spawnTime = wave.StartDelay + groupsDuration;

            float target = shrine.RequiredTime + ShrineWaveExtraTime;
            EnemySpawnInfo first = wave.EnemiesToSpawn[0];
            float interval = Mathf.Max(0.2f, first.SpawnInterval);
            int extra = Mathf.CeilToInt((target - spawnTime) / interval);
            if (extra <= 0) return wave;

            var copy = CopyWave(wave);
            _shrineExtension = new EnemySpawnInfo { EnemyType = first.EnemyType, Count = extra, SpawnInterval = interval };
            _shrineExtensionDelay = groupsDuration;
            copy.EnemiesToSpawn.Add(_shrineExtension);
            EnemiesRemaining += extra;
            Debug.Log($"WaveManager: Schrein-Welle ({shrine.DisplayName}) – +{extra} Gegner, Spawn-Phase {spawnTime:0}s → {target:0}s.");
            return copy;
        }

        private IEnumerator SpawnWaveRoutine(WaveConfigSO wave)
        {
            IsWaveActive = true;
            _shrineExtension = null;

            // Calculate total enemies BEFORE OnWaveStart, so extra enemies spawned by listeners
            // (e.g. shrine attackers via SpawnEnemyAt) are added on top and not overwritten.
            EnemiesRemaining = 0;
            foreach (var group in wave.EnemiesToSpawn) EnemiesRemaining += group.Count;
            Debug.Log($"WaveManager: Expecting {EnemiesRemaining} enemies.");

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
                StartCoroutine(SpawnGroupRoutine(group, GetGroupDelay(g, group)));
            }

            // If it was a procedural instance, we might want to clean it up, but Unity GC handles ScriptableObject instances eventually or on scene change.
        }

        private IEnumerator SpawnGroupRoutine(EnemySpawnInfo group, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            for (int i = 0; i < group.Count; i++)
            {
                if (IsGameOver) yield break;
                SpawnEnemy(group.EnemyType);
                yield return new WaitForSeconds(group.SpawnInterval);
            }
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
                InstantiateEnemy(config, pos, rot, CurrentHpBonus); // +8 HP per wave logic
            }
            else
            {
                Debug.LogError("WaveManager: Config or Prefab missing for enemy spawn!");
                // If spawn fails, we MUST reduce count, otherwise wave never ends!
                EnemiesRemaining--; 
                CheckWaveEnd();
            }
        }

        private EnemyBrain InstantiateEnemy(EnemyConfigSO config, Vector3 pos, Quaternion rot, float hpBonus)
        {
            GameObject go = Instantiate(config.Prefab, pos, rot);
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null)
            {
                brain.Config = config;
                brain.Initialize(hpBonus);
            }
            return brain;
        }

        // Zusatz-Gegner außerhalb der Wellenliste (z. B. Schrein-Angreifer). Gleicher Spawn-Pfad wie Wellengegner
        // (Config + Initialize(hpBonus)). countTowardWave: während einer aktiven Welle wird EnemiesRemaining erhöht,
        // d. h. die Welle endet erst, wenn auch diese Gegner tot sind. hpBonus < 0 → Bonus der aktuellen Welle.
        public EnemyBrain SpawnEnemyAt(EnemyConfigSO config, Vector3 position, float hpBonus = -1f, bool countTowardWave = true)
        {
            if (IsGameOver) return null;
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

            var brain = InstantiateEnemy(config, position, rot, hpBonus < 0f ? CurrentHpBonus : hpBonus);
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
            if (!IsWaveActive || IsGameOver) return;

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

            if (EconomyManager.Instance != null)
            {
                var settings = EconomyManager.Instance.Settings;
                if (settings != null)
                    bonus += settings.WaveBonusShardsBase + settings.WaveBonusShardsPerWave * CurrentWaveIndex; // CurrentWaveIndex = completedWave - 1
                EconomyManager.Instance.AddShards(bonus);
            }

            CurrentWaveIndex++;
            OnWaveEnd?.Invoke();
            
            // Trigger Upgrade Phase
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.PresentUpgrades();
            }
            
            // No Victory - Infinite War!
        }
    }
}