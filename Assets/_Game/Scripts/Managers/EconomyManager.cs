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

        // Seelensplitter: Bau-Währung (keine passive Regen; Quellen: Kopfgeld + Wellen-Bonus)
        public float CurrentShards { get; private set; }

        public event Action OnManaChanged;
        public event Action OnShardsChanged;

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
            EnemyBrain.OnEnemyDeath += HandleEnemyDeath;
        }

        void OnDisable()
        {
            EnemyBrain.OnEnemyDeath -= HandleEnemyDeath;
        }

        // Splitter-Kopfgeld pro Kill (Mana gibt es nur noch über passive Regen)
        private void HandleEnemyDeath()
        {
            float bounty = settings != null ? settings.ShardsPerKill : 6f;
            if (bounty > 0f) AddShards(bounty);
        }

        void OnDestroy()
        {
            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart -= RefillMana;
                WaveManager.Instance.OnWaveEnd -= RefillMana;
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

            CurrentShards = settings != null ? settings.StartShards : 130f;
                
            OnManaChanged?.Invoke();
            OnShardsChanged?.Invoke();

            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart += RefillMana;
                WaveManager.Instance.OnWaveEnd += RefillMana;
            }
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