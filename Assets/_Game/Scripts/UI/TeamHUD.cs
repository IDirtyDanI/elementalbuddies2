using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Mitspieler-Anzeige (nur im Mehrspieler): kleine Liste links unter der Top-Bar (Name, Champion-Porträt,
    // LP-Balken, "Gefallen – Wiederbelebung in Xs") und Namensschilder über fremden Spielfiguren.
    // Baut zur Laufzeit zwei eigene Overlay-Canvases (wird von HUDManager angelegt):
    // - Namensschilder (sortingOrder NameTagSortingOrder = −10): Welt-UI, liegt unter dem HUD (Spieltest 2026-10-17 #1)
    // - Gruppenliste (ListSortingOrder = −5): unter dem HUD-Canvas (0), damit Buddy-Menü, Händler, Kartenwahl,
    //   Pause usw. sie überdecken (#5). Die Liste ist rein informativ (kein raycastTarget).
    public class TeamHUD : MonoBehaviour
    {
        public static readonly Color[] SlotColors =
        {
            new Color(0.95f, 0.78f, 0.35f), // Host: Gold
            new Color(0.45f, 0.75f, 1f),
            new Color(0.55f, 0.9f, 0.45f),
            new Color(0.95f, 0.5f, 0.75f),
        };

        public Vector2 ListOrigin = new Vector2(24f, -130f); // Canvas-Einheiten ab oben links
        public float MinimapGap = 14f;                  // Abstand unter der Minimap (MinimapUI), falls sie oben links sitzt
        public float RowHeight = 64f;
        public float RowWidth = 330f;
        public float NameTagHeight = 2.6f;               // Weltmeter über dem Fußpunkt

        private class Row
        {
            public RectTransform Root;
            public Image Portrait;
            public Image PortraitBg;
            public TextMeshProUGUI Name;
            public Image HpFill;
            public TextMeshProUGUI Status;
        }

        private class Tag
        {
            public RectTransform Root;
            public TextMeshProUGUI Text;
        }

        private TMP_FontAsset _font;
        private Sprite[] _portraits = new Sprite[0];
        // Ebenen (siehe Wiki UI-HUD, „Canvas-Ebenen“): FeedbackCanvas −20 < Namensschilder < Gruppenliste < HUD 0
        public const int NameTagSortingOrder = -10;
        public const int ListSortingOrder = -5;

        private Canvas _canvas;      // Gruppenliste
        private Canvas _tagCanvas;   // Namensschilder
        private RectTransform _list;
        private RectTransform _tags;
        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<PlayerAvatar, Tag> _tagMap = new Dictionary<PlayerAvatar, Tag>();
        private readonly List<PlayerAvatar> _remove = new List<PlayerAvatar>();
        private readonly List<PlayerAvatar> _others = new List<PlayerAvatar>();

        public void Setup(TMP_FontAsset font, Sprite[] portraits)
        {
            _font = font;
            if (portraits != null) _portraits = portraits;
            if (_canvas == null) Build();
        }

        void Start()
        {
            if (_canvas == null) Build();
        }

        void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_tagCanvas != null) Destroy(_tagCanvas.gameObject);
        }

        private void Build()
        {
            _tagCanvas = NewOverlayCanvas("TeamHUD_NameTags", NameTagSortingOrder);
            _tags = NewRect("NameTags", _tagCanvas.transform);
            _tags.anchorMin = Vector2.zero;
            _tags.anchorMax = Vector2.one;
            _tags.offsetMin = _tags.offsetMax = Vector2.zero;

            _canvas = NewOverlayCanvas("TeamHUD", ListSortingOrder);
            // Touch: Liste im sicheren Bereich (Notch/Dynamic Island links) – wie der HUD-Canvas (SafeAreaFitter)
            if (MobilePlatform.TouchMode) SafeAreaFitter.Attach(_canvas, true, true);
            _list = NewRect("TeamList", SafeAreaFitter.ContentOf(_canvas.transform));
            _list.anchorMin = _list.anchorMax = new Vector2(0f, 1f);
            _list.pivot = new Vector2(0f, 1f);
            _list.anchoredPosition = ListOrigin;
            _list.sizeDelta = new Vector2(RowWidth, RowHeight * 3f);
            _canvas.gameObject.SetActive(false);
            _tagCanvas.gameObject.SetActive(false);
        }

        // Overlay-Canvas mit derselben Skalierung wie der HUD-Canvas (Liste rechnet mit MinimapUI.BottomOffset)
        private static Canvas NewOverlayCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2400f, 1350f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        void LateUpdate()
        {
            if (_canvas == null) return;
            bool show = Net.IsMultiplayer;
            if (_canvas.gameObject.activeSelf != show) _canvas.gameObject.SetActive(show);
            if (_tagCanvas != null && _tagCanvas.gameObject.activeSelf != show) _tagCanvas.gameObject.SetActive(show);
            if (!show) return;

            // Liste unter die Minimap schieben (gleiche Canvas-Skalierung → gleiche Einheiten)
            var mm = MinimapUI.Instance;
            float listY = mm != null && mm.Occupies ? Mathf.Min(ListOrigin.y, -(mm.BottomOffset + MinimapGap)) : ListOrigin.y;
            var origin = new Vector2(ListOrigin.x, listY);
            if (_list.anchoredPosition != origin) _list.anchoredPosition = origin;

            // Mitspieler (alle außer der eigenen Figur), Reihenfolge nach Beitritt
            _others.Clear();
            foreach (var a in PlayerAvatar.All)
                if (a != null && !a.IsLocal) _others.Add(a);
            _others.Sort((x, y) => x.OwnerClientId.CompareTo(y.OwnerClientId));

            while (_rows.Count < _others.Count) _rows.Add(BuildRow(_rows.Count));
            for (int i = 0; i < _rows.Count; i++)
            {
                bool used = i < _others.Count;
                if (_rows[i].Root.gameObject.activeSelf != used) _rows[i].Root.gameObject.SetActive(used);
                if (used) FillRow(_rows[i], _others[i]);
            }

            UpdateNameTags();
        }

        // ---------------- Liste ----------------

        private Row BuildRow(int index)
        {
            var r = new Row();
            r.Root = NewRect("Mate" + (index + 1), _list);
            r.Root.anchorMin = r.Root.anchorMax = new Vector2(0f, 1f);
            r.Root.pivot = new Vector2(0f, 1f);
            r.Root.anchoredPosition = new Vector2(0f, -index * (RowHeight + 6f));
            r.Root.sizeDelta = new Vector2(RowWidth, RowHeight);
            var bg = r.Root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.05f, 0.03f, 0.62f);
            bg.raycastTarget = false;

            var pbg = NewRect("PortraitBg", r.Root);
            Fixed(pbg, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(54f, 54f));
            r.PortraitBg = pbg.gameObject.AddComponent<Image>();
            r.PortraitBg.raycastTarget = false;
            var por = NewRect("Portrait", pbg);
            Fill(por, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            r.Portrait = por.gameObject.AddComponent<Image>();
            r.Portrait.preserveAspect = true;
            r.Portrait.raycastTarget = false;

            r.Name = NewText("Name", r.Root, 24f, TextAlignmentOptions.BottomLeft);
            Fill(r.Name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(70f, 0f), new Vector2(-8f, -4f));

            var barBg = NewRect("HpBar", r.Root);
            Fill(barBg, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(70f, 8f), new Vector2(-12f, -4f));
            var barImg = barBg.gameObject.AddComponent<Image>();
            barImg.color = new Color(0f, 0f, 0f, 0.6f);
            barImg.raycastTarget = false;
            var fill = NewRect("Fill", barBg);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = new Vector2(2f, 2f);
            fill.offsetMax = new Vector2(-2f, -2f);
            r.HpFill = fill.gameObject.AddComponent<Image>();
            r.HpFill.color = new Color(0.35f, 0.8f, 0.3f);
            r.HpFill.raycastTarget = false;

            r.Status = NewText("Status", r.Root, 19f, TextAlignmentOptions.MidlineLeft);
            Fill(r.Status.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(70f, 0f), new Vector2(-8f, 0f));
            r.Status.color = new Color(1f, 0.55f, 0.45f);
            return r;
        }

        private void FillRow(Row r, PlayerAvatar a)
        {
            var np = a.Owner;
            int slot = np != null ? Mathf.Clamp(np.Slot, 0, SlotColors.Length - 1) : 0;
            string name = np != null && !string.IsNullOrEmpty(np.DisplayName) ? np.DisplayName : "Mitspieler";
            r.Name.text = name;
            r.Name.color = SlotColors[slot];

            // Jeden Frame neu (kein Cache): ein spät ankommender NetPlayer.Champion wird sofort übernommen. Solange der
            // NetPlayer noch fehlt, würde PlayerAvatar.Champion auf die EIGENE Menü-Wahl zurückfallen → Porträt aus.
            Sprite portrait = np != null ? PortraitFor(np.ChampionClass) : null;
            r.Portrait.enabled = portrait != null;
            r.Portrait.sprite = portrait;
            r.PortraitBg.color = SlotColors[slot] * 0.8f;

            float frac = a.Health01;
            bool alive = a.IsAlive;
            var frt = r.HpFill.rectTransform;
            frt.anchorMax = new Vector2(frac, 1f);
            r.HpFill.color = Color.Lerp(new Color(0.85f, 0.25f, 0.2f), new Color(0.35f, 0.8f, 0.3f), frac);
            r.HpFill.transform.parent.gameObject.SetActive(alive);

            r.Status.gameObject.SetActive(!alive);
            if (!alive) r.Status.text = DownedText(a);
        }

        // Porträt zu einer Champion-Klasse. HUDManager.ChampionPortraits ist nach ChampionClass sortiert; ein falsch
        // belegter Eintrag (Spieltest 2026-10-17 #4: Berserker zeigte das Ritter-Bild) wird über den Spritenamen
        // erkannt und übersprungen – dann wird per Name ("portrait_champion_<klasse>") im Array gesucht.
        private Sprite PortraitFor(ChampionClass c)
        {
            int i = (int)c;
            Sprite s = i >= 0 && i < _portraits.Length ? _portraits[i] : null;
            if (s != null && MatchesClass(s.name, c)) return s;
            foreach (var p in _portraits)
                if (p != null && MatchesClass(p.name, c)) return p;
            return null; // lieber kein Porträt als das eines anderen Champions
        }

        public static string PortraitName(ChampionClass c) => "portrait_champion_" + c.ToString().ToLowerInvariant();

        private static bool MatchesClass(string spriteName, ChampionClass c) =>
            spriteName.StartsWith("portrait_champion_") ? spriteName == PortraitName(c) : true;

        // "Gefallen – Wiederbelebung in 7 s" (Restzeit vom Server über PlayerAvatar.RespawnRemaining)
        private static string DownedText(PlayerAvatar a)
        {
            float left = a.RespawnRemaining;
            return left > 0.05f ? $"Gefallen – Wiederbelebung in {Mathf.CeilToInt(left)} s" : "Gefallen – Wiederbelebung …";
        }

        // Zustand der Gruppenliste für Log-Tests (NetAutoTest): "Name:Porträt" je sichtbarer Zeile
        public string DebugRows()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _rows.Count && i < _others.Count; i++)
            {
                var r = _rows[i];
                if (sb.Length > 0) sb.Append(',');
                sb.Append(r.Name.text).Append(':').Append(r.Portrait.enabled && r.Portrait.sprite != null ? r.Portrait.sprite.name : "-");
            }
            return sb.ToString();
        }

        // ---------------- Namensschilder ----------------

        private void UpdateNameTags()
        {
            var cam = Camera.main;
            foreach (var a in _others)
            {
                if (!_tagMap.TryGetValue(a, out var tag))
                {
                    tag = BuildTag();
                    _tagMap[a] = tag;
                }
                bool visible = cam != null;
                Vector3 sp = Vector3.zero;
                if (visible)
                {
                    sp = cam.WorldToScreenPoint(a.transform.position + Vector3.up * NameTagHeight);
                    visible = sp.z > 0f;
                }
                if (tag.Root.gameObject.activeSelf != visible) tag.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                tag.Root.position = new Vector3(sp.x, sp.y, 0f);
                var np = a.Owner;
                int slot = np != null ? Mathf.Clamp(np.Slot, 0, SlotColors.Length - 1) : 0;
                string name = np != null && !string.IsNullOrEmpty(np.DisplayName) ? np.DisplayName : "Mitspieler";
                tag.Text.text = a.IsAlive ? name : name + " <size=80%>(gefallen)</size>";
                tag.Text.color = a.IsAlive ? SlotColors[slot] : new Color(0.75f, 0.7f, 0.7f);
            }

            // Schilder verschwundener Figuren entfernen
            _remove.Clear();
            foreach (var kv in _tagMap)
                if (kv.Key == null || !_others.Contains(kv.Key)) _remove.Add(kv.Key);
            foreach (var k in _remove)
            {
                if (_tagMap.TryGetValue(k, out var tag) && tag.Root != null) Destroy(tag.Root.gameObject);
                _tagMap.Remove(k);
            }
        }

        private Tag BuildTag()
        {
            var t = new Tag();
            t.Root = NewRect("NameTag", _tags);
            t.Root.anchorMin = t.Root.anchorMax = Vector2.zero;
            t.Root.pivot = new Vector2(0.5f, 0f);
            t.Root.sizeDelta = new Vector2(300f, 40f);
            t.Text = NewText("Text", t.Root, 26f, TextAlignmentOptions.Bottom);
            Fill(t.Text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            t.Text.fontStyle = FontStyles.Bold;
            t.Text.outlineWidth = 0.2f;
            t.Text.outlineColor = new Color32(20, 12, 6, 255);
            return t;
        }

        // ---------------- Hilfen ----------------

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Fixed(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Fill(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private TextMeshProUGUI NewText(string name, Transform parent, float size, TextAlignmentOptions align)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }
    }
}
