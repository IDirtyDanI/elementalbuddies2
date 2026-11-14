using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies.EditorTools
{
    // Dimensionsriss, Paket I (Integration; wiki: Design/Dimensionsriss-Plan): baut aus den Roh-Assets
    //  - die Arena-Prefabs (Models/Arena/*.fbx + *_layout.json → Prefabs/Arena/Arena_*.prefab, Vertrag siehe DimensionArena),
    //  - die beiden Dimensions-Bosse (Models/Enemies/Bosses/*.fbx + .boss.json → Prefabs/Bosses/Dimension/*.prefab,
    //    Configs ScriptableObjects/Configs/Bosses/BossSoulReaper|BossOgreChief, Fähigkeiten, Controller, Porträts),
    //  - Kodex-Einträge, Netz-Prefab-Liste und die Map-Felder (Boss, Arena, Lichtstimmung).
    // Alles idempotent, ohne Szenen zu speichern. Die Werte in dieser Datei sind die Quelle der Wahrheit für die beiden
    // Bosse (erneuter Lauf setzt Prefab/Config darauf zurück).
    public static class DimensionSetup
    {
        public const string ArenaModelDir = "Assets/_Game/Models/Arena/";
        public const string ArenaPrefabDir = "Assets/_Game/Prefabs/Arena/";
        public const string BossPrefabDir = "Assets/_Game/Prefabs/Bosses/Dimension/";
        public const string BossConfigDir = "Assets/ScriptableObjects/Configs/Bosses/";
        private const string SourceBossPrefab = "Assets/_Game/Prefabs/Bosses/BossBoneLord.prefab";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string CodexPath = "Assets/Resources/CodexDatabase.asset";
        private const string MapCity = "Assets/Resources/Maps/Map_City.asset";
        private const string MapForest = "Assets/Resources/Maps/Map_Forest.asset";
        private const string FxDir = "Assets/_Game/VFX/Prefabs/Bosses/";
        public const string DimFxDir = "Assets/_Game/VFX/Prefabs/Dimension/";
        private const string FlameMat = "Assets/_Game/VFX/Materials/VFX_Flame_Add.mat";
        private const string SoftMat = "Assets/_Game/VFX/Materials/VFX_SoftDot_Add.mat";
        public const string EventIconPath = IconDir + "event_dimension.png";

        public const string ReaperConfig = BossConfigDir + "BossSoulReaper.asset";
        public const string OgreConfig = BossConfigDir + "BossOgreChief.asset";
        public const string ReaperPrefab = BossPrefabDir + "Seelenschnitter.prefab";
        public const string OgrePrefab = BossPrefabDir + "Oger_Haeuptling.prefab";
        public const string ArenaCity = ArenaPrefabDir + "Arena_Totenreich.prefab";
        public const string ArenaForest = ArenaPrefabDir + "Arena_Kriegslager.prefab";

        // Begehbare Scheibe: 8 gedrehte Quader (Grundkörper – sicher für das Laufzeit-NavMesh im Build). Halblänge 16,9 m
        // (deckt in jeder Richtung mindestens bis 16,9 m), Ecken auf dem Umkreis 17,4 m: reicht überall bis unter den
        // Mauerring (innen 16,5 m, außen 17,5 m) und nirgends darüber hinaus.
        public const float FloorDiscRadius = 17.4f;
        public const float FloorHalfLength = 16.9f;
        public const int FloorBoxes = 8;
        // Leuchten der Arena-Materialien deckeln (Mobil-Low hat kein Tonemapping → sonst brennt es zu Weiß aus)
        private const float ArenaGlowCap = 1.5f;
        public const int MaxArenaLights = 4;

        // ---------------- Menü ----------------

        [MenuItem("BuddyTD/Dimensionsriss/Alles einrichten (Arenen, Bosse, Maps)")]
        public static void SetupAllMenu() => SetupAll();

        [MenuItem("BuddyTD/Dimensionsriss/1 Arenen bauen")]
        public static void ArenasMenu() => Run(BuildArenas);

        [MenuItem("BuddyTD/Dimensionsriss/2 Bosse bauen (Modelle, Prefabs, Configs, Porträts)")]
        public static void BossesMenu() => Run(sb => BuildBosses(sb, true));

        [MenuItem("BuddyTD/Dimensionsriss/2b Fähigkeits-Effekte der Bosse färben (VFX/Prefabs/Dimension)")]
        public static void FxMenu() => Run(BuildAbilityFx);

        [MenuItem("BuddyTD/Dimensionsriss/2c Nur Boss-Porträts rendern")]
        public static void PortraitsMenu() => Run(sb => { foreach (var spec in Bosses) RenderPortrait(spec, sb); });

        [MenuItem("BuddyTD/Dimensionsriss/3 Kodex ergänzen")]
        public static void CodexMenu() => Run(SetupCodex);

        [MenuItem("BuddyTD/Dimensionsriss/4 Netz-Prefab-Liste aktualisieren")]
        public static void NetMenu() => Run(SetupNet);

        [MenuItem("BuddyTD/Dimensionsriss/5 Maps eintragen (Boss, Arena, Lichtstimmung)")]
        public static void MapsMenu() => Run(SetupMaps);

        private static void Run(System.Action<StringBuilder> step)
        {
            var sb = new StringBuilder("DimensionSetup:");
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning(sb.Append(" nur im Edit-Modus.")); return; }
            step(sb);
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        public static string SetupAll(bool renderIcons = true)
        {
            var sb = new StringBuilder("DimensionSetup.SetupAll:");
            if (EditorApplication.isPlayingOrWillChangePlaymode) { sb.Append(" nur im Edit-Modus."); Debug.LogWarning(sb); return sb.ToString(); }
            BuildArenas(sb);
            BuildBosses(sb, renderIcons);
            SetupCodex(sb);
            SetupNet(sb);
            SetupMaps(sb);
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        // =====================================================================================================
        // Arenen
        // =====================================================================================================

        [System.Serializable] public class LayoutMat { public string name, hex, emissionHex; public float emission; }
        [System.Serializable] public class LayoutCollider { public string type, tag, note; public float[] center, size; public float rotY, radius, height; }
        [System.Serializable] public class LayoutPose { public float[] pos; public float rotY; }
        [System.Serializable] public class LayoutLight { public string name, colorHex, kind; public float[] pos; public float intensityHint, range; }
        [System.Serializable] public class LayoutVfx { public string name, kind; public float[] pos; }
        [System.Serializable]
        public class LayoutAnchors
        {
            public LayoutPose entry, bossSpawn, returnPortal;
            public LayoutLight[] lights = new LayoutLight[0];
            public LayoutVfx[] vfx = new LayoutVfx[0];
        }
        [System.Serializable]
        public class Layout
        {
            public string arena;
            public LayoutMat[] materials = new LayoutMat[0];
            public LayoutCollider[] colliders = new LayoutCollider[0];
            public LayoutAnchors anchors;
        }

        public static readonly string[] ArenaNames = { "Totenreich", "Kriegslager" };

        // Farb-Korrekturen gegenüber layout.json bzw. .boss.json (Slot → Hex). Quelle der Wahrheit für das Spiel:
        //  - Kriegslager (#573, Tablet „sehr einfarbig orange“): Erde grauer/kühler (das Orange kommt von den Feuern),
        //    Stroh heller, Banner-Rot und Grün kräftiger → mehr Farbtrennung
        //  - Totenreich (#574): Bodenmitte etwas heller, damit sich der dunkle Seelenschnitter abhebt
        //  - Seelenschnitter (#574): Robe/Mantel heller und entsättigt (Silhouette gegen den dunkelblauen Boden)
        public static readonly Dictionary<string, string> ColorOverrides = new Dictionary<string, string>
        {
            { "Lager_Earth", "#84725f" }, { "Lager_EarthDark", "#5a4d42" }, { "Lager_Straw", "#dcc88e" },
            { "Lager_Banner", "#c4281a" }, { "Lager_Green", "#5c9a3c" }, { "Lager_Rock", "#8a8a8c" },
            { "Reaper_Robe", "#6a60b4" }, { "Reaper_RobeDark", "#37315f" }, { "Reaper_Mantle", "#a89df2" },
        };

        // Eigenleuchten (Anteil der Grundfarbe) – hält die Farbe unabhängig vom dunkelblauen Licht des Totenreichs sichtbar
        public static readonly Dictionary<string, float> SelfGlow = new Dictionary<string, float>
        {
            { "Reaper_Robe", 0.22f }, { "Reaper_Mantle", 0.4f },
        };

        private static string OverrideHex(string slot, string hex) => ColorOverrides.TryGetValue(slot, out var o) ? o : hex;
        private static readonly string[] ArenaParts = { "Floor", "Ring", "Props", "Backdrop" };

        public static Layout LoadLayout(string arena)
        {
            string p = ArenaModelDir + arena + "_layout.json";
            return File.Exists(p) ? JsonUtility.FromJson<Layout>(File.ReadAllText(p)) : null;
        }

        private static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        public static void BuildArenas(StringBuilder sb)
        {
            NetworkSetup.EnsureFolder(ArenaPrefabDir.TrimEnd('/'));
            foreach (var name in ArenaNames)
            {
                try { BuildArena(name, sb); }
                catch (System.Exception e) { sb.Append($"\n  FEHLER Arena {name}: {e.Message}"); Debug.LogException(e); }
            }
        }

        private static void BuildArena(string name, StringBuilder sb)
        {
            var layout = LoadLayout(name);
            if (layout == null || layout.anchors == null) { sb.Append($"\n  Arena {name}: layout.json fehlt"); return; }

            // 1) Import: ohne Rig, ein URP/Lit-Material je Slot (Models/Arena/Materials), nicht lesbar (keine MeshCollider)
            var mats = new List<ForestEnemyBuilder.SideMat>();
            foreach (var m in layout.materials)
            {
                m.hex = OverrideHex(m.name, m.hex);
                mats.Add(new ForestEnemyBuilder.SideMat { name = m.name, hex = m.hex, emission = m.emission });
            }
            var side = new ForestEnemyBuilder.Sidecar { model = name, rig = "None", materials = mats.ToArray() };
            foreach (var part in ArenaParts)
            {
                string fbx = ArenaModelDir + name + "_" + part + ".fbx";
                if (AssetImporter.GetAtPath(fbx) == null) { sb.Append($"\n  Arena {name}: {fbx} fehlt"); return; }
                ForestEnemyBuilder.ConfigureModelImport(fbx, side, false);
            }
            foreach (var m in layout.materials)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(ArenaModelDir + "Materials/" + m.name + ".mat");
                if (mat == null) continue;
                if (ColorUtility.TryParseHtmlString(m.hex, out var baseCol)) { mat.SetColor("_BaseColor", baseCol); mat.SetColor("_Color", baseCol); }
                if (m.emission > 0f)
                {
                    string hex = string.IsNullOrEmpty(m.emissionHex) ? m.hex : m.emissionHex;
                    if (ColorUtility.TryParseHtmlString(hex, out var ec))
                    {
                        mat.EnableKeyword("_EMISSION");
                        // Boden-Runen/-Bemalung gedimmt: Sie liegen unter den Warnflächen (gleiche Farbfamilie wie die Leitfarbe
                        // des Bosses) und dürfen sie nicht überstrahlen
                        bool floorMark = m.name == "Toten_Rune";
                        mat.SetColor("_EmissionColor", ec * (floorMark ? 0.55f : Mathf.Min(m.emission, ArenaGlowCap)));
                        if (floorMark && ColorUtility.TryParseHtmlString(m.hex, out var bc)) { mat.SetColor("_BaseColor", bc * 0.75f); mat.SetColor("_Color", bc * 0.75f); }
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                }
                EditorUtility.SetDirty(mat);
            }

            // 2) Prefab
            var root = new GameObject("Arena_" + name);
            try
            {
                // Anker zuerst (DimensionArena sucht den ersten Treffer je Name)
                Anchor(root.transform, DimensionArena.EntryName, layout.anchors.entry);
                Anchor(root.transform, DimensionArena.BossSpawnName, layout.anchors.bossSpawn);
                Anchor(root.transform, DimensionArena.ReturnPortalName, layout.anchors.returnPortal);

                // Optik: vier Modelle unverändert im Ursprung
                var visual = Child(root.transform, "Visual");
                int tris = 0;
                foreach (var part in ArenaParts)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaModelDir + name + "_" + part + ".fbx");
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
                    inst.name = part;
                    inst.transform.localPosition = Vector3.zero;
                    inst.transform.localRotation = Quaternion.identity;
                    inst.transform.localScale = Vector3.one;
                    foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        // Schatten: nur Ring und Requisiten werfen (Boden/Hintergrund empfangen bzw. liegen im Nebel)
                        bool cast = part == "Ring" || part == "Props";
                        r.shadowCastingMode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.receiveShadows = part != "Backdrop";
                        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                    }
                    foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                }
                foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

                // Boden: nur die begehbare Scheibe (das Boden-Mesh hat Rand und Unterseite) – Layer/Tag Floor
                var floor = Child(root.transform, "FloorCollider");
                float a = FloorHalfLength;
                float b = Mathf.Sqrt(FloorDiscRadius * FloorDiscRadius - a * a); // Halbbreite (Ecken auf dem Umkreis)
                for (int i = 0; i < FloorBoxes; i++)
                {
                    var go = new GameObject("Floor_" + i) { layer = DimensionArena.FloorLayer, tag = "Floor" };
                    go.transform.SetParent(floor, false);
                    go.transform.localRotation = Quaternion.Euler(0f, i * 180f / FloorBoxes, 0f);
                    var box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(2f * a, 1f, 2f * b);
                    box.center = new Vector3(0f, -0.5f, 0f);
                }
                floor.gameObject.layer = DimensionArena.FloorLayer;
                floor.gameObject.tag = "Floor";

                // Begrenzung + Requisiten: Grundkörper aus dem Layout (Layer Default), im NavMesh als „nicht begehbar“
                var walls = Child(root.transform, "Walls");
                var props = Child(root.transform, "PropColliders");
                foreach (var t in new[] { walls, props })
                {
                    var mod = t.gameObject.AddComponent<NavMeshModifier>();
                    mod.overrideArea = true;
                    mod.area = 1; // Not Walkable: keine NavMesh-Inseln auf Mauerkrone/Sarkophagen
                    mod.applyToChildren = true;
                }
                int nWall = 0, nProp = 0;
                foreach (var c in layout.colliders)
                {
                    bool wall = c.tag == "wall";
                    var go = new GameObject(wall ? "Wall_" + nWall++ : "Prop_" + nProp++ + (string.IsNullOrEmpty(c.note) ? "" : " " + c.note));
                    go.transform.SetParent(wall ? walls : props, false);
                    go.transform.localPosition = V(c.center);
                    go.transform.localRotation = Quaternion.Euler(0f, c.rotY, 0f);
                    if (c.type == "capsule")
                    {
                        var cap = go.AddComponent<CapsuleCollider>();
                        cap.direction = 1;
                        cap.radius = c.radius;
                        cap.height = Mathf.Max(c.height, c.radius * 2f);
                    }
                    else go.AddComponent<BoxCollider>().size = V(c.size);
                }

                // Lichter: höchstens vier Punktlichter ohne Schatten (Mobil: Zusatzlichter je Objekt begrenzt)
                var lights = Child(root.transform, "Lights");
                for (int i = 0; i < Mathf.Min(MaxArenaLights, layout.anchors.lights.Length); i++)
                {
                    var l = layout.anchors.lights[i];
                    var go = new GameObject(l.name);
                    go.transform.SetParent(lights, false);
                    go.transform.localPosition = V(l.pos) + Vector3.up * 0.4f;
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.shadows = LightShadows.None;
                    light.range = l.range;
                    // Kriegslager: Die Feuer tragen das Orange (die Sonne ist warmweiß) → kräftiger und weiter
                    bool camp = name == "Kriegslager";
                    light.intensity = l.intensityHint * (camp ? 1.6f : 1f);
                    if (camp) light.range = l.range * 1.25f;
                    light.renderMode = LightRenderMode.Auto;
                    if (ColorUtility.TryParseHtmlString(l.colorHex, out var lc)) light.color = lc;
                }

                // Flammen: einfache Dauer-Partikel an allen Feuerstellen
                var vfx = Child(root.transform, "VFX");
                bool undead = name == "Totenreich";
                foreach (var v in layout.anchors.vfx)
                    Flame(vfx, v.name, V(v.pos), v.kind, undead);

                Directory.CreateDirectory(ArenaPrefabDir);
                string dst = ArenaPrefabDir + "Arena_" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, dst);
                sb.Append($"\n  {dst}: {tris} Dreiecke, {nWall} Mauer-Quader, {nProp} Requisiten-Collider, {FloorBoxes} Boden-Quader (2×{a:0.00} × 2×{b:0.00} m), " +
                          $"{Mathf.Min(MaxArenaLights, layout.anchors.lights.Length)} Lichter, {layout.anchors.vfx.Length} Flammen");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static void Anchor(Transform root, string name, LayoutPose pose)
        {
            var t = Child(root, name);
            t.localPosition = pose != null ? V(pose.pos) : Vector3.zero;
            t.localRotation = Quaternion.Euler(0f, pose != null ? pose.rotY : 0f, 0f);
        }

        // Flamme (Seelenfeuer grün / Lagerfeuer und Fackeln orange): zwei kleine Dauer-Partikelsysteme (Flamme + Funken)
        private static void Flame(Transform parent, string name, Vector3 pos, string kind, bool undead)
        {
            bool big = kind == "campfire";
            float s = big ? 1.5f : (undead ? 1.1f : 0.8f);
            Color core = undead ? new Color(0.45f, 1f, 0.5f, 0.9f) : new Color(1f, 0.62f, 0.2f, 0.9f);
            Color tip = undead ? new Color(0.15f, 0.9f, 0.55f, 0f) : new Color(1f, 0.25f, 0.05f, 0f);
            var flameMat = AssetDatabase.LoadAssetAtPath<Material>(FlameMat);
            var softMat = AssetDatabase.LoadAssetAtPath<Material>(SoftMat);
            if (flameMat == null) flameMat = softMat;

            var go = new GameObject("Flame_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.prewarm = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f * s, 1.3f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f * s, 0.8f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = core;
            main.maxParticles = 20;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 14f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.16f * s;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(core, 0f), new GradientColorKey(tip, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = flameMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var sparks = new GameObject("Sparks");
            sparks.transform.SetParent(go.transform, false);
            var sp = sparks.AddComponent<ParticleSystem>();
            var sm = sp.main;
            sm.loop = true; sm.playOnAwake = true; sm.prewarm = true;
            sm.duration = 3f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            sm.startColor = undead ? new Color(0.6f, 1f, 0.75f, 1f) : new Color(1f, 0.75f, 0.35f, 1f);
            sm.maxParticles = 10;
            sm.simulationSpace = ParticleSystemSimulationSpace.Local;
            var se = sp.emission; se.enabled = true; se.rateOverTime = big ? 4f : 2.5f;
            var ss = sp.shape; ss.enabled = true; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 22f; ss.radius = 0.2f * s;
            ss.rotation = new Vector3(-90f, 0f, 0f);
            var sc = sp.colorOverLifetime; sc.enabled = true;
            var g2 = new Gradient();
            g2.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            sc.color = g2;
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = softMat != null ? softMat : flameMat;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
        }

        // =====================================================================================================
        // Bosse
        // =====================================================================================================

        public class BossSpec
        {
            public string Model, ConfigPath, PrefabPath, DisplayName, Hint;
            public float Speed, AttackDamage, AttackInterval, AttackRange, BaseHP, Armor;
            public float CapsuleRadius, CapsuleHeight, AgentRadius;   // Wurzel-Raum (Spiel: × VisualScale)
            public SfxId SpawnSfx, MeleeSfx, DeathSfx, AttackSfx;
            public bool AttackSound, DeathSound;
            public string DeathVfx;
            public System.Func<List<BossAbility>> Abilities;
            // Warnflächen: Alpha 0 = ThemeColor des Bosses
            public Color Telegraph = new Color(0f, 0f, 0f, 0f);
            // Aura unter dem Boss (Glow/Ring): Größe und Deckkraft – absolut, damit erneute Läufe nichts aufschaukeln
            public float AuraGlowSize = 1.7f, AuraGlowAlpha = 0.35f, AuraRingSize = 1.1f, AuraRingAlpha = 0.6f;
            // Porträt: Ausschnitt um den Kopf-Knochen – Radius und Versatz der Bildmitte als Anteil der Figurenhöhe
            // Punktlicht am Boss in der Leitfarbe (0 = keins): heller Lichtfleck auf dem Boden, vor dem sich die Gestalt abhebt
            public float GlowLight = 0f, GlowLightRange = 7f;
            public float PortraitRadius = 0.26f, PortraitDown = 0.06f, PortraitYaw = 25f, PortraitUp = 0.12f;
        }

        public static readonly BossSpec Reaper = new BossSpec
        {
            Model = "Seelenschnitter", ConfigPath = ReaperConfig, PrefabPath = ReaperPrefab,
            DisplayName = "Seelenschnitter",
            Hint = "Boss des Totenreichs: Sensenwirbel, Seelenschnitt und Seelenregen – raus aus den Warnflächen",
            Speed = 1.6f, AttackDamage = ReaperMeleeDps, AttackInterval = 1.8f, AttackRange = 2.8f, BaseHP = 1000f, Armor = 0f,
            CapsuleRadius = 0.42f, CapsuleHeight = 2f, AgentRadius = 0.4f,
            SpawnSfx = SfxId.ReaperRoar, MeleeSfx = SfxId.EnemyChop, AttackSound = false, DeathSound = true, DeathSfx = SfxId.ReaperDeath,
            DeathVfx = "Assets/_Game/VFX/Prefabs/VFX_BossDeath.prefab",
            Abilities = ReaperAbilities,
            // #574: kräftiger heller Schein unter dem Boss – hebt die dunkle Gestalt vom dunklen Boden ab
            AuraGlowSize = 3.4f, AuraGlowAlpha = 0.6f, AuraRingSize = 2.3f, AuraRingAlpha = 0.9f,
            GlowLight = 3f, GlowLightRange = 7.5f,
            PortraitRadius = 0.2f, PortraitDown = 0.05f, PortraitYaw = 20f,
        };

        public static readonly BossSpec Ogre = new BossSpec
        {
            Model = "Oger_Haeuptling", ConfigPath = OgreConfig, PrefabPath = OgrePrefab,
            DisplayName = "Oger-Häuptling",
            Hint = "Boss des Kriegslagers: springt mit dem Erdstoß heran, Keulenschwung und Kriegsgebrüll – raus aus den Warnflächen",
            Speed = 1.5f, AttackDamage = OgreMeleeDps, AttackInterval = 2f, AttackRange = 3.1f, BaseHP = 1000f, Armor = 0f,
            CapsuleRadius = 0.55f, CapsuleHeight = 1.85f, AgentRadius = 0.5f,
            SpawnSfx = SfxId.OgreRoar, MeleeSfx = SfxId.EnemyChop, AttackSound = true, AttackSfx = SfxId.OrkGrunt, DeathSound = true, DeathSfx = SfxId.OrkDeath,
            DeathVfx = DeathVfxBuilder.BossForest,
            Abilities = OgreAbilities,
            // #573: Warnflächen in heißem Gelb statt Leitfarbe Orange (#FF7A2E ging auf dem braunen Boden unter)
            Telegraph = new Color(1f, 0.9f, 0.32f, 1f),
            PortraitRadius = 0.23f, PortraitDown = -0.02f, PortraitYaw = 22f,
        };

        public static readonly BossSpec[] Bosses = { Reaper, Ogre };

        // Grundangriff als DPS-Wert (Schaden je Hieb = Wert × AttackInterval × Wellen-Faktor).
        // Schadenswerte (Grundangriff + Fähigkeiten) auf „wer stehen bleibt, fällt nach ≈ 8–12 s“ abgestimmt (100 LP, Welle 20,
        // Schadensfaktor ×1,38 = halbe Wellen-Rampe; Play-Mode-Messung 2026-11-14: ≈ 11 LP/s im Stand). Die Stadt-/Dorf-Bosse
        // (20 DPS, Fähigkeiten 25–60) teilen ihren Schaden mit Buddies – hier ist der Champion das einzige Ziel.
        // 2026-11-14 (DimensionFightBench, echte Kämpfe mit Welle-20-Karten): Der Grundangriff ist für Nahkämpfer nicht
        // auszuweichen – er lief über die Kampfdauer auf mehr als ihr ganzes Leben auf. Deshalb halbiert; die Gefahr
        // tragen die Fähigkeiten mit Warnfläche (dafür etwas stärker).
        public const float ReaperMeleeDps = 1.8f;
        public const float OgreMeleeDps = 1.4f;

        private static BossAbility Ability(string name, BossAbilityKind kind, float cd, float windup, string trigger, float triggerRange, float damage, SfxId sfx)
        {
            return new BossAbility
            {
                Name = name, Kind = kind, Cooldown = cd, Windup = windup, AnimatorTrigger = trigger,
                TriggerRange = triggerRange, Damage = damage, ImpactSfx = sfx, Recovery = 0.5f,
                ScaleEffectBySize = false, EffectLifetime = 3f, BuddyStunDuration = 0f,
            };
        }

        private static GameObject Fx(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(FxDir + name + ".prefab");

        // Gefärbte Kopie eines Boss-Effekts (BuildAbilityFx); fehlt sie, das Original
        private static GameObject DimFx(string name, string fallback)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(DimFxDir + name + ".prefab");
            return go != null ? go : Fx(fallback);
        }

        // Seelenschnitter: Regen (auf Abstand) → Kegel → Wirbel. Ausholzeiten = Trefferzeiten der Clips (.boss.json).
        public static List<BossAbility> ReaperAbilities()
        {
            var rain = Ability("Seelenregen", BossAbilityKind.Rain, 14f, 1.2f, "Cast", 16f, 6f, SfxId.BowHit);
            rain.Radius = 4f; rain.MaxCastRange = 16f; rain.Pulses = 5; rain.PulseDuration = 1.25f; rain.ArrowsPerPulse = 5;
            // #570: drei Flächen je Wurf (Ziel + andere Spieler bzw. 4–7 m daneben); Schaden je Fläche unverändert – wer in
            // einer Fläche stehen bleibt, nimmt dasselbe wie zuvor (5 × 5)
            rain.RainAreas = 3; rain.RainSpread = new Vector2(4f, 7f);
            rain.FallingArrowPrefab = DimFx("Reaper_SoulBolt", "Boss_FallingArrow"); rain.PulseEffectPrefab = DimFx("VFX_Reaper_SoulRainImpact", "VFX_Boss_ArrowHailImpact");
            rain.ImpactEffectPrefab = DimFx("VFX_Reaper_SoulRainArea", "VFX_Boss_ArrowHailArea"); rain.ScaleEffectBySize = true; rain.EffectLifetime = 2f;

            var cone = Ability("Seelenschnitt", BossAbilityKind.Cone, 8f, 1.1f, "Reap", 8f, 26f, SfxId.FireWave);
            cone.Length = 9f; cone.ConeAngle = 70f; cone.PlayerSlow = 0.4f; cone.PlayerSlowDuration = 2f;
            cone.ImpactEffectPrefab = DimFx("VFX_Reaper_SoulCut", "VFX_Boss_SoulWave");

            var whirl = Ability("Sensenwirbel", BossAbilityKind.Whirl, 10f, 1f, "Whirl", 4.2f, 22f, SfxId.FireWave);
            whirl.Radius = 4.5f; whirl.PlayerKnockback = 2f; whirl.ImpactEffectPrefab = DimFx("VFX_Reaper_Whirl", "VFX_Boss_BloodWhirl");
            whirl.ArcFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/VFX/Prefabs/Champions/VFX_SwordSlash.prefab");
            return new List<BossAbility> { rain, cone, whirl };
        }

        // Oger-Häuptling: Sprung-Aufschlag → Gebrüll → Keulenschwung. „Betäubung“ der Spieler = starke kurze Verlangsamung
        // (die Fähigkeits-Technik kennt für Spieler nur Verlangsamung und Rückstoß).
        public static List<BossAbility> OgreAbilities()
        {
            var slam = Ability("Erdstoß", BossAbilityKind.Slam, 12f, 0.9f, "Slam", 12f, 28f, SfxId.StoneWall);
            slam.LeapDistance = 9f; slam.LeapDuration = 0.6f; slam.LeapHeight = 1.6f; slam.Radius = 5.5f;
            slam.PlayerSlow = 0.7f; slam.PlayerSlowDuration = 1f; slam.BuddyStunDuration = 1.5f;
            slam.ImpactEffectPrefab = DimFx("VFX_Ogre_Quake", "VFX_Boss_BloodQuake");

            // Schaden klein: Im Nahkampf ist dem Gebrüll (r 8 m, 1,3 s) nicht zu entkommen – es bremst, die Folge-Fähigkeit trifft
            var roar = Ability("Kriegsgebrüll", BossAbilityKind.Nova, 16f, 1.3f, "Roar", 6.5f, 8f, SfxId.OgreBellow);
            roar.Radius = 8f; roar.PlayerSlow = 0.6f; roar.PlayerSlowDuration = 1.5f; roar.BuddyStunDuration = 2f;
            roar.ImpactEffectPrefab = DimFx("VFX_Ogre_Roar", "VFX_Boss_GraveFrost");

            var sweep = Ability("Keulenschwung", BossAbilityKind.Cone, 7f, 1f, "Sweep", 5f, 22f, SfxId.FireWave);
            sweep.Length = 6f; sweep.ConeAngle = 100f; sweep.PlayerKnockback = 2.5f;
            sweep.ImpactEffectPrefab = DimFx("VFX_Ogre_Sweep", "VFX_Boss_SoulWave");
            return new List<BossAbility> { slam, roar, sweep };
        }

        public static void BuildBosses(StringBuilder sb, bool renderIcons)
        {
            NetworkSetup.EnsureFolder(BossPrefabDir.TrimEnd('/'));
            try { BuildAbilityFx(sb); }
            catch (System.Exception e) { sb.Append($"\n  FEHLER Fähigkeits-Effekte: {e.Message}"); Debug.LogException(e); }
            foreach (var spec in Bosses)
            {
                try { BuildBoss(spec, renderIcons, sb); }
                catch (System.Exception e) { sb.Append($"\n  FEHLER Boss {spec.Model}: {e.Message}"); Debug.LogException(e); }
            }
            PacingTexts();
        }

        // Namen/Hinweise wie PacingSetup.Enemies (dort stehen dieselben Zeilen für spätere Komplettläufe)
        private static void PacingTexts()
        {
            foreach (var spec in Bosses)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(spec.ConfigPath);
                if (cfg != null && PacingSetup.ApplyText(cfg, spec.ConfigPath)) EditorUtility.SetDirty(cfg);
            }
        }

        private static void BuildBoss(BossSpec spec, bool renderIcon, StringBuilder sb)
        {
            string fbx = BossBuilder.ForestModelDir + spec.Model + ".fbx";
            string sidePath = BossBuilder.ForestModelDir + spec.Model + ".boss.json";
            if (AssetImporter.GetAtPath(fbx) == null || !File.Exists(sidePath)) { sb.Append($"\n  {spec.Model}: Modell/Sidecar fehlt ({fbx})"); return; }
            string json = File.ReadAllText(sidePath);
            var side = JsonUtility.FromJson<ForestEnemyBuilder.Sidecar>(json);
            var boss = JsonUtility.FromJson<BossBuilder.BossSidecar>(json);
            // Leitfarbe aus dem unveränderten Sidecar; danach die Farb-Korrekturen (ColorOverrides)
            Color themeSrc = Color.white;
            foreach (var m in side.materials)
                if (m.emission > 0f && ColorUtility.TryParseHtmlString(m.hex, out var tc0)) { themeSrc = tc0; break; }
            foreach (var m in side.materials) m.hex = OverrideHex(m.name, m.hex);
            ForestEnemyBuilder.ConfigureModelImport(fbx, side, true);
            foreach (var m in side.materials)
            {
                if (!ColorOverrides.ContainsKey(m.name)) continue;
                var om = AssetDatabase.LoadAssetAtPath<Material>(BossBuilder.ForestModelDir + "Materials/" + m.name + ".mat");
                if (om != null && ColorUtility.TryParseHtmlString(m.hex, out var oc))
                {
                    om.SetColor("_BaseColor", oc);
                    om.SetColor("_Color", oc);
                    if (SelfGlow.TryGetValue(m.name, out float glow))
                    {
                        om.EnableKeyword("_EMISSION");
                        om.SetColor("_EmissionColor", oc * glow);
                        om.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    EditorUtility.SetDirty(om);
                }
            }

            // Leitfarbe = leuchtendes Material des Modells
            Color theme = Color.white;
            foreach (var m in side.materials)
                if (m.emission > 0f && ColorUtility.TryParseHtmlString(m.hex, out var tc)) { theme = tc; break; }
            // Leuchten deckeln (kein Tonemapping auf Mobil-Low)
            foreach (var m in side.materials)
            {
                if (m.emission <= 0f) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(BossBuilder.ForestModelDir + "Materials/" + m.name + ".mat");
                if (mat != null && ColorUtility.TryParseHtmlString(m.hex, out var gc)) { mat.SetColor("_EmissionColor", gc * Mathf.Min(m.emission, 1.6f)); EditorUtility.SetDirty(mat); }
            }

            // Config
            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(spec.ConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<EnemyConfigSO>();
                AssetDatabase.CreateAsset(cfg, spec.ConfigPath);
            }
            float sideAttack = side.Role("attack") != null ? side.Role("attack").hit : 0.5f;
            cfg.Type = UnitType.EnemyRunner;
            cfg.Role = EnemyRole.DimensionBoss;
            cfg.IsBoss = true;
            cfg.DisplayName = spec.DisplayName;
            cfg.PreviewHint = spec.Hint;
            cfg.ThemeColor = theme;
            cfg.ControlResistance = 0.6f;
            cfg.BountyMultiplier = 20f;
            cfg.HuntsBuddies = false;
            cfg.BuddyDamageMultiplier = 1f;
            cfg.BuddyAggroRadius = 6f;
            cfg.BaseHP = spec.BaseHP;       // beliebig: das Leben setzt WaveManager.DimensionBossTargetHp
            cfg.Armor = spec.Armor;
            cfg.Speed = spec.Speed;
            cfg.AttackDamage = spec.AttackDamage;
            cfg.AttackInterval = spec.AttackInterval;
            cfg.AttackWindup = sideAttack;
            cfg.AttackRange = 0f;
            cfg.ProjectilePrefab = null;
            cfg.VisualScale = Mathf.Max(0.01f, side.visualScale);
            cfg.MeleeHitSound = true; cfg.MeleeHitSfx = spec.MeleeSfx;
            cfg.AttackSound = spec.AttackSound; cfg.AttackSfx = spec.AttackSfx;
            cfg.DeathSound = spec.DeathSound; cfg.DeathSfx = spec.DeathSfx;
            cfg.SpawnSound = true; cfg.SpawnSfx = spec.SpawnSfx;
            cfg.DeathClipDuration = side.deathDuration;
            cfg.DeathVfx = string.IsNullOrEmpty(spec.DeathVfx) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(spec.DeathVfx);
            cfg.IsFlyer = false;
            cfg.WardShield = false;
            EditorUtility.SetDirty(cfg);

            var ctrl = BossBuilder.ForestBossController(spec.Model, fbx, boss, cfg.VisualScale, sb);

            // Prefab: Wurzel vom Knochenfürsten (alle Komponenten/Netz wie die Stadt-Bosse); erneuter Lauf baut auf dem
            // vorhandenen Prefab auf (GUID und Datei-Ids bleiben)
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) != null;
            var root = PrefabUtility.LoadPrefabContents(exists ? spec.PrefabPath : SourceBossPrefab);
            try
            {
                root.name = spec.Model;
                var agent = root.GetComponent<NavMeshAgent>();
                float baseOffset = agent != null ? agent.baseOffset : 0f;
                if (agent != null) { agent.radius = spec.AgentRadius; agent.height = spec.CapsuleHeight; }
                var capsule = root.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    capsule.radius = spec.CapsuleRadius;
                    capsule.height = spec.CapsuleHeight;
                    capsule.center = new Vector3(0f, -baseOffset + spec.CapsuleHeight * 0.5f, 0f); // Füße auf dem Boden
                }

                var oldVisual = root.transform.Find("Visual");
                var visual = new GameObject("Visual");
                visual.tag = oldVisual != null ? oldVisual.tag : root.tag;
                visual.layer = root.layer;
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, -baseOffset, 0f);
                visual.transform.localScale = Vector3.one / Mathf.Max(0.01f, root.transform.localScale.x);
                if (oldVisual != null)
                {
                    visual.transform.SetSiblingIndex(oldVisual.GetSiblingIndex());
                    Object.DestroyImmediate(oldVisual.gameObject);
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
                inst.name = spec.Model + "_Model";
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                foreach (var a in inst.GetComponentsInChildren<Animator>(true)) if (a != anim) Object.DestroyImmediate(a);
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Avatar av) { anim.avatar = av; break; }
                anim.runtimeAnimatorController = ctrl;
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = false;

                var brain = root.GetComponent<EnemyBrain>();
                if (brain != null) { brain.Config = cfg; brain.AttackRange = spec.AttackRange; }
                var bb = root.GetComponent<BossBrain>();
                if (bb == null) bb = root.AddComponent<BossBrain>();
                bb.Abilities = spec.Abilities();
                bb.GlobalCooldown = 1.5f;
                bb.TelegraphColorOverride = spec.Telegraph; // Alpha 0 = ThemeColor

                // Aura in der Leitfarbe
                var aura = root.transform.Find("BossAura");
                if (aura != null)
                    foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = ps.main;
                        float alpha = main.startColor.color.a;
                        if (ps.name == "Glow") { alpha = spec.AuraGlowAlpha; main.startSize = spec.AuraGlowSize; }
                        else if (ps.name == "Ring") { alpha = spec.AuraRingAlpha; main.startSize = spec.AuraRingSize; }
                        main.startColor = new Color(theme.r, theme.g, theme.b, alpha);
                    }
                // Lichtfleck unter dem Boss (ohne Schatten)
                var lightT = root.transform.Find("BossLight");
                if (spec.GlowLight > 0f)
                {
                    if (lightT == null)
                    {
                        lightT = new GameObject("BossLight").transform;
                        lightT.SetParent(root.transform, false);
                    }
                    lightT.localPosition = new Vector3(0f, -baseOffset + 1.6f, 0f);
                    var bl = lightT.GetComponent<Light>();
                    if (bl == null) bl = lightT.gameObject.AddComponent<Light>();
                    bl.type = LightType.Point;
                    bl.shadows = LightShadows.None;
                    bl.color = theme;
                    bl.intensity = spec.GlowLight;
                    bl.range = spec.GlowLightRange;
                }
                else if (lightT != null) Object.DestroyImmediate(lightT.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            ForestEnemyBuilder.MarkOwnBuilder(spec.PrefabPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            cfg.Prefab = prefab;

            string iconPath = IconPath(spec);
            if (renderIcon && prefab != null) RenderPortrait(spec, null);
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (icon != null) cfg.Icon = icon;
            EditorUtility.SetDirty(cfg);
            sb.Append($"\n  {spec.PrefabPath}: {side.tris} Dreiecke, Maßstab {cfg.VisualScale:0.##}, Leitfarbe #{ColorUtility.ToHtmlStringRGB(theme)}, " +
                      $"{spec.Abilities().Count} Fähigkeiten, Tod {cfg.DeathClipDuration:0.##} s, Config {spec.ConfigPath}");
        }

        // PacingSetup.RenderIcon setzt das Prefab kurz (HideAndDontSave) in die offene Szene; das NetworkObject der Bosse
        // markiert sie dabei als geändert, obwohl nichts bleibt. War die Szene vorher sauber, bleibt sie es (das Werkzeug
        // speichert und verändert keine Szene).
        // Porträt (#584): Kopf und Schultern statt der ganzen Figur – Bildmitte knapp unter dem Kopf-Knochen, Radius als
        // Anteil der Figurenhöhe (BossSpec.Portrait*). Symbol für Boss-Leiste, Wellenvorschau und Kodex.
        public static void RenderPortrait(BossSpec spec, StringBuilder sb)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            if (prefab == null) { if (sb != null) sb.Append($"\n  Porträt {spec.Model}: Prefab fehlt"); return; }
            string iconPath = IconPath(spec);
            bool found = false;
            RenderIconClean(prefab, iconPath, 128, spec.PortraitYaw, spec.PortraitUp, 1f, (inst, all) =>
            {
                Transform head = null;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Head") { head = t; break; }
                if (head == null) return all;
                found = true;
                float h = Mathf.Max(0.1f, all.size.y);
                float r = h * spec.PortraitRadius;
                return new Bounds(head.position + Vector3.down * (h * spec.PortraitDown), Vector3.one * (r * 2f));
            });
            ImportSprite(iconPath);
            if (sb != null) sb.Append($"\n  Porträt {spec.Model}: {iconPath}{(found ? "" : " (kein Kopf-Knochen – ganze Figur)")}");
        }

        private static void RenderIconClean(GameObject prefab, string path, int size = 128, float yaw = 25f, float up = 0.35f, float frame = 0.88f,
            System.Func<GameObject, Bounds, Bounds> focus = null)
        {
            var dirtyBefore = new List<bool>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                dirtyBefore.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty);
            PacingSetup.RenderIcon(prefab, path, size, yaw, up, frame, focus);
            var clear = typeof(UnityEditor.SceneManagement.EditorSceneManager).GetMethod("ClearSceneDirtiness",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (clear == null) return;
            System.Action clean = () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                for (int i = 0; i < Mathf.Min(dirtyBefore.Count, UnityEngine.SceneManagement.SceneManager.sceneCount); i++)
                {
                    var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (!dirtyBefore[i] && s.isDirty) clear.Invoke(null, new object[] { s });
                }
            };
            clean();
            // das NetworkObject der Instanz markiert die Szene teils erst einen Editor-Takt später (gesehen 2026-11-14)
            EditorApplication.delayCall += () => clean();
        }

        public static string IconPath(BossSpec spec) =>
            IconDir + "icon_enemy_" + Path.GetFileNameWithoutExtension(spec.ConfigPath).ToLowerInvariant() + ".png";

        private static void ImportSprite(string path)
        {
            AssetDatabase.ImportAsset(path);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }

        // =====================================================================================================
        // Fähigkeits-Effekte in den Farben der Bosse (#572)
        // =====================================================================================================

        // Farbton-Vorgabe: Farbige Töne bekommen Farbton H; Sättigung = S (≥ 0) bzw. alte Sättigung × SMul; Helligkeit × V.
        // Fast weiße/graue Töne (Sättigung < 0,1) bleiben, wie sie sind.
        public struct Tone
        {
            public float H, S, SMul, V;
            public static Tone Hue(float h, float sMul = 1f, float v = 1f) => new Tone { H = h, S = -1f, SMul = sMul, V = v };
            public static Tone Fixed(float h, float s, float v = 1f) => new Tone { H = h, S = s, SMul = 1f, V = v };

            public Color Apply(Color c)
            {
                Color.RGBToHSV(c, out _, out float s, out float v);
                if (s < 0.1f) return c;
                Color o = Color.HSVToRGB(H, Mathf.Clamp01(S >= 0f ? S : s * SMul), Mathf.Clamp01(v * V));
                o.a = c.a;
                return o;
            }
        }

        private class FxVariant
        {
            public string Source, Name;
            public Tone Default;
            public Dictionary<string, Tone> PerSystem = new Dictionary<string, Tone>(); // Name des Partikelsystems → Ton
            public bool SoulBolt; // fallender Pfeil → Seelen-Geschoss (Pfeil-Modell aus, leuchtender Kern + Schweif)
        }

        // Seelenschnitter: Türkis/Geisterhaft (Leitfarbe #4FE0C8, Farbton 0,47). Oger: Orange und erdbrauner Staub.
        private const float HTeal = 0.47f, HOrange = 0.07f, HEarth = 0.085f, HYellow = 0.12f;

        private static List<FxVariant> FxVariants() => new List<FxVariant>
        {
            new FxVariant { Source = "VFX_Boss_BloodWhirl", Name = "VFX_Reaper_Whirl", Default = Tone.Hue(HTeal, 0.85f) },
            new FxVariant { Source = "VFX_Boss_SoulWave", Name = "VFX_Reaper_SoulCut", Default = Tone.Hue(HTeal, 0.9f) },
            new FxVariant { Source = "VFX_Boss_ArrowHailArea", Name = "VFX_Reaper_SoulRainArea", Default = Tone.Hue(HTeal, 0.9f),
                PerSystem = { { "Smoke", Tone.Fixed(0.5f, 0.45f, 1.1f) } } },
            new FxVariant { Source = "VFX_Boss_ArrowHailImpact", Name = "VFX_Reaper_SoulRainImpact", Default = Tone.Hue(HTeal, 0.85f) },
            new FxVariant { Source = "Boss_FallingArrow", Name = "Reaper_SoulBolt", Default = Tone.Hue(HTeal, 0.8f), SoulBolt = true },

            new FxVariant { Source = "VFX_Boss_BloodQuake", Name = "VFX_Ogre_Quake", Default = Tone.Hue(HOrange),
                PerSystem = { { "DustRing", Tone.Fixed(HEarth, 0.42f, 1.25f) }, { "Rocks", Tone.Fixed(HEarth, 0.25f, 1f) }, { "Crack", Tone.Fixed(HEarth, 0.6f, 0.9f) } } },
            new FxVariant { Source = "VFX_Boss_GraveFrost", Name = "VFX_Ogre_Roar", Default = Tone.Fixed(HOrange, 0.8f),
                PerSystem = { { "Mist", Tone.Fixed(HEarth, 0.5f, 0.8f) }, { "Ring", Tone.Fixed(HYellow, 0.7f) } } },
            new FxVariant { Source = "VFX_Boss_SoulWave", Name = "VFX_Ogre_Sweep", Default = Tone.Fixed(HOrange, 0.75f),
                PerSystem = { { "Flames", Tone.Fixed(HEarth, 0.55f, 0.9f) } } },
        };

        // Kopien der wiederverwendeten Boss-Effekte unter VFX/Prefabs/Dimension, umgefärbt (Startfarben, Verläufe, Lichter,
        // Schweife). Idempotent: Die Kopie wird jedes Mal neu aus dem Original erzeugt (GUID bleibt über SaveAsPrefabAsset).
        public static void BuildAbilityFx(StringBuilder sb)
        {
            NetworkSetup.EnsureFolder(DimFxDir.TrimEnd('/'));
            int n = 0;
            foreach (var v in FxVariants())
            {
                string src = FxDir + v.Source + ".prefab";
                string dst = DimFxDir + v.Name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null) { sb.Append($"\n  Effekt fehlt: {src}"); continue; }
                var root = PrefabUtility.LoadPrefabContents(src);
                try
                {
                    root.name = v.Name;
                    foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        Tone tone = v.PerSystem.TryGetValue(ps.name, out var t) ? t : v.Default;
                        RiftVisuals.UnifyVelocityCurves(ps);
                        var main = ps.main;
                        main.startColor = Recolor(main.startColor, tone);
                        var col = ps.colorOverLifetime;
                        if (col.enabled) col.color = Recolor(col.color, tone);
                    }
                    foreach (var l in root.GetComponentsInChildren<Light>(true)) l.color = v.Default.Apply(l.color);
                    foreach (var tr in root.GetComponentsInChildren<TrailRenderer>(true))
                    {
                        tr.colorGradient = Recolor(tr.colorGradient, v.Default);
                        tr.startColor = v.Default.Apply(tr.startColor);
                        tr.endColor = v.Default.Apply(tr.endColor);
                    }
                    if (v.SoulBolt) MakeSoulBolt(root, v.Default);
                    PrefabUtility.SaveAsPrefabAsset(root, dst);
                    n++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            sb.Append($"\n  Fähigkeits-Effekte: {n} gefärbte Kopien in {DimFxDir}");
        }

        // Fallender Pfeil → Seelen-Geschoss: Pfeil-Modell aus, der leuchtende Kern wird größer und türkis (eigenes Material)
        private static void MakeSoulBolt(GameObject root, Tone tone)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name != "TipGlow") { r.enabled = false; continue; }
                string matPath = DimFxDir + "Reaper_SoulBolt_Glow.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(r.sharedMaterial);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else mat.CopyPropertiesFromMaterial(r.sharedMaterial);
                Color baseCol = tone.Apply(r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : Color.magenta);
                mat.SetColor("_BaseColor", baseCol);
                mat.SetColor("_Color", baseCol);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.Lerp(baseCol, Color.white, 0.25f) * 1.6f); // gedeckelt (kein Tonemapping auf Mobil-Low)
                EditorUtility.SetDirty(mat);
                r.sharedMaterial = mat;
                r.transform.localScale *= 1.8f;
            }
        }

        private static Gradient Recolor(Gradient g, Tone tone)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = tone.Apply(keys[i].color);
            var o = new Gradient { mode = g.mode };
            o.SetKeys(keys, g.alphaKeys);
            return o;
        }

        private static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient g, Tone tone)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(tone.Apply(g.color));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(tone.Apply(g.colorMin), tone.Apply(g.colorMax));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Recolor(g.gradientMin, tone), Recolor(g.gradientMax, tone));
                default:
                {
                    var o = new ParticleSystem.MinMaxGradient(Recolor(g.gradient, tone));
                    o.mode = g.mode;
                    return o;
                }
            }
        }

        // =====================================================================================================
        // Kodex
        // =====================================================================================================

        // Kodex-Einträge der Dimensions-Bosse (auch von MetaSetup.BuildCodex benutzt – die Bosse stehen in keiner Welle)
        public static List<CodexDatabaseSO.Entry> CodexBossEntries()
        {
            var list = new List<CodexDatabaseSO.Entry>();
            foreach (var spec in Bosses)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(spec.ConfigPath);
                if (cfg == null) continue;
                list.Add(new CodexDatabaseSO.Entry
                {
                    Key = Codex.EnemyKey(cfg), Category = CodexDatabaseSO.Category.Boss,
                    Title = cfg.DisplayName, Description = cfg.PreviewHint, Icon = cfg.Icon, Extra = "Dimensions-Boss",
                });
            }
            return list;
        }

        public static Sprite EventIcon() => AssetDatabase.LoadAssetAtPath<Sprite>(EventIconPath);

        // Symbol des Ereignisses: die Insel des Totenreichs (Boden-Modell mit Runenring), aus dem Modell gerendert
        private static void RenderEventIcon()
        {
            var floor = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaModelDir + "Totenreich_Floor.fbx");
            if (floor == null) return;
            RenderIconClean(floor, EventIconPath, 128, 20f, 0.75f, 0.95f);
            ImportSprite(EventIconPath);
        }

        // Ohne MetaSetup-Komplettlauf: Bosse hinter den letzten Boss/Gegner einfügen bzw. ersetzen, Ereignis-Symbol setzen
        public static void SetupCodex(StringBuilder sb)
        {
            var db = AssetDatabase.LoadAssetAtPath<CodexDatabaseSO>(CodexPath);
            if (db == null) { sb.Append("\n  Kodex fehlt: " + CodexPath); return; }
            RenderEventIcon();
            int last = -1;
            for (int i = 0; i < db.Entries.Count; i++)
            {
                var e = db.Entries[i];
                if (e != null && (e.Category == CodexDatabaseSO.Category.Enemy || e.Category == CodexDatabaseSO.Category.Boss)) last = i;
            }
            int added = 0;
            foreach (var entry in CodexBossEntries())
            {
                int idx = db.Entries.FindIndex(x => x != null && x.Key == entry.Key);
                if (idx >= 0) { db.Entries[idx] = entry; continue; }
                db.Entries.Insert(++last, entry);
                added++;
            }
            string evKey = Codex.EventKey(WaveEvent.Dimension);
            var ev = db.Entries.Find(x => x != null && x.Key == evKey);
            var icon = EventIcon();
            if (ev != null && icon != null) ev.Icon = icon;
            EditorUtility.SetDirty(db);
            sb.Append($"\n  Kodex: {added} Boss-Einträge neu, Ereignis-Symbol {(ev != null && ev.Icon != null ? "gesetzt" : "fehlt")}");
        }

        // =====================================================================================================
        // Netz
        // =====================================================================================================

        public static void SetupNet(StringBuilder sb)
        {
            NetSetupEnemies.SetupEnemyPrefabs();
            NetworkSetup.BuildPrefabListAndManager();
            var list = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkPrefabsList>("Assets/_Game/Net/NetworkPrefabs.asset");
            foreach (var spec in Bosses)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
                bool inList = false;
                if (list != null && prefab != null)
                    foreach (var p in list.PrefabList) if (p != null && p.Prefab == prefab) inList = true;
                sb.Append($"\n  Netz: {spec.Model} {(inList ? "in der Prefab-Liste" : "FEHLT in der Prefab-Liste")}");
            }
        }

        // =====================================================================================================
        // Maps
        // =====================================================================================================

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        // Totenreich: kaltes Mondlicht aus Nordwest, dunkler Himmel, grünlicher Nebel
        public static DimensionAtmosphere CityAtmosphere() => new DimensionAtmosphere
        {
            // Empfehlung der Arena (Sonne #9EC7FF 0,55, Umgebung #2A2440, Nebel 0,022) war im Spiel zu dunkel: Boss und
            // Figur gingen im Boden unter, der Hintergrund im Nebel → heller und weniger Nebel (Play-Mode 2026-11-14)
            SunEuler = new Vector3(50f, 135f, 0f),
            SunColor = Hex("#B4D2FF"),
            SunIntensity = 0.95f,
            ShadowStrength = 0.7f,
            AmbientSky = Hex("#4A4470"),
            AmbientEquator = Hex("#3A4458"),
            AmbientGround = Hex("#1C2028"),
            FogColor = Hex("#14262E"),
            FogDensity = 0.014f,
            SkyTint = Hex("#2A5560"),
            SkyGround = Hex("#0E1A1F"),
            SkyExposure = 0.55f,
            SkyAtmosphere = 1f,
            BlendTime = 0.5f,
        };

        // Kriegslager in der Dämmerung (#573, Tablet: „sehr einfarbig orange“): warmweiße, tief stehende Sonne statt
        // sattem Orange – das Orange kommt von Lagerfeuer und Fackeln (Punktlichter) –, Umgebungslicht blauviolett,
        // Nebel dunkel und kaum rot, Himmel dämmerblau mit warmem Horizont. So trennen sich Erde, Holz, Banner und Grün.
        public static DimensionAtmosphere ForestAtmosphere() => new DimensionAtmosphere
        {
            SunEuler = new Vector3(32f, 67.5f, 0f),
            SunColor = Hex("#FFE6C8"),
            SunIntensity = 1.05f,
            ShadowStrength = 0.8f,
            AmbientSky = Hex("#5A5E96"),
            AmbientEquator = Hex("#4E4868"),
            AmbientGround = Hex("#24222E"),
            FogColor = Hex("#1E1A26"),
            FogDensity = 0.014f,
            SkyTint = Hex("#3C3460"),
            SkyGround = Hex("#1A1620"),
            SkyExposure = 0.5f,
            SkyAtmosphere = 1.1f,
            BlendTime = 0.5f,
        };

        private const string ManagersPrefab = "Assets/_Game/Prefabs/Core/Managers.prefab";

        // Boss-Leben und Schadens-Anteil im Managers-Prefab (WaveManager) auf die Regel-Werte setzen – gemessen mit
        // DimensionFightBench (#556/#579), Quelle der Wahrheit: DimensionEventRules.TargetBossHp / DamageRampShare
        public static void SetupTuning(StringBuilder sb)
        {
            var root = PrefabUtility.LoadPrefabContents(ManagersPrefab);
            try
            {
                var wm = root.GetComponentInChildren<WaveManager>(true);
                if (wm == null) { sb.Append("\n  WaveManager fehlt im Managers-Prefab"); return; }
                wm.DimensionBossTargetHp = DimensionEventRules.TargetBossHp;
                wm.DimensionBossDamageRampShare = DimensionEventRules.DamageRampShare;
                PrefabUtility.SaveAsPrefabAsset(root, ManagersPrefab);
                sb.Append($"\n  Managers-Prefab: Boss-Leben {wm.DimensionBossTargetHp:0}, Schadens-Anteil {wm.DimensionBossDamageRampShare:0.##}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void SetupMaps(StringBuilder sb)
        {
            SetupTuning(sb);
            SetMap(MapCity, ReaperConfig, ArenaCity, CityAtmosphere(), sb);
            SetMap(MapForest, OgreConfig, ArenaForest, ForestAtmosphere(), sb);
        }

        private static void SetMap(string mapPath, string bossPath, string arenaPath, DimensionAtmosphere atmo, StringBuilder sb)
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(mapPath);
            if (map == null) { sb.Append("\n  Map fehlt: " + mapPath); return; }
            var boss = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(bossPath);
            var arena = AssetDatabase.LoadAssetAtPath<GameObject>(arenaPath);
            if (boss != null) map.DimensionBoss = boss;
            if (arena != null) map.DimensionArenaPrefab = arena;
            map.DimensionAtmosphere = atmo;
            EditorUtility.SetDirty(map);
            sb.Append($"\n  {mapPath}: Boss {(boss != null ? boss.name : "FEHLT")}, Arena {(arena != null ? arena.name : "FEHLT")}, Lichtstimmung gesetzt");
        }
    }
}
