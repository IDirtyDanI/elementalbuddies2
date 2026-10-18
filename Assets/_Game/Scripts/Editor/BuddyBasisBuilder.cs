using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SB = ElementalBuddies.EditorTools.SuperBuilder;

namespace ElementalBuddies.EditorTools
{
    // Standard-Buddies 2 (Paket B, eigene Blender-Modelle statt Meshy): baut Feuer/Eis/Erde/Licht.prefab auf die Modelle
    // Models/Characters/Buddies2/<El>Elemental2.fbx um. Wiederholbar (Menü BuddyTD → Buddies → Standard-Buddies 2 einbauen):
    //  1. Import: Humanoid (Avatar aus dem Modell), Clips <El>2_Idle (Loop) / <El>2_Cast / <El>2_Rest, Root-Bewegung in die Pose
    //     gebacken; eingebettete Materialien → URP-Lit unter Buddies2/Materials (Emission gedämpft, sonst brennt Bloom aus).
    //  2. Controller FireBuddy/IceBuddy/EarthBuddy/LightBuddy an Ort und Stelle (GUID bleibt): Idle → Cast (Trigger, Tempo 1).
    //  3. Prefab (GUID bleibt): neues Kind "Visual" wie bei den Fusionen (180° gedreht, ×0,78, 0,245 m auf dem Sockel), Stufe-3-Rüstung
    //     (Armor_*) an die neuen Knochen umgehängt, übrige Visual-Kinder (Lichter, Attackpoint …) übernommen, Feuerpunkte aus der
    //     Cast-Pose zum Trefferzeitpunkt (Hände) neu gesetzt, altes Meshy-Visual entfernt.
    //  4. Stufe 4 (Stage4Builder.BuildAll, inkl. portrait4_*) und Porträts portrait_* (Stufe 2) / portrait3_* (Stufe 3).
    public static class BuddyBasisBuilder
    {
        private const string ModelDir = "Assets/_Game/Models/Characters/Buddies2/";
        private const string MatDir = ModelDir + "Materials/";
        private const string PrefabDir = "Assets/_Game/Prefabs/";
        private const string AnimDir = "Assets/_Game/Animations/";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        public const float VisualScale = 0.78f;
        public static readonly Vector3 VisualPos = new Vector3(0f, 0.245f, 0f);

        // Stufe-3-Rüstung: Pivot = Knochenkopf in der Bind-Pose + Offset (Modellraum des Visuals, unskaliert), Drehung relativ zum
        // Visual, Skalierung relativ zum Visual (die Teile wurden für die Meshy-Figuren gebaut → an die neuen Körper angepasst)
        private class Armor
        {
            public string Name, Bone;
            public Vector3 Offset, Rot;
            public float Scale;
            public Armor(string name, string bone, Vector3 offset, float scale, Vector3 rot = default)
            { Name = name; Bone = bone; Offset = offset; Scale = scale; Rot = rot; }
        }

        private class Element
        {
            public string Name, Prefab, Controller, Icon;
            public bool HasFirePoint;      // Feuer/Eis/Licht: Feuerpunkt aus der Cast-Pose; Erde (Spott) braucht keinen
            public bool HandsTogether;     // Feuerpunkt zwischen beiden Händen (sonst die weiter vorn liegende Hand)
            public float Scale = 1f;       // Visual-Größe relativ zu VisualScale (Erde: Tank, deutlich wuchtiger)
            public Dictionary<string, float> Glow; // Emissions-Faktor auf die Blender-Emission (fehlt = 1)
            public Armor[] Armors;
            public string Fbx => ModelDir + Name + "Elemental2.fbx";
            public string Prefix => Name + "2";
        }

