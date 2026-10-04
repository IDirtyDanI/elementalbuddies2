using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SB = ElementalBuddies.EditorTools.SuperBuilder;

namespace ElementalBuddies.EditorTools
{
    // Stufe 4 der Basis-Buddies (Flammenkaiser, Frostkönig, Bergkönig, Sonnenerzengel):
    // Blender-Teile aus Models/Stage4/<Element>/ (Krone, Umhang/Schulterteile/Flügel, Waffe) an die Knochen des Humanoid-Visuals
    // von Fire/Ice/Earth/Light.prefab, Stufe-4-Aura (Partikel, Kind "Stage4FX"), alles in BuddyEvolution.Stage4Extras (inaktiv),
    // Stage4Scale, Stufe-4-VFX am ShooterBuddy (Brand/Frost), Porträts UI/Icons/portrait4_*.png + BuddyInfoPanelUI.Stage4Sprites
    // in der offenen Szene (Szene wird nicht gespeichert). Reproduzierbar: entfernt vorhandene S4_*-Teile und Stage4FX vorher.
    // Platzierung: wie Modellierer-placement.json – Pivot = Knochenkopf in Bind-Pose (+ Offset im Modellraum des Humanoids),
    // Waffen danach einmal gegen die Idle-Pose ausgerichtet (Griff bleibt in der Hand, Schaft fast senkrecht).
    public static class Stage4Builder
    {
        private const string ModelDir = "Assets/_Game/Models/Stage4/";
        private const string PrefabDir = "Assets/_Game/Prefabs/";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string VfxMat = "Assets/_Game/VFX/Materials/";
        private const string VfxPrefabs = "Assets/_Game/VFX/Prefabs/";
        public const float Stage4Scale = 1.15f;

        private class Piece
        {
            public string Name, Bone;
            public Vector3 Offset, Rot;
            public bool Weapon;
            public Vector3 IdleTilt; // Waffen: gewünschte Ausrichtung in der Idle-Pose (Modellraum-Euler: X = nach vorn, Z < 0 = nach außen)
            public Piece(string name, string bone, Vector3 offset, Vector3 rot) { Name = name; Bone = bone; Offset = offset; Rot = rot; }
        }

        private class Element
        {
            public string Name, Prefab, Icon;
            public float HeadY; // Kopf-Knochen (Bind-Pose, unskaliert) laut placement.json → Maßstab des Modells in Unity
            public Piece[] Pieces;
        }

        private static Piece W(string name, Vector3 offset, Vector3 rot, Vector3 idleTilt)
        {
            return new Piece(name, "RightHand", offset, rot) { Weapon = true, IdleTilt = idleTilt };
        }

