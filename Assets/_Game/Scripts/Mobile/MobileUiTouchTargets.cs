using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Touch-Trefferflächen (Paket U): Jedes aktive Bedienelement (Button, Toggle, Slider, Eingabefeld …) eines Canvas, das
    // kleiner als 48 dp ist, bekommt ein unsichtbares Kind „TouchPad“ (transparentes Image als Raycast-Ziel), das die
    // Trefferfläche vergrößert. Die Optik bleibt gleich. Nachbarn begrenzen das Wachstum auf die halbe Lücke
    // (MobileUiMath.TouchPadding) – vergrößerte Flächen überschneiden sich nie, und es wird nie in ein anderes
    // Bedienelement hineingetippt. Bildlaufleisten bekommen keine Fläche (sie lägen über der Liste).
    // Prüft alle 0,5 s (Layouts bewegen sich selten); Pads wandern als Kinder ohnehin mit. Nur im Touch-Modus.
    [DisallowMultipleComponent]
    public class MobileUiTouchTargets : MonoBehaviour
    {
        public const string PadName = "TouchPad";
        private const float Interval = 0.5f;

        private float _next;
        private readonly List<Selectable> _sel = new List<Selectable>();
        private Rect[] _rects = new Rect[64];
        private readonly Dictionary<Selectable, RectTransform> _pads = new Dictionary<Selectable, RectTransform>();
        private readonly List<Selectable> _dead = new List<Selectable>();
        private static readonly Vector3[] _corners = new Vector3[4];

        void LateUpdate()
        {
            if (!MobilePlatform.TouchMode || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            Refresh();
        }

        private void Refresh()
        {
            _sel.Clear();
            GetComponentsInChildren(false, _sel);
            for (int i = _sel.Count - 1; i >= 0; i--)
            {
                var s = _sel[i];
                if (s == null || !s.IsActive() || s is Scrollbar) _sel.RemoveAt(i);
            }
            if (_rects.Length < _sel.Count) _rects = new Rect[Mathf.NextPowerOfTwo(_sel.Count)];
            for (int i = 0; i < _sel.Count; i++) _rects[i] = ScreenRect((RectTransform)_sel[i].transform);

            float min = MobilePlatform.DpToPixels(MobilePlatform.MinTouchTargetDp);
            var minSize = new Vector2(min, min);
            for (int i = 0; i < _sel.Count; i++)
            {
                Rect r = _rects[i];
                if (r.width <= 1f || r.height <= 1f) continue;
                // eigenes Rechteck ausblenden: Overlaps(r) lässt es ohnehin aus
                Vector4 pad = MobileUiMath.TouchPadding(r, minSize, _rects, _sel.Count);
                SetPad(_sel[i], pad);
            }

            // Pads zerstörter Bedienelemente vergessen
            _dead.Clear();
            foreach (var kv in _pads) if (kv.Key == null || kv.Value == null) _dead.Add(kv.Key);
            foreach (var d in _dead) _pads.Remove(d);
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            rt.GetWorldCorners(_corners); // Overlay-Canvas: Welt = Bildschirmpixel
            float xMin = Mathf.Min(_corners[0].x, _corners[2].x), xMax = Mathf.Max(_corners[0].x, _corners[2].x);
            float yMin = Mathf.Min(_corners[0].y, _corners[2].y), yMax = Mathf.Max(_corners[0].y, _corners[2].y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void SetPad(Selectable s, Vector4 padPx)
        {
            _pads.TryGetValue(s, out var pad);
            bool need = padPx.x > 0.5f || padPx.y > 0.5f || padPx.z > 0.5f || padPx.w > 0.5f;
            if (!need)
            {
                if (pad != null && pad.gameObject.activeSelf) pad.gameObject.SetActive(false);
                return;
            }
            if (pad == null)
            {
                var existing = s.transform.Find(PadName);
                pad = existing != null ? (RectTransform)existing : CreatePad(s.transform);
                _pads[s] = pad;
            }
            if (!pad.gameObject.activeSelf) pad.gameObject.SetActive(true);
            // Pixel → lokale Einheiten des Bedienelements
            Vector3 ls = s.transform.lossyScale;
            float sx = Mathf.Max(0.0001f, Mathf.Abs(ls.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(ls.y));
            var mn = new Vector2(-padPx.x / sx, -padPx.y / sy);
            var mx = new Vector2(padPx.z / sx, padPx.w / sy);
            if ((pad.offsetMin - mn).sqrMagnitude > 0.25f) pad.offsetMin = mn;
            if ((pad.offsetMax - mx).sqrMagnitude > 0.25f) pad.offsetMax = mx;
        }

        private static RectTransform CreatePad(Transform parent)
        {
            var go = new GameObject(PadName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
            go.GetComponent<CanvasRenderer>().cullTransparentMesh = true; // unsichtbar, aber Raycast-Ziel
            return rt;
        }
    }
}
