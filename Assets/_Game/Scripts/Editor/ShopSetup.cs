using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Erzeugt den Inhalt des Skin-Shops (Plan: wiki/Design/Shop-Plan.md). Menü: BuddyTD → Shop → Katalog erzeugen.
    // Idempotent: vorhandene, hier erzeugte Assets werden an Ort und Stelle überschrieben (GUIDs/Ids bleiben stabil).
    // Ausgabe unter Assets/_Game/Shop/:
    //   Materials/Skins/  Kopien der Heroes2-Champion-Materialien mit neuen Farben (8 Rüstungs-Sätze)
    //   Fx/               Partikel-Effekte (Shuriken, mobilfreundlich: wenige Partikel, keine Lichter, Add/Alpha-Materialien aus VFX/)
    //   Prefabs/          Waffen/Zusatzteile mit Effekt (Variante des FBX: Ursprung/Achsen/„Grip2“ unverändert, Effekt am FxAnchor)
    //   Items/            20 ShopItemSO (je Champion 2 Waffen + 2 Rüstungen)
    //   Icons/            256²-Icons (Editor-Render, transparenter Hintergrund)
    // Katalog: Assets/_Game/Resources/ShopCatalog.asset
    public static class ShopSetup
    {
        public const string Root = "Assets/_Game/Shop/";
        public const string MatDir = Root + "Materials/Skins/";
        public const string FxDir = Root + "Fx/";
        public const string PrefabDir = Root + "Prefabs/";
        public const string ItemDir = Root + "Items/";
        public const string IconDir = Root + "Icons/";
        public const string CatalogPath = "Assets/_Game/Resources/ShopCatalog.asset";

        private const string SrcMat = "Assets/_Game/Models/Characters/Heroes2/Materials/";
        private const string ModelDir = ShopItemImport.Dir;
        private const string VfxMat = "Assets/_Game/VFX/Materials/";

        // Lage der Schulterplatten (in Unity nachgemessen; rechte Seite, links = Spiegelung an der YZ-Ebene des Knochens)
        private static readonly Quaternion PauldronRot = new Quaternion(0.47197f, 0.52487f, 0.45955f, 0.53905f);

        [MenuItem("BuddyTD/Shop/Katalog erzeugen")]
        public static void GenerateMenu()
        {
            Debug.Log(Generate(true));
        }

        [MenuItem("BuddyTD/Shop/Icons neu rendern")]
        public static void RenderIconsMenu()
        {
            Debug.Log(RenderIcons());
        }

        // Nur Items ohne Icon-Datei (neue Skins) – vorhandene Icons bleiben unangetastet
        [MenuItem("BuddyTD/Shop/Fehlende Icons rendern")]
        public static void RenderMissingIconsMenu()
        {
            Debug.Log(RenderIcons(true));
        }

        // Alles erzeugen; icons = false überspringt das (langsamere) Rendern der Icons
        public static string Generate(bool icons)
        {
            var log = new System.Text.StringBuilder("ShopSetup:\n");
            EnsureFolders();
            var mats = BuildMaterials();
            log.AppendLine("Materialien: " + mats);
            var fx = BuildFx();
            log.AppendLine("Effekte: " + fx.Count);
            var prefabs = BuildPrefabs(fx);
            log.AppendLine("Prefabs: " + prefabs.Count);
            var items = BuildItems(prefabs);
            log.AppendLine("Items: " + items.Count);
            BuildCatalog(items);
            AssetDatabase.SaveAssets();
            if (icons) log.Append(RenderIcons());
            return log.ToString();
        }

        private static void EnsureFolders()
        {
            Folder("Assets/_Game", "Shop");
            Folder("Assets/_Game/Shop", "Materials");
            Folder("Assets/_Game/Shop/Materials", "Skins");
            Folder("Assets/_Game/Shop", "Fx");
            Folder("Assets/_Game/Shop", "Prefabs");
            Folder("Assets/_Game/Shop", "Items");
            Folder("Assets/_Game/Shop", "Icons");
            Folder("Assets/_Game", "Resources");
        }

        private static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        // ======================================================================================
        // Materialsätze
        // ======================================================================================

        // Ein Eintrag: Original-Slot → neue Farbe (sRGB), optional Glanz/Metall/Emission
        private class Tint
        {
            public string Source;
            public Color Color;
            public float Smooth = -1f, Metal = -1f;
            public Color Emission;
            public float Glow; // 0 = keine Emission, sonst Emission = Emission-Farbe × Glow
        }

        private static Tint T(string src, float r, float g, float b, float smooth = -1f, float metal = -1f)
        {
            return new Tint { Source = src, Color = new Color(r, g, b), Smooth = smooth, Metal = metal };
        }

        private static Tint G(string src, float r, float g, float b, float glow, float er = -1f, float eg = -1f, float eb = -1f)
        {
            var e = er < 0f ? new Color(r, g, b) : new Color(er, eg, eb);
            return new Tint { Source = src, Color = new Color(r, g, b), Emission = e, Glow = glow };
        }

        // Farbsätze je Rüstung (Low-Poly-Flat-Look: kräftige, klar getrennte Flächenfarben). Haut/Augen bleiben.
        private static readonly Dictionary<string, Tint[]> Sets = new Dictionary<string, Tint[]>
        {
            // Berserker „Frostwolf“: weißes Fell, eisblaue Bemalung und Stoffe, heller Stahl, hellblondes Haar
            { "Frostwolf", new[] {
                T("Hero2_Fur", 0.90f, 0.92f, 0.95f), T("Berserker2_FurLight", 0.98f, 0.99f, 1f), T("Berserker2_FurDark", 0.66f, 0.71f, 0.79f),
                T("Hero2_WarPaint", 0.22f, 0.62f, 1f),
                T("Berserker2_Red", 0.36f, 0.64f, 0.88f), T("Berserker2_RedDark", 0.14f, 0.24f, 0.50f),
                T("Berserker2_Iron", 0.82f, 0.86f, 0.92f, 0.72f), T("Berserker2_IronDark", 0.50f, 0.55f, 0.63f, 0.65f),
                T("Berserker2_Bronze", 0.76f, 0.80f, 0.86f, 0.7f),
                T("Berserker2_Hair", 0.95f, 0.86f, 0.60f), T("Berserker2_HairDark", 0.82f, 0.70f, 0.44f),
                T("Berserker2_Leather", 0.42f, 0.38f, 0.37f), T("Berserker2_LeatherDark", 0.27f, 0.25f, 0.26f),
                T("Berserker2_Trousers", 0.30f, 0.34f, 0.42f), T("Berserker2_Ochre", 0.68f, 0.80f, 0.92f),
                T("Berserker2_Wrap", 0.86f, 0.88f, 0.90f), T("Berserker2_HornTip", 0.80f, 0.88f, 0.96f, 0.6f),
            } },
            // Berserker „Glutkrieger“: schwarzes Leder/Fell, glutorange Stoffe, glühende Bemalung, dunkles Eisen
            { "Glutkrieger", new[] {
                T("Hero2_Fur", 0.17f, 0.16f, 0.16f), T("Berserker2_FurLight", 0.30f, 0.28f, 0.28f), T("Berserker2_FurDark", 0.08f, 0.08f, 0.08f),
                G("Hero2_WarPaint", 0.6f, 0.2f, 0.04f, 1.8f, 1f, 0.28f, 0.02f),
                G("Berserker2_Ochre", 0.62f, 0.24f, 0.05f, 1.5f, 1f, 0.3f, 0.03f),
                T("Berserker2_Red", 0.95f, 0.42f, 0.08f), T("Berserker2_RedDark", 0.52f, 0.16f, 0.05f),
                T("Berserker2_Leather", 0.16f, 0.15f, 0.15f), T("Berserker2_LeatherDark", 0.08f, 0.08f, 0.09f),
                T("Berserker2_Trousers", 0.12f, 0.12f, 0.13f), T("Berserker2_Sole", 0.05f, 0.05f, 0.05f),
                T("Berserker2_Wrap", 0.24f, 0.22f, 0.21f),
                T("Berserker2_Iron", 0.27f, 0.27f, 0.30f, 0.55f), T("Berserker2_IronDark", 0.12f, 0.12f, 0.14f, 0.5f),
                T("Berserker2_Bronze", 0.48f, 0.28f, 0.15f, 0.6f),
                T("Berserker2_Hair", 0.86f, 0.30f, 0.07f), T("Berserker2_HairDark", 0.55f, 0.15f, 0.05f),
                T("Hero2_Horn", 0.22f, 0.20f, 0.20f, 0.5f), G("Berserker2_HornTip", 1f, 0.50f, 0.12f, 1.5f),
            } },
            // Ritter „Schwarzer Ritter“: anthrazitfarbener Stahl, dunkelroter Wappenrock, Bronze statt Gold
            { "SchwarzerRitter", new[] {
                T("Knight2_Steel", 0.27f, 0.28f, 0.31f, 0.72f, 0.2f), T("Knight2_SteelDark", 0.15f, 0.16f, 0.18f, 0.62f, 0.15f),
                T("Knight2_Mail", 0.17f, 0.17f, 0.19f), T("Knight2_Blue", 0.56f, 0.08f, 0.08f),
                T("Knight2_Gold", 0.64f, 0.42f, 0.25f, 0.6f, 0.15f), T("Knight2_Leather", 0.22f, 0.14f, 0.10f),
            } },
            // Ritter „Sonnenpaladin“: Goldrüstung, weißer Wappenrock, kräftiges, leicht leuchtendes Gold
            { "Sonnenpaladin", new[] {
                T("Knight2_Steel", 0.98f, 0.86f, 0.55f, 0.75f, 0.2f), T("Knight2_SteelDark", 0.84f, 0.63f, 0.30f, 0.7f, 0.2f),
                T("Knight2_Mail", 0.72f, 0.62f, 0.44f), T("Knight2_Blue", 0.97f, 0.95f, 0.89f),
                G("Knight2_Gold", 1f, 0.78f, 0.22f, 0.6f, 1f, 0.72f, 0.2f), T("Knight2_Leather", 0.58f, 0.40f, 0.23f),
            } },
            // Bogenschütze „Herbstjäger“: Rostrot/Orange, senffarbenes Leinen, dunkles Leder
            { "Herbstjaeger", new[] {
                T("Archer2_Green", 0.78f, 0.36f, 0.13f), T("Archer2_GreenDark", 0.52f, 0.20f, 0.09f),
                T("Archer2_Pants", 0.40f, 0.26f, 0.15f), T("Archer2_Linen", 0.86f, 0.67f, 0.24f),
                T("Archer2_Leather", 0.32f, 0.19f, 0.10f), T("Archer2_LeatherDark", 0.20f, 0.12f, 0.07f),
                T("Archer2_LeatherRim", 0.70f, 0.50f, 0.25f), T("Archer2_Buckle", 0.82f, 0.63f, 0.30f),
            } },
            // Bogenschütze „Schattenjäger“: Schwarz-Violett, dunkelgraues Leinen, violett glühende Säume/Schnallen
            { "Schattenjaeger", new[] {
                T("Archer2_Green", 0.17f, 0.11f, 0.24f), T("Archer2_GreenDark", 0.08f, 0.06f, 0.11f),
                T("Archer2_Pants", 0.10f, 0.09f, 0.13f), T("Archer2_Linen", 0.29f, 0.29f, 0.32f),
                T("Archer2_Leather", 0.15f, 0.13f, 0.17f), T("Archer2_LeatherDark", 0.08f, 0.07f, 0.09f),
                G("Archer2_LeatherRim", 0.66f, 0.36f, 1f, 1.5f, 0.6f, 0.25f, 1f),
                G("Archer2_Buckle", 0.78f, 0.52f, 1f, 1.2f, 0.65f, 0.3f, 1f),
                T("Archer2_Hair", 0.14f, 0.11f, 0.12f),
            } },
            // Schamane „Steppenwind“: sandfarbener Umhang, rostrote Bemalung/Säume, helles Fell, sonnengebleichtes Leder
            { "Steppenwind", new[] {
                T("Hero2_ShamanCloth", 0.80f, 0.67f, 0.45f), T("Shaman2_ClothDark", 0.56f, 0.24f, 0.13f),
                T("Hero2_ShamanPaint", 0.74f, 0.25f, 0.11f),
                T("Hero2_Fur", 0.80f, 0.72f, 0.58f), T("Shaman2_FurDark", 0.50f, 0.40f, 0.28f),
                T("Shaman2_Leather", 0.56f, 0.36f, 0.20f), T("Shaman2_LeatherDark", 0.36f, 0.22f, 0.13f),
                T("Shaman2_Wrap", 0.86f, 0.77f, 0.58f), T("Shaman2_WrapDark", 0.64f, 0.42f, 0.25f),
                T("Shaman2_Trousers", 0.60f, 0.46f, 0.31f), T("Shaman2_Hide", 0.74f, 0.60f, 0.42f),
                T("Hero2_Feather", 0.76f, 0.34f, 0.16f), T("Shaman2_FeatherDark", 0.44f, 0.19f, 0.10f),
            } },
            // Schamane „Geisterrufer“: nachtblauer Umhang, leuchtend cyanfarbene Bemalung, blasse Knochen und Federn
            { "Geisterrufer", new[] {
                T("Hero2_ShamanCloth", 0.07f, 0.10f, 0.27f), T("Shaman2_ClothDark", 0.03f, 0.05f, 0.14f),
                G("Hero2_ShamanPaint", 0.35f, 0.95f, 1f, 2.0f, 0.2f, 0.85f, 1f),
                T("Hero2_Fur", 0.22f, 0.26f, 0.38f), T("Shaman2_FurDark", 0.11f, 0.13f, 0.21f),
                T("Shaman2_Leather", 0.15f, 0.17f, 0.28f), T("Shaman2_LeatherDark", 0.07f, 0.08f, 0.15f),
                T("Shaman2_Wrap", 0.24f, 0.30f, 0.45f), T("Shaman2_WrapDark", 0.12f, 0.15f, 0.27f),
                T("Shaman2_Trousers", 0.10f, 0.12f, 0.23f), T("Shaman2_Hide", 0.21f, 0.27f, 0.42f),
                T("Shaman2_Wood", 0.20f, 0.21f, 0.30f),
                T("Hero2_Bone", 0.82f, 0.90f, 0.96f), T("Hero2_Antler", 0.62f, 0.70f, 0.84f),
                T("Hero2_Feather", 0.76f, 0.92f, 1f), T("Shaman2_FeatherDark", 0.26f, 0.46f, 0.66f),
            } },
            // Magier „Erzmagier“: königsblaue Robe und Hut, hellblaue Akzente, Silber statt Gold
            { "Erzmagier", new[] {
                T("Mage2_Robe", 0.12f, 0.26f, 0.78f), T("Mage2_Hat", 0.14f, 0.29f, 0.82f), T("Mage2_RobeInner", 0.06f, 0.12f, 0.42f),
                T("Mage2_Violet", 0.52f, 0.78f, 1f), T("Mage2_Gold", 0.84f, 0.87f, 0.92f, 0.72f),
                T("Mage2_Belt", 0.28f, 0.22f, 0.20f), T("Mage2_Crystal", 0.78f, 0.90f, 1f, 0.85f),
            } },
            // Magier „Sternenweber“: nachtblaue Robe, silberweiß leuchtende Borten, weiß-blau glühender Kristall
            { "Sternenweber", new[] {
                T("Mage2_Robe", 0.06f, 0.08f, 0.23f), T("Mage2_Hat", 0.07f, 0.09f, 0.26f), T("Mage2_RobeInner", 0.03f, 0.04f, 0.12f),
                T("Mage2_Violet", 0.22f, 0.28f, 0.62f),
                G("Mage2_Gold", 0.93f, 0.95f, 1f, 0.5f, 0.8f, 0.86f, 1f),
                G("Mage2_Crystal", 0.85f, 0.93f, 1f, 2.0f, 0.55f, 0.8f, 1f),
                T("Mage2_Belt", 0.15f, 0.15f, 0.26f), T("Mage2_Shoe", 0.12f, 0.12f, 0.20f),
            } },
        };

        // Materialien eines Satzes (Name → Swap-Liste), gefüllt von BuildMaterials
        private static readonly Dictionary<string, ShopItemSO.MaterialSwap[]> _swaps = new Dictionary<string, ShopItemSO.MaterialSwap[]>();

        private static int BuildMaterials()
        {
            _swaps.Clear();
            int n = 0;
            foreach (var kv in Sets)
            {
                var list = new List<ShopItemSO.MaterialSwap>();
                foreach (var t in kv.Value)
                {
                    var src = AssetDatabase.LoadAssetAtPath<Material>(SrcMat + t.Source + ".mat");
                    if (src == null) { Debug.LogWarning("ShopSetup: Original-Material fehlt: " + t.Source); continue; }
                    string path = MatDir + kv.Key + "_" + t.Source + ".mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null)
                    {
                        m = new Material(src);
                        AssetDatabase.CreateAsset(m, path);
                    }
                    else
                    {
                        // Legacy-_Color (von URP Lit nicht genutzt) behalten: CopyProperties setzt es sonst auf den Wert des
                        // Originals zurück und jede Erzeugung änderte vorhandene Materialien
                        Color? keep = RawColor(m, "_Color"); // roh aus der Serialisierung (Get/SetColor runden über den Farbraum)
                        m.shader = src.shader;
                        m.CopyPropertiesFromMaterial(src);
                        RawColor(m, "_Color", keep);
                    }
                    m.SetColor("_BaseColor", t.Color);
                    m.color = t.Color;
                    if (t.Smooth >= 0f) m.SetFloat("_Smoothness", t.Smooth);
                    if (t.Metal >= 0f) m.SetFloat("_Metallic", t.Metal);
                    if (t.Glow > 0f)
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", t.Emission * t.Glow);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    else
                    {
                        m.DisableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", Color.black);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    }
                    EditorUtility.SetDirty(m);
                    list.Add(new ShopItemSO.MaterialSwap { SourceMaterial = t.Source, Replacement = m });
                    n++;
                }
                _swaps[kv.Key] = list.ToArray();
            }
            return n;
        }

        // Farbeigenschaft roh aus m_SavedProperties lesen bzw. (value != null) schreiben
        private static Color? RawColor(Material m, string name, Color? value = null)
        {
            var so = new SerializedObject(m);
            var arr = so.FindProperty("m_SavedProperties.m_Colors");
            if (arr == null) return null;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue != name) continue;
                var v = e.FindPropertyRelative("second");
                if (value.HasValue)
                {
                    v.colorValue = value.Value;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                return v.colorValue;
            }
            return null;
        }

        // ======================================================================================
        // Effekte
        // ======================================================================================

        private static Material VfxMaterial(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(VfxMat + name + ".mat");
            if (m == null) Debug.LogWarning("ShopSetup: Partikel-Material fehlt: " + name);
            return m;
        }

        private static Material FlameAdd => VfxMaterial("VFX_Flame_Add");
        private static Material SparkAdd => VfxMaterial("VFX_Spark_Add");
        private static Material SoftAdd => VfxMaterial("VFX_SoftDot_Add");
        private static Material StarAdd => VfxMaterial("VFX_Star_Add");
        private static Material SmokeAlpha => VfxMaterial("VFX_Smoke_Alpha");

        // Dauer-Emitter (Schleife, vorgewärmt). Größen/Geschwindigkeiten in Metern (Prefab-Maßstab ≈ Welt).
        private static ParticleSystem Emitter(Transform parent, string name, Material mat, float rate, Vector2 life, Vector2 size, Color a, Color b, int max, bool world = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = max;
            main.cullingMode = ParticleSystemCullingMode.Automatic;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.maxParticleSize = 0.25f;
            return ps;
        }

        private static void Box(ParticleSystem ps, Vector3 size, Vector3 center)
        {
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = size;
            sh.position = center;
        }

        private static void Ball(ParticleSystem ps, float radius, float thickness, Vector3 center)
        {
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            sh.radiusThickness = thickness;
            sh.position = center;
        }

        private static void Speed(ParticleSystem ps, float min, float max)
        {
            var m = ps.main;
            m.startSpeed = new ParticleSystem.MinMaxCurve(min, max);
        }

        // negativ = steigt auf (Wärme), positiv = sinkt
        private static void Gravity(ParticleSystem ps, float g)
        {
            var m = ps.main;
            m.gravityModifier = g;
        }

        private static void Fade(ParticleSystem ps, Color from, Color to, float inAt = 0.15f, float outAt = 0.55f)
        {
            var c = ps.colorOverLifetime;
            c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inAt), new GradientAlphaKey(1f, outAt), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }

        private static void Shrink(ParticleSystem ps, float start = 1f, float end = 0.2f)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, start, 1f, end));
        }

        // Funkeln: klein → groß → klein
        private static void Twinkle(ParticleSystem ps)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f)));
        }

        private static void Spin(ParticleSystem ps, float degPerSec)
        {
            var r = ps.rotationOverLifetime;
            r.enabled = true;
            r.z = new ParticleSystem.MinMaxCurve(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }

        private static void Noise(ParticleSystem ps, float strength, float freq = 1.2f)
        {
            var n = ps.noise;
            n.enabled = true;
            n.strength = strength;
            n.frequency = freq;
            n.quality = ParticleSystemNoiseQuality.Low;
            n.scrollSpeed = 0.4f;
        }

        // Effekt-Prefab bauen und speichern (überschreibt, GUID bleibt)
        private static GameObject SaveFx(string name, System.Action<Transform> build)
        {
            var root = new GameObject(name);
            try
            {
                build(root.transform);
                string path = FxDir + name + ".prefab";
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static readonly Color Ember = new Color(1f, 0.55f, 0.15f);
        private static readonly Color EmberDeep = new Color(1f, 0.22f, 0.04f);
        // Flammen: Startfarbe hell, Verlauf (color over lifetime) bestimmt den Ton – sonst multipliziert sich Orange zu Dunkelrot
        private static readonly Color FlameStart = new Color(1f, 0.92f, 0.8f);
        private static readonly Color FlameHot = new Color(1f, 0.9f, 0.5f);
        private static readonly Color FlameCool = new Color(1f, 0.42f, 0.1f);
        private static readonly Color SunGold = new Color(1f, 0.86f, 0.45f);
        private static readonly Color SunWhite = new Color(1f, 0.97f, 0.85f);
        private static readonly Color FrostBlue = new Color(0.6f, 0.88f, 1f);
        private static readonly Color FrostWhite = new Color(0.92f, 0.98f, 1f);

        private static Dictionary<string, GameObject> BuildFx()
        {
            var fx = new Dictionary<string, GameObject>();

            // Glutaxt: Flammen und Glut entlang der Klinge (FxAnchor = Klingenmitte, Klinge quer in X); Welt-Simulation → Spur beim Schwingen
            fx["EmberAxe"] = SaveFx("Fx_EmberAxe", t =>
            {
                var f = Emitter(t, "Flames", FlameAdd, 20f, new Vector2(0.3f, 0.55f), new Vector2(0.14f, 0.24f), FlameStart, Color.white, 24);
                Box(f, new Vector3(0.42f, 0.22f, 0.05f), Vector3.zero);
                Speed(f, 0.05f, 0.2f); Gravity(f, -0.25f); Fade(f, FlameHot, FlameCool); Shrink(f, 1f, 0.3f); Spin(f, 90f);
                var s = Emitter(t, "Embers", SparkAdd, 6f, new Vector2(0.6f, 1.0f), new Vector2(0.03f, 0.05f), Color.white, FlameStart, 10);
                Box(s, new Vector3(0.45f, 0.25f, 0.05f), Vector3.zero);
                Speed(s, 0.15f, 0.4f); Gravity(s, -0.2f); Fade(s, new Color(1f, 0.85f, 0.5f), EmberDeep, 0.05f, 0.6f); Noise(s, 0.3f);
            });

            // Glutflammen allgemein: brennende Kugel des Flammenstabs
            fx["FlameStaff"] = SaveFx("Fx_FlameStaff", t =>
            {
                var f = Emitter(t, "Flames", FlameAdd, 18f, new Vector2(0.35f, 0.6f), new Vector2(0.12f, 0.2f), FlameStart, Color.white, 22);
                Ball(f, 0.06f, 1f, Vector3.zero);
                Speed(f, 0.02f, 0.1f); Gravity(f, -0.3f); Fade(f, FlameHot, FlameCool); Shrink(f, 1f, 0.25f); Spin(f, 90f);
                var s = Emitter(t, "Embers", SparkAdd, 4f, new Vector2(0.6f, 1.0f), new Vector2(0.025f, 0.045f), Color.white, FlameStart, 8);
                Ball(s, 0.07f, 1f, Vector3.zero);
                Speed(s, 0.1f, 0.3f); Gravity(s, -0.2f); Fade(s, new Color(1f, 0.85f, 0.5f), EmberDeep, 0.05f, 0.6f); Noise(s, 0.25f);
            });

            // Sonnenklinge: goldene Lichtfunken entlang der Klinge (FxAnchor = Klingenmitte, Klinge in +Y)
            fx["SunSword"] = SaveFx("Fx_SunSword", t =>
            {
                var s = Emitter(t, "Sparkles", StarAdd, 7f, new Vector2(0.5f, 0.9f), new Vector2(0.05f, 0.09f), SunGold, SunWhite, 12);
                Box(s, new Vector3(0.07f, 0.8f, 0.04f), Vector3.zero);
                Speed(s, 0.02f, 0.08f); Fade(s, SunWhite, SunGold, 0.2f, 0.5f); Twinkle(s); Spin(s, 60f);
                var g = Emitter(t, "Glow", SoftAdd, 6f, new Vector2(0.4f, 0.7f), new Vector2(0.06f, 0.1f), new Color(1f, 0.8f, 0.35f, 0.6f), new Color(1f, 0.9f, 0.6f, 0.6f), 8);
                Box(g, new Vector3(0.05f, 0.8f, 0.03f), Vector3.zero);
                Fade(g, SunGold, new Color(1f, 0.7f, 0.25f)); Shrink(g, 1f, 0.4f);
            });

            // Sonnenschild: Lichtfunken auf der Schildfront (FxAnchor = Mitte der Vorderseite)
            fx["SunShield"] = SaveFx("Fx_SunShield", t =>
            {
                var s = Emitter(t, "Sparkles", StarAdd, 6f, new Vector2(0.5f, 0.9f), new Vector2(0.05f, 0.09f), SunGold, SunWhite, 10);
                Box(s, new Vector3(0.45f, 0.6f, 0.02f), Vector3.zero);
                Speed(s, 0.02f, 0.06f); Fade(s, SunWhite, SunGold, 0.2f, 0.5f); Twinkle(s); Spin(s, 60f);
            });

            // Frostbogen: Eisglitzern entlang des Bogens (FxAnchor = Griffmitte, Bogen in ±Y, Wölbung +X), sinkt langsam
            fx["FrostBow"] = SaveFx("Fx_FrostBow", t =>
            {
                var g = Emitter(t, "Frost", SoftAdd, 9f, new Vector2(0.6f, 1.0f), new Vector2(0.07f, 0.12f), FrostBlue, FrostWhite, 14);
                Box(g, new Vector3(0.1f, 1.15f, 0.04f), new Vector3(0.06f, 0f, 0f));
                Speed(g, 0.01f, 0.05f); Gravity(g, 0.03f); Fade(g, FrostWhite, FrostBlue); Shrink(g, 1f, 0.3f);
                var s = Emitter(t, "Glints", SparkAdd, 5f, new Vector2(0.4f, 0.8f), new Vector2(0.03f, 0.05f), FrostWhite, FrostBlue, 8);
                Box(s, new Vector3(0.1f, 1.2f, 0.04f), new Vector3(0.06f, 0f, 0f));
                Speed(s, 0.02f, 0.08f); Gravity(s, 0.05f); Fade(s, Color.white, FrostBlue, 0.1f, 0.5f); Twinkle(s);
            });

            // Glutkrieger: Glutfunken, die von der Schulterplatte aufsteigen
            fx["EmberSparks"] = SaveFx("Fx_EmberSparks", t =>
            {
                var s = Emitter(t, "Sparks", SparkAdd, 7f, new Vector2(0.8f, 1.3f), new Vector2(0.03f, 0.055f), Color.white, FlameStart, 12);
                Box(s, new Vector3(0.18f, 0.08f, 0.26f), new Vector3(0.02f, 0.02f, 0f));
                Speed(s, 0.05f, 0.15f); Gravity(s, -0.12f); Fade(s, new Color(1f, 0.85f, 0.5f), EmberDeep, 0.05f, 0.6f); Noise(s, 0.25f);
                var g = Emitter(t, "Glow", FlameAdd, 4f, new Vector2(0.4f, 0.6f), new Vector2(0.08f, 0.13f), FlameStart, Color.white, 5);
                Box(g, new Vector3(0.14f, 0.05f, 0.2f), new Vector3(0.02f, 0.03f, 0f));
                Gravity(g, -0.15f); Fade(g, FlameHot, FlameCool); Shrink(g, 1f, 0.2f);
            });

            // Sonnenpaladin: goldener Schimmer um den Oberkörper (an UpperChest, lokal → bleibt am Körper)
            fx["Shimmer"] = SaveFx("Fx_Shimmer", t =>
            {
                var s = Emitter(t, "Shimmer", StarAdd, 6f, new Vector2(0.8f, 1.3f), new Vector2(0.04f, 0.075f), SunGold, SunWhite, 10, false);
                Ball(s, 0.32f, 0.25f, Vector3.zero);
                Speed(s, 0.01f, 0.04f); Fade(s, SunWhite, SunGold, 0.25f, 0.5f); Twinkle(s); Spin(s, 45f);
                var sh = s.shape; sh.scale = new Vector3(1f, 0.85f, 0.7f);
            });

            // Schattenjäger: dunkle, violette Schwaden vom Umhang (Alpha-Rauch) + wenige violette Funken
            fx["Shadow"] = SaveFx("Fx_Shadow", t =>
            {
                var w = Emitter(t, "Wisps", SmokeAlpha, 7f, new Vector2(1.0f, 1.5f), new Vector2(0.18f, 0.3f), new Color(0.5f, 0.3f, 0.7f, 0.8f), new Color(0.35f, 0.2f, 0.5f, 0.8f), 12);
                Box(w, new Vector3(0.55f, 0.06f, 0.2f), new Vector3(0f, -0.12f, -0.02f));
                Speed(w, 0.01f, 0.05f); Gravity(w, -0.02f); Fade(w, new Color(0.45f, 0.25f, 0.65f), new Color(0.12f, 0.06f, 0.2f), 0.2f, 0.5f); Shrink(w, 0.6f, 1.4f); Spin(w, 30f); Noise(w, 0.1f, 0.8f);
                var s = Emitter(t, "Motes", SparkAdd, 3f, new Vector2(0.6f, 1.0f), new Vector2(0.03f, 0.05f), new Color(0.7f, 0.4f, 1f), new Color(0.5f, 0.25f, 1f), 6);
                Box(s, new Vector3(0.55f, 0.1f, 0.2f), new Vector3(0f, -0.08f, -0.02f));
                Speed(s, 0.02f, 0.08f); Gravity(s, -0.05f); Fade(s, new Color(0.8f, 0.55f, 1f), new Color(0.4f, 0.15f, 0.9f)); Twinkle(s);
            });

            // Sternenweber: weiß-blaues Funkeln rund um die schwebende Krone (lokal)
            fx["StarSparkle"] = SaveFx("Fx_StarSparkle", t =>
            {
                var s = Emitter(t, "Stars", StarAdd, 5f, new Vector2(0.7f, 1.1f), new Vector2(0.045f, 0.08f), new Color(0.75f, 0.88f, 1f), Color.white, 8, false);
                var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.16f; sh.radiusThickness = 0.3f; sh.rotation = new Vector3(-90f, 0f, 0f);
                Speed(s, 0.01f, 0.04f); Fade(s, Color.white, new Color(0.6f, 0.8f, 1f), 0.25f, 0.5f); Twinkle(s); Spin(s, 60f);
                var d = Emitter(t, "Dust", SparkAdd, 4f, new Vector2(0.6f, 1.0f), new Vector2(0.015f, 0.03f), new Color(0.7f, 0.85f, 1f), Color.white, 6, false);
                Ball(d, 0.14f, 1f, Vector3.zero);
                Speed(d, 0.01f, 0.03f); Gravity(d, 0.02f); Fade(d, Color.white, new Color(0.5f, 0.7f, 1f)); Twinkle(d);
            });

            // Sturmtotem: blaue Blitzfunken um den schwebenden Blitzstein (FxAnchor), schnell und kurzlebig, flackernder Kern
            fx["Storm"] = SaveFx("Fx_Storm", t =>
            {
                var s = Emitter(t, "Sparks", SparkAdd, 16f, new Vector2(0.12f, 0.28f), new Vector2(0.04f, 0.075f), StormWhite, StormBlue, 14);
                Ball(s, 0.05f, 1f, Vector3.zero);
                Speed(s, 0.5f, 1.1f); Fade(s, StormWhite, StormBlue, 0.02f, 0.4f); Shrink(s, 1f, 0.1f); Noise(s, 0.8f, 3f);
                var st = s.main; st.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                var r = s.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.08f; r.lengthScale = 1.5f;
                var g = Emitter(t, "Flicker", StarAdd, 10f, new Vector2(0.08f, 0.18f), new Vector2(0.12f, 0.22f), StormBlue, StormWhite, 6);
                Ball(g, 0.03f, 1f, Vector3.zero);
                Fade(g, StormWhite, StormBlue, 0.05f, 0.3f); Twinkle(g); Spin(g, 360f);
                var c = Emitter(t, "Core", SoftAdd, 5f, new Vector2(0.3f, 0.5f), new Vector2(0.2f, 0.28f), new Color(0.35f, 0.6f, 1f, 0.9f), new Color(0.55f, 0.8f, 1f, 0.9f), 4, false);
                Fade(c, StormBlue, new Color(0.25f, 0.45f, 1f)); Shrink(c, 0.8f, 1.1f);
            });

            // Geisterrufer: deutlich sichtbare Geisterschwaden (additiv cyan statt dunklem Rauch – der Schattenjäger-Rauch
            // ging vor dem dunklen Körper unter), steigen um Schultern und Rücken auf (lokal → bleiben an der Figur)
            fx["SpiritWisps"] = SaveFx("Fx_SpiritWisps", t =>
            {
                var w = Emitter(t, "Wisps", SoftAdd, 9f, new Vector2(1.1f, 1.6f), new Vector2(0.16f, 0.26f), new Color(0.3f, 0.9f, 1f, 0.85f), new Color(0.5f, 1f, 0.95f, 0.85f), 14, false);
                Box(w, new Vector3(0.5f, 0.12f, 0.3f), new Vector3(0f, -0.05f, -0.06f));
                Speed(w, 0.02f, 0.06f); Gravity(w, -0.06f); Fade(w, SpiritCyan, new Color(0.2f, 0.5f, 1f), 0.2f, 0.55f); Shrink(w, 0.5f, 1.3f); Noise(w, 0.18f, 0.9f);
                var m = Emitter(t, "Motes", SparkAdd, 6f, new Vector2(0.8f, 1.3f), new Vector2(0.035f, 0.06f), Color.white, SpiritCyan, 10, false);
                Box(m, new Vector3(0.55f, 0.2f, 0.32f), new Vector3(0f, 0f, -0.04f));
                Speed(m, 0.03f, 0.1f); Gravity(m, -0.08f); Fade(m, Color.white, SpiritCyan, 0.1f, 0.5f); Twinkle(m); Noise(m, 0.25f);
            });

            return fx;
        }

        private static readonly Color StormBlue = new Color(0.35f, 0.62f, 1f);
        private static readonly Color StormWhite = new Color(0.85f, 0.94f, 1f);
        private static readonly Color SpiritCyan = new Color(0.35f, 0.95f, 1f);

        // ======================================================================================
        // Prefabs (Waffen/Zusatzteile mit Effekt)
        // ======================================================================================

        private static GameObject Model(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + name + ".fbx");
            if (go == null) Debug.LogWarning("ShopSetup: Modell fehlt: " + name);
            return go;
        }

        // Variante des FBX (Wurzel = FBX, Lage unverändert), Effekt als Kind am Punkt `anchor` (Kind-Name) bzw. an der Wurzel
        // swap: Materialien der Variante tauschen (Name → Ersatz), z. B. gedämpfter Glow am Umhang
        private static GameObject WithFx(string model, GameObject fx, string anchor, Vector3 offset = default(Vector3), ShopItemSO.MaterialSwap swap = null)
        {
            var src = Model(model);
            if (src == null) return null;
            if (fx == null) return src;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                Transform at = string.IsNullOrEmpty(anchor) ? inst.transform : FindDeep(inst.transform, anchor);
                if (at == null) { Debug.LogWarning("ShopSetup: " + anchor + " fehlt an " + model); at = inst.transform; }
                var f = (GameObject)PrefabUtility.InstantiatePrefab(fx, at);
                f.transform.localPosition = offset;
                f.transform.localRotation = Quaternion.identity;
                f.transform.localScale = Vector3.one;
                if (swap != null && swap.Replacement != null)
                    foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                            if (mats[i] != null && mats[i].name == swap.SourceMaterial) mats[i] = swap.Replacement;
                        r.sharedMaterials = mats;
                    }
                return PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + model + ".prefab");
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // Variante des FBX mit anderer Wurzel-Skalierung (Ursprung/Achsen unverändert)
        private static GameObject Variant(string model, float scale)
        {
            var src = Model(model);
            if (src == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                inst.transform.localScale = src.transform.localScale * scale;
                return PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + model + ".prefab");
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // Umhang-Saum des Schattenjägers: Kopie von A2S_Shadow_Glow mit schwächerer Emission – das Original (×2,5) übersteuert zu Weiß
        private static ShopItemSO.MaterialSwap MantleGlow()
        {
            const string srcName = "A2S_Shadow_Glow";
            var src = AssetDatabase.LoadAssetAtPath<Material>(ShopItemImport.MaterialDir + srcName + ".mat");
            if (src == null) return null;
            string path = MatDir + "Schattenjaeger_MantleGlow.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else
            {
                Color? keep = RawColor(m, "_Color"); // s. BuildMaterials
                m.shader = src.shader; m.CopyPropertiesFromMaterial(src);
                RawColor(m, "_Color", keep);
            }
            var c = new Color(0.62f, 0.32f, 1f);
            m.SetColor("_BaseColor", c);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.6f, 0.25f, 1f) * 1.4f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return new ShopItemSO.MaterialSwap { SourceMaterial = srcName, Replacement = m };
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private static Dictionary<string, GameObject> BuildPrefabs(Dictionary<string, GameObject> fx)
        {
            var p = new Dictionary<string, GameObject>();
            // ohne Effekt: direkt das FBX
            p["W2S_WarHammer"] = Model("W2S_WarHammer");
            p["W2S_Mace"] = Model("W2S_Mace");
            p["W2S_RoundShield"] = Model("W2S_RoundShield");
            // Langbogen (1,66 m) auf 90 %: die untere Spitze streifte sonst beim seitlichen Rennen den rechten Fuß
            // (Clipping-Prüfung). Skalierung an der Prefab-Wurzel wirkt in Spielfigur und Menü-Vorschau gleichermaßen.
            p["W2S_Longbow"] = Variant("W2S_Longbow", 0.9f);
            p["W2S_GnarledStaff"] = Model("W2S_GnarledStaff");
            p["A2S_KnightPlume"] = Model("A2S_KnightPlume");
            p["A2S_KnightPauldron"] = Model("A2S_KnightPauldron");
            // mit Effekt
            p["W2S_EmberAxe"] = WithFx("W2S_EmberAxe", fx["EmberAxe"], "FxAnchor");
            p["W2S_SunSword"] = WithFx("W2S_SunSword", fx["SunSword"], "FxAnchor");
            p["W2S_SunShield"] = WithFx("W2S_SunShield", fx["SunShield"], "FxAnchor");
            p["W2S_FrostBow"] = WithFx("W2S_FrostBow", fx["FrostBow"], "FxAnchor");
            p["W2S_FlameStaff"] = WithFx("W2S_FlameStaff", fx["FlameStaff"], "FxAnchor");
            p["A2S_BerserkerPauldron"] = WithFx("A2S_BerserkerPauldron", fx["EmberSparks"], null);
            p["A2S_ArcherMantle"] = WithFx("A2S_ArcherMantle", fx["Shadow"], null, default(Vector3), MantleGlow());
            p["A2S_StarCrown"] = WithFx("A2S_StarCrown", fx["StarSparkle"], null);
            p["Fx_Shimmer"] = fx["Shimmer"]; // als eigenes Zusatzteil am Oberkörper
            // Schamane
            p["W2S_AntlerStaff"] = Model("W2S_AntlerStaff");
            p["W2S_StormTotemStaff"] = WithFx("W2S_StormTotemStaff", fx["Storm"], "FxAnchor");
            p["A2S_SpiritWolfMask"] = Model("A2S_SpiritWolfMask");
            p["Fx_SpiritWisps"] = fx["SpiritWisps"]; // als eigenes Zusatzteil am Oberkörper
            return p;
        }

        // ======================================================================================
        // Items + Katalog
        // ======================================================================================

        private class ItemDef
        {
            public string Id, Name, Desc, Set;
            public ChampionClass Class;
            public ShopSlot Slot;
            public bool Epic;
            public string Right, Left;
            public ShopItemSO.AddOn[] AddOns;
        }

        private static ShopItemSO.AddOn Add(GameObject prefab, HumanBodyBones bone, Vector3 pos, Vector3 euler, bool mirror = false)
        {
            return new ShopItemSO.AddOn { Prefab = prefab, Bone = bone, LocalPosition = pos, LocalEuler = euler, LocalScale = Vector3.one, Mirror = mirror };
        }

        private static List<ItemDef> Defs(Dictionary<string, GameObject> p)
        {
            Vector3 pauldron = PauldronRot.eulerAngles;
            Vector3 knightShoulder = new Vector3(0.0032f, 0.2898f, 0.1162f);
            return new List<ItemDef>
            {
                // ---------- Berserker ----------
                new ItemDef { Id = "berserker_warhammer", Class = ChampionClass.Berserker, Slot = ShopSlot.Weapon, Name = "Kriegshammer",
                    Desc = "Ein wuchtiger Zweihandhammer, der Schilde zertrümmert.", Right = "W2S_WarHammer" },
                new ItemDef { Id = "berserker_emberaxe", Class = ChampionClass.Berserker, Slot = ShopSlot.Weapon, Epic = true, Name = "Glutaxt",
                    Desc = "Die Klinge glüht wie frisch aus der Esse – Funken inklusive.", Right = "W2S_EmberAxe" },
                new ItemDef { Id = "berserker_frostwolf", Class = ChampionClass.Berserker, Slot = ShopSlot.Armor, Name = "Frostwolf",
                    Desc = "Weißes Wolfsfell und eisblaue Kriegsbemalung aus dem Norden.", Set = "Frostwolf" },
                new ItemDef { Id = "berserker_emberwarrior", Class = ChampionClass.Berserker, Slot = ShopSlot.Armor, Epic = true, Name = "Glutkrieger",
                    Desc = "Schwarzes Leder, glühende Zeichen und ein Stachelpanzer.", Set = "Glutkrieger",
                    AddOns = new[] { Add(p["A2S_BerserkerPauldron"], HumanBodyBones.RightShoulder, new Vector3(0.0043f, 0.3332f, 0.1529f), pauldron) } },
                // ---------- Ritter ----------
                new ItemDef { Id = "knight_mace", Class = ChampionClass.Knight, Slot = ShopSlot.Weapon, Name = "Streitkolben & Rundschild",
                    Desc = "Schlichter Streitkolben und ein treuer Holzschild.", Right = "W2S_Mace", Left = "W2S_RoundShield" },
                new ItemDef { Id = "knight_sunblade", Class = ChampionClass.Knight, Slot = ShopSlot.Weapon, Epic = true, Name = "Sonnenklinge",
                    Desc = "Goldschwert und Sonnenschild, umtanzt von Lichtfunken.", Right = "W2S_SunSword", Left = "W2S_SunShield" },
                new ItemDef { Id = "knight_blackknight", Class = ChampionClass.Knight, Slot = ShopSlot.Armor, Name = "Schwarzer Ritter",
                    Desc = "Dunkler Stahl und ein blutroter Wappenrock.", Set = "SchwarzerRitter" },
                new ItemDef { Id = "knight_sunpaladin", Class = ChampionClass.Knight, Slot = ShopSlot.Armor, Epic = true, Name = "Sonnenpaladin",
                    Desc = "Goldrüstung, Federbusch und Sonnen-Schulterplatten.", Set = "Sonnenpaladin",
                    AddOns = new[] {
                        Add(p["A2S_KnightPlume"], HumanBodyBones.Head, new Vector3(0f, 0.292f, 0.005f), Vector3.zero),
                        Add(p["A2S_KnightPauldron"], HumanBodyBones.RightShoulder, knightShoulder, pauldron),
                        Add(p["A2S_KnightPauldron"], HumanBodyBones.LeftShoulder, knightShoulder, pauldron, true),
                        Add(p["Fx_Shimmer"], HumanBodyBones.UpperChest, new Vector3(0f, 0.08f, 0.02f), Vector3.zero),
                    } },
                // ---------- Bogenschütze ----------
                new ItemDef { Id = "archer_longbow", Class = ChampionClass.Archer, Slot = ShopSlot.Weapon, Name = "Langbogen",
                    Desc = "Heller Eibenbogen mit großer Reichweite.", Left = "W2S_Longbow" },
                new ItemDef { Id = "archer_frostbow", Class = ChampionClass.Archer, Slot = ShopSlot.Weapon, Epic = true, Name = "Frostbogen",
                    Desc = "Aus ewigem Eis geschliffen, die Sehne leuchtet kalt.", Left = "W2S_FrostBow" },
                new ItemDef { Id = "archer_autumn", Class = ChampionClass.Archer, Slot = ShopSlot.Armor, Name = "Herbstjäger",
                    Desc = "Rostrot und Senfgelb wie das Laub im Oktober.", Set = "Herbstjaeger" },
                new ItemDef { Id = "archer_shadow", Class = ChampionClass.Archer, Slot = ShopSlot.Armor, Epic = true, Name = "Schattenjäger",
                    Desc = "Violett glühende Säume und ein Umhang aus Schatten.", Set = "Schattenjaeger",
                    AddOns = new[] { Add(p["A2S_ArcherMantle"], HumanBodyBones.UpperChest, new Vector3(0f, 0.175f, 0.002f), new Vector3(1.96f, 0f, 0f)) } },
                // ---------- Magier ----------
                new ItemDef { Id = "mage_gnarled", Class = ChampionClass.Mage, Slot = ShopSlot.Weapon, Name = "Knorrenstab",
                    Desc = "Knorriges Holz, bemoost und voller alter Magie.", Right = "W2S_GnarledStaff" },
                new ItemDef { Id = "mage_flamestaff", Class = ChampionClass.Mage, Slot = ShopSlot.Weapon, Epic = true, Name = "Flammenstab",
                    Desc = "Auf seiner Spitze brennt eine Kugel, die nie erlischt.", Right = "W2S_FlameStaff" },
                new ItemDef { Id = "mage_archmage", Class = ChampionClass.Mage, Slot = ShopSlot.Armor, Name = "Erzmagier",
                    Desc = "Königsblaue Robe mit Silberborten.", Set = "Erzmagier" },
                new ItemDef { Id = "mage_starweaver", Class = ChampionClass.Mage, Slot = ShopSlot.Armor, Epic = true, Name = "Sternenweber",
                    Desc = "Nachtblaue Robe und eine schwebende Sternenkrone.", Set = "Sternenweber",
                    // 6 cm höher als nachgemessen (0,555): schwebt dann sichtbar über der Hutspitze statt sie zu umschließen
                    AddOns = new[] { Add(p["A2S_StarCrown"], HumanBodyBones.Head, new Vector3(0f, 0.615f, -0.1f), Vector3.zero) } },
                // ---------- Schamane ----------
                new ItemDef { Id = "shaman_antlerstaff", Class = ChampionClass.Shaman, Slot = ShopSlot.Weapon, Name = "Geweihstab",
                    Desc = "Ein Hirschgeweih auf festem Holz, mit Leder umwickelt.", Right = "W2S_AntlerStaff" },
                new ItemDef { Id = "shaman_stormtotem", Class = ChampionClass.Shaman, Slot = ShopSlot.Weapon, Epic = true, Name = "Sturmtotem",
                    Desc = "Über der Spitze schwebt ein Blitzstein, der knisternd Funken sprüht.", Right = "W2S_StormTotemStaff" },
                new ItemDef { Id = "shaman_steppenwind", Class = ChampionClass.Shaman, Slot = ShopSlot.Armor, Name = "Steppenwind",
                    Desc = "Sandfarbener Umhang und rostrote Zeichen aus der weiten Steppe.", Set = "Steppenwind" },
                new ItemDef { Id = "shaman_spiritcaller", Class = ChampionClass.Shaman, Slot = ShopSlot.Armor, Epic = true, Name = "Geisterrufer",
                    Desc = "Nachtblau, leuchtende Geisterzeichen und eine Wolfsmaske.", Set = "Geisterrufer",
                    AddOns = new[] {
                        Add(p["A2S_SpiritWolfMask"], HumanBodyBones.Head, new Vector3(0f, 0.278f, 0.062f), Vector3.zero),
                        Add(p["Fx_SpiritWisps"], HumanBodyBones.UpperChest, new Vector3(0f, 0.12f, 0f), Vector3.zero),
                    } },
            };
        }

        private static List<ShopItemSO> BuildItems(Dictionary<string, GameObject> p)
        {
            var list = new List<ShopItemSO>();
            foreach (var d in Defs(p))
            {
                string path = ItemDir + d.Id + ".asset";
                var it = AssetDatabase.LoadAssetAtPath<ShopItemSO>(path);
                if (it == null)
                {
                    it = ScriptableObject.CreateInstance<ShopItemSO>();
                    AssetDatabase.CreateAsset(it, path);
                }
                it.Id = d.Id;
                it.Class = d.Class;
                it.Slot = d.Slot;
                it.Tier = d.Epic ? ShopTier.Epic : ShopTier.Common;
                it.DisplayName = d.Name;
                it.Description = d.Desc;
                it.Price = d.Epic ? 800 : 200;
                it.NetIndex = d.Epic ? 2 : 1;
                it.RightHand = Weapon(p, d.Right);
                it.LeftHand = Weapon(p, d.Left);
                it.Back = new ShopItemSO.WeaponOverride();
                it.Materials = d.Set != null && _swaps.ContainsKey(d.Set) ? _swaps[d.Set] : new ShopItemSO.MaterialSwap[0];
                it.AddOns = d.AddOns ?? new ShopItemSO.AddOn[0];
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + d.Id + ".png");
                if (icon != null) it.Icon = icon;
                EditorUtility.SetDirty(it);
                list.Add(it);
            }
            return list;
        }

        // Waffen-Override ohne eigene Lage: übernimmt Position/Rotation/Größe des Standard-Sockels
        private static ShopItemSO.WeaponOverride Weapon(Dictionary<string, GameObject> p, string key)
        {
            var o = new ShopItemSO.WeaponOverride();
            if (!string.IsNullOrEmpty(key)) o.Prefab = p[key];
            return o;
        }

        private static void BuildCatalog(List<ShopItemSO> items)
        {
            var cat = AssetDatabase.LoadAssetAtPath<ShopCatalogSO>(CatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<ShopCatalogSO>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            cat.Items = new List<ShopItemSO>(items);
            EditorUtility.SetDirty(cat);
            ShopCatalogSO.Instance = cat;
        }

        // ======================================================================================
        // Icons
        // ======================================================================================

        public const int IconSize = 256, Super = 4;

        public static string RenderIcons(bool missingOnly = false)
        {
            var log = new System.Text.StringBuilder();
            var cat = AssetDatabase.LoadAssetAtPath<ShopCatalogSO>(CatalogPath);
            if (cat == null) return "ShopSetup: Katalog fehlt – zuerst „Katalog erzeugen“.\n";
            foreach (var it in cat.Items)
            {
                if (it == null) continue;
                string path = IconDir + it.Id + ".png";
                if (missingOnly && File.Exists(path)) continue;
                try
                {
                    if (it.Slot == ShopSlot.Weapon) RenderWeaponIcon(it, path);
                    else RenderArmorIcon(it, path);
                }
                catch (System.Exception e) { Debug.LogException(e); continue; }
                ImportSprite(path);
                it.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                EditorUtility.SetDirty(it);
                log.AppendLine("Icon " + it.Id);
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static void ImportSprite(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null || imp.textureType == TextureImporterType.Sprite && !imp.mipmapEnabled) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = 256;
            imp.SaveAndReimport();
        }

        private static readonly Vector3 Stage = new Vector3(0f, -700f, 0f);

        // Waffen-Icon: Waffe(n) schräg (≈ 40° gekippt) vor der Kamera; Ritter: Schild hinten, Klinge davor
        private static void RenderWeaponIcon(ShopItemSO it, string path)
        {
            var root = new GameObject("__ShopIconWeapon") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = Stage;
            try
            {
                var main = it.RightHand != null && it.RightHand.Prefab != null ? it.RightHand.Prefab : it.LeftHand != null ? it.LeftHand.Prefab : null;
                var second = it.RightHand != null && it.RightHand.Prefab != null && it.LeftHand != null ? it.LeftHand.Prefab : null;
                if (main == null) return;
                if (second != null)
                {
                    // Schild: Vorderseite (−Z) zur Kamera, leicht gedreht, hinten links
                    var s = Spawn(second, root.transform);
                    s.transform.localPosition = new Vector3(-0.12f, 0.05f, 0.25f);
                    s.transform.localRotation = Quaternion.Euler(0f, 15f, 8f);
                }
                var w = Spawn(main, root.transform);
                bool bow = it.Class == ChampionClass.Archer;
                // Klinge/Kopf nach rechts oben; Bogen: Wölbung (+X) nach links, sodass die Sehne rechts liegt
                w.transform.localRotation = bow ? Quaternion.Euler(0f, 180f, -38f) : Quaternion.Euler(0f, 180f, -40f) * Quaternion.Euler(0f, -20f, 0f);
                w.transform.localPosition = second != null ? new Vector3(0.1f, 0f, -0.1f) : Vector3.zero;
                SimulateParticles(root);
                var b = RenderBounds(root);
                // Kamera blickt in +Z (von −Z auf die Vorderseite)
                Shoot(path, b.center, Vector3.back, Mathf.Max(b.size.x, b.size.y) * 1.08f, 0f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject Spawn(GameObject prefab, Transform parent)
        {
            var go = Object.Instantiate(prefab, parent);
            go.hideFlags = HideFlags.HideAndDontSave;
            return go;
        }

        private static void SimulateParticles(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform.parent != null && ps.transform.parent.GetComponent<ParticleSystem>() != null) continue;
                ps.Simulate(1.5f, true, true, true);
            }
        }

        // Grenzen aller sichtbaren Mesh-Renderer (ohne Partikel)
        private static Bounds RenderBounds(GameObject root)
        {
            var b = new Bounds();
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // Rüstungs-Icon: Figur im Skin (Idle-Pose, Standardwaffen), Oberkörper bis zur Hüfte, leicht von rechts vorn
        private static void RenderArmorIcon(ShopItemSO it, string path)
        {
            var preview = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Champions/Preview_" + it.Class + ".prefab");
            var cv = preview != null ? preview.GetComponentInChildren<ChampionVisual>(true) : null;
            if (cv == null) return;
            using (var s = new ChampionPoseSampler(cv, Stage))
            {
                s.Sample(new PoseSample { Label = "Idle" }, 0f);
                var skin = ChampionSkin.For(s.Visual);
                skin.Apply(null, it);
                foreach (var t in s.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                // Handwaffen ausblenden: das Icon zeigt die Rüstung (Rücken-Items wie der Köcher bleiben)
                foreach (var w in new[] { s.Visual.RightHandInstance, s.Visual.LeftHandInstance })
                    if (w != null) w.gameObject.SetActive(false);
                s.Sample(new PoseSample { Label = "Idle" }, 0f);
                SimulateParticles(s.Root);
                var a = s.Animator;
                var root = s.Root.transform;
                float top = RenderBoundsBody(s.Root, s.Visual).max.y;
                float hips = a.GetBoneTransform(HumanBodyBones.Hips).position.y;
                float bottom = hips - 0.05f * root.lossyScale.y;
                float h = top - bottom;
                Vector3 head = a.GetBoneTransform(HumanBodyBones.Head).position;
                Vector3 center = new Vector3(head.x, (top + bottom) * 0.5f, head.z);
                Vector3 dir = Quaternion.Euler(0f, 18f, 0f) * root.forward;
                Shoot(path, center, dir, h * 1.06f, 0.04f * h);
                skin.ResetToStandard();
            }
        }

        // Körper + Zusatzteile (ohne Waffen, ohne Partikel) für den Bildausschnitt
        private static Bounds RenderBoundsBody(GameObject root, ChampionVisual v)
        {
            var b = new Bounds();
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                var t = r.transform;
                if (v.RightHandInstance != null && t.IsChildOf(v.RightHandInstance) || v.LeftHandInstance != null && t.IsChildOf(v.LeftHandInstance) || v.BackInstance != null && t.IsChildOf(v.BackInstance)) continue;
                var rb = r.bounds;
                if (first) { b = rb; first = false; }
                else b.Encapsulate(rb);
            }
            return b;
        }

        // Rendert mit transparentem Hintergrund (Schwarz/Weiß-Differenz wie PortraitRenderer, 4×4-Supersampling).
        // dir = Richtung vom Motiv zur Kamera; frame = sichtbare Höhe/Breite in Metern; lift = Kamera leicht über dem Motiv.
        private static void Shoot(string path, Vector3 center, Vector3 dir, float frame, float lift)
        {
            int n = IconSize * Super;
            var camGo = new GameObject("__ShopIconCam") { hideFlags = HideFlags.HideAndDontSave };
            var key = new GameObject("__ShopIconKey") { hideFlags = HideFlags.HideAndDontSave };
            var fill = new GameObject("__ShopIconFill") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(n, n, 24, RenderTextureFormat.ARGB32);
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 18f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 50f;
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                float dist = frame * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                cam.transform.position = center + dir.normalized * dist + Vector3.up * lift;
                cam.transform.LookAt(center);

                var l1 = key.AddComponent<Light>();
                l1.type = LightType.Directional;
                l1.intensity = 1.3f;
                l1.color = new Color(1f, 0.97f, 0.92f);
                key.transform.rotation = Quaternion.LookRotation(-(dir.normalized + Vector3.up * 0.8f + Vector3.Cross(Vector3.up, dir).normalized * 0.6f));
                var l2 = fill.AddComponent<Light>();
                l2.type = LightType.Directional;
                l2.intensity = 0.5f;
                l2.color = new Color(0.85f, 0.9f, 1f);
                fill.transform.rotation = Quaternion.LookRotation(-(dir.normalized + Vector3.up * 0.2f - Vector3.Cross(Vector3.up, dir).normalized * 0.8f));

                var black = Grab(cam, rt, Color.black);
                var white = Grab(cam, rt, Color.white);
                var outPx = new Color[IconSize * IconSize];
                for (int y = 0; y < IconSize; y++)
                    for (int x = 0; x < IconSize; x++)
                    {
                        float r = 0f, g = 0f, b = 0f, al = 0f;
                        for (int sy = 0; sy < Super; sy++)
                            for (int sx = 0; sx < Super; sx++)
                            {
                                int i = (y * Super + sy) * n + (x * Super + sx);
                                Color cb = black[i], cw = white[i];
                                float alpha = 1f - Mathf.Clamp01(((cw.r - cb.r) + (cw.g - cb.g) + (cw.b - cb.b)) / 3f);
                                r += cb.r; g += cb.g; b += cb.b; al += alpha;
                            }
                        float k = 1f / (Super * Super);
                        al *= k;
                        outPx[y * IconSize + x] = al > 0.001f ? new Color(Mathf.Clamp01(r * k / al), Mathf.Clamp01(g * k / al), Mathf.Clamp01(b * k / al), al) : new Color(0f, 0f, 0f, 0f);
                    }
                var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                tex.SetPixels(outPx);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
            }
            finally
            {
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
