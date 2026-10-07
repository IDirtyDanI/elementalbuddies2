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

    // Mehrspieler: Zustandswechsel entscheidet der Server (StartCombat/EndCombat/TriggerGameOver); Clients spiegeln den
    // Zustand über NetGame.Waves (ApplyRemoteState/ApplyRemoteGameOver) und feuern OnStateChanged/OnGameOver lokal.
    // Game Over: Nexus zerstört oder alle Champions gefallen (Server prüft PlayerAvatar.AnyAlive).
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
        // Jeder Zustandswechsel (Building/Combat/GameOver) – auf Server und Clients
        public event System.Action<GameState> OnStateChanged;

        // Neustart darf nur der Host (Clients: Button ausgrauen)
        public bool CanRestart => Net.IsServer;

        public const string AllDeadReason = "Alle Champions sind gefallen";
        [Tooltip("So lange (s) müssen alle Champions gleichzeitig gefallen sein, bevor das Spiel endet.")]
        public float AllDeadGrace = 0.5f;
        private float _allDeadSince = -1f;
        private float _startTime;

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
            _startTime = Time.time;
        }

        // Server: alle Champions gefallen → Game Over
        void Update()
        {
            if (!Net.IsServer || IsGameOver) return;
            bool allDead = PlayerAvatar.All.Count > 0 && !PlayerAvatar.AnyAlive() && Time.time - _startTime > 1f;
            if (!allDead) { _allDeadSince = -1f; return; }
            if (_allDeadSince < 0f) _allDeadSince = Time.time;
            if (Time.time - _allDeadSince >= AllDeadGrace) TriggerGameOver(AllDeadReason);
        }

        private void SetState(GameState state)
        {
            if (CurrentState == state) return;
            CurrentState = state;
            if (Net.IsServer && NetGame.Ready && state != GameState.GameOver) NetGame.Instance.ServerSetGameState(state);
            OnStateChanged?.Invoke(state);
        }

        // Client: Zustand vom Server (Building/Combat)
        public void ApplyRemoteState(GameState state)
        {
            if (Net.IsServer || IsGameOver || state == GameState.GameOver) return;
            SetState(state);
        }

        // Client: Game Over vom Server; Bestwert wird lokal gespeichert
        public void ApplyRemoteGameOver(string reason, int lastWaveReached)
        {
            if (Net.IsServer || IsGameOver) return;
            GameOverReason = reason;
            LastWaveReached = lastWaveReached;
            SaveBestWave();
            Debug.Log($"Game State: GameOver ({reason}) - Wave {LastWaveReached}, Best {BestWave} (vom Host)");
            SetState(GameState.GameOver);
            OnGameOver?.Invoke(reason);
        }

        private void SaveBestWave()
        {
            if (LastWaveReached > BestWave)
            {
                BestWave = LastWaveReached;
                PlayerPrefs.SetInt(BestWaveKey, BestWave);
                PlayerPrefs.Save();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void StartCombat()
        {
            if (!Net.IsServer || IsGameOver) return;
            SetState(GameState.Combat);
            Debug.Log("Game State: Combat");
        }

        public void EndCombat()
        {
            if (!Net.IsServer || IsGameOver) return;
            SetState(GameState.Building);
            Debug.Log("Game State: Building");
        }

        // Nur auf dem Server (Clients: ohne Wirkung, sie bekommen das Game Over über NetGame)
        public void TriggerGameOver(string reason)
        {
            if (!Net.IsServer || IsGameOver) return;

            GameOverReason = reason;

            // CurrentWaveIndex is 0-based and only incremented in WaveManager.EndWave().
            // During an active wave the player is in wave (index + 1); between waves, index == number of completed waves.
            var wm = WaveManager.Instance;
            if (wm != null)
                LastWaveReached = wm.IsWaveActive ? wm.CurrentWaveIndex + 1 : wm.CurrentWaveIndex;
            else
                LastWaveReached = 0;

            SaveBestWave();

            Debug.Log($"Game State: GameOver ({reason}) - Wave {LastWaveReached}, Best {BestWave}");

            if (NetGame.Ready) NetGame.Instance.ServerGameOver(reason, LastWaveReached);
            SetState(GameState.GameOver);
            // Koop: keine globale Pause – Gegner/Boss-KI und Spawns halten über den GameOver-Zustand an
            if (Net.CanPauseTime) Time.timeScale = 0f;
            OnGameOver?.Invoke(reason);
        }

        // Neustart: im Netz lädt der Host die Spielszene für alle neu (NetSession.RestartGame), Clients: ohne Wirkung
        public void Restart()
        {
            if (Net.IsRunning)
            {
                if (!Net.IsServer) return;
                Time.timeScale = 1f;
                NetSession.Instance.RestartGame();
                return;
            }

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

        // Zurück ins Hauptmenü (Verbindung beenden; der Host schließt damit den Raum für alle)
        public void LeaveToMenu()
        {
            Time.timeScale = 1f;
            if (Net.IsRunning || NetSession.Exists)
            {
                NetSession.Instance.LeaveToMenu();
                return;
            }
            SceneManager.LoadScene(GameSession.MenuScene);
        }
    }
}
