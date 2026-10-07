using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Einrichtung Paket D (Draft, Händler, Schreine, Gold). Idempotent, wird von „BuddyTD/Netzwerk/Einrichten (alles)“
    // aufgerufen. Händler/Schreine bleiben MonoBehaviours (Sync über das in-Szene-NetGame) → keine neuen NetworkObjects.
    public static class NetSetupProgress
    {
        private const string SettingsPath = "Assets/ScriptableObjects/Configs/GlobalSettings.asset";

        // GlobalSettings: neue Gold-Felder mit den Defaults ins Asset schreiben (falls das Asset sie noch nicht kennt)
        [NetSetupStep(150)]
        public static void SetupGoldSettings()
        {
            var gs = AssetDatabase.LoadAssetAtPath<GlobalSettingsSO>(SettingsPath);
            if (gs == null) { Debug.LogWarning("[NetSetup] GlobalSettings fehlt: " + SettingsPath); return; }
            var so = new SerializedObject(gs);
            if (so.FindProperty("GoldPerWave") == null) return;
            EditorUtility.SetDirty(gs); // speichert die Feld-Defaults (StartGold 0, GoldPerWave 15, Bonus 60) ins Asset
        }

        // Szene: MerchantManager bekommt die GlobalSettings für die Gold-Werte (Fallback wäre EconomyManager.Settings)
        [NetSetupStep(250)]
        public static void SetupSceneMerchantSettings()
        {
            var gs = AssetDatabase.LoadAssetAtPath<GlobalSettingsSO>(SettingsPath);
            var mm = Object.FindFirstObjectByType<MerchantManager>(FindObjectsInactive.Include);
            if (mm == null || gs == null || mm.Settings == gs) return;
            mm.Settings = gs;
            EditorUtility.SetDirty(mm);
        }
    }
}
