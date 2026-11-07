using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Hauptmenü → Shop (Skin-Shop, Plan „Shop-Plan“ → UI). Alles wird zur Laufzeit gebaut (übersteht „Szene neu bauen“):
    //  - Knopf „Shop“ in der untersten Knopfreihe (Beenden | Kodex | Shop), Kopie des Erfolge-Knopfs.
    //  - Shop-Seite als eigener Canvas: Panel rechts (Pergament), links bleibt die Drehbühne frei – die Kamera-Projektion
    //    wird dafür seitlich verschoben, sodass der Champion mittig im freien Bereich steht. Das Hauptmenü wird solange
    //    ausgeblendet (CanvasGroup), seine Tasten pausieren (MainMenuUI.Update fragt IsOpen ab).
    //  - Reiter je Champion (auch gesperrte – Ausrüsten ist erlaubt), Bereiche Waffen/Rüstung mit Karten (Icon, Name, Stufe,
    //    Preis, Kurztext, Knopf „Holen“ / „Ausrüsten“ / „Ausgerüstet“). Karte anklicken/antippen = Anprobe auf der Bühne
    //    (MenuChampionStage.PreviewSkin, die andere Kategorie bleibt wie ausgerüstet).
    //  - Pro Klasse + Slot gibt es immer eine Standard-Karte (aus dem Katalog oder synthetisch → ShopInventory.Unequip).
    //  - Ohne Katalog (Resources/ShopCatalog fehlt) nur Standard-Karten und ein Hinweis.
    // Bedienung: Esc / Android-Zurück / Gamepad B schließt, Q/E, ←/→, A/D, 1–4 bzw. LB/RB wechseln den Reiter.
    // Touch: größere Knöpfe, Trefferflächen ≥ 48 dp (MobileUiTouchTargets), sicherer Bereich (SafeAreaFitter), Liste scrollt.
    public class ShopUI : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.24f, 0.15f, 0.08f);
        private static readonly Color Soft = new Color(0.42f, 0.3f, 0.18f);
        private static readonly Color Cream = new Color(0.937f, 0.878f, 0.741f);
        private static readonly Color FreeColor = new Color(0.22f, 0.5f, 0.2f);

        public static ShopUI Instance { get; private set; }

        // Shop offen (auch noch im Bild des Schließens, damit dieselbe Esc-Taste nicht im Menü weiterwirkt)
        public static bool IsOpen => Instance != null && (Instance._open || Instance._closedFrame == Time.frameCount);

        private MainMenuUI _menu;
        private Button _shopButton;
        private TMP_FontAsset _titleFont, _boldFont, _bodyFont;
        private Sprite _parchment, _ribbon, _cardSprite, _cardSelectedSprite, _discSprite, _lockSprite, _shadeSprite;
        private Sprite _tabSelected, _tabSelectedHover, _tabNormal, _tabNormalHover;

        // Seite
        private GameObject _canvasGo;
        private RectTransform _canvasRt, _root, _panel, _content, _backdrop;
        private TextMeshProUGUI _balance, _info;
        private ScrollRect _scroll;
        private readonly List<Button> _tabs = new List<Button>();
        private readonly List<ChampionClass> _tabClasses = new List<ChampionClass>();
        private readonly List<GameObject> _cards = new List<GameObject>();
        private float _lastWidth = -1f;
        private bool _touch;

        // Zustand
        private bool _open;
        private int _closedFrame = -1;
        private ChampionClass _cls;
        private CanvasGroup _menuGroup;
        private GameObject _testerCanvas;
        private bool _refreshQueued;

        // Anprobe je Klasse: ein Slot + Item (null = Standard); die andere Kategorie zeigt die ausgerüstete
        private struct Trial { public bool Active; public ShopSlot Slot; public ShopItemSO Item; }
        private readonly Dictionary<ChampionClass, Trial> _trials = new Dictionary<ChampionClass, Trial>();

        // Kamera-Verschiebung (Projektion, NDC-Einheiten)
        private float _shift;
        private bool _shiftActive;

        // Maße (Referenz 2400 × 1350 wie das Menü, an der Höhe)
        private const float CardW = 300f, PanelTop = 70f, PanelBottom = 24f, PanelRight = 40f, Pad = 50f;
        private const float MaxTouchBoost = 1.25f;
        private float CardH => _touch ? 440f : 420f;
        private float ButtonH => _touch ? 78f : 60f;
        private float TabH => _touch ? 88f : 72f;

        // ---------------- Einbau ----------------

        public static ShopUI Attach(MainMenuUI menu)
        {
            if (menu == null || !Application.isPlaying) return null;
            var shop = menu.GetComponent<ShopUI>();
            if (shop == null) shop = menu.gameObject.AddComponent<ShopUI>();
            shop.Init(menu);
            return shop;
        }

        void OnEnable() => ShopInventory.Changed += HandleShopChanged;
        void OnDisable() => ShopInventory.Changed -= HandleShopChanged;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ResetCamera();
            if (_canvasGo != null) Destroy(_canvasGo);
        }

        private void Init(MainMenuUI menu)
        {
            Instance = this;
            _menu = menu;
            if (_shopButton != null) return;
            _touch = MobilePlatform.TouchMode;

            var playLabel = menu.PlayButton != null ? menu.PlayButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            _titleFont = playLabel != null ? playLabel.font : TMP_Settings.defaultFontAsset;
            _boldFont = menu.DifficultyDescription != null ? menu.DifficultyDescription.font : _titleFont;
            _bodyFont = menu.DetailDescription != null ? menu.DetailDescription.font : FindFont("AlegreyaSans-Regular", _boldFont);

            if (menu.DetailPanel != null)
            {
                var img = menu.DetailPanel.GetComponent<Image>();
                if (img != null) _parchment = img.sprite;
                var rib = menu.DetailPanel.transform.Find("Ribbon");
                var rimg = rib != null ? rib.GetComponent<Image>() : null;
                if (rimg != null) _ribbon = rimg.sprite;
            }
            _cardSprite = menu.CardNormalSprite;
            _cardSelectedSprite = menu.CardSelectedSprite;
            if (menu.Cards.Length > 0 && menu.Cards[0] != null)
            {
                if (menu.Cards[0].PortraitBg != null) _discSprite = menu.Cards[0].PortraitBg.sprite;
                if (menu.Cards[0].Lock != null) _lockSprite = menu.Cards[0].Lock.sprite;
            }
            var shade = MobileUi.FindDeep(menu.transform, "ShadeLeft");
            var simg = shade != null ? shade.GetComponent<Image>() : null;
            if (simg != null) _shadeSprite = simg.sprite;
            _tabSelected = menu.DifficultySelectedSprite;
            _tabSelectedHover = menu.DifficultySelectedHoverSprite;
            _tabNormal = menu.DifficultyNormalSprite;
            _tabNormalHover = menu.DifficultyNormalHoverSprite;

            BuildMenuButton();
        }

        private static TMP_FontAsset FindFont(string prefix, TMP_FontAsset fallback)
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f != null && f.name.StartsWith(prefix)) return f;
            return fallback;
        }

        // Knopf „Shop“: unterste Reihe Beenden | Kodex | Shop (gleich breit); liegt direkt hinter „Beenden“ in der
        // Hierarchie, damit die Overlays (Einstellungen, Erfolge, Kodex, Beitreten) ihn verdecken
        private void BuildMenuButton()
        {
            if (_menu.QuitButton == null) return;
            var quit = (RectTransform)_menu.QuitButton.transform;
            var src = _menu.AchievementsButton != null ? _menu.AchievementsButton : _menu.SettingsButton;
            if (src == null) return;
            var go = Instantiate(src.gameObject, quit.parent, false);
            go.name = "ShopButton";
            go.transform.SetSiblingIndex(quit.GetSiblingIndex() + 1);
            _shopButton = go.GetComponent<Button>();
            _shopButton.onClick = new Button.ButtonClickedEvent();
            _shopButton.onClick.AddListener(Open);
            var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = "Shop";
            foreach (var img in go.GetComponentsInChildren<Image>(true))
                if (img.gameObject != go) img.gameObject.SetActive(false); // Pokal-Symbol des Erfolge-Knopfs

            // Reihe neu aufteilen
            var row = new List<RectTransform> { quit };
            var codex = quit.parent.Find("CodexButton") as RectTransform;
            if (codex != null)
            {
                row.Add(codex);
                // Kodex-Knopf (MainMenuMeta) hing als letztes Kind über den Overlays → ebenfalls direkt hinter „Beenden“
                codex.SetSiblingIndex(quit.GetSiblingIndex() + 1);
                go.transform.SetSiblingIndex(codex.GetSiblingIndex() + 1);
            }
            float left = quit.anchoredPosition.x;
            float right = left + quit.sizeDelta.x;
            foreach (var r in row) right = Mathf.Max(right, r.anchoredPosition.x + r.sizeDelta.x);
            var brt = (RectTransform)go.transform;
            brt.anchorMin = quit.anchorMin;
            brt.anchorMax = quit.anchorMax;
            brt.pivot = quit.pivot;
            row.Add(brt);
            const float gap = 12f;
            float w = (right - left - gap * (row.Count - 1)) / row.Count;
            for (int i = 0; i < row.Count; i++)
            {
                row[i].anchoredPosition = new Vector2(left + i * (w + gap), quit.anchoredPosition.y);
                row[i].sizeDelta = new Vector2(w, quit.sizeDelta.y);
                var l = row[i].GetComponentInChildren<TextMeshProUGUI>(true);
                // Kopien des Erfolge-Knopfs: Text hatte einen linken Rand für das Pokal-Symbol → mittig
                if (l != null && row[i] != quit) l.margin = new Vector4(8f, 0f, 8f, 0f);
                if (l != null && !l.enableAutoSizing)
                {
                    l.enableAutoSizing = true;
                    l.fontSizeMax = l.fontSize;
                    l.fontSizeMin = Mathf.Min(l.fontSize, 22f);
                }
            }
        }

        // ---------------- Öffnen / Schließen ----------------

        public void Open()
        {
            if (_open || _menu == null || !_menu.IsMainView) return;
            if (_canvasGo == null) BuildPage();
            _open = true;
            _cls = _menu.Selected;
            _trials.Clear();

            // Hauptmenü ausblenden (Tasten pausieren über IsOpen)
            _menuGroup = _menu.GetComponent<CanvasGroup>();
            if (_menuGroup == null) _menuGroup = _menu.gameObject.AddComponent<CanvasGroup>();
            _menuGroup.alpha = 0f;
            _menuGroup.interactable = false;
            _menuGroup.blocksRaycasts = false;
            // Versionsanzeige/Tester-Knopf (Touch) läge über dem Kopf der Shop-Seite
            _testerCanvas = GameObject.Find("TesterToolsCanvas");
            if (_testerCanvas != null) _testerCanvas.SetActive(false);

            // Gesperrte Champions zur Anprobe nicht abgedunkelt zeigen
            if (_menu.Stage != null)
                foreach (var def in _menu.Champions)
                    if (def != null) _menu.Stage.SetLocked(def.Class, false);

            _canvasGo.SetActive(true);
            GameAudio.Play(SfxId.UiClick);
            SelectTab(_cls, true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _closedFrame = Time.frameCount;
            if (_canvasGo != null) _canvasGo.SetActive(false);

            var stage = _menu != null ? _menu.Stage : null;
            if (stage != null)
            {
                foreach (var def in _menu.Champions)
                {
                    if (def == null) continue;
                    stage.ResetPreview(def.Class);
                    stage.SetLocked(def.Class, MainMenuUI.IsLocked(def.Class));
                }
                stage.Focus(_menu.Selected, false);
            }
            _trials.Clear();

            if (_menuGroup != null)
            {
                _menuGroup.alpha = 1f;
                _menuGroup.interactable = true;
                _menuGroup.blocksRaycasts = true;
            }
            if (_testerCanvas != null) _testerCanvas.SetActive(true);
            _testerCanvas = null;
            GameAudio.Play(SfxId.UiClick);
            if (EventSystem.current != null && _menu != null && _menu.PlayButton != null)
                EventSystem.current.SetSelectedGameObject(_menu.PlayButton.gameObject);
        }

        void Update()
        {
            if (_menu == null) return;
            if (_shopButton != null && _menu.QuitButton != null)
            {
                // Nur im Hauptbildschirm (wie „Beenden“/„Kodex“), nicht in Lobby/Beitreten
                bool vis = _menu.QuitButton.gameObject.activeInHierarchy;
                if (_shopButton.gameObject.activeSelf != vis) _shopButton.gameObject.SetActive(vis);
            }
            if (!_open) return;
            // Lobby/Beitreten (z. B. Netz-Start) oder Spielstart → Shop schließen
            if (!_menu.IsMainView) { Close(); return; }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
                int idx = _tabClasses.IndexOf(_cls);
                if (kb.qKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) StepTab(idx - 1);
                else if (kb.eKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) StepTab(idx + 1);
                else if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) StepTab(0);
                else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) StepTab(1);
                else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) StepTab(2);
                else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) StepTab(3);
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                if (gp.buttonEast.wasPressedThisFrame) { Close(); return; }
                int idx = _tabClasses.IndexOf(_cls);
                if (gp.leftShoulder.wasPressedThisFrame) StepTab(idx - 1);
                else if (gp.rightShoulder.wasPressedThisFrame) StepTab(idx + 1);
            }
        }

        void LateUpdate()
        {
            if (_open)
            {
                UpdateLayout();
                if (_refreshQueued) { _refreshQueued = false; Refresh(); }
            }
            UpdateCamera();
        }

        private void StepTab(int i)
        {
            if (_tabClasses.Count == 0) return;
            i = ((i % _tabClasses.Count) + _tabClasses.Count) % _tabClasses.Count;
            SelectTab(_tabClasses[i], false);
        }

        private void SelectTab(ChampionClass c, bool initial)
        {
            if (!initial && c == _cls) return;
            _cls = c;
            if (_menu.Stage != null) _menu.Stage.Focus(c, false);
            if (!initial) GameAudio.Play(SfxId.UiClick);
            Refresh();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private void HandleShopChanged()
        {
            // Mehrere Änderungen in einem Bild (Holen + Ausrüsten) zusammenfassen; Karten nicht im eigenen Klick zerstören
            if (_open) _refreshQueued = true;
        }

        // ---------------- Kamera ----------------

        // Projektion seitlich verschieben: der vordere Sockel (gewählter Champion) steht mittig im freien Bereich links
        // vom Panel. Hintergrund bleibt bildschirmfüllend; beim Schließen gleitet die Kamera zurück.
        private void UpdateCamera()
        {
            var stage = _menu != null ? _menu.Stage : null;
            var cam = stage != null ? stage.Cam : null;
            if (cam == null) return;
            if (!_open && !_shiftActive) return;

            cam.ResetProjectionMatrix();
            float target = 0f;
            if (_open && _panel != null && Screen.width > 0)
            {
                Vector3 front = FrontPoint(stage, cam);
                float now = cam.WorldToViewportPoint(front).x * 2f - 1f;
                var corners = new Vector3[4];
                _panel.GetWorldCorners(corners); // Overlay: Welt = Bildschirmpixel
                float leftPx = MobilePlatform.TouchMode ? MobilePlatform.SafeArea.xMin : 0f;
                float freeCenter = (leftPx + corners[0].x) * 0.5f;
                float want = freeCenter / Screen.width * 2f - 1f;
                target = now - want;
            }
            _shift = Mathf.Lerp(_shift, target, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            if (!_open && Mathf.Abs(_shift) < 0.0005f)
            {
                _shift = 0f;
                _shiftActive = false;
                return; // Projektion bleibt zurückgesetzt
            }
            _shiftActive = true;
            var m = cam.projectionMatrix;
            m.m02 = _shift;
            cam.projectionMatrix = m;
        }

        private void ResetCamera()
        {
            var cam = _menu != null && _menu.Stage != null ? _menu.Stage.Cam : null;
            if (cam != null && _shiftActive) cam.ResetProjectionMatrix();
            _shift = 0f;
            _shiftActive = false;
        }

        // Punkt vorne auf der Drehscheibe (dort steht der gewählte Champion), in Brusthöhe
        private static Vector3 FrontPoint(MenuChampionStage stage, Camera cam)
        {
            Vector3 c = stage.Carousel != null ? stage.Carousel.position : Vector3.zero;
            float r = 0f, y = c.y;
            foreach (var p in stage.Pedestals)
                if (p != null && p.Anchor != null)
                {
                    Vector3 d = p.Anchor.position - c;
                    y = p.Anchor.position.y;
                    d.y = 0f;
                    r = Mathf.Max(r, d.magnitude);
                }
            Vector3 toCam = cam.transform.position - c;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.0001f) toCam = Vector3.back;
            return new Vector3(c.x, y + 0.8f, c.z) + toCam.normalized * r;
        }

        // ---------------- Seite bauen ----------------

        private void BuildPage()
        {
            _canvasGo = new GameObject("ShopCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var menuCanvas = _menu.GetComponentInParent<Canvas>();
            canvas.sortingOrder = (menuCanvas != null ? menuCanvas.sortingOrder : 0) + 10;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            var menuScaler = menuCanvas != null ? menuCanvas.GetComponent<CanvasScaler>() : null;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2400f, 1350f);
            scaler.matchWidthOrHeight = 1f;
            if (menuScaler != null && menuScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                scaler.referenceResolution = menuScaler.referenceResolution;
                scaler.matchWidthOrHeight = menuScaler.matchWidthOrHeight;
            }
            _canvasRt = (RectTransform)_canvasGo.transform;

            _root = NewRect("Root", _canvasRt);
            Stretch(_root);

            // Abdunkelung hinter dem Panel (Schatten des Menüs, gespiegelt)
            _backdrop = NewRect("Shade", _root);
            var bimg = _backdrop.gameObject.AddComponent<Image>();
            bimg.sprite = _shadeSprite;
            bimg.color = _shadeSprite != null ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.05f, 0.03f, 0.02f, 0.6f);
            bimg.raycastTarget = false;
            _backdrop.anchorMin = new Vector2(1f, 0f);
            _backdrop.anchorMax = new Vector2(1f, 1f);
            _backdrop.pivot = new Vector2(1f, 0.5f);
            _backdrop.localScale = new Vector3(-1f, 1f, 1f); // Schatten zeigt nach links (dunkel rechts)
            _backdrop.pivot = new Vector2(0f, 0.5f);

            // Panel
            _panel = NewRect("Panel", _root);
            _panel.anchorMin = new Vector2(1f, 0f);
            _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 0.5f);
            var pimg = _panel.gameObject.AddComponent<Image>();
            SetSprite(pimg, _parchment, new Color(0.93f, 0.86f, 0.7f, 1f));
            pimg.raycastTarget = true;

            // Banner mit Titel
            var ribbon = NewRect("Ribbon", _panel);
            ribbon.anchorMin = ribbon.anchorMax = new Vector2(0.5f, 1f);
            ribbon.pivot = new Vector2(0.5f, 0.5f);
            ribbon.anchoredPosition = new Vector2(0f, 10f);
            ribbon.sizeDelta = new Vector2(520f, 110f);
            var rimg = ribbon.gameObject.AddComponent<Image>();
            SetSprite(rimg, _ribbon, new Color(0.55f, 0.15f, 0.12f));
            rimg.raycastTarget = false;
            float titleSize = _menu.DetailName != null ? _menu.DetailName.fontSize : 52f;
            Color titleColor = _menu.DetailName != null ? _menu.DetailName.color : Cream;
            var title = Text(ribbon, "Title", "Shop", _titleFont, titleSize, titleColor, TextAlignmentOptions.Center);
            Stretch(title.rectTransform);
            title.rectTransform.offsetMin = new Vector2(40f, 14f);
            title.rectTransform.offsetMax = new Vector2(-40f, -6f);

            // Guthaben links oben
            _balance = Text(_panel, "Balance", "", _boldFont, 26f, Soft, TextAlignmentOptions.Left);
            _balance.textWrappingMode = TextWrappingModes.Normal;
            _balance.enableAutoSizing = true;
            _balance.fontSizeMin = 18f;
            _balance.fontSizeMax = 26f;
            Place(_balance.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -20f), new Vector2(300f, 70f));

            // Zurück rechts oben
            var back = CloneButton(_menu.SettingsButton, _panel, "BackButton", "Zurück", Close);
            var brt = (RectTransform)back.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
            brt.anchoredPosition = new Vector2(-Pad + 10f, -18f);
            brt.sizeDelta = new Vector2(230f, _touch ? 76f : 66f);

            // Reiter je Champion
            var tabsRt = NewRect("Tabs", _panel);
            tabsRt.anchorMin = new Vector2(0f, 1f);
            tabsRt.anchorMax = new Vector2(1f, 1f);
            tabsRt.pivot = new Vector2(0.5f, 1f);
            tabsRt.offsetMin = new Vector2(Pad, 0f);
            tabsRt.offsetMax = new Vector2(-Pad, 0f);
            tabsRt.anchoredPosition = new Vector2(0f, _touch ? -108f : -100f);
            tabsRt.sizeDelta = new Vector2(-2f * Pad, TabH + 8f);
            var hl = tabsRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12f;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = false;
            hl.childAlignment = TextAnchor.UpperCenter;
            foreach (var def in _menu.Champions)
                if (def != null) BuildTab(tabsRt, def);

            // Hinweiszeile (Anprobe, Sperre, fehlender Katalog)
            _info = Text(_panel, "Info", "", _boldFont, 22f, Soft, TextAlignmentOptions.Left);
            _info.textWrappingMode = TextWrappingModes.Normal;
            _info.enableAutoSizing = true;
            _info.fontSizeMin = 16f;
            _info.fontSizeMax = 22f;
            _info.richText = true;
            var irt = _info.rectTransform;
            irt.anchorMin = new Vector2(0f, 1f);
            irt.anchorMax = new Vector2(1f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            float infoTop = (_touch ? -108f : -100f) - TabH - 14f;
            irt.anchoredPosition = new Vector2(0f, infoTop);
            irt.sizeDelta = new Vector2(-2f * Pad, 54f);

            // Liste (vertikal scrollbar)
            BuildScroll(infoTop - 62f);
            _canvasGo.SetActive(false);

            if (_touch)
            {
                // Touch-Verstärkung gedeckelt: Texte etwas größer als im Menü (Verstärkung 1), links bleibt Platz für den Champion
                MobileUiScaler.Attach(canvas, false, MaxTouchBoost);
                SafeAreaFitter.Attach(canvas, true, true);
                if (_canvasGo.GetComponent<MobileUiTouchTargets>() == null) _canvasGo.AddComponent<MobileUiTouchTargets>();
            }
        }

        private void BuildTab(RectTransform parent, ChampionDefinitionSO def)
        {
            var b = CloneButton(_menu.SettingsButton, parent, "Tab_" + def.Class, def.DisplayName, null);
            var cls = def.Class;
            b.onClick.AddListener(() => SelectTab(cls, false));
            var le = b.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = TabH;
            le.minHeight = TabH;
            le.flexibleWidth = 1f;
            var label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.enableAutoSizing = true;
                label.fontSizeMax = _touch ? 30f : 28f;
                label.fontSizeMin = 16f;
                label.rectTransform.offsetMin = new Vector2(14f, 6f);
                label.rectTransform.offsetMax = new Vector2(-14f, -4f);
            }
            // Farbstreifen der Akzentfarbe unten
            var acc = NewRect("Accent", (RectTransform)b.transform);
            acc.anchorMin = new Vector2(0.2f, 0f);
            acc.anchorMax = new Vector2(0.8f, 0f);
            acc.pivot = new Vector2(0.5f, 0f);
            acc.anchoredPosition = new Vector2(0f, 8f);
            acc.sizeDelta = new Vector2(0f, 5f);
            var aimg = acc.gameObject.AddComponent<Image>();
            aimg.color = def.AccentColor;
            aimg.raycastTarget = false;
            // Schloss bei gesperrtem Champion
            var lk = NewRect("Lock", (RectTransform)b.transform);
            lk.anchorMin = lk.anchorMax = new Vector2(0f, 0.5f);
            lk.pivot = new Vector2(0f, 0.5f);
            lk.anchoredPosition = new Vector2(10f, 2f);
            lk.sizeDelta = new Vector2(34f, 34f);
            var limg = lk.gameObject.AddComponent<Image>();
            limg.sprite = _lockSprite;
            limg.preserveAspect = true;
            limg.raycastTarget = false;
            lk.gameObject.SetActive(false);
            _tabs.Add(b);
            _tabClasses.Add(cls);
        }

        private void BuildScroll(float top)
        {
            var srt = NewRect("Scroll", _panel);
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(Pad - 20f, 34f);
            srt.offsetMax = new Vector2(-Pad + 20f, top);
            _scroll = srt.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 45f;
            _scroll.inertia = true;

            var vp = NewRect("Viewport", srt);
            Stretch(vp);
            vp.offsetMax = new Vector2(-30f, 0f); // Platz für die Bildlaufleiste
            vp.gameObject.AddComponent<RectMask2D>();
            var vimg = vp.gameObject.AddComponent<Image>(); // Raycast-Fläche zum Ziehen zwischen den Karten
            vimg.color = new Color(1f, 1f, 1f, 0f);
            vimg.raycastTarget = true;
            _scroll.viewport = vp;

            _content = NewRect("Content", vp);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            var vl = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 8f;
            vl.padding = new RectOffset(20, 0, 0, 16);
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            var fit = _content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;

            // Bildlaufleiste wie auf der Erfolge-Seite
            var bar = NewRect("Scrollbar", srt);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(_touch ? 30f : 22f, 0f);
            var barImg = bar.gameObject.AddComponent<Image>();
            barImg.color = new Color(0.24f, 0.15f, 0.08f, 0.18f);
            var area = NewRect("Sliding Area", bar);
            Stretch(area);
            var handle = NewRect("Handle", area);
            Stretch(handle);
            var himg = handle.gameObject.AddComponent<Image>();
            himg.color = new Color(0.45f, 0.29f, 0.14f, 0.85f);
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = handle;
            sb.targetGraphic = himg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            _scroll.verticalScrollbarSpacing = 8f;
        }

        // Panelbreite folgt dem Seitenverhältnis (16:10 schmaler, 20:9 bleibt bei 1300)
        private void UpdateLayout()
        {
            if (_root == null) return;
            float w = _root.rect.width;
            if (Mathf.Abs(w - _lastWidth) < 0.5f) return;
            _lastWidth = w;
            float panelW = Mathf.Clamp(w * 0.55f, Mathf.Min(1000f, w - 2f * PanelRight), 1300f);
            _panel.sizeDelta = new Vector2(panelW, -(PanelTop + PanelBottom));
            _panel.anchoredPosition = new Vector2(-PanelRight, (PanelBottom - PanelTop) * 0.5f);
            _backdrop.sizeDelta = new Vector2(panelW + 520f, 0f);
            _backdrop.anchoredPosition = Vector2.zero;
        }

        // ---------------- Inhalt ----------------

        private ChampionDefinitionSO Def(ChampionClass c)
        {
            foreach (var d in _menu.Champions) if (d != null && d.Class == c) return d;
            return null;
        }

        private void Refresh()
        {
            if (_content == null) return;
            UpdateLayout();
            var def = Def(_cls);

            // Reiter
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool sel = _tabClasses[i] == _cls;
                bool locked = MainMenuUI.IsLocked(_tabClasses[i]);
                var img = _tabs[i].GetComponent<Image>();
                if (img != null && _tabSelected != null && _tabNormal != null)
                {
                    img.sprite = sel ? _tabSelected : _tabNormal;
                    var ss = _tabs[i].spriteState;
                    ss.highlightedSprite = sel ? _tabSelectedHover : _tabNormalHover;
                    ss.pressedSprite = ss.highlightedSprite;
                    ss.selectedSprite = null;
                    _tabs[i].spriteState = ss;
                }
                var label = _tabs[i].GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.color = sel ? Cream : (locked ? _menu.LockedNameColor : Ink);
                    label.rectTransform.offsetMin = new Vector2(locked ? 46f : 14f, 6f);
                }
                var lk = _tabs[i].transform.Find("Lock");
                if (lk != null) lk.gameObject.SetActive(locked && _lockSprite != null);
            }

            // Guthaben
            _balance.text = ShopInventory.FreeMode ? "Alles gratis\n<size=80%>(Testphase)</size>"
                                                   : "Edelsteine\n<b>" + ShopInventory.Gems + "</b>";
            _balance.richText = true;

            // Hinweise
            var sb = new System.Text.StringBuilder();
            sb.Append(MobilePlatform.TouchMode ? "Tippe auf eine Karte, um sie anzuprobieren." : "Klicke auf eine Karte, um sie anzuprobieren.");
            if (MainMenuUI.IsLocked(_cls))
                sb.Append("  <color=#").Append(ColorUtility.ToHtmlStringRGB(_menu.LockedTextColor)).Append(">Champion noch gesperrt – Ausrüsten geht trotzdem.</color>");
            if (ShopCatalogSO.Instance == null)
                sb.Append("\n<color=#").Append(ColorUtility.ToHtmlStringRGB(_menu.LockedTextColor)).Append(">Der Shop-Katalog fehlt noch – nur die Standardausrüstung ist verfügbar.</color>");
            _info.text = sb.ToString();

            // Karten neu aufbauen (wenige Objekte)
            foreach (var c in _cards) if (c != null) { c.SetActive(false); Destroy(c); }
            _cards.Clear();
            BuildSection("Waffen", ShopSlot.Weapon, def);
            BuildSection("Rüstung", ShopSlot.Armor, def);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private void BuildSection(string header, ShopSlot slot, ChampionDefinitionSO def)
        {
            var h = Text(_content, "Header_" + slot, header, _titleFont, 38f, Ink, TextAlignmentOptions.BottomLeft);
            var hle = h.gameObject.AddComponent<LayoutElement>();
            hle.preferredHeight = 50f;
            _cards.Add(h.gameObject);
            var line = NewRect("Line", h.rectTransform);
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 1f);
            line.anchoredPosition = new Vector2(0f, -2f);
            line.sizeDelta = new Vector2(-20f, 2f);
            var limg = line.gameObject.AddComponent<Image>();
            limg.color = new Color(0.43f, 0.29f, 0.16f, 0.55f);
            limg.raycastTarget = false;

            var gridRt = NewRect("Grid_" + slot, _content);
            var grid = gridRt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardW, CardH);
            grid.spacing = new Vector2(20f, 20f);
            grid.padding = new RectOffset(0, 0, 10, 6);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            _cards.Add(gridRt.gameObject);

            // Standard zuerst (aus dem Katalog oder synthetisch), dann nach Stufe/Preis
            var items = ShopCatalogSO.ItemsOf(_cls, slot);
            ShopItemSO standard = null;
            foreach (var it in items) if (it != null && it.IsStandard) { standard = it; break; }
            items.RemoveAll(it => it == null || it.IsStandard);
            items.Sort((a, b) =>
            {
                int t = a.Tier.CompareTo(b.Tier);
                if (t != 0) return t;
                int p = a.Price.CompareTo(b.Price);
                return p != 0 ? p : a.NetIndex.CompareTo(b.NetIndex);
            });
            BuildCard(gridRt, slot, standard, true, def);
            foreach (var it in items) BuildCard(gridRt, slot, it, false, def);
        }

        private static Color TierColor(ShopTier t)
        {
            switch (t)
            {
                case ShopTier.Common: return new Color(0.27f, 0.58f, 0.36f);   // Günstig: grün
                case ShopTier.Epic: return new Color(0.84f, 0.6f, 0.14f);      // Edel: gold
                default: return new Color(0.52f, 0.5f, 0.47f);                 // Standard: grau
            }
        }

        private bool IsTrying(ShopSlot slot, ShopItemSO item, bool isStandard)
        {
            if (!_trials.TryGetValue(_cls, out var t) || !t.Active || t.Slot != slot) return false;
            return isStandard ? (t.Item == null || t.Item == item) : t.Item == item;
        }

        // Karte: Stufe, Icon, Name, Kurztext, Preis, Knopf. item null + isStandard = synthetische Standard-Karte.
        private void BuildCard(RectTransform parent, ShopSlot slot, ShopItemSO item, bool isStandard, ChampionDefinitionSO def)
        {
            ShopTier tier = isStandard ? ShopTier.Standard : item.Tier;
            bool owned = isStandard || ShopInventory.IsOwned(item);
            bool equipped = isStandard ? ShopInventory.GetEquipped(_cls, slot) == null : ShopInventory.IsEquipped(item);
            bool trying = IsTrying(slot, item, isStandard);

            var card = NewRect(isStandard ? "Card_Standard" : "Card_" + item.Id, parent);
            var frame = card.gameObject.AddComponent<Image>();
            SetSprite(frame, trying && _cardSelectedSprite != null ? _cardSelectedSprite : _cardSprite, new Color(0.95f, 0.9f, 0.78f));
            frame.raycastTarget = true;
            var click = card.gameObject.AddComponent<ShopCardClick>();
            click.Frame = frame;
            var cls = _cls;
            click.OnClick = () => TryOn(cls, slot, isStandard ? null : item);

            // Stufe
            var band = NewRect("Tier", card);
            band.anchorMin = band.anchorMax = new Vector2(0.5f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.anchoredPosition = new Vector2(0f, -18f);
            band.sizeDelta = new Vector2(CardW - 70f, 34f);
            var bimg = band.gameObject.AddComponent<Image>();
            bimg.color = TierColor(tier);
            bimg.raycastTarget = false;
            var tl = Text(band, "Label", ShopItemSO.TierLabel(tier), _boldFont, 22f, Color.white, TextAlignmentOptions.Center);
            Stretch(tl.rectTransform);

            // Icon auf Scheibe in Stufenfarbe
            var disc = NewRect("IconBg", card);
            disc.anchorMin = disc.anchorMax = new Vector2(0.5f, 1f);
            disc.pivot = new Vector2(0.5f, 1f);
            disc.anchoredPosition = new Vector2(0f, -58f);
            disc.sizeDelta = new Vector2(124f, 124f);
            var dimg = disc.gameObject.AddComponent<Image>();
            dimg.sprite = _discSprite;
            dimg.color = Color.Lerp(TierColor(tier), Color.white, 0.35f);
            dimg.raycastTarget = false;
            Sprite icon = item != null ? item.Icon : null;
            if (icon == null && isStandard && def != null) icon = def.Portrait; // Standard: Porträt des Champions
            if (icon != null)
            {
                var ic = NewRect("Icon", disc);
                Stretch(ic);
                ic.offsetMin = new Vector2(10f, 10f);
                ic.offsetMax = new Vector2(-10f, -10f);
                var iimg = ic.gameObject.AddComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
            }
            else
            {
                // Platzhalter: Anfangsbuchstabe
                string n = item != null && !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : "?";
                var ph = Text(disc, "Placeholder", n.Substring(0, 1).ToUpperInvariant(), _titleFont, 64f, Ink, TextAlignmentOptions.Center);
                Stretch(ph.rectTransform);
            }

            // Name
            string name = item != null && !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName
                        : (slot == ShopSlot.Weapon ? "Standardwaffe" : "Standardrüstung");
            var nt = Text(card, "Name", name, _titleFont, 30f, Ink, TextAlignmentOptions.Center);
            nt.enableAutoSizing = true;
            nt.fontSizeMin = 20f;
            nt.fontSizeMax = 30f;
            Place(nt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -186f), new Vector2(CardW - 40f, 40f), new Vector2(0.5f, 1f));

            // Kurztext
            string desc = item != null && !string.IsNullOrEmpty(item.Description) ? item.Description
                        : "Die Grundausrüstung – immer verfügbar.";
            var dt = Text(card, "Desc", desc, _bodyFont, 20f, Soft, TextAlignmentOptions.Top);
            dt.textWrappingMode = TextWrappingModes.Normal;
            dt.overflowMode = TextOverflowModes.Ellipsis;
            dt.enableAutoSizing = true;
            dt.fontSizeMin = 16f;
            dt.fontSizeMax = 20f;
            float descH = CardH - 230f - 40f - ButtonH - 26f;
            Place(dt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -228f), new Vector2(CardW - 44f, descH), new Vector2(0.5f, 1f));

            // Preis
            string price;
            Color priceColor = Soft;
            if (isStandard) price = "Grundausrüstung";
            else if (owned) price = "Im Besitz";
            else if (ShopInventory.FreeMode) { price = "Gratis"; priceColor = FreeColor; }
            else price = item.Price + " Edelsteine";
            var pt = Text(card, "Price", price, _boldFont, 23f, priceColor, TextAlignmentOptions.Center);
            Place(pt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, ButtonH + 22f), new Vector2(CardW - 40f, 34f), new Vector2(0.5f, 0f));

            // Knopf je Zustand
            Button b;
            if (equipped)
            {
                b = CloneButton(_menu.SettingsButton, card, "Action", "Ausgerüstet", null);
                b.interactable = false;
                var l = b.GetComponentInChildren<TextMeshProUGUI>(true);
                if (l != null) l.color = new Color(Ink.r, Ink.g, Ink.b, 0.75f);
            }
            else if (owned)
            {
                b = CloneButton(_menu.PlayButton, card, "Action", "Ausrüsten", () =>
                {
                    TryOn(cls, slot, isStandard ? null : item, false);
                    if (isStandard && item == null) ShopInventory.Unequip(cls, slot);
                    else ShopInventory.Equip(item);
                    GameAudio.Play(SfxId.UiClick);
                });
            }
            else
            {
                b = CloneButton(_menu.PlayButton, card, "Action", "Holen", () =>
                {
                    TryOn(cls, slot, item, false);
                    // Holen rüstet gleich aus (sonst muss man nach dem Holen noch einmal „Ausrüsten“ tippen)
                    if (ShopInventory.Acquire(item)) ShopInventory.Equip(item);
                    GameAudio.Play(SfxId.UiClick);
                });
                b.interactable = ShopInventory.CanAcquire(item);
            }
            var brt = (RectTransform)b.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 18f);
            brt.sizeDelta = new Vector2(CardW - 60f, ButtonH);
            var bl = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (bl != null)
            {
                bl.enableAutoSizing = true;
                bl.fontSizeMax = _touch ? 32f : 28f;
                bl.fontSizeMin = 18f;
            }
            _cards.Add(card.gameObject);
        }

        // Anprobe: Item auf der Drehbühne zeigen, andere Kategorie = ausgerüstet
        private void TryOn(ChampionClass cls, ShopSlot slot, ShopItemSO item, bool refresh = true)
        {
            if (item != null && item.IsStandard) item = null;
            _trials[cls] = new Trial { Active = true, Slot = slot, Item = item };
            var stage = _menu.Stage;
            if (stage != null)
            {
                if (cls != _cls) stage.Focus(cls, false);
                ShopItemSO weapon = slot == ShopSlot.Weapon ? item : ShopInventory.GetEquipped(cls, ShopSlot.Weapon);
                ShopItemSO armor = slot == ShopSlot.Armor ? item : ShopInventory.GetEquipped(cls, ShopSlot.Armor);
                stage.PreviewSkin(cls, weapon, armor);
            }
            if (refresh)
            {
                GameAudio.Play(SfxId.UiClick);
                _refreshQueued = true; // Rahmen „anprobiert“ (nicht im eigenen Klick zerstören)
            }
        }

        // ---------------- Helfer ----------------

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void SetSprite(Image img, Sprite s, Color fallback)
        {
            img.sprite = s;
            img.type = s != null && s.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = s != null ? Color.white : fallback;
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        // Knopf im Menü-Stil (Holz bzw. Pergament mit Sprite-Tausch) als Kopie eines vorhandenen Knopfs
        private Button CloneButton(Button src, Transform parent, string name, string text, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go;
            if (src != null)
            {
                go = Instantiate(src.gameObject, parent, false);
                go.SetActive(true);
                foreach (var img in go.GetComponentsInChildren<Image>(true))
                    if (img.gameObject != go) Destroy(img.gameObject); // Symbole (Pokal) entfernen
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                go.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.16f);
                var t = Text(go.transform, "Text", text, _boldFont, 28f, Cream, TextAlignmentOptions.Center);
                Stretch(t.rectTransform);
            }
            go.name = name;
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            if (onClick != null) b.onClick.AddListener(onClick);
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            var l = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (l != null)
            {
                l.text = text;
                l.margin = Vector4.zero; // Rand für ein Symbol (Erfolge-Knopf) entfällt
            }
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            return b;
        }
    }

    // Klick/Tipp auf eine Shop-Karte (= Anprobe). Kein Selectable, damit die Trefferfläche des Karten-Knopfs frei wachsen
    // kann; Ziehen (Scrollen) löst keinen Klick aus.
    public class ShopCardClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action OnClick;
        public Image Frame;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
            OnClick?.Invoke();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (Frame != null && !MobilePlatform.TouchMode) Frame.color = new Color(1f, 0.95f, 0.82f, 1f);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (Frame != null) Frame.color = Color.white;
        }
    }
}
