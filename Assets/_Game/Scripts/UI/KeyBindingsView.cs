using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Seite „Tastaturbelegung“ (Hauptmenü und Pause-Menü), komplett zur Laufzeit gebaut – ein Neuaufbau der Szenen
    // (MainMenuBuilder) verliert nichts. Liste gruppiert (Bewegung, Kamera, Kampf, Zauber, Buddies, Sonstiges, Fest belegt);
    // je Zeile: Name, Tasten-Knopf (klicken → „Taste drücken …“, Esc bricht ab) und „Standard“. Unten „Alle
    // zurücksetzen“ und eine Meldungszeile (Tausch bei Konflikt usw.). Logik und Speicherung: KeyBindings.
    public class KeyBindingsView : MonoBehaviour
    {
        public const float RebindTimeout = 8f;

        private static readonly Color Ink = new Color(0.24f, 0.15f, 0.08f);
        private static readonly Color Soft = new Color(0.42f, 0.3f, 0.18f);
        private static readonly Color Muted = new Color(0.45f, 0.4f, 0.35f, 0.85f);
        private static readonly Color Listening = new Color(1f, 0.85f, 0.45f);
        private static readonly Color Changed = new Color(0.55f, 0.18f, 0.08f);
        private static readonly Color RowTint = new Color(0.45f, 0.33f, 0.2f, 0.10f);

        private class Row
        {
            public BindId Id;
            public Button KeyButton, ResetButton;
            public TextMeshProUGUI KeyText, Detail;
            public Image KeyImage;
        }

        private RectTransform _content;
        private TMP_FontAsset _titleFont, _bodyFont;
        private Button _template;
        private TextMeshProUGUI _status;
        private readonly List<Row> _rows = new List<Row>();
        private float _statusUntil;
        private const string DefaultStatus = "Klick auf eine Taste und drück die neue. Esc bricht ab. Änderungen gelten sofort.";

        // Eine Taste wird gerade neu belegt (Menüs ignorieren Esc/Enter)
        public static bool Busy => KeyBindings.InputConsumed;

        // content: Inhalt einer ScrollRect (wird mit VerticalLayoutGroup + ContentSizeFitter versehen);
        // template: Pergament-Knopf als Stilvorlage; status: Meldungszeile (optional)
        public static KeyBindingsView Create(GameObject host, RectTransform content, Button template, TMP_FontAsset titleFont, TMP_FontAsset bodyFont, TextMeshProUGUI status)
        {
            var v = host.GetComponent<KeyBindingsView>();
            if (v == null) v = host.AddComponent<KeyBindingsView>();
            v._content = content;
            v._template = template;
            v._titleFont = titleFont;
            v._bodyFont = bodyFont != null ? bodyFont : titleFont;
            v._status = status;
            v.Build();
            v.SetStatus(null);
            return v;
        }

        void OnEnable()
        {
            KeyBindings.OnBindingsChanged += RefreshAll;
            RefreshAll();
            SetStatus(null);
        }

        void OnDisable()
        {
            KeyBindings.OnBindingsChanged -= RefreshAll;
            if (KeyBindings.IsRebinding) KeyBindings.CancelRebind();
        }

        // Wurde die linke Maustaste als neue Taste gedrückt, löst ihr Loslassen im UI noch einen Klick aus (z. B. auf
        // denselben Knopf → sofort neues Neubelegen). Diesen einen Klick schlucken; ohne Klick nach dem Loslassen verwerfen.
        private bool _swallowClick;

        private bool SwallowClick()
        {
            if (!_swallowClick) return false;
            _swallowClick = false;
            return true;
        }

        void Update()
        {
            // Altes Input-Modul (activeInputHandler = Both): sieht auch unterdrückte Ereignisse des Neubelegens
            if (_swallowClick && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) _swallowClick = false;

            if (_status != null && _statusUntil > 0f && Time.unscaledTime > _statusUntil && !KeyBindings.IsRebinding)
            {
                _statusUntil = 0f;
                SetStatus(null);
            }
        }

        // ---------------- Aufbau ----------------

        private void Build()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _rows.Clear();

            var layout = _content.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(12, 12, 8, 16);
            var fitter = _content.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            KeyBindings.Group? group = null;
            foreach (var e in KeyBindings.Entries)
            {
                if (group != e.Group)
                {
                    group = e.Group;
                    Header(GroupName(e.Group));
                }
                AddRow(e);
            }
            Header("Fest belegt");
            for (int i = 0; i < KeyBindings.Fixed.GetLength(0); i++)
                FixedRow(KeyBindings.Fixed[i, 0], KeyBindings.Fixed[i, 1]);
            RefreshAll();
        }

        private static string GroupName(KeyBindings.Group g)
        {
            switch (g)
            {
                case KeyBindings.Group.Bewegung: return "Bewegung";
                case KeyBindings.Group.Kamera: return "Kamera";
                case KeyBindings.Group.Kampf: return "Kampf";
                case KeyBindings.Group.Zauber: return "Element-Fähigkeiten";
                case KeyBindings.Group.Buddies: return "Buddies bauen";
                default: return "Sonstiges";
            }
        }

        private void Header(string title)
        {
            var go = new GameObject("Header_" + title, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(_content, false);
            go.GetComponent<LayoutElement>().preferredHeight = 50f;
            var t = Text((RectTransform)go.transform, "Text", title, 30f, Soft, TextAlignmentOptions.BottomLeft, _titleFont);
            t.rectTransform.offsetMin = new Vector2(4f, 6f);
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            var lrt = (RectTransform)line.transform;
            lrt.SetParent(go.transform, false);
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.sizeDelta = new Vector2(0f, 2f);
            var img = line.GetComponent<Image>();
            img.color = new Color(0.45f, 0.33f, 0.2f, 0.45f);
            img.raycastTarget = false;
        }

        private RectTransform RowBase(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(_content, false);
            go.GetComponent<LayoutElement>().preferredHeight = 58f;
            var bg = go.GetComponent<Image>();
            bg.color = RowTint;
            bg.raycastTarget = true; // Mausrad scrollt auch zwischen den Knöpfen
            return (RectTransform)go.transform;
        }

        private void AddRow(KeyBindings.Entry e)
        {
            var rt = RowBase("Bind_" + e.Id);
            var label = Text(rt, "Label", e.Label, 26f, Ink, TextAlignmentOptions.Left, _bodyFont);
            Anchor(label.rectTransform, 0f, 0.5f, new Vector2(20f, 0f), new Vector2(300f, 50f));
            // Zusatz (Fähigkeitsname, Standard-Taste) füllt den Platz zwischen Name und Tasten-Knopf
            var detail = Text(rt, "Detail", "", 20f, Muted, TextAlignmentOptions.Left, _bodyFont);
            var drt = detail.rectTransform;
            drt.anchorMin = new Vector2(0f, 0f);
            drt.anchorMax = new Vector2(1f, 1f);
            drt.offsetMin = new Vector2(330f, 0f);
            drt.offsetMax = new Vector2(-470f, 0f);
            detail.enableAutoSizing = true;
            detail.fontSizeMax = 20f;
            detail.fontSizeMin = 14f;

            var row = new Row { Id = e.Id, Detail = detail };
            row.KeyButton = MakeButton(rt, "KeyButton", "", 26f);
            var krt = (RectTransform)row.KeyButton.transform;
            krt.anchorMin = krt.anchorMax = new Vector2(1f, 0.5f);
            krt.pivot = new Vector2(1f, 0.5f);
            krt.anchoredPosition = new Vector2(-190f, 0f);
            krt.sizeDelta = new Vector2(260f, 50f);
            row.KeyText = row.KeyButton.GetComponentInChildren<TextMeshProUGUI>(true);
            row.KeyImage = row.KeyButton.GetComponent<Image>();
            BindId id = e.Id;
            row.KeyButton.onClick.AddListener(() => BeginRebind(id));

            row.ResetButton = MakeButton(rt, "ResetButton", "Standard", 20f);
            var rrt = (RectTransform)row.ResetButton.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f);
            rrt.pivot = new Vector2(1f, 0.5f);
            rrt.anchoredPosition = new Vector2(-14f, 0f);
            rrt.sizeDelta = new Vector2(160f, 44f);
            row.ResetButton.onClick.AddListener(() => ResetOne(id));
            _rows.Add(row);
        }

        private void FixedRow(string label, string key)
        {
            var rt = RowBase("Fixed_" + label);
            rt.GetComponent<Image>().color = new Color(RowTint.r, RowTint.g, RowTint.b, 0.05f);
            var l = Text(rt, "Label", label, 24f, Muted, TextAlignmentOptions.Left, _bodyFont);
            Anchor(l.rectTransform, 0f, 0.5f, new Vector2(20f, 0f), new Vector2(420f, 50f));
            var k = Text(rt, "Key", key, 24f, Muted, TextAlignmentOptions.Center, _bodyFont);
            k.rectTransform.anchorMin = k.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            k.rectTransform.pivot = new Vector2(1f, 0.5f);
            k.rectTransform.anchoredPosition = new Vector2(-190f, 0f);
            k.rectTransform.sizeDelta = new Vector2(260f, 50f);
        }

        private Button MakeButton(RectTransform parent, string name, string text, float size)
        {
            GameObject go;
            if (_template != null)
            {
                go = Instantiate(_template.gameObject, parent, false);
                go.name = name;
                go.SetActive(true);
                foreach (var img in go.GetComponentsInChildren<Image>(true))
                    if (img.gameObject != go) img.gameObject.SetActive(false); // Symbole der Vorlage
                var le = go.GetComponent<LayoutElement>();
                if (le != null) le.ignoreLayout = true;
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                go.GetComponent<Image>().color = new Color(0.93f, 0.86f, 0.7f);
                Text((RectTransform)go.transform, "Text", text, size, Ink, TextAlignmentOptions.Center, _bodyFont);
            }
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.navigation = new Navigation { mode = Navigation.Mode.None }; // Leertaste/Enter sollen keinen Knopf auslösen
            var t = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (t != null)
            {
                t.text = text;
                t.font = _bodyFont != null ? _bodyFont : t.font;
                t.enableAutoSizing = true;
                t.fontSizeMax = size;
                t.fontSizeMin = 14f;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.richText = true;
            }
            return b;
        }

        private static void Anchor(RectTransform rt, float ax, float ay, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private TextMeshProUGUI Text(RectTransform parent, string name, string text, float size, Color color, TextAlignmentOptions align, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }

        // ---------------- Anzeige ----------------

        public void RefreshAll()
        {
            bool busy = KeyBindings.IsRebinding;
            foreach (var r in _rows)
            {
                bool listening = busy && KeyBindings.RebindingId == r.Id;
                bool isDefault = KeyBindings.IsDefault(r.Id);
                if (r.KeyText != null)
                {
                    r.KeyText.text = listening ? "<i>Taste drücken …</i>" : KeyBindings.Name(r.Id);
                    r.KeyText.color = !listening && !isDefault ? Changed : Ink;
                }
                if (r.KeyImage != null) r.KeyImage.color = listening ? Listening : Color.white;
                // „Standard“ nur zeigen, wenn die Zeile von der Standard-Taste abweicht
                if (r.ResetButton != null)
                {
                    r.ResetButton.gameObject.SetActive(!isDefault);
                    r.ResetButton.interactable = !busy;
                }
                if (r.KeyButton != null) r.KeyButton.interactable = !busy || listening;
                if (r.Detail != null) r.Detail.text = DetailOf(r.Id);
            }
        }

        // Kleiner Zusatz: im Spiel der Fähigkeitsname des aktiven Champions, Standardtaste bei geänderter Belegung
        private static string DetailOf(BindId id)
        {
            string extra = "";
            if (id >= BindId.Primary && id <= BindId.SpellLight)
            {
                var av = PlayerAvatar.Local;
                var a = av != null && av.Abilities != null ? av.Abilities : PlayerAbilities.Instance;
                if (a != null && a.ActiveKit != null)
                {
                    var slot = id == BindId.Primary ? AbilitySlot.Primary : id == BindId.Secondary ? AbilitySlot.Secondary
                             : AbilitySlots.OfElement(id - BindId.SpellFire);
                    extra = a.ActiveKit.GetName(a.GetAbility(slot));
                }
            }
            if (!KeyBindings.IsDefault(id))
            {
                string def = "Standard: " + KeyBindings.Name(KeyBindings.DefaultPath(id));
                extra = string.IsNullOrEmpty(extra) ? def : extra + " · " + def;
            }
            return extra;
        }

        private void SetStatus(string text, float seconds = 6f)
        {
            if (_status == null) return;
            _status.text = string.IsNullOrEmpty(text) ? DefaultStatus : text;
            _statusUntil = string.IsNullOrEmpty(text) ? 0f : Time.unscaledTime + seconds;
        }

        // ---------------- Bedienung ----------------

        private void BeginRebind(BindId id)
        {
            if (SwallowClick()) return;
            if (KeyBindings.IsRebinding)
            {
                // zweiter Klick auf denselben Knopf: abbrechen
                if (KeyBindings.RebindingId == id) KeyBindings.CancelRebind();
                return;
            }
            // Fokus lösen: sonst löst die Leertaste/Enter (Submit des UI-Moduls) den Knopf erneut aus
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            GameAudio.Play(SfxId.UiClick);
            var e = KeyBindings.Get(id);
            KeyBindings.StartRebind(id, RebindTimeout, (msg, changed) =>
            {
                if (changed && KeyBindings.SamePath(KeyBindings.EffectivePath(id), "<Mouse>/leftButton")) _swallowClick = true;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                RefreshAll();
                SetStatus(msg, changed ? 7f : 4f);
                if (changed) GameAudio.Play(SfxId.UiClick);
            });
            RefreshAll();
            SetStatus(e.AllowMainMouse
                ? $"Neue Taste oder Maustaste für „{e.Label}“ drücken – Esc bricht ab."
                : $"Neue Taste für „{e.Label}“ drücken (auch Mausrad-Klick, Maustaste 4/5) – Esc bricht ab.", RebindTimeout + 1f);
        }

        private void ResetOne(BindId id)
        {
            if (SwallowClick()) return;
            if (KeyBindings.IsRebinding) return;
            KeyBindings.ResetBinding(id, out string msg);
            GameAudio.Play(SfxId.UiClick);
            SetStatus(msg);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void ResetAll()
        {
            if (SwallowClick()) return;
            if (KeyBindings.IsRebinding) KeyBindings.CancelRebind();
            KeyBindings.ResetAll();
            GameAudio.Play(SfxId.UiClick);
            SetStatus("Alle Tasten auf Standard zurückgesetzt.");
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // ---------------- Bau-Hilfen für die Menüs ----------------

        // Eigene ScrollRect (Pause-Menü): Viewport mit RectMask2D, Inhalt oben verankert, schlanke Leiste rechts
        public static RectTransform CreateScroll(RectTransform parent, string name, out ScrollRect scroll)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            scroll = go.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var vrt = (RectTransform)vp.transform;
            vrt.SetParent(rt, false);
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = new Vector2(-26f, 0f);
            vp.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // fängt das Mausrad

            var content = new GameObject("Content", typeof(RectTransform));
            var crt = (RectTransform)content.transform;
            crt.SetParent(vrt, false);
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;

            var bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var brt = (RectTransform)bar.transform;
            brt.SetParent(rt, false);
            brt.anchorMin = new Vector2(1f, 0f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.sizeDelta = new Vector2(14f, 0f);
            bar.GetComponent<Image>().color = new Color(0.45f, 0.33f, 0.2f, 0.25f);
            var area = new GameObject("Sliding Area", typeof(RectTransform));
            var art = (RectTransform)area.transform;
            art.SetParent(brt, false);
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = art.offsetMax = Vector2.zero;
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var hrt = (RectTransform)handle.transform;
            hrt.SetParent(art, false);
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            var himg = handle.GetComponent<Image>();
            himg.color = new Color(0.45f, 0.3f, 0.16f, 0.85f);
            var sb = bar.GetComponent<Scrollbar>();
            sb.handleRect = hrt;
            sb.targetGraphic = himg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.navigation = new Navigation { mode = Navigation.Mode.None };

            scroll.viewport = vrt;
            scroll.content = crt;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            return crt;
        }
    }
}
