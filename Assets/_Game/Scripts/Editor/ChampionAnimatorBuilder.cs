using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Baut KnightVisual.controller und ArcherVisual.controller aus PlayerVisual.controller (Basis-Layer Locomotion/Sprung
    // wird kopiert) und ergänzt die Aktions-Zustände der Champions. Menü: BuddyTD → Champion-Animatoren neu bauen.
    // Zustands- und Parameternamen = Trigger-Namen aus KnightKit/ArcherKit.GetAnimTrigger – Clips lassen sich per
    // State-Motion austauschen, ohne Code anzufassen.
    public static class ChampionAnimatorBuilder
    {
        private const string AnimDir = "Assets/_Game/Animations/";
        private const string Mixamo = "Assets/MixamoAnimations/";
        private const string H2Anim = "Assets/_Game/Animations/Heroes2/";
        private const string KnightFbx = "Assets/_Game/Models/Characters/KnightChampion.fbx";
        private const string ArcherFbx = "Assets/_Game/Models/Characters/ArcherChampion.fbx";
        private const string MeshyEnemy = "Assets/3D Models/Enemy/Meshy_Merged_Animations.fbx";

        [MenuItem("BuddyTD/Champion-Animatoren neu bauen")]
        public static void BuildAll()
        {
            // Basis zuerst: die Kopien (CopyBase) übernehmen den richtungsabhängigen Locomotion-Blend aus PlayerVisual
            ApplyLocomotion(AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimDir + "PlayerVisual.controller"));
            BuildKnight();
            BuildArcher();
            BuildBerserkerController();
            ApplyHolds();
            AssetDatabase.SaveAssets();
            Debug.Log("ChampionAnimatorBuilder: KnightVisual.controller, ArcherVisual.controller und BerserkerVisual.controller gebaut.");
        }

        // ---------------- Schwertkämpfer ----------------

        private static void BuildKnight()
        {
            var ac = CopyBase("KnightVisual");
            foreach (var n in new[] { "Slash1", "Slash2", "Slash3", "FlameWhirl", "FrostStrike", "Earthquake", "LightOath" })
                ac.AddParameter(n, AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Block", AnimatorControllerParameterType.Bool);

            var upper = UpperLayer(ac);
            var empty = upper.defaultState;
            // Schwerthiebe: eigene Blender-Clips (art-src/heroes2/17_knight_slash_anims.py, Echtzeit 30 fps, Treffer passend zu
            // KnightKit.SlashHitDelay/FinisherHitDelay)
            var s1 = Clip(H2Anim + "Knight2_Slash1.fbx", "Knight2_Slash1");
            var s2 = Clip(H2Anim + "Knight2_Slash2.fbx", "Knight2_Slash2");
            var s3 = Clip(H2Anim + "Knight2_Slash3.fbx", "Knight2_Slash3");
            Action(upper, empty, "Slash1", s1, "Slash1", 1f, 0.7f);
            Action(upper, empty, "Slash2", s2, "Slash2", 1f, 0.7f);
            Action(upper, empty, "Slash3", s3, "Slash3", 1f, 0.72f);
            // Kombo: der nächste Hieb darf das Ausblenden nach Empty unterbrechen (sonst wartet der Trigger bis zum Ende
            // der Überblendung und die Klinge hinkt Treffer/Bogen-VFX hinterher)
            foreach (var cs in upper.states)
                if (cs.state.name.StartsWith("Slash"))
                    foreach (var t in cs.state.transitions) t.interruptionSource = TransitionInterruptionSource.Destination;
            Action(upper, empty, "FrostStrike", Clip(KnightFbx, "Knight_FrostStrike"), "FrostStrike", 1.2f, 0.85f);
            Action(upper, empty, "LightOath", Clip(KnightFbx, "Knight_LightOath"), "LightOath", 1.1f, 0.85f);

            // Block: Bool-gesteuerte Schleife
            var block = upper.AddState("Block", new Vector3(300, 380, 0));
            block.motion = Clip(KnightFbx, "Knight_Block");
            var toBlock = upper.AddAnyStateTransition(block);
            toBlock.AddCondition(AnimatorConditionMode.If, 0f, "Block");
            toBlock.duration = 0.08f;
            toBlock.canTransitionToSelf = false;
            var fromBlock = block.AddTransition(empty);
            fromBlock.AddCondition(AnimatorConditionMode.IfNot, 0f, "Block");
            fromBlock.duration = 0.12f;
            fromBlock.hasExitTime = false;

            // Ganzkörper: Drehschlag und Sprung-Stampfer
            var full = FullBodyLayer(ac);
            var fEmpty = full.defaultState;
            Action(full, fEmpty, "FlameWhirl", Clip(KnightFbx, "Knight_FlameWhirl"), "FlameWhirl", 1.2f, 0.85f);
            Action(full, fEmpty, "Earthquake", Clip(KnightFbx, "Knight_Earthquake"), "Earthquake", 1.1f, 0.85f);
            EditorUtility.SetDirty(ac);
        }

        // ---------------- Bogenschütze ----------------

        private static void BuildArcher()
        {
            var ac = CopyBase("ArcherVisual");
            foreach (var n in new[] { "Shoot", "Roll", "ArrowRain", "PlaceTrap" })
                ac.AddParameter(n, AnimatorControllerParameterType.Trigger);

            var upper = UpperLayer(ac);
            var empty = upper.defaultState;
            var shootClip = Clip(Mixamo + "Erika Archer@Standing Aim Recoil.fbx", null);
            if (shootClip == null) shootClip = Clip(MeshyEnemy, "Archery_Shot_3");
            Action(upper, empty, "Shoot", shootClip, "Shoot", 1.6f, 0.7f);
            Action(upper, empty, "ArrowRain", Clip(ArcherFbx, "Archer_ArrowRain"), "ArrowRain", 1.2f, 0.85f);

            var full = FullBodyLayer(ac);
            var fEmpty = full.defaultState;
            Action(full, fEmpty, "Roll", Clip(ArcherFbx, "Archer_Roll"), "Roll", 1.0f, 0.9f);
            Action(full, fEmpty, "PlaceTrap", Clip(ArcherFbx, "Archer_PlaceTrap"), "PlaceTrap", 1.2f, 0.85f);
            EditorUtility.SetDirty(ac);
        }

        // ---------------- Berserker ----------------

        // Nur den Berserker-Controller bauen (+ Hand-/Arm-Layer), ohne die anderen Controller anzufassen
        [MenuItem("BuddyTD/Champions/Berserker-Animator bauen")]
        public static void BuildBerserker()
        {
            BuildBerserkerController();
            var spec = ChampionPlayerSetup.EffectiveSpec(ChampionPlayerSetup.Spec(ChampionClass.Berserker));
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(spec.Controller);
            if (ac != null && spec.UseHeroes2) ChampionGrip.ApplyHoldLayers(ac, spec.Hold, spec.ClipPrefix);
            AssetDatabase.SaveAssets();
        }

        // Zustände = Trigger aus BerserkerKit.GetAnimTrigger. Clips: eigene Berserker-Clips (Animations/Heroes2 oder im
        // Modell BerserkerChampion2.fbx), sonst Ritter-Clips als Platzhalter. Neu bauen tauscht die Clips automatisch.
        private static void BuildBerserkerController()
        {
            var ac = CopyBase("BerserkerVisual");
            foreach (var n in new[] { "Cleave1", "Cleave2", "Cleave3", "Charge", "IceBreak", "LeapSlam", "Roar" })
                ac.AddParameter(n, AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Spinning", AnimatorControllerParameterType.Bool);

            var upper = UpperLayer(ac);
            var empty = upper.defaultState;
            AddOr(upper, empty, "Cleave1", BerserkerClip("Berserker_Cleave1"), 1f, 0.7f, Clip(H2Anim + "Knight2_Slash1.fbx", "Knight2_Slash1"), 0.85f);
            AddOr(upper, empty, "Cleave2", BerserkerClip("Berserker_Cleave2"), 1f, 0.7f, Clip(H2Anim + "Knight2_Slash2.fbx", "Knight2_Slash2"), 0.85f);
            AddOr(upper, empty, "Cleave3", BerserkerClip("Berserker_Cleave3"), 1f, 0.75f, Clip(H2Anim + "Knight2_Slash3.fbx", "Knight2_Slash3"), 0.85f);
            foreach (var cs in upper.states)
                if (cs.state.name.StartsWith("Cleave"))
                    foreach (var t in cs.state.transitions) t.interruptionSource = TransitionInterruptionSource.Destination;
            AddOr(upper, empty, "Roar", BerserkerClip("Berserker_Roar"), 1f, 0.85f, Clip(KnightFbx, "Knight_LightOath"), 1.1f);

            var full = FullBodyLayer(ac);
            var fEmpty = full.defaultState;
            // Ansturm: ohne eigenen Clip leerer Zustand (Lauf-Animation läuft weiter)
            AddOr(full, fEmpty, "Charge", BerserkerClip("Berserker_Charge"), 1f, 0.9f, null, 1f);
            AddOr(full, fEmpty, "IceBreak", BerserkerClip("Berserker_GroundSlam"), 1f, 0.85f, Clip(KnightFbx, "Knight_FrostStrike"), 1.2f);
            AddOr(full, fEmpty, "LeapSlam", BerserkerClip("Berserker_LeapSlam"), 1f, 0.9f, Clip(KnightFbx, "Knight_Earthquake"), 0.8f);

            // Glutsturm: Bool-gesteuerte Schleife
            // Clip-Name aus art-src/heroes2/20_berserker_anims.py: "Berserker_SpinLoop" (ältere Schreibweise mit "_Spin_Loop" als Rückfall)
            var spinClip = BerserkerClip("Berserker_SpinLoop") ?? BerserkerClip("Berserker_Spin_Loop") ?? Clip(KnightFbx, "Knight_FlameWhirl");
            var spin = full.AddState("Spinning", new Vector3(300, 420, 0));
            spin.motion = spinClip;
            var toSpin = full.AddAnyStateTransition(spin);
            toSpin.AddCondition(AnimatorConditionMode.If, 0f, "Spinning");
            toSpin.duration = 0.08f;
            toSpin.canTransitionToSelf = false;
            var fromSpin = spin.AddTransition(fEmpty);
            fromSpin.AddCondition(AnimatorConditionMode.IfNot, 0f, "Spinning");
            fromSpin.duration = 0.15f;
            fromSpin.hasExitTime = false;
            ApplyBerserkerJumps(ac);
            EditorUtility.SetDirty(ac);
        }

        // Berserker-Sprung (2026-10-15, #174): eigene Bein-Clips Berserker_JumpStart/InAir/JumpLand statt der StarterAssets-
        // Clips im Base Layer (Rumpf/Arme überschreibt weiter der HoldArm-Layer). Die StarterAssets-Beine zogen die Knie in
        // das untere Stielende der Axt. Ohne eigene Clips bleibt der kopierte Sprung. Gibt die Zahl der getauschten States zurück.
        public static int ApplyBerserkerJumps(AnimatorController ac)
        {
            int n = 0;
            foreach (var kv in new[] { new[] { "JumpStart", "Berserker_JumpStart" }, new[] { "InAir", "Berserker_InAir" }, new[] { "JumpLand", "Berserker_JumpLand" } })
            {
                var clip = BerserkerClip(kv[1]);
                if (clip == null) continue;
                foreach (var cs in ac.layers[0].stateMachine.states)
                    if (cs.state.name == kv[0] && cs.state.motion != clip) { cs.state.motion = clip; n++; }
            }
            if (n > 0) EditorUtility.SetDirty(ac);
            return n;
        }

        // Eigener Clip, sonst Platzhalter (Tempo placeholderSpeed)
        private static void AddOr(AnimatorStateMachine sm, AnimatorState empty, string state, AnimationClip own, float speed, float exitTime, AnimationClip placeholder, float placeholderSpeed)
        {
            if (own != null) Action(sm, empty, state, own, state, speed, exitTime);
            else if (placeholder != null) Action(sm, empty, state, placeholder, state, placeholderSpeed, 0.85f);
            else
            {
                // Kein Clip: leerer Zustand (Bewegung der unteren Layer bleibt sichtbar), Trigger läuft ins Leere
                var s = sm.AddState(state, new Vector3(550, 60 * sm.states.Length, 0));
                var t = sm.AddAnyStateTransition(s);
                t.AddCondition(AnimatorConditionMode.If, 0f, state);
                t.duration = 0.06f;
                var back = s.AddTransition(empty);
                back.hasExitTime = true;
                back.exitTime = 0f;
                back.duration = 0.1f;
            }
        }

        // Berserker-Clip suchen: Animations/Heroes2/<name>.fbx, dann im Modell, dann irgendwo unter den Heroes2-Ordnern
        private static AnimationClip BerserkerClip(string name)
        {
            var c = Clip(H2Anim + name + ".fbx", null);
            if (c != null) return c;
            c = Clip("Assets/_Game/Models/Characters/Heroes2/BerserkerChampion2.fbx", name);
            if (c != null) return c;
            foreach (var g in AssetDatabase.FindAssets(name + " t:AnimationClip", new[] { "Assets/_Game/Animations", "Assets/_Game/Models/Characters/Heroes2" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                c = Clip(path, name);
                if (c != null) return c;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip ac && ac.name == name) return ac;
            }
            return null;
        }

        // ---------------- Hände/Arme (Heroes2) ----------------

        // Hands-/HoldArm-Layer für alle Champions mit Heroes2-Modell (Finger-Rig) setzen, bei Alt-Modellen entfernen.
        // Ändert nur die Layer, nicht die Aktions-States (GUIDs der Controller bleiben).
        public static void ApplyHolds()
        {
            foreach (var raw in ChampionPlayerSetup.Specs)
            {
                var spec = ChampionPlayerSetup.EffectiveSpec(raw); // Berserker ohne eigenes Modell: Platzhalter (Ritter)
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(spec.Controller);
                if (ac == null) continue;
                if (spec.UseHeroes2) ChampionGrip.ApplyHoldLayers(ac, spec.Hold, spec.ClipPrefix);
                else ChampionGrip.RemoveHoldLayers(ac);
            }
            AssetDatabase.SaveAssets();
        }

        // ---------------- Lauf-Animationen (richtungsabhängig) ----------------

        // Base-Layer-State "Locomotion" als 2D Freeform Directional über MoveX/MoveZ (Geschwindigkeit im Raum der Figur,
        // m/s; gesetzt von PlayerVisualAnimator): Idle in der Mitte, Geh-Ring bei 2,5, Renn-Ring bei 6 (vorwärts,
        // rückwärts, links, rechts). Clips: Animations/Heroes2/Loco_{Walk,Run}_{F,B,L,R}.fbx (eigene Blender-Clips,
        // art-src/heroes2/22_locomotion_anims.py). Fehlt ein Richtungs-Clip → Vorwärts-Clip des Rings, fehlt auch der →
        // bisheriger Fremd-Clip (alle Richtungen gleich = altes Verhalten). Erneut ausführen, sobald die Clips da sind.
        public const string MoveXParam = "MoveX";
        public const string MoveZParam = "MoveZ";
        public const float WalkRing = 2.5f;
        public const float RunRing = 6f;
        // Ab diesem Tempo ist der Idle-Anteil 0 (darunter 1D-Überblendung Idle → Gehen)
        public const float MoveThreshold = 0.8f;
        // Tempo-Multiplikator des Locomotion-States (PlayerVisualAnimator.LocoSpeedFor): unter dem Geh-Ring und über dem
        // Renn-Ring läuft der Zyklus proportional zum Tempo, damit die Schrittlänge passt
        public const string LocoSpeedParam = "LocoSpeed";
        private static readonly string[] LocoDirs = { "F", "B", "L", "R" };
        private static readonly Vector2[] LocoDirVec = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        // Wiedergabe-Tempo je Richtungs-Clip (ChildMotion.timeScale, Reihenfolge F, B, L, R) – zum Abgleich der Schrittlänge
        // mit der Laufgeschwindigkeit (Fußrutschen); nach Änderung Menü erneut ausführen
        public static readonly float[] WalkTimeScale = { 1f, 1f, 1f, 1f };
        public static readonly float[] RunTimeScale = { 1f, 1f, 1f, 1f };

        // Bisherige Clips (1D-Blend über Speed) – Rückfall, solange die Loco_*-Clips fehlen
        private const string LegacyIdle = "Assets/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx";
        private const string LegacyWalk = "Assets/3D Models/Elemental/Light/Animation_Walking_withSkin.fbx";
        private const string LegacyRun = "Assets/3D Models/Elemental/Light/Animation_Running_withSkin.fbx";

        // Alle Champion-Controller (Magier = PlayerVisual) an Ort und Stelle umbauen: GUIDs, andere Layer und States bleiben
        [MenuItem("BuddyTD/Champions/Lauf-Animationen einbauen")]
        public static void ApplyLocomotionAll()
        {
            var done = new System.Collections.Generic.HashSet<string>();
            var paths = new System.Collections.Generic.List<string> { AnimDir + "PlayerVisual.controller" };
            foreach (var spec in ChampionPlayerSetup.Specs) paths.Add(spec.Controller);
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path) || !done.Add(path)) continue;
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ac == null) { Debug.LogWarning("ChampionAnimatorBuilder: Controller fehlt: " + path); continue; }
                ApplyLocomotion(ac);
            }
            AssetDatabase.SaveAssets();
            string torso = ApplyTorsoLayers();
            Debug.Log("ChampionAnimatorBuilder: Lauf-Animationen eingebaut (" + done.Count + " Controller) – " + LocoReport() + "; Rumpf-Ausgleich: " + torso);
        }

        // Parameter MoveX/MoveZ ergänzen und den Locomotion-Blend ersetzen (idempotent; der Blend-Baum bleibt dasselbe
        // Unter-Asset, nur Typ/Kinder ändern sich)
        public static void ApplyLocomotion(AnimatorController ac)
        {
            if (ac == null) return;
            EnsureFloat(ac, MoveXParam);
            EnsureFloat(ac, MoveZParam);
            EnsureFloat(ac, LocoSpeedParam, 1f);

            AnimatorState loco = null;
            foreach (var cs in ac.layers[0].stateMachine.states)
                if (cs.state.name == "Locomotion") loco = cs.state;
            if (loco == null) { Debug.LogWarning("ChampionAnimatorBuilder: kein Locomotion-State in " + ac.name); return; }

            string acPath = AssetDatabase.GetAssetPath(ac);
            var bt = loco.motion as BlendTree;
            if (bt == null || AssetDatabase.GetAssetPath(bt) != acPath)
            {
                bt = new BlendTree { name = "Locomotion", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(bt, ac);
            }
            // Wurzel: 1D über Speed (Idle 0 → Bewegung ab Geh-Ring). Idle steckt bewusst NICHT im 2D-Blend: dort bekäme es auf
            // den Diagonalen des Geh-Rings ~12 % Gewicht, und weil der Blend die Zykluslänge gewichtet mittelt (Idle 4,9 s,
            // Lauf-Clips 0,6 s), lief der Schritt dort fast halb so schnell → starkes Fußrutschen.
            bt.blendType = BlendTreeType.Simple1D;
            bt.blendParameter = "Speed";
            bt.useAutomaticThresholds = false;

            BlendTree move = null;
            foreach (var ch in bt.children)
                if (ch.motion is BlendTree sub && AssetDatabase.GetAssetPath(sub) == acPath) move = sub;
            if (move == null)
            {
                move = new BlendTree { name = "Move", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(move, ac);
            }
            move.name = "Move";
            move.blendType = BlendTreeType.FreeformDirectional2D;
            move.blendParameter = MoveXParam;
            move.blendParameterY = MoveZParam;
            move.useAutomaticThresholds = false;
            var children = new System.Collections.Generic.List<ChildMotion>();
            AddRing(children, "Walk", WalkRing, LegacyWalk, WalkTimeScale);
            AddRing(children, "Run", RunRing, LegacyRun, RunTimeScale);
            move.children = children.ToArray();
            EditorUtility.SetDirty(move);

            bt.children = new[]
            {
                new ChildMotion { motion = Clip(LegacyIdle, "Idle") ?? Clip(LegacyIdle, null), threshold = 0f, timeScale = 1f, directBlendParameter = "Blend" },
                new ChildMotion { motion = move, threshold = MoveThreshold, timeScale = 1f, directBlendParameter = "Blend" },
            };

            loco.motion = bt;
            loco.speedParameter = LocoSpeedParam;
            loco.speedParameterActive = true;
            EditorUtility.SetDirty(bt);
            EditorUtility.SetDirty(ac);
        }

        // Seitwärts-Clips drehen die Hüfte ~50° in Laufrichtung (Beine laufen „vorwärts“ zur Hüfte). Mit dem Rückwärts-Clip
        // (Hüfte gerade, Beine laufen „rückwärts“ zur Hüfte) überblendet heben sich die Beinbewegungen in der Muskel-Mischung
        // auf → auf den hinteren Diagonalen rutschten die Füße fast mit voller Geschwindigkeit. Darum hat jeder Ring hinten
        // zwei zusätzliche Seitwärts-Punkte: der GEGENSEITIGE Seitwärts-Clip rückwärts abgespielt (timeScale < 0; z. B. „nach
        // rechts“ = Loco_*_L rückwärts: Hüfte zeigt nach vorn-links, Beine laufen rückwärts zur Hüfte – passt zum Rückwärts-
        // Clip). Sie liegen BackStrafeAngle Grad hinter der Seitachse; dazwischen wechselt die Hüfte die Seite.
        public static float BackStrafeAngle = 10f;
        // Phasenversatz (ChildMotion.cycleOffset, normiert) der rückwärts gespielten Seitwärts-Clips, damit die Standphase des
        // linken Fußes mit der im Rückwärts-Clip zusammenfällt (gemessen: Mitte Standphase links Walk_B ≈ f5, Walk_L/R ≈ f5,
        // Run_B ≈ f2,75, Run_L/R ≈ f2,25 von 18; rückwärts abgespielt liegt sie bei 18 − c → Versatz (18 − c_LR − c_B) / 18)
        public static float WalkBackStrafeCycleOffset = 0.44f;
        public static float RunBackStrafeCycleOffset = 0.72f;

        private static void AddRing(System.Collections.Generic.List<ChildMotion> children, string gait, float radius, string legacy, float[] timeScale)
        {
            var fwd = LocoClip(gait, "F") ?? Clip(legacy, null);
            var clips = new AnimationClip[LocoDirs.Length];
            var ts = new float[LocoDirs.Length];
            for (int i = 0; i < LocoDirs.Length; i++)
            {
                clips[i] = LocoClip(gait, LocoDirs[i]) ?? fwd;
                if (clips[i] == null) Debug.LogWarning("ChampionAnimatorBuilder: kein Lauf-Clip für " + gait + "_" + LocoDirs[i]);
                ts[i] = timeScale != null && i < timeScale.Length && timeScale[i] > 0f ? timeScale[i] : 1f;
                children.Add(new ChildMotion { motion = clips[i], position = LocoDirVec[i] * radius, timeScale = ts[i], directBlendParameter = "Blend" });
            }
            // hintere Seitwärts-Punkte (nur mit eigenen Clips; Reihenfolge LocoDirs: F, B, L, R)
            var l = LocoClip(gait, "L");
            var r = LocoClip(gait, "R");
            if (l == null || r == null || BackStrafeAngle <= 0f) return;
            float a = BackStrafeAngle * Mathf.Deg2Rad;
            float off = gait == "Walk" ? WalkBackStrafeCycleOffset : RunBackStrafeCycleOffset;
            var backRight = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * radius; // rechts, etwas nach hinten
            var backLeft = new Vector2(-Mathf.Cos(a), -Mathf.Sin(a)) * radius;
            children.Add(new ChildMotion { motion = l, position = backRight, timeScale = -ts[2], cycleOffset = off, directBlendParameter = "Blend" });
            children.Add(new ChildMotion { motion = r, position = backLeft, timeScale = -ts[3], cycleOffset = off, directBlendParameter = "Blend" });
        }

        // ChampionVisual.SpineOverrideLayers/FullBodyLayers im Player-Prefab aus den Layer-Masken der Controller setzen:
        // Maske mit Rumpf (Body) ohne Beine → Rumpf-Layer (Gegendrehung nötig), keine Maske / mit Beinen → Ganzkörper
        public static string ApplyTorsoLayers()
        {
            const string prefabPath = "Assets/_Game/Prefabs/Player.prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            var report = new System.Text.StringBuilder();
            bool changed = false;
            try
            {
                foreach (var cv in root.GetComponentsInChildren<ChampionVisual>(true))
                {
                    var anim = cv.Animator != null ? cv.Animator : cv.GetComponentInChildren<Animator>(true);
                    var ac = ChampionPoseSampler.ControllerOf(anim);
                    if (ac == null) continue;
                    var spine = new System.Collections.Generic.List<string>();
                    var full = new System.Collections.Generic.List<string>();
                    var layers = ac.layers;
                    for (int i = 1; i < layers.Length; i++)
                    {
                        var m = layers[i].avatarMask;
                        bool legs = m == null || m.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg) || m.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg);
                        bool body = m == null || m.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body);
                        if (legs) full.Add(layers[i].name);
                        else if (body) spine.Add(layers[i].name);
                    }
                    if (!SameList(cv.SpineOverrideLayers, spine) || !SameList(cv.FullBodyLayers, full))
                    {
                        cv.SpineOverrideLayers = spine.ToArray();
                        cv.FullBodyLayers = full.ToArray();
                        EditorUtility.SetDirty(cv);
                        changed = true;
                    }
                    report.Append(cv.name + " [" + string.Join(",", spine) + " | " + string.Join(",", full) + "] ");
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return report.ToString() + (changed ? "(Prefab aktualisiert)" : "(unverändert)");
        }

        private static bool SameList(string[] a, System.Collections.Generic.List<string> b)
        {
            if (a == null) return b.Count == 0;
            if (a.Length != b.Count) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Eigener Richtungs-Clip, z. B. Loco_Walk_L (null, solange die FBX fehlt)
        private static AnimationClip LocoClip(string gait, string dir)
        {
            string name = "Loco_" + gait + "_" + dir;
            return Clip(H2Anim + name + ".fbx", name) ?? Clip(H2Anim + name + ".fbx", null);
        }

        private static string LocoReport()
        {
            int found = 0;
            var missing = new System.Collections.Generic.List<string>();
            foreach (var g in new[] { "Walk", "Run" })
                foreach (var d in LocoDirs)
                {
                    if (LocoClip(g, d) != null) found++;
                    else missing.Add("Loco_" + g + "_" + d);
                }
            return found + "/8 Loco-Clips" + (missing.Count > 0 ? ", Rückfall für: " + string.Join(", ", missing) : "");
        }

        private static void EnsureFloat(AnimatorController ac, string name, float defaultValue = 0f)
        {
            var ps = ac.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == name)
                {
                    if (ps[i].defaultFloat != defaultValue) { ps[i].defaultFloat = defaultValue; ac.parameters = ps; }
                    return;
                }
            ac.AddParameter(new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Float, defaultFloat = defaultValue });
        }

        // ---------------- Helfer ----------------

        // Kopie von PlayerVisual.controller (Base Layer + UpperBody-Maske), ohne den Magier-Cast
        private static AnimatorController CopyBase(string name)
        {
            string path = AnimDir + name + ".controller";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CopyAsset(AnimDir + "PlayerVisual.controller", path);
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            ChampionGrip.RemoveHoldLayers(ac); // Magier-Hände/-Arm nicht mitkopieren (ApplyHolds setzt eigene)

            var upper = UpperLayer(ac);
            foreach (var s in upper.states)
                if (s.state.name == "Cast") upper.RemoveState(s.state);
            foreach (var t in upper.anyStateTransitions)
                if (t.destinationState == null) upper.RemoveAnyStateTransition(t);
            for (int i = ac.parameters.Length - 1; i >= 0; i--)
                if (ac.parameters[i].name == "Cast") ac.RemoveParameter(i);
            return ac;
        }

        private static AnimatorStateMachine UpperLayer(AnimatorController ac)
        {
            foreach (var l in ac.layers) if (l.name == "UpperBody") return l.stateMachine;
            return ac.layers[ac.layers.Length - 1].stateMachine;
        }

        // Zweiter Aktions-Layer ohne Maske (Rolle, Drehschlag …) – überschreibt den ganzen Körper
        private static AnimatorStateMachine FullBodyLayer(AnimatorController ac)
        {
            ac.AddLayer("FullBody");
            var layers = ac.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            ac.layers = layers;
            var sm = layer.stateMachine;
            var empty = sm.AddState("Empty", new Vector3(300, 0, 0));
            sm.defaultState = empty;
            return sm;
        }

        // Any State → Aktion (Trigger), nach exitTime zurück nach Empty
        private static void Action(AnimatorStateMachine sm, AnimatorState empty, string state, Motion clip, string trigger, float speed, float exitTime)
        {
            var s = sm.AddState(state, new Vector3(550, 60 * sm.states.Length, 0));
            s.motion = clip;
            s.speed = speed;
            var t = sm.AddAnyStateTransition(s);
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            t.duration = 0.06f;
            t.canTransitionToSelf = true;
            var back = s.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = exitTime;
            back.duration = 0.18f;
            if (clip == null) Debug.LogWarning("ChampionAnimatorBuilder: kein Clip für " + state);
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
    }
}
