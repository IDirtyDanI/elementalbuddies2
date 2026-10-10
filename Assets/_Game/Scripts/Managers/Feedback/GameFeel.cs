using UnityEngine;

namespace ElementalBuddies
{
    // Spieler-Einstellungen für Game Feel (Bildschirmwackeln, Schadenszahlen, Treffer-Stopp, Aufblitzen).
    // Gespeichert in PlayerPrefs, gelesen von CameraFollow, TimeWarp, HitFlash und DamageNumbers.
    public static class GameFeel
    {
        public enum NumberMode { Off = 0, Champion = 1, All = 2 }

        public const string PrefShake = "feel_shake", PrefNumbers = "feel_numbers", PrefHitStop = "feel_hitstop", PrefFlash = "feel_flash";

        private static bool _loaded;
        private static float _shake = 1f;
        private static NumberMode _numbers = NumberMode.All;
        private static bool _hitStop = true, _flash = true;

        // 0..1, Faktor auf jedes Kamera-Wackeln
        public static float ShakeStrength { get { Load(); return _shake; } set { _shake = Mathf.Clamp01(value); Save(PrefShake, _shake); } }
        public static NumberMode Numbers { get { Load(); return _numbers; } set { _numbers = value; Save(PrefNumbers, (int)value); } }
        // Treffer-Stopp und Zeitlupe (nur Einzelspiel)
        public static bool HitStop { get { Load(); return _hitStop; } set { _hitStop = value; Save(PrefHitStop, value ? 1 : 0); } }
        public static bool Flash { get { Load(); return _flash; } set { _flash = value; Save(PrefFlash, value ? 1 : 0); } }

        public static string NumberModeLabel(NumberMode m) =>
            m == NumberMode.Off ? "Aus" : m == NumberMode.Champion ? "Nur Champion" : "Alle";

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            if (PlayerPrefs.HasKey(PrefShake)) _shake = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefShake));
            if (PlayerPrefs.HasKey(PrefNumbers)) _numbers = (NumberMode)Mathf.Clamp(PlayerPrefs.GetInt(PrefNumbers), 0, 2);
            if (PlayerPrefs.HasKey(PrefHitStop)) _hitStop = PlayerPrefs.GetInt(PrefHitStop) != 0;
            if (PlayerPrefs.HasKey(PrefFlash)) _flash = PlayerPrefs.GetInt(PrefFlash) != 0;
        }

        private static void Save(string key, float v) { PlayerPrefs.SetFloat(key, v); PlayerPrefs.Save(); }
        private static void Save(string key, int v) { PlayerPrefs.SetInt(key, v); PlayerPrefs.Save(); }
    }
}
