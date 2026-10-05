using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Erfolgs-Popup oben mittig: Medaille mit Icon, "Erfolg freigeschaltet!", Titel, Belohnungszeile.
    // Fliegt ein, steht DisplayTime Sekunden, blendet aus; mehrere Erfolge laufen nacheinander (Warteschlange).
    // Läuft in Echtzeit (auch bei Pause). Liegt als Prefab unter Resources/AchievementPopup (eigenes Overlay-Canvas)
    // und wird vom AchievementManager bei Bedarf erzeugt (EnsureInstance) – die Spielszene braucht kein eigenes Objekt.
    // Prefab bauen: Menü BuddyTD → Erfolge → Popup-Prefab bauen.
    public class AchievementPopupUI : MonoBehaviour
    {
        public const string ResourcePath = "AchievementPopup";

        public static AchievementPopupUI Instance { get; private set; }

        [Header("Aufbau")]
        [Tooltip("Ein-/Ausblenden (Alpha).")]
        public CanvasGroup Group;
        [Tooltip("Bewegter Teil (oben mittig verankert, Pivot oben).")]
        public RectTransform Panel;
        public Image MedalIcon;
        public Image MedalFrame;
        public TextMeshProUGUI Header;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Reward;
        [Tooltip("Icon der Freischaltung bzw. Pokal bei Trophäen.")]
        public Image RewardIcon;

        [Header("Lage")]
        [Tooltip("Abstand vom oberen Rand (Canvas-Einheiten), wenn keine Boss-Leiste sichtbar ist.")]
        public float TopOffset = 222f;
        [Tooltip("Abstand unter einer sichtbaren Boss-HP-Leiste.")]
        public float BossBarGap = 12f;

        [Header("Timing (Echtzeit)")]
        public float DisplayTime = 4f;
        public float InTime = 0.35f;
        public float OutTime = 0.45f;
        public float GapTime = 0.2f;
        [Tooltip("Einflug-Strecke von oben.")]
        public float SlideDistance = 70f;

        private static readonly Queue<AchievementDefinition> _queue = new Queue<AchievementDefinition>();
        private Coroutine _routine;
        private Canvas _canvas;
        private BossHealthBarUI _bossBar;
        private bool _bossBarSearched;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _queue.Clear();
            Instance = null;
        }

        // Popup aus Resources erzeugen, falls die Szene keins hat. null, wenn das Prefab fehlt.
        public static AchievementPopupUI EnsureInstance()
        {
            if (Instance != null) return Instance;
            Instance = FindFirstObjectByType<AchievementPopupUI>(FindObjectsInactive.Include);
            if (Instance != null) return Instance;
            var prefab = Resources.Load<AchievementPopupUI>(ResourcePath);
            if (prefab == null) return null;
            var inst = Instantiate(prefab);
            inst.name = prefab.name;
            return Instance = inst;
        }

        public static bool Show(AchievementDefinition a)
        {
            if (a == null) return false;
            var ui = EnsureInstance();
            if (ui == null) return false;
            _queue.Enqueue(a);
            ui.Pump();
            return true;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _canvas = GetComponentInParent<Canvas>();
            SetVisible(0f);
        }

        void OnEnable()
        {
            if (Instance == this) Pump();
        }

        void OnDisable()
        {
            _routine = null;
            SetVisible(0f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Pump()
        {
            if (_routine == null && _queue.Count > 0 && isActiveAndEnabled)
                _routine = StartCoroutine(ShowRoutine());
        }

        private void SetVisible(float alpha)
        {
            if (Group == null) return;
            Group.alpha = alpha;
            Group.blocksRaycasts = false;
            Group.interactable = false;
        }

        private IEnumerator ShowRoutine()
        {
            while (_queue.Count > 0)
            {
                // Game Over: das Game-Over-Panel listet die Erfolge selbst → Popup nicht über die Banderole legen
                if (IsGameOver)
                {
                    _queue.Clear();
                    break;
                }
                var a = _queue.Dequeue();
                Fill(a);
                float top = CurrentTopOffset();

                // Einflug: von oben herein, leichtes Überschwingen
                for (float t = 0f; t < InTime; t += Time.unscaledDeltaTime)
                {
                    float k = InTime > 0f ? t / InTime : 1f;
                    float e = EaseOutBack(k);
                    Place(top, (1f - e) * SlideDistance, Mathf.Lerp(0.85f, 1f, e));
                    SetVisible(Mathf.Clamp01(k * 1.6f));
                    yield return null;
                }
                Place(top, 0f, 1f);
                SetVisible(1f);

                float end = Time.unscaledTime + DisplayTime;
                while (Time.unscaledTime < end && !IsGameOver)
                {
                    // Boss-Leiste kann während der Anzeige erscheinen → ausweichen
                    float want = CurrentTopOffset();
                    top = Mathf.MoveTowards(top, want, 600f * Time.unscaledDeltaTime);
                    Place(top, 0f, 1f);
                    yield return null;
                }

                for (float t = 0f; t < OutTime; t += Time.unscaledDeltaTime)
                {
                    float k = OutTime > 0f ? t / OutTime : 1f;
                    Place(top, -k * SlideDistance * 0.3f, 1f);
                    SetVisible(1f - k);
                    yield return null;
                }
                SetVisible(0f);

                float gapEnd = Time.unscaledTime + GapTime;
                while (Time.unscaledTime < gapEnd) yield return null;
            }
            _routine = null;
        }

        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.IsGameOver;

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.4f, c3 = c1 + 1f;
            x = Mathf.Clamp01(x);
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        private void Place(float top, float lift, float scale)
        {
            if (Panel == null) return;
            Panel.anchoredPosition = new Vector2(0f, -top + lift);
            Panel.localScale = Vector3.one * scale;
        }

        // Unter der Boss-HP-Leiste bleiben, wenn sie sichtbar ist
        private float CurrentTopOffset()
        {
            float top = TopOffset;
            if (!_bossBarSearched || _bossBar == null)
            {
                _bossBarSearched = true;
                _bossBar = FindFirstObjectByType<BossHealthBarUI>(FindObjectsInactive.Include);
            }
            if (_bossBar == null || _bossBar.Root == null || !_bossBar.Root.activeInHierarchy) return top;
            var rt = _bossBar.Root.transform as RectTransform;
            if (rt == null) return top;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var bossCanvas = rt.GetComponentInParent<Canvas>();
            Camera cam = bossCanvas != null && bossCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? bossCanvas.worldCamera : null;
            float bottomPx = RectTransformUtility.WorldToScreenPoint(cam, corners[0]).y;
            float scale = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
            float below = (Screen.height - bottomPx) / scale + BossBarGap;
            return Mathf.Max(top, below);
        }

        private void Fill(AchievementDefinition a)
        {
            var db = AchievementDatabaseSO.Instance;
            if (MedalIcon != null)
            {
                MedalIcon.sprite = a.Icon;
                MedalIcon.enabled = a.Icon != null;
            }
            if (MedalFrame != null && db != null && db.MedalFrame != null) MedalFrame.sprite = db.MedalFrame;
            if (Header != null) Header.text = "Erfolg freigeschaltet!";
            if (Title != null) Title.text = a.Title;

            Sprite icon;
            if (Reward != null) Reward.text = RewardText(a, out icon);
            else RewardText(a, out icon);
            if (RewardIcon != null)
            {
                RewardIcon.sprite = icon;
                RewardIcon.gameObject.SetActive(icon != null);
            }
        }

        // "Ab dem nächsten Spiel: Schwertkämpfer" · Teil-Freischaltung · "Trophäe"
        public static string RewardText(AchievementDefinition a, out Sprite icon)
        {
            var db = AchievementDatabaseSO.Instance;
            if (a == null)
            {
                icon = null;
                return "";
            }
            if (a.IsTrophy)
            {
                icon = db != null ? db.TrophyIcon : null;
                return "Trophäe";
            }
            icon = db != null ? db.GetUnlockIcon(a.Unlock) : null;
            string name = Progression.GetUnlockName(a.Unlock);
            if (Progression.IsUnlockedPersistent(a.Unlock)) return $"Ab dem nächsten Spiel: <b>{name}</b>";
            return $"Teil von „{name}“ – {Progression.GoalText(a.Unlock)}";
        }
    }
}
