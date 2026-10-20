using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // UI-Helfer für Tippen-zum-Bauen (Mobil-Version, Paket B): prozedurale Knopf-Grafiken (Scheibe, Haken, Kreuz – eigene
    // Laufzeit-Texturen, keine Assets), „liegt der Finger auf UI?“, unsichtbare Trefferflächen ≥ 48 dp für kleine Knöpfe
    // und Verschiebung eines Rechtecks in den sicheren Bereich. Nur im Touch-Modus benutzt.
    public static class TouchBuildUi
    {
        private const int TexSize = 128;
        private static Sprite _disc, _check, _cross;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _disc = _check = _cross = null;
            _hits.Clear();
        }

        // Weiße Scheibe mit dunklem Rand (wird über Image.color eingefärbt)
        public static Sprite DiscSprite
        {
            get
            {
                if (_disc == null) _disc = MakeSprite("TouchDisc", (x, y) =>
                {
                    float r = Mathf.Sqrt(x * x + y * y);
                    float a = Mathf.Clamp01((0.98f - r) * TexSize * 0.5f);   // weiche Außenkante
                    float rim = Mathf.Clamp01((r - 0.84f) * TexSize * 0.5f);  // 1 = Rand
                    float v = Mathf.Lerp(1f, 0.18f, rim);
                    return new Color(v, v, v, a);
                });
                return _disc;
            }
        }

        // Weißer Haken (Linien in normierten Koordinaten −1..1)
        public static Sprite CheckSprite
        {
            get
            {
                if (_check == null) _check = MakeSprite("TouchCheck", (x, y) =>
                    Stroke(Mathf.Min(SegDist(x, y, -0.5f, 0.02f, -0.14f, -0.36f), SegDist(x, y, -0.14f, -0.36f, 0.52f, 0.4f)), 0.2f));
                return _check;
            }
        }

        // Weißes Kreuz
        public static Sprite CrossSprite
        {
            get
            {
                if (_cross == null) _cross = MakeSprite("TouchCross", (x, y) =>
                    Stroke(Mathf.Min(SegDist(x, y, -0.4f, -0.4f, 0.4f, 0.4f), SegDist(x, y, -0.4f, 0.4f, 0.4f, -0.4f)), 0.2f));
                return _cross;
            }
        }

        private static Color Stroke(float dist, float width)
        {
            float a = Mathf.Clamp01((width * 0.5f - dist) * TexSize * 0.5f + 0.5f);
            return new Color(1f, 1f, 1f, a);
        }

        private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            Vector2 p = new Vector2(px, py), a = new Vector2(ax, ay), ab = new Vector2(bx - ax, by - ay);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        private static Sprite MakeSprite(string name, System.Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color[TexSize * TexSize];
            for (int j = 0; j < TexSize; j++)
                for (int i = 0; i < TexSize; i++)
                {
                    float x = (i + 0.5f) / TexSize * 2f - 1f, y = (j + 0.5f) / TexSize * 2f - 1f;
                    px[j * TexSize + i] = pixel(x, y);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        // ---------------- Finger auf UI? ----------------

        private static readonly List<RaycastResult> _hits = new List<RaycastResult>();

        // Liegt der Bildschirmpunkt auf einem UI-Element (raycastTarget)? Eigener Raycast statt
        // IsPointerOverGameObject(id), das je nach Eingabe-Modul andere Finger-IDs erwartet.
        public static bool IsOverUI(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = screenPos };
            _hits.Clear();
            es.RaycastAll(data, _hits);
            bool over = false;
            for (int i = 0; i < _hits.Count; i++)
                if (_hits[i].gameObject != null && _hits[i].module is GraphicRaycaster) { over = true; break; }
            _hits.Clear();
            return over;
        }

        // ---------------- Trefferflächen ----------------

        public const string HitPadName = "TouchHitPad";

        // Skalierung Canvas-Einheiten → Bildschirm-Pixel für ein UI-Element
        public static float CanvasScale(Component c)
        {
            var canvas = c != null ? c.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return 1f;
            return Mathf.Max(0.01f, canvas.rootCanvas.scaleFactor);
        }

        // Unsichtbare Trefferfläche (Kind mit transparentem Image), damit das Ziel mindestens minDp × minDp groß ist.
        // Treffer auf dem Kind lösen den Button des Elternobjekts aus (Event-Bubbling). Ist das Ziel groß genug, wird
        // die Fläche ausgeblendet. Mehrfach aufrufbar (passt die Größe an, z. B. nach Änderung der UI-Skalierung).
        public static void EnsureHitPad(RectTransform target, float minDp)
        {
            if (target == null) return;
            float minUnits = MobilePlatform.DpToPixels(minDp) / CanvasScale(target);
            Vector2 size = target.rect.size;
            Vector2 want = new Vector2(Mathf.Max(size.x, minUnits), Mathf.Max(size.y, minUnits));
            bool needed = want.x > size.x + 0.5f || want.y > size.y + 0.5f;

            var pad = target.Find(HitPadName) as RectTransform;
            if (pad == null)
            {
                if (!needed) return;
                var go = new GameObject(HitPadName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                pad = (RectTransform)go.transform;
                pad.SetParent(target, false);
                pad.SetAsFirstSibling(); // hinter der eigenen Grafik
                go.GetComponent<LayoutElement>().ignoreLayout = true;
                var img = go.GetComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = true;
                img.canvasRenderer.cullTransparentMesh = false; // sonst kein Raycast-Treffer bei Alpha 0
                pad.anchorMin = pad.anchorMax = new Vector2(0.5f, 0.5f);
                pad.pivot = new Vector2(0.5f, 0.5f);
                pad.anchoredPosition = Vector2.zero;
            }
            if (pad.gameObject.activeSelf != needed) pad.gameObject.SetActive(needed);
            if (pad.anchoredPosition != Vector2.zero) pad.anchoredPosition = Vector2.zero; // Anker = Mitte des Ziels
            if (pad.sizeDelta != want) pad.sizeDelta = want;
        }

        // ---------------- Sicherer Bereich ----------------

        private static readonly Vector3[] _corners = new Vector3[4];

        // Bildschirm-Rechteck (Pixel) eines UI-Elements; Overlay-Canvas: Weltkoordinaten = Pixel, sonst über die Kamera
        public static Rect ScreenRect(RectTransform rt)
        {
            rt.GetWorldCorners(_corners);
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        // Verschiebung (Pixel), die das Rechteck r in den sicheren Bereich (mit Rand marginPx) bringt; 0, wenn es schon drin liegt.
        // Ist r größer als der Bereich, gewinnt unten bzw. links.
        public static Vector2 SafeAreaShift(Rect r, float marginPx)
        {
            Rect s = MobilePlatform.SafeArea;
            float xMin = s.xMin + marginPx, xMax = s.xMax - marginPx, yMin = s.yMin + marginPx, yMax = s.yMax - marginPx;
            Vector2 d = Vector2.zero;
            if (r.xMax > xMax) d.x = xMax - r.xMax;
            if (r.xMin + d.x < xMin) d.x = xMin - r.xMin;
            if (r.yMax > yMax) d.y = yMax - r.yMax;
            if (r.yMin + d.y < yMin) d.y = yMin - r.yMin;
            return d;
        }
    }
}
