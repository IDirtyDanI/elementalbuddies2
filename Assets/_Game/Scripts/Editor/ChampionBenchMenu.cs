using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Menü: BuddyTD → Balancing → Champion-Bench (Play Mode). Startet ChampionBench.Run("all") in der laufenden
    // Spielszene (zwischen zwei Wellen). Ergebnis: Konsole („[ChampionBench]“) und ChampionBench.LastReport.
    public static class ChampionBenchMenu
    {
        private const string MenuPath = "BuddyTD/Balancing/Champion-Bench (Play Mode)";

        [MenuItem(MenuPath)]
        private static void RunBench()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning($"{ChampionBench.LogPrefix} Nur im Play Mode: Spielszene starten, zwischen zwei Wellen erneut aufrufen.");
                return;
            }
            if (ChampionBench.IsRunning)
            {
                Debug.LogWarning($"{ChampionBench.LogPrefix} Läuft bereits.");
                return;
            }
            ChampionBench.Run("all");
        }
    }
}
