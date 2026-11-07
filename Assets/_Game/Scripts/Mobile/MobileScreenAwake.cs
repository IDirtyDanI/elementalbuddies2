using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElementalBuddies
{
    // Hält auf Touch-Geräten den Bildschirm wach, solange gespielt wird oder ein Online-Raum offen ist (Koop-Absturz-Fix
    // 2026-11-02, Regeln in ScreenAwakeRules). Merkt sich außerdem, wie lange die App zuletzt im Hintergrund war –
    // NetSession nennt dann beim Verbindungsverlust den echten Grund. PC: TouchMode false → nichts passiert.
    public class MobileScreenAwake : MonoBehaviour
    {
        // Dauer der letzten Hintergrund-Pause und Zeitpunkt der Rückkehr (Echtzeit), -1 = noch nie
        public static float LastPauseSeconds { get; private set; } = -1f;
        private static float _resumedAt = -1f;
        private float _pausedAt = -1f;
        private float _nextCheck;
        private int _applied = int.MinValue;

        public static bool PauseExplainsLoss =>
            _resumedAt >= 0f && ScreenAwakeRules.PauseExplainsLoss(LastPauseSeconds, Time.realtimeSinceStartup - _resumedAt);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { LastPauseSeconds = -1f; _resumedAt = -1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!MobilePlatform.TouchMode) return;
            var go = new GameObject("MobileScreenAwake");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileScreenAwake>();
        }

        void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            var ns = NetSession.Instance;
            bool inGame = SceneManager.GetActiveScene().name != GameSession.MenuScene;
            bool online = ns != null && ns.Mode == NetMode.Online;
            int want = ScreenAwakeRules.KeepAwake(MobilePlatform.TouchMode, inGame, online)
                ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            if (want == _applied) return;
            _applied = want;
            Screen.sleepTimeout = want;
            Debug.Log("[ScreenAwake] Bildschirm " + (want == SleepTimeout.NeverSleep ? "bleibt an" : "nach Systemeinstellung"));
        }

        void OnApplicationPause(bool paused)
        {
            float now = Time.realtimeSinceStartup;
            if (paused) { _pausedAt = now; return; }
            if (_pausedAt < 0f) return;
            LastPauseSeconds = now - _pausedAt;
            _resumedAt = now;
            _pausedAt = -1f;
            Debug.Log($"[ScreenAwake] zurück nach {LastPauseSeconds:0.0} s im Hintergrund");
        }
    }
}
