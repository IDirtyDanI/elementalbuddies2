using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElementalBuddies.EditorTools
{
    // Map-bezogene Pfade für Editor-Werkzeuge (Walddorf-Plan, Paket P0):
    // - Stadtszene als eine Konstante statt verstreuter "Assets/test.unity"-Literale
    // - NavMesh-Assets je Szene: die Stadt behält ihre alten Dateien (Assets/NavMesh-*.asset, GUIDs unverändert),
    //   jede andere Szene bekommt <Szenenordner>/<Szenenname>/NavMesh-*.asset – ein Bake dort überschreibt die Stadt nie.
    // - Spielszenen aller MapDefinitionSO-Assets (Build Settings)
    public static class MapEditorUtil
    {
        public const string CityScenePath = "Assets/test.unity";
        public const string EnvironmentNavMeshFile = "NavMesh-Environment.asset";
        public const string RamNavMeshFile = "NavMesh-Ramme.asset";

        // Ziel-Asset eines NavMesh-Bakes für die Szene (legt den Ordner bei Bedarf an)
        public static string NavMeshAssetPath(Scene scene, string fileName)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
                throw new System.InvalidOperationException("NavMesh-Pfad: Szene ist nicht gespeichert – erst speichern, dann backen.");
            if (scene.path == CityScenePath) return "Assets/" + fileName; // Stadt: alte Pfade/GUIDs
            string dir = Path.GetDirectoryName(scene.path).Replace('\\', '/');
            string folder = dir + "/" + scene.name;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(dir, scene.name);
            return folder + "/" + fileName;
        }

        // Alle Map-Definitionen im Projekt, sortiert nach Order
        public static List<MapDefinitionSO> AllMaps()
        {
            var list = new List<MapDefinitionSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(MapDefinitionSO)))
            {
                var m = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && !string.IsNullOrEmpty(m.Id)) list.Add(m);
            }
            list.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        // Spielszenen für die Build Settings: alle Maps, deren Szene existiert (ohne Maps: die Stadt)
        public static List<string> GameScenePaths()
        {
            var paths = new List<string>();
            foreach (var m in AllMaps())
            {
                if (string.IsNullOrEmpty(m.ScenePath) || paths.Contains(m.ScenePath)) continue;
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(m.ScenePath) == null)
                {
                    Debug.LogWarning($"MapEditorUtil: Szene der Map „{m.Id}“ fehlt ({m.ScenePath}) – nicht in die Build Settings.");
                    continue;
                }
                paths.Add(m.ScenePath);
            }
            if (paths.Count == 0) paths.Add(CityScenePath);
            return paths;
        }
    }
}
