using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Richtet das Player-Prefab für die drei Champions ein: Visual_Mage/_Knight/_Archer (Blender-Modelle, Humanoid),
    // Animator-Controller, ChampionVisual mit Waffen-Sockeln, die drei Kits (Werte, Effekte, Icons, Sounds) und den
    // Pfeil-Abschusspunkt. Menü: BuddyTD → Champion-Player einrichten. Idempotent (vorhandene Kits/Visuals werden aktualisiert).
    public static class ChampionPlayerSetup
    {
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Player.prefab";
        private const string Models = "Assets/_Game/Models/Characters/";
        private const string Weapons = "Assets/_Game/Models/Characters/Weapons/";
        private const string Icons = "Assets/_Game/UI/Icons/";
        private const string Audio = "Assets/_Game/Audio/";
        private const string Vfx = "Assets/_Game/VFX/Prefabs/";

        // Einheitliche Modell-Skalierung (Blender-Figuren ~2.1–2.4 m → ~1.8 m wie der bisherige Magier)
        public const float ModelScale = 0.76f;
        // Füße auf der Unterkante des CharacterControllers (center 0, height 2)
        public const float FootY = -1f;
        private const string IdleClip = "Assets/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx";

        [MenuItem("BuddyTD/Champion-Player einrichten")]
        public static void Setup()
        {
            // Hand-/Arm-Layer der Heroes2-Champions zuerst, die Griff-Sockel werden mit ihnen vermessen
            ChampionAnimatorBuilder.ApplyHolds();
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                SetupVisuals(root);
                SetupKits(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            UpdatePreviews();
            AssetDatabase.SaveAssets();
            Debug.Log("ChampionPlayerSetup: Player-Prefab mit drei Champions eingerichtet.");
        }

        // ---------------- Visuals ----------------

        private static void SetupVisuals(GameObject root)
        {
            // Altes Meshy-Visual entfernen (wird durch Visual_Mage ersetzt)
            var old = root.transform.Find("Visual");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            ChampionVisual mage, knight, archer;
            Vector3 fwd = Vector3.forward, up = Vector3.up, right = Vector3.right;

            // Neue Modelle (Heroes2), sobald Modell + alle Waffen vorhanden sind; sonst die alten Chibi-Modelle als Fallback
            var ms = Spec(ChampionClass.Mage);
            if (ms.UseHeroes2) mage = SetupHeroes2(root, ms);
            else
            {
                mage = Visual(root, ms.VisualName, Models + ms.LegacyModel, ChampionClass.Mage, ms.Controller, ModelScale);
                // Stab: senkrecht
                Socket(mage, mage.RightHandWeapon, HumanBodyBones.RightHand, Weapons + "Staff.fbx", Quaternion.LookRotation(fwd, up), Vector3.zero);
            }

            var ks = Spec(ChampionClass.Knight);
            if (ks.UseHeroes2) knight = SetupHeroes2(root, ks);
            else
            {
                knight = Visual(root, ks.VisualName, Models + ks.LegacyModel, ChampionClass.Knight, ks.Controller, ModelScale);
                // Schwert: Klinge (+Y) schräg nach vorne oben, Schneide (X) senkrecht
                Vector3 blade = (fwd + up * 0.35f).normalized;
                Socket(knight, knight.RightHandWeapon, HumanBodyBones.RightHand, Weapons + "Sword.fbx", Quaternion.LookRotation(right, blade), Vector3.zero);
                // Schild: Vorderseite (+Z, Blender −Y) nach vorne
                Socket(knight, knight.LeftHandWeapon, HumanBodyBones.LeftHand, Weapons + "Shield.fbx", Quaternion.LookRotation(fwd, up), new Vector3(-0.05f, 0.1f, 0.12f));
            }

            var ars = Spec(ChampionClass.Archer);
            if (ars.UseHeroes2) archer = SetupHeroes2(root, ars);
            else
            {
                archer = Visual(root, ars.VisualName, Models + ars.LegacyModel, ChampionClass.Archer, ars.Controller, ModelScale);
                // Bogen: senkrecht (+Y), Wölbung (−X) nach vorne
                Socket(archer, archer.LeftHandWeapon, HumanBodyBones.LeftHand, Weapons + "Bow.fbx", Quaternion.LookRotation(right, up), Vector3.zero);
                archer.BackItem.Prefab = null;
            }

            mage.gameObject.SetActive(true);
            knight.gameObject.SetActive(false);
            archer.gameObject.SetActive(false);

            // Pfeil-Abschusspunkt am Root (nicht am Knochen)
            var spawn = root.transform.Find("ArrowSpawn");
            if (spawn == null)
            {
                spawn = new GameObject("ArrowSpawn").transform;
                spawn.SetParent(root.transform, false);
            }
            spawn.localPosition = new Vector3(0.15f, 0.25f, 0.7f);
            spawn.localRotation = Quaternion.identity;
        }

        private static ChampionVisual Visual(GameObject root, string name, string fbx, ChampionClass cls, string controller, float scale)
        {
            var existing = root.transform.Find(name);
            int sibling = existing != null ? existing.GetSiblingIndex() : -1;
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            go.name = name;
            go.transform.localPosition = new Vector3(0f, FootY, 0f);
            go.transform.localRotation = Quaternion.identity; // Champion-FBX schauen bereits nach +Z (Render-Test: mit 180° sah man den Rücken)
            go.transform.localScale = Vector3.one * scale;
            if (sibling >= 0) go.transform.SetSiblingIndex(sibling);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controller);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var cv = go.AddComponent<ChampionVisual>();
            cv.Class = cls;
            cv.Animator = anim;
            return cv;
        }

        // Waffe so ausrichten, dass sie in der Idle-Pose (StarterAssets Idle) die gewünschte Weltrotation hat und im Handteller sitzt.
        // Gemessen an einer temporären, per AnimationMode gesampelten Szenen-Instanz des Modells.
        private static void Socket(ChampionVisual cv, ChampionVisual.Attachment a, HumanBodyBones boneId, string prefabPath, Quaternion worldRot, Vector3 extraOffset)
        {
            a.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            a.WorldScale = ModelScale;
            var model = PrefabUtility.GetCorrespondingObjectFromSource(cv.gameObject);
            if (model == null) return;

            var temp = (GameObject)Object.Instantiate(model);
            temp.hideFlags = HideFlags.HideAndDontSave;
            temp.transform.SetPositionAndRotation(new Vector3(0f, -300f, 0f), Quaternion.identity);
            temp.transform.localScale = Vector3.one * ModelScale;
            var anim = temp.GetComponent<Animator>();
            AnimationClip idle = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(IdleClip))
                if (o is AnimationClip && !o.name.StartsWith("__preview")) idle = (AnimationClip)o;
            bool sampled = false;
            try
            {
                if (idle != null)
                {
                    if (!AnimationMode.InAnimationMode()) { AnimationMode.StartAnimationMode(); sampled = true; }
                    AnimationMode.SampleAnimationClip(temp, idle, 1f);
                }
                var bone = anim != null ? anim.GetBoneTransform(boneId) : null;
                if (bone == null) return;
                // Handteller: zwischen Handgelenk und Mittelfinger-Grundgelenk
                var finger = anim.GetBoneTransform(boneId == HumanBodyBones.RightHand ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                Vector3 palm = finger != null ? Vector3.Lerp(bone.position, finger.position, 0.55f) : bone.position;
                a.LocalEuler = (Quaternion.Inverse(bone.rotation) * worldRot).eulerAngles;
                a.LocalPosition = Quaternion.Inverse(bone.rotation) * (palm - bone.position + extraOffset);
            }
            finally
            {
                if (sampled) AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(temp);
            }
        }

        // ---------------- Heroes2 (detaillierte Modelle, 6 Köpfe, Finger-Rig) ----------------

        // Heroes2-Figuren sind in Blender 1,80 m groß (mit Hut ~2,1 m) – so groß wie die alten Champions nach
        // Skalierung 0,76 (Kopf ~1,8 m) und passend zu den Skeletten (~1,9–2,05 m) und zum CharacterController (2 m).
        public const float ModelScale2 = 1f;
        // Menü-Vorschau relativ zum Spiel etwas kleiner (wie bisher 0,64 / 0,76)
        public const float PreviewRatio = 0.64f / 0.76f;
        private const string PreviewDir = "Assets/_Game/Prefabs/Champions/";

        // Eine Waffe der Heroes2-Champions. Konvention der Exporte: Ursprung = Griffmitte, Waffenachse = lokale +Y.
        public class WeaponSpec
        {
            public string Prefab;
            [Tooltip("true: Griff liegt in der Faust (Achse/Mitte aus der Finger-Geometrie); false: feste LocalPosition/Euler.")]
            public bool Grip = true;
            public float Roll;              // Drehung um die Waffenachse (Grad)
            public float AxisShift;         // Verschiebung entlang der Waffenachse (m)
            public Vector3 Offset;          // Zusatz-Versatz in Knochen-Achsen (m) bzw. feste Position (Grip = false)
            public Vector3 Euler;           // Zusatz-Drehung im Waffen-Raum bzw. feste Rotation (Grip = false)
            public float Scale = 1f;
            // Grip = false: Offset/Euler sind Weltposition/-drehung in der T-Pose des Modells (Blender-Export, Charakter blickt +Z)
            public bool BindPose;
            // Grip = false: Ursprung in der Handflächenmitte (z. B. Glutkugel), Offset/Euler als Zusatz
            public bool Palm;
        }

        public class ChampionSpec
        {
            public ChampionClass Class;
            public string VisualName, Controller, LegacyModel, Model2, ClipPrefix;
            public WeaponSpec Right, Left, Back;
            public HoldSpec Hold;
            // Freigabe des neuen Modells (erst nach Griff-/Clipping-Abnahme setzen); false = altes Modell bleibt
            public bool Heroes2Ready;

            public bool UseHeroes2
            {
                get
                {
                    if (!Heroes2Ready || !System.IO.File.Exists(Model2)) return false;
                    foreach (var w in new[] { Right, Left, Back })
                        if (w != null && !System.IO.File.Exists(w.Prefab)) return false;
                    return true;
                }
            }
        }

        private const string H2 = "Assets/_Game/Models/Characters/Heroes2/";

        // Magier: Stab rechts, Unterarm vorgestreckt, Stab senkrecht (Kristall oben) wie im Konzept-Render;
        // Muskelwerte per Optimierung (Griff bei Hüfte rechts vorn ~(0,32 | 1,10 | 0,33) m, Stab leicht nach außen geneigt)
        // und Clipping-Prüfung eingestellt (BuddyTD → Champions → Waffen-Clipping prüfen).
        public static readonly ChampionSpec[] Specs =
        {
            new ChampionSpec
            {
                Class = ChampionClass.Mage, VisualName = "Visual_Mage", Controller = "Assets/_Game/Animations/PlayerVisual.controller",
                LegacyModel = "MageChampion.fbx", Model2 = H2 + "MageChampion2.fbx", ClipPrefix = "Mage", Heroes2Ready = true,
                Right = new WeaponSpec { Prefab = H2 + "Weapons/W2_MageStaff.fbx" },
                Hold = new HoldSpec
                {
                    Right = HandPose.Fist, Left = HandPose.Relaxed,
                    ArmParts = new[] { AvatarMaskBodyPart.RightArm },
                    ArmPose = new System.Collections.Generic.Dictionary<string, float>
                    {
                        { "Right Shoulder Down-Up", 0f }, { "Right Shoulder Front-Back", 0f },
                        { "Right Arm Down-Up", -0.4f }, { "Right Arm Front-Back", 0.144f }, { "Right Arm Twist In-Out", 0f },
                        { "Right Forearm Stretch", 0.025f }, { "Right Forearm Twist In-Out", 0.263f },
                        { "Right Hand Down-Up", 0.288f }, { "Right Hand In-Out", -0.394f },
                    },
                    // IK hält den Griff rechts vor dem Körper (folgt der Hüfte in der Höhe, horizontal zu 40 %) (Stabfuß außerhalb von Robe/Knien), Stab leicht nach außen geneigt
                    IK = new HoldIK
                    {
                        GripOffset = new Vector3(0.32f, 1.1f, 0.33f), WeaponAxis = new Vector3(-0.05f, 1f, 0f),
                        HipsFollow = new Vector3(0.4f, 1f, 0.4f), ElbowHint = new Vector3(0.15f, -0.45f, -0.2f),
                        WeaponTop = 0.65f, WeaponBottom = 1.1f,
                        // Radien gemessen am gebackenen Mesh: Hutkrempe ≤ 0,32 m um die Kopfachse, Rumpf ≤ 0,2 m, Robe ≤ 0,30 m um die Unterschenkel
                        Clearances = new[]
                        {
                            new ChampionWeaponHold.Clearance { From = HumanBodyBones.Head, Height = 0.5f, Radius = 0.33f },
                            new ChampionWeaponHold.Clearance { From = HumanBodyBones.Hips, To = HumanBodyBones.Chest, Radius = 0.21f, MoveHand = true },
                            new ChampionWeaponHold.Clearance { From = HumanBodyBones.Chest, To = HumanBodyBones.Neck, Radius = 0.24f, MoveHand = true },
                            new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightUpperArm, To = HumanBodyBones.RightLowerArm, Radius = 0.14f },
                            new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightUpperLeg, To = HumanBodyBones.RightLowerLeg, Radius = 0.25f, MoveHand = true },
                            new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightLowerLeg, To = HumanBodyBones.RightFoot, Radius = 0.3f, MoveHand = true },
                            new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftLowerLeg, To = HumanBodyBones.LeftFoot, Radius = 0.3f, MoveHand = true },
                        },
                    },
                },
            },
            // Ritter/Bogenschütze: Modelle + Waffen (identisches Rig). Noch nicht freigegeben: Waffen-Konvention prüfen (Roll/Euler),
            // Hold-IK/Freiraum-Kapseln wie beim Magier eintragen, Clipping prüfen, dann Heroes2Ready = true.
            new ChampionSpec
            {
                Class = ChampionClass.Knight, VisualName = "Visual_Knight", Controller = "Assets/_Game/Animations/KnightVisual.controller",
                LegacyModel = "KnightChampion.fbx", Model2 = H2 + "KnightChampion2.fbx", ClipPrefix = "Knight", Heroes2Ready = true,
                // Roll 180: Waffen-+Z zur Handfläche (Blender-Konvention: T-Pose-Weltrotation Euler(90,0,0)) – Schwertschneiden in
                // Unterarm-Ebene, Schild-Vorderseite (−Z) auf der Handrücken-Seite
                Right = new WeaponSpec { Prefab = H2 + "Weapons/W2_Sword.fbx", Roll = 180f },
                Left = new WeaponSpec { Prefab = H2 + "Weapons/W2_Shield.fbx", Roll = 180f },
                Hold = new HoldSpec
                {
                    Right = HandPose.Fist, Left = HandPose.Fist,
                    // Schwert: Tragehaltung rechts vor der Hüfte, Klinge schräg nach vorne oben, Flachseite zum Körper;
                    // in allen Aktionen führt die Animation, Freiraum-Kapseln halten die Klinge aus Rumpf/Kopf/Beinen
                    IK = new HoldIK
                    {
                        GripOffset = new Vector3(0.26f, 1.0f, 0.3f), WeaponAxis = new Vector3(0.12f, 0.75f, 0.65f), WeaponForward = new Vector3(-1f, 0f, 0f),
                        FollowBone = HumanBodyBones.UpperChest, HipsFollow = new Vector3(0.8f, 1f, 0.8f),
                        ElbowHint = new Vector3(0.3f, -0.5f, -0.3f), WristAngle = new Vector2(50f, 130f),
                        WeaponTop = 0.92f, WeaponBottom = 0.17f, WeaponRadius = 0.03f, GroundClearance = 0.03f,
                        // Knauf → Spitze und Parierstange (±0,15 m in Schneidenrichtung X)
                        Segments = new[] { new Vector3(0f, -0.17f, 0f), new Vector3(0f, 0.9f, 0f), new Vector3(-0.15f, 0.1f, 0f), new Vector3(0.15f, 0.1f, 0f) },
                        Clearances = KnightClearances(true),
                        // Frostschlag endet tief quer vor den Beinen – im Lauf hebt sich der Oberschenkel in die Klinge:
                        // Ausklang (ab 38 % des States) per IK in eine Ruhehaltung rechts vorne, Spitze schräg nach unten/außen
                        States = new[]
                        {
                            new ChampionWeaponHold.StateTarget
                            {
                                State = "FrostStrike", UseCarry = false, Window = new Vector2(0.38f, 1f), WindowBlend = 0.1f,
                                GripOffset = new Vector3(0.3f, 0.95f, 0.35f), WeaponAxis = new Vector3(0.3f, -0.45f, 0.85f), WeaponForward = new Vector3(-1f, 0f, 0f),
                                ElbowHint = new Vector3(0.3f, -0.5f, -0.3f),
                            },
                            // Block: Schild vor dem Körper – Schwert rechts neben dem Schildrand bereithalten (nicht durch den Schild)
                            new ChampionWeaponHold.StateTarget
                            {
                                State = "Block", UseCarry = false,
                                GripOffset = new Vector3(0.36f, 1.05f, 0.24f), WeaponAxis = new Vector3(0.35f, 0.9f, 0.25f), WeaponForward = new Vector3(-1f, 0f, 0f),
                                ElbowHint = new Vector3(0.5f, -0.5f, -0.3f),
                            },
                        },
                    },
                    // Schild: Unterarm angewinkelt vor der linken Hüfte, Vorderseite nach links vorne; bei Hieben/Zaubern bleibt er dort
                    // (folgt der Hüftdrehung, z. B. Flammenwirbel), beim Block vor der Brust mit der Front nach vorne
                    IK2 = new HoldIK
                    {
                        RightHand = false, FollowHipsYaw = true, FollowBone = HumanBodyBones.UpperChest,
                        GripOffset = new Vector3(0.16f, 1.1f, 0.3f), WeaponAxis = new Vector3(0.15f, 1f, 0f), WeaponForward = new Vector3(-0.85f, 0f, -0.5f),
                        HipsFollow = new Vector3(1f, 1f, 1f), ElbowHint = new Vector3(0.35f, -0.5f, -0.2f), WristAngle = new Vector2(0f, 180f),
                        WeaponTop = 0.42f, WeaponBottom = 0.45f, WeaponRadius = 0.03f, GroundClearance = -1f,
                        Segments = ShieldSegments(),
                        Clearances = KnightClearances(false),
                        States = new[]
                        {
                            Keep("Slash1"), Keep("Slash2"),
                            // Stoß quer vor dem Körper: Schild weiter nach außen/hinten, Front zur Seite
                            new ChampionWeaponHold.StateTarget
                            {
                                State = "Slash3", UseCarry = false,
                                GripOffset = new Vector3(0.3f, 1.05f, 0.1f), WeaponAxis = new Vector3(0.15f, 1f, 0f), WeaponForward = new Vector3(-1f, 0f, -0.15f),
                                ElbowHint = new Vector3(0.4f, -0.5f, -0.3f),
                            }, Keep("FrostStrike"), Keep("LightOath"), Keep("FlameWhirl"), Keep("Earthquake"),
                            new ChampionWeaponHold.StateTarget
                            {
                                State = "Block", UseCarry = false,
                                GripOffset = new Vector3(0.02f, 1.2f, 0.32f), WeaponAxis = new Vector3(0f, 1f, 0f), WeaponForward = new Vector3(0f, 0f, -1f),
                                ElbowHint = new Vector3(0.6f, -0.3f, 0.2f),
                            },
                        },
                    },
                },
            },
            new ChampionSpec
            {
                Class = ChampionClass.Archer, VisualName = "Visual_Archer", Controller = "Assets/_Game/Animations/ArcherVisual.controller",
                LegacyModel = "ArcherChampion.fbx", Model2 = H2 + "ArcherChampion2.fbx", ClipPrefix = "Archer", Heroes2Ready = true,
                // Bogen: Roll 180 = Blender-Konvention (oberer Wurfarm zur Daumenseite, Sehne +X zur Schulter, Pfeilrichtung −X);
                // −30° zusätzlich, damit die (starre) Sehne innen am Unterarm vorbeiläuft statt durch ihn
                Left = new WeaponSpec { Prefab = H2 + "Weapons/W2_Bow.fbx", Roll = 150f },
                // Köcher: Lage aus dem Blender-Export (T-Pose): Ursprung = Rückenbefestigung (0 | 1,27 | −0,13), Charakterachsen
                Back = new WeaponSpec { Prefab = H2 + "Weapons/W2_Quiver.fbx", Grip = false, BindPose = true, Offset = new Vector3(0f, 1.3f, -0.19f), Euler = new Vector3(0f, 0f, 8f) },
                Hold = new HoldSpec
                {
                    Right = HandPose.Relaxed, Left = HandPose.Fist,
                    // Bogen: Tragehaltung links neben dem Körper, unterer Wurfarm nach außen, Pfeilrichtung nach vorn;
                    // Schuss/Frost-/Lichtpfeil: Arm nach vorn, Bogen senkrecht (oben leicht nach innen gekippt), Pfeil nach vorn;
                    // Feuerpfeil-Regen: Arm schräg nach oben; Rolle/Falle: Tragehaltung dreht mit dem Oberkörper
                    // (Achsen in Modell-Achsen mit x zur Bogenhand; WeaponForward = Bogen-+Z = −Kreuz(Sehnenseite, Wurfarm))
                    IK = new HoldIK
                    {
                        RightHand = false, FollowBone = HumanBodyBones.UpperChest, HipsFollow = new Vector3(0.8f, 1f, 0.8f),
                        GripOffset = new Vector3(0.25f, 1.0f, 0.27f), WeaponAxis = new Vector3(-0.25f, 0.92f, 0.3f), WeaponForward = new Vector3(-0.8f, -0.37f, 0.46f),
                        ElbowHint = new Vector3(0.3f, -0.6f, -0.3f), WristAngle = new Vector2(0f, 180f),
                        WeaponTop = 0.66f, WeaponBottom = 0.66f, WeaponRadius = 0.025f, GroundClearance = 0.03f,
                        Segments = BowSegments(),
                        Clearances = ArcherClearances(),
                        States = new[]
                        {
                            new ChampionWeaponHold.StateTarget
                            {
                                State = "Shoot", UseCarry = false,
                                GripOffset = new Vector3(-0.06f, 1.4f, 0.52f), WeaponAxis = new Vector3(-0.17f, 1f, 0f), WeaponForward = new Vector3(-1f, -0.17f, 0f),
                                ElbowHint = new Vector3(0.3f, -0.4f, 0f),
                            },
                            new ChampionWeaponHold.StateTarget
                            {
                                State = "ArrowRain", UseCarry = false,
                                GripOffset = new Vector3(-0.03f, 1.78f, 0.4f), WeaponAxis = new Vector3(-0.1f, 0.65f, -0.75f), WeaponForward = new Vector3(-0.985f, -0.065f, 0.075f),
                                ElbowHint = new Vector3(0.4f, -0.3f, 0f),
                            },
                            new ChampionWeaponHold.StateTarget { State = "Roll", UseCarry = true, FullFollow = true },
                            new ChampionWeaponHold.StateTarget { State = "PlaceTrap", UseCarry = true, FullFollow = true },
                        },
                    },
                    // Zughand: nur beim Schuss an die Sehne (Ankerpunkt am Kinn), sonst Animation
                    IK2 = new HoldIK
                    {
                        RightHand = true, HandOnly = true, IdleCarry = false, FollowBone = HumanBodyBones.UpperChest, FollowHipsYaw = true, HipsFollow = new Vector3(1f, 1f, 1f),
                        States = new[]
                        {
                            new ChampionWeaponHold.StateTarget { State = "Shoot", UseCarry = false, GripOffset = new Vector3(0.07f, 1.45f, 0.05f), ElbowHint = new Vector3(0.7f, -0.25f, -0.05f) },
                            new ChampionWeaponHold.StateTarget { State = "ArrowRain", UseCarry = false, GripOffset = new Vector3(-0.08f, 1.42f, 0.05f), ElbowHint = new Vector3(0.7f, -0.3f, -0.05f) },
                        },
                    },
                },
            },
        };

        private static ChampionWeaponHold.StateTarget Keep(string state)
        {
            return new ChampionWeaponHold.StateTarget { State = state, UseCarry = true };
        }

        // Schildfläche als drei senkrechte Strecken (Schild-Achsen: Front −Z, oben +Y, Ellenbogenseite +X; Mitte ≈ (+0,06 | −0,03), 0,85 × 0,61 m)
        // (Methode statt statischem Feld: Specs wird vorher initialisiert)
        private static Vector3[] ShieldSegments()
        {
            return new[]
            {
                new Vector3(-0.22f, -0.42f, -0.12f), new Vector3(-0.22f, 0.36f, -0.12f),
                new Vector3(0.06f, -0.46f, -0.14f), new Vector3(0.06f, 0.39f, -0.14f),
                new Vector3(0.34f, -0.40f, -0.12f), new Vector3(0.34f, 0.36f, -0.12f),
            };
        }

        // Bogen als Strecken (Bogen-Achsen: Griff im Ursprung, Wurfarme ±Y bis 0,66, zur Sehne +X gekrümmt, Sehne bei x 0,155)
        private static Vector3[] BowSegments()
        {
            return new[]
            {
                new Vector3(0f, -0.05f, 0f), new Vector3(0.13f, -0.6f, 0f),
                new Vector3(0f, 0.05f, 0f), new Vector3(0.13f, 0.6f, 0f),
                new Vector3(0.155f, -0.55f, 0f), new Vector3(0.155f, 0.55f, 0f),
            };
        }

        // Freiraum-Kapseln Bogenschütze (p90 am gebackenen Mesh: Rumpf 0,22, Brust/Hals 0,16, Kopf/Kapuze 0,12–0,15,
        // Oberschenkel 0,17, Unterschenkel 0,10, Oberarm 0,13)
        private static ChampionWeaponHold.Clearance[] ArcherClearances()
        {
            return new[]
            {
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.Hips, To = HumanBodyBones.UpperChest, Radius = 0.22f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.UpperChest, To = HumanBodyBones.Head, Radius = 0.17f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.Head, Height = 0.3f, Radius = 0.16f },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftUpperLeg, To = HumanBodyBones.LeftLowerLeg, Radius = 0.15f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightUpperLeg, To = HumanBodyBones.RightLowerLeg, Radius = 0.15f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftLowerLeg, To = HumanBodyBones.LeftFoot, Radius = 0.1f },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightLowerLeg, To = HumanBodyBones.RightFoot, Radius = 0.1f },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftUpperArm, To = HumanBodyBones.LeftLowerArm, Radius = 0.12f },
                // eigener Unterarm (ohne Handgelenk): die starre Sehne läuft daran vorbei
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftLowerArm, To = HumanBodyBones.LeftHand, Shorten = 0.08f, Radius = 0.07f },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightUpperArm, To = HumanBodyBones.RightLowerArm, Radius = 0.12f },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightLowerArm, To = HumanBodyBones.RightHand, Radius = 0.07f },
            };
        }

        // Freiraum-Kapseln Ritter (Radien am gebackenen Mesh gemessen, p90: Rumpf 0,20, Brust/Hals 0,18, Kopf 0,15,
        // Oberschenkel 0,18 (Beintaschen), Unterschenkel 0,09, Oberarm 0,15 (Schulterplatte))
        private static ChampionWeaponHold.Clearance[] KnightClearances(bool sword)
        {
            var l = new System.Collections.Generic.List<ChampionWeaponHold.Clearance>
            {
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.Hips, To = HumanBodyBones.UpperChest, Radius = 0.2f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.UpperChest, To = HumanBodyBones.Head, Radius = 0.18f, MoveHand = !sword },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.Head, Height = 0.3f, Radius = 0.17f },
                // Beine: Schild wird verschoben, die lange Klinge aus dem Handgelenk herausgedreht
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftUpperLeg, To = HumanBodyBones.LeftLowerLeg, Radius = 0.17f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightUpperLeg, To = HumanBodyBones.RightLowerLeg, Radius = 0.17f, MoveHand = true },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftLowerLeg, To = HumanBodyBones.LeftFoot, Radius = 0.1f, MoveHand = !sword },
                new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightLowerLeg, To = HumanBodyBones.RightFoot, Radius = 0.1f, MoveHand = !sword },
            };
            // Gegenarm (Schwert: linker Oberarm; Schild: rechter Ober-/Unterarm)
            if (sword)
            {
                l.Add(new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftUpperArm, To = HumanBodyBones.LeftLowerArm, Radius = 0.13f });
                l.Add(new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftLowerArm, To = HumanBodyBones.LeftMiddleProximal, Radius = 0.08f });
                // eigener Oberarm/Schulterplatte
                l.Add(new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightUpperArm, To = HumanBodyBones.RightLowerArm, Radius = 0.13f });
                // Becken/Beintaschen quer (tiefe Hiebe enden quer vor dem Schritt): Klinge herausdrehen
                l.Add(new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftUpperLeg, To = HumanBodyBones.RightUpperLeg, Radius = 0.2f });
            }
            else
            {
                l.Add(new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightUpperArm, To = HumanBodyBones.RightLowerArm, Radius = 0.13f, MoveHand = true });
                l.Add(new ChampionWeaponHold.Clearance { From = HumanBodyBones.RightLowerArm, To = HumanBodyBones.RightHand, Radius = 0.08f, MoveHand = true });
                // eigener Oberarm/Schulterplatte (beim Beugen des Rumpfs, Landung)
                l.Add(new ChampionWeaponHold.Clearance { From = HumanBodyBones.LeftUpperArm, To = HumanBodyBones.LeftLowerArm, Radius = 0.12f, MoveHand = true });
            }
            return l.ToArray();
        }

        public static ChampionSpec Spec(ChampionClass c)
        {
            foreach (var s in Specs) if (s.Class == c) return s;
            return null;
        }

        private static ChampionVisual SetupHeroes2(GameObject root, ChampionSpec spec)
        {
            var cv = Visual(root, spec.VisualName, spec.Model2, spec.Class, spec.Controller, ModelScale2);
            SetupProps(cv, spec.Model2, spec.Right, spec.Left, spec.Back, spec.Hold, new PoseSample { Label = "Idle" });
            return cv;
        }

        // Waffen/Requisiten eines Heroes2-Modells einrichten (auch für die Händler-Figuren, MerchantSetup):
        // Griff-Sockel in der gesampelten Faust (Hands-Layer), Handflächen-/Rücken-Sockel aus der T-Pose,
        // ChampionWeaponHold-Komponenten mit den Ruhewerten der Bezugsknochen. restPose = Pose für die Vermessung.
        public static void SetupProps(ChampionVisual cv, string modelPath, WeaponSpec right, WeaponSpec left, WeaponSpec back, HoldSpec hold, PoseSample restPose)
        {
            cv.RightHandWeapon = new ChampionVisual.Attachment();
            cv.LeftHandWeapon = new ChampionVisual.Attachment();
            cv.BackItem = new ChampionVisual.Attachment();
            // Ruhepose der Bezugsknochen (Hüfte, Oberkörper …) für die Trage-IK
            var rest = new System.Collections.Generic.Dictionary<HumanBodyBones, Vector3[]>();
            using (var s = new ChampionPoseSampler(cv, new Vector3(0f, -300f, 0f), false))
            {
                s.Sample(restPose, 0f);
                foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
                {
                    var t = s.Animator.GetBoneTransform(b);
                    if (t != null) rest[b] = new[] { s.Root.transform.InverseTransformPoint(t.position), t.InverseTransformDirection(s.Root.transform.forward),
                        (Quaternion.Inverse(s.Root.transform.rotation) * t.rotation).eulerAngles };
                }
                GripSocket(s, cv.RightHandWeapon, right, true);
                GripSocket(s, cv.LeftHandWeapon, left, false);
                FixedSocket(cv.BackItem, back);
            }
            if (right != null && (right.BindPose || right.Palm)) BindSocket(cv.RightHandWeapon, right, modelPath, HumanBodyBones.RightHand);
            if (left != null && (left.BindPose || left.Palm)) BindSocket(cv.LeftHandWeapon, left, modelPath, HumanBodyBones.LeftHand);
            if (back != null && back.BindPose) BindSocket(cv.BackItem, back, modelPath, HumanBodyBones.UpperChest);
            ConfigureHold(cv.gameObject, cv, hold, rest);
        }

        // Je HoldIK (IK, IK2) eine ChampionWeaponHold-Komponente; überzählige werden entfernt
        public static void ConfigureHold(GameObject go, ChampionVisual cv, HoldSpec spec, System.Collections.Generic.Dictionary<HumanBodyBones, Vector3[]> rest)
        {
            var iks = new System.Collections.Generic.List<HoldIK>();
            if (spec != null && spec.IK != null) iks.Add(spec.IK);
            if (spec != null && spec.IK2 != null) iks.Add(spec.IK2);
            var hs = go.GetComponents<ChampionWeaponHold>();
            for (int i = iks.Count; i < hs.Length; i++) Object.DestroyImmediate(hs[i]);
            for (int i = 0; i < iks.Count; i++)
            {
                var h = i < hs.Length ? hs[i] : go.AddComponent<ChampionWeaponHold>();
                h.Visual = cv;
                iks[i].ApplyTo(h);
                Vector3[] r;
                if (rest != null && rest.TryGetValue(h.FollowBone, out r)) { h.HipsRest = r[0]; h.HipsRestForward = r[1]; h.FollowRestEuler = r.Length > 2 ? r[2] : Vector3.zero; }
            }
        }

        private static void FixedSocket(ChampionVisual.Attachment a, WeaponSpec w)
        {
            if (w == null) { a.Prefab = null; return; }
            a.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(w.Prefab);
            a.WorldScale = ModelScale2 * w.Scale;
            a.LocalPosition = w.Offset;
            a.LocalEuler = w.Euler;
        }

        // Rücken-Sockel aus der T-Pose: Offset/Euler = Weltlage in der Ruhepose des FBX → lokal zum Brustknochen
        // (gleiche Knochenwahl wie ChampionVisual.Attach: UpperChest, sonst Chest/Spine)
        private static void BindSocket(ChampionVisual.Attachment a, WeaponSpec w, string modelPath, HumanBodyBones boneId)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var go = (GameObject)Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                var anim = go.GetComponent<Animator>();
                Transform bone = anim.GetBoneTransform(boneId);
                if (bone == null && boneId == HumanBodyBones.UpperChest) bone = anim.GetBoneTransform(HumanBodyBones.Chest);
                if (bone == null) bone = anim.GetBoneTransform(HumanBodyBones.Spine);
                Vector3 pos = w.Offset;
                Quaternion rot = Quaternion.Euler(w.Euler);
                if (w.Palm)
                {
                    // Handflächenmitte: zwischen Handgelenk und Mittelfinger-Grundgelenk, 2 cm zur Handfläche (T-Pose: Handflächen unten);
                    // Drehung wie die Griff-Waffen in der T-Pose (Euler 90,0,0: +Y nach vorn/Daumenseite, +Z zur Handfläche)
                    bool right = boneId == HumanBodyBones.RightHand;
                    var mid = anim.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                    pos = Vector3.Lerp(bone.position, mid.position, 0.55f) + Vector3.down * 0.02f + w.Offset;
                    rot = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(w.Euler);
                }
                a.LocalPosition = Quaternion.Inverse(bone.rotation) * (pos - bone.position) * ModelScale2;
                a.LocalEuler = (Quaternion.Inverse(bone.rotation) * rot).eulerAngles;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // Waffenachse (+Y) durch die Röhre der Faust, Ursprung (Griffmitte) in deren Mitte, Waffen-+Z zeigt zu den Knöcheln
        private static void GripSocket(ChampionPoseSampler s, ChampionVisual.Attachment a, WeaponSpec w, bool right)
        {
            FixedSocket(a, w);
            if (w == null || !w.Grip) return;
            var bone = s.Animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Vector3 center, axis;
            float radius;
            if (bone == null || !ChampionGrip.FitGrip(s.Animator, right, out center, out axis, out radius))
            {
                Debug.LogWarning("ChampionPlayerSetup: Griff nicht bestimmbar (" + w.Prefab + ")");
                return;
            }
            var knuckle = s.Animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal).position;
            Vector3 fwd = Vector3.ProjectOnPlane(knuckle - center, axis).normalized;
            Quaternion rot = Quaternion.AngleAxis(w.Roll, axis) * Quaternion.LookRotation(fwd, axis) * Quaternion.Euler(w.Euler);
            center += axis * w.AxisShift + bone.rotation * w.Offset;
            float scale = s.Root.transform.lossyScale.x;
            a.LocalEuler = (Quaternion.Inverse(bone.rotation) * rot).eulerAngles;
            a.LocalPosition = Quaternion.Inverse(bone.rotation) * (center - bone.position) / Mathf.Max(0.0001f, scale) * ModelScale2;
            Debug.Log(string.Format("ChampionPlayerSetup: Griff {0} – Röhrenradius (Finger-Mittellinie) {1:0.0} cm", System.IO.Path.GetFileNameWithoutExtension(w.Prefab), radius * 100f / scale));
        }

        // Menü-Vorschauen der Heroes2-Champions: Kind "Model" = Modell (verschachteltes FBX-Prefab) + Controller + ChampionVisual
        public static void UpdatePreviews()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            foreach (var spec in Specs)
            {
                if (!spec.UseHeroes2) continue;
                var srcT = player.transform.Find(spec.VisualName);
                var src = srcT != null ? srcT.GetComponent<ChampionVisual>() : null;
                string path = PreviewDir + "Preview_" + spec.Class + ".prefab";
                if (src == null || !System.IO.File.Exists(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var old = root.transform.Find("Model");
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(spec.Model2), root.transform);
                    go.name = "Model";
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one * (ModelScale2 * PreviewRatio);
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    var anim = go.GetComponent<Animator>();
                    if (anim == null) anim = go.AddComponent<Animator>();
                    anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(spec.Controller);
                    anim.applyRootMotion = false;
                    var cv = go.AddComponent<ChampionVisual>();
                    cv.Class = spec.Class;
                    cv.Animator = anim;
                    cv.RightHandWeapon = Scaled(src.RightHandWeapon, PreviewRatio);
                    cv.LeftHandWeapon = Scaled(src.LeftHandWeapon, PreviewRatio);
                    cv.BackItem = Scaled(src.BackItem, PreviewRatio);
                    // Ruhewerte der Bezugsknochen vom Player-Visual übernehmen
                    var rest = new System.Collections.Generic.Dictionary<HumanBodyBones, Vector3[]>();
                    foreach (var sh in src.GetComponents<ChampionWeaponHold>()) rest[sh.FollowBone] = new[] { sh.HipsRest, sh.HipsRestForward, sh.FollowRestEuler };
                    ConfigureHold(go, cv, spec.Hold, rest);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static ChampionVisual.Attachment Scaled(ChampionVisual.Attachment a, float k)
        {
            return new ChampionVisual.Attachment { Prefab = a.Prefab, LocalPosition = a.LocalPosition * k, LocalEuler = a.LocalEuler, WorldScale = a.WorldScale * k };
        }

        // ---------------- Kits ----------------

        private static T Get<T>(GameObject root) where T : Component
        {
            var c = root.GetComponent<T>();
            return c != null ? c : root.AddComponent<T>();
        }

        private static GameObject Prefab(string path) { return AssetDatabase.LoadAssetAtPath<GameObject>(path); }
        private static GameObject Champ(string name) { return Prefab(ChampionFxBuilder.Dir + name + ".prefab"); }
        private static GameObject ByGuid(string guid) { return Prefab(AssetDatabase.GUIDToAssetPath(guid)); }
        private static Sprite Icon(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(Icons + name + ".png"); }

        private static ChampionSfx Sfx(string clip, float volume, float pMin, float pMax, float interval = 0.05f)
        {
            return new ChampionSfx { Clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + clip), Volume = volume, Pitch = new Vector2(pMin, pMax), MinInterval = interval };
        }

        private static ChampionKit.AbilityDisplay D(AbilityId id, string name, string icon)
        {
            return new ChampionKit.AbilityDisplay { Id = id, Name = name, Icon = Icon(icon) };
        }

        private static void SetupKits(GameObject root)
        {
            var spawn = root.transform.Find("ArrowSpawn");

            // ---- Magier (Werte wie bisher in PlayerAbilities) ----
            var mage = Get<MageKit>(root);
            mage.DisplayName = "Magier";
            if (mage.ArcaneBallPrefab == null) mage.ArcaneBallPrefab = Prefab("Assets/_Game/Prefabs/ArcaneBall.prefab");
            mage.ObstacleLayer = 1;
            if (mage.FireWave.CastEffectPrefab == null) mage.FireWave.CastEffectPrefab = ByGuid("423d320dc682b40869f3426fb5709dfa");
            if (mage.FireWave.BurnVfxPrefab == null) mage.FireWave.BurnVfxPrefab = ByGuid("9b2f858c065484439a8507ccf83cb5c5");
            if (mage.FrostNova.CastEffectPrefab == null) mage.FrostNova.CastEffectPrefab = ByGuid("c02a290f0d01640608733aabe636d97b");
            if (mage.FrostNova.FrozenVfxPrefab == null) mage.FrostNova.FrozenVfxPrefab = ByGuid("4a39c28ab0eae4e2baa0c34042ff2038");
            if (mage.StoneWall.WallPrefab == null) mage.StoneWall.WallPrefab = ByGuid("17db8f5d19f584c018be6fdd6251bf58");
            if (mage.HolyCircle.CastEffectPrefab == null) mage.HolyCircle.CastEffectPrefab = ByGuid("7862d9904f5224fd0bfc5b85d9c4d895");
            if (mage.HolyCircle.BlindVfxPrefab == null) mage.HolyCircle.BlindVfxPrefab = ByGuid("e8dcdc109ff9c4c4f978531732e0bfd3");
            mage.Abilities = new System.Collections.Generic.List<ChampionKit.AbilityDisplay>
            {
                D(AbilityId.ArcaneBall, "Arkanball", "ability_arcane"),
                D(AbilityId.Blink, "Blinzeln", "ability_blink"),
                D(AbilityId.FireWave, "Flammenwelle", "emblem_fire"),
                D(AbilityId.FrostNova, "Frostnova", "emblem_ice"),
                D(AbilityId.StoneWall, "Steinwall", "emblem_earth"),
                D(AbilityId.HolyCircle, "Heiliger Kreis", "emblem_light"),
            };

            GameObject burn = mage.FireWave.BurnVfxPrefab, frozen = mage.FrostNova.FrozenVfxPrefab, blind = mage.HolyCircle.BlindVfxPrefab;

            // ---- Schwertkämpfer ----
            var k = Get<KnightKit>(root);
            k.DisplayName = "Schwertkämpfer";
            k.SlashFxPrefab = Champ("VFX_SwordSlash");
            k.SlashColor = new Color(1.3f, 1.45f, 1.8f, 1f);
            k.FinisherColor = new Color(2f, 1.5f, 0.45f, 1f);
            k.HitFxPrefab = Champ("VFX_SwordHit");
            k.BlockSparkPrefab = Champ("VFX_BlockSpark");
            k.BlockGlowPrefab = Champ("VFX_BlockGlow");
            k.FlameWhirl.CastEffectPrefab = Champ("VFX_FlameWhirl");
            k.FlameWhirl.EffectLifetime = 2.5f;
            k.FlameWhirl.ArcFxPrefab = Champ("VFX_SwordSlash");
            k.FlameWhirl.BurnVfxPrefab = burn;
            k.FrostStrike.CastEffectPrefab = Champ("VFX_FrostStrike");
            k.FrostStrike.EffectLifetime = 2.5f;
            k.FrostStrike.FrozenVfxPrefab = frozen;
            k.Earthquake.CastEffectPrefab = Champ("VFX_Earthquake");
            k.Earthquake.EffectLifetime = 2.5f;
            k.Earthquake.StunVfxPrefab = blind;
            k.LightOath.CastEffectPrefab = Champ("VFX_LightOath");
            k.LightOath.EffectLifetime = 2.5f;
            k.LightOath.AuraPrefab = Champ("VFX_LightOathAura");
            k.SwingSfx = Sfx("AIPfeilsSchuss.wav", 0.45f, 1.25f, 1.45f);
            k.FinisherSfx = Sfx("AIPfeilsSchuss.wav", 0.6f, 0.8f, 0.9f);
            k.BlockHitSfx = Sfx("Pfeiltreffer.mp3", 0.6f, 0.6f, 0.72f, 0.2f);
            k.BlockBreakSfx = Sfx("explosion.wav", 0.45f, 1.5f, 1.7f);
            k.Abilities = new System.Collections.Generic.List<ChampionKit.AbilityDisplay>
            {
                D(AbilityId.SwordSlash, "Schwerthieb", "ability_knight_slash"),
                D(AbilityId.ShieldBlock, "Schildblock", "ability_knight_block"),
                D(AbilityId.FlameWhirl, "Flammenwirbel", "ability_knight_flamewhirl"),
                D(AbilityId.FrostStrike, "Frostschlag", "ability_knight_froststrike"),
                D(AbilityId.Earthquake, "Erdbeben", "ability_knight_earthquake"),
                D(AbilityId.LightOath, "Lichtschwur", "ability_knight_lightoath"),
            };

            // ---- Bogenschütze ----
            var a = Get<ArcherKit>(root);
            a.DisplayName = "Bogenschütze";
            a.ProjectileSpawn = spawn;
            a.ArrowPrefab = Champ("Arrow");
            a.ArrowHitFxPrefab = Champ("VFX_ArrowHit");
            a.RollDustPrefab = Champ("VFX_RollDust");
            a.FireArrowRain.CastEffectPrefab = Champ("VFX_FireRainArea");
            a.FireArrowRain.ScaleEffectBySize = true;
            a.FireArrowRain.EffectLifetime = 2.5f;
            a.FireArrowRain.FallingArrowPrefab = Champ("FireArrow_Falling");
            a.FireArrowRain.ImpactFxPrefab = Champ("VFX_FireImpact");
            a.FireArrowRain.BurnVfxPrefab = burn;
            a.FrostArrow.ArrowPrefab = Champ("FrostArrow");
            a.FrostArrow.CastEffectPrefab = Champ("VFX_FrostArrowCast");
            a.FrostArrow.EffectLifetime = 1.5f;
            a.FrostArrow.FrozenVfxPrefab = frozen;
            a.ThornTrap.TrapPrefab = Champ("ThornTrap");
            a.ThornTrap.RootVfxPrefab = Champ("VFX_ThornRoot");
            a.ThornTrap.SnapFxPrefab = Champ("VFX_ThornSnap");
            a.LightArrow.BeamPrefab = Champ("VFX_LightBeam");
            a.LightArrow.HitFxPrefab = Prefab(Vfx + "LightHit.prefab");
            a.LightArrow.BlindVfxPrefab = blind;
            a.ShootSfx = Sfx("Pfeilschuss.mp3", 0.6f, 0.95f, 1.1f);
            a.ArrowHitSfx = Sfx("Pfeiltreffer.mp3", 0.55f, 0.95f, 1.1f);
            a.RollSfx = Sfx("AIPfeilsSchuss.wav", 0.35f, 0.55f, 0.65f);
            a.Abilities = new System.Collections.Generic.List<ChampionKit.AbilityDisplay>
            {
                D(AbilityId.ArrowShot, "Pfeilschuss", "ability_archer_shot"),
                D(AbilityId.Roll, "Rolle", "ability_archer_roll"),
                D(AbilityId.FireArrowRain, "Feuerpfeil-Regen", "ability_archer_firerain"),
                D(AbilityId.FrostArrow, "Frostpfeil", "ability_archer_frostarrow"),
                D(AbilityId.ThornTrap, "Dornenfalle", "ability_archer_trap"),
                D(AbilityId.LightArrow, "Lichtpfeil", "ability_archer_lightarrow"),
            };

            // Champion-Definitionen (Hauptmenü-Assets eines anderen Agenten), falls vorhanden
            foreach (var g in AssetDatabase.FindAssets("t:ChampionDefinitionSO"))
            {
                var def = AssetDatabase.LoadAssetAtPath<ChampionDefinitionSO>(AssetDatabase.GUIDToAssetPath(g));
                if (def == null) continue;
                if (def.Class == ChampionClass.Mage) mage.Definition = def;
                else if (def.Class == ChampionClass.Knight) k.Definition = def;
                else a.Definition = def;
            }

            EditorUtility.SetDirty(root);
        }
    }
}
