using UnityEngine;

namespace ElementalBuddies
{
    // Spielbare Champions (Auswahl im Hauptmenü)
    public enum ChampionClass
    {
        Mage,   // Magier: Arkanball / Blink
        Knight, // Schwertkämpfer: Schwerthieb / Schildblock
        Archer  // Bogenschütze: Pfeilschuss / Rolle
    }

    // Überträgt die Champion-Wahl vom Hauptmenü in die Spielszene (statisch + PlayerPrefs als Vorbelegung).
    public static class GameSession
    {
        private const string PrefKey = "champion";

        private static bool _loaded;
        private static ChampionClass _selected = ChampionClass.Mage;

        public static ChampionClass SelectedChampion
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _selected = (ChampionClass)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, 2);
                }
                return _selected;
            }
            set
            {
                _loaded = true;
                _selected = value;
                PlayerPrefs.SetInt(PrefKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        // ---------------- Schwierigkeit ----------------

        private const string DifficultyPrefKey = "difficulty";
        private static bool _difficultyLoaded;
        private static string _difficultyId = DifficultySO.DefaultId;

        // Id der gewählten Stufe (leicht/normal/schwer), Vorbelegung aus PlayerPrefs, Default Normal
        public static string DifficultyId
        {
            get
            {
                if (!_difficultyLoaded)
                {
                    _difficultyLoaded = true;
                    _difficultyId = PlayerPrefs.GetString(DifficultyPrefKey, DifficultySO.DefaultId);
                }
                return _difficultyId;
            }
            set
            {
                _difficultyLoaded = true;
                _difficultyId = string.IsNullOrEmpty(value) ? DifficultySO.DefaultId : value;
                PlayerPrefs.SetString(DifficultyPrefKey, _difficultyId);
                PlayerPrefs.Save();
            }
        }

        // Gewählte Stufe (nie null; unbekannte Id → Normal). Setzen speichert die Wahl in PlayerPrefs.
        public static DifficultySO Difficulty
        {
            get => DifficultySO.Get(DifficultyId);
            set => DifficultyId = value != null ? value.Id : DifficultySO.DefaultId;
        }

        // Name der Spielszene und des Hauptmenüs
        public const string GameScene = "test";
        public const string MenuScene = "MainMenu";
    }
}
