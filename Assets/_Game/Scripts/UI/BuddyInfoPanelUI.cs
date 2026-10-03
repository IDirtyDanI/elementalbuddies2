using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

        [Header("Fusion")]
        public GameObject FusionSection;              // nur für normale Buddies sichtbar
        public TextMeshProUGUI FusionHint;            // Grund, warum nicht verschmolzen werden kann / "Verschmelzen mit:"
        public RectTransform FusionOptionsContainer;  // VerticalLayoutGroup
        public Button FusionOptionTemplate;           // inaktiv; Kinder "Icon" (Image), "Label" (TMP)
        public TextMeshProUGUI FusionInfoText;        // für Fusions-Buddies: Beschreibung der Fähigkeit (statt der Sektion)
        [Tooltip("Optional: Hintergrund-Box um FusionInfoText, wird mit ihm ein-/ausgeblendet.")]
        public GameObject FusionInfoBox;
        public Color UnaffordableColor = new Color(0.85f, 0.2f, 0.2f);

        private ElementalBuddy _buddy;

        // Fusions-Optionen: Buttons werden nur neu gebaut, wenn sich die Menge der Optionen ändert
        private class OptionEntry
        {
            public Button Button;
            public TextMeshProUGUI Label;
            public ElementalBuddy Partner;
        }
        private readonly List<OptionEntry> _optionEntries = new List<OptionEntry>();
        private string _optionsKey = "";
        private readonly StringBuilder _keyBuilder = new StringBuilder();
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

            if (FusionOptionTemplate != null) FusionOptionTemplate.gameObject.SetActive(false);

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

            ClearFusionOptions();
            if (_buddy == null)
            {
                if (FusionSection != null) FusionSection.SetActive(false);
                if (FusionInfoText != null) FusionInfoText.gameObject.SetActive(false);
                if (FusionInfoBox != null) FusionInfoBox.SetActive(false);
            }

            if (Panel != null) Panel.SetActive(_buddy != null);
            Refresh();
        }

        private void Refresh()
        {
            if (_buddy == null) return;

            int level = _buddy.Level;
            bool canUpgrade = _buddy.CanUpgrade;
            int next = level + 1;

            var fusion = _buddy as FusionBuddy;
            var fusionRecipe = fusion != null && FusionManager.Instance != null ? FusionManager.Instance.FindRecipe(fusion.Element) : null;

            if (NameText != null) NameText.text = _buddy.StageName;
            if (LevelText != null)
                LevelText.text = fusion != null
                    ? $"Fusion aus {ElementInfo.Name(fusion.ParentA)} + {ElementInfo.Name(fusion.ParentB)}"
                    : $"Stufe {level} / {_buddy.MaxLevel}";

            if (EmblemImage != null)
            {
                int idx = _buddy.ElementIndex;
                Sprite[] stageSprites = _buddy.Stage >= 3 ? Stage3Sprites : (_buddy.Stage == 2 ? Stage2Sprites : Stage1Sprites);
                Sprite sprite = fusionRecipe != null ? fusionRecipe.Icon : null;
                if (sprite == null) sprite = PickSprite(stageSprites, idx);
                if (sprite == null) sprite = PickSprite(EmblemSprites, idx);
                EmblemImage.sprite = sprite;
                EmblemImage.enabled = sprite != null;
            }

            if (StatsText != null) StatsText.text = fusion != null ? BuildFusionStats(fusion) : BuildStats(canUpgrade, level, next);

            // Aufwerten
            if (UpgradeButton != null || UpgradeButtonText != null)
            {
                string label;
                bool interactable;
                if (fusion != null)
                {
                    label = "Fusion – keine Stufen";
                    interactable = false;
                }
                else if (!canUpgrade)
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

            // Fusion
            if (FusionInfoText != null)
            {
                bool show = fusion != null && fusionRecipe != null && !string.IsNullOrEmpty(fusionRecipe.Description);
                FusionInfoText.gameObject.SetActive(show);
                if (FusionInfoBox != null && FusionInfoBox.activeSelf != show) FusionInfoBox.SetActive(show);
                if (show) FusionInfoText.text = fusionRecipe.Description;
            }
            RefreshFusionSection(fusion != null);
        }

        // ---------------- Fusion ----------------

        private void RefreshFusionSection(bool isFusion)
        {
            var fm = FusionManager.Instance;
            bool show = !isFusion && fm != null;
            if (FusionSection != null && FusionSection.activeSelf != show) FusionSection.SetActive(show);
            if (!show)
            {
                ClearFusionOptions();
                return;
            }

            var options = fm.GetOptions(_buddy);
            bool canFuse = fm.CanFuseNow;

            if (FusionHint != null)
            {
                if (options.Count == 0) FusionHint.text = fm.GetBlockReason(_buddy) ?? "";
                else FusionHint.text = canFuse ? "Verschmelzen mit:" : "Verschmelzen mit: <i>(nur zwischen den Wellen)</i>";
            }

            // Menge der Optionen geändert? -> Buttons neu bauen, sonst nur Texte/Zustände aktualisieren
            _keyBuilder.Clear();
            foreach (var o in options)
                _keyBuilder.Append(o.Partner.GetInstanceID()).Append(':').Append((int)o.Recipe.Result).Append(';');
            string key = _keyBuilder.ToString();
            if (key != _optionsKey) RebuildFusionOptions(options, key);

            string red = ColorUtility.ToHtmlStringRGB(UnaffordableColor);
            for (int i = 0; i < _optionEntries.Count && i < options.Count; i++)
            {
                var o = options[i];
                var entry = _optionEntries[i];
                if (entry.Label != null)
                {
                    string cost = $"{Mathf.CeilToInt(o.Cost)} Splitter";
                    if (!o.Affordable) cost = $"<color=#{red}>{cost}</color>";
                    entry.Label.text = $"<b>{FusionInfo.DisplayName(o.Recipe.Result)}</b>  ·  mit {o.Partner.StageName} ({o.Distance.ToString("0.#", Inv)} m)  ·  {cost}";
                }
                if (entry.Button != null) entry.Button.interactable = canFuse && o.Affordable;
            }
        }

        private void RebuildFusionOptions(List<FusionManager.FusionOption> options, string key)
        {
            ClearFusionOptions();
            _optionsKey = key;
            if (FusionOptionTemplate == null) return;
            Transform parent = FusionOptionsContainer != null ? FusionOptionsContainer : FusionOptionTemplate.transform.parent;

            foreach (var o in options)
            {
                var button = Instantiate(FusionOptionTemplate, parent);
                button.gameObject.name = "FusionOption_" + FusionInfo.Name(o.Recipe.Result);
                button.gameObject.SetActive(true);

                var icon = FindChild<Image>(button.transform, "Icon");
                if (icon != null)
                {
                    icon.sprite = o.Recipe.Icon;
                    icon.gameObject.SetActive(o.Recipe.Icon != null);
                }

                var entry = new OptionEntry { Button = button, Label = FindChild<TextMeshProUGUI>(button.transform, "Label"), Partner = o.Partner };
                ElementalBuddy partner = o.Partner;

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OnFusionOptionClicked(partner));

                var hover = button.GetComponent<FusionOptionHover>();
                if (hover == null) hover = button.gameObject.AddComponent<FusionOptionHover>();
                hover.OnEnter = () =>
                {
                    if (FusionManager.Instance != null && _buddy != null && partner != null) FusionManager.Instance.ShowLink(_buddy, partner);
                };
                hover.OnExit = () =>
                {
                    if (FusionManager.Instance != null) FusionManager.Instance.HideLink();
                };

                _optionEntries.Add(entry);
            }
        }

        private void ClearFusionOptions()
        {
            foreach (var e in _optionEntries)
                if (e.Button != null) Destroy(e.Button.gameObject);
            _optionEntries.Clear();
            _optionsKey = "";
            if (FusionManager.Instance != null) FusionManager.Instance.HideLink();
        }

        private void OnFusionOptionClicked(ElementalBuddy partner)
        {
            var fm = FusionManager.Instance;
            if (fm == null || _buddy == null || partner == null) return;
            fm.HideLink();
            // Bei Erfolg wählt der FusionManager den neuen Buddy aus -> HandleBuddySelected baut das Panel neu
            if (fm.TryFuse(_buddy, partner) == null) Refresh();
        }

        // Werte eines Fusions-Buddys (keine Stufen -> keine Pfeile)
        private string BuildFusionStats(FusionBuddy fusion)
        {
            float dmg = fusion.GetDamageAtLevel(1);
            float rate = fusion.GetFireRateAtLevel(1);
            float range = fusion.GetRangeAtLevel(1);
            string rateLine = Line("Rate", rate, rate, false, "/s", "0.##");
            string rangeLine = Line("Reichweite", range, range, false, " m", "0.#");

            string stats;
            switch (fusion)
            {
                case LightningBuddy l:
                    stats = Line("Schaden", dmg, dmg, false, "", "0.#") + "\n"
                          + $"Sprünge: {l.GetJumps(false)} (+1 bei nassem Ziel)\n"
                          + rateLine + "\n" + rangeLine;
                    break;
                case WaterBuddy w:
                    stats = Line("Schaden", dmg, dmg, false, "", "0.#") + "\n"
                          + rateLine + "\n" + rangeLine + "\n"
                          + $"Strudel alle {w.WhirlpoolEvery} Schüsse";
                    break;
                case AirBuddy a:
                    stats = Line("Böe-Schaden", dmg, dmg, false, "", "0.#") + "\n"
                          + Line("Böe alle", Interval(rate), Interval(rate), false, " s", "0.#") + "\n"
                          + $"Aura: +{Mathf.RoundToInt(a.FireRateBonus * 100f)} % Feuerrate\n"
                          + Line("Radius", range, range, false, " m", "0.#");
                    break;
                case ShadowBuddy s:
                    stats = Line("Fluch-Schaden", dmg, dmg, false, "/s", "0.#") + "\n"
                          + $"Ziele: {s.Targets}\n"
                          + $"+{Mathf.RoundToInt(s.DamageTakenBonus * 100f)} % Schaden genommen\n"
                          + rangeLine;
                    break;
                case MagmaBuddy m:
                    float puddleDps = dmg * m.PuddleDamageFactor;
                    stats = Line("Einschlag-Schaden", dmg, dmg, false, "", "0.#") + "\n"
                          + rateLine + "\n" + rangeLine + "\n"
                          + $"Lavapfütze: {puddleDps.ToString("0.#", Inv)}/s für {m.PuddleLifetime.ToString("0.#", Inv)} s";
                    break;
                case CrystalBuddy c:
                    stats = $"Leben: {Mathf.CeilToInt(c.CurrentHP)}/{Mathf.CeilToInt(c.MaxHP)}\n"
                          + $"Frost-Aura: −{Mathf.RoundToInt(c.AuraSlow * 100f)} % Tempo\n"
                          + Line("Spott alle", Interval(rate), Interval(rate), false, " s", "0.#") + "\n"
                          + $"Splitter-Ladung: {Mathf.RoundToInt(c.Charge01 * 100f)} %\n"
                          + Line("Radius", range, range, false, " m", "0.#");
                    break;
                default:
                    stats = Line("Schaden", dmg, dmg, false, "", "0.#") + "\n" + rateLine + "\n" + rangeLine;
                    break;
            }
            return stats + AuraLine();
        }

        // Rückenwind eines Luft-Buddys in der Nähe
        private string AuraLine()
        {
            float m = AirBuddy.GetFireRateMultiplier(_buddy);
            if (m <= 1.0001f) return "";
            string hex = ColorUtility.ToHtmlStringRGB(NextValueColor);
            return $"\n<color=#{hex}>Rückenwind: +{Mathf.RoundToInt((m - 1f) * 100f)} % Feuerrate</color>";
        }

        private static T FindChild<T>(Transform root, string childName) where T : Component
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t.GetComponent<T>();
            return null;
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

            var healer = _buddy as HealerBuddy;
            string stats = Line(healer != null ? "Strahl" : "Schaden", _buddy.GetDamageAtLevel(level), _buddy.GetDamageAtLevel(next), canUpgrade, "", "0.#") + "\n"
                 + Line(healer != null ? "Angriffsrate" : "Feuerrate", _buddy.GetFireRateAtLevel(level), _buddy.GetFireRateAtLevel(next), canUpgrade, "/s", "0.##") + "\n"
                 + Line("Reichweite", _buddy.GetRangeAtLevel(level), _buddy.GetRangeAtLevel(next), canUpgrade, " m", "0.#");
            if (healer != null)
            {
                // Segen skaliert wie der Schaden mit der Stufe
                float ratio = _buddy.GetDamageAtLevel(level) > 0f ? _buddy.GetDamageAtLevel(next) / _buddy.GetDamageAtLevel(level) : 1f;
                stats += "\n" + Line("Segen", healer.EffectiveBlessHeal, healer.EffectiveBlessHeal * ratio, canUpgrade, $" HP / {healer.BlessInterval:0.#} s", "0.#");
            }
            return stats + AuraLine();
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
