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

        public event Action OnManaChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            // If this is on a root object that isn't DontDestroyOnLoad yet, we might want to do it.
            // But usually GameManager handles the root. We'll assume a "Managers" GameObject.
            
            // Fallback load if not assigned in Inspector
            if (settings == null) settings = Resources.Load<GlobalSettingsSO>("GlobalSettings");
        }

        void Start()
        {
            if (settings != null)
                CurrentMana = settings.StartMana;
            else
                CurrentMana = 100f;
                
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

        public float GetBuildingCost(float baseCost)
        {
            if (settings == null) return baseCost;

            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Combat)
            {
                return baseCost * settings.CombatSurcharge;
            }
            return baseCost;
        }
        
        public float GetRefundAmount(float buildCostPaid)
        {
             if (settings == null) return buildCostPaid * 0.7f;
             return buildCostPaid * settings.RefundRatio;
        }
    }
}