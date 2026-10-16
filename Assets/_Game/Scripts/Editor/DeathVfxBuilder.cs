using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // #409: eigene Tod-Effekte für die Dorf-Gegner und die Flieger (vorher Knochensplitter VFX_EnemyDeath für alle).
    //  1. Texturen (prozedural, weiß + Alpha, Farbe kommt aus den Partikeln): Blatt, Fellbüschel, Feder, Schuppe
    //  2. Meshes (low poly, flach schattiert): Holzsplitter, Erdbrocken; Materialien Holz/Erde/Chitin (Simple Lit)
    //  3. Prefabs Assets/_Game/VFX/Prefabs/Death/VFX_Death_*.prefab (einmaliger Ausstoß, Weltraum, Lebensdauer ≤ 2,4 s –
    //     FeedbackDirector zerstört nach 2,5 s bzw. Bosse nach 5 s)
    //  4. EnemyConfigSO.DeathVfx der Dorf-Configs und beider Flieger setzen (Untoten-Configs bleiben leer = Knochen).
    // FeedbackDirector wählt Config.DeathVfx vor dem Standard; Clients feuern OnEnemyKilled über EnemyNet → HandleRemoteKilled,
    // der Effekt erscheint also auf allen Rechnern. ThemeSetup.SetupVariant behält das Feld beim erneuten Lauf.
    // Mehrfach ausführbar (Prefabs werden geleert und neu gefüllt, GUIDs bleiben). Menü: BuddyTD → Game Feel → Tod-Effekte …
    public static class DeathVfxBuilder
    {
        private const string TexDir = "Assets/_Game/VFX/Textures/GameFeel/";
        private const string MatDir = "Assets/_Game/VFX/Materials/";
        private const string MeshDir = "Assets/_Game/VFX/Meshes/";
        public const string PrefabDir = "Assets/_Game/VFX/Prefabs/Death/";
        private const string ConfigDir = "Assets/ScriptableObjects/Configs/";

        public const string Forest = PrefabDir + "VFX_Death_Forest.prefab";
        public const string Fur = PrefabDir + "VFX_Death_Fur.prefab";
        public const string Spider = PrefabDir + "VFX_Death_Spider.prefab";
        public const string Wood = PrefabDir + "VFX_Death_Wood.prefab";
        public const string Bat = PrefabDir + "VFX_Death_Bat.prefab";
        public const string Wyvern = PrefabDir + "VFX_Death_Wyvern.prefab";
        public const string BossForest = PrefabDir + "VFX_BossDeath_Forest.prefab";

        // Config (relativ zu ConfigDir) → Effekt
        public static readonly KeyValuePair<string, string>[] Assignments =
        {
            new KeyValuePair<string, string>("Forest/Goblin_Pluenderer", Forest),
            new KeyValuePair<string, string>("Forest/Goblin_Bogenschuetze", Forest),
            new KeyValuePair<string, string>("Forest/Ork_Krieger", Forest),
            new KeyValuePair<string, string>("Forest/Wolf", Fur),
            new KeyValuePair<string, string>("Forest/Waldspinne", Spider),
            new KeyValuePair<string, string>("Forest/Ork_Rammbock", Wood),
            new KeyValuePair<string, string>("Forest/Ork_Kriegsherr", BossForest),
            new KeyValuePair<string, string>("Forest/Ork_Faehrtenmeister", BossForest),
            new KeyValuePair<string, string>("Forest/Goblin_Schamane", BossForest),
            new KeyValuePair<string, string>("Flyer_Bat", Bat),
            new KeyValuePair<string, string>("Flyer_Wyvern", Wyvern),
        };

        [MenuItem("BuddyTD/Game Feel/Tod-Effekte Dorf & Flieger bauen")]
        public static void BuildAllMenu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            var sb = new StringBuilder("DeathVfxBuilder:");
            BuildTextures();
            BuildMeshes();
            BuildPrefabs();
            AssetDatabase.SaveAssets();
            sb.Append(Assign());
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // Nur die Config-Felder setzen (Prefabs müssen existieren)
        public static string Assign()
        {
            var sb = new StringBuilder();
            foreach (var kv in Assignments)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + kv.Key + ".asset");
                var fx = AssetDatabase.LoadAssetAtPath<GameObject>(kv.Value);
                if (cfg == null || fx == null) { sb.Append($"\n  fehlt: {kv.Key} / {kv.Value}"); continue; }
                if (cfg.DeathVfx != fx) { cfg.DeathVfx = fx; EditorUtility.SetDirty(cfg); }
                sb.Append($"\n  {kv.Key} → {fx.name}");
            }
            return sb.ToString();
        }

        // ---------------- 1. Texturen ----------------

        private static readonly string[] TexNames = { "gf_leaf.png", "gf_fur.png", "gf_feather.png", "gf_scale.png" };

        private static void BuildTextures()
        {
            Directory.CreateDirectory(TexDir);
            // Blatt: spitzes Oval mit Stiel, Mittel- und Seitenrippen etwas dunkler
            WriteTex("gf_leaf.png", 64, (u, v) =>
            {
                float y = v * 1.05f;
                float w = 0.56f * Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)) * (1f - 0.3f * y);
                float a = Edge(w - Mathf.Abs(u), 0.04f);
                float stem = v < -0.8f ? Edge(0.05f - Mathf.Abs(u + (v + 0.8f) * 0.1f), 0.02f) : 0f;
                float rib = Mathf.Exp(-Mathf.Pow(u / 0.035f, 2f));
                float side = Mathf.Pow(Mathf.Abs(Mathf.Sin((v - Mathf.Abs(u) * 0.9f) * 9f)), 18f) * 0.6f;
                float shade = Mathf.Lerp(1f, 0.72f, Mathf.Max(rib, side)) * Mathf.Lerp(0.85f, 1f, (v + 1f) * 0.5f);
                return new Color(shade, shade, shade, Mathf.Max(a, stem));
            });
            // Fellbüschel: 9 leicht gebogene, spitz zulaufende Strähnen aus einem Ansatz
            WriteTex("gf_fur.png", 64, (u, v) =>
            {
                float best = 0f, tone = 1f;
                for (int i = 0; i < 9; i++)
                {
                    float ang = Mathf.Lerp(-0.62f, 0.62f, i / 8f) + Mathf.Sin(i * 2.3f) * 0.08f;
                    float len = 1.45f + Mathf.Sin(i * 1.7f) * 0.25f;
                    // Punkt in Strähnen-Koordinaten (Ansatz bei (0,-0.85))
                    float px = u, py = v + 0.85f;
                    float s = Mathf.Cos(ang) * py + Mathf.Sin(ang) * px;      // entlang
                    float t = -Mathf.Sin(ang) * py + Mathf.Cos(ang) * px;     // quer
                    if (s < 0f || s > len) continue;
                    float k = s / len;
                    t -= 0.18f * k * k * Mathf.Sign(ang + 0.001f);            // Biegung nach außen
                    float th = Mathf.Lerp(0.09f, 0.012f, k);
                    float a = Mathf.Exp(-Mathf.Pow(t / th, 2f)) * Mathf.Lerp(1f, 0.55f, k);
                    if (a > best) { best = a; tone = 0.8f + 0.2f * Mathf.Sin(i * 3.1f); }
                }
                return new Color(tone, tone, tone, best);
            });
            // Feder: Kiel + Fahne mit schrägen Ästen
            WriteTex("gf_feather.png", 64, (u, v) =>
            {
                float quill = v < 0.95f ? Edge(0.03f - Mathf.Abs(u), 0.015f) : 0f;
                float y = (v + 0.75f) / 1.7f;
                float w = y > 0f && y < 1f ? 0.42f * Mathf.Pow(Mathf.Sin(Mathf.PI * y), 0.7f) * (1f - 0.25f * y) : 0f;
                float vane = Edge(w - Mathf.Abs(u), 0.03f);
                float barbs = 0.7f + 0.3f * Mathf.Sin((v - Mathf.Abs(u) * 1.2f) * 55f);
                float a = Mathf.Max(quill, vane * barbs);
                float shade = quill > 0.5f ? 1f : Mathf.Lerp(0.95f, 0.7f, Mathf.Abs(u) / 0.42f);
                return new Color(shade, shade, shade, a);
            });
            // Schuppe: gerundetes Schild, oben heller, Mittelgrat
            WriteTex("gf_scale.png", 64, (u, v) =>
            {
                float yy = v + 0.15f;
                float d = Mathf.Pow(Mathf.Abs(u) / 0.8f, 1.7f) + Mathf.Pow(Mathf.Abs(yy) / (yy > 0f ? 0.85f : 0.95f), 1.7f);
                float a = Edge(1f - d, 0.08f);
                float shade = Mathf.Lerp(0.62f, 1f, (v + 1f) * 0.5f) + Mathf.Exp(-Mathf.Pow(u / 0.08f, 2f)) * 0.15f;
                shade = Mathf.Clamp01(shade * (d > 0.75f ? 0.8f : 1f));
                return new Color(shade, shade, shade, a);
            });
            AssetDatabase.Refresh();
            foreach (var f in TexNames)
            {
                var imp = AssetImporter.GetAtPath(TexDir + f) as TextureImporter;
                if (imp == null) continue;
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
        }

        private static float Edge(float d, float soft) => Mathf.Clamp01(d / soft + 0.5f);

        private static void WriteTex(string file, int n, System.Func<float, float, Color> f)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // 2×2-Überabtastung gegen harte Kanten
                Color c = Color.clear;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                {
                    float u = (x + 0.25f + sx * 0.5f) / n * 2f - 1f, v = (y + 0.25f + sy * 0.5f) / n * 2f - 1f;
                    c += f(u, v);
                }
                c *= 0.25f;
                c.a = Mathf.Clamp01(c.a);
                px[y * n + x] = c;
            }
            tex.SetPixels(px);
            File.WriteAllBytes(TexDir + file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ---------------- 2. Meshes & Materialien ----------------

        private static void BuildMeshes()
        {
            Directory.CreateDirectory(MeshDir);
            SaveMesh("GF_WoodSplinter", FillSplinter);
            SaveMesh("GF_Clod", FillClod);
        }

        private static void SaveMesh(string name, System.Action<Mesh> fill)
        {
            string p = MeshDir + name + ".asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, p); }
            fill(m);
            m.name = name;
            EditorUtility.SetDirty(m);
        }

        // Holzsplitter: unregelmäßiges Fünfeck-Prisma (0,5 m lang, flach), beide Enden schräg gesplittert
        private static void FillSplinter(Mesh m)
        {
            const int sides = 5;
            float[] ys = { -0.25f, -0.17f, 0.12f, 0.25f };
            float[] rs = { 0.012f, 1f, 0.85f, 0.01f };
            float[] sx = { 0.07f, 0.055f, 0.08f, 0.06f, 0.05f };
            float[] sz = { 0.03f, 0.035f, 0.025f, 0.03f, 0.028f };
            var verts = new List<Vector3>();
            for (int r = 0; r < ys.Length; r++)
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    float rx = r == 0 || r == ys.Length - 1 ? rs[r] : sx[s] * rs[r];
                    float rz = r == 0 || r == ys.Length - 1 ? rs[r] : sz[s] * rs[r];
                    float y = ys[r] + (r == ys.Length - 1 ? Mathf.Sin(s * 2.1f) * 0.05f : r == 0 ? Mathf.Cos(s * 1.7f) * 0.04f : 0f);
                    verts.Add(new Vector3(Mathf.Cos(a) * rx + (r == ys.Length - 1 ? 0.02f : 0f), y, Mathf.Sin(a) * rz));
                }
            var tris = new List<int>();
            for (int r = 0; r < ys.Length - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = r * sides + s, b = r * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            int bottom = verts.Count; verts.Add(new Vector3(0f, -0.27f, 0f));
            int top = verts.Count; verts.Add(new Vector3(0.02f, 0.29f, 0f));
            int last = (ys.Length - 1) * sides;
            for (int s = 0; s < sides; s++)
            {
                tris.AddRange(new[] { bottom, s, (s + 1) % sides });
                tris.AddRange(new[] { top, last + (s + 1) % sides, last + s });
            }
            Flat(m, verts, tris);
        }

        // Erdbrocken: Oktaeder einmal unterteilt, Ecken deterministisch verbeult (Ø ≈ 0,25 m)
        private static void FillClod(Mesh m)
        {
            var v = new List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var t = new List<int> { 2,4,0, 2,0,5, 2,5,1, 2,1,4, 3,0,4, 3,5,0, 3,1,5, 3,4,1 };
            var mid = new Dictionary<long, int>();
            System.Func<int, int, int> Mid = (a, b) =>
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int i)) return i;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                mid[key] = v.Count - 1;
                return v.Count - 1;
            };
            var nt = new List<int>();
            for (int i = 0; i < t.Count; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (int i = 0; i < v.Count; i++)
            {
                var p = v[i];
                float n = 0.78f + 0.22f * Mathf.Sin(p.x * 5.1f + p.y * 3.3f) * Mathf.Cos(p.z * 4.7f - p.x * 1.9f);
                v[i] = new Vector3(p.x * 0.14f, p.y * 0.1f, p.z * 0.12f) * n;
            }
            Flat(m, v, nt);
        }

        private static void Flat(Mesh m, List<Vector3> verts, List<int> tris)
        {
            var fv = new List<Vector3>();
            var ft = new List<int>();
            for (int i = 0; i < tris.Count; i++) { fv.Add(verts[tris[i]]); ft.Add(i); }
            m.Clear();
            m.SetVertices(fv);
            m.SetTriangles(ft, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
        }

        private static Material LitMat(string name, Color c, float smooth)
        {
            string p = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- 3. Prefabs ----------------

        private static void BuildPrefabs()
        {
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            var dust = GameFeelSetup.ParticleMat("GF_Dust_Alpha", "VFX_LavaTrail", "gf_dust.png");
            var dot = GameFeelSetup.ParticleMat("GF_SoftDot_Alpha", "VFX_LavaTrail", "gf_softdot.png");
            var glow = GameFeelSetup.ParticleMat("GF_SoftDot_Add", "VFX_SoftDot_Add", "gf_softdot.png");
            var ring = GameFeelSetup.ParticleMat("GF_Ring_Add", "VFX_SoftDot_Add", "gf_ring.png");
            var leaf = GameFeelSetup.ParticleMat("GF_Leaf_Alpha", "VFX_LavaTrail", "gf_leaf.png");
            var fur = GameFeelSetup.ParticleMat("GF_Fur_Alpha", "VFX_LavaTrail", "gf_fur.png");
            var feather = GameFeelSetup.ParticleMat("GF_Feather_Alpha", "VFX_LavaTrail", "gf_feather.png");
            var scale = GameFeelSetup.ParticleMat("GF_Scale_Alpha", "VFX_LavaTrail", "gf_scale.png");
            var wood = LitMat("GF_Wood", new Color(0.55f, 0.38f, 0.21f), 0.1f);
            var woodDark = LitMat("GF_WoodDark", new Color(0.36f, 0.24f, 0.13f), 0.1f);
            var earth = LitMat("GF_Earth", new Color(0.34f, 0.25f, 0.16f), 0.05f);
            var chitin = LitMat("GF_Chitin", new Color(0.16f, 0.13f, 0.11f), 0.65f);
            var splinter = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "GF_WoodSplinter.asset");
            var clod = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "GF_Clod.asset");

            var dustBrown = new Color(0.5f, 0.4f, 0.28f, 0.6f);
            var leafA = new Color(0.36f, 0.55f, 0.2f, 1f);
            var leafB = new Color(0.62f, 0.55f, 0.22f, 1f);

            // Goblins & Orks: Erdstaub, Erdbrocken, aufgewirbelte Blätter
            GameFeelSetup.Build(Forest, root =>
            {
                DustPuff(root, dust, dustBrown, 7);
                Clods(root, earth, clod, 6, 1f);
                Leaves(root, leaf, leafA, leafB, 10, 1f);
            });

            // Wolf: Staub, graue Fellfetzen, ein paar Blätter
            GameFeelSetup.Build(Fur, root =>
            {
                // Wolf spielt mit VisualScale 0,6 → FeedbackDirector skaliert den Effekt auf 0,6; daher größer angelegt
                DustPuff(root, dust, new Color(0.55f, 0.48f, 0.4f, 0.6f), 7, 1.5f);
                var f = Flakes(root, "Fur", fur, new Color(0.86f, 0.84f, 0.8f, 1f), new Color(0.56f, 0.54f, 0.5f, 1f), 18,
                    new Vector2(1.3f, 2f), new Vector2(0.55f, 0.85f), new Vector2(2f, 4f), 0.3f, 1.2f);
                GameFeelSetup.Cone(f, 50f, 0.3f);
                Clods(root, earth, clod, 4, 1.3f);
                Leaves(root, leaf, leafA, leafB, 5, 1.3f);
            });

            // Waldspinne: Staub, dunkle Chitin-Splitter, grünliche Tropfen, Blätter
            GameFeelSetup.Build(Spider, root =>
            {
                DustPuff(root, dust, dustBrown, 6);
                var c = GameFeelSetup.Burst(root, "Chitin", chitin, 8, new Vector2(0.9f, 1.2f), new Vector2(0.45f, 0.8f), new Vector2(2.5f, 4.5f), Color.white, 12);
                GameFeelSetup.MeshParticles(c, clod);
                GameFeelSetup.Cone(c, 40f, 0.25f);
                GameFeelSetup.Gravity(c, 1.6f);
                GameFeelSetup.Spin(c);
                GameFeelSetup.Collide(c);
                GameFeelSetup.Fade(c, new[] { 1f, 1f, 0f }, new[] { 0f, 0.8f, 1f }, size: true);
                var d = GameFeelSetup.Burst(root, "Ichor", dot, 10, new Vector2(0.5f, 0.8f), new Vector2(0.14f, 0.24f), new Vector2(2f, 4f), new Color(0.6f, 0.78f, 0.22f, 0.9f), 12);
                GameFeelSetup.Cone(d, 45f, 0.2f);
                GameFeelSetup.Gravity(d, 1.5f);
                GameFeelSetup.Fade(d, new[] { 1f, 1f, 0f }, new[] { 0f, 0.7f, 1f }, size: true);
                Leaves(root, leaf, leafA, leafB, 5, 0.8f);
            });

            // Ork-Rammbock: Holzsplitter (hell/dunkel), große Staubwolke, Erdbrocken
            GameFeelSetup.Build(Wood, root =>
            {
                DustPuff(root, dust, new Color(0.52f, 0.43f, 0.32f, 0.65f), 10, 1.4f);
                Splinters(root, "Splinters", wood, splinter, 10);
                Splinters(root, "SplintersDark", woodDark, splinter, 6);
                Clods(root, earth, clod, 4, 1f);
            });

            // Riesen-Fledermaus: dunkle Fellbüschel und Hautfetzen segeln herab, dunkler Staubstoß
            GameFeelSetup.Build(Bat, root =>
            {
                DustPuff(root, dust, new Color(0.25f, 0.22f, 0.24f, 0.5f), 5);
                var f = Flakes(root, "Fur", fur, new Color(0.42f, 0.34f, 0.33f, 1f), new Color(0.26f, 0.2f, 0.22f, 1f), 12,
                    new Vector2(1.5f, 2.2f), new Vector2(0.3f, 0.46f), new Vector2(1.2f, 2.6f), 0.22f, 1.4f);
                GameFeelSetup.Sphere(f, 0.35f);
                var m = Flakes(root, "Membrane", scale, new Color(0.5f, 0.34f, 0.38f, 1f), new Color(0.3f, 0.2f, 0.24f, 1f), 7,
                    new Vector2(1.6f, 2.2f), new Vector2(0.26f, 0.4f), new Vector2(1f, 2.2f), 0.2f, 1.6f);
                GameFeelSetup.Sphere(m, 0.3f);
            });

            // Mini-Wyvern: grüne Schuppen (blitzen), ein paar helle Federn am Kamm
            GameFeelSetup.Build(Wyvern, root =>
            {
                DustPuff(root, dust, new Color(0.45f, 0.48f, 0.38f, 0.45f), 5);
                var s = Flakes(root, "Scales", scale, new Color(0.38f, 0.62f, 0.3f, 1f), new Color(0.68f, 0.78f, 0.36f, 1f), 16,
                    new Vector2(1.2f, 1.9f), new Vector2(0.22f, 0.34f), new Vector2(2f, 4f), 0.45f, 1.1f);
                GameFeelSetup.Sphere(s, 0.35f);
                var f = Flakes(root, "Feathers", feather, new Color(0.85f, 0.8f, 0.62f, 1f), new Color(0.6f, 0.52f, 0.34f, 1f), 4,
                    new Vector2(1.6f, 2.2f), new Vector2(0.25f, 0.38f), new Vector2(1f, 2f), 0.18f, 1.5f);
                GameFeelSetup.Sphere(f, 0.3f);
            });

            // Ork-/Goblin-Bosse: Erdexplosion – Brocken, Staubring, Schockwelle, warmer Blitz, Blätter-Wirbel
            GameFeelSetup.Build(BossForest, root =>
            {
                var b = GameFeelSetup.Burst(root, "Clods", earth, 26, new Vector2(1.4f, 2f), new Vector2(1.2f, 2.2f), new Vector2(5f, 11f), Color.white, 36);
                GameFeelSetup.MeshParticles(b, clod);
                GameFeelSetup.Cone(b, 55f, 0.8f);
                GameFeelSetup.Gravity(b, 1.8f);
                GameFeelSetup.Spin(b);
                GameFeelSetup.Collide(b);
                GameFeelSetup.Fade(b, new[] { 1f, 1f, 0f }, new[] { 0f, 0.85f, 1f }, size: true);

                var d = GameFeelSetup.Burst(root, "DustRing", dust, 22, new Vector2(1f, 1.6f), new Vector2(2f, 3.2f), new Vector2(4f, 7f), new Color(0.5f, 0.41f, 0.3f, 0.65f), 30);
                var sh = d.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.8f; sh.rotation = new Vector3(90f, 0f, 0f);
                var lim = d.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.5f; lim.dampen = 0.08f;
                GameFeelSetup.Grow(d, 0.7f, 1.8f);
                GameFeelSetup.Fade(d, new[] { 0f, 1f, 0f }, new[] { 0f, 0.1f, 1f });

                var r = GameFeelSetup.Burst(root, "Shockwave", ring, 1, new Vector2(0.65f, 0.65f), new Vector2(1.5f, 1.5f), Vector2.zero, new Color(1f, 0.75f, 0.4f, 0.9f), 1);
                r.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                r.transform.localPosition = new Vector3(0f, -0.8f, 0f);
                GameFeelSetup.Grow(r, 1f, 12f);
                GameFeelSetup.Fade(r, new[] { 1f, 0.8f, 0f }, new[] { 0f, 0.3f, 1f });

                var f = GameFeelSetup.Burst(root, "Flash", glow, 1, new Vector2(0.25f, 0.25f), new Vector2(6f, 6f), Vector2.zero, new Color(1f, 0.82f, 0.55f, 1f), 1);
                GameFeelSetup.Fade(f, new[] { 1f, 0f }, new[] { 0f, 1f });

                var l = Flakes(root, "LeafStorm", leaf, leafA, leafB, 36, new Vector2(2.2f, 3.4f), new Vector2(0.3f, 0.5f), new Vector2(3f, 7f), 0.3f, 0.9f);
                GameFeelSetup.Sphere(l, 1f);
                var ov = l.velocityOverLifetime; ov.enabled = true; ov.space = ParticleSystemSimulationSpace.Local;
                ov.orbitalY = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
                ov.x = ov.y = ov.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                var ln = l.main; ln.simulationSpace = ParticleSystemSimulationSpace.Local;
            });
        }

        private static void DustPuff(Transform root, Material dust, Color c, int count, float size = 1f)
        {
            var d = GameFeelSetup.Burst(root, "Dust", dust, count, new Vector2(0.6f, 0.95f), new Vector2(0.8f * size, 1.3f * size), new Vector2(0.5f, 1.4f), c, count + 4);
            GameFeelSetup.Sphere(d, 0.35f * size);
            GameFeelSetup.Grow(d, 0.6f, 1.7f);
            GameFeelSetup.Fade(d, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f });
        }

        private static void Clods(Transform root, Material mat, Mesh mesh, int count, float size)
        {
            var b = GameFeelSetup.Burst(root, "Clods", mat, count, new Vector2(0.8f, 1.1f), new Vector2(0.6f * size, 1.05f * size), new Vector2(2.5f, 4.5f), Color.white, count + 4);
            GameFeelSetup.MeshParticles(b, mesh);
            GameFeelSetup.Cone(b, 40f, 0.25f);
            GameFeelSetup.Gravity(b, 1.7f);
            GameFeelSetup.Spin(b);
            GameFeelSetup.Collide(b);
            GameFeelSetup.Fade(b, new[] { 1f, 1f, 0f }, new[] { 0f, 0.8f, 1f }, size: true);
        }

        private static void Splinters(Transform root, string name, Material mat, Mesh mesh, int count)
        {
            var b = GameFeelSetup.Burst(root, name, mat, count, new Vector2(1f, 1.5f), new Vector2(0.8f, 1.5f), new Vector2(3.5f, 7f), Color.white, count + 4);
            GameFeelSetup.MeshParticles(b, mesh);
            GameFeelSetup.Cone(b, 60f, 0.6f);
            GameFeelSetup.Gravity(b, 1.7f);
            GameFeelSetup.Spin(b);
            GameFeelSetup.Collide(b);
            GameFeelSetup.Fade(b, new[] { 1f, 1f, 0f }, new[] { 0f, 0.85f, 1f }, size: true);
        }

        private static void Leaves(Transform root, Material leaf, Color a, Color b, int count, float size)
        {
            var l = Flakes(root, "Leaves", leaf, a, b, count, new Vector2(1.4f, 2.1f), new Vector2(0.24f * size, 0.38f * size), new Vector2(2f, 4f), 0.35f, 1.1f);
            GameFeelSetup.Cone(l, 55f, 0.3f);
        }

        // Leichte Flocken (Blatt, Fell, Feder, Schuppe): drehende Billboards, zwei Farben zufällig, flattern (Noise) und
        // segeln langsam herab (geringe Schwerkraft + Luftwiderstand)
        private static ParticleSystem Flakes(Transform root, string name, Material mat, Color a, Color b, int count,
            Vector2 life, Vector2 size, Vector2 speed, float gravity, float drag)
        {
            var ps = GameFeelSetup.Burst(root, name, mat, count, life, size, speed, a, count + 4);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            var rot = ps.rotationOverLifetime; rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
            var n = ps.noise; n.enabled = true; n.strength = 0.9f; n.frequency = 0.9f; n.scrollSpeed = 0.6f;
            n.quality = ParticleSystemNoiseQuality.Medium; n.damping = true;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 30f; lim.drag = drag;
            GameFeelSetup.Fade(ps, new[] { 1f, 1f, 0f }, new[] { 0f, 0.75f, 1f });
            return ps;
        }
    }
}
