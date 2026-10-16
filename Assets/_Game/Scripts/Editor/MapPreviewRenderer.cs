using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ElementalBuddies.EditorTools
{
    // Vorschaubilder der Maps für die Map-Auswahl im Hauptmenü (Walddorf, Paket C-Menü):
    // - „Vorschaubild rendern“: rendert die Spielszene einer Map mit einer temporären Kamera (HideAndDontSave, die Szene
    //   wird nicht verändert und nicht gespeichert) nach Assets/_Game/UI/Maps/map_preview_<id>.png (Sprite) und trägt das
    //   Bild als MapDefinitionSO.Preview ein. Fehlt die Szenendatei (Walddorf noch nicht gebaut), passiert nichts.
    // - „Platzhalter erzeugen“: prozedural gemalte Ersatzbilder (Walddorf, allgemein) für Maps ohne Szene – werden
    //   später vom echten Render an derselben Stelle (gleiche GUID) ersetzt.
    // Das Hauptmenü nimmt Preview der Map, sonst das Ersatzbild per Id (MainMenuUI.MapPreviews, MainMenuBuilder.AssignMapPreviews).
    public static class MapPreviewRenderer
    {
        public const string Folder = "Assets/_Game/UI/Maps";
        public const string PlaceholderId = "placeholder";
        public const int Width = 960, Height = 540;

        // Kamera je Map (Weltposition, Blickziel, vertikales Sichtfeld). Unbekannte Maps: Schrägblick aus Südwesten,
        // abgeleitet aus MinimapCenter/WallHalfSize.
        private static readonly Dictionary<string, (Vector3 pos, Vector3 look, float fov)> Poses =
            new Dictionary<string, (Vector3, Vector3, float)>
            {
                { MapDefinitionSO.CityId, (new Vector3(-58f, 38f, -50f), new Vector3(4f, 0f, 4f), 42f) },
            };

        public static string PreviewPath(string id) => Folder + "/map_preview_" + id + ".png";

        [MenuItem("BuddyTD/Karten/Vorschaubilder aller Maps rendern")]
        public static void RenderAllMenu()
        {
            foreach (var m in MapEditorUtil.AllMaps()) Render(m);
        }

        [MenuItem("BuddyTD/Karten/Platzhalter-Vorschaubilder erzeugen (fehlende)")]
        public static void CreatePlaceholdersMenu() { CreatePlaceholders(false); }

        // Map rendern; false, wenn die Szene fehlt oder etwas schiefgeht
        public static bool Render(MapDefinitionSO map)
        {
            if (map == null) return false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("MapPreviewRenderer: nur im Edit-Modus."); return false; }
            if (string.IsNullOrEmpty(map.ScenePath) || !File.Exists(map.ScenePath))
            {
                Debug.LogWarning($"MapPreviewRenderer: Szene der Map „{map.Id}“ fehlt ({map.ScenePath}) – Platzhalter bleibt.");
                return false;
            }

            Scene prevActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(map.ScenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            bool wasDirty = wasLoaded && scene.isDirty;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(map.ScenePath, OpenSceneMode.Additive);

            // Andere geladene Szenen ausblenden (Menü-Kulisse o. Ä. soll nicht ins Bild), danach exakt wiederherstellen
            var hidden = new List<GameObject>();
            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (s == scene || !s.isLoaded) continue;
                    foreach (var go in s.GetRootGameObjects())
                        if (go.activeSelf) { go.SetActive(false); hidden.Add(go); }
                }
                SceneManager.SetActiveScene(scene); // RenderSettings (Nebel, Himmel, Umgebungslicht) der Map

                var pose = PoseFor(map);
                camGo = new GameObject("MapPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                camGo.transform.position = pose.pos;
                camGo.transform.LookAt(pose.look);
                cam.fieldOfView = pose.fov;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 600f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.allowHDR = true;
                cam.allowMSAA = true;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                cam.Render(); // zweites Bild: temporale Effekte/Auto-Belichtung eingeschwungen

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;

                string path = SavePng(map.Id, tex);
                AssignPreview(map, path);
                Debug.Log($"MapPreviewRenderer: {path} gerendert (Kamera {pose.pos} → {pose.look}).");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("MapPreviewRenderer: " + e);
                return false;
            }
            finally
            {
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (tex != null) Object.DestroyImmediate(tex);
                foreach (var go in hidden) if (go != null) go.SetActive(true);
                if (prevActive.IsValid() && prevActive.isLoaded) SceneManager.SetActiveScene(prevActive);
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
                else if (!wasDirty && scene.isDirty) Debug.LogWarning($"MapPreviewRenderer: {scene.path} ist jetzt als geändert markiert – nicht speichern nötig, Inhalt unverändert.");
            }
        }

        private static (Vector3 pos, Vector3 look, float fov) PoseFor(MapDefinitionSO map)
        {
            if (Poses.TryGetValue(map.Id, out var p)) return p;
            var c = new Vector3(map.MinimapCenter.x, 0f, map.MinimapCenter.y);
            float w = Mathf.Max(10f, map.WallHalfSize);
            return (c + new Vector3(-1.65f * w, 1.1f * w, -1.45f * w), c + new Vector3(0.1f * w, 0f, 0.1f * w), 42f);
        }

        private static string SavePng(string id, Texture2D tex)
        {
            EnsureFolder();
            string path = PreviewPath(id);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool changed = imp.textureType != TextureImporterType.Sprite || imp.mipmapEnabled || imp.spriteImportMode != SpriteImportMode.Single;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = false;
                imp.maxTextureSize = 1024;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                if (changed) imp.SaveAndReimport();
            }
            return path;
        }

        private static void AssignPreview(MapDefinitionSO map, string path)
        {
            var spr = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (spr == null || map.Preview == spr) return;
            map.Preview = spr;
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssetIfDirty(map);
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Game/UI", "Maps");
        }

        // ---------------- Platzhalter (prozedural) ----------------

        // Walddorf (Id "forest") und allgemeiner Platzhalter; overwrite = false lässt vorhandene Bilder (z. B. echte Renders) stehen
        public static void CreatePlaceholders(bool overwrite)
        {
            foreach (var id in new[] { "forest", PlaceholderId })
            {
                string path = PreviewPath(id);
                if (!overwrite && File.Exists(path)) continue;
                var tex = Paint(id == "forest");
                SavePng(id, tex);
                Object.DestroyImmediate(tex);
                Debug.Log("MapPreviewRenderer: Platzhalter " + path + " erzeugt.");
            }
        }

        // Dämmerungs-Landschaft im Stil der Menü-Kulisse: Himmelsverlauf, Hügel, Palisade mit Hütten und Tannen (Walddorf)
        // bzw. nur Hügel und Dunst (allgemein). Deterministisch.
        private static Texture2D Paint(bool forest)
        {
            int w = Width, h = Height;
            var px = new Color[w * h];
            var rnd = new System.Random(forest ? 2610 : 77);
            Color skyTop = new Color(0.20f, 0.17f, 0.33f), skyMid = new Color(0.62f, 0.36f, 0.38f), skyLow = new Color(0.98f, 0.66f, 0.42f);
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1); // 0 unten, 1 oben
                Color c = t > 0.55f ? Color.Lerp(skyMid, skyTop, (t - 0.55f) / 0.45f) : Color.Lerp(skyLow, skyMid, Mathf.InverseLerp(0.3f, 0.55f, t));
                for (int x = 0; x < w; x++) px[y * w + x] = c;
            }
            // Sonne knapp über dem Horizont
            Disc(px, w, h, w * 0.7f, h * 0.36f, h * 0.07f, new Color(1f, 0.86f, 0.6f), 0.9f);

            // Hügelketten (hinten heller/dunstiger)
            Ridge(px, w, h, rnd, 0.40f, 0.05f, new Color(0.36f, 0.30f, 0.40f), 0.004f);
            Ridge(px, w, h, rnd, 0.33f, 0.04f, forest ? new Color(0.20f, 0.26f, 0.24f) : new Color(0.30f, 0.25f, 0.30f), 0.007f);

            if (forest)
            {
                // ferner Wald: viele kleine Tannen auf der hinteren Kette
                for (int i = 0; i < 70; i++)
                    Fir(px, w, h, (float)rnd.NextDouble() * w, h * (0.30f + 0.05f * (float)rnd.NextDouble()), h * (0.06f + 0.03f * (float)rnd.NextDouble()), new Color(0.13f, 0.20f, 0.18f));
                // Lichtung (Boden)
                FillBelow(px, w, h, x => h * (0.22f + 0.02f * Mathf.Sin(x * 0.01f)), new Color(0.30f, 0.36f, 0.20f), new Color(0.22f, 0.28f, 0.15f));
                // Palisade
                float baseY = h * 0.24f;
                for (float x = w * 0.18f; x < w * 0.82f; x += 9f)
                {
                    float ph = h * (0.07f + 0.01f * (float)rnd.NextDouble());
                    FillRect(px, w, h, x, baseY, 7f, ph, new Color(0.36f, 0.25f, 0.16f));
                    Tri(px, w, h, x - 0.5f, baseY + ph, 8f, 6f, new Color(0.36f, 0.25f, 0.16f));
                }
                // Hütten hinter der Palisade
                House(px, w, h, w * 0.30f, baseY + h * 0.05f, 70f, 34f, 30f);
                House(px, w, h, w * 0.47f, baseY + h * 0.06f, 90f, 40f, 38f);
                House(px, w, h, w * 0.62f, baseY + h * 0.05f, 64f, 30f, 28f);
                // Rauch
                for (int i = 0; i < 18; i++) Disc(px, w, h, w * 0.49f + i * 3f, baseY + h * 0.2f + i * 7f, 6f + i * 0.8f, new Color(0.75f, 0.68f, 0.70f), 0.12f);
                // große Tannen vorne links/rechts
                for (int i = 0; i < 7; i++)
                {
                    float x = i < 4 ? w * (0.02f + 0.05f * i) : w * (0.83f + 0.05f * (i - 4));
                    Fir(px, w, h, x + (float)rnd.NextDouble() * 20f, h * 0.02f, h * (0.38f + 0.12f * (float)rnd.NextDouble()), new Color(0.07f, 0.13f, 0.11f));
                }
            }
            else
            {
                FillBelow(px, w, h, x => h * (0.22f + 0.02f * Mathf.Sin(x * 0.012f)), new Color(0.32f, 0.27f, 0.24f), new Color(0.22f, 0.18f, 0.16f));
            }
            // Vignette
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x / (float)w - 0.5f) * 1.6f, dy = (y / (float)h - 0.5f) * 1.6f;
                    float v = Mathf.Clamp01(1f - 0.35f * (dx * dx + dy * dy));
                    var c = px[y * w + x];
                    px[y * w + x] = new Color(c.r * v, c.g * v, c.b * v, 1f);
                }
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static void Blend(Color[] px, int w, int h, int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            px[i] = Color.Lerp(px[i], c, a);
        }

        private static void Disc(Color[] px, int w, int h, float cx, float cy, float r, Color c, float a)
        {
            for (int y = Mathf.FloorToInt(cy - r - 1); y <= cy + r + 1; y++)
                for (int x = Mathf.FloorToInt(cx - r - 1); x <= cx + r + 1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float k = Mathf.Clamp01(r - d + 0.5f);
                    if (k > 0f) Blend(px, w, h, x, y, c, a * k);
                }
        }

        private static void Ridge(Color[] px, int w, int h, System.Random rnd, float baseT, float amp, Color c, float freq)
        {
            float p1 = (float)rnd.NextDouble() * 10f, p2 = (float)rnd.NextDouble() * 10f;
            FillBelow(px, w, h, x => h * (baseT + amp * (Mathf.Sin(x * freq + p1) * 0.6f + Mathf.Sin(x * freq * 2.7f + p2) * 0.4f)), c, c);
        }

        private static void FillBelow(Color[] px, int w, int h, System.Func<int, float> top, Color upper, Color lower)
        {
            for (int x = 0; x < w; x++)
            {
                float t = top(x);
                for (int y = 0; y < h && y <= t + 1; y++)
                {
                    float k = Mathf.Clamp01(t - y + 0.5f);
                    Blend(px, w, h, x, y, Color.Lerp(lower, upper, t > 0 ? y / t : 1f), k);
                }
            }
        }

        private static void FillRect(Color[] px, int w, int h, float x0, float y0, float rw, float rh, Color c)
        {
            for (int y = Mathf.FloorToInt(y0); y < y0 + rh; y++)
                for (int x = Mathf.FloorToInt(x0); x < x0 + rw; x++) Blend(px, w, h, x, y, c, 1f);
        }

        // Dreieck mit Grundlinie bei y0 (Mitte cx), Breite bw, Höhe th
        private static void Tri(Color[] px, int w, int h, float x0, float y0, float bw, float th, Color c)
        {
            float cx = x0 + bw * 0.5f;
            for (int y = Mathf.FloorToInt(y0); y < y0 + th; y++)
            {
                float half = bw * 0.5f * (1f - (y - y0) / th);
                for (int x = Mathf.FloorToInt(cx - half); x <= cx + half; x++) Blend(px, w, h, x, y, c, 1f);
            }
        }

        private static void Fir(Color[] px, int w, int h, float cx, float y0, float th, Color c)
        {
            FillRect(px, w, h, cx - th * 0.03f, y0, th * 0.06f, th * 0.15f, new Color(0.18f, 0.12f, 0.08f));
            for (int i = 0; i < 3; i++)
            {
                float yb = y0 + th * (0.12f + 0.26f * i);
                float bw = th * (0.55f - 0.13f * i);
                Tri(px, w, h, cx - bw * 0.5f, yb, bw, th * (0.42f - 0.04f * i), c);
            }
        }

        private static void House(Color[] px, int w, int h, float cx, float y0, float bw, float wallH, float roofH)
        {
            FillRect(px, w, h, cx - bw * 0.5f, y0, bw, wallH, new Color(0.55f, 0.45f, 0.34f));
            Tri(px, w, h, cx - bw * 0.62f, y0 + wallH - 2f, bw * 1.24f, roofH, new Color(0.42f, 0.30f, 0.16f));
            // warm erleuchtetes Fenster
            FillRect(px, w, h, cx - bw * 0.12f, y0 + wallH * 0.35f, bw * 0.14f, wallH * 0.3f, new Color(1f, 0.78f, 0.38f));
        }
    }
}
