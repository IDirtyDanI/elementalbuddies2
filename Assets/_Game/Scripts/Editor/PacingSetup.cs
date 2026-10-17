using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Plan „Fesselung“, Sprint 2 (Spannung & Rückblick): richtet reproduzierbar ein
    //  1. Gegner-Icons (aus den eigenen Prefabs gerendert, 128², transparent) → EnemyConfigSO.Icon
    //  2. Anzeigenamen + Vorschau-Hinweise der Gegnertypen
    //  3. Neuheiten entzerren: Grabspinne ab Welle 7 (Ernte-Welle nach dem ersten Boss) statt 6
    // Menü: BuddyTD → Game Feel → Sprint 2 einrichten (Vorschau, Icons).
    // Walddorf (C-Theme): Namen/Hinweise/Icons für alle Gegner-Themes (Untote, Dorf, Flieger); nur Icons + Texte ohne
    // Szenenänderung über BuddyTD → Walddorf → Gegner-Namen und -Icons (alle Themes).
    public static class PacingSetup
    {
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string ConfigDir = "Assets/ScriptableObjects/Configs/";
        private const string GameScene = MapEditorUtil.CityScenePath; // Stadt (Walddorf P0: eine Konstante)
        private const int Size = 128, Super = 4;

        private struct EnemyInfo
        {
            public string Asset, Name, Hint;
            public EnemyInfo(string asset, string name, string hint) { Asset = asset; Name = name; Hint = hint; }
        }

        // Asset-Pfad relativ zu ConfigDir ohne „.asset“; Name null = DisplayName nicht anfassen (Stadt-Bosse: BossBuilder)
        private static readonly EnemyInfo[] Enemies =
        {
            new EnemyInfo("Runner", "Skelett-Läufer", "Läuft direkt zum Nexus"),
            new EnemyInfo("Swarm", "Knochenschwarm", "Viele, schnell und schwach – Flächenschaden hilft"),
            new EnemyInfo("SkeletonArcher", "Skelett-Bogenschütze", "Fernkampf – beschießt Buddies"),
            new EnemyInfo("Knight", "Skelett-Ritter", "Gepanzert (−Schaden), greift Buddies im Weg an"),
            new EnemyInfo("GraveSpider", "Grabspinne", "Jagt Buddies – Erde-Buddy lenkt sie per Spott ab"),
            new EnemyInfo("Bosses/BossBoneLord", null, "Nahkampf-Boss: Bodenstampfer – aus den roten Flächen gehen"),
            new EnemyInfo("Bosses/BossNecromancer", null, "Beschwört Diener und heilt sie – zuerst ausschalten"),
            new EnemyInfo("Bosses/BossDeathHunter", null, "Fernkampf-Boss mit Pfeilhagel – in Bewegung bleiben"),
            // Flieger (Configs von C-Flieger; fehlen sie, wird übersprungen)
            new EnemyInfo("Flyer_Bat", "Riesen-Fledermaus", "Fliegt über Häuser und Buddies direkt zum Nexus – stößt zum Angriff herab"),
            new EnemyInfo("Flyer_Wyvern", "Mini-Wyvern", "Fliegt über Häuser und Buddies direkt zum Nexus – stößt zum Angriff herab"),
            // Walddorf-Theme (gleiche Rollen und Werte wie die Untoten, ThemeSetup)
            new EnemyInfo("Forest/Goblin_Pluenderer", "Goblin-Plünderer", "Läuft direkt zum Nexus"),
            new EnemyInfo("Forest/Wolf", "Wolf", "Viele, schnell und schwach – Flächenschaden hilft"),
            new EnemyInfo("Forest/Goblin_Bogenschuetze", "Goblin-Bogenschütze", "Fernkampf – beschießt Buddies"),
            new EnemyInfo("Forest/Ork_Krieger", "Ork-Krieger", "Gepanzert (−Schaden), greift Buddies im Weg an"),
            new EnemyInfo("Forest/Waldspinne", "Waldspinne", "Jagt Buddies – Erde-Buddy lenkt sie per Spott ab"),
            new EnemyInfo("Forest/Ork_Kriegsherr", "Ork-Kriegsherr", "Nahkampf-Boss: Kriegsstampfer – aus den roten Flächen gehen"),
            new EnemyInfo("Forest/Goblin_Schamane", "Goblin-Schamane", "Beschwört Diener und heilt sie – zuerst ausschalten"),
            new EnemyInfo("Forest/Ork_Faehrtenmeister", "Ork-Fährtenmeister", "Fernkampf-Boss mit Speerhagel – in Bewegung bleiben"),
            new EnemyInfo("Forest/Ork_Rammbock", "Ork-Rammbock", null),
            // Champion-Bedrohungen (Bannschild – nur der Champion kann sie verletzen)
            new EnemyInfo("Knochenbeschwoerer", ChampionThreatSetup.SummonerName, ChampionThreatSetup.SummonerHint),
            new EnemyInfo("Grabhexe", ChampionThreatSetup.HexerName, ChampionThreatSetup.HexerHint),
            new EnemyInfo("Forest/Goblin_Trommler", ChampionThreatSetup.DrummerName, ChampionThreatSetup.DrummerHint),
            new EnemyInfo("Forest/Dornenhexe", ChampionThreatSetup.ThornHagName, ChampionThreatSetup.ThornHagHint),
        };

        // Name/Hinweis aus der Tabelle auf eine Config schreiben (ThemeSetup beim Anlegen der Dorf-Configs)
        public static bool ApplyText(EnemyConfigSO cfg, string assetPath)
        {
            if (cfg == null || string.IsNullOrEmpty(assetPath)) return false;
            string rel = assetPath.StartsWith(ConfigDir) ? assetPath.Substring(ConfigDir.Length) : assetPath;
            if (rel.EndsWith(".asset")) rel = rel.Substring(0, rel.Length - 6);
            foreach (var e in Enemies)
            {
                if (e.Asset != rel) continue;
                if (!string.IsNullOrEmpty(e.Name)) cfg.DisplayName = e.Name;
                if (e.Hint != null) cfg.PreviewHint = e.Hint;
                return true;
            }
            return false;
        }

        [MenuItem("BuddyTD/Game Feel/Sprint 2 einrichten (Vorschau, Icons)")]
        public static void SetupAll()
        {
            SetupEnemyTexts(true);
            SpreadNovelties();
            Debug.Log("PacingSetup: fertig.");
        }

        // Namen, Hinweise und Icons aller Gegner-Themes – ohne Szenenänderung. Untoten-Icons werden nur neu gerendert,
        // wenn sie fehlen (sonst bleiben die PNGs byte-gleich); Dorf- und Flieger-Icons immer (Platzhalter → Modelle).
        [MenuItem("BuddyTD/Walddorf/Gegner-Namen und -Icons (alle Themes)")]
        public static void SetupEnemyTextsMenu() => SetupEnemyTexts(false);

        public static void SetupEnemyTexts(bool rerenderUndead)
        {
            Directory.CreateDirectory(IconDir);
            foreach (var e in Enemies)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + e.Asset + ".asset");
                if (cfg == null) { Debug.LogWarning("PacingSetup: Config fehlt: " + e.Asset); continue; }
                if (!string.IsNullOrEmpty(e.Name)) cfg.DisplayName = e.Name;
                if (e.Hint != null) cfg.PreviewHint = e.Hint;
                // Dorf-Configs, deren Prefab noch das Untoten-Prefab ist (Platzhalter fehlt), behalten das Untoten-Icon
                bool borrowedPrefab = e.Asset.StartsWith("Forest/") && cfg.Prefab != null
                                      && !AssetDatabase.GetAssetPath(cfg.Prefab).Contains("/Forest/");
                string file = "icon_enemy_" + Path.GetFileName(e.Asset).ToLowerInvariant() + ".png";
                bool themed = e.Asset.StartsWith("Forest/") || e.Asset.StartsWith("Flyer_");
                bool skipRender = !rerenderUndead && !themed && File.Exists(IconDir + file);
                if (e.Asset.StartsWith("Flyer_") && cfg.Prefab != null)
                {
                    // Flieger: eigene Icon-Kamera (schräg von oben, volle Spannweite) aus FlyerPlaceholderBuilder (#376)
                    FlyerPlaceholderBuilder.RenderIcon(cfg, new System.Text.StringBuilder());
                }
                else if (cfg.Prefab != null && !borrowedPrefab && !skipRender)
                {
                    RenderIcon(cfg.Prefab, IconDir + file);
                    AssetDatabase.ImportAsset(IconDir + file);
                    var imp = AssetImporter.GetAtPath(IconDir + file) as TextureImporter;
                    if (imp != null)
                    {
                        imp.textureType = TextureImporterType.Sprite;
                        imp.spriteImportMode = SpriteImportMode.Single; // Projekt-Vorgabe ist Multiple
                        imp.alphaIsTransparency = true;
                        imp.mipmapEnabled = false;
                        imp.SaveAndReimport();
                    }
                    cfg.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + file);
                }
                EditorUtility.SetDirty(cfg);
            }
            AssetDatabase.SaveAssets();
        }

        // Grabspinne (neuer Buddy-Jäger) nicht in der ersten Boss-Welle, sondern in der ruhigeren Ernte-Welle danach
        private static void SpreadNovelties()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GameScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(GameScene);
            }
            var wm = Object.FindFirstObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (wm == null) return;
            foreach (var g in wm.ExtraGroups)
                if (g != null && g.Config != null && g.Config.name == "GraveSpider" && g.StartWave == 6) g.StartWave = 7;
            EditorUtility.SetDirty(wm);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ---------------- Icon-Render (wie PortraitRenderer: Schwarz/Weiß-Differenz → Alpha, Supersampling) ----------------

        // size/yaw/up/frame optional (Elite-Symbole: 256 px, eigener Blickwinkel)
        public static void RenderIcon(GameObject prefab, string path, int size = Size, float yaw = 25f, float up = 0.35f, float frame = 0.88f)
        {
            int n = size * Super;
            var holder = new GameObject("__IconHolder") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.position = new Vector3(0f, -600f, 0f);
            var camGo = new GameObject("__IconCam") { hideFlags = HideFlags.HideAndDontSave };
            var key = new GameObject("__IconKey") { hideFlags = HideFlags.HideAndDontSave };
            var fill = new GameObject("__IconFill") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(n, n, 24, RenderTextureFormat.ARGB32);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.hideFlags = HideFlags.HideAndDontSave;
                inst.transform.SetParent(holder.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                foreach (var anim in inst.GetComponentsInChildren<Animator>(true))
                {
                    if (anim.runtimeAnimatorController == null) continue;
                    anim.Rebind();
                    anim.Update(0f);
                }
                // HP-Leisten, Partikel o. Ä. nicht mitrendern – nur Meshes
                foreach (var ps in inst.GetComponentsInChildren<ParticleSystemRenderer>(true)) ps.enabled = false;

                Bounds b = new Bounds(inst.transform.position, Vector3.zero);
                bool any = false;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || !r.enabled) continue;
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
                }

                var l1 = key.AddComponent<Light>();
                l1.type = LightType.Directional; l1.intensity = 1.3f; l1.color = new Color(1f, 0.97f, 0.92f);
                key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                var l2 = fill.AddComponent<Light>();
                l2.type = LightType.Directional; l2.intensity = 0.5f; l2.color = new Color(0.85f, 0.9f, 1f);
                fill.transform.rotation = Quaternion.Euler(10f, 230f, 0f);

                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 20f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                float radius = Mathf.Max(b.extents.y, Mathf.Max(b.extents.x, b.extents.z) * 0.9f);
                float dist = radius * frame / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * inst.transform.forward + Vector3.up * up;
                cam.transform.position = b.center + dir.normalized * dist;
                cam.transform.LookAt(b.center);

                var black = Grab(cam, rt, Color.black);
                var white = Grab(cam, rt, Color.white);
                var outPx = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float r = 0f, g = 0f, bl = 0f, al = 0f;
                        for (int sy = 0; sy < Super; sy++)
                            for (int sx = 0; sx < Super; sx++)
                            {
                                int i = (y * Super + sy) * n + (x * Super + sx);
                                Color cb = black[i], cw = white[i];
                                al += 1f - Mathf.Clamp01(((cw.r - cb.r) + (cw.g - cb.g) + (cw.b - cb.b)) / 3f);
                                r += cb.r; g += cb.g; bl += cb.b;
                            }
                        float k = 1f / (Super * Super);
                        al *= k;
                        outPx[y * size + x] = al > 0.001f ? new Color(Mathf.Clamp01(r * k / al), Mathf.Clamp01(g * k / al), Mathf.Clamp01(bl * k / al), al) : new Color(0f, 0f, 0f, 0f);
                    }
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.SetPixels(outPx);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
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
        }

        private static Color[] Grab(Camera cam, RenderTexture rt, Color bg)
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
    }
}
