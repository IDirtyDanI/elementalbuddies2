using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Bildschirm-Feedback auf eigenem Overlay-Canvas (unter dem HUD): Schadenszahlen, rote Treffer-Vignette,
    // großes Banner (Boss besiegt, Serien). Wird vom FeedbackDirector zur Laufzeit erzeugt.
    public class FeedbackCanvas : MonoBehaviour
    {
        private const int PoolSize = 64;
        private const float RefHeight = 1080f;

        private class Number
        {
            public TextMeshProUGUI Text;
            public Vector3 World;
            public float Start, Duration, Size, Rise, Drift;
            public Color Color;
            public bool Active;
        }

        private Canvas _canvas;
        private RectTransform _numberLayer;
        private readonly List<Number> _numbers = new List<Number>();
        private int _next;
        private TMP_FontAsset _font;

        private Image _vignette;
        private float _vignetteFlash;   // kurzer Treffer-Blitz 0..1
        private float _vignetteLow;     // Dauerpuls bei wenig Leben 0..1

        private TextMeshProUGUI _banner, _bannerSub;
        private CanvasGroup _bannerGroup;
        private float _bannerStart = -10f, _bannerDuration;

        public static FeedbackCanvas Create(TMP_FontAsset font)
        {
            var go = new GameObject("FeedbackCanvas");
            var fc = go.AddComponent<FeedbackCanvas>();
            fc.Build(font);
            return fc;
        }

        private void Build(TMP_FontAsset font)
        {
            _font = font != null ? font : TMP_Settings.defaultFontAsset;
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = -20; // unter HUD und Menüs

            _vignette = new GameObject("Vignette", typeof(RectTransform)).AddComponent<Image>();
            _vignette.transform.SetParent(transform, false);
            Stretch(_vignette.rectTransform);
            _vignette.sprite = MakeVignetteSprite();
            _vignette.color = new Color(0.75f, 0.05f, 0.03f, 0f);
            _vignette.raycastTarget = false;

            _numberLayer = new GameObject("Numbers", typeof(RectTransform)).GetComponent<RectTransform>();
            _numberLayer.SetParent(transform, false);
            Stretch(_numberLayer);
            for (int i = 0; i < PoolSize; i++)
            {
                var t = new GameObject("Num", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                t.transform.SetParent(_numberLayer, false);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
                t.rectTransform.sizeDelta = new Vector2(240f, 80f);
                t.alignment = TextAlignmentOptions.Center;
                t.font = _font;
                t.fontStyle = FontStyles.Bold;
                t.raycastTarget = false;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.outlineWidth = 0.22f;
                t.outlineColor = new Color32(30, 16, 8, 255);
                t.gameObject.SetActive(false);
                _numbers.Add(new Number { Text = t });
            }

            var bannerRoot = new GameObject("Banner", typeof(RectTransform));
            bannerRoot.transform.SetParent(transform, false);
            var brt = bannerRoot.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0.6f);
            brt.anchorMax = new Vector2(1f, 0.76f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            _bannerGroup = bannerRoot.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.blocksRaycasts = false;
            _banner = MakeBannerText(brt, "Title", new Vector2(0f, 0.35f), new Vector2(1f, 1f), 72f);
            _bannerSub = MakeBannerText(brt, "Sub", new Vector2(0f, 0f), new Vector2(1f, 0.35f), 34f);
        }

        private TextMeshProUGUI MakeBannerText(RectTransform parent, string name, Vector2 min, Vector2 max, float size)
        {
            var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            t.rectTransform.anchorMin = min;
            t.rectTransform.anchorMax = max;
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            t.alignment = TextAlignmentOptions.Center;
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(40, 20, 6, 255);
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Eigene, prozedural erzeugte Vignette (Rand deckend, Mitte frei)
        private static Sprite MakeVignetteSprite()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FeedbackVignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(u * u * 0.85f + v * v * 1.1f);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.15f, d));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        // ---------------- Schnittstelle ----------------

        public void ShowNumber(Vector3 world, float amount, Color color, float size, float duration = 0.8f)
        {
            if (amount < 0.5f) return;
            var n = _numbers[_next];
            _next = (_next + 1) % _numbers.Count;
            n.World = world;
            n.Start = Time.unscaledTime;
            n.Duration = duration;
            n.Size = size;
            n.Color = color;
            n.Rise = 70f + Random.Range(-10f, 15f);
            n.Drift = Random.Range(-28f, 28f);
            n.Active = true;
            n.Text.text = Format(amount);
            n.Text.gameObject.SetActive(true);
            n.Text.transform.SetAsLastSibling(); // neuere Zahlen oben
        }

        private static string Format(float v)
        {
            if (v >= 10000f) return (v / 1000f).ToString("0") + "k";
            if (v >= 1000f) return (v / 1000f).ToString("0.0") + "k";
            return Mathf.RoundToInt(v).ToString();
        }

        public void FlashVignette(float strength) => _vignetteFlash = Mathf.Max(_vignetteFlash, Mathf.Clamp01(strength));
        public void SetLowHealth(float amount) => _vignetteLow = Mathf.Clamp01(amount);

        public void ShowBanner(string title, string subtitle, Color color, float duration = 2.6f)
        {
            _banner.text = title;
            _banner.color = color;
            _bannerSub.text = subtitle ?? "";
            _bannerStart = Time.unscaledTime;
            _bannerDuration = duration;
        }

        // Untertitel eines laufenden Banners ändern (Countdown), ohne die Animation neu zu starten
        public void SetBannerSubtitle(string subtitle)
        {
            if (_bannerSub != null) _bannerSub.text = subtitle ?? "";
        }

        void LateUpdate()
        {
            float now = Time.unscaledTime;
            float scale = Screen.height / RefHeight;

            // Vignette: Treffer-Blitz klingt schnell ab, Dauerpuls bei wenig Leben
            _vignetteFlash = Mathf.Max(0f, _vignetteFlash - Time.unscaledDeltaTime * 2.8f);
            float pulse = _vignetteLow > 0f ? _vignetteLow * (0.55f + 0.2f * Mathf.Sin(now * 6.5f)) : 0f;
            var vc = _vignette.color;
            vc.a = Mathf.Clamp01(Mathf.Max(_vignetteFlash * 0.9f, pulse));
            _vignette.color = vc;

            // Banner: Pop-in, halten, ausblenden
            float bt = now - _bannerStart;
            if (bt < _bannerDuration)
            {
                float a = Mathf.Min(1f, bt / 0.15f) * Mathf.Clamp01((_bannerDuration - bt) / 0.5f);
                _bannerGroup.alpha = a;
                float pop = bt < 0.25f ? Mathf.Lerp(1.35f, 1f, EaseOut(bt / 0.25f)) : 1f;
                _bannerGroup.transform.localScale = Vector3.one * pop;
            }
            else if (_bannerGroup.alpha > 0f) _bannerGroup.alpha = 0f;

            var cam = Camera.main;
            float canvasScale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            foreach (var n in _numbers)
            {
                if (!n.Active) continue;
                float t = (now - n.Start) / n.Duration;
                if (t >= 1f || cam == null)
                {
                    n.Active = false;
                    n.Text.gameObject.SetActive(false);
                    continue;
                }
                Vector3 sp = cam.WorldToScreenPoint(n.World);
                if (sp.z < 0f) { n.Text.alpha = 0f; continue; }
                float e = EaseOut(t);
                sp.x += n.Drift * e * scale;
                sp.y += n.Rise * e * scale;
                n.Text.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y) / canvasScale;
                float pop = t < 0.12f ? Mathf.Lerp(1.5f, 1f, t / 0.12f) : 1f;
                n.Text.fontSize = n.Size * scale * pop;
                var c = n.Color;
                c.a = t > 0.65f ? Mathf.InverseLerp(1f, 0.65f, t) : 1f;
                n.Text.color = c;
            }
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    }
}
