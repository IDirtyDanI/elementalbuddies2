using UnityEngine;
using UnityEngine.SceneManagement;
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
        [Tooltip("Optional. Fehlt er, wird zur Laufzeit eine Kopie des Neustart-Buttons daneben erzeugt.")]
        public Button MainMenuButton;
        [Tooltip("Abstand der Button-Mitten, wenn der Hauptmenü-Button automatisch erzeugt wird.")]
        public float AutoButtonSpacing = 330f;

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

            SetupMainMenuButton();
        }

        // Button "Hauptmenü" neben "Neu starten" (nur wenn die Menü-Szene im Build ist)
        private void SetupMainMenuButton()
        {
            if (!Application.CanStreamedLevelBeLoaded(GameSession.MenuScene)) return;

            if (MainMenuButton == null && RestartButton != null)
            {
                var rt = (RectTransform)RestartButton.transform;
                MainMenuButton = Instantiate(RestartButton, rt.parent);
                MainMenuButton.name = "MainMenuButton";
                var mrt = (RectTransform)MainMenuButton.transform;
                mrt.anchoredPosition = rt.anchoredPosition + new Vector2(AutoButtonSpacing * 0.5f, 0f);
                rt.anchoredPosition -= new Vector2(AutoButtonSpacing * 0.5f, 0f);
                var label = MainMenuButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = "Hauptmenü";
            }

            if (MainMenuButton != null)
            {
                MainMenuButton.onClick.RemoveAllListeners();
                MainMenuButton.onClick.AddListener(() =>
                {
                    Time.timeScale = 1f;
                    AudioListener.pause = false;
                    SceneManager.LoadScene(GameSession.MenuScene);
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
