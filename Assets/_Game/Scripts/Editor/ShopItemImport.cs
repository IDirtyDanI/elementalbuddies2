using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Import-Einstellungen für die Shop-Skins unter Models/Characters/Heroes2/Shop (art-src/heroes2/30_shop_items.py):
    // 11 Waffen (W2S_*, Konvention wie Heroes2/Weapons: Ursprung = Griff, Achse +Y, Kind-Transforms "Grip2"/"FxAnchor")
    // und 4 starre Zusatzteile (A2S_*, Ursprung = Befestigungspunkt am Knochen).
    // - wie die Heroes2-Waffen: kein Rig, keine Animation, Normals "Import" (flache Facetten), Datei-Skalierung
    // - Materialien werden nach Heroes2/Shop/Materials ausgelagert (gleichnamige wiederverwendet)
    // - Materialien mit "_Glow" im Namen leuchten (Emission = Grundfarbe × GlowIntensity)
    // Heroes2Import trifft wegen des Ordner-Präfixes auch Shop/ (dort ohne "/Weapons/" → Humanoid); darum läuft dieser
    // Postprocessor danach (GetPostprocessOrder) und erzwingt die Waffen-Einstellungen bei jedem Import.
    // Erzwingen/erneuern: Menü BuddyTD → Shop → Shop-Modelle importieren.
    public class ShopItemImport : AssetPostprocessor
    {
        public const string Dir = "Assets/_Game/Models/Characters/Heroes2/Shop/";
        public const string MaterialDir = Dir + "Materials/";
        public const float GlowIntensity = 2.5f;

        public override int GetPostprocessOrder() { return 100; }

        public static bool IsShopModel(string path)
        {
            return path.StartsWith(Dir) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
        }

        void OnPreprocessModel()
        {
            if (!IsShopModel(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            if (mi.importSettingsMissing) Configure(mi);
            else EnforceNoRig(mi);
        }

        public static void Configure(ModelImporter mi)
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
            mi.addCollider = false;
            EnforceNoRig(mi);
        }

        // Falls Heroes2Import (Ordner-Präfix) Humanoid gesetzt hat: zurück auf reine Requisite
        private static void EnforceNoRig(ModelImporter mi)
        {
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importNormals = ModelImporterNormals.Import;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var list = new List<string>();
            foreach (var p in imported)
                if (IsShopModel(p)) list.Add(p);
            if (list.Count == 0) return;
            EditorApplication.delayCall += () =>
            {
                bool any = false;
                foreach (var p in list) any |= ExtractMaterials(p);
                if (TuneMaterials() || any) AssetDatabase.SaveAssets();
            };
        }

        [MenuItem("BuddyTD/Shop/Shop-Modelle importieren")]
        public static void ImportAll()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Dir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsShopModel(path)) continue;
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                Configure(mi);
                mi.SaveAndReimport();
                ExtractMaterials(path);
                n++;
            }
            TuneMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("ShopItemImport: " + n + " Shop-Modelle eingerichtet.");
        }

        // Eingebettete Materialien als .mat auslagern; vorhandene gleichnamige werden wiederverwendet (Remap).
        // Bereits umgeleitete Materialien werden übersprungen (kein Endlos-Reimport).
        private static bool ExtractMaterials(string path)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return false;
            if (!AssetDatabase.IsValidFolder(MaterialDir.TrimEnd('/')))
                AssetDatabase.CreateFolder(Dir.TrimEnd('/'), "Materials");
            var map = mi.GetExternalObjectMap();
            bool changed = false;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var m = o as Material;
                if (m == null) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                if (map.TryGetValue(id, out var ext) && ext != null) continue;
                string target = MaterialDir + m.name + ".mat";
                if (File.Exists(target))
                    mi.AddRemap(id, AssetDatabase.LoadAssetAtPath<Material>(target));
                else
                {
                    string err = AssetDatabase.ExtractAsset(m, target);
                    if (!string.IsNullOrEmpty(err)) Debug.LogWarning("ShopItemImport: " + m.name + ": " + err);
                }
                changed = true;
            }
            if (changed)
            {
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            return changed;
        }

        // *_Glow leuchten in ihrer Grundfarbe (Bloom), alle anderen ohne Emission
        private static bool TuneMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialDir.TrimEnd('/'))) return false;
            bool changed = false;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir.TrimEnd('/') }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.HasProperty("_BaseColor") || !m.HasProperty("_EmissionColor")) continue;
                Color target = Color.black;
                bool glow = m.name.Contains("_Glow");
                if (glow)
                {
                    Color c = m.GetColor("_BaseColor");
                    target = new Color(c.r, c.g, c.b) * GlowIntensity;
                }
                if (m.GetColor("_EmissionColor") == target && m.IsKeywordEnabled("_EMISSION") == glow) continue;
                if (glow)
                {
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    m.DisableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }
                m.SetColor("_EmissionColor", target);
                EditorUtility.SetDirty(m);
                changed = true;
            }
            return changed;
        }
    }
}
