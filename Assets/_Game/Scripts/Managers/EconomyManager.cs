using UnityEngine;
using System;

namespace ElementalBuddies
{
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private GlobalSettingsSO settings;

        public float CurrentMana { get; private set; }
        public float MaxMana => settings != null ? settings.ManaCap : 200f;
        public GlobalSettingsSO Settings => settings;

        // Startwerte (vor Upgrade-Karten) und aktuelle Regeneration, z. B. für die Pause-Übersicht
        public float BaseManaCap => _startManaCap;
        public float BaseRegenOut => _startRegenOut;
        public float BaseRegenIn => _startRegenIn;
        public float RegenOut => settings != null ? settings.RegenOutCombat : 0f;
        public float RegenIn => settings != null ? settings.RegenInCombat : 0f;

        // Seelensplitter: Bau-Währung (keine passive Regen; Quellen: Kill-Drops (ShardPickup) + Wellen-Bonus)
        public float CurrentShards { get; private set; }

        // Wellenkarte „Seelenernte": +X % auf Kill-Drops (additiv, nicht auf den Wellen-Bonus)
        public float ShardGainPercent { get; private set; }
        public float ShardGainMultiplier => 1f + ShardGainPercent / 100f;

        public void AddShardGainPercent(float percent)
        {
            ShardGainPercent = Mathf.Max(0f, ShardGainPercent + percent);
        }

        public event Action OnManaChanged;
        public event Action OnShardsChanged;
        // Seelensplitter verdient (eingesammelte Drops + Wellen-Bonus; nicht Verkauf/Dev-Auffüllen) – für Erfolge
        public event Action<float> OnShardsEarned;

        // Backup variables for Reset
        private float _startManaCap;
        private float _startRegenOut;
        private float _startRegenIn;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            // Fallback load if not assigned in Inspector
            if (settings == null) settings = Resources.Load<GlobalSettingsSO>("GlobalSettings");

            // Backup original values
            if (settings != null)
            {
                _startManaCap = settings.ManaCap;
                _startRegenOut = settings.RegenOutCombat;
                _startRegenIn = settings.RegenInCombat;
            }
        }

        void OnEnable()
        {
            EnemyBrain.OnEnemyKilled += HandleEnemyKilled;
        }

        void OnDisable()
        {
            EnemyBrain.OnEnemyKilled -= HandleEnemyKilled;
        }

        // Splitter-Kopfgeld pro Kill × Gegnertyp-Faktor × Seelenernte – fällt als Drop, der Spieler sammelt ihn ein.
        // Balancing: ÷ Anzahl-Multiplikator M(w) (mehr Gegner bringen nicht mehr Splitter pro Welle), × Drop-Abnahme
        // im Spätspiel (GlobalSettings.DropFactor) und × Einkommens-Faktor der Schwierigkeit.
        private void HandleEnemyKilled(EnemyBrain enemy)
        {
            if (enemy == null || (GameManager.Instance != null && GameManager.Instance.IsGameOver)) return;
            float bounty = GetKillBounty(enemy.Config, CurrentWaveNumber);
            if (bounty > 0f) ShardPickup.Spawn(enemy.transform.position, bounty, settings);
        }

        // Laufende (bzw. zwischen den Wellen: nächste) Welle, 1-basiert
        private static int CurrentWaveNumber => WaveManager.Instance != null ? WaveManager.Instance.UpcomingWaveNumber : 1;

        // Kopfgeld eines Kills dieses Gegnertyps in Welle wave (inkl. Seelenernte)
        public float GetKillBounty(EnemyConfigSO config, int wave)
        {
            float bounty = settings != null ? settings.ShardsPerKill : 6f;
            if (config != null) bounty *= config.BountyMultiplier;
            var wm = WaveManager.Instance;
            if (wm != null)
            {
                float m = wm.CountMultiplier(wave);
                if (m > 0f) bounty /= m;
                bounty *= wm.IncomeMultiplier;
            }
            if (settings != null) bounty *= settings.DropFactor(wave);
            return bounty * ShardGainMultiplier;
        }

        void OnDestroy()
        {
            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart -= RefillMana;
                WaveManager.Instance.OnWaveEnd -= RefillMana;
                WaveManager.Instance.OnWaveEnd -= RecallShards;
            }

            // Restore original values to keep Editor clean
            if (settings != null)
            {
                settings.ManaCap = _startManaCap;
                settings.RegenOutCombat = _startRegenOut;
                settings.RegenInCombat = _startRegenIn;
            }
        }

        void Start()
        {
            // Start immer mit vollem Mana (GlobalSettings.StartMana wird nicht mehr genutzt)
            CurrentMana = MaxMana;

            CurrentShards = settings != null ? settings.StartShards : 110f;
                
            OnManaChanged?.Invoke();
            OnShardsChanged?.Invoke();

            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart += RefillMana;
                WaveManager.Instance.OnWaveEnd += RefillMana;
                WaveManager.Instance.OnWaveEnd += RecallShards;
            }
        }

        // Wellenende: alle noch liegenden Seelensplitter fliegen zum Spieler
        private void RecallShards()
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            ShardPickup.RecallAll();
        }

        // Welle gestartet / geschafft -> Player-Mana komplett auffüllen
        private void RefillMana()
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            CurrentMana = MaxMana;
            OnManaChanged?.Invoke();
        }

        void Update()
        {
            if (settings == null) return;

            float regenRate = (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Combat) 
                ? settings.RegenInCombat 
                : settings.RegenOutCombat;

            AddMana(regenRate * Time.deltaTime);
        }

        public void AddMana(float amount)
        {
            CurrentMana += amount;
            if (CurrentMana > MaxMana) CurrentMana = MaxMana;
            OnManaChanged?.Invoke();
        }

        public bool TrySpendMana(float amount)
        {
            if (CurrentMana >= amount)
            {
                CurrentMana -= amount;
                OnManaChanged?.Invoke();
                return true;
            }
            return false;
        }

        public void AddShards(float amount)
        {
            if (amount <= 0f) return;
            CurrentShards += amount;
            OnShardsChanged?.Invoke();
        }

        // Wie AddShards, zählt aber als Einnahme (OnShardsEarned)
        public void EarnShards(float amount)
        {
            if (amount <= 0f) return;
            AddShards(amount);
            OnShardsEarned?.Invoke(amount);
        }

        public bool CanAfford(float shardCost)
        {
            return CurrentShards >= shardCost;
        }

        public bool TrySpendShards(float amount)
        {
            if (CurrentShards >= amount)
            {
                CurrentShards -= amount;
                OnShardsChanged?.Invoke();
                return true;
            }
            return false;
        }

        // Baukosten in Seelensplittern (inkl. Kampf-Aufschlag)
        public float GetBuildingCost(float baseCost)
        {
            if (settings == null) return baseCost;

            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Combat)
            {
                return baseCost * settings.CombatSurcharge;
            }
            return baseCost;
        }
        
        // Rückerstattung in Seelensplittern
        public float GetRefundAmount(float buildCostPaid)
        {
             if (settings == null) return buildCostPaid * 0.7f;
             return buildCostPaid * settings.RefundRatio;
        }
    }
}