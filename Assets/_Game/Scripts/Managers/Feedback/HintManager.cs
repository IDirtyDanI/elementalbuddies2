using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Kontext-Hinweise (Plan „Fesselung“ F1): einmalige Pergament-Tipps beim ersten Auftreten einer Situation –
    // Bewegung/Angriff, Bauen, Welle starten, Splitter, Aufwerten, Kartenwahl, Schrein, Fusion, Händler, Boss,
    // wenig Leben, Nexus in Gefahr. Gesehen-Stand in PlayerPrefs („hint_<Id>“), abschaltbar (Pause-Menü
    // „Tipps anzeigen“), nie mehr als einer gleichzeitig (Warteschlange). Rein lokal, auch auf Koop-Clients.
    // Liegt auf dem Managers-Objekt (FeedbackDirector legt ihn an).
    public class HintManager : MonoBehaviour
    {
        public const string PrefEnabled = "hints_enabled";
        private const string PrefPrefix = "hint_";

        [Tooltip("So lange bleibt ein Tipp stehen (s, Echtzeit), wenn er nicht vorher erledigt oder weggeklickt wird.")]
        public float ShowSeconds = 10f;
        [Tooltip("Mindestpause zwischen zwei Tipps (s).")]
        public float Gap = 1.5f;

        public static HintManager Instance { get; private set; }

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
            set { PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0); PlayerPrefs.Save(); if (!value && Instance != null) Instance.HideNow(); }
        }

        private struct Hint { public string Id, Text; public System.Func<bool> Done; }

        private readonly Queue<Hint> _queue = new Queue<Hint>();
        private readonly HashSet<string> _queued = new HashSet<string>();
        private Hint _current;
        private bool _showing;
        private float _shownAt, _nextAllowed;

        private RectTransform _bubble;
        private CanvasGroup _group;
        private TextMeshProUGUI _text;

        private WaveManager _waves;
        private GameManager _game;
        private float _nextPoll;
        private float _startTime;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _waves = WaveManager.Instance;
            _game = GameManager.Instance;
            _startTime = Time.unscaledTime;
            Build();
            if (_waves != null) _waves.OnWaveStart += HandleWaveStart;
            Shrine.OnAnyShrineAwakened += HandleShrine;
            Merchant.OnAnyMerchantActivated += HandleMerchant;
            EnemyBrain.OnBossSpawned += HandleBoss;
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.OnUpgradesAvailable += HandleDraft;
            if (_game != null) _game.OnGameOver += HandleGameOver;
        }

        void OnDisable()
        {
            if (_waves != null) _waves.OnWaveStart -= HandleWaveStart;
            Shrine.OnAnyShrineAwakened -= HandleShrine;
            Merchant.OnAnyMerchantActivated -= HandleMerchant;
            EnemyBrain.OnBossSpawned -= HandleBoss;
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.OnUpgradesAvailable -= HandleDraft;
            if (_game != null) _game.OnGameOver -= HandleGameOver;
        }

        public static bool Seen(string id) => PlayerPrefs.GetInt(PrefPrefix + id, 0) != 0;

        public static void ResetAll()
        {
            foreach (var id in AllIds) PlayerPrefs.DeleteKey(PrefPrefix + id);
            PlayerPrefs.Save();
        }

        private static readonly string[] AllIds =
            { "move", "build", "startwave", "shards", "upgrade", "draft", "shrine", "fusion", "merchant", "boss", "lowhp", "nexus" };

        // Tipp einreihen (einmal pro Spieler); done = Bedingung, bei der er vorzeitig verschwindet
        public void Show(string id, string text, System.Func<bool> done = null)
        {
            if (!Enabled || Seen(id) || _queued.Contains(id) || (_showing && _current.Id == id)) return;
            _queued.Add(id);
            _queue.Enqueue(new Hint { Id = id, Text = text, Done = done });
        }

        // ---------------- Auslöser ----------------

        private void HandleWaveStart()
        {
            if (_waves != null && _waves.UpcomingWaveNumber >= 2 && ElementalBuddy.ActiveCount == 0)
                Show("build", BuildText, () => ElementalBuddy.ActiveCount > 0);
        }

        private void HandleShrine(Shrine s) =>
            Show("shrine", "Ein <b>Schrein</b> ist erwacht! Stell dich in seinen Kreis, bis er eingenommen ist – das schaltet einen neuen Element-Zauber frei.");

        private void HandleMerchant(Merchant m) =>
            Show("merchant", "Ein <b>Händler</b> ist da! Nimm seinen Stand ein wie einen Schrein. Mit Gold kaufst du dort Verbesserungen für deine Fähigkeiten.");

        private void HandleBoss(EnemyBrain e) =>
            Show("boss", "Ein <b>Boss</b>! Rote Flächen am Boden zeigen seine Angriffe – geh raus, bevor sie zuschlagen. Bosse geben viele Splitter.");

        private void HandleDraft(List<UpgradeDefinitionSO> cards) =>
            Show("draft", "Nach jeder Welle wählst du eine <b>Karte</b>. Blau = selten, violett = episch. „Kombo!“ passt zu deinen bisherigen Karten. Gratis-Neuwürfe gibt es für jeden Boss.",
                () => !UpgradeManager.IsLocalChoosing);

        private void HandleGameOver(string reason)
        {
            _queue.Clear();
            HideNow();
        }

        private const string BuildText = "Bau <b>Buddies</b> mit den Tasten <b>1–4</b> (oder unten anklicken) und klick auf den Boden. Sie verteidigen die Stadt für dich.";

        // Zustände, die man nur durch Nachsehen erkennt (alle 0,5 s)
        private void Poll()
        {
            if (_game != null && _game.IsGameOver) return;
            float since = Time.unscaledTime - _startTime;
            bool building = _waves != null && !_waves.IsWaveActive;

            if (since > 2f)
                Show("move", "Willkommen! Bewege dich mit <b>WASD</b>, greif mit der <b>linken Maustaste</b> an. Deine Fähigkeiten liegen auf Rechtsklick, R, F, C und V.");
            if (since > 4f && building && ElementalBuddy.ActiveCount == 0)
                Show("build", BuildText, () => ElementalBuddy.ActiveCount > 0);
            if (building && ElementalBuddy.ActiveCount > 0 && _waves != null && _waves.UpcomingWaveNumber == 1)
                Show("startwave", "Bereit? Starte die <b>Welle</b> unten rechts. Die Vorschau darüber zeigt, was kommt – und je früher du startest, desto mehr Bonus-Splitter.",
                    () => _waves.IsWaveActive);
            if (ShardPickup.All.Count > 0)
                Show("shards", "<b>Seelensplitter!</b> Lauf nah heran, dann fliegen sie zu dir. Damit baust du Buddies und wertest sie auf.");
            if (building && CanAffordUpgrade())
                Show("upgrade", "Klick einen Buddy an, um ihn <b>aufzuwerten</b> – oft besser als noch ein neuer. Aufwerten geht nur zwischen den Wellen.",
                    () => InteractionManager.Instance != null && InteractionManager.Instance.SelectedBuddy != null);
            if (building && FusionPossible())
                Show("fusion", "Zwei Buddies verschiedener Elemente auf <b>Stufe 2</b> nebeneinander können <b>verschmelzen</b>. Wähl einen aus und sieh dir die Fusions-Box an.");
            var me = PlayerAvatar.Local;
            if (me != null && me.IsAlive && me.MaxHealth > 0f && me.Health01 < 0.3f)
                Show("lowhp", "<b>Wenig Leben!</b> Zieh dich kurz zurück – Licht-Buddies heilen dich, und am Wellenende füllt es sich wieder.");
            var nexus = Nexus.Instance;
            if (nexus != null && nexus.MaxHP > 0f && nexus.CurrentHP < nexus.MaxHP * 0.85f)
                Show("nexus", "Der <b>Nexus</b> wird angegriffen! Fällt er, ist der Run vorbei. Bau Buddies an die Wege, auf denen Gegner durchkommen.");
        }

        private static bool CanAffordUpgrade()
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return false;
            foreach (var b in ElementalBuddy.Active)
                if (b != null && b.CanUpgrade && b.NextUpgradeCost > 0f && eco.CurrentShards >= b.NextUpgradeCost) return true;
            return false;
        }

        private static bool FusionPossible()
        {
            var fm = FusionManager.Instance;
            if (fm == null) return false;
            foreach (var b in ElementalBuddy.Active)
                if (b != null && !b.IsFusion && b.Level >= 2 && fm.GetOptions(b).Count > 0) return true;
            return false;
        }

        // ---------------- Anzeige ----------------

        void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextPoll)
            {
                _nextPoll = now + 0.5f;
                if (Enabled) Poll();
            }

            if (_showing)
            {
                bool done = _current.Done != null && SafeDone(_current.Done);
                if (done || now - _shownAt > ShowSeconds) HideNow();
            }
            else if (_queue.Count > 0 && now >= _nextAllowed && Enabled && !PauseManager.IsPaused)
            {
                _current = _queue.Dequeue();
                _queued.Remove(_current.Id);
                if (!Seen(_current.Id)) Present(_current);
            }

            if (_group != null)
            {
                float target = _showing ? 1f : 0f;
                _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 4f);
                _group.blocksRaycasts = _showing;
                float pop = _showing ? Mathf.Lerp(1.06f, 1f, Mathf.Clamp01((now - _shownAt) / 0.25f)) : 1f;
                _bubble.localScale = Vector3.one * pop;
            }
        }

        private static bool SafeDone(System.Func<bool> f)
        {
            try { return f(); } catch { return false; }
        }

        private void Present(Hint h)
        {
            PlayerPrefs.SetInt(PrefPrefix + h.Id, 1);
            PlayerPrefs.Save();
            _showing = true;
            _shownAt = Time.unscaledTime;
            _text.text = "<color=#9a6a12><b>Tipp</b></color>\n" + h.Text;
            GameAudio.Play(GameAudio.Has(SfxId.Hint) ? SfxId.Hint : SfxId.UiClick);
        }

        public void HideNow()
        {
            if (!_showing) return;
            _showing = false;
            _nextAllowed = Time.unscaledTime + Gap;
        }

        private void Build()
        {
            var canvasGo = new GameObject("HintCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 75; // über HUD und Kartenwahl, unter dem Erfolgs-Popup
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Sprite parchment = null;
            var obj = FindFirstObjectByType<ObjectiveUI>(FindObjectsInactive.Include);
            if (obj != null)
                foreach (var img in obj.GetComponentsInChildren<Image>(true))
                    if (img.sprite != null && img.sprite.name.Contains("parchment")) { parchment = img.sprite; break; }
            TMP_FontAsset font = null;
            var hud = FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include);
            if (hud != null && hud.ShardText != null) font = hud.ShardText.font;

            var go = new GameObject("HintBubble", typeof(RectTransform));
            _bubble = (RectTransform)go.transform;
            _bubble.SetParent(canvasGo.transform, false);
            _bubble.anchorMin = _bubble.anchorMax = new Vector2(0f, 0f);
            _bubble.pivot = new Vector2(0f, 0f);
            _bubble.anchoredPosition = new Vector2(24f, 150f);
            _bubble.sizeDelta = new Vector2(560f, 140f);
            var bg = go.AddComponent<Image>();
            bg.sprite = parchment;
            bg.type = parchment != null && parchment.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            bg.color = parchment != null ? Color.white : new Color(0.93f, 0.86f, 0.7f, 0.97f);
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(HideNow); // Klick schließt
            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 20, 22);
            v.childControlHeight = true;
            v.childControlWidth = true;

            _text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _text.transform.SetParent(_bubble, false);
            if (font != null) _text.font = font;
            _text.fontSize = 23f;
            _text.color = new Color(0.23f, 0.14f, 0.07f);
            _text.richText = true;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.raycastTarget = false;
            _text.lineSpacing = 4f;
        }
    }
}
