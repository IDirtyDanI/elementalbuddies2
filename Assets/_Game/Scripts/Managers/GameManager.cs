using UnityEngine;

namespace ElementalBuddies
{
    public enum GameState
    {
        Building,
        Combat
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState CurrentState { get; private set; } = GameState.Building;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
        }

        // Example methods to change state
        public void StartCombat()
        {
            CurrentState = GameState.Combat;
            Debug.Log("Game State: Combat");
            // Trigger combat start events
        }

        public void EndCombat()
        {
            CurrentState = GameState.Building;
            Debug.Log("Game State: Building");
            // Trigger combat end events
        }
    }
}