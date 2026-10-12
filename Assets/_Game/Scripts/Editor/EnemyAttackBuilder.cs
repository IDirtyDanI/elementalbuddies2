using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Nahkampf-Angriffe der Gegner (2026-10-12): Einzelhiebe statt Dauerschaden (EnemyBrain.HandleMeleeAttack).
    //  1. Angriffs-FBX (Animations/Enemies/<Typ>_Attack.fbx, eigene Blender-Clips, Rig „Armature“ wie Skeletton.fbx)
    //     als Humanoid mit dem Avatar von Skeletton.fbx (CopyFromOther), kein Loop, Wurzel fest (keine Root Motion)
    //  2. Controller <Typ>Attack.controller: Running ↔ Idle über Moving, Attack per Any-State-Trigger
    //  3. Controller an Runner/Swarm/Knight.prefab (RunnerVisual.controller bleibt unverändert)
    //  4. Config-Werte: AttackWindup = Trefferframe, AttackInterval, Trefferton – auch Grabspinne und Knochenfürst
    //  5. Block-Funken/-Ton des Schwertkämpfers (Player.prefab) auf Einzelhiebe abgestimmt
    // Menü: BuddyTD → Gegner → Nahkampf-Angriffe einrichten. Mehrfach ausführbar (Controller behalten ihre GUID).
    // Die Sounds (SfxId.EnemyClaw/EnemySnap/EnemyChop) danach über BuddyTD → Game Feel → Nur Sounds zuweisen.
    public static class EnemyAttackBuilder
    {
        public const string AnimDir = "Assets/_Game/Animations/Enemies/";
        private const string SkelFbx = "Assets/3D Models/Enemy/Skeletton.fbx";
        private const string PrefabDir = "Assets/_Game/Prefabs/";
        private const string ConfigDir = "Assets/ScriptableObjects/Configs/";
        private const string SpiderController = "Assets/_Game/Animations/GraveSpiderVisual.controller";
        private const string SpiderBiteClip = "Assets/_Game/Animations/GraveSpider_Bite.anim";
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Player.prefab";

        // Knochenfürst: Klinge im Clip „Sword Outward Slash“ (2,03 s) bei ~0,9 s, Attack-State Tempo 1,6 (BossBuilder) → ~0,56 s
        public const float BoneLordWindup = 0.55f;
        public const float BoneLordInterval = 1.6f;
        // Grabspinne: Biss-Clip 0,45 s, Zubeißen am Clip-Ende → State-Tempo 0,45 / 0,25 = 1,8
        public const float SpiderWindup = 0.25f;
        public const float SpiderInterval = 1f;
        // Block-Funken/-Ton: fängt nur gleichzeitige Treffer ab (vorher 0,25 / 0,2 s gegen Dauerschaden)
        private const float BlockSparkInterval = 0.05f;

        private struct AttackType
        {
            public string Name; public float Windup, Interval; public SfxId Sfx;
            public AttackType(string name, float windup, float interval, SfxId sfx)
            { Name = name; Windup = windup; Interval = interval; Sfx = sfx; }
        }

        // Clip-Längen 0,9 / 0,6 / 1,2 s, Trefferframe 0,35 / 0,2 / 0,55 s → State-Tempo 1, Windup = Trefferzeit
        private static readonly AttackType[] Types =
        {
            new AttackType("Runner", 0.35f, 1.0f, SfxId.EnemyClaw),
            new AttackType("Swarm", 0.2f, 0.7f, SfxId.EnemySnap),
            new AttackType("Knight", 0.55f, 1.6f, SfxId.EnemyChop),
        };

        [MenuItem("BuddyTD/Gegner/Nahkampf-Angriffe einrichten")]
        public static void BuildMenu()
        {
            Debug.Log(Build());
        }

        public static string Build()
        {
            var log = new StringBuilder("EnemyAttackBuilder:\n");
            var avatar = SkeletonAvatar();
            var running = Clip(SkelFbx, "Running");
            if (avatar == null) log.AppendLine("  FEHLER: kein Avatar in " + SkelFbx);
            if (running == null) log.AppendLine("  FEHLER: Clip „Running“ fehlt in " + SkelFbx);

            foreach (var t in Types)
            {
                // Config immer (die Einzelhiebe laufen auch ohne neue Animation)
                SetConfig(t.Name, t.Windup, t.Interval, t.Sfx, log);
                if (avatar == null || running == null) continue;
                var clip = ImportAttack(AnimDir + t.Name + "_Attack.fbx", t.Name + "_Attack", avatar, log);
                if (clip == null) continue;
                var ctrl = BuildController(t.Name + "Attack", running, clip);
                log.AppendLine("  Controller " + AssetDatabase.GetAssetPath(ctrl) + " (Running/Idle/Attack)");
                AssignController(t.Name, ctrl, log);
            }

            SetConfig("GraveSpider", SpiderWindup, SpiderInterval, SfxId.EnemySnap, log);
            TuneSpiderBite(log);
            SetConfig("Bosses/BossBoneLord", BoneLordWindup, BoneLordInterval, SfxId.EnemyChop, log);
            TuneKnightBlock(log);

            AssetDatabase.SaveAssets();
            log.Append("  fertig – Sounds: BuddyTD → Game Feel → Nur Sounds zuweisen");
            return log.ToString();
        }

        // ---------------- 1. Import ----------------

        private static Avatar SkeletonAvatar()
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SkelFbx))
                if (o is Avatar a) return a;
            return null;
        }

        private static AnimationClip Clip(string path, string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var c = o as AnimationClip;
                if (c == null || c.name.StartsWith("__preview")) continue;
                if (name == null || c.name == name) return c;
            }
            return null;
        }

        // Humanoid mit dem Skeletton-Avatar, ein Clip (Name = clipName), kein Loop, Wurzel fest; nur bei Abweichung neu importieren
        private static AnimationClip ImportAttack(string path, string clipName, Avatar avatar, StringBuilder log)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null)
            {
                log.AppendLine("  " + path + " fehlt – Controller/Prefab übersprungen");
                return null;
            }

            if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CopyFromOther
                || mi.sourceAvatar != avatar || !mi.importAnimation || mi.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                mi.importCameras = false;
                mi.importLights = false;
                mi.importBlendShapes = false;
                mi.importConstraints = false;
                mi.useFileScale = true;
                mi.globalScale = 1f;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importAnimation = true;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                mi.sourceAvatar = avatar;
                mi.resampleCurves = true;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
                mi.SaveAndReimport();
            }

            var current = mi.clipAnimations;
            var defs = current != null && current.Length > 0 ? current : mi.defaultClipAnimations;
            if (defs == null || defs.Length == 0)
            {
                log.AppendLine("  " + path + ": keine Animation im FBX");
                return null;
            }
            // Take mit passendem Namen bevorzugen (Blender exportiert ggf. „Armature|Runner_Attack“)
            var c = defs[0];
            foreach (var d in defs)
                if ((d.takeName != null && d.takeName.Contains(clipName)) || d.name == clipName) { c = d; break; }

            bool clipDirty = current == null || current.Length != 1 || c.name != clipName || c.loopTime || c.loopPose
                             || !c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ
                             || !c.keepOriginalOrientation || !c.keepOriginalPositionY || !c.keepOriginalPositionXZ;
            if (clipDirty)
            {
                c.name = clipName;
                c.loopTime = false;
                c.loopPose = false;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
                mi.clipAnimations = new[] { c };
                mi.SaveAndReimport();
            }

            var clip = Clip(path, clipName);
            if (clip == null)
            {
                log.AppendLine("  " + path + ": Clip " + clipName + " nicht gefunden");
                return null;
            }
            log.AppendLine(string.Format("  {0}: {1} {2:0.00} s, Humanoid {3}, Loop {4}{5}", path, clipName, clip.length,
                clip.isHumanMotion ? "ja" : "NEIN", clip.isLooping ? "ja" : "nein",
                clip.isHumanMotion ? "" : " – WARNUNG: Rig passt nicht zum Skeletton-Avatar?"));
            return clip;
        }

        // ---------------- 2. Controller ----------------

        // Running (Skeletton-Laufclip, wie RunnerVisual) ↔ Idle (Kampfhaltung = erster Frame des Angriffs, Tempo 0) über Moving;
        // Attack per Any-State-Trigger, am Ende zurück nach Running bzw. Idle. Speed-Parameter nur für EnemyBrain vorhanden.
        private static AnimatorController BuildController(string name, AnimationClip running, AnimationClip attack)
        {
            // Bestehenden Controller leeren statt löschen (GUID bleibt, Prefab-Referenzen bleiben gültig)
            string path = AnimDir + name + ".controller";
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var s in sm.states) sm.RemoveState(s.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Moving", AnimatorControllerParameterType.Bool);

            var run = sm.AddState("Running", new Vector3(300, 0, 0));
            run.motion = running;
            run.speed = 1f;
            var idle = sm.AddState("Idle", new Vector3(300, 120, 0));
            idle.motion = attack;
            idle.speed = 0f; // steht zwischen zwei Hieben in der Ausgangspose des Angriffs
            sm.defaultState = run;

            var toIdle = run.AddTransition(idle);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            toIdle.hasExitTime = false; toIdle.duration = 0.15f;
            var toRun = idle.AddTransition(run);
            toRun.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            toRun.hasExitTime = false; toRun.duration = 0.15f;

            // Tempo 1: Trefferframe des Clips = AttackWindup der Config
            var atk = sm.AddState("Attack", new Vector3(600, 60, 0));
            atk.motion = attack;
            atk.speed = 1f;
            var any = sm.AddAnyStateTransition(atk);
            any.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            any.hasExitTime = false;
            any.hasFixedDuration = true;
            any.duration = 0.05f;
            any.canTransitionToSelf = true;
            var a = atk.AddTransition(run);
            a.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            a.hasExitTime = true; a.exitTime = 0.92f; a.hasFixedDuration = true; a.duration = 0.12f;
            var b = atk.AddTransition(idle);
            b.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            b.hasExitTime = true; b.exitTime = 0.92f; b.hasFixedDuration = true; b.duration = 0.12f;
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ---------------- 3. Prefabs ----------------

        private static void AssignController(string prefabName, AnimatorController ctrl, StringBuilder log)
        {
            string path = PrefabDir + prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                log.AppendLine("  " + path + " fehlt");
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var anim = root.GetComponentInChildren<Animator>(true);
                if (anim == null) log.AppendLine("  " + path + ": kein Animator");
                else if (anim.runtimeAnimatorController == ctrl && !anim.applyRootMotion) log.AppendLine("  " + path + ": Controller schon gesetzt");
                else
                {
                    anim.runtimeAnimatorController = ctrl;
                    anim.applyRootMotion = false;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine("  " + path + ": Animator „" + anim.name + "“ → " + ctrl.name);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------- 4. Configs ----------------

        private static void SetConfig(string name, float windup, float interval, SfxId sfx, StringBuilder log)
        {
            string path = ConfigDir + name + ".asset";
            var c = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(path);
            if (c == null)
            {
                log.AppendLine("  " + path + " fehlt");
                return;
            }
            c.AttackWindup = windup;
            c.AttackInterval = interval;
            c.MeleeHitSound = true;
            c.MeleeHitSfx = sfx;
            EditorUtility.SetDirty(c);
            // AttackDamage bleibt DPS: ein Hieb = AttackDamage × AttackInterval
            log.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  {0}: Ausholen {1:0.00} s, Intervall {2:0.00} s, {3:0.#} DPS → {4:0.#} je Hieb, Ton {5}",
                name, windup, interval, c.AttackDamage, c.AttackDamage * interval, sfx));
        }

        // Grabspinne: Biss-State so schnell, dass das Zubeißen (Clip-Ende) auf AttackWindup fällt; Ausgang erst am Clip-Ende
        private static void TuneSpiderBite(StringBuilder log)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(SpiderController);
            var bite = AssetDatabase.LoadAssetAtPath<AnimationClip>(SpiderBiteClip);
            if (ac == null || bite == null)
            {
                log.AppendLine("  Grabspinne: Controller oder Biss-Clip fehlt");
                return;
            }
            float speed = Mathf.Round(bite.length / SpiderWindup * 100f) / 100f;
            bool found = false;
            foreach (var s in ac.layers[0].stateMachine.states)
            {
                if (s.state.motion != bite) continue;
                found = true;
                s.state.speed = speed;
                foreach (var t in s.state.transitions)
                    if (t.hasExitTime) t.exitTime = 1f;
            }
            EditorUtility.SetDirty(ac);
            log.AppendLine(found
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Grabspinne: Biss {0:0.00} s × Tempo {1:0.00} → Zubeißen bei {2:0.00} s", bite.length, speed, bite.length / speed)
                : "  Grabspinne: kein State mit " + SpiderBiteClip);
        }

        // ---------------- 5. Schildblock ----------------

        private static void TuneKnightBlock(StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab) == null)
            {
                log.AppendLine("  " + PlayerPrefab + " fehlt");
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                bool changed = false;
                foreach (var k in root.GetComponentsInChildren<KnightKit>(true))
                {
                    if (Mathf.Approximately(k.BlockSparkInterval, BlockSparkInterval) && k.BlockHitSfx != null
                        && Mathf.Approximately(k.BlockHitSfx.MinInterval, BlockSparkInterval)) continue;
                    k.BlockSparkInterval = BlockSparkInterval;
                    if (k.BlockHitSfx != null) k.BlockHitSfx.MinInterval = BlockSparkInterval;
                    changed = true;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
                log.AppendLine("  Schildblock: Funken/Ton alle ≥ " + BlockSparkInterval.ToString(System.Globalization.CultureInfo.InvariantCulture)
                               + " s" + (changed ? " (Player.prefab gesetzt)" : " (schon gesetzt)"));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
