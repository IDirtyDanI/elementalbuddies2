using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Ein Eintrag der Bestenliste der eigenen Läufe (Hauptmenü → „Bestenliste“, 2026-11-18). Ids und Namen werden beide
    // gespeichert: angezeigt wird der aktuelle Name zur Id, der gespeicherte Name ist der Ersatz (z. B. Map entfernt).
    [Serializable]
    public class RunRecord
    {
        public string Id = "";            // Lauf-Kennung (Startzeit), damit Morgengrauen + späteres Game Over ein Eintrag bleiben
        public long Time;                 // Ende des Laufs, Unix-Sekunden (UTC)
        public string Map = "";           // MapDefinitionSO.Id
        public string MapName = "";
        public int Champion;              // (int)ChampionClass
        public string ChampionName = "";
        public string Difficulty = "";    // DifficultySO.Id
        public string DifficultyName = "";
        public int Siege;                 // Belagerungsstufe 0–5
        public int Wave;                  // erreichte Welle (wie GameManager.LastWaveReached)
        public float Seconds;             // Spielzeit
        public string Reason = "";        // Grund des Endes („Der Nexus wurde zerstört“, „Morgengrauen“ …)
        public bool Dawn;                 // Morgengrauen erreicht (Sieg nach Welle 20)
        public bool Coop;                 // mit Mitspielern
        public bool Continued;            // Nexus einmal per Werbevideo wiederbelebt (GameManager.Continued)
        public int Kills;
        public int TeamWipes;             // Team-Ausfälle (Wiederbelebung mit Preis)
    }

    // Regeln der Bestenliste: Rangfolge, Begrenzung, JSON. Rein, damit EditMode-Tests sie prüfen können.
    //  - Rang: Welle absteigend, bei Gleichstand kürzere Spielzeit, dann neuer zuerst.
    //  - Begrenzung: höchstens MaxEntries Einträge. Die KeepRecent neuesten bleiben immer (auch schwache Läufe sieht man
    //    so eine Weile), der Rest wird mit den bestplatzierten aufgefüllt – schlechte alte Läufe fallen zuerst heraus.
    //  - Eingetragen werden nur echte Läufe: erreichte Welle ≥ 1 und ohne DevTools-Cheats (ShouldRecord).
    public static class RunHistoryRules
    {
        public const int MaxEntries = 50;
        public const int KeepRecent = 10;
        public const int Version = 1;
        // Markierung „Nexus per Video wiederbelebt“ (UI ersetzt sie, falls die Schrift das Zeichen nicht hat)
        public const string ContinuedMark = "▶";

        [Serializable]
        private class Data
        {
            public int Version = RunHistoryRules.Version;
            public List<RunRecord> Runs = new List<RunRecord>();
        }

        public static bool ShouldRecord(int wave, bool cheats) => wave >= 1 && !cheats;

        // < 0: a steht vor b
        public static int Compare(RunRecord a, RunRecord b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int c = b.Wave.CompareTo(a.Wave);
            if (c != 0) return c;
            c = a.Seconds.CompareTo(b.Seconds);
            if (c != 0) return c;
            return b.Time.CompareTo(a.Time);
        }

        // Rangliste, optional nur eine Map (mapId null/leer = alle)
        public static List<RunRecord> Ranked(IEnumerable<RunRecord> runs, string mapId = null)
        {
            var list = new List<RunRecord>();
            if (runs != null)
                foreach (var r in runs)
                    if (r != null && (string.IsNullOrEmpty(mapId) || r.Map == mapId)) list.Add(r);
            list.Sort(Compare);
            return list;
        }

        // Eintrag hinzufügen oder (gleiche Id) ersetzen, danach begrenzen
        public static void Upsert(List<RunRecord> runs, RunRecord record)
        {
            if (runs == null || record == null) return;
            int i = string.IsNullOrEmpty(record.Id) ? -1 : runs.FindIndex(r => r != null && r.Id == record.Id);
            if (i >= 0)
            {
                // Derselbe Lauf (Morgengrauen, dann Game Over): nie schlechter werden, Morgengrauen bleibt erreicht
                var old = runs[i];
                record.Wave = Math.Max(record.Wave, old.Wave);
                record.Dawn |= old.Dawn;
                record.Continued |= old.Continued;
                runs[i] = record;
            }
            else runs.Add(record);
            Trim(runs);
        }

        // Auf MaxEntries kürzen: KeepRecent neueste behalten, Rest nach Rang auffüllen. Reihenfolge danach = Rang.
        public static void Trim(List<RunRecord> runs)
        {
            if (runs == null) return;
            runs.RemoveAll(r => r == null);
            if (runs.Count <= MaxEntries) { runs.Sort(Compare); return; }
            var byTime = new List<RunRecord>(runs);
            byTime.Sort((a, b) => b.Time.CompareTo(a.Time));
            var keep = new HashSet<RunRecord>();
            for (int i = 0; i < byTime.Count && keep.Count < Math.Min(KeepRecent, MaxEntries); i++) keep.Add(byTime[i]);
            runs.Sort(Compare);
            foreach (var r in runs)
            {
                if (keep.Count >= MaxEntries) break;
                keep.Add(r);
            }
            runs.RemoveAll(r => !keep.Contains(r));
        }

        // Platz (1-basiert) eines Laufs in der Rangliste, 0 = nicht enthalten
        public static int RankOf(IEnumerable<RunRecord> runs, string id, string mapId = null)
        {
            var ranked = Ranked(runs, mapId);
            for (int i = 0; i < ranked.Count; i++) if (ranked[i].Id == id) return i + 1;
            return 0;
        }

        public static string ToJson(List<RunRecord> runs)
        {
            var d = new Data();
            if (runs != null) foreach (var r in runs) if (r != null) d.Runs.Add(r);
            return JsonUtility.ToJson(d);
        }

        // Kaputter oder leerer Text → leere Liste (nie Ausnahme)
        public static List<RunRecord> FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return new List<RunRecord>();
            try
            {
                var d = JsonUtility.FromJson<Data>(json);
                var list = d != null && d.Runs != null ? d.Runs : new List<RunRecord>();
                list.RemoveAll(r => r == null);
                return list;
            }
            catch (Exception)
            {
                return new List<RunRecord>();
            }
        }

        // 754 s → "12:34", 3723 s → "1:02:03"
        public static string FormatDuration(float seconds)
        {
            int s = Math.Max(0, (int)Math.Floor(seconds));
            int h = s / 3600, m = s / 60 % 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
        }

        public static long ToUnix(DateTime utc) => (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        public static DateTime FromUnix(long seconds) => new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);

        // Kennzeichen hinter der Welle: ▶ = per Video fortgesetzt
        public static string Marks(RunRecord r, string continuedMark = ContinuedMark)
            => r != null && r.Continued ? continuedMark : "";
    }
}
