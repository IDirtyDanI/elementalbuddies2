using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies.EditorTools
{
    // Eigener NavMesh-Agententyp „Ramme“ (Bekannte Probleme #134): Die Belagerungsramme ist 2,54 m breit, der
    // Humanoid-Typ (Radius 0,5) ließ sie an Mauerecken und Zäunen durch die Geometrie schneiden. Typ mit Radius 1,3 +
    // zweite NavMeshSurface „Ramme“ am Environment-Objekt, gebacken nach Assets/NavMesh-Ramme.asset.
    // Wird von RamBuilder (Agent im Prefab) und MerchantSetup.RebakeNavMesh (mitbacken) genutzt.
    public static class RamNavMesh
    {
        public const string AgentName = "Ramme";
        public const float AgentRadius = 1.3f;   // halbe Rumpfbreite 1,27 m
        public const float AgentHeight = 2f;     // wie Humanoid – Baumkronen über 2 m bleiben durchfahrbar
        private const string AssetPath = "Assets/NavMesh-Ramme.asset";

        [MenuItem("BuddyTD/Ramme/NavMesh backen")]
        public static void BakeMenu() => Debug.Log(Bake());

        // Agententyp anlegen bzw. Werte nachziehen; liefert die agentTypeID
        public static int EnsureAgentType()
        {
            int id = FindAgentType();
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
            var settings = so.FindProperty("m_Settings");
            var names = so.FindProperty("m_SettingNames");
            if (id == int.MinValue)
            {
                var created = NavMesh.CreateSettings(); // neue ID, Werte unten
                id = created.agentTypeID;
                so.Update();
                settings = so.FindProperty("m_Settings");
                names = so.FindProperty("m_SettingNames");
            }
            for (int i = 0; i < settings.arraySize; i++)
            {
                var s = settings.GetArrayElementAtIndex(i);
                if (s.FindPropertyRelative("agentTypeID").intValue != id) continue;
                s.FindPropertyRelative("agentRadius").floatValue = AgentRadius;
                s.FindPropertyRelative("agentHeight").floatValue = AgentHeight;
                s.FindPropertyRelative("agentSlope").floatValue = 45f;
                s.FindPropertyRelative("agentClimb").floatValue = 0.75f;
                s.FindPropertyRelative("minRegionArea").floatValue = 2f;
                while (names.arraySize <= i) names.InsertArrayElementAtIndex(names.arraySize);
                names.GetArrayElementAtIndex(i).stringValue = AgentName;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return id;
        }

        public static int FindAgentType()
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                var s = NavMesh.GetSettingsByIndex(i);
                if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == AgentName) return s.agentTypeID;
            }
            return int.MinValue;
        }

        // Zweite NavMeshSurface neben „Environment“ (gleiche Sammel-Einstellungen) backen und als Asset speichern
        public static string Bake()
        {
            int id = EnsureAgentType();
            NavMeshSurface env = null, ram = null;
            foreach (var srf in Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (srf.agentTypeID == id) ram = srf;
                else if (srf.name == "Environment") env = srf;
            }
            if (env == null) return "RamNavMesh: NavMeshSurface 'Environment' fehlt";
            if (ram == null)
            {
                ram = Undo.AddComponent<NavMeshSurface>(env.gameObject);
                ram.agentTypeID = id;
            }
            ram.collectObjects = env.collectObjects;
            ram.layerMask = env.layerMask;
            ram.useGeometry = env.useGeometry;
            ram.defaultArea = env.defaultArea;
            ram.center = env.center;
            ram.size = env.size;
            ram.overrideVoxelSize = false;
            ram.overrideTileSize = false;

            ram.BuildNavMesh();
            var built = ram.navMeshData;
            var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath);
            if (built != null && built != asset)
            {
                if (asset == null)
                {
                    AssetDatabase.CreateAsset(Object.Instantiate(built), AssetPath);
                    asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath);
                }
                else EditorUtility.CopySerialized(built, asset);
                ram.RemoveData();
                ram.navMeshData = asset;
                ram.AddData();
                EditorUtility.SetDirty(asset);
            }
            EditorUtility.SetDirty(ram);
            AssetDatabase.SaveAssets();
            return $"RamNavMesh: Typ „{AgentName}“ (ID {id}, Radius {AgentRadius}) gebacken → {AssetPath}";
        }
    }
}