        private static readonly Element[] Elements =
        {
            new Element { Name = "Fire", Prefab = "Fire", Icon = "fire", HeadY = 1.0556f, Pieces = new[]
            {
                new Piece("S4_Fire_Crown", "Head", Vector3.zero, Vector3.zero),
                new Piece("S4_Fire_Cape", "Spine", Vector3.zero, Vector3.zero),
                W("S4_Fire_Lance", new Vector3(0.0313f, 0.001f, 0.0702f), Vector3.zero, new Vector3(8f, 0f, -12f)),
            } },
            new Element { Name = "Ice", Prefab = "Ice", Icon = "ice", HeadY = 1.1725f, Pieces = new[]
            {
                new Piece("S4_Ice_Crown", "Head", Vector3.zero, Vector3.zero),
                new Piece("S4_Ice_Mantle", "Spine", Vector3.zero, Vector3.zero),
                W("S4_Ice_Scepter", new Vector3(0.0606f, -0.0047f, -0.0574f), Vector3.zero, new Vector3(8f, 0f, -10f)),
            } },
            new Element { Name = "Earth", Prefab = "Earth", Icon = "earth", HeadY = 1.2763f, Pieces = new[]
            {
                new Piece("S4_Earth_Crown", "Head", Vector3.zero, Vector3.zero),
                new Piece("S4_Earth_Pauldron_L", "LeftArm", Vector3.zero, Vector3.zero),
                new Piece("S4_Earth_Pauldron_R", "RightArm", Vector3.zero, Vector3.zero),
                W("S4_Earth_Hammer", new Vector3(0.0248f, -0.098f, 0.0007f), new Vector3(0f, 0f, -20f), new Vector3(38f, 0f, -12f)), // nach vorn geneigt: senkrecht kollidiert der Kopf mit dem Fels-Schulterteil, Kopf nach unten verschwindet im Sockel
            } },
            new Element { Name = "Light", Prefab = "Light", Icon = "light", HeadY = 0.9674f, Pieces = new[]
            {
                new Piece("S4_Light_Halo", "Head", Vector3.zero, Vector3.zero),
                new Piece("S4_Light_Laurel", "Head", Vector3.zero, Vector3.zero),
                new Piece("S4_Light_Wings_L", "Spine", Vector3.zero, Vector3.zero),
                new Piece("S4_Light_Wings_R", "Spine", Vector3.zero, Vector3.zero),
                W("S4_Light_Spear", new Vector3(0.054f, 0.0166f, -0.0007f), Vector3.zero, new Vector3(8f, 0f, -10f)),
            } },
        };

        // Leuchtstärke der *_Glow-Materialien (HDR); Rest 1,2 (höher kippt Gelb/Eisblau nach Bloom ins Weiße)
        private static float GlowIntensity(string matName)
        {
            if (matName.Contains("Core")) return 1.8f;
            if (matName.Contains("Gem")) return 1.6f;
            if (matName.StartsWith("S4_Ice")) return 0.9f; // helles Eisblau kippt sonst ins Weiße
            return 1.2f;
        }

