using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Sicherer Bereich für einen Wurzel-Canvas (Paket U): legt das Kind „SafeArea“ an, das genau den sicheren Bereich
    // (Screen.safeArea – Notch, Dynamic Island, Home-Indikator, abgerundete Ecken) abdeckt, und hängt alle bisherigen und
    // später dazukommenden Kinder des Canvas hinein (Reihenfolge bleibt). Alle Anker beziehen sich damit auf den sicheren
    // Bereich, ohne dass ein Prefab geändert werden muss.
    //
    // Sonderfälle:
    //  - Vollbild-Hintergründe (über den ganzen Canvas gestreckt, mit eigener Grafik – Pause, Kartenwahl, Game Over,
    //    Menü-Overlays) decken weiter den ganzen Bildschirm ab. Ist ihr mittig verankerter Inhalt größer als der sichere
    //    Bereich (Pause-Menü 1560 × 940 auf dem Telefon), wird das Overlay als Ganzes verkleinert.
    //  - Reine Zierflächen, die an einer Kante anliegen (Schatten links im Hauptmenü), wachsen bis zum Bildschirmrand.
    // Wer ein bestimmtes Kind des Canvas sucht (canvas.transform.Find("TopBar")), nimmt FindInCanvas.
    // Nur im Touch-Modus angehängt (MobileUiRuntime, TeamHUD); der PC sieht die Hierarchie unverändert.
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        public const string ContainerName = "SafeArea";

        [Tooltip("Ränder links/rechts beachten.")]
        public bool Horizontal = true;
        [Tooltip("Ränder oben/unten beachten.")]
        public bool Vertical = true;
        [Tooltip("Abstand großer Overlays zum sicheren Rand (Canvas-Einheiten).")]
        public float OverlayMargin = 16f;

        private RectTransform _root, _content;
        private Vector2Int _lastScreen;
        private Rect _lastSafe;
        private Vector2 _lastRootSize;
        private Vector4 _insetUnits; // links, unten, rechts, oben (Canvas-Einheiten)

        private class Overlay
        {
            public RectTransform Rt;
            public bool WasActive;
            public int MeasureFrames;
            public float Fit = 1f;
        }

        private class Bleed
        {
            public RectTransform Rt;
            public Vector2 BaseMin, BaseMax;
            public bool Left, Right, Bottom, Top;
        }

        private readonly List<Overlay> _overlays = new List<Overlay>();
        private readonly List<Bleed> _bleeds = new List<Bleed>();
        private static readonly Vector3[] _corners = new Vector3[4];

        public RectTransform Content => _content;

        public static SafeAreaFitter Attach(Canvas canvas, bool horizontal = true, bool vertical = true)
        {
            if (canvas == null || !MobilePlatform.TouchMode) return null;
            canvas = canvas.rootCanvas;
            var f = canvas.GetComponent<SafeAreaFitter>();
            if (f != null) return f;
            f = canvas.gameObject.AddComponent<SafeAreaFitter>();
            f.Horizontal = horizontal;
            f.Vertical = vertical;
            f.Build();
            return f;
        }

        // Inhaltsbereich des Canvas (SafeArea-Kind, sonst die Wurzel selbst)
        public static RectTransform ContentOf(Transform canvasRoot)
        {
            if (canvasRoot == null) return null;
            var f = canvasRoot.GetComponent<SafeAreaFitter>();
            if (f != null && f._content != null) return f._content;
            return canvasRoot as RectTransform;
        }

        // Direktes Kind des Canvas – auch wenn es in den SafeArea-Bereich umgehängt wurde
        public static Transform FindInCanvas(Transform canvasRoot, string name)
        {
            if (canvasRoot == null) return null;
            var t = canvasRoot.Find(name);
            if (t != null) return t;
            var c = canvasRoot.Find(ContainerName);
            return c != null ? c.Find(name) : null;
        }

        private void Build()
        {
            _root = (RectTransform)transform;
            if (_content != null) return;
            var go = new GameObject(ContainerName, typeof(RectTransform));
            _content = (RectTransform)go.transform;
            _content.SetParent(_root, false);
            _content.anchorMin = Vector2.zero;
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 0.5f);
            _content.offsetMin = _content.offsetMax = Vector2.zero;
            _content.localScale = Vector3.one;
            _content.SetAsFirstSibling();
            Adopt(true);
            ApplyInsets(true);
        }

        // Kinder der Wurzel in den Bereich umhängen; vor dem Bereich liegende (SetAsFirstSibling) bleiben darunter
        private readonly List<Transform> _move = new List<Transform>();
        private readonly List<bool> _below = new List<bool>();

        private void Adopt(bool initial)
        {
            if (_root.childCount <= 1) return;
            int contentIndex = _content.GetSiblingIndex();
            _move.Clear();
            _below.Clear();
            for (int i = 0; i < _root.childCount; i++)
            {
                var c = _root.GetChild(i);
                if (c == _content) continue;
                _move.Add(c);
                _below.Add(!initial && i < contentIndex);
            }
            int front = 0;
            for (int i = 0; i < _move.Count; i++)
            {
                var c = _move[i];
                c.SetParent(_content, false); // hängt hinten an = bleibt über allem Bisherigen
                if (_below[i]) c.SetSiblingIndex(front++);
                Classify(c as RectTransform);
            }
            _move.Clear();
            _content.SetAsFirstSibling();
        }

        private void Classify(RectTransform rt)
        {
            if (rt == null) return;
            bool full = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one
                        && rt.offsetMin.sqrMagnitude < 0.01f && rt.offsetMax.sqrMagnitude < 0.01f;
            var g = rt.GetComponent<Graphic>();
            bool hasGraphic = g != null && (g.color.a > 0.001f || g is RawImage);
            if (full)
            {
                // Vollbild mit eigener Fläche → Hintergrund; ohne (Boss-Leiste, Randpfeile) → bleibt im sicheren Bereich
                if (g != null)
                    _overlays.Add(new Overlay { Rt = rt, WasActive = false, MeasureFrames = 2 });
                return;
            }
            if (!hasGraphic) return;
            if (rt.GetComponentInChildren<Selectable>(true) != null || rt.GetComponentInChildren<TMP_Text>(true) != null) return;
            _bleeds.Add(new Bleed { Rt = rt, BaseMin = rt.offsetMin, BaseMax = rt.offsetMax });
        }

        void LateUpdate()
        {
            if (_content == null) return;
            if (_root.childCount > 1) Adopt(false);
            ApplyInsets(false);
            UpdateOverlays();
        }

        private void ApplyInsets(bool force)
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            Rect safe = MobilePlatform.SafeArea;
            Vector2 rootSize = _root.rect.size;
            if (!force && screen == _lastScreen && safe == _lastSafe && (rootSize - _lastRootSize).sqrMagnitude < 0.01f) return;
            _lastScreen = screen;
            _lastSafe = safe;
            _lastRootSize = rootSize;

            Vector4 px = MobileUiMath.Insets(screen.x, screen.y, safe);
            float ux = screen.x > 0 ? rootSize.x / screen.x : 1f;
            float uy = screen.y > 0 ? rootSize.y / screen.y : 1f;
            _insetUnits = new Vector4(Horizontal ? px.x * ux : 0f, Vertical ? px.y * uy : 0f,
                                      Horizontal ? px.z * ux : 0f, Vertical ? px.w * uy : 0f);
            _content.offsetMin = new Vector2(_insetUnits.x, _insetUnits.y);
            _content.offsetMax = new Vector2(-_insetUnits.z, -_insetUnits.w);

            // Zierflächen an der Kante bis zum Bildschirmrand
            Vector2 size = _content.rect.size;
            foreach (var b in _bleeds)
            {
                if (b.Rt == null) continue;
                b.Rt.offsetMin = b.BaseMin;
                b.Rt.offsetMax = b.BaseMax;
                // Lage im Bereich (bei Skalierung 1, ohne Drehung)
                Vector2 pMin = new Vector2(b.Rt.anchorMin.x * size.x, b.Rt.anchorMin.y * size.y) + b.BaseMin;
                Vector2 pMax = new Vector2(b.Rt.anchorMax.x * size.x, b.Rt.anchorMax.y * size.y) + b.BaseMax;
                Vector2 mn = b.BaseMin, mx = b.BaseMax;
                if (pMin.x <= 0.5f) mn.x -= _insetUnits.x;
                if (pMin.y <= 0.5f) mn.y -= _insetUnits.y;
                if (pMax.x >= size.x - 0.5f) mx.x += _insetUnits.z;
                if (pMax.y >= size.y - 0.5f) mx.y += _insetUnits.w;
                b.Rt.offsetMin = mn;
                b.Rt.offsetMax = mx;
            }
            foreach (var o in _overlays) o.MeasureFrames = 2;
        }

        // Hintergründe über den ganzen Bildschirm; Inhalt bei Bedarf als Ganzes verkleinert
        private void UpdateOverlays()
        {
            for (int i = _overlays.Count - 1; i >= 0; i--)
            {
                var o = _overlays[i];
                if (o.Rt == null) { _overlays.RemoveAt(i); continue; }
                bool active = o.Rt.gameObject.activeInHierarchy;
                if (active && !o.WasActive) o.MeasureFrames = 2; // gerade geöffnet: Inhalt im selben Bild gebaut
                o.WasActive = active;
                if (!active) continue;
                if (o.MeasureFrames > 0)
                {
                    o.MeasureFrames--;
                    o.Fit = MeasureFit(o.Rt);
                }
                ApplyOverlay(o);
            }
        }

        private float MeasureFit(RectTransform rt)
        {
            Vector2 need = Vector2.zero;
            for (int i = 0; i < rt.childCount; i++)
            {
                var c = rt.GetChild(i) as RectTransform;
                if (c == null || !c.gameObject.activeSelf) continue;
                // Nur mittig verankerte Inhalte (Boxen, Karten, Knöpfe); an Kanten verankerte wandern mit dem Rand
                if (c.anchorMin != c.anchorMax || Mathf.Abs(c.anchorMin.x - 0.5f) > 0.01f || Mathf.Abs(c.anchorMin.y - 0.5f) > 0.01f) continue;
                Vector2 p = c.localPosition;
                Vector2 s = c.localScale;
                Rect r = c.rect;
                need.x = Mathf.Max(need.x, Mathf.Abs(p.x + r.xMin * s.x), Mathf.Abs(p.x + r.xMax * s.x));
                need.y = Mathf.Max(need.y, Mathf.Abs(p.y + r.yMin * s.y), Mathf.Abs(p.y + r.yMax * s.y));
            }
            Vector2 avail = _content.rect.size * 0.5f - new Vector2(OverlayMargin, OverlayMargin);
            return MobileUiMath.FitScale(need, avail, 0.5f);
        }

        private void ApplyOverlay(Overlay o)
        {
            float f = Mathf.Max(0.01f, o.Fit);
            Vector2 p = _content.rect.size;
            // Mitte = Mitte des sicheren Bereichs; groß genug, um nach dem Verkleinern den ganzen Bildschirm zu decken
            float maxX = Mathf.Max(_insetUnits.x, _insetUnits.z), maxY = Mathf.Max(_insetUnits.y, _insetUnits.w);
            Vector2 s = new Vector2(p.x + 2f * maxX + 4f, p.y + 2f * maxY + 4f) / f;
            Vector2 d = (s - p) * 0.5f;
            var rt = o.Rt;
            if ((rt.offsetMin + d).sqrMagnitude > 0.0001f || (rt.offsetMax - d).sqrMagnitude > 0.0001f)
            {
                rt.offsetMin = -d;
                rt.offsetMax = d;
            }
            if (Mathf.Abs(rt.localScale.x - f) > 0.0005f) rt.localScale = new Vector3(f, f, 1f);
        }
    }
}
