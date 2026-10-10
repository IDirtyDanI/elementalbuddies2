using UnityEngine;

namespace ElementalBuddies
{
    // Treffer-Stopp (Hitstop) und Zeitlupe über Time.timeScale – nur im Einzelspiel (Net.CanPauseTime).
    // Greift nur, wenn die Zeit gerade normal läuft (nicht während Pause, Draft oder Laden). Setzt jemand anderes
    // timeScale während eines Effekts um, gibt TimeWarp die Kontrolle ab und stellt nichts zurück.
    public class TimeWarp : MonoBehaviour
    {
        private static TimeWarp _instance;
        private static float _until, _scale = 1f, _nextHitStop;
        private static bool _active;

        public static bool IsActive => _active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _active = false;
            _scale = 1f;
            _until = _nextHitStop = 0f;
        }

        private static bool Allowed => GameFeel.HitStop && Net.CanPauseTime && !PauseManager.IsPaused;

        // Kurzes Einfrieren (fast Stillstand), gedrosselt damit Mehrfachtreffer nicht stottern
        public static void HitStop(float seconds)
        {
            if (!Allowed || Time.unscaledTime < _nextHitStop) return;
            if (_active && _scale > 0.1f) return; // läuft eine Zeitlupe, nicht dazwischenfrieren
            _nextHitStop = Time.unscaledTime + seconds + 0.12f;
            Apply(0.04f, seconds, false);
        }

        // Zeitlupe für große Momente (Boss-Tod)
        public static void SlowMo(float scale, float seconds)
        {
            if (!Allowed) return;
            Apply(Mathf.Clamp(scale, 0.05f, 1f), seconds, true);
        }

        // Laufenden Effekt sofort beenden (z. B. bevor die Pause timeScale sichert)
        public static void Cancel()
        {
            if (!_active) return;
            _active = false;
            if (Mathf.Approximately(Time.timeScale, _scale)) Time.timeScale = 1f;
        }

        // replace: Zeitlupe ersetzt einen laufenden Treffer-Stopp (sonst gilt der stärkere Effekt)
        private static void Apply(float scale, float seconds, bool replace)
        {
            if (_active)
            {
                if (!Mathf.Approximately(Time.timeScale, _scale)) { _active = false; return; }
                _scale = replace ? scale : Mathf.Min(_scale, scale);
                _until = Mathf.Max(_until, Time.unscaledTime + seconds);
            }
            else
            {
                if (!Mathf.Approximately(Time.timeScale, 1f)) return; // Pause/Draft/Game Over: nicht eingreifen
                _scale = scale;
                _until = Time.unscaledTime + seconds;
                _active = true;
            }
            Time.timeScale = _scale;
            EnsureRunner();
        }

        private static void EnsureRunner()
        {
            if (_instance != null) return;
            var go = new GameObject("TimeWarp");
            go.hideFlags = HideFlags.HideInHierarchy;
            _instance = go.AddComponent<TimeWarp>();
        }

        void Update()
        {
            if (!_active) return;
            // Jemand anderes (Pause, Draft, Game Over) hat übernommen
            if (!Mathf.Approximately(Time.timeScale, _scale)) { _active = false; return; }
            if (Time.unscaledTime >= _until)
            {
                _active = false;
                Time.timeScale = 1f;
            }
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                Cancel();
                _instance = null;
            }
        }
    }
}
