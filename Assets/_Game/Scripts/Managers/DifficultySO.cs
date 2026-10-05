using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Schwierigkeitsstufe (Leicht / Normal / Schwer): Multiplikatoren auf die Normal-Kurven des WaveManagers und
    // die Einnahmen des EconomyManagers. Gilt zusätzlich zur Wellen-Rampe – auf jeder Stufe werden die Gegner mit
    // der Zeit stärker und zahlreicher. Assets liegen unter Resources/Difficulty/, Auswahl über GameSession.Difficulty.
    [CreateAssetMenu(fileName = "Difficulty", menuName = "ElementalBuddies/Difficulty", order = 5)]
    public class DifficultySO : ScriptableObject
    {
        public const string ResourceFolder = "Difficulty";
        public const string DefaultId = "normal";

        [Tooltip("Schlüssel für PlayerPrefs/Code (leicht, normal, schwer).")]
        public string Id = DefaultId;
        [Tooltip("Anzeigename im Hauptmenü.")]
        public string DisplayName = "Normal";
        [TextArea] public string Description = "";
        [Tooltip("Sortierung in der Auswahl (aufsteigend).")]
        public int Order = 1;

        [Header("Gegner")]
        [Tooltip("Faktor auf die HP der Normalgegner.")]
        public float HpMultiplier = 1f;
        [Tooltip("Faktor auf die Anzahl der Normalgegner (Deckel MaxEnemiesPerWave gilt weiter).")]
        public float CountMultiplier = 1f;
        [Tooltip("Faktor auf den Schaden aller Gegner (Nahkampf, Fernkampf, Boss-Fähigkeiten).")]
        public float DamageMultiplier = 1f;
        [Tooltip("Faktor auf die Boss-HP.")]
        public float BossHpMultiplier = 1f;

        [Header("Spieler")]
        [Tooltip("Faktor auf die Einnahmen (Kill-Drops und Wellen-Bonus).")]
        public float IncomeMultiplier = 1f;

        private static List<DifficultySO> _all;
        private static DifficultySO _fallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all = null;
        }

        // Alle Stufen aus Resources/Difficulty/, nach Order sortiert (für die Auswahl im Hauptmenü)
        public static IReadOnlyList<DifficultySO> All
        {
            get
            {
                if (_all == null)
                {
                    _all = new List<DifficultySO>(Resources.LoadAll<DifficultySO>(ResourceFolder));
                    _all.Sort((a, b) => a.Order.CompareTo(b.Order));
                }
                return _all;
            }
        }

        // Stufe nach Id (Groß-/Kleinschreibung egal); unbekannt → Normal
        public static DifficultySO Get(string id)
        {
            foreach (var d in All)
                if (d != null && string.Equals(d.Id, id, System.StringComparison.OrdinalIgnoreCase)) return d;
            return Normal;
        }

        // Stufe nach Index in All (0 = Leicht, 1 = Normal, 2 = Schwer); außerhalb → Normal
        public static DifficultySO Get(int index)
        {
            var all = All;
            return index >= 0 && index < all.Count && all[index] != null ? all[index] : Normal;
        }

        // Kurzfassung der Faktoren für die Auswahl im Menü, z. B. "Gegner −25 % Leben, −20 % Anzahl, −20 % Schaden · +15 % Splitter";
        // ohne Abweichung von 1 die Beschreibung (Normal: "Das vorgesehene Spielerlebnis.")
        public string EffectSummary()
        {
            var enemy = new List<string>();
            AddPercent(enemy, HpMultiplier, "Leben");
            AddPercent(enemy, CountMultiplier, "Anzahl");
            AddPercent(enemy, DamageMultiplier, "Schaden");
            if (!Mathf.Approximately(BossHpMultiplier, HpMultiplier)) AddPercent(enemy, BossHpMultiplier, "Boss-Leben");
            var player = new List<string>();
            AddPercent(player, IncomeMultiplier, "Splitter");

            string s = enemy.Count > 0 ? "Gegner " + string.Join(", ", enemy.ToArray()) : "";
            if (player.Count > 0) s += (s.Length > 0 ? " · " : "") + string.Join(", ", player.ToArray());
            return s.Length > 0 ? s : Description;
        }

        private static void AddPercent(List<string> parts, float mult, string label)
        {
            int pct = Mathf.RoundToInt((mult - 1f) * 100f);
            if (pct == 0) return;
            parts.Add((pct > 0 ? "+" : "−") + Mathf.Abs(pct) + " % " + label);
        }

        // Normal-Stufe; fehlen die Assets, eine Laufzeit-Instanz mit allen Faktoren 1
        public static DifficultySO Normal
        {
            get
            {
                foreach (var d in All)
                    if (d != null && string.Equals(d.Id, DefaultId, System.StringComparison.OrdinalIgnoreCase)) return d;
                if (_fallback == null)
                {
                    _fallback = CreateInstance<DifficultySO>();
                    _fallback.name = "Difficulty_Normal_Fallback";
                    _fallback.hideFlags = HideFlags.DontSave;
                }
                return _fallback;
            }
        }
    }
}