        [MenuItem("BuddyTD/Stufe 4 einrichten")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Stage4Builder: nur im Edit-Mode bauen.");
                return;
            }
            var burn = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPrefabs + "VFX_Burning.prefab");
            var frozen = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPrefabs + "VFX_Frozen.prefab");
            foreach (var el in Elements)
            {
                ImportPieces(el);
                BuildPrefab(el, burn, frozen);
            }
            AssetDatabase.SaveAssets();
            foreach (var el in Elements) RenderPortrait4(el);
            WirePortraits();
            AssetDatabase.SaveAssets();
            Debug.Log("Stage4Builder: Stufe 4 für Feuer, Eis, Erde, Licht eingerichtet.");
        }

        // ---------------- Import ----------------

        private static void ImportPieces(Element el)
        {
            string dir = ModelDir + el.Name + "/";
            string matDir = dir + "Materials/";
            if (!AssetDatabase.IsValidFolder(dir + "Materials")) AssetDatabase.CreateFolder(ModelDir + el.Name, "Materials");
            foreach (var p in el.Pieces)
            {
                string path = dir + p.Name + ".fbx";
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) { Debug.LogError("Stage4Builder: kein Modell unter " + path); continue; }
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importCameras = false; imp.importLights = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var em = o as Material;
                    if (em == null) continue;
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name), PieceMaterial(em, matDir));
                }
                foreach (var kv in imp.GetExternalObjectMap())
                {
                    var ext = kv.Value as Material;
                    if (ext != null && AssetDatabase.GetAssetPath(ext).StartsWith(matDir)) PieceMaterial(ext, matDir);
                }
                imp.SaveAndReimport();
            }
        }

        // URP-Lit: Grundfarbe aus dem FBX; Metalle (Blender metallic > 0,3) metallic 0,6 / glatt 0,6; *_Glow HDR-Emission
        private static Material PieceMaterial(Material src, string matDir)
        {
            string p = matDir + src.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            bool created = false;
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
                created = true;
            }
            if (m.shader.name != "Universal Render Pipeline/Lit") m.shader = Shader.Find("Universal Render Pipeline/Lit");
            if (created || src != m)
            {
                Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : (src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);
                c.a = 1f;
                float metal = src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0f;
                m.SetColor("_BaseColor", c);
                bool isMetal = metal > 0.3f || src.name.Contains("Gold") || src.name.Contains("Bronze") || src.name.Contains("Trim");
                m.SetFloat("_Metallic", isMetal ? 0.6f : 0f);
                m.SetFloat("_Smoothness", isMetal ? 0.6f : 0.3f);
            }
            Color baseCol = m.GetColor("_BaseColor");
            if (src.name.EndsWith("_Glow"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", baseCol * GlowIntensity(src.name));
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

        // ---------------- Prefab ----------------

        private static void BuildPrefab(Element el, GameObject burn, GameObject frozen)
        {
            string path = PrefabDir + el.Prefab + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var evo = root.GetComponent<BuddyEvolution>();
                if (evo == null || evo.HumanoidVisual == null) { Debug.LogError("Stage4Builder: BuddyEvolution/HumanoidVisual fehlt in " + path); return; }
                var visual = evo.HumanoidVisual.transform;

                // Alte Stufe-4-Teile entfernen (Builder ist wiederholbar)
                var old = new List<GameObject>();
                foreach (var t in visual.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("S4_")) old.Add(t.gameObject);
                var oldFx = root.transform.Find("Stage4FX");
                if (oldFx != null) old.Add(oldFx.gameObject);
                foreach (var o in old) if (o != null) Object.DestroyImmediate(o);

                var head = Bone(visual, "Head");
                // Light-FBX ist mit globalScale 0,5 importiert → Teile (für das unskalierte Modell gebaut) mitskalieren
                float k = head != null ? visual.InverseTransformPoint(head.position).y / el.HeadY : 1f;
                if (Mathf.Abs(k - 1f) < 0.02f) k = 1f;

                var extras = new List<GameObject>();
                var weapons = new List<KeyValuePair<Transform, Piece>>();
                foreach (var p in el.Pieces)
                {
                    var bone = Bone(visual, p.Bone);
                    if (bone == null) { Debug.LogError("Stage4Builder: Knochen " + p.Bone + " fehlt (" + el.Name + ")"); continue; }
                    var t = Attach(visual, bone, ModelDir + el.Name + "/" + p.Name + ".fbx", p, k);
                    if (t == null) continue;
                    extras.Add(t.gameObject);
                    if (p.Weapon) weapons.Add(new KeyValuePair<Transform, Piece>(t, p));
                }
                if (weapons.Count > 0) AlignWeaponsToIdle(visual, weapons);

                var fx = new GameObject("Stage4FX").transform;
                fx.SetParent(root.transform, false);
                switch (el.Name)
                {
                    case "Fire": FireFx(fx); break;
                    case "Ice": IceFx(fx); break;
                    case "Earth": EarthFx(fx); break;
                    case "Light": LightFx(fx); break;
                }
                extras.Add(fx.gameObject);

                foreach (var e in extras) e.SetActive(false);
                evo.Stage4Extras = extras.ToArray();
                evo.Stage4Scale = Stage4Scale;

                var shooter = root.GetComponent<ShooterBuddy>();
                if (shooter != null)
                {
                    if (el.Name == "Fire") shooter.Stage4BurnVfxPrefab = burn;
                    if (el.Name == "Ice") shooter.Stage4FreezeVfxPrefab = frozen;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform Bone(Transform visual, string name)
        {
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                if (t.name == name && t.GetComponent<Renderer>() == null) return t;
            return null;
        }

        // Bind-Pose: Position = Knochen + Offset (Modellraum), Rotation = Visual × rot, Welt-Skalierung = Visual × k; dann an den Knochen hängen
        private static Transform Attach(Transform visual, Transform bone, string fbx, Piece p, float k)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (src == null) { Debug.LogError("Stage4Builder: fehlt " + fbx); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = p.Name;
            Quaternion srcRot = src.transform.localRotation;
            Vector3 srcScale = src.transform.localScale;
            var t = go.transform;
            t.SetParent(bone, false);
            t.position = bone.position + visual.TransformVector(p.Offset * k);
            t.rotation = visual.rotation * Quaternion.Euler(p.Rot) * srcRot;
            Vector3 vs = visual.lossyScale * k, bs = bone.lossyScale;
            t.localScale = new Vector3(vs.x / bs.x * srcScale.x, vs.y / bs.y * srcScale.y, vs.z / bs.z * srcScale.z);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            var an = go.GetComponent<Animator>();
            if (an != null) Object.DestroyImmediate(an);
            return t;
        }

        // Waffen wurden in T-Pose angepasst; die Buddies stehen mit hängenden Armen → in der Idle-Pose (t = 0,5 s) die gewünschte
        // Ausrichtung im Modellraum setzen und die so entstandene Hand-lokale Drehung behalten. Pose danach zurücksetzen.
        private static void AlignWeaponsToIdle(Transform visual, List<KeyValuePair<Transform, Piece>> weapons)
        {
            var an = visual.GetComponentInChildren<Animator>(true);
            var ac = an != null ? an.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
            var idle = ac != null && ac.layers.Length > 0 && ac.layers[0].stateMachine.defaultState != null
                ? ac.layers[0].stateMachine.defaultState.motion as AnimationClip : null;
            if (idle == null) { Debug.LogWarning("Stage4Builder: keine Idle-Animation unter " + visual.name); return; }

            var all = visual.GetComponentsInChildren<Transform>(true);
            var saved = new List<(Transform t, Vector3 p, Quaternion r, Vector3 s)>();
            foreach (var t in all) saved.Add((t, t.localPosition, t.localRotation, t.localScale));
            idle.SampleAnimation(an.gameObject, 0.5f);
            var local = new Dictionary<Transform, Quaternion>();
            foreach (var kv in weapons)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtilitySource(kv.Value));
                Quaternion srcRot = src != null ? src.transform.localRotation : Quaternion.identity;
                kv.Key.rotation = visual.rotation * Quaternion.Euler(kv.Value.IdleTilt) * srcRot;
                local[kv.Key] = kv.Key.localRotation;
            }
            foreach (var s in saved)
            {
                if (s.t == null) continue;
                s.t.localPosition = s.p; s.t.localRotation = s.r; s.t.localScale = s.s;
            }
            foreach (var kv in local) kv.Key.localRotation = kv.Value;
        }

        private static string PrefabUtilitySource(Piece p)
        {
            foreach (var el in Elements)
                foreach (var q in el.Pieces)
                    if (q == p) return ModelDir + el.Name + "/" + p.Name + ".fbx";
            return null;
        }

        // ---------------- Stufe-4-Auren (Root-Raum, Boden y = 0; Fußabdruck ≤ 1 m Radius wegen 1-m-Baugitter) ----------------

        private static Material Mat(string name) { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + name + ".mat"); }

        private static void FireFx(Transform fx)
        {
            var ring = SB.PS(fx, "FlameRing", new Vector3(0f, 0.05f, 0f), Mat("VFX_Ring_Add"), new Vector2(1.6f, 1.6f), new Vector2(3.3f, 3.3f),
                new Color(1f, 0.45f, 0.1f, 0.55f), 3, true);
            SB.Rate(ring, 1.3f); SB.Flat(ring); SB.Big(ring); SB.SizeLife(ring, 0.95f, 1.05f); SB.ColorLife(ring, SB.Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var flames = SB.PS(fx, "RingFlames", new Vector3(0f, 0.05f, 0f), Mat("VFX_Flame_Add"), new Vector2(0.45f, 0.7f), new Vector2(0.18f, 0.3f),
                new Color(1f, 0.5f, 0.12f), 30, true);
            SB.Rate(flames, 20f); SB.Ring(flames, 0.78f, 0.05f); SB.Rise(flames, 0.4f, 0.9f); SB.Spin(flames, 120f); SB.SizeLife(flames, 1f, 0.25f); SB.ColorLife(flames, SB.Heat());
            var embers = SB.PS(fx, "Embers", new Vector3(0f, 0.2f, 0f), Mat("VFX_Spark_Add"), new Vector2(1.2f, 2f), new Vector2(0.04f, 0.08f),
                new Color(1f, 0.6f, 0.2f), 30, true);
            SB.Rate(embers, 11f); SB.Ring(embers, 0.55f, 1f); SB.Rise(embers, 0.8f, 1.8f); SB.Noise(embers, 0.4f, 1.2f); SB.ColorLife(embers, SB.Heat());
            var glow = SB.PS(fx, "GroundGlow", new Vector3(0f, 0.04f, 0f), Mat("VFX_SoftDot_Add"), new Vector2(1.8f, 2.2f), new Vector2(2.6f, 3f),
                new Color(1f, 0.4f, 0.08f, 0.22f), 3, true);
            SB.Rate(glow, 1.2f); SB.Flat(glow); SB.Big(glow); SB.ColorLife(glow, SB.Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
        }

        private static void IceFx(Transform fx)
        {
            var ring = SB.PS(fx, "FrostRing", new Vector3(0f, 0.05f, 0f), Mat("VFX_Ring_Add"), new Vector2(1.8f, 1.8f), new Vector2(3.3f, 3.3f),
                new Color(0.55f, 0.85f, 1f, 0.5f), 3, true);
            SB.Rate(ring, 1.1f); SB.Flat(ring); SB.Big(ring); SB.SizeLife(ring, 0.95f, 1.05f); SB.ColorLife(ring, SB.Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var snow = SB.PS(fx, "SnowFlurry", new Vector3(0f, 1.1f, 0f), Mat("VFX_SoftDot_Add"), new Vector2(2f, 3f), new Vector2(0.06f, 0.11f),
                new Color(0.9f, 0.97f, 1f, 0.9f), 50, true);
            SB.Rate(snow, 10f); SB.Sphere(snow, 0.85f, new Vector3(1f, 0.9f, 1f), 0.4f); SB.Orbit(snow, 1.3f, -0.2f); SB.Noise(snow, 0.25f, 0.8f);
            SB.ColorLife(snow, SB.Fade(Color.white, Color.white, 1f, 0.15f, 0.75f));
            var glints = SB.PS(fx, "FrostGlints", new Vector3(0f, 0.1f, 0f), Mat("VFX_Star_Add"), new Vector2(0.7f, 1f), new Vector2(0.12f, 0.2f),
                new Color(0.7f, 0.92f, 1f), 10, true);
            SB.Rate(glints, 5f); SB.Ring(glints, 0.75f, 0.1f); SB.SizeBump(glints); SB.Spin(glints, 90f);
            var mist = SB.PS(fx, "FrostMist", new Vector3(0f, 0.04f, 0f), Mat("VFX_SoftDot_Add"), new Vector2(2f, 2.5f), new Vector2(2.4f, 2.8f),
                new Color(0.45f, 0.75f, 1f, 0.18f), 3, true);
            SB.Rate(mist, 1.1f); SB.Flat(mist); SB.Big(mist); SB.ColorLife(mist, SB.Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
        }

        private static void EarthFx(Transform fx)
        {
            var pebbleMat = SB.ParticleLitMat(ModelDir + "Earth/Materials/S4_Earth_PebbleFx.mat", new Color(0.52f, 0.4f, 0.28f));
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var pebbles = SB.PS(fx, "FloatingPebbles", new Vector3(0f, 0.35f, 0f), pebbleMat, new Vector2(4f, 6f), new Vector2(0.07f, 0.15f),
                Color.white, 16, true);
            SB.Rate(pebbles, 3f); SB.Ring(pebbles, 0.8f, 0.15f); SB.MeshParticles(pebbles, cube);
            var rot = pebbles.rotationOverLifetime;
            rot.x = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f); rot.y = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f); rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            SB.Orbit(pebbles, 0.5f, 0.12f); SB.Noise(pebbles, 0.12f, 0.5f); SB.SizeBump(pebbles);
            var amber = SB.PS(fx, "AmberSparks", new Vector3(0f, 0.3f, 0f), Mat("VFX_Spark_Add"), new Vector2(1.2f, 1.8f), new Vector2(0.04f, 0.07f),
                new Color(1f, 0.62f, 0.15f), 16, true);
            SB.Rate(amber, 5f); SB.Ring(amber, 0.7f, 0.5f); SB.Rise(amber, 0.3f, 0.7f); SB.ColorLife(amber, SB.Fade(Color.white, Color.white, 1f, 0.1f, 0.6f));
            var dust = SB.PS(fx, "Dust", new Vector3(0f, 0.08f, 0f), Mat("VFX_Smoke_Alpha"), new Vector2(1.8f, 2.6f), new Vector2(0.4f, 0.7f),
                new Color(0.6f, 0.5f, 0.38f, 0.3f), 12, true);
            SB.Rate(dust, 3.5f); SB.Ring(dust, 0.65f, 0.3f); SB.Rise(dust, 0.08f, 0.25f); SB.SizeLife(dust, 0.6f, 1.4f); SB.Spin(dust, 30f);
            SB.ColorLife(dust, SB.Fade(Color.white, Color.white, 1f, 0.2f, 0.6f));
            var ring = SB.PS(fx, "StoneRing", new Vector3(0f, 0.05f, 0f), Mat("VFX_Ring_Add"), new Vector2(2f, 2f), new Vector2(3.4f, 3.4f),
                new Color(1f, 0.6f, 0.15f, 0.3f), 3, true);
            SB.Rate(ring, 1f); SB.Flat(ring); SB.Big(ring); SB.ColorLife(ring, SB.Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
        }

        private static void LightFx(Transform fx)
        {
            var rays = SB.PS(fx, "SunRays", new Vector3(0f, 0.05f, 0f), Mat("VFX_SoftDot_Add"), new Vector2(0.9f, 1.3f), new Vector2(0.1f, 0.16f),
                new Color(1f, 0.85f, 0.4f, 0.45f), 20, true);
            SB.Rate(rays, 9f); SB.Ring(rays, 0.7f, 0.3f); SB.Rise(rays, 1.2f, 2.2f); SB.Stretch(rays, 0.25f, 6f);
            SB.ColorLife(rays, SB.Fade(Color.white, Color.white, 1f, 0.2f, 0.55f));
            var motes = SB.PS(fx, "Motes", new Vector3(0f, 1f, 0f), Mat("VFX_Star_Add"), new Vector2(1.2f, 1.8f), new Vector2(0.1f, 0.18f),
                new Color(1f, 0.92f, 0.6f), 20, true);
            SB.Rate(motes, 7f); SB.Sphere(motes, 0.8f, new Vector3(1f, 1f, 1f), 0.3f); SB.Orbit(motes, 0.9f, 0.15f); SB.SizeBump(motes); SB.Spin(motes, 60f);
            var ring = SB.PS(fx, "SunRing", new Vector3(0f, 0.05f, 0f), Mat("VFX_Ring_Add"), new Vector2(1.8f, 1.8f), new Vector2(3.3f, 3.3f),
                new Color(1f, 0.82f, 0.35f, 0.5f), 3, true);
            SB.Rate(ring, 1.1f); SB.Flat(ring); SB.Big(ring); SB.SizeLife(ring, 0.95f, 1.05f); SB.ColorLife(ring, SB.Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var glow = SB.PS(fx, "SunGlow", new Vector3(0f, 0.04f, 0f), Mat("VFX_SoftDot_Add"), new Vector2(1.8f, 2.2f), new Vector2(2.6f, 3f),
                new Color(1f, 0.8f, 0.35f, 0.2f), 3, true);
            SB.Rate(glow, 1.2f); SB.Flat(glow); SB.Big(glow); SB.ColorLife(glow, SB.Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
        }

        // ---------------- Porträts ----------------

        // Wie portrait3_*: Frontansicht Oberkörper, Idle-Pose, transparent (SuperBuilder.RenderPortrait mit Stufe-4-Zustand)
        private static void RenderPortrait4(Element el)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + el.Prefab + ".prefab");
            if (prefab == null) return;
            // Rahmen aus den Bounds im Stufe-4-Zustand (Prefab-Raum, Blick zur Kamera = −Z)
            var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            tmp.transform.position = new Vector3(0f, -400f, 0f);
            ShowStage4(tmp);
            var b = new Bounds();
            bool has = false;
            var evo = tmp.GetComponent<BuddyEvolution>();
            foreach (var r in evo.HumanoidVisual.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (r.name.Contains("Wings")) continue; // breite Flügel nicht in die Rahmenbreite einrechnen
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            Object.DestroyImmediate(tmp);
            if (!has) return;
            Vector3 c = b.center - new Vector3(0f, -400f, 0f);
            float top = b.max.y + 400f;
            float h = b.size.y * 0.8f;
            const float fov = 30f;
            Vector3 lookAt = new Vector3(c.x, top - h * 0.5f, c.z);
            float dist = h * 0.5f * 1.06f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Vector3 camPos = lookAt + new Vector3(0f, h * 0.12f, -dist);
            string path = IconDir + "portrait4_" + el.Icon + ".png";
            SB.RenderPortrait(prefab, path, camPos, lookAt, fov, ShowStage4);
        }

        // Instanz (Editor) in den Stufe-4-Zustand: Humanoid ×Stage4Scale, Stufe-3- + Stufe-4-Extras an, Idle-Pose t = 0,5 s
        private static void ShowStage4(GameObject inst)
        {
            var evo = inst.GetComponent<BuddyEvolution>();
            if (evo == null) return;
            if (evo.Stage1Visual != null) evo.Stage1Visual.SetActive(false);
            if (evo.HumanoidVisual != null)
            {
                evo.HumanoidVisual.SetActive(true);
                evo.HumanoidVisual.transform.localScale *= evo.Stage4Scale;
            }
            if (evo.Stage3Extras != null) foreach (var e in evo.Stage3Extras) if (e != null) e.SetActive(true);
            if (evo.Stage4Extras != null) foreach (var e in evo.Stage4Extras) if (e != null) e.SetActive(true);
            if (evo.StageEffects != null) foreach (var e in evo.StageEffects) if (e != null) e.SetActive(false);
            var an = evo.HumanoidVisual != null ? evo.HumanoidVisual.GetComponentInChildren<Animator>(true) : null;
            var ac = an != null ? an.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
            var idle = ac != null ? ac.layers[0].stateMachine.defaultState.motion as AnimationClip : null;
            if (idle != null) idle.SampleAnimation(an.gameObject, 0.5f);
        }

        // BuddyInfoPanelUI.Stage4Sprites in der offenen Szene (Reihenfolge wie Stage3Sprites: portrait3_<x> → portrait4_<x>)
        private static void WirePortraits()
        {
            var panel = Object.FindFirstObjectByType<BuddyInfoPanelUI>(FindObjectsInactive.Include);
            if (panel == null) { Debug.LogWarning("Stage4Builder: kein BuddyInfoPanelUI in der Szene"); return; }
            var s3 = panel.Stage3Sprites ?? new Sprite[0];
            var s4 = new Sprite[s3.Length];
            for (int i = 0; i < s3.Length; i++)
            {
                if (s3[i] == null) continue;
                string n = s3[i].name.Replace("portrait3_", "portrait4_");
                s4[i] = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + n + ".png");
            }
            panel.Stage4Sprites = s4;
            EditorUtility.SetDirty(panel);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        }
    }
}
