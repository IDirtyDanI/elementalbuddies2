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
        [Tooltip("So lange (s) müssen alle Champions gleichzeitig gefallen sein, bevor der Team-Ausfall zählt.")]
        public float AllDeadGrace = 0.5f;
        private float _allDeadSince = -1f;

        // Team-Ausfall (Nutzer-Entscheidung 2026-10-09: „Wiederbelebung mit Preis“): Sind alle Champions gefallen, opfert der
        // Nexus Leben und alle stehen nach WipeReviveDelay wieder auf. Der Preis steigt mit jedem Ausfall; reicht das
        // Nexus-Leben nicht, ist der Run vorbei. Die Buddies verteidigen in der Zwischenzeit allein.
        [Header("Wiederbelebung bei Team-Ausfall")]
        [Tooltip("Wartezeit (s), bis alle Champions nach einem Team-Ausfall wieder aufstehen.")]
        public float WipeReviveDelay = 10f;
        [Tooltip("Preis des ersten Team-Ausfalls als Anteil der Nexus-Max-LP …")]
        public float WipeCostFirst = 0.15f;
        [Tooltip("… und so viel mehr bei jedem weiteren.")]
        public float WipeCostStep = 0.10f;
        public int TeamWipes { get; private set; }
        private bool _wipeHandled;
        // Alle Rechner: Team-Ausfall (Nummer, geopferte Nexus-LP, Wartezeit)
        public static event System.Action<int, float, float> OnTeamWipe;

        // Preis des nächsten Team-Ausfalls in Nexus-LP
        public float NextWipeCost => Nexus.Instance != null
            ? Nexus.Instance.MaxHP * (WipeCostFirst + SiegeLevels.WipeCostExtra + WipeCostStep * TeamWipes) : 0f;

        // Alle Rechner (NetGame.BroadcastTeamWipe): Ereignis lokal auslösen
        public void RaiseTeamWipe(int wipe, float cost, float delay)
        {
            if (!Net.IsServer) TeamWipes = wipe;
            OnTeamWipe?.Invoke(wipe, cost, delay);
        }
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
            if (!allDead) { _allDeadSince = -1f; _wipeHandled = false; return; }
            if (_allDeadSince < 0f) _allDeadSince = Time.time;
            if (_wipeHandled || Time.time - _allDeadSince < AllDeadGrace) return;
            _wipeHandled = true;

            // Wiederbelebung mit Preis: Nexus opfert Leben; reicht es nicht, ist der Run vorbei
            var nexus = Nexus.Instance;
            float cost = NextWipeCost;
            if (nexus == null || nexus.CurrentHP - cost < 1f)
            {
                TriggerGameOver(AllDeadReason);
                return;
            }
            TeamWipes++;
            nexus.Sacrifice(cost);
            foreach (var a in PlayerAvatar.All)
                if (a != null) a.ServerScheduleWipeRevive(WipeReviveDelay);
            NetGame.BroadcastTeamWipe(TeamWipes, cost, WipeReviveDelay);
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
            // Rekord der Belagerungsstufe (nur ohne Cheats)
            var am = AchievementManager.Instance;
            if (am == null || am.AchievementsAllowed) SiegeLevels.RecordWave(SiegeLevels.Current, LastWaveReached);

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
