using System.IO;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Import-Einstellungen für die detaillierten Champion-Modelle unter Models/Characters/Heroes2 (6-Kopf-Proportionen,
    // Humanoid-Rig mit Fingern, 52 Knochen mit Unity-Humanoid-Namen). Beim Erstimport automatisch (AssetPostprocessor),
    // erzwingen/erneuern über Menü BuddyTD → Champions → Heroes2 importieren:
    // - Figuren: Rig Humanoid, Avatar je Modell (identisches Rig → identische Avatare, Finger automatisch gemappt)
    // - Waffen (Unterordner Weapons): kein Rig
    // - Normals „Import“: die Blender-Normals (flache Facetten des Low-Poly-Stils) bleiben erhalten
    // - Materialien werden nach Heroes2/Materials extrahiert; gleichnamige (Hero2_Skin …) teilen sich alle Modelle
    public class Heroes2Import : AssetPostprocessor
    {
        public const string Dir = "Assets/_Game/Models/Characters/Heroes2/";
        public const string MaterialDir = Dir + "Materials/";

        // Eigene Blender-Clips (nur Armature, eine Take je Datei, z. B. Knight2_Slash1.fbx aus art-src/heroes2/18_export_anims.py)
        public const string AnimDir = "Assets/_Game/Animations/Heroes2/";
        public const string AvatarSource = Dir + "KnightChampion2.fbx";

        void OnPreprocessModel()
        {
            if (assetPath.StartsWith(AnimDir))
            {
                var ai = (ModelImporter)assetImporter;
                if (ai.importSettingsMissing) ConfigureAnim(ai);
                return;
            }
            if (!assetPath.StartsWith(Dir)) return;
            var mi = (ModelImporter)assetImporter;
            if (!mi.importSettingsMissing) return; // nur Erstimport – spätere Handanpassungen bleiben erhalten
            Configure(mi, assetPath);
        }

        public static bool IsWeapon(string path) { return path.Contains("/Weapons/"); }

        public static void Configure(ModelImporter mi, string path)
        {
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialSearch = ModelImporterMaterialSearch.Local;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.importAnimation = false;
            if (IsWeapon(path))
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.addCollider = false;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.optimizeGameObjects = false; // Waffen hängen zur Laufzeit an Hand-Knochen
            }
        }

        // Animations-FBX: Humanoid mit dem Avatar von KnightChampion2 (identisches Hero2_Rig), keine Materialien/Meshes
        public static void ConfigureAnim(ModelImporter mi)
        {
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = true;
            mi.animationType = ModelImporterAnimationType.Human;
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarSource);
            if (avatar != null)
            {
                mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                mi.sourceAvatar = avatar;
            }
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        // Clip je Datei: Name = Dateiname, kein Loop, Wurzel (Hüfte) fest – die Clips bewegen nur den Oberkörper
        public static void ConfigureAnimClips(ModelImporter mi, string path)
        {
            var defs = mi.defaultClipAnimations;
            if (defs == null || defs.Length == 0) return;
            var c = defs[0];
            c.name = Path.GetFileNameWithoutExtension(path);
            c.loopTime = false;
            c.loopPose = false;
            c.lockRootRotation = true;
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = true;
            c.keepOriginalPositionY = true;
            c.keepOriginalPositionXZ = true;
            mi.clipAnimations = new[] { c };
        }

        [MenuItem("BuddyTD/Champions/Heroes2-Animationen importieren")]
        public static void ImportAnims()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { AnimDir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                ConfigureAnim(mi);
                mi.SaveAndReimport();
                ConfigureAnimClips(mi, path);
                mi.SaveAndReimport();
                n++;
            }
            Debug.Log("Heroes2Import: " + n + " Animations-FBX eingerichtet.");
        }

        [MenuItem("BuddyTD/Champions/Heroes2 importieren")]
        public static void ImportAll()
        {
            if (!AssetDatabase.IsValidFolder(MaterialDir.TrimEnd('/'))) AssetDatabase.CreateFolder(Dir.TrimEnd('/'), "Materials");
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Dir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                Configure(mi, path);
                mi.SaveAndReimport();
                ExtractMaterials(path);
                n++;
            }
            TuneMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("Heroes2Import: " + n + " Modelle eingerichtet.");
        }

        // Eingebettete Materialien als .mat auslagern; vorhandene gleichnamige werden wiederverwendet (Remap)
        private static void ExtractMaterials(string path)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var m = o as Material;
                if (m == null) continue;
                string target = MaterialDir + m.name + ".mat";
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                if (File.Exists(target))
                    mi.AddRemap(id, AssetDatabase.LoadAssetAtPath<Material>(target));
                else
                    AssetDatabase.ExtractAsset(m, target);
                changed = true;
            }
            if (changed)
            {
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        // Nachbearbeitung: Kristalle leuchten (Bloom), Metalle etwas glänzender
        private static void TuneMaterials()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir.TrimEnd('/') }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.HasProperty("_BaseColor")) continue;
                if (m.name.Contains("Crystal"))
                {
                    Color c = m.GetColor("_BaseColor");
                    float intensity = m.name.StartsWith("W2_") ? 2f : 1.2f; // Stabkristall kräftig, Kleidungs-Steine dezent
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    m.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * intensity);
                    EditorUtility.SetDirty(m);
                }
            }
        }
    }
}
