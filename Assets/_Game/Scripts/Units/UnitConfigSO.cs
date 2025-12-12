using UnityEngine;

namespace ElementalBuddies
{
    public enum UnitType
    {
        Fire,
        Ice,
        Earth,
        Light,
        EnemyRunner // For EnemyConfigSO which will inherit from this
    }

    [CreateAssetMenu(fileName = "UnitConfig", menuName = "ElementalBuddies/Unit Config", order = 1)]
    public class UnitConfigSO : ScriptableObject
    {
        public UnitType Type;
        public float CostOutCombat;
        public float CostInCombat => CostOutCombat * CombatSurcharge; // Read-only property
        public float Range;
        public float FireRate; // Attacks per second
        public float Damage;
        public GameObject Prefab; // Reference to the unit's prefab

        // Reference to global settings for surcharge calculation
        private GlobalSettingsSO _globalSettings;
        public GlobalSettingsSO GlobalSettings
        {
            get
            {
                if (_globalSettings == null)
                {
                    // Attempt to load from resources or find existing instance
                    _globalSettings = Resources.Load<GlobalSettingsSO>("GlobalSettings");
                    if (_globalSettings == null)
                    {
                        Debug.LogError("GlobalSettingsSO not found! Please create one in Assets/ScriptableObjects/Configs.");
                    }
                }
                return _globalSettings;
            }
        }

        // Helper for combat surcharge (used in CostInCombat calculation)
        protected float CombatSurcharge
        {
            get
            {
                if (GlobalSettings != null)
                {
                    return GlobalSettings.CombatSurcharge;
                }
                return 1.0f; // Default if settings not found
            }
        }
    }
}