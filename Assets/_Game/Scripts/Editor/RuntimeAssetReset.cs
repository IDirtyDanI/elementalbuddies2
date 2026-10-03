using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Upgrades verändern UnitConfigSO/GlobalSettingsSO zur Laufzeit direkt. UpgradeManager/EconomyManager stellen die
    // Werte in OnDestroy wieder her – geht das schief (z. B. Recompile im Play-Mode verliert die Backups), blieben die
    // aufgewerteten Werte im Editor-Speicher hängen und würden beim nächsten Speichern auf Platte landen.
    // Daher beim Verlassen des Play-Modes die Daten nicht-dirty Assets frisch von der Platte in das bestehende Objekt
    // kopieren (CopySerialized). Kein Entladen: Die Objekt-Identität bleibt, Referenzen aus Szene/Scripts bleiben gültig.
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
            int n = Restore<UnitConfigSO>() + Restore<GlobalSettingsSO>();
            if (n > 0) Debug.Log($"RuntimeAssetReset: {n} Config-Assets nach dem Play-Mode auf den gespeicherten Stand zurückgesetzt.");
        }

        private static int Restore<T>() where T : ScriptableObject
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var live = AssetDatabase.LoadAssetAtPath<T>(path);
                if (live == null || EditorUtility.IsDirty(live)) continue; // bewusste Inspector-Änderungen behalten

                Object[] fromDisk = InternalEditorUtility.LoadSerializedFileAndForget(path);
                foreach (Object o in fromDisk)
                {
                    if (o is T disk && EditorJsonUtility.ToJson(disk) != EditorJsonUtility.ToJson(live))
                    {
                        EditorUtility.CopySerialized(disk, live);
                        EditorUtility.ClearDirty(live);
                        count++;
                    }
                    Object.DestroyImmediate(o);
                }
            }
            return count;
        }
    }
}
