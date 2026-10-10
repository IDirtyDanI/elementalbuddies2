using UnityEngine;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    // Pause per ESC (Solo: Zeit + Audio anhalten; Koop: nur lokales Overlay). Liegt auf dem Managers-Objekt; UI hängt an OnPauseChanged.
    public class PauseManager : MonoBehaviour
    {
        public static PauseManager Instance { get; private set; }
        public static bool IsPaused { get; private set; }

        public event System.Action<bool> OnPauseChanged;

        private float _prevTimeScale = 1f;
        private bool _frozeTime; // Pause hat Time.timeScale angehalten (nur Solo)

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            IsPaused = false;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            // Szenen-Neustart: Statics zurücksetzen
            IsPaused = false;
            AudioListener.pause = false;
        }

        // LateUpdate: InteractionManager hat ESC (Ghost/Auswahl) in Update schon verarbeitet
        void LateUpdate()
        {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            if (InteractionManager.Instance != null && InteractionManager.Instance.EscapeConsumedThisFrame) return;
            TogglePause();
        }

        public void TogglePause()
        {
            if (IsPaused) Resume();
            else Pause();
        }

        // Solo: Zeit + Audio anhalten. Koop (!Net.CanPauseTime): nur lokales Overlay – das Spiel läuft weiter,
        // die eigene Eingabe ist über IsPaused gesperrt.
        public void Pause()
        {
            if (IsPaused) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            _frozeTime = Net.CanPauseTime;
            if (_frozeTime)
            {
                TimeWarp.Cancel(); // Treffer-Stopp/Zeitlupe nicht als Pausen-Rückgabewert sichern
                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }
            IsPaused = true;
            OnPauseChanged?.Invoke(true);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            if (_frozeTime)
            {
                // War vorher 0 (Upgrade-Screen offen), bleibt es 0
                Time.timeScale = _prevTimeScale;
                AudioListener.pause = false;
            }
            _frozeTime = false;
            IsPaused = false;
            OnPauseChanged?.Invoke(false);
        }

        // Neustart darf nur der Host (lädt die Spielszene für alle neu)
        public static bool CanRestart => Net.IsServer;

        public void RestartGame()
        {
            if (!CanRestart) return;
            IsPaused = false;
            AudioListener.pause = false;
            _frozeTime = false;
            if (GameManager.Instance != null) GameManager.Instance.Restart();
        }

        // Verbindung beenden und zurück ins Hauptmenü (Host schließt damit den Raum für alle)
        public void LeaveToMenu()
        {
            if (_frozeTime || Net.CanPauseTime) Time.timeScale = 1f;
            AudioListener.pause = false;
            IsPaused = false;
            _frozeTime = false;
            if (Net.IsRunning) NetSession.Instance.LeaveToMenu();
            else UnityEngine.SceneManagement.SceneManager.LoadScene(GameSession.MenuScene);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
