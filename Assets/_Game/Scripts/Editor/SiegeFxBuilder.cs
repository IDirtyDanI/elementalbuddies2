using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Baut die Brand-Effekte der Belagerung reproduzierbar: VFX_HouseFire (Flammen, Rauchsäule, Funken)
    // und VFX_Smolder (Glut + dünner Rauch für Ruinen). Menü: BuddyTD → Belagerung → Feuer-Effekte neu bauen.
    // Partikel laufen in unskalierter Zeit (Upgrade-Bildschirm setzt timeScale = 0).
    public static class SiegeFxBuilder
    {
        private const string VfxMat = "Assets/_Game/VFX/Materials/";
        private const string VfxTex = "Assets/_Game/VFX/Textures/";
        private const string VfxPrefabs = "Assets/_Game/VFX/Prefabs/";
        public const string HouseFirePath = VfxPrefabs + "VFX_HouseFire.prefab";
        public const string SmolderPath = VfxPrefabs + "VFX_Smolder.prefab";

        [MenuItem("BuddyTD/Belagerung/Feuer-Effekte neu bauen")]
        public static void BuildAll()
        {
            PrepareTexture("vfx_flame.png");
            Build(HouseFirePath, HouseFire);
            Build(SmolderPath, Smolder);
            AssetDatabase.SaveAssets();
            Debug.Log("SiegeFxBuilder: VFX_HouseFire und VFX_Smolder neu gebaut.");
        }

        // Bestehendes Prefab wird inhaltlich ersetzt (GUID bleibt → Instanzen in Gebäude-Prefabs bleiben gültig)
        private static void Build(string path, System.Action<Transform> fill)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject();
            root.name = System.IO.Path.GetFileNameWithoutExtension(path);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            fill(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }

        private static void PrepareTexture(string file)
        {
            var imp = AssetImporter.GetAtPath(VfxTex + file) as TextureImporter;
            if (imp == null) return;
            if (imp.alphaIsTransparency && imp.wrapMode == TextureWrapMode.Clamp) return;
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
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

        private static Material FlameAdd { get { return Mat("VFX_Flame_Add", "VFX_SoftDot_Add", "vfx_flame.png"); } }
        private static Material SoftAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_SoftDot_Add.mat"); } }
        private static Material SmokeAlpha { get { return Mat("VFX_Smoke_Alpha", "VFX_LavaTrail", "vfx_smoke.png"); } }

        // ---------------- Partikel-Helfer (wie ElementAuraBuilder) ----------------

        private static ParticleSystem PS(Transform parent, string name, Vector3 pos, Material mat, float rate,
            Vector2 life, Vector2 size, Color color, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.prewarm = false;
            main.duration = 5f;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
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

        private static Gradient Grad(Color[] colors, float[] colorTimes, float[] alphas, float[] alphaTimes)
        {
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i], colorTimes[i]);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i], alphaTimes[i]);
            var g = new Gradient();
            g.SetKeys(ck, ak);
            return g;
        }

        private static void ColorLife(ParticleSystem ps, Gradient g)
        {
            var c = ps.colorOverLifetime; c.enabled = true; c.color = g;
        }

        private static void SizeCurve(ParticleSystem ps, params Keyframe[] keys)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        // Ellipsoid (flach) um die Ankerposition
        private static void Blob(ParticleSystem ps, float radius, Vector3 scale)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius; sh.radiusThickness = 1f; sh.scale = scale;
        }

        // Aufsteigen + seitlicher Wind (Weltraum, damit alle Rauchsäulen gleich treiben)
        private static void Drift(ParticleSystem ps, Vector2 up, float windX, float windZ)
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(windX * 0.6f, windX);
            v.z = new ParticleSystem.MinMaxCurve(windZ * 0.6f, windZ);
            v.y = new ParticleSystem.MinMaxCurve(up.x, up.y);
        }

        private static void Noise(ParticleSystem ps, float strength, float freq)
        {
            var n = ps.noise; n.enabled = true; n.strength = strength; n.frequency = freq; n.scrollSpeed = 0.5f;
        }

        private static void Spin(ParticleSystem ps, float degPerSec)
        {
            var m = ps.main; m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var r = ps.rotationOverLifetime; r.enabled = true;
            r.z = new ParticleSystem.MinMaxCurve(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }

        // 2×2-Flipbook, zufälliges Startbild, wechselt während der Lebenszeit (Flackern)
        private static void Flipbook(ParticleSystem ps, float cycles)
        {
            var ts = ps.textureSheetAnimation; ts.enabled = true;
            ts.mode = ParticleSystemAnimationMode.Grid;
            ts.numTilesX = 2; ts.numTilesY = 2;
            ts.animation = ParticleSystemAnimationType.WholeSheet;
            ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            ts.cycleCount = Mathf.Max(1, Mathf.RoundToInt(cycles));
        }

        private const float WindX = 0.55f, WindZ = 0.25f;

        // ---------------- Hausbrand ----------------

        // Flammen aus Dach/Fenstern: Kern-Glühen, flackernde Flammenzungen, Funken, dunkle Rauchsäule
        private static void HouseFire(Transform fx)
        {
            var glow = PS(fx, "Glow", new Vector3(0, 0.2f, 0), SoftAdd, 7f, new Vector2(0.5f, 0.8f), new Vector2(1.8f, 2.6f), new Color(1f, 0.45f, 0.1f, 0.55f), 12);
            Blob(glow, 0.4f, new Vector3(1f, 0.4f, 1f));
            ColorLife(glow, Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 1f, 0f }, new[] { 0f, 0.3f, 1f }));

            var fl = PS(fx, "Flames", new Vector3(0, 0.1f, 0), FlameAdd, 18f, new Vector2(0.55f, 0.85f), new Vector2(1.0f, 1.6f), Color.white, 30);
            Blob(fl, 0.55f, new Vector3(1f, 0.3f, 1f));
            Drift(fl, new Vector2(1.6f, 2.6f), WindX * 0.4f, WindZ * 0.4f);
            Noise(fl, 0.35f, 1.4f);
            Flipbook(fl, 2f);
            var m = fl.main; m.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            SizeCurve(fl, new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.25f));
            ColorLife(fl, Grad(
                new[] { new Color(1f, 0.95f, 0.65f), new Color(1f, 0.62f, 0.15f), new Color(0.95f, 0.25f, 0.05f) }, new[] { 0f, 0.35f, 1f },
                new[] { 0f, 0.95f, 0.8f, 0f }, new[] { 0f, 0.12f, 0.6f, 1f }));

            var core = PS(fx, "FlameCore", new Vector3(0, 0.15f, 0), FlameAdd, 10f, new Vector2(0.4f, 0.6f), new Vector2(0.6f, 0.9f), new Color(1f, 0.92f, 0.6f), 12);
            Blob(core, 0.35f, new Vector3(1f, 0.3f, 1f));
            Drift(core, new Vector2(1.0f, 1.6f), 0f, 0f);
            Flipbook(core, 2f);
            SizeCurve(core, new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.3f));
            ColorLife(core, Grad(new[] { Color.white, new Color(1f, 0.8f, 0.4f) }, new[] { 0f, 1f }, new[] { 0f, 1f, 0f }, new[] { 0f, 0.15f, 1f }));

            var em = PS(fx, "Embers", new Vector3(0, 0.8f, 0), SoftAdd, 9f, new Vector2(1.6f, 2.6f), new Vector2(0.07f, 0.14f), new Color(1f, 0.6f, 0.15f), 30);
            Blob(em, 0.6f, new Vector3(1f, 0.5f, 1f));
            Drift(em, new Vector2(1.6f, 3.0f), WindX, WindZ);
            Noise(em, 1.1f, 1.3f);
            ColorLife(em, Grad(
                new[] { new Color(1f, 0.9f, 0.5f), new Color(1f, 0.45f, 0.08f), new Color(0.8f, 0.12f, 0.02f) }, new[] { 0f, 0.5f, 1f },
                new[] { 0f, 1f, 0.8f, 0f }, new[] { 0f, 0.08f, 0.6f, 1f }));

            var sm = PS(fx, "Smoke", new Vector3(0, 1.4f, 0), SmokeAlpha, 4.5f, new Vector2(4.5f, 5.5f), new Vector2(1.3f, 1.8f), new Color(0.1f, 0.085f, 0.08f, 1f), 30);
            Blob(sm, 0.5f, new Vector3(1f, 0.4f, 1f));
            Drift(sm, new Vector2(1.1f, 1.5f), WindX * 1.6f, WindZ * 1.6f);
            Noise(sm, 0.3f, 0.35f);
            Spin(sm, 25f);
            SizeCurve(sm, new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1.4f), new Keyframe(1f, 2.4f));
            ColorLife(sm, Grad(
                new[] { new Color(0.75f, 0.45f, 0.3f), Color.white, new Color(1.8f, 1.8f, 1.9f) }, new[] { 0f, 0.25f, 1f },
                new[] { 0f, 0.62f, 0.42f, 0f }, new[] { 0f, 0.12f, 0.55f, 1f }));
            // Rauch hinter den Flammen zeichnen
            sm.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;
        }

        // ---------------- Ruine ----------------

        // Glimmende Glut am Boden, wenige Funken, dünner grauer Rauch; beim Einsturz ein Staub-/Rauchstoß
        private static void Smolder(Transform fx)
        {
            var glow = PS(fx, "EmberGlow", new Vector3(0, 0.15f, 0), SoftAdd, 3f, new Vector2(1.4f, 2.2f), new Vector2(1.0f, 1.7f), new Color(1f, 0.32f, 0.06f, 0.6f), 8);
            Blob(glow, 0.8f, new Vector3(1f, 0.2f, 1f));
            glow.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ColorLife(glow, Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 1f, 0.6f, 0f }, new[] { 0f, 0.3f, 0.7f, 1f }));

            var em = PS(fx, "Embers", new Vector3(0, 0.3f, 0), SoftAdd, 3f, new Vector2(1.2f, 2.0f), new Vector2(0.06f, 0.1f), new Color(1f, 0.5f, 0.12f), 10);
            Blob(em, 0.8f, new Vector3(1f, 0.3f, 1f));
            Drift(em, new Vector2(0.8f, 1.6f), WindX * 0.6f, WindZ * 0.6f);
            Noise(em, 0.8f, 1.2f);
            ColorLife(em, Grad(new[] { new Color(1f, 0.8f, 0.4f), new Color(0.9f, 0.2f, 0.03f) }, new[] { 0f, 1f }, new[] { 0f, 1f, 0f }, new[] { 0f, 0.1f, 1f }));

            var sm = PS(fx, "Smoke", new Vector3(0, 0.4f, 0), SmokeAlpha, 2.2f, new Vector2(5f, 6.5f), new Vector2(0.9f, 1.3f), new Color(0.2f, 0.19f, 0.19f, 1f), 18);
            Blob(sm, 0.6f, new Vector3(1f, 0.3f, 1f));
            Drift(sm, new Vector2(0.6f, 0.95f), WindX * 1.3f, WindZ * 1.3f);
            Noise(sm, 0.25f, 0.3f);
            Spin(sm, 20f);
            SizeCurve(sm, new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1.5f), new Keyframe(1f, 2.4f));
            ColorLife(sm, Grad(new[] { Color.white, new Color(1.6f, 1.6f, 1.7f) }, new[] { 0f, 1f },
                new[] { 0f, 0.45f, 0.3f, 0f }, new[] { 0f, 0.15f, 0.6f, 1f }));

            // Einsturz-Stoß: einmalig beim Aktivieren
            var puff = PS(fx, "CollapsePuff", new Vector3(0, 0.6f, 0), SmokeAlpha, 0f, new Vector2(1.6f, 2.4f), new Vector2(1.6f, 2.4f), new Color(0.35f, 0.32f, 0.3f, 1f), 16);
            var pm = puff.main; pm.loop = false; pm.duration = 1f;
            pm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            var pe = puff.emission; pe.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
            Blob(puff, 1.2f, new Vector3(1.4f, 0.4f, 1.4f));
            var lim = puff.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.4f; lim.dampen = 0.15f;
            Spin(puff, 30f);
            SizeCurve(puff, new Keyframe(0f, 0.6f), new Keyframe(1f, 2f));
            ColorLife(puff, Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 0.7f, 0f }, new[] { 0f, 0.1f, 1f }));
        }
    }
}
