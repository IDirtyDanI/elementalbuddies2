using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Nahkampf-Angriffe der Gegner (2026-10-12): Einzelhiebe statt Dauerschaden (EnemyBrain.HandleMeleeAttack).
    //  1. Angriffs-Clips: seit 2026-10-18 (Fremd-Assets-Ersatz Paket C) aus Animations/Enemies/Skeleton2_Attacks.fbx
    //     (Skeleton2_<Typ>_Attack, aus den eigenen Blender-Clips <Typ>_Attack.fbx aufs eigene Skelett übertragen), Import
    //     Humanoid mit dem Avatar von Skeleton2.fbx über Skeleton2Builder.Import (kein Loop, Wurzel fest)
    //  2. Controller <Typ>Attack.controller: Running ↔ Idle über Moving, Attack per Any-State-Trigger
    //  3. Controller an Runner/Swarm/Knight.prefab
    //  4. Config-Werte: AttackWindup = Trefferframe, AttackInterval, Trefferton – auch Grabspinne und Knochenfürst
    //     (Knochenfürst: Clip Skeleton2_BoneLord_Attack im Attack-State von BossBoneLordVisual.controller)
    //  5. Block-Funken/-Ton des Schwertkämpfers (Player.prefab) auf Einzelhiebe abgestimmt
    // Menü: BuddyTD → Gegner → Nahkampf-Angriffe einrichten. Mehrfach ausführbar (Controller behalten ihre GUID).
    // Die Sounds (SfxId.EnemyClaw/EnemySnap/EnemyChop) danach über BuddyTD → Game Feel → Nur Sounds zuweisen.
    public static class EnemyAttackBuilder
    {
        public const string AnimDir = "Assets/_Game/Animations/Enemies/";
        private const string PrefabDir = "Assets/_Game/Prefabs/";
        private const string ConfigDir = "Assets/ScriptableObjects/Configs/";
        private const string SpiderController = "Assets/_Game/Animations/GraveSpiderVisual.controller";
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Player.prefab";

        // Knochenfürst (seit 2026-10-15 eigener Clip BoneLord_Attack, art-src/enemy_attacks, ersetzt Mixamo „Sword Outward
        // Slash“; seit 2026-10-18 Skeleton2_BoneLord_Attack aufs eigene Skelett): Bihänder-Schräghieb 1,2 s, Treffer bei 0,55 s,
        // Attack-State Tempo 1 → Windup = Trefferzeit (unverändert 0,55)
        public const float BoneLordWindup = 0.55f;
        public const float BoneLordInterval = 1.6f;
        public const float BoneLordExit = 0.85f;   // Ausstieg nach 1,02 s (Überblendung 0,2 s), vor dem nächsten Hieb nach 1,6 s
        public const string BoneLordController = "Assets/_Game/Animations/Bosses/BossBoneLordVisual.controller";
        // Grabspinne: Biss-Treffer bei 0,25 s (seit 2026-10-18 GraveSpider2_Bite, Tempo 1; vorher alter Clip 0,45 s × Tempo 1,8)
        public const float SpiderWindup = 0.25f;
        public const float SpiderInterval = 1f;
        // Block-Funken/-Ton: fängt nur gleichzeitige Treffer ab (vorher 0,25 / 0,2 s gegen Dauerschaden)
        private const float BlockSparkInterval = 0.05f;

        private struct AttackType
        {
            public string Name; public float Windup, Interval; public SfxId Sfx;
            public string Run; public float RunSpeed; public bool IdleFromAttack;
            public AttackType(string name, float windup, float interval, SfxId sfx, string run, float runSpeed, bool idleFromAttack)
            { Name = name; Windup = windup; Interval = interval; Sfx = sfx; Run = run; RunSpeed = runSpeed; IdleFromAttack = idleFromAttack; }
        }

        // Clip-Längen 0,9 / 0,6 / 1,2 s, Trefferframe 0,35 / 0,2 / 0,55 s → State-Tempo 1, Windup = Trefferzeit.
        // Lauf-Tempo = Config.Speed / (Clip-Tempo Modell × 0,808 × VisualScale) → Füße rutschen nicht:
        // Runner 3,5 / (4,33 × 0,808) = 1,0; Schwarm 5,2 / (5,0 × 0,808 × 0,6) = 2,15; Ritter 2,5 / (2,58 × 0,808 × 1,2) = 1,0.
        // Idle: Skeleton_Idle; Ritter: Kampfhaltung = erster Frame des Hiebs (Tempo 0, Schild vorn) – Skeleton_Idle ließe den
        // Schild hängen
        private static readonly AttackType[] Types =
        {
            new AttackType("Runner", 0.35f, 1.0f, SfxId.EnemyClaw, "Skeleton_Run", 1f, false),
            new AttackType("Swarm", 0.2f, 0.7f, SfxId.EnemySnap, "SkeletonSwarm_Run", 2.15f, false),
            new AttackType("Knight", 0.55f, 1.6f, SfxId.EnemyChop, "SkeletonKnight_Run", 1f, true),
        };

        [MenuItem("BuddyTD/Gegner/Nahkampf-Angriffe einrichten")]
        public static void BuildMenu()
        {
            Debug.Log(Build());
        }

        public static string Build()
        {
            var log = new StringBuilder("EnemyAttackBuilder:\n");
            foreach (var t in Types)
                // Config immer (die Einzelhiebe laufen auch ohne neue Animation)
                SetConfig(t.Name, t.Windup, t.Interval, t.Sfx, log);
            SetConfig("GraveSpider", SpiderWindup, SpiderInterval, SfxId.EnemySnap, log);
            TuneSpiderBite(log);
            SetConfig("Bosses/BossBoneLord", BoneLordWindup, BoneLordInterval, SfxId.EnemyChop, log);
            if (Skeleton2Builder.Import(log)) BuildSkeletonControllers(log);
            TuneKnightBlock(log);

            AssetDatabase.SaveAssets();
            log.Append("  fertig – Sounds: BuddyTD → Game Feel → Nur Sounds zuweisen");
            return log.ToString();
        }

        // Controller der Skelett-Nahkämpfer (Runner/Swarm/Knight) + Knochenfürst-Hieb aus den Skeleton2-Clips; setzt voraus,
        // dass Skeleton2Builder.Import gelaufen ist (auch von Skeleton2Builder.Build aufgerufen)
        public static void BuildSkeletonControllers(StringBuilder log)
        {
            var idleClip = Skeleton2Builder.Clip("Skeleton_Idle");
            foreach (var t in Types)
            {
                var clip = Skeleton2Builder.Clip("Skeleton2_" + t.Name + "_Attack");
                var run = Skeleton2Builder.Clip(t.Run);
                if (clip == null || run == null || idleClip == null)
                {
                    log.Append("\n  FEHLER: Clip fehlt für " + t.Name + " (Skeleton2_" + t.Name + "_Attack / " + t.Run + " / Skeleton_Idle)");
                    continue;
                }
                var ctrl = BuildController(t.Name + "Attack", run, t.RunSpeed, t.IdleFromAttack ? clip : idleClip, t.IdleFromAttack ? 0f : 1f, clip);
                log.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "\n  Controller {0}: Running {1} ×{2:0.##}, Idle {3}, Attack {4} ({5:0.00} s, Treffer {6:0.00} s)",
                    AssetDatabase.GetAssetPath(ctrl), run.name, t.RunSpeed, t.IdleFromAttack ? clip.name + " Frame 0" : idleClip.name, clip.name, clip.length, t.Windup));
                AssignController(t.Name, ctrl, log);
            }
            var boneClip = Skeleton2Builder.Clip("Skeleton2_BoneLord_Attack");
            if (boneClip != null) TuneBoneLordAttack(boneClip, log);
            else log.Append("\n  FEHLER: Clip Skeleton2_BoneLord_Attack fehlt");
        }

        // ---------------- 2. Controller ----------------

        // Running (Lauf-Clip, festes Tempo) ↔ Idle über Moving; Attack per Any-State-Trigger, am Ende zurück nach Running bzw.
        // Idle. Speed-Parameter nur für EnemyBrain vorhanden.
        private static AnimatorController BuildController(string name, AnimationClip running, float runSpeed, AnimationClip idleClip, float idleSpeed, AnimationClip attack)
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
            run.speed = runSpeed;
            var idle = sm.AddState("Idle", new Vector3(300, 120, 0));
            idle.motion = idleClip;
            idle.speed = idleSpeed; // Tempo 0 = steht zwischen zwei Hieben in der Ausgangspose des Angriffs
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

        // Grabspinne: seit 2026-10-18 eigenes Modell GraveSpider2 – Controller samt Biss (Tempo 1, Treffer bei 0,25 s =
        // SpiderWindup) kommt aus GraveSpiderBuilder; der alte Biss-Clip GraveSpider_Bite.anim (Rig „Fiese Spinne“) ist gelöscht.
        private static void TuneSpiderBite(StringBuilder log)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(SpiderController);
            log.AppendLine(ac != null && AssetDatabase.GetAssetPath(ac.layers[0].stateMachine.defaultState?.motion) == GraveSpiderBuilder.Fbx
                ? "  Grabspinne: eigenes Modell (GraveSpiderBuilder) – Biss-Tempo bleibt 1"
                : "  Grabspinne: Controller nicht von GraveSpiderBuilder gebaut – erst BuddyTD → Gegner → Grabspinne bauen");
        }

        // Knochenfürst: eigener Hieb-Clip in den Attack-State von BossBoneLordVisual.controller (Tempo 1, Ausstieg BoneLordExit);
        // der Rest des Boss-Controllers (BossBuilder) bleibt unangetastet
        private static void TuneBoneLordAttack(AnimationClip clip, StringBuilder log)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(BoneLordController);
            if (ac == null)
            {
                log.AppendLine("  Knochenfürst: " + BoneLordController + " fehlt (erst „Bosse neu bauen“)");
                return;
            }
            bool found = false;
            foreach (var s in ac.layers[0].stateMachine.states)
            {
                if (s.state.name != "Attack") continue;
                found = true;
                s.state.motion = clip;
                s.state.speed = 1f;
                foreach (var t in s.state.transitions)
                    if (t.hasExitTime) t.exitTime = BoneLordExit;
            }
            EditorUtility.SetDirty(ac);
            log.AppendLine(found
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Knochenfürst: Attack = {0} ({1:0.00} s, Tempo 1, Treffer {2:0.00} s, Ausstieg {3:0.00})", clip.name, clip.length, BoneLordWindup, BoneLordExit)
                : "  Knochenfürst: kein Attack-State in " + BoneLordController);
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
