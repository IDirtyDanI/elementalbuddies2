using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ElementalBuddies.EditorTools
{
    // Belagerungsramme (Wellen-Ereignis W20) mit eigenem Blender-Modell statt des skalierten Skelett-Ritters.
    // Alle Schritte idempotent, beliebig oft ausführbar (Menü: BuddyTD → Ramme → Prefab bauen, im Code RamBuilder.Build()):
    //  1. Import-Einstellungen von Ramme.fbx / Ramme_Truemmer.fbx (1 Blender-m = 1 Unity-m, keine Collider/Animation),
    //     Material-Slots → gemeinsame Town_*.mat bzw. neue Ram_*.mat (Models/Siege/Materials, werden angelegt)
    //  2. Config ScriptableObjects/Configs/Ram.asset (einmalig aus Knight.asset kopiert; Werte bleiben danach erhalten)
    //  3. Prefab Prefabs/Ram.prefab: Wurzel wie Knight.prefab (Layer, Tag, EnemyBrain-Werte, NetworkObject,
    //     NetworkTransform, EnemyNet), BoxCollider ums Modell, Kind „Visual“ mit Modell + RamVisual.
    //     Pivot wie bei allen Gegnern baseOffset (1) über dem Boden; Visual auf 1/1,7, weil die Elite-Eigenschaft
    //     „Ramme“ die Wurzel ×1,7 skaliert → im Spiel hat das Modell seine echte Größe.
    //  4. WaveManager.RamConfig in der geöffneten Spielszene (nur markiert, nicht gespeichert)
    //  5. Netzwerk-Prefab-Liste neu (NetworkSetup), Icon für Statistik/Vorschau (PacingSetup.RenderIcon)
    public static class RamBuilder
    {
        private const string ModelDir = "Assets/_Game/Models/Siege/";
        private const string MatDir = ModelDir + "Materials/";
        private const string TownMatDir = "Assets/_Game/Models/Town/Materials/";
        private const string ModelPath = ModelDir + "Ramme.fbx";
        private const string DebrisPath = ModelDir + "Ramme_Truemmer.fbx";
        private const string KnightPrefab = "Assets/_Game/Prefabs/Knight.prefab";
        private const string KnightConfig = "Assets/ScriptableObjects/Configs/Knight.asset";
        public const string RamPrefab = "Assets/_Game/Prefabs/Ram.prefab";
        public const string RamConfig = "Assets/ScriptableObjects/Configs/Ram.asset";
        private const string DustVfx = "Assets/_Game/VFX/Prefabs/Champions/VFX_RollDust.prefab";
        private const string IconPath = "Assets/_Game/UI/Icons/icon_enemy_ram.png";

        // Soll-Maße des Modells (m): Länge (Z), Breite (X), Höhe (Y)
        private static readonly Vector3 NominalSize = new Vector3(2.4f, 3f, 4.6f);
        private const float PivotHeight = 1f; // NavMeshAgent.baseOffset (lokal, wird mit der Wurzel skaliert)

        private static readonly string[] TownSlots = { "Town_Wood", "Town_Timber", "Town_Metal", "Town_Rope" };

        [MenuItem("BuddyTD/Ramme/Prefab bauen")]
        private static void BuildMenu() => Build();

        // Gibt das Protokoll zurück (steht auch in der Konsole)
        public static string Build(bool renderIcon = true)
        {
            var sb = new StringBuilder("RamBuilder:");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.Append(" nur im Edit-Modus.");
                Debug.LogWarning(sb.ToString());
                return sb.ToString();
            }
            NetworkSetup.EnsureFolder(MatDir.TrimEnd('/'));

            var mats = EnsureMaterials(sb);
            if (!ImportModel(ModelPath, mats, sb))
            {
                sb.Append("\n  Abbruch: " + ModelPath + " fehlt.");
                Debug.LogWarning(sb.ToString());
                return sb.ToString();
            }
            bool debris = ImportModel(DebrisPath, mats, sb);
            if (!debris) sb.Append("\n  Hinweis: " + DebrisPath + " fehlt – Ramme ohne Trümmer.");

            var config = EnsureConfig(sb);
            var prefab = BuildPrefab(config, sb);
            if (prefab == null)
            {
                Debug.LogWarning(sb.ToString());
                return sb.ToString();
            }
            config.Prefab = prefab;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            WireScene(config, sb);
            NetworkSetup.BuildPrefabListAndManager();
            sb.Append("\n  Netzwerk-Prefab-Liste aktualisiert.");
            if (renderIcon) RenderIcon(config, sb);

            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        // ---------------- Ork-Rammbock (Walddorf, Etappe 3, 2026-10-16) ----------------
        // Eigenes Modell aus art-src/ram/ork.py (gleiche Teilenamen/Pivots/Maße wie Ramme.fbx → RamVisual unverändert).
        // Aktualisiert das bestehende Prefab Prefabs/Enemies/Forest/Ork_Rammbock (GUID → Netz-Hash bleibt): Wurzel (Agent,
        // Collider, EnemyBrain inkl. NexusAttackRange, Netz) bleibt 1:1 wie die Untoten-Ramme (Balance), nur die Kinder von
        // „Visual“ werden ersetzt und RamVisual neu verdrahtet. Materialien: Dorfpalette (Models/Village/Materials, angelegt von
        // VillageSetup) + OrkRam_* (hier angelegt, Farben = ORK_PALETTE in ork.py). Icon = Blender-Render ork_ram_codex.png.
        public const string OrkPrefab = "Assets/_Game/Prefabs/Enemies/Forest/Ork_Rammbock.prefab";
        public const string OrkConfig = "Assets/ScriptableObjects/Configs/Forest/Ork_Rammbock.asset";
        private const string OrkModelPath = ModelDir + "Ork_Rammbock.fbx";
        private const string OrkDebrisPath = ModelDir + "Ork_Rammbock_Truemmer.fbx";
        private const string OrkIconPath = "Assets/_Game/UI/Icons/icon_enemy_ork_rammbock.png";
        private const string OrkBlenderIcon = "../art-src/ram/ork/renders/ork_ram_codex.png";
        private static readonly string[] OrkVillageSlots =
            { "Village_BarkLog", "Village_BarkLogDark", "Village_LogCut", "Village_Timber", "Village_Wood", "Village_Rope", "Village_Leather", "Village_Flame" };

        [MenuItem("BuddyTD/Ramme/Ork-Rammbock bauen (Walddorf)")]
        private static void BuildOrkMenu() => Debug.Log(BuildOrk());

        public static string BuildOrk(bool renderIcon = true)
        {
            var sb = new StringBuilder("RamBuilder (Ork-Rammbock):");
            if (EditorApplication.isPlayingOrWillChangePlaymode) { sb.Append(" nur im Edit-Modus."); return sb.ToString(); }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(OrkPrefab) == null) { sb.Append("\n  Abbruch: " + OrkPrefab + " fehlt (erst „Dorf-Gegner bauen“)."); return sb.ToString(); }
            NetworkSetup.EnsureFolder(MatDir.TrimEnd('/'));
            var mats = new Dictionary<string, Material>();
            foreach (var n in OrkVillageSlots)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(VillageSetup.VillageMaterialFolder + "/" + n + ".mat");
                if (m != null) mats[n] = m; else sb.Append($"\n  Warnung: {n}.mat fehlt (VillageSetup)");
            }
            mats["OrkRam_Hide"] = LitMaterial("OrkRam_Hide", Hex("7a5a3e"), 0.12f, null);
            mats["OrkRam_HideDark"] = LitMaterial("OrkRam_HideDark", Hex("5a4232"), 0.12f, null);
            mats["OrkRam_Fur"] = LitMaterial("OrkRam_Fur", Hex("958670"), 0.08f, null);
            mats["OrkRam_Iron"] = LitMaterial("OrkRam_Iron", Hex("45423f"), 0.35f, null);
            mats["OrkRam_Rust"] = LitMaterial("OrkRam_Rust", Hex("8a5a3c"), 0.2f, null);
            mats["OrkRam_Bone"] = LitMaterial("OrkRam_Bone", Hex("ddd0b0"), 0.15f, null);
            mats["OrkRam_Cloth"] = LitMaterial("OrkRam_Cloth", Hex("7d2a22"), 0.1f, null);
            mats["OrkRam_Paint"] = LitMaterial("OrkRam_Paint", Hex("d8d0b8"), 0.1f, null);
            Color glow = Hex("ffd24a");
            mats["OrkRam_Glow"] = LitMaterial("OrkRam_Glow", glow, 0.3f, glow * 2.2f);
            if (!ImportModel(OrkModelPath, mats, sb)) { sb.Append("\n  Abbruch: " + OrkModelPath + " fehlt (art-src/ram/ork.py -- export)."); return sb.ToString(); }
            bool debris = ImportModel(OrkDebrisPath, mats, sb);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(OrkModelPath);

            var root = PrefabUtility.LoadPrefabContents(OrkPrefab);
            try
            {
                var visual = root.transform.Find("Visual");
                if (visual == null) { sb.Append("\n  Abbruch: Kind „Visual“ fehlt."); return sb.ToString(); }
                for (int i = visual.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visual.GetChild(i).gameObject);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
                inst.name = "Ramme";
                if (TryLocalBounds(inst.transform, visual, null, out Bounds mb))
                    sb.Append($"\n  Modell: {mb.size.z:0.00} m lang, {mb.size.x:0.00} m breit, {mb.size.y:0.00} m hoch (Soll {NominalSize.z}/{NominalSize.x}/{NominalSize.y}), Boden bei y={mb.min.y:0.00}");
                var rv = visual.GetComponent<RamVisual>();
                if (rv == null) rv = visual.gameObject.AddComponent<RamVisual>();
                rv.Model = inst.transform;
                var wheels = new List<Transform>();
                foreach (var n in new[] { "Rad_VL", "Rad_VR", "Rad_HL", "Rad_HR" })
                {
                    var w = RamVisual.Find(inst.transform, n);
                    if (w != null) wheels.Add(w); else sb.Append($"\n  Warnung: Rad „{n}“ fehlt");
                }
                rv.Wheels = wheels.ToArray();
                rv.Beam = RamVisual.Find(inst.transform, "Rammbalken");
                if (rv.Beam == null) sb.Append("\n  Warnung: „Rammbalken“ fehlt – kein Rammstoß");
                if (wheels.Count > 0 && TryLocalBounds(wheels[0], visual, null, out Bounds wb))
                    rv.WheelRadius = Mathf.Max(0.1f, Mathf.Max(wb.size.y, wb.size.z) * 0.5f);
                rv.DebrisPrefab = debris ? AssetDatabase.LoadAssetAtPath<GameObject>(OrkDebrisPath) : null;
                if (rv.ImpactVfx == null) rv.ImpactVfx = AssetDatabase.LoadAssetAtPath<GameObject>(DustVfx);
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(OrkConfig);
                var brain = root.GetComponent<EnemyBrain>();
                if (brain != null && cfg != null) brain.Config = cfg;
                PrefabUtility.SaveAsPrefabAsset(root, OrkPrefab);
                ForestEnemyBuilder.MarkOwnBuilder(OrkPrefab); // „Dorf-Gegner bauen“ überschreibt den Rammbock nicht mehr (#405)
                var box = root.GetComponent<BoxCollider>();
                sb.Append($"\n  Prefab: {OrkPrefab} aktualisiert (GUID {AssetDatabase.AssetPathToGUID(OrkPrefab)}), Wurzel unverändert" +
                          (box != null ? $" (Collider {box.size.x:0.00}×{box.size.y:0.00}×{box.size.z:0.00} lokal)" : "") +
                          $", Rad-Radius {rv.WheelRadius:0.00} m, {wheels.Count} Räder, Trümmer {(rv.DebrisPrefab != null ? "ja" : "nein")}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (renderIcon && File.Exists(OrkBlenderIcon))
            {
                File.Copy(OrkBlenderIcon, OrkIconPath, true);
                AssetDatabase.ImportAsset(OrkIconPath, ImportAssetOptions.ForceUpdate);
                var imp = AssetImporter.GetAtPath(OrkIconPath) as TextureImporter;
                if (imp != null)
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.alphaIsTransparency = true;
                    imp.mipmapEnabled = false;
                    imp.SaveAndReimport();
                }
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(OrkConfig);
                if (cfg != null)
                {
                    var icon = AssetDatabase.LoadAssetAtPath<Sprite>(OrkIconPath);
                    if (icon != null && cfg.Icon != icon) { cfg.Icon = icon; EditorUtility.SetDirty(cfg); }
                }
                sb.Append("\n  Icon: " + OrkIconPath + " (Blender-Render)");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // ---------------- 1. Materialien & Import ----------------

        private static Dictionary<string, Material> EnsureMaterials(StringBuilder sb)
        {
            var mats = new Dictionary<string, Material>();
            foreach (var n in TownSlots)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(TownMatDir + n + ".mat");
                if (m != null) mats[n] = m;
                else sb.Append($"\n  Warnung: {TownMatDir}{n}.mat fehlt");
            }
            mats["Ram_Hide"] = LitMaterial("Ram_Hide", Hex("6e4b3a"), 0.15f, null); // heller als im Blender-Entwurf (3b2b26): nachts sonst ein schwarzer Kasten
            mats["Ram_Bone"] = LitMaterial("Ram_Bone", Hex("d8cfb8"), 0.15f, null);
            Color glow = Hex("6dff9e");
            mats["Ram_Glow"] = LitMaterial("Ram_Glow", glow, 0.3f, glow * 2.2f);
            return mats;
        }

        // URP-Lit wie Town_Wood (opak, matt); emission != null → _EMISSION an
        private static Material LitMaterial(string name, Color color, float smooth, Color? emission)
        {
            string p = MatDir + name + ".mat";
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(m, p);
            }
            if (lit != null && m.shader != lit) m.shader = lit;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;

        // Wie VegetationSetup.ImportSettings: statisches Prop, Normalen aus der FBX, Slots per Name umgeleitet
        private static bool ImportModel(string path, Dictionary<string, Material> mats, StringBuilder sb)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null && File.Exists(path))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                imp = AssetImporter.GetAtPath(path) as ModelImporter;
            }
            if (imp == null) return false;

            bool changed = false;
            void Set<T>(T cur, T val, System.Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(cur, val)) { apply(val); changed = true; } }
            Set(imp.useFileScale, true, v => imp.useFileScale = v);
            Set(imp.addCollider, false, v => imp.addCollider = v);
            Set(imp.importNormals, ModelImporterNormals.Import, v => imp.importNormals = v);
            Set(imp.isReadable, false, v => imp.isReadable = v);
            Set(imp.importAnimation, false, v => imp.importAnimation = v);
            Set(imp.animationType, ModelImporterAnimationType.None, v => imp.animationType = v);
            Set(imp.importCameras, false, v => imp.importCameras = v);
            Set(imp.importLights, false, v => imp.importLights = v);
            Set(imp.importBlendShapes, false, v => imp.importBlendShapes = v);
            Set(imp.importVisibility, false, v => imp.importVisibility = v);
            Set(imp.materialImportMode, ModelImporterMaterialImportMode.ImportStandard, v => imp.materialImportMode = v);
            Set(imp.materialLocation, ModelImporterMaterialLocation.InPrefab, v => imp.materialLocation = v);
            if (changed) imp.SaveAndReimport();

            bool remapped = false;
            var map = imp.GetExternalObjectMap();
            var slots = new List<string>();
            foreach (var id in SourceMaterials(imp, path))
            {
                string slot = Regex.Replace(id.name, @"\.\d+$", "");
                slots.Add(slot);
                if (!mats.TryGetValue(slot, out var mat))
                {
                    sb.Append($"\n  Warnung: {Path.GetFileName(path)}: unbekannter Material-Slot „{id.name}“ (bleibt eingebettet)");
                    continue;
                }
                if (map.TryGetValue(id, out var cur) && cur == mat) continue;
                imp.AddRemap(id, mat);
                remapped = true;
            }
            if (remapped) imp.SaveAndReimport();
            sb.Append($"\n  {Path.GetFileName(path)}: Slots {string.Join(", ", slots)}");
            return true;
        }

        // Material-Slots der FBX: bereits umgeleitete + eingebettete
        private static List<AssetImporter.SourceAssetIdentifier> SourceMaterials(ModelImporter imp, string path)
        {
            var ids = new List<AssetImporter.SourceAssetIdentifier>();
            foreach (var kv in imp.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material)) ids.Add(kv.Key);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Material m && !ids.Exists(i => i.name == m.name)) ids.Add(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name));
            return ids;
        }

        // ---------------- 2. Config ----------------

        private static EnemyConfigSO EnsureConfig(StringBuilder sb)
        {
            var c = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(RamConfig);
            if (c == null)
            {
                // Einmalig alle Werte vom Ritter (bisherige Ramme) übernehmen: Leben, Tempo, Schaden, Rüstung, Kopfgeld …
                var knight = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(KnightConfig);
                c = knight != null ? Object.Instantiate(knight) : ScriptableObject.CreateInstance<EnemyConfigSO>();
                c.name = "Ram";
                AssetDatabase.CreateAsset(c, RamConfig);
                sb.Append("\n  Config angelegt (Werte aus Knight.asset): " + RamConfig);
            }
            c.DisplayName = EliteInfo.Name(EliteAffix.Ram);
            c.PreviewHint = "Marschiert stur zum Nexus – aufhalten!";
            c.VisualScale = 1f;
            c.IsBoss = false;
            c.ControlResistance = 0.4f; // zwischen Normalgegnern (0) und Bossen (0,6): Stun/Frost/Slow ×0,6
            c.ProjectilePrefab = null;
            EditorUtility.SetDirty(c);
            return c;
        }

        // ---------------- 3. Prefab ----------------

        private static GameObject BuildPrefab(EnemyConfigSO config, StringBuilder sb)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) { sb.Append("\n  Abbruch: Modell nicht ladbar."); return null; }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(KnightPrefab) == null) { sb.Append("\n  Abbruch: " + KnightPrefab + " fehlt."); return null; }

            // Wurzel = Kopie von Knight.prefab (Layer, Tag, EnemyBrain-Werte inkl. HP-Leiste, Netz-Komponenten)
            var root = PrefabUtility.LoadPrefabContents(KnightPrefab);
            try
            {
                root.name = "Ram";
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                foreach (var col in root.GetComponents<Collider>()) Object.DestroyImmediate(col);

                float elite = EliteInfo.Scale(EliteAffix.Ram);
                var visual = new GameObject("Visual");
                visual.tag = root.tag; // wie beim Ritter
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, -PivotHeight, 0f);
                visual.transform.localScale = Vector3.one / elite;

                // Modell-Wurzel behält ihre Import-Transformation (Achsen-Korrektur der FBX)
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
                inst.name = "Ramme";

                // Maße im Visual-Raum (= echte Größe im Spiel) prüfen
                var beam = RamVisual.Find(inst.transform, "Rammbalken");
                if (!TryLocalBounds(inst.transform, visual.transform, null, out Bounds mb))
                {
                    sb.Append("\n  Abbruch: Modell ohne Meshes.");
                    return null;
                }
                sb.Append($"\n  Modell: {mb.size.z:0.00} m lang, {mb.size.x:0.00} m breit, {mb.size.y:0.00} m hoch (Soll {NominalSize.z}/{NominalSize.x}/{NominalSize.y}), Boden bei y={mb.min.y:0.00}, Mitte z={mb.center.z:0.00}");
                if (Mathf.Abs(mb.size.z / NominalSize.z - 1f) > 0.25f || Mathf.Abs(mb.size.y / NominalSize.y - 1f) > 0.25f)
                    sb.Append("\n  Warnung: Modellmaße weichen > 25 % vom Soll ab (Einheiten/Achsen der FBX prüfen).");
                if (mb.size.x > mb.size.z * 1.2f)
                    sb.Append("\n  Warnung: Modell ist breiter als lang – zeigt +Z wirklich nach vorn?");

                // Collider: Aufbau + Räder (ohne den vorstehenden Balken), im Wurzel-Raum (wird mit der Elite ×1,7 skaliert)
                if (!TryLocalBounds(inst.transform, root.transform, beam, out Bounds rb)) TryLocalBounds(inst.transform, root.transform, null, out rb);
                var box = root.AddComponent<BoxCollider>();
                box.isTrigger = false;
                box.center = rb.center;
                box.size = rb.size;

                var agent = root.GetComponent<NavMeshAgent>();
                // Eigener Agententyp „Ramme“ (Radius 1,3 m, RamNavMesh) – mit Humanoid (0,5) schnitt der 2,54 m breite
                // Rumpf an Ecken durch Mauern und Zäune (#134). radius ist lokal und wird ×1,7 skaliert → ~1,3 m.
                agent.agentTypeID = RamNavMesh.EnsureAgentType();
                agent.radius = RamNavMesh.AgentRadius / EliteInfo.Scale(EliteAffix.Ram);
                agent.height = 2f;
                agent.baseOffset = PivotHeight;
                agent.avoidancePriority = 30;

                // Ramme hält vor dem Nexus an (EnemyBrain.HandleMovement), Reichweite = Bug (inkl. Rammkopf) + Puffer, in Welt-m
                TryLocalBounds(inst.transform, root.transform, null, out Bounds all);
                var brain = root.GetComponent<EnemyBrain>();
                brain.Config = config;
                brain.NexusAttackRange = Mathf.Max(2.5f, all.max.z * elite + 0.4f);

                var rv = visual.AddComponent<RamVisual>();
                rv.Model = inst.transform;
                var wheels = new List<Transform>();
                foreach (var n in new[] { "Rad_VL", "Rad_VR", "Rad_HL", "Rad_HR" })
                {
                    var w = RamVisual.Find(inst.transform, n);
                    if (w != null) wheels.Add(w); else sb.Append($"\n  Warnung: Rad „{n}“ fehlt");
                }
                rv.Wheels = wheels.ToArray();
                rv.Beam = beam;
                if (beam == null) sb.Append("\n  Warnung: „Rammbalken“ fehlt – kein Rammstoß");
                if (wheels.Count > 0 && TryLocalBounds(wheels[0], visual.transform, null, out Bounds wb))
                    rv.WheelRadius = Mathf.Max(0.1f, Mathf.Max(wb.size.y, wb.size.z) * 0.5f);
                rv.DebrisPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DebrisPath);
                rv.ImpactVfx = AssetDatabase.LoadAssetAtPath<GameObject>(DustVfx);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, RamPrefab);
                sb.Append($"\n  Prefab: {RamPrefab} (Collider {box.size.x * elite:0.0}×{box.size.y * elite:0.0}×{box.size.z * elite:0.0} m im Spiel, " +
                          $"Rad-Radius {rv.WheelRadius:0.00} m, {wheels.Count} Räder, Nexus-Reichweite {brain.NexusAttackRange:0.0} m, Trümmer {(rv.DebrisPrefab != null ? "ja" : "nein")})");
                return prefab;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Achsenparallele Hülle aller Meshes unter part (ohne exclude) im lokalen Raum von space
        private static bool TryLocalBounds(Transform part, Transform space, Transform exclude, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || (exclude != null && mf.transform.IsChildOf(exclude))) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (any) bounds.Encapsulate(p); else { bounds = new Bounds(p, Vector3.zero); any = true; }
                }
            }
            return any;
        }

        // ---------------- 4. Szene ----------------

        private static void WireScene(EnemyConfigSO config, StringBuilder sb)
        {
            WaveManager wm = null;
            for (int i = 0; i < SceneManager.sceneCount && wm == null; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (var r in s.GetRootGameObjects())
                {
                    wm = r.GetComponentInChildren<WaveManager>(true);
                    if (wm != null) break;
                }
            }
            if (wm == null)
            {
                sb.Append("\n  Hinweis: kein WaveManager in den geöffneten Szenen – RamConfig bitte in test.unity setzen (oder test.unity öffnen und erneut bauen).");
                return;
            }
            if (wm.RamConfig != config)
            {
                Undo.RecordObject(wm, "Ramme: RamConfig");
                wm.RamConfig = config;
                EditorUtility.SetDirty(wm);
                EditorSceneManager.MarkSceneDirty(wm.gameObject.scene);
                sb.Append($"\n  WaveManager.RamConfig → Ram.asset ({wm.gameObject.scene.name}, Szene markiert, nicht gespeichert)");
            }
            else sb.Append("\n  WaveManager.RamConfig ist bereits Ram.asset");
        }

        // ---------------- 5. Icon ----------------

        // Blender-Render aus art-src/ram (run_all.py → renders/ram_codex.png); fehlt er, rendert PacingSetup das Prefab
        private const string BlenderIcon = "../art-src/ram/renders/ram_codex.png";

        private static void RenderIcon(EnemyConfigSO config, StringBuilder sb)
        {
            if (config.Prefab == null) return;
            if (File.Exists(BlenderIcon)) File.Copy(BlenderIcon, IconPath, true);
            else PacingSetup.RenderIcon(config.Prefab, IconPath);
            AssetDatabase.ImportAsset(IconPath);
            var imp = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single; // Projekt-Vorgabe ist Multiple
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            config.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            EditorUtility.SetDirty(config);
            // Kodex-Eintrag „Belagerungsramme“ zeigt dasselbe Bild (sonst erst nach MetaSetup.BuildCodex)
            var db = Resources.Load<CodexDatabaseSO>("CodexDatabase");
            if (db != null)
                foreach (var e in db.Entries)
                    if (e.Key == Codex.EventKey(WaveEvent.Ram)) { e.Icon = config.Icon; EditorUtility.SetDirty(db); }
            sb.Append("\n  Icon: " + IconPath);
        }
    }
}
