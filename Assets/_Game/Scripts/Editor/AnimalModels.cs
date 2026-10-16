using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Walddorf Etappe 2, Paket A-Tiere: Import der eigenen Tier-Modelle (art-src/enemies/tiere_run.py) –
    // Wolf, Waldspinne, Mini-Wyvern, Riesen-Fledermaus unter Assets/_Game/Models/Enemies/<Ordner>/<Name>.fbx.
    // Menü: BuddyTD → Walddorf → Tier-Modelle importieren (Wolf, Spinne, Flieger). Idempotent.
    //  - FBX: Generic-Rig (Avatar aus dem Modell), keine Kollider, Clips = Takes (Name nach dem „|“), Loop für
    //    *_Idle/*_Run/*_FlyLoop/*_Glide. Materialien je Slot (Models/Enemies/<Ordner>/Materials/<Slot>.mat, URP/Lit wie
    //    Stadt/Vegetation, Farben aus art-src/enemies/palette.json + art-src/village/palette.json, „_emission“-Slots
    //    emissiv).
    //  - Wolf: Import-Skalierung 1/0,6. Die Dorf-Config behält VisualScale 0,6 der Schwarm-Rolle (Agent/Collider der
    //    Schwarm-Wurzel unverändert), das Modell erscheint im Spiel trotzdem in echter Größe (1,63 m inkl. Rute).
    //  - Controller <Ordner>/<Name>.controller (ForestEnemyBuilder nimmt ihn statt des Automatik-Controllers):
    //    Bodentiere Idle ⇄ Run (Bool „Moving“; Run-Tempo = Parameter „Speed“ (m/s) × 1/Entwurfstempo → rutschfrei),
    //    Flieger FlyLoop; beide „Attack“ (Trigger, AnyState) und „Die“ (Trigger, AnyState, bleibt am Ende stehen –
    //    EnemyBrain.PlayDeathVisual + EnemyCorpse, Länge = EnemyConfigSO.DeathClipDuration).
    public static class AnimalModels
    {
        public const string ModelRoot = "Assets/_Game/Models/Enemies/";
        private const string ArtSrc = "/Users/daniel/Desktop/BuddyTD/art-src/";

        public class Animal
        {
            public string Name, Folder;
            public float ImportScale = 1f;
            public float RunSpeed;          // Entwurfstempo des Lauf-Clips (m/s); 0 = Flieger
            public bool Flyer => RunSpeed <= 0f;
            public string FbxPath => ModelRoot + Folder + "/" + Name + ".fbx";
            public string ControllerPath => ModelRoot + Folder + "/" + Name + ".controller";
            public string MaterialDir => ModelRoot + Folder + "/Materials";
        }

        public static readonly Animal Wolf = new Animal { Name = "Wolf", Folder = "Wolf", ImportScale = 1f / 0.6f, RunSpeed = 5.2f };
        public static readonly Animal Spider = new Animal { Name = "Waldspinne", Folder = "Waldspinne", RunSpeed = 4.6f };
        public static readonly Animal Wyvern = new Animal { Name = "Wyvern", Folder = "Wyvern" };
        public static readonly Animal Bat = new Animal { Name = "Bat", Folder = "Bat" };
        public static readonly Animal[] All = { Wolf, Spider, Wyvern, Bat };

        [MenuItem("BuddyTD/Walddorf/Tier-Modelle importieren (Wolf, Spinne, Flieger)")]
        private static void SetupMenu() => Debug.Log(SetupAll());

        public static string SetupAll()
        {
            var sb = new StringBuilder("AnimalModels:");
            foreach (var a in All)
            {
                if (AssetImporter.GetAtPath(a.FbxPath) == null) { sb.Append($"\n  {a.Name}: FBX fehlt ({a.FbxPath})"); continue; }
                sb.Append("\n  " + Import(a));
                BuildController(a, sb);
            }
            SetDeathClip("Assets/ScriptableObjects/Configs/Forest/Wolf.asset", WolfDeath, sb);
            SetDeathClip("Assets/ScriptableObjects/Configs/Forest/Waldspinne.asset", SpiderDeath, sb);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // Tod-Clip-Längen (Clip + kurzes Liegen; EnemyCorpse entfernt das Modell danach). Nur dieses Optik-Feld der
        // Dorf-Configs wird gesetzt, die Balance-Werte bleiben unverändert.
        public const float WolfDeath = 1.0f, SpiderDeath = 1.0f;

        private static void SetDeathClip(string cfgPath, float seconds, StringBuilder sb)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(cfgPath);
            if (cfg == null) { sb.Append($"\n  Config fehlt: {cfgPath}"); return; }
            if (!Mathf.Approximately(cfg.DeathClipDuration, seconds)) { cfg.DeathClipDuration = seconds; EditorUtility.SetDirty(cfg); }
            sb.Append($"\n  {Path.GetFileNameWithoutExtension(cfgPath)}: DeathClipDuration {seconds:0.0} s");
        }

        // ---------------- Import ----------------

        public static string Import(Animal a)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(a.FbxPath);
            imp.useFileScale = true;
            imp.globalScale = a.ImportScale;
            imp.addCollider = false;
            imp.importNormals = ModelImporterNormals.Import;
            imp.isReadable = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.importVisibility = false;
            imp.importConstraints = false;
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.optimizeGameObjects = false;
            imp.importAnimation = true;
            imp.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            imp.animationRotationError = 0.2f;
            imp.animationPositionError = 0.2f;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.SaveAndReimport();

            // Clips: Name ohne „Rig|“-Präfix, Loops
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var c in imp.defaultClipAnimations)
            {
                string n = c.takeName;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.name = n;
                c.loopTime = c.loopPose = IsLoop(n);
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = false;
                clips.Add(c);
            }
            imp.clipAnimations = clips.ToArray();

            // Materialien je Slot
            NetworkSetup.EnsureFolder(a.MaterialDir);
            var pal = ReadPalette(out var emission);
            var slots = new List<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(a.FbxPath))
                if (o is Material m && !slots.Contains(m.name)) slots.Add(m.name);
            foreach (var kv in imp.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material) && !slots.Contains(kv.Key.name)) slots.Add(kv.Key.name);
            foreach (var s in slots)
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), s), SlotMaterial(a, s, pal, emission));
            imp.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(a.FbxPath);
            int tris = 0;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) tris += r.sharedMesh.triangles.Length / 3;
            var names = new List<string>();
            foreach (var c in imp.clipAnimations) names.Add(c.name);
            return $"{a.Name}: {tris} Tris, Skalierung {a.ImportScale:0.###}, Clips {string.Join(",", names)}, Slots {slots.Count}";
        }

        public static bool IsLoop(string clip) =>
            clip.EndsWith("_Idle") || clip.EndsWith("_Run") || clip.EndsWith("_FlyLoop") || clip.EndsWith("_Glide");

        private static Material SlotMaterial(Animal a, string slot, Dictionary<string, Color> pal, Dictionary<string, float> emission)
        {
            string p = a.MaterialDir + "/" + slot + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = slot };
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", 2f);
            m.enableInstancing = true;
            if (pal.TryGetValue(slot, out var c)) { m.SetColor("_BaseColor", c); m.SetColor("_Color", c); }
            else Debug.LogWarning("AnimalModels: keine Farbe für " + slot);
            if (emission.TryGetValue(slot, out var e))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * e);
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

        private static Dictionary<string, Color> ReadPalette(out Dictionary<string, float> emission)
        {
            var result = new Dictionary<string, Color>();
            emission = new Dictionary<string, float>();
            foreach (var f in new[] { ArtSrc + "village/palette.json", ArtSrc + "enemies/palette.json" })
            {
                if (!File.Exists(f)) { Debug.LogWarning("AnimalModels: Palette fehlt: " + f); continue; }
                string json = File.ReadAllText(f);
                foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*\"#([0-9a-fA-F]{6})\""))
                    if (ColorUtility.TryParseHtmlString("#" + mt.Groups[2].Value, out var c)) result[mt.Groups[1].Value] = c;
                var block = System.Text.RegularExpressions.Regex.Match(json, "\"_emission\"\\s*:\\s*\\{([^}]*)\\}");
                if (block.Success)
                    foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(block.Groups[1].Value, "\"(\\w+)\"\\s*:\\s*([0-9.]+)"))
                        emission[mt.Groups[1].Value] = float.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            return result;
        }

        // ---------------- Controller ----------------

        public static AnimationClip Clip(Animal a, string suffix)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(a.FbxPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview") && c.name.EndsWith("_" + suffix)) return c;
            return null;
        }

        public static AnimatorController BuildController(Animal a, StringBuilder sb)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(a.ControllerPath);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(a.ControllerPath);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var st in sm.states) sm.RemoveState(st.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            AnimatorState idle, run = null;
            var attack = sm.AddState("Attack", new Vector3(560, 60, 0));
            var die = sm.AddState("Die", new Vector3(560, 200, 0));
            if (a.Flyer)
            {
                idle = sm.AddState("Fly", new Vector3(300, 0, 0));
                idle.motion = Clip(a, "FlyLoop");
                attack.motion = Clip(a, "Attack");
            }
            else
            {
                ac.AddParameter("Moving", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
                idle = sm.AddState("Idle", new Vector3(300, 120, 0));
                idle.motion = Clip(a, "Idle");
                run = sm.AddState("Run", new Vector3(300, 0, 0));
                run.motion = Clip(a, "Run");
                run.speedParameterActive = true;
                run.speedParameter = "Speed";
                run.speed = 1f / a.RunSpeed;   // Clip-Tempo = Laufgeschwindigkeit / Entwurfstempo → Pfoten rutschen nicht
                var toIdle = run.AddTransition(idle);
                toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
                toIdle.hasExitTime = false; toIdle.duration = 0.12f;
                var toRun = idle.AddTransition(run);
                toRun.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
                toRun.hasExitTime = false; toRun.duration = 0.12f;
                attack.motion = Clip(a, "Bite");
            }
            sm.defaultState = idle;
            die.motion = Clip(a, "Die");

            // Die zuerst (Vorrang vor Attack im selben Frame)
            var anyDie = sm.AddAnyStateTransition(die);
            anyDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            anyDie.duration = 0.08f;
            anyDie.canTransitionToSelf = false;
            var anyAtk = sm.AddAnyStateTransition(attack);
            anyAtk.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            anyAtk.duration = 0.05f;
            anyAtk.canTransitionToSelf = true;
            if (run != null)
            {
                // Rückweg muss vor dem nächsten Biss fertig sein (Wolf: Clip 0,7 s = AttackInterval 0,7 s). Während
                // einer laufenden Transition greift der AnyState-Übergang nicht – der Biss käme sonst ~0,1 s zu spät
                // und der Treffer-Frame verrutschte (Play-Test: Treffer bei Clip-Zeit 0,10 statt 0,20).
                var b1 = attack.AddTransition(run);
                b1.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
                b1.hasExitTime = true; b1.exitTime = 0.8f; b1.duration = 0.1f;
                var b2 = attack.AddTransition(idle);
                b2.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
                b2.hasExitTime = true; b2.exitTime = 0.8f; b2.duration = 0.1f;
            }
            else
            {
                var b = attack.AddTransition(idle);
                b.hasExitTime = true; b.exitTime = 0.94f; b.duration = 0.12f;
            }
            foreach (var st in sm.states)
                if (st.state.motion == null) sb.Append($"\n  Warnung {a.Name}: State {st.state.name} ohne Clip");
            EditorUtility.SetDirty(ac);
            return ac;
        }
    }
}
