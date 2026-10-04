using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace ElementalBuddies.EditorTools
{
    // Baut die drei Bosse (Knochenfürst, Nekromant, Totenjäger) neu: Import der Blender-Accessoires
    // (Models/Bosses), Materialien, Effekt-Umfärbungen (VFX/Prefabs/Bosses), Projektile, Animator-Controller,
    // Boss-Prefabs (Prefabs/Bosses) und Configs (ScriptableObjects/Configs/Bosses).
    // Menü: BuddyTD → Bosse neu bauen. Szene (HUD-Leiste, Wellen, DevTools, Audio): BuddyTD → Bosse in Szene verdrahten.
    public static class BossBuilder
    {
        private const string ModelDir = "Assets/_Game/Models/Bosses/";
        private const string ModelMatDir = "Assets/_Game/Models/Bosses/Materials/";
        private const string PrefabDir = "Assets/_Game/Prefabs/Bosses/";
        private const string ConfigDir = "Assets/ScriptableObjects/Configs/Bosses/";
        private const string FxDir = "Assets/_Game/VFX/Prefabs/Bosses/";
        private const string FxMatDir = "Assets/_Game/VFX/Prefabs/Bosses/Materials/";
        private const string AnimDir = "Assets/_Game/Animations/Bosses/";
        private const string VfxMat = "Assets/_Game/VFX/Materials/";

        private const string RunnerPrefab = "Assets/_Game/Prefabs/Runner.prefab";
        private const string KnightPrefab = "Assets/_Game/Prefabs/Knight.prefab";
        private const string ArcherPrefab = "Assets/_Game/Prefabs/SkeletonArcher.prefab";
        private const string SkelFbx = "Assets/3D Models/Enemy/Skeletton.fbx";
        private const string MeshyEnemy = "Assets/3D Models/Enemy/Meshy_Merged_Animations.fbx";
        private const string KnightFbx = "Assets/_Game/Models/Characters/KnightChampion.fbx";
        private const string Mixamo = "Assets/MixamoAnimations/";

        public static readonly Color BoneLordColor = new Color(0.85f, 0.1f, 0.08f);
        public static readonly Color NecroColor = new Color(0.35f, 1f, 0.25f);
        public static readonly Color HunterColor = new Color(0.6f, 0.2f, 1f);

        // Visual-Raum (ungeskaliertes Skeletton.fbx): Augenhöhlen vorn am Schädel
        private static readonly Vector3 EyeL = new Vector3(-0.072f, 1.50f, 0.215f);
        private static readonly Vector3 EyeR = new Vector3(0.072f, 1.50f, 0.215f);

        [MenuItem("BuddyTD/Bosse neu bauen")]
        public static void BuildAll()
        {
            Folders();
            ImportModels();
            var fx = BuildEffects();
            BuildProjectiles(fx);
            var ctrl = BuildControllers();
            BuildBoneLord(fx, ctrl[0]);
            BuildNecromancer(fx, ctrl[1]);
            BuildDeathHunter(fx, ctrl[2]);
            AssetDatabase.SaveAssets();
            foreach (var n in new[] { "BossBoneLord", "BossNecromancer", "BossDeathHunter" })
                AssetDatabase.ImportAsset(PrefabDir + n + ".prefab", ImportAssetOptions.ForceUpdate);
            Debug.Log("BossBuilder: Knochenfürst, Nekromant und Totenjäger gebaut (" + PrefabDir + ", " + ConfigDir + ").");
        }

        private static void Folders()
        {
            Folder("Assets/_Game/Models", "Bosses");
            Folder("Assets/_Game/Models/Bosses", "Materials");
            Folder("Assets/_Game/Prefabs", "Bosses");
            Folder("Assets/ScriptableObjects/Configs", "Bosses");
            Folder("Assets/_Game/VFX/Prefabs", "Bosses");
            Folder("Assets/_Game/VFX/Prefabs/Bosses", "Materials");
            Folder("Assets/_Game/Animations", "Bosses");
        }

        private static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        // ---------------- Modelle & Materialien ----------------

        // FBX ohne Animation/Rig; eingebettete Materialien werden als URP-Lit-Materialien nach Models/Bosses/Materials
        // ausgelagert (gleiche Namen → von allen Teilen geteilt). *_Glow: Emission = Grundfarbe × 2,5, *_Inner fast schwarz.
        private static void ImportModels()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Models/Bosses" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;
                bool changed = false;
                if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
                if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; changed = true; }
                if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; changed = true; }

                var map = imp.GetExternalObjectMap();
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var em = o as Material;
                    if (em == null) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name);
                    if (map.ContainsKey(id) && map[id] != null) continue;
                    imp.AddRemap(id, ModelMaterial(em));
                    changed = true;
                }
                if (changed) imp.SaveAndReimport();
            }
        }

        private static Material ModelMaterial(Material embedded)
        {
            string p = ModelMatDir + embedded.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color baseCol = embedded.HasProperty("_BaseColor") ? embedded.GetColor("_BaseColor") : Color.white;
            baseCol.a = 1f;
            m.SetColor("_BaseColor", baseCol);
            m.SetFloat("_Smoothness", embedded.HasProperty("_Smoothness") ? Mathf.Min(0.6f, embedded.GetFloat("_Smoothness")) : 0.3f);
            m.SetFloat("_Metallic", 0f);
            if (embedded.name.EndsWith("_Glow"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", baseCol * 2.5f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else if (embedded.name.EndsWith("_Inner"))
            {
                m.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.035f));
                m.SetFloat("_Smoothness", 0f);
            }
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        // Verkohlte Knochen: Skelett-Textur, dunkel getönt
        private static Material BoneMaterial(string name, Color tint)
        {
            string p = ModelMatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Models/Materials/Enemy_ArcherBone.mat"));
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GlowMaterial(string dir, string name, Color color, float intensity)
        {
            string p = dir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", color * 0.6f);
            m.SetFloat("_Smoothness", 0.2f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- Effekte (Umfärbungen der Champion-/Zauber-Effekte) ----------------

        public class Fx
        {
            public GameObject Slam, Whirl, Nova, Cone, Heal, Beam, RainArea, RainArrow, RainImpact, OrbHit, ArrowHit;
        }

        private static Fx BuildEffects()
        {
            const string champ = "Assets/_Game/VFX/Prefabs/Champions/";
            const string vfx = "Assets/_Game/VFX/Prefabs/";
            var fx = new Fx();
            // Skalierung = Boss-Fläche / Fläche des Originals (Effekte werden mit ScaleEffectBySize = false gespawnt)
            fx.Slam = Recolor(champ + "VFX_Earthquake.prefab", "VFX_Boss_BloodQuake", BoneLordColor, 0.9f, 4.5f / 4f,
                go => Remove(go, "Grass"));
            fx.Whirl = Recolor(champ + "VFX_FlameWhirl.prefab", "VFX_Boss_BloodWhirl", BoneLordColor, 1f, 4f / 3.5f, null);
            fx.Nova = Recolor(vfx + "VFX_FrostNova.prefab", "VFX_Boss_GraveFrost", NecroColor, 1f, 5f / 6f, null);
            fx.Cone = Recolor(vfx + "VFX_FlameWave.prefab", "VFX_Boss_SoulWave", NecroColor, 1f, 8f / 7f, null);
            fx.Heal = Recolor(vfx + "VFX_HolyCircle.prefab", "VFX_Boss_DeathCircle", new Color(0.15f, 0.6f, 0.2f), 0.65f, 7f / 6f, null);
            fx.Beam = Recolor(champ + "VFX_LightBeam.prefab", "VFX_Boss_ShadowBeam", HunterColor, 1f, 1f, go =>
            {
                var b = go.GetComponent<BeamFx>();
                if (b != null) { b.StartWidth = 1.4f; b.Color = HunterColor * 1.6f; }
            });
            fx.RainArea = Recolor(champ + "VFX_FireRainArea.prefab", "VFX_Boss_ArrowHailArea", HunterColor, 1f, 1f, null);
            fx.RainImpact = Recolor(champ + "VFX_FireImpact.prefab", "VFX_Boss_ArrowHailImpact", HunterColor, 1f, 0.8f, null);
            fx.RainArrow = Recolor(champ + "FireArrow_Falling.prefab", "Boss_FallingArrow", HunterColor, 1f, 1.2f, null);
            fx.OrbHit = Recolor(vfx + "EnemyArrowHit.prefab", "VFX_NecroOrbHit", NecroColor, 1f, 1.8f, null);
            fx.ArrowHit = Recolor(vfx + "EnemyArrowHit.prefab", "VFX_HeavyArrowHit", HunterColor, 1f, 1.5f, null);
            return fx;
        }

        private static void Remove(GameObject root, string child)
        {
            var t = root.transform.Find(child);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        // Kopie eines Effekt-Prefabs, alle Farben (Partikel, Licht, Linien, Spuren, Lit-Materialien) auf den Farbton der
        // Boss-Farbe gedreht; rootScale wird in den Prefab-Maßstab eingebacken.
        private static GameObject Recolor(string src, string name, Color target, float valueMul, float rootScale, System.Action<GameObject> extra)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (prefab == null) { Debug.LogWarning("BossBuilder: Effekt fehlt " + src); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            try
            {
                go.transform.localScale = prefab.transform.localScale * rootScale;
                RecolorHierarchy(go, target, valueMul, name);
                extra?.Invoke(go);
                return PrefabUtility.SaveAsPrefabAsset(go, FxDir + name + ".prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void RecolorHierarchy(GameObject go, Color target, float valueMul, string matPrefix)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = Map(main.startColor, target, valueMul);
                var col = ps.colorOverLifetime;
                if (col.enabled) col.color = Map(col.color, target, valueMul);
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.color = MapColor(l.color, target, 1f);
            foreach (var lr in go.GetComponentsInChildren<LineRenderer>(true)) lr.colorGradient = MapGradient(lr.colorGradient, target, valueMul);
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true)) tr.colorGradient = MapGradient(tr.colorGradient, target, valueMul);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].shader.name.Contains("Lit") && !mats[i].shader.name.Contains("Unlit"))
                        mats[i] = RecolorMaterial(mats[i], target, valueMul, matPrefix);
                r.sharedMaterials = mats;
            }
        }

        private static Material RecolorMaterial(Material src, Color target, float valueMul, string prefix)
        {
            string p = FxMatDir + prefix + "_" + src.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(src);
                AssetDatabase.CreateAsset(m, p);
            }
            else m.CopyPropertiesFromMaterial(src);
            if (src.HasProperty("_BaseColor"))
            {
                Color c = src.GetColor("_BaseColor");
                // Holz/Stahl/Federn dunkel, farbige Teile (Spitze, Glut) in Boss-Farbe
                float h, s, v;
                Color.RGBToHSV(c, out h, out s, out v);
                m.SetColor("_BaseColor", s > 0.5f ? MapColor(c, target, valueMul) : new Color(c.r * 0.35f, c.g * 0.32f, c.b * 0.38f, c.a));
            }
            if (src.IsKeywordEnabled("_EMISSION")) m.SetColor("_EmissionColor", MapColor(src.GetColor("_EmissionColor"), target, valueMul));
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Color MapColor(Color c, Color target, float valueMul)
        {
            float h, s, v, th, ts, tv;
            Color.RGBToHSV(new Color(c.r, c.g, c.b), out h, out s, out v);
            Color.RGBToHSV(target, out th, out ts, out tv);
            float s2 = s < 0.12f ? Mathf.Lerp(s, ts, 0.35f) : Mathf.Lerp(s, ts, 0.6f);
            Color o = Color.HSVToRGB(th, Mathf.Clamp01(s2), v * valueMul, true);
            o.a = c.a;
            return o;
        }

        private static Gradient MapGradient(Gradient g, Color target, float valueMul)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = MapColor(keys[i].color, target, valueMul);
            var n = new Gradient { mode = g.mode };
            n.SetKeys(keys, g.alphaKeys);
            return n;
        }

        private static ParticleSystem.MinMaxGradient Map(ParticleSystem.MinMaxGradient g, Color target, float valueMul)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(MapColor(g.color, target, valueMul));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(MapColor(g.colorMin, target, valueMul), MapColor(g.colorMax, target, valueMul));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(MapGradient(g.gradient, target, valueMul));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(MapGradient(g.gradientMin, target, valueMul), MapGradient(g.gradientMax, target, valueMul));
                case ParticleSystemGradientMode.RandomColor:
                    var r = new ParticleSystem.MinMaxGradient(MapGradient(g.gradient, target, valueMul));
                    r.mode = ParticleSystemGradientMode.RandomColor;
                    return r;
            }
            return g;
        }

        // ---------------- Projektile ----------------

        private static void BuildProjectiles(Fx fx)
        {
            BuildNecroOrb(fx);
            BuildHeavyArrow(fx);
        }

        // Grüne Variante der Arkanen Kugel: Kern-Mesh mit umgefärbtem Feuerball-Shader, Partikelschweif statt VFX-Graph,
        // EnemyProjectile statt ArcaneBall-Skript (kein Rigidbody/Collider)
        private static void BuildNecroOrb(Fx fx)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/ArcaneBall.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "NecroOrb";
            try
            {
                foreach (var c in go.GetComponents<Component>())
                    if (c is MonoBehaviour) Object.DestroyImmediate(c);
                foreach (var c in go.GetComponents<Rigidbody>()) Object.DestroyImmediate(c);
                foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
                foreach (var v in go.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true)) Object.DestroyImmediate(v.gameObject);
                go.transform.localScale = Vector3.one;

                var vis = go.transform.Find("Visual");
                vis.localScale = Vector3.one * 0.17f;
                var mr = vis.GetComponent<MeshRenderer>();
                string mp = FxMatDir + "NecroOrb_Core.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null) { m = new Material(mr.sharedMaterial); AssetDatabase.CreateAsset(m, mp); }
                else m.CopyPropertiesFromMaterial(mr.sharedMaterial);
                m.SetColor("_MainTint", new Color(0.12f, 1f, 0.08f));
                m.SetColor("_CoreTint", new Color(0.7f, 1f, 0.55f));
                EditorUtility.SetDirty(m);
                mr.sharedMaterial = m;

                foreach (var l in go.GetComponentsInChildren<Light>(true)) { l.color = NecroColor; l.intensity = 2f; l.range = 5f; }

                // Schweif: Seelenrauch + Funken (Weltraum) und kurze Spur
                var soft = AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_SoftDot_Add.mat");
                var smoke = PS(go.transform, "SoulTrail", Vector3.zero, soft, new Vector2(0.35f, 0.55f), new Vector2(0.25f, 0.45f), new Color(0.35f, 1f, 0.25f, 0.8f), 60, true);
                Rate(smoke, 45f);
                var sh = smoke.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.08f;
                SizeLife(smoke, 1f, 0.1f);
                ColorLife(smoke, Fade(new Color(0.75f, 1f, 0.6f), new Color(0.05f, 0.4f, 0.05f), 1f, 0.05f, 0.4f));
                var sparks = PS(go.transform, "Wisps", Vector3.zero, AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Spark_Add.mat"), new Vector2(0.3f, 0.5f), new Vector2(0.05f, 0.09f), new Color(0.6f, 1f, 0.4f), 30, true);
                Rate(sparks, 20f);
                var sh2 = sparks.shape; sh2.enabled = true; sh2.shapeType = ParticleSystemShapeType.Sphere; sh2.radius = 0.15f;
                var m2 = sparks.main; m2.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1f);
                ColorLife(sparks, Fade(Color.white, NecroColor));

                var trailGo = new GameObject("Trail");
                trailGo.transform.SetParent(go.transform, false);
                var tr = trailGo.AddComponent<TrailRenderer>();
                tr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Trail_Add.mat");
                tr.time = 0.25f; tr.startWidth = 0.32f; tr.endWidth = 0f; tr.minVertexDistance = 0.05f;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.6f, 1f, 0.45f), 0f), new GradientColorKey(new Color(0.1f, 0.6f, 0.1f), 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;

                var proj = go.AddComponent<EnemyProjectile>();
                proj.HitEffectPrefab = fx.OrbHit;
                proj.Lifetime = 4f;
                proj.HitDistance = 0.5f;
                PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "NecroOrb.prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // Größerer, violetter Pfeil (Kopie von EnemyArrow)
        private static void BuildHeavyArrow(Fx fx)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/EnemyArrow.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "HeavyArrow";
            try
            {
                var model = Deep(go.transform, "Model");
                model.localScale = Vector3.one * 1.45f;
                model.localPosition = new Vector3(0f, 0f, -0.6f);
                var mr = model.GetComponent<MeshRenderer>();
                var mats = mr.sharedMaterials;
                mats[0] = PlainMaterial(FxMatDir + "HeavyArrow_Shaft.mat", new Color(0.12f, 0.1f, 0.14f), 0.3f, null);
                mats[1] = PlainMaterial(FxMatDir + "HeavyArrow_Tip.mat", new Color(0.55f, 0.2f, 0.95f), 0.5f, HunterColor * 2.5f);
                if (mats.Length > 2) mats[2] = PlainMaterial(FxMatDir + "HeavyArrow_Feather.mat", new Color(0.22f, 0.08f, 0.32f), 0.1f, null);
                mr.sharedMaterials = mats;

                var trail = Deep(go.transform, "Trail");
                if (trail != null)
                {
                    trail.localPosition = new Vector3(0f, 0f, -0.9f);
                    var tr = trail.GetComponent<TrailRenderer>();
                    tr.startWidth = 0.22f; tr.time = 0.3f;
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(new Color(0.8f, 0.5f, 1f), 0f), new GradientColorKey(new Color(0.35f, 0.08f, 0.6f), 1f) },
                        new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                    tr.colorGradient = g;
                }
                var tip = Deep(go.transform, "TipGlow");
                if (tip == null)
                {
                    var l = new GameObject("TipGlow");
                    l.transform.SetParent(go.transform, false);
                    l.transform.localPosition = new Vector3(0f, 0f, 0.3f);
                    var light = l.AddComponent<Light>();
                    light.type = LightType.Point; light.color = HunterColor; light.intensity = 1.5f; light.range = 3f; light.shadows = LightShadows.None;
                }
                var proj = go.GetComponent<EnemyProjectile>();
                proj.HitEffectPrefab = fx.ArrowHit;
                proj.HitDistance = 0.5f;
                PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "HeavyArrow.prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static Transform Deep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static Material PlainMaterial(string p, Color c, float smooth, Color? emission)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
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

        // ---------------- Animator ----------------

        private static AnimatorController[] BuildControllers()
        {
            var walk = LoopedCopy(Clip(SkelFbx, "Walking"), AnimDir + "Boss_Walking_Loop.anim");
            var run = Clip(SkelFbx, "Running");
            // Idle: Elementar-Idle mit Händen seitlich vor dem Körper (Stab/Schwert bleiben sichtbar, StarterAssets-Idle hält sie hinter der Robe)
            var idle = Clip("Assets/_Game/Models/Characters/LightningElemental.fbx", "Lightning_Idle");
            var archerIdle = Clip(MeshyEnemy, "Archery_Shot");
            var shot = Clip(MeshyEnemy, "Archery_Shot_3");

            // Knochenfürst: schwerer Gang, Hieb, Sprung-Stampfer (Landung bei ~1,5 s = Windup 1,0 + Sprung 0,45), Drehschlag
            var bone = NewController("BossBoneLordVisual", walk, 0.55f, idle, "Attack", "Slam", "Whirl");
            Action(bone, "Attack", Clip(Mixamo + "X Bot@Stable Sword Outward Slash (1).fbx", null), 1.6f, 0.62f);
            Action(bone, "Slam", Clip(KnightFbx, "Knight_Earthquake"), 0.4f, 0.8f);
            Action(bone, "Whirl", Clip(KnightFbx, "Knight_FlameWhirl"), 0.76f, 0.85f);

            // Nekromant: Grund-Zauber (Magma_Cast, Stoß nach vorn bei ~0,4 s) und Fähigkeiten-Zauber (Shadow_Cast, Arme hoch,
            // Niederschlag bei ~0,8 s)
            var necro = NewController("BossNecromancerVisual", walk, 0.5f, idle, "Attack", "Cast");
            Action(necro, "Attack", Clip("Assets/_Game/Models/Characters/MagmaElemental.fbx", "Magma_Cast"), 1f, 0.8f);
            Action(necro, "Cast", Clip("Assets/_Game/Models/Characters/ShadowElemental.fbx", "Shadow_Cast"), 1.25f, 0.88f);

            // Totenjäger: wie der Skelett-Bogenschütze; Fähigkeiten-Schuss langsam (Loslassen nach ~1 s)
            var hunter = NewController("BossDeathHunterVisual", run, 0.33f, archerIdle, "Attack", "Shoot");
            foreach (var s in hunter.layers[0].stateMachine.states)
                if (s.state.name == "Idle") s.state.speed = 0f;
            Action(hunter, "Attack", shot, 1f, 0.9f);
            Action(hunter, "Shoot", shot, 0.36f, 0.95f);
            foreach (var c in new[] { bone, necro, hunter }) EditorUtility.SetDirty(c);
            return new[] { bone, necro, hunter };
        }

        private static AnimationClip LoopedCopy(AnimationClip src, string path)
        {
            var c = Object.Instantiate(src);
            c.name = System.IO.Path.GetFileNameWithoutExtension(path);
            var s = AnimationUtility.GetAnimationClipSettings(c);
            s.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(c, s);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) { AssetDatabase.CreateAsset(c, path); return c; }
            EditorUtility.CopySerialized(c, existing);
            Object.DestroyImmediate(c);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // Base Layer: Run (Tempo × Speed-Parameter) ↔ Idle über Moving; Aktionen per Any-State-Trigger
        private static AnimatorController NewController(string name, AnimationClip move, float moveSpeedPerMeter, AnimationClip idle, params string[] triggers)
        {
            // Bestehenden Controller leeren statt löschen (GUID bleibt, Prefab-Referenzen bleiben gültig)
            string path = AnimDir + name + ".controller";
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var old = ac.layers[0].stateMachine;
            foreach (var t in old.anyStateTransitions) old.RemoveAnyStateTransition(t);
            foreach (var s in old.states) old.RemoveState(s.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            ac.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            foreach (var t in triggers) ac.AddParameter(t, AnimatorControllerParameterType.Trigger);
            var sm = ac.layers[0].stateMachine;
            var run = sm.AddState("Run", new Vector3(300, 0, 0));
            run.motion = move;
            run.speed = moveSpeedPerMeter;
            run.speedParameterActive = true;
            run.speedParameter = "Speed";
            var idleS = sm.AddState("Idle", new Vector3(300, 120, 0));
            idleS.motion = idle;
            sm.defaultState = idleS;
            var toIdle = run.AddTransition(idleS);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            toIdle.hasExitTime = false; toIdle.duration = 0.15f;
            var toRun = idleS.AddTransition(run);
            toRun.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            toRun.hasExitTime = false; toRun.duration = 0.15f;
            return ac;
        }

        private static void Action(AnimatorController ac, string trigger, AnimationClip clip, float speed, float exitTime)
        {
            var sm = ac.layers[0].stateMachine;
            AnimatorState run = null, idle = null;
            foreach (var s in sm.states)
            {
                if (s.state.name == "Run") run = s.state;
                if (s.state.name == "Idle") idle = s.state;
            }
            var st = sm.AddState(trigger, new Vector3(600, 60 * sm.states.Length, 0));
            st.motion = clip;
            st.speed = speed;
            var any = sm.AddAnyStateTransition(st);
            any.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            any.duration = 0.08f;
            any.canTransitionToSelf = true;
            var a = st.AddTransition(run);
            a.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            a.hasExitTime = true; a.exitTime = exitTime; a.duration = 0.2f;
            var b = st.AddTransition(idle);
            b.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            b.hasExitTime = true; b.exitTime = exitTime; b.duration = 0.2f;
            if (clip == null) Debug.LogWarning("BossBuilder: kein Clip für " + ac.name + "/" + trigger);
        }

        private static AnimationClip Clip(string path, string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var c = o as AnimationClip;
                if (c == null || c.name.StartsWith("__preview")) continue;
                if (name == null || c.name == name) return c;
            }
            return null;
        }

        // ---------------- Boss-Prefabs ----------------

        // Rohbau aus dem Runner: Tag/Layer/Agent/Collider/EnemyBrain/HP-Leiste übernommen, Knochen verkohlt, Augen, Aura
        private class Rig
        {
            public GameObject Root;
            public Transform Visual;
            public SkinnedMeshRenderer Body;
            public Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
            public Dictionary<string, Matrix4x4> Bind = new Dictionary<string, Matrix4x4>();
        }

        private static Rig NewRig(string name, AnimatorController ctrl, Material bone, Color theme, string prefix)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.position = Vector3.zero;
            var rig = new Rig { Root = go, Visual = go.transform.Find("Visual") };
            rig.Body = go.GetComponentInChildren<SkinnedMeshRenderer>();
            rig.Body.sharedMaterial = bone;

            var anim = rig.Visual.GetComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Bind-Pose der Knochen im Visual-Raum (aus den Bindposes des Skinned Mesh)
            Matrix4x4 smrInVis = rig.Visual.worldToLocalMatrix * rig.Body.transform.localToWorldMatrix;
            var bp = rig.Body.sharedMesh.bindposes;
            for (int i = 0; i < rig.Body.bones.Length; i++)
            {
                var b = rig.Body.bones[i];
                if (b == null) continue;
                rig.Bones[b.name] = b;
                rig.Bind[b.name] = smrInVis * bp[i].inverse;
            }

            // Agent: schmaler als die Optik (×2), damit der Boss durch die Gassen kommt
            var agent = go.GetComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 2f;
            agent.baseOffset = 1f;
            agent.avoidancePriority = 30;
            var col = go.GetComponent<CapsuleCollider>();
            col.radius = 0.4f;

            // Leuchtende Augen
            var eyeMat = GlowMaterial(ModelMatDir, prefix + "_Eye", theme, 2.2f);
            var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            int k = 0;
            foreach (var p in new[] { EyeL, EyeR })
            {
                var e = Piece(rig, "Head", prefix + "_Eye" + k++, sphere, new[] { eyeMat }, p - BindPos(rig, "Head"), 0.075f);
                e.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Aura(go.transform, theme);
            return rig;
        }

        private static Vector3 BindPos(Rig rig, string bone) => rig.Bind[bone].GetColumn(3);

        // Teil an einen Knochen hängen: Position = Kopf des Knochens in der Bind-Pose + Versatz (Visual-Raum),
        // Ausrichtung = Visual-Raum (Mesh-Achsen wie der Charakter), Maßstab = Visual (Knochen haben Maßstab 1)
        private static Transform Piece(Rig rig, string bone, string name, Mesh mesh, Material[] mats, Vector3 offset, float scale = 1f, Quaternion? boneLocalRot = null)
        {
            var b = rig.Bones[bone];
            Matrix4x4 bind = rig.Bind[bone];
            var go = new GameObject(name);
            go.transform.SetParent(b, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            Vector3 posVis = (Vector3)bind.GetColumn(3) + offset;
            go.transform.localPosition = bind.inverse.MultiplyPoint3x4(posVis);
            go.transform.localRotation = boneLocalRot ?? Quaternion.Inverse(bind.rotation);
            go.transform.localScale = Vector3.one * scale;
            return go.transform;
        }

        private static Transform Model(Rig rig, string bone, string fbx, Vector3 offset, float scale = 1f, Quaternion? boneLocalRot = null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + fbx + ".fbx");
            var mf = model.GetComponentInChildren<MeshFilter>();
            var mr = model.GetComponentInChildren<MeshRenderer>();
            return Piece(rig, bone, fbx, mf.sharedMesh, mr.sharedMaterials, offset, scale, boneLocalRot);
        }

        // Wie Model, aber mit Ausrichtung im Charakter-Raum (Bind-Pose)
        private static Transform ModelRoot(Rig rig, string bone, string fbx, Vector3 offset, Quaternion rootRotation, float scale)
        {
            var t = Model(rig, bone, fbx, offset, scale);
            t.localRotation = Quaternion.Inverse(rig.Bind[bone].rotation) * rootRotation;
            return t;
        }

        // Lokale Rotation eines Teils am Knochen eines bestehenden Prefabs (Waffenhaltung übernehmen)
        private static Quaternion HeldRotation(string prefabPath, string child)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            foreach (var t in p.GetComponentsInChildren<Transform>(true))
                if (t.name == child) return t.localRotation;
            return Quaternion.identity;
        }

        // Dezente Boden-Aura (Schleife, wenige Partikel): pulsierender Ring + aufsteigende Funken in der Boss-Farbe
        private static void Aura(Transform root, Color theme)
        {
            var go = new GameObject("BossAura");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, -0.97f, 0f); // Pivot liegt baseOffset (1) über dem Boden
            var ring = AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_Ring_Add.mat");
            var soft = AssetDatabase.LoadAssetAtPath<Material>(VfxMat + "VFX_SoftDot_Add.mat");

            var glow = PS(go.transform, "Glow", Vector3.zero, soft, new Vector2(2.4f, 2.4f), new Vector2(1.7f, 1.7f), new Color(theme.r, theme.g, theme.b, 0.35f), 4, true);
            Rate(glow, 1f);
            Flat(glow);
            var gm = glow.main; gm.simulationSpace = ParticleSystemSimulationSpace.Local; gm.prewarm = true;
            ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.3f, 0.7f));

            var pulse = PS(go.transform, "Ring", new Vector3(0f, 0.02f, 0f), ring, new Vector2(1.6f, 1.6f), new Vector2(1.1f, 1.1f), new Color(theme.r, theme.g, theme.b, 0.6f), 4, true);
            Rate(pulse, 0.9f);
            Flat(pulse);
            var pm = pulse.main; pm.simulationSpace = ParticleSystemSimulationSpace.Local; pm.prewarm = true;
            SizeLife(pulse, 0.55f, 1.25f);
            ColorLife(pulse, Fade(Color.white, Color.white, 1f, 0.15f, 0.5f));

            var motes = PS(go.transform, "Motes", new Vector3(0f, 0.05f, 0f), soft, new Vector2(1f, 1.6f), new Vector2(0.05f, 0.1f), theme, 16, true);
            Rate(motes, 7f);
            var sh = motes.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.55f; sh.radiusThickness = 0.3f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var mm = motes.main; mm.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.6f); mm.simulationSpace = ParticleSystemSimulationSpace.World;
            ColorLife(motes, Fade(Color.white, Color.white, 1f, 0.2f, 0.5f));
        }

        private static GameObject SaveBoss(Rig rig, EnemyConfigSO config, System.Action<BossBrain> abilities, float meleeRange, float beamHeight, float playerAggro)
        {
            var go = rig.Root;
            try
            {
                var brain = go.GetComponent<EnemyBrain>();
                brain.Config = config;
                brain.AttackRange = meleeRange;
                // Fernkampf-Bosse greifen den Spieler schon aus ihrer Schussweite an (Standard 6 m < Reichweite)
                brain.PlayerAggroRadius = playerAggro;
                brain.NexusAttackRange = Mathf.Max(brain.NexusAttackRange, meleeRange + 0.5f);
                var boss = go.GetComponent<BossBrain>();
                if (boss == null) boss = go.AddComponent<BossBrain>();
                boss.Abilities = new List<BossAbility>();
                boss.GlobalCooldown = 1.5f;
                boss.BeamHeight = beamHeight;
                abilities(boss);
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + go.name + ".prefab");
                config.Prefab = prefab;
                EditorUtility.SetDirty(config);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static EnemyConfigSO Config(string name)
        {
            string p = ConfigDir + name + ".asset";
            var c = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(p);
            if (c == null)
            {
                c = ScriptableObject.CreateInstance<EnemyConfigSO>();
                AssetDatabase.CreateAsset(c, p);
            }
            c.Type = UnitType.EnemyRunner;
            c.IsBoss = true;
            c.ControlResistance = 0.6f;
            c.BountyMultiplier = 20f;
            c.HuntsBuddies = false;
            c.BuddyDamageMultiplier = 1f;
            return c;
        }

        private static BossAbility Ability(string name, BossAbilityKind kind, float cd, float windup, string trigger, float triggerRange, float damage, SfxId sfx)
        {
            return new BossAbility
            {
                Name = name, Kind = kind, Cooldown = cd, Windup = windup, AnimatorTrigger = trigger,
                TriggerRange = triggerRange, Damage = damage, ImpactSfx = sfx, Recovery = 0.5f,
                ScaleEffectBySize = false, EffectLifetime = 3f
            };
        }

        // ---------------- Knochenfürst ----------------

        private static void BuildBoneLord(Fx fx, AnimatorController ctrl)
        {
            var c = Config("BossBoneLord");
            c.DisplayName = "Knochenfürst";
            c.ThemeColor = BoneLordColor;
            c.BaseHP = 1400f; c.HpBonusMultiplier = 6f; c.Speed = 2f; c.AttackDamage = 20f; c.Armor = 0.4f;
            c.VisualScale = 2f; c.BuddyAggroRadius = 6f; c.AttackInterval = 1.6f;
            c.AttackRange = 0f; c.ProjectilePrefab = null;

            var rig = NewRig("BossBoneLord", ctrl, BoneMaterial("Boss_BoneLord_Bone", new Color(0.33f, 0.29f, 0.29f)), BoneLordColor, "BoneLord");
            Model(rig, "Head", "Boss_Knight_Helm", Vector3.zero);
            Model(rig, "LeftArm", "Boss_Knight_Pauldron_L", Vector3.zero);
            Model(rig, "RightArm", "Boss_Knight_Pauldron_R", Vector3.zero);
            Model(rig, "Hips", "Boss_Knight_Tabard", Vector3.zero);
            ModelRoot(rig, "RightHand", "Boss_Knight_Greatsword", GreatswordOffset, GreatswordRootRotation, 0.92f);

            SaveBoss(rig, c, b =>
            {
                var slam = Ability("Grabbeben", BossAbilityKind.Slam, 9f, 1f, "Slam", 10f, 45f, SfxId.StoneWall);
                slam.LeapDistance = 6f; slam.LeapDuration = 0.45f; slam.LeapHeight = 1.5f; slam.Radius = 4.5f;
                slam.BuddyStunDuration = 1.5f; slam.ImpactEffectPrefab = fx.Slam;
                var whirl = Ability("Blutwirbel", BossAbilityKind.Whirl, 7f, 0.8f, "Whirl", 4f, 35f, SfxId.FireWave);
                whirl.Radius = 4f; whirl.PlayerKnockback = 1.5f; whirl.ImpactEffectPrefab = fx.Whirl;
                whirl.ArcFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/VFX/Prefabs/Champions/VFX_SwordSlash.prefab");
                b.Abilities.Add(slam);
                b.Abilities.Add(whirl);
            }, 2.6f, 0.2f, 7f);
        }

        // Griff in der Faust; in der T-Pose zeigt die Klinge nach vorn-unten (180° um Z, dann 65° nach vorn gekippt):
        // im Gang schräg vor dem Körper, beim Hieb (Outward Slash) waagerecht nach außen
        public static Vector3 GreatswordOffset = new Vector3(0.114f, -0.021f, 0.061f);
        public static Quaternion GreatswordRootRotation = Quaternion.Euler(-65f, 0f, 0f) * Quaternion.Euler(0f, 0f, 180f);

        // ---------------- Nekromant ----------------

        private static void BuildNecromancer(Fx fx, AnimatorController ctrl)
        {
            var c = Config("BossNecromancer");
            c.DisplayName = "Nekromant";
            c.ThemeColor = NecroColor;
            c.BaseHP = 900f; c.HpBonusMultiplier = 5f; c.Speed = 2.2f; c.AttackDamage = 18f; c.Armor = 0.15f;
            c.VisualScale = 1.9f; c.BuddyAggroRadius = 13f;
            c.AttackRange = 11f; c.AttackInterval = 2f; c.AttackWindup = 0.4f; c.ProjectileSpeed = 12f;
            c.ProjectileSpawnHeight = NecroOrbHeight;
            c.ProjectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "NecroOrb.prefab");

            var rig = NewRig("BossNecromancer", ctrl, BoneMaterial("Boss_Necro_Bone", new Color(0.3f, 0.32f, 0.29f)), NecroColor, "Necro");
            Model(rig, "Head", "Boss_Mage_Hood", Vector3.zero);
            Model(rig, "Spine", "Boss_Mage_Collar", Vector3.zero);
            Model(rig, "Hips", "Boss_Mage_Robe", Vector3.zero);
            Model(rig, "RightHand", "Boss_Mage_Staff", StaffOffset, 1f, StaffHandRotation);

            SaveBoss(rig, c, b =>
            {
                var nova = Ability("Grabfrost", BossAbilityKind.Nova, 11f, 0.9f, "Cast", 5f, 25f, SfxId.FrostNova);
                nova.Radius = 5f; nova.PlayerSlow = 0.4f; nova.PlayerSlowDuration = 2.5f; nova.BuddyStunDuration = 2f;
                nova.ImpactEffectPrefab = fx.Nova;
                var cone = Ability("Seelenwelle", BossAbilityKind.Cone, 8f, 0.7f, "Cast", 8f, 35f, SfxId.FireWave);
                cone.Length = 8f; cone.ConeAngle = 60f; cone.ImpactEffectPrefab = fx.Cone;
                var heal = Ability("Totenkreis", BossAbilityKind.AllyHeal, 14f, 0.8f, "Cast", 7f, 0f, SfxId.HolyCircle);
                heal.Radius = 7f; heal.HealPercent = 0.2f; heal.SelfHealPercent = 0.05f; heal.HealThreshold = 0.8f; heal.MinAllies = 2;
                heal.ImpactEffectPrefab = fx.Heal;
                b.Abilities.Add(nova);
                b.Abilities.Add(cone);
                b.Abilities.Add(heal);
            }, 1.5f, 0.6f, 10f);
        }

        // Stab-Rotation im RightHand-Knochenraum: gemittelt über Idle-, Gang- und Zauber-Startposen (Stab dort 12–46° aus
        // der Senkrechten statt bis 97° bei Charakter-Ausrichtung in der T-Pose). Projektil startet 0,6 m über dem Pivot (~2,5 m, Stabkopf-Höhe).
        public static Vector3 StaffOffset = new Vector3(0.114f, -0.021f, 0.061f);
        public static Quaternion StaffHandRotation = new Quaternion(-0.7466f, 0.1273f, 0.6306f, 0.1693f);
        public static float NecroOrbHeight = 0.6f;

        // ---------------- Totenjäger ----------------

        private static void BuildDeathHunter(Fx fx, AnimatorController ctrl)
        {
            var c = Config("BossDeathHunter");
            c.DisplayName = "Totenjäger";
            c.ThemeColor = HunterColor;
            c.BaseHP = 800f; c.HpBonusMultiplier = 5f; c.Speed = 2.3f; c.AttackDamage = 14f; c.Armor = 0.1f;
            c.VisualScale = 1.9f; c.BuddyAggroRadius = 16f;
            c.AttackRange = 14f; c.AttackInterval = 1.8f; c.AttackWindup = 0.35f; c.ProjectileSpeed = 20f;
            c.ProjectileSpawnHeight = HunterArrowHeight;
            c.ProjectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "HeavyArrow.prefab");

            var rig = NewRig("BossDeathHunter", ctrl, BoneMaterial("Boss_Hunter_Bone", new Color(0.31f, 0.29f, 0.34f)), HunterColor, "Hunter");
            Model(rig, "Head", "Boss_Archer_Hood", Vector3.zero);
            Model(rig, "Spine", "Boss_Archer_Quiver", Vector3.zero);
            Model(rig, "LeftForeArm", "Boss_Archer_Bracer_L", Vector3.zero);
            Model(rig, "RightForeArm", "Boss_Archer_Bracer_R", Vector3.zero);
            Model(rig, "LeftHand", "Boss_Archer_Longbow", BowOffset, BowScale, HeldRotation(ArcherPrefab, "Archer_Bow"));

            SaveBoss(rig, c, b =>
            {
                var beam = Ability("Schattenpfeil", BossAbilityKind.Beam, 12f, 1f, "Shoot", 18f, 60f, SfxId.ArcaneBallCast);
                beam.Length = 20f; beam.Width = 1.4f; beam.PlayerSlow = 0.5f; beam.PlayerSlowDuration = 2f;
                beam.BeamPrefab = fx.Beam; beam.ImpactEffectPrefab = null;
                var rain = Ability("Pfeilhagel", BossAbilityKind.Rain, 9f, 1f, "Shoot", 16f, 10f, SfxId.ArcaneBallHit);
                rain.Radius = 3.5f; rain.MaxCastRange = 16f; rain.Pulses = 5; rain.PulseDuration = 1.25f; rain.ArrowsPerPulse = 4;
                rain.FallingArrowPrefab = fx.RainArrow; rain.PulseEffectPrefab = fx.RainImpact;
                rain.ImpactEffectPrefab = fx.RainArea; rain.ScaleEffectBySize = true; rain.EffectLifetime = 2f;
                b.Abilities.Add(beam);
                b.Abilities.Add(rain);
            }, 1.5f, HunterArrowHeight, 12f);
        }

        public static Vector3 BowOffset = new Vector3(-0.115f, -0.025f, 0.062f);
        public static float BowScale = 0.9f;
        // Bogengriff beim Schuss ~0,35 m unter dem Pivot (Pivot = Boden + baseOffset × 1,9)
        public static float HunterArrowHeight = -0.3f;

        // ---------------- Szene ----------------

        [MenuItem("BuddyTD/Bosse in Szene verdrahten")]
        public static void WireScene()
        {
            var bone = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + "BossBoneLord.asset");
            var necro = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + "BossNecromancer.asset");
            var hunter = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + "BossDeathHunter.asset");
            if (bone == null || necro == null || hunter == null) { Debug.LogError("BossBuilder: zuerst „Bosse neu bauen“."); return; }

            // Wellen: ab 6/10/14, jede 12. Welle → alle 4 Wellen ein Boss
            var wm = Object.FindFirstObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (wm != null)
            {
                Undo.RecordObject(wm, "Boss-Wellen");
                wm.ExtraGroups.RemoveAll(g => g == null || g.Config == bone || g.Config == necro || g.Config == hunter);
                wm.ExtraGroups.Add(new ExtraEnemyGroup { Config = bone, StartWave = 6, BaseCount = 1, PerWave = 0f, SpawnInterval = 1f, EveryNthWave = 12 });
                wm.ExtraGroups.Add(new ExtraEnemyGroup { Config = necro, StartWave = 10, BaseCount = 1, PerWave = 0f, SpawnInterval = 1f, EveryNthWave = 12 });
                wm.ExtraGroups.Add(new ExtraEnemyGroup { Config = hunter, StartWave = 14, BaseCount = 1, PerWave = 0f, SpawnInterval = 1f, EveryNthWave = 12 });
                EditorUtility.SetDirty(wm);
            }

            var dev = Object.FindFirstObjectByType<DevTools>(FindObjectsInactive.Include);
            if (dev != null)
            {
                Undo.RecordObject(dev, "Dev-Bosse");
                dev.DevBossConfigs = new List<EnemyConfigSO> { bone, necro, hunter };
                EditorUtility.SetDirty(dev);
            }

            var audio = Object.FindFirstObjectByType<GameAudio>(FindObjectsInactive.Include);
            if (audio != null)
            {
                Undo.RecordObject(audio, "Boss-Sound");
                audio.Entries.RemoveAll(e => e != null && e.Id == SfxId.BossSpawn);
                audio.Entries.Add(new GameAudio.SfxEntry
                {
                    Id = SfxId.BossSpawn,
                    Clips = new[] { AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Game/Audio/battleshout.wav") },
                    Volume = 0.9f,
                    Pitch = new Vector2(0.55f, 0.6f),
                    MinInterval = 1f
                });
                EditorUtility.SetDirty(audio);
            }

            var gs = AssetDatabase.LoadAssetAtPath<GlobalSettingsSO>("Assets/ScriptableObjects/Configs/GlobalSettings.asset");
            if (gs != null && gs.BuddyStunEffectPrefab == null)
            {
                gs.BuddyStunEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/VFX/Prefabs/VFX_Blinded.prefab");
                EditorUtility.SetDirty(gs);
            }

            BuildBossBar();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("BossBuilder: Wellen, DevTools, Boss-Sound und Boss-HP-Leiste verdrahtet.");
        }

        // Boss-HP-Leiste: oben mittig unter der Toast-Zeile (Toast „… ist erschienen!“ verdeckt sie sonst beim Spawn)
        private static void BuildBossBar()
        {
            var canvas = GameObject.Find("Canvas");
            if (canvas == null) { Debug.LogError("BossBuilder: kein Canvas"); return; }
            var old = canvas.transform.Find("BossHealthBar");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var host = UI("BossHealthBar", canvas.transform);
            Undo.RegisterCreatedObjectUndo(host.gameObject, "Boss-HP-Leiste");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = host.offsetMax = Vector2.zero;
            var toast = canvas.transform.Find("Toast");
            host.SetSiblingIndex(toast != null ? toast.GetSiblingIndex() : canvas.transform.childCount - 1);

            var panel = UI("Panel", host);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -222f);
            panel.sizeDelta = new Vector2(560f, 92f);

            var frame = Img("Frame", panel, "panel_wood.png", Image.Type.Sliced, Color.white);
            Stretch(frame.rectTransform, 0f, 0f, 0f, 0f);
            frame.pixelsPerUnitMultiplier = 2.2f;

            var name = new GameObject("Name", typeof(RectTransform)).GetComponent<RectTransform>();
            name.SetParent(panel, false);
            name.anchorMin = new Vector2(0f, 1f); name.anchorMax = new Vector2(1f, 1f); name.pivot = new Vector2(0.5f, 1f);
            name.anchoredPosition = new Vector2(0f, -8f); name.sizeDelta = new Vector2(-40f, 36f);
            var tmp = name.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FindAsset("MedievalSharp SDF", "t:TMP_FontAsset"));
            var outline = AssetDatabase.LoadAssetAtPath<Material>(FindAsset("MedievalSharp Outline", "t:Material"));
            if (outline != null) tmp.fontSharedMaterial = outline;
            tmp.fontSize = 30; tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = new Color(0.937f, 0.878f, 0.741f);
            tmp.text = "Knochenfürst";
            tmp.raycastTarget = false;

            var bar = UI("Bar", panel);
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(1f, 0f); bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(0f, 14f); bar.sizeDelta = new Vector2(-40f, 34f);
            var bg = Img("Background", bar, "bar_bg.png", Image.Type.Sliced, Color.white);
            Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);
            var chip = Img("DamageChip", bar, "bar_fill.png", Image.Type.Sliced, new Color(1f, 0.85f, 0.6f, 0.9f));
            Stretch(chip.rectTransform, 4f, 4f, 4f, 4f);
            chip.rectTransform.anchorMax = new Vector2(1f, 1f);
            var fill = Img("Fill", bar, "bar_fill.png", Image.Type.Filled, new Color(0.75f, 0.1f, 0.08f));
            fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0; fill.fillAmount = 1f;
            Stretch(fill.rectTransform, 4f, 4f, 4f, 4f);

            var ui = host.gameObject.AddComponent<BossHealthBarUI>();
            ui.Root = panel.gameObject;
            ui.NameText = tmp;
            ui.Fill = fill;
            ui.DamageChip = chip.rectTransform;
            ui.Frame = frame;
            ui.TintFill = false;
            panel.gameObject.SetActive(false);
        }

        private static string FindAsset(string name, string filter)
        {
            foreach (var g in AssetDatabase.FindAssets(name + " " + filter))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return p;
            }
            return null;
        }

        private static RectTransform UI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image Img(string name, Transform parent, string sprite, Image.Type type, Color color)
        {
            var rt = UI(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Game/UI/Sprites/" + sprite);
            img.type = type;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform rt, float l, float r, float t, float b)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        // ---------------- Partikel-Helfer (wie ChampionFxBuilder) ----------------

        private static ParticleSystem PS(Transform parent, string name, Vector3 pos, Material mat, Vector2 life, Vector2 size, Color color, int max, bool loop)
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
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static void Rate(ParticleSystem ps, float rate) { var em = ps.emission; em.rateOverTime = rate; }

        private static void Flat(ParticleSystem ps)
        {
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        private static void SizeLife(ParticleSystem ps, float a, float b)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, a, 1f, b));
        }

        private static void ColorLife(ParticleSystem ps, Gradient g) { var c = ps.colorOverLifetime; c.enabled = true; c.color = g; }

        private static Gradient Fade(Color a, Color b, float peak = 1f, float inAt = 0.1f, float outAt = 0.6f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inAt), new GradientAlphaKey(peak, outAt), new GradientAlphaKey(0f, 1f) });
            return g;
        }
    }
}
