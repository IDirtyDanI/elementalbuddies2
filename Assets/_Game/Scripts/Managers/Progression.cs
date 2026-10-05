using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElementalBuddies
{
    // Meta-Fortschritt: Erfolge (PlayerPrefs, ein Schlüssel pro Erfolg "ach_<id>") und daraus folgende Freischaltungen.
    // Freischaltungen gelten erst ab dem nächsten Spiel: IsUnlocked nutzt einen Snapshot, der beim ersten Abfragen in
    // einer Szene genommen und beim Entladen der Szene verworfen wird (neues Spiel / Neustart = neuer Snapshot).
    // Erfolge werden sofort gespeichert (IsAchieved / IsUnlockedPersistent sehen sie sofort).
    // Hauptmenü: IsUnlockedPersistent / IsChampionUnlocked (persistenter Stand).
    public static class Progression
    {
        public const string AchievementPrefix = "ach_";
        public const string BossKindsKey = "ach_progress_boss_kinds"; // Bitmaske besiegter Boss-Arten (über alle Spiele)
        public const string RetroDoneKey = "ach_retro_done";          // veraltet (frühere rückwirkende Vergabe), wird nur noch gelöscht
        public const string BestWaveKey = "BestWave";                 // Schlüssel aus GameManager (nur lesen!)
        public const string AchBestWaveKey = "ach_progress_best_wave"; // beste Welle in Spielen ohne Cheats (Fortschritt der Wellen-Erfolge)

        // Neuer Erfolg erreicht (bereits gespeichert)
        public static event Action<AchievementDefinition> OnAchievementUnlocked;
        // Erfolge/Fortschritt geändert (auch Reset/Alle freischalten) – für UI-Aktualisierung
        public static event Action OnProgressChanged;

        // DevTools.UnlockAllContent (nur Session, nichts wird gespeichert); wird von DevTools jedes Frame gesetzt
        public static bool SessionUnlockAllOverride;

        private static readonly HashSet<string> _achieved = new HashSet<string>();
        private static bool _loaded;
        private static readonly HashSet<UnlockId> _session = new HashSet<UnlockId>();
        private static bool _hasSnapshot;
        private static bool _snapshotUnlockAll;
        // Laufender Fortschritt in diesem Spiel (vom AchievementManager gemeldet), für GetProgress
        private static readonly Dictionary<string, int> _live = new Dictionary<string, int>();
        private static readonly List<AchievementDefinition> _reqBuffer = new List<AchievementDefinition>();

        public static AchievementDatabaseSO Database => AchievementDatabaseSO.Instance;
        public static IReadOnlyList<AchievementDefinition> All =>
            Database != null ? (IReadOnlyList<AchievementDefinition>)Database.Achievements : Array.Empty<AchievementDefinition>();

        // Reset bei deaktiviertem Domain Reload; Snapshot bei jedem Szenenwechsel verwerfen
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _achieved.Clear();
            _loaded = false;
            InvalidateSnapshot();
            OnAchievementUnlocked = null;
            OnProgressChanged = null;
            SessionUnlockAllOverride = false;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        private static void HandleSceneUnloaded(Scene scene) => InvalidateSnapshot();

        public static void InvalidateSnapshot()
        {
            _session.Clear();
            _hasSnapshot = false;
            _snapshotUnlockAll = false;
            _live.Clear();
        }

        // ---------------- Laden / Speichern ----------------

        public static void Load(bool force = false)
        {
            if (_loaded && !force) return;
            _loaded = true;
            _achieved.Clear();
            var db = Database;
            if (db == null) return;
            foreach (var a in db.Achievements)
                if (a != null && !string.IsNullOrEmpty(a.Id) && PlayerPrefs.GetInt(AchievementPrefix + a.Id, 0) == 1)
                    _achieved.Add(a.Id);
        }

        public static bool IsAchieved(string id)
        {
            Load();
            return !string.IsNullOrEmpty(id) && _achieved.Contains(id);
        }

        public static bool IsAchieved(AchievementDefinition a) => a != null && IsAchieved(a.Id);

        // Erfolg speichern; true, wenn er neu ist (dann feuert OnAchievementUnlocked)
        public static bool Grant(AchievementDefinition a)
        {
            if (a == null || string.IsNullOrEmpty(a.Id) || IsAchieved(a.Id)) return false;
            _achieved.Add(a.Id);
            PlayerPrefs.SetInt(AchievementPrefix + a.Id, 1);
            PlayerPrefs.Save();
            Debug.Log($"Erfolge: „{a.Title}“ erreicht.");
            OnAchievementUnlocked?.Invoke(a);
            OnProgressChanged?.Invoke();
            return true;
        }

        public static bool Grant(string id) => Grant(Database != null ? Database.Find(id) : null);

        // Alle Erfolge löschen (BestWave des GameManagers bleibt). Keine rückwirkende Vergabe aus der Bestwelle –
        // Champions & Co. müssen im Spiel erspielt werden.
        public static void ResetAll()
        {
            var db = Database;
            if (db != null)
                foreach (var a in db.Achievements)
                    if (a != null && !string.IsNullOrEmpty(a.Id)) PlayerPrefs.DeleteKey(AchievementPrefix + a.Id);
            PlayerPrefs.DeleteKey(BossKindsKey);
            PlayerPrefs.DeleteKey(AchBestWaveKey);
            PlayerPrefs.DeleteKey(RetroDoneKey);
            PlayerPrefs.Save();
            _achieved.Clear();
            _loaded = false;
            Load();
            OnProgressChanged?.Invoke();
        }

        // Alle Erfolge erreicht setzen (ohne Meldungen); gilt (wie immer) ab dem nächsten Spiel
        public static void UnlockAllPersistent()
        {
            Load();
            var db = Database;
            if (db == null) return;
            foreach (var a in db.Achievements)
                if (a != null && !string.IsNullOrEmpty(a.Id))
                {
                    _achieved.Add(a.Id);
                    PlayerPrefs.SetInt(AchievementPrefix + a.Id, 1);
                }
            PlayerPrefs.SetInt(BossKindsKey, (1 << Mathf.Max(0, db.BossKinds.Count)) - 1);
            PlayerPrefs.Save();
            OnProgressChanged?.Invoke();
        }

        // ---------------- Beste Welle (Fortschritt Wellen-Erfolge) ----------------

        public static int BestWaveReached => PlayerPrefs.GetInt(AchBestWaveKey, 0);

        // Erreichte Welle (ohne Cheats) merken
        public static void RecordWave(int wave)
        {
            if (wave <= BestWaveReached) return;
            PlayerPrefs.SetInt(AchBestWaveKey, wave);
            PlayerPrefs.Save();
            OnProgressChanged?.Invoke();
        }

        // ---------------- Boss-Arten (kumulativ) ----------------

        public static int BossKindsMask => PlayerPrefs.GetInt(BossKindsKey, 0);
        public static int BossKindsDefeated => BitCount(BossKindsMask);
        public static bool IsBossKindDefeated(int index) => index >= 0 && (BossKindsMask & (1 << index)) != 0;

        // Boss-Art als besiegt speichern; true, wenn neu
        public static bool RecordBossKind(int index)
        {
            if (index < 0 || index > 30) return false;
            int mask = BossKindsMask;
            if ((mask & (1 << index)) != 0) return false;
            PlayerPrefs.SetInt(BossKindsKey, mask | (1 << index));
            PlayerPrefs.Save();
            OnProgressChanged?.Invoke();
            return true;
        }

        private static int BitCount(int m)
        {
            int n = 0;
            for (; m != 0; m &= m - 1) n++;
            return n;
        }

        // ---------------- Freischaltungen ----------------

        // Persistenter Stand: alle Erfolge mit dieser Freischaltung erreicht (Hauptmenü, Erfolge-Seite)
        public static bool IsUnlockedPersistent(UnlockId id)
        {
            if (id == UnlockId.None) return true;
            var db = Database;
            if (db == null) return true; // ohne Datenbank nichts sperren
            Load();
            foreach (var a in db.GetRequirements(id, _reqBuffer))
                if (!_achieved.Contains(a.Id)) return false;
            return true;
        }

        // Stand dieses Spiels (Snapshot beim Szenenstart) – fürs Gating im Spiel
        public static bool IsUnlocked(UnlockId id)
        {
            if (id == UnlockId.None || SessionUnlockAllOverride) return true;
            EnsureSnapshot();
            return _snapshotUnlockAll || _session.Contains(id);
        }

        public static void EnsureSnapshot()
        {
            if (_hasSnapshot) return;
            TakeSnapshot();
        }

        // Snapshot der Freischaltungen neu nehmen (passiert automatisch beim Szenenstart)
        public static void TakeSnapshot()
        {
            Load();
            _session.Clear();
            foreach (UnlockId id in Enum.GetValues(typeof(UnlockId)))
                if (IsUnlockedPersistent(id)) _session.Add(id);
            _snapshotUnlockAll = false;
#if UNITY_EDITOR
            // DevTools.UnlockAllContent schon vor DevTools.Update berücksichtigen (Champion-Wahl in Awake)
            var dev = UnityEngine.Object.FindFirstObjectByType<DevTools>();
            _snapshotUnlockAll = dev != null && dev.Enabled && dev.UnlockAllContent;
#endif
            _hasSnapshot = true;
        }

        // Im laufenden Spiel erreicht, wirkt aber erst im nächsten
        public static bool IsPendingNextGame(UnlockId id) => IsUnlockedPersistent(id) && !IsUnlocked(id);

        public static string GetUnlockName(UnlockId id) => Database != null ? Database.GetUnlockName(id) : id.ToString();

        // Noch fehlende Erfolge, z. B. "Erfolg „Zwillingskraft“: 2 Buddies gleichzeitig auf Stufe 2"
        public static string RequirementText(UnlockId id)
        {
            var db = Database;
            if (db == null || id == UnlockId.None) return "";
            Load();
            var parts = new List<string>();
            foreach (var a in db.GetRequirements(id, _reqBuffer))
                if (!_achieved.Contains(a.Id)) parts.Add($"Erfolg „{a.Title}“: {AchievementDatabaseSO.GetConditionText(a)}");
            if (parts.Count == 0) return IsUnlocked(id) ? "" : "Freigeschaltet ab dem nächsten Spiel";
            return string.Join(" · ", parts);
        }

        // Kurzform der fehlenden Bedingungen (persistenter Stand), z. B. "Erreiche Welle 6" – für Karten/Knöpfe im Hauptmenü
        public static string GoalText(UnlockId id)
        {
            var db = Database;
            if (db == null || id == UnlockId.None) return "";
            Load();
            var parts = new List<string>();
            foreach (var a in db.GetRequirements(id, _reqBuffer))
                if (!_achieved.Contains(a.Id)) parts.Add(AchievementDatabaseSO.GetGoalText(a));
            return string.Join(" · ", parts);
        }

        // "2er-Fusionen gesperrt – Erfolg „Zwillingskraft“: …"
        public static string LockText(UnlockId id) => $"{GetUnlockName(id)} gesperrt – {RequirementText(id)}";

        // ---------------- Bequeme Abfragen ----------------

        public static UnlockId ChampionUnlock(ChampionClass c) =>
            c == ChampionClass.Knight ? UnlockId.ChampionKnight : c == ChampionClass.Archer ? UnlockId.ChampionArcher : UnlockId.None;

        // Hauptmenü: persistenter Stand; sessionSnapshot = true für die Spielszene
        public static bool IsChampionUnlocked(ChampionClass c, bool sessionSnapshot = false) =>
            sessionSnapshot ? IsUnlocked(ChampionUnlock(c)) : IsUnlockedPersistent(ChampionUnlock(c));

        public static UnlockId Stage4Unlock(int elementIndex)
        {
            switch (elementIndex)
            {
                case 0: return UnlockId.Stage4Fire;
                case 1: return UnlockId.Stage4Ice;
                case 2: return UnlockId.Stage4Earth;
                case 3: return UnlockId.Stage4Light;
            }
            return UnlockId.None;
        }

        // Spielszene (Snapshot)
        public static bool IsStage4Locked(int elementIndex) => !IsUnlocked(Stage4Unlock(elementIndex));

        // ---------------- Fortschritt für die UI ----------------

        // Laufender Wert in diesem Spiel (AchievementManager); es zählt der höchste gemeldete Wert
        public static void ReportLive(string id, int value)
        {
            if (string.IsNullOrEmpty(id)) return;
            int old;
            if (!_live.TryGetValue(id, out old) || value > old) _live[id] = value;
        }

        // Aktueller Wert / Ziel. Erreicht → current = target. Wellen: beste Welle ohne Cheats (ach_progress_best_wave) bzw. laufende Welle; Boss-Arten: x/3 über alle Spiele;
        // übrige (pro Spiel): bester Wert im laufenden Spiel (im Hauptmenü 0).
        public static void GetProgress(AchievementDefinition a, out int current, out int target)
        {
            current = 0;
            target = a != null ? Mathf.Max(1, a.Threshold) : 1;
            if (a == null) return;
            if (IsAchieved(a.Id))
            {
                current = target;
                return;
            }
            int live;
            _live.TryGetValue(a.Id, out live);
            switch (a.Condition)
            {
                case AchievementCondition.ReachWave:
                    current = Mathf.Max(BestWaveReached, live);
                    break;
                case AchievementCondition.DefeatBossKinds:
                    current = BossKindsDefeated;
                    break;
                default:
                    current = live;
                    break;
            }
            current = Mathf.Clamp(current, 0, target);
        }

        public static float GetProgress01(AchievementDefinition a)
        {
            int c, t;
            GetProgress(a, out c, out t);
            return t > 0 ? Mathf.Clamp01(c / (float)t) : 0f;
        }

        public static int AchievedCount
        {
            get
            {
                int n = 0;
                foreach (var a in All) if (a != null && IsAchieved(a.Id)) n++;
                return n;
            }
        }

        // Kurzer Statusbericht (Editor-Menü "Stand ausgeben")
        public static string DescribeState()
        {
            Load(true);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Erfolge: {AchievedCount}/{All.Count} · Bestwelle {PlayerPrefs.GetInt(BestWaveKey, 0)} (ohne Cheats {BestWaveReached}) · Boss-Arten {BossKindsDefeated}/{(Database != null ? Database.BossKinds.Count : 0)}");
            foreach (var a in All)
            {
                if (a == null) continue;
                int c, t;
                GetProgress(a, out c, out t);
                sb.AppendLine($"  [{(IsAchieved(a.Id) ? "x" : " ")}] {a.Id} „{a.Title}“ ({c}/{t}) → {(a.IsTrophy ? "Trophäe" : GetUnlockName(a.Unlock))}");
            }
            foreach (UnlockId id in Enum.GetValues(typeof(UnlockId)))
                if (id != UnlockId.None)
                    sb.AppendLine($"  {id}: {(IsUnlockedPersistent(id) ? "frei" : "gesperrt")}");
            return sb.ToString();
        }
    }
}
