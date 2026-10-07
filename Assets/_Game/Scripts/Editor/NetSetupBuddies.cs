using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Mehrspieler-Einrichtung Paket C (Buddies): Alle Buddy-Prefabs unter Assets/_Game/Prefabs (Basis, Fusionen, Super-Elementare)
    // bekommen am Root ein NetworkObject + BuddyNet. Keine NetworkTransform: Buddy-Roots bewegen sich nicht (der Phönix fliegt nur
    // mit seinem Kind "Visual", das auf jedem Rechner lokal simuliert wird). Die Prefab-Liste baut NetworkSetup danach automatisch.
    // Idempotent; läuft über "BuddyTD/Netzwerk/Einrichten (alles)".
    public static class NetSetupBuddies
    {
        [NetSetupStep(130)]
        public static void SetupBuddyPrefabs()
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { NetworkSetup.PrefabRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentInChildren<ElementalBuddy>(true) == null) continue;
                // Bereits eingerichtet -> nichts speichern
                if (asset.GetComponent<NetworkObject>() != null && asset.GetComponent<BuddyNet>() != null) { count++; continue; }

                NetworkSetup.EditPrefab(path, root =>
                {
                    NetworkSetup.Ensure<NetworkObject>(root);
                    NetworkSetup.Ensure<BuddyNet>(root);
                });
                count++;
                Debug.Log($"[NetSetup] Buddy-Prefab vernetzt: {path}");
            }
            CheckSuperConfigs();
            Debug.Log($"[NetSetup] Buddies: {count} Prefabs mit NetworkObject + BuddyNet");
        }

        // Super-Elementare brauchen im Netzbetrieb ein Prefab (kein Laufzeit-Platzhalter). Warnung, falls eine Config keins hat.
        private static void CheckSuperConfigs()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UnitConfigSO"))
            {
                var config = AssetDatabase.LoadAssetAtPath<UnitConfigSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (config == null || (int)config.Type < (int)UnitType.VolcanoTitan) continue;
                if (config.Prefab == null)
                    Debug.LogWarning($"[NetSetup] Super-Config '{config.name}' hat kein Prefab – im Mehrspieler nicht baubar.");
                else if (config.Prefab.GetComponent<NetworkObject>() == null)
                    Debug.LogWarning($"[NetSetup] Super-Prefab '{config.Prefab.name}' liegt nicht unter {NetworkSetup.PrefabRoot} oder hat kein NetworkObject.");
            }
        }
    }
}
