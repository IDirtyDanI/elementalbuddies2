using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Entdeckt-Stand des Kodex (Plan „Fesselung“ E4) in PlayerPrefs: „codex_<Schlüssel>“ = 1, Karten zusätzlich
    // „codex_taken_<Asset>“ = wie oft genommen. Entdeckt wird auf jedem Rechner selbst (auch Koop-Clients):
    // Gegner/Eliten beim Erscheinen (EnemyBrain), Ereignisse beim Wellenstart, Fusionen beim Verschmelzen und Karten
    // beim Anbieten bzw. Wählen (FeedbackDirector).
    public static class Codex
    {
        private const string Prefix = "codex_";
        private const string TakenPrefix = "codex_taken_";
        private static readonly HashSet<string> _known = new HashSet<string>();

        public static event System.Action<string> OnDiscovered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _known.Clear();
            OnDiscovered = null;
        }

        public static string EnemyKey(EnemyConfigSO c) => c != null ? "enemy:" + c.name : null;
        public static string EliteKey(EliteAffix a) => "elite:" + a;
        public static string EventKey(WaveEvent e) => "event:" + e;
        public static string FusionKey(FusionElement e) => "fusion:" + e;
        public static string CardKey(UpgradeDefinitionSO c) => c != null ? "card:" + c.name : null;

        public static bool IsDiscovered(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (_known.Contains(key)) return true;
            if (PlayerPrefs.GetInt(Prefix + key, 0) == 0) return false;
            _known.Add(key);
            return true;
        }

        // true, wenn neu entdeckt
        public static bool Discover(string key)
        {
            if (string.IsNullOrEmpty(key) || IsDiscovered(key)) return false;
            _known.Add(key);
            PlayerPrefs.SetInt(Prefix + key, 1);
            PlayerPrefs.Save();
            OnDiscovered?.Invoke(key);
            return true;
        }

        public static int TakenCount(UpgradeDefinitionSO card) => card != null ? PlayerPrefs.GetInt(TakenPrefix + card.name, 0) : 0;

        public static void RecordTaken(UpgradeDefinitionSO card)
        {
            if (card == null) return;
            Discover(CardKey(card));
            PlayerPrefs.SetInt(TakenPrefix + card.name, TakenCount(card) + 1);
            PlayerPrefs.Save();
        }

        // Fortschritt einer Kategorie bzw. gesamt
        public static void Count(CodexDatabaseSO.Category? category, out int found, out int total)
        {
            found = total = 0;
            var db = CodexDatabaseSO.Instance;
            if (db == null) return;
            foreach (var e in db.Entries)
            {
                if (e == null || (category.HasValue && e.Category != category.Value)) continue;
                total++;
                if (IsDiscovered(e.Key)) found++;
            }
        }

        public static void ResetAll()
        {
            var db = CodexDatabaseSO.Instance;
            if (db != null)
                foreach (var e in db.Entries)
                    if (e != null)
                    {
                        PlayerPrefs.DeleteKey(Prefix + e.Key);
                        if (e.Key.StartsWith("card:")) PlayerPrefs.DeleteKey(TakenPrefix + e.Key.Substring(5));
                    }
            _known.Clear();
            PlayerPrefs.Save();
        }
    }
}
