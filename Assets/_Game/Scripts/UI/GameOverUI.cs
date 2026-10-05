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
        [Tooltip("Optional: Zeile \"In diesem Spiel erreichte Erfolge\". Fehlt sie, wird bei Bedarf eine Kopie von BestText darunter erzeugt.")]
        public TextMeshProUGUI AchievementsText;
        [Tooltip("So viel wächst die Box, wenn die Erfolge-Zeile automatisch erzeugt wird.")]
        public float AutoAchievementsHeight = 80f;

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
            var diff = WaveManager.Instance != null ? WaveManager.Instance.Difficulty : null;
            if (WaveText != null) WaveText.text = diff != null ? $"Welle erreicht: {wave} ({diff.DisplayName})" : $"Welle erreicht: {wave}";
            if (BestText != null) BestText.text = $"Beste Welle: {best}";
            ShowEarnedAchievements();
        }

        // "In diesem Spiel erreichte Erfolge: Verteidiger, Zwillingskraft" (nur wenn welche erreicht wurden)
        private void ShowEarnedAchievements()
        {
            var am = AchievementManager.Instance;
            var earned = am != null ? am.EarnedThisGame : null;
            if (earned == null || earned.Count == 0)
            {
                if (AchievementsText != null) AchievementsText.gameObject.SetActive(false);
                return;
            }

            if (AchievementsText == null && BestText != null)
            {
                // Kopie von BestText darunter; Box wächst nach oben und unten, damit der Neustart-Button frei bleibt
                var brt = BestText.rectTransform;
                AchievementsText = Instantiate(BestText, brt.parent);
                AchievementsText.name = "Achievements";
                var art = AchievementsText.rectTransform;
                art.sizeDelta = new Vector2(brt.sizeDelta.x, AutoAchievementsHeight);
                art.anchoredPosition = brt.anchoredPosition - new Vector2(0f, brt.sizeDelta.y * 0.5f + AutoAchievementsHeight * 0.5f + 4f);
                AchievementsText.fontSize = Mathf.Max(18f, BestText.fontSize - 2f);
                AchievementsText.enableAutoSizing = true;
                AchievementsText.fontSizeMin = 16f;
                AchievementsText.fontSizeMax = AchievementsText.fontSize;
                AchievementsText.color = new Color(0.55f, 0.36f, 0.05f);
                var box = brt.parent as RectTransform;
                if (box != null) box.sizeDelta += new Vector2(0f, AutoAchievementsHeight);
            }
            if (AchievementsText == null) return;

            var names = new System.Collections.Generic.List<string>();
            foreach (var a in earned) if (a != null) names.Add(a.Title);
            AchievementsText.text = $"<b>In diesem Spiel erreichte Erfolge:</b>\n{string.Join(", ", names)}";
            AchievementsText.gameObject.SetActive(true);
        }
    }
}
