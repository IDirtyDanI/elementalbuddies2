using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Plan „Fesselung“, Sprint 4 (Bogen & Meta): richtet reproduzierbar ein
    //  1. Erfolge „Erste Nacht“ (W3) und „Morgengrauen“ (schaltet Belagerungsstufen frei)
    //  2. Kodex-Datenbank Resources/CodexDatabase (Gegner, Bosse, Eliten, Ereignisse, Fusionen, Supers, Karten)
    //  3. Szene: Ramme (Skelett-Ritter) und Elite-Symbole am WaveManager, Icons als Sprites
    // Menü: BuddyTD → Game Feel → Sprint 4 einrichten (Meta, Kodex).
    public static class MetaSetup
    {
        private const string GameScene = "Assets/test.unity";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string CodexPath = "Assets/Resources/CodexDatabase.asset";
        private const string MenuScene = "Assets/Scenes/MainMenu.unity";
        private const string MusicDir = "Assets/_Game/Audio/Music/";

        // ---------------- Musik (B2) ----------------

        private static void ImportMusic()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicDir.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null) continue;
                bool stinger = path.Contains("stinger_");
                var s = imp.defaultSampleSettings;
                // Loops im Speicher (sample-genaues PlayScheduled für das Kampf-Paar), Stinger entpackt
                s.loadType = stinger ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.75f;
                s.preloadAudioData = true;
                imp.defaultSampleSettings = s;
                imp.forceToMono = false;
                imp.loadInBackground = !stinger;
                imp.SaveAndReimport();
            }
        }

        private static AudioClip Music(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(MusicDir + name + ".wav");

        // GameAudio der Szene: MusicDirector mit den eigenen Stücken, alter Loop (FunProject) raus
        private static void AddDirector(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var ga in root.GetComponentsInChildren<GameAudio>(true))
                {
                    var md = ga.GetComponent<MusicDirector>();
                    if (md == null) md = ga.gameObject.AddComponent<MusicDirector>();
                    md.MenuClip = Music("music_menu");
                    md.BuildClip = Music("music_build");
                    md.CombatBaseClip = Music("music_combat_base");
                    md.CombatIntenseClip = Music("music_combat_intense");
                    md.BossClip = Music("music_boss");
                    ga.MusicClip = null;
                    EditorUtility.SetDirty(md);
                    EditorUtility.SetDirty(ga);
                }
        }

        private static void SetupMusic(string path)
        {
            var menu = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            AddDirector(menu);
            EditorSceneManager.MarkSceneDirty(menu);
            EditorSceneManager.SaveScene(menu);
            EditorSceneManager.CloseScene(menu, true);
        }

        [MenuItem("BuddyTD/Game Feel/Sprint 4 einrichten (Meta, Kodex)")]
        public static void SetupAll()
        {
            ImportSprites("elite_ram", "event_bloodmoon", "event_soulstorm");
            AchievementTools.CreateOrUpdateDatabase();
            ImportMusic();
            GameFeelSetup.SetupAll(); // Stinger Dawn/WaveEvent in GameAudio beider Szenen (öffnet test.unity)

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GameScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(GameScene);
            }
            var wm = Object.FindFirstObjectByType<WaveManager>(FindObjectsInactive.Include);
            var um = Object.FindFirstObjectByType<UpgradeManager>(FindObjectsInactive.Include);
            var fm = Object.FindFirstObjectByType<FusionManager>(FindObjectsInactive.Include);

            if (wm != null)
            {
                wm.RamConfig = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>("Assets/ScriptableObjects/Configs/Knight.asset");
                var icons = new List<Sprite>(wm.EliteIcons ?? new Sprite[0]);
                while (icons.Count < 5) icons.Add(null);
                if (icons.Count < 6) icons.Add(Icon("elite_ram"));
                else icons[5] = Icon("elite_ram");
                wm.EliteIcons = icons.ToArray();
                EditorUtility.SetDirty(wm);
            }
            BuildCodex(wm, um, fm);
            AddDirector(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SetupMusic(MenuScene);
            AssetDatabase.SaveAssets();
            Debug.Log("MetaSetup: fertig.");
        }

        private static void ImportSprites(params string[] names)
        {
            foreach (var n in names)
            {
                var imp = AssetImporter.GetAtPath(IconDir + n + ".png") as TextureImporter;
                if (imp == null || (imp.textureType == TextureImporterType.Sprite && imp.spriteImportMode == SpriteImportMode.Single)) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
        }

        private static Sprite Icon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + name + ".png");

        private static void BuildCodex(WaveManager wm, UpgradeManager um, FusionManager fm)
        {
            var db = AssetDatabase.LoadAssetAtPath<CodexDatabaseSO>(CodexPath);
            if (db == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                db = ScriptableObject.CreateInstance<CodexDatabaseSO>();
                AssetDatabase.CreateAsset(db, CodexPath);
            }
            db.Entries.Clear();

            // Gegner und Bosse in Reihenfolge des ersten Auftretens
            var enemies = new List<EnemyConfigSO>();
            if (wm != null)
            {
                for (int w = 1; w <= 40; w++)
                {
                    var wave = wm.BuildWave(w);
                    if (wave == null) continue;
                    foreach (var g in wave.EnemiesToSpawn)
                        if (g != null && g.EnemyType != null && g.Count > 0 && !enemies.Contains(g.EnemyType)) enemies.Add(g.EnemyType);
                }
            }
            foreach (var e in enemies)
                db.Entries.Add(new CodexDatabaseSO.Entry
                {
                    Key = Codex.EnemyKey(e),
                    Category = e.IsBoss ? CodexDatabaseSO.Category.Boss : CodexDatabaseSO.Category.Enemy,
                    Title = string.IsNullOrEmpty(e.DisplayName) ? e.name : e.DisplayName,
                    Description = e.PreviewHint,
                    Icon = e.Icon,
                    Extra = e.IsBoss ? "Boss" : (e.Armor > 0f ? $"Rüstung {e.Armor * 100f:0} %" : ""),
                });

            string[] eliteIcons = { "elite_fireproof", "elite_frostguard", "elite_swift", "elite_shieldbearer", "elite_shardthief" };
            for (int i = 1; i <= EliteInfo.Count; i++)
            {
                var a = (EliteAffix)i;
                db.Entries.Add(new CodexDatabaseSO.Entry
                {
                    Key = Codex.EliteKey(a), Category = CodexDatabaseSO.Category.Elite,
                    Title = "Elite: " + EliteInfo.Name(a),
                    Description = char.ToUpper(EliteInfo.Hint(a)[0]) + EliteInfo.Hint(a).Substring(1) + ". 2,5× Leben, 3× Splitter.",
                    Icon = Icon(eliteIcons[i - 1]), Extra = "ab Welle 12",
                });
            }
            string[] eventIcons = { null, "event_bloodmoon", "event_soulstorm", "elite_ram" };
            foreach (var ev in new[] { WaveEvent.BloodMoon, WaveEvent.SoulStorm, WaveEvent.Ram })
                db.Entries.Add(new CodexDatabaseSO.Entry
                {
                    Key = Codex.EventKey(ev), Category = CodexDatabaseSO.Category.Event,
                    Title = WaveEvents.Name(ev), Description = WaveEvents.Description(ev),
                    Icon = Icon(eventIcons[(int)ev]), Extra = "ab Welle 16",
                });

            string[] elementNames = { "Feuer", "Eis", "Erde", "Licht" };
            if (fm != null)
            {
                foreach (var r in fm.Recipes)
                    db.Entries.Add(new CodexDatabaseSO.Entry
                    {
                        Key = Codex.FusionKey(r.Result), Category = CodexDatabaseSO.Category.Fusion,
                        Title = FusionInfo.DisplayName(r.Result), Description = r.Description,
                        Icon = r.Icon != null ? r.Icon : FusionPortrait(r.Result),
                        Extra = elementNames[r.ElementA] + " + " + elementNames[r.ElementB],
                    });
                foreach (var r in fm.TriRecipes)
                    db.Entries.Add(new CodexDatabaseSO.Entry
                    {
                        Key = Codex.FusionKey(r.Result), Category = CodexDatabaseSO.Category.Super,
                        Title = FusionInfo.DisplayName(r.Result), Description = r.Description,
                        Icon = r.Icon != null ? r.Icon : FusionPortrait(r.Result),
                        Extra = elementNames[r.ElementA] + " + " + elementNames[r.ElementB] + " + " + elementNames[r.ElementC],
                    });
            }

            if (um != null)
            {
                // Karten nach Seltenheit sortiert (Gewöhnlich, Selten, Episch), sonst Pool-Reihenfolge
                var cards = new List<UpgradeDefinitionSO>(um.AllUpgrades);
                cards.RemoveAll(c => c == null);
                var sorted = new List<UpgradeDefinitionSO>();
                for (int r = 0; r <= (int)CardRarity.Epic; r++)
                    foreach (var c in cards) if ((int)c.Rarity == r && !sorted.Contains(c)) sorted.Add(c);
                foreach (var c in sorted)
                    db.Entries.Add(new CodexDatabaseSO.Entry
                    {
                        Key = Codex.CardKey(c), Category = CodexDatabaseSO.Category.Card,
                        Title = c.Title, Description = c.Description, Icon = c.Icon,
                        Extra = UpgradeCardUI.RarityName(c.Rarity),
                    });
            }
            EditorUtility.SetDirty(db);
            Debug.Log($"MetaSetup: Kodex mit {db.Entries.Count} Einträgen.");
        }

        private static Sprite FusionPortrait(FusionElement e)
        {
            switch (e)
            {
                case FusionElement.Lightning: return Icon("portrait_fusion_blitz");
                case FusionElement.Water: return Icon("portrait_fusion_wasser");
                case FusionElement.Air: return Icon("portrait_fusion_luft");
                case FusionElement.Shadow: return Icon("portrait_fusion_schatten");
                case FusionElement.Magma: return Icon("portrait_fusion_magma");
                case FusionElement.Crystal: return Icon("portrait_fusion_kristall");
                case FusionElement.VolcanoTitan: return Icon("portrait_super_vulkan");
                case FusionElement.StormLord: return Icon("portrait_super_sturm");
                case FusionElement.Phoenix: return Icon("portrait_super_phoenix");
                case FusionElement.WorldTree: return Icon("portrait_super_weltenbaum");
                default: return null;
            }
        }
    }
}
