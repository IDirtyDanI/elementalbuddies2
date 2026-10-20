using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Pfeile am Bildschirmrand zu allen „Points of Interest“, solange sie außerhalb des Bildschirms liegen – ein Pfeil
    // je Ziel (Spieltest 2026-10-18 Paket R, vorher 3 feste Kanäle): alle lebenden Champion-Bedrohungen
    // (Beschwörer/Bannwirker) und Bosse, frisch geöffnete Portale, erwachte Schreine und der geöffnete Händler.
    // Höchstens MaxArrows (8) gleichzeitig; bei mehr Zielen entscheiden Priorität (Bedrohung/Boss > Portal > Schrein
    // > Händler) und Abstand. Pfeile, die am Rand übereinander lägen, werden entlang des Rands auseinandergeschoben
    // (MinArrowSpacing). Rechenregeln (Auswahl, Rand-Projektion, Entzerrung) in EdgeArrowLayout (Rules, getestet).
    // Funktioniert mit Screen Space Overlay + CanvasScaler (und Screen Space Camera): Bildschirm → Canvas-Koordinaten
    // per RectTransformUtility.
    //
    // Pool: Eintrag 0 sind die Szenen-Objekte (Arrow/DistanceText/TargetIcon), weitere Einträge werden bei Bedarf
    // einmalig kopiert und danach wiederverwendet; ungenutzte werden ausgeblendet. Ein Ziel behält seinen Eintrag,
    // solange es angezeigt wird (Puls/Ping laufen ungestört weiter).
    //
    // Ebene (Spieltest 2026-10-17 #2): Das Objekt bekommt ein verschachteltes Canvas mit overrideSorting
    // (ArrowSortingOrder = 30) – liegt damit über HUD, Buddy-Menü und Gruppenliste, behält aber Skalierung und
    // Koordinaten des HUD-Canvas (TopBar/Minimap-Abstände stimmen weiter). Unter modalen Menüs (Pause, Kartenwahl,
    // Händler, Game Over, Tastenbelegung) wird es ausgeblendet statt überdeckt.
    // Effekt (#7): Grundpuls + weicher Leuchtschein hinter jedem Pfeil; beim Erscheinen bzw. neuen Ziel ein „Ping“
    // (Ring + Randblitz, ~0,6 s). Alles unscaled (läuft in Zeitlupe weiter); Sprites prozedural erzeugt.
    public class OffscreenIndicatorUI : MonoBehaviour
    {
        public const int ArrowSortingOrder = 30; // über HUD (0) und Gruppenliste (−5), unter Pings (40) und Hinweisen (75)

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
        [Tooltip("HUD-Leiste oben (TopBar): Pfeile bleiben darunter. Leer → Objekt \"TopBar\" im Canvas.")]
        public RectTransform TopHud;
        [Tooltip("Abstand der Pfeilmitte zur Unterkante der TopBar, Canvas-Einheiten.")]
        public float TopHudPadding = 46f;
        [Tooltip("Abstand der Pfeilmitte zur Minimap oben links (MinimapUI), Canvas-Einheiten.")]
        public float MinimapPadding = 40f;
        [Tooltip("Abstand der Pfeilmitte zu Meldungs-Banner (ToastUI) und Ziel-Panels oben rechts (ObjectiveUI), Canvas-Einheiten.")]
        public float PanelPadding = 40f;
        [Tooltip("Mindestabstand benachbarter Pfeile entlang des Bildschirmrands (≈ Pfeil + Beschriftung), Canvas-Einheiten.")]
        public float MinArrowSpacing = 80f;

        [Header("Ziele")]
        [Tooltip("Höchstzahl gleichzeitiger Pfeile; bei mehr Zielen gewinnen Priorität und Nähe.")]
        [Range(1, 16)] public int MaxArrows = EdgeArrowLayout.DefaultMaxArrows;
        [Tooltip("Wie lange nach dem Öffnen ein Portal angezeigt wird (s).")]
        public float PortalHighlightDuration = 6f;
        public Color PortalColor = new Color(0.75f, 0.35f, 1f);
        [Tooltip("Pfeilfarbe für Bosse (wie der Boss-Punkt der Minimap, etwas heller).")]
        public Color BossColor = new Color(0.9f, 0.12f, 0.15f);
        public bool TintWithElement = true;

        [Header("Effekt")]
        [Tooltip("Dauer des Pings beim Erscheinen (Ring + Randblitz), Sekunden (unscaled).")]
        public float PingDuration = 0.6f;
        [Tooltip("Frühester erneuter Ping für dasselbe Ziel (z. B. wenn es kurz im Bild war), Sekunden.")]
        public float PingCooldown = 3f;
        [Tooltip("Frühester erneuter Klang für dasselbe Ziel, Sekunden (nur starke Ziele: Bedrohung/Boss/Portal).")]
        public float PingSoundCooldown = 8f;
        public bool PingSound = true;

        // Abstufung: Bedrohungen/Bosse/neue Monster stark, Schrein/Händler dezent
        private struct FxStyle
        {
            public float PulseAmp, PulseHz, GlowAlpha, GlowScale, RingScale, FlashAlpha, Brighten;
            public bool Sound;
        }
        private static readonly FxStyle Strong = new FxStyle { PulseAmp = 0.14f, PulseHz = 1.6f, GlowAlpha = 0.75f, GlowScale = 3.0f, RingScale = 3.4f, FlashAlpha = 0.55f, Brighten = 0.35f, Sound = true };
        private static readonly FxStyle Subtle = new FxStyle { PulseAmp = 0.07f, PulseHz = 0.9f, GlowAlpha = 0.42f, GlowScale = 2.4f, RingScale = 2.6f, FlashAlpha = 0.28f, Brighten = 0.15f, Sound = false };

        private Camera _cam;
        private static readonly Vector3[] _corners = new Vector3[4];
        private Canvas _canvas;
        private RectTransform _area;
        private Transform _player;
        private Canvas _layer;                 // eigenes, verschachteltes Canvas (overrideSorting)
        private CanvasGroup _group;            // Ausblenden unter modalen Menüs
        private UpgradeScreenUI _upgradeUi;
        private MerchantScreenUI _merchantUi;
        private GameOverUI _gameOverUi;
        private float _nextModalLookup;
        private ObjectiveUI _objectiveUi;
        private float _nextObjectiveLookup;
        private readonly Dictionary<int, float> _lastPing = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _lastSound = new Dictionary<int, float>();
        private static Sprite _glowSprite, _ringSprite;

        // Frisch geöffnete Portale (mehrere gleichzeitig möglich) mit Ablaufzeit (unscaled)
        private readonly List<SpawnPortal> _portals = new List<SpawnPortal>();
        private readonly List<float> _portalUntil = new List<float>();

        void Awake()
        {
            var parentCanvas = GetComponentInParent<Canvas>();
            _canvas = parentCanvas != null ? parentCanvas.rootCanvas : null; // HUD-Canvas (Skalierung, TopBar, Kamera)
            SetupLayer();
            if (Arrow != null) _area = Arrow.parent as RectTransform;
            if (ArrowImage == null && Arrow != null) ArrowImage = Arrow.GetComponent<Image>();
            if (TopHud == null && _canvas != null)
            {
                var t = SafeAreaFitter.FindInCanvas(_canvas.transform, "TopBar"); // auch im SafeArea-Bereich (Touch)
                if (t != null) TopHud = t as RectTransform;
            }
            // Früher lag der Pfeil unter dem Meldungs-Banner (Geschwister-Reihenfolge); mit eigenem Canvas liegt er
            // bewusst über allem Spiel-UI (Spieltest 2026-10-17 #2).
        }

        // Verschachteltes Canvas: erbt Skalierung/Render-Modus vom HUD-Canvas, sortiert aber eigenständig.
        // Kein GraphicRaycaster – Pfeile sind nicht klickbar und dürfen keine Klicks abfangen.
        private void SetupLayer()
        {
            _layer = GetComponent<Canvas>();
            if (_layer == null) _layer = gameObject.AddComponent<Canvas>();
            _layer.overrideSorting = true;
            _layer.sortingOrder = ArrowSortingOrder;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
        }

        void Start()
        {
            WaveManager.OnPortalOpened += HandlePortalOpened;
            _player = PlayerAvatar.Local != null ? PlayerAvatar.Local.transform : null;
            HideAll();
        }

        void OnDestroy()
        {
            WaveManager.OnPortalOpened -= HandlePortalOpened;
        }

        private void HandlePortalOpened(SpawnPortal p)
        {
            if (p == null) return;
            float until = Time.unscaledTime + PortalHighlightDuration;
            int i = _portals.IndexOf(p);
            if (i >= 0) _portalUntil[i] = until; // erneut geöffnet → Anzeige verlängern
            else { _portals.Add(p); _portalUntil.Add(until); }
        }

        // Ein Pool-Eintrag: Pfeil + Distanz + Icon (+ Effekt). Eintrag 0 = Szenen-Objekte, weitere = Kopien.
        private class Channel
        {
            public RectTransform Arrow;
            public Image ArrowImage;
            public TextMeshProUGUI DistanceText;
            public Image TargetIcon;
            public bool Visible = true;
            // Effekt
            public Image Glow, Ring, Flash;
            public Vector3 BaseScale = Vector3.one;
            public int TargetId;
            public float PingStart = -100f;
            public bool Used;                  // in diesem Frame vergeben
        }

        private readonly List<Channel> _pool = new List<Channel>();

        // Ein Ziel dieses Frames (nur außerhalb des Bildschirms)
        private struct Candidate
        {
            public Object Target;
            public int Id;
            public Vector3 Pos;
            public Color Color;
            public Sprite Icon;
            public bool Strong;
            public int Priority;
            public float Distance;
            public Vector2 LocalDir;           // Richtung im Bereich (Canvas-Einheiten, normiert)
        }

        private readonly List<Candidate> _candidates = new List<Candidate>();
        private int[] _prio = new int[16];
        private float[] _dist = new float[16];
        private int[] _chosen = new int[16];
        private Vector2[] _edge = new Vector2[16];
        private Channel[] _assigned = new Channel[16];

        // Frame-Geometrie (einmal je Frame berechnet)
        private Vector2 _localCenter, _areaCenter;
        private float _halfW, _halfDown, _halfUp;
        private Camera _uiCam;

        private void EnsurePool()
        {
            if (_pool.Count > 0) return;
            var first = new Channel { Arrow = Arrow, ArrowImage = ArrowImage, DistanceText = DistanceText, TargetIcon = TargetIcon };
            _pool.Add(first);
            CreateFx(first);
        }

        // Neuer Pool-Eintrag: Kopie von Pfeil, Distanz-Text und Icon (einmalig, danach wiederverwendet)
        private Channel CloneChannel(string suffix)
        {
            var ch = new Channel();
            if (Arrow != null)
            {
                ch.Arrow = Instantiate(Arrow.gameObject, Arrow.parent).GetComponent<RectTransform>();
                ch.Arrow.name = Arrow.name + suffix;
                ch.ArrowImage = ch.Arrow.GetComponent<Image>();
                if (_pool.Count > 0) ch.Arrow.localScale = _pool[0].BaseScale; // Original pulsiert evtl. gerade → Grundskalierung
            }
            if (DistanceText != null)
            {
                ch.DistanceText = Instantiate(DistanceText.gameObject, DistanceText.transform.parent).GetComponent<TextMeshProUGUI>();
                ch.DistanceText.name = DistanceText.name + suffix;
            }
            if (TargetIcon != null)
            {
                ch.TargetIcon = Instantiate(TargetIcon.gameObject, TargetIcon.transform.parent).GetComponent<Image>();
                ch.TargetIcon.name = TargetIcon.name + suffix;
            }
            SetVisible(ch, false);
            CreateFx(ch);
            return ch;
        }

        void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            EnsurePool();
            // Unter modalen Menüs unsichtbar (CanvasGroup; ein abgeschaltetes verschachteltes Canvas würde seine Kinder
            // wieder ins HUD-Canvas einsortieren). Zustand einfrieren, damit nach dem Schließen kein Ping-Schwall folgt.
            bool modal = IsModalOpen();
            if (_group != null) _group.alpha = modal ? 0f : 1f;
            if (modal) return;
            if (Arrow == null || _cam == null || _area == null)
            {
                HideAll();
                return;
            }

            PrepareGeometry();
            CollectCandidates();

            // Auswahl: höchstens MaxArrows nach Priorität und Abstand
            int n = _candidates.Count;
            EnsureBuffers(n);
            for (int i = 0; i < n; i++) { _prio[i] = _candidates[i].Priority; _dist[i] = _candidates[i].Distance; }
            int shown = EdgeArrowLayout.SelectByPriority(_prio, _dist, n, Mathf.Max(1, MaxArrows), _chosen);

            // Rand-Projektion, dann Entzerrung entlang des Rands, danach Minimap/Panels meiden
            for (int k = 0; k < shown; k++)
                _edge[k] = EdgeArrowLayout.ProjectToEdge(_areaCenter, _candidates[_chosen[k]].LocalDir, _halfW, _halfDown, _halfUp);
            EdgeArrowLayout.SpreadOnEdge(_edge, shown, _areaCenter, _halfW, _halfDown, _halfUp, MinArrowSpacing);
            for (int k = 0; k < shown; k++) _edge[k] = AvoidPanels(AvoidMinimap(_edge[k]));

            AssignChannels(shown);
            for (int k = 0; k < shown; k++) Draw(_assigned[k], _candidates[_chosen[k]], _edge[k]);
            for (int i = 0; i < _pool.Count; i++)
                if (!_pool[i].Used) SetVisible(_pool[i], false);
            PruneTimers();
        }

        private void EnsureBuffers(int n)
        {
            if (_prio.Length >= n) return;
            int len = Mathf.Max(n, _prio.Length * 2);
            System.Array.Resize(ref _prio, len);
            System.Array.Resize(ref _dist, len);
            System.Array.Resize(ref _chosen, len);
            System.Array.Resize(ref _edge, len);
            System.Array.Resize(ref _assigned, len);
        }

        // Bildschirmmitte im Bereich und das um EdgePadding verkleinerte Rand-Rechteck (oben unter der TopBar)
        private void PrepareGeometry()
        {
            _uiCam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screenCenter, _uiCam, out _localCenter);

            Rect r = _area.rect;
            _halfW = Mathf.Max(1f, r.width * 0.5f - EdgePadding);
            _halfDown = Mathf.Max(1f, r.height * 0.5f - EdgePadding);
            _areaCenter = r.center;
            // Oben nur bis unter die TopBar – sonst lagen Pfeil und „74 m“ auf HP-/Mana-Leiste bzw. der Wellen-Anzeige
            // und ragten unten aus der Leiste (Bekannte-Probleme #158)
            _halfUp = _halfDown;
            if (TopHud != null && TopHud.gameObject.activeInHierarchy)
            {
                TopHud.GetWorldCorners(_corners); // 0 = unten links
                float barBottom = _area.InverseTransformPoint(_corners[0]).y;
                _halfUp = Mathf.Clamp(barBottom - TopHudPadding - _areaCenter.y, 1f, _halfDown);
            }
        }

        // Alle Ziele sammeln; sichtbare (im Bild) bekommen wie bisher keinen Pfeil
        private void CollectCandidates()
        {
            _candidates.Clear();
            if (_player == null && PlayerAvatar.Local != null) _player = PlayerAvatar.Local.transform;

            // Champion-Bedrohungen (nur der Champion kann sie besiegen → immer zeigen, wo sie sind) und Bosse
            var enemies = EnemyBrain.All;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null || e.IsDead || e.Config == null) continue;
                if (e.Config.IsChampionThreat)
                    Add(e, e.transform.position + Vector3.up, ThreatVisuals.WardColor, e.Config.Icon, true, EdgeArrowLayout.PriorityThreat);
                else if (e.IsBoss)
                    Add(e, e.transform.position + Vector3.up, BossColor, e.Config.Icon, true, EdgeArrowLayout.PriorityThreat);
            }

            // Frisch geöffnete Portale (neue Monster)
            float now = Time.unscaledTime;
            for (int i = 0; i < _portals.Count; i++)
            {
                var p = _portals[i];
                if (p == null || now >= _portalUntil[i]) continue;
                Add(p, p.SpawnTransform.position, PortalColor, PortalIcon, true, EdgeArrowLayout.PriorityPortal);
            }

            // Erwachte Schreine (heute höchstens einer gleichzeitig, aber alle anzeigen, falls sich das ändert)
            var smgr = ShrineManager.Instance;
            Shrine active = smgr != null && smgr.ActiveShrine != null && smgr.ActiveShrine.IsAwakened ? smgr.ActiveShrine : null;
            if (active != null) AddShrine(active);
            var shrines = Shrine.All;
            for (int i = 0; i < shrines.Count; i++)
                if (shrines[i] != null && shrines[i] != active && shrines[i].IsAwakened) AddShrine(shrines[i]);

            // Geöffneter Händler
            var mgr = MerchantManager.Instance;
            Merchant m = mgr != null && mgr.ActiveMerchant != null && mgr.ActiveMerchant.IsActive ? mgr.ActiveMerchant : null;
            if (m != null)
                Add(m, m.CenterPosition, TintWithElement ? m.Color : Color.white, mgr.GetEmblem(m.Kind), false, EdgeArrowLayout.PriorityMerchant);
        }

        private void AddShrine(Shrine s)
        {
            Sprite icon = ElementIcons != null && s.ElementIndex < ElementIcons.Length ? ElementIcons[s.ElementIndex] : null;
            Add(s, s.CenterPosition, TintWithElement ? ElementInfo.GetColor(s.ElementIndex) : Color.white, icon, false, EdgeArrowLayout.PriorityShrine);
        }

        private void Add(Object target, Vector3 targetPos, Color color, Sprite icon, bool strong, int priority)
        {
            Vector3 vp = _cam.WorldToViewportPoint(targetPos);
            bool behind = vp.z < 0f;
            bool onScreen = !behind
                && vp.x >= OnScreenMargin && vp.x <= 1f - OnScreenMargin
                && vp.y >= OnScreenMargin && vp.y <= 1f - OnScreenMargin;
            if (onScreen) return;

            // Richtung von der Bildschirmmitte (Viewport), hinter der Kamera gespiegelt
            Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (behind) dir = -dir;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;

            // In Bildschirm-Pixeln rechnen (Seitenverhältnis), dann in Canvas-Koordinaten umrechnen
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dirPx = new Vector2(dir.x * Screen.width, dir.y * Screen.height).normalized;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screenCenter + dirPx * 100f, _uiCam, out Vector2 localDirPoint);
            Vector2 localDir = (localDirPoint - _localCenter).normalized;
            if (localDir.sqrMagnitude < 1e-6f) localDir = Vector2.down;

            // Abstand zur eigenen Figur (ohne Figur zur Kamera – dann nur für die Auswahl, Text bleibt leer)
            Vector3 from = _player != null ? _player.position : _cam.transform.position;
            Vector3 d = targetPos - from;
            d.y = 0f;
            float dist = d.magnitude;

            _candidates.Add(new Candidate
            {
                Target = target,
                Id = target != null ? target.GetInstanceID() : 0,
                Pos = targetPos,
                Color = color,
                Icon = icon,
                Strong = strong,
                Priority = priority,
                Distance = dist,
                LocalDir = localDir,
            });
        }

        // Ziele auf Pool-Einträge verteilen: bereits angezeigte Ziele behalten ihren Eintrag (kein erneuter Ping,
        // Puls läuft weiter), neue Ziele bekommen freie Einträge; fehlen welche, wird einmalig kopiert.
        private void AssignChannels(int shown)
        {
            for (int i = 0; i < _pool.Count; i++) _pool[i].Used = false;
            for (int k = 0; k < shown; k++)
            {
                _assigned[k] = null;
                int id = _candidates[_chosen[k]].Id;
                for (int i = 0; i < _pool.Count; i++)
                {
                    var ch = _pool[i];
                    if (!ch.Used && ch.Visible && ch.TargetId == id) { ch.Used = true; _assigned[k] = ch; break; }
                }
            }
            for (int k = 0; k < shown; k++)
            {
                if (_assigned[k] != null) continue;
                Channel free = null;
                // zuerst ausgeblendete Einträge, dann solche, deren Ziel nicht mehr angezeigt wird
                for (int i = 0; i < _pool.Count && free == null; i++)
                    if (!_pool[i].Used && !_pool[i].Visible) free = _pool[i];
                for (int i = 0; i < _pool.Count && free == null; i++)
                    if (!_pool[i].Used) free = _pool[i];
                if (free == null)
                {
                    free = CloneChannel("_" + _pool.Count);
                    _pool.Add(free);
                }
                free.Used = true;
                _assigned[k] = free;
            }
        }

        private void Draw(Channel ch, Candidate c, Vector2 pos)
        {
            if (ch == null || ch.Arrow == null) return;
            FxStyle style = c.Strong ? Strong : Subtle;
            Vector2 localDir = c.LocalDir;

            bool appeared = !ch.Visible;
            SetVisible(ch, true);
            ch.Arrow.anchoredPosition = ToAnchored(ch.Arrow, pos);
            float angle = Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;
            ch.Arrow.localRotation = Quaternion.Euler(0f, 0f, angle + SpriteAngleOffset);
            if (ch.ArrowImage != null) ch.ArrowImage.color = c.Color;

            if (appeared || c.Id != ch.TargetId) TryPing(ch, c.Id, style);
            ch.TargetId = c.Id;
            UpdateFx(ch, pos, localDir, angle, c.Color, style);

            Vector2 labelPos = pos - localDir * LabelOffset;
            if (ch.DistanceText != null)
            {
                ch.DistanceText.text = _player != null ? $"{Mathf.RoundToInt(c.Distance)} m" : "";
                ch.DistanceText.rectTransform.anchoredPosition = ToAnchored(ch.DistanceText.rectTransform, labelPos);
            }
            if (ch.TargetIcon != null)
            {
                ch.TargetIcon.sprite = c.Icon;
                ch.TargetIcon.enabled = c.Icon != null;
                ch.TargetIcon.rectTransform.anchoredPosition = ToAnchored(ch.TargetIcon.rectTransform, labelPos + Vector2.up * 30f);
            }
        }

        // Abgelaufene Portale und alte Ping-Zeiten entfernen (viele Bedrohungen über einen langen Lauf)
        private void PruneTimers()
        {
            float now = Time.unscaledTime;
            for (int i = _portals.Count - 1; i >= 0; i--)
            {
                if (_portals[i] != null && now < _portalUntil[i]) continue;
                _portals.RemoveAt(i);
                _portalUntil.RemoveAt(i);
            }
            if (_lastPing.Count > 64) PruneOlderThan(_lastPing, now - PingCooldown);
            if (_lastSound.Count > 64) PruneOlderThan(_lastSound, now - PingSoundCooldown);
        }

        private static readonly List<int> _pruneKeys = new List<int>();

        private static void PruneOlderThan(Dictionary<int, float> map, float threshold)
        {
            _pruneKeys.Clear();
            foreach (var kv in map)
                if (kv.Value < threshold) _pruneKeys.Add(kv.Key);
            for (int i = 0; i < _pruneKeys.Count; i++) map.Remove(_pruneKeys[i]);
        }

        // Spieltest 2026-10-17 (Abschlusstest D): Pfeil, Symbol und „xx m“ lagen über dem Ziel-Panel oben rechts
        // („Eis-Schrein erwacht!“) bzw. konnten das Meldungs-Banner oben mittig überdecken (eigenes Canvas über dem HUD).
        // → sichtbare Panels meiden: neben bzw. unter das Panel schieben (kürzester Weg, nie nach oben, im Bereich bleiben).
        private Vector2 AvoidPanels(Vector2 pos)
        {
            if (_objectiveUi == null && Time.unscaledTime >= _nextObjectiveLookup)
            {
                _nextObjectiveLookup = Time.unscaledTime + 2f;
                _objectiveUi = FindFirstObjectByType<ObjectiveUI>(FindObjectsInactive.Include);
            }
            if (_objectiveUi != null)
            {
                pos = AvoidRect(pos, _objectiveUi.Panel);
                pos = AvoidRect(pos, _objectiveUi.MerchantPanel); // liegt unter dem Schrein-Panel → danach prüfen
            }
            var toast = ToastUI.Instance;
            if (toast != null && toast.Root != null && toast.Root.alpha > 0.01f) pos = AvoidRect(pos, toast.Root.gameObject);
            // Spieltest 2026-10-18: Pfeile am unteren Rand lagen auf Bau- und Fähigkeitenleiste → über die Leisten schieben
            if (_bottomBars == null && Time.unscaledTime >= _nextBarLookup)
            {
                _nextBarLookup = Time.unscaledTime + 2f;
                var build = FindFirstObjectByType<BuildBarUI>(FindObjectsInactive.Include);
                var abilities = FindFirstObjectByType<AbilityBarUI>(FindObjectsInactive.Include);
                if (build != null || abilities != null)
                    _bottomBars = new[] { build != null ? build.gameObject : null, abilities != null ? abilities.gameObject : null };
            }
            if (_bottomBars != null)
                for (int i = 0; i < _bottomBars.Length; i++) pos = AvoidRectAbove(pos, _bottomBars[i]);
            return pos;
        }

        private GameObject[] _bottomBars;
        private float _nextBarLookup;

        // Wie AvoidRect, aber für Leisten am unteren Rand: Pfeil über die Leiste setzen
        private Vector2 AvoidRectAbove(Vector2 pos, GameObject panel)
        {
            if (panel == null || !panel.activeInHierarchy) return pos;
            var rt = panel.transform as RectTransform;
            if (rt == null) return pos;
            rt.GetWorldCorners(_corners);
            Vector2 min = (Vector2)_area.InverseTransformPoint(_corners[0]) - Vector2.one * PanelPadding;
            Vector2 max = (Vector2)_area.InverseTransformPoint(_corners[2]) + Vector2.one * PanelPadding;
            if (pos.x < min.x || pos.x > max.x || pos.y < min.y || pos.y > max.y) return pos;
            return new Vector2(pos.x, max.y);
        }

        private Vector2 AvoidRect(Vector2 pos, GameObject panel)
        {
            if (panel == null || !panel.activeInHierarchy) return pos;
            var rt = panel.transform as RectTransform;
            if (rt == null) return pos;
            rt.GetWorldCorners(_corners);
            Vector2 min = (Vector2)_area.InverseTransformPoint(_corners[0]) - Vector2.one * PanelPadding;
            Vector2 max = (Vector2)_area.InverseTransformPoint(_corners[2]) + Vector2.one * PanelPadding;
            if (pos.x < min.x || pos.x > max.x || pos.y < min.y || pos.y > max.y) return pos;
            Rect r = _area.rect;
            float left = r.xMin + EdgePadding, right = r.xMax - EdgePadding;
            float best = float.PositiveInfinity;
            Vector2 result = pos;
            if (min.x >= left && pos.x - min.x < best) { best = pos.x - min.x; result = new Vector2(min.x, pos.y); }
            if (max.x <= right && max.x - pos.x < best) { best = max.x - pos.x; result = new Vector2(max.x, pos.y); }
            if (pos.y - min.y < best) result = new Vector2(pos.x, min.y);
            return result;
        }

        // Pfeil am Rand oben links würde auf der Minimap liegen → am Rand entlang neben bzw. unter sie schieben
        // (kürzerer Weg gewinnt)
        private Vector2 AvoidMinimap(Vector2 pos)
        {
            var mm = MinimapUI.Instance;
            if (mm == null || !mm.IsShown || mm.Rect == null) return pos;
            mm.Rect.GetWorldCorners(_corners);
            Vector2 min = _area.InverseTransformPoint(_corners[0]);
            Vector2 max = _area.InverseTransformPoint(_corners[2]);
            min -= Vector2.one * MinimapPadding;
            max += Vector2.one * MinimapPadding;
            if (pos.x < min.x || pos.x > max.x || pos.y < min.y || pos.y > max.y) return pos;
            float toRight = max.x - pos.x;
            float toBelow = pos.y - min.y;
            if (toRight <= toBelow) pos.x = max.x;
            else pos.y = min.y;
            return pos;
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

        // Alle Pool-Einträge ausblenden
        private void HideAll()
        {
            EnsurePool();
            for (int i = 0; i < _pool.Count; i++) SetVisible(_pool[i], false);
        }

        private static void SetVisible(Channel ch, bool v)
        {
            if (ch == null || ch.Visible == v) return;
            ch.Visible = v;
            if (ch.Arrow != null) ch.Arrow.gameObject.SetActive(v);
            if (ch.DistanceText != null) ch.DistanceText.gameObject.SetActive(v);
            if (ch.TargetIcon != null) ch.TargetIcon.gameObject.SetActive(v);
            if (ch.Glow != null) ch.Glow.gameObject.SetActive(v);
            if (!v)
            {
                if (ch.Ring != null) ch.Ring.gameObject.SetActive(false);
                if (ch.Flash != null) ch.Flash.gameObject.SetActive(false);
            }
        }

        // ---------------- Modale Menüs ----------------

        // Pause, Kartenwahl, Händler, Game Over/Morgengrauen, Tastenbelegung: Pfeile ausblenden (sie lägen sonst darüber)
        private bool IsModalOpen()
        {
            if (PauseManager.IsPaused || KeyBindingsMenu.IsOpen) return true;
            var pause = PauseMenuUI.Active;
            if (pause != null && pause.Root != null && pause.Root.activeInHierarchy) return true;
            if ((_upgradeUi == null || _merchantUi == null || _gameOverUi == null) && Time.unscaledTime >= _nextModalLookup)
            {
                _nextModalLookup = Time.unscaledTime + 2f;
                if (_upgradeUi == null) _upgradeUi = FindFirstObjectByType<UpgradeScreenUI>(FindObjectsInactive.Include);
                if (_merchantUi == null) _merchantUi = FindFirstObjectByType<MerchantScreenUI>(FindObjectsInactive.Include);
                if (_gameOverUi == null) _gameOverUi = FindFirstObjectByType<GameOverUI>(FindObjectsInactive.Include);
            }
            if (_upgradeUi != null && _upgradeUi.Panel != null && _upgradeUi.Panel.activeInHierarchy) return true;
            if (_merchantUi != null && _merchantUi.Panel != null && _merchantUi.Panel.activeInHierarchy) return true;
            if (_gameOverUi != null && _gameOverUi.Panel != null && _gameOverUi.Panel.activeInHierarchy) return true;
            return false;
        }

        // ---------------- Effekt (Puls, Leuchtschein, Ping) ----------------

        // Leuchtschein, Ping-Ring und Randblitz als Geschwister VOR dem Pfeil (zeichnen dahinter)
        private void CreateFx(Channel ch)
        {
            if (ch == null || ch.Arrow == null || _area == null) return;
            ch.BaseScale = ch.Arrow.localScale;
            ch.Flash = NewFxImage(ch.Arrow.name + "_Flash", GlowSprite(), ch.Arrow);
            ch.Glow = NewFxImage(ch.Arrow.name + "_Glow", GlowSprite(), ch.Arrow);
            ch.Ring = NewFxImage(ch.Arrow.name + "_Ring", RingSprite(), ch.Arrow);
            ch.Glow.gameObject.SetActive(ch.Visible);
            ch.Ring.gameObject.SetActive(false);
            ch.Flash.gameObject.SetActive(false);
        }

        private Image NewFxImage(string name, Sprite sprite, RectTransform arrow)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_area, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.SetSiblingIndex(arrow.GetSiblingIndex()); // direkt hinter den Pfeil
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        private void TryPing(Channel ch, int id, FxStyle style)
        {
            float now = Time.unscaledTime;
            if (_lastPing.TryGetValue(id, out float last) && now - last < PingCooldown) return;
            _lastPing[id] = now;
            ch.PingStart = now;
            if (PingSound && style.Sound && GameAudio.Has(SfxId.Ping)
                && (!_lastSound.TryGetValue(id, out float ls) || now - ls >= PingSoundCooldown))
            {
                _lastSound[id] = now;
                GameAudio.Play(SfxId.Ping);
            }
        }

        private void UpdateFx(Channel ch, Vector2 pos, Vector2 localDir, float angle, Color color, FxStyle style)
        {
            float now = Time.unscaledTime;
            float size = Mathf.Max(ch.Arrow.rect.width, ch.Arrow.rect.height);
            if (size < 1f) size = 60f;

            // Ping-Verlauf 0..1 (danach 1); in der ersten Hälfte zusätzlich etwas größer
            float pt = PingDuration > 0f ? Mathf.Clamp01((now - ch.PingStart) / PingDuration) : 1f;
            bool pinging = pt < 1f;
            float pingBoost = pinging ? (1f - pt) * (1f - pt) : 0f;

            // Grundpuls: Skalierung + Helligkeit
            float wave = 0.5f + 0.5f * Mathf.Sin(now * style.PulseHz * Mathf.PI * 2f);
            float scale = 1f + style.PulseAmp * wave + 0.35f * pingBoost;
            ch.Arrow.localScale = ch.BaseScale * scale;
            if (ch.ArrowImage != null) ch.ArrowImage.color = Color.Lerp(color, Color.white, style.Brighten * wave + 0.5f * pingBoost);

            if (ch.Glow != null)
            {
                var g = ch.Glow.rectTransform;
                g.anchoredPosition = ToAnchored(g, pos);
                g.sizeDelta = Vector2.one * size * style.GlowScale * (0.92f + 0.16f * wave);
                ch.Glow.color = WithAlpha(color, style.GlowAlpha * (0.65f + 0.35f * wave) + 0.25f * pingBoost);
            }
            if (ch.Ring != null)
            {
                if (ch.Ring.gameObject.activeSelf != pinging) ch.Ring.gameObject.SetActive(pinging);
                if (pinging)
                {
                    var r = ch.Ring.rectTransform;
                    r.anchoredPosition = ToAnchored(r, pos);
                    float e = 1f - (1f - pt) * (1f - pt); // ease-out
                    r.sizeDelta = Vector2.one * size * Mathf.Lerp(0.8f, style.RingScale, e);
                    ch.Ring.color = WithAlpha(Color.Lerp(color, Color.white, 0.3f), 1f - pt);
                }
            }
            if (ch.Flash != null)
            {
                if (ch.Flash.gameObject.activeSelf != pinging) ch.Flash.gameObject.SetActive(pinging);
                if (pinging)
                {
                    // Länglicher Schein direkt am Bildschirmrand, quer zur Pfeilrichtung
                    var f = ch.Flash.rectTransform;
                    f.anchoredPosition = ToAnchored(f, pos + localDir * EdgePadding);
                    f.sizeDelta = new Vector2(size * 3.2f, size * 11f);
                    f.localRotation = Quaternion.Euler(0f, 0f, angle);
                    float a = pt < 0.15f ? pt / 0.15f : 1f - (pt - 0.15f) / 0.85f;
                    ch.Flash.color = WithAlpha(color, style.FlashAlpha * a);
                }
            }
        }

        private static Color WithAlpha(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

        // Prozedurale Sprites (keine Fremd-Assets): weicher Radialverlauf bzw. weicher Ring, 128 px, weiß
        private static Sprite GlowSprite()
        {
            if (_glowSprite == null) _glowSprite = MakeRadial("FxGlow", r => { float t = Mathf.Clamp01(1f - r); return t * t * (3f - 2f * t) * t; });
            return _glowSprite;
        }

        private static Sprite RingSprite()
        {
            if (_ringSprite == null) _ringSprite = MakeRadial("FxRing", r => { float d = (r - 0.82f) / 0.09f; return Mathf.Exp(-d * d); });
            return _ringSprite;
        }

        private static Sprite MakeRadial(string name, System.Func<float, float> alpha)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(r >= 1f ? 0f : alpha(r)) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            sp.name = name;
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }
    }
}
