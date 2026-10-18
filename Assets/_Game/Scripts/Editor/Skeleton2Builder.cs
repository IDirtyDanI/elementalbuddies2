using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Fremd-Assets-Ersatz Paket C (2026-10-18): Skelett-Gegner mit eigenem Skelett Skeleton2 (art-src/enemies/skeletons.py)
    // statt Meshy „Skeletton.fbx“ + „Meshy_Merged_Animations.fbx“ + UndeadAsset-Waffen.
    // Menü: BuddyTD → Gegner → Skelette mit eigenem Modell bauen (Skeleton2). Idempotent (GUIDs von Prefabs/Controllern bleiben).
    //  1. Import: Skeleton2.fbx Humanoid mit eigenem Avatar (Jaw → Jaw; Root/BowNock/Arrow frei), Varianten
    //     (Runner/Swarm/Knight/Archer) und beide Clip-FBX (Skeleton2_Clips, Skeleton2_Attacks) Humanoid mit dem Avatar von
    //     Skeleton2 (CopyFromOther); Takes → Clips (Name = Take), Schleifen laut Sidecar, Wurzel fest (keine Root Motion).
    //     Bogen-Knochen BowNock/Arrow sind keine Humanoid-Knochen: Clip-Maske „CreateFromThisModel“ mit allen Transforms,
    //     damit ihre Kurven als Generic-Kurven mitkommen. Requisiten (Schild/Schwert/Bogen) ohne Rig.
    //     Materialien je Slot aus der Palette der Sidecars (Models/Enemies/Skeleton2/Materials/<Slot>.mat, URP/Lit, matt).
    //  2. Controller: Runner/Swarm/KnightAttack (EnemyAttackBuilder.BuildSkeletonControllers), RunnerVisual,
    //     SkeletonArcherVisual (Ziel-Idle, Lauf, Schuss); Boss-Controller über BossBuilder.RebuildSkeletonVisuals
    //  3. Prefabs Runner/Swarm/Knight/SkeletonArcher (GUID bleibt): Inhalt von „Visual“ (Skalierung 0,808 und Animator bleiben)
    //     durch die passende Variante ersetzt, Visual-Höhe = VisualY (Sohlen auf dem NavMesh-Boden) – Meshy-Armature samt
    //     Augen-Kugeln, Ritter-Rüstung/-Waffen und Bogenschützen-Bogen/-Köcher entfallen (bei den Varianten im Mesh). Wurzel-Komponenten bleiben unverändert.
    //  4. Icons (Wellenvorschau/Kodex) neu gerendert (PacingSetup.RenderIcon in die bestehenden Dateien)
    public static class Skeleton2Builder
    {
        public const string ModelDir = "Assets/_Game/Models/Enemies/Skeleton2/";
        public const string BaseFbx = ModelDir + "Skeleton2.fbx";
        public const string ClipsFbx = "Assets/_Game/Animations/Enemies/Skeleton2_Clips.fbx";
        public const string AttacksFbx = "Assets/_Game/Animations/Enemies/Skeleton2_Attacks.fbx";
        private const string MatDir = ModelDir + "Materials";
        private const string PrefabDir = "Assets/_Game/Prefabs/";
        private const string ConfigDir = "Assets/ScriptableObjects/Configs/";
        public const string RunnerVisualController = "Assets/_Game/Animations/RunnerVisual.controller";
        public const string ArcherController = "Assets/_Game/Animations/SkeletonArcherVisual.controller";

        // Gegner → Modell-Variante
        private static readonly string[,] Variants =
        {
            { "Runner", "Skeleton2_Runner" },
            { "Swarm", "Skeleton2_Swarm" },
            { "Knight", "Skeleton2_Knight" },
            { "SkeletonArcher", "Skeleton2_Archer" },
        };
        private static readonly string[] Props = { "Skeleton_Shield", "Skeleton_SwordBroken", "Skeleton_Bow" };

        [System.Serializable] private class SkelClip { public string name, take; public bool loop; public float seconds; }
        [System.Serializable] private class SkelSidecar { public string model; public SkelClip[] clips = new SkelClip[0]; public ForestEnemyBuilder.SideMat[] materials = new ForestEnemyBuilder.SideMat[0]; }

        [MenuItem("BuddyTD/Gegner/Skelette mit eigenem Modell bauen (Skeleton2)")]
        private static void BuildMenu() => Debug.Log(Build());

        public static string Build(bool renderIcons = true)
        {
            var sb = new StringBuilder("Skeleton2Builder:");
            if (EditorApplication.isPlayingOrWillChangePlaymode) return sb.Append(" nur im Edit-Modus.").ToString();
            if (!Import(sb)) return sb.ToString();
            EnemyAttackBuilder.BuildSkeletonControllers(sb);
            BuildRunnerVisual(sb);
            BuildArcherController(sb);
            SetArcherArrowHeight(sb);
            for (int i = 0; i < Variants.GetLength(0); i++)
                BuildPrefab(Variants[i, 0], ModelDir + Variants[i, 1] + ".fbx", sb);
            AssetDatabase.SaveAssets();
            if (renderIcons)
                for (int i = 0; i < Variants.GetLength(0); i++) RenderIcon(Variants[i, 0], sb);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // ---------------- 1. Import ----------------

        private static SkelSidecar ReadSidecar(string fbx)
        {
            string p = Path.ChangeExtension(fbx, null) + ".skel.json";
            if (!File.Exists(p)) return null;
            try { return JsonUtility.FromJson<SkelSidecar>(File.ReadAllText(p)); }
            catch (System.Exception e) { Debug.LogWarning("Skeleton2Builder: Sidecar " + p + ": " + e.Message); return null; }
        }

        public static Avatar SkeletonAvatar()
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(BaseFbx))
                if (o is Avatar a) return a;
            return null;
        }

        // Clip aus Skeleton2_Clips oder Skeleton2_Attacks (Name = Take)
        public static AnimationClip Clip(string name)
        {
            foreach (var fbx in new[] { ClipsFbx, AttacksFbx })
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                    if (o is AnimationClip c && c.name == name) return c;
            return null;
        }

        public static bool Import(StringBuilder sb)
        {
            if (AssetImporter.GetAtPath(BaseFbx) == null) { sb.Append("\n  FEHLER: " + BaseFbx + " fehlt"); return false; }
            ImportModel(BaseFbx, null, sb);
            var avatar = SkeletonAvatar();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) { sb.Append("\n  FEHLER: kein gültiger Humanoid-Avatar in " + BaseFbx); return false; }
            for (int i = 0; i < Variants.GetLength(0); i++) ImportModel(ModelDir + Variants[i, 1] + ".fbx", avatar, sb);
            foreach (var p in Props) ImportProp(ModelDir + p + ".fbx", sb);
            ImportClips(ClipsFbx, avatar, sb);
            ImportClips(AttacksFbx, avatar, sb);
            return true;
        }

        // Humanoid; avatar == null → eigener Avatar (Grundskelett), sonst CopyFromOther. Normals aus Blender (flache Facetten),
        // keine Takes (Modelle sind ohne Animation exportiert), Materialien je Slot aus der Palette
        private static void ImportModel(string fbx, Avatar avatar, StringBuilder sb)
        {
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) { sb.Append("\n  " + fbx + " fehlt"); return; }
            var side = ReadSidecar(fbx);
            bool dirty = imp.animationType != ModelImporterAnimationType.Human
                         || imp.avatarSetup != (avatar == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther)
                         || (avatar != null && imp.sourceAvatar != avatar) || imp.importAnimation || imp.optimizeGameObjects
                         || imp.importNormals != ModelImporterNormals.Import || imp.addCollider || imp.importCameras || imp.importLights;
            if (dirty)
            {
                imp.useFileScale = true;
                imp.globalScale = 1f;
                imp.addCollider = false;
                imp.importNormals = ModelImporterNormals.Import;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importBlendShapes = false;
                imp.importVisibility = false;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.Human;
                imp.optimizeGameObjects = false;
                if (avatar == null) imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                else { imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; imp.sourceAvatar = avatar; }
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                imp.SaveAndReimport();
            }
            if (RemapMaterials(imp, side)) imp.SaveAndReimport();
            Avatar own = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Avatar a) own = a;
            bool valid = avatar == null ? own != null && own.isValid && own.isHuman : imp.sourceAvatar == avatar;
            sb.Append($"\n  Import {fbx}: Humanoid {(avatar == null ? "eigener Avatar" : "Avatar " + avatar.name)}, gültig {(valid ? "ja" : "NEIN")}");
            if (avatar == null && own != null)
            {
                // Jaw muss gemappt sein, Root/BowNock/Arrow bleiben frei
                var map = new Dictionary<string, string>();
                foreach (var hb in imp.humanDescription.human) map[hb.humanName] = hb.boneName;
                sb.Append($" ({map.Count} Humanoid-Knochen, Jaw → {(map.TryGetValue("Jaw", out var j) ? j : "–")})");
            }
        }

        private static void ImportProp(string fbx, StringBuilder sb)
        {
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) return;
            if (imp.animationType != ModelImporterAnimationType.None || imp.importAnimation || imp.addCollider || imp.importNormals != ModelImporterNormals.Import)
            {
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.addCollider = false;
                imp.importNormals = ModelImporterNormals.Import;
                imp.importCameras = false;
                imp.importLights = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                imp.SaveAndReimport();
            }
            if (RemapMaterials(imp, ReadSidecar(fbx))) imp.SaveAndReimport();
            sb.Append("\n  Import " + fbx + ": Requisite ohne Rig");
        }

        // Clip-FBX: Humanoid CopyFromOther, keine Materialien; Takes → Clips (Name = Take nach dem „|“), Schleifen laut Sidecar,
        // Wurzel in die Pose gebacken (in place), Maske mit allen Transforms (BowNock/Arrow als Generic-Kurven)
        private static void ImportClips(string fbx, Avatar avatar, StringBuilder sb)
        {
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) { sb.Append("\n  " + fbx + " fehlt"); return; }
            var side = ReadSidecar(fbx);
            if (imp.animationType != ModelImporterAnimationType.Human || imp.avatarSetup != ModelImporterAvatarSetup.CopyFromOther
                || imp.sourceAvatar != avatar || !imp.importAnimation || imp.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                imp.importCameras = false;
                imp.importLights = false;
                imp.importBlendShapes = false;
                imp.useFileScale = true;
                imp.globalScale = 1f;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = true;
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                imp.sourceAvatar = avatar;
                imp.optimizeGameObjects = false;
                imp.resampleCurves = true;
                imp.animationCompression = ModelImporterAnimationCompression.Optimal;
                imp.SaveAndReimport();
            }

            var list = new List<ModelImporterClipAnimation>();
            foreach (var d in imp.defaultClipAnimations)
            {
                string n = d.takeName ?? d.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                d.name = n;
                bool loop = false;
                if (side != null) foreach (var c in side.clips) if (c.name == n || c.take == n) loop = c.loop;
                d.loopTime = loop;
                d.loopPose = false; // erster = letzter Frame ist in Blender schon identisch
                d.lockRootRotation = true;
                d.lockRootHeightY = true;
                d.lockRootPositionXZ = true;
                d.keepOriginalOrientation = true;
                d.keepOriginalPositionY = true;
                d.keepOriginalPositionXZ = true;
                d.maskType = ClipAnimationMaskType.CreateFromThisModel;
                ActivateAllTransforms(d);
                list.Add(d);
            }
            if (!SameClips(imp.clipAnimations, list))
            {
                imp.clipAnimations = list.ToArray();
                imp.SaveAndReimport();
            }
            sb.Append($"\n  Clips {fbx}:");
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview")) continue;
                bool bow = false;
                foreach (var b in AnimationUtility.GetCurveBindings(c)) if (b.path.EndsWith("BowNock") || b.path.EndsWith("Arrow")) { bow = true; break; }
                sb.Append($" {c.name} {c.length:0.00}s{(c.isLooping ? " L" : "")}{(c.isHumanMotion ? "" : " NICHT-HUMANOID")}{(bow ? " +Bogen" : "")};");
            }
        }

        // Humanoid-Import schaltet Nicht-Humanoid-Knochen in der Transform-Maske ab – alle aktivieren (BowNock/Arrow, Jaw)
        private static bool ActivateAllTransforms(ModelImporterClipAnimation d)
        {
            var mask = new AvatarMask();
            try
            {
                d.ConfigureMaskFromClip(ref mask);
                bool changed = false;
                for (int i = 0; i < mask.transformCount; i++)
                    if (!mask.GetTransformActive(i)) { mask.SetTransformActive(i, true); changed = true; }
                if (changed) d.ConfigureClipFromMask(mask);
                return changed;
            }
            finally { Object.DestroyImmediate(mask); }
        }

        private static bool SameClips(ModelImporterClipAnimation[] a, List<ModelImporterClipAnimation> b)
        {
            if (a == null || a.Length != b.Count) return false;
            foreach (var x in a) if (ActivateAllTransforms(x)) return false;
            for (int i = 0; i < a.Length; i++)
            {
                var x = a[i]; var y = b[i];
                if (x.name != y.name || x.takeName != y.takeName || x.loopTime != y.loopTime || x.loopPose != y.loopPose
                    || !x.lockRootRotation || !x.lockRootHeightY || !x.lockRootPositionXZ || !x.keepOriginalOrientation
                    || !x.keepOriginalPositionY || !x.keepOriginalPositionXZ || x.maskType != y.maskType) return false;
            }
            return true;
        }

        private static bool RemapMaterials(ModelImporter imp, SkelSidecar side)
        {
            if (side == null || side.materials == null) return false;
            NetworkSetup.EnsureFolder(MatDir);
            var map = imp.GetExternalObjectMap();
            bool changed = false;
            foreach (var m in side.materials)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                var mat = PaletteMaterial(m);
                if (map.TryGetValue(id, out var cur) && cur == mat) continue;
                imp.AddRemap(id, mat);
                changed = true;
            }
            return changed;
        }

        // wie ForestEnemyBuilder.EnemyMaterial: URP/Lit, opak, matt, GPU-Instancing; emissive Slots (Augen) leuchten
        private static Material PaletteMaterial(ForestEnemyBuilder.SideMat sm)
        {
            string p = MatDir + "/" + sm.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            bool created = m == null;
            if (created)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = sm.name };
                AssetDatabase.CreateAsset(m, p);
            }
            if (!ColorUtility.TryParseHtmlString(sm.hex, out var c)) c = Color.magenta;
            if (!created && m.GetColor("_BaseColor") == c) return m;
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", 2f);
            m.enableInstancing = true;
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            if (sm.emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * sm.emission);
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

        public static Material Palette(string slot) => AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + slot + ".mat");

        // ---------------- 2. Controller ----------------

        // RunnerVisual (nur Lauf, z. B. Vorschau-Figuren): Skeleton_Run, Tempo 1
        private static void BuildRunnerVisual(StringBuilder sb)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(RunnerVisualController);
            if (ac == null) { sb.Append("\n  " + RunnerVisualController + " fehlt"); return; }
            foreach (var s in ac.layers[0].stateMachine.states)
                if (s.state.name == "Running") { s.state.motion = Clip("Skeleton_Run"); s.state.speed = 1f; }
            EditorUtility.SetDirty(ac);
            sb.Append("\n  Controller " + RunnerVisualController + ": Running = Skeleton_Run (Tempo 1)");
        }

        // Bogenschütze: Idle = SkeletonArcher_Aim (Schleife), Run = SkeletonArcher_Run (3,71 m/s Modell × 0,808 = 3 m/s =
        // Config.Speed → Tempo 1), Shoot = SkeletonArcher_Shoot (Loslassen 0,35 s = AttackWindup, Tempo 1)
        private static void BuildArcherController(StringBuilder sb)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ArcherController);
            if (ac == null) { sb.Append("\n  " + ArcherController + " fehlt"); return; }
            foreach (var s in ac.layers[0].stateMachine.states)
            {
                switch (s.state.name)
                {
                    case "Idle": s.state.motion = Clip("SkeletonArcher_Aim"); s.state.speed = 1f; break;
                    case "Run": s.state.motion = Clip("SkeletonArcher_Run"); s.state.speed = 1f; break;
                    case "Shoot": s.state.motion = Clip("SkeletonArcher_Shoot"); s.state.speed = 1f; break;
                }
            }
            foreach (var s in ac.layers[0].stateMachine.states)
                if (s.state.motion == null) sb.Append("\n  Warnung: " + ArcherController + "/" + s.state.name + " ohne Clip");
            EditorUtility.SetDirty(ac);
            sb.Append("\n  Controller " + ArcherController + ": Idle SkeletonArcher_Aim, Run SkeletonArcher_Run, Shoot SkeletonArcher_Shoot (Tempo 1)");
        }

        // Pfeil startet an der Sehne statt 1,3 m über dem Pivot (#360): BowNock beim Loslassen (SkeletonArcher_Shoot 0,35 s)
        // 0,95 m über dem Boden bei Pivot 1,0 m (baseOffset 1, Visual y −1,005) → −0,05 (mit dem alten Visual y −0,744: 0,2). Optik-Feld (ThemeParityTests ignoriert es);
        // die Sichtlinien-Prüfung läuft damit ebenfalls auf Sehnenhöhe (wie beim Goblin-Bogenschützen, −0,1).
        public const float ArcherArrowHeight = -0.05f;

        private static void SetArcherArrowHeight(StringBuilder sb)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + "SkeletonArcher.asset");
            if (cfg == null || Mathf.Approximately(cfg.ProjectileSpawnHeight, ArcherArrowHeight)) return;
            cfg.ProjectileSpawnHeight = ArcherArrowHeight;
            EditorUtility.SetDirty(cfg);
            sb.Append($"\n  SkeletonArcher.ProjectileSpawnHeight = {ArcherArrowHeight:0.##} (Sehne beim Loslassen)");
        }

        // ---------------- 3. Prefabs ----------------

        private static void BuildPrefab(string name, string fbx, StringBuilder sb)
        {
            string path = PrefabDir + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { sb.Append("\n  " + path + " fehlt"); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var visual = root.transform.Find("Visual");
                if (visual == null) { sb.Append("\n  " + path + ": kein Visual"); return; }
                var removed = ReplaceVisual(visual, fbx);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                sb.Append($"\n  Prefab {path}: Visual ← {Path.GetFileNameWithoutExtension(fbx)} (entfernt: {removed})");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Inhalt von „Visual“ (Animator-Wurzel) durch die Modell-Hierarchie der FBX ersetzen: Mesh + Skeleton2_Rig direkt unter
        // Visual (Pfade wie in der FBX → Avatar und Generic-Kurven greifen), Animator-Avatar = Skeleton2. Visual-Höhe = VisualY,
        // Drehung/Maßstab und Animator-Einstellungen von Visual bleiben. Gibt die Namen der entfernten Anbauteile zurück.
        // Höhe von „Visual“ im Wurzel-Raum: Wurzel = Boden + NavMeshAgent.baseOffset (1, skaliert mit VisualScale), Sohlen
        // von Skeleton2 (Idle/Lauf, Maßstab 0,808) liegen 0,005 über dem Visual-Ursprung → −1,005 setzt sie auf den Boden.
        // Früher −0,744 (von Meshy übernommen): Skelette schwebten 0,26 m × VisualScale (gemessen 2026-10-18).
        public const float VisualY = -1.005f;

        public static string ReplaceVisual(Transform visual, string fbx)
        {
            var lp = visual.localPosition;
            visual.localPosition = new Vector3(lp.x, VisualY, lp.z);
            var removed = new List<string>();
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                if (t != visual && (t.GetComponent<MeshRenderer>() != null) && t.parent.GetComponent<MeshRenderer>() == null) removed.Add(t.name);
            for (int i = visual.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visual.GetChild(i).gameObject);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            try
            {
                var kids = new List<Transform>();
                foreach (Transform c in inst.transform) kids.Add(c);
                foreach (var c in kids) c.SetParent(visual, false);
            }
            finally { Object.DestroyImmediate(inst); }
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = visual.gameObject.layer;

            var anim = visual.GetComponent<Animator>();
            if (anim == null) anim = visual.gameObject.AddComponent<Animator>();
            anim.avatar = SkeletonAvatar();
            anim.applyRootMotion = false;
            foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Bounds großzügig (Angriffe/Bogen strecken die Arme), sonst schneidet das Frustum-Culling ab
                var lb = smr.localBounds;
                lb.Expand(0.15f / Mathf.Max(1e-4f, smr.rootBone != null ? smr.rootBone.lossyScale.x / visual.lossyScale.x : 1f));
                smr.localBounds = lb;
                smr.updateWhenOffscreen = false;
            }
            return removed.Count > 0 ? string.Join(", ", removed) : "–";
        }

        // ---------------- 4. Icons ----------------

        // Icon (Wellenvorschau, Kodex, Boss-Porträt) in die bestehende Datei der Config neu rendern; name relativ zu Configs/
        public static void RenderIcon(string name, StringBuilder sb)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + name + ".asset");
            if (cfg == null || cfg.Prefab == null) return;
            string iconPath = cfg.Icon != null ? AssetDatabase.GetAssetPath(cfg.Icon) : "Assets/_Game/UI/Icons/icon_enemy_" + Path.GetFileName(name).ToLowerInvariant() + ".png";
            PacingSetup.RenderIcon(cfg.Prefab, iconPath);
            AssetDatabase.ImportAsset(iconPath);
            var ti = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
            cfg.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            EditorUtility.SetDirty(cfg);
            sb.Append("\n  Icon " + iconPath);
        }
        // ---------------- Prüfung: Kontaktbogen ----------------

        // Zellen (Prefab, Zustand des Controllers oder null = Default, Zeit in s Spielzeit, Gier in Grad); Pose per
        // clip.SampleAnimation (Zustandstempo berücksichtigt), gleiche Kamera-Entfernung für alle Zellen (größte Höhe),
        // Spielmaßstab = Config.VisualScale. Ergebnis als PNG (z. B. Wiki-Bilder).
        public static string RenderSheet(string outPath, IList<(string prefab, string state, float time, float yaw)> cells, int cols, int cell = 256)
        {
            var log = new StringBuilder("Kontaktbogen " + outPath + ":");
            int rows = Mathf.CeilToInt(cells.Count / (float)cols);
            var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
            var bg = new Color[cols * cell * rows * cell];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color(0.25f, 0.27f, 0.3f);
            sheet.SetPixels(bg);
            var camGo = new GameObject("__SkelSheetCam") { hideFlags = HideFlags.HideAndDontSave };
            var key = new GameObject("__SkelSheetKey") { hideFlags = HideFlags.HideAndDontSave };
            var fill = new GameObject("__SkelSheetFill") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var insts = new List<GameObject>();
            try
            {
                var l1 = key.AddComponent<Light>(); l1.type = LightType.Directional; l1.intensity = 1.3f; l1.color = new Color(1f, 0.97f, 0.92f);
                key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                var l2 = fill.AddComponent<Light>(); l2.type = LightType.Directional; l2.intensity = 0.5f; l2.color = new Color(0.85f, 0.9f, 1f);
                fill.transform.rotation = Quaternion.Euler(10f, 230f, 0f);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 26f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f; cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.32f, 0.36f, 0.40f);
                var bounds = new List<Bounds>();
                float maxH = 0f;
                for (int i = 0; i < cells.Count; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(cells[i].prefab);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.hideFlags = HideFlags.HideAndDontSave;
                    inst.transform.position = new Vector3(0f, -800f, 0f);
                    var brain = inst.GetComponent<EnemyBrain>();
                    if (brain != null && brain.Config != null && brain.Config.VisualScale > 0f) inst.transform.localScale *= brain.Config.VisualScale;
                    foreach (var ps in inst.GetComponentsInChildren<ParticleSystemRenderer>(true)) ps.enabled = false;
                    foreach (var cv in inst.GetComponentsInChildren<Canvas>(true)) cv.enabled = false;
                    log.Append("\n  " + Sample(inst, cells[i].state, cells[i].time));
                    var b = new Bounds(inst.transform.position, Vector3.zero); bool any = false;
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                        if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
                    }
                    bounds.Add(b);
                    maxH = Mathf.Max(maxH, b.size.y);
                    inst.SetActive(false);
                    insts.Add(inst);
                }
                for (int i = 0; i < insts.Count; i++)
                {
                    var inst = insts[i];
                    inst.SetActive(true);
                    Sample(inst, cells[i].state, cells[i].time);
                    var b = bounds[i];
                    float dist = maxH * 0.62f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    Vector3 dir = Quaternion.Euler(0f, cells[i].yaw, 0f) * Vector3.forward + Vector3.up * 0.25f;
                    Vector3 center = new Vector3(inst.transform.position.x, b.min.y + maxH * 0.5f, inst.transform.position.z);
                    cam.transform.position = center + dir.normalized * dist;
                    cam.transform.LookAt(center);
                    cam.Render();
                    inst.SetActive(false);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(cell, cell, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, cell, cell), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    sheet.SetPixels((i % cols) * cell, (rows - 1 - i / cols) * cell, cell, cell, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }
                cam.targetTexture = null;
                sheet.Apply();
                File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            }
            finally
            {
                foreach (var i in insts) if (i != null) Object.DestroyImmediate(i);
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(key); Object.DestroyImmediate(fill);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet);
            }
            return log.ToString();
        }

        private static string Sample(GameObject inst, string state, float time)
        {
            var anim = inst.GetComponentInChildren<Animator>(true);
            if (anim == null) return inst.name + ": kein Animator";
            anim.Rebind();
            anim.Update(0f);
            var ac = anim.runtimeAnimatorController as AnimatorController;
            if (ac == null) return inst.name + ": kein Controller";
            AnimatorState st = ac.layers[0].stateMachine.defaultState;
            if (state != null) foreach (var s in ac.layers[0].stateMachine.states) if (s.state.name == state) st = s.state;
            var clip = st != null ? st.motion as AnimationClip : null;
            if (clip == null) return inst.name + ": kein Clip für " + state;
            float ct = time * Mathf.Abs(st.speed);
            if (clip.isLooping && clip.length > 0f) ct %= clip.length;
            clip.SampleAnimation(anim.gameObject, Mathf.Min(ct, clip.length));
            return $"{inst.name.Replace("(Clone)", "")} {st.name}: {clip.name} {ct:0.00}/{clip.length:0.00} s";
        }
    }
}
