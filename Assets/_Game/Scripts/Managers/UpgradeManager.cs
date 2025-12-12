using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public class UpgradeManager : MonoBehaviour
    {
        public static UpgradeManager Instance { get; private set; }

        [Header("Data")]
        public List<UpgradeDefinitionSO> AllUpgrades;

        [Header("References")]
        public PlayerStats PlayerStatsRef;
        public PlayerController PlayerControllerRef;
        public InteractionManager InteractionRef; 
        public GlobalSettingsSO GlobalSettings;

        // UI Event
        public event System.Action<List<UpgradeDefinitionSO>> OnUpgradesAvailable;
        public event System.Action OnUpgradeSelected;

        // Backup for UnitConfigs
        private struct UnitBackup
        {
            public UnitConfigSO Config;
            public float Damage;
            public float Range;
            public float FireRate;
        }
        private List<UnitBackup> _backups = new List<UnitBackup>();

        void Awake()
        {
            Instance = this;
        }
        
        void Start()
        {
             if (PlayerStatsRef == null) PlayerStatsRef = FindObjectOfType<PlayerStats>();
             if (PlayerControllerRef == null) PlayerControllerRef = FindObjectOfType<PlayerController>();
             if (InteractionRef == null) InteractionRef = FindObjectOfType<InteractionManager>();
             if (GlobalSettings == null) Debug.LogWarning("UpgradeManager: Please assign GlobalSettings in Inspector!");

             // Create Backups
             if (InteractionRef != null)
             {
                 foreach (var config in InteractionRef.UnitConfigs)
                 {
                     if (config != null)
                     {
                         _backups.Add(new UnitBackup 
                         { 
                             Config = config,
                             Damage = config.Damage,
                             Range = config.Range,
                             FireRate = config.FireRate
                         });
                     }
                 }
             }
        }

        void OnDestroy()
        {
            // Restore Backups
            foreach (var backup in _backups)
            {
                if (backup.Config != null)
                {
                    backup.Config.Damage = backup.Damage;
                    backup.Config.Range = backup.Range;
                    backup.Config.FireRate = backup.FireRate;
                }
            }
        }

        public void PresentUpgrades()
        {
            List<UpgradeDefinitionSO> selection = GetRandomUpgrades(3);
            
            // Pause Game
            Time.timeScale = 0f;
            
            OnUpgradesAvailable?.Invoke(selection);
        }

        private List<UpgradeDefinitionSO> GetRandomUpgrades(int count)
        {
            if (AllUpgrades.Count <= count) return new List<UpgradeDefinitionSO>(AllUpgrades);

            List<UpgradeDefinitionSO> pool = new List<UpgradeDefinitionSO>(AllUpgrades);
            List<UpgradeDefinitionSO> picked = new List<UpgradeDefinitionSO>();

            for (int i = 0; i < count; i++)
            {
                int idx = Random.Range(0, pool.Count);
                picked.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return picked;
        }

        public void SelectUpgrade(UpgradeDefinitionSO upgrade)
        {
            ApplyUpgrade(upgrade);
            
            // Resume Game
            Time.timeScale = 1f;
            
            OnUpgradeSelected?.Invoke();
        }

        private void ApplyUpgrade(UpgradeDefinitionSO upgrade)
        {
            Debug.Log($"Applying Upgrade: {upgrade.Title}");

            switch (upgrade.Type)
            {
                case UpgradeType.StatIncrease:
                    ApplyStatUpgrade(upgrade);
                    break;
                case UpgradeType.Heal:
                    if (PlayerStatsRef != null) PlayerStatsRef.Heal(upgrade.Value);
                    break;
                case UpgradeType.ManaBoost:
                    ApplyManaUpgrade(upgrade);
                    break;
            }
        }

        private void ApplyStatUpgrade(UpgradeDefinitionSO upgrade)
        {
            if (upgrade.Target == UpgradeTarget.Player)
            {
                if (upgrade.StatToBuff == StatType.Speed && PlayerControllerRef != null)
                {
                    PlayerControllerRef.MoveSpeed = ModifyValue(PlayerControllerRef.MoveSpeed, upgrade);
                }
                else if (upgrade.StatToBuff == StatType.Health && PlayerStatsRef != null)
                {
                    PlayerStatsRef.MaxHP = ModifyValue(PlayerStatsRef.MaxHP, upgrade);
                    PlayerStatsRef.Heal(0); 
                }
            }
            else
            {
                if (InteractionRef == null) return;
                var allConfigs = InteractionRef.UnitConfigs;

                foreach (var conf in allConfigs)
                {
                    if (MatchesTarget(conf.Type, upgrade.Target))
                    {
                        ApplyToConfig(conf, upgrade);
                    }
                }
            }
        }

        private void ApplyToConfig(UnitConfigSO config, UpgradeDefinitionSO upgrade)
        {
            float oldValue = 0f;
            float newValue = 0f;

            switch (upgrade.StatToBuff)
            {
                case StatType.Damage: 
                    oldValue = config.Damage;
                    config.Damage = ModifyValue(config.Damage, upgrade); 
                    newValue = config.Damage;
                    break;
                case StatType.Range: 
                    oldValue = config.Range;
                    config.Range = ModifyValue(config.Range, upgrade); 
                    newValue = config.Range;
                    break;
                case StatType.FireRate: 
                    oldValue = config.FireRate;
                    config.FireRate = ModifyValue(config.FireRate, upgrade); 
                    newValue = config.FireRate;
                    break;
            }
            Debug.Log($"UpgradeManager: Modified {config.name} {upgrade.StatToBuff} from {oldValue} to {newValue}");
        }

        private bool MatchesTarget(UnitType unitType, UpgradeTarget target)
        {
            if (target == UpgradeTarget.AllUnits) return true;
            if (target == UpgradeTarget.FireUnit && unitType == UnitType.Fire) return true;
            if (target == UpgradeTarget.IceUnit && unitType == UnitType.Ice) return true;
            if (target == UpgradeTarget.EarthUnit && unitType == UnitType.Earth) return true;
            if (target == UpgradeTarget.LightUnit && unitType == UnitType.Light) return true;
            return false;
        }

        private float ModifyValue(float current, UpgradeDefinitionSO upgrade)
        {
            if (upgrade.IsPercentage)
                return current * (1f + upgrade.Value);
            else
                return current + upgrade.Value;
        }

        private void ApplyManaUpgrade(UpgradeDefinitionSO upgrade)
        {
            if (GlobalSettings == null) return;
            if (upgrade.StatToBuff == StatType.ManaCap)
                GlobalSettings.ManaCap = ModifyValue(GlobalSettings.ManaCap, upgrade);
            else if (upgrade.StatToBuff == StatType.ManaRegen)
            {
                GlobalSettings.RegenOutCombat = ModifyValue(GlobalSettings.RegenOutCombat, upgrade);
                GlobalSettings.RegenInCombat = ModifyValue(GlobalSettings.RegenInCombat, upgrade);
            }
        }
    }
}