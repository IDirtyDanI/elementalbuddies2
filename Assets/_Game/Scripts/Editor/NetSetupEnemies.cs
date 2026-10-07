using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Paket B (Gegner & Wellen): Gegner- und Boss-Prefabs werden NetworkObjects mit server-autoritativer
    // NetworkTransform (Position + Gierwinkel, keine Skalierung – VisualScale setzt EnemyBrain lokal) und EnemyNet.
    // Gegner-Projektile bleiben lokale Optik (kein NetworkObject). Idempotent.
    public static class NetSetupEnemies
    {
        [NetSetupStep(120)]
        public static void SetupEnemyPrefabs()
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { NetworkSetup.PrefabRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                // Nur Prefabs mit EnemyBrain an der Wurzel (Runner, Swarm, GraveSpider, SkeletonArcher, Knight, Bosse)
                if (asset == null || asset.GetComponent<EnemyBrain>() == null) continue;

                NetworkSetup.EditPrefab(path, root =>
                {
                    var no = NetworkSetup.Ensure<NetworkObject>(root);
                    no.AutoObjectParentSync = false;
                    no.SynchronizeTransform = true;

                    var nt = NetworkSetup.Ensure<NetworkTransform>(root);
                    nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
                    nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true; // Y: Sprung-Bogen der Bosse (baseOffset)
                    nt.SyncRotAngleX = false;
                    nt.SyncRotAngleY = true;
                    nt.SyncRotAngleZ = false;
                    nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                    nt.InLocalSpace = false;
                    nt.Interpolate = true;
                    nt.UseUnreliableDeltas = true; // viele Gegner: verlorene Pakete sollen nichts aufstauen
                    nt.UseHalfFloatPrecision = true;
                    nt.PositionThreshold = 0.01f;
                    nt.RotAngleThreshold = 1f;

                    NetworkSetup.Ensure<EnemyNet>(root);
                    EditorUtility.SetDirty(root);
                });
                count++;
            }
            Debug.Log($"[NetSetup] Gegner: {count} Prefabs mit NetworkObject/NetworkTransform/EnemyNet");
        }
    }
}
