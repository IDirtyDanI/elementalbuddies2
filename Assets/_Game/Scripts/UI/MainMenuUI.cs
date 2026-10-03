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
    // Spielen / Einstellungen / Beenden. Die Hierarchie baut der Editor-Builder (BuddyTD/Hauptmenü/Szene bauen).
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
                InitSettings();
            }

            EnsureClickable();
            if (AbilityRowTemplate != null) AbilityRowTemplate.SetActive(false);
            if (Fader != null)
            {
                Fader.alpha = 0f;
                Fader.blocksRaycasts = false;
            }
            FillCards();
            if (Stage != null) Stage.SpawnPreviews(Champions);
            ShowSettings(false);
            Select(GameSession.SelectedChampion, true);
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

            // Enter startet direkt – unabhängig davon, ob der Spielen-Button gerade ausgewählt ist
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                Play();
                return;
            }

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

        // ---------------- Buttons ----------------

        public void Play()
        {
            if (_loading) return;
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
