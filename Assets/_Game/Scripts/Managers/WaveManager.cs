using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public class WaveManager : MonoBehaviour
    {
        public static WaveManager Instance { get; private set; }

        public List<WaveConfigSO> Waves;
        public List<Transform> SpawnPoints;

        public int CurrentWaveIndex { get; private set; } = 0;
        public bool IsWaveActive { get; private set; } = false;
        public int EnemiesRemaining { get; private set; }

        public event System.Action OnWaveStart;
        public event System.Action OnWaveEnd;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            EnemyBrain.OnEnemyDeath += HandleEnemyDeath;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;
        }

        void OnDestroy()
        {
            EnemyBrain.OnEnemyDeath -= HandleEnemyDeath;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
        }

        private bool IsGameOver => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;

        private void HandleGameOver(string reason)
        {
            // Stop spawning immediately; IsWaveActive stays as-is so the HUD still shows the wave the player died in
            StopAllCoroutines();
        }

        public void StartNextWave()
        {
            if (IsWaveActive || IsGameOver) return;

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

            StartCoroutine(SpawnWaveRoutine(waveToSpawn));
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

        private IEnumerator SpawnWaveRoutine(WaveConfigSO wave)
        {
            IsWaveActive = true;
            OnWaveStart?.Invoke();
            if (GameManager.Instance != null) GameManager.Instance.StartCombat();
            Debug.Log($"WaveManager: Wave {CurrentWaveIndex + 1} Started!");

            yield return new WaitForSeconds(wave.StartDelay);

            // Calculate total enemies
            EnemiesRemaining = 0;
            foreach (var group in wave.EnemiesToSpawn) EnemiesRemaining += group.Count;
            Debug.Log($"WaveManager: Expecting {EnemiesRemaining} enemies.");

            foreach (var group in wave.EnemiesToSpawn)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    if (IsGameOver) yield break;
                    SpawnEnemy(group.EnemyType);
                    yield return new WaitForSeconds(group.SpawnInterval);
                }
            }
            
            // If it was a procedural instance, we might want to clean it up, but Unity GC handles ScriptableObject instances eventually or on scene change.
        }

        private void SpawnEnemy(EnemyConfigSO config)
        {
            if (SpawnPoints.Count == 0) return;
            Transform sp = SpawnPoints[Random.Range(0, SpawnPoints.Count)];

            if (config != null && config.Prefab != null)
            {
                GameObject go = Instantiate(config.Prefab, sp.position, sp.rotation);
                var brain = go.GetComponent<EnemyBrain>();
                if (brain != null)
                {
                    brain.Config = config;
                    brain.Initialize(CurrentWaveIndex * 8f); // +8 HP per wave logic
                }
            }
            else
            {
                Debug.LogError("WaveManager: Config or Prefab missing for enemy spawn!");
                // If spawn fails, we MUST reduce count, otherwise wave never ends!
                EnemiesRemaining--; 
            }
        }

        private void HandleEnemyDeath()
        {
            if (!IsWaveActive || IsGameOver) return;

            EnemiesRemaining--;
            Debug.Log($"WaveManager: Enemy died. Remaining: {EnemiesRemaining}");

            if (EnemiesRemaining <= 0)
            {
                EndWave();
            }
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