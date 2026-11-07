using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Freiwillige Werbung (Rewarded Ads, Plan Werbung-Plan 2026-11-02). Nur Mobil (Touch-Modus), nur Einzelspiel, nur auf
    // Knopfdruck, Deckel pro Spiel aus AdRules. Ohne Anbieter (PC) gibt es keine Instanz → keine Knöpfe, alles wie bisher.
    // Anbieter: auf Android/iOS LevelPlay (LevelPlayAds, startet nach der Einwilligung AdConsent); TestVideo im Editor und
    // im Development-Build, wenn die Datei „testvideo“ in persistentDataPath liegt (adb push, Test-Fernsteuerung), und
    // immer im Tester-Build (Define EB_TESTVIDEO).
    // Während des Videos: Spiel angehalten, Ton stumm (die Musik ignoriert AudioListener.pause, deshalb volume = 0).
    public class RewardedAds : MonoBehaviour
    {
        // Werbe-Anbieter je Belohnung (eigene Ad-Unit): bereit? und Video zeigen → true = bis zum Ende geschaut
        public interface IProvider
        {
            string Name { get; }
            bool IsReady(AdReward r);
            void Show(AdReward r, Action<bool> done);
        }

        public static RewardedAds Instance { get; private set; }
        public static bool Enabled => Instance != null;
        // Gerade läuft ein Video
        public static bool IsShowing => Instance != null && Instance._showing;

        private static readonly AdRunCounter Run = new AdRunCounter();
        private IProvider _provider;
        private bool _showing;
        private float _prevTimeScale = 1f, _prevVolume = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null || !MobilePlatform.TouchMode) return;
            bool testVideo = Application.isEditor || (Debug.isDebugBuild &&
                System.IO.File.Exists(System.IO.Path.Combine(Application.persistentDataPath, "testvideo")));
#if EB_TESTVIDEO
            // Tester-Build (Build-Option extraScriptingDefines): nie echte Werbung – fremde Geräte sind keine
            // LevelPlay-Testgeräte, Klicks auf echte Anzeigen vor dem Release können das Werbekonto gefährden
            testVideo = true;
