using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Anzeige der Touch-Steuerung (Mobil, Paket S): eigenes Overlay-Canvas, zur Laufzeit erzeugt (keine Prefab-/Szenen-
    // Änderung), Größen in Pixeln aus TouchControls (dp-Layout im sicheren Bereich). Stil wie das HUD („Pergament & Holz“):
    // Holzscheibe mit Goldrand, dunkler Umriss; halbtransparent. Sprites prozedural im Code (keine Fremd-Assets),
    // Fähigkeits-Icons, Schloss und Schrift kommen vom aktiven Champion bzw. aus der (im Touch-Modus ausgeblendeten)
    // AbilityBarUI. Kein GraphicRaycaster – die Eingabe liest TouchControls direkt, UI-Klicks bleiben unberührt.
    //
    // Ebene: sortingOrder −3 = über Gruppenliste (−5), unter dem HUD-Canvas (0) mit allen Fenstern (Händler, Karten,
    // Pause, Game Over) – die liegen also immer darüber; zusätzlich blendet sich alles unter modalen Menüs aus.
    public class TouchControlsView : MonoBehaviour
    {
        public const int SortingOrder = -3;

        // Farben wie AbilityBarUI (werden von dort übernommen, falls vorhanden)
        private Color _readyTint = Color.white;
        private Color _noManaTint = new Color(0.45f, 0.55f, 0.85f, 1f);
        private Color _lockedTint = new Color(0.3f, 0.3f, 0.3f, 1f);
        private Color _activeColor = new Color(1f, 0.8f, 0.25f, 1f);
        private Color _pipFull = new Color(1f, 0.85f, 0.35f, 1f);
        private Color _tooExpensive = new Color(0.9f, 0.3f, 0.3f);
        private static readonly Color CancelColor = new Color(0.95f, 0.25f, 0.2f, 1f);
        private static readonly Color AimColor = new Color(1f, 0.95f, 0.75f, 1f);
        private static readonly Color CooldownShade = new Color(0.06f, 0.04f, 0.02f, 0.62f);

        private TouchControls _tc;
        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _root;
        private TMP_FontAsset _font;
        private Material _fontMaterial;
        private Sprite _padlock;
        private int _appliedLayout = -1;
        private float _alpha;

        private class Btn
        {
            public RectTransform Root;
            public Image Face, Icon, Cooldown, Lock, Ring;
            public TextMeshProUGUI CdText, Charges;
            public AbilityId Id = (AbilityId)(-1);
            public float Flash = -1f;
        }

        private readonly Btn[] _buttons = new Btn[TouchControls.ButtonCount];
        // Knopf-Rechteck (Anker für den Fähigkeits-Tooltip bei langem Drücken)
        public RectTransform ButtonRoot(int i) => i >= 0 && i < _buttons.Length && _buttons[i] != null ? _buttons[i].Root : null;
        private Btn _jump;
        private RectTransform _moveBase, _moveKnob, _atkBase, _atkKnob;
        private Image _moveBaseImg, _moveKnobImg, _atkBaseImg, _atkKnobImg, _atkIcon;
        private AbilityId _atkIconId = (AbilityId)(-1);
        private PlayerAbilities _bound;

        void Awake()
        {
            _tc = GetComponent<TouchControls>();
            BorrowHudStyle();
            BuildCanvas();
        }

        void OnDestroy()
        {
            Unbind();
        }

        // ---------------- Aufbau ----------------

        private void BorrowHudStyle()
        {
            var bar = FindFirstObjectByType<AbilityBarUI>(FindObjectsInactive.Include);
            if (bar != null)
            {
                _readyTint = bar.ReadyTint;
                _noManaTint = bar.NotEnoughManaTint;
                _lockedTint = bar.LockedTint;
                _activeColor = bar.ActiveFrameColor;
                _pipFull = bar.PipFull;
                _tooExpensive = bar.ManaCostTooExpensive;
                foreach (var s in bar.Slots)
                {
                    if (s == null) continue;
                    if (_font == null && s.CooldownText != null)
                    {
                        _font = s.CooldownText.font;
                        _fontMaterial = s.CooldownText.fontSharedMaterial;
                    }
                    if (_padlock == null && s.LockedOverlay != null)
                    {
                        foreach (var img in s.LockedOverlay.GetComponentsInChildren<Image>(true))
                            if (img.sprite != null && img.sprite.name.Contains("padlock")) { _padlock = img.sprite; break; }
                    }
                }
            }
            if (_font == null) _font = TMP_Settings.defaultFontAsset;
        }

        private void BuildCanvas()
        {
            var go = new GameObject("TouchControlsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // 1 Canvas-Einheit = 1 Bildschirm-Pixel
            scaler.scaleFactor = 1f;
            _group = go.GetComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
            _canvas.enabled = false;
            _root = (RectTransform)go.transform;

            _moveBase = NewImage("MoveBase", _root, TouchSprites.StickBase, out _moveBaseImg);
            _moveKnob = NewImage("MoveKnob", _root, TouchSprites.ButtonFace, out _moveKnobImg);
            _atkBase = NewImage("AttackBase", _root, TouchSprites.StickBase, out _atkBaseImg);
            _atkKnob = NewImage("AttackKnob", _root, TouchSprites.ButtonFace, out _atkKnobImg);
            NewImage("Icon", _atkKnob, null, out _atkIcon);
            Stretch((RectTransform)_atkIcon.transform, 0.62f);
            _atkIcon.preserveAspect = true;

            for (int i = 0; i < TouchControls.ButtonCount; i++) _buttons[i] = NewButton("Ability_" + TouchControls.ButtonSlots[i]);
            _jump = NewButton("Jump");
            _jump.Icon.sprite = TouchSprites.JumpIcon;
            _jump.Icon.color = new Color(1f, 0.93f, 0.75f, 1f);
        }

        private Btn NewButton(string name)
        {
            var b = new Btn();
            b.Root = NewImage(name, _root, TouchSprites.ButtonFace, out b.Face);
            NewImage("Icon", b.Root, null, out b.Icon);
            Stretch((RectTransform)b.Icon.transform, 0.6f);
            b.Icon.preserveAspect = true;
            NewImage("Cooldown", b.Root, TouchSprites.Disc, out b.Cooldown);
            Stretch((RectTransform)b.Cooldown.transform, 0.8f);
            b.Cooldown.type = Image.Type.Filled;
            b.Cooldown.fillMethod = Image.FillMethod.Radial360;
            b.Cooldown.fillOrigin = (int)Image.Origin360.Top;
            b.Cooldown.fillClockwise = false;
            b.Cooldown.fillAmount = 0f;
            b.Cooldown.color = CooldownShade;
            b.CdText = NewText("CooldownText", b.Root, TextAlignmentOptions.Center);
            Stretch((RectTransform)b.CdText.transform, 1f);
            b.CdText.gameObject.SetActive(false);
            NewImage("Lock", b.Root, _padlock, out b.Lock);
            var lrt = (RectTransform)b.Lock.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.78f, 0.22f);
            b.Lock.preserveAspect = true;
            b.Lock.enabled = false;
            b.Charges = NewText("Charges", b.Root, TextAlignmentOptions.Center);
            var crt = (RectTransform)b.Charges.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.82f, 0.84f);
            b.Charges.gameObject.SetActive(false);
            NewImage("Ring", b.Root, TouchSprites.Ring, out b.Ring);
            Stretch((RectTransform)b.Ring.transform, 1.16f);
            b.Ring.color = new Color(1f, 1f, 1f, 0f);
            return b;
        }

        private static RectTransform NewImage(string name, Transform parent, Sprite sprite, out Image img)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return rt;
        }

        private TextMeshProUGUI NewText(string name, Transform parent, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            if (_fontMaterial != null) t.fontSharedMaterial = _fontMaterial;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = Color.white; // Material (Umriss) kommt von der Fähigkeitenleiste, keine eigene Instanz pro Text
            return t;
        }

        // Kind mittig mit Anteil der Elterngröße
        private static void Stretch(RectTransform rt, float fraction)
        {
            float m = (1f - fraction) * 0.5f;
            rt.anchorMin = new Vector2(m, m);
            rt.anchorMax = new Vector2(1f - m, 1f - m);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private void ApplyLayout()
        {
            _appliedLayout = _tc.LayoutVersion;
            float d = _tc.ButtonRadius * 2f;
            for (int i = 0; i < TouchControls.ButtonCount; i++) Place(_buttons[i].Root, _tc.ButtonCenters[i], d);
            Place(_jump.Root, _tc.JumpCenter, d);
            foreach (var b in _buttons) SizeTexts(b, d);
            SizeTexts(_jump, d);
            _moveBase.sizeDelta = _atkBase.sizeDelta = Vector2.one * (_tc.StickRadius * 2f);
            _moveKnob.sizeDelta = Vector2.one * (_tc.KnobRadius * 2f);
            _atkKnob.sizeDelta = Vector2.one * (_tc.KnobRadius * 2.5f);
        }

        private static void SizeTexts(Btn b, float d)
        {
            b.CdText.fontSize = d * 0.38f;
            b.Charges.fontSize = d * 0.3f;
            ((RectTransform)b.Charges.transform).sizeDelta = new Vector2(d * 0.45f, d * 0.4f);
            ((RectTransform)b.Lock.transform).sizeDelta = Vector2.one * (d * 0.36f);
        }

        private static void Place(RectTransform rt, Vector2 screenPos, float size)
        {
            rt.anchoredPosition = screenPos;
            rt.sizeDelta = new Vector2(size, size);
        }

        // ---------------- Bindung (Cast-Aufblitzen) ----------------

        private void Bind(PlayerAbilities a)
        {
            if (a == _bound) return;
            Unbind();
            _bound = a;
            if (_bound != null) _bound.OnAbilityCast += HandleCast;
            foreach (var b in _buttons) b.Id = (AbilityId)(-1);
            _atkIconId = (AbilityId)(-1);
        }

        private void Unbind()
        {
            if (_bound != null) _bound.OnAbilityCast -= HandleCast;
            _bound = null;
        }

        private void HandleCast(AbilityId id)
        {
            foreach (var b in _buttons) if (b.Id == id) b.Flash = 0f;
        }

        // ---------------- Aktualisieren ----------------

        void LateUpdate()
        {
            if (_tc == null) return;
            float target = _tc.ControlsVisible && _tc.LayoutVersion > 0 ? (_tc.Dimmed ? 0.45f : 1f) : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 8f);
            _group.alpha = _alpha;
            bool show = _alpha > 0.001f;
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) return;

            if (_appliedLayout != _tc.LayoutVersion) ApplyLayout();
            Bind(_tc.Abilities);

            UpdateSticks();
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < TouchControls.ButtonCount; i++) UpdateButton(_buttons[i], i, dt);
            UpdateJump();
        }

        private void UpdateSticks()
        {
            bool mv = _tc.MoveActive;
            _moveBase.anchoredPosition = mv ? _tc.MoveOrigin : _tc.MoveRest;
            _moveKnob.anchoredPosition = mv ? _tc.MoveKnob : _tc.MoveRest;
            SetAlpha(_moveBaseImg, mv ? 0.95f : 0.4f);
            SetAlpha(_moveKnobImg, mv ? 0.95f : 0.45f);

            bool atk = _tc.AttackActive;
            _atkBase.anchoredPosition = atk ? _tc.AttackOrigin : _tc.AttackCenter;
            _atkKnob.anchoredPosition = atk ? _tc.AttackKnob : _tc.AttackCenter;
            SetAlpha(_atkBaseImg, atk ? 0.95f : 0.5f);
            SetAlpha(_atkKnobImg, atk ? 1f : 0.85f);
            _atkKnob.localScale = Vector3.one * (_tc.AttackFiring ? 0.94f : 1f);

            // Icon des Grundangriffs auf dem Angriffs-Stick
            if (_bound != null)
            {
                AbilityId id = _bound.GetAbility(AbilitySlot.Primary);
                if (id != _atkIconId)
                {
                    _atkIconId = id;
                    _atkIcon.sprite = _bound.GetAbilityIcon(id);
                    _atkIcon.enabled = _atkIcon.sprite != null;
                }
            }
        }

        private void UpdateButton(Btn b, int index, float dt)
        {
            if (_bound == null) return;
            AbilitySlot slot = TouchControls.ButtonSlots[index];
            AbilityId id = _bound.GetAbility(slot);
            if (id != b.Id)
            {
                b.Id = id;
                b.Icon.sprite = _bound.GetAbilityIcon(id);
                b.Icon.enabled = b.Icon.sprite != null;
            }

            var kit = _bound.ActiveKit;
            float mana = _bound.Mana != null ? _bound.Mana.CurrentMana : 0f;
            bool unlocked = _bound.IsUnlocked(id);
            float required = kit != null ? kit.GetRequiredMana(id) : _bound.GetManaCost(id);
            float remaining = _bound.GetCooldownRemaining(id);
            float duration = _bound.GetCooldownDisplayDuration(id);
            bool enoughMana = mana >= required;
            bool active = _bound.IsAbilityActive(id);

            b.Icon.color = !unlocked ? _lockedTint : (enoughMana || active ? _readyTint : _noManaTint);
            if (b.Lock.enabled == unlocked) b.Lock.enabled = !unlocked && b.Lock.sprite != null;
            b.Face.color = unlocked ? Color.white : new Color(0.7f, 0.7f, 0.7f, 1f);

            bool onCooldown = unlocked && remaining > 0f && duration > 0f;
            b.Cooldown.fillAmount = onCooldown ? Mathf.Clamp01(remaining / duration) : 0f;
            bool showCd = onCooldown && remaining >= 0.15f;
            if (b.CdText.gameObject.activeSelf != showCd) b.CdText.gameObject.SetActive(showCd);
            if (showCd) b.CdText.text = remaining >= 1f ? Mathf.CeilToInt(remaining).ToString() : remaining.ToString("0.0");

            int max = _bound.GetMaxCharges(id);
            bool showCharges = max > 1 && unlocked;
            if (b.Charges.gameObject.activeSelf != showCharges) b.Charges.gameObject.SetActive(showCharges);
            if (showCharges)
            {
                int charges = _bound.GetCharges(id);
                b.Charges.text = charges.ToString();
                b.Charges.color = charges > 0 ? _pipFull : _tooExpensive;
            }

            // Ring: abbrechen (rot) > zielen (hell) > aktiv/gehalten (Gold, pulsierend)
            Color ring;
            if (_tc.ButtonCancel(index)) ring = CancelColor;
            else if (_tc.ButtonAiming(index)) ring = AimColor;
            else if (active)
            {
                ring = _activeColor;
                ring.a = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 9f);
            }
            else ring = new Color(1f, 1f, 1f, 0f);
            b.Ring.color = ring;

            // Druck: etwas kleiner; Wirken: kurzer Puls
            float scale = _tc.ButtonDown(index) ? 0.92f : 1f;
            if (b.Flash >= 0f)
            {
                b.Flash += dt;
                float t = b.Flash / 0.25f;
                if (t >= 1f) b.Flash = -1f;
                else scale = Mathf.Max(scale, 1f + 0.12f * Mathf.Sin(t * Mathf.PI));
            }
            b.Root.localScale = Vector3.one * scale;
        }

        private void UpdateJump()
        {
            _jump.Root.localScale = Vector3.one * (_tc.JumpDown ? 0.92f : 1f);
        }

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            if (Mathf.Approximately(c.a, a)) return;
            c.a = a;
            g.color = c;
        }
    }

    // Prozedurale Sprites der Touch-Steuerung (128 px, im Code gezeichnet – keine Fremd-Assets).
    // Farben aus den HUD-Sprites: Gold 255/209/89, Holz 115/75/40, Umriss 46/29/15.
    public static class TouchSprites
    {
        private static Sprite _disc, _ring, _face, _base, _jump;

        private static readonly Color Gold = new Color32(255, 209, 89, 255);
        private static readonly Color GoldDark = new Color32(196, 142, 52, 255);
        private static readonly Color WoodLight = new Color32(150, 100, 55, 255);
        private static readonly Color WoodDark = new Color32(92, 58, 28, 255);
        private static readonly Color Outline = new Color32(46, 29, 15, 255);

        // Weiße Scheibe (Abklingzeit-Füllung)
        public static Sprite Disc => _disc != null ? _disc : (_disc = Make("TouchDisc", (x, y, r, aa) => White(Edge(r, 1f, aa))));

        // Weißer Ring (Zustandsrahmen)
        public static Sprite Ring => _ring != null ? _ring : (_ring = Make("TouchRing", (x, y, r, aa) =>
            White(Edge(r, 1f, aa) * (1f - Edge(r, 0.8f, aa)))));

        // Knopf: Holzscheibe, Goldrand, dunkler Umriss
        public static Sprite ButtonFace => _face != null ? _face : (_face = Make("TouchButton", (x, y, r, aa) =>
        {
            float outer = Edge(r, 1f, aa);
            if (outer <= 0f) return new Color(0f, 0f, 0f, 0f);
            Color wood = Color.Lerp(WoodLight, WoodDark, Mathf.Clamp01(r / 0.8f + y * 0.15f));
            wood.a = 0.8f;
            Color gold = Color.Lerp(GoldDark, Gold, Mathf.Clamp01(0.5f + y * 0.5f));
            Color c = Color.Lerp(wood, gold, 1f - Edge(r, 0.8f, aa));
            c = Color.Lerp(c, Outline, 1f - Edge(r, 0.92f, aa));
            c.a *= outer;
            return c;
        }));

        // Stick-Sockel: dunkle, durchscheinende Fläche mit Goldring
        public static Sprite StickBase => _base != null ? _base : (_base = Make("TouchStickBase", (x, y, r, aa) =>
        {
            float outer = Edge(r, 1f, aa);
            if (outer <= 0f) return new Color(0f, 0f, 0f, 0f);
            Color fill = new Color32(30, 18, 8, 90);
            Color gold = Gold;
            gold.a = 0.85f;
            float ring = Edge(r, 0.97f, aa) * (1f - Edge(r, 0.88f, aa));
            Color c = Color.Lerp(fill, gold, ring);
            c = Color.Lerp(c, new Color(Outline.r, Outline.g, Outline.b, 0.8f), 1f - Edge(r, 0.97f, aa));
            c.a *= outer;
            return c;
        }));

        // Sprung-Symbol: zwei Winkel nach oben (weiß, wird getönt)
        public static Sprite JumpIcon => _jump != null ? _jump : (_jump = Make("TouchJump", (x, y, r, aa) =>
        {
            float d = Mathf.Min(Chevron(x, y, 0.28f), Chevron(x, y, -0.22f));
            return White(Mathf.Clamp01((0.13f - d) / aa));
        }));

        // Abstand zu einem Winkel „^“ mit Spitze bei (0, tipY)
        private static float Chevron(float x, float y, float tipY)
        {
            Vector2 p = new Vector2(x, y);
            Vector2 tip = new Vector2(0f, tipY);
            return Mathf.Min(SegDist(p, tip, tip + new Vector2(-0.5f, -0.42f)), SegDist(p, tip, tip + new Vector2(0.5f, -0.42f)));
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        // 1 innerhalb von radius, weicher Rand über aa
        private static float Edge(float r, float radius, float aa) => Mathf.Clamp01((radius - r) / aa + 0.5f);

        private static Color White(float a) => new Color(1f, 1f, 1f, Mathf.Clamp01(a));

        private static Sprite Make(string name, System.Func<float, float, float, float, Color> shade)
        {
            const int n = 128;
            float aa = 2.5f / n;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n * 2f - 1f, fy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(fx * fx + fy * fy);
                    px[y * n + x] = shade(fx, fy, r, aa);
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
