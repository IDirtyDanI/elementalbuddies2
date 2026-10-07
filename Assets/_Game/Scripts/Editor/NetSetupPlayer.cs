using System.Collections.Generic;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElementalBuddies.EditorTools
{
    // Mehrspieler-Einrichtung der Spielfigur (Paket A – Champion). Läuft über „BuddyTD/Netzwerk/Einrichten (alles)“.
    public static class NetSetupPlayer
    {
        // Player.prefab: Owner-NetworkTransform (Besitzer bewegt, alle anderen interpolieren) + persönliches Mana.
        // NetworkObject + PlayerAvatar fügt der Kern-Schritt 100 hinzu.
        [NetSetupStep(110)]
        public static void SetupPlayerPrefab()
        {
            NetworkSetup.EditPrefab(NetworkSetup.PlayerPrefabPath, root =>
            {
                var nt = NetworkSetup.Ensure<NetworkTransform>(root);
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                nt.SyncPositionX = true;
                nt.SyncPositionY = true;
                nt.SyncPositionZ = true;
                // Figur dreht sich nur um Y
                nt.SyncRotAngleX = false;
                nt.SyncRotAngleY = true;
                nt.SyncRotAngleZ = false;
                nt.SyncScaleX = false;
                nt.SyncScaleY = false;
                nt.SyncScaleZ = false;
                nt.InLocalSpace = false;
                nt.Interpolate = true;
                // Deltas unzuverlässig: ein verlorenes Paket blockiert nicht alle folgenden (weniger Ruckeln übers Internet)
                nt.UseUnreliableDeltas = true;

                NetworkSetup.Ensure<PlayerMana>(root);
                EditorUtility.SetDirty(root);
            });
        }

        // test.unity: Die früher in der Szene platzierte Spielfigur entfernen (die Figur spawnt jetzt der Server über
        // NetBootstrap). Position/Drehung gehen auf „NetBootstrap“ über – dessen Transform ist der Spawn-Mittelpunkt.
        // Szenen-Referenzen auf die alte Figur (CameraFollow.Target, UpgradeManager.PlayerStatsRef/PlayerControllerRef)
        // werden dadurch leer; diese Skripte suchen die eigene Figur zur Laufzeit (PlayerAvatar.Local).
        [NetSetupStep(210)]
        public static void RemoveScenePlayer()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkSetup.PlayerPrefabPath);
            if (prefab == null) { Debug.LogError("[NetSetup] Player.prefab fehlt"); return; }

            var scene = SceneManager.GetActiveScene();
            var found = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
                    if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) != NetworkSetup.PlayerPrefabPath) continue;
                    if (!found.Contains(go)) found.Add(go);
                }
            }
            if (found.Count == 0)
            {
                Debug.Log("[NetSetup] Keine Szenen-Instanz von Player.prefab (schon entfernt)");
                return;
            }

            var boot = NetworkSetup.EnsureSceneObject("NetBootstrap");
            var first = found[0].transform;
            boot.transform.SetPositionAndRotation(first.position, Quaternion.Euler(0f, first.eulerAngles.y, 0f));
            EditorUtility.SetDirty(boot.transform);

            foreach (var go in found)
            {
                Debug.Log($"[NetSetup] Entferne Szenen-Spielfigur „{go.name}“ (Spawn-Mittelpunkt → NetBootstrap {first.position})");
                Object.DestroyImmediate(go);
            }
        }
    }
}
