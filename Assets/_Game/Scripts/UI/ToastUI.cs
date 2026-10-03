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
            }
            Pump();
        }

        void OnDestroy()
        {
            WaveManager.OnPortalOpened -= HandlePortalOpened;
            Shrine.OnAnyShrineAwakened -= HandleShrineAwakened;
            Shrine.OnAnyShrineCompleted -= HandleShrineCompleted;
            Shrine.OnAnyShrineFailed -= HandleShrineFailed;
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

                yield return Fade(0f, 1f);
                float end = Time.unscaledTime + DisplayTime;
                while (Time.unscaledTime < end) yield return null;
                yield return Fade(1f, 0f);

                float gapEnd = Time.unscaledTime + GapTime;
                while (Time.unscaledTime < gapEnd) yield return null;
            }
            _routine = null;
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
                Show($"{ElementInfo.AbilityName(i)} erlernt! (Taste {ElementInfo.AbilityKey(i)})", ElementIcon(i));
            Show($"{ElementInfo.Name(i)}-Buddies: +{Mathf.RoundToInt(s.BuddyDamageBonus * 100f)} % Schaden", ElementIcon(i));
        }

        private void HandleShrineFailed(Shrine s)
        {
            Show($"Der {s.DisplayName} ist wieder verfallen...", ElementIcon(s.ElementIndex));
        }
    }
}
