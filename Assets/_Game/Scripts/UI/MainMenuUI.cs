using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Hauptmenü (Szene MainMenu): Champion-Karten, Detail-Panel mit Fähigkeiten, 3D-Vorschau (MenuChampionStage),
    // Spielen / Einstellungen / Erfolge / Beenden. Die Hierarchie baut der Editor-Builder (BuddyTD/Hauptmenü/Szene bauen).
    // Gesperrte Champions (Progression.IsChampionUnlocked) sind ansehbar, aber nicht spielbar. Sperren und Erfolge-Seite
    // beziehen sich auf die gewählte Schwierigkeitsstufe (Erfolge gelten pro Stufe, siehe Progression).
    public class MainMenuUI : MonoBehaviour
    {
        [System.Serializable]
        public class Card
        {
            public ChampionClass Class;
            public Button Button;
            public Image Frame;          // slot_card / slot_card_selected
            public Image PortraitBg;     // Plakette in Akzentfarbe
            public Image Portrait;       // ChampionDefinitionSO.Portrait (ausgeblendet, solange keins gesetzt ist)
            public TextMeshProUGUI Initial; // Platzhalter-Initiale ohne Porträt
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Tagline;
            public Image Accent;         // Farbstreifen unten
            public Image Lock;           // Schloss über dem Porträt (nur bei gesperrtem Champion sichtbar)
        }

        [Header("Daten")]
        public ChampionDefinitionSO[] Champions = new ChampionDefinitionSO[0];
        public MenuChampionStage Stage;

        [Header("Karten")]
        public Card[] Cards = new Card[0];
        public Sprite CardNormalSprite, CardSelectedSprite;

        [Header("Detail-Panel")]
        public TextMeshProUGUI DetailName;
        public TextMeshProUGUI DetailTagline;
        public TextMeshProUGUI DetailDescription;
        public Image DetailAccent;
        public RectTransform AbilityContainer;
        [Tooltip("Inaktive Vorlage; Kinder: Icon (Image), KeyBadge/Key (TMP), Name (TMP), Desc (TMP).")]
        public GameObject AbilityRowTemplate;

        [Header("Hauptbuttons")]
        public Button PlayButton;
        public Button SettingsButton;
        public Button QuitButton;

        [Header("Einstellungen")]
        public GameObject SettingsPanel;
        public Button SettingsBackButton;
        public Slider MasterSlider, MusicSlider, SfxSlider;
        public TextMeshProUGUI MasterValue, MusicValue, SfxValue;
        public Toggle FullscreenToggle;

        [Header("Gesperrte Champions")]
        [Tooltip("Hinweiszeile über \"Spielen\" (Bedingung des gewählten, gesperrten Champions).")]
        public TextMeshProUGUI PlayLockHint;
        public Color LockedPortraitTint = new Color(0.36f, 0.36f, 0.38f, 1f);
        public Color LockedTextColor = new Color(0.55f, 0.2f, 0.12f);
        public Color LockedPlayTint = new Color(0.45f, 0.42f, 0.4f, 1f);
        [Tooltip("Ausgegraute Karte (Rahmen) eines gesperrten Champions.")]
        public Color LockedCardTint = new Color(0.62f, 0.62f, 0.62f, 1f);
        public Color LockedNameColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        [Header("Erfolge")]
        public Button AchievementsButton;
        public GameObject AchievementsPanel;
        public Button AchievementsBackButton;
        public TextMeshProUGUI AchievementsCounter;
        [Tooltip("Überschrift im Banner (\"Erfolge – Normal\"); fehlt sie, wird Ribbon/Title im Panel gesucht.")]
        public TextMeshProUGUI AchievementsTitle;
        public ScrollRect AchievementsScroll;
        public RectTransform AchievementsContent;
        [Tooltip("Inaktive Vorlage; Kinder: MedalIcon, MedalFrame (Image), Title, Desc, Progress (TMP), Bar/BarFill (Image), RewardLabel, RewardName (TMP), RewardIcon (Image), Tiers/Tier1..3 (Image + Label) = Stufen-Marker L/N/S. Hintergrund = Image auf der Vorlage.")]
        public GameObject AchievementRowTemplate;
        public Color AchievedRowColor = new Color(1f, 0.93f, 0.72f, 1f);
        public Color OpenRowColor = new Color(0.9f, 0.87f, 0.82f, 1f);
        public Color LockedIconTint = new Color(0.42f, 0.42f, 0.45f, 1f);
        public Color AchievedBarColor = new Color(0.86f, 0.64f, 0.18f);
        public Color OpenBarColor = new Color(0.62f, 0.45f, 0.28f);
        [Tooltip("Stufen-Marker (L/N/S) einer Zeile: auf dieser Stufe erreicht bzw. nicht erreicht.")]
        public Color TierAchievedColor = new Color(0.86f, 0.64f, 0.18f);
        public Color TierOpenColor = new Color(0.6f, 0.58f, 0.55f, 0.75f);
        public Color TierAchievedTextColor = new Color(0.24f, 0.15f, 0.08f);
        public Color TierOpenTextColor = new Color(0.93f, 0.91f, 0.87f);

        [Header("Schwierigkeit")]
        [Tooltip("Segment-Knöpfe in der Reihenfolge von DifficultySO.All (Leicht, Normal, Schwer); Beschriftung = DisplayName.")]
        public Button[] DifficultyButtons = new Button[0];
        [Tooltip("Zeile unter den Knöpfen: Faktoren der gewählten Stufe (DifficultySO.EffectSummary). Teilt sich den Platz mit PlayLockHint.")]
        public TextMeshProUGUI DifficultyDescription;
        public Sprite DifficultySelectedSprite, DifficultySelectedHoverSprite;
        public Sprite DifficultyNormalSprite, DifficultyNormalHoverSprite;
        public Color DifficultySelectedTextColor = new Color(0.937f, 0.878f, 0.741f);
        public Color DifficultyNormalTextColor = new Color(0.239f, 0.149f, 0.078f);

        [Header("Übergang")]
        [Tooltip("Schwarzer Vollbild-Überblender (Alpha 0..1) beim Spielstart.")]
        public CanvasGroup Fader;
        public float FadeTime = 0.45f;

        private const string PrefFullscreen = "fullscreen";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] ElementNames = { "Feuer", "Eis", "Erde", "Licht" };
        private static readonly string[] ElementColors = { "#c2461e", "#2f7fb5", "#5f7d2a", "#b8860b" };

        private ChampionClass _selected;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<GameObject> _achievementRows = new List<GameObject>();
        private readonly List<AchievementDefinition> _reqBuffer = new List<AchievementDefinition>();
        private TextMeshProUGUI _playLabel;
        private string _playLabelText;
        private Color _taglineColor, _detailTaglineColor;
        private bool _colorsCached;
        private bool _initialized;
        private bool _loading;

        public ChampionClass Selected { get { return _selected; } }

        void Start()
        {
            Init();
        }

        // Öffentlich, damit der Editor die Szene ohne Play-Mode befüllen kann (Vorschau-Renders)
        public void Init()
        {
            // Rückweg aus dem Spiel: Pause/Zeitlupe dürfen nicht hängen bleiben
            if (Application.isPlaying)
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }

            if (!_initialized)
            {
                _initialized = true;
                for (int i = 0; i < Cards.Length; i++)
                {
                    var card = Cards[i];
                    if (card == null || card.Button == null) continue;
                    ChampionClass c = card.Class;
                    card.Button.onClick.AddListener(() => Select(c, false));
                }
                if (PlayButton != null) PlayButton.onClick.AddListener(Play);
                if (SettingsButton != null) SettingsButton.onClick.AddListener(() => ShowSettings(true));
                if (QuitButton != null) QuitButton.onClick.AddListener(Quit);
                if (SettingsBackButton != null) SettingsBackButton.onClick.AddListener(() => ShowSettings(false));
                if (AchievementsButton != null) AchievementsButton.onClick.AddListener(() => ShowAchievements(true));
                if (AchievementsBackButton != null) AchievementsBackButton.onClick.AddListener(() => ShowAchievements(false));
                for (int i = 0; i < DifficultyButtons.Length; i++)
                {
                    if (DifficultyButtons[i] == null) continue;
                    int idx = i;
                    DifficultyButtons[i].onClick.AddListener(() => SelectDifficulty(idx));
                }
                InitSettings();
            }

            EnsureClickable();
            if (AbilityRowTemplate != null) AbilityRowTemplate.SetActive(false);
            if (AchievementRowTemplate != null) AchievementRowTemplate.SetActive(false);
            CacheColors();
            if (Fader != null)
            {
                Fader.alpha = 0f;
                Fader.blocksRaycasts = false;
            }
            FillCards();
            if (Stage != null) Stage.SpawnPreviews(Champions);
            ShowSettings(false);
            ShowAchievements(false);
            // Gespeicherte Wahl gesperrt (z. B. nach dem Zurücksetzen der Erfolge) → Magier vorwählen
            ChampionClass start = GameSession.SelectedChampion;
            if (IsLocked(start)) start = ChampionClass.Mage;
            RefreshDifficulty();
            Select(start, true);
        }

        void OnEnable()
        {
            Progression.OnProgressChanged += HandleProgressChanged;
        }

        void OnDisable()
        {
            Progression.OnProgressChanged -= HandleProgressChanged;
        }

        // Erfolge geändert (z. B. Editor-Menü "Alle freischalten") → Sperren und Erfolge-Seite aktualisieren
        private void HandleProgressChanged()
        {
            if (!_initialized) return;
            RefreshLocks();
            if (AchievementsPanel != null && AchievementsPanel.activeSelf) FillAchievements();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _loading) return;

            if (SettingsPanel != null && SettingsPanel.activeSelf)
            {
                if (kb.escapeKey.wasPressedThisFrame) ShowSettings(false);
                return;
            }
            if (AchievementsPanel != null && AchievementsPanel.activeSelf)
            {
                if (kb.escapeKey.wasPressedThisFrame) ShowAchievements(false);
                // Stufe auch auf der Erfolge-Seite wechseln (Liste zeigt den Stand der gewählten Stufe)
                else if (kb.qKey.wasPressedThisFrame) SelectDifficulty(DifficultyIndex() - 1);
                else if (kb.eKey.wasPressedThisFrame) SelectDifficulty(DifficultyIndex() + 1);
                return;
            }

            // Enter startet direkt – unabhängig davon, ob der Spielen-Button gerade ausgewählt ist
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                Play();
                return;
            }

            // Schwierigkeit per Tastatur: Q leichter, E schwerer (ohne Umlauf)
            if (kb.qKey.wasPressedThisFrame) { SelectDifficulty(DifficultyIndex() - 1); return; }
            if (kb.eKey.wasPressedThisFrame) { SelectDifficulty(DifficultyIndex() + 1); return; }

            // Champion per Tastatur wechseln: ←/→, A/D oder 1–3
            int idx = IndexOf(_selected);
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Select(ClassAt(idx - 1), false);
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Select(ClassAt(idx + 1), false);
            else if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) Select(ClassAt(0), false);
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) Select(ClassAt(1), false);
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) Select(ClassAt(2), false);
        }

        // Alle Bedienelemente müssen Raycasts empfangen (Absicherung, falls die Szene ohne Raycast-Ziele gebaut wurde)
        private void EnsureClickable()
        {
            foreach (var sel in GetComponentsInChildren<Selectable>(true))
            {
                if (sel.targetGraphic != null) sel.targetGraphic.raycastTarget = true;
                var slider = sel as Slider;
                if (slider != null)
                {
                    var bg = slider.transform.Find("Background");
                    var bgImg = bg != null ? bg.GetComponent<Graphic>() : null;
                    if (bgImg != null) bgImg.raycastTarget = true;
                }
            }
        }

        // ---------------- Champion-Auswahl ----------------

        private int IndexOf(ChampionClass c)
        {
            for (int i = 0; i < Champions.Length; i++) if (Champions[i] != null && Champions[i].Class == c) return i;
            return 0;
        }

        private ChampionClass ClassAt(int i)
        {
            if (Champions.Length == 0) return ChampionClass.Mage;
            i = ((i % Champions.Length) + Champions.Length) % Champions.Length;
            return Champions[i] != null ? Champions[i].Class : ChampionClass.Mage;
        }

        private ChampionDefinitionSO Def(ChampionClass c)
        {
            foreach (var d in Champions) if (d != null && d.Class == c) return d;
            return null;
        }

        private void FillCards()
        {
            foreach (var card in Cards)
            {
                if (card == null) continue;
                var def = Def(card.Class);
                if (def == null) continue;
                if (card.Name != null) card.Name.text = def.DisplayName;
                if (card.Tagline != null) card.Tagline.text = def.Tagline;
                if (card.Accent != null) card.Accent.color = def.AccentColor;
                if (card.PortraitBg != null) card.PortraitBg.color = Color.Lerp(def.AccentColor, Color.white, 0.15f);
                bool hasPortrait = def.Portrait != null;
                if (card.Portrait != null)
                {
                    card.Portrait.gameObject.SetActive(hasPortrait);
                    if (hasPortrait) card.Portrait.sprite = def.Portrait;
                }
                if (card.Initial != null)
                {
                    card.Initial.gameObject.SetActive(!hasPortrait);
                    card.Initial.text = string.IsNullOrEmpty(def.DisplayName) ? "?" : def.DisplayName.Substring(0, 1);
                }
            }
        }

        // Champion wählen: Karte hervorheben, Details füllen, 3D-Vorschau fokussieren (Wahl wird erst mit "Spielen" gespeichert)
        public void Select(ChampionClass c, bool instant)
        {
            _selected = c;
            var def = Def(c);

            foreach (var card in Cards)
            {
                if (card == null) continue;
                bool sel = card.Class == c;
                if (card.Frame != null && CardNormalSprite != null && CardSelectedSprite != null)
                    card.Frame.sprite = sel ? CardSelectedSprite : CardNormalSprite;
                if (card.Button != null) card.Button.transform.localScale = sel ? Vector3.one * 1.06f : Vector3.one;
            }

            if (def != null)
            {
                if (DetailName != null) DetailName.text = def.DisplayName;
                if (DetailTagline != null) DetailTagline.text = def.Tagline;
                if (DetailDescription != null) DetailDescription.text = def.Description;
                if (DetailAccent != null) DetailAccent.color = def.AccentColor;
                FillAbilities(def);
            }

            if (Stage != null) Stage.Focus(c, instant);
            RefreshLocks();

            // Tastatur-Fokus auf "Spielen", damit Enter direkt startet
            if (Application.isPlaying && PlayButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(PlayButton.gameObject);
        }

        private void FillAbilities(ChampionDefinitionSO def)
        {
            foreach (var row in _rows)
            {
                if (row == null) continue;
                row.SetActive(false);
                if (Application.isPlaying) Destroy(row);
                else DestroyImmediate(row);
            }
            _rows.Clear();
            if (AbilityRowTemplate == null || AbilityContainer == null) return;

            foreach (var ab in def.Abilities)
            {
                if (ab == null) continue;
                var row = Instantiate(AbilityRowTemplate, AbilityContainer);
                row.name = "Ability_" + ab.Key;
                row.SetActive(true);
                _rows.Add(row);

                var icon = FindChild<Image>(row.transform, "Icon");
                if (icon != null)
                {
                    icon.enabled = ab.Icon != null;
                    icon.sprite = ab.Icon;
                }
                var key = FindChild<TextMeshProUGUI>(row.transform, "Key");
                if (key != null) key.text = ab.Key;
                var title = FindChild<TextMeshProUGUI>(row.transform, "Name");
                if (title != null)
                {
                    string t = ab.Name;
                    if (ab.Element >= 0 && ab.Element < ElementNames.Length)
                        t += "  <size=70%><color=" + ElementColors[ab.Element] + ">" + ElementNames[ab.Element] +
                             " · per Schrein freischaltbar</color></size>";
                    title.text = t;
                }
                var desc = FindChild<TextMeshProUGUI>(row.transform, "Desc");
                if (desc != null) desc.text = ab.Description;
            }
        }

        private static T FindChild<T>(Transform root, string name) where T : Component
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<T>();
            return null;
        }

        // ---------------- Sperren ----------------

        // Rang der im Menü gewählten Stufe (Erfolge und Freischaltungen gelten pro Stufe)
        private static int MenuRank => Progression.Rank(GameSession.Difficulty);

        public static bool IsLocked(ChampionClass c) => !Progression.IsChampionUnlocked(c, false, MenuRank);

        // "Erreiche Welle 6 auf Schwer" (withRank = false: "Erreiche Welle 6" für die schmalen Karten)
        private static string LockGoal(ChampionClass c, bool withRank = true) =>
            Progression.GoalText(Progression.ChampionUnlock(c), MenuRank, withRank);

        private Color _nameColor = Color.white;

        private void CacheColors()
        {
            if (_colorsCached) return;
            _colorsCached = true;
            foreach (var card in Cards)
                if (card != null && card.Tagline != null) { _taglineColor = card.Tagline.color; break; }
            foreach (var card in Cards)
                if (card != null && card.Name != null) { _nameColor = card.Name.color; break; }
            if (DetailTagline != null) _detailTaglineColor = DetailTagline.color;
            if (PlayButton != null)
            {
                _playLabel = PlayButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (_playLabel != null) _playLabelText = _playLabel.text;
            }
        }

        // Karten, Detail-Panel, 3D-Bühne und "Spielen" an den Sperr-Stand anpassen
        private void RefreshLocks()
        {
            CacheColors();
            foreach (var card in Cards)
            {
                if (card == null) continue;
                var def = Def(card.Class);
                bool locked = IsLocked(card.Class);
                if (card.Lock != null) card.Lock.gameObject.SetActive(locked);
                // Gesperrt: ganze Karte ausgrauen (Rahmen, Name, Farbstreifen)
                if (card.Frame != null) card.Frame.color = locked ? LockedCardTint : Color.white;
                if (card.Name != null) card.Name.color = locked ? LockedNameColor : _nameColor;
                if (card.Initial != null) card.Initial.color = locked ? LockedNameColor : _nameColor;
                if (card.Accent != null && def != null)
                {
                    float ga = def.AccentColor.grayscale;
                    card.Accent.color = locked ? new Color(ga * 0.7f, ga * 0.7f, ga * 0.7f, def.AccentColor.a) : def.AccentColor;
                }
                if (card.Portrait != null) card.Portrait.color = locked ? LockedPortraitTint : Color.white;
                if (card.PortraitBg != null && def != null)
                {
                    Color bg = Color.Lerp(def.AccentColor, Color.white, 0.15f);
                    if (locked)
                    {
                        float g = bg.grayscale;
                        bg = Color.Lerp(bg, new Color(g, g, g), 0.75f) * 0.55f;
                        bg.a = 1f;
                    }
                    card.PortraitBg.color = bg;
                }
                if (card.Tagline != null && def != null)
                {
                    card.Tagline.text = locked ? "Gesperrt: " + LockGoal(card.Class, false) : def.Tagline;
                    card.Tagline.color = locked ? LockedTextColor : _taglineColor;
                }
                if (Stage != null) Stage.SetLocked(card.Class, locked);
            }

            bool selLocked = IsLocked(_selected);
            var sdef = Def(_selected);
            string onRank = " auf " + Progression.RankName(MenuRank);
            if (DetailTagline != null && sdef != null)
            {
                DetailTagline.text = selLocked ? "Gesperrt" + onRank + " – " + LockGoal(_selected) : sdef.Tagline;
                DetailTagline.color = selLocked ? LockedTextColor : _detailTaglineColor;
            }
            if (PlayButton != null)
            {
                PlayButton.interactable = !selLocked;
                if (PlayButton.image != null) PlayButton.image.color = selLocked ? LockedPlayTint : Color.white;
            }
            if (_playLabel != null) _playLabel.text = selLocked ? "Gesperrt" : _playLabelText;
            if (PlayLockHint != null)
            {
                PlayLockHint.gameObject.SetActive(selLocked);
                if (selLocked)
                    PlayLockHint.text = (sdef != null ? sdef.DisplayName : _selected.ToString()) + onRank + " gesperrt – " + LockGoal(_selected);
            }
            // Sperr-Hinweis und Stufen-Beschreibung teilen sich die Zeile über "Spielen"
            if (DifficultyDescription != null) DifficultyDescription.gameObject.SetActive(!(selLocked && PlayLockHint != null));
        }

        // ---------------- Schwierigkeit ----------------

        // Index der gespeicherten Stufe in DifficultySO.All (unbekannt → Normal)
        private static int DifficultyIndex()
        {
            var all = DifficultySO.All;
            var cur = GameSession.Difficulty;
            for (int i = 0; i < all.Count; i++) if (all[i] == cur) return i;
            return 0;
        }

        // Stufe wählen und sofort speichern (gilt für das nächste Spiel); Sperren und Erfolge-Seite folgen der Stufe
        public void SelectDifficulty(int index)
        {
            var all = DifficultySO.All;
            if (all.Count == 0) return;
            index = Mathf.Clamp(index, 0, all.Count - 1);
            if (all[index] == null) return;
            GameSession.Difficulty = all[index];
            RefreshDifficulty();
            RefreshLocks();
            if (AchievementsPanel != null && AchievementsPanel.activeSelf)
            {
                FillAchievements();
                return; // Fokus bleibt auf der Erfolge-Seite
            }
            // Fokus zurück auf "Spielen", damit Enter weiter direkt startet
            if (Application.isPlaying && PlayButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(PlayButton.gameObject);
        }

        // Knöpfe beschriften, gewählte Stufe hervorheben (Holz statt Pergament), Beschreibung füllen
        private void RefreshDifficulty()
        {
            var all = DifficultySO.All;
            var cur = GameSession.Difficulty;
            for (int i = 0; i < DifficultyButtons.Length; i++)
            {
                var btn = DifficultyButtons[i];
                if (btn == null) continue;
                var d = i < all.Count ? all[i] : null;
                btn.gameObject.SetActive(d != null);
                if (d == null) continue;
                bool sel = d == cur;
                if (btn.image != null)
                {
                    var spr = sel ? DifficultySelectedSprite : DifficultyNormalSprite;
                    if (spr != null) btn.image.sprite = spr;
                }
                var ss = btn.spriteState;
                var hover = sel ? DifficultySelectedHoverSprite : DifficultyNormalHoverSprite;
                if (hover != null)
                {
                    ss.highlightedSprite = hover;
                    ss.selectedSprite = hover;
                    ss.pressedSprite = hover;
                    btn.spriteState = ss;
                }
                var label = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.text = d.DisplayName;
                    label.color = sel ? DifficultySelectedTextColor : DifficultyNormalTextColor;
                }
            }
            if (DifficultyDescription != null)
                DifficultyDescription.text = "<b>" + cur.DisplayName + "</b> – " + cur.EffectSummary();
        }

        // ---------------- Erfolge ----------------

        public void ShowAchievements(bool show)
        {
            if (AchievementsPanel != null) AchievementsPanel.SetActive(show);
            if (show)
            {
                FillAchievements();
                if (AchievementsScroll != null) AchievementsScroll.verticalNormalizedPosition = 1f;
            }
            if (!Application.isPlaying || EventSystem.current == null) return;
            if (show && AchievementsBackButton != null) EventSystem.current.SetSelectedGameObject(AchievementsBackButton.gameObject);
            else if (!show && AchievementsButton != null && AchievementsPanel != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // Liste neu aufbauen (Reihenfolge wie in der Datenbank)
        public void FillAchievements()
        {
            foreach (var row in _achievementRows)
            {
                if (row == null) continue;
                row.SetActive(false);
                if (Application.isPlaying) Destroy(row);
                else DestroyImmediate(row);
            }
            _achievementRows.Clear();

            var all = Progression.All;
            int rank = MenuRank;
            if (AchievementsTitle == null && AchievementsPanel != null)
            {
                var ribbon = FindChild<RectTransform>(AchievementsPanel.transform, "Ribbon");
                if (ribbon != null) AchievementsTitle = FindChild<TextMeshProUGUI>(ribbon, "Title");
            }
            if (AchievementsTitle != null) AchievementsTitle.text = "Erfolge – " + Progression.RankName(rank);
            if (AchievementsCounter != null) AchievementsCounter.text = $"{Progression.AchievedCountFor(rank)} / {all.Count}";
            if (AchievementRowTemplate == null || AchievementsContent == null) return;

            var db = AchievementDatabaseSO.Instance;
            foreach (var a in all)
            {
                if (a == null) continue;
                var row = Instantiate(AchievementRowTemplate, AchievementsContent);
                row.name = "Achievement_" + a.Id;
                row.SetActive(true);
                _achievementRows.Add(row);
                FillAchievementRow(row, a, db, rank);
            }
            if (Application.isPlaying) Canvas.ForceUpdateCanvases();
        }

        // Zeile für die Stufe rank: Status/Fortschritt der Stufe, Marker L/N/S für alle Stufen
        private void FillAchievementRow(GameObject row, AchievementDefinition a, AchievementDatabaseSO db, int rank)
        {
            bool done = Progression.IsAchieved(a, rank);
            var t = row.transform;

            var bg = row.GetComponent<Image>();
            if (bg != null)
            {
                if (CardNormalSprite != null && CardSelectedSprite != null) bg.sprite = done ? CardSelectedSprite : CardNormalSprite;
                bg.color = done ? AchievedRowColor : OpenRowColor;
            }

            var icon = FindChild<Image>(t, "MedalIcon");
            if (icon != null)
            {
                icon.sprite = a.Icon;
                icon.enabled = a.Icon != null;
                icon.color = done ? Color.white : LockedIconTint;
            }
            var frame = FindChild<Image>(t, "MedalFrame");
            if (frame != null && db != null)
            {
                Sprite f = done ? db.MedalFrame : (db.MedalFrameLocked != null ? db.MedalFrameLocked : db.MedalFrame);
                if (f != null) frame.sprite = f;
            }

            var title = FindChild<TextMeshProUGUI>(t, "Title");
            if (title != null)
            {
                title.text = a.Title;
            }
            var desc = FindChild<TextMeshProUGUI>(t, "Desc");
            if (desc != null) desc.text = AchievementDatabaseSO.GetConditionText(a);

            int cur, target;
            Progression.GetProgress(a, out cur, out target, rank);
            var fill = FindChild<Image>(t, "BarFill");
            if (fill != null)
            {
                var frt = fill.rectTransform;
                frt.anchorMax = new Vector2(Mathf.Clamp01(target > 0 ? cur / (float)target : 0f), frt.anchorMax.y);
                fill.color = done ? AchievedBarColor : OpenBarColor;
                fill.enabled = cur > 0;
            }
            var progress = FindChild<TextMeshProUGUI>(t, "Progress");
            if (progress != null)
                progress.text = done ? "<color=#3E7A26>Erreicht</color>" : cur.ToString(Inv) + " / " + target.ToString(Inv);

            FillTierMarkers(t, Progression.AchievedRank(a));

            // Belohnung
            var label = FindChild<TextMeshProUGUI>(t, "RewardLabel");
            var name = FindChild<TextMeshProUGUI>(t, "RewardName");
            var rIcon = FindChild<Image>(t, "RewardIcon");
            Sprite rs;
            string rLabel, rName;
            if (a.IsTrophy)
            {
                rs = db != null ? db.TrophyIcon : null;
                rLabel = "Belohnung";
                rName = "Trophäe";
            }
            else
            {
                rs = db != null ? db.GetUnlockIcon(a.Unlock) : null;
                rName = Progression.GetUnlockName(a.Unlock);
                rLabel = "Schaltet frei:";
                if (db != null)
                {
                    // Gemeinsame Freischaltung (z. B. Super-Elementare): Partner-Erfolge nennen
                    var others = new List<string>();
                    foreach (var r in db.GetRequirements(a.Unlock, _reqBuffer))
                        if (r != a) others.Add("„" + r.Title + "“");
                    if (others.Count > 0) rLabel = "Schaltet frei (mit " + string.Join(", ", others.ToArray()) + "):";
                }
            }
            if (label != null) label.text = rLabel;
            if (name != null) name.text = rName;
            if (rIcon != null)
            {
                rIcon.sprite = rs;
                rIcon.enabled = rs != null;
                rIcon.color = done ? Color.white : new Color(0.8f, 0.8f, 0.8f, 1f);
            }
        }

        // Marker Tier1..3 (Leicht/Normal/Schwer): erreicht, wenn der Erfolg auf dieser Stufe oder höher geschafft wurde
        private void FillTierMarkers(Transform row, int achievedRank)
        {
            var tiers = FindChild<RectTransform>(row, "Tiers");
            if (tiers == null) return;
            for (int r = Progression.RankEasy; r <= Progression.MaxRank; r++)
            {
                var marker = tiers.Find("Tier" + r);
                if (marker == null) continue;
                bool on = achievedRank >= r;
                var img = marker.GetComponent<Image>();
                if (img != null) img.color = on ? TierAchievedColor : TierOpenColor;
                var lbl = marker.GetComponentInChildren<TextMeshProUGUI>(true);
                if (lbl != null)
                {
                    string name = Progression.RankName(r);
                    lbl.text = name.Length > 0 ? name.Substring(0, 1) : r.ToString(Inv);
                    lbl.color = on ? TierAchievedTextColor : TierOpenTextColor;
                }
            }
        }

        // ---------------- Buttons ----------------

        public void Play()
        {
            if (_loading || IsLocked(_selected)) return;
            GameSession.SelectedChampion = _selected;
            if (!Application.isPlaying) return;
            _loading = true;
            StartCoroutine(LoadGame());
        }

        private IEnumerator LoadGame()
        {
            if (Fader != null)
            {
                Fader.blocksRaycasts = true;
                for (float t = 0f; t < FadeTime; t += Time.unscaledDeltaTime)
                {
                    Fader.alpha = t / FadeTime;
                    yield return null;
                }
                Fader.alpha = 1f;
            }

            if (Application.CanStreamedLevelBeLoaded(GameSession.GameScene))
            {
                SceneManager.LoadScene(GameSession.GameScene);
            }
            else
            {
#if UNITY_EDITOR
                // Szene nicht in den Build Settings: im Editor trotzdem starten
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    "Assets/" + GameSession.GameScene + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
                Debug.LogError("MainMenuUI: Spielszene '" + GameSession.GameScene + "' fehlt in den Build Settings.");
                _loading = false;
#endif
            }
        }

        public void Quit()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------- Einstellungen ----------------

        public void ShowSettings(bool show)
        {
            if (SettingsPanel != null) SettingsPanel.SetActive(show);
            if (show) SyncSettings();
            if (!Application.isPlaying || EventSystem.current == null) return;
            if (show && MasterSlider != null) EventSystem.current.SetSelectedGameObject(MasterSlider.gameObject);
            else if (!show && SettingsButton != null) EventSystem.current.SetSelectedGameObject(SettingsButton.gameObject);
        }

        private void InitSettings()
        {
            SetupSlider(MasterSlider, MasterValue, v =>
            {
                if (GameAudio.Instance != null) GameAudio.Instance.SetMaster(v);
                else SavePref(GameAudio.PrefMaster, v);
            });
            SetupSlider(MusicSlider, MusicValue, v =>
            {
                if (GameAudio.Instance != null) GameAudio.Instance.SetMusic(v);
                else SavePref(GameAudio.PrefMusic, v);
            });
            SetupSlider(SfxSlider, SfxValue, v =>
            {
                if (GameAudio.Instance != null) GameAudio.Instance.SetSfx(v);
                else SavePref(GameAudio.PrefSfx, v);
            });

            if (FullscreenToggle != null)
            {
                FullscreenToggle.onValueChanged.AddListener(v =>
                {
                    Screen.fullScreen = v;
                    PlayerPrefs.SetInt(PrefFullscreen, v ? 1 : 0);
                    PlayerPrefs.Save();
                });
            }
            SyncSettings();
        }

        private static void SavePref(string key, float v)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(v));
            PlayerPrefs.Save();
        }

        private static void SetupSlider(Slider slider, TextMeshProUGUI label, System.Action<float> apply)
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

        // Regler auf aktuelle Werte setzen (ohne Events); ohne GameAudio direkt aus den PlayerPrefs
        private void SyncSettings()
        {
            var ga = GameAudio.Instance;
            SyncSlider(MasterSlider, MasterValue, ga != null ? ga.MasterVolume : PlayerPrefs.GetFloat(GameAudio.PrefMaster, 1f));
            SyncSlider(MusicSlider, MusicValue, ga != null ? ga.MusicLevel : PlayerPrefs.GetFloat(GameAudio.PrefMusic, 1f));
            SyncSlider(SfxSlider, SfxValue, ga != null ? ga.SfxVolume : PlayerPrefs.GetFloat(GameAudio.PrefSfx, 1f));
            if (FullscreenToggle != null) FullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
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
    }
}
