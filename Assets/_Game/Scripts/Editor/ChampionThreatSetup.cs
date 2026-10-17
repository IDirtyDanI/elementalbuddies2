using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies.EditorTools
{
    // Champion-Bedrohungen (Design/Champion-Bedrohungen-Plan, Paket P1) – reproduzierbares Setup, idempotent:
    //  1. Platzhalter-Requisiten je Theme (Prefabs/Threats/): Ward_* (Ward_Shell, Ward_Shards), Ritual_* (Ritual_Base,
    //     Ritual_Runes, Ritual_Candle_1..4), Rift_* (Rift_Ground, Rift_Hand), Bind_* (Bind_Ring, Bind_Chain_1..3),
    //     HexRunes_* (Bodenrunen). P5 ersetzt sie durch die echten Modelle (gleiche Teilenamen → gleicher Code).
    //  2. Kanonische Configs Knochenbeschwoerer/Grabhexe (Untote) und Dorf-Configs Goblin_Trommler/Dornenhexe
    //     (ThemeSetup.SetupVariant: Werte 1:1, ThemeParityTests) + Einträge im Dorf-Theme, Schildfarbe/-Modell je Theme.
    //  3. Platzhalter-Gegner: Kopie des Nekromanten (Stadt) bzw. Goblin-Schamanen (Dorf), verkleinert und getönt,
    //     ohne BossBrain, mit SummonerBrain/HexerBrain. Bleiben bestehen, wenn sie schon existieren (nur Felder).
    //  4. ExtraGroups im Managers-Prefab (Stufen 8/14/20 bzw. 11/17/23, feste Anzahl), Elite-Symbol „Gebannt“.
    //  5. Icons + Kodex-Einträge (ohne den ganzen MetaSetup-Lauf).
    // Menü: BuddyTD → Champion-Bedrohungen → Assets einrichten; Ritualplätze separat (öffnet/speichert Szenen).
    //
    // P5 Integration (2026-10-17) – liegen die echten Modelle vor, ersetzen sie die Platzhalter (sonst bleibt der Rückfall):
    //  a. Gegner: Models/Enemies/Undead/Undead_{Knochenbeschwoerer,Grabhexe}.fbx, Models/Enemies/Forest/Threats/
    //     {Goblin_Trommler,Dornenhexe}.fbx mit Sidecar .anim.json. Import wie ForestEnemyBuilder (Generic, Takes → Clips,
    //     Schleifen, Palette), Controller <Modell>.controller daneben: Idle ⇄ Run (Moving/Speed, Tempo = Speed / Clip-Tempo
    //     → rutschfrei), Bool „Channel“ (Clip-Rolle channel bzw. beat), Trigger „Summon“ bzw. „Cast“ (AnyState), „Die“.
    //     Das Visual-Kind wird IM BESTEHENDEN Prefab getauscht (GUID/GlobalObjectIdHash bleiben), Kollider an die Höhe
    //     angepasst. DeathClipDuration aus dem Sidecar, SummonerBrain.SummonImpactDelay = Beschwörungs-Frame des Clips.
    //  b. Requisiten: Models/Props/Threats/<Theme>_*.fbx mit .parts.json (Materialien je Slot unter Props/Threats/Materials,
    //     Schilde transparent und beidseitig). Wrapper-Prefabs unter Prefabs/Threats behalten Pfad/GUID und skalieren auf die
    //     Erwartung des Codes: Ward 0,5 (Schale 1 m), Bind 0,625 (Ring ~1 m), Ritual/Rift 1:1, HexRunes = Runenring des
    //     Ritualkreises auf 1 m. Ritualkreis trägt die Schleife (ThreatLoopAudio: ritual_loop bzw. drum_loop).
    //  c. Elite-Symbol „Gebannt“ aus dem Bannschild-Modell gerendert (256 px wie die übrigen Elite-Symbole).
    public static class ChampionThreatSetup
    {
        public const string SummonerName = "Knochenbeschwörer";
        public const string SummonerHint = "Beschwört Verstärkung – nur dein Champion kann ihn verletzen";
        public const string HexerName = "Grabhexe";
        public const string HexerHint = "Bannt Buddy-Gruppen – nur dein Champion kann sie verletzen";
        public const string DrummerName = "Goblin-Trommler";
        public const string DrummerHint = "Trommelt Verstärkung herbei – nur dein Champion kann ihn verletzen";
        public const string ThornHagName = "Dornenhexe";
        public const string ThornHagHint = "Fesselt Buddy-Gruppen – nur dein Champion kann sie verletzen";

        private const string ConfigDir = ThemeSetup.ConfigDir;
        public const string SummonerConfigPath = ConfigDir + "Knochenbeschwoerer.asset";
        public const string HexerConfigPath = ConfigDir + "Grabhexe.asset";
        private const string ThreatDir = "Assets/_Game/Prefabs/Threats/";
        private const string MatDir = ThreatDir + "Materials/";
        private const string EnemyDir = "Assets/_Game/Prefabs/Enemies/";
        private const string ForestEnemyDir = EnemyDir + "Forest/";
        public const string SummonerPrefabPath = EnemyDir + "Knochenbeschwoerer.prefab";
        public const string HexerPrefabPath = EnemyDir + "Grabhexe.prefab";
        public const string DrummerPrefabPath = ForestEnemyDir + "Goblin_Trommler.prefab";
        public const string ThornHagPrefabPath = ForestEnemyDir + "Dornenhexe.prefab";
        private const string NecroPrefab = "Assets/_Game/Prefabs/Bosses/BossNecromancer.prefab";
        private const string ShamanPrefab = "Assets/_Game/Prefabs/Bosses/Forest/Goblin_Schamane.prefab";
        private const string ManagersPrefab = "Assets/_Game/Prefabs/Core/Managers.prefab";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string WardedIcon = IconDir + "elite_warded.png";
        private const string CodexPath = "Assets/Resources/CodexDatabase.asset";

        public static readonly Color UndeadWard = new Color(0.66f, 0.42f, 1f);
        public static readonly Color ForestWard = new Color(1f, 0.68f, 0.18f);

        // Werte laut Plan (gleich in beiden Themes)
        private const float SummonerHp = 200f, SummonerSpeed = 2.8f, SummonerBounty = 6f;
        private const float HexerHp = 180f, HexerSpeed = 3.0f, HexerBounty = 5f;
        // Balancing-Modell (P4): kein dritter Beschwörer/Bannwirker
        public static readonly int[] SummonerSteps = { 9, 18 };
        public static readonly int[] HexerSteps = { 11, 20 };

        private struct Props
        {
            public GameObject Ward, Ritual, Rift, Bind, HexRunes;
        }

        [MenuItem("BuddyTD/Champion-Bedrohungen/Assets einrichten (Configs, Prefabs, Wellen, Kodex)")]
        public static void SetupAssetsMenu() => Debug.Log(SetupAssets());

        public static string SetupAssets()
        {
            var sb = new StringBuilder("[ChampionThreatSetup]\n");
            NetworkSetup.EnsureFolder(ThreatDir.TrimEnd('/'));

            var undeadProps = BuildProps("Undead", UndeadWard, sb);
            var forestProps = BuildProps("Forest", ForestWard, sb);

            // Kanonische Configs (zuerst ohne Prefab, damit die Prefabs sie referenzieren können)
            var summonerCfg = EnsureCanonical(SummonerConfigPath, EnemyRole.Summoner, SummonerName, SummonerHint, SummonerHp, SummonerSpeed, SummonerBounty, UndeadWard, SidecarDeath(SummonerModel));
            var hexerCfg = EnsureCanonical(HexerConfigPath, EnemyRole.Hexer, HexerName, HexerHint, HexerHp, HexerSpeed, HexerBounty, UndeadWard, SidecarDeath(HexerModel));
            var runner = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + "Runner.asset");
            var swarm = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + "Swarm.asset");

            summonerCfg.Prefab = EnsureEnemyPrefab(SummonerPrefabPath, NecroPrefab, summonerCfg, true, 0.78f, new Color(0.55f, 0.35f, 0.85f), undeadProps, UndeadRuneGlow, runner, swarm, SfxId.SummonCall, sb);
            hexerCfg.Prefab = EnsureEnemyPrefab(HexerPrefabPath, NecroPrefab, hexerCfg, false, 0.68f, new Color(0.45f, 0.6f, 0.55f), undeadProps, UndeadBindGlow, runner, swarm, SfxId.HexCast, sb);
            ApplyEnemyModel(SummonerPrefabPath, SummonerModel, true, sb);
            ApplyEnemyModel(HexerPrefabPath, HexerModel, false, sb);
            EditorUtility.SetDirty(summonerCfg);
            EditorUtility.SetDirty(hexerCfg);
            AssetDatabase.SaveAssets();

            // Dorf-Prefabs zuerst (ThemeSetup.SetupVariant lädt Prefabs/Enemies/Forest/<Name>.prefab)
            EnsureEnemyPrefab(DrummerPrefabPath, ShamanPrefab, null, true, 0.85f, new Color(0.95f, 0.6f, 0.2f), forestProps, ForestWard, runner, swarm, SfxId.SummonDrum, sb);
            EnsureEnemyPrefab(ThornHagPrefabPath, ShamanPrefab, null, false, 0.8f, new Color(0.45f, 0.75f, 0.3f), forestProps, ForestWard, runner, swarm, SfxId.HexCast, sb);
            ApplyEnemyModel(DrummerPrefabPath, DrummerModel, true, sb);
            ApplyEnemyModel(ThornHagPrefabPath, ThornHagModel, false, sb);

            // Dorf-Configs (Kopie, Werte 1:1) + Theme-Einträge
            var forestTheme = AssetDatabase.LoadAssetAtPath<EnemyThemeSO>(ThemeSetup.ForestThemePath);
            var undeadTheme = AssetDatabase.LoadAssetAtPath<EnemyThemeSO>(ThemeSetup.UndeadThemePath);
            foreach (var v in ThemeSetup.Forest)
            {
                if (v.Role != EnemyRole.Summoner && v.Role != EnemyRole.Hexer) continue;
                var cfg = ThemeSetup.SetupVariant(v);
                if (cfg == null) continue;
                cfg.ThemeColor = ForestWard;
                // Optik-Felder der Dorf-Modelle (ThemeParityTests.OptikFields): Tod-Clip-Länge, Todeslaut
                bool drummer = v.Role == EnemyRole.Summoner;
                float death = SidecarDeath(drummer ? DrummerModel : ThornHagModel);
                if (death > 0f) cfg.DeathClipDuration = death;
                if (drummer) { cfg.DeathSound = true; cfg.DeathSfx = SfxId.GoblinScreech; }
                EditorUtility.SetDirty(cfg);
                // Dorf-Prefab zeigt auf die Dorf-Config
                NetworkSetup.EditPrefab(AssetDatabase.GetAssetPath(cfg.Prefab), root => root.GetComponent<EnemyBrain>().Config = cfg);
                if (forestTheme != null)
                {
                    forestTheme.Roles.RemoveAll(e => e.Role == v.Role);
                    forestTheme.Roles.Add(new EnemyThemeSO.RoleEntry { Role = v.Role, Config = cfg });
                }
                sb.AppendLine($"Dorf-Config {cfg.name}: Prefab {AssetDatabase.GetAssetPath(cfg.Prefab)}");
            }
            if (forestTheme != null)
            {
                forestTheme.WardColor = ForestWard;
                forestTheme.WardShellPrefab = forestProps.Ward;
                EditorUtility.SetDirty(forestTheme);
            }
            if (undeadTheme != null)
            {
                undeadTheme.WardColor = UndeadWard;
                undeadTheme.WardShellPrefab = undeadProps.Ward;
                EditorUtility.SetDirty(undeadTheme);
            }

            SetupWaveManager(summonerCfg, hexerCfg, sb);
            AssetDatabase.SaveAssets();
            // Namen/Hinweise + Icons nur der vier neuen Configs (PacingSetup.SetupEnemyTexts würde alle Dorf-Icons neu rendern)
            foreach (var p in new[] { SummonerConfigPath, HexerConfigPath, ThemeSetup.ForestConfigDir + "Goblin_Trommler.asset", ThemeSetup.ForestConfigDir + "Dornenhexe.asset" })
                RenderEnemyIcon(p, sb);
            SetupCodex(sb);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        private static void RenderEnemyIcon(string configPath, StringBuilder sb)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(configPath);
            if (cfg == null || cfg.Prefab == null) return;
            PacingSetup.ApplyText(cfg, configPath);
            string file = IconDir + "icon_enemy_" + cfg.name.ToLowerInvariant() + ".png";
            PacingSetup.RenderIcon(cfg.Prefab, file);
            AssetDatabase.ImportAsset(file);
            var imp = AssetImporter.GetAtPath(file) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            cfg.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(file);
            EditorUtility.SetDirty(cfg);
            sb.AppendLine($"Icon {file}");
        }

        // ---------------- Configs ----------------

        private static EnemyConfigSO EnsureCanonical(string path, EnemyRole role, string displayName, string hint, float hp, float speed, float bounty, Color ward, float deathClip)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(path);
            if (cfg == null)
            {
                var runner = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + "Runner.asset");
                cfg = ScriptableObject.CreateInstance<EnemyConfigSO>();
                if (runner != null) EditorUtility.CopySerialized(runner, cfg);
                AssetDatabase.CreateAsset(cfg, path);
                cfg.Icon = null;
            }
            cfg.name = Path.GetFileNameWithoutExtension(path);
            cfg.Role = role;
            cfg.BaseHP = hp;
            cfg.Speed = speed;
            cfg.AttackDamage = 0f;
            cfg.Armor = 0f;
            cfg.BountyMultiplier = bounty;
            cfg.VisualScale = 1f;
            cfg.BuddyAggroRadius = 0f;
            cfg.HuntsBuddies = false;
            cfg.BuddyDamageMultiplier = 1f;
            cfg.AttackRange = 0f;
            cfg.ProjectilePrefab = null;
            cfg.MeleeHitSound = false;
            cfg.AttackSound = false;
            cfg.DeathSound = false;
            cfg.SpawnSound = false;
            cfg.IsBoss = false;
            cfg.IsFlyer = false;
            cfg.ControlResistance = 0f;
            cfg.WardShield = true;
            cfg.DeathClipDuration = deathClip; // Tod-Clip des Modells (0 = ohne Modell, Gegner verschwindet sofort)
            cfg.DisplayName = displayName;
            cfg.PreviewHint = hint;
            cfg.ThemeColor = ward;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        // ---------------- Platzhalter-Gegner ----------------

        // Kopie eines Boss-Prefabs (Kapsel, Agent, EnemyBrain, Netz-Komponenten) ohne BossBrain/Boss-Aura, Modell
        // verkleinert und getönt, mit SummonerBrain bzw. HexerBrain. Existiert das Prefab schon, werden nur Felder gesetzt.
        private static GameObject EnsureEnemyPrefab(string path, string source, EnemyConfigSO cfg, bool summoner, float scale, Color tint,
            Props props, Color ward, EnemyConfigSO runner, EnemyConfigSO swarm, SfxId sfx, StringBuilder sb)
        {
            bool created = false;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                if (!AssetDatabase.CopyAsset(source, path)) { sb.AppendLine("FEHLER: Kopie " + source + " → " + path); return null; }
                created = true;
            }
            string baseName = Path.GetFileNameWithoutExtension(path);
            var prefab = NetworkSetup.EditPrefab(path, root =>
            {
                if (created)
                {
                    var boss = root.GetComponent<BossBrain>();
                    if (boss != null) Object.DestroyImmediate(boss, true);
                    var aura = root.transform.Find("BossAura");
                    if (aura != null) Object.DestroyImmediate(aura.gameObject, true);
                    var visual = root.transform.Find("Visual");
                    if (visual != null)
                    {
                        visual.localScale *= scale;
                        TintRenderers(visual.gameObject, baseName, tint);
                    }
                }
                var brain = root.GetComponent<EnemyBrain>();
                if (brain != null && cfg != null) brain.Config = cfg;
                if (summoner)
                {
                    var s = NetworkSetup.Ensure<SummonerBrain>(root);
                    s.RunnerConfig = runner;
                    s.SwarmConfig = swarm;
                    s.RitualPropPrefab = props.Ritual;
                    s.RiftPropPrefab = props.Rift;
                    s.SummonSfx = sfx;
                    s.PlaceholderColor = ward;
                }
                else
                {
                    var h = NetworkSetup.Ensure<HexerBrain>(root);
                    h.BindPropPrefab = props.Bind;
                    h.TelegraphPrefab = props.HexRunes;
                    h.BindColor = ward;
                    h.CastSfx = sfx;
                }
                EditorUtility.SetDirty(root);
            });
            sb.AppendLine($"Prefab {path}{(created ? " (neu aus " + Path.GetFileName(source) + ")" : "")}");
            return prefab;
        }

        // Materialien des Modells kopieren und zur Tönung hin verschieben (Platzhalter-Unterscheidung)
        private static void TintRenderers(GameObject visual, string baseName, Color tint)
        {
            NetworkSetup.EnsureFolder(MatDir.TrimEnd('/'));
            var copies = new Dictionary<Material, Material>();
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (!copies.TryGetValue(m, out var c))
                    {
                        c = new Material(m) { name = baseName + "_" + m.name };
                        Color baseC = c.HasProperty("_BaseColor") ? c.GetColor("_BaseColor") : (c.HasProperty("_Color") ? c.color : Color.white);
                        Color t = Color.Lerp(baseC, tint * Mathf.Max(0.4f, baseC.grayscale * 1.4f), 0.55f);
                        t.a = baseC.a;
                        if (c.HasProperty("_BaseColor")) c.SetColor("_BaseColor", t);
                        if (c.HasProperty("_Color")) c.color = t;
                        string p = AssetDatabase.GenerateUniqueAssetPath(MatDir + c.name + ".mat");
                        AssetDatabase.CreateAsset(c, p);
                        copies[m] = c;
                    }
                    mats[i] = c;
                }
                r.sharedMaterials = mats;
            }
        }

        // ---------------- Platzhalter-Requisiten ----------------

        private static Props BuildProps(string theme, Color ward, StringBuilder sb)
        {
            if (PropModelsPresent(theme)) return BuildModelProps(theme, sb);
            var dark = new Color(0.18f, 0.16f, 0.2f);
            var earth = new Color(0.25f, 0.19f, 0.13f);
            Material shell = Mat("Ward_Shell_" + theme, new Color(ward.r, ward.g, ward.b, 0.28f));
            Material solid = Mat("Ward_Solid_" + theme, ward);
            Material runes = Mat("Runes_" + theme, new Color(ward.r, ward.g, ward.b, 0.55f));
            Material stone = Mat("Stone_" + theme, dark);
            Material soil = Mat("Soil_" + theme, earth);
            Material flame = Mat("Flame_" + theme, Color.Lerp(ward, Color.white, 0.4f));

            var p = new Props();
            p.Ward = SavePrefab("Ward_" + theme, root =>
            {
                Part(PrimitiveType.Sphere, "Ward_Shell", shell, root, Vector3.zero, Vector3.one);
                var shards = new GameObject("Ward_Shards").transform;
                shards.SetParent(root, false);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 d = Quaternion.Euler(i % 2 == 0 ? 25f : -25f, i * 45f, 0f) * Vector3.forward * 0.45f;
                    Part(PrimitiveType.Cube, "Shard_" + (i + 1), shell, shards, d, Vector3.one * 0.14f);
                }
                shards.gameObject.SetActive(false);
            });
            p.Ritual = SavePrefab("Ritual_" + theme, root =>
            {
                Part(PrimitiveType.Cylinder, "Ritual_Base", stone, root, Vector3.zero, new Vector3(4.2f, 0.04f, 4.2f));
                Part(PrimitiveType.Cylinder, "Ritual_Runes", runes, root, Vector3.up * 0.03f, new Vector3(3.4f, 0.05f, 3.4f));
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    var c = Part(PrimitiveType.Cylinder, "Ritual_Candle_" + (i + 1), stone, root, new Vector3(Mathf.Cos(a), 0.25f, Mathf.Sin(a)) * 1.9f, new Vector3(0.18f, 0.25f, 0.18f));
                    Part(PrimitiveType.Sphere, "Flame", flame, c.transform, Vector3.up * 1.3f, new Vector3(0.9f, 0.6f, 0.9f));
                }
            });
            p.Rift = SavePrefab("Rift_" + theme, root =>
            {
                Part(PrimitiveType.Cylinder, "Rift_Ground", soil, root, Vector3.zero, new Vector3(1.3f, 0.06f, 1.3f));
                Part(PrimitiveType.Cube, "Rift_Hand", solid, root, Vector3.up * 0.35f, new Vector3(0.25f, 0.7f, 0.25f));
            });
            p.Bind = SavePrefab("Bind_" + theme, root =>
            {
                Part(PrimitiveType.Cylinder, "Bind_Ring", runes, root, Vector3.zero, new Vector3(1f, 0.03f, 1f));
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 120f * Mathf.Deg2Rad;
                    Part(PrimitiveType.Cylinder, "Bind_Chain_" + (i + 1), solid, root, new Vector3(Mathf.Cos(a) * 0.45f, -0.5f, Mathf.Sin(a) * 0.45f), new Vector3(0.05f, 0.5f, 0.05f));
                }
            });
            p.HexRunes = SavePrefab("HexRunes_" + theme, root =>
            {
                Part(PrimitiveType.Cylinder, "Hex_Runes", runes, root, Vector3.up * 0.05f, new Vector3(1f, 0.02f, 1f));
                Part(PrimitiveType.Cylinder, "Hex_Core", shell, root, Vector3.up * 0.06f, new Vector3(0.35f, 0.02f, 0.35f));
            });
            sb.AppendLine($"Requisiten {theme}: Ward/Ritual/Rift/Bind/HexRunes");
            return p;
        }

        private static Material Mat(string name, Color color)
        {
            NetworkSetup.EnsureFolder(MatDir.TrimEnd('/')); // nur Platzhalter (ohne Modelle) brauchen eigene Materialien
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var fresh = SuperBuddy.RuntimeMaterial(color);
            fresh.name = name;
            if (m == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            m.CopyPropertiesFromMaterial(fresh);
            m.shader = fresh.shader;
            m.renderQueue = fresh.renderQueue;
            m.shaderKeywords = fresh.shaderKeywords;
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static GameObject Part(PrimitiveType type, string name, Material mat, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            return go;
        }

        private static GameObject SavePrefab(string name, System.Action<Transform> build)
        {
            var root = new GameObject(name);
            try
            {
                build(root.transform);
                return PrefabUtility.SaveAsPrefabAsset(root, ThreatDir + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---------------- P5: echte Modelle (Gegner) ----------------

        private const string UndeadModelDir = "Assets/_Game/Models/Enemies/Undead/";
        private const string ForestModelDir = "Assets/_Game/Models/Enemies/Forest/Threats/";
        private const string PropModelDir = "Assets/_Game/Models/Props/Threats/";
        private const string AudioDir = "Assets/_Game/Audio/Generated/";
        public const string SummonerModel = UndeadModelDir + "Undead_Knochenbeschwoerer.fbx";
        public const string HexerModel = UndeadModelDir + "Undead_Grabhexe.fbx";
        public const string DrummerModel = ForestModelDir + "Goblin_Trommler.fbx";
        public const string ThornHagModel = ForestModelDir + "Dornenhexe.fbx";

        // Leuchtfarben der Untoten-Requisiten (Runen #5cff6a, Geisterkette #6dff9a): Fortschrittsring/Fluchstrahl bzw. Bann-Strahl
        public static readonly Color UndeadRuneGlow = new Color(0.36f, 1f, 0.42f);
        public static readonly Color UndeadBindGlow = new Color(0.43f, 1f, 0.6f);

        // Zusatzfelder der Sidecar-Clips (summon = Beschwörungs-Frame, release = Freigabe des Banns, s)
        [System.Serializable] private class ThreatClip { public string name, role; public float summon = -1f, release = -1f; }
        [System.Serializable] private class ThreatSide { public ThreatClip[] clips = new ThreatClip[0]; }

        private static ThreatSide ReadThreatSide(string fbx)
        {
            string p = Path.ChangeExtension(fbx, null) + ".anim.json";
            return File.Exists(p) ? JsonUtility.FromJson<ThreatSide>(File.ReadAllText(p)) : null;
        }

        private static float SidecarDeath(string fbx)
        {
            if (AssetImporter.GetAtPath(fbx) == null) return 0f;
            var side = ForestEnemyBuilder.ReadSidecar(fbx);
            return side != null ? side.deathDuration : 0f;
        }

        // Modell ins bestehende Prefab: Visual-Kind tauschen (BossBuilder.BuildForestBoss), Kollider an die Modellhöhe,
        // SummonImpactDelay aus dem Clip. Ohne FBX/Sidecar bleibt der Platzhalter.
        private static void ApplyEnemyModel(string prefabPath, string fbx, bool summoner, StringBuilder sb)
        {
            if (AssetImporter.GetAtPath(fbx) == null || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) { sb.AppendLine($"Modell {fbx}: fehlt – Platzhalter bleibt"); return; }
            var side = ForestEnemyBuilder.ReadSidecar(fbx);
            var extra = ReadThreatSide(fbx);
            if (side == null || extra == null) { sb.AppendLine($"Modell {fbx}: Sidecar fehlt – Platzhalter bleibt"); return; }
            ForestEnemyBuilder.ConfigureModelImport(fbx, side, true);
            float vs = Mathf.Max(0.01f, side.visualScale);
            var ctrl = ThreatController(fbx, side, summoner, vs, sb);
            float impact = 0f, release = -1f;
            foreach (var c in extra.clips)
            {
                if (c.summon > 0f) impact = c.summon;
                if (c.release > 0f) release = c.release;
            }
            string name = Path.GetFileNameWithoutExtension(prefabPath);
            float height = 2f;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var agent = root.GetComponent<NavMeshAgent>();
                float baseOffset = agent != null ? agent.baseOffset : 0f;
                var oldVisual = root.transform.Find("Visual");
                var visual = new GameObject("Visual");
                visual.tag = oldVisual != null ? oldVisual.tag : root.tag;
                visual.layer = root.layer;
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, -baseOffset, 0f);
                visual.transform.localScale = Vector3.one / Mathf.Max(0.01f, root.transform.localScale.x);
                if (oldVisual != null)
                {
                    visual.transform.SetSiblingIndex(oldVisual.GetSiblingIndex());
                    Object.DestroyImmediate(oldVisual.gameObject);
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
                inst.name = name + "_Model";
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                foreach (var a in inst.GetComponentsInChildren<Animator>(true)) if (a != anim) Object.DestroyImmediate(a);
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Avatar av) { anim.avatar = av; break; }
                anim.runtimeAnimatorController = ctrl;

                // Höhe über dem Boden (Ruhepose, Wurzel-Raum) → Trefferkapsel steht auf dem Boden und reicht bis knapp über den Kopf
                anim.Rebind();
                anim.Update(0f);
                bool any = false;
                Bounds b = default;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
                }
                if (any) height = (b.max.y - visual.transform.position.y) / Mathf.Max(0.01f, root.transform.lossyScale.y);
                var col = root.GetComponent<CapsuleCollider>();
                if (col != null)
                {
                    col.height = Mathf.Clamp(height + 0.05f, 1.4f, 2f);
                    col.center = new Vector3(0f, -baseOffset + col.height * 0.5f, 0f);
                }

                if (summoner)
                {
                    var s = root.GetComponent<SummonerBrain>();
                    if (s != null) s.SummonImpactDelay = impact;
                }
                else
                {
                    var h = root.GetComponent<HexerBrain>();
                    if (h != null && release > 0f && !Mathf.Approximately(h.CastTime, release))
                        sb.AppendLine($"  Hinweis {name}: Freigabe im Clip {release:0.##} s ≠ CastTime {h.CastTime:0.##} s");
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            sb.AppendLine($"Modell {name}: {fbx} ({side.tris} Tris), Controller {AssetDatabase.GetAssetPath(ctrl)}, Höhe {height:0.00} m, " +
                          $"Lauf 1/{side.moveSpeedModel * vs:0.##} m/s, Tod {side.deathDuration:0.##} s" + (summoner ? $", Beschwörung +{impact:0.###} s" : $", Freigabe {release:0.##} s"));
        }

        // Idle ⇄ Run (Moving, Tempo = Speed / Clip-Tempo), Channel (Bool; Clip-Rolle channel, Trommler beat), Aktion per Trigger
        // (Summon bzw. Cast, AnyState; zurück nach Channel/Run/Idle zum Clip-Ende), Die (AnyState, bleibt). GUID bleibt.
        private static UnityEditor.Animations.AnimatorController ThreatController(string fbx, ForestEnemyBuilder.Sidecar side, bool summoner, float vs, StringBuilder sb)
        {
            string path = Path.ChangeExtension(fbx, null) + ".controller";
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            if (ac == null) ac = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var st in sm.states) sm.RemoveState(st.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            string action = summoner ? "Summon" : "Cast";
            ac.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Channel", AnimatorControllerParameterType.Bool);
            ac.AddParameter(action, AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            AnimationClip Clip(params string[] roles)
            {
                foreach (var role in roles)
                {
                    var c = side.Role(role);
                    if (c == null) continue;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                        if (o is AnimationClip ac2 && ac2.name == c.name) return ac2;
                }
                return null;
            }
            const UnityEditor.Animations.AnimatorConditionMode If = UnityEditor.Animations.AnimatorConditionMode.If;
            const UnityEditor.Animations.AnimatorConditionMode IfNot = UnityEditor.Animations.AnimatorConditionMode.IfNot;
            var idle = sm.AddState("Idle", new Vector3(300, 120, 0)); idle.motion = Clip("idle");
            var run = sm.AddState("Run", new Vector3(300, 0, 0)); run.motion = Clip("move");
            run.speedParameterActive = true;
            run.speedParameter = "Speed";
            run.speed = 1f / Mathf.Max(0.05f, side.moveSpeedModel * vs);
            var channel = sm.AddState("Channel", new Vector3(300, 260, 0)); channel.motion = Clip("channel", "beat");
            var act = sm.AddState(action, new Vector3(600, 60, 0)); act.motion = Clip(summoner ? "summon" : "cast");
            var die = sm.AddState("Die", new Vector3(600, 220, 0)); die.motion = Clip("die");
            sm.defaultState = idle;

            UnityEditor.Animations.AnimatorStateTransition T(UnityEditor.Animations.AnimatorState from, UnityEditor.Animations.AnimatorState to, float dur)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.duration = dur;
                return t;
            }
            T(idle, channel, 0.2f).AddCondition(If, 0f, "Channel");
            T(run, channel, 0.2f).AddCondition(If, 0f, "Channel");
            T(idle, run, 0.15f).AddCondition(If, 0f, "Moving");
            T(run, idle, 0.15f).AddCondition(IfNot, 0f, "Moving");
            T(channel, idle, 0.2f).AddCondition(IfNot, 0f, "Channel");

            var anyAct = sm.AddAnyStateTransition(act);
            anyAct.AddCondition(If, 0f, action);
            anyAct.duration = 0.08f;
            anyAct.canTransitionToSelf = true;
            float len = act.motion is AnimationClip ca && ca.length > 0f ? ca.length : 1f;
            float exit = Mathf.Clamp(1f - 0.2f / len, 0.5f, 0.95f);
            var back = new[] { (channel, "Channel", If), (run, "Moving", If), (idle, (string)null, If) };
            foreach (var (to, param, mode) in back)
            {
                var t = act.AddTransition(to);
                t.hasExitTime = true;
                t.exitTime = exit;
                t.duration = 0.2f;
                if (param != null) t.AddCondition(mode, 0f, param);
            }
            var anyDie = sm.AddAnyStateTransition(die);
            anyDie.AddCondition(If, 0f, "Die");
            anyDie.duration = 0.08f;
            anyDie.canTransitionToSelf = false;
            foreach (var st in sm.states) if (st.state.motion == null) sb.AppendLine($"  Warnung {Path.GetFileName(fbx)}: kein Clip für {st.state.name}");
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ---------------- P5: echte Modelle (Requisiten) ----------------

        [System.Serializable] private class PropPart { public string name, type, parent; }
        [System.Serializable] private class PropSidecar { public string model; public PropPart[] parts = new PropPart[0]; public ForestEnemyBuilder.SideMat[] materials = new ForestEnemyBuilder.SideMat[0]; public int tris; }

        private static string PropFbx(string theme, string kind)
        {
            bool undead = theme == "Undead";
            switch (kind)
            {
                case "Ritual": return PropModelDir + (undead ? "Undead_Ritualkreis" : "Forest_Trommelplatz") + ".fbx";
                case "Rift": return PropModelDir + (undead ? "Undead_Grabriss" : "Forest_Erdloch") + ".fbx";
                case "Bind": return PropModelDir + (undead ? "Undead_Geisterkette" : "Forest_Dornenfessel") + ".fbx";
                default: return PropModelDir + theme + "_Bannschild.fbx";
            }
        }

        private static bool PropModelsPresent(string theme)
        {
            foreach (var k in new[] { "Ritual", "Rift", "Bind", "Ward" })
                if (AssetImporter.GetAtPath(PropFbx(theme, k)) == null) return false;
            return true;
        }

        // Import ohne Rig, Materialien je Slot (Palette des .parts.json); Schild-Materialien danach transparent und beidseitig
        private static PropSidecar ImportProp(string fbx)
        {
            string p = Path.ChangeExtension(fbx, null) + ".parts.json";
            var parts = File.Exists(p) ? JsonUtility.FromJson<PropSidecar>(File.ReadAllText(p)) : new PropSidecar();
            var side = new ForestEnemyBuilder.Sidecar { model = parts.model, rig = "None", materials = parts.materials, tris = parts.tris };
            ForestEnemyBuilder.ConfigureModelImport(fbx, side, false);
            string matDir = Path.GetDirectoryName(fbx).Replace('\\', '/') + "/Materials/";
            foreach (var m in parts.materials)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matDir + m.name + ".mat");
                if (m.name.StartsWith("Ward_") && ColorUtility.TryParseHtmlString(m.hex, out var wc))
                {
                    bool day = m.name.Contains("Forest"); // Walddorf bei Tag: etwas heller/leuchtender, sonst olivgrau statt Bernstein
                    MakeWardMaterial(mat, wc, day ? 0.6f : WardBase, day ? 0.7f : WardGlow);
                }
                // Runen/Fesseln: ohne Tonemapping würde Leuchten > 1,2 zu Weiß ausbrennen – Farbton (Grün/Bernstein) erhalten
                else if ((m.name.StartsWith("Bind_") || m.name.StartsWith("Ritual_Rune")) && m.emission > GlowCap && mat != null
                         && ColorUtility.TryParseHtmlString(m.hex, out var gc))
                {
                    mat.SetColor("_EmissionColor", gc * GlowCap);
                    EditorUtility.SetDirty(mat);
                }
            }
            return parts;
        }

        public const float WardAlpha = 0.2f;
        // Schale: dunkle Grundfarbe (sonst milchig-weiß unter der Szenenbeleuchtung), Leuchten statt 1,2/0,8 aus den Berichten nur
        // 0,45 (Stadt, Nacht) bzw. 0,7 (Dorf, Tag) – große transparente Fläche, mit vollem Leuchten brennt sie zu Weiß aus
        private const float WardBase = 0.5f, WardGlow = 0.45f;
        private const float GlowCap = 1.2f;

        // URP/Lit transparent (Alpha-Blend), Cull Off, ohne Schatten/ZWrite; Leuchten wird mit dem Alpha gemischt. WardVisual
        // moduliert _BaseColor.a per MaterialPropertyBlock (Pulsieren, „Immun“-Blitz), ShardBurst blendet die Scherben aus.
        private static void MakeWardMaterial(Material m, Color hex, float baseK, float glowK)
        {
            if (m == null) return;
            Color c = hex * baseK;
            c.a = WardAlpha;
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", hex * glowK);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_Smoothness", 0.35f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f); // sonst vormultipliziert: Glanz/Leuchten ohne Alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            // URP-eigene Schlüsselwort-Logik (wie der Material-Inspektor), damit Import/Validierung nichts zurückdreht
            var gui = System.Type.GetType("UnityEditor.BaseShaderGUI, Unity.RenderPipelines.Universal.Editor");
            var setKw = gui?.GetMethod("SetMaterialKeywords", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (setKw != null) setKw.Invoke(null, new object[] { m, null, null });
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            EditorUtility.SetDirty(m);
        }

        private static GameObject SaveModelProp(string name, string fbx, float scale, bool shadows, System.Action<Transform, GameObject> extra)
        {
            var root = new GameObject(name);
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                inst.name = "Model";
                inst.transform.localScale = Vector3.one * scale;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = shadows;
                }
                extra?.Invoke(root.transform, inst);
                return PrefabUtility.SaveAsPrefabAsset(root, ThreatDir + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Props BuildModelProps(string theme, StringBuilder sb)
        {
            foreach (var k in new[] { "Ritual", "Rift", "Bind", "Ward" }) ImportProp(PropFbx(theme, k));
            bool undead = theme == "Undead";
            var p = new Props();

            // Bannschild: Modell 2 m → Schale 1 m um den Pivot; Scherben aus, bis WardVisual.Break sie wegschleudert
            p.Ward = SaveModelProp("Ward_" + theme, PropFbx(theme, "Ward"), 0.5f, false, (root, inst) =>
            {
                var shards = ThreatVisuals.FindPart(inst.transform, "Ward_Shards");
                if (shards != null) shards.gameObject.SetActive(false);
            });

            // Ritualkreis 1:1 (Ø 3,5 m Stadt / 3,8 m Dorf), Runen drehen/pulsen (SummonerBrain), Schleife am Kreis
            var loop = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + (undead ? "ritual_loop" : "drum_loop") + ".wav");
            p.Ritual = SaveModelProp("Ritual_" + theme, PropFbx(theme, "Ritual"), 1f, true, (root, inst) =>
            {
                var runes = ThreatVisuals.FindPart(inst.transform, "Ritual_Runes");
                if (runes != null) foreach (var r in runes.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                var go = new GameObject("Ritual_Loop");
                go.transform.SetParent(root, false);
                go.transform.localPosition = Vector3.up * 0.5f;
                var la = go.AddComponent<ThreatLoopAudio>();
                la.Clip = loop;
                la.Volume = 0.5f;
            });

            // Riss/Erdloch 1:1 (Ø ~1,2 m wie der Platzhalter), Hand fährt von −0,8 m hoch (RiftProp)
            p.Rift = SaveModelProp("Rift_" + theme, PropFbx(theme, "Rift"), 1f, false, null);

            // Fessel: Ring Ø 1,6 m → ~1 m (BuddyBindVisual skaliert auf die Buddy-Breite)
            p.Bind = SaveModelProp("Bind_" + theme, PropFbx(theme, "Bind"), 0.625f, false, null);

            // Telegraph: Runenring des Ritualkreises auf 1 m Durchmesser (HexerBrain skaliert auf 2 × HexRadius), übrige Teile aus
            string ritualFbx = PropFbx(theme, "Ritual");
            float runesDiameter = PartDiameter(ritualFbx, "Ritual_Runes");
            p.HexRunes = SaveModelProp("HexRunes_" + theme, ritualFbx, 1f / Mathf.Max(0.1f, runesDiameter), false, (root, inst) =>
            {
                foreach (Transform c in inst.transform)
                    if (c.name != "Ritual_Runes") c.gameObject.SetActive(false);
                inst.transform.localPosition = Vector3.up * 0.02f;
            });
            sb.AppendLine($"Requisiten {theme} (Modelle): Ward ×0,5, Ritual ×1 (+{(loop != null ? loop.name : "kein Loop")}), Rift ×1, Bind ×0,625, HexRunes ×{1f / Mathf.Max(0.1f, runesDiameter):0.###} (Runen Ø {runesDiameter:0.00} m)");
            return p;
        }

        // Waagerechter Durchmesser eines Modellteils (Renderer-Bounds bei Maßstab 1)
        private static float PartDiameter(string fbx, string part)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            try
            {
                var t = ThreatVisuals.FindPart(inst.transform, part);
                var r = t != null ? t.GetComponent<Renderer>() : null;
                return r != null ? Mathf.Max(r.bounds.size.x, r.bounds.size.z) : 1f;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // Elite-Symbol „Gebannt“: Bannschild der Stadt (Schale + zwei abplatzende Scherben) im Stil der übrigen Elite-Symbole
        // (3D, kräftig, freigestellt, 256 px). Für die Lesbarkeit im HUD opak statt transparent (eigene Icon-Materialien).
        private static bool RenderWardedIcon()
        {
            string fbx = PropFbx("Undead", "Ward");
            if (AssetImporter.GetAtPath(fbx) == null) return false;
            string tmp = ThreatDir + "__WardIcon.prefab";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material IconMat(string file, Color c, float glow)
            {
                var m = new Material(shader);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", 0.55f);
                m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * glow);
                AssetDatabase.CreateAsset(m, ThreatDir + file);
                return m;
            }
            var shellMat = IconMat("__WardIconShell.mat", new Color(0.55f, 0.36f, 0.95f), 0.25f);
            var shardMat = IconMat("__WardIconShard.mat", new Color(0.78f, 0.64f, 1f), 0.6f);
            var root = new GameObject("__WardIcon");
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), root.transform);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = shellMat;
                var shards = ThreatVisuals.FindPart(inst.transform, "Ward_Shards");
                if (shards != null)
                {
                    // die beiden Scherben oben rechts platzen ab, der Rest bleibt verborgen
                    var list = new List<Transform>();
                    foreach (Transform s in shards) list.Add(s);
                    list.Sort((a, b) => (b.position.y + 0.6f * b.position.x).CompareTo(a.position.y + 0.6f * a.position.x));
                    for (int k = 0; k < list.Count; k++)
                    {
                        bool show = k < 2;
                        list[k].gameObject.SetActive(show);
                        if (!show) continue;
                        list[k].position += (list[k].position - inst.transform.position).normalized * 0.5f;
                        foreach (var r in list[k].GetComponentsInChildren<Renderer>()) r.sharedMaterial = shardMat;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, tmp);
            }
            finally { Object.DestroyImmediate(root); }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(tmp);
            PacingSetup.RenderIcon(prefab, WardedIcon, 256, 20f, 0.45f, 1.45f);
            AssetDatabase.DeleteAsset(tmp);
            AssetDatabase.DeleteAsset(ThreatDir + "__WardIconShell.mat");
            AssetDatabase.DeleteAsset(ThreatDir + "__WardIconShard.mat");
            AssetDatabase.ImportAsset(WardedIcon);
            var imp = AssetImporter.GetAtPath(WardedIcon) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            return true;
        }

        // ---------------- Wellen ----------------

        private static void SetupWaveManager(EnemyConfigSO summoner, EnemyConfigSO hexer, StringBuilder sb)
        {
            var icon = EnsureWardedIcon();
            NetworkSetup.EditPrefab(ManagersPrefab, root =>
            {
                var wm = root.GetComponentInChildren<WaveManager>(true);
                if (wm == null) { sb.AppendLine("FEHLER: WaveManager fehlt im Managers-Prefab"); return; }
                wm.ExtraGroups.RemoveAll(g => g != null && g.Config != null && g.Config.IsChampionThreat);
                wm.ExtraGroups.Add(new ExtraEnemyGroup
                {
                    Config = summoner, StartWave = SummonerSteps[0], BaseCount = 1, PerWave = 0f, SpawnInterval = 6f,
                    CountSteps = (int[])SummonerSteps.Clone(), FixedCount = true, SkipHarvestWaves = true, MaxInBossWave = 1,
                });
                wm.ExtraGroups.Add(new ExtraEnemyGroup
                {
                    Config = hexer, StartWave = HexerSteps[0], BaseCount = 1, PerWave = 0f, SpawnInterval = 6f,
                    CountSteps = (int[])HexerSteps.Clone(), FixedCount = true, SkipHarvestWaves = false, MaxInBossWave = -1,
                });
                var icons = new List<Sprite>(wm.EliteIcons ?? new Sprite[0]);
                while (icons.Count < (int)EliteAffix.Warded) icons.Add(null);
                icons[(int)EliteAffix.Warded - 1] = icon;
                wm.EliteIcons = icons.ToArray();
                EditorUtility.SetDirty(wm);
                sb.AppendLine($"ExtraGroups: {wm.ExtraGroups.Count} (Beschwörer {string.Join("/", SummonerSteps)}, Bannwirker {string.Join("/", HexerSteps)}), EliteIcons {wm.EliteIcons.Length}");
            });
        }

        // Elite-Symbol „Gebannt“: prozeduraler Runenkreis (Platzhalter bis P5 die gerenderten Icons liefert)
        private static Sprite EnsureWardedIcon()
        {
            if (RenderWardedIcon()) return AssetDatabase.LoadAssetAtPath<Sprite>(WardedIcon);
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            Color ward = new Color(0.42f, 0.55f, 1f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    Color c = new Color(0, 0, 0, 0);
                    // Außenring, Innenring mit 6 Runen-Strichen, Kern
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.07f);
                    float inner = Mathf.Clamp01(1f - Mathf.Abs(r - 0.55f) / 0.04f);
                    float runes = r > 0.6f && r < 0.76f && Mathf.Abs(Mathf.Repeat(ang * 6f / (2f * Mathf.PI), 1f) - 0.5f) < 0.08f ? 1f : 0f;
                    float core = Mathf.Clamp01(1f - r / 0.32f);
                    float a = Mathf.Max(Mathf.Max(ring, inner), Mathf.Max(runes, core * 0.85f));
                    if (r < 0.95f) a = Mathf.Max(a, 0.35f * (1f - r));
                    if (a > 0f) c = Color.Lerp(ward * 0.55f, Color.Lerp(ward, Color.white, 0.5f), Mathf.Max(ring, runes, core));
                    c.a = a;
                    if (r > 0.92f) c.a *= Mathf.Clamp01((0.98f - r) / 0.06f);
                    px[y * n + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(WardedIcon, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(WardedIcon);
            var imp = AssetImporter.GetAtPath(WardedIcon) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(WardedIcon);
        }

        // ---------------- Kodex ----------------

        public static CodexDatabaseSO.Entry WardedCodexEntry() => new CodexDatabaseSO.Entry
        {
            Key = Codex.EliteKey(EliteAffix.Warded), Category = CodexDatabaseSO.Category.Elite,
            Title = "Elite: " + EliteInfo.Name(EliteAffix.Warded),
            Description = "Bannschild schluckt Buddy-, Fusions- und Schrein-Schaden, bis dein Champion die Hälfte ihres Lebens " +
                          "abgetragen hat. 2,5× Leben, 3× Splitter.",
            Icon = AssetDatabase.LoadAssetAtPath<Sprite>(WardedIcon), Extra = "ab Welle 14",
        };

        // Kodex ohne MetaSetup-Komplettlauf ergänzen: neue Gegner hinter den letzten Gegner/Boss, „Gebannt“ hinter die
        // letzte Elite (MetaSetup.BuildCodex erzeugt dieselben Einträge beim nächsten Komplettlauf)
        private static void SetupCodex(StringBuilder sb)
        {
            var db = AssetDatabase.LoadAssetAtPath<CodexDatabaseSO>(CodexPath);
            if (db == null) { sb.AppendLine("Kodex fehlt: " + CodexPath); return; }
            string[] paths = { SummonerConfigPath, HexerConfigPath, ThemeSetup.ForestConfigDir + "Goblin_Trommler.asset", ThemeSetup.ForestConfigDir + "Dornenhexe.asset" };
            int lastEnemy = -1, lastElite = -1;
            for (int i = 0; i < db.Entries.Count; i++)
            {
                var e = db.Entries[i];
                if (e == null) continue;
                if (e.Category == CodexDatabaseSO.Category.Enemy || e.Category == CodexDatabaseSO.Category.Boss) lastEnemy = i;
                if (e.Category == CodexDatabaseSO.Category.Elite) lastElite = i;
            }
            int added = 0;
            foreach (var p in paths)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(p);
                if (cfg == null) continue;
                var entry = new CodexDatabaseSO.Entry
                {
                    Key = Codex.EnemyKey(cfg), Category = CodexDatabaseSO.Category.Enemy,
                    Title = cfg.DisplayName, Description = cfg.PreviewHint, Icon = cfg.Icon, Extra = "Bannschild",
                };
                int idx = db.Entries.FindIndex(x => x != null && x.Key == entry.Key);
                if (idx >= 0) { db.Entries[idx] = entry; continue; }
                db.Entries.Insert(++lastEnemy, entry);
                if (lastElite >= lastEnemy) lastElite++;
                added++;
            }
            var warded = WardedCodexEntry();
            int w = db.Entries.FindIndex(x => x != null && x.Key == warded.Key);
            if (w >= 0) db.Entries[w] = warded;
            else { db.Entries.Insert(lastElite >= 0 ? lastElite + 1 : db.Entries.Count, warded); added++; }
            EditorUtility.SetDirty(db);
            sb.AppendLine($"Kodex: +{added} Einträge, gesamt {db.Entries.Count}");
        }

        // ---------------- Ritualplätze ----------------

        [MenuItem("BuddyTD/Champion-Bedrohungen/Ritualplätze setzen")]
        public static void SetupRitualSitesMenu()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Nicht im Play Mode."); return; }
            Debug.Log(SetupRitualSites(new[] { MapEditorUtil.CityScenePath, "Assets/Scenes/Walddorf.unity" }));
        }

        private const string SitesRoot = "RitualSites";
        private const float MinPathOffset = 8f, MaxPathOffset = 15f;
        private const int SitesPerMap = 7;
        private const float MinSiteSpacing = 11f;

        public static string SetupRitualSites(string[] scenes)
        {
            var sb = new StringBuilder("[Ritualplätze]\n");
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty)
            {
                sb.AppendLine("ABBRUCH: aktive Szene " + active.path + " hat ungespeicherte Änderungen.");
                return sb.ToString();
            }
            foreach (var path in scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                PlaceSites(scene, sb);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            return sb.ToString();
        }

        private static void PlaceSites(UnityEngine.SceneManagement.Scene scene, StringBuilder sb)
        {
            sb.AppendLine("== " + scene.path);
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == SitesRoot) Object.DestroyImmediate(go);

            // Humanoid-Agent (Index 0); die Ramme hat ein eigenes NavMesh
            var filter = new NavMeshQueryFilter { agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID, areaMask = NavMesh.AllAreas };
            var nexus = Object.FindFirstObjectByType<Nexus>();
            var portals = Object.FindObjectsByType<SpawnPortal>(FindObjectsSortMode.None);
            if (nexus == null || portals.Length == 0) { sb.AppendLine("  Nexus/Portale fehlen"); return; }
            if (!NavMesh.SamplePosition(nexus.GetClosestPoint(nexus.transform.position + Vector3.right * 50f), out NavMeshHit nexusHit, 8f, filter)
                && !NavMesh.SamplePosition(nexus.transform.position, out nexusHit, 15f, filter))
            { sb.AppendLine("  kein NavMesh am Nexus"); return; }

            // Gegnerwege: Portal → Nexus (Polylinie, alle 1 m ein Punkt)
            var pathPts = new List<Vector3>();
            var portalPts = new List<Vector3>();
            var navPath = new NavMeshPath();
            foreach (var p in portals)
            {
                Vector3 sp = p.SpawnPoint != null ? p.SpawnPoint.position : p.transform.position;
                if (!NavMesh.SamplePosition(sp, out NavMeshHit ph, 6f, filter)) continue;
                portalPts.Add(ph.position);
                if (!NavMesh.CalculatePath(ph.position, nexusHit.position, filter, navPath)) continue;
                var c = navPath.corners;
                for (int i = 0; i + 1 < c.Length; i++)
                {
                    float len = Vector3.Distance(c[i], c[i + 1]);
                    for (float t = 0f; t < len; t += 1f) pathPts.Add(Vector3.Lerp(c[i], c[i + 1], t / len));
                }
                if (c.Length > 0) pathPts.Add(c[c.Length - 1]);
            }

            // Kandidaten: Raster 3 m über das NavMesh
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices.Length == 0) { sb.AppendLine("  NavMesh leer"); return; }
            var b = new Bounds(tri.vertices[0], Vector3.zero);
            foreach (var v in tri.vertices) b.Encapsulate(v);
            var cands = new List<(Vector3 pos, float pathDist, float portalDist, float nexusDist, float edge)>();
            for (float x = b.min.x; x <= b.max.x; x += 3f)
                for (float z = b.min.z; z <= b.max.z; z += 3f)
                {
                    // Von oben: der erste Treffer muss der Boden mit NavMesh sein (nicht Dach, Baumkrone, Mauer, Wasser)
                    if (!Physics.Raycast(new Vector3(x, b.max.y + 20f, z), Vector3.down, out RaycastHit top, b.size.y + 60f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!NavMesh.SamplePosition(top.point, out NavMeshHit h, 1.5f, filter) || Mathf.Abs(h.position.y - top.point.y) > 0.6f) continue;
                    Vector3 p = h.position;
                    float pd = ChampionThreatRules.NearestDistance(p, pathPts);
                    if (pd < MinPathOffset || pd > MaxPathOffset) continue;
                    // Platz für den Kreis + Beschwörungsring (2–4 m)
                    if (!NavMesh.FindClosestEdge(p, out NavMeshHit edge, filter) || edge.distance < 2.2f) continue;
                    // vom Nexus aus erreichbar
                    if (!NavMesh.CalculatePath(nexusHit.position, p, filter, navPath) || navPath.status != NavMeshPathStatus.PathComplete) continue;
                    float len = 0f;
                    for (int i = 0; i + 1 < navPath.corners.Length; i++) len += Vector3.Distance(navPath.corners[i], navPath.corners[i + 1]);
                    cands.Add((p, pd, ChampionThreatRules.NearestDistance(p, portalPts), len, edge.distance));
                }
            sb.AppendLine($"  Wegpunkte {pathPts.Count}, Portale {portalPts.Count}, Kandidaten {cands.Count}");

            // Auswahl: je Portal die zwei nächsten Kandidaten, dann die vom Nexus am weitesten entfernten (Kartenrand)
            var chosen = new List<int>();
            bool Free(int i)
            {
                foreach (int c in chosen) if (Vector3.Distance(cands[c].pos, cands[i].pos) < MinSiteSpacing) return false;
                return true;
            }
            foreach (var pp in portalPts)
            {
                var order = new List<int>();
                for (int i = 0; i < cands.Count; i++) order.Add(i);
                order.Sort((a, c) => Horizontal(cands[a].pos, pp).CompareTo(Horizontal(cands[c].pos, pp)));
                int taken = 0;
                foreach (int i in order)
                {
                    if (taken >= 2 || chosen.Count >= SitesPerMap) break;
                    if (Horizontal(cands[i].pos, pp) > 30f) break;
                    if (!Free(i)) continue;
                    chosen.Add(i);
                    taken++;
                }
            }
            var far = new List<int>();
            for (int i = 0; i < cands.Count; i++) far.Add(i);
            far.Sort((a, c) => cands[c].nexusDist.CompareTo(cands[a].nexusDist));
            foreach (int i in far)
            {
                if (chosen.Count >= SitesPerMap) break;
                if (Free(i)) chosen.Add(i);
            }

            var root = new GameObject(SitesRoot);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            for (int k = 0; k < chosen.Count; k++)
            {
                var c = cands[chosen[k]];
                var go = new GameObject("RitualSite_" + (k + 1));
                go.transform.SetParent(root.transform, false);
                go.transform.position = c.pos;
                go.AddComponent<RitualSite>();
                sb.AppendLine($"  RitualSite_{k + 1}: ({c.pos.x:0.0}, {c.pos.y:0.0}, {c.pos.z:0.0}) Weg {c.pathDist:0.0} m, Portal {c.portalDist:0.0} m, Nexus-Pfad {c.nexusDist:0} m, Rand {c.edge:0.0} m");
            }
            if (chosen.Count < 6) sb.AppendLine($"  WARNUNG: nur {chosen.Count} Plätze");
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
