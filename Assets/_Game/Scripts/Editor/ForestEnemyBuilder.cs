using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies.EditorTools
{
    // Walddorf, Paket C-Prefabs: Gegner-Prefabs des Dorf-Themes (Goblins, Orks, Wolf, Waldspinne, Ork-Bosse, Ork-Rammbock).
    // Menü: BuddyTD → Walddorf → Dorf-Gegner bauen. Idempotent, beliebig oft ausführbar.
    //
    // Etappe 1 (Platzhalter): jedes Prefab ist eine vollständige Kopie (kein Variant) eines Untoten-Prefabs – Wurzel mit
    //   Layer/Tag/NavMeshAgent/Collider/EnemyBrain/HP-Leiste/NetworkObject/NetworkTransform/EnemyNet bleibt 1:1 (Balance-
    //   Gleichheit: Trefferzone und Agent unverändert), nur das Kind „Visual“ wird umskaliert und eingefärbt
    //   (eigene Material-Kopien unter …/Forest/Materials, Augen Gegner-Gelb statt Untoten-Grün). Bosse: Kopie inkl.
    //   BossBrain.Abilities → Fähigkeiten identisch, Boss-Glühen/Aura (ThemeColor) bleibt.
    //   Wolf = Wurzel des Schwarms + Visual der Grabspinne (verkleinert).
    // Etappe 2 (echte Modelle): liegt Assets/_Game/Models/Enemies/<Ordner>/<Name>.fbx vor, ersetzt das Modell das Visual
    //   (Maßstab 1:1 aus Blender, Ursprung = Boden; Controller <Name>.controller im Modellordner oder automatisch aus den
    //   Clips: *Run/*Walk = Lauf, *Attack/*Bite/*Throw/*Shoot = Attack, *Idle, weitere Boss-Trigger per Clipname).
    //   Ohne Modell bleibt der Platzhalter (Rückfall). Der Ork-Rammbock bleibt auch dann ein Platzhalter – das echte
    //   Modell gehört in eine RamBuilder-Variante (gleiche Teilenamen für RamVisual).
    //   Der Animator sitzt auf der Modell-Instanz (= FBX-Wurzel, Kind von „Visual“), damit Generic-Clips (Pfade relativ
    //   zur FBX-Wurzel) greifen; EnemyBrain findet ihn per GetComponentInChildren, der Tod-Clip löst genau diese Instanz.
    // Etappe 2, A-Humanoide (2026-10-16): Liegt neben der FBX eine Sidecar-Datei <Name>.anim.json (art-src/enemies/
    //   build_humanoids.py), richtet ImportSidecarModels Import (Generic, Takes → Clips mit Schleifen, Materialien je Slot aus
    //   der Palette), den Controller <Name>.controller im Modellordner (Idle ⇄ Run mit Tempo = Speed / (Clip-Tempo ×
    //   VisualScale) → rutschfrei, Attack + Die über AnyState) und DeathClipDuration der Dorf-Config ein.
    //   Goblin-Bogenschütze: eigenes Projektil Prefabs/Enemies/Forest/GoblinArrow.prefab (Kopie von EnemyArrow mit
    //   gleichen EnemyProjectile-Werten, Modell Gob_Arrow.fbx).
    // Netz: NGO vergibt beim Speichern einen eigenen GlobalObjectIdHash (aus der neuen GUID). Die Netz-Prefab-Liste baut
    //   nur der Integrator (BuddyTD → Netzwerk → Prefab-Liste aktualisieren).
    // EnemyBrain.Config zeigt auf die Dorf-Config (Configs/Forest/<Name>.asset), sobald sie existiert (Clients lesen
    //   VisualScale/Projektil aus dem Prefab), sonst auf die Untoten-Config (gleiche Werte).
    public static class ForestEnemyBuilder
    {
        public const string EnemyDir = "Assets/_Game/Prefabs/Enemies/Forest/";
        public const string BossDir = "Assets/_Game/Prefabs/Bosses/Forest/";
        public const string ModelRoot = "Assets/_Game/Models/Enemies/";
        public const string ForestConfigDir = "Assets/ScriptableObjects/Configs/Forest";
        private const string CanonConfigDir = "Assets/ScriptableObjects/Configs/";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string Prefabs = "Assets/_Game/Prefabs/";

        private static readonly Color GoblinSkin = Hex("7fa04a");
        private static readonly Color OrkSkin = Hex("5f7a3e");
        private static readonly Color Leather = Hex("6b4a2f");
        private static readonly Color Bark = Hex("5a4030");
        private static readonly Color EnemyYellow = Hex("ffd24a");

        public class Spec
        {
            public string Name, Source, VisualFrom, SourceConfig, VisualConfig, ModelFolder;
            public string Projectile;           // eigenes Projektil-Prefab (Prefabs/Enemies/Forest/<Projectile>.prefab)
            public bool Boss;
            public float Scale = 1f;            // Faktor auf das Visual (Platzhalter), relativ zur Vorlage im Spiel
            public Color Skin, Accent;
            public float AccentAmount;
            public Color? Eye;                  // null = Augen/Glühen unverändert (Bosse: ThemeColor)
            public string[] BodyKeys = new string[0];
            public string Dir => Boss ? BossDir : EnemyDir;
            public string PrefabPath => Dir + Name + ".prefab";
            // gleicher Dateiname wie PacingSetup (icon_enemy_<config>.png) → beide Werkzeuge schreiben dieselbe Datei
            public string IconPath => IconDir + "icon_enemy_" + Name.ToLowerInvariant() + ".png";
        }

        public static readonly Spec[] Specs =
        {
            new Spec { Name = "Goblin_Pluenderer", Source = "Runner", SourceConfig = "Runner", ModelFolder = "Goblin",
                       Scale = 0.85f, Skin = GoblinSkin, Accent = Leather, AccentAmount = 0.5f, Eye = EnemyYellow,
                       BodyKeys = new[] { "Material_1", "Bone" } },
            new Spec { Name = "Wolf", Source = "Swarm", SourceConfig = "Swarm", VisualFrom = "GraveSpider", VisualConfig = "GraveSpider",
                       ModelFolder = "Wolf", Scale = 0.8f, Skin = Hex("9a8f80"), Accent = Hex("6f6a64"), AccentAmount = 0.8f,
                       Eye = EnemyYellow, BodyKeys = new[] { "Body", "Legs" } },
            new Spec { Name = "Ork_Krieger", Source = "Knight", SourceConfig = "Knight", ModelFolder = "Ork",
                       Scale = 1.15f, Skin = OrkSkin, Accent = Bark, AccentAmount = 0.45f, Eye = EnemyYellow,
                       BodyKeys = new[] { "Material_1", "Bone" } },
            new Spec { Name = "Goblin_Bogenschuetze", Source = "SkeletonArcher", SourceConfig = "SkeletonArcher",
                       ModelFolder = "Goblin", Projectile = "GoblinArrow", Scale = 0.85f, Skin = GoblinSkin, Accent = Leather, AccentAmount = 0.4f, Eye = EnemyYellow,
                       BodyKeys = new[] { "Bone" } },
            new Spec { Name = "Waldspinne", Source = "GraveSpider", SourceConfig = "GraveSpider", ModelFolder = "Waldspinne",
                       Scale = 1f, Skin = Hex("3f4f26"), Accent = Hex("4b3424"), AccentAmount = 0.75f, Eye = EnemyYellow,
                       BodyKeys = new[] { "Body" } },
            new Spec { Name = "Ork_Kriegsherr", Source = "Bosses/BossBoneLord", SourceConfig = "Bosses/BossBoneLord", Boss = true,
                       ModelFolder = "Bosses", Scale = 1.05f, Skin = OrkSkin, Accent = Bark, AccentAmount = 0.4f,
                       BodyKeys = new[] { "Bone" } },
            new Spec { Name = "Goblin_Schamane", Source = "Bosses/BossNecromancer", SourceConfig = "Bosses/BossNecromancer", Boss = true,
                       ModelFolder = "Bosses", Scale = 0.95f, Skin = GoblinSkin, Accent = Leather, AccentAmount = 0.35f,
                       BodyKeys = new[] { "Bone" } },
            new Spec { Name = "Ork_Faehrtenmeister", Source = "Bosses/BossDeathHunter", SourceConfig = "Bosses/BossDeathHunter", Boss = true,
                       ModelFolder = "Bosses", Scale = 1f, Skin = OrkSkin, Accent = Bark, AccentAmount = 0.35f,
                       BodyKeys = new[] { "Bone" } },
            new Spec { Name = "Ork_Rammbock", Source = "Ram", SourceConfig = "Ram", ModelFolder = null,
                       Scale = 1f, Skin = Bark, Accent = Hex("4a5030"), AccentAmount = 0.6f },
        };

        [MenuItem("BuddyTD/Walddorf/Dorf-Gegner bauen (Prefabs, Icons)")]
        private static void BuildMenu() => Build();

        // Gibt das Protokoll zurück (steht auch in der Konsole)
        public static string Build(bool renderIcons = true)
        {
            var sb = new StringBuilder("ForestEnemyBuilder:");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                sb.Append(" nur im Edit-Modus.");
                Debug.LogWarning(sb.ToString());
                return sb.ToString();
            }
            NetworkSetup.EnsureFolder(EnemyDir + "Materials");
            NetworkSetup.EnsureFolder(BossDir + "Materials");
            ImportSidecarModels(sb);
            BuildProjectiles(sb);
            foreach (var s in Specs)
            {
                if (HasOwnBuilder(s))
                {
                    sb.Append($"\n  {s.Name}: übersprungen (eigenes Modell aus RamBuilder/BossBuilder)");
                    continue;
                }
                try
                {
                    var prefab = BuildOne(s, sb);
                    if (prefab != null && renderIcons) RenderIcon(s, prefab, sb);
                }
                catch (System.Exception e)
                {
                    sb.Append($"\n  FEHLER {s.Name}: {e.Message}");
                    Debug.LogException(e);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        // Prefabs mit eigenem Modell-Builder (Ork-Rammbock: RamBuilder.BuildOrk, Dorf-Bosse: BossBuilder) tragen dieses
        // Asset-Label; der Platzhalter-Lauf lässt sie dann in Ruhe (#405).
        public const string OwnBuilderLabel = "EB_OwnBuilder";

        public static void MarkOwnBuilder(string prefabPath)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(prefabPath);
            if (obj == null) return;
            var labels = new List<string>(AssetDatabase.GetLabels(obj));
            if (!labels.Contains(OwnBuilderLabel)) { labels.Add(OwnBuilderLabel); AssetDatabase.SetLabels(obj, labels.ToArray()); }
        }

        private static bool HasOwnBuilder(Spec s)
        {
            string dir = s.Boss ? BossDir : EnemyDir;
            var obj = AssetDatabase.LoadMainAssetAtPath(dir + s.Name + ".prefab");
            return obj != null && System.Array.IndexOf(AssetDatabase.GetLabels(obj), OwnBuilderLabel) >= 0;
        }

        // ---------------- Prefab ----------------

        private static GameObject BuildOne(Spec s, StringBuilder sb)
        {
            string src = Prefabs + s.Source + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null) { sb.Append($"\n  {s.Name}: Vorlage {src} fehlt"); return null; }
            var root = PrefabUtility.LoadPrefabContents(src);
            try
            {
                root.name = s.Name;
                var visual = root.transform.Find("Visual");
                var agent = root.GetComponent<NavMeshAgent>();
                float ground = agent != null ? -agent.baseOffset : 0f; // Boden im Wurzel-Raum
                string model = ModelPath(s);
                string mode;
                if (model != null)
                {
                    ReplaceWithModel(root, visual, model, s, ground, sb);
                    mode = "Modell " + model;
                }
                else
                {
                    if (!string.IsNullOrEmpty(s.VisualFrom)) visual = TransplantVisual(root, visual, s, ground);
                    else if (visual != null && !Mathf.Approximately(s.Scale, 1f)) ScaleAboutGround(visual, s.Scale, ground);
                    int n = Tint(root, s);
                    mode = $"Platzhalter ({n} Materialien getönt, Visual ×{s.Scale:0.##})";
                }

                var brain = root.GetComponent<EnemyBrain>();
                var cfg = ForestConfig(s.Name);
                if (brain != null && cfg != null) brain.Config = cfg;
                if (cfg != null && !string.IsNullOrEmpty(s.Projectile))
                {
                    var proj = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + s.Projectile + ".prefab");
                    if (proj != null && cfg.ProjectilePrefab != proj) { cfg.ProjectilePrefab = proj; EditorUtility.SetDirty(cfg); }
                }

                if (root.GetComponent<NetworkObject>() == null || root.GetComponent<EnemyNet>() == null)
                    sb.Append($"\n  Warnung {s.Name}: NetworkObject/EnemyNet fehlt in der Vorlage");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, s.PrefabPath);
                sb.Append($"\n  {s.PrefabPath}: {mode}, Config {(brain != null && brain.Config != null ? AssetDatabase.GetAssetPath(brain.Config) : "-")}");
                return prefab;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ScaleAboutGround(Transform visual, float k, float ground)
        {
            var g = new Vector3(0f, ground, 0f);
            visual.localPosition = g + (visual.localPosition - g) * k;
            visual.localScale *= k;
        }

        // Visual eines anderen Prefabs übernehmen (Wolf: Schwarm-Wurzel + Spinnen-Visual). Faktor so, dass das Modell im Spiel
        // s.Scale × so groß ist wie bei der Vorlage (VisualScale der beiden Untoten-Configs ausgeglichen).
        private static Transform TransplantVisual(GameObject root, Transform oldVisual, Spec s, float ground)
        {
            var donor = PrefabUtility.LoadPrefabContents(Prefabs + s.VisualFrom + ".prefab");
            try
            {
                var dv = donor.transform.Find("Visual");
                var dAgent = donor.GetComponent<NavMeshAgent>();
                float dGround = dAgent != null ? -dAgent.baseOffset : 0f;
                float k = s.Scale * ConfigScale(s.VisualConfig) / Mathf.Max(0.01f, ConfigScale(s.SourceConfig));
                k *= donor.transform.localScale.x / Mathf.Max(0.01f, root.transform.localScale.x);
                var copy = Object.Instantiate(dv.gameObject, root.transform, false);
                copy.name = "Visual";
                copy.tag = oldVisual != null ? oldVisual.tag : root.tag;
                var g = new Vector3(0f, dGround, 0f);
                copy.transform.localPosition = new Vector3(0f, ground, 0f) + (dv.localPosition - g) * k;
                copy.transform.localRotation = dv.localRotation;
                copy.transform.localScale = dv.localScale * k;
                if (oldVisual != null)
                {
                    copy.transform.SetSiblingIndex(oldVisual.GetSiblingIndex());
                    Object.DestroyImmediate(oldVisual.gameObject);
                }
                return copy.transform;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(donor);
            }
        }

        private static float ConfigScale(string cfgName)
        {
            var c = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(CanonConfigDir + cfgName + ".asset");
            return c != null && c.VisualScale > 0f ? c.VisualScale : 1f;
        }

        public static EnemyConfigSO ForestConfig(string name)
        {
            if (!AssetDatabase.IsValidFolder(ForestConfigDir)) return null;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:EnemyConfigSO", new[] { ForestConfigDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(p);
            }
            return null;
        }

        // ---------------- Etappe 2: echtes Modell ----------------

        public static string ModelPath(Spec s)
        {
            if (string.IsNullOrEmpty(s.ModelFolder)) return null;
            string p = ModelRoot + s.ModelFolder + "/" + s.Name + ".fbx";
            return AssetDatabase.LoadAssetAtPath<GameObject>(p) != null ? p : null;
        }

        private static void ReplaceWithModel(GameObject root, Transform oldVisual, string modelPath, Spec s, float ground, StringBuilder sb)
        {
            // Boss-Trigger (Slam/Whirl/Cast/Shoot …) aus dem alten Controller übernehmen
            var triggers = new List<string> { "Attack" };
            var oldAnim = oldVisual != null ? oldVisual.GetComponent<Animator>() : null;
            var oldCtrl = oldAnim != null ? oldAnim.runtimeAnimatorController as AnimatorController : null;
            if (oldCtrl != null)
                foreach (var p in oldCtrl.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger && !triggers.Contains(p.name)) triggers.Add(p.name);

            var visual = new GameObject("Visual");
            visual.tag = oldVisual != null ? oldVisual.tag : root.tag;
            visual.layer = root.layer;
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, ground, 0f);
            visual.transform.localScale = Vector3.one / Mathf.Max(0.01f, root.transform.localScale.x); // Modell in echten Metern
            if (oldVisual != null)
            {
                visual.transform.SetSiblingIndex(oldVisual.GetSiblingIndex());
                Object.DestroyImmediate(oldVisual.gameObject);
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
            inst.name = s.Name + "_Model";
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;

            // Animator auf der FBX-Wurzel (Avatar-/Clip-Pfade relativ dazu), weitere Animatoren entfernen
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) if (a != anim) Object.DestroyImmediate(a);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                if (o is Avatar av) { anim.avatar = av; break; }
            anim.runtimeAnimatorController = ModelController(s, modelPath, triggers, sb);
        }

        private static AnimatorController ModelController(Spec s, string modelPath, List<string> triggers, StringBuilder sb)
        {
            string dir = Path.GetDirectoryName(modelPath).Replace('\\', '/') + "/";
            var own = AssetDatabase.LoadAssetAtPath<AnimatorController>(dir + s.Name + ".controller");
            if (own != null) return own;

            // Clips aus allen FBX im Modellordner, deren Name zum Gegner passt (z. B. Goblin_Run, Goblin_Attack)
            var clips = new List<AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { dir.TrimEnd('/') }))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview") && !clips.Contains(c)) clips.Add(c);
            AnimationClip Find(params string[] keys)
            {
                foreach (var k in keys)
                    foreach (var c in clips)
                        if (c.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) return c;
                return null;
            }
            var move = Find("Run", "Walk", "Gallop");
            var idle = Find("Idle", "Hover") ?? move;
            var attack = Find("Attack", "Bite", "Throw", "Shoot", "Swing");

            string ctrlDir = s.Dir + "Controllers";
            NetworkSetup.EnsureFolder(ctrlDir);
            string path = ctrlDir + "/" + s.Name + ".controller";
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var st in sm.states) sm.RemoveState(st.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            ac.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            foreach (var t in triggers) ac.AddParameter(t, AnimatorControllerParameterType.Trigger);

            var run = sm.AddState("Run", new Vector3(300, 0, 0));
            run.motion = move;
            run.speedParameterActive = true;
            run.speedParameter = "Speed";
            run.speed = 0.3f; // Clip-Tempo je m/s – in Etappe 2 am Modell abstimmen (Fußrutschen)
            var idleS = sm.AddState("Idle", new Vector3(300, 120, 0));
            idleS.motion = idle;
            sm.defaultState = idleS;
            var toIdle = run.AddTransition(idleS);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            toIdle.hasExitTime = false; toIdle.duration = 0.15f;
            var toRun = idleS.AddTransition(run);
            toRun.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            toRun.hasExitTime = false; toRun.duration = 0.15f;
            foreach (var trig in triggers)
            {
                var clip = trig == "Attack" ? attack : (Find(trig) ?? attack);
                var st = sm.AddState(trig, new Vector3(600, 60 * sm.states.Length, 0));
                st.motion = clip;
                var any = sm.AddAnyStateTransition(st);
                any.AddCondition(AnimatorConditionMode.If, 0f, trig);
                any.duration = 0.08f;
                any.canTransitionToSelf = true;
                var a = st.AddTransition(run);
                a.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
                a.hasExitTime = true; a.exitTime = 0.9f; a.duration = 0.2f;
                var b = st.AddTransition(idleS);
                b.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
                b.hasExitTime = true; b.exitTime = 0.9f; b.duration = 0.2f;
                if (clip == null) sb.Append($"\n  Warnung {s.Name}: kein Clip für Trigger {trig}");
            }
            if (move == null) sb.Append($"\n  Warnung {s.Name}: kein Lauf-Clip im Modellordner");
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ---------------- Etappe 2: Sidecar-Modelle (A-Humanoide: Goblins, Ork) ----------------

        [System.Serializable] public class SideClip { public string name, role; public bool loop; public int frames; public float hit = -1f; }
        [System.Serializable] public class SideMat { public string name, hex; public float emission; }
        [System.Serializable]
        public class Sidecar
        {
            public string model, rig;
            public int fps = 30, tris;
            public float visualScale = 1f, moveSpeedModel, deathDuration;
            public SideClip[] clips = new SideClip[0];
            public SideMat[] materials = new SideMat[0];
            public SideClip Role(string role) { foreach (var c in clips) if (c.role == role) return c; return null; }
        }

        public static Sidecar ReadSidecar(string fbxPath)
        {
            string p = Path.ChangeExtension(fbxPath, null) + ".anim.json";
            if (!File.Exists(p)) return null;
            try { return JsonUtility.FromJson<Sidecar>(File.ReadAllText(p)); }
            catch (System.Exception e) { Debug.LogWarning("ForestEnemyBuilder: Sidecar " + p + ": " + e.Message); return null; }
        }

        private static void ImportSidecarModels(StringBuilder sb)
        {
            foreach (var s in Specs)
            {
                if (string.IsNullOrEmpty(s.ModelFolder)) continue;
                string fbx = ModelRoot + s.ModelFolder + "/" + s.Name + ".fbx";
                var side = ReadSidecar(fbx);
                if (side == null || AssetImporter.GetAtPath(fbx) == null) continue;
                ConfigureModelImport(fbx, side, true);
                var cfg = ForestConfig(s.Name);
                float vs = cfg != null && cfg.VisualScale > 0f ? cfg.VisualScale : Mathf.Max(0.01f, side.visualScale);
                var ac = SidecarController(s, fbx, side, vs);
                if (cfg != null && side.deathDuration > 0f && !Mathf.Approximately(cfg.DeathClipDuration, side.deathDuration))
                {
                    cfg.DeathClipDuration = side.deathDuration;
                    EditorUtility.SetDirty(cfg);
                }
                sb.Append($"\n  Import {fbx}: {side.tris} Tris, {side.clips.Length} Clips, Controller {AssetDatabase.GetAssetPath(ac)}, Lauf-Tempo 1/{side.moveSpeedModel * vs:0.###} m/s, Tod {side.deathDuration:0.##} s");
            }
        }

        // Generic-Rig (Avatar aus dem Modell), keine Kollider, Normals aus Blender (flache Facetten), Takes → Clips (Name nach
        // dem „|“, Schleifen laut Sidecar), Materialien je Slot (Models/Enemies/<Ordner>/Materials/<Slot>.mat, URP/Lit)
        public static void ConfigureModelImport(string fbx, Sidecar side, bool rigged)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            imp.useFileScale = true;
            imp.globalScale = 1f;
            imp.addCollider = false;
            imp.importNormals = ModelImporterNormals.Import;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.importVisibility = false;
            imp.isReadable = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            if (rigged)
            {
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.optimizeGameObjects = false;
                imp.importAnimation = true;
                imp.resampleCurves = true;
                imp.animationCompression = ModelImporterAnimationCompression.Optimal;
            }
            else
            {
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
            }
            imp.SaveAndReimport();
            if (rigged)
            {
                var defs = imp.defaultClipAnimations;
                var list = new List<ModelImporterClipAnimation>();
                foreach (var d in defs)
                {
                    string n = d.takeName ?? d.name;
                    int bar = n.LastIndexOf('|');
                    if (bar >= 0) n = n.Substring(bar + 1);
                    d.name = n;
                    bool loop = false;
                    foreach (var c in side.clips) if (c.name == n) loop = c.loop;
                    d.loopTime = loop;
                    d.loopPose = false; // erster = letzter Frame ist in Blender schon identisch
                    d.lockRootRotation = false;
                    d.lockRootHeightY = false;
                    d.lockRootPositionXZ = false;
                    list.Add(d);
                }
                imp.clipAnimations = list.ToArray();
            }
            string matDir = Path.GetDirectoryName(fbx).Replace('\\', '/') + "/Materials";
            NetworkSetup.EnsureFolder(matDir);
            foreach (var m in side.materials)
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), EnemyMaterial(matDir, m));
            imp.SaveAndReimport();
        }

        private static Material EnemyMaterial(string dir, SideMat sm)
        {
            string p = dir + "/" + sm.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = sm.name };
                AssetDatabase.CreateAsset(m, p);
            }
            // wie Stadt/Vegetation/Dorf: URP/Lit, opak, matt, GPU-Instancing
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", 2f);
            m.enableInstancing = true;
            if (!ColorUtility.TryParseHtmlString(sm.hex, out var c)) c = Color.magenta;
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            if (sm.emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * sm.emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static AnimationClip ClipOf(string fbx, string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (o is AnimationClip c && c.name == name) return c;
            return null;
        }

        // Controller <Name>.controller im Modellordner (ModelController nimmt ihn vor dem Automatik-Controller):
        // Idle ⇄ Run (Bool Moving), Run-Tempo = Parameter Speed (m/s im Spiel) / (Clip-Tempo Modell × VisualScale) → die
        // Füße stehen in der Standphase still; Attack (Trigger, AnyState, zurück nach 90 %), Die (Trigger, AnyState, bleibt).
        private static AnimatorController SidecarController(Spec s, string fbx, Sidecar side, float visualScale)
        {
            string path = Path.ChangeExtension(fbx, null) + ".controller";
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var st in sm.states) sm.RemoveState(st.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            ac.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            AnimationClip C(string role) { var c = side.Role(role); return c != null ? ClipOf(fbx, c.name) : null; }
            var idle = sm.AddState("Idle", new Vector3(300, 120, 0)); idle.motion = C("idle");
            var run = sm.AddState("Run", new Vector3(300, 0, 0)); run.motion = C("move");
            run.speedParameterActive = true;
            run.speedParameter = "Speed";
            run.speed = 1f / Mathf.Max(0.05f, side.moveSpeedModel * visualScale);
            var atk = sm.AddState("Attack", new Vector3(600, 0, 0)); atk.motion = C("attack");
            var die = sm.AddState("Die", new Vector3(600, 160, 0)); die.motion = C("die");
            sm.defaultState = idle;
            var toRun = idle.AddTransition(run); toRun.AddCondition(AnimatorConditionMode.If, 0f, "Moving"); toRun.hasExitTime = false; toRun.duration = 0.15f;
            var toIdle = run.AddTransition(idle); toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving"); toIdle.hasExitTime = false; toIdle.duration = 0.15f;
            var anyDie = sm.AddAnyStateTransition(die);
            anyDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            anyDie.duration = 0.08f; anyDie.canTransitionToSelf = false;
            var anyAtk = sm.AddAnyStateTransition(atk);
            anyAtk.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            anyAtk.duration = 0.06f; anyAtk.canTransitionToSelf = true;
            var a1 = atk.AddTransition(run); a1.AddCondition(AnimatorConditionMode.If, 0f, "Moving"); a1.hasExitTime = true; a1.exitTime = 0.9f; a1.duration = 0.2f;
            var a2 = atk.AddTransition(idle); a2.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving"); a2.hasExitTime = true; a2.exitTime = 0.9f; a2.duration = 0.2f;
            foreach (var st in sm.states)
                if (st.state.motion == null) Debug.LogWarning($"ForestEnemyBuilder: {s.Name}: kein Clip für Zustand {st.state.name}");
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // Eigene Projektile: Kopie von EnemyArrow (gleiche EnemyProjectile-Werte, Treffer-VFX, Spur), Modell aus der FBX
        // (Spitze = +Z, Ursprung Pfeilmitte → um die halbe Länge nach hinten, die Spitze liegt am Wurzelpunkt wie beim Original)
        public const string GoblinArrowModel = ModelRoot + "Goblin/Gob_Arrow.fbx";

        private static void BuildProjectiles(StringBuilder sb)
        {
            foreach (var s in Specs)
            {
                if (string.IsNullOrEmpty(s.Projectile)) continue;
                string target = EnemyDir + s.Projectile + ".prefab";
                var side = ReadSidecar(GoblinArrowModel);
                if (side == null || AssetImporter.GetAtPath(GoblinArrowModel) == null) { sb.Append($"\n  {s.Projectile}: Modell {GoblinArrowModel} fehlt"); continue; }
                ConfigureModelImport(GoblinArrowModel, side, false);
                var root = PrefabUtility.LoadPrefabContents(Prefabs + "EnemyArrow.prefab");
                try
                {
                    root.name = s.Projectile;
                    var old = root.transform.Find("Model");
                    int idx = old != null ? old.GetSiblingIndex() : root.transform.childCount;
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(GoblinArrowModel);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    inst.name = "Model";
                    inst.transform.SetSiblingIndex(idx);
                    inst.transform.localPosition = new Vector3(0f, 0f, -0.31f);
                    inst.transform.localRotation = Quaternion.identity;
                    inst.transform.localScale = Vector3.one;
                    foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    PrefabUtility.SaveAsPrefabAsset(root, target);
                    sb.Append($"\n  {target}: Projektil (Kopie EnemyArrow, Modell {GoblinArrowModel}, {side.tris} Tris)");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        // Einmalig (Etappe 2): Goblin-Speerwerfer → Goblin-Bogenschütze. Assets werden verschoben (MoveAsset), GUIDs und damit
        // Theme-/Netz-Verweise (GlobalObjectIdHash) bleiben erhalten.
        [MenuItem("BuddyTD/Walddorf/Goblin-Speerwerfer in Goblin-Bogenschütze umbenennen")]
        public static string RenameSpearToArcher()
        {
            var sb = new StringBuilder("Umbenennung Speerwerfer → Bogenschütze:");
            void Move(string from, string to)
            {
                if (AssetDatabase.LoadMainAssetAtPath(from) == null) { sb.Append($"\n  {from}: fehlt (schon umbenannt?)"); return; }
                string err = AssetDatabase.MoveAsset(from, to);
                sb.Append(string.IsNullOrEmpty(err) ? $"\n  {from} → {to} (GUID {AssetDatabase.AssetPathToGUID(to)})" : $"\n  FEHLER {from}: {err}");
            }
            Move(ForestConfigDir + "/Goblin_Speerwerfer.asset", ForestConfigDir + "/Goblin_Bogenschuetze.asset");
            Move(EnemyDir + "Goblin_Speerwerfer.prefab", EnemyDir + "Goblin_Bogenschuetze.prefab");
            Move(IconDir + "icon_enemy_goblin_speerwerfer.png", IconDir + "icon_enemy_goblin_bogenschuetze.png");
            foreach (var guid in AssetDatabase.FindAssets("Goblin_Speerwerfer_ t:Material", new[] { EnemyDir + "Materials" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileName(p);
                if (n.StartsWith("Goblin_Speerwerfer_")) Move(p, Path.GetDirectoryName(p).Replace('\\', '/') + "/" + n.Replace("Goblin_Speerwerfer_", "Goblin_Bogenschuetze_"));
            }
            var cfg = ForestConfig("Goblin_Bogenschuetze");
            if (cfg != null)
            {
                cfg.DisplayName = "Goblin-Bogenschütze";
                EditorUtility.SetDirty(cfg);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        // ---------------- Tönung ----------------

        private enum MatKind { Skip, Body, Eye, Other }

        private static int Tint(GameObject root, Spec s)
        {
            var cache = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (!cache.TryGetValue(m, out var t)) { t = TintedMaterial(m, s); cache[m] = t; }
                    if (t != null && t != m) { mats[i] = t; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            int n = 0;
            foreach (var kv in cache) if (kv.Value != null && kv.Value != kv.Key) n++;
            return n;
        }

        private static MatKind Classify(Material m, Spec s)
        {
            if (m.name.StartsWith("VFX_") || !m.HasProperty("_BaseColor")) return MatKind.Skip;
            string sh = m.shader != null ? m.shader.name : "";
            if (!sh.Contains("Universal Render Pipeline/Lit") && !sh.Contains("Universal Render Pipeline/Unlit")) return MatKind.Skip;
            bool eyeName = m.name.Contains("Eye") || m.name.Contains("Rune");
            if (eyeName || m.name.Contains("Glow")) return s.Eye.HasValue && eyeName ? MatKind.Eye : MatKind.Skip;
            foreach (var k in s.BodyKeys) if (m.name.Contains(k)) return MatKind.Body;
            return MatKind.Other;
        }

        private static Material TintedMaterial(Material src, Spec s)
        {
            var kind = Classify(src, s);
            if (kind == MatKind.Skip) return src;
            string path = s.Dir + "Materials/" + s.Name + "_" + Sanitize(src.name) + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(src);
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = src.shader;
                m.CopyPropertiesFromMaterial(src);
                m.shaderKeywords = src.shaderKeywords;
            }
            m.name = Path.GetFileNameWithoutExtension(path);

            Color bc = src.GetColor("_BaseColor");
            Color nc;
            switch (kind)
            {
                case MatKind.Body:
                    // Knochen/Körper → Haut/Fell; Helligkeit grob erhalten (texturierte Meshy-Knochen sind weiß × Textur)
                    nc = s.Skin * Mathf.Clamp(Lum(bc) / 0.7f, 0.6f, 1.2f);
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black); // kein Untoten-Glimmen
                    m.DisableKeyword("_EMISSION");
                    break;
                case MatKind.Eye:
                    nc = s.Eye.Value;
                    if (m.HasProperty("_EmissionColor"))
                    {
                        Color e = src.GetColor("_EmissionColor");
                        float inten = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                        m.SetColor("_EmissionColor", s.Eye.Value * Mathf.Max(inten, 0f));
                    }
                    break;
                default:
                    float l = Lum(bc), la = Mathf.Max(0.05f, Lum(s.Accent));
                    Color target = s.Accent * Mathf.Clamp(l / la, 0.3f, 2f);
                    // kräftige Stadtfarben (Wappen, Banner) stärker zum Dorf-Ton ziehen
                    Color.RGBToHSV(bc, out _, out float sat, out _);
                    float amt = s.AccentAmount + (1f - s.AccentAmount) * sat * 0.6f;
                    nc = Color.Lerp(bc, target, amt);
                    if (m.HasProperty("_EmissionColor") && m.IsKeywordEnabled("_EMISSION"))
                    {
                        Color e = src.GetColor("_EmissionColor");
                        m.SetColor("_EmissionColor", Color.Lerp(e, s.Accent * Lum(e), s.AccentAmount));
                    }
                    break;
            }
            nc.a = bc.a;
            m.SetColor("_BaseColor", nc);
            if (m.HasProperty("_Color")) m.SetColor("_Color", nc);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static float Lum(Color c) => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
        private static string Sanitize(string n) => n.Replace(' ', '_').Replace("/", "_");
        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;

        // ---------------- Icons (Wellenvorschau + Kodex) ----------------

        private static void RenderIcon(Spec s, GameObject prefab, StringBuilder sb)
        {
            PacingSetup.RenderIcon(prefab, s.IconPath);
            AssetDatabase.ImportAsset(s.IconPath);
            var imp = AssetImporter.GetAtPath(s.IconPath) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single; // Projekt-Vorgabe ist Multiple
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            sb.Append(" | Icon " + s.IconPath);
        }

        // Für den Integrator/C-Theme: Dorf-Configs auf Prefab und Icon zeigen lassen (schreibt Configs/Forest/*.asset)
        [MenuItem("BuddyTD/Walddorf/Dorf-Configs mit Prefabs+Icons verknüpfen")]
        public static void LinkConfigs()
        {
            var sb = new StringBuilder("ForestEnemyBuilder.LinkConfigs:");
            foreach (var s in Specs)
            {
                var cfg = ForestConfig(s.Name);
                if (cfg == null) { sb.Append($"\n  {s.Name}: keine Config unter {ForestConfigDir}"); continue; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(s.PrefabPath);
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>(s.IconPath);
                if (prefab != null) cfg.Prefab = prefab;
                if (icon != null) cfg.Icon = icon;
                EditorUtility.SetDirty(cfg);
                sb.Append($"\n  {s.Name}: Prefab {(prefab != null ? "ok" : "fehlt")}, Icon {(icon != null ? "ok" : "fehlt")}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        // ---------------- Kontaktbogen (Prüfung) ----------------

        // Spalten = Dorf-Gegner; Zeilen: Untoten-Vorlage (Ruhepose) | Dorf-Prefab (Ruhepose) | Vorlage im Treffer-Frame |
        // Dorf-Prefab im Treffer-Frame (Attack-Zustand bei AttackWindup). Beide mit VisualScale der Config, gleiche Kamera je Spalte.
        public static string RenderContactSheet(string outPath, int cell = 200)
        {
            int cols = Specs.Length, rows = 4;
            var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
            var log = new StringBuilder("Kontaktbogen:");
            for (int c = 0; c < cols; c++)
            {
                var s = Specs[c];
                var orig = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + s.Source + ".prefab");
                var forest = AssetDatabase.LoadAssetAtPath<GameObject>(s.PrefabPath);
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(CanonConfigDir + s.SourceConfig + ".asset");
                float vs = cfg != null && cfg.VisualScale > 0f ? cfg.VisualScale : 1f;
                float windup = cfg != null ? cfg.AttackWindup : 0.35f;
                var px = RenderColumn(new[] { orig, forest, orig, forest }, new[] { -1f, -1f, windup, windup }, vs, cell, log, s.Name);
                for (int r = 0; r < rows; r++)
                    sheet.SetPixels(c * cell, (rows - 1 - r) * cell, cell, cell, px[r]);
            }
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Debug.Log(log.ToString());
            return log.ToString();
        }

        private static Color[][] RenderColumn(GameObject[] prefabs, float[] times, float visualScale, int size, StringBuilder log, string label)
        {
            var result = new Color[prefabs.Length][];
            var insts = new List<GameObject>();
            var camGo = new GameObject("__SheetCam") { hideFlags = HideFlags.HideAndDontSave };
            var key = new GameObject("__SheetKey") { hideFlags = HideFlags.HideAndDontSave };
            var fill = new GameObject("__SheetFill") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            try
            {
                var l1 = key.AddComponent<Light>();
                l1.type = LightType.Directional; l1.intensity = 1.3f; l1.color = new Color(1f, 0.97f, 0.92f);
                key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                var l2 = fill.AddComponent<Light>();
                l2.type = LightType.Directional; l2.intensity = 0.5f; l2.color = new Color(0.85f, 0.9f, 1f);
                fill.transform.rotation = Quaternion.Euler(10f, 230f, 0f);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 24f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.32f, 0.36f, 0.40f);

                // gemeinsame Hülle über alle Zeilen → gleicher Maßstab
                Bounds all = default; bool anyAll = false;
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (prefabs[i] == null) { insts.Add(null); continue; }
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i]);
                    inst.hideFlags = HideFlags.HideAndDontSave;
                    inst.transform.position = new Vector3(0f, -800f, 0f);
                    inst.transform.localScale *= visualScale;
                    foreach (var ps in inst.GetComponentsInChildren<ParticleSystemRenderer>(true)) ps.enabled = false;
                    foreach (var cv in inst.GetComponentsInChildren<Canvas>(true)) cv.enabled = false;
                    Pose(inst, times[i], log, label + "[" + i + "]");
                    inst.SetActive(false);
                    insts.Add(inst);
                    if (TryBounds(inst, out var b)) { if (anyAll) all.Encapsulate(b); else { all = b; anyAll = true; } }
                }
                float radius = Mathf.Max(all.extents.y, Mathf.Max(all.extents.x, all.extents.z) * 0.9f);
                float dist = radius * 1.05f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                Vector3 dir = Quaternion.Euler(0f, 30f, 0f) * Vector3.forward + Vector3.up * 0.3f;
                cam.transform.position = all.center + dir.normalized * dist;
                cam.transform.LookAt(all.center);

                for (int i = 0; i < insts.Count; i++)
                {
                    if (insts[i] == null) { result[i] = Fill(size, Color.magenta); continue; }
                    insts[i].SetActive(true);
                    Pose(insts[i], times[i], null, null); // nach SetActive neu auswerten
                    cam.Render();
                    insts[i].SetActive(false);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    result[i] = tex.GetPixels();
                    Object.DestroyImmediate(tex);
                }
                cam.targetTexture = null;
            }
            finally
            {
                foreach (var i in insts) if (i != null) Object.DestroyImmediate(i);
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(key); Object.DestroyImmediate(fill);
                rt.Release(); Object.DestroyImmediate(rt);
            }
            return result;
        }

        private static Color[] Fill(int size, Color c)
        {
            var a = new Color[size * size];
            for (int i = 0; i < a.Length; i++) a[i] = c;
            return a;
        }

        // time < 0: Ruhepose (Standardzustand); sonst Attack-Zustand bei time Sekunden (Zustandstempo berücksichtigt)
        private static void Pose(GameObject inst, float time, StringBuilder log, string label)
        {
            var anim = inst.GetComponentInChildren<Animator>(true);
            if (anim == null || anim.runtimeAnimatorController == null) return;
            anim.Rebind();
            anim.Update(0f); // Zustandsautomat initialisieren, sonst greift Play im Edit-Modus nicht
            if (time >= 0f)
            {
                var ac = anim.runtimeAnimatorController as AnimatorController;
                AnimatorState st = null;
                if (ac != null)
                {
                    foreach (var t in ac.layers[0].stateMachine.anyStateTransitions)
                        foreach (var cnd in t.conditions)
                            if (cnd.parameter == "Attack" && t.destinationState != null) st = t.destinationState;
                    if (st == null)
                        foreach (var cs in ac.layers[0].stateMachine.states)
                            foreach (var t in cs.state.transitions)
                                foreach (var cnd in t.conditions)
                                    if (cnd.parameter == "Attack" && t.destinationState != null) st = t.destinationState;
                }
                var clip = st != null ? st.motion as AnimationClip : null;
                if (clip != null && clip.length > 0f)
                {
                    float norm = time * Mathf.Abs(st.speed) / clip.length;
                    // Animator.Play greift im Edit-Modus an versteckten Instanzen nicht → Clip direkt abtasten
                    clip.SampleAnimation(anim.gameObject, time * Mathf.Abs(st.speed));
                    log?.Append($"\n  {label}: {st.name}/{clip.name} t={time:0.00}s → {norm:0.00} (Clip {clip.length:0.00}s, Tempo {st.speed:0.##})");
                    return;
                }
                else log?.Append($"\n  {label}: kein Attack-Zustand");
            }
            anim.Update(0f);
        }

        private static bool TryBounds(GameObject go, out Bounds b)
        {
            b = default; bool any = false;
            bool was = go.activeSelf; go.SetActive(true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            go.SetActive(was);
            return any;
        }
    }
}
