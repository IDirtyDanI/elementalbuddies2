using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ElementalBuddies
{
    // Balancing-Telemetrie (wiki Design/Balancing-Methode (h)): eine CSV pro Run unter
    // Application.persistentDataPath/telemetry/run_<datum>.csv, eine Zeile pro Welle.
    // Eine Zeile umfasst die Welle selbst UND die anschließende Bauphase bis zum nächsten Wellenstart
    // (Ausgaben, Karten, Händler). Aufstellung (Buddies, Stufen, tower_dps_sum) = Stand beim Wellenstart.
    // Schaden: über EnemyBrain.OnDamageDealt (tatsächlich abgezogene HP). player_dmg = Champion-Angriffe/-Zauber
    // (EnemyBrain.DealPlayerDamage); tower_dmg = alles andere (Buddies, DoTs wie Brand/Fluch, Schrein-/Händler-Aura).
    // Liegt auf dem Managers-Objekt der Spielszene.
    public class RunTelemetry : MonoBehaviour
    {
        public static RunTelemetry Instance { get; private set; }

        [Tooltip("CSV schreiben (im Build und Editor).")]
        public bool Enabled = true;
        [Tooltip("Pfad der CSV beim Start im Log ausgeben.")]
        public bool LogPath = true;

        public string FilePath { get; private set; }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string Header =
            "run_id,version,difficulty,champion,stage4_unlocked,dev_cheats,wave,wave_type," +
            "enemies_spawned,enemies_killed,enemies_leaked,ehp_total,hp_total,spawn_duration_s,wave_duration_s," +
            "nexus_hp_start,nexus_dmg,player_dmg_taken,buddies_lost," +
            "shards_start,shards_earned_drops,shards_earned_bonus,shards_spent,shards_end," +
            "slots,buddies,buddy_level_sum,buddy_levels,fusions,supers,fusions_made,tower_dps_sum," +
            "tower_dmg,player_dmg,boss_name,boss_hp,boss_ttk_s,boss_reached_nexus," +
            "card_offered,card_picked,merchant_bought,p_estimate,game_over_reason";

        // Datensatz einer Welle (+ folgende Bauphase)
        private class Row
        {
            public int Wave;
            public bool Shrine, Merchant, Boss, Cheats;
            public string Champion = "";
            public int Spawned, Killed, Leaked, BuddiesLost, FusionsMade;
            public float Ehp, Hp;
            public float StartTime, LastSpawnTime = -1f, EndTime = -1f;
            public float NexusStart, NexusDmg, PlayerDmgTaken;
            public float ShardsStart, EarnedTotal, Bonus, Spent;
            public int Slots, Buddies, LevelSum, Fusions, Supers;
            public string Levels = "";
            public float TowerDps, TowerDmg, PlayerDmg;
            public string BossName = "";
            public float BossHp, BossSpawnTime = -1f, BossTtk = -1f;
            public bool BossReachedNexus;
            public string Offered = "", Picked = "", MerchantBought = "";
        }

        private Row _row;           // laufende Welle bzw. Bauphase danach (noch nicht geschrieben)
        private string _runId;
        private bool _finished;
        private float _lastNexusHp, _lastPlayerHp, _lastShards;
        private PlayerStats _player;
        private readonly HashSet<EnemyBrain> _bosses = new HashSet<EnemyBrain>();

        void Awake()
        {
            Instance = this;
        }

        void OnEnable()
        {
            WaveManager.OnEnemySpawned += HandleSpawned;
            EnemyBrain.OnEnemyKilled += HandleKilled;
            EnemyBrain.OnReachedNexus += HandleReachedNexus;
            EnemyBrain.OnDamageDealt += HandleDamage;
            ElementalBuddy.OnBuddyDestroyed += HandleBuddyDestroyed;
            Shrine.OnAnyShrineAwakened += HandleShrine;
            Merchant.OnAnyMerchantActivated += HandleMerchant;
        }

        void OnDisable()
        {
            WaveManager.OnEnemySpawned -= HandleSpawned;
            EnemyBrain.OnEnemyKilled -= HandleKilled;
            EnemyBrain.OnReachedNexus -= HandleReachedNexus;
            EnemyBrain.OnDamageDealt -= HandleDamage;
            ElementalBuddy.OnBuddyDestroyed -= HandleBuddyDestroyed;
            Shrine.OnAnyShrineAwakened -= HandleShrine;
            Merchant.OnAnyMerchantActivated -= HandleMerchant;
        }

        void Start()
        {
            if (!Enabled) return;
            _runId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "telemetry");
                Directory.CreateDirectory(dir);
                FilePath = Path.Combine(dir, "run_" + _runId + ".csv");
                File.WriteAllText(FilePath, Header + "\n", Encoding.UTF8);
                if (LogPath) Debug.Log("RunTelemetry: schreibt nach " + FilePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("RunTelemetry: CSV kann nicht angelegt werden – " + e.Message);
                FilePath = null;
            }

            var wm = WaveManager.Instance;
            if (wm != null)
            {
                wm.OnWaveStart += HandleWaveStart;
                wm.OnWaveEnd += HandleWaveEnd;
            }
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;
            var eco = EconomyManager.Instance;
            if (eco != null)
            {
                eco.OnShardsEarned += HandleEarned;
                eco.OnShardsChanged += HandleShardsChanged;
                _lastShards = eco.CurrentShards;
            }
            var nexus = Nexus.Instance;
            if (nexus != null)
            {
                nexus.OnHealthChanged += HandleNexusHealth;
                _lastNexusHp = nexus.CurrentHP;
            }
            _player = PlayerAbilities.Instance != null ? PlayerAbilities.Instance.GetComponent<PlayerStats>() : FindFirstObjectByType<PlayerStats>();
            if (_player != null)
            {
                _player.OnHealthChanged += HandlePlayerHealth;
                _lastPlayerHp = _player.CurrentHP;
            }
            var um = UpgradeManager.Instance;
            if (um != null)
            {
                um.OnUpgradesAvailable += HandleOffered;
                um.OnUpgradePicked += HandlePicked;
            }
            if (MerchantManager.Instance != null) MerchantManager.Instance.OnCardPicked += HandleMerchantCard;
            if (FusionManager.Instance != null) FusionManager.Instance.OnFused += HandleFused;
        }

        void OnDestroy()
        {
            // Letzte offene Zeile (z. B. Play-Mode beendet) noch schreiben
            if (_row != null && !_finished) WriteRow(_row, "abgebrochen");
            _row = null;

            var wm = WaveManager.Instance;
            if (wm != null)
            {
                wm.OnWaveStart -= HandleWaveStart;
                wm.OnWaveEnd -= HandleWaveEnd;
            }
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
            var eco = EconomyManager.Instance;
            if (eco != null)
            {
                eco.OnShardsEarned -= HandleEarned;
                eco.OnShardsChanged -= HandleShardsChanged;
            }
            if (Nexus.Instance != null) Nexus.Instance.OnHealthChanged -= HandleNexusHealth;
            if (_player != null) _player.OnHealthChanged -= HandlePlayerHealth;
            var um = UpgradeManager.Instance;
            if (um != null)
            {
                um.OnUpgradesAvailable -= HandleOffered;
                um.OnUpgradePicked -= HandlePicked;
            }
            if (MerchantManager.Instance != null) MerchantManager.Instance.OnCardPicked -= HandleMerchantCard;
            if (FusionManager.Instance != null) FusionManager.Instance.OnFused -= HandleFused;
            if (Instance == this) Instance = null;
        }

        // ---------------- Wellen ----------------

        private void HandleWaveStart()
        {
            if (!Enabled || _finished) return;
            if (_row != null) WriteRow(_row, "");

            var wm = WaveManager.Instance;
            var r = new Row
            {
                Wave = wm != null ? wm.UpcomingWaveNumber : 0,
                StartTime = Time.time,
                NexusStart = Nexus.Instance != null ? Nexus.Instance.CurrentHP : 0f,
                ShardsStart = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentShards : 0f,
                Cheats = CheatsActive(),
                Champion = PlayerAbilities.Instance != null ? PlayerAbilities.Instance.ActiveClass.ToString() : ""
            };
            // Einnahme-Wellen: Schrein erwacht in OnWaveStart (Reihenfolge der Handler offen) → auch per Event nachgetragen
            var sm = ShrineManager.Instance;
            if (sm != null && sm.ActiveShrine != null && sm.ActiveShrine.IsAwakened) r.Shrine = true;
            var mm = MerchantManager.Instance;
            if (mm != null && mm.ActiveMerchant != null && mm.ActiveMerchant.IsActive) r.Merchant = true;
            CaptureSetup(r);
            _row = r;
            _bosses.Clear();
        }

        private void HandleWaveEnd()
        {
            if (_row == null) return;
            _row.EndTime = Time.time;
            var wm = WaveManager.Instance;
            if (wm != null) _row.Bonus = wm.LastWaveBonus;
        }

        private void HandleGameOver(string reason)
        {
            if (_row == null || _finished) return;
            if (_row.EndTime < 0f) _row.EndTime = Time.time;
            WriteRow(_row, string.IsNullOrEmpty(reason) ? "game_over" : reason);
            _row = null;
            _finished = true;
        }

        // Aufstellung beim Wellenstart
        private static void CaptureSetup(Row r)
        {
            var counts = new SortedDictionary<string, int>();
            foreach (var b in ElementalBuddy.Active)
            {
                if (b == null || b.IsDead || b.Config == null) continue;
                r.Buddies++;
                r.LevelSum += b.Level;
                if (b.IsSuper) r.Supers++;
                else if (b.IsFusion) r.Fusions++;
                r.TowerDps += b.EffectiveDamage * b.EffectiveFireRate;
                string key = ShortName(b) + b.Level;
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
            var sb = new StringBuilder();
            foreach (var kv in counts)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
            }
            r.Levels = sb.ToString();
            r.Slots = BuddySlotManager.Instance != null ? BuddySlotManager.Instance.MaxSlots : 0;
        }

        private static string ShortName(ElementalBuddy b)
        {
            if (b.IsFusion || b.IsSuper) return b.Config.name.Replace("Fusion_", "").Replace("Super_", "");
            switch (b.ElementIndex)
            {
                case 0: return "F";
                case 1: return "Ei";
                case 2: return "Er";
                case 3: return "L";
                default: return b.Config.name;
            }
        }

        private static bool CheatsActive()
        {
#if UNITY_EDITOR
            var dev = FindFirstObjectByType<DevTools>();
            return dev != null && dev.isActiveAndEnabled && dev.CheatsActive;
#else
            return false;
#endif
        }

        // ---------------- Ereignisse ----------------

        private void HandleSpawned(EnemyBrain e)
        {
            if (_row == null || e == null) return;
            _row.Spawned++;
            _row.Hp += e.MaxHP;
            _row.Ehp += e.MaxHP / Mathf.Max(0.1f, 1f - e.Armor);
            _row.LastSpawnTime = Time.time;
            if (e.IsBoss)
            {
                _row.Boss = true;
                _bosses.Add(e);
                if (_row.BossSpawnTime < 0f)
                {
                    _row.BossName = e.DisplayName;
                    _row.BossHp = e.MaxHP;
                    _row.BossSpawnTime = Time.time;
                }
            }
        }

        private void HandleKilled(EnemyBrain e)
        {
            if (_row == null || e == null) return;
            _row.Killed++;
            if (e.IsBoss && _bosses.Contains(e) && _row.BossTtk < 0f && _row.BossSpawnTime >= 0f)
                _row.BossTtk = Time.time - _row.BossSpawnTime;
        }

        private void HandleReachedNexus(EnemyBrain e)
        {
            if (_row == null || e == null) return;
            _row.Leaked++;
            if (e.IsBoss) _row.BossReachedNexus = true;
        }

        private void HandleDamage(EnemyBrain e, float amount, bool fromPlayer)
        {
            if (_row == null) return;
            if (fromPlayer) _row.PlayerDmg += amount;
            else _row.TowerDmg += amount;
        }

        private void HandleBuddyDestroyed(ElementalBuddy b)
        {
            if (_row != null && b != null && b.IsDead) _row.BuddiesLost++;
        }

        private void HandleShrine(Shrine s)
        {
            if (_row != null && _row.EndTime < 0f) _row.Shrine = true;
        }

        private void HandleMerchant(Merchant m)
        {
            if (_row != null && _row.EndTime < 0f) _row.Merchant = true;
        }

        private void HandleNexusHealth()
        {
            if (Nexus.Instance == null) return;
            float hp = Nexus.Instance.CurrentHP;
            if (_row != null && hp < _lastNexusHp) _row.NexusDmg += _lastNexusHp - hp;
            _lastNexusHp = hp;
        }

        private void HandlePlayerHealth()
        {
            if (_player == null) return;
            float hp = _player.CurrentHP;
            if (_row != null && hp < _lastPlayerHp) _row.PlayerDmgTaken += _lastPlayerHp - hp;
            _lastPlayerHp = hp;
        }

        private void HandleEarned(float amount)
        {
            if (_row != null) _row.EarnedTotal += amount;
        }

        private void HandleShardsChanged()
        {
            if (EconomyManager.Instance == null) return;
            float now = EconomyManager.Instance.CurrentShards;
            if (_row != null && now < _lastShards) _row.Spent += _lastShards - now;
            _lastShards = now;
        }

        private void HandleOffered(List<UpgradeDefinitionSO> offer)
        {
            if (_row == null || offer == null) return;
            var names = new List<string>();
            foreach (var u in offer) if (u != null) names.Add(u.name);
            _row.Offered = string.Join(";", names);
        }

        private void HandlePicked(UpgradeDefinitionSO up)
        {
            if (_row != null && up != null) _row.Picked = Append(_row.Picked, up.name);
        }

        private void HandleMerchantCard(MerchantCardSO card)
        {
            if (_row != null && card != null) _row.MerchantBought = Append(_row.MerchantBought, card.name);
        }

        private void HandleFused(FusionBuddy f)
        {
            if (_row != null) _row.FusionsMade++;
        }

        private static string Append(string list, string item) => string.IsNullOrEmpty(list) ? item : list + ";" + item;

        // ---------------- Schreiben ----------------

        private void WriteRow(Row r, string gameOverReason)
        {
            if (FilePath == null || r == null) return;
            float end = r.EndTime >= 0f ? r.EndTime : Time.time;
            float duration = Mathf.Max(0f, end - r.StartTime);
            float spawnDur = r.LastSpawnTime >= 0f ? r.LastSpawnTime - r.StartTime : 0f;
            float shardsEnd = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentShards : 0f;
            float drops = Mathf.Max(0f, r.EarnedTotal - r.Bonus);
            float p = duration > 0f && r.TowerDps > 0f ? r.Ehp / duration / r.TowerDps : 0f;

            var types = new List<string>();
            if (r.Boss) types.Add("boss");
            if (r.Shrine) types.Add("schrein");
            if (r.Merchant) types.Add("haendler");
            string waveType = types.Count > 0 ? string.Join("+", types) : "normal";

            var diff = WaveManager.Instance != null ? WaveManager.Instance.Difficulty : null;
            var c = new List<string>
            {
                Q(_runId), Q(Application.version), Q(diff != null ? diff.Id : ""), Q(r.Champion), Stage4Unlocked() ? "1" : "0",
                r.Cheats ? "1" : "0", I(r.Wave), Q(waveType),
                I(r.Spawned), I(r.Killed), I(r.Leaked), F(r.Ehp), F(r.Hp), F(spawnDur, 1), F(duration, 1),
                F(r.NexusStart), F(r.NexusDmg), F(r.PlayerDmgTaken), I(r.BuddiesLost),
                F(r.ShardsStart), F(drops), F(r.Bonus), F(r.Spent), F(shardsEnd),
                I(r.Slots), I(r.Buddies), I(r.LevelSum), Q(r.Levels), I(r.Fusions), I(r.Supers), I(r.FusionsMade), F(r.TowerDps, 1),
                F(r.TowerDmg), F(r.PlayerDmg), Q(r.BossName), r.BossSpawnTime >= 0f ? F(r.BossHp) : "",
                r.BossTtk >= 0f ? F(r.BossTtk, 1) : "", r.BossSpawnTime >= 0f ? (r.BossReachedNexus ? "1" : "0") : "",
                Q(r.Offered), Q(r.Picked), Q(r.MerchantBought), F(p, 3), Q(gameOverReason)
            };
            try { File.AppendAllText(FilePath, string.Join(",", c) + "\n", Encoding.UTF8); }
            catch (System.Exception e) { Debug.LogWarning("RunTelemetry: Zeile nicht geschrieben – " + e.Message); }
        }

        private static bool Stage4Unlocked()
        {
            for (int i = 0; i < 4; i++)
                if (!Progression.IsStage4Locked(i)) return true;
            return false;
        }

        private static string I(int v) => v.ToString(Inv);
        private static string F(float v, int digits = 0) => v.ToString(digits <= 0 ? "0" : "0." + new string('#', digits), Inv);
        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "'") + "\"";
    }
}
