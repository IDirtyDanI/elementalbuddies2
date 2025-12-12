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
        }

        void OnDestroy()
        {
            EnemyBrain.OnEnemyDeath -= HandleEnemyDeath;
        }

        public void StartNextWave()
        {
            if (IsWaveActive) return;

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
            newWave.EndBonusMana = template.EndBonusMana; // Constant bonus? Or scale it? Let's keep constant for challenge.
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
            if (!IsWaveActive) return;

            EnemiesRemaining--;
            Debug.Log($"WaveManager: Enemy died. Remaining: {EnemiesRemaining}");

            if (EnemiesRemaining <= 0)
            {
                EndWave();
            }
        }

        private void EndWave()
        {
            Debug.Log("WaveManager: Wave Ended!");
            IsWaveActive = false;
            if (GameManager.Instance != null) GameManager.Instance.EndCombat();
            
            // Mana Bonus: If we are in range, take from config. If endless, take from last config.
            float bonus = 0;
            if (CurrentWaveIndex < Waves.Count)
                bonus = Waves[CurrentWaveIndex].EndBonusMana;
            else if (Waves.Count > 0)
                bonus = Waves[Waves.Count - 1].EndBonusMana;

            if (EconomyManager.Instance != null) EconomyManager.Instance.AddMana(bonus);

            CurrentWaveIndex++;
            OnWaveEnd?.Invoke();
            
            // No Victory - Infinite War!
        }
    }
}