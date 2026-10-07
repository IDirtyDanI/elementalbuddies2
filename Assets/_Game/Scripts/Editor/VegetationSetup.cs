using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ElementalBuddies.EditorTools
{
    // Ersetzt die alten Meshy-Bäume/-Büsche (Treeorigin, Kiefer, Busch) durch die Low-Poly-Vegetation aus Blender
    // (Assets/_Game/Models/Vegetation/Veg_*.fbx). Alle Schritte idempotent, beliebig oft ausführbar:
    //  1. Import-Einstellungen der FBX (1 Blender-m = 1 Unity-m, keine Collider/Animation, Materialien remapped)
    //  2. URP-Lit-Materialien je Material-Slot (Farben aus art-src/vegetation/palette.json)
    //  3. Prefabs Assets/_Game/Prefabs/Vegetation/Veg_*.prefab (Collider aus den Mesh-Maßen, Kronen-Volumen für den OcclusionFader)
    //  4. Austausch in test.unity (Variante/Drehung/Größe deterministisch aus der Position, Y per Raycast auf den Boden)
    //  5. NavMesh neu backen (die NavMeshSurface „Environment" sammelt Render-Meshes → Vegetation wirkt aufs NavMesh)
    // Rückgängig: Liste der alten Instanzen in art-src/vegetation/old_instances.json + Menüpunkt „Alte Vegetation wiederherstellen".
    public static class VegetationSetup
    {
        private const string ModelDir = "Assets/_Game/Models/Vegetation/";
        private const string MatDir = ModelDir + "Materials/";
        private const string PrefabDir = "Assets/_Game/Prefabs/Vegetation/";
        private const string ScenePath = "Assets/test.unity";
        private const float WallHalf = 35f;          // Stadtmauer bei ±35
        private const int IgnoreRaycastLayer = 2;    // Kronen-Volumen: nicht in ObstacleLayer, nicht im Standard-Raycast

        private static string ArtDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art-src/vegetation"));

        private static readonly Dictionary<string, string> OldPrefabs = new Dictionary<string, string>
        {
            { "Assets/3D Models/Environment/Tree/Treeorigin.prefab", "Laub" },
            { "Assets/3D Models/Environment/Tree/Kiefer.prefab", "Kiefer" },
            { "Assets/3D Models/Environment/Tree/Busch.prefab", "Busch" },
        };

        private enum Kind { Baum, Busch, Stumpf }

        private struct Def
        {
            public string Name; public Kind Kind; public float Height; public float Crown;
            public Def(string n, Kind k, float h, float c) { Name = n; Kind = k; Height = h; Crown = c; }
        }

        // Soll-Maße laut Wiki (Design/Vegetation.md) – dienen nur der Plausibilitätsprüfung des Import-Maßstabs
        private static readonly Def[] Defs =
        {
            new Def("Veg_Eiche_A", Kind.Baum, 5.5f, 4.5f),
            new Def("Veg_Eiche_B", Kind.Baum, 5f, 4f),
            new Def("Veg_Linde", Kind.Baum, 6.5f, 3.5f),
            new Def("Veg_Birke", Kind.Baum, 6f, 3f),
            new Def("Veg_Apfelbaum", Kind.Baum, 4f, 3.5f),
            new Def("Veg_Totbaum", Kind.Baum, 5f, 3f),
            new Def("Veg_Kiefer_A", Kind.Baum, 7f, 3.5f),
            new Def("Veg_Kiefer_B", Kind.Baum, 6f, 3f),
            new Def("Veg_Busch_A", Kind.Busch, 1.1f, 1.5f),
            new Def("Veg_Busch_B", Kind.Busch, 0.8f, 1.2f),
            new Def("Veg_Bluetenbusch", Kind.Busch, 1f, 1.3f),
            new Def("Veg_Beerenbusch", Kind.Busch, 1.2f, 1.4f),
            new Def("Veg_Baumstumpf", Kind.Stumpf, 0.5f, 0.8f),
        };

        public static readonly string[] Slots =
        {
            "Veg_Bark", "Veg_BarkDark", "Veg_BirchBark", "Veg_BirchMark", "Veg_Leaf", "Veg_LeafLight", "Veg_LeafDark",
            "Veg_Needle", "Veg_NeedleDark", "Veg_Blossom", "Veg_Fruit", "Veg_Berry", "Veg_DeadWood", "Veg_StumpCut"
        };

        private static readonly HashSet<string> WoodSlots = new HashSet<string> { "Veg_Bark", "Veg_BarkDark", "Veg_BirchBark", "Veg_BirchMark", "Veg_DeadWood" };

        // Ersetzungsregeln (gewichtete Varianten)
        private static readonly (string, float)[] LaubInnen = { ("Veg_Eiche_A", 0.26f), ("Veg_Eiche_B", 0.22f), ("Veg_Linde", 0.20f), ("Veg_Birke", 0.17f), ("Veg_Apfelbaum", 0.15f) };
        private static readonly (string, float)[] LaubAussen = { ("Veg_Eiche_A", 0.25f), ("Veg_Eiche_B", 0.22f), ("Veg_Linde", 0.20f), ("Veg_Birke", 0.18f), ("Veg_Totbaum", 0.15f) };
        private static readonly (string, float)[] Kiefern = { ("Veg_Kiefer_A", 0.55f), ("Veg_Kiefer_B", 0.45f) };
        private static readonly (string, float)[] Buesche = { ("Veg_Busch_A", 0.30f), ("Veg_Busch_B", 0.25f), ("Veg_Bluetenbusch", 0.22f), ("Veg_Beerenbusch", 0.23f) };

        // ---------------- Menü ----------------

        [MenuItem("BuddyTD/Vegetation/Alles einrichten")]
        public static void SetupAll()
        {
            if (!EditModeOnly()) return;
            ImportSettings();
            Materials();
            Prefabs();
            ReplaceInScene();
            Debug.Log("VegetationSetup: " + RebakeAndCheck());
            SaveScene();
        }

        [MenuItem("BuddyTD/Vegetation/1 Import-Einstellungen")]
        public static void ImportSettingsMenu() { if (EditModeOnly()) ImportSettings(); }

        [MenuItem("BuddyTD/Vegetation/2 Materialien")]
        public static void MaterialsMenu() { if (EditModeOnly()) Materials(); }

        [MenuItem("BuddyTD/Vegetation/3 Prefabs")]
        public static void PrefabsMenu() { if (EditModeOnly()) Prefabs(); }

        [MenuItem("BuddyTD/Vegetation/4 Austausch in test.unity")]
        public static void ReplaceMenu()
        {
            if (!EditModeOnly()) return;
            ReplaceInScene();
            SaveScene();
        }

        [MenuItem("BuddyTD/Vegetation/5 NavMesh neu backen + Wege prüfen")]
        public static void NavMeshMenu()
        {
            if (!EditModeOnly()) return;
            Debug.Log("VegetationSetup: " + RebakeAndCheck());
            SaveScene();
        }

        [MenuItem("BuddyTD/Vegetation/Bodenhöhe neu setzen")]
        public static void RegroundMenu()
        {
            if (!EditModeOnly() || !EnsureScene()) return;
            int n = 0;
            var skip = VegetationColliders();
            foreach (var go in NewInstances())
            {
                var def = DefOf(go);
                var p = go.transform.position;
                float y = GroundY(p, def.Kind == Kind.Baum ? 0.45f : 0.3f, skip, p.y, out _);
                if (Mathf.Abs(y - p.y) > 0.001f) { Undo.RecordObject(go.transform, "Vegetation Bodenhöhe"); go.transform.position = new Vector3(p.x, y, p.z); n++; }
            }
            Debug.Log($"VegetationSetup: Bodenhöhe bei {n} Objekten korrigiert.");
            EditorSceneManager.MarkSceneDirty(SceneManagerActive());
        }

        [MenuItem("BuddyTD/Vegetation/Alte Vegetation wiederherstellen (aus old_instances.json)")]
        public static void RestoreMenu()
        {
            if (!EditModeOnly() || !EnsureScene()) return;
            Restore();
        }

        private static bool EditModeOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("VegetationSetup: nur im Edit-Modus."); return false; }
            return true;
        }

        // ---------------- 1. Import ----------------

        public static void ImportSettings()
        {
            EnsureFolder("Assets/_Game/Models", "Vegetation");
            EnsureFolder("Assets/_Game/Models/Vegetation", "Materials");
            var sb = new StringBuilder("VegetationSetup Import:");
            foreach (var def in Defs)
            {
                string path = ModelDir + def.Name + ".fbx";
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) { sb.Append($"\n  {def.Name}: FBX fehlt"); continue; }

                bool changed = false;
                void Set<T>(T cur, T val, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(cur, val)) { apply(val); changed = true; } }
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

                // Material-Slots auf die gemeinsamen Vegetations-Materialien umleiten
                bool remapped = false;
                var map = imp.GetExternalObjectMap();
                foreach (var id in SourceMaterials(imp, path))
                {
                    var mat = EnsureMaterial(SlotName(id.name));
                    if (map.TryGetValue(id, out var cur) && cur == mat) continue;
                    imp.AddRemap(id, mat);
                    remapped = true;
                }
                if (remapped) imp.SaveAndReimport();

                // Maßstab prüfen: gemessene Höhe gegen Soll-Höhe (z. B. cm statt m → Faktor 100)
                float h = MeasureHeight(path);
                if (h > 0.0001f)
                {
                    float ratio = def.Height / h;
                    if (ratio > 3f || ratio < 1f / 3f)
                    {
                        float f = Mathf.Pow(10f, Mathf.Round(Mathf.Log10(ratio)));
                        imp.globalScale *= f;
                        imp.SaveAndReimport();
                        h = MeasureHeight(path);
                        sb.Append($"\n  {def.Name}: Maßstab ×{f} korrigiert");
                    }
                }
                sb.Append($"\n  {def.Name}: Höhe {h:0.00} m (Soll {def.Height}), Slots {string.Join(",", SourceMaterials(imp, path).Select(s => s.name))}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        // Material-Slots der FBX: eingebettete (noch nicht umgeleitete) + bereits umgeleitete
        private static List<AssetImporter.SourceAssetIdentifier> SourceMaterials(ModelImporter imp, string path)
        {
            var ids = new List<AssetImporter.SourceAssetIdentifier>();
            foreach (var kv in imp.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material)) ids.Add(kv.Key);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Material m && !ids.Any(i => i.name == m.name)) ids.Add(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name));
            return ids;
        }

        private static string SlotName(string n) => Regex.Replace(n, @"\.\d+$", "");

        private static float MeasureHeight(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return 0f;
            var tmp = Object.Instantiate(asset);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            tmp.transform.position = Vector3.zero;
            var b = RendererBounds(tmp);
            Object.DestroyImmediate(tmp);
            return b.size.y;
        }

        // ---------------- 2. Materialien ----------------

        public static void Materials()
        {
            EnsureFolder("Assets/_Game/Models", "Vegetation");
            EnsureFolder("Assets/_Game/Models/Vegetation", "Materials");
            var palette = ReadPalette();
            int n = 0;
            foreach (var slot in Slots)
            {
                var m = EnsureMaterial(slot);
                if (palette.TryGetValue(slot, out var c)) { m.SetColor("_BaseColor", c); m.SetColor("_Color", c); n++; }
                else Debug.LogWarning($"VegetationSetup: keine Farbe für {slot} in palette.json");
                ApplyMaterialDefaults(m);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"VegetationSetup Materialien: {Slots.Length} Materialien, {n} Farben aus palette.json");
        }

        private static Material EnsureMaterial(string slot)
        {
            string p = MatDir + slot + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) return m;
            EnsureFolder("Assets/_Game/Models/Vegetation", "Materials");
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = slot };
            ApplyMaterialDefaults(m);
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        // Wie die Stadt-Materialien (URP/Lit, opak, matt), plus GPU-Instancing
        private static void ApplyMaterialDefaults(Material m)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null && m.shader != lit) m.shader = lit;
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", 2f);
            m.enableInstancing = true;
        }

        // palette.json tolerant lesen: je Slot "#RRGGBB", [r,g,b(,a)] (0–1 oder 0–255) oder ein Objekt mit solchen Werten.
        // Hex gilt als sRGB; Zahlen-Arrays gelten als sRGB, außer der Schlüssel heißt „linear".
        private static Dictionary<string, Color> ReadPalette()
        {
            var result = new Dictionary<string, Color>();
            string file = Path.Combine(ArtDir, "palette.json");
            if (!File.Exists(file)) { Debug.LogWarning("VegetationSetup: palette.json fehlt: " + file); return result; }
            string json = File.ReadAllText(file);
            foreach (var slot in Slots)
            {
                int i = json.IndexOf("\"" + slot + "\"", StringComparison.Ordinal);
                if (i < 0) continue;
                int colon = json.IndexOf(':', i);
                if (colon < 0) continue;
                // Wert bis zum nächsten Slot-Schlüssel
                int end = json.Length;
                foreach (var other in Slots)
                {
                    if (other == slot) continue;
                    int j = json.IndexOf("\"" + other + "\"", colon, StringComparison.Ordinal);
                    if (j > colon && j < end) end = j;
                }
                string val = json.Substring(colon + 1, end - colon - 1);
                if (TryColor(val, out var c)) result[slot] = c;
            }
            return result;
        }

        private static bool TryColor(string val, out Color c)
        {
            c = Color.white;
            // bevorzugt sRGB-Angaben
            var srgbArr = Regex.Match(val, "\"(?:srgb|sRGB|rgb|color|colour|base_color)\"\\s*:\\s*\\[([^\\]]*)\\]");
            var hex = Regex.Match(val, "#([0-9a-fA-F]{6})");
            var linArr = Regex.Match(val, "\"linear[^\"]*\"\\s*:\\s*\\[([^\\]]*)\\]");
            var anyArr = Regex.Match(val, "\\[([^\\]]*)\\]");
            if (srgbArr.Success && ParseArr(srgbArr.Groups[1].Value, out c)) return true;
            if (hex.Success) { ColorUtility.TryParseHtmlString("#" + hex.Groups[1].Value, out c); return true; }
            if (linArr.Success && ParseArr(linArr.Groups[1].Value, out c)) { c = c.gamma; c.a = 1f; return true; }
            if (anyArr.Success && ParseArr(anyArr.Groups[1].Value, out c)) return true;
            return false;
        }

        private static bool ParseArr(string s, out Color c)
        {
            c = Color.white;
            var v = s.Split(',').Select(t => float.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : float.NaN).ToArray();
            if (v.Length < 3 || v.Take(3).Any(float.IsNaN)) return false;
            float div = v.Take(3).Any(f => f > 1.001f) ? 255f : 1f;
            c = new Color(v[0] / div, v[1] / div, v[2] / div, 1f);
            return true;
        }

        // ---------------- 3. Prefabs ----------------

        public static void Prefabs()
        {
            EnsureFolder("Assets/_Game/Prefabs", "Vegetation");
            var sb = new StringBuilder("VegetationSetup Prefabs:");
            foreach (var def in Defs)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + def.Name + ".fbx");
                if (model == null) { sb.Append($"\n  {def.Name}: FBX fehlt"); continue; }
                sb.Append("\n  " + BuildPrefab(def, model));
            }
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        private static string BuildPrefab(Def def, GameObject model)
        {
            var root = new GameObject(def.Name);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "Modell";
                inst.transform.SetParent(root.transform, false);
                inst.transform.localPosition = Vector3.zero;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                    r.lightProbeUsage = LightProbeUsage.BlendProbes;
                }

                var b = RendererBounds(root);
                float h = b.max.y;
                string info;
                if (def.Kind == Kind.Baum)
                {
                    // Stamm: Kapsel um den Stamm (Radius aus den Holz-Vertices in 0,25–0,9 m Höhe), Höhe = Baumhöhe
                    Vector2 trunkC; float trunkR;
                    if (!TrunkFromMesh(root, out trunkC, out trunkR)) { trunkC = new Vector2(b.center.x, b.center.z); trunkR = 0.3f; }
                    float r = Mathf.Clamp(trunkR + 0.1f, 0.4f, 0.5f);
                    var cap = root.AddComponent<CapsuleCollider>();
                    cap.direction = 1;
                    cap.radius = r;
                    cap.height = h;
                    cap.center = new Vector3(trunkC.x, h * 0.5f, trunkC.y);

                    // Krone: Trigger-Volumen auf Ignore Raycast → nur für den OcclusionFader (blendet den Baum vor der Spielfigur aus)
                    var crown = new GameObject("Kronenvolumen") { layer = IgnoreRaycastLayer };
                    crown.transform.SetParent(root.transform, false);
                    float bottom = Mathf.Max(h * 0.4f, 1.6f);
                    float cr = Mathf.Max(0.5f, 0.85f * 0.5f * Mathf.Max(b.size.x, b.size.z));
                    var cc = crown.AddComponent<CapsuleCollider>();
                    cc.isTrigger = true;
                    cc.direction = 1;
                    cc.radius = cr;
                    cc.height = Mathf.Max(h - bottom, 2f * cr);
                    cc.center = new Vector3(b.center.x, (bottom + h) * 0.5f, b.center.z);

                    var fo = root.AddComponent<FadeOccluder>();
                    fo.TriggerVolumes = true;
                    fo.FadedAlpha = 0.35f;
                    info = $"{def.Name}: H {h:0.00} m, Krone ⌀ {Mathf.Max(b.size.x, b.size.z):0.00} m, Stamm r {r:0.00} (Mesh {trunkR:0.00}), Krone r {cr:0.00}";
                }
                else
                {
                    // Busch / Stumpf: aufrechte Kapsel über den Umriss (blockiert wie bisher)
                    float r = (def.Kind == Kind.Busch ? 0.4f : 0.42f) * (b.size.x + b.size.z) * 0.5f;
                    var cap = root.AddComponent<CapsuleCollider>();
                    cap.direction = 1;
                    cap.radius = r;
                    cap.height = Mathf.Max(h, 2f * r);
                    cap.center = new Vector3(b.center.x, Mathf.Max(h * 0.5f, r), b.center.z);
                    info = $"{def.Name}: H {h:0.00} m, ⌀ {Mathf.Max(b.size.x, b.size.z):0.00} m, Kapsel r {r:0.00}";
                }

                string path = PrefabDir + def.Name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return info;
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Stammmitte/-radius aus den Vertices der Holz-Submeshes in Stammhöhe (0,25–0,9 m)
        private static bool TrunkFromMesh(GameObject root, out Vector2 center, out float radius)
        {
            center = Vector2.zero; radius = 0f;
            var pts = new List<Vector2>();
            try
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mesh == null || mr == null) continue;
                    var verts = mesh.vertices;
                    var mats = mr.sharedMaterials;
                    var m = mf.transform.localToWorldMatrix;
                    for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                    {
                        if (mats[s] == null || !WoodSlots.Contains(SlotName(mats[s].name))) continue;
                        foreach (int idx in mesh.GetIndices(s))
                        {
                            var p = m.MultiplyPoint3x4(verts[idx]);
                            if (p.y >= 0.25f && p.y <= 0.9f) pts.Add(new Vector2(p.x, p.z));
                        }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("VegetationSetup: Stamm-Messung fehlgeschlagen: " + e.Message); return false; }
            if (pts.Count < 6) return false;
            float minX = pts.Min(p => p.x), maxX = pts.Max(p => p.x), minY = pts.Min(p => p.y), maxY = pts.Max(p => p.y);
            center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            var c = center;
            var d = pts.Select(p => Vector2.Distance(p, c)).OrderBy(x => x).ToList();
            radius = d[Mathf.Clamp(Mathf.FloorToInt(d.Count * 0.9f), 0, d.Count - 1)];
            return true;
        }

        // ---------------- 4. Austausch ----------------

        [Serializable] private class OldEntry
        {
            public string prefab; public string name; public string parentPath; public int siblingIndex;
            public Vector3 position; public Vector3 eulerAngles; public Vector3 localPosition; public Quaternion localRotation; public Vector3 localScale;
            public int staticFlags; public bool hasCollider;
        }
        [Serializable] private class OldList { public string scene; public string created; public List<OldEntry> items = new List<OldEntry>(); }

        public static void ReplaceInScene()
        {
            if (!EnsureScene()) return;
            var scene = SceneManagerActive();
            if (scene.isDirty) Debug.LogWarning("VegetationSetup: test.unity hatte vor dem Austausch bereits ungespeicherte Änderungen.");

            var olds = OldInstances();
            if (olds.Count == 0) { Debug.Log("VegetationSetup: keine alten Vegetations-Instanzen mehr in der Szene – nichts zu tun."); return; }

            // Rückgängig-Weg: Liste der alten Instanzen sichern
            var list = new OldList { scene = ScenePath, created = DateTime.Now.ToString("s") };
            foreach (var go in olds)
            {
                var t = go.transform;
                list.items.Add(new OldEntry
                {
                    prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go), name = go.name,
                    parentPath = t.parent != null ? PathOf(t.parent) : "", siblingIndex = t.GetSiblingIndex(),
                    position = t.position, eulerAngles = t.eulerAngles, localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale,
                    staticFlags = (int)GameObjectUtility.GetStaticEditorFlags(go), hasCollider = go.GetComponent<Collider>() != null
                });
            }
            Directory.CreateDirectory(ArtDir);
            string jsonPath = Path.Combine(ArtDir, "old_instances.json");
            if (File.Exists(jsonPath)) File.Copy(jsonPath, jsonPath + ".bak", true);
            File.WriteAllText(jsonPath, JsonUtility.ToJson(list, true));

            var prefabs = Defs.ToDictionary(d => d.Name, d => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + d.Name + ".prefab"));
            if (prefabs.Values.Any(p => p == null)) { Debug.LogError("VegetationSetup: Prefabs fehlen – zuerst „3 Prefabs“ ausführen."); return; }

            Physics.SyncTransforms();
            var skip = VegetationColliders();
            var counts = new SortedDictionary<string, int>();
            int bigDy = 0; float maxDy = 0f; int noGround = 0; int walkable = 0;
            Undo.SetCurrentGroupName("Vegetation austauschen");
            int group = Undo.GetCurrentGroup();

            foreach (var go in olds)
            {
                var t = go.transform;
                string kind = OldPrefabs[PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go)];
                Vector3 p = t.position;
                bool inside = Mathf.Abs(p.x) <= WallHalf && Mathf.Abs(p.z) <= WallHalf;
                var table = kind == "Kiefer" ? Kiefern : kind == "Busch" ? Buesche : inside ? LaubInnen : LaubAussen;
                string variant = Pick(table, Rand01(p, 11u));
                var def = Defs.First(d => d.Name == variant);

                float yaw = Rand01(p, 23u) * 360f;
                float scale = Mathf.Lerp(0.88f, 1.12f, Rand01(p, 37u));
                float y = GroundY(p, def.Kind == Kind.Baum ? 0.45f : 0.3f, skip, p.y, out bool found);
                if (!found) noGround++;
                float dy = Mathf.Abs(y - p.y); maxDy = Mathf.Max(maxDy, dy); if (dy > 0.5f) bigDy++;

                var parent = t.parent;
                int sib = t.GetSiblingIndex();
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                // Spielverhalten erhalten: Die alten Instanzen außerhalb der Stadt hatten ihren Collider per Override
                // entfernt (begehbar). Nur wo die alte Instanz einen festen Collider hatte, bekommt die neue einen.
                bool hadSolidCollider = go.GetComponentsInChildren<Collider>(true).Any(col => !col.isTrigger);

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[variant], go.scene);
                Undo.RegisterCreatedObjectUndo(inst, "Vegetation austauschen");
                inst.transform.SetParent(parent, false);
                inst.transform.SetSiblingIndex(sib);
                inst.transform.position = new Vector3(p.x, y, p.z);
                inst.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                inst.transform.localScale = Vector3.one * scale;
                inst.name = variant;
                foreach (var tr in inst.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(tr.gameObject, flags);
                if (!hadSolidCollider)
                {
                    foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                        if (!col.isTrigger) Object.DestroyImmediate(col);
                    walkable++;
                }

                Undo.DestroyObjectImmediate(go);
                counts[variant] = counts.TryGetValue(variant, out var c) ? c + 1 : 1;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);

            var sb = new StringBuilder($"VegetationSetup Austausch: {olds.Count} alte Instanzen ersetzt (Liste: {jsonPath})");
            foreach (var kv in counts) sb.Append($"\n  {kv.Key}: {kv.Value}");
            sb.Append($"\n  ohne festen Collider (wie bisher begehbar): {walkable}");
            sb.Append($"\n  Bodenhöhe: max. Abweichung zur alten Y {maxDy:0.00} m, >0,5 m bei {bigDy}, ohne Bodentreffer {noGround}");
            Debug.Log(sb.ToString());
        }

        private static string Pick((string, float)[] table, float r)
        {
            float sum = table.Sum(e => e.Item2), acc = 0f;
            foreach (var e in table) { acc += e.Item2 / sum; if (r < acc) return e.Item1; }
            return table[table.Length - 1].Item1;
        }

        // Deterministischer Hash aus der Position (auf 10 cm gerundet) + Salz
        private static float Rand01(Vector3 p, uint salt)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)Mathf.RoundToInt(p.x * 10f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(p.z * 10f)) * 16777619u;
                h = (h ^ salt) * 16777619u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        // Bodenhöhe: niedrigster Boden (Terrain oder Layer Floor) an Mitte + 4 Punkten im Stammradius → kein Schweben am Hang
        private static float GroundY(Vector3 p, float radius, HashSet<Collider> skip, float fallback, out bool found)
        {
            float best = float.PositiveInfinity;
            var offs = new[] { Vector2.zero, new Vector2(radius, 0f), new Vector2(-radius, 0f), new Vector2(0f, radius), new Vector2(0f, -radius) };
            foreach (var o in offs)
                if (GroundAt(new Vector3(p.x + o.x, 0f, p.z + o.y), skip, out float y)) best = Mathf.Min(best, y);
            found = !float.IsInfinity(best);
            if (!found)
            {
                var terrain = Terrain.activeTerrain;
                if (terrain != null) { found = true; return terrain.SampleHeight(p) + terrain.transform.position.y; }
                return fallback;
            }
            return best - 0.03f;
        }

        private static bool GroundAt(Vector3 p, HashSet<Collider> skip, out float y)
        {
            y = 0f;
            int floor = LayerMask.NameToLayer("Floor");
            var hits = Physics.RaycastAll(new Vector3(p.x, 200f, p.z), Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits.OrderBy(h => h.distance))
            {
                if (skip.Contains(h.collider)) continue;
                if (h.collider is TerrainCollider || h.collider.gameObject.layer == floor) { y = h.point.y; return true; }
            }
            return false;
        }

        private static HashSet<Collider> VegetationColliders()
        {
            var set = new HashSet<Collider>();
            foreach (var go in OldInstances().Concat(NewInstances()))
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) set.Add(c);
            return set;
        }

        private static List<GameObject> OldInstances() => PrefabRoots(path => OldPrefabs.ContainsKey(path));

        private static List<GameObject> NewInstances() => PrefabRoots(path => path.StartsWith(PrefabDir, StringComparison.Ordinal));

        // Prefab-Instanz-Wurzeln in Hierarchie-Reihenfolge
        private static List<GameObject> PrefabRoots(Func<string, bool> match)
        {
            var result = new List<GameObject>();
            void Walk(Transform t)
            {
                var go = t.gameObject;
                if (PrefabUtility.IsAnyPrefabInstanceRoot(go) && match(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) ?? ""))
                { result.Add(go); return; }
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            foreach (var r in SceneManagerActive().GetRootGameObjects()) Walk(r.transform);
            return result;
        }

        private static Def DefOf(GameObject inst)
        {
            string n = Path.GetFileNameWithoutExtension(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(inst));
            return Defs.FirstOrDefault(d => d.Name == n);
        }

        // ---------------- Wiederherstellen ----------------

        private static void Restore()
        {
            string jsonPath = Path.Combine(ArtDir, "old_instances.json");
            if (!File.Exists(jsonPath)) { Debug.LogError("VegetationSetup: " + jsonPath + " fehlt."); return; }
            var list = JsonUtility.FromJson<OldList>(File.ReadAllText(jsonPath));
            Undo.SetCurrentGroupName("Alte Vegetation wiederherstellen");
            foreach (var go in NewInstances()) Undo.DestroyObjectImmediate(go);
            int n = 0;
            foreach (var e in list.items)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefab);
                var parentGo = string.IsNullOrEmpty(e.parentPath) ? null : GameObject.Find("/" + e.parentPath);
                if (prefab == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManagerActive());
                Undo.RegisterCreatedObjectUndo(inst, "Alte Vegetation wiederherstellen");
                if (parentGo != null) inst.transform.SetParent(parentGo.transform, false);
                inst.transform.localPosition = e.localPosition;
                inst.transform.localRotation = e.localRotation;
                inst.transform.localScale = e.localScale;
                inst.name = e.name;
                GameObjectUtility.SetStaticEditorFlags(inst, (StaticEditorFlags)e.staticFlags);
                if (!e.hasCollider) { var c = inst.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c); }
                if (parentGo != null && e.siblingIndex < parentGo.transform.childCount) inst.transform.SetSiblingIndex(e.siblingIndex);
                n++;
            }
            EditorSceneManager.MarkSceneDirty(SceneManagerActive());
            Debug.Log($"VegetationSetup: {n} alte Instanzen wiederhergestellt (NavMesh danach neu backen).");
        }

        // ---------------- 5. NavMesh ----------------

        public static string RebakeAndCheck()
        {
            if (!EnsureScene()) return "Szene fehlt";
            string before = MerchantSetup.PortalPaths();
            string bake = MerchantSetup.RebakeNavMesh();
            string after = MerchantSetup.PortalPaths();
            if (after.Contains("Partial") || after.Contains("Invalid")) Debug.LogError("VegetationSetup: Weg Portal → Nexus unvollständig! " + after);
            return $"{bake}\n  Wege vorher: {before}\n  Wege nachher: {after}";
        }

        // ---------------- Hilfen ----------------

        private static UnityEngine.SceneManagement.Scene SceneManagerActive() => EditorSceneManager.GetActiveScene();

        private static bool EnsureScene()
        {
            var s = SceneManagerActive();
            if (s.path == ScenePath) return true;
            if (s.isDirty) { Debug.LogError($"VegetationSetup: aktive Szene {s.path} hat ungespeicherte Änderungen – bitte {ScenePath} öffnen."); return false; }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return true;
        }

        private static void SaveScene()
        {
            var s = SceneManagerActive();
            if (s.path != ScenePath || !s.isDirty) return;
            EditorSceneManager.SaveScene(s);
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        private static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
