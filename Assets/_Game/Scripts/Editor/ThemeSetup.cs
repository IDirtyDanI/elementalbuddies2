using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Walddorf-Plan, Paket C-Theme: legt reproduzierbar an bzw. aktualisiert
    //  1. die Dorf-Configs ScriptableObjects/Configs/Forest/*.asset als Kopien der Untoten-Configs (gleiche Werte je
    //     Rolle; nur Optik eigen: Prefab, Name, Hinweis, Icon) – ThemeParityTests prüft die Gleichheit
    //  2. Resources/Maps/Theme_Forest.asset (alle Rollen inkl. Flieger Mini-Wyvern) und im Stadt-Theme Theme_Undead nur
    //     den Flieger (Riesen-Fledermaus) – alle übrigen Stadt-Rollen bleiben Identität
    //  3. Resources/Maps/Map_Forest.asset (Szene Walddorf, Freischaltung „dawn“, Palisaden-Texte) – nur beim Anlegen
    //     befüllt; spätere Läufe setzen nur Theme/Id/Szene/Freischaltung, damit Feinschliff (Preview, Geometrie) bleibt.
    // Prefabs: C-Prefabs legt sie unter Prefabs/Enemies/Forest bzw. Prefabs/Bosses/Forest an. Fehlen sie, zeigt die
    // Dorf-Config vorerst auf das Untoten-Prefab (spielbar, nur Optik falsch) – nach dem Anlegen einfach erneut ausführen.
    // Menü: BuddyTD → Walddorf → Dorf-Theme einrichten (Configs, Theme, Map).
    public static class ThemeSetup
    {
        public const string ConfigDir = "Assets/ScriptableObjects/Configs/";
        public const string ForestConfigDir = ConfigDir + "Forest/";
        public const string MapsDir = "Assets/Resources/Maps/";
        public const string ForestThemePath = MapsDir + "Theme_Forest.asset";
        public const string UndeadThemePath = MapsDir + "Theme_Undead.asset";
        public const string ForestMapPath = MapsDir + "Map_Forest.asset";
        public const string FlyerBatPath = ConfigDir + "Flyer_Bat.asset";
        public const string FlyerWyvernPath = ConfigDir + "Flyer_Wyvern.asset";
        private const string EnemyPrefabDir = "Assets/_Game/Prefabs/Enemies/Forest/";
        private const string BossPrefabDir = "Assets/_Game/Prefabs/Bosses/Forest/";

        public struct Variant
        {
            public EnemyRole Role;
            public string Canonical; // relativ zu ConfigDir, ohne .asset
            public string Name;      // Asset-/Prefab-Name im Dorf
            public bool Boss;
            public Variant(EnemyRole role, string canonical, string name, bool boss = false)
            { Role = role; Canonical = canonical; Name = name; Boss = boss; }
            public string CanonicalPath => ConfigDir + Canonical + ".asset";
            public string ForestPath => ForestConfigDir + Name + ".asset";
        }

        // Rolle → kanonische Untoten-Config → Dorf-Variante (Namen laut Walddorf-Umsetzung/etappe1)
        public static readonly Variant[] Forest =
        {
            new Variant(EnemyRole.Runner, "Runner", "Goblin_Pluenderer"),
            new Variant(EnemyRole.Swarm, "Swarm", "Wolf"),
            new Variant(EnemyRole.Knight, "Knight", "Ork_Krieger"),
            new Variant(EnemyRole.Archer, "SkeletonArcher", "Goblin_Bogenschuetze"),
            new Variant(EnemyRole.Spider, "GraveSpider", "Waldspinne"),
            new Variant(EnemyRole.BossMelee, "Bosses/BossBoneLord", "Ork_Kriegsherr", true),
            new Variant(EnemyRole.BossCaster, "Bosses/BossNecromancer", "Goblin_Schamane", true),
            new Variant(EnemyRole.BossHunter, "Bosses/BossDeathHunter", "Ork_Faehrtenmeister", true),
            new Variant(EnemyRole.Ram, "Ram", "Ork_Rammbock"),
        };

        [MenuItem("BuddyTD/Walddorf/Dorf-Theme einrichten (Configs, Theme, Map)")]
        public static void SetupAll()
        {
            EnsureFolder(ForestConfigDir.TrimEnd('/'));
            EnsureFolder(MapsDir.TrimEnd('/'));
            var roles = new List<EnemyThemeSO.RoleEntry>();
            foreach (var v in Forest)
            {
                var cfg = SetupVariant(v);
                if (cfg != null) roles.Add(new EnemyThemeSO.RoleEntry { Role = v.Role, Config = cfg });
            }

            var wyvern = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(FlyerWyvernPath);
            var bat = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(FlyerBatPath);
            if (wyvern != null) roles.Add(new EnemyThemeSO.RoleEntry { Role = EnemyRole.Flyer, Config = wyvern });
            else Debug.LogWarning("ThemeSetup: " + FlyerWyvernPath + " fehlt (C-Flieger) – Dorf ohne Flieger, nach dem Anlegen erneut ausführen.");
            if (bat == null) Debug.LogWarning("ThemeSetup: " + FlyerBatPath + " fehlt (C-Flieger) – Stadt ohne Flieger, nach dem Anlegen erneut ausführen.");

            var forest = LoadOrCreate<EnemyThemeSO>(ForestThemePath);
            forest.Id = "forest";
            forest.DisplayName = "Goblins & Orks";
            forest.Roles = roles;
            EditorUtility.SetDirty(forest);

            // Stadt: nur der Flieger (Identität für alle Untoten-Rollen bleibt erhalten)
            var undead = AssetDatabase.LoadAssetAtPath<EnemyThemeSO>(UndeadThemePath);
            if (undead != null)
            {
                undead.Roles = new List<EnemyThemeSO.RoleEntry>();
                if (bat != null) undead.Roles.Add(new EnemyThemeSO.RoleEntry { Role = EnemyRole.Flyer, Config = bat });
                EditorUtility.SetDirty(undead);
            }
            else Debug.LogWarning("ThemeSetup: " + UndeadThemePath + " fehlt (P0).");

            SetupForestMap(forest);
            AssetDatabase.SaveAssets();
            MapRegistry.Reload();
            Debug.Log($"ThemeSetup: {roles.Count} Rollen im Dorf-Theme, Stadt-Flieger {(bat != null ? bat.name : "–")}.");
        }

        // Dorf-Config = Kopie der kanonischen Config; Optik (Prefab, Icon, Projektil, Tod-Clip, Abschusshöhe) bleibt bei erneutem Lauf erhalten
        private static EnemyConfigSO SetupVariant(Variant v)
        {
            var canonical = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(v.CanonicalPath);
            if (canonical == null) { Debug.LogWarning("ThemeSetup: kanonische Config fehlt: " + v.CanonicalPath); return null; }

            var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(v.ForestPath);
            GameObject oldPrefab = null, oldProjectile = null;
            Sprite oldIcon = null;
            float oldDeath = 0f, oldSpawnHeight = float.NaN;
            EnemyConfigSO oldSound = null; // Ton-Felder (Dorf-Klänge, #406)
            GameObject oldDeathVfx = null;
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<EnemyConfigSO>();
                AssetDatabase.CreateAsset(cfg, v.ForestPath);
            }
            else
            {
                oldPrefab = cfg.Prefab;
                oldIcon = cfg.Icon;
                oldProjectile = cfg.ProjectilePrefab;   // eigenes Projektil (z. B. GoblinArrow) behalten (#361)
                oldDeath = cfg.DeathClipDuration;        // Tod-Clip-Länge aus dem Modell-Import behalten (#361)
                oldSpawnHeight = cfg.ProjectileSpawnHeight; // an die Bogenhöhe des Modells angepasst (#360)
                oldSound = Object.Instantiate(cfg);
                oldDeathVfx = cfg.DeathVfx;               // eigener Tod-Effekt (DeathVfxBuilder, #409)
            }

            EditorUtility.CopySerialized(canonical, cfg);
            cfg.name = v.Name;
            cfg.Role = v.Role;

            var prefab = LoadForestPrefab(v);
            cfg.Prefab = prefab != null ? prefab : oldPrefab != null ? oldPrefab : canonical.Prefab;
            cfg.Icon = oldIcon != null ? oldIcon : canonical.Icon;
            if (oldProjectile != null) cfg.ProjectilePrefab = oldProjectile;
            if (oldDeath > 0f) cfg.DeathClipDuration = oldDeath;
            if (oldDeathVfx != null) cfg.DeathVfx = oldDeathVfx;
            if (!float.IsNaN(oldSpawnHeight)) cfg.ProjectileSpawnHeight = oldSpawnHeight;
            if (oldSound != null)
            {
                cfg.MeleeHitSound = oldSound.MeleeHitSound; cfg.MeleeHitSfx = oldSound.MeleeHitSfx;
                cfg.AttackSound = oldSound.AttackSound; cfg.AttackSfx = oldSound.AttackSfx;
                cfg.DeathSound = oldSound.DeathSound; cfg.DeathSfx = oldSound.DeathSfx;
                cfg.SpawnSound = oldSound.SpawnSound; cfg.SpawnSfx = oldSound.SpawnSfx;
                Object.DestroyImmediate(oldSound);
            }
            if (!PacingSetup.ApplyText(cfg, v.ForestPath) && v.Role == EnemyRole.Ram) cfg.DisplayName = "Ork-Rammbock";
            if (prefab == null)
                Debug.Log($"ThemeSetup: Prefab für {v.Name} fehlt noch (C-Prefabs) – {AssetDatabase.GetAssetPath(cfg.Prefab)} als Platzhalter.");
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static GameObject LoadForestPrefab(Variant v)
        {
            string first = (v.Boss ? BossPrefabDir : EnemyPrefabDir) + v.Name + ".prefab";
            string second = (v.Boss ? EnemyPrefabDir : BossPrefabDir) + v.Name + ".prefab";
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(first);
            return p != null ? p : AssetDatabase.LoadAssetAtPath<GameObject>(second);
        }

        // Walddorf (Walddorf-Plan: Palisade Achteck, Tore West/Nord/Süd bei 34 m, Nexus/Anger (24, 0))
        private static void SetupForestMap(EnemyThemeSO theme)
        {
            bool created = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(ForestMapPath) == null;
            var map = LoadOrCreate<MapDefinitionSO>(ForestMapPath);
            map.Id = "forest";
            map.ScenePath = "Assets/Scenes/Walddorf.unity";
            map.UnlockAchievementId = "dawn";
            map.Theme = theme;
            if (created)
            {
                map.DisplayName = "Walddorf";
                map.Tagline = "Ein Dorf mitten im Wald – Goblins, Orks und Wölfe greifen an";
                map.Order = 1;
                map.RecordKeyPrefix = "forest_";
                map.WallHalfSize = 34f;
                map.Gates = new[] { new Vector2(-34f, 0f), new Vector2(0f, 34f), new Vector2(0f, -34f) };
                map.Plaza = new Vector2(24f, 0f);
                map.MinimapCenter = Vector2.zero;
                map.MinimapRadius = 68f;
                map.DawnSunEuler = new Vector3(9f, 95f, 0f);
                map.InsidePhrase = "im Dorf";
                map.WallNoun = "Palisade";
                map.WallSideSuffix = "palisade";
                map.SettlementNoun = "das Dorf";
                map.BossRelief = "Das Dorf atmet auf";
                map.DawnVictoryLine = "Das Dorf hat die Nacht überstanden";
                map.BattleCry = "Für das Dorf!";
                map.SiegeLevelNames = new string[0];
            }
            EditorUtility.SetDirty(map);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, i));
            AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
        }
    }
}
