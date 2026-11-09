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

        // Requisiten der Champions ohne Rig (Totems, Geisterwolf des Schamanen): Models/Champions/<Klasse>/
        public const string PropDir = "Assets/_Game/Models/Champions/";
        public const string ShamanPropDir = PropDir + "Shaman/";
        public const string ShamanPropMaterialDir = ShamanPropDir + "Materials/";

        void OnPreprocessModel()
        {
            if (assetPath.StartsWith(PropDir))
            {
                var pi = (ModelImporter)assetImporter;
                if (pi.importSettingsMissing) ConfigureProp(pi);
                return;
            }
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

        // Requisite ohne Rig/Animation (Totem, Geisterwolf): Blender-Normals, Materialien wie die Heroes2-Modelle
        public static void ConfigureProp(ModelImporter mi)
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
            mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false;
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

        // Schleifen-Clips: Lauf-Animationen (Loco_Walk_F … Loco_Run_R), Trage-Haltung (Berserker_HoldIdle/HoldRun),
        // Glutsturm (Berserker_SpinLoop), Ansturm (Berserker_Charge), Flugphase (Berserker_InAir, Hero2_InAir) und Stand-Idle
        // (Hero2_Idle; „_Idle“ trifft nicht „_HoldIdle“); alle übrigen (Hiebe, Absprung/Landung, Schuss, Zauber …) laufen einmal
        public static bool IsLoopClip(string name)
        {
            return name.StartsWith("Loco_") || name.EndsWith("_HoldIdle") || name.EndsWith("_HoldRun")
                || name.EndsWith("_SpinLoop") || name.EndsWith("_Charge") || name.EndsWith("_InAir")
                || name.EndsWith("_Idle");
        }

        // Clip je Datei: Name = Dateiname, Wurzel (Hüfte) in die Pose gebacken (Rotation, Höhe, XZ – in-place, die Figur
        // bewegt der CharacterController). Loop Time + Loop Pose nur für Schleifen-Clips (IsLoopClip), sonst einmalig.
        public static void ConfigureAnimClips(ModelImporter mi, string path)
        {
            var defs = mi.defaultClipAnimations;
            if (defs == null || defs.Length == 0) return;
            var c = defs[0];
            c.name = Path.GetFileNameWithoutExtension(path);
            bool loop = IsLoopClip(c.name);
            c.loopTime = loop;
            c.loopPose = loop;
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

        // Nur die Schamanen-Assets einrichten (Figur, Totemstab, 5 Clips, Totems, Geisterwolf) – die übrigen Modelle
        // (auch Shop/) bleiben unberührt. Idempotent.
        public static readonly string[] ShamanClips = { "Shaman_Cast", "Shaman_Dash", "Shaman_PlantTotem", "Shaman_Wave", "Shaman_Summon" };

        [MenuItem("BuddyTD/Champions/Schamanen-Assets importieren")]
        public static void ImportShaman()
        {
            if (!AssetDatabase.IsValidFolder(MaterialDir.TrimEnd('/'))) AssetDatabase.CreateFolder(Dir.TrimEnd('/'), "Materials");
            foreach (var path in new[] { Dir + "ShamanChampion2.fbx", Dir + "Weapons/W2_TotemStaff.fbx" })
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { Debug.LogWarning("Heroes2Import: fehlt " + path); continue; }
                Configure(mi, path);
                mi.SaveAndReimport();
                ExtractMaterials(path);
            }
            foreach (var clip in ShamanClips)
            {
                string path = AnimDir + clip + ".fbx";
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { Debug.LogWarning("Heroes2Import: fehlt " + path); continue; }
                ConfigureAnim(mi);
                mi.SaveAndReimport();
                ConfigureAnimClips(mi, path);
                mi.SaveAndReimport();
            }
            if (!AssetDatabase.IsValidFolder(ShamanPropMaterialDir.TrimEnd('/'))) AssetDatabase.CreateFolder(ShamanPropDir.TrimEnd('/'), "Materials");
            foreach (var name in new[] { "Totem_War", "Totem_Stone", "Spirit_Wolf" })
            {
                string path = ShamanPropDir + name + ".fbx";
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { Debug.LogWarning("Heroes2Import: fehlt " + path); continue; }
                ConfigureProp(mi);
                mi.SaveAndReimport();
                ExtractMaterials(path, ShamanPropMaterialDir);
            }
            TuneMaterials();
            TuneShamanPropMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("Heroes2Import: Schamanen-Assets eingerichtet.");
        }

        // Leuchtende Teile ohne „Crystal“ im Namen: Geiststein des Stabs, Glutschale, Runen, heller Geisterkern
        private static readonly string[] GlowMaterials = { "W2_SpiritGlow", "Totem_Ember", "Totem_RuneGlow", "Spirit_GhostBright" };

        private static void TuneShamanPropMaterials()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { ShamanPropMaterialDir.TrimEnd('/') }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.HasProperty("_BaseColor")) continue;
                if (System.Array.IndexOf(GlowMaterials, m.name) >= 0) SetGlow(m, 1.8f);
                if (m.name == "Spirit_Ghost")
                {
                    MakeTransparent(m, 0.45f);
                    SetGlow(m, 0.6f);
                }
                if (m.name == "Spirit_GhostBright") MakeTransparent(m, 0.75f);
            }
        }

        private static void SetGlow(Material m, float intensity)
        {
            Color c = m.GetColor("_BaseColor");
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * intensity);
            EditorUtility.SetDirty(m);
        }

        // URP Lit: Surface Transparent, Blend Alpha, ohne Tiefe schreiben
        private static void MakeTransparent(Material m, float alpha)
        {
            Color c = m.GetColor("_BaseColor");
            c.a = alpha;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
        }

        // Eingebettete Materialien als .mat auslagern; vorhandene gleichnamige werden wiederverwendet (Remap)
        private static void ExtractMaterials(string path) { ExtractMaterials(path, MaterialDir); }

        private static void ExtractMaterials(string path, string materialDir)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var m = o as Material;
                if (m == null) continue;
                string target = materialDir + m.name + ".mat";
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
                if (m.name.Contains("Crystal") || System.Array.IndexOf(GlowMaterials, m.name) >= 0)
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
