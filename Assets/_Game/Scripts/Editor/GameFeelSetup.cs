using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Plan „Fesselung“, Sprint 1 (Game Feel + Audio): richtet alles reproduzierbar ein.
    //  1. Import-Einstellungen der eigenen Synthese-Sounds (Assets/_Game/Audio/Generated, Quelle art-src/audio/synth_sfx.py)
    //  2. Eigene, prozedural erzeugte Partikel-Texturen + Knochensplitter-Mesh
    //  3. VFX-Prefabs: VFX_EnemyDeath, VFX_BossDeath, VFX_HitSpark
    //  4. GameAudio-Einträge (SfxId → Clips) im Managers-Objekt der Spielszene
    //  5. Champion-Sounds in den Kits (KnightKit/ArcherKit/ArrowProjectile)
    //  6. FeedbackDirector auf dem Managers-Objekt
    // Menü: BuddyTD → Game Feel → Einrichten. Mehrfach ausführbar (Prefabs behalten ihre GUID).
    public static class GameFeelSetup
    {
        private const string AudioDir = "Assets/_Game/Audio/Generated/";
        private const string MusicDir = "Assets/_Game/Audio/Music/";
        private const string TexDir = "Assets/_Game/VFX/Textures/GameFeel/";
        private const string MatDir = "Assets/_Game/VFX/Materials/";
        private const string MeshDir = "Assets/_Game/VFX/Meshes/";
        private const string PrefabDir = "Assets/_Game/VFX/Prefabs/";
        public const string EnemyDeathPath = PrefabDir + "VFX_EnemyDeath.prefab";
        public const string BossDeathPath = PrefabDir + "VFX_BossDeath.prefab";
        public const string HitSparkPath = PrefabDir + "VFX_HitSpark.prefab";
        // Sprint 3 (Draft & Eliten)
        public const string FrostShatterPath = PrefabDir + "VFX_FrostShatter.prefab";
        public const string EliteAuraPath = PrefabDir + "VFX_EliteAura.prefab";
        private const string GameScene = "Assets/test.unity";
        private const string MenuScene = "Assets/Scenes/MainMenu.unity";

        [MenuItem("BuddyTD/Game Feel/Einrichten (Sounds, VFX, Szene)")]
        public static void SetupAll()
        {
            ImportAudio();
            BuildTextures();
            BuildVfx();
            AssignChampionSfx();
            SetupScene();
            AssetDatabase.SaveAssets();
            Debug.Log("GameFeelSetup: fertig.");
        }

        [MenuItem("BuddyTD/Game Feel/Nur VFX neu bauen")]
        public static void BuildVfxOnly()
        {
            BuildTextures();
            BuildVfx();
            AssetDatabase.SaveAssets();
        }

        // ---------------- 1. Audio ----------------

        private static void ImportAudio()
        {
            if (!AssetDatabase.IsValidFolder(AudioDir.TrimEnd('/'))) { Debug.LogWarning("GameFeelSetup: " + AudioDir + " fehlt."); return; }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null) continue;
                var s = imp.defaultSampleSettings;
                bool longClip = path.Contains("game_over") || path.Contains("boss_") || path.Contains("wave_") || path.Contains("horn");
                s.loadType = longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                s.preloadAudioData = true;
                imp.defaultSampleSettings = s;
                imp.forceToMono = true;
                imp.loadInBackground = false;
                imp.SaveAndReimport();
            }
        }

        private static AudioClip Clip(string name)
        {
            string path = name.StartsWith("../Music/") ? MusicDir + name.Substring(9) + ".wav" : AudioDir + name + ".wav";
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (c == null) Debug.LogWarning("GameFeelSetup: Clip fehlt: " + name);
            return c;
        }

        private struct Sfx
        {
            public SfxId Id; public string[] Clips; public float Volume, Interval; public Vector2 Pitch;
            public Sfx(SfxId id, float vol, float interval, Vector2 pitch, params string[] clips)
            { Id = id; Volume = vol; Interval = interval; Pitch = pitch; Clips = clips; }
        }

        private static readonly Vector2 P = new Vector2(0.95f, 1.05f);
        private static readonly Vector2 PFix = new Vector2(1f, 1f);
        private static readonly Vector2 PWide = new Vector2(0.9f, 1.1f);

        private static IEnumerable<Sfx> SfxTable()
        {
            // vorhandene Ids, jetzt mit eigenen Sounds
            yield return new Sfx(SfxId.Blink, 0.6f, 0.1f, P, "blink");
            yield return new Sfx(SfxId.EnemyDeath, 0.42f, 0.06f, PWide, "bone_break_1", "bone_break_2", "bone_break_3");
            yield return new Sfx(SfxId.BuddyDeath, 0.7f, 0.3f, P, "buddy_down");
            yield return new Sfx(SfxId.ShardPickup, 0.38f, 0.035f, PFix, "shard_chime");
            // neu
            yield return new Sfx(SfxId.SwordSwing, 0.5f, 0.05f, P, "sword_swing_1", "sword_swing_2", "sword_swing_3");
            yield return new Sfx(SfxId.SwordHit, 0.55f, 0.05f, PWide, "sword_hit");
            yield return new Sfx(SfxId.ShieldBlock, 0.6f, 0.15f, P, "shield_block");
            yield return new Sfx(SfxId.BowShot, 0.55f, 0.05f, P, "bow_shot");
            yield return new Sfx(SfxId.BowHit, 0.5f, 0.05f, PWide, "bow_hit");
            yield return new Sfx(SfxId.DodgeRoll, 0.45f, 0.1f, P, "dodge_roll");
            yield return new Sfx(SfxId.HeavyImpact, 0.55f, 0.1f, P, "heavy_impact");
            yield return new Sfx(SfxId.EnemyHit, 0.32f, 0.05f, PWide, "enemy_hit");
            yield return new Sfx(SfxId.BossDeath, 0.9f, 1f, PFix, "boss_death");
            yield return new Sfx(SfxId.EliteSpawn, 0.7f, 0.5f, P, "elite_spawn");
            yield return new Sfx(SfxId.UiClick, 0.45f, 0.05f, new Vector2(0.97f, 1.03f), "ui_click");
            yield return new Sfx(SfxId.UiHover, 0.22f, 0.04f, new Vector2(0.97f, 1.03f), "ui_hover");
            yield return new Sfx(SfxId.CardPick, 0.7f, 0.2f, PFix, "card_pick");
            yield return new Sfx(SfxId.Coin, 0.65f, 0.1f, P, "coin");
            yield return new Sfx(SfxId.Reroll, 0.6f, 0.2f, P, "reroll");
            yield return new Sfx(SfxId.MerchantBell, 0.6f, 1f, PFix, "merchant_bell");
            yield return new Sfx(SfxId.WaveStart, 0.7f, 2f, PFix, "wave_start_horn");
            yield return new Sfx(SfxId.WaveClear, 0.6f, 2f, PFix, "wave_clear");
            yield return new Sfx(SfxId.LevelUp, 0.6f, 0.1f, PFix, "level_up");
            yield return new Sfx(SfxId.GameOver, 0.8f, 2f, PFix, "game_over");
            yield return new Sfx(SfxId.BossDefeated, 0.85f, 2f, PFix, "boss_defeated");
            yield return new Sfx(SfxId.Streak, 0.6f, 1f, PFix, "streak");
            yield return new Sfx(SfxId.NexusAlarm, 0.75f, 2f, PFix, "nexus_alarm");
            yield return new Sfx(SfxId.LowHpHeartbeat, 0.55f, 0.5f, PFix, "low_hp_heartbeat");
            yield return new Sfx(SfxId.FireShot, 0.26f, 0.09f, PWide, "fire_shot");
            yield return new Sfx(SfxId.FireHit, 0.28f, 0.07f, PWide, "fire_hit");
            yield return new Sfx(SfxId.IceShot, 0.26f, 0.09f, PWide, "ice_shot");
            yield return new Sfx(SfxId.IceHit, 0.28f, 0.07f, PWide, "ice_hit");
            yield return new Sfx(SfxId.EarthShot, 0.42f, 0.2f, P, "earth_shot");
            yield return new Sfx(SfxId.EarthHit, 0.32f, 0.08f, PWide, "earth_hit");
            yield return new Sfx(SfxId.LightShot, 0.24f, 0.1f, PWide, "light_shot");
            yield return new Sfx(SfxId.LightHeal, 0.22f, 1.2f, P, "light_heal"); // kurz + selten (Nutzer-Feedback 2026-10-08)
            // Sprint 3
            yield return new Sfx(SfxId.CardRevealCommon, 0.5f, 0.05f, PFix, "card_reveal_common");
            yield return new Sfx(SfxId.CardRevealRare, 0.6f, 0.05f, PFix, "card_reveal_rare");
            yield return new Sfx(SfxId.CardRevealEpic, 0.7f, 0.05f, PFix, "card_reveal_epic");
            yield return new Sfx(SfxId.FrostShatter, 0.5f, 0.1f, PWide, "frost_shatter");
            // Sprint 4 (Stinger aus synth_music.py, liegen bei der Musik)
            yield return new Sfx(SfxId.Dawn, 0.85f, 5f, PFix, "../Music/stinger_dawn");
            yield return new Sfx(SfxId.WaveEvent, 0.75f, 2f, PFix, "../Music/stinger_event");
            // Sprint 5
            yield return new Sfx(SfxId.Hint, 0.5f, 0.5f, PFix, "hint_pop");
            yield return new Sfx(SfxId.Ping, 0.7f, 0.15f, PFix, "ping");
            yield return new Sfx(SfxId.BuddyHappy, 0.4f, 0.25f, P, "buddy_happy");
            yield return new Sfx(SfxId.BuddySad, 0.4f, 0.4f, P, "buddy_sad");
            yield return new Sfx(SfxId.BuddyCheer, 0.45f, 0.3f, P, "buddy_cheer");
            yield return new Sfx(SfxId.Curse, 0.7f, 1f, PFix, "curse");
        }

        private static void AssignGameAudio(GameAudio ga)
        {
            var so = new SerializedObject(ga);
            so.Update();
            foreach (var s in SfxTable())
            {
                var clips = new List<AudioClip>();
                foreach (var n in s.Clips) { var c = Clip(n); if (c != null) clips.Add(c); }
                if (clips.Count == 0) continue;
                var e = ga.Entries.Find(x => x != null && x.Id == s.Id);
                if (e == null) { e = new GameAudio.SfxEntry { Id = s.Id }; ga.Entries.Add(e); }
                e.Clips = clips.ToArray();
                e.Volume = s.Volume;
                e.MinInterval = s.Interval;
                e.Pitch = s.Pitch;
            }
            EditorUtility.SetDirty(ga);
        }

        // ---------------- 5. Champion-Sounds ----------------

        private static void SetSfx(ChampionSfx sfx, string clip, float vol, float pMin, float pMax)
        {
            if (sfx == null) return;
            var c = Clip(clip);
            if (c == null) return;
            sfx.Clip = c;
            sfx.Volume = vol;
            sfx.Pitch = new Vector2(pMin, pMax);
        }

        private static void AssignChampionSfx()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                bool relevant = asset.GetComponentInChildren<KnightKit>(true) != null || asset.GetComponentInChildren<ArcherKit>(true) != null
                                || asset.GetComponentInChildren<ArrowProjectile>(true) != null;
                if (!relevant) continue;
                // Nur Prefabs ändern, die die Komponente selbst besitzen (nicht nur über ein verschachteltes Prefab)
                var root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                foreach (var k in root.GetComponentsInChildren<KnightKit>(true))
                {
                    SetSfx(k.SwingSfx, "sword_swing_1", 0.5f, 0.92f, 1.1f);
                    SetSfx(k.FinisherSfx, "sword_swing_3", 0.65f, 0.85f, 0.95f);
                    SetSfx(k.BlockHitSfx, "shield_block", 0.6f, 0.9f, 1.05f);
                    SetSfx(k.BlockBreakSfx, "heavy_impact", 0.6f, 1.1f, 1.25f);
                    changed = true;
                }
                foreach (var a in root.GetComponentsInChildren<ArcherKit>(true))
                {
                    SetSfx(a.ShootSfx, "bow_shot", 0.6f, 0.95f, 1.08f);
                    SetSfx(a.ArrowHitSfx, "bow_hit", 0.55f, 0.92f, 1.1f);
                    SetSfx(a.RollSfx, "dodge_roll", 0.45f, 0.95f, 1.08f);
                    changed = true;
                }
                foreach (var ap in root.GetComponentsInChildren<ArrowProjectile>(true))
                {
                    if (ap.HitSfx == null) ap.HitSfx = new ChampionSfx();
                    SetSfx(ap.HitSfx, "bow_hit", 0.55f, 0.92f, 1.1f);
                    changed = true;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                if (changed) Debug.Log("GameFeelSetup: Champion-Sounds gesetzt in " + path);
            }
        }

        // ---------------- 6. Szene ----------------

        private static void SetupScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GameScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(GameScene);
            }
            var ga = Object.FindFirstObjectByType<GameAudio>(FindObjectsInactive.Include);
            if (ga == null) { Debug.LogError("GameFeelSetup: kein GameAudio in " + GameScene); return; }
            AssignGameAudio(ga);

            var dir = ga.GetComponent<FeedbackDirector>();
            if (dir == null) dir = ga.gameObject.AddComponent<FeedbackDirector>();
            dir.EnemyDeathVfx = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDeathPath);
            dir.BossDeathVfx = AssetDatabase.LoadAssetAtPath<GameObject>(BossDeathPath);
            dir.HitSparkVfx = AssetDatabase.LoadAssetAtPath<GameObject>(HitSparkPath);
            dir.FrostShatterVfx = AssetDatabase.LoadAssetAtPath<GameObject>(FrostShatterPath);
            EditorUtility.SetDirty(dir);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Hauptmenü: eigenes GameAudio (UI-Klick/-Hover); additiv öffnen, zuweisen, speichern, schließen
            var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Additive);
            foreach (var root in menu.GetRootGameObjects())
                foreach (var mga in root.GetComponentsInChildren<GameAudio>(true)) AssignGameAudio(mga);
            EditorSceneManager.MarkSceneDirty(menu);
            EditorSceneManager.SaveScene(menu);
            EditorSceneManager.CloseScene(menu, true);
        }

        // ---------------- 2. Texturen & Mesh (eigene, prozedural) ----------------

        private static void BuildTextures()
        {
            Directory.CreateDirectory(TexDir);
            WriteTex("gf_softdot.png", 64, (u, v) => { float d = Mathf.Sqrt(u * u + v * v); return Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f); });
            WriteTex("gf_spark.png", 64, (u, v) => Mathf.Exp(-(u * u) * 2.5f) * Mathf.Exp(-(v * v) * 60f));
            WriteTex("gf_ring.png", 128, (u, v) => { float d = Mathf.Sqrt(u * u + v * v); return Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.07f, 2f)); });
            WriteTex("gf_dust.png", 128, (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);
                float n = Mathf.PerlinNoise(u * 3.1f + 7.3f, v * 3.1f + 1.7f) * 0.6f + Mathf.PerlinNoise(u * 7.7f + 2.1f, v * 7.7f + 5.5f) * 0.4f;
                return Mathf.Clamp01((1f - d) * 1.6f) * Mathf.Lerp(0.45f, 1f, n);
            });
            AssetDatabase.Refresh();
            foreach (var f in new[] { "gf_softdot.png", "gf_spark.png", "gf_ring.png", "gf_dust.png" })
            {
                var imp = AssetImporter.GetAtPath(TexDir + f) as TextureImporter;
                if (imp == null) continue;
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }

            Directory.CreateDirectory(MeshDir);
            string meshPath = MeshDir + "GF_BoneShard.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
            FillBoneMesh(mesh);
            EditorUtility.SetDirty(mesh);
        }

        // Weiße Textur, Alpha aus f(u,v) mit u,v in [-1,1]
        private static void WriteTex(string file, int n, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
            tex.SetPixels32(px);
            File.WriteAllBytes(TexDir + file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // Knochensplitter: sechseckiger Schaft mit verdickten, abgeschrägten Enden (Länge 0,5 m, low poly)
        private static void FillBoneMesh(Mesh m)
        {
            const int sides = 6;
            float[] ys = { -0.25f, -0.2f, -0.12f, 0.12f, 0.2f, 0.25f };
            float[] rs = { 0.03f, 0.075f, 0.04f, 0.04f, 0.075f, 0.03f };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int ring = 0; ring < ys.Length; ring++)
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    verts.Add(new Vector3(Mathf.Cos(a) * rs[ring], ys[ring], Mathf.Sin(a) * rs[ring]));
                }
            for (int ring = 0; ring < ys.Length - 1; ring++)
                for (int s = 0; s < sides; s++)
                {
                    int a = ring * sides + s, b = ring * sides + (s + 1) % sides;
                    int c = a + sides, d = b + sides;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            int bottom = verts.Count; verts.Add(new Vector3(0f, ys[0], 0f));
            int top = verts.Count; verts.Add(new Vector3(0f, ys[ys.Length - 1], 0f));
            int last = (ys.Length - 1) * sides;
            for (int s = 0; s < sides; s++)
            {
                tris.AddRange(new[] { bottom, s, (s + 1) % sides });
                tris.AddRange(new[] { top, last + (s + 1) % sides, last + s });
            }
            // Flat Shading wie der Low-Poly-Stil: Dreiecke entflechten
            var fv = new List<Vector3>();
            var ft = new List<int>();
            for (int i = 0; i < tris.Count; i++) { fv.Add(verts[tris[i]]); ft.Add(i); }
            m.Clear();
            m.SetVertices(fv);
            m.SetTriangles(ft, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.name = "GF_BoneShard";
        }

        // ---------------- 3. VFX ----------------

        private static Material ParticleMat(string name, string template, string tex)
        {
            string p = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                var tpl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + template + ".mat");
                m = tpl != null ? new Material(tpl) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, p);
            }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + tex);
            m.SetTexture("_BaseMap", t);
            m.mainTexture = t;
            m.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material BoneMat()
        {
            string p = MatDir + "GF_Bone.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", new Color(0.86f, 0.82f, 0.7f));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Add(string n, string tex) => ParticleMat(n, "VFX_SoftDot_Add", tex);
        private static Material Alpha(string n, string tex) => ParticleMat(n, "VFX_LavaTrail", tex);

        private static void BuildVfx()
        {
            Directory.CreateDirectory(PrefabDir);
            var bone = BoneMat();
            var boneMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "GF_BoneShard.asset");
            var dust = Alpha("GF_Dust_Alpha", "gf_dust.png");
            var glow = Add("GF_SoftDot_Add", "gf_softdot.png");
            var spark = Add("GF_Spark_Add", "gf_spark.png");
            var ring = Add("GF_Ring_Add", "gf_ring.png");
            var soul = new Color(0.45f, 0.95f, 1f, 1f);

            Build(EnemyDeathPath, root =>
            {
                var b = Burst(root, "Bones", bone, 8, new Vector2(0.8f, 1.1f), new Vector2(0.7f, 1.1f), new Vector2(2.5f, 5f), Color.white, 16);
                MeshParticles(b, boneMesh);
                Cone(b, 35f, 0.25f);
                Gravity(b, 1.6f);
                Spin(b);
                Collide(b);
                Fade(b, new[] { 1f, 1f, 0f }, new[] { 0f, 0.8f, 1f }, size: true);

                var d = Burst(root, "Dust", dust, 6, new Vector2(0.55f, 0.85f), new Vector2(0.8f, 1.3f), new Vector2(0.4f, 1.2f), new Color(0.55f, 0.5f, 0.42f, 0.55f), 12);
                Sphere(d, 0.35f);
                Grow(d, 0.6f, 1.6f);
                Fade(d, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f });

                var w = Burst(root, "Soul", glow, 1, new Vector2(0.6f, 0.7f), new Vector2(1.1f, 1.3f), new Vector2(0f, 0f), soul, 2);
                Rise(w, 2.2f);
                Fade(w, new[] { 0f, 0.9f, 0f }, new[] { 0f, 0.2f, 1f });
            });

            Build(BossDeathPath, root =>
            {
                var b = Burst(root, "Bones", bone, 30, new Vector2(1.4f, 2f), new Vector2(1.2f, 2f), new Vector2(5f, 11f), Color.white, 40);
                MeshParticles(b, boneMesh);
                Cone(b, 55f, 0.8f);
                Gravity(b, 1.8f);
                Spin(b);
                Collide(b);
                Fade(b, new[] { 1f, 1f, 0f }, new[] { 0f, 0.85f, 1f }, size: true);

                var d = Burst(root, "DustRing", dust, 22, new Vector2(1f, 1.5f), new Vector2(2f, 3.2f), new Vector2(4f, 7f), new Color(0.5f, 0.45f, 0.38f, 0.6f), 30);
                var sh = d.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.8f; sh.rotation = new Vector3(90f, 0f, 0f);
                var lim = d.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.5f; lim.dampen = 0.08f;
                Grow(d, 0.7f, 1.8f);
                Fade(d, new[] { 0f, 1f, 0f }, new[] { 0f, 0.1f, 1f });

                var r = Burst(root, "Shockwave", ring, 1, new Vector2(0.65f, 0.65f), new Vector2(1.5f, 1.5f), Vector2.zero, new Color(1f, 0.85f, 0.55f, 1f), 1);
                r.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                r.transform.localPosition = new Vector3(0f, -0.8f, 0f);
                Grow(r, 1f, 12f);
                Fade(r, new[] { 1f, 0.8f, 0f }, new[] { 0f, 0.3f, 1f });

                var f = Burst(root, "Flash", glow, 1, new Vector2(0.25f, 0.25f), new Vector2(7f, 7f), Vector2.zero, new Color(1f, 0.92f, 0.7f, 1f), 1);
                Fade(f, new[] { 1f, 0f }, new[] { 0f, 1f });

                var s = Burst(root, "SoulColumn", glow, 40, new Vector2(1.2f, 2f), new Vector2(0.25f, 0.5f), new Vector2(0.5f, 1.5f), soul, 50);
                Sphere(s, 0.9f);
                Rise(s, 5f);
                Fade(s, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f });
            });

            Build(HitSparkPath, root =>
            {
                var s = Burst(root, "Sparks", spark, 7, new Vector2(0.12f, 0.22f), new Vector2(0.18f, 0.3f), new Vector2(5f, 9f), new Color(1f, 0.9f, 0.6f, 1f), 12);
                var pr = s.GetComponent<ParticleSystemRenderer>();
                pr.renderMode = ParticleSystemRenderMode.Stretch;
                pr.velocityScale = 0.06f;
                pr.lengthScale = 1.5f;
                Sphere(s, 0.1f);
                Fade(s, new[] { 1f, 0f }, new[] { 0f, 1f }, size: true);

                var f = Burst(root, "Flash", glow, 1, new Vector2(0.09f, 0.09f), new Vector2(1.1f, 1.1f), Vector2.zero, new Color(1f, 0.95f, 0.8f, 1f), 1);
                Fade(f, new[] { 1f, 0f }, new[] { 0f, 1f });
            });

            BuildSprint3Vfx(glow, spark, ring);
        }

        // Sprint 3: Eis zerspringt (Splitterfrost-Karte), Aura unter Elite-Gegnern (Farbe setzt EliteVisual)
        private static void BuildSprint3Vfx(Material glow, Material spark, Material ring)
        {
            var ice = new Color(0.7f, 0.93f, 1f, 1f);
            Build(FrostShatterPath, root =>
            {
                var s = Burst(root, "Shards", spark, 18, new Vector2(0.25f, 0.45f), new Vector2(0.18f, 0.35f), new Vector2(5f, 10f), ice, 24);
                var pr = s.GetComponent<ParticleSystemRenderer>();
                pr.renderMode = ParticleSystemRenderMode.Stretch;
                pr.velocityScale = 0.05f;
                pr.lengthScale = 2f;
                Sphere(s, 0.3f);
                Gravity(s, 0.8f);
                Fade(s, new[] { 1f, 0f }, new[] { 0f, 1f }, size: true);

                var r = Burst(root, "Ring", ring, 1, new Vector2(0.35f, 0.35f), new Vector2(1f, 1f), Vector2.zero, ice, 1);
                r.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                r.transform.localPosition = new Vector3(0f, -0.5f, 0f);
                Grow(r, 1f, 6f);
                Fade(r, new[] { 1f, 0f }, new[] { 0f, 1f });

                var f = Burst(root, "Flash", glow, 1, new Vector2(0.15f, 0.15f), new Vector2(2.5f, 2.5f), Vector2.zero, new Color(0.85f, 0.97f, 1f, 1f), 1);
                Fade(f, new[] { 1f, 0f }, new[] { 0f, 1f });
            });

            Build(EliteAuraPath, root =>
            {
                var r = Burst(root, "Ring", ring, 0, new Vector2(1.2f, 1.2f), new Vector2(1.8f, 1.8f), Vector2.zero, new Color(1f, 1f, 1f, 0.7f), 4);
                var main = r.main; main.loop = true; main.duration = 1f; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var em = r.emission; em.SetBursts(new ParticleSystem.Burst[0]); em.rateOverTime = 2.5f;
                r.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                Grow(r, 0.8f, 1.15f);
                Fade(r, new[] { 0f, 1f, 0f }, new[] { 0f, 0.3f, 1f });

                var m = Burst(root, "Motes", glow, 0, new Vector2(0.9f, 1.4f), new Vector2(0.12f, 0.25f), new Vector2(0f, 0f), new Color(1f, 1f, 1f, 0.9f), 30);
                var mm = m.main; mm.loop = true; mm.duration = 1f;
                var mem = m.emission; mem.SetBursts(new ParticleSystem.Burst[0]); mem.rateOverTime = 12f;
                var sh = m.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.7f; sh.rotation = new Vector3(90f, 0f, 0f);
                Rise(m, 1.6f);
                Fade(m, new[] { 0f, 1f, 0f }, new[] { 0f, 0.2f, 1f });
            });
        }

        private static void Build(string path, System.Action<Transform> fill)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject();
            root.name = Path.GetFileNameWithoutExtension(path);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            fill(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }

        // Einmaliger Ausstoß (kein Loop), Weltraum, skalierbar über das Wurzel-Objekt
        private static ParticleSystem Burst(Transform parent, string name, Material mat, int count, Vector2 life, Vector2 size,
            Vector2 speed, Color color, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false; main.playOnAwake = true;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = max;
            main.stopAction = ParticleSystemStopAction.None;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static void MeshParticles(ParticleSystem ps, Mesh mesh)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
            r.alignment = ParticleSystemRenderSpace.World;
            r.enableGPUInstancing = true;
            var main = ps.main;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        private static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = angle; sh.radius = radius;
            sh.rotation = new Vector3(-90f, 0f, 0f); // nach oben
        }

        private static void Sphere(ParticleSystem ps, float radius)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
        }

        private static void Gravity(ParticleSystem ps, float g) { var main = ps.main; main.gravityModifier = g; }

        private static void Spin(ParticleSystem ps)
        {
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rot.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
        }

        private static void Collide(ParticleSystem ps)
        {
            var c = ps.collision; c.enabled = true;
            c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision3D;
            c.quality = ParticleSystemCollisionQuality.Low;
            c.bounce = 0.3f; c.dampen = 0.45f; c.lifetimeLoss = 0f;
            c.radiusScale = 0.5f;
        }

        private static void Rise(ParticleSystem ps, float speed)
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            v.y = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed);
            v.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        private static void Grow(ParticleSystem ps, float from, float to)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from / Mathf.Max(from, to), 1f, to / Mathf.Max(from, to)));
            var main = ps.main;
            main.startSizeMultiplier *= Mathf.Max(from, to);
        }

        // Alpha-Verlauf (und optional Schrumpfen am Ende)
        private static void Fade(ParticleSystem ps, float[] alphas, float[] times, bool size = false)
        {
            var g = new Gradient();
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i], times[i]);
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, ak);
            var c = ps.colorOverLifetime; c.enabled = true; c.color = g;
            if (size)
            {
                var s = ps.sizeOverLifetime; s.enabled = true;
                s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
            }
        }
    }
}
