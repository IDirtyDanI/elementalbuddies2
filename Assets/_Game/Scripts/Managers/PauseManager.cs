using UnityEngine;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    // Pause per ESC (Zeit + Audio anhalten). Liegt auf dem Managers-Objekt; UI hängt an OnPauseChanged.
    public class PauseManager : MonoBehaviour
    {
        public static PauseManager Instance { get; private set; }
        public static bool IsPaused { get; private set; }

        public event System.Action<bool> OnPauseChanged;

        private float _prevTimeScale = 1f;

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

        public void Pause()
        {
            if (IsPaused) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            IsPaused = true;
            OnPauseChanged?.Invoke(true);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            // War vorher 0 (Upgrade-Screen offen), bleibt es 0
            Time.timeScale = _prevTimeScale;
            AudioListener.pause = false;
            IsPaused = false;
            OnPauseChanged?.Invoke(false);
        }

        public void RestartGame()
        {
            IsPaused = false;
            AudioListener.pause = false;
            if (GameManager.Instance != null) GameManager.Instance.Restart();
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
