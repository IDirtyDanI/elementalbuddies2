using System.IO;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Baut alle Effekte, Pfeile, die Dornenfalle und Platzhalter-Waffen der Champions (Schwertkämpfer, Bogenschütze) neu.
    // Menü: BuddyTD → Champion-Effekte neu bauen. Ziel: Assets/_Game/VFX/Prefabs/Champions/ (+ Materialien/Texturen unter VFX/).
    public static class ChampionFxBuilder
    {
        public const string Dir = "Assets/_Game/VFX/Prefabs/Champions/";
        private const string VfxMat = "Assets/_Game/VFX/Materials/";
        private const string VfxTex = "Assets/_Game/VFX/Textures/";
        public const string WeaponDir = "Assets/_Game/Models/Characters/Weapons/";

        // Element-Farben
        private static readonly Color Fire = new Color(1f, 0.5f, 0.12f);
        private static readonly Color Ice = new Color(0.55f, 0.85f, 1f);
        private static readonly Color Earth = new Color(0.55f, 0.4f, 0.22f);
        private static readonly Color Moss = new Color(0.35f, 0.55f, 0.2f);
        private static readonly Color Gold = new Color(1f, 0.85f, 0.4f);

        [MenuItem("BuddyTD/Champion-Effekte neu bauen")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/VFX/Prefabs/Champions"))
                AssetDatabase.CreateFolder("Assets/_Game/VFX/Prefabs", "Champions");
            EnsureSlashTexture();

            Save("VFX_SwordSlash", SwordSlash);
            Save("VFX_SwordHit", SwordHit);
            Save("VFX_BlockSpark", BlockSpark);
            Save("VFX_BlockGlow", BlockGlow);
            Save("VFX_FlameWhirl", FlameWhirl);
            Save("VFX_FrostStrike", FrostStrike);
            Save("VFX_Earthquake", Earthquake);
            Save("VFX_LightOath", LightOath);
            Save("VFX_LightOathAura", LightOathAura);

            Save("VFX_ArrowHit", ArrowHit);
            Save("VFX_RollDust", RollDust);
            Save("Arrow", go => ArrowPrefab(go, ArrowKind.Normal));
            Save("FrostArrow", go => ArrowPrefab(go, ArrowKind.Frost));
            Save("FireArrow_Falling", go => ArrowPrefab(go, ArrowKind.Fire));
            Save("VFX_FireRainArea", FireRainArea);
            Save("VFX_FireImpact", FireImpact);
            Save("VFX_FrostArrowCast", FrostArrowCast);
            Save("ThornTrap", ThornTrapPrefab);
            Save("VFX_ThornRoot", ThornRoot);
            Save("VFX_ThornSnap", ThornSnap);
            Save("VFX_LightBeam", LightBeam);

            // Schwert, Schild, Bogen, Stab kommen als Blender-Modelle (Models/Characters/Weapons); nur der Köcher ist Platzhalter
            Save("Weapon_Quiver", Quiver);

            AssetDatabase.SaveAssets();
            Debug.Log("ChampionFxBuilder: Champion-Effekte, Pfeile, Falle und Platzhalter-Waffen gebaut (" + Dir + ").");
        }

        public static GameObject Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(Dir + name + ".prefab");
        }

        private static void Save(string name, System.Action<GameObject> fill)
        {
            var go = new GameObject(name);
            try
            {
                fill(go);
                PrefabUtility.SaveAsPrefabAsset(go, Dir + name + ".prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------- Texturen / Materialien ----------------

        // Schwert-Bogen: Kante (v = 1) hell, nach innen weich ausblendend
        private static void EnsureSlashTexture()
        {
            string path = VfxTex + "vfx_slash.png";
            const int w = 128, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float v = (float)y / (h - 1);
                float core = 0.25f + 0.75f * Mathf.Pow(v, 1.4f);              // nach außen heller
                float edge = 1f - Mathf.Clamp01((v - 0.9f) / 0.1f) * 0.6f;   // äußerster Rand leicht weich
                float a = Mathf.Clamp01(core * edge);
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / (w - 1);
                    float ends = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 12f); // Enden weich
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * ends));
                }
            }
            tex.Apply();
            bool existed = File.Exists(path);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (existed) return;
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }

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

        private static Material Add(string name, string tex) { return Mat(name, "VFX_SoftDot_Add", tex); }
        private static Material Alpha(string name, string tex) { return Mat(name, "VFX_LavaTrail", tex); }

        private static Material SoftAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_SoftDot_Add.mat"); } }
        private static Material RingAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Ring_Add.mat"); } }
        private static Material SparkAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Spark_Add.mat"); } }
        private static Material StarAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Star_Add.mat"); } }
        private static Material FlameAdd { get { return AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Flame_Add.mat"); } }
        private static Material SmokeAlpha { get { return Alpha("VFX_Smoke_Alpha", "vfx_smoke.png"); } }
        private static Material DustAlpha { get { var m = AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Dust_Alpha.mat"); return m != null ? m : SmokeAlpha; } }
        private static Material LeafAlpha { get { return Alpha("VFX_Leaf_Alpha", "vfx_leaf.png"); } }
        private static Material TrailAdd { get { return Add("VFX_Trail_Add", "vfx_trail.png"); } }
        private static Material SlashAdd { get { return Add("VFX_Slash_Add", "vfx_slash.png"); } }
        private static Material RingAlpha { get { return Alpha("VFX_Ring_Alpha", "vfx_ring.png"); } }
        private static Material SoftAlpha { get { return Alpha("VFX_SoftDot_Alpha", "vfx_soft_dot.png"); } }

        // Einfaches URP-Lit-Material für Platzhalter-Meshes (Waffen, Pfeile, Dornen)
        private static Material Lit(string name, Color color, float smooth = 0.3f, float metal = 0f, Color? emission = null)
        {
            string p = VfxMat + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Mesh IceShardMesh { get { return AssetDatabase.LoadAssetAtPath<Mesh>(VfxMat + "IceShard.asset"); } }
        private static Mesh CubeMesh { get { return Resources.GetBuiltinResource<Mesh>("Cube.fbx"); } }

        // ---------------- Partikel-Helfer ----------------

        // Einmal-Effekt (Burst) – loop = false
        private static ParticleSystem PS(Transform parent, string name, Vector3 pos, Material mat, Vector2 life, Vector2 size, Color color, int max = 60, bool loop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = loop; main.playOnAwake = true; main.prewarm = false;
            main.duration = loop ? 2f : 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = max;
            main.stopAction = ParticleSystemStopAction.None;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static void Burst(ParticleSystem ps, int count, float time = 0f)
        {
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(time, (short)count) });
        }

        private static void Rate(ParticleSystem ps, float rate)
        {
            var em = ps.emission; em.rateOverTime = rate;
        }

        private static void Speed(ParticleSystem ps, float min, float max)
        {
            var m = ps.main; m.startSpeed = new ParticleSystem.MinMaxCurve(min, max);
        }

        private static void Gravity(ParticleSystem ps, float g)
        {
            var m = ps.main; m.gravityModifier = g;
        }

        private static Gradient Fade(Color a, Color b, float peak = 1f, float inAt = 0.1f, float outAt = 0.6f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inAt), new GradientAlphaKey(peak, outAt), new GradientAlphaKey(0f, 1f) });
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

        private static void SizeCurve(ParticleSystem ps, params Keyframe[] keys)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        private static void Ring(ParticleSystem ps, float radius, float thickness = 0f, float arc = 360f)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = radius; sh.radiusThickness = thickness; sh.arc = arc;
            sh.rotation = new Vector3(-90f, 0f, 0f);
        }

        private static void Sphere(ParticleSystem ps, float radius, float thickness = 1f)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius; sh.radiusThickness = thickness;
        }

        private static void Hemisphere(ParticleSystem ps, float radius)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Hemisphere;
            sh.radius = radius; sh.rotation = new Vector3(-90f, 0f, 0f);
        }

        private static void Cone(ParticleSystem ps, float angle, float radius, Vector3 rotation, float length = 1f, bool volume = false)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = volume ? ParticleSystemShapeType.ConeVolume : ParticleSystemShapeType.Cone;
            sh.angle = angle; sh.radius = radius; sh.length = length; sh.rotation = rotation;
        }

        private static void Flat(ParticleSystem ps)
        {
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        private static void Stretch(ParticleSystem ps, float length, float velocityScale = 0.04f)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = length; r.velocityScale = velocityScale;
        }

        private static void Spin(ParticleSystem ps, float degPerSec)
        {
            var m = ps.main; m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var r = ps.rotationOverLifetime; r.enabled = true;
            r.z = new ParticleSystem.MinMaxCurve(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var l = ps.limitVelocityOverLifetime; l.enabled = true; l.drag = drag; l.dampen = 0f;
            l.limit = 1000f;
        }

        private static void MeshParticles(ParticleSystem ps, Mesh mesh)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = mesh;
            r.alignment = ParticleSystemRenderSpace.World;
            var m = ps.main; m.startRotation3D = true;
            m.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        private static Light AddLight(Transform parent, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        // Würfel-Teil eines Platzhalter-Meshes (ohne Collider)
        private static GameObject Part(Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ---------------- Schwertkämpfer ----------------

        // Bogen-Mesh (SlashArcFx) + kleine Funken entlang der Klinge
        private static void SwordSlash(GameObject go)
        {
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SlashAdd;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var fx = go.AddComponent<SlashArcFx>();
            fx.Radius = 2.6f; fx.Width = 0.9f; fx.Angle = 120f;
        }

        private static void SwordHit(GameObject go)
        {
            var sp = PS(go.transform, "Sparks", Vector3.zero, SparkAdd, new Vector2(0.15f, 0.3f), new Vector2(0.05f, 0.1f), new Color(1f, 0.9f, 0.65f));
            Burst(sp, 12); Sphere(sp, 0.1f); Speed(sp, 4f, 8f); Gravity(sp, 1.2f); Stretch(sp, 3f);
            var fl = PS(go.transform, "Flash", Vector3.zero, SoftAdd, new Vector2(0.12f, 0.12f), new Vector2(0.9f, 0.9f), new Color(1f, 0.95f, 0.8f, 0.9f), 2);
            Burst(fl, 1); SizeLife(fl, 0.4f, 1.2f); ColorLife(fl, Fade(Color.white, Color.white, 1f, 0.05f, 0.3f));
        }

        // Geblockter Treffer: goldene Funken-Fontäne + Ring-Blitz am Schild
        private static void BlockSpark(GameObject go)
        {
            var sp = PS(go.transform, "Sparks", Vector3.zero, SparkAdd, new Vector2(0.2f, 0.4f), new Vector2(0.06f, 0.12f), new Color(1f, 0.85f, 0.45f));
            Burst(sp, 18); Cone(sp, 55f, 0.15f, new Vector3(0f, 180f, 0f)); Speed(sp, 4f, 9f); Gravity(sp, 1.5f); Stretch(sp, 3.5f);
            var ring = PS(go.transform, "Ring", Vector3.zero, RingAdd, new Vector2(0.22f, 0.22f), new Vector2(1.1f, 1.1f), new Color(1f, 0.9f, 0.55f, 1f), 2);
            Burst(ring, 1); SizeLife(ring, 0.3f, 1.4f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.05f, 0.3f));
            var r = ring.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            var fl = PS(go.transform, "Flash", Vector3.zero, SoftAdd, new Vector2(0.1f, 0.1f), new Vector2(1.4f, 1.4f), new Color(1f, 0.92f, 0.7f, 0.8f), 2);
            Burst(fl, 1); ColorLife(fl, Fade(Color.white, Color.white, 1f, 0.05f, 0.3f));
        }

        // Schild-Leuchten beim Halten (Schleife)
        private static void BlockGlow(GameObject go)
        {
            var gl = PS(go.transform, "Glow", Vector3.zero, SoftAdd, new Vector2(0.5f, 0.5f), new Vector2(1.1f, 1.3f), new Color(1f, 0.85f, 0.45f, 0.35f), 6, true);
            Rate(gl, 8f); ColorLife(gl, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var main = gl.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var mo = PS(go.transform, "Motes", Vector3.zero, SoftAdd, new Vector2(0.4f, 0.6f), new Vector2(0.04f, 0.07f), new Color(1f, 0.9f, 0.6f), 20, true);
            Rate(mo, 14f); Ring(mo, 0.45f); Speed(mo, 0.1f, 0.4f);
            var m2 = mo.main; m2.simulationSpace = ParticleSystemSimulationSpace.Local;
            var sh = mo.shape; sh.rotation = Vector3.zero; // Kreis vor dem Spieler (XY-Ebene)
            ColorLife(mo, Fade(Color.white, Color.white));
        }

        // Flammenwirbel: Flammenring, Glut, Brandfleck (der Feuerbogen selbst ist ein SlashArcFx)
        private static void FlameWhirl(GameObject go)
        {
            var fl = PS(go.transform, "FlameRing", new Vector3(0, 0.6f, 0), FlameAdd, new Vector2(0.35f, 0.6f), new Vector2(0.6f, 1.1f), new Color(1f, 0.6f, 0.2f), 80);
            Burst(fl, 46); Ring(fl, 1.2f, 0.6f); Speed(fl, 4f, 7f); Drag(fl, 4f); Spin(fl, 180f);
            var vel = fl.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(6f); vel.y = new ParticleSystem.MinMaxCurve(0.8f);
            ColorLife(fl, Fade(new Color(1f, 0.85f, 0.4f), new Color(0.9f, 0.2f, 0.05f), 1f, 0.05f, 0.5f));
            SizeLife(fl, 1f, 0.4f);
            var em = PS(go.transform, "Embers", new Vector3(0, 0.6f, 0), SoftAdd, new Vector2(0.6f, 1.1f), new Vector2(0.05f, 0.1f), Fire, 60);
            Burst(em, 40); Ring(em, 1.5f, 0.8f); Speed(em, 3f, 7f); Gravity(em, -0.2f); Drag(em, 2f);
            ColorLife(em, Fade(Color.white, new Color(1f, 0.3f, 0.05f)));
            var sc = PS(go.transform, "Scorch", new Vector3(0, 0.05f, 0), RingAdd, new Vector2(0.5f, 0.5f), new Vector2(7f, 7f), new Color(1f, 0.45f, 0.1f, 0.8f), 2);
            Burst(sc, 1); Flat(sc); SizeLife(sc, 0.3f, 1.05f); ColorLife(sc, Fade(Color.white, Color.white, 1f, 0.05f, 0.4f));
            AddLight(go.transform, new Vector3(0, 1f, 0), Fire, 4f, 7f).gameObject.AddComponent<LightFade>().Duration = 0.6f;
        }

        // Frostschlag: Eiszacken brechen im Kegel nach vorne aus dem Boden, dazu Frostnebel und Schockwellen-Schlieren
        private static void FrostStrike(GameObject go)
        {
            var shardMat = AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Ice_Shard.mat");
            for (int i = 0; i < 3; i++)
            {
                float t = i * 0.06f;
                var sh = PS(go.transform, "Spikes" + i, Vector3.zero, shardMat, new Vector2(0.9f, 1.2f), new Vector2(0.5f + i * 0.15f, 0.9f + i * 0.2f), Color.white, 30);
                Burst(sh, 9, t);
                Cone(sh, 32f, 0.4f, Vector3.zero, 2.5f + i * 2.2f, true);
                var s = sh.shape; s.position = new Vector3(0f, 0f, 0.6f + i * 1.6f); s.length = 1.8f;
                MeshParticles(sh, IceShardMesh);
                var m = sh.main; m.startRotationX = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f); m.startRotationZ = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                SizeCurve(sh, new Keyframe(0f, 0f), new Keyframe(0.12f, 1.1f), new Keyframe(0.2f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f));
            }
            var wave = PS(go.transform, "Shockwave", new Vector3(0, 0.4f, 0.3f), SoftAdd, new Vector2(0.4f, 0.5f), new Vector2(0.25f, 0.45f), Ice, 60);
            Burst(wave, 40); Cone(wave, 30f, 0.3f, Vector3.zero); Speed(wave, 12f, 16f); Stretch(wave, 4f);
            ColorLife(wave, Fade(Color.white, new Color(0.5f, 0.8f, 1f)));
            var mist = PS(go.transform, "FrostMist", new Vector3(0, 0.3f, 0.5f), SmokeAlpha, new Vector2(0.8f, 1.2f), new Vector2(0.8f, 1.4f), new Color(0.85f, 0.95f, 1f, 0.6f), 40);
            Burst(mist, 22); Cone(mist, 30f, 0.4f, Vector3.zero); Speed(mist, 5f, 9f); Drag(mist, 3f); Spin(mist, 40f);
            SizeLife(mist, 0.6f, 1.5f); ColorLife(mist, Fade(Color.white, Color.white, 0.8f));
            var gl = PS(go.transform, "Glints", new Vector3(0, 0.6f, 2.5f), StarAdd, new Vector2(0.3f, 0.6f), new Vector2(0.15f, 0.3f), new Color(0.8f, 0.95f, 1f), 30);
            Burst(gl, 16, 0.05f); var gs = gl.shape; gs.enabled = true; gs.shapeType = ParticleSystemShapeType.Box; gs.scale = new Vector3(3f, 0.8f, 5f);
            ColorLife(gl, Fade(Color.white, Color.white));
        }

        // Erdbeben: Staubring, fliegende Brocken, Riss-Ring
        private static void Earthquake(GameObject go)
        {
            var dust = PS(go.transform, "DustRing", new Vector3(0, 0.2f, 0), DustAlpha, new Vector2(0.8f, 1.3f), new Vector2(0.9f, 1.6f), new Color(0.6f, 0.48f, 0.32f, 0.85f), 60);
            Burst(dust, 40); Ring(dust, 0.6f); Speed(dust, 6f, 9f); Drag(dust, 4f); Spin(dust, 60f);
            SizeLife(dust, 0.6f, 1.6f); ColorLife(dust, Fade(Color.white, Color.white, 0.9f, 0.05f, 0.5f));
            var rocks = PS(go.transform, "Rocks", new Vector3(0, 0.2f, 0), Lit("Champ_Rock", new Color(0.42f, 0.33f, 0.24f), 0.1f), new Vector2(0.9f, 1.2f), new Vector2(0.15f, 0.35f), Color.white, 30);
            Burst(rocks, 18); Hemisphere(rocks, 1.2f); Speed(rocks, 4f, 8f); Gravity(rocks, 2.2f); MeshParticles(rocks, CubeMesh);
            var rot = rocks.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            SizeCurve(rocks, new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f));
            var crack = PS(go.transform, "Crack", new Vector3(0, 0.05f, 0), RingAlpha, new Vector2(1.2f, 1.2f), new Vector2(8f, 8f), new Color(0.3f, 0.2f, 0.1f, 0.9f), 2);
            Burst(crack, 1); Flat(crack); SizeLife(crack, 0.2f, 1f); ColorLife(crack, Fade(Color.white, Color.white, 1f, 0.03f, 0.6f));
            var glow = PS(go.transform, "GroundGlow", new Vector3(0, 0.08f, 0), RingAdd, new Vector2(0.45f, 0.45f), new Vector2(8f, 8f), new Color(1f, 0.6f, 0.25f, 0.6f), 2);
            Burst(glow, 1); Flat(glow); SizeLife(glow, 0.15f, 1f); ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.05f, 0.3f));
            var moss = PS(go.transform, "Grass", new Vector3(0, 0.2f, 0), LeafAlpha, new Vector2(0.8f, 1.2f), new Vector2(0.12f, 0.2f), new Color(0.45f, 0.65f, 0.25f), 30);
            Burst(moss, 16); Ring(moss, 1f); Speed(moss, 3f, 6f); Gravity(moss, 0.8f); Spin(moss, 300f);
        }

        // Lichtschwur: goldene Lichtsäule, Ring bis zum Spott-Radius, Heil-Funken
        private static void LightOath(GameObject go)
        {
            var pil = PS(go.transform, "Pillar", new Vector3(0, 0.1f, 0), SoftAdd, new Vector2(0.5f, 0.8f), new Vector2(0.3f, 0.6f), Gold, 80);
            Burst(pil, 50); Ring(pil, 0.5f, 1f); Speed(pil, 6f, 12f); Stretch(pil, 6f, 0.08f);
            var sh = pil.shape; sh.rotation = new Vector3(-90f, 0f, 0f);
            ColorLife(pil, Fade(Color.white, new Color(1f, 0.8f, 0.3f)));
            var vel = pil.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local; vel.y = new ParticleSystem.MinMaxCurve(8f);
            var ring = PS(go.transform, "TauntRing", new Vector3(0, 0.08f, 0), RingAdd, new Vector2(0.6f, 0.6f), new Vector2(14f, 14f), new Color(1f, 0.85f, 0.4f, 0.9f), 2);
            Burst(ring, 1); Flat(ring); SizeLife(ring, 0.1f, 1f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.05f, 0.5f));
            var st = PS(go.transform, "HealSparkles", new Vector3(0, 1f, 0), StarAdd, new Vector2(0.8f, 1.3f), new Vector2(0.15f, 0.3f), new Color(1f, 0.95f, 0.6f), 40);
            Burst(st, 26); Sphere(st, 1.3f); Speed(st, 0.5f, 1.5f); Gravity(st, -0.3f);
            ColorLife(st, Fade(Color.white, Color.white));
            AddLight(go.transform, new Vector3(0, 2f, 0), Gold, 5f, 9f).gameObject.AddComponent<LightFade>().Duration = 1f;
        }

        // Aura während des Schwurs (am Spieler, Schleife)
        private static void LightOathAura(GameObject go)
        {
            var ring = PS(go.transform, "FeetRing", new Vector3(0, 0.06f, 0), RingAdd, new Vector2(1f, 1f), new Vector2(2.6f, 2.6f), new Color(1f, 0.85f, 0.4f, 0.55f), 4, true);
            Rate(ring, 2f); Flat(ring); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var m = ring.main; m.simulationSpace = ParticleSystemSimulationSpace.Local;
            var up = PS(go.transform, "Motes", new Vector3(0, 0.2f, 0), SoftAdd, new Vector2(0.8f, 1.2f), new Vector2(0.05f, 0.09f), Gold, 40, true);
            Rate(up, 18f); Ring(up, 0.6f); Speed(up, 0f, 0f);
            var v = up.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local; v.y = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            var mm = up.main; mm.simulationSpace = ParticleSystemSimulationSpace.Local;
            ColorLife(up, Fade(Color.white, Color.white));
        }

        // ---------------- Bogenschütze ----------------

        private static void ArrowHit(GameObject go)
        {
            var sp = PS(go.transform, "Sparks", Vector3.zero, SparkAdd, new Vector2(0.12f, 0.25f), new Vector2(0.04f, 0.08f), new Color(1f, 0.92f, 0.7f));
            Burst(sp, 8); Cone(sp, 45f, 0.05f, Vector3.zero); Speed(sp, 3f, 6f); Gravity(sp, 1f); Stretch(sp, 2.5f);
            var fl = PS(go.transform, "Puff", Vector3.zero, SoftAdd, new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.5f), new Color(1f, 0.95f, 0.85f, 0.7f), 2);
            Burst(fl, 1); ColorLife(fl, Fade(Color.white, Color.white, 1f, 0.05f, 0.3f));
        }

        private static void RollDust(GameObject go)
        {
            var d = PS(go.transform, "Dust", new Vector3(0, 0.15f, 0), DustAlpha, new Vector2(0.5f, 0.8f), new Vector2(0.45f, 0.8f), new Color(0.7f, 0.6f, 0.45f, 0.7f), 30);
            Burst(d, 14); Ring(d, 0.35f); Speed(d, 1.2f, 2.5f); Drag(d, 3f); Spin(d, 60f);
            SizeLife(d, 0.6f, 1.5f); ColorLife(d, Fade(Color.white, Color.white, 0.85f, 0.05f, 0.4f));
            var streak = PS(go.transform, "Streaks", new Vector3(0, 0.6f, 0), SoftAlpha, new Vector2(0.25f, 0.35f), new Vector2(0.06f, 0.1f), new Color(1f, 1f, 1f, 0.5f), 10);
            Burst(streak, 6); Cone(streak, 10f, 0.3f, new Vector3(0f, 180f, 0f)); Speed(streak, 4f, 6f); Stretch(streak, 4f);
        }

        private enum ArrowKind { Normal, Frost, Fire }

        // Pfeil-Mesh (+Z = Flugrichtung): Schaft, Spitze, Federn; Schweif als TrailRenderer (wird beim Aufprall gelöst)
        private static void ArrowPrefab(GameObject go, ArrowKind kind)
        {
            Color tipCol = kind == ArrowKind.Frost ? Ice : (kind == ArrowKind.Fire ? Fire : new Color(0.75f, 0.75f, 0.78f));
            var wood = Lit("Champ_ArrowWood", new Color(0.55f, 0.38f, 0.22f), 0.2f);
            var tip = kind == ArrowKind.Normal
                ? Lit("Champ_Steel", new Color(0.72f, 0.74f, 0.78f), 0.7f, 0.8f)
                : Lit(kind == ArrowKind.Frost ? "Champ_IceTip" : "Champ_FireTip", tipCol, 0.6f, 0f, tipCol * 2.2f);
            var feather = Lit(kind == ArrowKind.Frost ? "Champ_FeatherIce" : (kind == ArrowKind.Fire ? "Champ_FeatherFire" : "Champ_Feather"),
                kind == ArrowKind.Normal ? new Color(0.9f, 0.88f, 0.82f) : tipCol, 0.1f);

            var mesh = new GameObject("Mesh").transform;
            mesh.SetParent(go.transform, false);
            var arrowModel = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponDir + "Arrow.fbx");
            if (arrowModel != null)
            {
                // Blender-Pfeil: Spitze +Y, Ursprung Mitte → um X drehen, damit die Spitze nach +Z (Flugrichtung) zeigt
                var model = (GameObject)PrefabUtility.InstantiatePrefab(arrowModel);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Model";
                model.transform.SetParent(mesh, false);
                model.transform.localPosition = new Vector3(0f, 0f, -0.2f);
                model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Element-Pfeile: Spitze leuchtet in der Elementfarbe (kleiner Kern an der Spitze)
                if (kind != ArrowKind.Normal) Part(mesh, "TipGlow", new Vector3(0, 0, 0.24f), new Vector3(0, 45f, 0), new Vector3(0.06f, 0.04f, 0.06f), tip);
            }
            else
            {
            Part(mesh, "Shaft", new Vector3(0, 0, -0.25f), Vector3.zero, new Vector3(0.035f, 0.035f, 0.75f), wood);
            Part(mesh, "Tip", new Vector3(0, 0, 0.16f), new Vector3(0, 45f, 0), new Vector3(0.07f, 0.04f, 0.07f), tip);
            Part(mesh, "TipPoint", new Vector3(0, 0, 0.2f), new Vector3(0, 45f, 0), new Vector3(0.045f, 0.03f, 0.045f), tip);
            Part(mesh, "FeatherA", new Vector3(0, 0.035f, -0.56f), Vector3.zero, new Vector3(0.008f, 0.06f, 0.16f), feather);
            Part(mesh, "FeatherB", new Vector3(0.03f, -0.02f, -0.56f), new Vector3(0, 0, 120f), new Vector3(0.008f, 0.06f, 0.16f), feather);
            Part(mesh, "FeatherC", new Vector3(-0.03f, -0.02f, -0.56f), new Vector3(0, 0, -120f), new Vector3(0.008f, 0.06f, 0.16f), feather);
            }

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(go.transform, false);
            trailGo.transform.localPosition = new Vector3(0, 0, -0.3f);
            var tr = trailGo.AddComponent<TrailRenderer>();
            tr.sharedMaterial = TrailAdd;
            tr.time = kind == ArrowKind.Normal ? 0.12f : 0.22f;
            tr.minVertexDistance = 0.05f;
            tr.widthMultiplier = kind == ArrowKind.Normal ? 0.09f : 0.14f;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            Color tc = kind == ArrowKind.Normal ? new Color(1f, 1f, 1f, 0.6f) : tipCol;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(tc, 0f), new GradientColorKey(tc, 1f) }, new[] { new GradientAlphaKey(tc.a, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.autodestruct = false;

            if (kind == ArrowKind.Frost)
            {
                var mist = PS(trailGo.transform, "FrostMist", Vector3.zero, SoftAdd, new Vector2(0.3f, 0.5f), new Vector2(0.15f, 0.3f), Ice, 60, true);
                var em = mist.emission; em.rateOverDistance = 12f;
                ColorLife(mist, Fade(Color.white, Color.white, 0.8f));
                var gl = PS(trailGo.transform, "Glints", Vector3.zero, StarAdd, new Vector2(0.3f, 0.5f), new Vector2(0.08f, 0.15f), new Color(0.85f, 0.95f, 1f), 30, true);
                var em2 = gl.emission; em2.rateOverDistance = 4f;
            }
            else if (kind == ArrowKind.Fire)
            {
                var fl = PS(trailGo.transform, "Flames", new Vector3(0, 0, 0.45f), FlameAdd, new Vector2(0.15f, 0.25f), new Vector2(0.18f, 0.3f), new Color(1f, 0.6f, 0.2f), 40, true);
                Rate(fl, 40f);
                ColorLife(fl, Fade(Color.white, new Color(1f, 0.25f, 0.05f)));
            }

            if (kind != ArrowKind.Fire)
            {
                var ap = go.AddComponent<ArrowProjectile>();
                ap.DetachOnEnd = trailGo.transform;
                ap.HitFxPrefab = Load("VFX_ArrowHit");
                if (kind == ArrowKind.Frost) ap.HitRadius = 0.4f;
            }
        }

        // Zielgebiet des Feuerpfeil-Regens (für Radius 1 m gebaut, wird per ScaleEffectBySize skaliert)
        private static void FireRainArea(GameObject go)
        {
            var ring = PS(go.transform, "Marker", new Vector3(0, 0.05f, 0), RingAdd, new Vector2(1.7f, 1.7f), new Vector2(2.1f, 2.1f), new Color(1f, 0.45f, 0.1f, 0.85f), 2);
            Burst(ring, 1); Flat(ring); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.08f, 0.8f));
            var glow = PS(go.transform, "Glow", new Vector3(0, 0.06f, 0), SoftAdd, new Vector2(1.7f, 1.7f), new Vector2(2.2f, 2.2f), new Color(1f, 0.35f, 0.05f, 0.45f), 2);
            Burst(glow, 1); Flat(glow); ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.1f, 0.7f));
            var em = PS(go.transform, "Embers", new Vector3(0, 0.1f, 0), SoftAdd, new Vector2(0.6f, 1f), new Vector2(0.04f, 0.08f), Fire, 60, true);
            var main = em.main; main.duration = 1.6f; main.loop = false;
            Rate(em, 30f); Ring(em, 1f, 1f); Speed(em, 0.5f, 1.5f); Gravity(em, -0.3f);
            ColorLife(em, Fade(Color.white, new Color(1f, 0.3f, 0.05f)));
            var sm = PS(go.transform, "Smoke", new Vector3(0, 0.2f, 0), SmokeAlpha, new Vector2(1f, 1.5f), new Vector2(0.4f, 0.7f), new Color(0.3f, 0.25f, 0.22f, 0.6f), 30, true);
            var sm2 = sm.main; sm2.duration = 1.6f; sm2.loop = false;
            Rate(sm, 10f); Ring(sm, 0.9f, 1f); Speed(sm, 0.3f, 0.8f); SizeLife(sm, 0.6f, 1.6f); ColorLife(sm, Fade(Color.white, Color.white, 0.7f));
        }

        private static void FireImpact(GameObject go)
        {
            var fl = PS(go.transform, "Flames", new Vector3(0, 0.15f, 0), FlameAdd, new Vector2(0.25f, 0.45f), new Vector2(0.35f, 0.6f), new Color(1f, 0.6f, 0.2f), 20);
            Burst(fl, 6); Hemisphere(fl, 0.2f); Speed(fl, 0.8f, 2f); ColorLife(fl, Fade(Color.white, new Color(1f, 0.25f, 0.05f), 1f, 0.05f, 0.5f));
            var sp = PS(go.transform, "Sparks", new Vector3(0, 0.1f, 0), SparkAdd, new Vector2(0.2f, 0.4f), new Vector2(0.04f, 0.08f), new Color(1f, 0.75f, 0.3f), 20);
            Burst(sp, 8); Hemisphere(sp, 0.1f); Speed(sp, 2.5f, 5f); Gravity(sp, 1.5f); Stretch(sp, 2.5f);
        }

        // Kleiner Frost-Ausstoß am Bogen beim Frostpfeil
        private static void FrostArrowCast(GameObject go)
        {
            var m = PS(go.transform, "Burst", Vector3.zero, SoftAdd, new Vector2(0.25f, 0.4f), new Vector2(0.2f, 0.35f), Ice, 20);
            Burst(m, 12); Cone(m, 25f, 0.1f, Vector3.zero); Speed(m, 2f, 5f); Drag(m, 3f);
            ColorLife(m, Fade(Color.white, Color.white));
            var gl = PS(go.transform, "Glints", Vector3.zero, StarAdd, new Vector2(0.3f, 0.5f), new Vector2(0.1f, 0.2f), new Color(0.85f, 0.95f, 1f), 12);
            Burst(gl, 6); Sphere(gl, 0.3f); ColorLife(gl, Fade(Color.white, Color.white));
        }

        // Dornenfalle: Ring aus Dornen + Wurzeln, grünes "scharf"-Glimmen; ThornTrap-Komponente
        private static void ThornTrapPrefab(GameObject go)
        {
            var thornMat = Lit("Champ_Thorn", new Color(0.32f, 0.22f, 0.12f), 0.15f);
            var vineMat = Lit("Champ_Vine", new Color(0.25f, 0.45f, 0.15f), 0.2f);
            var thorns = new GameObject("Thorns").transform;
            thorns.SetParent(go.transform, false);
            var trapModel = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponDir + "ThornTrap.fbx");
            if (trapModel != null)
            {
                // Blender-Falle (Radius ~0.65 m) auf den Auslöse-Radius (~2 m Optik) skalieren
                var model = (GameObject)PrefabUtility.InstantiatePrefab(trapModel);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Model";
                model.transform.SetParent(thorns, false);
                model.transform.localScale = Vector3.one * 2.6f;
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }
            const int n = 12;
            for (int i = 0; i < n && trapModel == null; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float r = 1.5f + (i % 2) * 0.25f;
                Vector3 p = new Vector3(Mathf.Sin(a) * r, 0.18f, Mathf.Cos(a) * r);
                float yaw = a * Mathf.Rad2Deg;
                var spike = Part(thorns, "Spike" + i, p, new Vector3(-28f, yaw, 0f), new Vector3(0.09f, 0.55f + (i % 3) * 0.12f, 0.09f), thornMat, PrimitiveType.Cylinder);
                spike.transform.localScale = new Vector3(0.08f, 0.28f + (i % 3) * 0.06f, 0.08f);
                Part(thorns, "Vine" + i, new Vector3(Mathf.Sin(a + 0.26f) * 1.6f, 0.05f, Mathf.Cos(a + 0.26f) * 1.6f), new Vector3(0f, yaw + 90f, 0f), new Vector3(0.12f, 0.08f, 0.85f), vineMat);
            }
            var armed = new GameObject("Armed");
            armed.transform.SetParent(go.transform, false);
            var gl = PS(armed.transform, "Glow", new Vector3(0, 0.05f, 0), RingAdd, new Vector2(1.2f, 1.2f), new Vector2(4.4f, 4.4f), new Color(0.45f, 0.9f, 0.3f, 0.45f), 4, true);
            Rate(gl, 1.6f); Flat(gl); ColorLife(gl, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var glm = gl.main; glm.simulationSpace = ParticleSystemSimulationSpace.Local;
            var lv = PS(armed.transform, "Leaves", new Vector3(0, 0.1f, 0), LeafAlpha, new Vector2(1.2f, 1.8f), new Vector2(0.1f, 0.16f), new Color(0.55f, 0.8f, 0.3f), 20, true);
            Rate(lv, 4f); Ring(lv, 1.6f); Speed(lv, 0.1f, 0.3f); Gravity(lv, -0.05f); Spin(lv, 120f);
            var trap = go.AddComponent<ThornTrap>();
            trap.SnapVisual = thorns;
            trap.ArmedIndicator = armed;
        }

        // Dornen an den Füßen festgehaltener Gegner
        private static void ThornRoot(GameObject go)
        {
            var thornMat = Lit("Champ_Thorn", new Color(0.32f, 0.22f, 0.12f), 0.15f);
            var vineMat = Lit("Champ_Vine", new Color(0.25f, 0.45f, 0.15f), 0.2f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                Vector3 p = new Vector3(Mathf.Sin(a) * 0.45f, 0.25f, Mathf.Cos(a) * 0.45f);
                Part(go.transform, "Spike" + i, p, new Vector3(25f, a * Mathf.Rad2Deg, 0f), new Vector3(0.07f, 0.28f, 0.07f), thornMat, PrimitiveType.Cylinder);
                Part(go.transform, "Vine" + i, new Vector3(Mathf.Sin(a) * 0.4f, 0.12f, Mathf.Cos(a) * 0.4f), new Vector3(0f, a * Mathf.Rad2Deg + 90f, 15f), new Vector3(0.08f, 0.06f, 0.5f), vineMat);
            }
            var lv = PS(go.transform, "Leaves", new Vector3(0, 0.3f, 0), LeafAlpha, new Vector2(0.6f, 1f), new Vector2(0.08f, 0.14f), new Color(0.5f, 0.75f, 0.3f), 12);
            Burst(lv, 8); Ring(lv, 0.4f); Speed(lv, 1f, 2f); Gravity(lv, 0.6f); Spin(lv, 200f);
        }

        private static void ThornSnap(GameObject go)
        {
            var dirt = PS(go.transform, "Dirt", new Vector3(0, 0.15f, 0), DustAlpha, new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.9f), new Color(0.5f, 0.38f, 0.25f, 0.8f), 30);
            Burst(dirt, 18); Ring(dirt, 1.4f, 0.4f); Speed(dirt, 1f, 3f); Gravity(dirt, 0.5f); SizeLife(dirt, 0.6f, 1.4f);
            ColorLife(dirt, Fade(Color.white, Color.white, 0.85f, 0.05f, 0.4f));
            var lv = PS(go.transform, "Leaves", new Vector3(0, 0.3f, 0), LeafAlpha, new Vector2(0.8f, 1.3f), new Vector2(0.12f, 0.2f), new Color(0.5f, 0.8f, 0.3f), 30);
            Burst(lv, 20); Ring(lv, 1.5f, 0.5f); Speed(lv, 2f, 4f); Gravity(lv, 0.5f); Spin(lv, 300f);
            var ring = PS(go.transform, "Ring", new Vector3(0, 0.06f, 0), RingAdd, new Vector2(0.35f, 0.35f), new Vector2(5f, 5f), new Color(0.5f, 1f, 0.35f, 0.8f), 2);
            Burst(ring, 1); Flat(ring); SizeLife(ring, 0.5f, 1.1f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.05f, 0.4f));
        }

        // Lichtpfeil-Strahl: LineRenderer (BeamFx) + Funken entlang der Bahn
        private static void LightBeam(GameObject go)
        {
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Add("VFX_Beam_Add", "vfx_soft_dot.png");
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, Vector3.forward * 10f);
            var core = new GameObject("Core");
            core.transform.SetParent(go.transform, false);
            var beam = go.AddComponent<BeamFx>();
            beam.StartWidth = 1.1f;
            var sp = PS(go.transform, "Sparkles", Vector3.zero, StarAdd, new Vector2(0.3f, 0.6f), new Vector2(0.12f, 0.25f), new Color(1f, 0.92f, 0.6f), 80);
            Burst(sp, 45); var sh = sp.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.6f, 0.6f, 10f);
            Speed(sp, 0.2f, 1f); ColorLife(sp, Fade(Color.white, Color.white));
            var mo = PS(go.transform, "Motes", Vector3.zero, SoftAdd, new Vector2(0.4f, 0.7f), new Vector2(0.06f, 0.12f), Gold, 80);
            Burst(mo, 50); var sh2 = mo.shape; sh2.enabled = true; sh2.shapeType = ParticleSystemShapeType.Box; sh2.scale = new Vector3(0.4f, 0.4f, 10f);
            Speed(mo, 0.5f, 2f); ColorLife(mo, Fade(Color.white, Color.white));
            AddLight(go.transform, new Vector3(0, 0, 1f), Gold, 4f, 6f).gameObject.AddComponent<LightFade>().Duration = 0.4f;
            Object.DestroyImmediate(core);
        }

        // ---------------- Platzhalter: Köcher ----------------

        private static void Quiver(GameObject go)
        {
            var leather = Lit("Champ_Leather", new Color(0.35f, 0.22f, 0.12f), 0.2f);
            var wood = Lit("Champ_ArrowWood", new Color(0.55f, 0.38f, 0.22f), 0.2f);
            var feather = Lit("Champ_Feather", new Color(0.9f, 0.88f, 0.82f), 0.1f);
            Part(go.transform, "Body", new Vector3(0, 0, 0), Vector3.zero, new Vector3(0.14f, 0.28f, 0.14f), leather, PrimitiveType.Cylinder);
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * 0.03f;
                Part(go.transform, "Shaft" + i, new Vector3(x, 0.34f, (i % 2) * 0.02f), Vector3.zero, new Vector3(0.012f, 0.12f, 0.012f), wood, PrimitiveType.Cylinder);
                Part(go.transform, "Fletch" + i, new Vector3(x, 0.44f, (i % 2) * 0.02f), Vector3.zero, new Vector3(0.006f, 0.08f, 0.035f), feather);
            }
        }

    }

}
