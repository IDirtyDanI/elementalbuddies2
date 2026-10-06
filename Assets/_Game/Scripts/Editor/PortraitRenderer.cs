using System.IO;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Porträts (256², transparenter Hintergrund) der Champions und Händler aus den Heroes2-Modellen rendern:
    // Modell in der Idle-Pose (mit Waffen/Requisiten, Hand-Layer, Trage-IK) auf versteckter Kopie, Brustbild von vorn,
    // je ein Render auf Schwarz und Weiß → Alpha aus der Differenz, 4×4-Supersampling.
    // Menü: BuddyTD → Porträts neu rendern (Champions + Händler). Überschreibt UI/Icons/portrait_champion_* und portrait_merchant_*.
    public static class PortraitRenderer
    {
        public const string IconDir = "Assets/_Game/UI/Icons/";
        public const int Size = 256, Super = 4;

        [MenuItem("BuddyTD/Porträts neu rendern (Champions + Händler)")]
        public static void RenderAllMenu()
        {
            Debug.Log(RenderAll());
        }

        public static string RenderAll()
        {
            var log = new System.Text.StringBuilder();
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Player.prefab");
            string[] champ = { "Visual_Mage", "Visual_Knight", "Visual_Archer" };
            string[] champFile = { "mage", "knight", "archer" };
            for (int i = 0; i < champ.Length; i++)
            {
                var t = player.transform.Find(champ[i]);
                var cv = t != null ? t.GetComponent<ChampionVisual>() : null;
                if (cv == null) continue;
                Render(cv, new PoseSample { Label = "Idle" }, IconDir + "portrait_champion_" + champFile[i] + ".png");
                log.AppendLine("portrait_champion_" + champFile[i]);
            }
            foreach (var kv in MerchantFigures.SceneFigures())
            {
                string file = "portrait_merchant_" + kv.Key.ToString().ToLowerInvariant() + ".png";
                Render(kv.Value, new PoseSample { Label = "Idle", BaseState = "Idle" }, IconDir + file);
                log.AppendLine(file);
            }
            AssetDatabase.Refresh();
            return "PortraitRenderer:\n" + log;
        }

        public static void Render(ChampionVisual source, PoseSample pose, string path)
        {
            int n = Size * Super;
            var camGo = new GameObject("__PortraitCam") { hideFlags = HideFlags.HideAndDontSave };
            var key = new GameObject("__PortraitKey") { hideFlags = HideFlags.HideAndDontSave };
            var fill = new GameObject("__PortraitFill") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(n, n, 24, RenderTextureFormat.ARGB32);
            try
            {
                var l1 = key.AddComponent<Light>();
                l1.type = LightType.Directional;
                l1.intensity = 1.25f;
                l1.color = new Color(1f, 0.97f, 0.92f);
                key.transform.rotation = Quaternion.Euler(30f, 150f, 0f);   // von vorn links oben
                var l2 = fill.AddComponent<Light>();
                l2.type = LightType.Directional;
                l2.intensity = 0.45f;
                l2.color = new Color(0.85f, 0.9f, 1f);
                fill.transform.rotation = Quaternion.Euler(10f, 230f, 0f);  // Aufhellung von vorn rechts

                using (var s = new ChampionPoseSampler(source, new Vector3(0f, -600f, 0f)))
                {
                    s.Sample(pose, 0f);
                    var a = s.Animator;
                    var root = s.Root.transform;
                    float scale = root.lossyScale.y;
                    Vector3 head = a.GetBoneTransform(HumanBodyBones.Head).position;
                    Vector3 chest = a.GetBoneTransform(HumanBodyBones.UpperChest) != null ? a.GetBoneTransform(HumanBodyBones.UpperChest).position : a.GetBoneTransform(HumanBodyBones.Chest).position;
                    // Bildausschnitt: Scheitel (~Kopf + 0,24 m) bis unter die Brust; Mitte leicht über dem Hals
                    Vector3 center = Vector3.Lerp(chest, head, 0.75f) + Vector3.up * 0.06f * scale;
                    float height = 0.8f * scale;
                    var cam = camGo.AddComponent<Camera>();
                    cam.fieldOfView = 18f;
                    cam.nearClipPlane = 0.05f;
                    cam.targetTexture = rt;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    float dist = height * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    Vector3 dir = Quaternion.Euler(0f, 12f, 0f) * root.forward; // leicht von rechts vorn
                    cam.transform.position = center + dir * dist + Vector3.up * 0.06f * scale;
                    cam.transform.LookAt(center);

                    var black = Grab(cam, rt, Color.black);
                    var white = Grab(cam, rt, Color.white);
                    var outPx = new Color[Size * Size];
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                        {
                            float r = 0f, g = 0f, b = 0f, al = 0f;
                            for (int sy = 0; sy < Super; sy++)
                                for (int sx = 0; sx < Super; sx++)
                                {
                                    int i = (y * Super + sy) * n + (x * Super + sx);
                                    Color cb = black[i], cw = white[i];
                                    float alpha = 1f - Mathf.Clamp01(((cw.r - cb.r) + (cw.g - cb.g) + (cw.b - cb.b)) / 3f);
                                    // vormultipliziert: Farbe auf Schwarz = Farbe × Alpha
                                    r += cb.r; g += cb.g; b += cb.b; al += alpha;
                                }
                            float k = 1f / (Super * Super);
                            al *= k;
                            outPx[y * Size + x] = al > 0.001f ? new Color(Mathf.Clamp01(r * k / al), Mathf.Clamp01(g * k / al), Mathf.Clamp01(b * k / al), al) : new Color(0f, 0f, 0f, 0f);
                        }
                    var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                    tex.SetPixels(outPx);
                    tex.Apply();
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    cam.targetTexture = null;
                }
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