        private static readonly Element[] Elements =
        {
            new Element { Name = "Fire", Prefab = "Fire", Controller = "FireBuddy", Icon = "fire", HasFirePoint = true, HandsTogether = true,
                Glow = new Dictionary<string, float> { { "Fire2_Eye", 0.45f }, { "Fire2_Core", 0.6f }, { "Fire2_Body", 0.7f }, { "Fire2_Flame", 0.85f }, { "Fire2_Tip", 0.7f } },
                Armors = new[]
                {
                    new Armor("Armor_Fire_Helm", "Head", new Vector3(0f, 0.128f, 0f), 0.806f),
                    new Armor("Armor_Fire_Pauldron_L", "LeftArm", new Vector3(0f, 0.016f, 0f), 1.088f),
                    new Armor("Armor_Fire_Pauldron_R", "RightArm", new Vector3(0f, 0.016f, 0f), 1.088f),
                    new Armor("Armor_Fire_Chest", "Chest", new Vector3(0f, -0.024f, 0.169f), 0.967f),
                } },
            new Element { Name = "Ice", Prefab = "Ice", Controller = "IceBuddy", Icon = "ice", HasFirePoint = true,
                Glow = new Dictionary<string, float> { { "Ice2_Eye", 0.45f } },
                Armors = new[]
                {
                    new Armor("Armor_Ice_Crown", "Head", new Vector3(0f, 0.32f, 0f), 0.95f),
                    new Armor("Armor_Ice_Pauldron_L", "LeftArm", new Vector3(0f, 0.02f, 0f), 1.36f),
                    new Armor("Armor_Ice_Pauldron_R", "RightArm", new Vector3(0f, 0.02f, 0f), 1.36f),
                    new Armor("Armor_Ice_Chest", "Chest", new Vector3(0f, -0.03f, 0.097f), 1.21f),
                } },
            // Erde ×1,32 (#523): Figur 1,21 → ~1,60 m über dem Sockel (Fusionen ~1,93 m); Rüstung, Stufe 4, Feuerpunkte und
            // HP-Leiste ziehen über die Visual-Skalierung automatisch mit
            new Element { Name = "Earth", Prefab = "Earth", Controller = "EarthBuddy", Icon = "earth", Scale = 1.32f,
                Glow = new Dictionary<string, float> { { "Earth2_Eye", 0.5f }, { "Earth2_Glow", 0.9f } },
                Armors = new[]
                {
                    new Armor("Armor_Earth_Helm", "Head", Vector3.zero, 1.128f),
                    new Armor("Armor_Earth_Pauldron_L", "LeftArm", new Vector3(0f, 0.056f, 0f), 1.2f),
                    new Armor("Armor_Earth_Pauldron_R", "RightArm", new Vector3(0f, 0.056f, 0f), 1.2f),
                    new Armor("Armor_Earth_Chest", "Chest", new Vector3(0f, -0.034f, 0.24f), 1.128f),
                } },
            new Element { Name = "Light", Prefab = "Light", Controller = "LightBuddy", Icon = "light", HasFirePoint = true, HandsTogether = true,
                Glow = new Dictionary<string, float> { { "Light2_Eye", 0.6f }, { "Light2_Glow", 0.6f }, { "Light2_Band", 0.75f }, { "Light2_Body", 0.4f }, { "Light2_Gold", 0.6f }, { "Light2_Robe", 0.5f } },
                Armors = new[]
                {
                    // Armor_Light_Halo entfällt (#522): das Modell hat schon einen eingebauten Heiligenschein (sonst doppelt)
                    new Armor("Armor_Light_Circlet", "Head", new Vector3(0f, 0.06f, 0f), 1.15f),
                    new Armor("Armor_Light_Wings_L", "Chest", new Vector3(-0.088f, 0.029f, -0.19f), 2.2f),
                    new Armor("Armor_Light_Wings_R", "Chest", new Vector3(0.088f, 0.029f, -0.19f), 2.2f),
                } },
        };

