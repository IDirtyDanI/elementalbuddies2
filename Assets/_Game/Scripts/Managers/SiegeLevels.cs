using UnityEngine;

namespace ElementalBuddies
{
    // Belagerungsstufen (Plan „Fesselung“ E6, Heat/Ascension): freigeschaltet mit dem ersten Morgengrauen (Erfolg
    // „Morgengrauen“ → UnlockId.SiegeLevels). Stufe n ist wählbar, sobald Stufe n−1 einmal bis zum Morgengrauen
    // gespielt wurde. Die Erschwernisse stapeln sich (Stufe 3 enthält 1 und 2). Jede Stufe hat einen eigenen Rekord.
    // Die gewählte Stufe liegt in GameSession.SiegeLevel und geht im Koop mit der Schwierigkeit an alle (NetLobby).
    public static class SiegeLevels
    {
        public const int Max = 5;
        // Welle, nach der die Nacht überstanden ist (Morgengrauen, E5)
        public const int DawnWave = 20;

        private const string PrefWon = "siege_won";          // höchste bis zum Morgengrauen gespielte Stufe (−1 = nie)
        private const string PrefBest = "siege_best_";       // + Stufe: beste Welle auf dieser Stufe

        public static readonly string[] Names =
        {
            "Keine",
            "Unruhige Nacht",
            "Hungrige Tote",
            "Knochenkönige",
            "Karge Vorräte",
            "Ewige Finsternis",
        };

        public static readonly string[] Descriptions =
        {
            "Die normale Belagerung.",
            "Eliten ab Welle 8, 50 % mehr Eliten",
            "Gegner laufen 10 % schneller",
            "Bosse haben 30 % mehr Leben",
            "Wellenbonus −25 %",
            "Gegner verursachen 15 % mehr Schaden, Wiederbelebung kostet 10 % mehr",
        };

        // Stufe des laufenden Spiels (Spielszene) bzw. die gewählte (Menü)
        public static int Current => Mathf.Clamp(GameSession.SiegeLevel, 0, Max);

        public static bool Unlocked => Progression.IsUnlockedPersistent(UnlockId.SiegeLevels);

        // Rekorde und Freischalt-Kette gelten pro Map (Nutzerentscheidung 2026-10-15): Schlüssel mit dem Rekord-Präfix der
        // Map (Progression.RecordKey; Stadt = alte Schlüssel ohne Präfix). map == null → Map der laufenden Szene
        // (MapContext.Current, im Menü die Stadt) – das Menü übergibt die gewählte Map.
        private static string Key(MapDefinitionSO map, string key) => Progression.RecordKey(map, key);

        public static int HighestWonOn(MapDefinitionSO map) => PlayerPrefs.GetInt(Key(map, PrefWon), -1);
        public static int HighestWon => HighestWonOn(null);
        // Höchste wählbare Stufe (eigene Kette je Map ab Stufe 1)
        public static int MaxSelectableOn(MapDefinitionSO map) => Unlocked ? Mathf.Clamp(HighestWonOn(map) + 1, 1, Max) : 0;
        public static int MaxSelectable => MaxSelectableOn(null);

        public static int BestWave(int level, MapDefinitionSO map = null) => PlayerPrefs.GetInt(Key(map, PrefBest + level), 0);

        // Name der Stufe: map-eigener Name (MapDefinitionSO.SiegeLevelNames, Stadt: keine) oder Standard
        public static string Name(int level, MapDefinitionSO map = null)
        {
            level = Mathf.Clamp(level, 0, Max);
            if (map == null) map = MapContext.Current;
            return (map != null ? map.SiegeLevelName(level) : null) ?? Names[level];
        }

        public static string Label(int level) => level <= 0 ? "Keine Belagerungsstufe" : $"Belagerungsstufe {level} – {Name(level)}";

        // Alle aktiven Erschwernisse als Aufzählung (Menü-Tooltip)
        public static string Summary(int level)
        {
            if (level <= 0) return Descriptions[0];
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= Mathf.Min(level, Max); i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("• ").Append(Descriptions[i]);
            }
            return sb.ToString();
        }

        // Spielende/Morgengrauen: Rekord der Stufe speichern; true = neue höchste gewonnene Stufe
        public static bool RecordDawn(int level, MapDefinitionSO map = null)
        {
            RecordWave(level, DawnWave, map);
            if (level <= HighestWonOn(map)) return false;
            PlayerPrefs.SetInt(Key(map, PrefWon), level);
            PlayerPrefs.Save();
            return true;
        }

        public static void RecordWave(int level, int wave, MapDefinitionSO map = null)
        {
            if (wave <= BestWave(level, map)) return;
            PlayerPrefs.SetInt(Key(map, PrefBest + level), wave);
            PlayerPrefs.Save();
        }

        // Alle Maps
        public static void ResetAll()
        {
            var maps = new System.Collections.Generic.List<MapDefinitionSO>(MapRegistry.All);
            if (!maps.Contains(MapRegistry.Default)) maps.Add(MapRegistry.Default);
            foreach (var m in maps)
            {
                PlayerPrefs.DeleteKey(Key(m, PrefWon));
                for (int i = 0; i <= Max; i++) PlayerPrefs.DeleteKey(Key(m, PrefBest + i));
            }
        }

        // ---------------- Erschwernisse (kumulativ) ----------------

        public static int EliteFromWave(int normal) => Current >= 1 ? Mathf.Min(normal, 8) : normal;
        public static float EliteShareFactor => Current >= 1 ? 1.5f : 1f;
        public static float EnemySpeedFactor => Current >= 2 ? 1.1f : 1f;
        public static float BossHpFactor => Current >= 3 ? 1.3f : 1f;
        public static float WaveBonusFactor => Current >= 4 ? 0.75f : 1f;
        public static float EnemyDamageFactor => Current >= 5 ? 1.15f : 1f;
        public static float WipeCostExtra => Current >= 5 ? 0.10f : 0f;
    }
}
