using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ElementalBuddies.EditorTools
{
    // Walddorf-Szene (Paket S-Walddorf): baut Assets/Scenes/Walddorf.unity datengetrieben aus den CSV-Tabellen, die
    // art-src/village/plan.py nach Assets/Scenes/Walddorf/Plan/ schreibt (Lageplan → Wiki Design/Walddorf-Plan).
    //
    //  1. Szene anlegen: Kern-Prefabs (Assets/_Game/Prefabs/Core), MapContext (Map_Forest), NetGame (neues
    //     in-scene NetworkObject → eigener GlobalObjectIdHash), Nexus/Portale/Schreine/Kartengrenze/Sonne als Kopie
    //     aus der Stadt (test.unity wird nur gelesen, nie gespeichert), Environment mit NavMeshSurface.
    //  2. Gelände: eigenes Terrain Assets/Scenes/Walddorf/ForestTerrain.asset (Hang/Waldring, Mühlbach, Teiche),
    //     eigene Terrain-Layer (Wiese/Erdweg/Waldboden prozedural aus plan.py --textures, Fels/Pflaster der Stadt-Kacheln).
    //  3. Bebauung: Palisade (Platzhalter aus ArenaMauer_Segment), Gebäude (Stadt-Prefabs als Platzhalter), Requisiten,
    //     Zäune, Fackeln, Brücke/Steg, Wasser + NavMesh-Bereich „Wasser“ (Furt), Wald (Veg_*), Händlerstände.
    //  4. NavMesh: MerchantSetup.RebakeNavMesh → Assets/Scenes/Walddorf/NavMesh-*.asset (Stadt-Assets unberührt).
    //  Prüfen: Weglängen Portal → Nexus (Humanoid + Ramme), Erreichbarkeit der Schrein-/Händler-Kreise, Bauplätze.
    //
    // Etappe 2 tauscht die Modelle nur über die Prefab-Spalten in plan.py und lässt „Alles neu aufbauen“ laufen.
    public static class VillageSetup
    {
        public const string ScenePath = "Assets/Scenes/Walddorf.unity";
        public const string Folder = "Assets/Scenes/Walddorf";
        public const string PlanFolder = Folder + "/Plan";
        public const string TerrainDataPath = Folder + "/ForestTerrain.asset";
        public const string LayerFolder = Folder + "/Layers";
        public const string TextureFolder = Folder + "/Textures";
        public const string MeshFolder = Folder + "/Meshes";
        public const string PrefabFolder = "Assets/_Game/Prefabs/Village";
        public const string MaterialFolder = PrefabFolder + "/Materials";
        public const string ForestVariantFolder = PrefabFolder + "/Wald";
        public const string MapAssetPath = "Assets/Resources/Maps/Map_Forest.asset";
        const string CoreFolder = "Assets/_Game/Prefabs/Core/";
        static readonly string[] CoreRoots = { "Managers", "Canvas", "Main Camera", "EventSystem", "NetBootstrap" };

        public const int WaterArea = 3;
        public const string WaterAreaName = "Wasser";
        public const float WaterCost = 5f;

        const float TerrainSize = 160f, TerrainHeight = 40f, TerrainBase = 1f; // Terrain 1 m tiefer → Bachbett unter 0
        const float Flat = 57f, HillHeight = 12.5f;
        const float StreamHalf = 1.6f, StreamBank = 1.4f;
        const int FloorLayer = 8, WaterLayer = 4;

        // ---------------------------------------------------------------- Menü

        [MenuItem("BuddyTD/Walddorf/1 Szene anlegen (Skelett)")]
        public static void CreateSceneMenu() => Debug.Log(CreateScene());

        [MenuItem("BuddyTD/Walddorf/Alles neu aufbauen (Gelände, Bebauung, NavMesh)")]
        public static void BuildAllMenu() => Debug.Log(BuildAll(true));

        [MenuItem("BuddyTD/Walddorf/Nur Bebauung neu platzieren")]
        public static void PlaceMenu() => Debug.Log(BuildAll(false));

        [MenuItem("BuddyTD/Walddorf/Prüfen (Wege, Kreise, Bauplätze)")]
        public static void MeasureMenu() => Debug.Log(Measure(null));

        // ---------------------------------------------------------------- 1. Skelett

        public static string CreateScene() => CreateScene(false);

        // ignoreDirtyCity: eine offene, nur durch Skripte „dirty“ markierte Stadt verwerfen (sie wird nie gespeichert)
        public static string CreateScene(bool ignoreDirtyCity)
        {
            if (File.Exists(ScenePath)) return "VillageSetup: " + ScenePath + " existiert schon (Skelett wird nicht überschrieben).";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty && !(ignoreDirtyCity && SceneManager.GetSceneAt(i).path == MapEditorUtil.CityScenePath)) return "VillageSetup: ungespeicherte Szene offen (" + SceneManager.GetSceneAt(i).path + ") – abgebrochen.";
            var sb = new StringBuilder();
            EnsureFolder(Folder);

            // Stadt nur lesen (Quelle für Nexus/Portale/Schreine/Sonne/Grenze), nie speichern
            var city = EditorSceneManager.OpenScene(MapEditorUtil.CityScenePath, OpenSceneMode.Single);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            // Kern-Prefabs
            var roots = new Dictionary<string, GameObject>();
            foreach (var n in CoreRoots)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CoreFolder + n + ".prefab");
                if (prefab == null) { sb.AppendLine("Kern-Prefab fehlt: " + n); continue; }
                roots[n] = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            }

            // MapContext
            var mc = new GameObject("MapContext");
            var ctx = mc.AddComponent<MapContext>();
            ctx.Map = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(MapAssetPath);
            sb.AppendLine("MapContext → " + (ctx.Map != null ? ctx.Map.Id : "(Map_Forest fehlt)"));

            // NetGame: neues in-scene NetworkObject (Hash entsteht beim Speichern aus Szenen-GUID + FileID)
            var ng = new GameObject("NetGame");
            ng.AddComponent<NetworkObject>();
            ng.AddComponent<NetGame>();

            // Bausteine aus der Stadt kopieren
            GameObject Copy(string path)
            {
                var src = FindInScene(city, path);
                if (src == null) { sb.AppendLine("Stadt: " + path + " fehlt"); return null; }
                GameObject copy = null;
                SceneManager.SetActiveScene(city);
                Selection.objects = new Object[] { src };
                Unsupported.CopyGameObjectsToPasteboard();
                SceneManager.SetActiveScene(scene);
                Unsupported.PasteGameObjectsFromPasteboard();
                copy = Selection.activeGameObject;
                if (copy == null || copy.scene != scene)
                {
                    copy = Object.Instantiate(src);
                    SceneManager.MoveGameObjectToScene(copy, scene);
                }
                copy.name = src.name;
                if (copy.transform.parent != null) copy.transform.SetParent(null, true);
                return copy;
            }

            var light = Copy("Directional Light");
            var nexus = Copy("Nexus");
            var env = new GameObject("Environment");
            var welt = Copy("Environment/Welt");
            var grenze = Copy("Environment/Kartengrenze");
            foreach (var g in new[] { welt, grenze })
                if (g != null) { g.transform.SetParent(env.transform, false); g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; }

            // NavMeshSurface „Environment“ mit den Einstellungen der Stadt (Render-Meshes, alle Objekte)
            var citySurface = FindInScene(city, "Environment").GetComponent<NavMeshSurface>();
            var surface = env.AddComponent<NavMeshSurface>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(citySurface), surface);
            surface.navMeshData = null;

            // Belagerung: Stimmung/Fortschritt (Werte der Stadt, Szenenbezüge neu)
            var dorf = Child(env.transform, "Dorf");
            var bel = Child(dorf, "Belagerung");
            var cityAtmo = FindInScene(city, "Environment/Stadt/Belagerung").GetComponent<SiegeAtmosphere>();
            var atmo = bel.gameObject.AddComponent<SiegeAtmosphere>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(cityAtmo), atmo);
            atmo.Lanterns = new List<Light>();
            atmo.LanternBase = new List<float>();
            atmo.Sun = light != null ? light.GetComponent<Light>() : null;
            var prog = bel.gameObject.AddComponent<SiegeProgression>();
            prog.Atmosphere = atmo;
            prog.Buildings = new List<SiegeProgression.BuildingStage>();
            prog.Fences = new List<SiegeProgression.FenceStage>();

            // Nexus-HP-Leiste → Canvas-Instanz dieser Szene (gleicher Pfad wie in der Stadt)
            var cityNexus = FindInScene(city, "Nexus").GetComponent<Nexus>();
            var nx = nexus != null ? nexus.GetComponent<Nexus>() : null;
            if (nx != null && cityNexus.HPSlider != null && roots.TryGetValue("Canvas", out var canvas))
            {
                string rel = RelativePath(cityNexus.HPSlider.transform);
                var t = canvas.transform.Find(rel);
                nx.HPSlider = t != null ? t.GetComponent<UnityEngine.UI.Slider>() : null;
                sb.AppendLine("Nexus.HPSlider → Canvas/" + rel + (nx.HPSlider != null ? "" : " (nicht gefunden!)"));
            }

            // Render-Einstellungen der Stadt als Ausgangspunkt (Skybox, Umgebungslicht)
            SceneManager.SetActiveScene(city);
            var skybox = RenderSettings.skybox;
            var ambientMode = RenderSettings.ambientMode;
            float ambientIntensity = RenderSettings.ambientIntensity;
            SceneManager.SetActiveScene(scene);
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientIntensity = ambientIntensity;

            Selection.objects = new Object[0];
            if (city.isDirty) sb.AppendLine("WARNUNG: Stadt wurde verändert – wird verworfen.");
            EditorSceneManager.CloseScene(city, true);
            EditorSceneManager.SaveScene(scene, ScenePath);
            sb.AppendLine("Szene angelegt: " + ScenePath + " – " + NetHashInfo());
            return sb.ToString();
        }

        // GlobalObjectIdHash des NetGame (nach dem Speichern); Stadt: 2791533827
        public static string NetHashInfo()
        {
            var ng = GameObject.Find("NetGame");
            var no = ng != null ? ng.GetComponent<NetworkObject>() : null;
            if (no == null) return "NetGame fehlt";
            var so = new SerializedObject(no);
            return "NetGame-Hash " + so.FindProperty("GlobalObjectIdHash").uintValue + " (InScenePlaced " + so.FindProperty("m_InScenePlaced").boolValue + ")";
        }

        // ---------------------------------------------------------------- Alles aufbauen

        public static string BuildAll(bool terrain)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "VillageSetup: erst " + ScenePath + " öffnen (aktive Szene).";
            var sb = new StringBuilder();
            var plan = Plan.Load();
            EnsureFolder(Folder); EnsureFolder(PrefabFolder); EnsureFolder(MaterialFolder);
            EnsureWaterArea(sb);

            var env = GameObject.Find("Environment");
            var dorf = Child(env.transform, "Dorf");
            if (terrain) BuildTerrain(env.transform, plan, sb);
            var terr = Terrain.activeTerrain;

            PlaceAnchors(plan, sb);
            WireSpawnPoints(sb);
            BuildPalisade(dorf, plan, sb);
            var buildings = BuildBuildings(dorf, plan, sb);
            BuildProps(env.transform, dorf, plan, sb);
            BuildCrossings(dorf, plan, sb);
            BuildWater(dorf, plan, sb);
            BuildForest(env.transform, plan, sb);
            BuildStalls(dorf, sb);
            SetupAtmosphere(dorf, buildings, sb);
            SetupVillageSiege(dorf, sb);
            if (terrain) ScatterGrass(terr.terrainData, plan, sb);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine(BakeNavMesh());
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine(NetHashInfo());
            return sb.ToString();
        }

        // ---------------------------------------------------------------- Plan (CSV)

        public class Row
        {
            public Dictionary<string, string> F = new Dictionary<string, string>();
            public string S(string k) => F.TryGetValue(k, out var v) ? v : "";
            public float N(string k) => float.Parse(S(k), CultureInfo.InvariantCulture);
            public int I(string k) => int.Parse(S(k), CultureInfo.InvariantCulture);
            public List<Vector2> Pts()
            {
                var l = new List<Vector2>();
                foreach (var p in S("points").Split('|'))
                {
                    var xy = p.Split(':');
                    l.Add(new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture)));
                }
                return l;
            }
        }

        public class Plan
        {
            public List<Row> Anchors, Buildings, Props, Paths, Water, Crossings, Palisade, Forest, BuildSpots;
            public List<Vector2> Stream = new List<Vector2>();
            public List<Vector4> Ponds = new List<Vector4>(); // x, z, r, depth
            public float StreamDepth = 0.45f;

            public static Plan Load()
            {
                var p = new Plan
                {
                    Anchors = Read("anchors.csv"), Buildings = Read("buildings.csv"), Props = Read("props.csv"), Paths = Read("paths.csv"),
                    Water = Read("water.csv"), Crossings = Read("crossings.csv"), Palisade = Read("palisade.csv"), Forest = Read("forest.csv"),
                    BuildSpots = Read("buildspots.csv"),
                };
                foreach (var w in p.Water)
                {
                    if (w.S("kind") == "stream") { p.Stream = w.Pts(); p.StreamDepth = w.N("depth"); }
                    else { var c = w.Pts()[0]; p.Ponds.Add(new Vector4(c.x, c.y, w.N("size"), w.N("depth"))); }
                }
                return p;
            }

            static List<Row> Read(string file)
            {
                var rows = new List<Row>();
                string path = PlanFolder + "/" + file;
                if (!File.Exists(path)) { Debug.LogWarning("VillageSetup: Plan-Datei fehlt: " + path + " (art-src/village/plan.py ausführen)"); return rows; }
                var lines = File.ReadAllLines(path);
                if (lines.Length == 0) return rows;
                var head = lines[0].Trim().Split(',');
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cells = lines[i].Trim().Split(',');
                    var r = new Row();
                    for (int k = 0; k < head.Length && k < cells.Length; k++) r.F[head[k]] = cells[k];
                    rows.Add(r);
                }
                return rows;
            }
        }

        // ---------------------------------------------------------------- Geometrie-Hilfen

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float l2 = d.sqrMagnitude;
            float t = l2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, d) / l2);
            return Vector2.Distance(p, a + d * t);
        }

        public static float PolyDist(Vector2 p, List<Vector2> pts)
        {
            if (pts.Count == 1) return Vector2.Distance(p, pts[0]);
            float m = float.MaxValue;
            for (int i = 0; i + 1 < pts.Count; i++) m = Mathf.Min(m, SegDist(p, pts[i], pts[i + 1]));
            return m;
        }

        static float SDist(float x, float z, float p = 6f) => Mathf.Pow(Mathf.Pow(Mathf.Abs(x), p) + Mathf.Pow(Mathf.Abs(z), p), 1f / p);
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // Höhe über dem Spielniveau (Welt-y) – Hang/Waldring, Bachbett, Teiche
        public static float Height(Plan plan, float x, float z)
        {
            var p = new Vector2(x, z);
            float ds = plan.Stream.Count > 1 ? PolyDist(p, plan.Stream) : 999f;
            float hill = 0f;
            float d = SDist(x, z);
            if (d > Flat)
            {
                float t = Mathf.Clamp01((d - Flat) / (80f - Flat));
                float n = Mathf.PerlinNoise(x * 0.06f + 3.1f, z * 0.06f + 7.3f);
                hill = HillHeight * Smooth(t) + (n - 0.5f) * 3f * t;
                hill *= Smooth((ds - 2f) / 10f); // Bachtal im Hang
            }
            float bed = 0f;
            if (ds < StreamHalf) bed = -plan.StreamDepth;
            else if (ds < StreamHalf + StreamBank) bed = -plan.StreamDepth * (1f - Smooth((ds - StreamHalf) / StreamBank));
            foreach (var pond in plan.Ponds)
            {
                float dp = Vector2.Distance(p, new Vector2(pond.x, pond.y)) - pond.z;
                float h = dp < -1f ? -pond.w : (dp < 1.2f ? -pond.w * (1f - Smooth((dp + 1f) / 2.2f)) : 0f);
                bed = Mathf.Min(bed, h);
            }
            return hill + bed;
        }

        static float GroundY(float x, float z)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : 0f;
        }

        // ---------------------------------------------------------------- 2. Gelände

        static void BuildTerrain(Transform env, Plan plan, StringBuilder sb)
        {
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (td == null)
            {
                td = new TerrainData();
                AssetDatabase.CreateAsset(td, TerrainDataPath);
            }
            td.heightmapResolution = 513;
            td.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            td.alphamapResolution = 512;
            td.baseMapResolution = 512;
            td.SetDetailResolution(416, 16);
            td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode); // wie GrassTerrain (neue TerrainData: Coverage)

            int hr = td.heightmapResolution;
            var h = new float[hr, hr];
            for (int y = 0; y < hr; y++)
                for (int x = 0; x < hr; x++)
                {
                    float wx = -TerrainSize / 2f + x / (float)(hr - 1) * TerrainSize;
                    float wz = -TerrainSize / 2f + y / (float)(hr - 1) * TerrainSize;
                    h[y, x] = (Height(plan, wx, wz) + TerrainBase) / TerrainHeight;
                }
            td.SetHeights(0, 0, h);

            td.terrainLayers = EnsureLayers();
            PaintSplat(td, plan);

            // Gras-Prototypen wie in der Stadt (eigene Detail-Prefabs GrasDetail_*)
            var city = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainGrass.TerrainDataPath);
            if (city != null)
            {
                td.detailPrototypes = city.detailPrototypes;
                td.wavingGrassTint = city.wavingGrassTint;
                td.wavingGrassAmount = city.wavingGrassAmount;
                td.wavingGrassSpeed = city.wavingGrassSpeed;
                td.wavingGrassStrength = city.wavingGrassStrength;
            }
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            // Terrain-Objekt
            var tt = env.Find("Terrain");
            GameObject go;
            if (tt == null)
            {
                go = Terrain.CreateTerrainGameObject(td);
                go.name = "Terrain";
                go.transform.SetParent(env, false);
            }
            else go = tt.gameObject;
            go.transform.position = new Vector3(-TerrainSize / 2f, -TerrainBase, -TerrainSize / 2f);
            go.layer = FloorLayer;
            go.tag = "Floor";
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.terrainData = td;
            go.GetComponent<TerrainCollider>().terrainData = td;
            terrain.materialTemplate = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("594ea882c5a793440b60ff72d896021e"));
            terrain.detailObjectDistance = 80f;
            terrain.heightmapPixelError = 5f;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            terrain.basemapDistance = 1000f;
            sb.AppendLine($"Gelände: {TerrainDataPath} (Heightmap {hr}, 5 Layer)");
        }

        static TerrainLayer[] EnsureLayers()
        {
            EnsureFolder(LayerFolder);
            (string name, string tex, float tile)[] defs =
            {
                ("Walddorf_Wiese", TextureFolder + "/walddorf_wiese.png", 5f),
                ("Walddorf_Fels", "Assets/_Game/Environment/rock_painterly.png", 6f),
                ("Walddorf_Erdweg", TextureFolder + "/walddorf_erdweg.png", 4.5f),
                ("Walddorf_Waldboden", TextureFolder + "/walddorf_waldboden.png", 5f),
                ("Walddorf_Pflaster", "Assets/_Game/Environment/Town/plaza_stone.png", 6f),
            };
            var layers = new TerrainLayer[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                string path = LayerFolder + "/" + defs[i].name + ".terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
                l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(defs[i].tex);
                if (l.diffuseTexture == null) Debug.LogWarning("VillageSetup: Textur fehlt " + defs[i].tex);
                l.tileSize = new Vector2(defs[i].tile, defs[i].tile);
                l.smoothness = 0f;
                l.metallic = 0f;
                EditorUtility.SetDirty(l);
                layers[i] = l;
            }
            return layers;
        }

        // Gewichte je Weltpunkt: 0 Wiese, 1 Fels, 2 Erdweg, 3 Waldboden, 4 Pflaster
        static void Weights(Plan plan, TerrainData td, float wx, float wz, float[] w)
        {
            var p = new Vector2(wx, wz);
            float earth = 0f, plaza = 0f, rock = 0f;
            foreach (var r in plan.Paths)
            {
                string kind = r.S("kind"), layer = r.S("layer");
                var pts = r.Pts();
                if (kind == "plaza" || layer == "field")
                {
                    float x0 = Mathf.Min(pts[0].x, pts[1].x), x1 = Mathf.Max(pts[0].x, pts[1].x);
                    float z0 = Mathf.Min(pts[0].y, pts[1].y), z1 = Mathf.Max(pts[0].y, pts[1].y);
                    float inside = Mathf.Min(Mathf.Min(wx - x0, x1 - wx), Mathf.Min(wz - z0, z1 - wz));
                    float e = Smooth((inside + 0.4f) / 0.8f);
                    if (kind == "plaza") plaza = Mathf.Max(plaza, e);
                    else
                    {
                        float stripe = Mathf.Repeat(wz, 1.4f) < 0.7f ? 0.9f : 0.55f; // Ackerfurchen
                        earth = Mathf.Max(earth, e * stripe);
                    }
                    continue;
                }
                float half = r.N("width") / 2f;
                float ds = PolyDist(p, pts);
                float v = 1f - Smooth((ds - (half - 0.4f)) / 0.8f);
                if (kind == "field") v *= 0.55f + 0.35f * Mathf.PerlinNoise(wx * 0.35f, wz * 0.35f);
                if (layer == "plaza") plaza = Mathf.Max(plaza, v); else earth = Mathf.Max(earth, v);
            }
            if (plan.Stream.Count > 1)
            {
                float ds = PolyDist(p, plan.Stream);
                float b = 1f - Smooth((ds - StreamHalf) / StreamBank);
                earth = Mathf.Max(earth, 0.55f * b);
                rock = Mathf.Max(rock, 0.45f * b);
            }
            foreach (var pond in plan.Ponds)
            {
                float dp = Vector2.Distance(p, new Vector2(pond.x, pond.y)) - pond.z;
                float b = 1f - Smooth((dp + 0.5f) / 1.5f);
                earth = Mathf.Max(earth, 0.55f * b);
                rock = Mathf.Max(rock, 0.45f * b);
            }
            float nx = (wx + TerrainSize / 2f) / TerrainSize, nz = (wz + TerrainSize / 2f) / TerrainSize;
            float steep = td.GetSteepness(nx, nz);
            rock = Mathf.Max(rock, Mathf.Clamp01((steep - 33f) / 10f) * 0.8f);
            float d = SDist(wx, wz);
            float forest = Smooth((d - 50f) / 8f) * (0.75f + 0.25f * Mathf.PerlinNoise(wx * 0.2f + 40f, wz * 0.2f + 9f));

            float w4 = Mathf.Clamp01(plaza);
            float w2 = Mathf.Clamp01(earth) * (1f - w4);
            float rem = 1f - w4 - w2;
            float w1 = Mathf.Clamp01(rock) * rem;
            float rem2 = rem - w1;
            float w3 = forest * rem2;
            w[0] = rem2 - w3; w[1] = w1; w[2] = w2; w[3] = w3; w[4] = w4;
        }

        static float[,,] _splat;

        static void PaintSplat(TerrainData td, Plan plan)
        {
            int ar = td.alphamapResolution;
            var am = new float[ar, ar, 5];
            var w = new float[5];
            for (int y = 0; y < ar; y++)
                for (int x = 0; x < ar; x++)
                {
                    float wx = -TerrainSize / 2f + (x + 0.5f) / ar * TerrainSize;
                    float wz = -TerrainSize / 2f + (y + 0.5f) / ar * TerrainSize;
                    Weights(plan, td, wx, wz, w);
                    for (int l = 0; l < 5; l++) am[y, x, l] = w[l];
                }
            td.SetAlphamaps(0, 0, am);
            _splat = am;
        }

        // ---------------------------------------------------------------- 3. Platzierung

        static void PlaceAnchors(Plan plan, StringBuilder sb)
        {
            foreach (var a in plan.Anchors)
            {
                string name = a.S("name"), kind = a.S("kind");
                float x = a.N("x"), z = a.N("z"), yaw = a.N("yaw");
                GameObject go = null;
                if (kind == "nexus") go = GameObject.Find("Nexus");
                else if (kind == "player") go = GameObject.Find("NetBootstrap");
                else go = GameObject.Find("Environment/Welt/" + name);
                if (go == null) { sb.AppendLine("Anker fehlt: " + name); continue; }
                float y = GroundY(x, z) + (kind == "player" ? 1.35f : 0f);
                go.transform.position = new Vector3(x, y, z);
                if (kind != "nexus") go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            sb.AppendLine($"Anker: {plan.Anchors.Count} gesetzt");
        }

        // WaveManager.SpawnPoints (Rückfall ohne Portale, Prefab: 2 leere Einträge, #262) → Spawnpunkte der Portale
        static void WireSpawnPoints(StringBuilder sb)
        {
            var wm = Object.FindFirstObjectByType<WaveManager>();
            if (wm == null) return;
            wm.SpawnPoints = new List<Transform>();
            foreach (var p in Object.FindObjectsByType<SpawnPortal>(FindObjectsSortMode.InstanceID))
                if (p.SpawnPoint != null) wm.SpawnPoints.Add(p.SpawnPoint);
            PrefabUtility.RecordPrefabInstancePropertyModifications(wm);
            EditorUtility.SetDirty(wm);
            sb.AppendLine($"WaveManager.SpawnPoints: {wm.SpawnPoints.Count} Portal-Spawnpunkte");
        }

        // Nach dem Eintragen in die Build Settings (Koordinator): NetworkObject.OnValidate erneut ausführen, damit NGO
        // das NetGame als in-scene placed markiert (m_InScenePlaced = 1 nur bei buildIndex >= 0) und den Hash setzt.
        [MenuItem("BuddyTD/Walddorf/NetGame nachziehen (nach Build Settings)")]
        public static void RefreshNetGameMenu() => Debug.Log(RefreshNetGame());

        public static string RefreshNetGame()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "VillageSetup: erst " + ScenePath + " öffnen.";
            var ng = GameObject.Find("NetGame");
            var no = ng != null ? ng.GetComponent<NetworkObject>() : null;
            if (no == null) return "NetGame fehlt";
            var m = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            m?.Invoke(no, null);
            EditorUtility.SetDirty(no);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "Build-Index " + scene.buildIndex + ", " + NetHashInfo();
        }

        static Transform ClearGroup(Transform parent, string name)
        {
            var g = Child(parent, name);
            for (int i = g.childCount - 1; i >= 0; i--) Object.DestroyImmediate(g.GetChild(i).gameObject);
            g.localPosition = Vector3.zero;
            g.localRotation = Quaternion.identity;
            return g;
        }

        static GameObject Spawn(string prefabPath, Transform parent, string name, float x, float z, float yaw, Vector3 scale, float sink = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogWarning("VillageSetup: Prefab fehlt " + prefabPath); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (!string.IsNullOrEmpty(name)) go.name = name;
            go.transform.position = new Vector3(x, GroundY(x, z) - sink, z);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale;
            return go;
        }

        // Palisade aus palisade.csv: Spalte „prefab“ (eigene Holzpalisade, 2026-10-16) – fehlt sie oder das Prefab,
        // gelten die Platzhalter (ArenaMauer-Segment / Zylinder-Pfosten). Lokales +Z der Prefabs zeigt nach außen.
        static void BuildPalisade(Transform dorf, Plan plan, StringBuilder sb)
        {
            var seg = EnsurePalisadePrefab();
            var post = EnsurePostPrefab();
            var g = ClearGroup(dorf, "Palisade");
            int n = 0, real = 0;
            foreach (var r in plan.Palisade)
            {
                string kind = r.S("kind");
                bool isSeg = kind == "segment";
                string path = r.S("prefab");
                if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    if (kind == "gate") continue; // Platzhalter-Tore = offene Lücke
                    path = isSeg ? seg : post;
                }
                else real++;
                var go = Spawn(path, g, r.S("name"), r.N("x"), r.N("z"), r.N("yaw"),
                    isSeg ? new Vector3(r.N("scale_x"), 1f, 1f) : Vector3.one);
                if (go != null) { SetStaticRecursive(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic); n++; }
            }
            sb.AppendLine($"Palisade: {n} Teile (davon {real} eigene Modelle)");
        }

        static void SetStaticRecursive(GameObject go, StaticEditorFlags flags)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        static List<KeyValuePair<BuildingDamage, int>> BuildBuildings(Transform dorf, Plan plan, StringBuilder sb)
        {
            var g = ClearGroup(dorf, "Gebaeude");
            var list = new List<KeyValuePair<BuildingDamage, int>>();
            foreach (var r in plan.Buildings)
            {
                float s = r.N("scale");
                var go = Spawn(r.S("prefab"), g, r.S("name"), r.N("x"), r.N("z"), r.N("yaw"), Vector3.one * s);
                if (go == null) continue;
                var bd = go.GetComponent<BuildingDamage>();
                if (bd != null) list.Add(new KeyValuePair<BuildingDamage, int>(bd, r.I("burn")));
            }
            sb.AppendLine($"Gebäude: {g.childCount} (davon {list.Count} mit BuildingDamage)");
            return list;
        }

        static void BuildProps(Transform env, Transform dorf, Plan plan, StringBuilder sb)
        {
            var groups = new Dictionary<string, Transform>();
            var counts = new Dictionary<string, int>();
            foreach (var r in plan.Props)
            {
                string grp = r.S("group");
                if (!groups.TryGetValue(grp, out var g))
                {
                    g = grp == "Dorfbaeume" ? ClearGroup(Child(env, "Vegetation"), grp) : ClearGroup(dorf, grp);
                    groups[grp] = g;
                    counts[grp] = 0;
                }
                var go = Spawn(r.S("prefab"), g, null, r.N("x"), r.N("z"), r.N("yaw"), Vector3.one * r.N("scale"));
                if (go == null) continue;
                counts[grp]++;
                if (r.I("collider") == 0)
                    foreach (var c in go.GetComponentsInChildren<Collider>(true))
                        if (!c.isTrigger) Object.DestroyImmediate(c);
                if (r.I("navmesh") == 0) IgnoreInNavMesh(go);
            }
            foreach (var kv in counts) sb.AppendLine($"{kv.Key}: {kv.Value}");
        }

        static void BuildCrossings(Transform dorf, Plan plan, StringBuilder sb)
        {
            var g = ClearGroup(dorf, "Bruecken");
            foreach (var r in plan.Crossings)
            {
                // Etappe 2: eigenes Modell aus der Spalte „prefab“ (Dorf_Bruecke/Dorf_Steg), sonst Platzhalter aus Kästen
                string path = r.S("prefab");
                if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    path = EnsureCrossingPrefab(r.S("name"), r.S("kind") == "bridge", r.N("length"), r.N("width"));
                var go = Spawn(path, g, r.S("name"), r.N("x"), r.N("z"), r.N("yaw"), Vector3.one);
                if (go != null)
                {
                    var p = go.transform.position; p.y = 0f; go.transform.position = p; // Deck auf Spielniveau, nicht auf dem Bachbett
                    if (r.S("kind") != "bridge")
                    {
                        // Steg: für die Ramme gesperrt (Voxel-Rundung ließ sonst einen schmalen Streifen übrig)
                        var rv = go.AddComponent<NavMeshModifierVolume>();
                        rv.size = new Vector3(r.N("width") + 1f, 2f, r.N("length") + 1f);
                        rv.center = Vector3.zero;
                        rv.area = 1;
                        SetAgents(rv, RamNavMesh.FindAgentType());
                    }
                }
            }
            sb.AppendLine($"Querungen: {plan.Crossings.Count}");
        }

        // Wasserfläche (Mesh), NavMesh-Bereich „Wasser“ (Furt, Kosten 5), Teich-Tiefen nicht begehbar, Bauverbot-Kästen
        static void BuildWater(Transform dorf, Plan plan, StringBuilder sb)
        {
            var g = ClearGroup(dorf, "Wasser");
            var s = plan.Stream;
            BuildWaterSurface(g, plan, sb);

            // Furt-Volumen + Bauverbot entlang des Bachs
            int furt = 0;
            for (int i = 0; i + 1 < s.Count; i++)
            {
                Vector2 a = s[i], b = s[i + 1];
                Vector2 mid = (a + b) / 2f;
                float len = Vector2.Distance(a, b);
                float yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
                var fv = new GameObject("Furt_" + i);
                fv.transform.SetParent(g, false);
                fv.transform.SetPositionAndRotation(new Vector3(mid.x, 0f, mid.y), Quaternion.Euler(0f, yaw, 0f));
                var vol = fv.AddComponent<NavMeshModifierVolume>();
                vol.size = new Vector3((StreamHalf + 0.5f) * 2f, 1.1f, len + 0.6f);
                vol.center = new Vector3(0f, -0.65f, 0f);
                vol.area = WaterArea;
                SetAgents(vol, 0); // Humanoide: Furt (Kosten 5)
                // Ramme: Bach nicht befahrbar → nur über die Hauptbrücke (Steg ist zu schmal)
                var rv = fv.AddComponent<NavMeshModifierVolume>();
                rv.size = vol.size;
                rv.center = vol.center;
                rv.area = 1;
                SetAgents(rv, RamNavMesh.FindAgentType());
                furt++;

                var bv = new GameObject("Bauverbot_" + i);
                bv.transform.SetParent(g, false);
                bv.transform.SetPositionAndRotation(new Vector3(mid.x, -0.35f, mid.y), Quaternion.Euler(0f, yaw, 0f));
                var box = bv.AddComponent<BoxCollider>();
                box.size = new Vector3(StreamHalf * 2f + 0.4f, 0.3f, len + 0.4f);
            }
            foreach (var pond in plan.Ponds)
            {
                var pv = new GameObject("Teich_Furt");
                pv.transform.SetParent(g, false);
                pv.transform.position = new Vector3(pond.x, 0f, pond.y);
                var vol = pv.AddComponent<NavMeshModifierVolume>();
                vol.size = new Vector3(pond.z * 2f + 1.5f, 1.3f, pond.z * 2f + 1.5f);
                vol.center = new Vector3(0f, -0.75f, 0f);
                vol.area = WaterArea;
                var deep = new GameObject("Teich_Tiefe");
                deep.transform.SetParent(g, false);
                deep.transform.position = new Vector3(pond.x, 0f, pond.y);
                var dv = deep.AddComponent<NavMeshModifierVolume>();
                dv.size = new Vector3(pond.z * 1.3f, 1.2f, pond.z * 1.3f);
                dv.center = new Vector3(0f, -0.85f, 0f);
                dv.area = 1; // Not Walkable
                var bv = new GameObject("Bauverbot_Teich");
                bv.transform.SetParent(g, false);
                bv.transform.position = new Vector3(pond.x, -0.4f, pond.y);
                bv.AddComponent<BoxCollider>().size = new Vector3(pond.z * 2f, 0.3f, pond.z * 2f);
            }
            sb.AppendLine($"Wasser: Mühlbach + {plan.Ponds.Count} Teiche, {furt} Furt-Volumen (Humanoid: Bereich „{WaterAreaName}“, Kosten {WaterCost}; Ramme: nicht begehbar)");
        }

        // ---------------------------------------------------------------- Wasser-Optik und Bachgeräusch (2026-10-16)
        // Eigener Shader BuddyTD/StylizedWater (Assets/_Game/Shaders/StylizedWater.shader), Texturen aus
        // art-src/village/water_textures.py, Klänge aus art-src/audio/synth_ambience.py. Rein optisch/akustisch:
        // Furt-Volumen, Bauverbot-Kästen und NavMesh bleiben unberührt.
        public const string WaterMaterialFolder = Folder + "/Materials";
        public const string WaterShaderName = "BuddyTD/StylizedWater";
        const float WaterLevel = -0.12f;
        const string StreamClipPath = "Assets/_Game/Audio/Generated/amb_bach.wav";
        const string WheelClipPath = "Assets/_Game/Audio/Generated/amb_muehlrad.wav";

        [MenuItem("BuddyTD/Walddorf/Wasser: Oberfläche, Material, Bachgeräusch (ohne NavMesh)")]
        public static void RebuildWaterLookMenu() => Debug.Log(RebuildWaterLook());

        public static string RebuildWaterLook()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "VillageSetup: erst " + ScenePath + " öffnen (aktive Szene).";
            var sb = new StringBuilder();
            var plan = Plan.Load();
            var env = GameObject.Find("Environment");
            var g = Child(Child(env.transform, "Dorf"), "Wasser");
            BuildWaterSurface(g, plan, sb);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return sb.ToString();
        }

        // Ersetzt nur die Wasserflächen und Geräusch-Objekte in der Gruppe „Wasser“ (idempotent).
        static void BuildWaterSurface(Transform g, Plan plan, StringBuilder sb)
        {
            foreach (var n in new[] { "Wasserflaeche", "Wasserflaeche_Teiche", "Bachgeraeusch", "Muehlrad_Rauschen" })
                for (int i = g.childCount - 1; i >= 0; i--)
                    if (g.GetChild(i).name == n) Object.DestroyImmediate(g.GetChild(i).gameObject);
            EnsureFolder(MeshFolder);
            EnsureFolder(WaterMaterialFolder);
            var tex = EnsureWaterTextures();
            var wheel = GameObject.Find("Muehlrad");
            var matBach = EnsureWaterMaterial("Water_Bach", 3000, tex, null);
            var matTeich = EnsureWaterMaterial("Water_Teich", 2999, tex, wheel != null ? wheel.transform : null);

            var bach = BuildStreamMesh(plan, out float length);
            var teiche = BuildPondMesh(plan);
            // Wasser.asset behält seine GUID (Bach), Teiche in eigenem Mesh
            var mb = SaveMesh(bach, MeshFolder + "/Wasser.asset");
            var mt = SaveMesh(teiche, MeshFolder + "/Wasser_Teiche.asset");
            MakeWaterRenderer(g, "Wasserflaeche", mb, matBach);
            MakeWaterRenderer(g, "Wasserflaeche_Teiche", mt, matTeich);
            sb.AppendLine($"Wasser-Optik: Bach {mb.vertexCount} Vertices ({length:F1} m, UV V entlang), Teiche {mt.vertexCount} Vertices; Shader {WaterShaderName}, Materialien Water_Bach/Water_Teich");

            // Bachgeräusch: eine Quelle, die auf den nächsten Punkt der Bachlinie wandert
            EnsureLoopImport(StreamClipPath);
            EnsureLoopImport(WheelClipPath);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(StreamClipPath);
            if (clip != null && plan.Stream.Count > 1)
            {
                var go = new GameObject("Bachgeraeusch");
                go.transform.SetParent(g, false);
                var amb = go.AddComponent<StreamAmbience>();
                var pts = new Vector3[plan.Stream.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(plan.Stream[i].x, WaterLevel, plan.Stream[i].y);
                amb.Points = pts; amb.Clip = clip; amb.Volume = 0.42f; amb.FullDistance = 3.5f; amb.FadeDistance = 17f; amb.SpatialBlend = 0.6f;
                sb.AppendLine($"Bachgeräusch: {pts.Length} Linienpunkte, voll ≤ {amb.FullDistance} m, still ab {amb.FadeDistance} m, Lautstärke {amb.Volume}");
            }
            else sb.AppendLine("WARNUNG: " + StreamClipPath + " fehlt (art-src/audio/synth_ambience.py ausführen)");
            var wclip = AssetDatabase.LoadAssetAtPath<AudioClip>(WheelClipPath);
            if (wclip != null && wheel != null)
            {
                var go = new GameObject("Muehlrad_Rauschen");
                go.transform.SetParent(g, false);
                var amb = go.AddComponent<StreamAmbience>();
                var w = wheel.transform.position;
                amb.Points = new[] { new Vector3(w.x, 0.3f, w.z) };
                amb.Clip = wclip; amb.Volume = 0.36f; amb.FullDistance = 2.5f; amb.FadeDistance = 13f; amb.SpatialBlend = 0.7f; amb.Height = 0.5f;
                sb.AppendLine($"Mühlrad-Rauschen bei ({w.x:F1}; {w.z:F1}), voll ≤ {amb.FullDistance} m, still ab {amb.FadeDistance} m");
            }
        }

        // Loops: PCM (keine Kodierlücke an der Naht), ohne Unity-„Normalize“ (Pegel kommt aus dem Mastering in
        // synth_ambience.py), beim Laden entpackt, im Hintergrund geladen.
        static void EnsureLoopImport(string path)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (ai == null) return;
            var st = ai.defaultSampleSettings;
            var so = new SerializedObject(ai);
            var norm = so.FindProperty("m_Normalize") ?? so.FindProperty("normalize");
            bool dirty = (norm != null && norm.boolValue) || !ai.forceToMono || !ai.loadInBackground
                || st.compressionFormat != AudioCompressionFormat.PCM || st.loadType != AudioClipLoadType.DecompressOnLoad;
            if (!dirty) return;
            if (norm != null) { norm.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            ai.forceToMono = true;
            ai.loadInBackground = true;
            st.compressionFormat = AudioCompressionFormat.PCM;
            st.loadType = AudioClipLoadType.DecompressOnLoad;
            ai.defaultSampleSettings = st;
            ai.SaveAndReimport();
        }

        static void MakeWaterRenderer(Transform g, string name, Mesh mesh, Material mat)
        {
            var water = new GameObject(name);
            water.transform.SetParent(g, false);
            water.layer = WaterLayer;
            water.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = water.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            IgnoreInNavMesh(water);
        }

        static Mesh SaveMesh(Mesh src, string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(src.vertices);
            mesh.SetUVs(0, src.uv);
            mesh.SetUVs(1, src.uv2);
            mesh.SetColors(src.colors);
            mesh.SetTriangles(src.triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            Object.DestroyImmediate(src);
            return mesh;
        }

        // Strömungs-Faktor: 0 im Teich (stehendes Wasser), steigt nach dem Teichrand über 4 m auf 1
        static float FlowFactor(Plan plan, Vector2 p)
        {
            float f = 1f;
            foreach (var pond in plan.Ponds)
            {
                float d = Vector2.Distance(p, new Vector2(pond.x, pond.y)) - (pond.z + 1.3f);
                f = Mathf.Min(f, Smooth(d / 4f));
            }
            return f;
        }

        static Color WaterVertexColor(Plan plan, Vector2 p, float flow)
        {
            float depth = Mathf.Clamp01(WaterLevel - Height(plan, p.x, p.y));
            return new Color(flow, depth, 0f, 1f);
        }

        // Bach als Band: U quer (m, + = links der Fließrichtung), V entlang (m ab Quelle), Fließrichtung in uv1.
        // Die Mittellinie folgt den Plansegmenten (wie das Bachbett im Gelände); Querrichtung weich zwischen den Ecken.
        static Mesh BuildStreamMesh(Plan plan, out float length)
        {
            var s = plan.Stream;
            // bis über die Uferlinie hinaus (Wasser −0,12 trifft die Böschung bei ≈ 2,5 m), der Rest liegt unter dem Gelände
            float rib = StreamHalf + StreamBank + 0.1f;
            const int cols = 8;
            const float step = 0.75f;
            var verts = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var colr = new List<Color>(); var tris = new List<int>();
            var corner = new Vector2[s.Count];
            for (int i = 0; i < s.Count; i++)
                corner[i] = (i == 0 ? s[1] - s[0] : (i == s.Count - 1 ? s[i] - s[i - 1] : (s[i + 1] - s[i]).normalized + (s[i] - s[i - 1]).normalized)).normalized;
            float v = 0f;
            int rows = 0;
            for (int k = 0; k + 1 < s.Count; k++)
            {
                float segLen = Vector2.Distance(s[k], s[k + 1]);
                int n = Mathf.Max(1, Mathf.CeilToInt(segLen / step));
                for (int j = (k == 0 ? 0 : 1); j <= n; j++)
                {
                    float f = j / (float)n;
                    Vector2 c = Vector2.Lerp(s[k], s[k + 1], f);
                    Vector2 dir = Vector2.Lerp(corner[k], corner[k + 1], f).normalized;
                    Vector2 nrm = new Vector2(-dir.y, dir.x);
                    float vv = v + segLen * f;
                    for (int q = 0; q <= cols; q++)
                    {
                        float u = rib - 2f * rib * q / cols;
                        Vector2 p = c + nrm * u;
                        verts.Add(new Vector3(p.x, WaterLevel, p.y));
                        uv.Add(new Vector2(u, vv));
                        uv2.Add(dir);
                        colr.Add(WaterVertexColor(plan, p, FlowFactor(plan, p)));
                    }
                    if (rows > 0)
                    {
                        int b = (rows - 1) * (cols + 1), t0 = rows * (cols + 1);
                        for (int q = 0; q < cols; q++)
                            tris.AddRange(new[] { b + q, t0 + q, b + q + 1, b + q + 1, t0 + q, t0 + q + 1 });
                    }
                    rows++;
                }
                v += segLen;
            }
            length = v;
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetColors(colr); m.SetTriangles(tris, 0);
            return m;
        }

        // Teiche als Ringe (Vertex-Tiefe als Rückfall gut aufgelöst), UV = Welt-XZ, Strömung 0 = stehendes Wasser
        static Mesh BuildPondMesh(Plan plan)
        {
            var verts = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var colr = new List<Color>(); var tris = new List<int>();
            const int seg = 48, rings = 8;
            foreach (var pond in plan.Ponds)
            {
                float R = pond.z + 1.3f; // Uferlinie bei ≈ r + 0,7 m, Rand unter dem Gelände
                int c = verts.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float rad = R * r / rings;
                    int cnt = r == 0 ? 1 : seg;
                    for (int k = 0; k < cnt; k++)
                    {
                        float a = k * Mathf.PI * 2f / seg;
                        var p = new Vector2(pond.x + Mathf.Cos(a) * rad, pond.y + Mathf.Sin(a) * rad);
                        verts.Add(new Vector3(p.x, WaterLevel, p.y));
                        uv.Add(p);
                        uv2.Add(new Vector2(0f, 1f));
                        colr.Add(WaterVertexColor(plan, p, 0f));
                    }
                }
                for (int k = 0; k < seg; k++) tris.AddRange(new[] { c, c + 1 + (k + 1) % seg, c + 1 + k });
                for (int r = 1; r < rings; r++)
                {
                    int a0 = c + 1 + (r - 1) * seg, a1 = c + 1 + r * seg;
                    for (int k = 0; k < seg; k++)
                    {
                        int k1 = (k + 1) % seg;
                        tris.AddRange(new[] { a0 + k, a0 + k1, a1 + k, a1 + k, a0 + k1, a1 + k1 });
                    }
                }
            }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetColors(colr); m.SetTriangles(tris, 0);
            return m;
        }

        static Texture2D[] EnsureWaterTextures()
        {
            string nPath = TextureFolder + "/walddorf_wasser_normal.png", zPath = TextureFolder + "/walddorf_wasser_noise.png";
            if (!File.Exists(nPath) || !File.Exists(zPath))
            {
                Debug.LogWarning("VillageSetup: Wasser-Texturen fehlen (art-src/village/water_textures.py ausführen)");
                return new Texture2D[2];
            }
            foreach (var path in new[] { nPath, zPath })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null) { AssetDatabase.ImportAsset(path); ti = (TextureImporter)AssetImporter.GetAtPath(path); }
                bool isNormal = path == nPath;
                bool dirty = false;
                if (isNormal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (!isNormal && (ti.sRGBTexture || ti.alphaSource != TextureImporterAlphaSource.FromInput || ti.alphaIsTransparency))
                { ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false; dirty = true; }
                if (ti.wrapMode != TextureWrapMode.Repeat || ti.anisoLevel != 4 || !ti.mipmapEnabled)
                { ti.wrapMode = TextureWrapMode.Repeat; ti.anisoLevel = 4; ti.mipmapEnabled = true; dirty = true; }
                if (ti.maxTextureSize != 512) { ti.maxTextureSize = 512; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            return new[] { AssetDatabase.LoadAssetAtPath<Texture2D>(nPath), AssetDatabase.LoadAssetAtPath<Texture2D>(zPath) };
        }

        // Material anlegen, falls es fehlt; vorhandene Werte (im Inspector abgestimmt) bleiben erhalten.
        // Immer gesetzt: Shader, Queue (Teich vor Bach → Stencil), Texturen, Strudel-Mitte am Mühlrad.
        static Material EnsureWaterMaterial(string name, int queue, Texture2D[] tex, Transform wheel)
        {
            string path = WaterMaterialFolder + "/" + name + ".mat";
            var shader = Shader.Find(WaterShaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = false;
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
                created = true;
            }
            if (mat.shader != shader && shader != null) mat.shader = shader;
            mat.renderQueue = queue;
            if (tex[0] != null) mat.SetTexture("_NormalTex", tex[0]);
            if (tex[1] != null) mat.SetTexture("_NoiseTex", tex[1]);
            if (created && name == "Water_Teich")
            {
                mat.SetFloat("_WaveStrength", 0.45f);
                mat.SetFloat("_StreakStrength", 0f);
            }
            if (wheel != null)
            {
                // Strudel am Teichrand unter dem Mühlrad (dort fällt das Wasser aus dem Rad zurück)
                var pond = Plan.Load().Ponds;
                Vector2 w = new Vector2(wheel.position.x, wheel.position.z);
                Vector2 best = w; float bd = float.MaxValue;
                foreach (var p in pond)
                {
                    var c = new Vector2(p.x, p.y);
                    float d = Vector2.Distance(w, c) - p.z;
                    if (d < bd) { bd = d; best = c + (w - c).normalized * (p.z - 0.4f); }
                }
                var sw = mat.GetVector("_SwirlPos");
                if (created || sw.w <= 0f) { sw.z = 2.6f; sw.w = 0.85f; }
                sw.x = best.x; sw.y = best.y;
                mat.SetVector("_SwirlPos", sw);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetAgents(NavMeshModifierVolume v, int agentType)
        {
            var so = new SerializedObject(v);
            var list = so.FindProperty("m_AffectedAgents");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).intValue = agentType;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildForest(Transform env, Plan plan, StringBuilder sb)
        {
            var veg = Child(env, "Vegetation");
            var g = ClearGroup(veg, "Wald");
            int n = 0, far = 0;
            var variantCache = new Dictionary<string, string>();
            foreach (var r in plan.Forest)
            {
                string prefab = r.S("prefab");
                bool coll = r.I("collider") == 1;
                if (!coll)
                {
                    if (!variantCache.TryGetValue(prefab, out var v)) variantCache[prefab] = v = EnsureFarVariant(prefab);
                    prefab = v;
                    far++;
                }
                var go = Spawn(prefab, g, null, r.N("x"), r.N("z"), r.N("yaw"), Vector3.one * r.N("scale"), 0.15f);
                if (go != null) n++;
            }
            sb.AppendLine($"Wald: {n} Objekte (davon {far} ohne Collider/Durchsicht jenseits der Kartengrenze)");
        }

        static void BuildStalls(Transform dorf, StringBuilder sb)
        {
            var group = Child(dorf, "Haendler");
            var build = typeof(MerchantSetup).GetMethod("BuildStall", BindingFlags.NonPublic | BindingFlags.Static);
            if (build == null) { sb.AppendLine("Händler: MerchantSetup.BuildStall nicht gefunden"); return; }
            foreach (var def in MerchantSetup.Stalls)
                build.Invoke(null, new object[] { group, def, sb });
        }

        const float DayFog = 0f;

        static void SetupAtmosphere(Transform dorf, List<KeyValuePair<BuildingDamage, int>> buildings, StringBuilder sb)
        {
            var bel = Child(dorf, "Belagerung");
            var atmo = bel.GetComponent<SiegeAtmosphere>();
            var prog = bel.GetComponent<SiegeProgression>();
            var sunGo = GameObject.Find("Directional Light");
            var sun = sunGo != null ? sunGo.GetComponent<Light>() : null;

            // Grundlicht: später Nachmittag, Sonne aus Südwest (Licht fällt nach Nordost), warm; leichter Dunst
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(38f, 45f, 0f);
                sun.color = new Color(1f, 0.87f, 0.68f);
                sun.intensity = 1.15f;
                sun.shadowStrength = 0.9f;
                RenderSettings.sun = sun;
            }
            // Tag ohne Nebel: MinimapUI rendert die Karte einmal aus 200 m Höhe – mit Nebel wird sie grau (Wunsch: Nebel dort
            // abschalten, dann DayFog wieder auf 0,0055). Ab Nachmittag Waldnebel über die Keyframes.
            RenderSettings.fog = DayFog > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.7f);
            RenderSettings.fogDensity = DayFog;

            if (atmo != null)
            {
                atmo.DayCaptured = false;
                SiegeSetup.CaptureDay(atmo);
                SiegeSetup.DefaultKeys(atmo);
                atmo.Day.FogColor = RenderSettings.fogColor;
                atmo.Day.FogDensity = RenderSettings.fogDensity;
                // Sonne wandert nach Westen und sinkt; Waldnebel nachts dichter und bläulich-grün
                atmo.Afternoon.SunEuler = new Vector3(26f, 70f, 0f);
                atmo.Afternoon.FogColor = new Color(0.84f, 0.76f, 0.6f); atmo.Afternoon.FogDensity = 0.0065f;
                atmo.Dusk.SunEuler = new Vector3(11f, 88f, 0f);
                atmo.Dusk.FogColor = new Color(0.44f, 0.36f, 0.42f); atmo.Dusk.FogDensity = 0.01f;
                atmo.Night.FogColor = new Color(0.08f, 0.15f, 0.16f); atmo.Night.FogDensity = 0.0145f;
                // Fensterglühen: Dorf-Material (alle Dorfgebäude), sonst das der Stadt (Platzhalter)
                atmo.WindowMaterial = AssetDatabase.LoadAssetAtPath<Material>(VillageMaterialFolder + "/Village_WindowGlow.mat");
                if (atmo.WindowMaterial == null)
                    atmo.WindowMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("25a178b0ef419419782f341a012c8bcd"));

                // Laternen = Fackel-Platzhalter + Händlerlampen
                atmo.Lanterns = new List<Light>();
                atmo.LanternBase = new List<float>();
                foreach (var grp in new[] { "Fackeln", "Haendler" })
                {
                    var t = dorf.Find(grp);
                    if (t == null) continue;
                    foreach (var l in t.GetComponentsInChildren<Light>(true))
                    {
                        if (l.type != LightType.Point || l.name == "Leuchte") continue;
                        atmo.Lanterns.Add(l);
                        atmo.LanternBase.Add(l.intensity);
                    }
                }
                EditorUtility.SetDirty(atmo);
            }
            if (prog != null)
            {
                prog.Atmosphere = atmo;
                prog.Buildings = new List<SiegeProgression.BuildingStage>();
                int max = 0;
                foreach (var kv in buildings)
                {
                    int burn = kv.Value;
                    prog.Buildings.Add(new SiegeProgression.BuildingStage { Building = kv.Key, BurnWave = burn, RuinWave = burn > 0 ? burn + 3 : 0 });
                    max = Mathf.Max(max, burn);
                }
                prog.MaxWave = Mathf.Max(14, max);
                prog.Fences = new List<SiegeProgression.FenceStage>();
                EditorUtility.SetDirty(prog);
            }
            sb.AppendLine($"Stimmung: Sonne (38, 45), Tagnebel {DayFog}; Laternen {(atmo != null ? atmo.Lanterns.Count : 0)}; Belagerung {buildings.Count} Gebäude");
        }

        // ---------------------------------------------------------------- Gras

        static void ScatterGrass(TerrainData td, Plan plan, StringBuilder sb)
        {
            if (td.detailPrototypes.Length < 4) { sb.AppendLine("Gras: keine Detail-Prototypen"); return; }
            int res = td.detailResolution;
            var maps = new int[4][,];
            for (int l = 0; l < 4; l++) maps[l] = new int[res, res];

            // Hindernisse (Gebäude, Palisade, Stände, Brücken) als Kästen
            var boxes = new List<BoxCollider>();
            var env = GameObject.Find("Environment/Dorf");
            foreach (var grp in new[] { "Gebaeude", "Palisade", "Haendler", "Bruecken", "Requisiten" })
            {
                var t = env.transform.Find(grp);
                if (t != null) boxes.AddRange(t.GetComponentsInChildren<BoxCollider>());
            }
            var aabbs = new List<Bounds>();
            foreach (var b in boxes) { var bb = b.bounds; bb.Expand(0.6f); aabbs.Add(bb); }

            var rnd = new System.Random(12);
            int ar = td.alphamapResolution;
            int cells = 0;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float nx = (x + 0.5f) / res, nz = (y + 0.5f) / res;
                    float wx = -TerrainSize / 2f + nx * TerrainSize, wz = -TerrainSize / 2f + nz * TerrainSize;
                    float d = SDist(wx, wz);
                    float steep = td.GetSteepness(nx, nz);
                    float h = Height(plan, wx, wz);
                    int count = d <= 50f ? 2 + rnd.Next(2) : 1 + rnd.Next(2);
                    if (d > 58f) count = rnd.NextDouble() < 0.3 ? 1 : 0;
                    if (steep > 26f || h < -0.06f) count = 0;
                    float nL = Mathf.PerlinNoise(wx * 0.09f + 11f, wz * 0.09f + 5f);
                    float nD = Mathf.PerlinNoise(wx * 0.07f + 51f, wz * 0.07f + 23f);
                    float nK = Mathf.PerlinNoise(wx * 0.11f + 91f, wz * 0.11f + 71f);
                    float pD = Mathf.Clamp01((nD - 0.62f) * 3f) * 0.8f;
                    float pL = Mathf.Clamp01((nL - 0.45f) * 2.2f) * 0.7f;
                    float pK = Mathf.Clamp01((nK - 0.55f) * 2.5f) * 0.7f;
                    var add = new int[4];
                    for (int k = 0; k < count; k++)
                    {
                        double r = rnd.NextDouble();
                        int v = 0;
                        if (r < pD) v = 2;
                        else if (r < pD + pL * (1f - pD)) v = 1;
                        else if (rnd.NextDouble() < pK) v = 3;
                        add[v]++;
                    }
                    if (count == 0) continue;
                    int ax = Mathf.Clamp((int)(nx * ar), 0, ar - 1), ay = Mathf.Clamp((int)(nz * ar), 0, ar - 1);
                    if (_splat != null && _splat[ay, ax, 2] + _splat[ay, ax, 4] > 0.25f) continue;
                    var wp = new Vector3(wx, 0.5f + Mathf.Max(0f, h), wz);
                    bool blocked = false;
                    for (int i = 0; i < boxes.Count && !blocked; i++)
                    {
                        if (!aabbs[i].Contains(wp)) continue;
                        var b = boxes[i];
                        Vector3 lp = b.transform.InverseTransformPoint(wp) - b.center;
                        Vector3 sc = b.transform.lossyScale;
                        Vector3 half = b.size * 0.5f;
                        if (Mathf.Abs(lp.x) * Mathf.Abs(sc.x) <= half.x * Mathf.Abs(sc.x) + 0.35f && Mathf.Abs(lp.z) * Mathf.Abs(sc.z) <= half.z * Mathf.Abs(sc.z) + 0.35f) blocked = true;
                    }
                    if (blocked) continue;
                    for (int l = 0; l < 4; l++) maps[l][y, x] = add[l];
                    cells++;
                }
            for (int l = 0; l < 4; l++) td.SetDetailLayer(0, 0, l, maps[l]);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"Gras: {cells} Zellen");
        }

        // ---------------------------------------------------------------- Platzhalter-Prefabs (Prefabs/Village)

        static Material WoodMaterial()
        {
            string path = MaterialFolder + "/Platzhalter_Holz.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.45f, 0.31f, 0.19f));
            m.SetFloat("_Smoothness", 0.1f);
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static string EnsurePalisadePrefab()
        {
            string path = PrefabFolder + "/Palisade_Platzhalter.prefab";
            if (File.Exists(path)) return path;
            var root = new GameObject("Palisade_Platzhalter");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Models/ArenaMauer_Segment.fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.name = "Modell";
            inst.transform.localScale = new Vector3(1f, 1.15f, 1f);
            var wood = WoodMaterial();
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = wood;
                r.sharedMaterials = mats;
            }
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(4.05f, 3f, 0.9f);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        static string EnsurePostPrefab()
        {
            string path = PrefabFolder + "/Palisade_Eckpfosten_Platzhalter.prefab";
            if (File.Exists(path)) return path;
            var root = new GameObject("Palisade_Eckpfosten_Platzhalter");
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "Pfosten";
            cyl.transform.SetParent(root.transform, false);
            cyl.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            cyl.transform.localScale = new Vector3(0.75f, 1.9f, 0.75f);
            cyl.GetComponent<MeshRenderer>().sharedMaterial = WoodMaterial();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        // ---------------------------------------------------------------- Holzpalisade (eigene Modelle, 2026-10-16)
        // Quelle: art-src/village/palisade.py (Blender) → Assets/_Game/Models/Village/*.fbx (+ Damaged/). Import wie
        // VegetationSetup (Meter, keine Collider/Animation, Normalen importieren), Material-Slots → gemeinsame URP/Lit-
        // Materialien Models/Village/Materials/<Slot>.mat mit Farben aus art-src/village/palette.json („_emission“ =
        // emissive Slots). Prefabs in Prefabs/Village mit Collidern wie die Platzhalter (Segment 4,05 × 3 × 0,9,
        // Eckpfosten Kapsel Ø 0,75 × 3,8); Torhaus: Pfosten + Flügel als Hindernis, Oberbau (Wehrgang) aus dem NavMesh.

        public const string VillageModelFolder = "Assets/_Game/Models/Village";
        public const string VillageMaterialFolder = VillageModelFolder + "/Materials";
        static string VillagePalettePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art-src/village/palette.json"));
        static readonly string[] PalisadeSegments = { "Palisade_Segment_A", "Palisade_Segment_B", "Palisade_Segment_C" };
        static readonly string[] PalisadeModels =
        {
            "Palisade_Segment_A", "Palisade_Segment_B", "Palisade_Segment_C", "Palisade_Eck", "Palisade_Tor", "Damaged/Palisade_Segment_Verkohlt",
            "Damaged/Palisade_Tor_Beschaedigt", "Damaged/Palisade_Tor_Ruine"   // Belagerung (#332, Etappe 3)
        };

        [MenuItem("BuddyTD/Walddorf/Holzpalisade: importieren, Prefabs, neu platzieren + NavMesh")]
        public static void PalisadeMenu() => Debug.Log(RebuildPalisade());

        // Import + Prefabs + nur die Gruppe Environment/Dorf/Palisade neu aufbauen, Szene speichern, NavMesh backen
        public static string RebuildPalisade()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "VillageSetup: erst " + ScenePath + " öffnen (aktive Szene).";
            var sb = new StringBuilder();
            sb.AppendLine(ImportPalisadeModels());
            sb.AppendLine(BuildPalisadePrefabs());
            var plan = Plan.Load();
            var env = GameObject.Find("Environment");
            BuildPalisade(Child(env.transform, "Dorf"), plan, sb);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine(BakeNavMesh());
            EditorSceneManager.SaveScene(scene);
            return sb.ToString();
        }

        public static string ImportPalisadeModels() => ImportPalisadeModels(PalisadeModels);

        public static string ImportPalisadeModels(string[] models)
        {
            EnsureFolder(VillageMaterialFolder);
            var palette = ReadVillagePalette(out var emission);
            var sb = new StringBuilder("Holzpalisade Import:");
            foreach (var name in models)
            {
                string path = VillageModelFolder + "/" + name + ".fbx";
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) { sb.Append($"\n  {name}: FBX fehlt"); continue; }
                imp.useFileScale = true;
                imp.globalScale = 1f;
                imp.addCollider = false;
                imp.importNormals = ModelImporterNormals.Import;
                imp.isReadable = false;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importBlendShapes = false;
                imp.importVisibility = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                imp.SaveAndReimport();
                var slots = new List<string>();
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Material m && !slots.Contains(m.name)) slots.Add(m.name);
                foreach (var kv in imp.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material) && !slots.Contains(kv.Key.name)) slots.Add(kv.Key.name);
                foreach (var slot in slots)
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), VillageMaterial(slot, palette, emission));
                imp.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                int tris = 0;
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true)) tris += mf.sharedMesh.triangles.Length / 3;
                sb.Append($"\n  {name}: {tris} Tris, Slots {string.Join(",", slots)}");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static Material VillageMaterial(string slot, Dictionary<string, Color> palette, Dictionary<string, float> emission)
        {
            string p = VillageMaterialFolder + "/" + slot + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = slot };
                AssetDatabase.CreateAsset(m, p);
            }
            // wie Stadt/Vegetation: URP/Lit, opak, matt, GPU-Instancing
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", 2f);
            m.enableInstancing = true;
            if (palette.TryGetValue(slot, out var c)) { m.SetColor("_BaseColor", c); m.SetColor("_Color", c); }
            else Debug.LogWarning("VillageSetup: keine Farbe für " + slot + " in palette.json");
            if (emission.TryGetValue(slot, out var e))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * e);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // palette.json: "Slot": "#rrggbb" (sRGB), "_emission": { "Slot": Stärke }
        static Dictionary<string, Color> ReadVillagePalette(out Dictionary<string, float> emission)
        {
            var result = new Dictionary<string, Color>();
            emission = new Dictionary<string, float>();
            if (!File.Exists(VillagePalettePath)) { Debug.LogWarning("VillageSetup: palette.json fehlt: " + VillagePalettePath); return result; }
            string json = File.ReadAllText(VillagePalettePath);
            foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*\"#([0-9a-fA-F]{6})\""))
                if (ColorUtility.TryParseHtmlString("#" + mt.Groups[2].Value, out var c)) result[mt.Groups[1].Value] = c;
            var block = System.Text.RegularExpressions.Regex.Match(json, "\"_emission\"\\s*:\\s*\\{([^}]*)\\}");
            if (block.Success)
                foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(block.Groups[1].Value, "\"(\\w+)\"\\s*:\\s*([0-9.]+)"))
                    emission[mt.Groups[1].Value] = float.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture);
            return result;
        }

        // Blender −Y (= außen) soll lokal +Z sein. Prüft am Segment, wo die Innen-Streben liegen, und dreht das Modell
        // im Prefab um 180°, falls der Export anders herum ankommt.
        static float ModelYaw()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(VillageModelFolder + "/Palisade_Segment_A.fbx");
            if (model == null) return 0f;
            var tmp = (GameObject)PrefabUtility.InstantiatePrefab(model);
            tmp.transform.position = Vector3.zero;
            var b = tmp.GetComponentInChildren<Renderer>().bounds;
            Object.DestroyImmediate(tmp);
            return b.center.z > 0f ? 180f : 0f; // Streben (innen) sollen bei −Z liegen
        }

        static GameObject NewModelRoot(string name, string fbx, float yaw)
        {
            var root = new GameObject(name);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(VillageModelFolder + "/" + fbx + ".fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.name = "Modell";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return root;
        }

        static void SavePrefab(GameObject root, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            SetStaticRecursive(root, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        public static string BuildPalisadePrefabs()
        {
            float yaw = ModelYaw();
            var sb = new StringBuilder($"Holzpalisade Prefabs (Modell-Drehung {yaw:0}°):");
            var segs = new List<string>(PalisadeSegments) { "Damaged/Palisade_Segment_Verkohlt" };
            foreach (var s in segs)
            {
                var root = NewModelRoot(Path.GetFileName(s), s, yaw);
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 1.5f, 0f);
                box.size = new Vector3(4.05f, 3f, 0.9f);
                SavePrefab(root, PrefabFolder + "/" + s + ".prefab");
                sb.Append("\n  " + s);
            }
            {
                var root = NewModelRoot("Palisade_Eck", "Palisade_Eck", yaw);
                var cap = root.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 1.9f, 0f);
                cap.radius = 0.375f;
                cap.height = 3.8f;
                SavePrefab(root, PrefabFolder + "/Palisade_Eck.prefab");
                sb.Append("\n  Palisade_Eck");
            }
            {
                var root = NewModelRoot("Palisade_Tor", "Palisade_Tor", yaw);
                var model = root.transform.Find("Modell");
                AddGateColliders(root, model, sb);
                // Oberbau (Sturz, Wehrgang, Brustwehr, Banner) nicht ins NavMesh (sonst Inseln auf dem Wehrgang)
                var top = model.Find("Torhaus_Oben");
                if (top != null) IgnoreInNavMesh(top.gameObject);
                else sb.Append("\n  WARN Torhaus_Oben fehlt");
                SavePrefab(root, PrefabFolder + "/Palisade_Tor.prefab");
                sb.Append("\n  Palisade_Tor");
            }
            sb.Append(BuildGateDamagePrefabs(yaw));
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // Pfostenpaare links/rechts (Ø 0,6, x ±4,42, Tiefe ±0,85) – die lichte Weite x −4,1…4,1 bleibt frei; Torflügel: Box aus
        // den Renderer-Grenzen (offen, fast senkrecht zur Mauer nach innen). wingSource = Modell mit Fluegel_L/R.
        static void AddGateColliders(GameObject root, Transform wingSource, StringBuilder sb)
        {
            foreach (float sx in new[] { -1f, 1f })
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(sx * 4.42f, 3f, 0f);
                box.size = new Vector3(0.7f, 6f, 1.75f);
            }
            foreach (var wing in new[] { "Fluegel_L", "Fluegel_R" })
            {
                var t = wingSource != null ? wingSource.Find(wing) : null;
                if (t == null) { sb.Append("\n  WARN Torflügel fehlt: " + wing); continue; }
                var b = t.GetComponent<Renderer>().bounds;
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(b.center.x, 1.6f, b.center.z);
                box.size = new Vector3(Mathf.Max(b.size.x, 0.25f), 3.2f, b.size.z);
            }
        }

        // ---------------------------------------------------------------- Torhaus beschädigt / Ruine (Etappe 3, #332)
        // art-src/village/palisade.py → Models/Village/Damaged/Palisade_Tor_Beschaedigt.fbx (Fire_*-Anker), _Ruine.fbx (Smoke_*).
        // Eigenständige Prefabs in Prefabs/Village/Damaged/ (Modell + dieselben Collider wie Palisade_Tor, damit Wege/NavMesh
        // gleich bleiben); in der Szene setzt SetupVillageSiege die Modelle als Belagerungs-Zustände unter die Torhäuser.
        static readonly string[] GateDamageModels = { "Damaged/Palisade_Tor_Beschaedigt", "Damaged/Palisade_Tor_Ruine" };

        static string BuildGateDamagePrefabs(float yaw)
        {
            var sb = new StringBuilder();
            var intact = AssetDatabase.LoadAssetAtPath<GameObject>(VillageModelFolder + "/Palisade_Tor.fbx");
            foreach (var m in GateDamageModels)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(VillageModelFolder + "/" + m + ".fbx") == null) { sb.Append("\n  " + m + ": FBX fehlt"); continue; }
                var root = NewModelRoot(Path.GetFileName(m), m, yaw);
                // Flügel-Collider aus dem intakten Torhaus (gleiche Lage), Pfosten wie dort
                GameObject tmp = intact != null ? (GameObject)PrefabUtility.InstantiatePrefab(intact, root.transform) : null;
                if (tmp != null) { tmp.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); }
                AddGateColliders(root, tmp != null ? tmp.transform : null, sb);
                if (tmp != null) Object.DestroyImmediate(tmp);
                SavePrefab(root, PrefabFolder + "/" + m + ".prefab");
                sb.Append("\n  " + m);
            }
            return sb.ToString();
        }

        [MenuItem("BuddyTD/Walddorf/Belagerung: Torhaus-Schaden einbauen (ohne NavMesh)")]
        public static void GateDamageMenu() => Debug.Log(RebuildGateDamage());

        // Import + Prefabs der Torhaus-Varianten, dann nur SetupVillageSiege (Zäune, verkohlte Segmente, Torhäuser), Szene
        // speichern. Das NavMesh bleibt unverändert (Collider der Torhäuser werden nicht angefasst).
        public static string RebuildGateDamage()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "VillageSetup: erst " + ScenePath + " öffnen (aktive Szene).";
            var sb = new StringBuilder();
            sb.AppendLine(ImportPalisadeModels(GateDamageModels));
            sb.AppendLine("Torhaus-Prefabs:" + BuildGateDamagePrefabs(ModelYaw()));
            var env = GameObject.Find("Environment");
            SetupVillageSiege(Child(env.transform, "Dorf"), sb);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- Dorfgebäude (Etappe 2, A-Dorf, 2026-10-16)
        // Eigene Modelle aus art-src/village/village_build.py (buildings.py, props.py) → Models/Village/*.fbx, Kennwerte in
        // art-src/village/village_export.json (Tris, Maße, Boden-Rechteck ohne Dachüberstand). Materialien wie die Palisade
        // (eines je Farbslot, URP/Lit, Smoothness 0,1, GPU-Instancing); Veg_*-Slots nutzen die Vegetation-Materialien.
        // Prefabs in Prefabs/Village/<Modell>.prefab: Kind „Model“ (FBX), Collider; Gebäude zusätzlich FadeOccluder +
        // BuildingDamage (Feuer an den Fire_*-Ankern, Glut an Smoke_*, ein FireLight) wie SiegeSetup für die Stadt.

        public static readonly string[] VillageBuildings =
        {
            "Dorf_Bauernhaus_Klein", "Dorf_Bauernhaus_B", "Dorf_Hofhaus", "Dorf_Wohnstallhaus", "Dorf_Scheune", "Dorf_Stall", "Dorf_Zimmerei",
            "Dorf_Muehle", "Dorf_Kapelle", "Dorf_Taverne", "Dorf_Schulzenhaus", "Dorf_Kornspeicher", "Dorf_Backhaus", "Dorf_Kraeuterhuette",
            "Dorf_Huehnerstall", "Dorf_Schmiede", "Palisade_Wachturm",
        };
        public static readonly string[] VillageProps =
        {
            "Dorf_Brunnen", "Dorf_Maibaum", "Dorf_Flechtzaun", "Dorf_Koppelzaun", "Dorf_Fackelpfahl", "Dorf_Heu", "Dorf_Heuschober", "Dorf_Karren",
            "Dorf_Schubkarre", "Dorf_Fass", "Dorf_Faesser", "Dorf_Kisten", "Dorf_Holzstapel", "Dorf_Beet", "Dorf_Bank", "Dorf_Holzhackklotz",
            "Dorf_Schilf", "Dorf_Bruecke", "Dorf_Steg", "Dorf_Feld_Getreide", "Dorf_Feld_Kohl", "Dorf_Dorflinde",
        };
        const string VegMaterialFolder = "Assets/_Game/Models/Vegetation/Materials";
        static string VillageExportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art-src/village/village_export.json"));

        [System.Serializable] class VillageAssetInfo { public string name = ""; public int tris; public float[] size = null; public float[] ground = null; public float[] bbox_min = null; public float[] bbox_max = null; }
        [System.Serializable] class VillageExportInfo { public VillageAssetInfo[] assets = null; }

        static Dictionary<string, VillageAssetInfo> ReadVillageExport()
        {
            var d = new Dictionary<string, VillageAssetInfo>();
            if (!File.Exists(VillageExportPath)) { Debug.LogWarning("VillageSetup: village_export.json fehlt"); return d; }
            var info = JsonUtility.FromJson<VillageExportInfo>(File.ReadAllText(VillageExportPath));
            if (info != null && info.assets != null) foreach (var a in info.assets) d[a.name] = a;
            return d;
        }

        [MenuItem("BuddyTD/Walddorf/Dorfgebäude: importieren, Prefabs, Bebauung neu + NavMesh")]
        public static void VillageMenu() => Debug.Log(RebuildVillage());

        // Import + Prefabs + nur die Bebauung (Gebäude, Requisiten/Zäune/Fackeln/Felder/Ufer, Dorfbäume, Brücken) neu,
        // Belagerung einrichten, Gras unter den neuen Grundflächen neu streuen, NavMesh backen. Gelände, Wald, Wasser und
        // Palisade bleiben (die Palisade bekommt nur die verkohlten Varianten für die Belagerung, #332).
        public static string RebuildVillage()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "VillageSetup: erst " + ScenePath + " öffnen (aktive Szene).";
            var sb = new StringBuilder();
            sb.AppendLine(ImportVillageModels());
            sb.AppendLine(BuildVillagePrefabs());
            var plan = Plan.Load();
            var env = GameObject.Find("Environment");
            var dorf = Child(env.transform, "Dorf");
            var buildings = BuildBuildings(dorf, plan, sb);
            BuildProps(env.transform, dorf, plan, sb);
            BuildCrossings(dorf, plan, sb);
            SetupAtmosphere(dorf, buildings, sb);
            SetupVillageSiege(dorf, sb);
            var terr = Terrain.activeTerrain;
            if (terr != null)
            {
                var td = terr.terrainData;
                _splat = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight); // Wege/Plätze aus dem bestehenden Anstrich
                ScatterGrass(td, plan, sb);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine(BakeNavMesh());
            EditorSceneManager.SaveScene(scene);
            return sb.ToString();
        }

        static void ImportModel(string path, Dictionary<string, Color> palette, Dictionary<string, float> emission, StringBuilder sb)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { sb.Append($"\n  {path}: FBX fehlt"); return; }
            imp.useFileScale = true;
            imp.globalScale = 1f;
            imp.addCollider = false;
            imp.importNormals = ModelImporterNormals.Import;
            imp.isReadable = false;
            imp.importAnimation = false;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.importVisibility = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.SaveAndReimport();
            var slots = new List<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Material m && !slots.Contains(m.name)) slots.Add(m.name);
            foreach (var kv in imp.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material) && !slots.Contains(kv.Key.name)) slots.Add(kv.Key.name);
            foreach (var slot in slots)
            {
                var veg = AssetDatabase.LoadAssetAtPath<Material>(VegMaterialFolder + "/" + slot + ".mat");
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), veg != null ? veg : VillageMaterial(slot, palette, emission));
            }
            imp.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int tris = 0;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true)) tris += mf.sharedMesh.triangles.Length / 3;
            sb.Append($"\n  {Path.GetFileNameWithoutExtension(path)}: {tris} Tris, {slots.Count} Slots");
        }

        public static string ImportVillageModels()
        {
            EnsureFolder(VillageMaterialFolder);
            var palette = ReadVillagePalette(out var emission);
            var sb = new StringBuilder("Dorfmodelle Import:");
            var names = new List<string>(VillageBuildings);
            names.AddRange(VillageProps);
            foreach (var n in names)
            {
                ImportModel(VillageModelFolder + "/" + n + ".fbx", palette, emission, sb);
                foreach (var v in new[] { "_Beschaedigt", "_Ruine" })
                {
                    string dp = VillageModelFolder + "/Damaged/" + n + v + ".fbx";
                    if (File.Exists(dp)) ImportModel(dp, palette, emission, sb);
                }
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // Unity-Lokalkoordinaten aus Blender (FBX Forward −Z, Up Y): x = −x, y = z, z = −y. Boden-Rechteck → (x0, z0, x1, z1)
        static Vector4 GroundRect(VillageAssetInfo a)
        {
            if (a == null || a.ground == null || a.ground.Length < 4) return new Vector4(-1f, -1f, 1f, 1f);
            return new Vector4(-a.ground[2], -a.ground[3], -a.ground[0], -a.ground[1]);
        }

        static float ModelHeight(VillageAssetInfo a) => a != null && a.bbox_max != null && a.bbox_max.Length > 2 ? a.bbox_max[2] : 2f;

        static BoxCollider AddBox(GameObject go, Vector4 r, float y0, float h)
        {
            var b = go.AddComponent<BoxCollider>();
            b.center = new Vector3((r.x + r.z) / 2f, y0 + h / 2f, (r.y + r.w) / 2f);
            b.size = new Vector3(Mathf.Max(0.1f, r.z - r.x), h, Mathf.Max(0.1f, r.w - r.y));
            return b;
        }

        static List<Vector3> AnchorsIn(Transform root, Transform variant, string prefix)
        {
            var list = new List<KeyValuePair<string, Vector3>>();
            if (variant != null)
                foreach (var t in variant.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(prefix)) list.Add(new KeyValuePair<string, Vector3>(t.name, root.InverseTransformPoint(t.position)));
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            var res = new List<Vector3>();
            foreach (var kv in list) res.Add(kv.Value);
            return res;
        }

        static Transform AddVillageVariant(Transform root, Transform model, string fbxPath, string childName)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root);
            go.name = childName;
            go.transform.localPosition = model.localPosition;
            go.transform.localRotation = model.localRotation;
            go.transform.localScale = model.localScale;
            go.SetActive(false);
            return go.transform;
        }

        static void PlaceFx(string prefabPath, Transform container, Transform root, List<Vector3> pts, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return;
            for (int i = 0; i < pts.Count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
                go.name = name + "_" + i;
                go.transform.position = root.TransformPoint(pts[i]);
                go.transform.rotation = Quaternion.identity;
            }
        }

        // Belagerungs-Einrichtung eines Dorfgebäudes (wie SiegeSetup.SetupPrefab): Varianten, Feuer, Glut, Licht, BuildingDamage
        static string SetupBuildingDamage(GameObject root, Transform model, string name, Vector4 rect, float height)
        {
            var rt = root.transform;
            var damaged = AddVillageVariant(rt, model, VillageModelFolder + "/Damaged/" + name + "_Beschaedigt.fbx", "Model_Beschaedigt");
            var ruined = AddVillageVariant(rt, model, VillageModelFolder + "/Damaged/" + name + "_Ruine.fbx", "Model_Ruine");
            var fire = AnchorsIn(rt, damaged != null ? damaged : model, "Fire_");
            if (fire.Count == 0) fire = AnchorsIn(rt, model, "Fire_");
            if (fire.Count == 0) fire.Add(new Vector3((rect.x + rect.z) / 2f, height * 0.72f, (rect.y + rect.w) / 2f));
            var smoke = AnchorsIn(rt, ruined != null ? ruined : model, "Smoke_");
            if (smoke.Count == 0) smoke.Add(new Vector3((rect.x + rect.z) / 2f, 0.6f, (rect.y + rect.w) / 2f));
            var fireRoot = new GameObject("FX_Feuer").transform; fireRoot.SetParent(rt, false); fireRoot.gameObject.SetActive(false);
            PlaceFx(SiegeFxBuilder.HouseFirePath, fireRoot, rt, fire, "Feuer");
            var smokeRoot = new GameObject("FX_Glut").transform; smokeRoot.SetParent(rt, false); smokeRoot.gameObject.SetActive(false);
            var sm = new List<Vector3>();
            foreach (var p in smoke) sm.Add(p + Vector3.down * 0.45f);
            PlaceFx(SiegeFxBuilder.SmolderPath, smokeRoot, rt, sm, "Glut");
            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(rt, false);
            Vector3 c = Vector3.zero;
            foreach (var a in fire) c += a;
            lightGo.transform.localPosition = c / fire.Count + Vector3.up * 1.2f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.52f, 0.2f);
            light.range = 13f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            var bd = root.AddComponent<BuildingDamage>();
            bd.IntactModel = model.gameObject;
            bd.DamagedModel = damaged != null ? damaged.gameObject : null;
            bd.RuinedModel = ruined != null ? ruined.gameObject : null;
            bd.FireFx = fireRoot.gameObject;
            bd.SmokeFx = smokeRoot.gameObject;
            bd.FireLight = light;
            root.AddComponent<FadeOccluder>();
            return $"{fire.Count} Feuer, {smoke.Count} Glut{(damaged != null ? ", Beschädigt" : "")}{(ruined != null ? ", Ruine" : "")}";
        }

        static void SaveVillagePrefab(GameObject root, string path, params GameObject[] dynamicParts)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            SetStaticRecursive(root, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            foreach (var d in dynamicParts)
                if (d != null) foreach (var t in d.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        public static string BuildVillagePrefabs()
        {
            var info = ReadVillageExport();
            var sb = new StringBuilder("Dorf-Prefabs:");
            foreach (var n in VillageBuildings)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(VillageModelFolder + "/" + n + ".fbx") == null) { sb.Append("\n  FBX fehlt: " + n); continue; }
                info.TryGetValue(n, out var a);
                var root = NewModelRoot(n, n, 0f);
                var model = root.transform.Find("Modell");
                model.name = "Model";
                var r = GroundRect(a);
                float h = ModelHeight(a);
                AddBox(root, r, 0f, Mathf.Min(h * 0.85f, 8f));
                string fx = SetupBuildingDamage(root, model, n, r, h);
                GameObject wheel = null;
                if (n == "Dorf_Muehle")
                {
                    var w = model.Find("Muehlrad");
                    if (w != null)
                    {
                        wheel = w.gameObject;
                        var ww = wheel.AddComponent<WaterWheel>();
                        ww.Axis = Vector3.forward;
                        IgnoreInNavMesh(wheel);
                    }
                    else sb.Append("\n  WARN Muehlrad fehlt");
                }
                SaveVillagePrefab(root, PrefabFolder + "/" + n + ".prefab", wheel);
                sb.Append($"\n  {n}: Collider {r.z - r.x:0.0} × {r.w - r.y:0.0} m, {fx}");
            }
            foreach (var n in VillageProps)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(VillageModelFolder + "/" + n + ".fbx") == null) { sb.Append("\n  FBX fehlt: " + n); continue; }
                info.TryGetValue(n, out var a);
                var root = NewModelRoot(n, n, 0f);
                var model = root.transform.Find("Modell");
                model.name = "Model";
                var r = GroundRect(a);
                float h = ModelHeight(a);
                var dyn = new List<GameObject>();
                switch (n)
                {
                    case "Dorf_Flechtzaun":
                        { var b = root.AddComponent<BoxCollider>(); b.center = new Vector3(0f, 0.61f, 0f); b.size = new Vector3(2.03f, 1.22f, 0.16f); dyn.Add(root); break; }
                    case "Dorf_Koppelzaun":
                        { var b = root.AddComponent<BoxCollider>(); b.center = new Vector3(0f, 0.6f, 0f); b.size = new Vector3(3.1f, 1.2f, 0.2f); dyn.Add(root); break; }
                    case "Dorf_Fackelpfahl":
                        {
                            var b = root.AddComponent<BoxCollider>(); b.center = new Vector3(0f, 1.5f, 0f); b.size = new Vector3(0.4f, 3f, 0.4f);
                            var lp = model.Find("LightPoint");
                            var lg = new GameObject("Licht");
                            lg.transform.SetParent(root.transform, false);
                            lg.transform.localPosition = lp != null ? root.transform.InverseTransformPoint(lp.position) : new Vector3(0f, 2.8f, 0.38f);
                            var l = lg.AddComponent<Light>();
                            l.type = LightType.Point; l.color = new Color(1f, 0.72f, 0.4f); l.intensity = 2.2f; l.range = 7f; l.shadows = LightShadows.None;
                            break;
                        }
                    case "Dorf_Bruecke":
                    case "Dorf_Steg":
                        {
                            bool br = n == "Dorf_Bruecke";
                            float W = br ? 6f : 2.5f, L = br ? 8f : 7f;
                            var deck = root.AddComponent<BoxCollider>(); deck.center = new Vector3(0f, -0.05f, 0f); deck.size = new Vector3(W, 0.3f, L);
                            if (br)
                                foreach (float s in new[] { -1f, 1f })
                                { var g = root.AddComponent<BoxCollider>(); g.center = new Vector3(s * (W / 2f - 0.1f), 0.55f, 0f); g.size = new Vector3(0.16f, 1f, L); }
                            break;
                        }
                    case "Dorf_Feld_Getreide":
                    case "Dorf_Feld_Kohl":
                    case "Dorf_Schilf":
                        foreach (var rr in root.GetComponentsInChildren<Renderer>()) rr.shadowCastingMode = n == "Dorf_Schilf" ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                        IgnoreInNavMesh(root);
                        break;
                    case "Dorf_Dorflinde":
                        {
                            // Ursprung = Nexus-Mitte; Stamm bei x 4,6. Stamm-Collider; Krone als Trigger (Layer Ignore Raycast) für die Durchsicht
                            var cap = root.AddComponent<CapsuleCollider>(); cap.center = new Vector3(4.6f, 3f, 0f); cap.radius = 1.0f; cap.height = 6f;
                            var kv = new GameObject("Kronenvolumen"); kv.layer = 2; kv.transform.SetParent(root.transform, false);
                            var kc = kv.AddComponent<CapsuleCollider>(); kc.isTrigger = true; kc.center = new Vector3(5.0f, 8.4f, 0f); kc.radius = 4.6f; kc.height = 9.4f;
                            var fo = root.AddComponent<FadeOccluder>(); fo.FadedAlpha = 0.35f; fo.TriggerVolumes = true;
                            // Ramme: nicht in die 1-m-Lücke zwischen Hainstein und Stamm (sonst liegt der nächste Rammen-NavMesh-
                            // Punkt zum Nexus östlich und die Ramme fährt einen Bogen um den Nexus)
                            var rs = new GameObject("Ramme_Sperre"); rs.transform.SetParent(root.transform, false);
                            var rv = rs.AddComponent<NavMeshModifierVolume>(); rv.center = new Vector3(3.7f, 1f, 0f); rv.size = new Vector3(4.6f, 2.5f, 6.5f); rv.area = 1;
                            SetAgents(rv, RamNavMesh.FindAgentType());
                            foreach (var part in new[] { "Dorflinde_Wurzeln", "Dorflinde_Krone" })
                            {
                                var t = model.GetComponentsInChildren<Transform>(true);
                                foreach (var x in t) if (x.name == part) IgnoreInNavMesh(x.gameObject);
                            }
                            break;
                        }
                    default:
                        AddBox(root, r, 0f, Mathf.Clamp(h, 0.3f, 3f));
                        if (n == "Dorf_Maibaum" || n == "Dorf_Brunnen") root.AddComponent<FadeOccluder>();
                        break;
                }
                SaveVillagePrefab(root, PrefabFolder + "/" + n + ".prefab", dyn.ToArray());
                sb.Append("\n  " + n);
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // ---------------------------------------------------------------- Belagerung im Dorf (Zäune, Palisade verkohlt)
        static readonly Vector2[] VillageGates = { new Vector2(-34f, 0f), new Vector2(0f, 34f), new Vector2(0f, -34f) };
        static readonly int[] GateOpenWave = { 1, 4, 7 }; // Portale West/Nord/Süd werden ab W1/W4/W7 aktiv

        static float NearestGate(Vector3 p, out int gi)
        {
            gi = 0; float best = float.MaxValue;
            for (int i = 0; i < VillageGates.Length; i++)
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), VillageGates[i]);
                if (d < best) { best = d; gi = i; }
            }
            return best;
        }

        // Flechtzäune kippen/fallen (wie SiegeSetup in der Stadt), Palisadensegmente nahe den Toren werden verkohlt
        // (BuildingDamage nur mit Ruinen-Modell → kein Feuer-/Lichtbudget, NavMesh unverändert)
        static void SetupVillageSiege(Transform dorf, StringBuilder sb)
        {
            var prog = Child(dorf, "Belagerung").GetComponent<SiegeProgression>();
            if (prog == null) { sb.AppendLine("Belagerung: SiegeProgression fehlt"); return; }
            int maxWave = Mathf.Max(1, prog.MaxWave);
            // Zäune
            var fences = new List<Transform>();
            var zg = dorf.Find("Zaeune");
            if (zg != null) foreach (Transform t in zg) fences.Add(t);
            fences.Sort((a, b) =>
            {
                int ga, gb;
                float sa = NearestGate(a.position, out ga) / Mathf.Max(0.01f, NearestGate(a.position, out ga) + Vector3.Distance(a.position, new Vector3(24f, 0f, 0f)));
                float sbb = NearestGate(b.position, out gb) / Mathf.Max(0.01f, NearestGate(b.position, out gb) + Vector3.Distance(b.position, new Vector3(24f, 0f, 0f)));
                int c = sa.CompareTo(sbb);
                return c != 0 ? c : a.position.x.CompareTo(b.position.x);
            });
            int broken = Mathf.RoundToInt(fences.Count * 0.6f);
            prog.Fences = new List<SiegeProgression.FenceStage>();
            for (int i = 0; i < fences.Count; i++)
            {
                var t = fences[i];
                var s = new SiegeProgression.FenceStage { Fence = t, IntactPos = t.localPosition, IntactRot = t.localRotation };
                s.BreakWave = i < broken ? 1 + (i * maxWave) / Mathf.Max(1, broken) : 0;
                var rnd = new System.Random(4711 + i * 7919);
                System.Func<float, float, float> range = (a, b) => a + (float)rnd.NextDouble() * (b - a);
                bool fall = rnd.NextDouble() < (i < broken / 2 ? 0.55 : 0.3);
                s.Mode = fall ? SiegeProgression.FenceBreak.Fall : SiegeProgression.FenceBreak.Tilt;
                NearestGate(t.position, out int gi);
                var away = new Vector3(t.position.x - VillageGates[gi].x, 0f, t.position.z - VillageGates[gi].y);
                var fwd = t.rotation * Vector3.forward; fwd.y = 0f;
                float sign = Vector3.Dot(fwd, away) >= 0f ? 1f : -1f;
                Quaternion delta = fall ? Quaternion.Euler(sign * range(86f, 90f), range(-12f, 12f), range(-2f, 2f))
                                        : Quaternion.Euler(sign * range(14f, 30f), range(-7f, 7f), range(-5f, 5f));
                s.BrokenRot = s.IntactRot * delta;
                var box = t.GetComponent<BoxCollider>();
                Vector3 center = box != null ? box.center : new Vector3(0f, 0.61f, 0f), size = box != null ? box.size : new Vector3(2.03f, 1.22f, 0.16f);
                Quaternion parentRot = t.parent != null ? t.parent.rotation : Quaternion.identity;
                Quaternion bw = parentRot * s.BrokenRot;
                float minY = float.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    var corner = center + Vector3.Scale(size * 0.5f, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    minY = Mathf.Min(minY, (bw * Vector3.Scale(corner, t.lossyScale)).y);
                }
                Vector3 wp = t.position + Vector3.up * (Mathf.Max(0f, -minY) + (fall ? 0.01f : 0f));
                s.BrokenPos = t.parent != null ? t.parent.InverseTransformPoint(wp) : wp;
                prog.Fences.Add(s);
            }
            // Palisade: je Tor die 3 nächsten Segmente auf jeder Seite verkohlen nacheinander
            int charred = 0;
            var pal = dorf.Find("Palisade");
            var charredFbx = AssetDatabase.LoadAssetAtPath<GameObject>(VillageModelFolder + "/Damaged/Palisade_Segment_Verkohlt.fbx");
            if (pal != null) prog.Buildings.RemoveAll(st => st == null || st.Building == null || st.Building.transform.IsChildOf(pal));
            if (pal != null && charredFbx != null)
            {
                var perGate = new List<KeyValuePair<float, Transform>>[VillageGates.Length];
                for (int i = 0; i < perGate.Length; i++) perGate[i] = new List<KeyValuePair<float, Transform>>();
                foreach (Transform seg in pal)
                {
                    var old = seg.Find("Modell_Verkohlt");
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    var oldBd = seg.GetComponent<BuildingDamage>();
                    if (oldBd != null) Object.DestroyImmediate(oldBd);
                    if (!seg.name.StartsWith("Palisade_") || seg.Find("Modell") == null) continue;
                    float d = NearestGate(seg.position, out int gi);
                    if (d < 18f) perGate[gi].Add(new KeyValuePair<float, Transform>(d, seg));
                }
                for (int gi = 0; gi < perGate.Length; gi++)
                {
                    perGate[gi].Sort((a, b) => a.Key.CompareTo(b.Key));
                    for (int k = 0; k < perGate[gi].Count && k < 6; k++)
                    {
                        var seg = perGate[gi][k].Value;
                        var model = seg.Find("Modell");
                        var v = (GameObject)PrefabUtility.InstantiatePrefab(charredFbx, seg);
                        v.name = "Modell_Verkohlt";
                        v.transform.localPosition = model.localPosition;
                        v.transform.localRotation = model.localRotation;
                        v.transform.localScale = model.localScale;
                        v.SetActive(false);
                        var bd = seg.gameObject.AddComponent<BuildingDamage>();
                        bd.IntactModel = model.gameObject;
                        bd.RuinedModel = v;
                        int wave = Mathf.Min(maxWave, GateOpenWave[gi] + 2 + (k / 2) * 2);
                        prog.Buildings.Add(new SiegeProgression.BuildingStage { Building = bd, BurnWave = wave, RuinWave = wave });
                        charred++;
                    }
                }
            }
            // Torhäuser (#332, Etappe 3): brennen ab Tor-Öffnung + 3 (Palisade_Tor_Beschaedigt mit Feuer an den Fire_*-Ankern),
            // Ruine 4 Wellen später (Palisade_Tor_Ruine, Glut an Smoke_*). Collider/NavMesh bleiben (nur Modelle wechseln).
            string gates = "";
            if (pal != null)
            {
                foreach (Transform g in pal)
                {
                    if (!g.name.StartsWith("Tor_")) continue;
                    foreach (var old in new[] { "Model_Beschaedigt", "Model_Ruine", "FX_Feuer", "FX_Glut", "FireLight" })
                    {
                        var o = g.Find(old);
                        if (o != null) Object.DestroyImmediate(o.gameObject);
                    }
                    var oldBd = g.GetComponent<BuildingDamage>();
                    if (oldBd != null) Object.DestroyImmediate(oldBd);
                    var model = g.Find("Modell");
                    if (model == null || AssetDatabase.LoadAssetAtPath<GameObject>(VillageModelFolder + "/Damaged/Palisade_Tor_Beschaedigt.fbx") == null) continue;
                    string info = SetupBuildingDamage(g.gameObject, model, "Palisade_Tor", new Vector4(-4.8f, -1f, 4.8f, 1f), 6f);
                    var fo = g.GetComponent<FadeOccluder>();
                    if (fo != null) Object.DestroyImmediate(fo); // Torhaus blendet wie bisher nicht aus
                    var bd = g.GetComponent<BuildingDamage>();
                    NearestGate(g.position, out int gi);
                    int burn = Mathf.Min(maxWave, GateOpenWave[gi] + 3), ruin = Mathf.Min(maxWave, burn + 4);
                    prog.Buildings.Add(new SiegeProgression.BuildingStage { Building = bd, BurnWave = burn, RuinWave = ruin });
                    gates += $" {g.name} W{burn}/W{ruin} ({info})";
                }
            }
            EditorUtility.SetDirty(prog);
            sb.AppendLine($"Belagerung Dorf: {fences.Count} Zäune ({broken} kippen/fallen bis W{maxWave}), {charred} Palisadensegmente verkohlen an den Toren, Torhäuser:{gates}");
        }

        // Brücke/Steg aus Kästen: Deck (Oberkante +0,1 m, Layer Default → kein Bauplatz), Brücke mit Geländer
        static string EnsureCrossingPrefab(string name, bool rails, float length, float width)
        {
            string path = PrefabFolder + "/" + name + "_Platzhalter.prefab";
            var root = new GameObject(name + "_Platzhalter");
            var wood = WoodMaterial();
            void Box(string n, Vector3 pos, Vector3 size)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = n;
                c.transform.SetParent(root.transform, false);
                c.transform.localPosition = pos;
                c.transform.localScale = size;
                c.GetComponent<MeshRenderer>().sharedMaterial = wood;
            }
            Box("Deck", new Vector3(0f, -0.05f, 0f), new Vector3(width, 0.3f, length));
            if (rails)
            {
                Box("Gelaender_L", new Vector3(-width / 2f + 0.08f, 0.55f, 0f), new Vector3(0.16f, 0.9f, length));
                Box("Gelaender_R", new Vector3(width / 2f - 0.08f, 0.55f, 0f), new Vector3(0.16f, 0.9f, length));
            }
            for (int s = -1; s <= 1; s += 2)
                Box("Joch_" + (s < 0 ? "A" : "B"), new Vector3(0f, -0.35f, s * (length / 2f - 0.4f)), new Vector3(width + 0.2f, 0.4f, 0.4f));
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        // Wald jenseits der Kartengrenze: Variante ohne Collider/Kronen-Trigger/Durchsicht (Leistung)
        static string EnsureFarVariant(string prefabPath)
        {
            EnsureFolder(ForestVariantFolder);
            string path = ForestVariantFolder + "/" + Path.GetFileNameWithoutExtension(prefabPath) + "_Fern.prefab";
            if (!File.Exists(path))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                PrefabUtility.SaveAsPrefabAsset(inst, path);
                Object.DestroyImmediate(inst);
            }
            // Ohne Collider, Kronen-Trigger und Durchsicht; wirft keinen Schatten (Leistung: ~250 Bäume am Hang)
            var root = PrefabUtility.LoadPrefabContents(path);
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var f in root.GetComponentsInChildren<FadeOccluder>(true)) Object.DestroyImmediate(f);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            return path;
        }

        // ---------------------------------------------------------------- NavMesh-Bereich „Wasser“

        static void EnsureWaterArea(StringBuilder sb)
        {
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
            var areas = so.FindProperty("areas");
            var a = areas.GetArrayElementAtIndex(WaterArea);
            var nameProp = a.FindPropertyRelative("name");
            var costProp = a.FindPropertyRelative("cost");
            if (nameProp.stringValue == WaterAreaName && Mathf.Approximately(costProp.floatValue, WaterCost)) return;
            if (!string.IsNullOrEmpty(nameProp.stringValue) && nameProp.stringValue != WaterAreaName)
            {
                sb.AppendLine($"WARNUNG: NavMesh-Bereich {WaterArea} heißt schon „{nameProp.stringValue}“ – nicht überschrieben");
                return;
            }
            nameProp.stringValue = WaterAreaName;
            costProp.floatValue = WaterCost;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            sb.AppendLine($"NavMesh-Bereich {WaterArea} „{WaterAreaName}“ angelegt (Kosten {WaterCost})");
        }

        // Humanoid-Surface (Agententyp 0) backen → <Szene>/NavMesh-Environment.asset, danach RamNavMesh.Bake().
        // Eigene Variante von MerchantSetup.RebakeNavMesh: dort wird die Surface nur über den Objektnamen „Environment“
        // gesucht – mit zwei Surfaces am selben Objekt entscheidet die Such-Reihenfolge, welche gebacken wird.
        public static string BakeNavMesh()
        {
            NavMeshSurface surface = null;
            foreach (var srf in Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
                if (srf.name == "Environment" && srf.agentTypeID == 0) surface = srf;
            if (surface == null) return "NavMeshSurface (Humanoid) an 'Environment' fehlt";
            string assetPath = MapEditorUtil.NavMeshAssetPath(surface.gameObject.scene, MapEditorUtil.EnvironmentNavMeshFile);
            if (assetPath.StartsWith("Assets/NavMesh-")) return "Abbruch: Stadt-NavMesh-Pfad";
            var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
            surface.BuildNavMesh();
            var built = surface.navMeshData;
            if (built != null && built != asset)
            {
                if (asset == null) { AssetDatabase.CreateAsset(Object.Instantiate(built), assetPath); asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath); }
                else EditorUtility.CopySerialized(built, asset);
                surface.RemoveData();
                surface.navMeshData = asset;
                surface.AddData();
                EditorUtility.SetDirty(asset);
                EditorUtility.SetDirty(surface);
                AssetDatabase.SaveAssets();
            }
            return "NavMesh (Humanoid) gebacken → " + assetPath + "\n" + RamNavMesh.Bake();
        }

        // ---------------------------------------------------------------- Prüfen

        // Weglänge a → b; stopRadius > 0: nur bis zum ersten Punkt im Abstand stopRadius (horizontal) um b („bis zum Nexus-Rand“)
        public static float PathLength(Vector3 a, Vector3 b, int agentType, out NavMeshPathStatus status, out float waterMeters, float stopRadius = 0f)
        {
            var f = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
            for (int i = 0; i < 32; i++) f.SetAreaCost(i, NavMesh.GetAreaCost(i));
            status = NavMeshPathStatus.PathInvalid;
            waterMeters = 0f;
            if (!NavMesh.SamplePosition(a, out var ha, 6f, f) || !NavMesh.SamplePosition(b, out var hb, 6f, f)) return -1f;
            var path = new NavMeshPath();
            NavMesh.CalculatePath(ha.position, hb.position, f, path);
            status = path.status;
            float len = 0f;
            var wf = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = 1 << WaterArea };
            for (int i = 1; i < path.corners.Length; i++)
            {
                float l = Vector3.Distance(path.corners[i - 1], path.corners[i]);
                if (stopRadius > 0f)
                {
                    Vector2 bb = new Vector2(b.x, b.z), p0 = new Vector2(path.corners[i - 1].x, path.corners[i - 1].z), p1 = new Vector2(path.corners[i].x, path.corners[i].z);
                    if (Vector2.Distance(p1, bb) <= stopRadius)
                    {
                        // Schnittpunkt des Segments mit dem Kreis (Bisektion)
                        float lo = 0f, hi = 1f;
                        for (int it = 0; it < 30; it++) { float m = (lo + hi) / 2f; if (Vector2.Distance(Vector2.Lerp(p0, p1, m), bb) <= stopRadius) hi = m; else lo = m; }
                        l *= hi;
                        len += l;
                        break;
                    }
                }
                len += l;
                int steps = Mathf.CeilToInt(l / 0.5f);
                for (int k = 0; k < steps; k++)
                {
                    var q = Vector3.Lerp(path.corners[i - 1], path.corners[i], (k + 0.5f) / steps);
                    if (NavMesh.SamplePosition(q, out var wh, 0.6f, wf) && Vector2.Distance(new Vector2(wh.position.x, wh.position.z), new Vector2(q.x, q.z)) < 0.2f) waterMeters += l / steps;
                }
            }
            _lastPath = path.corners;
            return len;
        }

        static Vector3[] _lastPath;
        const float NexusEdge = 2.5f; // Nexus-Kapsel r 1,5 + 1 m Angriffsabstand

        public static string Measure(string topPng)
        {
            var sb = new StringBuilder();
            Physics.SyncTransforms();
            var nexus = Object.FindFirstObjectByType<Nexus>();
            if (nexus == null) return "Nexus fehlt";
            Vector3 np = nexus.transform.position;
            int ram = RamNavMesh.FindAgentType();
            var paths = new List<KeyValuePair<Color, Vector3[]>>();
            Color[] cols = { Color.red, Color.magenta, Color.yellow };
            int ci = 0;
            var portals = new List<SpawnPortal>(Object.FindObjectsByType<SpawnPortal>(FindObjectsSortMode.None));
            portals.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
            foreach (var p in portals)
            {
                Vector3 s = p.transform.position;
                float h = PathLength(s, np, 0, out var st, out var wm, NexusEdge);
                var hp = _lastPath;
                NavMeshPathStatus st2 = NavMeshPathStatus.PathInvalid;
                float wm2 = 0f, r = -1f;
                _lastPath = null;
                if (ram != int.MinValue) r = PathLength(s, np, ram, out st2, out wm2, NexusEdge);
                sb.AppendLine($"Weg {p.name}: Humanoid {h:0.0} m ({st}, Furt {wm:0.0} m) | Ramme {r:0.0} m ({st2}, Furt {wm2:0.0} m)");
                paths.Add(new KeyValuePair<Color, Vector3[]>(cols[ci % 3], hp));
                if (_lastPath != null) paths.Add(new KeyValuePair<Color, Vector3[]>(cols[ci % 3] * 0.55f + Color.black * 0.45f, _lastPath));
                ci++;
            }

            // Erreichbarkeit der Kreise (Raster 0,5 m: NavMesh + frei in Spielerhöhe + Weg zum Nexus)
            var circles = new List<KeyValuePair<string, Vector4>>();
            foreach (var sh in Object.FindObjectsByType<Shrine>(FindObjectsSortMode.None))
            {
                var c = sh.Center != null ? sh.Center.position : sh.transform.position;
                circles.Add(new KeyValuePair<string, Vector4>(sh.name, new Vector4(c.x, c.y, c.z, sh.CaptureRadius)));
            }
            foreach (var m in Object.FindObjectsByType<Merchant>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var c = m.Center != null ? m.Center.position : m.transform.position;
                circles.Add(new KeyValuePair<string, Vector4>(m.name, new Vector4(c.x, c.y, c.z, m.CaptureRadius)));
            }
            var nexusCol = nexus.GetComponent<Collider>();
            foreach (var kv in circles)
            {
                Vector3 c = kv.Value;
                float rad = kv.Value.w;
                int tot = 0, ok = 0;
                for (float dx = -rad; dx <= rad; dx += 0.5f)
                    for (float dz = -rad; dz <= rad; dz += 0.5f)
                    {
                        if (dx * dx + dz * dz > rad * rad) continue;
                        tot++;
                        if (Walkable(c + new Vector3(dx, 0f, dz), nexusCol)) ok++;
                    }
                float pl = PathLength(nexusCol.ClosestPoint(c), c, 0, out var cst, out _);
                sb.AppendLine($"Kreis {kv.Key}: begehbar {ok}/{tot}, Weg vom Nexus {pl:0.0} m ({cst})");
            }

            // Bauplätze
            var plan = Plan.Load();
            int match = 0, game = 0;
            foreach (var r in plan.BuildSpots)
            {
                string reason = BuildCheck(new Vector3(r.N("x"), 0f, r.N("z")), nexus, Humanoid);
                // genau wie InteractionManager.IsReachableByEnemies (ohne Agententyp-Filter → kann das Rammen-NavMesh treffen)
                string reasonGame = BuildCheck(new Vector3(r.N("x"), 0f, r.N("z")), nexus, null);
                if ((reason == null) == (reasonGame == null)) game++;
                else sb.AppendLine($"  Spiel-Logik weicht ab bei {r.S("name")}: {reasonGame ?? "baubar"}");
                bool okSpot = reason == null;
                bool expect = r.S("expect") == "ok";
                if (okSpot == expect) match++;
                sb.AppendLine($"Bauplatz {r.S("name")} ({r.N("x")}, {r.N("z")}): {(okSpot ? "baubar" : reason)} – erwartet {(expect ? "baubar" : "gesperrt")} {(okSpot == expect ? "✓" : "✗")}");
            }
            sb.AppendLine($"Bauplätze wie erwartet: {match}/{plan.BuildSpots.Count}; Spiel-Logik (ohne Agentenfilter) gleich: {game}/{plan.BuildSpots.Count}");
            if (!string.IsNullOrEmpty(topPng)) RenderTop(topPng, paths, circles);
            return sb.ToString();
        }

        static NavMeshQueryFilter? Humanoid => new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };

        static bool Sample(Vector3 p, out NavMeshHit hit, float r, NavMeshQueryFilter? f) =>
            f.HasValue ? NavMesh.SamplePosition(p, out hit, r, f.Value) : NavMesh.SamplePosition(p, out hit, r, NavMesh.AllAreas);

        static bool Complete(Vector3 a, Vector3 b, NavMeshQueryFilter? f)
        {
            var path = new NavMeshPath();
            bool ok = f.HasValue ? NavMesh.CalculatePath(a, b, f.Value, path) : NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path);
            return ok && path.status == NavMeshPathStatus.PathComplete;
        }

        static bool Walkable(Vector3 q, Collider nexusCol)
        {
            if (!Sample(q, out var nh, 0.6f, Humanoid)) return false;
            foreach (var h in Physics.OverlapCapsule(nh.position + Vector3.up * 0.4f, nh.position + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h is TerrainCollider || h is CharacterController) continue;
                return false;
            }
            if (!Sample(nexusCol.ClosestPoint(nh.position), out var nx, 4f, Humanoid)) return false;
            return Complete(nh.position, nx.position, Humanoid);
        }

        // Räumlicher Teil von InteractionManager.ValidatePlacement (Boden-Layer, Belegt-Prüfung, Erreichbarkeit)
        public static string BuildCheck(Vector3 pos, Nexus nexus, NavMeshQueryFilter? filter)
        {
            pos.x = Mathf.Round(pos.x); pos.z = Mathf.Round(pos.z);
            if (!Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out var floor, 60f, 1 << FloorLayer)) return "kein Boden (Floor)";
            pos.y = floor.point.y;
            int obstacle = (1 << 0) | (1 << 6) | (1 << 7) | StoneWall.BlockMask; // wie InteractionManager.ObstacleLayer (193)
            if (Physics.CheckSphere(pos, 0.45f, obstacle)) return "belegt";
            if (!Sample(pos, out var hit, 2.3f, filter)) return "kein NavMesh";
            Vector3 d = hit.position - pos; d.y = 0f;
            if (d.magnitude > 1.3f) return $"NavMesh zu weit ({d.magnitude:0.0} m)";
            var col = nexus.GetComponent<Collider>();
            Vector3 np = col != null ? col.ClosestPoint(hit.position) : nexus.transform.position;
            if (!Sample(np, out var nh, 4f, filter)) return "Nexus ohne NavMesh";
            if (!Complete(hit.position, nh.position, filter)) return "für Gegner unerreichbar";
            return null;
        }

        static void RenderTop(string png, List<KeyValuePair<Color, Vector3[]>> paths, List<KeyValuePair<string, Vector4>> circles)
        {
            const float half = 80f;
            const int W = 1280;
            float s = W / (2f * half);
            var go = new GameObject("TmpTopCam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 120f, 0f), Quaternion.Euler(90f, 0f, 0f));
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(W, W, 24);
            cam.targetTexture = rt;
            cam.aspect = 1f;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            cam.Render();
            RenderSettings.fog = fog;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, W, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, W), 0, 0);
            void Plot(Vector3 p, Color c, int th)
            {
                int x = (int)((p.x + half) * s), y = (int)((p.z + half) * s);
                for (int i = -th; i <= th; i++)
                    for (int j = -th; j <= th; j++)
                        if (x + i >= 0 && y + j >= 0 && x + i < W && y + j < W) tex.SetPixel(x + i, y + j, c);
            }
            foreach (var kv in paths)
            {
                if (kv.Value == null) continue;
                bool dark = kv.Key.maxColorComponent < 0.9f;
                for (int i = 1; i < kv.Value.Length; i++)
                {
                    Vector3 a = kv.Value[i - 1], b = kv.Value[i];
                    int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) * s));
                    for (int k = 0; k <= steps; k++)
                    {
                        if (dark && (k / 6) % 2 == 1) continue; // Ramme gestrichelt
                        Plot(Vector3.Lerp(a, b, k / (float)steps), kv.Key, dark ? 1 : 2);
                    }
                }
            }
            foreach (var c in circles)
                for (float a = 0f; a < Mathf.PI * 2f; a += 0.01f)
                    Plot(new Vector3(c.Value.x + Mathf.Cos(a) * c.Value.w, 0f, c.Value.z + Mathf.Sin(a) * c.Value.w), Color.cyan, 1);
            tex.Apply();
            File.WriteAllBytes(png, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(go);
            rt.Release();
            Object.DestroyImmediate(tex);
        }

        // Bild aus der Hauptkamera (mit Post-FX) an beliebiger Pose; Kamera wird danach zurückgesetzt
        public static void Shot(string png, Vector3 pos, Vector3 euler, int w = 1600, int h = 900, float fov = 60f)
        {
            var camGo = GameObject.Find("Main Camera");
            var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            if (cam == null) return;
            var oldPos = cam.transform.position;
            var oldRot = cam.transform.rotation;
            float oldFov = cam.fieldOfView;
            var rt = new RenderTexture(w, h, 24);
            cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
            cam.fieldOfView = fov;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(png, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
            cam.fieldOfView = oldFov;
            rt.Release();
            Object.DestroyImmediate(tex);
        }

        // ---------------------------------------------------------------- Allgemein

        static GameObject FindInScene(Scene scene, string path)
        {
            var parts = path.Split('/');
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.name != parts[0]) continue;
                if (parts.Length == 1) return r;
                var t = r.transform.Find(string.Join("/", parts, 1, parts.Length - 1));
                if (t != null) return t.gameObject;
            }
            return null;
        }

        static string RelativePath(Transform t)
        {
            var parts = new List<string>();
            while (t.parent != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            return t;
        }

        static void IgnoreInNavMesh(GameObject go)
        {
            var mod = go.GetComponent<NavMeshModifier>();
            if (mod == null) mod = go.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
