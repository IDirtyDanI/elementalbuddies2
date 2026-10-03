using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Info-Panel für den ausgewählten Buddy: Stufe, Werte, Aufwerten (nur zwischen den Wellen) und Verkaufen.
    // Script auf ein immer aktives Objekt (z. B. Canvas) oder direkt aufs Panel legen (Panel muss beim Start aktiv sein).
    public class BuddyInfoPanelUI : MonoBehaviour
    {
        public GameObject Panel;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI LevelText;
        public TextMeshProUGUI StatsText;

        [Header("Emblem")]
        public Image EmblemImage;
        // Index = Element wie ElementalBuddy.ElementIndex / UnitType-Reihenfolge: 0 Feuer, 1 Eis, 2 Erde, 3 Licht
        public Sprite[] Stage1Sprites; // Kleine Wesen
        public Sprite[] Stage2Sprites; // Humanoid
        public Sprite[] Stage3Sprites; // Humanoid mit Rüstung
        public Sprite[] EmblemSprites; // Fallback, falls das Stufen-Array leer ist / keinen Eintrag hat

        [Header("Buttons")]
        public Button UpgradeButton;
        public TextMeshProUGUI UpgradeButtonText;
        public Button SellButton;
        public TextMeshProUGUI SellButtonText;
        public Button CloseButton;

        [Header("Darstellung")]
        public string Arrow = "→"; // Falls die Schrift keinen Pfeil hat: "->"
        public Color NextValueColor = new Color(0.25f, 0.65f, 0.2f);

        private ElementalBuddy _buddy;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        void Start()
        {
            if (UpgradeButton != null)
            {
                UpgradeButton.onClick.RemoveAllListeners();
                UpgradeButton.onClick.AddListener(() =>
                {
                    if (InteractionManager.Instance != null) InteractionManager.Instance.UpgradeSelected();
                    Refresh();
                });
            }
            if (SellButton != null)
            {
                SellButton.onClick.RemoveAllListeners();
                SellButton.onClick.AddListener(() =>
                {
                    if (InteractionManager.Instance != null) InteractionManager.Instance.SellSelected();
                });
            }
            if (CloseButton != null)
            {
                CloseButton.onClick.RemoveAllListeners();
                CloseButton.onClick.AddListener(() =>
                {
                    if (InteractionManager.Instance != null) InteractionManager.Instance.DeselectBuddy();
                });
            }

            if (InteractionManager.Instance != null) InteractionManager.Instance.OnBuddySelected += HandleBuddySelected;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnShardsChanged += Refresh;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;

            HandleBuddySelected(InteractionManager.Instance != null ? InteractionManager.Instance.SelectedBuddy : null);
        }

        void OnDestroy()
        {
            if (InteractionManager.Instance != null) InteractionManager.Instance.OnBuddySelected -= HandleBuddySelected;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnShardsChanged -= Refresh;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
            if (_buddy != null) _buddy.OnLevelChanged -= Refresh;
        }

        // Spielphase (Bau/Kampf/Pause) ändert die Button-Zustände -> leicht pollen
        void Update()
        {
            if (_buddy != null && Time.frameCount % 10 == 0) Refresh();
        }

        private void HandleGameOver(string reason)
        {
            HandleBuddySelected(null);
        }

        private void HandleBuddySelected(ElementalBuddy buddy)
        {
            if (_buddy != null) _buddy.OnLevelChanged -= Refresh;
            _buddy = buddy;
            if (_buddy != null) _buddy.OnLevelChanged += Refresh;

            if (Panel != null) Panel.SetActive(_buddy != null);
            Refresh();
        }

        private void Refresh()
        {
            if (_buddy == null) return;

            int level = _buddy.Level;
            bool canUpgrade = _buddy.CanUpgrade;
            int next = level + 1;

            if (NameText != null) NameText.text = _buddy.StageName;
            if (LevelText != null) LevelText.text = $"Stufe {level} / {_buddy.MaxLevel}";

            if (EmblemImage != null)
            {
                int idx = _buddy.ElementIndex;
                Sprite[] stageSprites = _buddy.Stage >= 3 ? Stage3Sprites : (_buddy.Stage == 2 ? Stage2Sprites : Stage1Sprites);
                Sprite sprite = PickSprite(stageSprites, idx);
                if (sprite == null) sprite = PickSprite(EmblemSprites, idx);
                EmblemImage.sprite = sprite;
                EmblemImage.enabled = sprite != null;
            }

            if (StatsText != null) StatsText.text = BuildStats(canUpgrade, level, next);

            // Aufwerten
            if (UpgradeButton != null || UpgradeButtonText != null)
            {
                string label;
                bool interactable;
                if (!canUpgrade)
                {
                    label = "Maximale Stufe";
                    interactable = false;
                }
                else if (!ElementalBuddy.IsUpgradePhase)
                {
                    label = "Nur zwischen den Wellen";
                    interactable = false;
                }
                else
                {
                    float cost = _buddy.NextUpgradeCost;
                    label = $"Aufwerten ({Mathf.CeilToInt(cost)})";
                    interactable = EconomyManager.Instance != null && EconomyManager.Instance.CanAfford(cost);
                }
                if (UpgradeButtonText != null) UpgradeButtonText.text = label;
                if (UpgradeButton != null) UpgradeButton.interactable = interactable;
            }

            // Verkaufen (nur in der Bauphase, wie Rechtsklick)
            var im = InteractionManager.Instance;
            if (im != null)
            {
                if (SellButtonText != null) SellButtonText.text = $"Verkaufen (+{Mathf.FloorToInt(im.GetSellRefund(_buddy))})";
                if (SellButton != null) SellButton.interactable = im.CanSellNow;
            }
        }

        private string BuildStats(bool canUpgrade, int level, int next)
        {
            if (_buddy is TankBuddy)
            {
                // Tank: Damage = Aura-DPS, FireRate = Spott-Rate, Range = Spott-/Aura-Radius
                return Line("Aura-Schaden", _buddy.GetDamageAtLevel(level), _buddy.GetDamageAtLevel(next), canUpgrade, "/s", "0.#") + "\n"
                     + Line("Spott alle", Interval(_buddy.GetFireRateAtLevel(level)), Interval(_buddy.GetFireRateAtLevel(next)), canUpgrade, " s", "0.#") + "\n"
                     + Line("Radius", _buddy.GetRangeAtLevel(level), _buddy.GetRangeAtLevel(next), canUpgrade, " m", "0.#");
            }

            bool healer = _buddy is HealerBuddy;
            return Line(healer ? "Heilung" : "Schaden", _buddy.GetDamageAtLevel(level), _buddy.GetDamageAtLevel(next), canUpgrade, "", "0.#") + "\n"
                 + Line(healer ? "Heilrate" : "Feuerrate", _buddy.GetFireRateAtLevel(level), _buddy.GetFireRateAtLevel(next), canUpgrade, "/s", "0.##") + "\n"
                 + Line("Reichweite", _buddy.GetRangeAtLevel(level), _buddy.GetRangeAtLevel(next), canUpgrade, " m", "0.#");
        }

        private static Sprite PickSprite(Sprite[] sprites, int idx)
        {
            return sprites != null && idx >= 0 && idx < sprites.Length ? sprites[idx] : null;
        }

        private static float Interval(float rate) => rate > 0f ? 1f / rate : 0f;

        // "Schaden: 12 → 16.2" (Pfeil + nächster Wert nur, wenn aufwertbar)
        private string Line(string label, float current, float nextValue, bool showNext, string unit, string format)
        {
            string text = $"{label}: {current.ToString(format, Inv)}{unit}";
            if (showNext)
            {
                string hex = ColorUtility.ToHtmlStringRGB(NextValueColor);
                text += $" {Arrow} <color=#{hex}>{nextValue.ToString(format, Inv)}{unit}</color>";
            }
            return text;
        }
    }
}
