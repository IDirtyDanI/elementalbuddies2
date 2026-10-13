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
            // KnightKit.SlashHitDelay/FinisherHitDelay); Mixamo-Clips nur noch als Rückfall, falls die FBX fehlen
            var s1 = Clip(H2Anim + "Knight2_Slash1.fbx", "Knight2_Slash1");
            var s2 = Clip(H2Anim + "Knight2_Slash2.fbx", "Knight2_Slash2");
            var s3 = Clip(H2Anim + "Knight2_Slash3.fbx", "Knight2_Slash3");
            if (s1 != null) Action(upper, empty, "Slash1", s1, "Slash1", 1f, 0.7f);
            else Action(upper, empty, "Slash1", Clip(Mixamo + "X Bot@Stable Sword Inward Slash (1).fbx", null), "Slash1", 1.9f, 0.55f);
            if (s2 != null) Action(upper, empty, "Slash2", s2, "Slash2", 1f, 0.7f);
            else Action(upper, empty, "Slash2", Clip(Mixamo + "X Bot@Stable Sword Outward Slash (1).fbx", null), "Slash2", 1.9f, 0.55f);
            if (s3 != null) Action(upper, empty, "Slash3", s3, "Slash3", 1f, 0.72f);
            else Action(upper, empty, "Slash3", Clip(Mixamo + "X Bot@Thrust Slash (1).fbx", null), "Slash3", 2.4f, 0.5f);
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
            EditorUtility.SetDirty(ac);
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
