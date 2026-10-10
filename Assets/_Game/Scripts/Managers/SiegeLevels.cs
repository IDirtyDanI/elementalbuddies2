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
        public static int HighestWon => PlayerPrefs.GetInt(PrefWon, -1);
        // Höchste wählbare Stufe
        public static int MaxSelectable => Unlocked ? Mathf.Clamp(HighestWon + 1, 1, Max) : 0;

        public static int BestWave(int level) => PlayerPrefs.GetInt(PrefBest + level, 0);

        public static string Label(int level) => level <= 0 ? "Keine Belagerungsstufe" : $"Belagerungsstufe {level} – {Names[Mathf.Clamp(level, 0, Max)]}";

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
        public static bool RecordDawn(int level)
        {
            RecordWave(level, DawnWave);
            if (level <= HighestWon) return false;
            PlayerPrefs.SetInt(PrefWon, level);
            PlayerPrefs.Save();
            return true;
        }

        public static void RecordWave(int level, int wave)
        {
            if (wave <= BestWave(level)) return;
            PlayerPrefs.SetInt(PrefBest + level, wave);
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(PrefWon);
            for (int i = 0; i <= Max; i++) PlayerPrefs.DeleteKey(PrefBest + i);
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
