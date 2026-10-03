using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Baut die Element-Effekte („ElementFX") um die Fusions-Elementare herum neu auf.
    // Menü: BuddyTD → Element-Auren neu bauen. Ersetzt jeweils das Kind „ElementFX" im Prefab.
    public static class ElementAuraBuilder
    {
        private const string VfxMat = "Assets/_Game/VFX/Materials/";
        private const string VfxTex = "Assets/_Game/VFX/Textures/";

        [MenuItem("BuddyTD/Element-Auren neu bauen")]
        public static void BuildAll()
        {
            Build("Fusion_Wasser", Water);
            Build("Fusion_Blitz", Lightning);
            Build("Fusion_Luft", Air);
            Build("Fusion_Schatten", Shadow);
            Build("Fusion_Magma", Magma);
            Build("Fusion_Kristall", Crystal);
            AssetDatabase.SaveAssets();
            Debug.Log("ElementAuraBuilder: Element-Auren für 6 Fusions-Prefabs neu gebaut.");
        }

        private static void Build(string prefab, System.Action<Transform> fill)
        {
            string path = "Assets/_Game/Prefabs/" + prefab + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            var old = root.transform.Find("ElementFX");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var fx = new GameObject("ElementFX").transform;
            fx.SetParent(root.transform, false);
            fill(fx);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------------- Materialien ----------------

        private static Material Mat(string name, string template, string texture)
        {
            string p = VfxMat + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(AssetDatabase.LoadAssetAtPath<Material>(VfxMat + template + ".mat"));
                AssetDatabase.CreateAsset(m, p);
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(VfxTex + texture);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Additiv (Vorlage VFX_SoftDot_Add) bzw. Alpha-Blend (Vorlage VFX_LavaTrail)
        private static Material Add(string name, string tex) { return Mat(name, "VFX_SoftDot_Add", tex); }
        private static Material Alpha(string name, string tex) { return Mat(name, "VFX_LavaTrail", tex); }

        private static Material SoftAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_SoftDot_Add.mat"); } }
        private static Material RingAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Ring_Add.mat"); } }
        private static Material SparkAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Spark_Add.mat"); } }
        private static Material StarAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Star_Add.mat"); } }
        private static Material TrailAdd { get { return Add("VFX_Trail_Add", "vfx_trail.png"); } }
        private static Material TrailAlpha { get { return Alpha("VFX_Trail_Alpha", "vfx_trail.png"); } }
        private static Material SmokeAlpha { get { return Alpha("VFX_Smoke_Alpha", "vfx_smoke.png"); } }
        private static Material LeafAlpha { get { return Alpha("VFX_Leaf_Alpha", "vfx_leaf.png"); } }
        private static Material SoftAlpha { get { return Alpha("VFX_SoftDot_Alpha", "vfx_soft_dot.png"); } }

        // ---------------- Partikel-Helfer ----------------

        private static ParticleSystem PS(Transform parent, string name, Vector3 pos, Material mat, float rate,
            Vector2 life, Vector2 size, Color color, int max = 60)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.prewarm = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = max;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static Gradient Fade(Color a, Color b, float peakAlpha = 1f, float inAt = 0.15f, float outAt = 0.7f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, inAt), new GradientAlphaKey(peakAlpha, outAt), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        private static void ColorLife(ParticleSystem ps, Gradient g)
        {
            var c = ps.colorOverLifetime; c.enabled = true; c.color = g;
        }

        private static void SizeLife(ParticleSystem ps, float a, float b)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, a, 1f, b));
        }

        private static void SizeBump(ParticleSystem ps)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
        }

        // Kreis in der Bodenebene (XZ)
        private static void Ring(ParticleSystem ps, float radius, float thickness = 0f, float tiltX = 0f, float tiltZ = 0f)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = radius; sh.radiusThickness = thickness;
            sh.rotation = new Vector3(-90f + tiltX, 0f, tiltZ);
        }

        private static void Sphere(ParticleSystem ps, float radius, Vector3 scale, float thickness = 1f)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius; sh.radiusThickness = thickness; sh.scale = scale;
        }

        private static void Orbit(ParticleSystem ps, float orbitalY, float up, float radial = 0f)
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.space = ParticleSystemSimulationSpace.Local;
            v.x = 0f; v.z = 0f; v.y = up;
            v.orbitalX = 0f; v.orbitalZ = 0f; v.orbitalY = orbitalY;
            v.radial = radial;
        }

        private static void Rise(ParticleSystem ps, float min, float max)
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.space = ParticleSystemSimulationSpace.Local;
            v.x = new ParticleSystem.MinMaxCurve(0f, 0f); v.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            v.y = new ParticleSystem.MinMaxCurve(min, max);
        }

        private static void Noise(ParticleSystem ps, float strength, float freq = 0.6f)
        {
            var n = ps.noise; n.enabled = true; n.strength = strength; n.frequency = freq; n.scrollSpeed = 0.3f;
        }

        private static void Trails(ParticleSystem ps, Material mat, float lifetime, float width, Color c)
        {
            var t = ps.trails; t.enabled = true;
            t.mode = ParticleSystemTrailMode.PerParticle;
            t.ratio = 1f; t.lifetime = lifetime; t.minVertexDistance = 0.04f;
            t.dieWithParticles = true; t.inheritParticleColor = true; t.sizeAffectsWidth = false;
            t.widthOverTrail = new ParticleSystem.MinMaxCurve(width, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            t.colorOverTrail = g;
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = mat;
        }

        private static void Flat(ParticleSystem ps)
        {
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        private static void Spin(ParticleSystem ps, float degPerSec)
        {
            var m = ps.main; m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var r = ps.rotationOverLifetime; r.enabled = true;
            r.z = new ParticleSystem.MinMaxCurve(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }

        // ---------------- Elemente ----------------

        // Wasser: zwei kreisende Wasserbänder, Spritzer, Wellenringe am Boden
        private static void Water(Transform fx)
        {
            var light = new Color(0.45f, 0.85f, 1f);
            var o1 = PS(fx, "WaterOrbitLow", new Vector3(0, 0.75f, 0), SoftAdd, 9f, new Vector2(1.6f, 1.6f), new Vector2(0.14f, 0.18f), light);
            Ring(o1, 0.95f, 0f, 0f, 8f); Orbit(o1, 3.4f, 0.12f);
            Trails(o1, TrailAdd, 0.45f, 0.16f, new Color(0.2f, 0.65f, 1f));
            ColorLife(o1, Fade(Color.white, Color.white));
            var o2 = PS(fx, "WaterOrbitHigh", new Vector3(0, 1.55f, 0), SoftAdd, 7f, new Vector2(1.4f, 1.4f), new Vector2(0.1f, 0.14f), light);
            Ring(o2, 0.85f, 0f, 14f, -10f); Orbit(o2, -3.0f, -0.08f);
            Trails(o2, TrailAdd, 0.4f, 0.12f, new Color(0.25f, 0.75f, 1f));
            ColorLife(o2, Fade(Color.white, Color.white));
            var drops = PS(fx, "Droplets", new Vector3(0, 1.0f, 0), SoftAdd, 7f, new Vector2(0.6f, 0.9f), new Vector2(0.05f, 0.09f), light);
            Ring(drops, 0.95f); var m = drops.main; m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f); m.gravityModifier = 0.9f;
            Rise(drops, 1.2f, 2.0f);
            var rip = PS(fx, "Ripples", new Vector3(0, 0.27f, 0), RingAdd, 1.1f, new Vector2(1.8f, 1.8f), new Vector2(1f, 1f), new Color(0.3f, 0.75f, 1f, 0.7f), 6);
            Flat(rip); SizeLife(rip, 0.6f, 2.6f); ColorLife(rip, Fade(Color.white, Color.white, 1f, 0.1f, 0.4f));
        }

        // Blitz: zuckende Lichtbögen (ElectricArcs), Funkenstöße, knisternde Ladung
        private static void Lightning(Transform fx)
        {
            var arcs = new GameObject("Arcs"); arcs.transform.SetParent(fx, false);
            var ea = arcs.AddComponent<ElectricArcs>();
            ea.ArcMaterial = TrailAdd;
            ea.ArcColor = new Color(1f, 0.92f, 0.5f);
            ea.ArcCount = 5; ea.Width = 0.055f;
            ea.HullCenter = new Vector3(0f, 1.3f, 0f);
            ea.HullRadius = new Vector3(0.8f, 0.95f, 0.65f);
            var glow = fx.parent.Find("Glow");
            if (glow != null) ea.FlickerLight = glow.GetComponent<Light>();
            var sp = PS(fx, "Sparks", new Vector3(0, 1.3f, 0), SparkAdd, 0f, new Vector2(0.15f, 0.3f), new Vector2(0.05f, 0.09f), new Color(1f, 0.9f, 0.45f), 40);
            Sphere(sp, 0.75f, new Vector3(1f, 1.3f, 0.9f), 0.2f);
            var m = sp.main; m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f); m.gravityModifier = 0.6f;
            var em = sp.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 3, 6, 20, 0.3f) { probability = 0.65f } });
            var r = sp.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 3f; r.velocityScale = 0.05f;
            var st = PS(fx, "Static", new Vector3(0, 1.3f, 0), SoftAdd, 10f, new Vector2(0.6f, 1.0f), new Vector2(0.05f, 0.1f), new Color(0.55f, 0.45f, 1f));
            Sphere(st, 0.8f, new Vector3(1f, 1.3f, 0.9f), 0.15f); Noise(st, 0.8f, 2f);
            ColorLife(st, Fade(Color.white, Color.white));
        }

        // Luft: spiralförmig aufsteigende Windschlieren + wirbelnde Blätter
        private static void Air(Transform fx)
        {
            var w1 = PS(fx, "WindStreaks", new Vector3(0, 0.35f, 0), SoftAlpha, 6f, new Vector2(1.6f, 1.6f), new Vector2(0.02f, 0.03f), new Color(1f, 1f, 1f, 0.8f));
            Ring(w1, 1.0f); Orbit(w1, 4.2f, 1.1f);
            Trails(w1, TrailAlpha, 0.5f, 0.07f, new Color(0.92f, 1f, 0.97f));
            ColorLife(w1, Fade(Color.white, Color.white, 0.75f));
            var w2 = PS(fx, "WindStreaksWide", new Vector3(0, 0.6f, 0), SoftAlpha, 4f, new Vector2(1.8f, 1.8f), new Vector2(0.02f, 0.03f), new Color(0.8f, 1f, 0.9f, 0.6f));
            Ring(w2, 1.3f, 0f, 6f, 0f); Orbit(w2, 3.3f, 0.7f);
            Trails(w2, TrailAlpha, 0.45f, 0.05f, new Color(0.75f, 1f, 0.88f));
            ColorLife(w2, Fade(Color.white, Color.white, 0.6f));
            var lv = PS(fx, "Leaves", new Vector3(0, 0.3f, 0), LeafAlpha, 4.5f, new Vector2(2.2f, 2.8f), new Vector2(0.12f, 0.2f), Color.white, 30);
            Ring(lv, 0.95f, 0.3f); Orbit(lv, 2.8f, 0.75f); Noise(lv, 0.35f, 0.8f);
            var main = lv.main;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.35f, 0.75f, 0.2f), 0f), new GradientColorKey(new Color(0.65f, 0.85f, 0.25f), 0.5f), new GradientColorKey(new Color(0.95f, 0.65f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
            Spin(lv, 300f);
            ColorLife(lv, Fade(Color.white, Color.white, 1f, 0.12f, 0.8f));
        }

        // Schatten: dunkler, aufsteigender Rauch, violette Irrlichter, dunkler Bodenfleck
        private static void Shadow(Transform fx)
        {
            var pool = PS(fx, "DarkPool", new Vector3(0, 0.27f, 0), SoftAlpha, 0.8f, new Vector2(3f, 3f), new Vector2(2.6f, 2.6f), new Color(0.05f, 0.01f, 0.09f, 0.95f), 4);
            Flat(pool); Spin(pool, 15f); ColorLife(pool, Fade(Color.white, Color.white, 1f, 0.3f, 0.7f));
            var sm = PS(fx, "ShadowSmoke", new Vector3(0, 0.3f, 0), SmokeAlpha, 13f, new Vector2(1.8f, 2.4f), new Vector2(0.55f, 0.85f), new Color(0.24f, 0.06f, 0.38f, 1f), 50);
            Ring(sm, 0.65f, 0.4f); Rise(sm, 0.35f, 0.6f); Noise(sm, 0.15f, 0.5f); Spin(sm, 40f);
            SizeLife(sm, 0.7f, 1.6f); ColorLife(sm, Fade(Color.white, new Color(0.45f, 0.35f, 0.55f), 0.9f, 0.2f, 0.55f));
            var vg = PS(fx, "VoidGlow", new Vector3(0, 0.28f, 0), RingAdd, 0.9f, new Vector2(2.2f, 2.2f), new Vector2(1.4f, 1.4f), new Color(0.6f, 0.15f, 1f, 0.8f), 4);
            Flat(vg); SizeLife(vg, 1.6f, 0.5f); ColorLife(vg, Fade(Color.white, Color.white, 1f, 0.2f, 0.6f));
            var wi = PS(fx, "Wisps", new Vector3(0, 1.1f, 0), SoftAdd, 12f, new Vector2(1.2f, 1.8f), new Vector2(0.08f, 0.13f), new Color(0.7f, 0.3f, 1f));
            Sphere(wi, 0.7f, new Vector3(1f, 1.4f, 0.9f), 0.2f); Rise(wi, 0.3f, 0.6f); Noise(wi, 0.6f, 1.2f);
            Trails(wi, TrailAdd, 0.3f, 0.06f, new Color(0.5f, 0.15f, 0.9f));
            ColorLife(wi, Fade(Color.white, Color.white));
        }

        // Magma: aufsteigende Glut, Hitzerauch von Schultern und Kopf, Lava-Glimmen am Boden
        private static void Magma(Transform fx)
        {
            var em = PS(fx, "Embers", new Vector3(0, 0.9f, 0), SoftAdd, 28f, new Vector2(1.2f, 2.0f), new Vector2(0.06f, 0.11f), new Color(1f, 0.6f, 0.15f), 80);
            Sphere(em, 0.8f, new Vector3(1.1f, 1.4f, 0.9f), 0.3f); Rise(em, 0.7f, 1.4f); Noise(em, 0.5f, 1.2f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.08f), 0.5f), new GradientColorKey(new Color(0.8f, 0.12f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ColorLife(em, g);
            var sm = PS(fx, "HeatSmoke", new Vector3(0, 1.65f, 0), SmokeAlpha, 5f, new Vector2(1.6f, 2.2f), new Vector2(0.35f, 0.55f), new Color(0.3f, 0.25f, 0.22f, 0.75f), 20);
            Sphere(sm, 0.45f, new Vector3(1.6f, 0.4f, 0.8f), 0.5f); Rise(sm, 0.5f, 0.8f); Noise(sm, 0.2f, 0.5f); Spin(sm, 30f);
            SizeLife(sm, 0.5f, 1.4f); ColorLife(sm, Fade(Color.white, Color.white, 0.7f, 0.15f, 0.5f));
            var gl = PS(fx, "LavaGlow", new Vector3(0, 0.27f, 0), SoftAdd, 1.2f, new Vector2(1.6f, 2.2f), new Vector2(1.7f, 2.2f), new Color(1f, 0.35f, 0.05f, 0.85f), 6);
            Flat(gl); ColorLife(gl, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
        }

        // Kristall: umkreisende Kristallsplitter, funkelnde Glitzer, kalter Bodennebel
        private static void Crystal(Transform fx)
        {
            var shardMesh = AssetDatabase.LoadAssetAtPath<Mesh>(VfxMat + "IceShard.asset");
            var shardMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Models/Characters/Materials/Crystal_Shard.mat");
            var sh = PS(fx, "OrbitShards", new Vector3(0, 1.25f, 0), shardMat, 1.6f, new Vector2(4f, 4f), new Vector2(0.38f, 0.55f), Color.white, 10);
            Ring(sh, 1.05f, 0f, 10f, 0f); Orbit(sh, 1.5f, 0f);
            var r = sh.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = shardMesh;
            r.alignment = ParticleSystemRenderSpace.Local;
            var m = sh.main; m.startRotation3D = true;
            m.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = sh.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f); rot.y = new ParticleSystem.MinMaxCurve(-2f, 2f); rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            var sz = sh.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.12f, 1f), new Keyframe(0.88f, 1f), new Keyframe(1f, 0f)));
            var gl = PS(fx, "Glints", new Vector3(0, 1.3f, 0), StarAdd, 6f, new Vector2(0.4f, 0.6f), new Vector2(0.18f, 0.3f), new Color(0.8f, 0.92f, 1f));
            Sphere(gl, 0.8f, new Vector3(1.1f, 1.3f, 0.9f), 0.2f); SizeBump(gl); Spin(gl, 90f);
            var mist = PS(fx, "FrostMist", new Vector3(0, 0.32f, 0), SmokeAlpha, 4f, new Vector2(2.2f, 2.8f), new Vector2(0.35f, 0.55f), new Color(0.8f, 0.9f, 1f, 0.45f), 20);
            Ring(mist, 0.65f, 0.4f); Orbit(mist, 0.6f, 0.05f, 0.15f); Spin(mist, 25f);
            SizeLife(mist, 0.8f, 1.5f); ColorLife(mist, Fade(Color.white, Color.white, 0.8f, 0.25f, 0.6f));
        }
    }
}
