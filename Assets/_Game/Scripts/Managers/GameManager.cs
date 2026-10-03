using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElementalBuddies
{
    public enum GameState
    {
        Building,
        Combat,
        GameOver
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private const string BestWaveKey = "BestWave";

        public GameState CurrentState { get; private set; } = GameState.Building;
        public bool IsGameOver => CurrentState == GameState.GameOver;

        // Highscore (Endless Mode)
        public int LastWaveReached { get; private set; }
        public int BestWave { get; private set; }
        public string GameOverReason { get; private set; }

        public event System.Action<string> OnGameOver;

        void Awake()
        {
            // Single-scene game: no DontDestroyOnLoad, so a scene reload (Restart) creates a fresh instance.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BestWave = PlayerPrefs.GetInt(BestWaveKey, 0);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void StartCombat()
        {
            if (IsGameOver) return;
            CurrentState = GameState.Combat;
            Debug.Log("Game State: Combat");
        }

        public void EndCombat()
        {
            if (IsGameOver) return;
            CurrentState = GameState.Building;
            Debug.Log("Game State: Building");
        }

        public void TriggerGameOver(string reason)
        {
            if (IsGameOver) return;

            CurrentState = GameState.GameOver;
            GameOverReason = reason;

            // CurrentWaveIndex is 0-based and only incremented in WaveManager.EndWave().
            // During an active wave the player is in wave (index + 1); between waves, index == number of completed waves.
            var wm = WaveManager.Instance;
            if (wm != null)
                LastWaveReached = wm.IsWaveActive ? wm.CurrentWaveIndex + 1 : wm.CurrentWaveIndex;
            else
                LastWaveReached = 0;

            if (LastWaveReached > BestWave)
            {
                BestWave = LastWaveReached;
                PlayerPrefs.SetInt(BestWaveKey, BestWave);
                PlayerPrefs.Save();
            }

            Debug.Log($"Game State: GameOver ({reason}) - Wave {LastWaveReached}, Best {BestWave}");

            Time.timeScale = 0f;
            OnGameOver?.Invoke(reason);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            CurrentState = GameState.Building;

            Scene active = SceneManager.GetActiveScene();
            if (active.buildIndex >= 0)
            {
                SceneManager.LoadScene(active.buildIndex);
            }
            else
            {
#if UNITY_EDITOR
                // Scene not in Build Settings: still allow restart while testing in the Editor
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    active.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
                Debug.LogError($"GameManager: Scene '{active.name}' is not in Build Settings, cannot restart.");
#endif
            }
        }
    }
}
