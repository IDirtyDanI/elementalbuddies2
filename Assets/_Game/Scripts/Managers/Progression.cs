using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElementalBuddies
{
    // Meta-Fortschritt: Erfolge (PlayerPrefs, ein Schlüssel pro Erfolg "ach_<id>") und daraus folgende Freischaltungen.
    // Erfolge gelten pro Schwierigkeitsstufe: gespeichert wird je Erfolg der höchste Rang, auf dem er erreicht wurde
    // (1 = Leicht, 2 = Normal, 3 = Schwer). Ein Erfolg zählt für eine Stufe, wenn er auf ihr oder einer höheren erreicht
    // wurde (Normal-Erfolg zählt für Normal und Leicht, Schwer-Erfolg für alle). Freischaltungen gelten damit ebenfalls
    // pro Stufe. Ohne Rang-Angabe (rank <= 0) gilt die gewählte Stufe (GameSession.Difficulty).
    // Freischaltungen gelten erst ab dem nächsten Spiel: IsUnlocked nutzt einen Snapshot (mit der gespielten Stufe), der
    // beim ersten Abfragen in einer Szene genommen und beim Entladen der Szene verworfen wird (neues Spiel / Neustart =
    // neuer Snapshot). Erfolge werden sofort gespeichert (IsAchieved / IsUnlockedPersistent sehen sie sofort).
    // Hauptmenü: IsUnlockedPersistent / IsChampionUnlocked (persistenter Stand der im Menü gewählten Stufe).
    // Speicherformat-Version 2 (ach_version): ältere Stände (ach_<id> = 1 ohne Stufen) werden einmalig als Normal übernommen.
    public static class Progression
    {
        public const string AchievementPrefix = "ach_";
        public const string VersionKey = "ach_version";                // Speicherformat (2 = Erfolge mit Stufen-Rang)
        public const int CurrentVersion = 2;
        public const string BossKindsKey = "ach_progress_boss_kinds"; // + "_<Rang>": Bitmaske besiegter Boss-Arten (über alle Spiele dieser Stufe)
        public const string RetroDoneKey = "ach_retro_done";          // veraltet (frühere rückwirkende Vergabe), wird nur noch gelöscht
        public const string BestWaveKey = "BestWave";                 // Schlüssel aus GameManager (nur lesen!)
        public const string AchBestWaveKey = "ach_progress_best_wave"; // + "_<Rang>": beste Welle ohne Cheats auf dieser Stufe (Fortschritt der Wellen-Erfolge)

        // Stufen-Ränge (Leicht < Normal < Schwer)
        public const int RankEasy = 1;
        public const int RankNormal = 2;
        public const int RankHard = 3;
        public const int MaxRank = RankHard;

        // Neuer Erfolg erreicht bzw. auf höherer Stufe erreicht (bereits gespeichert; Stufe: AchievedRank)
        public static event Action<AchievementDefinition> OnAchievementUnlocked;
        // Erfolge/Fortschritt geändert (auch Reset/Alle freischalten) – für UI-Aktualisierung
        public static event Action OnProgressChanged;

        // DevTools.UnlockAllContent (nur Session, nichts wird gespeichert); wird von DevTools jedes Frame gesetzt
        public static bool SessionUnlockAllOverride;

        // Id → höchster erreichter Rang (nur erreichte Erfolge)
        private static readonly Dictionary<string, int> _achieved = new Dictionary<string, int>();
        private static bool _loaded;
        private static readonly HashSet<UnlockId> _session = new HashSet<UnlockId>();
        private static bool _hasSnapshot;
        private static bool _snapshotUnlockAll;
        private static int _snapshotRank = RankNormal;
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

        // ---------------- Stufen ----------------

        // Rang einer Stufe: aus der Id (leicht/normal/schwer), sonst aus der Order (0 → 1 …); null → Normal
        public static int Rank(DifficultySO d)
        {
            if (d == null) return RankNormal;
            if (string.Equals(d.Id, "leicht", StringComparison.OrdinalIgnoreCase)) return RankEasy;
            if (string.Equals(d.Id, "normal", StringComparison.OrdinalIgnoreCase)) return RankNormal;
            if (string.Equals(d.Id, "schwer", StringComparison.OrdinalIgnoreCase)) return RankHard;
            return Mathf.Clamp(d.Order + 1, RankEasy, MaxRank);
        }

        // Rang der gewählten Stufe (Hauptmenü: Auswahl, Spiel: gespielte Stufe)
        public static int CurrentRank => Rank(GameSession.Difficulty);

        // rank <= 0 → gewählte Stufe
        private static int R(int rank) => rank <= 0 ? CurrentRank : Mathf.Clamp(rank, RankEasy, MaxRank);

        // Anzeigename einer Stufe ("Leicht", "Normal", "Schwer")
        public static string RankName(int rank)
        {
            rank = R(rank);
            foreach (var d in DifficultySO.All)
                if (d != null && Rank(d) == rank) return d.DisplayName;
            return rank == RankEasy ? "Leicht" : rank == RankHard ? "Schwer" : "Normal";
        }

        // Für welche Stufen ein auf diesem Rang erreichter Erfolg zählt: "auf allen Stufen" / "auf Normal und Leicht" / "auf Leicht"
        public static string RankScopeText(int rank)
        {
            rank = R(rank);
            if (rank >= MaxRank) return "auf allen Stufen";
            if (rank <= RankEasy) return "auf " + RankName(RankEasy);
            var names = new List<string>();
            for (int r = rank; r >= RankEasy; r--) names.Add(RankName(r));
            return "auf " + string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " und " + names[names.Count - 1];
        }

        // Bedingung für Sperrtexte: "auf Schwer" (höchste Stufe) bzw. "auf Normal oder höher"
        public static string RankRequirementText(int rank)
        {
            rank = R(rank);
            return rank >= MaxRank ? "auf " + RankName(rank) : "auf " + RankName(rank) + " oder höher";
        }

        // ---------------- Laden / Speichern ----------------

        public static void Load(bool force = false)
        {
            if (_loaded && !force) return;
            _loaded = true;
            _achieved.Clear();
            var db = Database;
            if (db == null) return;
            Migrate(db);
            foreach (var a in db.Achievements)
            {
                if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                int rank = Mathf.Min(PlayerPrefs.GetInt(AchievementPrefix + a.Id, 0), MaxRank);
                if (rank > 0) _achieved[a.Id] = rank;
            }
        }

        // Einmalige Übernahme alter Stände (vor den Stufen): Erfolg = 1 → Normal, Fortschritts-Schlüssel → Normal
        private static void Migrate(AchievementDatabaseSO db)
        {
            if (PlayerPrefs.GetInt(VersionKey, 1) >= CurrentVersion) return;
            int migrated = 0;
            foreach (var a in db.Achievements)
            {
                if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                string key = AchievementPrefix + a.Id;
                if (PlayerPrefs.GetInt(key, 0) != 1) continue;
                PlayerPrefs.SetInt(key, RankNormal);
                migrated++;
            }
            if (PlayerPrefs.HasKey(BossKindsKey))
            {
                string key = BossKindsKey + "_" + RankNormal;
                PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) | PlayerPrefs.GetInt(BossKindsKey, 0));
                PlayerPrefs.DeleteKey(BossKindsKey);
            }
            if (PlayerPrefs.HasKey(AchBestWaveKey))
            {
                string key = AchBestWaveKey + "_" + RankNormal;
                PlayerPrefs.SetInt(key, Mathf.Max(PlayerPrefs.GetInt(key, 0), PlayerPrefs.GetInt(AchBestWaveKey, 0)));
                PlayerPrefs.DeleteKey(AchBestWaveKey);
            }
            PlayerPrefs.SetInt(VersionKey, CurrentVersion);
            PlayerPrefs.Save();
            if (migrated > 0) Debug.Log($"Erfolge: {migrated} Erfolg(e) aus dem alten Stand als „{RankName(RankNormal)}“ übernommen.");
        }

        // Höchster Rang, auf dem der Erfolg erreicht wurde (0 = nicht erreicht)
        public static int AchievedRank(string id)
        {
            Load();
            int rank;
            return !string.IsNullOrEmpty(id) && _achieved.TryGetValue(id, out rank) ? rank : 0;
        }

        public static int AchievedRank(AchievementDefinition a) => a != null ? AchievedRank(a.Id) : 0;

        // Zählt der Erfolg für diese Stufe? (auf ihr oder einer höheren erreicht; rank <= 0 → gewählte Stufe)
        public static bool IsAchieved(string id, int rank = 0) => AchievedRank(id) >= R(rank);

        public static bool IsAchieved(AchievementDefinition a, int rank = 0) => a != null && IsAchieved(a.Id, rank);

        // Erfolg auf dieser Stufe speichern (rank <= 0 → gewählte Stufe); true, wenn er neu ist bzw. auf einer höheren
        // Stufe als bisher erreicht wurde (dann feuert OnAchievementUnlocked)
        public static bool Grant(AchievementDefinition a, int rank = 0)
        {
            if (a == null || string.IsNullOrEmpty(a.Id)) return false;
            rank = R(rank);
            if (AchievedRank(a.Id) >= rank) return false;
            _achieved[a.Id] = rank;
            PlayerPrefs.SetInt(AchievementPrefix + a.Id, rank);
            PlayerPrefs.Save();
            Debug.Log($"Erfolge: „{a.Title}“ auf {RankName(rank)} erreicht.");
            OnAchievementUnlocked?.Invoke(a);
            OnProgressChanged?.Invoke();
            return true;
        }

        public static bool Grant(string id, int rank = 0) => Grant(Database != null ? Database.Find(id) : null, rank);

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
            for (int r = RankEasy; r <= MaxRank; r++)
            {
                PlayerPrefs.DeleteKey(BossKindsKey + "_" + r);
                PlayerPrefs.DeleteKey(AchBestWaveKey + "_" + r);
            }
            PlayerPrefs.DeleteKey(RetroDoneKey);
            PlayerPrefs.SetInt(VersionKey, CurrentVersion); // leerer Stand ist schon im neuen Format
            PlayerPrefs.Save();
            _achieved.Clear();
            _loaded = false;
            Load();
            OnProgressChanged?.Invoke();
        }

        // Alle Erfolge auf dieser Stufe erreicht setzen (ohne Meldungen; Schwer = alle Stufen); höhere Ränge bleiben.
        // Gilt (wie immer) ab dem nächsten Spiel.
        public static void UnlockAllPersistent(int rank = MaxRank)
        {
            Load();
            var db = Database;
            if (db == null) return;
            rank = R(rank);
            foreach (var a in db.Achievements)
                if (a != null && !string.IsNullOrEmpty(a.Id) && AchievedRank(a.Id) < rank)
                {
                    _achieved[a.Id] = rank;
                    PlayerPrefs.SetInt(AchievementPrefix + a.Id, rank);
                }
            string bossKey = BossKindsKey + "_" + rank;
            PlayerPrefs.SetInt(bossKey, PlayerPrefs.GetInt(bossKey, 0) | ((1 << Mathf.Max(0, db.BossKinds.Count)) - 1));
            PlayerPrefs.Save();
            OnProgressChanged?.Invoke();
        }

        // ---------------- Beste Welle (Fortschritt Wellen-Erfolge) ----------------

        // Beste Welle ohne Cheats, die für diese Stufe zählt (Maximum über diese und höhere Stufen)
        public static int BestWaveReachedFor(int rank)
        {
            int best = 0;
            for (int r = R(rank); r <= MaxRank; r++) best = Mathf.Max(best, PlayerPrefs.GetInt(AchBestWaveKey + "_" + r, 0));
            return best;
        }

        public static int BestWaveReached => BestWaveReachedFor(0);

        // Erreichte Welle (ohne Cheats) auf der gespielten Stufe merken
        public static void RecordWave(int wave)
        {
            Load(); // Migration alter Schlüssel vor dem ersten Schreiben
            string key = AchBestWaveKey + "_" + CurrentRank;
            if (wave <= PlayerPrefs.GetInt(key, 0)) return;
            PlayerPrefs.SetInt(key, wave);
            PlayerPrefs.Save();
            OnProgressChanged?.Invoke();
        }

        // ---------------- Boss-Arten (kumulativ, pro Stufe) ----------------

        // Besiegte Boss-Arten, die für diese Stufe zählen (Vereinigung über diese und höhere Stufen)
        public static int BossKindsMaskFor(int rank)
        {
            int mask = 0;
            for (int r = R(rank); r <= MaxRank; r++) mask |= PlayerPrefs.GetInt(BossKindsKey + "_" + r, 0);
            return mask;
        }

        public static int BossKindsMask => BossKindsMaskFor(0);
        public static int BossKindsDefeated => BitCount(BossKindsMask);
        public static int BossKindsDefeatedFor(int rank) => BitCount(BossKindsMaskFor(rank));
        public static bool IsBossKindDefeated(int index) => index >= 0 && (BossKindsMask & (1 << index)) != 0;

        // Boss-Art auf der gespielten Stufe als besiegt speichern; true, wenn sie für diese Stufe neu ist
        public static bool RecordBossKind(int index)
        {
            if (index < 0 || index > 30) return false;
            Load();
            if ((BossKindsMask & (1 << index)) != 0) return false;
            string key = BossKindsKey + "_" + CurrentRank;
            PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) | (1 << index));
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

        // Persistenter Stand der Stufe (rank <= 0 → gewählte Stufe): alle Erfolge mit dieser Freischaltung erreicht
        // (Hauptmenü, Erfolge-Seite)
        public static bool IsUnlockedPersistent(UnlockId id, int rank = 0)
        {
            if (id == UnlockId.None) return true;
            var db = Database;
            if (db == null) return true; // ohne Datenbank nichts sperren
            Load();
            rank = R(rank);
            foreach (var a in db.GetRequirements(id, _reqBuffer))
                if (AchievedRank(a.Id) < rank) return false;
            return true;
        }

        // Stand dieses Spiels (Snapshot beim Szenenstart, gespielte Stufe) – fürs Gating im Spiel
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

        // Stufe des Snapshots (gespielte Stufe)
        public static int SnapshotRank
        {
            get
            {
                EnsureSnapshot();
                return _snapshotRank;
            }
        }

        // Snapshot der Freischaltungen für die gespielte Stufe neu nehmen (passiert automatisch beim Szenenstart)
        public static void TakeSnapshot()
        {
            Load();
            _snapshotRank = CurrentRank;
            _session.Clear();
            foreach (UnlockId id in Enum.GetValues(typeof(UnlockId)))
                if (IsUnlockedPersistent(id, _snapshotRank)) _session.Add(id);
            _snapshotUnlockAll = false;
#if UNITY_EDITOR
            // DevTools.UnlockAllContent schon vor DevTools.Update berücksichtigen (Champion-Wahl in Awake)
            var dev = UnityEngine.Object.FindFirstObjectByType<DevTools>();
            _snapshotUnlockAll = dev != null && dev.Enabled && dev.UnlockAllContent;
#endif
            _hasSnapshot = true;
        }

        // Im laufenden Spiel erreicht, wirkt aber erst im nächsten
        public static bool IsPendingNextGame(UnlockId id) => IsUnlockedPersistent(id, SnapshotRank) && !IsUnlocked(id);

        public static string GetUnlockName(UnlockId id) => Database != null ? Database.GetUnlockName(id) : id.ToString();

        // Noch fehlende Erfolge der Stufe (rank <= 0 → gewählte Stufe), z. B.
        // "Erfolg „Zwillingskraft“ auf Normal oder höher: 2 Buddies gleichzeitig auf Stufe 2"
        public static string RequirementText(UnlockId id, int rank = 0)
        {
            var db = Database;
            if (db == null || id == UnlockId.None) return "";
            Load();
            rank = R(rank);
            var parts = new List<string>();
            foreach (var a in db.GetRequirements(id, _reqBuffer))
                if (AchievedRank(a.Id) < rank) parts.Add($"Erfolg „{a.Title}“ {RankRequirementText(rank)}: {AchievementDatabaseSO.GetConditionText(a)}");
            if (parts.Count == 0) return IsUnlocked(id) ? "" : "Freigeschaltet ab dem nächsten Spiel";
            return string.Join(" · ", parts);
        }

        // Kurzform der fehlenden Bedingungen (persistenter Stand der Stufe), z. B. "Erreiche Welle 6 auf Schwer" –
        // für Karten/Knöpfe im Hauptmenü; withRank = false lässt " auf <Stufe>" weg (knapper Platz)
        public static string GoalText(UnlockId id, int rank = 0, bool withRank = true)
        {
            var db = Database;
            if (db == null || id == UnlockId.None) return "";
            Load();
            rank = R(rank);
            var parts = new List<string>();
            foreach (var a in db.GetRequirements(id, _reqBuffer))
                if (AchievedRank(a.Id) < rank) parts.Add(AchievementDatabaseSO.GetGoalText(a));
            if (parts.Count == 0) return "";
            return string.Join(" · ", parts) + (withRank ? " auf " + RankName(rank) : "");
        }

        // "2er-Fusionen gesperrt – Erfolg „Zwillingskraft“ auf Normal oder höher: …" (gespielte Stufe)
        public static string LockText(UnlockId id) => $"{GetUnlockName(id)} gesperrt – {RequirementText(id, SnapshotRank)}";

        // ---------------- Bequeme Abfragen ----------------

        public static UnlockId ChampionUnlock(ChampionClass c) =>
            c == ChampionClass.Knight ? UnlockId.ChampionKnight : c == ChampionClass.Archer ? UnlockId.ChampionArcher : UnlockId.None;

        // Hauptmenü: persistenter Stand der Stufe (rank <= 0 → gewählte Stufe); sessionSnapshot = true für die Spielszene
        public static bool IsChampionUnlocked(ChampionClass c, bool sessionSnapshot = false, int rank = 0) =>
            sessionSnapshot ? IsUnlocked(ChampionUnlock(c)) : IsUnlockedPersistent(ChampionUnlock(c), rank);

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

        // Aktueller Wert / Ziel für die Stufe (rank <= 0 → gewählte Stufe). Erreicht → current = target.
        // Wellen: beste Welle ohne Cheats dieser Stufe (oder höher) bzw. laufende Welle; Boss-Arten: x/3 über alle Spiele
        // dieser Stufe (oder höher); übrige (pro Spiel): bester Wert im laufenden Spiel (im Hauptmenü 0).
        public static void GetProgress(AchievementDefinition a, out int current, out int target, int rank = 0)
        {
            current = 0;
            target = a != null ? Mathf.Max(1, a.Threshold) : 1;
            if (a == null) return;
            rank = R(rank);
            if (IsAchieved(a.Id, rank))
            {
                current = target;
                return;
            }
            // Laufende Werte gehören zur gespielten Stufe
            int live = 0;
            if (rank == CurrentRank) _live.TryGetValue(a.Id, out live);
            switch (a.Condition)
            {
                case AchievementCondition.ReachWave:
                    current = Mathf.Max(BestWaveReachedFor(rank), live);
                    break;
                case AchievementCondition.DefeatBossKinds:
                    current = BossKindsDefeatedFor(rank);
                    break;
                default:
                    current = live;
                    break;
            }
            current = Mathf.Clamp(current, 0, target);
        }

        public static float GetProgress01(AchievementDefinition a, int rank = 0)
        {
            int c, t;
            GetProgress(a, out c, out t, rank);
            return t > 0 ? Mathf.Clamp01(c / (float)t) : 0f;
        }

        // Anzahl der Erfolge, die für die Stufe zählen (rank <= 0 → gewählte Stufe)
        public static int AchievedCountFor(int rank)
        {
            int n = 0;
            foreach (var a in All) if (a != null && IsAchieved(a.Id, rank)) n++;
            return n;
        }

        public static int AchievedCount => AchievedCountFor(0);

        // Kurzer Statusbericht (Editor-Menü "Stand ausgeben"): Rang je Erfolg (L/N/S), Fortschritt und Freischaltungen pro Stufe
        public static string DescribeState()
        {
            Load(true);
            var sb = new System.Text.StringBuilder();
            sb.Append($"Erfolge (Format v{PlayerPrefs.GetInt(VersionKey, 1)}):");
            for (int r = RankEasy; r <= MaxRank; r++) sb.Append($" {RankName(r)} {AchievedCountFor(r)}/{All.Count}");
            sb.AppendLine($" · Bestwelle {PlayerPrefs.GetInt(BestWaveKey, 0)}");
            for (int r = RankEasy; r <= MaxRank; r++)
                sb.AppendLine($"  {RankName(r)}: Welle ohne Cheats {BestWaveReachedFor(r)} · Boss-Arten {BossKindsDefeatedFor(r)}/{(Database != null ? Database.BossKinds.Count : 0)}");
            foreach (var a in All)
            {
                if (a == null) continue;
                int rank = AchievedRank(a.Id);
                string marks = "";
                for (int r = RankEasy; r <= MaxRank; r++) marks += rank >= r ? RankName(r).Substring(0, 1) : "-";
                sb.AppendLine($"  [{marks}] {a.Id} „{a.Title}“ (Rang {rank}{(rank > 0 ? " = " + RankName(rank) : "")}) → {(a.IsTrophy ? "Trophäe" : GetUnlockName(a.Unlock))}");
            }
            foreach (UnlockId id in Enum.GetValues(typeof(UnlockId)))
            {
                if (id == UnlockId.None) continue;
                sb.Append($"  {id}:");
                for (int r = RankEasy; r <= MaxRank; r++) sb.Append($" {RankName(r)} {(IsUnlockedPersistent(id, r) ? "frei" : "gesperrt")}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
