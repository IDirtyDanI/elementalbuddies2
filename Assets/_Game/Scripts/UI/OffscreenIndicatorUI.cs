using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Pfeil am Bildschirmrand zum erwachten Schrein (Priorität) bzw. kurz zu einem neu geöffneten Portal,
    // solange das Ziel außerhalb des Bildschirms liegt. Funktioniert mit Screen Space Overlay + CanvasScaler
    // (und Screen Space Camera): Bildschirm → Canvas-Koordinaten per RectTransformUtility.
    public class OffscreenIndicatorUI : MonoBehaviour
    {
        [Tooltip("Pfeil-RectTransform (Kind eines Vollbild-RectTransforms, Anchor/Pivot mittig).")]
        public RectTransform Arrow;
        public Image ArrowImage;
        [Tooltip("Optional: Distanz-Text (sollte NICHT Kind des Pfeils sein, sonst dreht er mit).")]
        public TextMeshProUGUI DistanceText;
        [Tooltip("Optional: Element-Icon neben dem Pfeil (ebenfalls nicht Kind des Pfeils).")]
        public Image TargetIcon;
        public Sprite[] ElementIcons = new Sprite[4];
        public Sprite PortalIcon;

        [Header("Layout")]
        [Tooltip("Abstand vom Bildschirmrand in Canvas-Einheiten.")]
        public float EdgePadding = 60f;
        [Tooltip("Abstand von Distanz-Text/Icon zum Pfeil (Richtung Bildmitte), Canvas-Einheiten.")]
        public float LabelOffset = 55f;
        [Tooltip("Ausrichtung des Pfeil-Sprites: 0 = zeigt nach rechts, -90 = zeigt nach oben.")]
        public float SpriteAngleOffset = -90f;
        [Tooltip("Ziel gilt als sichtbar, wenn es mindestens so weit (Viewport 0..1) innerhalb des Bildschirms liegt.")]
        public float OnScreenMargin = 0.03f;

        [Header("Ziele")]
        [Tooltip("Wie lange nach dem Öffnen ein Portal angezeigt wird (s).")]
        public float PortalHighlightDuration = 6f;
        public Color PortalColor = new Color(0.75f, 0.35f, 1f);
        public bool TintWithElement = true;

        private Camera _cam;
        private Canvas _canvas;
        private RectTransform _area;
        private Transform _player;
        private SpawnPortal _portal;
        private float _portalUntil;

        void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (Arrow != null) _area = Arrow.parent as RectTransform;
            if (ArrowImage == null && Arrow != null) ArrowImage = Arrow.GetComponent<Image>();
        }

        void Start()
        {
            WaveManager.OnPortalOpened += HandlePortalOpened;
            _player = GameObject.FindGameObjectWithTag("Player")?.transform;
            SetVisible(false);
        }

        void OnDestroy()
        {
            WaveManager.OnPortalOpened -= HandlePortalOpened;
        }

        private void HandlePortalOpened(SpawnPortal p)
        {
            _portal = p;
            _portalUntil = Time.unscaledTime + PortalHighlightDuration;
        }

        void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (Arrow == null || _cam == null || _area == null)
            {
                SetVisible(false);
                return;
            }

            // Ziel wählen: erwachter Schrein > frisch geöffnetes Portal
            Vector3 targetPos;
            Color color;
            Sprite icon;
            Shrine shrine = FindAwakenedShrine();
            if (shrine != null)
            {
                targetPos = shrine.CenterPosition;
                color = TintWithElement ? ElementInfo.GetColor(shrine.ElementIndex) : Color.white;
                icon = ElementIcons != null && shrine.ElementIndex < ElementIcons.Length ? ElementIcons[shrine.ElementIndex] : null;
            }
            else if (_portal != null && Time.unscaledTime < _portalUntil)
            {
                targetPos = _portal.SpawnTransform.position;
                color = PortalColor;
                icon = PortalIcon;
            }
            else
            {
                SetVisible(false);
                return;
            }

            Vector3 vp = _cam.WorldToViewportPoint(targetPos);
            bool behind = vp.z < 0f;
            bool onScreen = !behind
                && vp.x >= OnScreenMargin && vp.x <= 1f - OnScreenMargin
                && vp.y >= OnScreenMargin && vp.y <= 1f - OnScreenMargin;
            if (onScreen)
            {
                SetVisible(false);
                return;
            }

            // Richtung von der Bildschirmmitte (Viewport), hinter der Kamera gespiegelt
            Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (behind) dir = -dir;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;

            // In Bildschirm-Pixeln rechnen (Seitenverhältnis), dann in Canvas-Koordinaten umrechnen
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dirPx = new Vector2(dir.x * Screen.width, dir.y * Screen.height).normalized;

            Camera uiCam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screenCenter, uiCam, out Vector2 localCenter);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screenCenter + dirPx * 100f, uiCam, out Vector2 localDirPoint);
            Vector2 localDir = (localDirPoint - localCenter).normalized;

            // Auf das um EdgePadding verkleinerte Rechteck des Bereichs projizieren
            Rect r = _area.rect;
            float halfW = Mathf.Max(1f, r.width * 0.5f - EdgePadding);
            float halfH = Mathf.Max(1f, r.height * 0.5f - EdgePadding);
            Vector2 areaCenter = r.center;
            float tx = Mathf.Abs(localDir.x) > 1e-5f ? halfW / Mathf.Abs(localDir.x) : float.PositiveInfinity;
            float ty = Mathf.Abs(localDir.y) > 1e-5f ? halfH / Mathf.Abs(localDir.y) : float.PositiveInfinity;
            Vector2 pos = areaCenter + localDir * Mathf.Min(tx, ty);

            SetVisible(true);
            Arrow.anchoredPosition = ToAnchored(Arrow, pos);
            float angle = Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;
            Arrow.localRotation = Quaternion.Euler(0f, 0f, angle + SpriteAngleOffset);
            if (ArrowImage != null) ArrowImage.color = color;

            Vector2 labelPos = pos - localDir * LabelOffset;
            if (DistanceText != null)
            {
                if (_player == null) _player = GameObject.FindGameObjectWithTag("Player")?.transform;
                if (_player != null)
                {
                    Vector3 d = targetPos - _player.position;
                    d.y = 0f;
                    DistanceText.text = $"{Mathf.RoundToInt(d.magnitude)} m";
                }
                else DistanceText.text = "";
                DistanceText.rectTransform.anchoredPosition = ToAnchored(DistanceText.rectTransform, labelPos);
            }
            if (TargetIcon != null)
            {
                TargetIcon.sprite = icon;
                TargetIcon.enabled = icon != null;
                TargetIcon.rectTransform.anchoredPosition = ToAnchored(TargetIcon.rectTransform, labelPos + Vector2.up * 30f);
            }
        }

        // Lokale Position im Bereich → anchoredPosition des Elements (berücksichtigt beliebige Anchors)
        private Vector2 ToAnchored(RectTransform rt, Vector2 localPos)
        {
            if (rt.parent != _area)
            {
                // Element hängt woanders: über Welt-Koordinaten umrechnen
                Vector3 world = _area.TransformPoint(localPos);
                var parent = rt.parent as RectTransform;
                if (parent == null) return localPos;
                Vector2 pLocal = parent.InverseTransformPoint(world);
                return pLocal - AnchorReference(rt, parent.rect);
            }
            return localPos - AnchorReference(rt, _area.rect);
        }

        private static Vector2 AnchorReference(RectTransform rt, Rect parentRect)
        {
            Vector2 anchorMid = (rt.anchorMin + rt.anchorMax) * 0.5f;
            return new Vector2(parentRect.x + parentRect.width * anchorMid.x, parentRect.y + parentRect.height * anchorMid.y);
        }

        private static Shrine FindAwakenedShrine()
        {
            var mgr = ShrineManager.Instance;
            if (mgr != null && mgr.ActiveShrine != null && mgr.ActiveShrine.IsAwakened) return mgr.ActiveShrine;
            var all = Shrine.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].IsAwakened) return all[i];
            return null;
        }

        private bool _visible = true;
        private void SetVisible(bool v)
        {
            if (_visible == v) return;
            _visible = v;
            if (Arrow != null) Arrow.gameObject.SetActive(v);
            if (DistanceText != null) DistanceText.gameObject.SetActive(v);
            if (TargetIcon != null) TargetIcon.gameObject.SetActive(v);
        }
    }
}
