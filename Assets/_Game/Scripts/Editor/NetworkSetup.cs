using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElementalBuddies.EditorTools
{
    // Markiert einen statischen, parameterlosen Einrichtungsschritt für den Mehrspieler-Modus.
    // "BuddyTD/Netzwerk/Einrichten" ruft alle Schritte nach Order auf. Schritte müssen idempotent sein.
    // Konvention: Prefab-Schritte Order 100–199, Szenen-Schritte 200–299 (test.unity ist dann bereits geöffnet
    // und wird am Ende gespeichert), Abschluss (Prefab-Liste, NetworkManager) läuft immer zuletzt.
    [AttributeUsage(AttributeTargets.Method)]
    public class NetSetupStepAttribute : Attribute
    {
        public readonly int Order;
        public NetSetupStepAttribute(int order) { Order = order; }
    }

    public static class NetworkSetup
    {
        public const string PrefabRoot = "Assets/_Game/Prefabs";
        public const string NetFolder = "Assets/_Game/Net";
        public const string PrefabListPath = NetFolder + "/NetworkPrefabs.asset";
        public const string NetPlayerPath = PrefabRoot + "/Net/NetPlayer.prefab";
        public const string ManagerPath = "Assets/Resources/Net/NetworkManager.prefab";
        public const string PlayerPrefabPath = PrefabRoot + "/Player.prefab";
        public const string GameScenePath = "Assets/test.unity";

        [MenuItem("BuddyTD/Netzwerk/Einrichten (alles)")]
        public static void SetupAll()
        {
            var steps = TypeCache.GetMethodsWithAttribute<NetSetupStepAttribute>()
                .Where(m => m.IsStatic && m.GetParameters().Length == 0)
                .OrderBy(m => m.GetCustomAttribute<NetSetupStepAttribute>().Order)
                .ToList();

            Scene scene = default;
            bool sceneOpened = false;
            foreach (var m in steps)
            {
                int order = m.GetCustomAttribute<NetSetupStepAttribute>().Order;
                if (order >= 200 && !sceneOpened)
                {
                    scene = OpenGameScene();
                    sceneOpened = true;
                }
                Debug.Log($"[NetSetup] {order}: {m.DeclaringType.Name}.{m.Name}");
                try { m.Invoke(null, null); }
                catch (TargetInvocationException e) { Debug.LogError($"[NetSetup] {m.Name} fehlgeschlagen: {e.InnerException}"); }
            }
            if (sceneOpened)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            BuildPrefabListAndManager();
            AssetDatabase.SaveAssets();
            Debug.Log("[NetSetup] fertig");
        }

        public static Scene OpenGameScene()
        {
            var s = SceneManager.GetSceneByPath(GameScenePath);
            if (s.IsValid() && s.isLoaded) return s;
            return EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        }

        // ---------------- Hilfen für Schritte ----------------

        // Fügt einer Prefab-Wurzel Komponenten hinzu (falls fehlend) und speichert
        public static GameObject EditPrefab(string path, Action<GameObject> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogError("[NetSetup] Prefab fehlt: " + path); return null; }
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        public static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        // Findet oder erzeugt ein Wurzelobjekt in der aktiven Szene
        public static GameObject EnsureSceneObject(string name)
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == name) return r;
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        // ---------------- Kern-Schritte ----------------

        [NetSetupStep(100)]
        public static void SetupCorePrefabs()
        {
            EnsureFolder(PrefabRoot + "/Net");
            // NetPlayer (Netcode-Spielerobjekt)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath) == null)
            {
                var go = new GameObject("NetPlayer");
                go.AddComponent<NetworkObject>();
                go.AddComponent<NetPlayer>();
                PrefabUtility.SaveAsPrefabAsset(go, NetPlayerPath);
                UnityEngine.Object.DestroyImmediate(go);
            }
            // Spielfigur: NetworkObject + PlayerAvatar
            EditPrefab(PlayerPrefabPath, root =>
            {
                Ensure<NetworkObject>(root);
                Ensure<PlayerAvatar>(root);
            });
        }

        [NetSetupStep(200)]
        public static void SetupCoreScene()
        {
            var ng = EnsureSceneObject("NetGame");
            Ensure<NetworkObject>(ng);
            Ensure<NetGame>(ng);

            var boot = EnsureSceneObject("NetBootstrap");
            var nb = Ensure<NetBootstrap>(boot);
            nb.AvatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            EditorUtility.SetDirty(nb);
        }

        // Prefab-Liste aus allen Prefabs mit NetworkObject unter Assets/_Game/Prefabs + NetworkManager-Prefab
        [MenuItem("BuddyTD/Netzwerk/Prefab-Liste aktualisieren")]
        public static void BuildPrefabListAndManager()
        {
            EnsureFolder(NetFolder);
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }
            var netPlayer = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath);
            // vorhandene Einträge leeren
            foreach (var p in list.PrefabList.ToList()) list.Remove(p);
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go == netPlayer || go.GetComponent<NetworkObject>() == null) continue;
                list.Add(new NetworkPrefab { Prefab = go });
                count++;
            }
            EditorUtility.SetDirty(list);

            EnsureFolder("Assets/Resources/Net");
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPath);
            GameObject root = existing != null ? PrefabUtility.LoadPrefabContents(ManagerPath) : new GameObject("NetworkManager");
            try
            {
                var nm = Ensure<NetworkManager>(root);
                var utp = Ensure<UnityTransport>(root);
                // Große Wellen erzeugen viele Positions-Updates pro Tick → größere Sende-Warteschlange
                utp.MaxPacketQueueSize = 1024;
                nm.NetworkConfig ??= new NetworkConfig();
                nm.NetworkConfig.NetworkTransport = utp;
                nm.NetworkConfig.PlayerPrefab = netPlayer;
                nm.NetworkConfig.EnableSceneManagement = true;
                nm.NetworkConfig.ConnectionApproval = true;
                nm.NetworkConfig.ForceSamePrefabs = true;
                nm.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { list };
                PrefabUtility.SaveAsPrefabAsset(root, ManagerPath);
            }
            finally
            {
                if (existing != null) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[NetSetup] Prefab-Liste: {count} Netzwerk-Prefabs, NetworkManager unter {ManagerPath}");
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
