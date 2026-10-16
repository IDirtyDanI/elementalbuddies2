using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies.EditorTools
{
    // Flieger-Builder (Walddorf). Etappe 1 (C-Flieger): Configs Flyer_Wyvern/Flyer_Bat (Rolle Flyer, gleiche Werte) und
    // Platzhalter aus dem Phönix-Modell. Etappe 2 (A-Tiere): echte Modelle Models/Enemies/Wyvern/Wyvern.fbx und
    // Models/Enemies/Bat/Bat.fbx (art-src/enemies/tiere_run.py, Import + Controller: AnimalModels).
    // Menü: BuddyTD → Walddorf → Flieger bauen (Wyvern, Fledermaus). Idempotent.
    //  - Prefabs heißen seit Etappe 2 Prefabs/Enemies/Flyer_Wyvern.prefab / Flyer_Bat.prefab (per MoveAsset aus
    //    Flyer_Placeholder_*: GUID und GlobalObjectIdHash bleiben). Vorhandene Prefabs werden nur im Kind „Visual“
    //    ersetzt (LoadPrefabContents) – Wurzel (Kapsel, Agent aus, EnemyBrain, FlyingMotor, FlyerHurtbox, FlyerShadow,
    //    NetworkObject/NetworkTransform/EnemyNet) bleibt unangetastet.
    //  - Visual = Modell-Instanz mit Animator (Generic-Avatar, Controller <Name>.controller: FlyLoop, Attack, Die),
    //    Körpermitte am Pivot (Flughöhe setzt der FlyingMotor). Ohne FBX: Phönix-Platzhalter (Rückfall).
    //  - Configs: bestehende behalten alle Balance-Werte; gesetzt werden nur Prefab und DeathClipDuration (Absturz-Clip).
    //    Fehlt eine Config, wird sie mit den Werten aus Etappe 1 angelegt.
    //  - Tod: EnemyBrain.PlayDeathVisual löst das Modell, EnemyCorpse lässt es mit 18 m/s² auf den Untergrund fallen;
    //    der Clip *_Die läuft in place (Trudeln, ab 0,5 s liegend, Root +0,14 m = Bauch auf dem Boden).
    // Netz: NetworkObject + server-autoritative NetworkTransform (Position inkl. Höhe, Gierwinkel) + EnemyNet wie
    // NetSetupEnemies. Die Netz-Prefab-Liste baut nur der Integrator (BuddyTD → Netzwerk → Prefab-Liste aktualisieren).
    public static class FlyerPlaceholderBuilder
    {
        public const string ConfigDir = "Assets/ScriptableObjects/Configs/";
        public const string WyvernConfigPath = ConfigDir + "Flyer_Wyvern.asset";
        public const string BatConfigPath = ConfigDir + "Flyer_Bat.asset";
        public const string PrefabDir = "Assets/_Game/Prefabs/Enemies/";
        public const string WyvernPrefabPath = PrefabDir + "Flyer_Wyvern.prefab";
        public const string BatPrefabPath = PrefabDir + "Flyer_Bat.prefab";
        private const string OldWyvernPrefabPath = PrefabDir + "Flyer_Placeholder_Wyvern.prefab";
        private const string OldBatPrefabPath = PrefabDir + "Flyer_Placeholder_Bat.prefab";
        // Länge des Absturz-Clips + kurzes Liegen (Clip 1,1 s; EnemyCorpse entfernt das Modell danach)
        public const float DeathClipDuration = 1.2f;
        public const string AssetDir = PrefabDir + "FlyerPlaceholder/";
        private const string PhoenixModel = "Assets/_Game/Models/Super/Phoenix/Phoenix.fbx";
        private const string RunnerPrefab = "Assets/_Game/Prefabs/Runner.prefab";
        private const string RunnerConfig = ConfigDir + "Runner.asset";

        // Spannweite des Platzhalters (Walddorf-Assets: Wyvern 2,6 m; Fledermaus gleich groß)
        private const float Wingspan = 2.4f;

        [MenuItem("BuddyTD/Walddorf/Flieger bauen (Wyvern, Fledermaus)")]
        public static void BuildAll() => Debug.Log(Build());

        public static string Build()
        {
            var sb = new System.Text.StringBuilder("[FlyerBuilder]");
            NetworkSetup.EnsureFolder(AssetDir.TrimEnd('/'));
            Rename(OldWyvernPrefabPath, WyvernPrefabPath, sb);
            Rename(OldBatPrefabPath, BatPrefabPath, sb);
            var wyvernCfg = EnsureConfig(WyvernConfigPath, "Mini-Wyvern",
                "Fliegt über Häuser und Buddies direkt zum Nexus – stößt herab, um zuzuschlagen");
            var batCfg = EnsureConfig(BatConfigPath, "Riesen-Fledermaus",
                "Fliegt über Häuser und Buddies direkt zum Nexus – stößt herab, um zuzuschlagen");

            var wyvern = BuildFlyer(WyvernPrefabPath, "Flyer_Wyvern", wyvernCfg, AnimalModels.Wyvern,
                new Color(0.36f, 0.58f, 0.27f), new Color(0.55f, 0.72f, 0.35f), sb);
            var bat = BuildFlyer(BatPrefabPath, "Flyer_Bat", batCfg, AnimalModels.Bat,
                new Color(0.22f, 0.13f, 0.29f), new Color(0.33f, 0.2f, 0.42f), sb);

            wyvernCfg.Prefab = wyvern;
            batCfg.Prefab = bat;
            EditorUtility.SetDirty(wyvernCfg);
            EditorUtility.SetDirty(batCfg);
            AssetDatabase.SaveAssets();
            RenderIcon(wyvernCfg, sb);
            RenderIcon(batCfg, sb);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // ---------------- Icon (Wellenvorschau/Kodex) ----------------
        // Eigene Kamera für Flieger: schräg von oben (Flügel ausgebreitet lesbar, wie aus der Spielkamera) und die volle
        // Spannweite im Bild. Gleiche Datei/Technik wie PacingSetup.RenderIcon (Schwarz/Weiß-Differenz → Alpha,
        // 4× Supersampling, 128²): icon_enemy_flyer_<tier>.png, Config.Icon zeigt darauf.
        private const int IconSize = 128, IconSuper = 4;

        public static void RenderIcon(EnemyConfigSO cfg, System.Text.StringBuilder sb)
        {
            if (cfg == null || cfg.Prefab == null) return;
            string path = "Assets/_Game/UI/Icons/icon_enemy_" + cfg.name.ToLowerInvariant() + ".png";
            bool existed = System.IO.File.Exists(path);
            int n = IconSize * IconSuper;
            var holder = new GameObject("__FlyerIconHolder") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.position = new Vector3(0f, -600f, 0f);
            var camGo = new GameObject("__FlyerIconCam") { hideFlags = HideFlags.HideAndDontSave };
            var key = new GameObject("__FlyerIconKey") { hideFlags = HideFlags.HideAndDontSave };
            var fill = new GameObject("__FlyerIconFill") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(n, n, 24, RenderTextureFormat.ARGB32);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(cfg.Prefab);
                inst.hideFlags = HideFlags.HideAndDontSave;
                inst.transform.SetParent(holder.transform, false);
                foreach (var ps in inst.GetComponentsInChildren<ParticleSystemRenderer>(true)) ps.enabled = false;
                // genaue Hülle aus dem gebackenen Skinned Mesh (die importierten Bounds gelten für die Bindpose)
                bool any = false;
                var b = new Bounds(inst.transform.position, Vector3.zero);
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m = new Mesh();
                    smr.BakeMesh(m, true);
                    foreach (var v in m.vertices)
                    {
                        var w = smr.transform.TransformPoint(v);
                        if (any) b.Encapsulate(w); else { b = new Bounds(w, Vector3.zero); any = true; }
                    }
                    Object.DestroyImmediate(m);
                }
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                    if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }

                var l1 = key.AddComponent<Light>();
                l1.type = LightType.Directional; l1.intensity = 1.3f; l1.color = new Color(1f, 0.97f, 0.92f);
                key.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
                var l2 = fill.AddComponent<Light>();
                l2.type = LightType.Directional; l2.intensity = 0.55f; l2.color = new Color(0.85f, 0.9f, 1f);
                fill.transform.rotation = Quaternion.Euler(20f, 230f, 0f);

                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 20f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                Vector3 dir = (Quaternion.Euler(0f, 20f, 0f) * inst.transform.forward + Vector3.up * 1.0f).normalized;
                float radius = b.extents.magnitude * 0.82f;
                float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                cam.transform.position = b.center + dir * dist;
                cam.transform.LookAt(b.center);

                var black = IconGrab(cam, rt, Color.black);
                var white = IconGrab(cam, rt, Color.white);
                var outPx = new Color[IconSize * IconSize];
                for (int y = 0; y < IconSize; y++)
                    for (int x = 0; x < IconSize; x++)
                    {
                        float r = 0f, g = 0f, bl = 0f, al = 0f;
                        for (int sy = 0; sy < IconSuper; sy++)
                            for (int sx = 0; sx < IconSuper; sx++)
                            {
                                int i = (y * IconSuper + sy) * n + (x * IconSuper + sx);
                                Color cb = black[i], cw = white[i];
                                al += 1f - Mathf.Clamp01(((cw.r - cb.r) + (cw.g - cb.g) + (cw.b - cb.b)) / 3f);
                                r += cb.r; g += cb.g; bl += cb.b;
                            }
                        float k = 1f / (IconSuper * IconSuper);
                        al *= k;
                        outPx[y * IconSize + x] = al > 0.001f ? new Color(Mathf.Clamp01(r * k / al), Mathf.Clamp01(g * k / al), Mathf.Clamp01(bl * k / al), al) : new Color(0f, 0f, 0f, 0f);
                    }
                var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                tex.SetPixels(outPx);
                tex.Apply();
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
            }
            finally
            {
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(key);
                Object.DestroyImmediate(fill);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && (!existed || imp.textureType != TextureImporterType.Sprite))
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null && cfg.Icon != sprite) { cfg.Icon = sprite; EditorUtility.SetDirty(cfg); }
            sb.Append($"\n  Icon {path}");
        }

        private static Color[] IconGrab(Camera cam, RenderTexture rt, Color bg)
        {
            cam.backgroundColor = bg;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return px;
        }

        // Umbenennen per MoveAsset: GUID (Netz-Liste, Configs) und GlobalObjectIdHash bleiben gleich
        private static void Rename(string from, string to, System.Text.StringBuilder sb)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(from) == null || AssetDatabase.LoadAssetAtPath<GameObject>(to) != null) return;
            string err = AssetDatabase.MoveAsset(from, to);
            sb.Append(string.IsNullOrEmpty(err) ? $"\n  umbenannt {from} → {to}" : $"\n  FEHLER Umbenennen {from}: {err}");
        }

        private static GameObject BuildFlyer(string path, string name, EnemyConfigSO cfg, AnimalModels.Animal animal,
            Color bodyCol, Color wingCol, System.Text.StringBuilder sb)
        {
            bool hasModel = AssetDatabase.LoadAssetAtPath<GameObject>(animal.FbxPath) != null;
            if (hasModel)
            {
                AnimalModels.Import(animal);
                AnimalModels.BuildController(animal, sb);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                // Neu anlegen (nur wenn es das Prefab noch gar nicht gibt – sonst bliebe der Netz-Hash nicht erhalten)
                var ctrl = BuildController();
                BuildPrefab(path, name, cfg, ctrl, Mat("Mat_" + name + "_Body", bodyCol), Mat("Mat_" + name + "_Wing", wingCol));
                sb.Append($"\n  {path}: neu angelegt");
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = name;
                var old = root.transform.Find("Visual");
                int sibling = old != null ? old.GetSiblingIndex() : 0;
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var visual = new GameObject("Visual");
                visual.layer = 0;
                visual.transform.SetParent(root.transform, false);
                visual.transform.SetSiblingIndex(sibling);
                if (hasModel)
                {
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>(animal.FbxPath);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(src, visual.transform);
                    model.name = animal.Name + "_Model";
                    foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        smr.updateWhenOffscreen = false;
                    var anim = model.GetComponent<Animator>();
                    if (anim == null) anim = model.AddComponent<Animator>();
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(animal.FbxPath))
                        if (o is Avatar av) { anim.avatar = av; break; }
                    anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(animal.ControllerPath);
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    // Ruhepose im Prefab = FlyLoop-Frame 0 (Flügel ausgebreitet): Editor-Ansicht und Icon-Render
                    // (PacingSetup.RenderIcon tastet den Animator nicht ab) zeigen den Flieger im Flug
                    var fly = AnimalModels.Clip(animal, "FlyLoop");
                    if (fly != null) fly.SampleAnimation(model, 0f);
                    sb.Append($"\n  {path}: Modell {animal.FbxPath}");
                }
                else
                {
                    AddPlaceholderModel(visual, BuildController(), Mat("Mat_" + name + "_Body", bodyCol), Mat("Mat_" + name + "_Wing", wingCol), root.transform);
                    sb.Append($"\n  {path}: Phönix-Platzhalter (kein FBX)");
                }
                var brain = root.GetComponent<EnemyBrain>();
                if (brain != null) brain.Config = cfg;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                var no = prefab.GetComponent<NetworkObject>();
                sb.Append($", Hash {(no != null ? no.PrefabIdHash : 0)}");
                cfg.DeathClipDuration = hasModel ? DeathClipDuration : 0f;
                return prefab;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Werte laut Walddorf-Umsetzung „Werte-Vorschlag“: 45 HP, 4,2 m/s, Sturzflug 11 je Treffer alle 1,4 s
        // (AttackDamage ist DPS: 11 / 1,4), Windup 0,45 s, Reichweite 2,5 m horizontal, Kopfgeld × 1,3, ignoriert Buddies
        private static EnemyConfigSO EnsureConfig(string path, string displayName, string hint)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(path);
            if (cfg != null) return cfg; // Balance/Texte/Icon der bestehenden Config bleiben (Etappe 2: nur Optik-Felder)
            cfg = ScriptableObject.CreateInstance<EnemyConfigSO>();
            AssetDatabase.CreateAsset(cfg, path);
            var runner = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(RunnerConfig);
            if (runner != null)
            {
                cfg.Type = runner.Type;
                cfg.MeleeHitSfx = runner.MeleeHitSfx;
            }
            cfg.Role = EnemyRole.Flyer;
            cfg.IsFlyer = true;
            cfg.FlyHeight = 2f;
            cfg.ObstacleClearance = 1.5f;
            cfg.DiveHeight = 0.8f;
            cfg.BaseHP = 45f;
            cfg.Speed = 4.2f;
            cfg.AttackInterval = 1.4f;
            cfg.AttackDamage = 11f / 1.4f;
            cfg.AttackWindup = 0.45f;
            cfg.AttackRange = 2.5f;
            cfg.ProjectilePrefab = null;
            cfg.Armor = 0f;
            cfg.ControlResistance = 0f;
            cfg.BountyMultiplier = 1.3f;
            cfg.VisualScale = 1f;
            cfg.BuddyAggroRadius = 0f;
            cfg.HuntsBuddies = false;
            cfg.BuddyDamageMultiplier = 1f;
            cfg.MeleeHitSound = true;
            cfg.IsBoss = false;
            cfg.DisplayName = displayName;
            cfg.PreviewHint = hint;
            cfg.ThemeColor = Color.white;
            cfg.DeathClipDuration = 0f; // Etappe 2: Absturz-Clip
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static Material Mat(string name, Color color)
        {
            string path = AssetDir + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Fly (Schlag-Loop) als Grundzustand, Attack (Phönix-Cast, beschleunigt) per Trigger
        private static AnimatorController BuildController()
        {
            string path = AssetDir + "Flyer_Placeholder.controller";
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var s in sm.states) sm.RemoveState(s.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

            AnimationClip fly = null, cast = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(PhoenixModel))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview__")) continue;
                if (c.name.EndsWith("Fly")) fly = c;
                else if (c.name.EndsWith("Cast")) cast = c;
            }
            var flyS = sm.AddState("Fly", new Vector3(300, 0, 0));
            flyS.motion = fly;
            sm.defaultState = flyS;
            var atk = sm.AddState("Attack", new Vector3(300, 120, 0));
            atk.motion = cast;
            // Cast-Clip auf ~0,9 s strecken (Treffer bei 0,45 s ≈ Mitte)
            atk.speed = cast != null && cast.length > 0.01f ? cast.length / 0.9f : 1f;
            var any = sm.AddAnyStateTransition(atk);
            any.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            any.duration = 0.06f;
            any.canTransitionToSelf = true;
            var back = atk.AddTransition(flyS);
            back.hasExitTime = true;
            back.exitTime = 0.95f;
            back.duration = 0.15f;
            if (fly == null || cast == null) Debug.LogWarning("[FlyerPlaceholderBuilder] Phönix-Clips Fly/Cast nicht gefunden");
            EditorUtility.SetDirty(ac);
            return ac;
        }

        private static GameObject BuildPrefab(string path, string name, EnemyConfigSO cfg, RuntimeAnimatorController controller, Material body, Material wing)
        {
            var root = new GameObject(name);
            try
            {
                root.layer = LayerMask.NameToLayer("Enemy");
                root.tag = "Enemy";

                // Trefferkapsel (Trigger, FlyerHurtbox führt sie bis zum Boden nach)
                var cap = root.AddComponent<CapsuleCollider>();
                cap.isTrigger = true;
                cap.radius = 0.5f;
                cap.height = 2f;
                cap.direction = 1;

                // RequireComponent des EnemyBrain – beim Flieger aus (FlyingMotor bewegt)
                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = 0.45f;
                agent.height = 1f;
                agent.baseOffset = 0.5f;
                agent.speed = cfg.Speed;
                agent.enabled = false;

                var brain = root.AddComponent<EnemyBrain>();
                brain.Config = cfg;
                var runner = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerPrefab);
                var runnerBrain = runner != null ? runner.GetComponent<EnemyBrain>() : null;
                if (runnerBrain != null)
                {
                    brain.HealthBarPrefab = runnerBrain.HealthBarPrefab;
                    brain.PlayerAggroRadius = runnerBrain.PlayerAggroRadius;
                    brain.StragglerRescueTime = runnerBrain.StragglerRescueTime;
                }

                var motor = root.AddComponent<FlyingMotor>();
                motor.BodyRadius = 0.45f;
                var hurt = root.AddComponent<FlyerHurtbox>();
                hurt.Radius = 0.5f;
                root.AddComponent<FlyerShadow>();

                // Netz wie NetSetupEnemies
                var no = root.AddComponent<NetworkObject>();
                no.AutoObjectParentSync = false;
                no.SynchronizeTransform = true;
                var nt = root.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
                nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true; // Flughöhe
                nt.SyncRotAngleX = false;
                nt.SyncRotAngleY = true;
                nt.SyncRotAngleZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.InLocalSpace = false;
                nt.Interpolate = true;
                nt.UseUnreliableDeltas = true;
                nt.UseHalfFloatPrecision = true;
                nt.PositionThreshold = 0.01f;
                nt.RotAngleThreshold = 1f;
                root.AddComponent<EnemyNet>();

                var visual = new GameObject("Visual");
                visual.layer = 0;
                visual.transform.SetParent(root.transform, false);
                AddPlaceholderModel(visual, controller, body, wing, root.transform);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Rückfall ohne FBX: Phönix ohne Effekte, eingefärbt, auf Spannweite skaliert, Körpermitte am Pivot
        private static void AddPlaceholderModel(GameObject visual, RuntimeAnimatorController controller, Material body, Material wing, Transform rootT)
        {
                // Modell: Phönix ohne Effekte, eingefärbt, auf Spannweite skaliert, Körpermitte am Pivot
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(PhoenixModel);
                var model = (GameObject)Object.Instantiate(src);
                model.name = "Model";
                model.transform.SetParent(visual.transform, false);
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string n = mats[i] != null ? mats[i].name : "";
                        mats[i] = n.Contains("Feather") || n.Contains("Plumage") ? wing : body;
                    }
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                // Animator am Modell (Generic-Avatar des FBX, Clip-Pfade relativ zur Modellwurzel)
                var anim = model.GetComponent<Animator>();
                if (anim == null) anim = model.AddComponent<Animator>();
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                // Größe: Spannweite (größere horizontale Ausdehnung) → Wingspan; Körpermitte auf den Pivot
                Bounds b = RendererBounds(model);
                float span = Mathf.Max(b.size.x, b.size.z);
                if (span > 0.01f) model.transform.localScale *= Wingspan / span;
                b = RendererBounds(model);
                model.transform.position -= new Vector3(b.center.x, b.center.y, b.center.z) - rootT.position;

        }

        private static Bounds RendererBounds(GameObject go)
        {
            bool found = false;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
