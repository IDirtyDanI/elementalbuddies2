using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Fremd-Assets-Ersatz Paket D (2026-10-18): Grabspinne mit eigenem Modell GraveSpider2 (art-src/enemies/grave_spider.py)
    // statt „Fiese Spinne.fbx“. Menü: BuddyTD → Gegner → Grabspinne mit eigenem Modell bauen (GraveSpider2). Idempotent.
    //  1. Import wie die Dorf-/Untoten-Modelle (ForestEnemyBuilder.ConfigureModelImport: Generic, Takes → Clips nach dem „|“,
    //     Loop laut Sidecar für _Idle/_Walk, Materialien je Slot unter GraveSpider2/Materials)
    //  2. GraveSpiderVisual.controller (GUID bleibt, Parameter Moving/Speed/Attack bleiben, neu „Die“):
    //     Walk-Tempo = Speed (m/s) × 1/4,6 (Entwurfstempo des Clips) → Füße rutschen nicht; Idle/Bite Tempo 1
    //     (Biss-Treffer 0,25 s = AttackWindup); Die per AnyState (bleibt stehen, EnemyBrain.PlayDeathVisual + EnemyCorpse)
    //  3. Config: DeathClipDuration aus dem Sidecar (1,0 s)
    //  4. GraveSpider.prefab (GUID bleibt): Visual-Kind geleert, GraveSpider2.fbx in Skalierung 1 eingesetzt, Animator auf der
    //     Modell-Wurzel (Clip-Pfade relativ dazu), Skinned-Bounds bis 1,15 m Höhe; Wurzel-Komponenten bleiben unverändert
    //  5. Icon/Kodex-Bild icon_enemy_gravespider.png neu gerendert (PacingSetup.RenderIcon)
    public static class GraveSpiderBuilder
    {
        public const string Fbx = "Assets/_Game/Models/Enemies/GraveSpider2/GraveSpider2.fbx";
        public const string PrefabPath = "Assets/_Game/Prefabs/GraveSpider.prefab";
        public const string ControllerPath = "Assets/_Game/Animations/GraveSpiderVisual.controller";
        public const string ConfigPath = "Assets/ScriptableObjects/Configs/GraveSpider.asset";
        private const string IconPath = "Assets/_Game/UI/Icons/icon_enemy_gravespider.png";

        // Lage des Modells im Visual-Kind: Wurzel steht 0,45 m über dem Boden (NavMeshAgent.baseOffset), Körper leicht zurück
        private static readonly Vector3 VisualPos = new Vector3(0f, -0.45f, -0.05f);
        private const float BoundsTop = 1.15f;   // Skinned-Bounds bis Kopfhöhe beim Biss (Aufbäumen)

        [MenuItem("BuddyTD/Gegner/Grabspinne mit eigenem Modell bauen (GraveSpider2)")]
        private static void BuildMenu() => Debug.Log(Build());

        public static string Build(bool renderIcon = true)
        {
            var sb = new StringBuilder("GraveSpiderBuilder:");
            var side = ForestEnemyBuilder.ReadSidecar(Fbx);
            if (side == null || AssetImporter.GetAtPath(Fbx) == null) return sb.Append("\n  FBX oder Sidecar fehlt: " + Fbx).ToString();
            ForestEnemyBuilder.ConfigureModelImport(Fbx, side, true);
            var ctrl = BuildController(side, sb);

            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigPath);
            if (cfg != null && side.deathDuration > 0f && !Mathf.Approximately(cfg.DeathClipDuration, side.deathDuration))
            {
                cfg.DeathClipDuration = side.deathDuration;
                EditorUtility.SetDirty(cfg);
            }
            var bite = side.Role("attack");
            if (cfg != null && bite != null && bite.hit > 0f && !Mathf.Approximately(cfg.AttackWindup, bite.hit))
                sb.Append($"\n  Hinweis: Biss-Treffer {bite.hit:0.##} s ≠ AttackWindup {cfg.AttackWindup:0.##} s");

            BuildPrefab(ctrl, sb);
            if (renderIcon && cfg != null) RenderIcon(cfg, sb);
            AssetDatabase.SaveAssets();
            sb.Append($"\n  Config: DeathClipDuration {(cfg != null ? cfg.DeathClipDuration : 0f):0.0} s");
            return sb.ToString();
        }

        private static AnimationClip Clip(ForestEnemyBuilder.Sidecar side, string role)
        {
            var c = side.Role(role);
            if (c == null) return null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx))
                if (o is AnimationClip ac && ac.name == c.name) return ac;
            return null;
        }

        // Zustände neu aufbauen, Parameter behalten (EnemyBrain sucht Moving/Speed/Attack beim Start), „Die“ ergänzen
        private static AnimatorController BuildController(ForestEnemyBuilder.Sidecar side, StringBuilder sb)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var st in sm.states) sm.RemoveState(st.state);
            void Param(string n, AnimatorControllerParameterType type)
            {
                foreach (var p in ac.parameters) if (p.name == n && p.type == type) return;
                ac.AddParameter(n, type);
            }
            Param("Attack", AnimatorControllerParameterType.Trigger);
            Param("Moving", AnimatorControllerParameterType.Bool);
            Param("Speed", AnimatorControllerParameterType.Float);
            Param("Die", AnimatorControllerParameterType.Trigger);

            var idle = sm.AddState("Idle", new Vector3(300, 120, 0));
            idle.motion = Clip(side, "idle");
            var walk = sm.AddState("Walk", new Vector3(300, 0, 0));
            walk.motion = Clip(side, "move");
            walk.speedParameterActive = true;
            walk.speedParameter = "Speed";
            walk.speed = 1f / Mathf.Max(0.05f, side.moveSpeedModel);   // Clip-Tempo = Laufgeschwindigkeit / Entwurfstempo
            var bite = sm.AddState("Bite", new Vector3(560, 60, 0));
            bite.motion = Clip(side, "attack");                          // Tempo 1: Treffer-Frame = AttackWindup
            var die = sm.AddState("Die", new Vector3(560, 200, 0));
            die.motion = Clip(side, "death");
            sm.defaultState = idle;

            var toWalk = idle.AddTransition(walk);
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            toWalk.hasExitTime = false; toWalk.duration = 0.12f;
            var toIdle = walk.AddTransition(idle);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            toIdle.hasExitTime = false; toIdle.duration = 0.12f;

            // Die zuerst (Vorrang vor Attack im selben Frame)
            var anyDie = sm.AddAnyStateTransition(die);
            anyDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            anyDie.duration = 0.08f;
            anyDie.canTransitionToSelf = false;
            var anyBite = sm.AddAnyStateTransition(bite);
            anyBite.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            anyBite.duration = 0.05f;
            anyBite.canTransitionToSelf = true;
            // Rückweg (0,8 s × 0,85 + 0,1 s) endet vor dem nächsten Biss nach AttackInterval 1 s – während einer laufenden
            // Transition griffe der AnyState-Übergang nicht (vgl. AnimalModels)
            foreach (var (to, mode) in new[] { (walk, AnimatorConditionMode.If), (idle, AnimatorConditionMode.IfNot) })
            {
                var t = bite.AddTransition(to);
                t.AddCondition(mode, 0f, "Moving");
                t.hasExitTime = true; t.exitTime = 0.85f; t.duration = 0.1f;
            }
            foreach (var st in sm.states)
                if (st.state.motion == null) sb.Append($"\n  Warnung: State {st.state.name} ohne Clip");
            EditorUtility.SetDirty(ac);
            sb.Append($"\n  Controller {ControllerPath}: Walk 1/{side.moveSpeedModel:0.##} × Speed, Idle/Bite Tempo 1, Die (AnyState)");
            return ac;
        }

        private static void BuildPrefab(AnimatorController ctrl, StringBuilder sb)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var visual = root.transform.Find("Visual");
                if (visual == null)
                {
                    visual = new GameObject("Visual").transform;
                    visual.SetParent(root.transform, false);
                }
                for (int i = visual.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visual.GetChild(i).gameObject);
                // Animator wandert auf die Modell-Wurzel (Generic-Avatar aus dem FBX, Clip-Pfade relativ dazu)
                var oldAnim = visual.GetComponent<Animator>();
                if (oldAnim != null) Object.DestroyImmediate(oldAnim);
                visual.localPosition = VisualPos;
                visual.localRotation = Quaternion.identity;
                visual.localScale = Vector3.one;

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx), visual);
                inst.name = "GraveSpider2";
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = visual.gameObject.layer;
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if (o is Avatar av) { anim.avatar = av; break; }
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                // Skinned-Bounds: Ruhepose umschließen und nach oben bis BoundsTop (über dem Boden) erweitern, damit
                // Biss/Aufbäumen nicht vom Frustum-Culling abgeschnitten werden
                anim.Rebind();
                anim.Update(0f);
                float groundY = visual.position.y;
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var space = smr.rootBone != null ? smr.rootBone : smr.transform;
                    var lb = smr.localBounds;
                    Vector3 top = space.InverseTransformPoint(new Vector3(visual.position.x, groundY + BoundsTop, visual.position.z));
                    Vector3 bottom = space.InverseTransformPoint(new Vector3(visual.position.x, groundY, visual.position.z));
                    lb.Encapsulate(top);
                    lb.Encapsulate(bottom);
                    lb.Expand(0.1f / Mathf.Max(1e-4f, space.lossyScale.x));   // 10 cm Rand (Knochenraum ist ×100 skaliert)
                    smr.localBounds = lb;
                    smr.updateWhenOffscreen = false;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                sb.Append($"\n  Prefab {PrefabPath}: Visual {VisualPos}, Modell {Fbx} (Skalierung 1), Animator auf {inst.name}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // wie ChampionThreatSetup.RenderEnemyIcon: gleiche Datei → Config-Icon und Kodex-Eintrag zeigen das neue Bild
        private static void RenderIcon(EnemyConfigSO cfg, StringBuilder sb)
        {
            if (cfg.Prefab == null) return;
            PacingSetup.RenderIcon(cfg.Prefab, IconPath);
            AssetDatabase.ImportAsset(IconPath);
            var imp = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            cfg.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            EditorUtility.SetDirty(cfg);
            sb.Append("\n  Icon " + IconPath);
        }
    }
}
