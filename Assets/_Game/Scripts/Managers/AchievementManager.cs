using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Prüft im Spiel die Bedingungen der Erfolge (AchievementDatabaseSO) und speichert neue Erfolge über Progression.
    // Erfolge gelten für die gespielte Stufe (und leichtere); Freischaltungen wirken erst ab dem nächsten Spiel
    // (Snapshot beim Szenenstart). Liegt auf dem Managers-Objekt.
    // Quellen: Wellenstart (WaveManager), Buddy-Scan (ElementalBuddy.Active, periodisch), Fusionen (FusionManager.OnFused),
    // Boss-Kills (EnemyBrain.OnEnemyKilled), Splitter-Einnahmen (EconomyManager.OnShardsEarned).
    // Cheat-Schutz: Bei aktiven DevTools-Cheats werden keine Erfolge vergeben (außer DevTools.AllowAchievementsWithCheats).
    public class AchievementManager : MonoBehaviour
    {
        public static AchievementManager Instance { get; private set; }

        [Tooltip("Abstand der Buddy-Prüfung (s).")]
        public float ScanInterval = 0.5f;
        [Tooltip("Meldung im Spiel (Popup bzw. Toast + Sound) bei neuem Erfolg.")]
        public bool ShowToast = true;
        [Tooltip("Erfolgs-Popup (Resources/AchievementPopup) statt Toast; fehlt das Prefab, wird der Toast genutzt.")]
        public bool UsePopup = true;
        public SfxId Sound = SfxId.ShrineCaptured;

        // Neuer Erfolg in diesem Spiel (nach dem Speichern), z. B. für die Liste im Game-Over-Panel
        public event System.Action<AchievementDefinition> OnAchievementEarned;
        // Alle in diesem Spiel erreichten Erfolge (Reihenfolge des Erreichens)
        public readonly List<AchievementDefinition> EarnedThisGame = new List<AchievementDefinition>();

        // Zähler dieses Spiels
        public float ShardsEarnedThisGame { get; private set; }
        public int BossesKilledThisGame { get; private set; }
        public int SupersBuiltThisGame { get; private set; }

        private DevTools _dev;
        private WaveManager _waveManager;
        private EconomyManager _economy;
        private FusionManager _fusion;
        private float _nextScan;
        private bool _cheatLogged;
        private readonly List<AchievementDefinition> _pending = new List<AchievementDefinition>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Progression.EnsureSnapshot(); // Freischaltungen dieses Spiels festhalten, bevor etwas erreicht werden kann
            _dev = FindFirstObjectByType<DevTools>();
            if (AchievementDatabaseSO.Instance == null)
                Debug.LogWarning("AchievementManager: Resources/AchievementDatabase fehlt (Menü BuddyTD → Erfolge → Datenbank anlegen-aktualisieren).");
        }

        void OnEnable()
        {
            EnemyBrain.OnEnemyKilled += HandleEnemyKilled;
            Progression.OnAchievementUnlocked += HandleAchievementUnlocked;
        }

        void OnDisable()
        {
            EnemyBrain.OnEnemyKilled -= HandleEnemyKilled;
            Progression.OnAchievementUnlocked -= HandleAchievementUnlocked;
        }

        void Start()
        {
            _waveManager = WaveManager.Instance;
            if (_waveManager != null) _waveManager.OnWaveStart += HandleWaveStart;
            _economy = EconomyManager.Instance;
            if (_economy != null) _economy.OnShardsEarned += HandleShardsEarned;
            _fusion = FusionManager.Instance;
            if (_fusion != null) _fusion.OnFused += HandleFused;
            if (ShowToast && UsePopup) AchievementPopupUI.EnsureInstance(); // Popup schon beim Start bereitlegen
        }

        void OnDestroy()
        {
            if (_waveManager != null) _waveManager.OnWaveStart -= HandleWaveStart;
            if (_economy != null) _economy.OnShardsEarned -= HandleShardsEarned;
            if (_fusion != null) _fusion.OnFused -= HandleFused;
            if (Instance == this) Instance = null;
        }

        // Dürfen gerade Erfolge vergeben werden? (Cheat-Schutz)
        public bool AchievementsAllowed
        {
            get
            {
                // Mehrspieler: Cheats nur beim Host – Clients übernehmen dessen Cheat-Status
                bool cheats = Net.IsServer ? _dev != null && _dev.CheatsActive && !_dev.AllowAchievementsWithCheats
                                           : NetGame.HostCheatsActive;
                if (!cheats) return true;
                if (!_cheatLogged)
                {
                    _cheatLogged = true;
                    Debug.Log("AchievementManager: DevTools-Cheats aktiv – in diesem Spiel werden keine Erfolge vergeben (DevTools.AllowAchievementsWithCheats zum Testen).");
                }
                return false;
            }
        }

        void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + Mathf.Max(0.1f, ScanInterval);
            ScanBuddies();
        }

        // ---------------- Bedingungen ----------------

        private void ScanBuddies()
        {
            var db = AchievementDatabaseSO.Instance;
            if (db == null) return;
            var active = ElementalBuddy.Active;
            foreach (var a in db.Achievements)
            {
                if (a == null) continue;
                int value;
                switch (a.Condition)
                {
                    case AchievementCondition.BaseBuddiesAtLevel:
                        value = 0;
                        foreach (var b in active)
                            if (b != null && !b.IsFusion && !b.IsDead && b.Level >= a.Level && (a.Element < 0 || b.ElementIndex == a.Element)) value++;
                        break;
                    case AchievementCondition.FusionBuddiesOnField:
                        value = 0;
                        foreach (var b in active)
                            if (b != null && b.IsFusion && !b.IsDead) value++;
                        break;
                    case AchievementCondition.ReachWave:
                        // Wellen-Erfolge laufen über HandleWaveStart; hier nur der Fortschritt (laufende Welle)
                        if (_waveManager != null && _waveManager.IsWaveActive && AchievementsAllowed) Progression.ReportLive(a.Id, _waveManager.CurrentWaveIndex + 1);
                        continue;
                    default:
                        continue;
                }
                Check(a, value);
            }
        }

        private void HandleWaveStart()
        {
            if (_waveManager == null) return;
            int wave = _waveManager.CurrentWaveIndex + 1;
            if (AchievementsAllowed) Progression.RecordWave(wave);
            CheckAll(AchievementCondition.ReachWave, wave);
        }

        private void HandleShardsEarned(float amount)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            ShardsEarnedThisGame += amount;
            CheckAll(AchievementCondition.ShardsCollected, Mathf.FloorToInt(ShardsEarnedThisGame));
        }

        private void HandleFused(FusionBuddy fusion)
        {
            if (fusion == null || !fusion.IsSuper) return;
            SupersBuiltThisGame++;
            CheckAll(AchievementCondition.BuildSuper, SupersBuiltThisGame);
        }

        private void HandleEnemyKilled(EnemyBrain enemy)
        {
            if (enemy == null || !enemy.IsBoss) return;
            BossesKilledThisGame++;
            CheckAll(AchievementCondition.DefeatBoss, BossesKilledThisGame);

            var db = AchievementDatabaseSO.Instance;
            if (db == null) return;
            int kind = db.GetBossKindIndex(enemy.Config);
            if (kind < 0)
            {
                Debug.LogWarning($"AchievementManager: Boss-Art von '{(enemy.Config != null ? enemy.Config.name : enemy.name)}' unbekannt (AchievementDatabase.BossKinds).");
                return;
            }
            // Boss-Arten sind kumulativer Fortschritt – nur ohne Cheats speichern
            if (AchievementsAllowed && Progression.RecordBossKind(kind))
                Debug.Log($"Erfolge: Boss-Art „{db.GetBossKindName(kind)}“ besiegt ({Progression.BossKindsDefeated}/{db.BossKinds.Count}).");
            CheckAll(AchievementCondition.DefeatBossKinds, Progression.BossKindsDefeated);
        }

        private void CheckAll(AchievementCondition condition, int value)
        {
            var db = AchievementDatabaseSO.Instance;
            if (db == null) return;
            _pending.Clear();
            foreach (var a in db.Achievements)
                if (a != null && a.Condition == condition) _pending.Add(a);
            foreach (var a in _pending) Check(a, value);
        }

        private void Check(AchievementDefinition a, int value)
        {
            if (!AchievementsAllowed) return;
            Progression.ReportLive(a.Id, value);
            // Neu ist ein Erfolg auch, wenn er bisher nur auf einer leichteren Stufe erreicht wurde
            if (value < Mathf.Max(1, a.Threshold) || Progression.IsAchieved(a.Id)) return;
            Progression.Grant(a);
        }

        // ---------------- Meldung ----------------

        private void HandleAchievementUnlocked(AchievementDefinition a)
        {
            if (a != null && !EarnedThisGame.Contains(a)) EarnedThisGame.Add(a);
            OnAchievementEarned?.Invoke(a);
            if (!ShowToast || a == null) return;
            if (!UsePopup || !AchievementPopupUI.Show(a)) ToastUI.Show(ToastText(a), a.Icon);
            GameAudio.Play(GameAudio.Has(Sound) ? Sound : SfxId.Fusion, PlayerAbilities.Instance != null ? PlayerAbilities.Instance.transform.position : Vector3.zero);
        }

        // "Erfolg (Normal): Zwillingskraft – 2er-Fusionen ab dem nächsten Spiel auf Normal und Leicht"
        // (Trophäe: nur "Erfolg (Normal): <Titel>"); Stufe = höchster erreichter Rang des Erfolgs
        public static string ToastText(AchievementDefinition a)
        {
            if (a == null) return "";
            int rank = Mathf.Max(1, Progression.AchievedRank(a));
            string head = $"Erfolg ({Progression.RankName(rank)}): {a.Title}";
            if (a.IsTrophy) return head;
            string unlock = Progression.GetUnlockName(a.Unlock);
            if (Progression.IsUnlockedPersistent(a.Unlock, rank)) return $"{head} – {unlock} ab dem nächsten Spiel {Progression.RankScopeText(rank)}";
            // Freischaltung braucht noch weitere Erfolge (z. B. Super-Elementare)
            return $"{head} – Teil von „{unlock}“";
        }
    }
}
