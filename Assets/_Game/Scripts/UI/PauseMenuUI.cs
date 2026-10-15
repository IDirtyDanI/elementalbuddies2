using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace ElementalBuddies
{
    // Pause-Menü (ESC): links Navigation, rechts Seiten "Übersicht" (Champion, Karten + Werte) und "Einstellungen".
    // Script auf ein immer aktives Objekt (Canvas) legen; Root wird ein-/ausgeblendet.
    public class PauseMenuUI : MonoBehaviour
    {
        public GameObject Root;                 // Vollbild-Overlay
        public GameObject OverviewPage;         // Seite 1
        public GameObject SettingsPage;         // Seite 2
        public GameObject ConfirmQuitPanel;     // Bestätigungs-Dialog (Kind von Root)
        public Button ResumeButton, OverviewButton, SettingsButton, RestartButton, QuitButton, ConfirmYesButton, ConfirmNoButton;
        [Tooltip("Zurück ins Hauptmenü (GameSession.MenuScene).")]
        public Button MainMenuButton;
        public Image OverviewButtonImage, SettingsButtonImage;   // Tab-Hervorhebung
        public Sprite TabNormalSprite, TabActiveSprite;           // optional; sonst Farbton
        public RectTransform CardsContainer;     // VerticalLayoutGroup für Karten-Zeilen
        public GameObject CardRowTemplate;       // inaktiv; Kinder "Icon"(Image), "Title"/"Desc"/"Count"(TMP)
        public TextMeshProUGUI CardsEmptyText;
        public TextMeshProUGUI StatsText;
        public TextMeshProUGUI WaveInfoText;
        public Slider MasterSlider, MusicSlider, SfxSlider;
        public TextMeshProUGUI MasterValue, MusicValue, SfxValue;
        public Toggle FullscreenToggle;

        private const string PrefFullscreen = "fullscreen";
        private const string HeaderOpen = "<b><color=#5a3a1a>";
        private const string HeaderClose = "</color></b>";
        private const string Green = "#2e6b2e";
        private static readonly Color TabActiveTint = new Color(1f, 0.93f, 0.75f);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private PauseManager _pause;
        private readonly List<GameObject> _rows = new List<GameObject>();

        // Aktives Pause-Menü der Szene (PauseManager fragt hier, ob Esc nur die Belegungs-Seite schließt)
        public static PauseMenuUI Active { get; private set; }

        void Start()
        {
            Active = this;
            _pause = PauseManager.Instance != null ? PauseManager.Instance : FindFirstObjectByType<PauseManager>();
            if (_pause != null) _pause.OnPauseChanged += HandlePauseChanged;

            if (ResumeButton != null) ResumeButton.onClick.AddListener(() => { if (_pause != null) _pause.Resume(); });
            if (OverviewButton != null) OverviewButton.onClick.AddListener(() => ShowPage(true));
            if (SettingsButton != null) SettingsButton.onClick.AddListener(() => ShowPage(false));
            if (RestartButton != null) RestartButton.onClick.AddListener(() => { if (_pause != null) _pause.RestartGame(); });
            if (QuitButton != null) QuitButton.onClick.AddListener(() => SetConfirm(true));
            if (MainMenuButton != null) MainMenuButton.onClick.AddListener(LoadMainMenu);
            if (ConfirmYesButton != null) ConfirmYesButton.onClick.AddListener(() => { if (_pause != null) _pause.QuitGame(); });
            if (ConfirmNoButton != null) ConfirmNoButton.onClick.AddListener(() => SetConfirm(false));

            InitSettings();

            if (CardRowTemplate != null) CardRowTemplate.SetActive(false);
            SetConfirm(false);
            if (Root != null) Root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            if (_pause != null) _pause.OnPauseChanged -= HandlePauseChanged;
        }

        private void HandlePauseChanged(bool paused)
        {
            SetConfirm(false);
            ResetMenuConfirm();
            if (paused)
            {
                // Neustart nur beim Host (Clients warten auf ihn)
                if (RestartButton != null) RestartButton.gameObject.SetActive(PauseManager.CanRestart);
                if (Root != null) Root.SetActive(true);
                ShowPage(true); // inkl. Refresh()
                SyncSettings();
            }
            else if (Root != null) Root.SetActive(false);
        }

        // Verbindung beenden und das Hauptmenü laden. Host im Koop: erst nachfragen (Raum wird für alle geschlossen).
        public void LoadMainMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(GameSession.MenuScene))
            {
                Debug.LogWarning($"PauseMenuUI: Szene '{GameSession.MenuScene}' ist nicht in den Build Settings.");
                return;
            }
            if (Net.IsMultiplayer && Net.IsServer && Time.unscaledTime > _menuConfirmUntil)
            {
                _menuConfirmUntil = Time.unscaledTime + 4f;
                var label = MainMenuButton != null ? MainMenuButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                if (label != null)
                {
                    if (_menuLabelText == null) _menuLabelText = label.text;
                    label.text = "Raum wird für alle geschlossen – nochmal klicken";
                    label.enableAutoSizing = true;
                }
                return;
            }
            if (_pause != null) _pause.LeaveToMenu();
            else
            {
                if (Net.CanPauseTime) Time.timeScale = 1f;
                AudioListener.pause = false;
                if (Net.IsRunning) NetSession.Instance.LeaveToMenu();
                else SceneManager.LoadScene(GameSession.MenuScene);
            }
        }

        private float _menuConfirmUntil = -1f;
        private string _menuLabelText;

        void Update()
        {
            if (_menuLabelText != null && Time.unscaledTime > _menuConfirmUntil) ResetMenuConfirm();
        }

        private void ResetMenuConfirm()
        {
            _menuConfirmUntil = -1f;
            if (_menuLabelText == null || MainMenuButton == null) return;
            var label = MainMenuButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = _menuLabelText;
            _menuLabelText = null;
        }

        // Eigene Spielfigur (über das Netz gespawnt)
        private static PlayerAbilities LocalAbilities()
        {
            var av = PlayerAvatar.Local;
            return av != null && av.Abilities != null ? av.Abilities : PlayerAbilities.Instance;
        }

        private void SetConfirm(bool show)
        {
            if (ConfirmQuitPanel != null) ConfirmQuitPanel.SetActive(show);
        }

        private void ShowPage(bool overview)
        {
            if (_keysPage != null) _keysPage.SetActive(false);
            if (OverviewPage != null) OverviewPage.SetActive(overview);
            if (SettingsPage != null) SettingsPage.SetActive(!overview);
            SetTab(OverviewButtonImage, overview);
            SetTab(SettingsButtonImage, !overview);
            if (overview) Refresh();
        }

        private void SetTab(Image img, bool active)
        {
            if (img == null) return;
            if (TabNormalSprite != null && TabActiveSprite != null)
            {
                img.sprite = active ? TabActiveSprite : TabNormalSprite;
                img.color = Color.white;
            }
            else img.color = active ? TabActiveTint : Color.white;
        }

        // ---------------- Einstellungen ----------------

        private void InitSettings()
        {
            SetupSlider(MasterSlider, MasterValue, v => { if (GameAudio.Instance != null) GameAudio.Instance.SetMaster(v); });
            SetupSlider(MusicSlider, MusicValue, v => { if (GameAudio.Instance != null) GameAudio.Instance.SetMusic(v); });
            SetupSlider(SfxSlider, SfxValue, v => { if (GameAudio.Instance != null) GameAudio.Instance.SetSfx(v); });

            if (FullscreenToggle != null)
            {
                FullscreenToggle.onValueChanged.AddListener(v =>
                {
                    Screen.fullScreen = v;
                    PlayerPrefs.SetInt(PrefFullscreen, v ? 1 : 0);
                    PlayerPrefs.Save();
                });
            }
            BuildFeelRows();
            BuildKeyBindings();
            SyncSettings();
        }

        // ---------------- Tastaturbelegung (Unterseite der Einstellungen, zur Laufzeit gebaut) ----------------

        private GameObject _keysPage;

        public bool KeysPageOpen => _keysPage != null && _keysPage.activeSelf;

        // Knopf „Tastaturbelegung“ oben rechts auf der Einstellungs-Seite; Seite mit Liste, Meldungszeile,
        // „Alle zurücksetzen“ und „Zurück“ (→ Einstellungen)
        private void BuildKeyBindings()
        {
            if (SettingsPage == null) return;
            var template = OverviewButton != null ? OverviewButton : ResumeButton;
            if (template == null) return;
            var settings = (RectTransform)SettingsPage.transform;

            var open = MakePageButton(template, settings, "KeyBindingsButton", "Tastaturbelegung", new Vector2(300f, 50f));
            var ort = (RectTransform)open.transform;
            ort.anchorMin = ort.anchorMax = ort.pivot = new Vector2(1f, 1f);
            ort.anchoredPosition = new Vector2(0f, 2f);
            open.onClick.AddListener(ShowKeys);

            _keysPage = new GameObject("KeyBindingsPage", typeof(RectTransform));
            var page = (RectTransform)_keysPage.transform;
            page.SetParent(settings.parent, false);
            page.anchorMin = settings.anchorMin;
            page.anchorMax = settings.anchorMax;
            page.pivot = settings.pivot;
            page.anchoredPosition = settings.anchoredPosition;
            page.sizeDelta = settings.sizeDelta;

            var header = settings.Find("Header");
            TMP_FontAsset titleFont = null;
            if (header != null)
            {
                var h = Instantiate(header.gameObject, page, false);
                h.name = "Header";
                var ht = h.GetComponent<TextMeshProUGUI>();
                if (ht != null) { ht.text = "Tastaturbelegung"; titleFont = ht.font; }
            }
            var line = settings.Find("Line");
            if (line != null) Instantiate(line.gameObject, page, false).name = "Line";

            var hint = settings.Find("Hint");
            TextMeshProUGUI status = null;
            if (hint != null)
            {
                var sgo = Instantiate(hint.gameObject, page, false);
                sgo.name = "Status";
                status = sgo.GetComponent<TextMeshProUGUI>();
                var srt = status.rectTransform;
                srt.offsetMin = new Vector2(0f, srt.offsetMin.y);
                srt.offsetMax = new Vector2(-470f, srt.offsetMax.y);
                srt.sizeDelta = new Vector2(srt.sizeDelta.x, 56f);
                srt.anchoredPosition = new Vector2(srt.anchoredPosition.x, 4f);
                status.textWrappingMode = TextWrappingModes.Normal;
                status.enableAutoSizing = true;
                status.fontSizeMax = status.fontSize;
                status.fontSizeMin = 15f;
                status.alignment = TextAlignmentOptions.Left;
            }

            var area = KeyBindingsView.CreateScroll(page, "Scroll", out var scroll);
            var art = (RectTransform)scroll.transform;
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(0f, 72f);
            art.offsetMax = new Vector2(0f, -62f);

            var reset = MakePageButton(template, page, "ResetAllButton", "Alle zurücksetzen", new Vector2(250f, 54f));
            var rrt = (RectTransform)reset.transform;
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(1f, 0f);
            rrt.anchoredPosition = new Vector2(-200f, 6f);
            var back = MakePageButton(template, page, "BackButton", "Zurück", new Vector2(190f, 54f));
            var bkrt = (RectTransform)back.transform;
            bkrt.anchorMin = bkrt.anchorMax = bkrt.pivot = new Vector2(1f, 0f);
            bkrt.anchoredPosition = new Vector2(0f, 6f);
            back.onClick.AddListener(() => ShowPage(false));

            // Schrift der Einstellungs-Zeilen (Fallback: Übersicht)
            var rowLabel = SfxSlider != null ? SfxSlider.transform.parent.Find("Label") : null;
            var body = rowLabel != null && rowLabel.GetComponent<TextMeshProUGUI>() != null ? rowLabel.GetComponent<TextMeshProUGUI>().font
                     : StatsText != null ? StatsText.font : null;
            var view = KeyBindingsView.Create(_keysPage, area, template, titleFont, body, status);
            reset.onClick.AddListener(view.ResetAll);
            _keysPage.SetActive(false);
        }

        private static Button MakePageButton(Button template, RectTransform parent, string name, string text, Vector2 size)
        {
            var go = Instantiate(template.gameObject, parent, false);
            go.name = name;
            var le = go.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;
            ((RectTransform)go.transform).sizeDelta = size;
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var t = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (t != null)
            {
                t.text = text;
                t.fontSizeMax = Mathf.Min(t.fontSize, 26f);
                t.fontSizeMin = 16f;
                t.enableAutoSizing = true;
            }
            return b;
        }

        private void ShowKeys()
        {
            if (_keysPage == null) return;
            if (OverviewPage != null) OverviewPage.SetActive(false);
            if (SettingsPage != null) SettingsPage.SetActive(false);
            _keysPage.SetActive(true);
            SetTab(OverviewButtonImage, false);
            SetTab(SettingsButtonImage, true);
            var scroll = _keysPage.GetComponentInChildren<ScrollRect>(true);
            Canvas.ForceUpdateCanvases();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        // Esc auf der Belegungs-Seite: zurück zu den Einstellungen statt das Spiel fortzusetzen
        public bool TryCloseKeysPage()
        {
            if (!KeysPageOpen) return false;
            ShowPage(false);
            return true;
        }

        // ---------------- Game Feel (Zeilen werden zur Laufzeit aus den vorhandenen Zeilen geklont) ----------------

        private Slider _shakeSlider, _numbersSlider;
        private TextMeshProUGUI _shakeValue, _numbersValue;
        private Toggle _hitStopToggle, _flashToggle, _hintsToggle, _minimapToggle;
        private Button _hintReset;

        private void BuildFeelRows()
        {
            if (SfxSlider != null && SfxSlider.transform.parent != null)
            {
                _shakeSlider = CloneSliderRow(SfxSlider, "ShakeRow", "Bildschirmwackeln", out _shakeValue);
                if (_shakeSlider != null)
                    SetupSlider(_shakeSlider, _shakeValue, v => GameFeel.ShakeStrength = v);

                _numbersSlider = CloneSliderRow(SfxSlider, "NumbersRow", "Schadenszahlen", out _numbersValue);
                if (_numbersSlider != null)
                {
                    _numbersSlider.minValue = 0f;
                    _numbersSlider.maxValue = 2f;
                    _numbersSlider.wholeNumbers = true;
                    _numbersSlider.onValueChanged.AddListener(v =>
                    {
                        GameFeel.Numbers = (GameFeel.NumberMode)Mathf.RoundToInt(v);
                        if (_numbersValue != null) _numbersValue.text = GameFeel.NumberModeLabel(GameFeel.Numbers);
                    });
                }
            }
            if (FullscreenToggle != null && FullscreenToggle.transform.parent != null)
            {
                _hitStopToggle = CloneToggleRow(FullscreenToggle, "HitStopRow", "Treffer-Stopp & Zeitlupe");
                if (_hitStopToggle != null) _hitStopToggle.onValueChanged.AddListener(v => GameFeel.HitStop = v);
                _flashToggle = CloneToggleRow(FullscreenToggle, "FlashRow", "Treffer-Aufblitzen");
                if (_flashToggle != null) _flashToggle.onValueChanged.AddListener(v => GameFeel.Flash = v);
                _hintsToggle = CloneToggleRow(FullscreenToggle, "HintsRow", "Tipps anzeigen");
                if (_hintsToggle != null)
                {
                    _hintsToggle.onValueChanged.AddListener(v => HintManager.Enabled = v);
                    _hintReset = HintResetButton.Attach(_hintsToggle.transform.parent, OverviewButton != null ? OverviewButton : ResumeButton);
                }
                _minimapToggle = CloneToggleRow(FullscreenToggle, "MinimapRow", "Minikarte anzeigen");
                if (_minimapToggle != null) _minimapToggle.onValueChanged.AddListener(v => MinimapUI.Enabled = v);
            }
            CompactRows();
        }

        // Mit acht statt vier Zeilen: Abstände verkleinern, damit alles über dem Hinweis unten Platz hat
        private void CompactRows()
        {
            var rows = SfxSlider != null ? SfxSlider.transform.parent.parent as RectTransform : null;
            var layout = rows != null ? rows.GetComponent<VerticalLayoutGroup>() : null;
            if (layout == null) return;
            int count = 0;
            foreach (RectTransform c in rows) if (c.gameObject.activeSelf) count++;
            // Ab neun Zeilen (Tipps anzeigen, Sprint 5) enger: kleinere Abstände und Zeilenhöhe
            bool dense = count > 8;
            layout.spacing = dense ? 4f : 10f;
            layout.padding = new RectOffset(layout.padding.left, layout.padding.right, dense ? 0 : 4, dense ? 0 : 4);
            float h = layout.padding.top + layout.padding.bottom;
            int n = 0;
            foreach (RectTransform c in rows)
            {
                if (!c.gameObject.activeSelf) continue;
                if (dense)
                {
                    float rh = Mathf.Min(c.sizeDelta.y, 48f);
                    c.sizeDelta = new Vector2(c.sizeDelta.x, rh);
                    var le = c.GetComponent<LayoutElement>();
                    if (le != null) { le.preferredHeight = rh; le.minHeight = Mathf.Min(le.minHeight, rh); }
                }
                h += c.sizeDelta.y;
                n++;
            }
            h += Mathf.Max(0, n - 1) * layout.spacing;
            rows.sizeDelta = new Vector2(rows.sizeDelta.x, h);
        }

        private static Slider CloneSliderRow(Slider template, string name, string label, out TextMeshProUGUI value)
        {
            value = null;
            var row = template.transform.parent;
            var copy = Instantiate(row.gameObject, row.parent);
            copy.name = name;
            var slider = copy.GetComponentInChildren<Slider>(true);
            if (slider == null) { Destroy(copy); return null; }
            slider.onValueChanged.RemoveAllListeners();
            SetRowLabel(copy.transform, label);
            var v = copy.transform.Find("Value");
            if (v != null) value = v.GetComponent<TextMeshProUGUI>();
            return slider;
        }

        private static Toggle CloneToggleRow(Toggle template, string name, string label)
        {
            var row = template.transform.parent;
            var copy = Instantiate(row.gameObject, row.parent);
            copy.name = name;
            var toggle = copy.GetComponentInChildren<Toggle>(true);
            if (toggle == null) { Destroy(copy); return null; }
            toggle.onValueChanged.RemoveAllListeners();
            SetRowLabel(copy.transform, label);
            return toggle;
        }

        private static void SetRowLabel(Transform row, string text)
        {
            var l = row.Find("Label");
            var tmp = l != null ? l.GetComponent<TextMeshProUGUI>() : null;
            if (tmp != null) tmp.text = text;
        }

        private void SetupSlider(Slider slider, TextMeshProUGUI label, System.Action<float> apply)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(v =>
            {
                apply(v);
                SetPercent(label, v);
            });
        }

        // Regler auf aktuelle Werte setzen (ohne Events)
        private void SyncSettings()
        {
            var ga = GameAudio.Instance;
            SyncSlider(MasterSlider, MasterValue, ga != null ? ga.MasterVolume : 1f);
            SyncSlider(MusicSlider, MusicValue, ga != null ? ga.MusicLevel : 1f);
            SyncSlider(SfxSlider, SfxValue, ga != null ? ga.SfxVolume : 1f);
            if (FullscreenToggle != null) FullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
            SyncSlider(_shakeSlider, _shakeValue, GameFeel.ShakeStrength);
            if (_numbersSlider != null) _numbersSlider.SetValueWithoutNotify((int)GameFeel.Numbers);
            if (_numbersValue != null) _numbersValue.text = GameFeel.NumberModeLabel(GameFeel.Numbers);
            if (_hitStopToggle != null) _hitStopToggle.SetIsOnWithoutNotify(GameFeel.HitStop);
            if (_flashToggle != null) _flashToggle.SetIsOnWithoutNotify(GameFeel.Flash);
            if (_hintsToggle != null) _hintsToggle.SetIsOnWithoutNotify(HintManager.Enabled);
            if (_minimapToggle != null) _minimapToggle.SetIsOnWithoutNotify(MinimapUI.Enabled);
            HintResetButton.ResetLabel(_hintReset);
        }

        private static void SyncSlider(Slider slider, TextMeshProUGUI label, float v)
        {
            if (slider != null) slider.SetValueWithoutNotify(v);
            SetPercent(label, v);
        }

        private static void SetPercent(TextMeshProUGUI label, float v)
        {
            if (label != null) label.text = (v * 100f).ToString("0", Inv) + " %";
        }

        // ---------------- Übersicht ----------------

        public void Refresh()
        {
            RefreshCards();
            if (StatsText != null) StatsText.text = BuildStats();
            if (WaveInfoText != null) WaveInfoText.text = BuildWaveInfo();
        }

        private void RefreshCards()
        {
            foreach (var row in _rows)
            {
                if (row == null) continue;
                row.SetActive(false);
                Destroy(row);
            }
            _rows.Clear();

            // Gleiche Karten zusammenfassen (Reihenfolge der ersten Wahl)
            var order = new List<UpgradeDefinitionSO>();
            var counts = new Dictionary<UpgradeDefinitionSO, int>();
            var um = UpgradeManager.Instance;
            if (um != null)
            {
                foreach (var up in um.PickedUpgrades)
                {
                    if (up == null) continue;
                    if (counts.TryGetValue(up, out int c)) counts[up] = c + 1;
                    else
                    {
                        counts[up] = 1;
                        order.Add(up);
                    }
                }
            }

            var mm = MerchantManager.Instance;
            var merchantCards = mm != null ? mm.GetPickedSummary() : new List<KeyValuePair<MerchantCardSO, int>>();

            if (CardsEmptyText != null)
            {
                CardsEmptyText.gameObject.SetActive(order.Count == 0 && merchantCards.Count == 0);
                CardsEmptyText.text = "Noch keine Karten gewählt – nach jeder Welle darfst du eine aussuchen.";
            }
            if (CardRowTemplate == null || CardsContainer == null) return;

            foreach (var card in order)
            {
                int n = counts[card];
                var row = Instantiate(CardRowTemplate, CardsContainer);
                row.name = "CardRow_" + card.name;
                row.SetActive(true);
                _rows.Add(row);

                var icon = FindChild<Image>(row.transform, "Icon");
                var cardIcon = card.LocalIcon;
                if (icon != null)
                {
                    icon.gameObject.SetActive(cardIcon != null);
                    if (cardIcon != null) icon.sprite = cardIcon;
                }

                var title = FindChild<TextMeshProUGUI>(row.transform, "Title");
                if (title != null) title.text = card.LocalTitle;

                var count = FindChild<TextMeshProUGUI>(row.transform, "Count");
                if (count != null)
                {
                    count.gameObject.SetActive(n > 1);
                    count.text = "×" + n;
                }

                var desc = FindChild<TextMeshProUGUI>(row.transform, "Desc");
                if (desc != null)
                {
                    string effect = EffectLine(card, n);
                    string text = card.LocalDescription ?? "";
                    if (!string.IsNullOrEmpty(effect))
                        text += (text.Length > 0 ? "\n" : "") + $"<color={Green}>{effect}</color>";
                    desc.text = text;
                }
            }

            // Händlerkarten (Fähigkeits-Verbesserungen) mit Gesamtwirkung und aktuellem Wert
            foreach (var kv in merchantCards)
            {
                var card = kv.Key;
                int n = kv.Value; // Kartenwert-Vielfache: normale Karte 1, verfluchte MerchantManager.CurseMultiplier
                int cursed = mm != null ? mm.GetPickedCursedCount(card) : 0;
                var row = Instantiate(CardRowTemplate, CardsContainer);
                row.name = "MerchantRow_" + card.name;
                row.SetActive(true);
                _rows.Add(row);

                var icon = FindChild<Image>(row.transform, "Icon");
                if (icon != null)
                {
                    icon.gameObject.SetActive(card.Icon != null);
                    if (card.Icon != null) icon.sprite = card.Icon;
                }
                var title = FindChild<TextMeshProUGUI>(row.transform, "Title");
                if (title != null) title.text = cursed > 0 ? $"{card.Title} <color=#8e3a92>(verflucht)</color>" : card.Title;
                var count = FindChild<TextMeshProUGUI>(row.transform, "Count");
                if (count != null)
                {
                    count.gameObject.SetActive(n > 1);
                    count.text = "×" + n;
                }
                var desc = FindChild<TextMeshProUGUI>(row.transform, "Desc");
                if (desc != null) desc.text = $"{MerchantInfo.Name(card.Merchant)}: {card.Description}\n<color={Green}>{MerchantEffectLine(card, n)}</color>";
            }
        }

        // "Gesamt: +40 % · Schwerthieb – Reichweite: 2,6 m → 3,6 m"
        private static string MerchantEffectLine(MerchantCardSO card, int n)
        {
            string total = "Gesamt: " + AbilityMods.FormatValue(card.Stat, card.Value * n);
            // Vorschau rückwärts: Wert ohne diese Karten → jetziger Wert
            if (MerchantManager.TryPreview(card, -card.Value * n, out string label, out float now, out float without, out string unit))
            {
                var pa = LocalAbilities();
                string ability = pa != null && pa.ActiveKit != null ? pa.ActiveKit.GetName(card.Ability) : card.Ability.ToString();
                total += $" · {ability} – {label}: {MerchantManager.FormatNumber(without)}{unit} → {MerchantManager.FormatNumber(now)}{unit}";
            }
            return total;
        }

        // Gesamtwirkung aller Exemplare einer Karte
        private static string EffectLine(UpgradeDefinitionSO card, int n)
        {
            if (card.StatToBuff == StatType.BuddySlot)
            {
                int slots = n * Mathf.Max(1, Mathf.RoundToInt(card.Value));
                return $"+{slots} Buddy-Slot" + (slots == 1 ? "" : "s");
            }
            if (card.Type == UpgradeType.Heal)
                return $"Gesamt: {Num(n * card.Value, "0")} HP geheilt";
            // Sonderkarten (Sprint 3) ohne einfachen Wert: Zusammenfassung aus CardEffects
            if (card.Effect != CardEffect.None && card.Effect != CardEffect.BloodPact)
                return "Gesamt: " + CardEffects.Summary(card.Effect);

            if (card.StatToBuff == StatType.ShardGain)
                return $"Gesamt: Splitter-Ausbeute {Signed(n * card.Value, "0.#")} %";

            var cls = UpgradeDefinitionSO.LocalClass;
            string stat = StatName(card, cls) + TargetSuffix(card.Target);
            // Berserker: Mana-Obergrenzen-Karten wirken anteilig auf die Wut-Obergrenze (ResourceProfile.CapBonusScale)
            if (card.StatToBuff == StatType.ManaCap && !card.IsPercentage && PlayerMana.Local != null && PlayerMana.Local.HasProfile)
                return $"Gesamt: {Signed(n * card.Value * PlayerMana.Local.Profile.CapBonusScale, "0.#")} {stat}";
            if (card.StatToBuff == StatType.Cooldown)
            {
                float total = (1f - Mathf.Pow(1f - Mathf.Clamp(card.Value, 0f, 90f) / 100f, n)) * 100f;
                return $"Gesamt: −{Num(total, "0.#")} % {stat}";
            }
            if (card.IsPercentage)
            {
                // Prozentkarten stapeln additiv gegen den Basiswert (UpgradeManager.ModifyValue)
                float total = n * card.Value;
                return $"Gesamt: {Signed(total, "0.#")} % {stat}";
            }
            // Flacher Spieler-Schaden / Mobilität zählt als ganze Prozentpunkte (siehe UpgradeManager)
            if (card.Target == UpgradeTarget.Player && (card.StatToBuff == StatType.Damage || card.StatToBuff == StatType.Mobility))
                return $"Gesamt: {Signed(n * card.Value, "0.#")} % {stat}";
            return $"Gesamt: {Signed(n * card.Value, "0.#")}{StatUnit(card.StatToBuff)} {stat}";
        }

        private static string StatName(UpgradeDefinitionSO card, ChampionClass cls)
        {
            bool rage = cls == ChampionClass.Berserker;
            switch (card.StatToBuff)
            {
                case StatType.Damage: return card.Target == UpgradeTarget.Player ? "Fähigkeitsschaden" : "Schaden";
                case StatType.Cooldown: return "Abklingzeit";
                case StatType.Mobility: return MobilityName(cls);
                case StatType.Range: return "Reichweite";
                case StatType.FireRate: return "Feuerrate";
                case StatType.Health: return "Max. Leben";
                case StatType.Speed: return "Lauftempo";
                case StatType.ManaRegen: return rage ? "Wutaufbau" : "Mana-Regeneration";
                case StatType.ManaCap: return rage ? "Max. Wut" : "Max. Mana";
                case StatType.ShardGain: return "Splitter-Ausbeute";
                default: return "";
            }
        }

        // Was die Mobilitäts-Karte („Ausweichkunst“) bei der Klasse verbessert
        private static string MobilityName(ChampionClass cls)
        {
            switch (cls)
            {
                case ChampionClass.Knight: return "Block-Ausdauer";
                case ChampionClass.Archer: return "Rollweite";
                case ChampionClass.Berserker: return "Ansturm-/Sprungweite";
                default: return "Blink-Reichweite";
            }
        }

        private static string StatUnit(StatType stat)
        {
            switch (stat)
            {
                case StatType.Range: return " m";
                case StatType.FireRate: return "/s";
                case StatType.Health: return " HP";
                case StatType.Speed: return " m/s";
                case StatType.ManaRegen: return "/s";
                default: return "";
            }
        }

        private static string TargetSuffix(UpgradeTarget target)
        {
            switch (target)
            {
                case UpgradeTarget.AllUnits: return " (alle Buddies)";
                case UpgradeTarget.FireUnit: return $" ({ElementInfo.Name(0)}-Buddy)";
                case UpgradeTarget.IceUnit: return $" ({ElementInfo.Name(1)}-Buddy)";
                case UpgradeTarget.EarthUnit: return $" ({ElementInfo.Name(2)}-Buddy)";
                case UpgradeTarget.LightUnit: return $" ({ElementInfo.Name(3)}-Buddy)";
                default: return "";
            }
        }

        // ---------------- Werte ----------------

        private string BuildStats()
        {
            var sb = new StringBuilder();
            var um = UpgradeManager.Instance;

            // Champion + Fähigkeiten (exakte Live-Werte wie im Tooltip)
            var abilities = LocalAbilities();
            if (abilities != null && abilities.ActiveKit != null)
            {
                AppendChampion(sb, abilities);
                sb.Append('\n');
            }

            // Spieler
            sb.Append(HeaderOpen).Append("Spieler").Append(HeaderClose).Append('\n');
            if (abilities != null)
            {
                float cur = abilities.DamageMultiplier;
                float bas = um != null ? um.BaseDamageMultiplier : 1f;
                string line = "Fähigkeitsschaden: " + Colored(Num(cur * 100f, "0") + " %", cur > bas + 0.0001f);
                if (!Same(cur, bas)) line += $" ({Signed((cur - bas) * 100f, "0")} %)";
                sb.Append(line).Append('\n');

                float cdBase = um != null ? um.BaseCooldownMultiplier : 1f;
                if (!Same(abilities.CooldownMultiplier, cdBase))
                    sb.Append("Abklingzeiten: ").Append(Colored(Num(abilities.CooldownMultiplier * 100f, "0") + " %", abilities.CooldownMultiplier < cdBase)).Append($" ({Signed((abilities.CooldownMultiplier - cdBase) * 100f, "0")} %)").Append('\n');
                float mobBase = um != null ? um.BaseMobilityMultiplier : 1f;
                if (!Same(abilities.MobilityMultiplier, mobBase))
                    sb.Append(MobilityName(abilities.ActiveClass)).Append(": ").Append(Colored(Num(abilities.MobilityMultiplier * 100f, "0") + " %", abilities.MobilityMultiplier > mobBase)).Append($" ({Signed((abilities.MobilityMultiplier - mobBase) * 100f, "0")} %)").Append('\n');
            }

            var local = PlayerAvatar.Local;
            var controller = local != null && local.Controller != null ? local.Controller
                : (um != null && um.PlayerControllerRef != null ? um.PlayerControllerRef : FindFirstObjectByType<PlayerController>());
            if (controller != null)
                sb.Append(StatLine("Lauftempo", controller.MoveSpeed, um != null && um.BasePlayerSpeed > 0f ? um.BasePlayerSpeed : controller.MoveSpeed, "0.#", " m/s")).Append('\n');

            var stats = local != null && local.Stats != null ? local.Stats
                : (um != null && um.PlayerStatsRef != null ? um.PlayerStatsRef : FindFirstObjectByType<PlayerStats>());
            if (stats != null)
                sb.Append(StatLine("Max. Leben", stats.MaxHP, um != null && um.BasePlayerMaxHP > 0f ? um.BasePlayerMaxHP : stats.MaxHP, "0", "")).Append('\n');

            var mana = PlayerMana.Local;
            if (mana != null)
            {
                var eco = mana;
                if (eco.HasProfile)
                {
                    // Berserker: Wut statt Mana (Obergrenze mit Karten-Anteil, Aufbau-Faktor aus Regenerations-Karten)
                    string rn = eco.ResourceName;
                    sb.Append(StatLine("Max. " + rn, eco.MaxMana, eco.Profile.Cap, "0", "")).Append('\n');
                    float gain = eco.GainFactor;
                    string g = Colored(Num(gain * 100f, "0") + " %", gain > 1.0001f);
                    if (!Same(gain, 1f)) g += $" ({Signed((gain - 1f) * 100f, "0")} %)";
                    sb.Append(rn).Append("aufbau: ").Append(g).Append('\n');
                }
                else
                {
                float baseCap = eco.BaseManaCap > 0f ? eco.BaseManaCap : eco.MaxMana;
                sb.Append(StatLine("Max. Mana", eco.MaxMana, baseCap, "0", "")).Append('\n');

                bool regenChanged = !Same(eco.RegenOut, eco.BaseRegenOut) || !Same(eco.RegenIn, eco.BaseRegenIn);
                bool regenUp = eco.RegenOut > eco.BaseRegenOut + 0.0001f || eco.RegenIn > eco.BaseRegenIn + 0.0001f;
                string regen = Colored(Num(eco.RegenOut, "0.#") + "/s außerhalb, " + Num(eco.RegenIn, "0.#") + "/s im Kampf", regenUp);
                if (regenChanged)
                {
                    string pct = eco.BaseRegenOut > 0f ? $", {Signed((eco.RegenOut / eco.BaseRegenOut - 1f) * 100f, "0")} %" : "";
                    regen += $" (Basis {Num(eco.BaseRegenOut, "0.#")} / {Num(eco.BaseRegenIn, "0.#")}{pct})";
                }
                sb.Append("Mana-Regeneration: ").Append(regen).Append('\n');
                }

            }
            var economy = EconomyManager.Instance;
            if (economy != null && economy.ShardGainPercent > 0.0001f)
                sb.Append("Splitter-Ausbeute: ").Append(Colored($"{Signed(economy.ShardGainPercent, "0.#")} %", true)).Append('\n');

            // Buddies
            var im = InteractionManager.Instance;
            if (im != null && im.UnitConfigs != null && im.UnitConfigs.Count > 0)
            {
                sb.Append('\n').Append(HeaderOpen).Append("Buddies").Append(HeaderClose).Append('\n');
                foreach (var cfg in im.UnitConfigs)
                {
                    if (cfg == null) continue;
                    sb.Append(BuddyLine(cfg, um)).Append('\n');
                }

                // Aktive Fusions- und Super-Buddies pro Typ
                var fusionCounts = new int[FusionInfo.Names.Length];
                foreach (var b in ElementalBuddy.Active)
                {
                    if (!(b is FusionBuddy f)) continue;
                    fusionCounts[Mathf.Clamp((int)f.Element, 0, fusionCounts.Length - 1)]++;
                }
                var fusions = new List<string>();
                var supers = new List<string>();
                for (int i = 0; i < fusionCounts.Length; i++)
                {
                    if (fusionCounts[i] <= 0) continue;
                    var list = FusionInfo.IsSuper((FusionElement)i) ? supers : fusions;
                    list.Add($"{fusionCounts[i]}× {FusionInfo.Names[i]}");
                }
                if (fusions.Count > 0) sb.Append("Fusionen: ").Append(string.Join(" · ", fusions)).Append('\n');
                if (supers.Count > 0) sb.Append("Super-Elementare: ").Append(string.Join(" · ", supers)).Append('\n');
            }

            // Buddy-Slots
            var slots = BuddySlotManager.Instance;
            if (slots != null)
            {
                sb.Append('\n').Append(HeaderOpen).Append("Buddy-Slots").Append(HeaderClose).Append('\n');
                if (slots.Unlimited)
                    sb.Append($"Slots: unbegrenzt ({slots.UsedSlots} belegt)");
                else
                    sb.Append($"Slots: {slots.UsedSlots} / {slots.MaxSlots} belegt");

                int fromCards = 0;
                if (um != null)
                {
                    foreach (var up in um.PickedUpgrades)
                        if (up != null && up.StatToBuff == StatType.BuddySlot) fromCards += Mathf.Max(1, Mathf.RoundToInt(up.Value));
                }
                if (fromCards > 0) sb.Append(" · ").Append(Colored($"+{fromCards} durch Karten", true));
                sb.Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }

        // "Champion: Schwertkämpfer" + je Fähigkeit Name, Taste, Kosten und Beschreibung mit Live-Werten
        private static void AppendChampion(StringBuilder sb, PlayerAbilities abilities)
        {
            var kit = abilities.ActiveKit;
            string name = !string.IsNullOrEmpty(kit.DisplayName) ? kit.DisplayName : ChampionName(kit.Class);
            sb.Append(HeaderOpen).Append("Champion: ").Append(name).Append(HeaderClose).Append('\n');
            for (int i = 0; i < AbilitySlots.Count; i++)
            {
                var slot = (AbilitySlot)i;
                AbilityId id = abilities.GetAbility(slot);
                bool unlocked = abilities.IsUnlocked(id);
                sb.Append("<b>").Append(kit.GetName(id)).Append("</b> <size=85%>(").Append(AbilitySlots.ShortKey(slot)).Append(") · ")
                  .Append(kit.GetCostLine(id)).Append("</size>");
                if (!unlocked) sb.Append(" <size=85%><color=#8a2a12>– gesperrt (").Append(ElementInfo.ShrineName(AbilitySlots.ElementOf(slot))).Append(")</color></size>");
                sb.Append('\n');
                sb.Append("<size=85%>").Append(kit.Describe(id, abilities.GetDamageMultiplier(id))).Append("</size>").Append('\n');
            }
        }

        public static string ChampionName(ChampionClass cls)
        {
            switch (cls)
            {
                case ChampionClass.Knight: return "Schwertkämpfer";
                case ChampionClass.Archer: return "Bogenschütze";
                case ChampionClass.Berserker: return "Berserker";
                default: return "Magier";
            }
        }

        private static string BuddyLine(UnitConfigSO cfg, UpgradeManager um)
        {
            int element = ElementOf(cfg);
            Color c = Color.Lerp(ElementInfo.GetColor(element), Color.black, 0.3f); // abgedunkelt für hellen Hintergrund
            string label = $"<b><color=#{ColorUtility.ToHtmlStringRGB(c)}>{ElementInfo.Name(element)}</color></b>";

            float baseDmg = cfg.Damage, baseRange = cfg.Range, baseRate = cfg.FireRate;
            if (um != null && um.TryGetBaseUnitStats(cfg, out float d, out float r, out float f))
            {
                baseDmg = d;
                baseRange = r;
                baseRate = f;
            }

            var tank = cfg.Prefab != null ? cfg.Prefab.GetComponentInChildren<TankBuddy>(true) : null;
            var healer = cfg.Prefab != null ? cfg.Prefab.GetComponentInChildren<HealerBuddy>(true) : null;

            var parts = new List<string>();
            if (tank != null)
            {
                // Erd-Buddy: Aura-DPS/Radius aus dem Prefab, Config.FireRate = Spott-Rate
                parts.Add($"Aura-Schaden {Num(tank.AuraDps, "0.#")}/s");
                parts.Add("Spott alle " + Change(Interval(baseRate), Interval(cfg.FireRate), "0.#", " s", true));
                parts.Add($"Radius {Num(tank.TauntRadius, "0.#")} m");
            }
            else
            {
                parts.Add((healer != null ? "Strahl " : "Schaden ") + Change(baseDmg, cfg.Damage, "0.#", "", false));
                parts.Add((healer != null ? "Angriffsrate " : "Feuerrate ") + Change(baseRate, cfg.FireRate, "0.##", "/s", false));
                parts.Add("Reichweite " + Change(baseRange, cfg.Range, "0.#", " m", false));
            }

            float shrine = ShrineBonuses.GetDamageMultiplier(element);
            if (shrine > 1.0001f) parts.Add(Colored($"Schrein {Signed((shrine - 1f) * 100f, "0")} % Schaden", true));

            string line = label + ": " + string.Join(" · ", parts);
            if (tank != null && (!Same(baseDmg, cfg.Damage) || !Same(baseRange, cfg.Range)))
                line += " <i>(Schaden-/Reichweiten-Karten wirken nicht auf Erd-Buddy)</i>";
            return line;
        }

        // "12 → 13.2 (+10 %)" bei Änderung, sonst nur der Wert. lowerIsBetter für Intervalle.
        private static string Change(float bas, float cur, string fmt, string unit, bool lowerIsBetter)
        {
            if (Same(bas, cur)) return Num(cur, fmt) + unit;
            bool better = lowerIsBetter ? cur < bas : cur > bas;
            string pct = bas != 0f ? $" ({Signed((cur / bas - 1f) * 100f, "0")} %)" : "";
            return Num(bas, fmt) + " → " + Colored(Num(cur, fmt) + unit, better) + pct;
        }

        // "Name: <aktuell> (Basis <basis>, +X %)" – Klammer nur bei Abweichung
        private static string StatLine(string label, float cur, float bas, string fmt, string unit)
        {
            string line = label + ": " + Colored(Num(cur, fmt) + unit, cur > bas + 0.0001f);
            if (!Same(cur, bas))
            {
                string pct = bas != 0f ? $", {Signed((cur / bas - 1f) * 100f, "0")} %" : "";
                line += $" (Basis {Num(bas, fmt)}{unit}{pct})";
            }
            return line;
        }

        private static string BuildWaveInfo()
        {
            var wm = WaveManager.Instance;
            string wave;
            if (wm == null) wave = "";
            else if (wm.IsWaveActive) wave = $"Welle {wm.CurrentWaveIndex + 1} läuft";
            else if (wm.CurrentWaveIndex > 0) wave = $"Welle {wm.CurrentWaveIndex} geschafft";
            else wave = "Vor Welle 1";

            if (wm != null && wm.Difficulty != null)
                wave = string.IsNullOrEmpty(wave) ? wm.Difficulty.DisplayName : $"{wave} · {wm.Difficulty.DisplayName}";

            // Koop: Raumcode zum Weitergeben (Beitritt nach Spielstart ist gesperrt, aber zur Orientierung)
            if (Net.IsMultiplayer && NetSession.Exists && !string.IsNullOrEmpty(NetSession.Instance.RoomCode))
                wave = (string.IsNullOrEmpty(wave) ? "" : wave + " · ") + $"Raum {NetSession.Instance.RoomCode} · {Net.PlayerCount} Spieler";

            int best = GameManager.Instance != null ? GameManager.Instance.BestWave : 0;
            if (best <= 0) return wave;
            return string.IsNullOrEmpty(wave) ? $"Beste Welle: {best}" : $"{wave} · Beste Welle: {best}";
        }

        // ---------------- Helfer ----------------

        // Element über Config-Namen (wie ElementalBuddy.ResolveElementIndex), sonst UnitType
        private static int ElementOf(UnitConfigSO cfg)
        {
            string n = cfg.name.ToLowerInvariant();
            if (n.Contains("fire")) return 0;
            if (n.Contains("ice")) return 1;
            if (n.Contains("earth")) return 2;
            if (n.Contains("light")) return 3;
            int t = (int)cfg.Type;
            return t < ShrineBonuses.ElementCount ? t : 0;
        }

        private static T FindChild<T>(Transform root, string childName) where T : Component
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t.GetComponent<T>();
            return null;
        }

        private static float Interval(float rate) => rate > 0f ? 1f / rate : 0f;
        private static bool Same(float a, float b) => Mathf.Abs(a - b) < 0.0001f;
        private static string Num(float v, string fmt) => v.ToString(fmt, Inv);
        private static string Signed(float v, string fmt) => (v >= 0f ? "+" : "") + v.ToString(fmt, Inv);
        private static string Colored(string s, bool highlight) => highlight ? $"<color={Green}>{s}</color>" : s;
    }
}
