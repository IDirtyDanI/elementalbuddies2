using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElementalBuddies.EditorTools
{
    // Richtet die Belagerung ein (idempotent, beliebig oft ausführbar):
    //  1. baut VFX_HouseFire / VFX_Smolder (SiegeFxBuilder)
    //  2. ergänzt die 9 Gebäude-Prefabs um BuildingDamage, Schadensmodelle (Models/Town/Damaged/<Name>_Beschaedigt|_Ruine.fbx),
    //     Feuer an den Fire_*-Ankern und Glut an den Smoke_*-Ankern (Fallback: Bounds des intakten Modells)
    //  3. berechnet in der offenen Szene den Zeitplan (von den Toren nach innen) und die Stimmungs-Keyframes
    // Dazu Edit-Vorschau einzelner Wellen + Zurücksetzen. Vor dem Speichern der Szene wird eine Vorschau automatisch zurückgesetzt.
    [InitializeOnLoad]
    public static class SiegeSetup
    {
        private const string PrefabDir = "Assets/_Game/Prefabs/Town/";
        private const string DamagedDir = "Assets/_Game/Models/Town/Damaged/";
        private const string WindowMatPath = "Assets/_Game/Models/Town/Materials/Town_WindowGlow.mat";

        public static readonly string[] BuildingNames =
            { "Haus_Klein", "Haus_Mittel", "Haus_Lang", "Haus_Turm", "Taverne", "Schmiede", "Kapelle", "Lagerhaus", "Wachturm" };

        // Tore (Durchgänge in der Mauer bei ±35) und Nexus-Platz
        private static readonly Vector2[] Gates = { new Vector2(-35f, 0f), new Vector2(0f, 35f), new Vector2(0f, -35f) };
        private static readonly Vector2 Plaza = new Vector2(26f, 0f);

        // Neue Brände pro abgeschlossener Welle 1..14 (Summe 20 von 30 Gebäuden, die 10 platznächsten bleiben stehen)
        private static readonly int[] BurnsPerWave = { 1, 1, 1, 2, 1, 2, 1, 2, 1, 2, 2, 1, 2, 1 };
        private const int RuinAfter = 3;           // RuinWave = BurnWave + 3
        private const float FenceShare = 0.6f;     // Anteil kaputter Zäune bei MaxWave
        private const int MaxWave = 14;

        static SiegeSetup()
        {
            EditorSceneManager.sceneSaving += (scene, path) =>
            {
                foreach (var sp in Object.FindObjectsByType<SiegeProgression>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (sp.gameObject.scene == scene && sp.PreviewActive) ResetPreview(sp);
            };
        }

        // ---------------- Menü ----------------

        [MenuItem("BuddyTD/Belagerung/Einrichten")]
        public static void SetupAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("SiegeSetup: nur im Edit-Modus."); return; }
            var existing = Object.FindFirstObjectByType<SiegeProgression>();
            if (existing != null && existing.PreviewActive) ResetPreview(existing);

            SiegeFxBuilder.BuildAll();
            foreach (var n in BuildingNames) SetupPrefab(n);
            AssetDatabase.SaveAssets();
            SetupScene();
        }

        [MenuItem("BuddyTD/Belagerung/Vorschau Welle 1 (Start)")] private static void P1() { Preview(1); }
        [MenuItem("BuddyTD/Belagerung/Vorschau Welle 3")] private static void P3() { Preview(3); }
        [MenuItem("BuddyTD/Belagerung/Vorschau Welle 5")] private static void P5() { Preview(5); }
        [MenuItem("BuddyTD/Belagerung/Vorschau Welle 7")] private static void P7() { Preview(7); }
        [MenuItem("BuddyTD/Belagerung/Vorschau Welle 9")] private static void P9() { Preview(9); }
        [MenuItem("BuddyTD/Belagerung/Vorschau Welle 12")] private static void P12() { Preview(12); }
        [MenuItem("BuddyTD/Belagerung/Vorschau Welle 15 (Nacht, Endzustand)")] private static void P15() { Preview(15); }

        [MenuItem("BuddyTD/Belagerung/Zurücksetzen (intakt)")]
        public static void ResetMenu()
        {
            var sp = Object.FindFirstObjectByType<SiegeProgression>();
            if (sp != null) ResetPreview(sp);
        }

        [MenuItem("BuddyTD/Belagerung/Stimmungs-Keyframes auf Standard")]
        public static void ResetKeysMenu()
        {
            var sp = Object.FindFirstObjectByType<SiegeProgression>();
            if (sp == null || sp.Atmosphere == null) return;
            if (sp.PreviewActive) ResetPreview(sp);
            CaptureDay(sp.Atmosphere);
            DefaultKeys(sp.Atmosphere);
            EditorUtility.SetDirty(sp.Atmosphere);
        }

        // Edit-Vorschau: Zustand während Welle n (= n-1 abgeschlossene Wellen)
        public static void Preview(int wave)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("SiegeSetup: Vorschau nur im Edit-Modus."); return; }
            var sp = Object.FindFirstObjectByType<SiegeProgression>();
            if (sp == null) { Debug.LogWarning("SiegeSetup: Kein SiegeProgression in der Szene – erst Einrichten."); return; }
            sp.PreviewActive = true;
            sp.ApplyInstant(Mathf.Max(0, wave - 1));
            SceneView.RepaintAll();
            Debug.Log($"SiegeSetup: Vorschau Welle {wave} ({wave - 1} abgeschlossen). Zurücksetzen über BuddyTD → Belagerung → Zurücksetzen.");
        }

        // Setzt Stadt + Stimmung auf den intakten Szenenzustand und entfernt Vorschau-Overrides
        public static void ResetPreview(SiegeProgression sp)
        {
            sp.ApplyInstant(0);
            if (sp.Atmosphere != null) sp.Atmosphere.RestoreScene();
            foreach (var b in sp.Buildings)
            {
                if (b == null || b.Building == null) continue;
                foreach (Transform child in b.Building.GetComponentsInChildren<Transform>(true))
                {
                    if (child == b.Building.transform || !PrefabUtility.IsPartOfPrefabInstance(child)) continue;
                    RevertIfOverridden(child.gameObject);
                    foreach (var c in child.GetComponents<Component>())
                        if (c is Light || c is ParticleSystem || c is Renderer) RevertIfOverridden(c);
                }
            }
            foreach (var f in sp.Fences)
            {
                if (f == null || f.Fence == null) continue;
                var col = f.Fence.GetComponent<Collider>();
                if (col == null || !PrefabUtility.IsPartOfPrefabInstance(col)) continue;
                var prop = new SerializedObject(col).FindProperty("m_Enabled");
                if (prop != null && prop.prefabOverride) PrefabUtility.RevertPropertyOverride(prop, InteractionMode.AutomatedAction);
            }
            // Laternen-Lichter und Renderer mit Fenster-Material: Overrides entfernen, die nur noch dem Prefab-Wert entsprechen
            if (sp.Atmosphere != null)
            {
                foreach (var l in sp.Atmosphere.Lanterns)
                {
                    if (l == null || !PrefabUtility.IsPartOfPrefabInstance(l)) continue;
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(l);
                    if (src != null && Mathf.Approximately(src.intensity, l.intensity)) RevertIfOverridden(l);
                }
                var window = sp.Atmosphere.WindowMaterial;
                if (window != null)
                    foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        if (!PrefabUtility.IsPartOfPrefabInstance(r) || !r.sharedMaterials.Contains(window)) continue;
                        var src = PrefabUtility.GetCorrespondingObjectFromSource(r);
                        if (src != null && src.sharedMaterials.SequenceEqual(r.sharedMaterials)) RevertIfOverridden(r);
                    }
            }

            // Sicherheitshalber: alle Zäune exakt auf die gespeicherte intakte Pose
            foreach (var f in sp.Fences)
            {
                if (f == null || f.Fence == null) continue;
                f.Fence.localPosition = f.IntactPos;
                f.Fence.localRotation = f.IntactRot;
                var col = f.Fence.GetComponent<Collider>();
                if (col != null) col.enabled = true;
            }
            sp.PreviewActive = false;
            SceneView.RepaintAll();
        }

        private static void RevertIfOverridden(Object o)
        {
            var g = o as GameObject;
            if (g != null && PrefabUtility.IsAddedGameObjectOverride(g)) return;
            var c = o as Component;
            if (c != null && PrefabUtility.IsAddedComponentOverride(c)) return;
            PrefabUtility.RevertObjectOverride(o, InteractionMode.AutomatedAction);
        }

        // ---------------- Prefabs ----------------

        private static void SetupPrefab(string name)
        {
            string path = PrefabDir + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning("SiegeSetup: Prefab fehlt: " + path); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            var rt = root.transform;
            var model = rt.Find("Model");
            if (model == null) { Debug.LogWarning("SiegeSetup: Kein Kind 'Model' in " + path); PrefabUtility.UnloadPrefabContents(root); return; }

            // Alte Einrichtung entfernen (idempotent)
            foreach (var old in new[] { "Model_Beschaedigt", "Model_Ruine", "FX_Feuer", "FX_Glut", "FireLight" })
            {
                var t = rt.Find(old);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            Bounds b = LocalBounds(rt, model);
            var damaged = AddVariant(rt, model, DamagedDir + name + "_Beschaedigt.fbx", "Model_Beschaedigt");
            var ruined = AddVariant(rt, model, DamagedDir + name + "_Ruine.fbx", "Model_Ruine");

            // Feuer an Fire_*-Ankern (sonst 1–3 Punkte oben auf den Bounds)
            var fireAnchors = Anchors(rt, damaged, "Fire_");
            if (fireAnchors.Count == 0) fireAnchors = FallbackFire(b);
            var fireRoot = Container(rt, "FX_Feuer");
            var firePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SiegeFxBuilder.HouseFirePath);
            for (int i = 0; i < fireAnchors.Count; i++) Place(firePrefab, fireRoot, fireAnchors[i], "Feuer_" + i);

            // Glut/Rauch an Smoke_*-Ankern (liegen ~0,6 m über dem Schutt → Effekt etwas absenken)
            var smokeAnchors = Anchors(rt, ruined, "Smoke_");
            if (smokeAnchors.Count > 0) for (int i = 0; i < smokeAnchors.Count; i++) smokeAnchors[i] += Vector3.down * 0.45f;
            else smokeAnchors = FallbackSmoke(b);
            var smokeRoot = Container(rt, "FX_Glut");
            var smolderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SiegeFxBuilder.SmolderPath);
            for (int i = 0; i < smokeAnchors.Count; i++) Place(smolderPrefab, smokeRoot, smokeAnchors[i], "Glut_" + i);

            // Ein flackerndes Punktlicht pro Gebäude (globales Budget in BuildingDamage)
            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(rt, false);
            Vector3 c = Vector3.zero;
            foreach (var a in fireAnchors) c += a;
            lightGo.transform.localPosition = c / fireAnchors.Count + Vector3.up * 1.2f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.52f, 0.2f);
            light.range = 13f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            var bd = root.GetComponent<BuildingDamage>();
            if (bd == null) bd = root.AddComponent<BuildingDamage>();
            bd.IntactModel = model.gameObject;
            bd.DamagedModel = damaged != null ? damaged.gameObject : null;
            bd.RuinedModel = ruined != null ? ruined.gameObject : null;
            bd.FireFx = fireRoot.gameObject;
            bd.SmokeFx = smokeRoot.gameObject;
            bd.FireLight = light;
            if (root.GetComponent<FadeOccluder>() == null) root.AddComponent<FadeOccluder>();

            string info = $"SiegeSetup: {name} – Beschädigt {(damaged != null ? "FBX" : "Fallback")}, Ruine {(ruined != null ? "FBX" : "Fallback")}, {fireAnchors.Count} Feuer, {smokeAnchors.Count} Glut.";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log(info);
        }

        // Schadensmodell als inaktives Kind mit gleicher lokaler Transform wie "Model"
        private static Transform AddVariant(Transform root, Transform model, string fbxPath, string childName)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root);
            go.name = childName;
            go.transform.localPosition = model.localPosition;
            go.transform.localRotation = model.localRotation;
            go.transform.localScale = model.localScale;
            go.transform.SetSiblingIndex(model.GetSiblingIndex() + 1);
            go.SetActive(false);
            return go.transform;
        }

        // Anker (Fire_0, Fire_0.002 …) in Root-Koordinaten, nach Namen sortiert
        private static List<Vector3> Anchors(Transform root, Transform variant, string prefix)
        {
            var list = new List<Vector3>();
            if (variant == null) return list;
            var anchors = variant.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(prefix)).OrderBy(t => t.name);
            foreach (var a in anchors) list.Add(root.InverseTransformPoint(a.position));
            return list;
        }

        // Bounds aller Mesh-Renderer des Modells im Root-Raum
        private static Bounds LocalBounds(Transform root, Transform model)
        {
            bool has = false;
            var b = new Bounds();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!has) { b = new Bounds(p, Vector3.zero); has = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static List<Vector3> FallbackFire(Bounds b)
        {
            bool alongX = b.size.x >= b.size.z;
            float len = alongX ? b.size.x : b.size.z;
            int n = len > 8f ? 3 : len > 5f ? 2 : 1;
            float y = b.min.y + b.size.y * 0.72f;
            var list = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float f = n == 1 ? 0f : Mathf.Lerp(-0.3f, 0.3f, i / (float)(n - 1)) * len;
                list.Add(new Vector3(b.center.x + (alongX ? f : 0f), y, b.center.z + (alongX ? 0f : f)));
            }
            return list;
        }

        private static List<Vector3> FallbackSmoke(Bounds b)
        {
            bool alongX = b.size.x >= b.size.z;
            float len = alongX ? b.size.x : b.size.z;
            int n = len > 6f ? 2 : 1;
            var list = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float f = n == 1 ? 0f : (i == 0 ? -0.22f : 0.22f) * len;
                list.Add(new Vector3(b.center.x + (alongX ? f : 0f), b.min.y + 0.6f, b.center.z + (alongX ? 0f : f)));
            }
            return list;
        }

        private static Transform Container(Transform root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.SetActive(false);
            return go.transform;
        }

        private static void Place(GameObject prefab, Transform parent, Vector3 rootLocalPos, string name)
        {
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = parent.parent.TransformPoint(rootLocalPos);
            go.transform.rotation = Quaternion.identity;
        }

        // ---------------- Szene / Zeitplan ----------------

        private static float GateDistance(Vector3 p, out Vector2 gate)
        {
            var q = new Vector2(p.x, p.z);
            gate = Gates[0];
            float best = float.MaxValue;
            foreach (var g in Gates)
            {
                float d = Vector2.Distance(q, g);
                if (d < best) { best = d; gate = g; }
            }
            return best;
        }

        // 0 = direkt am Tor, 1 = direkt am Nexus-Platz
        private static float InwardScore(Vector3 p)
        {
            Vector2 gate;
            float dg = GateDistance(p, out gate);
            float dp = Vector2.Distance(new Vector2(p.x, p.z), Plaza);
            return dg / Mathf.Max(0.01f, dg + dp);
        }

        private static void SetupScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var stadt = GameObject.Find("Environment/Stadt");
            if (stadt == null) { Debug.LogWarning("SiegeSetup: Environment/Stadt nicht gefunden."); return; }

            var holder = stadt.transform.Find("Belagerung");
            if (holder == null)
            {
                holder = new GameObject("Belagerung").transform;
                holder.SetParent(stadt.transform, false);
            }
            var sp = holder.GetComponent<SiegeProgression>();
            if (sp == null) sp = holder.gameObject.AddComponent<SiegeProgression>();
            var atmo = holder.GetComponent<SiegeAtmosphere>();
            if (atmo == null) atmo = holder.gameObject.AddComponent<SiegeAtmosphere>();
            sp.Atmosphere = atmo;
            sp.MaxWave = MaxWave;

            // Gebäude: von den Toren nach innen
            var buildings = Object.FindObjectsByType<BuildingDamage>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => x.gameObject.scene == scene)
                .OrderBy(x => InwardScore(x.transform.position)).ThenBy(x => x.transform.position.x).ThenBy(x => x.transform.position.z).ToList();
            sp.Buildings = new List<SiegeProgression.BuildingStage>();
            int idx = 0;
            for (int w = 0; w < BurnsPerWave.Length; w++)
                for (int k = 0; k < BurnsPerWave[w] && idx < buildings.Count; k++, idx++)
                    sp.Buildings.Add(new SiegeProgression.BuildingStage { Building = buildings[idx], BurnWave = w + 1, RuinWave = w + 1 + RuinAfter });
            for (; idx < buildings.Count; idx++)
                sp.Buildings.Add(new SiegeProgression.BuildingStage { Building = buildings[idx], BurnWave = 0, RuinWave = 0 });

            // Zäune (Zaeune + einzelne unter Requisiten)
            var fences = new List<Transform>();
            foreach (var parentName in new[] { "Environment/Stadt/Zaeune", "Environment/Stadt/Requisiten" })
            {
                var p = GameObject.Find(parentName);
                if (p == null) continue;
                foreach (Transform t in p.transform) if (t.name.StartsWith("Zaun")) fences.Add(t);
            }
            var oldPoses = new Dictionary<Transform, SiegeProgression.FenceStage>();
            if (sp.Fences != null) foreach (var f in sp.Fences) if (f != null && f.Fence != null) oldPoses[f.Fence] = f;
            fences = fences.OrderBy(t => InwardScore(t.position)).ThenBy(t => t.position.x).ThenBy(t => t.position.z).ToList();
            int broken = Mathf.RoundToInt(fences.Count * FenceShare);
            sp.Fences = new List<SiegeProgression.FenceStage>();
            for (int i = 0; i < fences.Count; i++)
            {
                var t = fences[i];
                // Intakte Pose: wie in der Szene (beim erneuten Einrichten die gespeicherte nehmen, falls gerade verschoben)
                SiegeProgression.FenceStage prev;
                var stage = new SiegeProgression.FenceStage { Fence = t };
                if (oldPoses.TryGetValue(t, out prev)) { stage.IntactPos = prev.IntactPos; stage.IntactRot = prev.IntactRot; }
                else { stage.IntactPos = t.localPosition; stage.IntactRot = t.localRotation; }
                stage.BreakWave = i < broken ? 1 + (i * MaxWave) / Mathf.Max(1, broken) : 0;
                ComputeBrokenPose(stage, i, i < broken / 2);
                sp.Fences.Add(stage);
            }

            // Stimmung
            if (!atmo.DayCaptured) { CaptureDay(atmo); DefaultKeys(atmo); }
            atmo.Sun = RenderSettings.sun != null ? RenderSettings.sun : FindDirectional();
            atmo.WindowMaterial = AssetDatabase.LoadAssetAtPath<Material>(WindowMatPath);
            atmo.Lanterns = new List<Light>();
            atmo.LanternBase = new List<float>();
            var req = GameObject.Find("Environment/Stadt/Requisiten");
            if (req != null)
                foreach (var l in req.GetComponentsInChildren<Light>(true))
                    if (l.type == LightType.Point && HasAncestorNamed(l.transform, "Laterne"))
                    { atmo.Lanterns.Add(l); atmo.LanternBase.Add(l.intensity); }

            EditorUtility.SetDirty(sp);
            EditorUtility.SetDirty(atmo);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"SiegeSetup: Szene eingerichtet – {buildings.Count} Gebäude ({sp.Buildings.Count(x => x.BurnWave > 0)} brennen bis Welle {MaxWave}), {fences.Count} Zäune ({broken} kaputt bis Welle {MaxWave}), {atmo.Lanterns.Count} Laternen.");
        }

        private static bool HasAncestorNamed(Transform t, string prefix)
        {
            for (; t != null; t = t.parent) if (t.name.StartsWith(prefix)) return true;
            return false;
        }

        private static Light FindDirectional()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) return l;
            return null;
        }

        // Kippen/Umfallen weg vom nächsten Tor; deterministischer Zufall pro Index
        private static void ComputeBrokenPose(SiegeProgression.FenceStage s, int index, bool nearGate)
        {
            var t = s.Fence;
            var rnd = new System.Random(4711 + index * 7919);
            System.Func<float, float, float> range = (a, b) => a + (float)rnd.NextDouble() * (b - a);
            bool fall = rnd.NextDouble() < (nearGate ? 0.55 : 0.3);
            s.Mode = fall ? SiegeProgression.FenceBreak.Fall : SiegeProgression.FenceBreak.Tilt;

            Quaternion parentRot = t.parent != null ? t.parent.rotation : Quaternion.identity;
            Vector3 worldPos = t.parent != null ? t.parent.TransformPoint(s.IntactPos) : s.IntactPos;
            Quaternion worldRot = parentRot * s.IntactRot;
            Vector2 gate;
            GateDistance(worldPos, out gate);
            var away = new Vector3(worldPos.x - gate.x, 0f, worldPos.z - gate.y);
            var fwd = worldRot * Vector3.forward; fwd.y = 0f;
            float sign = Vector3.Dot(fwd, away) >= 0f ? 1f : -1f;

            Quaternion delta = fall
                ? Quaternion.Euler(sign * range(86f, 90f), range(-12f, 12f), range(-2f, 2f))
                : Quaternion.Euler(sign * range(14f, 30f), range(-7f, 7f), range(-5f, 5f));
            s.BrokenRot = s.IntactRot * delta;

            // Anheben, damit nichts im Boden steckt (Box des Colliders bzw. Standardmaß 2,03 × 1,22 × 0,16)
            Vector3 center = new Vector3(0f, 0.61f, -0.02f), size = new Vector3(2.03f, 1.22f, 0.16f);
            var box = t.GetComponent<BoxCollider>();
            if (box != null) { center = box.center; size = box.size; }
            Quaternion brokenWorld = parentRot * s.BrokenRot;
            float minY = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = center + Vector3.Scale(size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                minY = Mathf.Min(minY, (brokenWorld * Vector3.Scale(corner, t.lossyScale)).y);
            }
            float lift = Mathf.Max(0f, -minY) + (fall ? 0.01f : 0f);
            Vector3 brokenWorldPos = worldPos + Vector3.up * lift;
            s.BrokenPos = t.parent != null ? t.parent.InverseTransformPoint(brokenWorldPos) : brokenWorldPos;
        }

        // ---------------- Stimmung ----------------

        // Tag-Keyframe und Originalwerte aus der aktuellen Szene übernehmen
        public static void CaptureDay(SiegeAtmosphere a)
        {
            var sun = RenderSettings.sun != null ? RenderSettings.sun : FindDirectional();
            a.Sun = sun;
            a.OrigAmbientMode = RenderSettings.ambientMode;
            a.OrigAmbientSky = RenderSettings.ambientSkyColor;
            a.OrigAmbientEquator = RenderSettings.ambientEquatorColor;
            a.OrigAmbientGround = RenderSettings.ambientGroundColor;
            a.OrigFog = RenderSettings.fog;
            a.OrigFogMode = RenderSettings.fogMode;
            a.OrigFogColor = RenderSettings.fogColor;
            a.OrigFogDensity = RenderSettings.fogDensity;
            a.OrigSkybox = RenderSettings.skybox;
            a.OrigSunShadows = sun != null ? sun.shadows : LightShadows.Soft;

            // Umgebungslicht der Skybox als Trilight-Farben annähern (oben / Horizont / unten)
            var dirs = new[] { Vector3.up, Vector3.forward, Vector3.down };
            var cols = new Color[3];
            RenderSettings.ambientProbe.Evaluate(dirs, cols);

            var d = new AtmosphereKey { Name = "Tag", Time = 0f };
            if (sun != null)
            {
                d.SunEuler = sun.transform.eulerAngles;
                d.SunColor = sun.color;
                d.SunIntensity = sun.intensity;
                d.ShadowStrength = sun.shadowStrength;
            }
            // Empirisch ×2: Trilight wirkt bei gleichen Farben deutlich dunkler als die SH-Auswertung (kein Helligkeitssprung bei t > 0)
            d.AmbientSky = cols[0] * 2f; d.AmbientEquator = cols[1] * 2f; d.AmbientGround = cols[2] * 2f;
            d.FogColor = new Color(0.78f, 0.8f, 0.82f);
            d.FogDensity = 0f;
            var sky = RenderSettings.skybox;
            d.SkyExposure = sky != null && sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1f;
            d.SkyTint = sky != null && sky.HasProperty("_SkyTint") ? sky.GetColor("_SkyTint") : Color.gray;
            d.SkyGround = sky != null && sky.HasProperty("_GroundColor") ? sky.GetColor("_GroundColor") : Color.gray;
            d.SkyAtmosphere = sky != null && sky.HasProperty("_AtmosphereThickness") ? sky.GetFloat("_AtmosphereThickness") : 1f;
            d.LanternFactor = 1f;
            d.WindowGlow = 1f;
            a.Day = d;
            a.DayCaptured = true;
        }

        // Standardwerte Nachmittag / Dämmerung / Nacht
        public static void DefaultKeys(SiegeAtmosphere a)
        {
            a.Afternoon = new AtmosphereKey
            {
                Name = "Nachmittag", Time = 0.35f,
                SunEuler = new Vector3(34f, 305f, 0f), SunColor = new Color(1f, 0.84f, 0.6f), SunIntensity = 1.05f, ShadowStrength = 1f,
                AmbientSky = new Color(0.62f, 0.6f, 0.58f), AmbientEquator = new Color(0.62f, 0.52f, 0.42f), AmbientGround = new Color(0.3f, 0.26f, 0.22f),
                FogColor = new Color(0.86f, 0.74f, 0.58f), FogDensity = 0.003f,
                SkyExposure = 1.25f, SkyTint = new Color(0.55f, 0.48f, 0.42f), SkyGround = new Color(0.42f, 0.36f, 0.3f), SkyAtmosphere = 1.3f,
                LanternFactor = 1.2f, WindowGlow = 1.3f,
            };
            a.Dusk = new AtmosphereKey
            {
                Name = "Dämmerung", Time = 0.7f,
                SunEuler = new Vector3(14f, 282f, 0f), SunColor = new Color(1f, 0.55f, 0.38f), SunIntensity = 0.9f, ShadowStrength = 0.9f,
                AmbientSky = new Color(0.4f, 0.33f, 0.5f), AmbientEquator = new Color(0.5f, 0.34f, 0.36f), AmbientGround = new Color(0.17f, 0.13f, 0.17f),
                FogColor = new Color(0.5f, 0.34f, 0.42f), FogDensity = 0.008f,
                SkyExposure = 0.9f, SkyTint = new Color(0.62f, 0.34f, 0.55f), SkyGround = new Color(0.3f, 0.2f, 0.25f), SkyAtmosphere = 1.4f,
                LanternFactor = 2f, WindowGlow = 1.6f,
            };
            a.Night = new AtmosphereKey
            {
                Name = "Nacht", Time = 1f,
                SunEuler = new Vector3(52f, 145f, 0f), SunColor = new Color(0.56f, 0.68f, 1f), SunIntensity = 0.65f, ShadowStrength = 0.7f,
                AmbientSky = new Color(0.27f, 0.32f, 0.52f), AmbientEquator = new Color(0.19f, 0.22f, 0.36f), AmbientGround = new Color(0.09f, 0.09f, 0.14f),
                FogColor = new Color(0.1f, 0.12f, 0.22f), FogDensity = 0.011f,
                SkyExposure = 0.3f, SkyTint = new Color(0.2f, 0.26f, 0.5f), SkyGround = new Color(0.05f, 0.05f, 0.08f), SkyAtmosphere = 0.6f,
                LanternFactor = 2.6f, WindowGlow = 1.9f,
            };
        }
    }
}
