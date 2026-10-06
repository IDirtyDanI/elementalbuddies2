using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    public enum HandPose { Animated, Relaxed, Fist }

    // Haltung der Hände/Arme eines Champions (Heroes2-Modelle mit Fingerknochen).
    // - Layer „Hands“ (letzter Layer, Maske nur Finger): konstanter Pose-Clip mit Finger-Muskeln. Weil er nach allen
    //   Aktions-Layern kommt, bleibt die Faust in jeder Animation (Locomotion, Cast, Slash, Rolle …) geschlossen.
    // - Layer „HoldArm“ (direkt nach dem Base Layer, Maske z. B. rechter Arm): Trage-Haltung für die Waffe während
    //   Locomotion/Sprung; Aktions-Layer (UpperBody/FullBody) überschreiben ihn beim Angreifen.
    public class HoldSpec
    {
        public HandPose Left = HandPose.Relaxed;
        public HandPose Right = HandPose.Relaxed;
        // Muskelwerte (Clip-Kurvennamen, z. B. "Right Arm Down-Up") – leer = kein HoldArm-Layer
        public Dictionary<string, float> ArmPose;
        public AvatarMaskBodyPart[] ArmParts;

        // Laufzeit-Komponente ChampionWeaponHold (IK-Tragehaltung + Freiraum Kopf/Hut); null = keine.
        // IK2: zweite Hand (z. B. Ritter: Schwert rechts + Schild links) – eigene Komponente
        public HoldIK IK, IK2;
    }

    public class HoldIK
    {
        public bool RightHand = true;
        public bool Carry = true, IdleCarry = true, HandOnly;
        public Vector3 GripOffset, WeaponAxis = Vector3.up, ElbowHint = new Vector3(0.3f, -0.3f, -0.25f);
        public Vector3 HipsFollow = new Vector3(0.4f, 1f, 0.4f);
        public Vector2 WristAngle = new Vector2(70f, 125f);
        public ChampionWeaponHold.Clearance[] Clearances;
        public float WeaponTop, WeaponBottom, WeaponRadius = 0.04f;
        public Vector3 WeaponForward;
        public bool FollowHipsYaw;
        public HumanBodyBones FollowBone = HumanBodyBones.Hips;
        public ChampionWeaponHold.StateTarget[] States;
        public Vector3[] Segments;
        public float GroundClearance = 0.03f;
        public string[] ActionLayers; // null = UpperBody/FullBody (Champions); Händler: Base Layer

        public void ApplyTo(ChampionWeaponHold h)
        {
            h.RightHand = RightHand;
            h.Carry = Carry;
            h.IdleCarry = IdleCarry;
            h.HandOnly = HandOnly;
            h.GripOffset = GripOffset;
            h.HipsFollow = HipsFollow;
            h.WristAngle = WristAngle;
            h.WeaponAxis = WeaponAxis;
            h.ElbowHint = ElbowHint;
            h.Clearances = Clearances ?? new ChampionWeaponHold.Clearance[0];
            h.WeaponTop = WeaponTop;
            h.WeaponBottom = WeaponBottom;
            h.WeaponRadius = WeaponRadius;
            h.WeaponForward = WeaponForward;
            h.FollowHipsYaw = FollowHipsYaw;
            h.FollowBone = FollowBone;
            h.States = States ?? new ChampionWeaponHold.StateTarget[0];
            h.Segments = Segments ?? new Vector3[0];
            h.GroundClearance = GroundClearance;
            if (ActionLayers != null) h.ActionLayers = ActionLayers;
        }
    }

    public static class ChampionGrip
    {
        public const string Dir = "Assets/_Game/Animations/Heroes2/";
        public const string HandsLayer = "Hands";
        public const string HoldLayer = "HoldArm";

        private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

        // Faust um einen ~3 cm dicken Griff (−1 = ganz eingerollt, +1 = gestreckt). Feinjustiert per Clipping-Prüfung/Renders.
        public static float FistCurl1 = -0.55f, FistCurl2 = -0.8f, FistCurl3 = -0.55f;
        public static float ThumbStretch1 = -0.6f, ThumbSpread = -0.2f, ThumbStretch2 = -0.4f, ThumbStretch3 = -0.4f;
        // Freie Hand: leicht gekrümmt
        public static float RelaxCurl1 = 0.3f, RelaxCurl2 = 0.2f, RelaxCurl3 = 0.3f;

        // ---------------- Muskel-Sätze ----------------

        public static void AddHand(Dictionary<string, float> m, bool right, HandPose pose)
        {
            if (pose == HandPose.Animated) return;
            string s = right ? "RightHand." : "LeftHand.";
            bool fist = pose == HandPose.Fist;
            for (int i = 0; i < Fingers.Length; i++)
            {
                string f = s + Fingers[i];
                m[f + ".1 Stretched"] = fist ? FistCurl1 : RelaxCurl1;
                m[f + ".2 Stretched"] = fist ? FistCurl2 : RelaxCurl2;
                m[f + ".3 Stretched"] = fist ? FistCurl3 : RelaxCurl3;
                // Faust: Finger eng beieinander; locker: leicht gefächert (Zeigefinger nach außen, kleiner Finger nach innen)
                m[f + ".Spread"] = fist ? 0f : (i == 0 ? 0.15f : i == 3 ? -0.1f : 0f);
            }
            m[s + "Thumb.1 Stretched"] = fist ? ThumbStretch1 : 0f;
            m[s + "Thumb.Spread"] = fist ? ThumbSpread : 0.2f;
            m[s + "Thumb.2 Stretched"] = fist ? ThumbStretch2 : 0.2f;
            m[s + "Thumb.3 Stretched"] = fist ? ThumbStretch3 : 0.2f;
        }

        // ---------------- Assets ----------------

        public static void EnsureDir()
        {
            if (!AssetDatabase.IsValidFolder(Dir.TrimEnd('/'))) AssetDatabase.CreateFolder("Assets/_Game/Animations", "Heroes2");
        }

        // Konstanter Humanoid-Pose-Clip aus Muskelwerten (überschreibt vorhandene Kurven, GUID bleibt)
        public static AnimationClip PoseClip(string path, Dictionary<string, float> muscles)
        {
            EnsureDir();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            else clip.ClearCurves();
            foreach (var kv in muscles)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), AnimationCurve.Constant(0f, 1f, kv.Value));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        public static AvatarMask Mask(string path, params AvatarMaskBodyPart[] parts)
        {
            EnsureDir();
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, path);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var p in parts) mask.SetHumanoidBodyPartActive(p, true);
            mask.transformCount = 0;
            EditorUtility.SetDirty(mask);
            return mask;
        }

        // ---------------- Animator-Layer ----------------

        public static void RemoveHoldLayers(AnimatorController ac)
        {
            for (int i = ac.layers.Length - 1; i >= 0; i--)
                if (ac.layers[i].name == HandsLayer || ac.layers[i].name == HoldLayer) ac.RemoveLayer(i);
        }

        // Fügt HoldArm (nach Base) und Hands (am Ende) hinzu; idempotent. prefix = Dateiname-Präfix der Clips (z. B. "Mage")
        public static void ApplyHoldLayers(AnimatorController ac, HoldSpec spec, string prefix)
        {
            RemoveHoldLayers(ac);
            if (spec == null) return;

            if (spec.ArmPose != null && spec.ArmPose.Count > 0)
            {
                var clip = PoseClip(Dir + prefix + "_HoldArm.anim", spec.ArmPose);
                var mask = Mask(Dir + prefix + "_HoldArm.mask", spec.ArmParts);
                AddPoseLayer(ac, HoldLayer, clip, mask);
                // direkt hinter den Base Layer schieben, damit Aktions-Layer ihn überschreiben
                var layers = ac.layers;
                var hold = layers[layers.Length - 1];
                for (int i = layers.Length - 1; i > 1; i--) layers[i] = layers[i - 1];
                layers[1] = hold;
                ac.layers = layers;
            }

            var hands = new Dictionary<string, float>();
            AddHand(hands, false, spec.Left);
            AddHand(hands, true, spec.Right);
            if (hands.Count > 0)
            {
                var parts = new List<AvatarMaskBodyPart>();
                if (spec.Left != HandPose.Animated) parts.Add(AvatarMaskBodyPart.LeftFingers);
                if (spec.Right != HandPose.Animated) parts.Add(AvatarMaskBodyPart.RightFingers);
                AddPoseLayer(ac, HandsLayer, PoseClip(Dir + prefix + "_Hands.anim", hands), Mask(Dir + prefix + "_Hands.mask", parts.ToArray()));
            }
            EditorUtility.SetDirty(ac);
        }

        private static void AddPoseLayer(AnimatorController ac, string name, AnimationClip clip, AvatarMask mask)
        {
            ac.AddLayer(name);
            var layers = ac.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.avatarMask = mask;
            ac.layers = layers;
            var state = layer.stateMachine.AddState("Pose", new Vector3(300, 0, 0));
            state.motion = clip;
            state.writeDefaultValues = true;
            layer.stateMachine.defaultState = state;
        }

        // ---------------- Griff-Geometrie ----------------

        // Bestimmt in der aktuell gesampelten Pose die „Röhre“ der Faust: Achse = kleiner Finger → Zeigefinger
        // (Richtung Daumenseite), Mitte = Kreismittelpunkt durch die Gelenke des Mittelfingers (in der Ebene ⟂ Achse).
        // radius = Abstand der Finger-Mittellinie zur Achse (Griffradius ≈ radius − halbe Fingerdicke).
        public static bool FitGrip(Animator a, bool right, out Vector3 center, out Vector3 axis, out float radius)
        {
            center = axis = Vector3.zero;
            radius = 0f;
            var idx = a.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            var lit = a.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            var p1 = a.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            var p2 = a.GetBoneTransform(right ? HumanBodyBones.RightMiddleIntermediate : HumanBodyBones.LeftMiddleIntermediate);
            var p3 = a.GetBoneTransform(right ? HumanBodyBones.RightMiddleDistal : HumanBodyBones.LeftMiddleDistal);
            if (idx == null || lit == null || p1 == null || p2 == null || p3 == null) return false;
            axis = (idx.position - lit.position).normalized;
            Vector3 o = p1.position;
            Vector3 a2 = Vector3.ProjectOnPlane(p2.position - o, axis), a3 = Vector3.ProjectOnPlane(p3.position - o, axis);
            // Umkreismittelpunkt von 0, a2, a3
            Vector3 n = Vector3.Cross(a2, a3);
            float d = 2f * n.sqrMagnitude;
            if (d < 1e-10f) return false;
            Vector3 c = (Vector3.Cross(n, a2) * a3.sqrMagnitude + Vector3.Cross(a3, n) * a2.sqrMagnitude) / d;
            center = o + c;
            radius = c.magnitude;
            return true;
        }
    }
}
