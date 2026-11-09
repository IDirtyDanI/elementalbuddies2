using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Champion-Auswahl im Hauptmenü als einzelne Karte mit Pfeil-Knöpfen (2026-11-09), ersetzt die Liste der
    // Champion-Karten. Wird zur Laufzeit von MainMenuUI.Init angehängt und aus den Sprites, Schriften und Farben der
    // vorhandenen Karten gebaut – die Szene bleibt unverändert (die alten Karten werden nur ausgeblendet, ShopUI liest
    // weiter ihre Sprites). Karte: großes Porträt im Medaillon, Name, Untertitel bzw. Sperr-Hinweis, Positionsanzeige
    // (Punkte bis ChampionCarouselRules.MaxDots, sonst „3 / 12“). Bedienung: Pfeile ◀ ▶, Tastatur ←/→ und A/D
    // (MainMenuUI.Update), Steuerkreuz, auf Touch-Geräten Wischen über Karte oder Figur. Endloses Blättern.
    public class MenuChampionCarousel : MonoBehaviour
    {
        // Maße in Einheiten der Menü-Referenz (2400 × 1350, Match an der Höhe)
        private const float CardHeight = 320f;
        private const float CardTopGap = 8f;       // Abstand zur Oberkante der bisherigen ersten Karte
        private const float MedallionSize = 160f;
        private const float MedallionTop = 16f;
        private const float ArrowWidth = 104f;     // Tablet 1920 × 1200: ≈ 92 px
        private const float ArrowHeight = 124f;
        private const float ArrowInset = 18f;
        private const float DotSize = 18f, DotSelectedSize = 26f, DotGap = 12f;
        private const float SwipeMinDp = 36f;

        private static readonly Color DotColor = new Color(0.239f, 0.149f, 0.078f, 0.45f);
        private static readonly Color DotSelectedColor = new Color(0.86f, 0.64f, 0.18f, 1f);

        private MainMenuUI _menu;
        private RectTransform _card;
        private Image _frame, _disc, _portrait, _ring, _lock;
        private TextMeshProUGUI _initial, _name, _tagline, _position;
        private Button _prev, _next;
        private RectTransform _dots;
        private readonly List<Image> _dotImages = new List<Image>();
        private Color _nameColor, _taglineColor;

        // Wischen
        private bool _tracking;
        private Vector2 _pressPos;
        private float _pressTime;

        public RectTransform Card => _card;

        public static MenuChampionCarousel Attach(MainMenuUI menu)
        {
            if (menu == null || !Application.isPlaying) return null; // Edit-Modus (Builder-Renders): alte Liste bleibt
            var c = menu.GetComponent<MenuChampionCarousel>();
            if (c != null) return c;
            if (menu.Cards.Length == 0 || menu.Cards[0] == null || menu.Cards[0].Button == null) return null;
            c = menu.gameObject.AddComponent<MenuChampionCarousel>();
            c._menu = menu;
            c.Build();
            return c;
        }

        // ---------------- Aufbau ----------------

        private void Build()
        {
            var t = _menu.Cards[0];
            var tRt = (RectTransform)t.Button.transform;
            _nameColor = _menu.CardNameColor;
            _taglineColor = _menu.CardTaglineColor;

            _card = NewRect("ChampionCarouselCard", tRt.parent);
            _card.SetSiblingIndex(tRt.GetSiblingIndex());
            _card.anchorMin = _card.anchorMax = new Vector2(0f, 1f);
            _card.pivot = new Vector2(0f, 1f);
            _card.anchoredPosition = new Vector2(tRt.anchoredPosition.x, tRt.anchoredPosition.y - CardTopGap);
            _card.sizeDelta = new Vector2(tRt.sizeDelta.x, CardHeight);

            // Rahmen: Goldrahmen der gewählten Karte (9-Slice), Fläche fängt Raycasts (Wischen startet auf der Karte)
            _frame = _card.gameObject.AddComponent<Image>();
            _frame.sprite = _menu.CardSelectedSprite != null ? _menu.CardSelectedSprite : (t.Frame != null ? t.Frame.sprite : null);
            _frame.type = Image.Type.Sliced;
            _frame.pixelsPerUnitMultiplier = 1.25f;
            _frame.raycastTarget = true;

            // Medaillon mit Porträt (wie auf den alten Karten: Scheibe in Akzentfarbe, Porträt, Goldring, Schloss)
            var med = NewRect("Medallion", _card);
            Place(med, new Vector2(0.5f, 1f), new Vector2(0f, -MedallionTop - MedallionSize * 0.5f), Vector2.one * MedallionSize, new Vector2(0.5f, 0.5f));
            _disc = med.gameObject.AddComponent<Image>();
            _disc.sprite = t.PortraitBg != null ? t.PortraitBg.sprite : null;
            _disc.raycastTarget = false;
            _portrait = AddChildImage(med, "Portrait", t.Portrait != null ? t.Portrait.sprite : null, -18f);
            _portrait.preserveAspect = true;
            var ringSrc = t.PortraitBg != null ? t.PortraitBg.transform.Find("Ring") : null;
            var ringImg = ringSrc != null ? ringSrc.GetComponent<Image>() : null;
            _ring = AddChildImage(med, "Ring", ringImg != null ? ringImg.sprite : null, 10f);
            _ring.enabled = _ring.sprite != null;
            _initial = NewText("Initial", med, t.Initial != null ? t.Initial : t.Name, 84f);
            Stretch(_initial.rectTransform);
            _lock = AddChildImage(med, "Lock", t.Lock != null ? t.Lock.sprite : null, 0f);
            var lr = _lock.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
            lr.sizeDelta = Vector2.one * (MedallionSize * 0.55f);
            lr.anchoredPosition = new Vector2(0f, -4f);
            _lock.preserveAspect = true;

            // Name und Untertitel
            float y = -MedallionTop - MedallionSize - 4f;
            _name = NewText("Name", _card, t.Name, 44f);
            Place(_name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(_card.sizeDelta.x - 60f, 48f), new Vector2(0.5f, 1f));
            _name.enableAutoSizing = true;
            _name.fontSizeMin = 30f;
            _name.fontSizeMax = 44f;
            y -= 48f;
            _tagline = NewText("Tagline", _card, t.Tagline, 25f);
            Place(_tagline.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(_card.sizeDelta.x - 50f, 32f), new Vector2(0.5f, 1f));
            _tagline.enableAutoSizing = true;
            _tagline.fontSizeMin = 18f;
            _tagline.fontSizeMax = 25f;
            y -= 32f + 10f;

            // Positionsanzeige: Punkte oder „3 / 5“
            _dots = NewRect("Dots", _card);
            Place(_dots, new Vector2(0.5f, 1f), new Vector2(0f, y - DotSelectedSize * 0.5f), new Vector2(_card.sizeDelta.x - 2f * (ArrowInset + ArrowWidth), DotSelectedSize), new Vector2(0.5f, 0.5f));
            var hl = _dots.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = DotGap;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = hl.childControlHeight = false;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            _position = NewText("Position", _card, t.Tagline, 24f);
            Place(_position.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y - DotSelectedSize * 0.5f), new Vector2(200f, 30f), new Vector2(0.5f, 0.5f));

            // Pfeil-Knöpfe links und rechts vom Porträt (Holzknopf wie die gewählte Schwierigkeit)
            float arrowY = -MedallionTop - MedallionSize * 0.5f;
            _prev = NewArrow("PrevChampion", -1, new Vector2(ArrowInset + ArrowWidth * 0.5f, arrowY), new Vector2(0f, 1f));
            _next = NewArrow("NextChampion", 1, new Vector2(-(ArrowInset + ArrowWidth * 0.5f), arrowY), new Vector2(1f, 1f));

            // Alte Liste ausblenden (Objekte bleiben für ShopUI/RefreshLocks erhalten)
            foreach (var card in _menu.Cards)
                if (card != null && card.Button != null) card.Button.gameObject.SetActive(false);

            // Tasten-Hinweis (PC): Zifferntasten gibt es nicht mehr
            var hint = MobileUi.FindDeep(_menu.transform, "KeyHint");
            var ht = hint != null ? hint.GetComponent<TextMeshProUGUI>() : null;
            if (ht != null) ht.text = Regex.Replace(ht.text, "oder 1–\\d+", "oder A/D");
        }

        private Button NewArrow(string name, int delta, Vector2 pos, Vector2 anchor)
        {
            var rt = NewRect(name, _card);
            Place(rt, anchor, pos, new Vector2(ArrowWidth, ArrowHeight), new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _menu.DifficultySelectedSprite;
            img.type = Image.Type.Sliced;
            img.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            if (_menu.DifficultySelectedSprite != null && _menu.DifficultySelectedHoverSprite != null)
            {
                b.transition = Selectable.Transition.SpriteSwap;
                b.spriteState = new SpriteState
                {
                    highlightedSprite = _menu.DifficultySelectedHoverSprite,
                    pressedSprite = _menu.DifficultySelectedHoverSprite,
                    selectedSprite = _menu.DifficultySelectedSprite,
                };
            }
            b.navigation = new Navigation { mode = Navigation.Mode.None }; // ←/→ wechseln den Champion, nicht den Fokus
            b.onClick.AddListener(() => _menu.Step(delta));

            // Beschriftung ◀ ▶ (AlegreyaSans-Bold, Schrift der Schwierigkeits-Knöpfe), sonst ‹ › bzw. < >
            TextMeshProUGUI src = null;
            foreach (var db in _menu.DifficultyButtons)
                if (db != null) { src = db.GetComponentInChildren<TextMeshProUGUI>(true); if (src != null) break; }
            var label = NewText("Label", rt, src != null ? src : _menu.Cards[0].Name, 72f);
            Stretch(label.rectTransform);
            label.color = _menu.DifficultySelectedTextColor;
            label.text = ArrowGlyph(label.font, delta);
            label.rectTransform.anchoredPosition = new Vector2(delta < 0 ? -2f : 2f, 2f); // optisch mittig
            return b;
        }

        private static string ArrowGlyph(TMP_FontAsset font, int delta)
        {
            char a = delta < 0 ? '◀' : '▶';
            char b = delta < 0 ? '‹' : '›';
            if (font != null && font.HasCharacter(a, true, true)) return a.ToString();
            if (font != null && font.HasCharacter(b, true, true)) return b.ToString();
            return delta < 0 ? "<" : ">";
        }

        // ---------------- Anzeige ----------------

        // Gewählten Champion anzeigen (von MainMenuUI.RefreshLocks, also nach jeder Wahl, Stufe oder Freischaltung)
        public void Refresh()
        {
            if (_card == null || _menu == null) return;
            var defs = _menu.Champions;
            var sel = _menu.Selected;
            int count = 0, index = 0;
            ChampionDefinitionSO def = null;
            foreach (var d in defs)
            {
                if (d == null) continue;
                if (d.Class == sel) { index = count; def = d; }
                count++;
            }
            bool locked = MainMenuUI.IsLocked(sel);

            Color accent = def != null ? def.AccentColor : Color.gray;
            Color bg = Color.Lerp(accent, Color.white, 0.15f);
            if (locked)
            {
                float g = bg.grayscale;
                bg = Color.Lerp(bg, new Color(g, g, g), 0.75f) * 0.55f;
                bg.a = 1f;
            }
            _disc.color = bg;
            _frame.color = locked ? _menu.LockedCardTint : Color.white;
            bool hasPortrait = def != null && def.Portrait != null;
            _portrait.gameObject.SetActive(hasPortrait);
            if (hasPortrait) _portrait.sprite = def.Portrait;
            _portrait.color = locked ? _menu.LockedPortraitTint : Color.white;
            _initial.gameObject.SetActive(!hasPortrait);
            _initial.text = def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName.Substring(0, 1) : "?";
            _initial.color = locked ? _menu.LockedNameColor : _nameColor;
            _lock.gameObject.SetActive(locked);

            _name.text = def != null ? def.DisplayName : sel.ToString();
            _name.color = locked ? _menu.LockedNameColor : _nameColor;
            _tagline.text = locked ? "Gesperrt: " + MainMenuUI.LockGoalShort(sel) : def != null ? def.Tagline : "";
            _tagline.color = locked ? _menu.LockedTextColor : _taglineColor;

            bool dots = ChampionCarouselRules.UseDots(count);
            _dots.gameObject.SetActive(dots);
            _position.gameObject.SetActive(!dots && count > 1);
            _position.text = ChampionCarouselRules.PositionText(index, count);
            if (dots) FillDots(count, index);

            bool browse = count > 1;
            _prev.gameObject.SetActive(browse);
            _next.gameObject.SetActive(browse);
        }

        private void FillDots(int count, int index)
        {
            while (_dotImages.Count < count)
            {
                var img = AddChildImage(_dots, "Dot" + _dotImages.Count, _disc.sprite, 0f);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _dotImages.Add(img);
            }
            for (int i = 0; i < _dotImages.Count; i++)
            {
                var img = _dotImages[i];
                bool on = i < count;
                if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
                if (!on) continue;
                bool cur = i == index;
                img.rectTransform.sizeDelta = Vector2.one * (cur ? DotSelectedSize : DotSize);
                img.color = cur ? DotSelectedColor : DotColor;
            }
        }

        // ---------------- Wischen (Touch) ----------------

        void Update()
        {
            if (_menu == null || _card == null) return;
            if (!_menu.CanBrowseChampions || !_card.gameObject.activeInHierarchy) { _tracking = false; return; }

            // Touch-Geräte; die Maus nur im (erzwungenen) Touch-Modus, damit sich die PC-Bedienung nicht ändert
            bool pressed = false, released = false;
            Vector2 pos = Vector2.zero;
            var ts = Touchscreen.current;
            if (ts != null && (ts.primaryTouch.press.wasPressedThisFrame || ts.primaryTouch.press.wasReleasedThisFrame || _tracking))
            {
                var touch = ts.primaryTouch;
                pos = touch.position.ReadValue();
                pressed = touch.press.wasPressedThisFrame;
                released = touch.press.wasReleasedThisFrame;
            }
            else if (MobilePlatform.TouchMode && Mouse.current != null)
            {
                var m = Mouse.current;
                pos = m.position.ReadValue();
                pressed = m.leftButton.wasPressedThisFrame;
                released = m.leftButton.wasReleasedThisFrame;
            }

            if (pressed)
            {
                _tracking = InSwipeZone(pos);
                _pressPos = pos;
                _pressTime = Time.unscaledTime;
            }
            else if (released && _tracking)
            {
                _tracking = false;
                Vector2 d = pos - _pressPos;
                float min = Mathf.Max(MobilePlatform.DpToPixels(SwipeMinDp), Screen.height * 0.04f);
                int step = ChampionCarouselRules.SwipeStep(d.x, d.y, Time.unscaledTime - _pressTime, min);
                if (step != 0) _menu.Step(step);
            }
        }

        // Karte (außer den Pfeil-Knöpfen – dort zählt der Klick) oder die freie Bühne zwischen Karte und rechtem Panel
        private bool InSwipeZone(Vector2 screen)
        {
            if (Over(_prev, screen) || Over(_next, screen)) return false;
            if (RectTransformUtility.RectangleContainsScreenPoint(_card, screen, null)) return true;

            var corners = new Vector3[4];
            _card.GetWorldCorners(corners); // Overlay-Canvas: Welt = Bildschirmpixel
            float left = corners[2].x;
            float right = Screen.width;
            var panel = _menu.LobbyPanel != null && _menu.LobbyPanel.activeInHierarchy ? _menu.LobbyPanel
                : _menu.DetailPanel != null && _menu.DetailPanel.activeInHierarchy ? _menu.DetailPanel : null;
            if (panel != null)
            {
                ((RectTransform)panel.transform).GetWorldCorners(corners);
                right = corners[0].x;
            }
            float bottom = 0f;
            var strip = MobileUi.FindDeep(_menu.transform, "MetaStrip") as RectTransform;
            if (strip != null && strip.gameObject.activeInHierarchy)
            {
                strip.GetWorldCorners(corners);
                bottom = corners[1].y;
            }
            return screen.x > left && screen.x < right && screen.y > bottom && screen.y < Screen.height;
        }

        private static bool Over(Button b, Vector2 screen) =>
            b != null && b.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, screen, null);

        // ---------------- Helfer ----------------

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Kind-Bild, das das Elternteil füllt (grow > 0 = größer, < 0 = eingerückt)
        private static Image AddChildImage(RectTransform parent, string name, Sprite sprite, float grow)
        {
            var rt = NewRect(name, parent);
            Stretch(rt);
            rt.offsetMin = new Vector2(-grow, -grow);
            rt.offsetMax = new Vector2(grow, grow);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        // Text mit Schrift, Material und Stil einer Vorlage (Schriften des Menüs bleiben unverändert)
        private static TextMeshProUGUI NewText(string name, RectTransform parent, TextMeshProUGUI style, float size)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (style != null)
            {
                t.font = style.font;
                t.fontSharedMaterial = style.fontSharedMaterial;
                t.fontStyle = style.fontStyle;
                t.color = style.color;
                t.characterSpacing = style.characterSpacing;
            }
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }
    }
}
