using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Ping-System (Plan „Fesselung“ G1): Aktion „Ping“ (Standard G oder mittlere Maustaste, belegbar über KeyBindings) setzt eine Welt-Markierung an der Maus.
    // Touch-Modus: langes Drücken (≥ 0,5 s) in der Welt-Zone, ausgelöst vom InteractionManager (TryPingAtScreen).
    // Kontext aus der Umgebung: Gegner in der Nähe → „Gefahr!“, Schrein/Händler → „Hierher!“, sonst „Hier bauen“.
    // Im Koop geht der Ping über NetGame an alle (mit Spielernamen). Liegt auf dem Managers-Objekt (FeedbackDirector).
    // Pings außerhalb des Bildes zeigt eine Randmarke in Ping-Farbe. Linien-Material: Resources/PingLine.mat (im Build sicher dabei).
    public class PingSystem : MonoBehaviour
    {
        public enum PingType : byte { Build, Danger, Gather }

        [Tooltip("Mindestabstand zwischen zwei Pings eines Spielers (s).")]
        public float Cooldown = 0.8f;
        public float Lifetime = 5f;

        public static PingSystem Instance { get; private set; }
        private float _nextLocal;
        private InputAction _pingAction;
        private Camera _cam;
        private TMP_FontAsset _font;
        private readonly List<Marker> _markers = new List<Marker>();
        private Material _lineMaterial;
        private RectTransform _edgeCanvas;
        // Randabstände der Randmarken (Referenz 1920×1080): oben Kopfleiste, unten Bau-/Fähigkeitenleiste
        private const float EdgeMarginX = 50f, EdgeMarginTop = 110f, EdgeMarginBottom = 265f;

        private class Marker
        {
            public GameObject Root;
            public LineRenderer Ring, Beam;
            public TextMeshPro Label;
            public float Born;
            public Color Color;
            public RectTransform Edge;
            public CanvasGroup EdgeGroup;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            var hud = FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include);
            if (hud != null && hud.ShardText != null) _font = hud.ShardText.font;
            _lineMaterial = Resources.Load<Material>("PingLine");
            BuildEdgeCanvas();
        }

        private void BuildEdgeCanvas()
        {
            var go = new GameObject("PingEdgeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // über dem HUD, unter Pause/Karten
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _edgeCanvas = (RectTransform)go.transform;
        }

        // Randmarke: Raute in Ping-Farbe mit dunklem Rand, darunter die Beschriftung
        private RectTransform MakeEdge(PingType type, Color color, out CanvasGroup group)
        {
            group = null;
            if (_edgeCanvas == null) return null;
            var root = new GameObject("PingEdge", typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)root.transform;
            rt.SetParent(_edgeCanvas, false);
            rt.sizeDelta = new Vector2(40f, 40f);
            group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            var frt = (RectTransform)frame.transform;
            frt.SetParent(rt, false);
            frt.sizeDelta = new Vector2(34f, 34f);
            frt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var fimg = frame.GetComponent<Image>();
            fimg.color = new Color32(30, 18, 8, 230);
            fimg.raycastTarget = false;

            var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            var drt = (RectTransform)dot.transform;
            drt.SetParent(frt, false);
            drt.sizeDelta = new Vector2(24f, 24f);
            var dimg = dot.GetComponent<Image>();
            dimg.color = color;
            dimg.raycastTarget = false;

            var label = new GameObject("Label", typeof(RectTransform));
            var lrt = (RectTransform)label.transform;
            lrt.SetParent(rt, false);
            lrt.sizeDelta = new Vector2(200f, 30f);
            lrt.anchoredPosition = new Vector2(0f, -36f);
            var tmp = label.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = Label(type);
            tmp.fontSize = 22f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = new Color32(30, 18, 8, 255);
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            root.SetActive(false);
            return rt;
        }

        public static string Label(PingType t) =>
            t == PingType.Danger ? "Gefahr!" : t == PingType.Gather ? "Hierher!" : "Hier bauen";

        public static Color ColorOf(PingType t) =>
            t == PingType.Danger ? new Color(1f, 0.3f, 0.2f) : t == PingType.Gather ? new Color(1f, 0.82f, 0.3f) : new Color(0.4f, 0.8f, 1f);

        void Update()
        {
            HandleInput();
            Animate();
        }

        private void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (_pingAction == null) _pingAction = KeyBindings.Action("Ping");
            // Aktion „Ping“ (Standard G + Mausrad-Klick, belegbar); aktiv, solange die eigene Figur Eingaben hat
            bool pressed = _pingAction != null && _pingAction.WasPressedThisFrame();
            if (!pressed) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            PingAtScreen(mouse.position.ReadValue());
        }

        // Touch-Modus (Mobil, Paket B): langes Drücken in der Welt-Zone (InteractionManager prüft Welt-Zone/UI/Bau-Geist).
        // true = Ping gesendet
        public bool TryPingAtScreen(Vector2 screenPos) => PingAtScreen(screenPos);

        // Gemeinsamer Teil Maus/Touch: Sperren, Abklingzeit, Boden-Raycast, Anfrage über NetGame
        private bool PingAtScreen(Vector2 screenPos)
        {
            if (Time.unscaledTime < _nextLocal) return false;
            if (PauseManager.IsPaused || InteractionManager.LocalInputBlocked) return false;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return false;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return false;

            var im = InteractionManager.Instance;
            int mask = im != null && im.FloorLayer.value != 0 ? im.FloorLayer.value : Physics.DefaultRaycastLayers;
            Ray ray = _cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Ignore)) return false;

            _nextLocal = Time.unscaledTime + Cooldown;
            NetGame.RequestPing(hit.point, (byte)ContextAt(hit.point));
            return true;
        }

        // Kontext: Gegner in 4 m → Gefahr; Schrein/Händler in 5 m → Hierher; sonst Hier bauen
        public static PingType ContextAt(Vector3 p)
        {
            foreach (var e in CombatUtil.FindEnemies(p, 4f))
                if (e != null && !e.IsDead) return PingType.Danger;
            foreach (var c in Physics.OverlapSphere(p, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                if (c.GetComponentInParent<Shrine>() != null || c.GetComponentInParent<Merchant>() != null) return PingType.Gather;
            return PingType.Build;
        }

        // Alle Rechner (NetGame): Markierung zeigen
        public void ShowPing(Vector3 pos, PingType type, string who)
        {
            var color = ColorOf(type);
            var root = new GameObject("Ping");
            root.transform.position = pos + Vector3.up * 0.05f;

            var m = new Marker { Root = root, Born = Time.unscaledTime, Color = color };
            m.Ring = MakeLine(root.transform, "Ring", color, 0.12f, 33);
            m.Beam = MakeLine(root.transform, "Beam", color, 0.18f, 2);
            m.Beam.SetPosition(0, Vector3.zero);
            m.Beam.SetPosition(1, Vector3.up * 3.2f);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = Vector3.up * 3.7f;
            m.Label = labelGo.AddComponent<TextMeshPro>();
            if (_font != null) m.Label.font = _font;
            m.Label.text = string.IsNullOrEmpty(who) ? Label(type) : $"{Label(type)}\n<size=70%>{who}</size>";
            m.Label.fontSize = 6f;
            m.Label.alignment = TextAlignmentOptions.Center;
            m.Label.color = color;
            m.Label.outlineWidth = 0.25f;
            m.Label.outlineColor = new Color32(30, 18, 8, 255);
            m.Edge = MakeEdge(type, color, out m.EdgeGroup);
            _markers.Add(m);
            GameAudio.Play(GameAudio.Has(SfxId.Ping) ? SfxId.Ping : SfxId.UiClick, pos);
        }

        private LineRenderer MakeLine(Transform parent, string name, Color color, float width, int points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = points;
            lr.loop = false;
            lr.widthMultiplier = width;
            lr.material = _lineMaterial != null ? new Material(_lineMaterial) : new Material(Shader.Find("Sprites/Default"));
            if (lr.material.HasProperty("_BaseColor")) lr.material.SetColor("_BaseColor", color);
            lr.startColor = lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void Animate()
        {
            if (_markers.Count == 0) return;
            if (_cam == null) _cam = Camera.main;
            float now = Time.unscaledTime;
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var m = _markers[i];
                float t = now - m.Born;
                if (m.Root == null || t > Lifetime)
                {
                    if (m.Root != null)
                    {
                        foreach (var lr in m.Root.GetComponentsInChildren<LineRenderer>()) Destroy(lr.material);
                        Destroy(m.Root);
                    }
                    if (m.Edge != null) Destroy(m.Edge.gameObject);
                    _markers.RemoveAt(i);
                    continue;
                }
                float fade = Mathf.Clamp01((Lifetime - t) / 0.8f);
                // Ring pulsiert nach außen (in der ersten Sekunde größer), danach ruhiges Atmen
                float r = t < 0.6f ? Mathf.Lerp(2.6f, 1.2f, t / 0.6f) : 1.2f + 0.12f * Mathf.Sin(t * 5f);
                for (int k = 0; k < m.Ring.positionCount; k++)
                {
                    float a = k / (float)(m.Ring.positionCount - 1) * Mathf.PI * 2f;
                    m.Ring.SetPosition(k, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                }
                var c = m.Color;
                c.a = fade;
                m.Ring.startColor = m.Ring.endColor = c;
                var cb = c;
                m.Beam.startColor = cb;
                cb.a = 0f;
                m.Beam.endColor = cb;
                m.Label.alpha = fade;
                if (_cam != null) m.Label.transform.rotation = _cam.transform.rotation;
                UpdateEdge(m, fade, t);
            }
        }

        // Ping außerhalb des Bildes: Randmarke in Richtung des Pings (vom Bildmittelpunkt aus), sonst ausblenden
        private void UpdateEdge(Marker m, float fade, float age)
        {
            if (m.Edge == null || _cam == null || _edgeCanvas == null) return;
            Vector3 sp = _cam.WorldToScreenPoint(m.Root.transform.position + Vector3.up * 1.5f);
            bool behind = sp.z < 0f;
            if (behind) sp = new Vector3(Screen.width - sp.x, Screen.height - sp.y, 0f);
            bool onScreen = !behind && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
            if (onScreen)
            {
                if (m.Edge.gameObject.activeSelf) m.Edge.gameObject.SetActive(false);
                return;
            }
            if (!m.Edge.gameObject.activeSelf) m.Edge.gameObject.SetActive(true);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_edgeCanvas, sp, null, out Vector2 local);
            // Sichtbereich ohne Kopf- und Fußleisten; Richtung vom Mittelpunkt dieses Bereichs aus
            Vector2 size = _edgeCanvas.rect.size;
            float cy = (EdgeMarginBottom - EdgeMarginTop) * 0.5f;
            Vector2 half = new Vector2(size.x * 0.5f - EdgeMarginX, (size.y - EdgeMarginTop - EdgeMarginBottom) * 0.5f);
            Vector2 d = local - new Vector2(0f, cy);
            if (d.sqrMagnitude < 0.01f) d = Vector2.down;
            float k = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(d.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(d.y)));
            Vector2 pos = new Vector2(0f, cy) + d * Mathf.Min(1f, k);
            m.Edge.anchoredPosition = pos;
            // Beschriftung nach innen ausrichten, damit sie am Rand nicht abgeschnitten wird
            var label = m.Edge.childCount > 1 ? m.Edge.GetChild(1) as RectTransform : null;
            if (label != null)
            {
                float side = pos.x > half.x - 120f ? 1f : pos.x < -half.x + 120f ? -1f : 0f;
                label.pivot = new Vector2(side > 0f ? 1f : side < 0f ? 0f : 0.5f, 0.5f);
                label.anchoredPosition = new Vector2(side * 20f, -36f);
                var tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.alignment = side > 0f ? TextAlignmentOptions.Right : side < 0f ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            }
            float pulse = age < 1f ? 1f + 0.35f * Mathf.Sin(age * Mathf.PI * 3f) * (1f - age) : 1f;
            m.Edge.localScale = Vector3.one * pulse;
            m.EdgeGroup.alpha = fade;
        }
    }
}
