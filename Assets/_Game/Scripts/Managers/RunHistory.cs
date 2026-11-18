using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Bestenliste der eigenen Läufe (2026-11-18): Speicher in PlayerPrefs (JSON unter PrefKey, Regeln in RunHistoryRules).
    // Eingetragen wird auf jedem Rechner (Server und Koop-Client) lokal:
    //  - beim Game Over (GameManager.TriggerGameOver bzw. ApplyRemoteGameOver),
    //  - beim Morgengrauen (WaveManager.ApplyDawn) – wer danach „Beenden“ wählt, hat den Sieg so trotzdem in der Liste;
    //    spielt er endlos weiter, ersetzt das spätere Game Over denselben Eintrag (gleiche Lauf-Id).
    // Abbrüche (Pause → Hauptmenü, Spiel beendet) zählen nicht. Läufe mit DevTools-Cheats werden nicht eingetragen.
    public static class RunHistory
    {
        public const string PrefKey = "run_history";

        // Alle Einträge (nach Rang sortiert)
        public static List<RunRecord> Load() => RunHistoryRules.FromJson(PlayerPrefs.GetString(PrefKey, ""));

        public static void Save(List<RunRecord> runs)
        {
            PlayerPrefs.SetString(PrefKey, RunHistoryRules.ToJson(runs));
            PlayerPrefs.Save();
        }

        public static void Add(RunRecord record)
        {
            var runs = Load();
            RunHistoryRules.Upsert(runs, record);
            Save(runs);
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
        }

        // Laufenden Lauf eintragen (dawn = Morgengrauen erreicht, Lauf geht ggf. weiter). Nie Ausnahmen nach außen.
        public static void RecordCurrentRun(bool dawn)
        {
            try
            {
                var gm = GameManager.Instance;
                // Morgengrauen nach dem Game Over gibt es nicht – sonst würde ein beendeter Lauf überschrieben
                if (gm == null || (dawn && gm.IsGameOver)) return;
                var wm = WaveManager.Instance;
                int wave = dawn ? SiegeLevels.DawnWave : gm.LastWaveReached;
                var am = AchievementManager.Instance;
                bool cheats = am != null && !am.AchievementsAllowed;
                if (!RunHistoryRules.ShouldRecord(wave, cheats)) return;

                var map = MapContext.Current;
                var cls = PlayerAbilities.Instance != null ? PlayerAbilities.Instance.ActiveClass : GameSession.SelectedChampion;
                var roster = ChampionRosterSO.Load();
                var def = roster != null ? roster.Find(cls) : null;
                var diff = wm != null ? wm.Difficulty : GameSession.Difficulty;
                var rs = RunStats.Instance;

                var r = new RunRecord
                {
                    Id = gm.RunId,
                    Time = RunHistoryRules.ToUnix(System.DateTime.UtcNow),
                    Map = map != null ? map.Id : "",
                    MapName = map != null ? map.DisplayName : "",
                    Champion = (int)cls,
                    ChampionName = def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : cls.ToString(),
                    Difficulty = diff != null ? diff.Id : "",
                    DifficultyName = diff != null ? diff.DisplayName : "",
                    Siege = SiegeLevels.Current,
                    Wave = wave,
                    Seconds = rs != null ? rs.PlaySeconds : Time.time - gm.StartTime,
                    Reason = dawn ? "Morgengrauen" : gm.GameOverReason ?? "",
                    Dawn = dawn || (wm != null && wm.DawnReached),
                    Coop = Net.IsMultiplayer,
                    Continued = gm.Continued,
                    Kills = rs != null ? rs.Kills : 0,
                    TeamWipes = gm.TeamWipes,
                };
                Add(r);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("RunHistory: Lauf konnte nicht eingetragen werden – " + e.Message);
            }
        }
    }
}
