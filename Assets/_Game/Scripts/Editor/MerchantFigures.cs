using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Händler-Figuren der Stände (Heroes2-Modelle Merchant2_<Typ>.fbx, 1,80 m, Finger-Rig) mit Requisite in der Hand:
    // Schmied Hammer rechts, Feuerhändlerin Glutkugel auf der linken Handfläche, Eishändler Kristall links,
    // Druide Stab rechts, Lichtpriester Buch links. Wie bei den Champions:
    // - ChampionVisual hängt die Requisite an den Handknochen (Griff-Sockel aus der gesampelten Faust bzw. Handflächenmitte),
    // - Override-Controller je Händler ersetzt den Pose-Clip des Layers „Hands“ (Faust/locker),
    // - ChampionWeaponHold hält die Requisite im Idle per IK in einer Tragehaltung und hält sie in Winken/Jubel aus dem Körper.
    // Aufruf aus MerchantSetup.BuildStall bzw. Menü BuddyTD → Händler → Figuren aktualisieren (nur die Figuren, Szene wird nicht gespeichert).
    public static class MerchantFigures
    {
        public const string Folder = "Assets/_Game/Models/Characters/Heroes2/Merchants";
        public const string PropFolder = "Assets/_Game/Models/Characters/Heroes2/Weapons/";
        // Heroes2-Figuren sind 1,80 m groß (alte Händler: 2,11 m × 0,76 ≈ 1,6 m) – gleiche Größe wie die Champions
        public const float Scale = 1f;
        public const string OverrideDir = "Assets/_Game/Animations/Heroes2/";

        public class FigureSpec
        {
            public MerchantKind Kind;
            public ChampionPlayerSetup.WeaponSpec Right, Left;
            public HoldSpec Hold;
        }

        public static string ModelPath(MerchantKind k) { return Folder + "/Merchant2_" + k + ".fbx"; }
        public static bool Available(MerchantKind k) { return File.Exists(ModelPath(k)); }

        private static readonly string[] BaseLayer = { "Base Layer" };

        public static FigureSpec Spec(MerchantKind k)
        {
            foreach (var f in Specs()) if (f.Kind == k) return f;
            return null;
        }

        // Werte: Tragehaltung im Idle (Modell-Achsen, x zur Requisiten-Hand positiv; WeaponForward = Requisiten-+Z = Handflächenseite).
        // Winken (rechter Arm) / Jubel (beide Arme hoch): Animation führt, Freiraum-Kapseln halten die Requisite aus Körper/Kopf;
        // wer links hält, behält beim Winken die Tragehaltung.
        public static FigureSpec[] Specs()
        {
            return new[]
            {
                // Schmied: Hammer rechts vor der Hüfte, Kopf oben, Bahn nach vorn
                new FigureSpec
                {
                    Kind = MerchantKind.Waffen,
                    Right = new ChampionPlayerSetup.WeaponSpec { Prefab = PropFolder + "W2_SmithHammer.fbx", Roll = 180f },
                    Hold = new HoldSpec
                    {
                        Right = HandPose.Fist, Left = HandPose.Relaxed,
                        IK = Carry(true, new Vector3(0.24f, 1.0f, 0.28f), new Vector3(0.05f, 0.85f, 0.5f), new Vector3(-1f, 0f, 0f), 0.31f, 0.22f, 0.045f, false, false),
                    },
                },
                // Feuerhändlerin: Glutkugel auf der linken Handfläche (Handfläche oben, Unterarm vor dem Körper)
                new FigureSpec
                {
                    Kind = MerchantKind.Feuer,
                    Left = new ChampionPlayerSetup.WeaponSpec { Prefab = PropFolder + "W2_EmberOrb.fbx", Grip = false, Palm = true },
                    Hold = new HoldSpec
                    {
                        Right = HandPose.Relaxed, Left = HandPose.Relaxed,
                        IK = Carry(false, new Vector3(0.2f, 1.05f, 0.32f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), 0.07f, 0.07f, 0.07f, true, true,
                            new[] { new Vector3(0f, 0f, 0.07f), new Vector3(0f, 0f, 0.14f) }),
                    },
                },
                // Eishändler: Kristall links, aufrecht vor der Hüfte
                new FigureSpec
                {
                    Kind = MerchantKind.Eis,
                    Left = new ChampionPlayerSetup.WeaponSpec { Prefab = PropFolder + "W2_IceCrystal.fbx", Roll = 180f },
                    Hold = new HoldSpec
                    {
                        Right = HandPose.Relaxed, Left = HandPose.Fist,
                        IK = Carry(false, new Vector3(0.22f, 1.02f, 0.3f), new Vector3(0f, 1f, 0.2f), new Vector3(-1f, 0f, 0f), 0.37f, 0.07f, 0.06f, false, true),
                    },
                },
                // Druide: Stab rechts, senkrecht neben dem Körper (wie der Magier), Fuß außerhalb der Robe
                new FigureSpec
                {
                    Kind = MerchantKind.Erde,
                    Right = new ChampionPlayerSetup.WeaponSpec { Prefab = PropFolder + "W2_DruidStaff.fbx", Roll = 180f },
                    Hold = new HoldSpec
                    {
                        Right = HandPose.Fist, Left = HandPose.Relaxed,
                        IK = Carry(true, new Vector3(0.32f, 1.1f, 0.3f), new Vector3(-0.05f, 1f, 0f), Vector3.zero, 0.52f, 0.98f, 0.05f, true, false),
                    },
                },
                // Lichtpriester: Buch links vor dem Bauch, Rücken senkrecht, Buchblock zum Körper
                new FigureSpec
                {
                    Kind = MerchantKind.Licht,
                    Left = new ChampionPlayerSetup.WeaponSpec { Prefab = PropFolder + "W2_HolyBook.fbx", Roll = 180f },
                    Hold = new HoldSpec
                    {
                        Right = HandPose.Relaxed, Left = HandPose.Fist,
                        IK = Carry(false, new Vector3(0.2f, 1.05f, 0.28f), new Vector3(0f, 1f, 0f), new Vector3(-1f, 0f, 0f), 0.15f, 0.15f, 0.04f, true, true,
                            new[] { new Vector3(0f, -0.14f, 0.02f), new Vector3(0f, 0.14f, 0.02f), new Vector3(0f, -0.14f, 0.15f), new Vector3(0f, 0.14f, 0.15f) }),
                    },
                },
            };
        }

        // Tragehaltung im Idle (+ beim Winken, wenn die Requisite links ist); robe = Robe/Mantel (breitere Bein-Kapseln)
        private static HoldIK Carry(bool right, Vector3 grip, Vector3 axis, Vector3 fwd, float top, float bottom, float radius, bool robe, bool keepOnWave, Vector3[] segments = null)
        {
            var states = new List<ChampionWeaponHold.StateTarget> { new ChampionWeaponHold.StateTarget { State = "Idle", UseCarry = true } };
            if (keepOnWave) states.Add(new ChampionWeaponHold.StateTarget { State = "Winken", UseCarry = true });
            return new HoldIK
            {
                RightHand = right, ActionLayers = BaseLayer, FollowBone = HumanBodyBones.Hips, HipsFollow = new Vector3(0.4f, 1f, 0.4f),
                GripOffset = grip, WeaponAxis = axis, WeaponForward = fwd, ElbowHint = new Vector3(0.3f, -0.5f, -0.3f), WristAngle = new Vector2(0f, 180f),
                WeaponTop = top, WeaponBottom = bottom, WeaponRadius = radius, GroundClearance = 0.03f, Segments = segments,
                States = states.ToArray(),
                Clearances = Clearances(right, robe),
            };
        }

        // Freiraum-Kapseln (wie beim Magier: Robe um die Beine breiter)
        private static ChampionWeaponHold.Clearance[] Clearances(bool right, bool robe)
        {
            var other = right ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            var otherLow = right ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            var own = right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
            var ownLow = right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
            float upper = robe ? 0.25f : 0.15f, lower = robe ? 0.3f : 0.1f;
            return new[]
            {
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.Hips, To = HumanBodyBones.Chest, Radius = 0.22f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.Chest, To = HumanBodyBones.Neck, Radius = 0.22f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.Head, Height = 0.4f, Radius = 0.2f },
                new ChampionWeaponHold.Clearance { From = own, To = ownLow, Radius = 0.12f },
                new ChampionWeaponHold.Clearance { From = other, To = otherLow, Radius = 0.12f },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftUpperLeg, To = HumanBodyBones.LeftLowerLeg, Radius = upper, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightUpperLeg, To = HumanBodyBones.RightLowerLeg, Radius = upper, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftLowerLeg, To = HumanBodyBones.LeftFoot, Radius = lower, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightLowerLeg, To = HumanBodyBones.RightFoot, Radius = lower, MoveHand = true },
            };
        }

        [MenuItem("BuddyTD/Händler/Figuren aktualisieren (Heroes2)")]
        public static void UpdateSceneFiguresMenu()
        {
            Debug.Log(UpdateSceneFigures());
        }

        // Nur die Figuren der vorhandenen Stände neu aufbauen (Modell, Controller, Requisiten, Hold); markiert die Szene dirty
        public static string UpdateSceneFigures()
        {
            var sb = new StringBuilder();
            foreach (var m in Object.FindObjectsByType<Merchant>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (m.Figur == null) continue;
                var anim = Build(m.Figur.transform, m.Kind, sb);
                m.FigureAnimator = anim;
                EditorUtility.SetDirty(m);
                EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
            }
            return sb.ToString();
        }

        // Figur unter "Figur" aufbauen (Kind "Modell"); liefert den Animator. Fallback: altes Modell (Merchant_<Typ>.fbx, 0,76).
        public static Animator Build(Transform fig, MerchantKind kind, StringBuilder log)
        {
            string key = kind.ToString();
            bool h2 = Available(kind);
            string path = h2 ? ModelPath(kind) : MerchantSetup.FigureModelFolder + "/Merchant_" + key + ".fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { log.AppendLine("MerchantFigures: Modell fehlt: " + path); return null; }

            // vorhandenes Modell behalten, wenn gleich; sonst ersetzen
            GameObject model = null;
            for (int i = fig.childCount - 1; i >= 0; i--)
            {
                var c = fig.GetChild(i).gameObject;
                if (model == null && PrefabUtility.GetCorrespondingObjectFromSource(c) == asset) model = c;
                else Object.DestroyImmediate(c);
            }
            if (model == null) model = (GameObject)PrefabUtility.InstantiatePrefab(asset, fig);
            model.name = "Modell";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * (h2 ? Scale : 0.76f);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            anim.updateMode = AnimatorUpdateMode.UnscaledTime; // jubelt auch, während die Kartenauswahl das Spiel pausiert

            var cv = model.GetComponent<ChampionVisual>();
            if (!h2)
            {
                anim.runtimeAnimatorController = MerchantSetup.EnsureFigureController();
                if (cv != null) Object.DestroyImmediate(cv);
                foreach (var h in model.GetComponents<ChampionWeaponHold>()) Object.DestroyImmediate(h);
                log.AppendLine(key + ": altes Modell (Heroes2 fehlt)");
                return anim;
            }

            var spec = Spec(kind);
            anim.runtimeAnimatorController = Override(kind, spec);
            if (cv == null) cv = model.AddComponent<ChampionVisual>();
            cv.Animator = anim;
            ChampionPlayerSetup.SetupProps(cv, ModelPath(kind), spec.Right, spec.Left, null, spec.Hold, new PoseSample { Label = "Idle", BaseState = "Idle" });
            EditorUtility.SetDirty(model);
            log.AppendLine(key + ": " + Path.GetFileName(path) + " (Skalierung " + Scale + "), Requisite " +
                Path.GetFileNameWithoutExtension((spec.Right ?? spec.Left).Prefab) + ", Hold " + model.GetComponents<ChampionWeaponHold>().Length);
            return anim;
        }

        // Basis-Controller mit Hands-Layer (Platzhalter-Pose, beide Hände locker) + Override je Händler (eigene Hand-Pose)
        public static RuntimeAnimatorController Override(MerchantKind kind, FigureSpec spec)
        {
            var ctrl = (AnimatorController)MerchantSetup.EnsureFigureController();
            bool hasHands = false;
            foreach (var l in ctrl.layers) if (l.name == ChampionGrip.HandsLayer) hasHands = true;
            if (!hasHands)
            {
                ChampionGrip.ApplyHoldLayers(ctrl, new HoldSpec { Left = HandPose.Relaxed, Right = HandPose.Relaxed }, "Merchant");
                AssetDatabase.SaveAssets();
            }
            var placeholder = AssetDatabase.LoadAssetAtPath<AnimationClip>(ChampionGrip.Dir + "Merchant_Hands.anim");

            var m = new Dictionary<string, float>();
            ChampionGrip.AddHand(m, false, spec.Hold.Left == HandPose.Animated ? HandPose.Relaxed : spec.Hold.Left);
            ChampionGrip.AddHand(m, true, spec.Hold.Right == HandPose.Animated ? HandPose.Relaxed : spec.Hold.Right);
            var hands = ChampionGrip.PoseClip(ChampionGrip.Dir + "Merchant_" + kind + "_Hands.anim", m);

            string path = OverrideDir + "MerchantFigure_" + kind + ".overrideController";
            var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (ov == null)
            {
                ov = new AnimatorOverrideController(ctrl);
                AssetDatabase.CreateAsset(ov, path);
            }
            ov.runtimeAnimatorController = ctrl;
            ov[placeholder] = hands;
            EditorUtility.SetDirty(ov);
            AssetDatabase.SaveAssets();
            return ov;
        }

        // Alle Figuren der Szene (Heroes2) auf Durchdringung prüfen (WeaponClippingCheck, Posen Idle/Winken/Jubel)
        [MenuItem("BuddyTD/Händler/Requisiten-Clipping prüfen")]
        public static void CheckSceneFigures()
        {
            var sb = new StringBuilder();
            foreach (var kv in SceneFigures())
            {
                string label = "Haendler_" + kv.Key;
                sb.AppendLine(WeaponClippingCheck.Summary(label, WeaponClippingCheck.Check(kv.Value, 30, true, 8, null, label)));
            }
            Debug.Log("Händler-Clipping:\n" + sb);
        }

        public static SortedDictionary<MerchantKind, ChampionVisual> SceneFigures()
        {
            var list = new SortedDictionary<MerchantKind, ChampionVisual>();
            foreach (var m in Object.FindObjectsByType<Merchant>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (m.Figur == null) continue;
                var cv = m.Figur.GetComponentInChildren<ChampionVisual>(true);
                if (cv != null) list[m.Kind] = cv;
            }
            return list;
        }
    }
}
