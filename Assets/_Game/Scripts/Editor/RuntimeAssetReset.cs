using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Upgrades verändern UnitConfigSO/GlobalSettingsSO zur Laufzeit direkt. UpgradeManager/EconomyManager stellen die
    // Werte in OnDestroy wieder her – geht das schief (z. B. Recompile im Play-Mode verliert die Backups), blieben die
    // aufgewerteten Werte im Editor-Speicher hängen und würden beim nächsten Speichern auf Platte landen.
    // Daher beim Verlassen des Play-Modes alle nicht-dirty Assets dieser Typen entladen → frisch von der Platte.
    [InitializeOnLoad]
    public static class RuntimeAssetReset
    {
        static RuntimeAssetReset()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            int n = Reload<UnitConfigSO>() + Reload<GlobalSettingsSO>();
            if (n > 0) Debug.Log($"RuntimeAssetReset: {n} Config-Assets nach dem Play-Mode von der Platte neu geladen.");
        }

        private static int Reload<T>() where T : ScriptableObject
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null || EditorUtility.IsDirty(asset)) continue; // bewusste Inspector-Änderungen behalten
                Resources.UnloadAsset(asset);
                count++;
            }
            return count;
        }
    }
}
