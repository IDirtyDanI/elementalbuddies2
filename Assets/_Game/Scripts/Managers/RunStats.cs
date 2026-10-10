using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Run-Statistik für den Rückblick am Game Over (Plan „Fesselung“ E1). Zählt lokal mit, auf jedem Rechner;
    // einige Werte kennt nur der Server (bester Buddy, Durchbrüche zum Nexus, Früh-Start-Bonus) – auf Clients bleiben sie leer.
    // Wird vom GameOverUI angelegt (eigenes Objekt), lebt so lange wie die Spielszene.
    public class RunStats : MonoBehaviour
    {
        public static RunStats Instance { get; private set; }

        public float StartTime { get; private set; }
        public int Kills { get; private set; }
        public float ChampionDamage { get; private set; }
        public float TowerDamage { get; private set; }
        public float ShardsEarned { get; private set; }
        public float EarlyCallBonusTotal { get; private set; }
        public int EarlyStarts { get; private set; }
        public int Fusions { get; private set; }
        public int EliteKills { get; private set; }
        public int BuddiesBuilt => _seenBuddies.Count;
        public readonly List<string> BossesKilled = new List<string>();
        public readonly Dictionary<string, int> Leaks = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Cards = new Dictionary<string, int>();

        private class BuddyRecord { public string Label; public float Damage; }
        private readonly Dictionary<int, BuddyRecord> _buddyDamage = new Dictionary<int, BuddyRecord>();
        private readonly HashSet<int> _seenBuddies = new HashSet<int>();
        private float _nextScan;

        private WaveManager _waves;
        private EconomyManager _eco;
        private UpgradeManager _upgrades;
        private MerchantManager _merchants;
        private FusionManager _fusion;

        public static RunStats Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("RunStats");
            return go.AddComponent<RunStats>();
        }

        void Awake()
        {
            Instance = this;
            StartTime = Time.unscaledTime;
        }

        void Start()
        {
            EnemyBrain.OnEnemyKilled += HandleKilled;
            EnemyBrain.OnLocalHit += HandleHit;
            EnemyBrain.OnDamageDealt += HandleDamageDealt;
            EnemyBrain.OnReachedNexus += HandleReachedNexus;
            _waves = WaveManager.Instance;
            if (_waves != null) _waves.OnWaveStart += HandleWaveStart;
            _eco = EconomyManager.Instance;
            if (_eco != null) _eco.OnShardsEarned += HandleEarned;
            _upgrades = UpgradeManager.Instance;
            if (_upgrades != null) _upgrades.OnUpgradePicked += HandleUpgrade;
            _merchants = MerchantManager.Instance;
            if (_merchants != null) _merchants.OnCardPicked += HandleMerchantCard;
            _fusion = FusionManager.Instance;
            if (_fusion != null) _fusion.OnFused += HandleFused;
        }

        void OnDestroy()
        {
            EnemyBrain.OnEnemyKilled -= HandleKilled;
            EnemyBrain.OnLocalHit -= HandleHit;
            EnemyBrain.OnDamageDealt -= HandleDamageDealt;
            EnemyBrain.OnReachedNexus -= HandleReachedNexus;
            if (_waves != null) _waves.OnWaveStart -= HandleWaveStart;
            if (_eco != null) _eco.OnShardsEarned -= HandleEarned;
            if (_upgrades != null) _upgrades.OnUpgradePicked -= HandleUpgrade;
            if (_merchants != null) _merchants.OnCardPicked -= HandleMerchantCard;
            if (_fusion != null) _fusion.OnFused -= HandleFused;
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // Gebaute Buddies: einmal pro Sekunde die Registry ansehen (Bau-Vorschauen sind dann schon wieder deaktiviert)
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            foreach (var b in ElementalBuddy.Active)
                if (b != null && b.isActiveAndEnabled) _seenBuddies.Add(b.GetInstanceID());
        }

        private void HandleKilled(EnemyBrain e)
        {
            Kills++;
            if (e != null && e.IsBoss) BossesKilled.Add(e.DisplayName);
            if (e != null && e.IsElite) EliteKills++;
        }

        private void HandleHit(EnemyBrain e, float amount, bool fromPlayer, bool lethal)
        {
            if (fromPlayer) ChampionDamage += amount;
            else TowerDamage += amount;
        }

        private void HandleDamageDealt(EnemyBrain e, float amount, bool fromPlayer)
        {
            var src = EnemyBrain.DamageSource;
            if (fromPlayer || src == null) return;
            int id = src.GetInstanceID();
            if (!_buddyDamage.TryGetValue(id, out var r)) _buddyDamage[id] = r = new BuddyRecord();
            r.Damage += amount;
            r.Label = $"{src.DisplayName} (Stufe {src.Level})";
        }

        private void HandleReachedNexus(EnemyBrain e)
        {
            string n = e != null ? e.DisplayName : "?";
            Leaks.TryGetValue(n, out int c);
            Leaks[n] = c + 1;
        }

        private void HandleWaveStart()
        {
            if (_waves == null || _waves.LastEarlyCallBonus < 1f) return;
            EarlyCallBonusTotal += _waves.LastEarlyCallBonus;
            EarlyStarts++;
        }

        private void HandleEarned(float amount) => ShardsEarned += Mathf.Max(0f, amount);
        private void HandleUpgrade(UpgradeDefinitionSO card) { if (card != null) AddCard(card.Title); }
        private void HandleMerchantCard(MerchantCardSO card) { if (card != null) AddCard(card.Title); }
        private void HandleFused(FusionBuddy f) => Fusions++;

        private void AddCard(string title)
        {
            if (string.IsNullOrEmpty(title)) return;
            Cards.TryGetValue(title, out int c);
            Cards[title] = c + 1;
        }

        // Buddy mit dem meisten zugeordneten Schaden (nur Server); false, wenn nichts bekannt ist
        public bool TryGetBestBuddy(out string label, out float damage, out float share)
        {
            label = null; damage = 0f; share = 0f;
            float total = 0f;
            foreach (var r in _buddyDamage.Values)
            {
                total += r.Damage;
                if (r.Damage > damage) { damage = r.Damage; label = r.Label; }
            }
            if (label == null || TowerDamage <= 0f) return false;
            share = damage / Mathf.Max(TowerDamage, total);
            return true;
        }

        public float PlaySeconds => Time.unscaledTime - StartTime;
    }
}