#endif
            IProvider provider = testVideo ? new TestVideo() : LevelPlayAds.Create();
            if (provider == null) return;
            var go = new GameObject("RewardedAds");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RewardedAds>();
            Instance._provider = provider;
            if (provider is TestVideo tv) tv.Host = Instance;
            // LevelPlay erst nach der Einwilligung starten (erster Start: Frage; danach gespeicherte Antwort)
            else AdConsent.Ensure(consent => LevelPlayAds.Start(Instance, provider, consent));
            SceneManager.sceneLoaded += (s, m) => Run.Reset(); // neues Spiel (auch Neustart) = neue Deckel
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Run.Reset();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public static int Remaining(AdReward r) => Run.Remaining(r);
        // Anbieter für Anzeige/Test-Fernsteuerung („Testvideo“, „LevelPlay“, „LevelPlay (startet …)“)
        public static string ProviderName => Instance != null && Instance._provider != null ? Instance._provider.Name : "keiner";

        // Jetzt anbieten? (Video geladen, Deckel frei, Einzelspiel, Bedingungen aus AdRules)
        public static bool CanOffer(AdReward r)
        {
            if (Instance == null || Instance._showing || Instance._provider == null || !Instance._provider.IsReady(r)) return false;
            int completed = WaveManager.Instance != null ? WaveManager.Instance.CurrentWaveIndex : 0;
            bool atCap = BuddySlotManager.Instance != null && BuddySlotManager.Instance.IsAtCap;
            return AdRules.CanOffer(r, Run.Used(r), Net.IsMultiplayer, completed, atCap);
        }

        // Video zeigen; onRewarded nur, wenn bis zum Ende geschaut. Es zählt gegen den Deckel, wenn onRewarded die
        // Belohnung tatsächlich vergeben konnte (true)
        public static bool Show(AdReward r, Func<bool> onRewarded, Action onNoReward = null)
        {
            if (!CanOffer(r)) return false;
            var me = Instance;
            me._showing = true;
            me._prevTimeScale = Time.timeScale;
            me._prevVolume = AudioListener.volume;
            Time.timeScale = 0f;
            AudioListener.volume = 0f;
            me._provider.Show(r, ok =>
            {
                me._showing = false;
                Time.timeScale = me._prevTimeScale;
                AudioListener.volume = me._prevVolume;
                bool granted = ok && onRewarded != null && onRewarded();
                if (granted) Run.Use(r);
                Debug.Log($"[RewardedAds] {r}: {(granted ? "belohnt" : ok ? "geschaut, aber nicht mehr vergeben" : "keine Belohnung")} (übrig {Run.Remaining(r)})");
                if (!ok) onNoReward?.Invoke();
            });
            return true;
        }

        // ---------------- Angebote (Knöpfe, Test-Fernsteuerung) ----------------

        public static bool OfferReroll()
        {
            var um = UpgradeManager.Instance;
            if (um == null || !UpgradeManager.IsLocalChoosing) return false;
            return Show(AdReward.Reroll, () => UpgradeManager.Instance != null && UpgradeManager.Instance.RequestAdReward(AdReward.Reroll));
        }

        public static bool OfferBuddySlot()
        {
            var um = UpgradeManager.Instance;
            if (um == null || !UpgradeManager.IsLocalChoosing) return false;
            return Show(AdReward.BuddySlot, () => UpgradeManager.Instance != null && UpgradeManager.Instance.RequestAdReward(AdReward.BuddySlot));
        }

        public static bool OfferContinue()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsContinueOffered) return false;
            return Show(AdReward.Continue, () => GameManager.Instance != null && GameManager.Instance.AcceptContinue());
        }

        // ---------------- Test-Video (Development-Build, bis LevelPlay angebunden ist) ----------------

        // Vollbild „Werbung (Test)“ mit Countdown; „Abbrechen“ = keine Belohnung (wie ein vorzeitig geschlossenes Video).
        // NextResult (Test-Fernsteuerung „ad ok|fail|cancel“): fail = Video lädt nicht (keine Knöpfe), cancel = abgebrochen.
        public sealed class TestVideo : IProvider
        {
            public enum Result { Ok, Fail, Cancel }
            public static Result NextResult = Result.Ok;
            public const float Seconds = 3f;

            public MonoBehaviour Host;
            public string Name => "Testvideo";
            public bool IsReady(AdReward r) => NextResult != Result.Fail;

            public void Show(AdReward r, Action<bool> done)
            {
                if (Host == null) { done(false); return; }
                Host.StartCoroutine(Play(done));
            }

            private System.Collections.IEnumerator Play(Action<bool> done)
            {
                var canvasGo = new GameObject("TestVideo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(canvasGo);
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);

                var bg = new GameObject("Bg", typeof(Image));
                bg.transform.SetParent(canvasGo.transform, false);
                var bgRt = (RectTransform)bg.transform;
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
                bg.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.97f);

                var label = NewText(canvasGo.transform, new Vector2(0f, 60f), 64f);
                bool cancelled = false;
                var btnGo = new GameObject("Abbrechen", typeof(Image), typeof(Button));
                btnGo.transform.SetParent(canvasGo.transform, false);
                var brt = (RectTransform)btnGo.transform;
                brt.sizeDelta = new Vector2(360f, 90f);
                brt.anchoredPosition = new Vector2(0f, -160f);
                btnGo.GetComponent<Image>().color = new Color(0.35f, 0.2f, 0.12f);
                btnGo.GetComponent<Button>().onClick.AddListener(() => cancelled = true);
                NewText(btnGo.transform, Vector2.zero, 40f).text = "Abbrechen";

                bool forcedCancel = NextResult == Result.Cancel;
                float end = Time.unscaledTime + Seconds;
                while (Time.unscaledTime < end && !cancelled)
                {
                    label.text = $"Werbung (Testvideo)\n{Mathf.CeilToInt(end - Time.unscaledTime)}";
                    if (forcedCancel && end - Time.unscaledTime < Seconds * 0.5f) cancelled = true;
                    yield return null;
                }
                UnityEngine.Object.Destroy(canvasGo);
                done(!cancelled);
            }

            private static TextMeshProUGUI NewText(Transform parent, Vector2 pos, float size)
            {
                var go = new GameObject("Text", typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(1200f, 300f);
                rt.anchoredPosition = pos;
                var t = go.GetComponent<TextMeshProUGUI>();
                t.alignment = TextAlignmentOptions.Center;
                t.fontSize = size;
                t.color = Color.white;
                t.raycastTarget = false;
                return t;
            }
        }
    }
}
