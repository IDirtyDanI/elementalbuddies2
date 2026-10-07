using UnityEngine;
using System;

namespace ElementalBuddies
{
    // Mehrspieler: Seelensplitter sind eine Teamkasse. Nur der Server ändert CurrentShards/ShardGainPercent (AddShards, EarnShards,
    // TrySpendShards); NetGame.Economy spiegelt beides per NetworkVariable auf die Clients, dort feuern OnShardsChanged und
    // (per RPC) OnShardsEarned ebenfalls. Clients geben nie selbst aus, sondern fragen per Server-RPC an (NetGame.Build).
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private GlobalSettingsSO settings;

        // Mana ist pro Spieler (PlayerMana, Paket A). Diese Member leiten auf PlayerMana.Local weiter; ohne lokale
        // Spielfigur (z. B. vor dem Netz-Spawn) gilt der alte lokale Wert als Fallback.
        private float _legacyMana;
        private const string ManaObsolete = "Mana ist pro Spieler: PlayerMana.Local verwenden.";

        [Obsolete(ManaObsolete)]
        public float CurrentMana => PlayerMana.Local != null ? PlayerMana.Local.CurrentMana : _legacyMana;
        [Obsolete(ManaObsolete)]
        public float MaxMana => PlayerMana.Local != null ? PlayerMana.Local.MaxMana : LegacyMaxMana;
        private float LegacyMaxMana => settings != null ? settings.ManaCap : 200f;
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

        // Nur Server (Clients bekommen den Wert über NetGame.Economy; dort ignoriert, um Doppelzählung zu vermeiden)
        public void AddShardGainPercent(float percent)
        {
            if (!Net.IsServer) return;
            ShardGainPercent = Mathf.Max(0f, ShardGainPercent + percent);
            PushToNet();
        }

        [Obsolete(ManaObsolete)]
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
            PlayerMana.OnLocalManaChanged += RaiseManaChanged;
        }

        void OnDisable()
        {
            EnemyBrain.OnEnemyKilled -= HandleEnemyKilled;
            PlayerMana.OnLocalManaChanged -= RaiseManaChanged;
        }

#pragma warning disable 618 // eigene, veraltete Mana-Member
        private void RaiseManaChanged() => OnManaChanged?.Invoke();
#pragma warning restore 618

        // Splitter-Kopfgeld pro Kill × Gegnertyp-Faktor × Seelenernte – fällt als Drop, der Spieler sammelt ihn ein.
        // Balancing: ÷ Anzahl-Multiplikator M(w) (mehr Gegner bringen nicht mehr Splitter pro Welle), × Drop-Abnahme
        // im Spätspiel (GlobalSettings.DropFactor) und × Einkommens-Faktor der Schwierigkeit.
        private void HandleEnemyKilled(EnemyBrain enemy)
        {
            // Drops entscheidet nur der Server (ShardPickup repliziert sie an die Clients)
            if (!Net.IsServer) return;
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
            _legacyMana = LegacyMaxMana;

            CurrentShards = settings != null ? settings.StartShards : 110f;
            // Teamkasse: Server meldet den Startwert, Clients übernehmen den Server-Stand (falls NetGame schon gespawnt ist)
            if (Net.IsServer) PushToNet();
            else if (NetGame.Ready) NetGame.Instance.ClientPullEconomy();
                
            RaiseManaChanged();
            OnShardsChanged?.Invoke();

            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart += RefillMana;
                WaveManager.Instance.OnWaveEnd += RefillMana;
                WaveManager.Instance.OnWaveEnd += RecallShards;
            }
        }

        // Wellenende: alle noch liegenden Seelensplitter fliegen zur nächsten lebenden Spielfigur (Server entscheidet)
        private void RecallShards()
        {
            if (!Net.IsServer) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            ShardPickup.RecallAll();
        }

        // Welle gestartet / geschafft -> Fallback-Mana auffüllen (PlayerMana füllt sich selbst auf)
        private void RefillMana()
        {
            if (PlayerMana.Local != null) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            _legacyMana = LegacyMaxMana;
            RaiseManaChanged();
        }

        void Update()
        {
            if (settings == null || PlayerMana.Local != null) return;

            float regenRate = (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Combat) 
                ? settings.RegenInCombat 
                : settings.RegenOutCombat;

            _legacyMana = Mathf.Min(LegacyMaxMana, _legacyMana + regenRate * Time.deltaTime);
            RaiseManaChanged();
        }

        [Obsolete(ManaObsolete)]
        public void AddMana(float amount)
        {
            if (PlayerMana.Local != null)
            {
                PlayerMana.Local.Add(amount);
                return;
            }
            _legacyMana = Mathf.Min(LegacyMaxMana, _legacyMana + amount);
            RaiseManaChanged();
        }

        [Obsolete(ManaObsolete)]
        public bool TrySpendMana(float amount)
        {
            if (PlayerMana.Local != null) return PlayerMana.Local.TrySpend(amount);
            if (_legacyMana >= amount)
            {
                _legacyMana -= amount;
                RaiseManaChanged();
                return true;
            }
            return false;
        }

        // Nur Server (Teamkasse). Auf Clients wirkungslos.
        public void AddShards(float amount)
        {
            if (amount <= 0f || !Net.IsServer) return;
            CurrentShards += amount;
            PushToNet();
            OnShardsChanged?.Invoke();
        }

        // Wie AddShards, zählt aber als Einnahme (OnShardsEarned, feuert per RPC auch auf den Clients). Nur Server.
        public void EarnShards(float amount)
        {
            if (amount <= 0f || !Net.IsServer) return;
            AddShards(amount);
            OnShardsEarned?.Invoke(amount);
            if (NetGame.Ready) NetGame.Instance.ServerShardsEarned(amount);
        }

        // ---------------- Netzwerk (NetGame.Economy) ----------------

        private void PushToNet()
        {
            if (Net.IsServer && NetGame.Ready) NetGame.Instance.ServerSetEconomy(CurrentShards, ShardGainPercent);
        }

        // Clients: Stand der Teamkasse vom Server
        internal void NetApplyShards(float shards)
        {
            if (Mathf.Approximately(shards, CurrentShards)) return;
            CurrentShards = shards;
            OnShardsChanged?.Invoke();
        }

        internal void NetApplyShardGain(float percent) => ShardGainPercent = Mathf.Max(0f, percent);

        // Clients: Einnahme der Teamkasse (für Erfolge, z. B. „Seelensammler“)
        internal void NetShardsEarned(float amount)
        {
            if (amount > 0f) OnShardsEarned?.Invoke(amount);
        }

        public bool CanAfford(float shardCost)
        {
            return CurrentShards >= shardCost;
        }

        // Nur Server (Teamkasse); Clients fragen per Server-RPC an (NetGame.RequestBuild/Upgrade/Fuse) -> hier immer false
        public bool TrySpendShards(float amount)
        {
            if (!Net.IsServer) return false;
            if (CurrentShards >= amount)
            {
                CurrentShards -= amount;
                PushToNet();
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