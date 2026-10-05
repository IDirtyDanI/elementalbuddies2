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
        public event System.Action<UpgradeDefinitionSO> OnUpgradePicked;

        // Bisher gewählte Karten (in Wahl-Reihenfolge, Mehrfachwahl möglich)
        private readonly List<UpgradeDefinitionSO> _picked = new List<UpgradeDefinitionSO>();
        public IReadOnlyList<UpgradeDefinitionSO> PickedUpgrades => _picked;

        // Upgrade-Auswahl gerade offen (Spiel pausiert)
        public bool IsChoosing { get; private set; }
        // Wellenende fiel in eine offene Händler-Kartenauswahl → danach zeigen
        private bool _pendingPresent;

        // Spieler-Startwerte vor allen Karten (für die Pause-Übersicht)
        public float BasePlayerSpeed { get; private set; }
        public float BasePlayerMaxHP { get; private set; }
        public float BaseDamageMultiplier { get; private set; } = 1f;
        public float BaseCooldownMultiplier { get; private set; } = 1f;
        public float BaseMobilityMultiplier { get; private set; } = 1f;

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
             if (PlayerStatsRef == null) PlayerStatsRef = FindFirstObjectByType<PlayerStats>();
             if (PlayerControllerRef == null) PlayerControllerRef = FindFirstObjectByType<PlayerController>();
             if (InteractionRef == null) InteractionRef = FindFirstObjectByType<InteractionManager>();
             if (GlobalSettings == null) Debug.LogWarning("UpgradeManager: Please assign GlobalSettings in Inspector!");

             // Spieler-Basiswerte merken
             if (PlayerControllerRef != null) BasePlayerSpeed = PlayerControllerRef.MoveSpeed;
             if (PlayerStatsRef != null) BasePlayerMaxHP = PlayerStatsRef.MaxHP;
             if (PlayerAbilities.Instance != null)
             {
                 BaseDamageMultiplier = PlayerAbilities.Instance.DamageMultiplier;
                 BaseCooldownMultiplier = PlayerAbilities.Instance.CooldownMultiplier;
                 BaseMobilityMultiplier = PlayerAbilities.Instance.MobilityMultiplier;
             }

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
            // Händler-Kartenauswahl ist offen → warten, bis dort gewählt wurde (MerchantManager ruft PresentPendingUpgrades)
            if (MerchantManager.Instance != null && MerchantManager.Instance.IsChoosing)
            {
                _pendingPresent = true;
                return;
            }

            List<UpgradeDefinitionSO> selection = GetRandomUpgrades(3);
            if (selection.Count == 0)
            {
                // Pool leer → keine Pause, wartende Händlerauswahl trotzdem freigeben
                OnUpgradeSelected?.Invoke();
                return;
            }
            
            // Pause Game
            Time.timeScale = 0f;
            IsChoosing = true;
            
            OnUpgradesAvailable?.Invoke(selection);
        }

        // Aufgeschobenen Upgrade-Bildschirm zeigen. true, wenn einer wartete.
        public bool PresentPendingUpgrades()
        {
            if (!_pendingPresent) return false;
            _pendingPresent = false;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return false;
            PresentUpgrades();
            return true;
        }

        // count zufällige Karten ohne Doppelte; Slot-Karten fallen raus, sobald die Slot-Obergrenze erreicht ist
        public List<UpgradeDefinitionSO> GetRandomUpgrades(int count)
        {
            var pool = new List<UpgradeDefinitionSO>();
            if (AllUpgrades != null)
            {
                foreach (var up in AllUpgrades)
                {
                    if (up == null || pool.Contains(up)) continue;
                    if (!IsDraftable(up)) continue;
                    pool.Add(up);
                }
            }

            var picked = new List<UpgradeDefinitionSO>();
            while (picked.Count < count && pool.Count > 0)
            {
                int idx = Random.Range(0, pool.Count);
                picked.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return picked;
        }

        public static bool IsDraftable(UpgradeDefinitionSO up)
        {
            if (up == null) return false;
            var slots = BuddySlotManager.Instance;
            if (up.StatToBuff == StatType.BuddySlot && slots != null && slots.IsAtCap) return false;
            // Deckel pro Run (z. B. Beschwörerband höchstens 6×)
            if (up.MaxPicks > 0 && Instance != null && Instance.GetPickCount(up) >= up.MaxPicks) return false;
            return true;
        }

        // Wie oft diese Karte in diesem Run schon gewählt wurde
        public int GetPickCount(UpgradeDefinitionSO up)
        {
            int n = 0;
            foreach (var p in _picked)
                if (p == up) n++;
            return n;
        }

        public void SelectUpgrade(UpgradeDefinitionSO upgrade)
        {
            // Pause-Menü liegt darüber -> keine Wahl (defensiv)
            if (PauseManager.IsPaused || upgrade == null) return;

            ApplyUpgrade(upgrade);
            
            // Resume Game
            Time.timeScale = 1f;
            IsChoosing = false;

            _picked.Add(upgrade);
            OnUpgradePicked?.Invoke(upgrade);
            OnUpgradeSelected?.Invoke();
        }

        // Unit-Werte vor allen Karten (aus dem Backup)
        public bool TryGetBaseUnitStats(UnitConfigSO cfg, out float damage, out float range, out float fireRate)
        {
            foreach (var b in _backups)
            {
                if (b.Config == cfg)
                {
                    damage = b.Damage;
                    range = b.Range;
                    fireRate = b.FireRate;
                    return true;
                }
            }
            damage = range = fireRate = 0f;
            return false;
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
            if (upgrade.StatToBuff == StatType.BuddySlot)
            {
                ApplySlotUpgrade(upgrade);
                return;
            }
            if (upgrade.StatToBuff == StatType.ShardGain)
            {
                ApplyShardGain(upgrade);
                return;
            }

            if (upgrade.Target == UpgradeTarget.Player)
            {
                if (upgrade.StatToBuff == StatType.Speed && PlayerControllerRef != null)
                {
                    PlayerControllerRef.MoveSpeed = ModifyValue(PlayerControllerRef.MoveSpeed, BasePlayerSpeed, upgrade);
                }
                else if (upgrade.StatToBuff == StatType.Health && PlayerStatsRef != null)
                {
                    PlayerStatsRef.MaxHP = ModifyValue(PlayerStatsRef.MaxHP, BasePlayerMaxHP, upgrade);
                    PlayerStatsRef.Heal(0);
                }
                else if (upgrade.StatToBuff == StatType.Damage && PlayerAbilities.Instance != null)
                {
                    // Spieler-Schaden skaliert alle Fähigkeiten des aktiven Champions. Prozent additiv gegen den Basis-
                    // Multiplikator (Arkane Wucht +25 % → +0,25 je Karte), flache Werte zählen als ganze Prozent
                    var pa = PlayerAbilities.Instance;
                    pa.DamageMultiplier = upgrade.IsPercentage
                        ? ModifyValue(pa.DamageMultiplier, BaseDamageMultiplier, upgrade)
                        : pa.DamageMultiplier + upgrade.Value / 100f;
                }
                else if (upgrade.StatToBuff == StatType.Cooldown && PlayerAbilities.Instance != null)
                {
                    // Abklingzeit-Karte: Wert = Prozent schneller (20 = −20 % Abklingzeit), wirkt auf alle Fähigkeiten
                    var pa = PlayerAbilities.Instance;
                    float pct = Mathf.Clamp(upgrade.Value, 0f, 90f) / 100f;
                    pa.CooldownMultiplier = Mathf.Max(0.2f, pa.CooldownMultiplier * (1f - pct));
                }
                else if (upgrade.StatToBuff == StatType.Mobility && PlayerAbilities.Instance != null)
                {
                    // Mobilitäts-Karte: Blink-Reichweite bzw. Rollen-Distanz (Prozent oder flach in Prozentpunkten)
                    var pa = PlayerAbilities.Instance;
                    pa.MobilityMultiplier = upgrade.IsPercentage
                        ? ModifyValue(pa.MobilityMultiplier, BaseMobilityMultiplier, upgrade)
                        : pa.MobilityMultiplier + upgrade.Value / 100f;
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
            // Basiswerte vor allen Karten (Backup); ohne Backup gilt der aktuelle Wert als Basis
            bool hasBase = TryGetBaseUnitStats(config, out float baseDamage, out float baseRange, out float baseFireRate);

            switch (upgrade.StatToBuff)
            {
                case StatType.Damage: 
                    oldValue = config.Damage;
                    config.Damage = ModifyValue(config.Damage, hasBase ? baseDamage : config.Damage, upgrade); 
                    newValue = config.Damage;
                    break;
                case StatType.Range: 
                    oldValue = config.Range;
                    config.Range = ModifyValue(config.Range, hasBase ? baseRange : config.Range, upgrade); 
                    newValue = config.Range;
                    break;
                case StatType.FireRate: 
                    oldValue = config.FireRate;
                    config.FireRate = ModifyValue(config.FireRate, hasBase ? baseFireRate : config.FireRate, upgrade); 
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

        // Prozentwerte sind ganze Prozent (10 = +10 %, 100 = +100 %) und stapeln ADDITIV gegen den Basiswert vor allen
        // Karten: Basis × (1 + Σ%) – kein Zinseszins (Balancing-Methode (e)). Flache Werte: + Value.
        private static float ModifyValue(float current, float baseValue, UpgradeDefinitionSO upgrade)
        {
            if (upgrade.IsPercentage)
                return current + baseValue * upgrade.Value / 100f;
            else
                return current + upgrade.Value;
        }

        private void ApplyManaUpgrade(UpgradeDefinitionSO upgrade)
        {
            if (upgrade.StatToBuff == StatType.BuddySlot)
            {
                ApplySlotUpgrade(upgrade);
                return;
            }

            if (upgrade.StatToBuff == StatType.ShardGain)
            {
                ApplyShardGain(upgrade);
                return;
            }

            if (GlobalSettings == null) return;
            // Basiswerte aus dem EconomyManager-Backup (vor allen Karten)
            var eco = EconomyManager.Instance;
            bool hasBase = eco != null && eco.Settings == GlobalSettings;
            if (upgrade.StatToBuff == StatType.ManaCap)
                GlobalSettings.ManaCap = ModifyValue(GlobalSettings.ManaCap, hasBase ? eco.BaseManaCap : GlobalSettings.ManaCap, upgrade);
            else if (upgrade.StatToBuff == StatType.ManaRegen)
            {
                GlobalSettings.RegenOutCombat = ModifyValue(GlobalSettings.RegenOutCombat, hasBase ? eco.BaseRegenOut : GlobalSettings.RegenOutCombat, upgrade);
                GlobalSettings.RegenInCombat = ModifyValue(GlobalSettings.RegenInCombat, hasBase ? eco.BaseRegenIn : GlobalSettings.RegenInCombat, upgrade);
            }
        }

        // Seelenernte: Wert = Prozent mehr Splitter pro Kill-Drop (additiv, 15 = +15 %)
        private void ApplyShardGain(UpgradeDefinitionSO upgrade)
        {
            if (EconomyManager.Instance != null) EconomyManager.Instance.AddShardGainPercent(upgrade.Value);
        }

        private void ApplySlotUpgrade(UpgradeDefinitionSO upgrade)
        {
            if (BuddySlotManager.Instance == null) return;
            int amount = Mathf.Max(1, Mathf.RoundToInt(upgrade.Value));
            BuddySlotManager.Instance.AddSlot(amount);
        }
    }
}