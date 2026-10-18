using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Spieltest 2026-10-18, Paket V (Punkte 2 und 6): Arkanball-Optik und Treffer-Effekte je Fähigkeit.
    //  1. Texturen kommen aus art-src/vfx/make_vfx_textures.py (Pillow, weiß + Alpha): Arcane/arcane_rune_ring.png,
    //     arcane_spark.png, Hits/hit_slash.png, hit_shard.png, hit_star.png, hit_leaf.png – hier nur Import-Einstellungen.
    //  2. Materialien: M_ArcaneCore (Shader BuddyTD/ArcaneCore: additiv, Fresnel-Hülle, pulsierend), Partikel-Materialien
    //     als Kopien der vorhandenen Vorlagen VFX_SoftDot_Add (additiv) bzw. VFX_LavaTrail (Alpha).
    //  3. Prefabs Assets/_Game/VFX/Prefabs/Arcane/VFX_ArcaneMuzzle, VFX_ArcaneBurst (für 1 m Radius – ArcaneBall skaliert
    //     mit Splitter-Radius × Kugelgröße) und Assets/_Game/VFX/Prefabs/Hits/VFX_Hit_<Familie> (Normalgegner, ~0,3–0,6 s).
    //  4. ArcaneBall.prefab: alte Optik („Visual“ mit Feuerball-Shader, VFX-Graph „FireballFlames“, Licht) raus, neues Kind
    //     „ArcaneFx“ (Kern, zwei gegenläufige Runenringe, Schein, Funken, Schweif, Licht), ImpactVfx/MuzzleVfx und
    //     ArcaneBallHum (arcane_loop) am Root. Der Necro-Orb des Bosses (BossBuilder.BuildNecroOrb) entfernt „ArcaneFx“ wieder.
    //  5. Tabelle Assets/Resources/HitVfxTable.asset (HitVfx lädt sie zur Laufzeit): Fähigkeit → Familie, Tönung, Größe.
    // Mehrfach ausführbar (Prefabs werden geleert und neu gefüllt, GUIDs bleiben). Menü: BuddyTD → Game Feel → Arkanball & Treffer-Effekte bauen
    public static class ArcaneVfxBuilder
    {
        private const string TexDir = "Assets/_Game/VFX/Textures/";
        private const string MatDir = "Assets/_Game/VFX/Materials/";
        private const string MeshDir = "Assets/_Game/VFX/Meshes/";
        public const string ArcaneDir = "Assets/_Game/VFX/Prefabs/Arcane/";
        public const string HitDir = "Assets/_Game/VFX/Prefabs/Hits/";
        public const string BallPath = "Assets/_Game/Prefabs/ArcaneBall.prefab";
        public const string MuzzlePath = ArcaneDir + "VFX_ArcaneMuzzle.prefab";
        public const string BurstPath = ArcaneDir + "VFX_ArcaneBurst.prefab";
        public const string TablePath = "Assets/Resources/" + HitVfx.ResourcePath + ".asset";
        private const string LoopClip = "Assets/_Game/Audio/Generated/arcane_loop.wav";

        public static string HitPath(HitVfxFamily f) => HitDir + "VFX_Hit_" + f + ".prefab";

        // Arkan-Farben
        private static readonly Color Violet = new Color(0.72f, 0.38f, 1f, 1f);
        private static readonly Color VioletWhite = new Color(0.92f, 0.82f, 1f, 1f);
        private static readonly Color ArcBlue = new Color(0.32f, 0.45f, 1f, 1f);

        [MenuItem("BuddyTD/Game Feel/Arkanball & Treffer-Effekte bauen")]
        public static void BuildAllMenu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            var sb = new StringBuilder("ArcaneVfxBuilder:");
            ImportTextures();
            BuildMaterials();
            Directory.CreateDirectory(ArcaneDir);
            Directory.CreateDirectory(HitDir);
            AssetDatabase.Refresh();
            BuildMuzzle();
            BuildBurst();
            BuildHits();
            AssetDatabase.SaveAssets();
            sb.Append("\n  ").Append(BuildBall());
            sb.Append("\n  ").Append(BuildTable());
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // ---------------- 1. Texturen ----------------

        private static readonly string[] Textures =
        {
            "Arcane/arcane_rune_ring.png", "Arcane/arcane_spark.png",
            "Hits/hit_slash.png", "Hits/hit_shard.png", "Hits/hit_star.png", "Hits/hit_leaf.png"
        };

        private static void ImportTextures()
        {
            AssetDatabase.Refresh();
            foreach (var f in Textures)
            {
                var imp = AssetImporter.GetAtPath(TexDir + f) as TextureImporter;
                if (imp == null) { Debug.LogWarning("ArcaneVfxBuilder: Textur fehlt – art-src/vfx/make_vfx_textures.py ausführen: " + f); continue; }
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
        }

        // ---------------- 2. Materialien ----------------

        private static Material _core, _rune, _runeDouble, _spark, _glow, _ring, _sparkLine, _slash, _shard, _star, _leaf, _dust, _flame, _trail;
        private static Material _ice, _rock, _wood, _feather;
        private static Mesh _iceMesh, _clod, _splinter, _sphere, _quad;

        private static void BuildMaterials()
        {
            _core = CoreMat();
            _rune = Particle("M_ArcaneRune_Add", true, "Arcane/arcane_rune_ring.png", false);
            _runeDouble = Particle("M_ArcaneRuneRing_Add", true, "Arcane/arcane_rune_ring.png", true); // Ringe am Ball: beidseitig
            _spark = Particle("M_ArcaneSpark_Add", true, "Arcane/arcane_spark.png", false);
            _slash = Particle("M_Hit_Slash_Add", true, "Hits/hit_slash.png", false);
            _shard = Particle("M_Hit_Shard_Alpha", false, "Hits/hit_shard.png", false);
            _star = Particle("M_Hit_Star_Add", true, "Hits/hit_star.png", false);
            _leaf = Particle("M_Hit_Leaf_Alpha", false, "Hits/hit_leaf.png", false);
            // vorhandene eigene Materialien (GameFeelSetup, ChampionFxBuilder, DeathVfxBuilder)
            _glow = Load<Material>(MatDir + "GF_SoftDot_Add.mat");
            _ring = Load<Material>(MatDir + "GF_Ring_Add.mat");
            _sparkLine = Load<Material>(MatDir + "GF_Spark_Add.mat");
            _dust = Load<Material>(MatDir + "GF_Dust_Alpha.mat");
            _flame = Load<Material>(MatDir + "VFX_Flame_Add.mat");
            _trail = Load<Material>(MatDir + "VFX_Trail_Add.mat");
            _ice = Load<Material>(MatDir + "VFX_Ice_Shard.mat");
            _rock = Load<Material>(MatDir + "Champ_Rock.mat");
            _wood = Load<Material>(MatDir + "GF_Wood.mat");
            _feather = Load<Material>(MatDir + "GF_Feather_Alpha.mat");
            _iceMesh = Load<Mesh>(MatDir + "IceShard.asset");
            _clod = Load<Mesh>(MeshDir + "GF_Clod.asset");
            _splinter = Load<Mesh>(MeshDir + "GF_WoodSplinter.asset");
            _sphere = BuiltinMesh(PrimitiveType.Sphere);
            _quad = BuiltinMesh(PrimitiveType.Quad);
        }

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) Debug.LogWarning("ArcaneVfxBuilder: fehlt " + path);
            return a;
        }

        private static Mesh BuiltinMesh(PrimitiveType type)
        {
            var tmp = GameObject.CreatePrimitive(type);
            var m = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
            return m;
        }

        private static Material CoreMat()
        {
            string p = MatDir + "M_ArcaneCore.mat";
            var shader = Shader.Find("BuddyTD/ArcaneCore");
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, p); }
            else if (m.shader != shader) m.shader = shader;
            m.SetColor("_CoreColor", new Color(1.7f, 1.45f, 2.3f, 1f));
            m.SetColor("_RimColor", new Color(0.8f, 0.28f, 2.6f, 1f));
            m.SetFloat("_RimPower", 2.2f);
            m.SetFloat("_CoreSize", 0.55f);
            m.SetFloat("_PulseSpeed", 7f);
            m.SetFloat("_PulseAmount", 0.3f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // Partikel-Material als Kopie einer vorhandenen Vorlage (additiv: VFX_SoftDot_Add, Alpha: VFX_LavaTrail)
        private static Material Particle(string name, bool additive, string tex, bool doubleSided)
        {
            string p = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                var tpl = AssetDatabase.LoadAssetAtPath<Material>(MatDir + (additive ? "VFX_SoftDot_Add" : "VFX_LavaTrail") + ".mat");
                m = tpl != null ? new Material(tpl) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, p);
            }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + tex);
            m.SetTexture("_BaseMap", t);
            m.mainTexture = t;
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- Partikel-Helfer (zusätzlich zu GameFeelSetup) ----------------

        private static ParticleSystem Burst(Transform root, string name, Material mat, int count, Vector2 life, Vector2 size, Vector2 speed, Color color)
            => GameFeelSetup.Burst(root, name, mat, count, life, size, speed, color, count + 4);

        // Farbverlauf über die Lebenszeit (zwei Farben, Alpha-Stützpunkte)
        private static void ColorLife(ParticleSystem ps, Color from, Color to, float[] alphas, float[] times)
        {
            var g = new Gradient();
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i], times[i]);
            g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) }, ak);
            var c = ps.colorOverLifetime; c.enabled = true; c.color = g;
        }

        private static void Stretch(ParticleSystem ps, float length, float velocityScale = 0.05f)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velocityScale;
            r.lengthScale = length;
        }

        private static void RandomRotation(ParticleSystem ps, float minDeg, float maxDeg)
        {
            var main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(minDeg * Mathf.Deg2Rad, maxDeg * Mathf.Deg2Rad);
        }

        private static void SpinZ(ParticleSystem ps, float degPerSecMin, float degPerSecMax)
        {
            var rot = ps.rotationOverLifetime; rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(degPerSecMin * Mathf.Deg2Rad, degPerSecMax * Mathf.Deg2Rad);
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 100f; lim.drag = drag;
        }

        private static void Flat(ParticleSystem ps, float y = 0f)
        {
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            if (!Mathf.Approximately(y, 0f)) ps.transform.localPosition = new Vector3(0f, y, 0f);
        }

        // Kurzer, heller Schein (additiver Weichpunkt)
        private static ParticleSystem Flash(Transform root, Color c, float size, float life)
        {
            var f = Burst(root, "Flash", _glow, 1, new Vector2(life, life), new Vector2(size, size), Vector2.zero, c);
            GameFeelSetup.Fade(f, new[] { 1f, 0f }, new[] { 0f, 1f });
            return f;
        }

        // Ausklingendes Punktlicht (LightFade) – nur bei großen Einschlägen
        private static void FlashLight(Transform root, Color c, float intensity, float range, float duration)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(root, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            go.AddComponent<LightFade>().Duration = duration;
        }

        // ---------------- 3a. Arkan: Wirk-Blitz und Einschlag ----------------

        // Wirk-Blitz an der Hand: Schein, Runenring (zur Kamera), wenige Funken nach vorn – ~0,3 s
        private static void BuildMuzzle()
        {
            GameFeelSetup.Build(MuzzlePath, root =>
            {
                Flash(root, new Color(0.85f, 0.6f, 1f, 0.95f), 1.1f, 0.14f);
                var r = Burst(root, "Runes", _rune, 1, new Vector2(0.28f, 0.28f), new Vector2(0.9f, 0.9f), Vector2.zero, VioletWhite);
                GameFeelSetup.Grow(r, 0.35f, 1f);
                RandomRotation(r, 0f, 360f);
                SpinZ(r, 300f, 300f);
                GameFeelSetup.Fade(r, new[] { 0f, 1f, 0f }, new[] { 0f, 0.2f, 1f });
                var s = Burst(root, "Sparks", _spark, 7, new Vector2(0.18f, 0.3f), new Vector2(0.08f, 0.16f), new Vector2(2.5f, 5f), VioletWhite);
                var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.05f; // nach vorn (+Z)
                Drag(s, 4f);
                ColorLife(s, Color.white, ArcBlue, new[] { 1f, 0f }, new[] { 0f, 1f });
            });
        }

        // Einschlag (1 m Radius): Schockwellen-Ring, Funkenstoß, Lichtblitz + Licht, Runenkreis am Boden – ~0,7 s
        private static void BuildBurst()
        {
            GameFeelSetup.Build(BurstPath, root =>
            {
                Flash(root, new Color(0.9f, 0.75f, 1f, 1f), 2.4f, 0.16f);
                FlashLight(root, Violet, 4f, 5f, 0.35f);

                var w = Burst(root, "Shockwave", _ring, 1, new Vector2(0.32f, 0.32f), new Vector2(2.2f, 2.2f), Vector2.zero, new Color(0.8f, 0.55f, 1f, 1f));
                Flat(w);
                GameFeelSetup.Grow(w, 0.15f, 1f);
                GameFeelSetup.Fade(w, new[] { 1f, 0.7f, 0f }, new[] { 0f, 0.4f, 1f });

                var s = Burst(root, "Sparks", _spark, 18, new Vector2(0.28f, 0.5f), new Vector2(0.1f, 0.2f), new Vector2(3f, 6.5f), Color.white);
                GameFeelSetup.Sphere(s, 0.15f);
                Drag(s, 3f);
                GameFeelSetup.Gravity(s, 0.3f);
                ColorLife(s, VioletWhite, ArcBlue, new[] { 1f, 1f, 0f }, new[] { 0f, 0.5f, 1f });

                var l = Burst(root, "Streaks", _sparkLine, 10, new Vector2(0.12f, 0.2f), new Vector2(0.14f, 0.22f), new Vector2(6f, 10f), new Color(0.8f, 0.6f, 1f, 1f));
                GameFeelSetup.Sphere(l, 0.1f);
                Stretch(l, 2.2f);
                GameFeelSetup.Fade(l, new[] { 1f, 0f }, new[] { 0f, 1f }, size: true);

                // Runenkreis am Boden (Einschlag sitzt ~1 m über dem Boden, Kreis knapp darunter; dreht langsam, verblasst)
                var c = Burst(root, "RuneCircle", _rune, 1, new Vector2(0.7f, 0.7f), new Vector2(2f, 2f), Vector2.zero, new Color(0.75f, 0.45f, 1f, 0.9f));
                Flat(c, -0.6f);
                var cm = c.main; cm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                SpinZ(c, 90f, 90f);
                GameFeelSetup.Grow(c, 0.6f, 1f);
                GameFeelSetup.Fade(c, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f });
            });
        }

        // ---------------- 3b. Treffer-Familien (Normalgegner, Mitte Körper) ----------------

        private static void BuildHits()
        {
            // Arkan: kleiner Einschlag (Splitter-Treffer des Arkanballs)
            GameFeelSetup.Build(HitPath(HitVfxFamily.Arcane), root =>
            {
                Flash(root, new Color(0.85f, 0.6f, 1f, 0.9f), 1.2f, 0.12f);
                var r = Burst(root, "Runes", _rune, 1, new Vector2(0.32f, 0.32f), new Vector2(1.1f, 1.1f), Vector2.zero, VioletWhite);
                GameFeelSetup.Grow(r, 0.4f, 1f);
                RandomRotation(r, 0f, 360f);
                SpinZ(r, 240f, 240f);
                GameFeelSetup.Fade(r, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f });
                var s = Burst(root, "Sparks", _spark, 10, new Vector2(0.22f, 0.4f), new Vector2(0.1f, 0.18f), new Vector2(2.5f, 5f), Color.white);
                GameFeelSetup.Sphere(s, 0.15f);
                Drag(s, 3f);
                ColorLife(s, VioletWhite, ArcBlue, new[] { 1f, 0f }, new[] { 0f, 1f });
            });

            // Klinge: heller Schnitt-Streifen quer über den Gegner + Funken
            GameFeelSetup.Build(HitPath(HitVfxFamily.Blade), root =>
            {
                var c = Burst(root, "Slash", _slash, 1, new Vector2(0.2f, 0.2f), new Vector2(1.8f, 1.8f), Vector2.zero, new Color(0.9f, 0.96f, 1f, 1f));
                RandomRotation(c, -40f, 40f);
                GameFeelSetup.Grow(c, 0.75f, 1.1f);
                GameFeelSetup.Fade(c, new[] { 1f, 1f, 0f }, new[] { 0f, 0.35f, 1f });
                var s = Burst(root, "Sparks", _sparkLine, 9, new Vector2(0.15f, 0.28f), new Vector2(0.12f, 0.2f), new Vector2(4f, 8f), new Color(1f, 0.92f, 0.65f, 1f));
                GameFeelSetup.Sphere(s, 0.1f);
                GameFeelSetup.Gravity(s, 1.2f);
                Stretch(s, 2f);
                GameFeelSetup.Fade(s, new[] { 1f, 0f }, new[] { 0f, 1f }, size: true);
                Flash(root, new Color(1f, 0.97f, 0.9f, 0.8f), 0.9f, 0.08f);
            });

            // Wucht: zwei breite rote Schnitte (über Kreuz) + Staubstoß
            GameFeelSetup.Build(HitPath(HitVfxFamily.Heavy), root =>
            {
                var c = Burst(root, "Slash", _slash, 1, new Vector2(0.24f, 0.24f), new Vector2(2.3f, 2.3f), Vector2.zero, new Color(1f, 0.38f, 0.26f, 1f));
                RandomRotation(c, -25f, 25f);
                GameFeelSetup.Grow(c, 0.7f, 1.15f);
                GameFeelSetup.Fade(c, new[] { 1f, 1f, 0f }, new[] { 0f, 0.3f, 1f });
                var c2 = Burst(root, "Slash2", _slash, 1, new Vector2(0.2f, 0.2f), new Vector2(1.6f, 1.6f), Vector2.zero, new Color(1f, 0.7f, 0.45f, 1f));
                RandomRotation(c2, 120f, 160f);
                GameFeelSetup.Grow(c2, 0.7f, 1.1f);
                GameFeelSetup.Fade(c2, new[] { 1f, 1f, 0f }, new[] { 0f, 0.3f, 1f });
                var d = Burst(root, "Dust", _dust, 5, new Vector2(0.4f, 0.6f), new Vector2(0.7f, 1.1f), new Vector2(0.6f, 1.5f), new Color(0.55f, 0.46f, 0.36f, 0.55f));
                GameFeelSetup.Sphere(d, 0.3f);
                d.transform.localPosition = new Vector3(0f, -0.35f, 0f);
                GameFeelSetup.Grow(d, 0.6f, 1.5f);
                GameFeelSetup.Fade(d, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f });
                var s = Burst(root, "Sparks", _sparkLine, 7, new Vector2(0.15f, 0.25f), new Vector2(0.14f, 0.22f), new Vector2(4f, 7f), new Color(1f, 0.55f, 0.3f, 1f));
                GameFeelSetup.Sphere(s, 0.1f);
                GameFeelSetup.Gravity(s, 1.5f);
                Stretch(s, 2f);
                GameFeelSetup.Fade(s, new[] { 1f, 0f }, new[] { 0f, 1f }, size: true);
            });

            // Stich: Holzsplitter (in Schussrichtung) + Federflocken + kleiner Blitz
            GameFeelSetup.Build(HitPath(HitVfxFamily.Pierce), root =>
            {
                var w = Burst(root, "Splinters", _wood, 7, new Vector2(0.35f, 0.55f), new Vector2(0.55f, 0.85f), new Vector2(2f, 4.5f), Color.white);
                GameFeelSetup.MeshParticles(w, _splinter);
                var sh = w.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 45f; sh.radius = 0.08f;
                GameFeelSetup.Gravity(w, 1.4f);
                GameFeelSetup.Spin(w);
                GameFeelSetup.Fade(w, new[] { 1f, 1f, 0f }, new[] { 0f, 0.75f, 1f }, size: true);
                var f = Burst(root, "Feathers", _feather, 6, new Vector2(0.45f, 0.6f), new Vector2(0.32f, 0.46f), new Vector2(1f, 2.4f), new Color(0.95f, 0.92f, 0.85f, 1f));
                GameFeelSetup.Sphere(f, 0.15f);
                RandomRotation(f, 0f, 360f);
                SpinZ(f, -300f, 300f);
                GameFeelSetup.Gravity(f, 0.25f);
                Drag(f, 2.5f);
                GameFeelSetup.Fade(f, new[] { 1f, 1f, 0f }, new[] { 0f, 0.7f, 1f });
                Flash(root, new Color(1f, 0.95f, 0.85f, 0.8f), 1f, 0.08f);
            });

            // Feuer: Glutstoß, Flammenzungen, aufsteigende Funken
            GameFeelSetup.Build(HitPath(HitVfxFamily.Fire), root =>
            {
                Flash(root, new Color(1f, 0.6f, 0.2f, 0.9f), 1.4f, 0.14f);
                var t = Burst(root, "Flames", _flame, 6, new Vector2(0.3f, 0.45f), new Vector2(0.45f, 0.75f), new Vector2(0.4f, 1.2f), new Color(1f, 0.6f, 0.2f, 1f));
                GameFeelSetup.Sphere(t, 0.25f);
                GameFeelSetup.Rise(t, 2.2f);
                RandomRotation(t, -20f, 20f);
                GameFeelSetup.Fade(t, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f }, size: true);
                var e = Burst(root, "Embers", _sparkLine, 10, new Vector2(0.3f, 0.55f), new Vector2(0.08f, 0.14f), new Vector2(2f, 4.5f), new Color(1f, 0.75f, 0.3f, 1f));
                GameFeelSetup.Sphere(e, 0.2f);
                GameFeelSetup.Gravity(e, -0.4f);
                Drag(e, 2f);
                Stretch(e, 1.6f);
                GameFeelSetup.Fade(e, new[] { 1f, 1f, 0f }, new[] { 0f, 0.6f, 1f });
            });

            // Eis: Eissplitter-Stoß (Mesh + Splitter-Sprites), Frosthauch
            GameFeelSetup.Build(HitPath(HitVfxFamily.Ice), root =>
            {
                Flash(root, new Color(0.65f, 0.9f, 1f, 0.85f), 1.2f, 0.12f);
                var m = Burst(root, "Shards", _ice, 5, new Vector2(0.35f, 0.5f), new Vector2(0.18f, 0.3f), new Vector2(2.5f, 4.5f), Color.white);
                GameFeelSetup.MeshParticles(m, _iceMesh);
                GameFeelSetup.Sphere(m, 0.15f);
                GameFeelSetup.Gravity(m, 1.3f);
                GameFeelSetup.Spin(m);
                GameFeelSetup.Fade(m, new[] { 1f, 1f, 0f }, new[] { 0f, 0.7f, 1f }, size: true);
                var s = Burst(root, "Splinters", _shard, 9, new Vector2(0.2f, 0.35f), new Vector2(0.18f, 0.3f), new Vector2(3.5f, 6.5f), new Color(0.8f, 0.95f, 1f, 1f));
                GameFeelSetup.Sphere(s, 0.1f);
                Stretch(s, 1.3f, 0.02f);
                Drag(s, 3f);
                GameFeelSetup.Fade(s, new[] { 1f, 1f, 0f }, new[] { 0f, 0.6f, 1f });
                var b = Burst(root, "Frost", _dust, 4, new Vector2(0.45f, 0.6f), new Vector2(0.7f, 1f), new Vector2(0.3f, 0.9f), new Color(0.8f, 0.93f, 1f, 0.5f));
                GameFeelSetup.Sphere(b, 0.25f);
                GameFeelSetup.Grow(b, 0.6f, 1.4f);
                GameFeelSetup.Fade(b, new[] { 0f, 1f, 0f }, new[] { 0f, 0.2f, 1f });
            });

            // Erde: Steinbrocken (Champ_Rock auf GF_Clod) + Staub
            GameFeelSetup.Build(HitPath(HitVfxFamily.Earth), root =>
            {
                var r = Burst(root, "Rocks", _rock, 8, new Vector2(0.45f, 0.6f), new Vector2(1.1f, 1.7f), new Vector2(2.5f, 4.5f), Color.white);
                GameFeelSetup.MeshParticles(r, _clod);
                GameFeelSetup.Cone(r, 40f, 0.2f);
                GameFeelSetup.Gravity(r, 1.8f);
                GameFeelSetup.Spin(r);
                GameFeelSetup.Fade(r, new[] { 1f, 1f, 0f }, new[] { 0f, 0.8f, 1f }, size: true);
                var d = Burst(root, "Dust", _dust, 7, new Vector2(0.45f, 0.6f), new Vector2(1f, 1.5f), new Vector2(0.6f, 1.8f), new Color(0.52f, 0.42f, 0.3f, 0.7f));
                GameFeelSetup.Sphere(d, 0.3f);
                GameFeelSetup.Grow(d, 0.6f, 1.6f);
                GameFeelSetup.Fade(d, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f });
            });

            // Licht: goldene Sternfunken + Lichtkreuz
            GameFeelSetup.Build(HitPath(HitVfxFamily.Light), root =>
            {
                Flash(root, new Color(1f, 0.88f, 0.5f, 0.9f), 1.3f, 0.14f);
                var x = Burst(root, "Cross", _star, 1, new Vector2(0.35f, 0.35f), new Vector2(1.9f, 1.9f), Vector2.zero, new Color(1f, 0.92f, 0.6f, 1f));
                GameFeelSetup.Grow(x, 0.4f, 1f);
                GameFeelSetup.Fade(x, new[] { 1f, 1f, 0f }, new[] { 0f, 0.3f, 1f });
                var s = Burst(root, "Stars", _star, 9, new Vector2(0.3f, 0.5f), new Vector2(0.3f, 0.5f), new Vector2(1.5f, 3.4f), new Color(1f, 0.85f, 0.45f, 1f));
                GameFeelSetup.Sphere(s, 0.2f);
                RandomRotation(s, 0f, 45f);
                SpinZ(s, -180f, 180f);
                Drag(s, 2.5f);
                GameFeelSetup.Fade(s, new[] { 1f, 1f, 0f }, new[] { 0f, 0.5f, 1f }, size: true);
            });

            // Natur: Blatt- und Dornensplitter
            GameFeelSetup.Build(HitPath(HitVfxFamily.Nature), root =>
            {
                var l = Burst(root, "Leaves", _leaf, 8, new Vector2(0.4f, 0.6f), new Vector2(0.24f, 0.36f), new Vector2(1.5f, 3.5f), new Color(0.4f, 0.65f, 0.22f, 1f));
                var lm = l.main; lm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 0.65f, 0.22f, 1f), new Color(0.65f, 0.75f, 0.25f, 1f));
                GameFeelSetup.Sphere(l, 0.2f);
                RandomRotation(l, 0f, 360f);
                SpinZ(l, -400f, 400f);
                GameFeelSetup.Gravity(l, 0.5f);
                Drag(l, 2.5f);
                GameFeelSetup.Fade(l, new[] { 1f, 1f, 0f }, new[] { 0f, 0.7f, 1f });
                var t = Burst(root, "Thorns", _shard, 7, new Vector2(0.2f, 0.32f), new Vector2(0.16f, 0.26f), new Vector2(3f, 5.5f), new Color(0.42f, 0.32f, 0.18f, 1f));
                GameFeelSetup.Sphere(t, 0.1f);
                Stretch(t, 1.3f, 0.02f);
                Drag(t, 3f);
                GameFeelSetup.Fade(t, new[] { 1f, 1f, 0f }, new[] { 0f, 0.6f, 1f });
                Flash(root, new Color(0.65f, 1f, 0.45f, 0.6f), 0.9f, 0.1f);
            });
        }

        // ---------------- 4. ArcaneBall.prefab ----------------

        public static string BuildBall()
        {
            var root = PrefabUtility.LoadPrefabContents(BallPath);
            try
            {
                // alte Optik: Kugel-Mesh mit Feuerball-Shader + VFX-Graph „FireballFlames“ + Licht (alles unter „Visual“)
                var old = root.transform.Find("Visual");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var prev = root.transform.Find("ArcaneFx");
                if (prev != null) Object.DestroyImmediate(prev.gameObject);

                // Kind in Metern: der Root ist auf 0,42 skaliert (Kollider), Händlerkarten skalieren den Root weiter
                var fx = new GameObject("ArcaneFx").transform;
                fx.SetParent(root.transform, false);
                fx.localScale = Vector3.one / Mathf.Max(0.01f, root.transform.localScale.x);

                // Kern: violett-weiße Fresnel-Kugel (pulsiert im Shader)
                var core = new GameObject("Core");
                core.transform.SetParent(fx, false);
                core.transform.localScale = Vector3.one * 0.4f;
                core.AddComponent<MeshFilter>().sharedMesh = _sphere;
                var cr = core.AddComponent<MeshRenderer>();
                cr.sharedMaterial = _core;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cr.receiveShadows = false;

                // Schein um den Kern (lokal, flackert leicht)
                var halo = Loop(fx, "Halo", _glow, 12f, new Vector2(0.22f, 0.3f), new Vector2(1.2f, 1.4f), new Color(0.7f, 0.4f, 1f, 0.6f), 6, true);
                GameFeelSetup.Grow(halo, 0.85f, 1f);
                GameFeelSetup.Fade(halo, new[] { 0f, 1f, 0f }, new[] { 0f, 0.4f, 1f });

                // zwei Runenringe, schräg gestellt, gegenläufig
                Ring(fx, "RuneRingA", new Vector3(62f, 0f, 20f), 0.95f, 220f, new Color(0.85f, 0.65f, 1f, 1f));
                Ring(fx, "RuneRingB", new Vector3(62f, 90f, -20f), 0.8f, -300f, new Color(0.55f, 0.6f, 1f, 1f));

                // Funken lösen sich (Weltraum), Violett → Blau
                var sp = Loop(fx, "Sparks", _spark, 34f, new Vector2(0.3f, 0.5f), new Vector2(0.07f, 0.13f), Color.white, 30, false);
                GameFeelSetup.Sphere(sp, 0.16f);
                var spm = sp.main; spm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
                Drag(sp, 1.5f);
                ColorLife(sp, VioletWhite, ArcBlue, new[] { 1f, 1f, 0f }, new[] { 0f, 0.4f, 1f });
                var sz = sp.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

                // Schweif
                var tg = new GameObject("Trail");
                tg.transform.SetParent(fx, false);
                var tr = tg.AddComponent<TrailRenderer>();
                tr.sharedMaterial = _trail;
                tr.time = 0.22f; tr.minVertexDistance = 0.08f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                tr.widthMultiplier = 0.38f;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.6f, 1f), 0f), new GradientColorKey(Violet, 0.35f), new GradientColorKey(ArcBlue, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;

                // Punktlicht (wie bisher, kräftiger Violett-Ton, kleine Reichweite – mehrere Kugeln gleichzeitig)
                var lg = new GameObject("Point Light");
                lg.transform.SetParent(fx, false);
                var light = lg.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(0.62f, 0.34f, 1f); light.intensity = 2.2f; light.range = 4f;
                light.shadows = LightShadows.None;

                var ball = root.GetComponent<ArcaneBall>();
                ball.ImpactVfx = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPath);
                ball.MuzzleVfx = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzlePath);
                var hum = root.GetComponent<ArcaneBallHum>();
                if (hum == null) hum = root.AddComponent<ArcaneBallHum>();
                hum.Clip = AssetDatabase.LoadAssetAtPath<AudioClip>(LoopClip);

                PrefabUtility.SaveAsPrefabAsset(root, BallPath);
                return "ArcaneBall.prefab: ArcaneFx gebaut, ImpactVfx/MuzzleVfx/ArcaneBallHum gesetzt (Clip " + (hum.Clip != null ? hum.Clip.name : "FEHLT") + ")";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Dauer-Emitter (Schleife) unter dem Ball
        private static ParticleSystem Loop(Transform parent, string name, Material mat, float rate, Vector2 life, Vector2 size, Color color, int max, bool local)
        {
            var ps = GameFeelSetup.Burst(parent, name, mat, 0, life, size, Vector2.zero, color, max);
            var main = ps.main;
            main.loop = true; main.duration = 1f; main.prewarm = false;
            main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.SetBursts(new ParticleSystem.Burst[0]);
            em.rateOverTime = rate;
            return ps;
        }

        // Runenring: beidseitiges Quad mit Runen-Textur, dreht um die eigene Normale (VfxSpin)
        private static void Ring(Transform parent, string name, Vector3 euler, float size, float degPerSec, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RingMat(name, color); // eigenes Material je Ring (Farbe), Kopie von M_ArcaneRuneRing_Add
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var spin = go.AddComponent<VfxSpin>();
            spin.Axis = Vector3.forward;
            spin.DegreesPerSecond = degPerSec;
        }

        private static Material RingMat(string name, Color color)
        {
            string p = MatDir + "M_Arcane" + name + "_Add.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(_runeDouble); AssetDatabase.CreateAsset(m, p); }
            else m.CopyPropertiesFromMaterial(_runeDouble);
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- 5. Tabelle ----------------

        public static string BuildTable()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TablePath));
            var t = AssetDatabase.LoadAssetAtPath<HitVfxTableSO>(TablePath);
            if (t == null)
            {
                t = ScriptableObject.CreateInstance<HitVfxTableSO>();
                AssetDatabase.CreateAsset(t, TablePath);
            }
            t.Entries.Clear();
            var white = Color.white;
            // Magier
            Add(t, AbilityId.ArcaneBall, HitVfxFamily.Arcane, white, 0.9f);
            Add(t, AbilityId.FireWave, HitVfxFamily.Fire, white, 1.1f);
            Add(t, AbilityId.FrostNova, HitVfxFamily.Ice, white, 1.1f);
            // Schwertkämpfer
            Add(t, AbilityId.SwordSlash, HitVfxFamily.Blade, white, 1f);
            Add(t, AbilityId.FlameWhirl, HitVfxFamily.Fire, new Color(1f, 0.78f, 0.6f), 1.05f);
            Add(t, AbilityId.FrostStrike, HitVfxFamily.Ice, white, 1.2f);
            Add(t, AbilityId.Earthquake, HitVfxFamily.Earth, white, 1.1f, height: 0.25f);
            // Bogenschütze
            Add(t, AbilityId.ArrowShot, HitVfxFamily.Pierce, white, 1f, height: 0.45f);
            Add(t, AbilityId.FireArrowRain, HitVfxFamily.Fire, new Color(1f, 0.85f, 0.7f), 0.8f, minInterval: 0.45f);
            Add(t, AbilityId.FrostArrow, HitVfxFamily.Ice, new Color(0.85f, 0.95f, 1f), 0.95f, height: 0.45f);
            Add(t, AbilityId.ThornTrap, HitVfxFamily.Nature, white, 1.1f, height: 0.25f);
            Add(t, AbilityId.LightArrow, HitVfxFamily.Light, white, 1.1f, height: 0.45f);
            // Berserker
            Add(t, AbilityId.AxeCleave, HitVfxFamily.Heavy, white, 1f);
            Add(t, AbilityId.BerserkCharge, HitVfxFamily.Heavy, new Color(1f, 0.85f, 0.7f), 1.2f);
            Add(t, AbilityId.EmberStorm, HitVfxFamily.Fire, new Color(1f, 0.7f, 0.5f), 0.85f, minInterval: 0.6f);
            Add(t, AbilityId.IceBreaker, HitVfxFamily.Ice, white, 1.2f);
            Add(t, AbilityId.Landslide, HitVfxFamily.Earth, white, 1.25f, height: 0.25f);
            EditorUtility.SetDirty(t);
            return "HitVfxTable: " + t.Entries.Count + " Fähigkeiten";
        }

        private static void Add(HitVfxTableSO t, AbilityId id, HitVfxFamily family, Color tint, float scale, float minInterval = 0.2f, float height = 0.4f)
        {
            t.Entries.Add(new HitVfxTableSO.Entry
            {
                Ability = id,
                Family = family,
                Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HitPath(family)),
                Tint = tint,
                Scale = scale,
                MinInterval = minInterval,
                Height = height
            });
        }
    }
}
