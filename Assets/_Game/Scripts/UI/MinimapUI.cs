using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Runde Minimap oben links unter der Top-Bar (Holz-/Gold-Rahmen wie das HUD). Norden oben, dreht sich nicht mit;
    // zeigt die ganze Karte (Mitte 0/0, Radius 68 m: Stadtmauer ±35, Portale 44, Händler 44,8, Schreine ±42/±42).
    //
    // - Hintergrund: einmal beim Start von oben gerendert (orthografische Hilfskamera, Figuren/Gegner/Buddies/Splitter
    //   ausgeblendet), leicht pergament-getönt. Schlägt das fehl → einfache gezeichnete Karte (Wiese + Mauerring).
    // - Marker: Nexus, Portale, Schreine, Händlerstände, Buddies (Elementfarbe), Gegner (rot; Elite größer, Bosse groß,
    //   Belagerungsramme als orangefarbenes Quadrat), Spieler (eigener Pfeil mit Blickrichtung, Mitspieler in Slot-Farbe).
    //   Gegner/Buddies werden mit UpdateRate (12 Hz) aus den Registern (EnemyBrain.All, ElementalBuddy.Active) gelesen,
    //   Spieler und feste Orte jedes Bild. Läuft auf allen Rechnern (Gegner/Buddies sind auf Clients Netzobjekte).
    // - Alle Sprites (Rahmen, Maske, Punkte, Raute, Stand …) entstehen zur Laufzeit im Code; Nexus-Icon und Pfeil
    //   kommen aus dem vorhandenen HUD (TopBar/NexusIcon, OffscreenIndicatorUI).
    // - Hängt im HUD-Canvas direkt hinter der TopBar (Pause/Karten/Game Over liegen darüber). Wird von HUDManager
    //   angelegt, die Szene bleibt unverändert. Weicht dem Buddy-Panel aus (blendet sich aus, wenn es die Karte
    //   überdecken würde); TeamHUD, Meldungs-Banner und Randpfeile richten sich nach der Minimap.
    public class MinimapUI : MonoBehaviour
    {
        public static MinimapUI Instance { get; private set; }

        public const string PrefEnabled = "minimap_enabled";
        // Einstellung (Pause-Menü „Minikarte anzeigen“)
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
            set { PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        [Header("Karte")]
        [Tooltip("Welt-x/z der Kartenmitte.")]
        public Vector2 WorldCenter = Vector2.zero;
        [Tooltip("Weltmeter von der Mitte bis zum Kartenrand (68 → Schreine bei ±42/±42 liegen mit Abstand innerhalb des Rahmens).")]
        public float WorldRadius = 68f;
        [Tooltip("Auflösung der gerenderten Hintergrundkarte (Pixel).")]
        public int TextureSize = 512;

        [Header("Layout (Canvas-Einheiten)")]
        public float Size = 280f;          // Außendurchmesser inkl. Rahmen
        public float RingWidth = 15f;      // Holzrahmen
        public float LeftMargin = 24f;
        public float TopGap = 12f;         // Abstand zur Unterkante der TopBar
        [Tooltip("Fallback, falls keine TopBar gefunden wird: Abstand von oben.")]
        public float TopFallback = 118f;
        public float UpdateRate = 12f;     // Gegner/Buddies pro Sekunde

        [Header("Marker")]
        public float EnemySize = 7f;
        public float EliteSize = 10f;
        public float BossSize = 16f;
        public float RamSize = 14f;
        [Tooltip("Flieger: Dreieck in Flugrichtung (Walddorf-Plan).")]
        public float FlyerSize = 11f;
        public float BuddySize = 9f;
        public float LocalPlayerSize = 26f;
        public float MatePlayerSize = 21f;
        public float NexusSize = 30f;
        public float ShrineSize = 16f;
        public float MerchantSize = 17f;
        public float PortalSize = 16f;
        public Color EnemyColor = new Color(0.93f, 0.17f, 0.13f);
        public Color EliteColor = new Color(1f, 0.3f, 0.45f);
        public Color BossColor = new Color(0.62f, 0.03f, 0.06f);
        public Color RamColor = new Color(1f, 0.58f, 0.15f);
        public Color PortalColor = new Color(0.75f, 0.35f, 1f);
        [Tooltip("Champion-Bedrohungen: Beschwörer = Raute, Bannwirker = Ring, Farbe = Bannschild des Themes (pulsiert).")]
        public float ThreatSize = 15f;
        public Color ClosedColor = new Color(0.55f, 0.48f, 0.4f, 0.75f);

        // ---------------- Zustand ----------------

        private RectTransform _root, _mask, _markers;
        private CanvasGroup _group;
        private RawImage _map;
        private float _mapRadius;   // Radius der Kartentextur (Maßstab)
        private float _clampRadius; // sichtbarer Radius innerhalb des Rahmens (Marker bleiben darin)
        private float _nextSlowUpdate;
        private RectTransform _topBar;
        private BuddyInfoPanelUI _buddyPanel;
        private bool _blockedByPanel;
        private readonly List<Object> _owned = new List<Object>(); // erzeugte Texturen/Sprites (OnDestroy)
        private static readonly Vector3[] _corners = new Vector3[4];
        private static readonly Vector3[] _corners2 = new Vector3[4];

        private class Layer
        {
            public RectTransform Root;
            public Sprite Sprite;
            public readonly List<Image> Pool = new List<Image>();
            public int Used;
        }
        private Layer _portals, _shrines, _merchants, _nexus, _buddies, _enemies, _bosses, _mates, _localHalo, _local;
        private Sprite _dot, _ring, _diamond, _square, _stall, _arrow, _nexusIcon, _triangle;

        // Für TeamHUD/ToastUI/OffscreenIndicatorUI
        public RectTransform Rect => _root;
        // Einstellung an → der Platz oben links gehört der Minimap (auch wenn sie kurz dem Buddy-Panel ausweicht)
        public bool Occupies => _root != null && Enabled && isActiveAndEnabled;
        // Gerade sichtbar
        public bool IsShown => Occupies && _group != null && _group.alpha > 0.01f;
        // Abstand der Unterkante von der Canvas-Oberkante (Canvas-Einheiten)
        public float BottomOffset => _root != null ? -_root.anchoredPosition.y + _root.sizeDelta.y : 0f;

        // ---------------- Aufbau ----------------

        public static MinimapUI Create(HUDManager hud)
        {
            if (Instance != null) return Instance;
            Canvas canvas = null;
            if (hud != null)
            {
                var anchor = hud.ShardText != null ? hud.ShardText.transform : hud.transform;
                var c = anchor.GetComponentInParent<Canvas>();
                if (c != null) canvas = c.rootCanvas;
            }
            if (canvas == null) return null;
            var go = new GameObject("Minimap", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var mm = go.AddComponent<MinimapUI>();
            var map = MapContext.Current; // Kartenausschnitt der Map (Stadt: Mitte 0/0, Radius 68)
            mm.WorldCenter = map.MinimapCenter;
            mm.WorldRadius = map.MinimapRadius;
            TMP_FontAsset font = hud.WaveText != null ? hud.WaveText.font : hud.ShardText != null ? hud.ShardText.font : null;
            mm.Build(canvas, font);
            return mm;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var o in _owned) if (o != null) Destroy(o);
            _owned.Clear();
        }

        private void Build(Canvas canvas, TMP_FontAsset font)
        {
            _root = (RectTransform)transform;
            var top = canvas.transform.Find("TopBar");
            _topBar = top as RectTransform;
            // Direkt hinter der TopBar einsortieren: später kommende Panels (Karten, Pause, Game Over) liegen darüber
            if (top != null) _root.SetSiblingIndex(top.GetSiblingIndex() + 1);

            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.sizeDelta = new Vector2(Size, Size);
            _root.anchoredPosition = new Vector2(LeftMargin, -TopFallback);
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = Enabled ? 1f : 0f;

            // Sprites (prozedural)
            _dot = MakeShapeSprite("mm_dot", 64, SdCircle);
            _ring = MakeShapeSprite("mm_ring", 64, SdRing);
            _diamond = MakeShapeSprite("mm_diamond", 64, SdDiamond);
            _square = MakeShapeSprite("mm_square", 64, SdSquare);
            _stall = MakeShapeSprite("mm_stall", 64, SdStall);
            _triangle = MakeShapeSprite("mm_triangle", 64, SdTriangle);
            var offscreen = FindFirstObjectByType<OffscreenIndicatorUI>(FindObjectsInactive.Include);
            _arrow = offscreen != null && offscreen.ArrowImage != null ? offscreen.ArrowImage.sprite : null;
            if (_arrow == null) _arrow = MakeShapeSprite("mm_arrow", 64, SdArrow);
            var nexusIcon = top != null ? top.Find("NexusIcon") : null;
            var nexusImg = nexusIcon != null ? nexusIcon.GetComponent<Image>() : null;
            _nexusIcon = nexusImg != null && nexusImg.sprite != null ? nexusImg.sprite : _diamond;

            // Kartenfläche (rund maskiert)
            float inner = Size - 2f * (RingWidth - 2f); // Maske reicht 2 Einheiten unter den Rahmen
            _mapRadius = inner * 0.5f;
            _clampRadius = Size * 0.5f - RingWidth - 4f;
            _mask = NewRect("Mask", _root);
            Center(_mask, Vector2.zero, new Vector2(inner, inner));
            var maskImg = _mask.gameObject.AddComponent<Image>();
            maskImg.sprite = Own(MakeSprite(BuildCircleTexture(256), "mm_mask"));
            maskImg.raycastTarget = false;
            var mask = _mask.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var mapRt = NewRect("Map", _mask);
            Stretch(mapRt);
            _map = mapRt.gameObject.AddComponent<RawImage>();
            _map.raycastTarget = false;
            _map.color = new Color(0.36f, 0.45f, 0.24f); // bis die Karte gerendert ist

            _markers = NewRect("Markers", _mask);
            Center(_markers, Vector2.zero, Vector2.zero);

            _portals = NewLayer("Portals", _ring);
            _shrines = NewLayer("Shrines", _diamond);
            _merchants = NewLayer("Merchants", _stall);
            _nexus = NewLayer("Nexus", _nexusIcon);
            _buddies = NewLayer("Buddies", _dot);
            _enemies = NewLayer("Enemies", _dot);
            _bosses = NewLayer("Bosses", _dot);
            _mates = NewLayer("Mates", _arrow);
            _localHalo = NewLayer("LocalHalo", _dot);
            _local = NewLayer("Local", _arrow);

            // Rahmen + Himmelsrichtung
            var frameRt = NewRect("Frame", _root);
            Stretch(frameRt);
            var frame = frameRt.gameObject.AddComponent<Image>();
            frame.sprite = Own(MakeSprite(BuildFrameTexture(512, RingWidth / (Size * 0.5f)), "mm_frame"));
            frame.raycastTarget = false;

            var north = NewRect("North", _root);
            Center(north, new Vector2(0f, Size * 0.5f - RingWidth * 0.5f), new Vector2(40f, 30f));
            var plate = north.gameObject.AddComponent<Image>();
            plate.sprite = _dot;
            plate.color = new Color(0.3f, 0.19f, 0.09f);
            plate.raycastTarget = false;
            plate.preserveAspect = true;
            var nText = NewRect("Text", north);
            Stretch(nText);
            var t = nText.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = "N";
            t.fontSize = 20f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(1f, 0.85f, 0.45f);
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;

            StartCoroutine(RenderWhenReady());
        }

        private Layer NewLayer(string name, Sprite sprite)
        {
            var l = new Layer { Root = NewRect(name, _markers), Sprite = sprite };
            Center(l.Root, Vector2.zero, Vector2.zero);
            return l;
        }

        private IEnumerator RenderWhenReady()
        {
            // zwei Bilder warten: Szene/Belagerungszustand sind dann gesetzt, Spielfiguren evtl. schon gespawnt
            yield return null;
            yield return null;
            Texture2D tex = null;
            try { tex = RenderMapTexture(WorldCenter, WorldRadius, TextureSize); }
            catch (System.Exception e) { Debug.LogWarning("Minimap: Karte konnte nicht gerendert werden – einfache Karte. " + e.Message); }
            if (tex == null) tex = BuildFallbackTexture(256, WorldCenter, WorldRadius, MapContext.Current.WallHalfSize);
            Own(tex);
            _map.texture = tex;
            _map.color = Color.white;
        }

        // ---------------- Laufzeit ----------------

        void LateUpdate()
        {
            if (_root == null) return;
            PlaceRoot();

            bool on = Enabled;
            _blockedByPanel = on && OverlapsBuddyPanel();
            float target = on && !_blockedByPanel ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 8f);
            if (_group.alpha <= 0.001f) return;

            UpdatePlayers();
            UpdateStatics();
            if (Time.unscaledTime >= _nextSlowUpdate)
            {
                _nextSlowUpdate = Time.unscaledTime + 1f / Mathf.Max(1f, UpdateRate);
                UpdateUnits();
            }
        }

        // Unter die TopBar (fest in Canvas-Einheiten, folgt aber einer geänderten TopBar)
        private void PlaceRoot()
        {
            float y = -TopFallback;
            var parent = _root.parent as RectTransform;
            if (_topBar != null && _topBar.gameObject.activeInHierarchy && parent != null)
            {
                _topBar.GetWorldCorners(_corners);
                float barBottom = parent.InverseTransformPoint(_corners[0]).y;
                float canvasTop = parent.rect.yMax;
                y = barBottom - canvasTop - TopGap;
            }
            var pos = new Vector2(LeftMargin, y);
            if ((_root.anchoredPosition - pos).sqrMagnitude > 0.01f) _root.anchoredPosition = pos;
        }

        // Buddy-Panel (links, bei 21:9 oder nach oben geschoben) würde die Karte überdecken → ausblenden
        private bool OverlapsBuddyPanel()
        {
            if (_buddyPanel == null) _buddyPanel = FindFirstObjectByType<BuddyInfoPanelUI>(FindObjectsInactive.Include);
            if (_buddyPanel == null || _buddyPanel.Panel == null || !_buddyPanel.Panel.activeInHierarchy) return false;
            var prt = _buddyPanel.Panel.transform as RectTransform;
            if (prt == null) return false;
            prt.GetWorldCorners(_corners2);
            _root.GetWorldCorners(_corners);
            // Bildschirm-/Weltkoordinaten des Overlay-Canvas: 0 = unten links, 2 = oben rechts
            bool overlapX = _corners2[0].x < _corners[2].x && _corners2[2].x > _corners[0].x;
            bool overlapY = _corners2[0].y < _corners[2].y && _corners2[2].y > _corners[0].y;
            return overlapX && overlapY;
        }

        private void UpdatePlayers()
        {
            Begin(_mates); Begin(_local); Begin(_localHalo);
            foreach (var a in PlayerAvatar.All)
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                float rot = MinimapMath.HeadingToRotation(a.transform.forward);
                bool local = a == PlayerAvatar.Local || (PlayerAvatar.Local == null && a.IsLocalControl);
                bool alive = a.IsAlive;
                if (local)
                {
                    float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f);
                    Place(Next(_localHalo), a.transform.position, LocalPlayerSize * 1.25f * pulse, new Color(1f, 0.95f, 0.75f, alive ? 0.35f : 0.15f));
                    Place(Next(_local), a.transform.position, LocalPlayerSize, alive ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.7f), rot);
                }
                else
                {
                    var np = a.Owner;
                    int slot = np != null ? Mathf.Clamp(np.Slot, 0, TeamHUD.SlotColors.Length - 1) : 1;
                    Color c = TeamHUD.SlotColors[slot];
                    if (!alive) c = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    Place(Next(_mates), a.transform.position, MatePlayerSize, c, rot);
                }
            }
            End(_mates); End(_local); End(_localHalo);
        }

        private void UpdateStatics()
        {
            float pulse = 1f + 0.18f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));

            Begin(_nexus);
            if (Nexus.Instance != null) Place(Next(_nexus), Nexus.Instance.transform.position, NexusSize, Color.white);
            End(_nexus);

            Begin(_portals);
            foreach (var p in SpawnPortal.All)
            {
                if (p == null) continue;
                Vector3 pos = p.SpawnPoint != null ? p.SpawnPoint.position : p.transform.position;
                Place(Next(_portals), pos, PortalSize, p.IsOpen ? PortalColor : ClosedColor);
            }
            End(_portals);

            Begin(_shrines);
            foreach (var s in Shrine.All)
            {
                if (s == null) continue;
                Color c = ElementInfo.GetColor(s.ElementIndex);
                float size = ShrineSize;
                switch (s.State)
                {
                    case ShrineState.Dormant: c = Color.Lerp(c, ClosedColor, 0.55f); c.a = 0.8f; break;
                    case ShrineState.Awakened: size *= pulse; break;
                    case ShrineState.Completed: c.a = 0.9f; break;
                }
                Place(Next(_shrines), s.CenterPosition, size, c);
            }
            End(_shrines);

            Begin(_merchants);
            foreach (var m in Merchant.All)
            {
                if (m == null) continue;
                Color c = MerchantInfo.Colors[Mathf.Clamp((int)m.Kind, 0, MerchantInfo.Colors.Length - 1)];
                float size = MerchantSize;
                if (m.State == MerchantState.Geschlossen) c = ClosedColor;
                else if (m.IsActive) size *= pulse;
                else c.a = 0.75f;
                Place(Next(_merchants), m.CenterPosition, size, c);
            }
            End(_merchants);
        }

        private void UpdateUnits()
        {
            Begin(_buddies);
            foreach (var b in ElementalBuddy.Active)
            {
                if (b == null || b.IsDead || !b.isActiveAndEnabled) continue;
                Color c = b is FusionBuddy f
                    ? FusionInfo.Colors[Mathf.Clamp((int)f.Element, 0, FusionInfo.Colors.Length - 1)]
                    : ElementInfo.GetColor(b.ElementIndex);
                Place(Next(_buddies), b.transform.position, b.IsFusion ? BuddySize * 1.2f : BuddySize, c);
            }
            End(_buddies);

            Begin(_enemies); Begin(_bosses);
            foreach (var e in EnemyBrain.All)
            {
                if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
                var role = e.Config != null ? e.Config.Role : EnemyRole.Runner;
                if (role == EnemyRole.Summoner || role == EnemyRole.Hexer)
                {
                    // Champion-Bedrohung: eigene Form in Schildfarbe, leicht pulsierend (über den Normalgegnern)
                    float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f);
                    Place(Next(_bosses, role == EnemyRole.Summoner ? _diamond : _ring), e.transform.position, ThreatSize * pulse, ThreatVisuals.WardColor);
                }
                else if (e.Elite == EliteAffix.Ram)
                    Place(Next(_bosses, _square), e.transform.position, RamSize, RamColor);
                else if (e.IsBoss)
                    Place(Next(_bosses, _dot), e.transform.position, BossSize, BossColor);
                else if (e.IsFlyer)
                    Place(Next(_enemies, _triangle), e.transform.position, e.IsElite ? FlyerSize * 1.3f : FlyerSize,
                        e.IsElite ? EliteColor : EnemyColor, MinimapMath.HeadingToRotation(e.transform.forward));
                else if (e.IsElite)
                    Place(Next(_enemies, _dot), e.transform.position, EliteSize, EliteColor);
                else
                    Place(Next(_enemies, _dot), e.transform.position, EnemySize, EnemyColor);
            }
            End(_enemies); End(_bosses);
        }

        // ---------------- Marker-Pool ----------------

        private static void Begin(Layer l) => l.Used = 0;

        private static void End(Layer l)
        {
            for (int i = l.Used; i < l.Pool.Count; i++)
                if (l.Pool[i].gameObject.activeSelf) l.Pool[i].gameObject.SetActive(false);
        }

        private Image Next(Layer l, Sprite sprite = null)
        {
            Image img;
            if (l.Used < l.Pool.Count) img = l.Pool[l.Used];
            else
            {
                var rt = NewRect("M", l.Root);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                l.Pool.Add(img);
            }
            l.Used++;
            var s = sprite != null ? sprite : l.Sprite;
            if (img.sprite != s) img.sprite = s;
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
            return img;
        }

        private void Place(Image img, Vector3 world, float size, Color color, float rotation = 0f)
        {
            Vector2 p = MinimapMath.WorldToMap(world, WorldCenter, WorldRadius, _mapRadius);
            p = MinimapMath.ClampToCircle(p, _clampRadius - size * 0.5f, out _);
            var rt = img.rectTransform;
            rt.anchoredPosition = p;
            rt.sizeDelta = new Vector2(size, size);
            rt.localEulerAngles = new Vector3(0f, 0f, rotation);
            img.color = color;
        }

        // ---------------- Hintergrund-Karte ----------------

        // Rendert die Karte von oben (Norden oben) in eine Textur. Spielfiguren, Gegner, Buddies und Splitter werden
        // währenddessen ausgeblendet. Auch im Edit-Mode nutzbar (Vorschau). Gibt null zurück, wenn nichts gerendert wurde.
        public static Texture2D RenderMapTexture(Vector2 center, float radius, int size)
        {
            size = Mathf.Clamp(size, 64, 2048);
            var hidden = new List<Renderer>();
            void Hide(Component c)
            {
                if (c == null) return;
                foreach (var r in c.GetComponentsInChildren<Renderer>())
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
            }
            foreach (var a in PlayerAvatar.All) Hide(a);
            foreach (var e in EnemyBrain.All) Hide(e);
            foreach (var b in ElementalBuddy.Active) Hide(b);
            foreach (var s in ShardPickup.All) Hide(s);

            var go = new GameObject("MinimapCamera") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture rt = null;
            var prevActive = RenderTexture.active;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = radius;
                cam.aspect = 1f;
                go.transform.SetPositionAndRotation(new Vector3(center.x, 200f, center.y), Quaternion.Euler(90f, 0f, 0f));
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 400f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.22f, 0.3f, 0.16f);
                cam.cullingMask = ~LayerMask.GetMask("UI", "TransparentFX", "Ignore Raycast", "Player", "Enemy", "Buddy");
                cam.allowHDR = false;
                cam.allowMSAA = true;

                var desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4, sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear };
                rt = RenderTexture.GetTemporary(desc);
                cam.targetTexture = rt;
                bool prevFog = RenderSettings.fog;
                RenderSettings.fog = false; // Kartenbild ohne Nebel (#313)
                try { cam.Render(); } finally { RenderSettings.fog = prevFog; }
                cam.targetTexture = null;

                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "MinimapMap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                RenderTexture.active = prevActive;
                if (!Stylize(tex)) { DestroyObj(tex); return null; }
                tex.Apply(false, false);
                return tex;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                DestroyObj(go);
                foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            }
        }

        // Leicht pergament-warm getönt, zum Rand hin abgedunkelt. false = Bild ist (fast) leer/schwarz.
        private static bool Stylize(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int n = tex.width;
            float sum = 0f;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n * 2f - 1f;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f;
                    int i = y * n + x;
                    Color c = px[i];
                    float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    sum += lum;
                    var warm = new Color(lum * 1.1f, lum * 0.97f, lum * 0.74f);
                    c = Color.Lerp(c, warm, 0.22f);
                    c = (c * 1.18f - new Color(0.5f, 0.5f, 0.5f)) * 1.06f + new Color(0.52f, 0.52f, 0.52f); // heller, etwas Kontrast
                    float d = Mathf.Sqrt(u * u + v * v);
                    c *= Mathf.Lerp(1f, 0.7f, Mathf.SmoothStep(0.72f, 1f, d));
                    c.a = 1f;
                    px[i] = c;
                }
            }
            tex.SetPixels32(px);
            return sum / (n * n) > 0.02f;
        }

        // Ersatzkarte ohne Rendern: Wiese, Siedlung, Mauerring ±wallHalf (Stadt: ±35 m)
        public static Texture2D BuildFallbackTexture(int size, Vector2 center, float radius, float wallHalf = 35f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "MinimapFallback", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            var grass = new Color(0.42f, 0.5f, 0.27f);
            var town = new Color(0.62f, 0.55f, 0.42f);
            var wall = new Color(0.33f, 0.28f, 0.22f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector3 w = MinimapMath.MapToWorld(new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f), center, radius, size * 0.5f);
                    float ax = Mathf.Abs(w.x), az = Mathf.Abs(w.z);
                    Color c = grass * (0.92f + 0.08f * Mathf.PerlinNoise(w.x * 0.15f, w.z * 0.15f));
                    if (ax < wallHalf && az < wallHalf) c = town;
                    float wd = Mathf.Max(ax, az);
                    if (wd > wallHalf - 1f && wd < wallHalf + 1f) c = wall;
                    px[y * size + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ---------------- Prozedurale Sprites ----------------

        private Sprite MakeShapeSprite(string name, int size, System.Func<float, float, float> sd)
        {
            return Own(MakeSprite(BuildShapeTexture(size, sd), name));
        }

        private T Own<T>(T o) where T : Object
        {
            if (o == null) return o;
            _owned.Add(o);
            if (o is Sprite s && s.texture != null && !_owned.Contains(s.texture)) _owned.Add(s.texture);
            return o;
        }

        private static Sprite MakeSprite(Texture2D tex, string name)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }

        // Form per Abstandsfunktion (Koordinaten −1..1, negativ = innen): weiße Füllung (wird getönt), dunkler Rand
        public static Texture2D BuildShapeTexture(int size, System.Func<float, float, float> sd)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float aa = 2.5f / size;
            const float outline = 0.2f;
            var dark = new Color(0.16f, 0.09f, 0.04f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float d = sd(u, v);
                    float alpha = 1f - Mathf.SmoothStep(-aa, aa, d);
                    float fill = 1f - Mathf.SmoothStep(-outline - aa, -outline + aa, d);
                    Color c = Color.Lerp(dark, Color.white, fill);
                    c.a = alpha;
                    px[y * size + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        private static float SdCircle(float x, float y) => Mathf.Sqrt(x * x + y * y) - 0.92f;
        private static float SdRing(float x, float y) => Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 0.62f) - 0.32f;
        private static float SdDiamond(float x, float y) => (Mathf.Abs(x) + Mathf.Abs(y) - 0.95f) * 0.7071f;
        private static float SdBox(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
        private static float SdSquare(float x, float y) => SdBox(x, y, 0.82f, 0.82f, 0.2f);
        // Marktstand: Kasten mit Dach (Giebel)
        private static float SdStall(float x, float y)
        {
            float body = SdBox(x, y + 0.38f, 0.7f, 0.52f, 0.08f);
            float roof = Mathf.Max(-(y + 0.02f), (y - 0.95f + 0.95f * Mathf.Abs(x)) * 0.72f);
            return Mathf.Min(body, roof);
        }
        // Flieger: Dreieck mit Spitze nach oben (gedreht in Flugrichtung)
        private static float SdTriangle(float x, float y)
        {
            float side = (Mathf.Abs(x) * 0.894f + (y - 0.9f) * 0.447f);
            return Mathf.Max(side, -(y + 0.75f));
        }

        // Ersatz-Pfeil nach oben (falls OffscreenIndicatorUI fehlt)
        private static float SdArrow(float x, float y)
        {
            float head = Mathf.Max(-(y - 0.05f), (y - 0.95f + 1.0f * Mathf.Abs(x)) * 0.7f);
            float shaft = SdBox(x, y + 0.45f, 0.32f, 0.52f, 0.04f);
            return Mathf.Min(head, shaft);
        }

        // Harte Kreismaske (weiß)
        public static Texture2D BuildCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    px[y * size + x] = dx * dx + dy * dy <= r * r ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // Runder Holzrahmen im Stil der TopBar (panel_wood): dunkle Außenkante, Holzring mit Maserung, goldene Innenlinie,
        // vier Messingnieten. ringFrac = Rahmenbreite / Außenradius.
        public static Texture2D BuildFrameTexture(int size, float ringFrac)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float aa = 1.5f / half;
            float inner = 1f - ringFrac;              // Innenkante des Holzes
            float goldIn = inner - 0.018f;            // goldene Linie innen
            var edge = new Color(0.12f, 0.07f, 0.03f);
            var woodA = new Color(0.36f, 0.23f, 0.12f);
            var woodB = new Color(0.55f, 0.36f, 0.19f);
            var gold = new Color(0.95f, 0.78f, 0.4f);
            var brass = new Color(0.85f, 0.66f, 0.3f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(u * u + v * v);
                    Color c;
                    float a = 1f;
                    if (d > 1f - aa) a = Mathf.Clamp01((1f - d) / aa);
                    if (d < goldIn - 0.012f)
                    {
                        // Innenschatten auf der Karte
                        float s = Mathf.Clamp01((d - (goldIn - 0.07f)) / 0.058f);
                        c = new Color(0f, 0f, 0f, 0.45f * s * s);
                        px[y * size + x] = c;
                        continue;
                    }
                    if (d < goldIn) c = edge;
                    else if (d < inner) c = gold;
                    else if (d > 1f - 0.035f) c = edge;
                    else
                    {
                        float ang = Mathf.Atan2(v, u);
                        float t = (d - inner) / Mathf.Max(0.001f, 1f - 0.035f - inner); // 0 innen … 1 außen
                        float grain = Mathf.PerlinNoise(ang * 9f + 31f, d * 55f);
                        float stripes = 0.5f + 0.5f * Mathf.Sin(ang * 70f + grain * 6f);
                        c = Color.Lerp(woodA, woodB, 0.35f + 0.4f * grain + 0.15f * stripes);
                        c *= 0.82f + 0.3f * Mathf.Sin(t * Mathf.PI); // gewölbt: Mitte heller
                        // feine Kanten oben/unten des Rings
                        if (t < 0.08f || t > 0.92f) c *= 0.75f;
                    }
                    // Messingnieten auf den Diagonalen
                    float mid = (inner + 1f - 0.035f) * 0.5f;
                    for (int k = 0; k < 4; k++)
                    {
                        float ang = Mathf.PI * 0.25f + k * Mathf.PI * 0.5f;
                        float rx = u - Mathf.Cos(ang) * mid, ry = v - Mathf.Sin(ang) * mid;
                        float rd = Mathf.Sqrt(rx * rx + ry * ry);
                        float rr = ringFrac * 0.28f;
                        if (rd < rr)
                        {
                            float sh = Mathf.Clamp01(0.6f + (-rx + ry) / rr * 0.5f);
                            c = Color.Lerp(brass * 0.55f, brass * 1.2f, sh);
                            if (rd > rr * 0.8f) c = edge;
                        }
                    }
                    c.a = a;
                    px[y * size + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ---------------- Hilfen ----------------

        private static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Center(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
