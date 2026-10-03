using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    public class GameOverUI : MonoBehaviour
    {
        public GameObject Panel; // The whole game over screen
        public TextMeshProUGUI ReasonText;
        public TextMeshProUGUI WaveText;
        public TextMeshProUGUI BestText;
        public Button RestartButton;

        void Start()
        {
            if (Panel != null) Panel.SetActive(false);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameOver += ShowGameOver;
            }

            if (RestartButton != null)
            {
                RestartButton.onClick.RemoveAllListeners();
                RestartButton.onClick.AddListener(() =>
                {
                    if (GameManager.Instance != null) GameManager.Instance.Restart();
                });
            }
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameOver -= ShowGameOver;
            }
        }

        private void ShowGameOver(string reason)
        {
            if (Panel != null) Panel.SetActive(true);

            int wave = GameManager.Instance != null ? GameManager.Instance.LastWaveReached : 0;
            int best = GameManager.Instance != null ? GameManager.Instance.BestWave : 0;

            if (ReasonText != null) ReasonText.text = reason;
            if (WaveText != null) WaveText.text = $"Welle erreicht: {wave}";
            if (BestText != null) BestText.text = $"Beste Welle: {best}";
        }
    }
}