        [MenuItem("BuddyTD/Buddies/Standard-Buddies 2 einbauen (Modelle, Prefabs, Stufe 4, Porträts)")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("BuddyBasisBuilder: nur im Edit-Mode bauen.");
                return;
            }
            ImportAll();
            ControllersAll();
            PrefabsAll();
            Stage4Builder.BuildAll();
            PortraitsAll();
            Debug.Log("BuddyBasisBuilder: Feuer, Eis, Erde, Licht auf Buddies2 umgebaut.");
        }

        public static void ImportAll() { foreach (var el in Elements) Import(el); AssetDatabase.SaveAssets(); }
        public static void ControllersAll() { foreach (var el in Elements) Controller(el); AssetDatabase.SaveAssets(); }
        public static void PrefabsAll() { foreach (var el in Elements) BuildPrefab(el); AssetDatabase.SaveAssets(); }
        public static void PortraitsAll() { foreach (var el in Elements) { RenderPortrait(el, 2); RenderPortrait(el, 3); } }

        // ---------------- Import ----------------

        private static void Import(Element el)
        {
            var imp = AssetImporter.GetAtPath(el.Fbx) as ModelImporter;
            if (imp == null) { Debug.LogError("BuddyBasisBuilder: kein Modell unter " + el.Fbx); return; }
            if (!AssetDatabase.IsValidFolder(ModelDir + "Materials")) AssetDatabase.CreateFolder(ModelDir.TrimEnd('/'), "Materials");
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.importCameras = false; imp.importLights = false;
            // MaterialDescription: liefert die Blender-Emission (ImportStandard verliert sie)
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                // Take "Fire2_rig|Fire2_Idle" → Clip "Fire2_Idle"
                int bar = c.takeName.LastIndexOf('|');
                c.name = bar >= 0 ? c.takeName.Substring(bar + 1) : c.takeName;
                c.loopTime = c.name.EndsWith("_Idle");
                c.loopPose = false;
                // Buddies stehen: Root-Drehung/-Höhe/-XZ in die Pose (Licht schwebt über die Hüfthöhe im Clip)
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            // Remaps kurz entfernen → eingebettete Blender-Materialien lesbar (Folge-Builds wenden die Faktoren neu an)
            foreach (var kv in imp.GetExternalObjectMap())
                if (kv.Value is Material && AssetDatabase.GetAssetPath(kv.Value).StartsWith(MatDir)) imp.RemoveRemap(kv.Key);
            imp.SaveAndReimport();
            EnsureChestMapped(imp, el);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(el.Fbx))
            {
                var em = o as Material;
                if (em == null) continue;
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name), ModelMaterial(em, el));
            }
            imp.SaveAndReimport();

            var avatar = LoadAvatar(el);
            if (avatar == null || !avatar.isValid || !avatar.isHuman) Debug.LogError("BuddyBasisBuilder: Avatar ungültig für " + el.Fbx);
            else
            {
                string upper = null;
                foreach (var hb in avatar.humanDescription.human) if (hb.humanName == "LeftUpperArm") upper = hb.boneName;
                if (upper != "LeftArm") Debug.LogWarning("BuddyBasisBuilder: LeftUpperArm ist " + upper + " statt LeftArm (" + el.Name + ")");
            }
        }

        // URP-Lit aus dem eingebetteten Material; Emission = Blender-Emission × Faktor (Bloom: helle Farben kippen sonst ins Weiße)
        private static Material ModelMaterial(Material embedded, Element el)
        {
            string p = MatDir + embedded.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            if (m.shader.name != "Universal Render Pipeline/Lit") m.shader = Shader.Find("Universal Render Pipeline/Lit");
            Color baseCol = embedded.HasProperty("_BaseColor") ? embedded.GetColor("_BaseColor") : embedded.color;
            baseCol.a = 1f;
            m.SetColor("_BaseColor", baseCol);
            float metal = embedded.HasProperty("_Metallic") ? embedded.GetFloat("_Metallic") : 0f;
            bool isMetal = metal > 0.3f || embedded.name.Contains("Gold");
            m.SetFloat("_Metallic", isMetal ? 0.5f : 0f);
            float smooth = embedded.HasProperty("_Smoothness") ? embedded.GetFloat("_Smoothness") : 0.3f;
            m.SetFloat("_Smoothness", Mathf.Clamp(isMetal ? Mathf.Max(smooth, 0.55f) : smooth, 0.05f, 0.6f));
            Color e = embedded.HasProperty("_EmissionColor") ? embedded.GetColor("_EmissionColor") : Color.black;
            float f;
            if (el.Glow == null || !el.Glow.TryGetValue(embedded.name, out f)) f = 1f;
            e *= f;
            e.a = 1f;
            if (e.maxColorComponent > 0.01f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", e);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // Die Auto-Zuordnung lässt den Knochen "Chest" bei Feuer/Eis/Erde weg (optional im Humanoid) → die Brust-Muskeln der Clips
        // gingen verloren. Zuordnung aus dem Avatar übernehmen und Chest ergänzen.
        private static void EnsureChestMapped(ModelImporter imp, Element el)
        {
            var avatar = LoadAvatar(el);
            if (avatar == null || !avatar.isHuman) return;
            var hd = avatar.humanDescription;
            var human = new List<HumanBone>(hd.human);
            foreach (var hb in human) if (hb.humanName == "Chest") return;
            var chest = new HumanBone { humanName = "Chest", boneName = "Chest" };
            chest.limit.useDefaultValues = true;
            human.Add(chest);
            hd.human = human.ToArray();
            imp.humanDescription = hd;
            imp.SaveAndReimport();
        }

        private static Avatar LoadAvatar(Element el)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(el.Fbx)) if (o is Avatar a) return a;
            return null;
        }

        // ---------------- Controller ----------------

        private static void Controller(Element el)
        {
            var idle = SB.Clip(el.Fbx, el.Prefix + "_Idle");
            var cast = SB.Clip(el.Fbx, el.Prefix + "_Cast");
            if (idle == null || cast == null) return;
            // Treffer-Frame liegt früh im Clip (Projektil kommt im selben Frame wie der Trigger) → Tempo 1; Cast→Cast bei schnellem Feuer
            SB.BuildController(AnimDir + el.Controller + ".controller", idle, cast, 1f, null);
        }

        // ---------------- Prefab ----------------

        private static void BuildPrefab(Element el)
        {
            string path = PrefabDir + el.Prefab + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var evo = root.GetComponent<BuddyEvolution>();
                if (evo == null || evo.HumanoidVisual == null) { Debug.LogError("BuddyBasisBuilder: BuddyEvolution/HumanoidVisual fehlt in " + path); return; }
                var old = evo.HumanoidVisual.transform;
                bool wasActive = old.gameObject.activeSelf;
                int sibling = old.GetSiblingIndex();
                old.name = "Visual_Old";

                // Neues Visual (Modell entpackt, wie die Fusionen)
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(el.Fbx);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = "Visual";
                var vis = go.transform;
                vis.SetParent(root.transform, false);
                vis.SetSiblingIndex(sibling);
                vis.localPosition = VisualPos;
                vis.localRotation = Quaternion.Euler(0f, 180f, 0f);
                vis.localScale = Vector3.one * (VisualScale * el.Scale);
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = old.gameObject.layer;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
                }
                var an = go.GetComponent<Animator>();
                if (an == null) an = go.AddComponent<Animator>();
                an.avatar = LoadAvatar(el);
                an.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimDir + el.Controller + ".controller");
                an.applyRootMotion = false;
                an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                // Stufe-3-Rüstung umhängen (vorhandene Objekte behalten → Materialien/Mesh bleiben)
                var armors = new List<GameObject>();
                foreach (var a in el.Armors)
                {
                    Transform piece = null;
                    foreach (var t in old.GetComponentsInChildren<Transform>(true)) if (t.name == a.Name) { piece = t; break; }
                    var bone = Bone(vis, a.Bone);
                    if (piece == null || bone == null) { Debug.LogError("BuddyBasisBuilder: " + a.Name + " / Knochen " + a.Bone + " fehlt (" + el.Name + ")"); continue; }
                    piece.SetParent(bone, false);
                    piece.position = bone.position + vis.TransformVector(a.Offset);
                    piece.rotation = vis.rotation * Quaternion.Euler(a.Rot);
                    Vector3 vs = vis.lossyScale * a.Scale, bs = bone.lossyScale;
                    piece.localScale = new Vector3(vs.x / bs.x, vs.y / bs.y, vs.z / bs.z);
                    piece.gameObject.layer = old.gameObject.layer;
                    piece.gameObject.SetActive(false);
                    armors.Add(piece.gameObject);
                }
                evo.Stage3Extras = armors.ToArray();

                // Übrige eigene Kinder des alten Visuals (Lichter, Attackpoint, Aura-Anker) übernehmen – Weltlage bleibt
                var keep = new List<Transform>();
                foreach (Transform c in old)
                {
                    if (c.GetComponent<SkinnedMeshRenderer>() != null || c.name == "Armature" || c.name.EndsWith("_rig")) continue;
                    keep.Add(c);
                }
                foreach (var c in keep) c.SetParent(vis, true);

                evo.HumanoidVisual = go;
                Object.DestroyImmediate(old.gameObject);
                go.SetActive(wasActive);

                PlaceFirePoints(el, root.transform, evo, vis, an);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Feuerpunkt Stufe 2/3 (FirePoint_S2) und Stufe 4 (FirePoint_S4, Visual ×Stage4Scale) an die Hände zum Trefferzeitpunkt des
        // Cast-Clips (Projektil startet im selben Frame wie der Trigger); Attackpoint vor die Brust.
        private static void PlaceFirePoints(Element el, Transform root, BuddyEvolution evo, Transform vis, Animator an)
        {
            var cast = SB.Clip(el.Fbx, el.Prefix + "_Cast");
            var saved = new List<(Transform t, Vector3 p, Quaternion r, Vector3 s)>();
            foreach (var t in vis.GetComponentsInChildren<Transform>(true)) saved.Add((t, t.localPosition, t.localRotation, t.localScale));
            Vector3 baseScale = vis.localScale;
            bool active = vis.gameObject.activeSelf;
            vis.gameObject.SetActive(true);
            try
            {
                Vector3 S2 = Muzzle(el, root, vis, an, cast);
                vis.localScale = baseScale * Stage4Builder.Stage4Scale;
                Vector3 S4 = Muzzle(el, root, vis, an, cast);
                vis.localScale = baseScale;
                foreach (var s in saved) { if (s.t == null) continue; s.t.localPosition = s.p; s.t.localRotation = s.r; s.t.localScale = s.s; }
                var chestBone = Bone(vis, "Chest");
                Vector3 chestBind = root.InverseTransformPoint(chestBone.position);
                foreach (Transform c in vis)
                    if (c.name.ToLowerInvariant().Contains("tackpoint")) c.position = root.TransformPoint(chestBind + new Vector3(0f, 0f, -0.25f));

                if (!el.HasFirePoint) return;
                var fp2 = root.Find("FirePoint_S2");
                if (fp2 == null) { fp2 = new GameObject("FirePoint_S2").transform; fp2.SetParent(root, false); }
                fp2.localPosition = S2; fp2.localRotation = Quaternion.identity;
                var fp4 = root.Find("FirePoint_S4");
                if (fp4 == null) { fp4 = new GameObject("FirePoint_S4").transform; fp4.SetParent(root, false); fp4.SetSiblingIndex(fp2.GetSiblingIndex() + 1); }
                fp4.localPosition = S4; fp4.localRotation = Quaternion.identity;
                var fp1 = root.Find("FirePoint_S1");
                evo.StageFirePoints = new[] { fp1, fp2, fp2, fp4 };
            }
            finally
            {
                vis.localScale = baseScale;
                vis.gameObject.SetActive(active);
            }
        }

        private static Vector3 Muzzle(Element el, Transform root, Transform vis, Animator an, AnimationClip cast)
        {
            if (cast == null) return new Vector3(0f, 1f, -0.3f);
            float hit = HitSeconds(el);
            cast.SampleAnimation(an.gameObject, hit);
            Vector3 l = root.InverseTransformPoint(Bone(vis, "LeftHand").position);
            Vector3 r = root.InverseTransformPoint(Bone(vis, "RightHand").position);
            Vector3 p = el.HandsTogether ? (l + r) * 0.5f : (l.z < r.z ? l : r); // vorn = −Z (Root-Raum)
            return p + new Vector3(0f, 0f, -0.1f);
        }

        // Trefferzeitpunkte laut Blender-Bericht (art-src/buddies/buddies_report.json)
        private static float HitSeconds(Element el)
        {
            switch (el.Name)
            {
                case "Fire": return 4f / 30f;
                case "Ice": return 4f / 30f;
                case "Earth": return 6f / 30f;
                default: return 2f / 30f;
            }
        }

        internal static Transform Bone(Transform visual, string name)
        {
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                if (t.name == name && t.GetComponent<Renderer>() == null) return t;
            return null;
        }

        // ---------------- Porträts ----------------

        // portrait_<x> (Stufe 2) bzw. portrait3_<x> (Stufe 3): Brustbild von vorn, Idle-Pose, transparent (SuperBuilder.RenderPortrait)
        private static void RenderPortrait(Element el, int stage)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + el.Prefab + ".prefab");
            if (prefab == null) return;
            System.Action<GameObject> show = inst => ShowStage(inst, stage);
            var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Vector3 origin = new Vector3(0f, -400f, 0f);
            tmp.transform.position = origin;
            show(tmp);
            var b = new Bounds();
            bool has = false;
            var evo = tmp.GetComponent<BuddyEvolution>();
            foreach (var r in evo.HumanoidVisual.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (r.name.Contains("Wings")) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            Object.DestroyImmediate(tmp);
            if (!has) return;
            Vector3 c = b.center - origin;
            float top = b.max.y - origin.y;
            float h = b.size.y * 0.7f;
            const float fov = 30f;
            Vector3 lookAt = new Vector3(c.x, top - h * 0.5f, c.z);
            float dist = h * 0.5f * 1.08f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Vector3 camPos = lookAt + new Vector3(0f, h * 0.12f, -dist);
            string file = (stage >= 3 ? "portrait3_" : "portrait_") + el.Icon + ".png";
            SB.RenderPortrait(prefab, IconDir + file, camPos, lookAt, fov, show);
        }

        // Instanz (Editor) in Stufe 2/3: Humanoid an, Stufe-3-Extras ab Stufe 3, Effekte aus, Idle-Pose t = 0,5 s
        private static void ShowStage(GameObject inst, int stage)
        {
            var evo = inst.GetComponent<BuddyEvolution>();
            if (evo == null) return;
            if (evo.Stage1Visual != null) evo.Stage1Visual.SetActive(false);
            if (evo.HumanoidVisual != null) evo.HumanoidVisual.SetActive(true);
            if (evo.Stage3Extras != null) foreach (var e in evo.Stage3Extras) if (e != null) e.SetActive(stage >= 3);
            if (evo.Stage4Extras != null) foreach (var e in evo.Stage4Extras) if (e != null) e.SetActive(false);
            if (evo.StageEffects != null) foreach (var e in evo.StageEffects) if (e != null) e.SetActive(false);
            var an = evo.HumanoidVisual != null ? evo.HumanoidVisual.GetComponentInChildren<Animator>(true) : null;
            var ac = an != null ? an.runtimeAnimatorController as AnimatorController : null;
            var idle = ac != null ? ac.layers[0].stateMachine.defaultState.motion as AnimationClip : null;
            if (idle != null) idle.SampleAnimation(an.gameObject, 0.5f);
        }
    }
}
