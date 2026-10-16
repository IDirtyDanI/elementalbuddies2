using UnityEngine;

namespace ElementalBuddies
{
    // Spielbare Champions (Auswahl im Hauptmenü)
    public enum ChampionClass
    {
        Mage,   // Magier: Arkanball / Blink
        Knight, // Schwertkämpfer: Schwerthieb / Schildblock
        Archer, // Bogenschütze: Pfeilschuss / Rolle
        Berserker // Berserker: Axthieb / Ansturm (Wut statt Mana)
    }

    // Überträgt die Champion-Wahl vom Hauptmenü in die Spielszene (statisch + PlayerPrefs als Vorbelegung).
    public static class GameSession
    {
        // Anzahl der Champions (Enum-Werte) – für Klammern, F6-Wechsel, Menü
        public static readonly int ChampionCount = System.Enum.GetValues(typeof(ChampionClass)).Length;

        // Ungültige Werte (alte PlayerPrefs, Netz) auf einen gültigen Champion begrenzen
        public static ChampionClass ClampChampion(int value) => (ChampionClass)Mathf.Clamp(value, 0, ChampionCount - 1);

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
                    _selected = ClampChampion(PlayerPrefs.GetInt(PrefKey, 0));
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
                    // Sanfter Einstieg (Plan Fesselung F2): beim allerersten Spiel ist „Leicht“ vorgewählt
                    string first = PlayerPrefs.HasKey("BestWave") ? DifficultySO.DefaultId : "leicht";
                    _difficultyId = PlayerPrefs.GetString(DifficultyPrefKey, first);
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

        // ---------------- Belagerungsstufe (Plan Fesselung E6) ----------------

        private const string SiegePrefKey = "siege_level";

        // Gewählte Belagerungsstufe 0..SiegeLevels.Max (Menü: auf die freigeschaltete Stufe begrenzt; Koop: vom Host)
        public static int SiegeLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SiegePrefKey, 0), 0, SiegeLevels.Max);
            set
            {
                PlayerPrefs.SetInt(SiegePrefKey, Mathf.Clamp(value, 0, SiegeLevels.Max));
                PlayerPrefs.Save();
            }
        }

        // ---------------- Map (Walddorf-Plan, Paket P0) ----------------

        private const string MapPrefKey = "map";

        // Id der gewählten Map (PlayerPrefs „map“, Standard Stadt; unbekannte Ids → Stadt). Gelesen wird ohne
        // Schreiben – der Schlüssel entsteht erst, wenn das Menü eine Map wählt. Freischaltung prüft das Menü (C-Menü).
        public static string MapId
        {
            get
            {
                string id = PlayerPrefs.GetString(MapPrefKey, MapDefinitionSO.CityId);
                var m = MapRegistry.Find(id);
                return m != null ? m.Id : MapRegistry.Default.Id;
            }
            set
            {
                var m = MapRegistry.Find(value) ?? MapRegistry.Default;
                PlayerPrefs.SetString(MapPrefKey, m.Id);
                PlayerPrefs.Save();
            }
        }

        // Gewählte Map (nie null)
        public static MapDefinitionSO Map => MapRegistry.Get(MapId);

        // Name der Spielszene der gewählten Map (früher fest „test“) und des Hauptmenüs
        public static string GameScene => Map.SceneName;
        public const string MenuScene = "MainMenu";
    }
}
