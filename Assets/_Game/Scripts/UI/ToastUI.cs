using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Kurze Meldungen oben in der Bildschirmmitte (Warteschlange, Ein-/Ausblenden über CanvasGroup).
    // ToastUI.Show("...") von überall; Meldungen vor dem Start der UI werden gepuffert.
    // AnnounceGameEvents: meldet Portal-Öffnungen und Schrein-Ereignisse selbst.
    public class ToastUI : MonoBehaviour
    {
        public static ToastUI Instance { get; private set; }

        [Tooltip("CanvasGroup des Toast-Panels (wird ein-/ausgeblendet).")]
        public CanvasGroup Root;
        public TextMeshProUGUI Text;
        [Tooltip("Optional: Icon links neben dem Text (wird ausgeblendet, wenn kein Sprite übergeben wird).")]
        public Image Icon;

        [Header("Timing (Echtzeit, läuft auch bei Pause)")]
        public float DisplayTime = 3f;
        public float FadeTime = 0.25f;
        public float GapTime = 0.1f;

        [Header("Automatische Meldungen")]
        public bool AnnounceGameEvents = true;
        [Tooltip("Optional: Element-Icons (Feuer, Eis, Erde, Licht) für Schrein-Meldungen.")]
        public Sprite[] ElementIcons = new Sprite[4];
        public Sprite PortalIcon;

        private struct Toast
        {
            public string Message;
            public Sprite Icon;
        }

        private static readonly Queue<Toast> _queue = new Queue<Toast>();
        private Coroutine _routine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _queue.Clear();

        public static void Show(string msg, Sprite icon = null)
        {
            if (string.IsNullOrEmpty(msg)) return;
            _queue.Enqueue(new Toast { Message = msg, Icon = icon });
            if (Instance != null) Instance.Pump();
            else Debug.Log($"Toast (no ToastUI yet): {msg}");
        }

        void Awake()
        {
            Instance = this;
            if (Root != null)
            {
                Root.alpha = 0f;
                Root.blocksRaycasts = false;
                Root.interactable = false;
            }
        }

        void Start()
        {
            if (AnnounceGameEvents)
            {
                WaveManager.OnPortalOpened += HandlePortalOpened;
                Shrine.OnAnyShrineAwakened += HandleShrineAwakened;
                Shrine.OnAnyShrineCompleted += HandleShrineCompleted;
                Shrine.OnAnyShrineFailed += HandleShrineFailed;
                Merchant.OnAnyMerchantActivated += HandleMerchantActivated;
                Merchant.OnAnyMerchantCaptured += HandleMerchantCaptured;
                Merchant.OnAnyMerchantFailed += HandleMerchantFailed;
                if (MerchantManager.Instance != null) MerchantManager.Instance.OnCardPicked += HandleMerchantCardPicked;
            }
            Pump();
        }

        void OnDestroy()
        {
            WaveManager.OnPortalOpened -= HandlePortalOpened;
            Shrine.OnAnyShrineAwakened -= HandleShrineAwakened;
            Shrine.OnAnyShrineCompleted -= HandleShrineCompleted;
            Shrine.OnAnyShrineFailed -= HandleShrineFailed;
            Merchant.OnAnyMerchantActivated -= HandleMerchantActivated;
            Merchant.OnAnyMerchantCaptured -= HandleMerchantCaptured;
            Merchant.OnAnyMerchantFailed -= HandleMerchantFailed;
            if (MerchantManager.Instance != null) MerchantManager.Instance.OnCardPicked -= HandleMerchantCardPicked;
            if (Instance == this) Instance = null;
        }

        void OnDisable()
        {
            // Coroutine stirbt mit dem Deaktivieren → beim nächsten Pump neu starten
            _routine = null;
            if (Root != null) Root.alpha = 0f;
        }

        void OnEnable()
        {
            if (Instance == this) Pump();
        }

        private void Pump()
        {
            if (_routine == null && _queue.Count > 0 && isActiveAndEnabled)
                _routine = StartCoroutine(ShowRoutine());
        }

        private IEnumerator ShowRoutine()
        {
            while (_queue.Count > 0)
            {
                Toast t = _queue.Dequeue();
                if (Text != null) Text.text = t.Message;
                if (Icon != null)
                {
                    Icon.sprite = t.Icon;
                    Icon.gameObject.SetActive(t.Icon != null);
                }
                FitRibbon(t.Icon != null && Icon != null);

                yield return Fade(0f, 1f);
                float end = Time.unscaledTime + DisplayTime;
                while (Time.unscaledTime < end) yield return null;
                yield return Fade(1f, 0f);

                float gapEnd = Time.unscaledTime + GapTime;
                while (Time.unscaledTime < gapEnd) yield return null;
            }
            _routine = null;
        }

        // ---------------- Banner-Breite ----------------

        [Header("Banner-Breite")]
        [Tooltip("Abstand Text ↔ Bannerende (Schwalbenschwänze), Canvas-Einheiten.")]
        public float TextInset = 100f;
        [Tooltip("Mindestabstand des Banners zum Bildschirmrand (je Seite), Canvas-Einheiten.")]
        public float ScreenMargin = 60f;

        private bool _fitInit;
        private RectTransform _ribbon, _objective;
        private static readonly Vector3[] _corners = new Vector3[4];
        private float _ribbonBaseWidth, _baseFont;

        // Banner wächst mit dem Text (bis zur Bildschirmbreite), danach verkleinert TMP-Auto-Size die Schrift.
        // Mit Icon rückt der Text rechts neben das Icon (beidseitig gleich, damit er mittig bleibt).
        // Früher feste 820er-Breite ohne Umbruch: lange Meldungen (Händler „… vor der Nordmauer geöffnet!“) ragten
        // links und rechts aus dem Banner, und der Text lag unter dem Icon (Bekannte-Probleme #158).
        private void FitRibbon(bool hasIcon)
        {
            if (Text == null) return;
            if (!_fitInit)
            {
                _fitInit = true;
                _ribbon = Root != null ? Root.transform as RectTransform : Text.rectTransform.parent as RectTransform;
                if (_ribbon != null) _ribbonBaseWidth = _ribbon.sizeDelta.x;
                _baseFont = Text.fontSize;
                Text.textWrappingMode = TextWrappingModes.NoWrap;
                Text.overflowMode = TextOverflowModes.Ellipsis;
                Text.fontSizeMax = _baseFont;
                Text.fontSizeMin = Mathf.Round(_baseFont * 0.65f);
                // Ziel-Panels oben rechts (Schrein/Händler) – erscheinen meist zusammen mit der Meldung
                var obj = FindFirstObjectByType<ObjectiveUI>(FindObjectsInactive.Include);
                if (obj != null) _objective = obj.transform as RectTransform;
            }
            if (_ribbon == null) return;

            float inset = TextInset;
            if (hasIcon)
            {
                var irt = Icon.rectTransform;
                float iconRight = irt.anchoredPosition.x + (1f - irt.pivot.x) * irt.sizeDelta.x; // Icon links verankert
                inset = Mathf.Max(inset, iconRight + 14f);
            }

            Text.enableAutoSizing = false;
            Text.fontSize = _baseFont;
            float pref = Text.GetPreferredValues(Text.text, Mathf.Infinity, Mathf.Infinity).x;
            var parent = _ribbon.parent as RectTransform;
            float maxW = parent != null ? Mathf.Max(_ribbonBaseWidth, parent.rect.width - 2f * ScreenMargin) : float.MaxValue;
            if (parent != null && _objective != null)
            {
                // symmetrisch um die Mitte, rechts nur bis vor die Ziel-Panels (sonst überdeckt das Banner sie bei 16:10/4:3)
                _objective.GetWorldCorners(_corners);
                float objLeft = parent.InverseTransformPoint(_corners[0]).x;
                float centerX = parent.InverseTransformPoint(_ribbon.position).x;
                maxW = Mathf.Max(_ribbonBaseWidth, Mathf.Min(maxW, 2f * (objLeft - centerX - 16f)));
            }
            var minimap = MinimapUI.Instance;
            if (parent != null && minimap != null && minimap.IsShown && minimap.Rect != null)
            {
                // ebenso links nur bis neben die Minimap (oben links unter der TopBar, gleiche Höhe wie das Banner)
                minimap.Rect.GetWorldCorners(_corners);
                float mapRight = parent.InverseTransformPoint(_corners[2]).x;
                float centerX = parent.InverseTransformPoint(_ribbon.position).x;
                maxW = Mathf.Max(_ribbonBaseWidth, Mathf.Min(maxW, 2f * (centerX - mapRight - 16f)));
            }
            float width = Mathf.Clamp(pref + 2f * inset + 8f, _ribbonBaseWidth, maxW);
            Text.enableAutoSizing = pref + 2f * inset + 8f > width; // nur wenn der Bildschirm nicht reicht
            _ribbon.sizeDelta = new Vector2(width, _ribbon.sizeDelta.y);

            var trt = Text.rectTransform;
            trt.offsetMin = new Vector2(inset, trt.offsetMin.y);
            trt.offsetMax = new Vector2(-inset, trt.offsetMax.y);
        }

        private IEnumerator Fade(float from, float to)
        {
            if (Root == null) yield break;
            float t = 0f;
            while (t < FadeTime)
            {
                t += Time.unscaledDeltaTime;
                Root.alpha = Mathf.Lerp(from, to, FadeTime > 0f ? t / FadeTime : 1f);
                yield return null;
            }
            Root.alpha = to;
        }

        // ---------------- Automatische Meldungen ----------------

        private Sprite ElementIcon(int idx) =>
            ElementIcons != null && idx >= 0 && idx < ElementIcons.Length ? ElementIcons[idx] : null;

        private void HandlePortalOpened(SpawnPortal p)
        {
            Show($"Ein neues Portal öffnet sich im {p.DisplayName}!", PortalIcon);
        }

        private void HandleShrineAwakened(Shrine s)
        {
            Show($"Der {s.DisplayName} ist erwacht! Halte seinen Kreis.", ElementIcon(s.ElementIndex));
        }

        private void HandleShrineCompleted(Shrine s)
        {
            int i = s.ElementIndex;
            if (s.AbilityUnlocked)
            {
                // Name der Element-Fähigkeit des aktiven Champions (Magier: Flammenwelle, Ritter: Flammenwirbel …)
                var av = PlayerAvatar.Local;
                var pa = av != null && av.Abilities != null ? av.Abilities : PlayerAbilities.Instance;
                string ability = pa != null ? pa.GetElementAbilityName(i) : ElementInfo.AbilityName(i);
                Show($"{ability} erlernt! ({ElementInfo.AbilityKey(i)})", ElementIcon(i));
            }
            Show($"{ElementInfo.Name(i)}-Buddies: +{Mathf.RoundToInt(s.BuddyDamageBonus * 100f)} % Schaden", ElementIcon(i));
        }

        private void HandleShrineFailed(Shrine s)
        {
            Show($"Der {s.DisplayName} ist wieder verfallen...", ElementIcon(s.ElementIndex));
        }

        private static Sprite MerchantIcon(Merchant m) =>
            MerchantManager.Instance != null && m != null ? MerchantManager.Instance.GetEmblem(m.Kind) : null;

        private void HandleMerchantActivated(Merchant m)
        {
            Show($"Der {m.DisplayName} hat seinen Stand {m.LocationPhrase} geöffnet! Nimm ihn ein.", MerchantIcon(m));
        }

        private void HandleMerchantCaptured(Merchant m)
        {
            Show($"{m.DisplayName} eingenommen!", MerchantIcon(m));
        }

        private void HandleMerchantFailed(Merchant m)
        {
            Show($"Der {m.DisplayName} hat seinen Stand wieder geschlossen...", MerchantIcon(m));
        }

        private void HandleMerchantCardPicked(MerchantCardSO card)
        {
            var mm = MerchantManager.Instance;
            int mult = mm != null ? mm.LastPickMultiplier : 1;
            Show(mult > 1
                ? $"Gekauft: {card.Title} ({card.ValueTextTimes(mult)}, ×{mult} durch Fluch)"
                : $"Gekauft: {card.Title} ({card.ValueText})", card.Icon);
        }
    }
}
