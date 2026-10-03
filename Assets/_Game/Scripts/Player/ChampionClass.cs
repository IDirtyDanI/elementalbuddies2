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

        // Name der Spielszene und des Hauptmenüs
        public const string GameScene = "test";
        public const string MenuScene = "MainMenu";
    }
}
