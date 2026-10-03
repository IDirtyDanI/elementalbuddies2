using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace ElementalBuddies
{
    // Pause-Menü (ESC): links Navigation, rechts Seiten "Übersicht" (Champion, Karten + Werte) und "Einstellungen".
    // Script auf ein immer aktives Objekt (Canvas) legen; Root wird ein-/ausgeblendet.
    public class PauseMenuUI : MonoBehaviour
    {
        public GameObject Root;                 // Vollbild-Overlay
        public GameObject OverviewPage;         // Seite 1
        public GameObject SettingsPage;         // Seite 2
        public GameObject ConfirmQuitPanel;     // Bestätigungs-Dialog (Kind von Root)
        public Button ResumeButton, OverviewButton, SettingsButton, RestartButton, QuitButton, ConfirmYesButton, ConfirmNoButton;
        [Tooltip("Zurück ins Hauptmenü (GameSession.MenuScene).")]
        public Button MainMenuButton;
        public Image OverviewButtonImage, SettingsButtonImage;   // Tab-Hervorhebung
        public Sprite TabNormalSprite, TabActiveSprite;           // optional; sonst Farbton
        public RectTransform CardsContainer;     // VerticalLayoutGroup für Karten-Zeilen
        public GameObject CardRowTemplate;       // inaktiv; Kinder "Icon"(Image), "Title"/"Desc"/"Count"(TMP)
        public TextMeshProUGUI CardsEmptyText;
        public TextMeshProUGUI StatsText;
        public TextMeshProUGUI WaveInfoText;
        public Slider MasterSlider, MusicSlider, SfxSlider;
        public TextMeshProUGUI MasterValue, MusicValue, SfxValue;
        public Toggle FullscreenToggle;

        private const string PrefFullscreen = "fullscreen";
        private const string HeaderOpen = "<b><color=#5a3a1a>";
        private const string HeaderClose = "</color></b>";
        private const string Green = "#2e6b2e";
        private static readonly Color TabActiveTint = new Color(1f, 0.93f, 0.75f);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private PauseManager _pause;
        private readonly List<GameObject> _rows = new List<GameObject>();

        void Start()
        {
            _pause = PauseManager.Instance != null ? PauseManager.Instance : FindFirstObjectByType<PauseManager>();
            if (_pause != null) _pause.OnPauseChanged += HandlePauseChanged;

            if (ResumeButton != null) ResumeButton.onClick.AddListener(() => { if (_pause != null) _pause.Resume(); });
            if (OverviewButton != null) OverviewButton.onClick.AddListener(() => ShowPage(true));
            if (SettingsButton != null) SettingsButton.onClick.AddListener(() => ShowPage(false));
            if (RestartButton != null) RestartButton.onClick.AddListener(() => { if (_pause != null) _pause.RestartGame(); });
            if (QuitButton != null) QuitButton.onClick.AddListener(() => SetConfirm(true));
            if (MainMenuButton != null) MainMenuButton.onClick.AddListener(LoadMainMenu);
            if (ConfirmYesButton != null) ConfirmYesButton.onClick.AddListener(() => { if (_pause != null) _pause.QuitGame(); });
            if (ConfirmNoButton != null) ConfirmNoButton.onClick.AddListener(() => SetConfirm(false));

            InitSettings();

            if (CardRowTemplate != null) CardRowTemplate.SetActive(false);
            SetConfirm(false);
            if (Root != null) Root.SetActive(false);
        }

        void OnDestroy()
        {
            if (_pause != null) _pause.OnPauseChanged -= HandlePauseChanged;
        }

        private void HandlePauseChanged(bool paused)
        {
            SetConfirm(false);
            if (paused)
            {
                if (Root != null) Root.SetActive(true);
                ShowPage(true); // inkl. Refresh()
                SyncSettings();
            }
            else if (Root != null) Root.SetActive(false);
        }

        // Zeit und Audio freigeben, dann das Hauptmenü laden
        public void LoadMainMenu()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (!Application.CanStreamedLevelBeLoaded(GameSession.MenuScene))
            {
                Debug.LogWarning($"PauseMenuUI: Szene '{GameSession.MenuScene}' ist nicht in den Build Settings.");
                return;
            }
            SceneManager.LoadScene(GameSession.MenuScene);
        }

        private void SetConfirm(bool show)
        {
            if (ConfirmQuitPanel != null) ConfirmQuitPanel.SetActive(show);
        }

        private void ShowPage(bool overview)
        {
            if (OverviewPage != null) OverviewPage.SetActive(overview);
            if (SettingsPage != null) SettingsPage.SetActive(!overview);
            SetTab(OverviewButtonImage, overview);
            SetTab(SettingsButtonImage, !overview);
            if (overview) Refresh();
        }

        private void SetTab(Image img, bool active)
        {
            if (img == null) return;
            if (TabNormalSprite != null && TabActiveSprite != null)
            {
                img.sprite = active ? TabActiveSprite : TabNormalSprite;
                img.color = Color.white;
            }
            else img.color = active ? TabActiveTint : Color.white;
        }

        // ---------------- Einstellungen ----------------

        private void InitSettings()
        {
            SetupSlider(MasterSlider, MasterValue, v => { if (GameAudio.Instance != null) GameAudio.Instance.SetMaster(v); });
            SetupSlider(MusicSlider, MusicValue, v => { if (GameAudio.Instance != null) GameAudio.Instance.SetMusic(v); });
            SetupSlider(SfxSlider, SfxValue, v => { if (GameAudio.Instance != null) GameAudio.Instance.SetSfx(v); });

            if (FullscreenToggle != null)
            {
                FullscreenToggle.onValueChanged.AddListener(v =>
                {
                    Screen.fullScreen = v;
                    PlayerPrefs.SetInt(PrefFullscreen, v ? 1 : 0);
                    PlayerPrefs.Save();
                });
            }
            SyncSettings();
        }

        private void SetupSlider(Slider slider, TextMeshProUGUI label, System.Action<float> apply)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(v =>
            {
                apply(v);
                SetPercent(label, v);
            });
        }

        // Regler auf aktuelle Werte setzen (ohne Events)
        private void SyncSettings()
        {
            var ga = GameAudio.Instance;
            SyncSlider(MasterSlider, MasterValue, ga != null ? ga.MasterVolume : 1f);
            SyncSlider(MusicSlider, MusicValue, ga != null ? ga.MusicLevel : 1f);
            SyncSlider(SfxSlider, SfxValue, ga != null ? ga.SfxVolume : 1f);
            if (FullscreenToggle != null) FullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
        }

        private static void SyncSlider(Slider slider, TextMeshProUGUI label, float v)
        {
            if (slider != null) slider.SetValueWithoutNotify(v);
            SetPercent(label, v);
        }

        private static void SetPercent(TextMeshProUGUI label, float v)
        {
            if (label != null) label.text = (v * 100f).ToString("0", Inv) + " %";
        }

        // ---------------- Übersicht ----------------

        public void Refresh()
        {
            RefreshCards();
            if (StatsText != null) StatsText.text = BuildStats();
            if (WaveInfoText != null) WaveInfoText.text = BuildWaveInfo();
        }

        private void RefreshCards()
        {
            foreach (var row in _rows)
            {
                if (row == null) continue;
                row.SetActive(false);
                Destroy(row);
            }
            _rows.Clear();

            // Gleiche Karten zusammenfassen (Reihenfolge der ersten Wahl)
            var order = new List<UpgradeDefinitionSO>();
            var counts = new Dictionary<UpgradeDefinitionSO, int>();
            var um = UpgradeManager.Instance;
            if (um != null)
            {
                foreach (var up in um.PickedUpgrades)
                {
                    if (up == null) continue;
                    if (counts.TryGetValue(up, out int c)) counts[up] = c + 1;
                    else
                    {
                        counts[up] = 1;
                        order.Add(up);
                    }
                }
            }

            var mm = MerchantManager.Instance;
            var merchantCards = mm != null ? mm.GetPickedSummary() : new List<KeyValuePair<MerchantCardSO, int>>();

            if (CardsEmptyText != null)
            {
                CardsEmptyText.gameObject.SetActive(order.Count == 0 && merchantCards.Count == 0);
                CardsEmptyText.text = "Noch keine Karten gewählt – nach jeder Welle darfst du eine aussuchen.";
            }
            if (CardRowTemplate == null || CardsContainer == null) return;

            foreach (var card in order)
            {
                int n = counts[card];
                var row = Instantiate(CardRowTemplate, CardsContainer);
                row.name = "CardRow_" + card.name;
                row.SetActive(true);
                _rows.Add(row);

                var icon = FindChild<Image>(row.transform, "Icon");
                if (icon != null)
                {
                    icon.gameObject.SetActive(card.Icon != null);
                    if (card.Icon != null) icon.sprite = card.Icon;
                }

                var title = FindChild<TextMeshProUGUI>(row.transform, "Title");
                if (title != null) title.text = card.Title;

                var count = FindChild<TextMeshProUGUI>(row.transform, "Count");
                if (count != null)
                {
                    count.gameObject.SetActive(n > 1);
                    count.text = "×" + n;
                }

                var desc = FindChild<TextMeshProUGUI>(row.transform, "Desc");
                if (desc != null)
                {
                    string effect = EffectLine(card, n);
                    string text = card.Description ?? "";
                    if (!string.IsNullOrEmpty(effect))
                        text += (text.Length > 0 ? "\n" : "") + $"<color={Green}>{effect}</color>";
                    desc.text = text;
                }
            }

            // Händlerkarten (Fähigkeits-Verbesserungen) mit Gesamtwirkung und aktuellem Wert
            foreach (var kv in merchantCards)
            {
                var card = kv.Key;
                int n = kv.Value;
                var row = Instantiate(CardRowTemplate, CardsContainer);
                row.name = "MerchantRow_" + card.name;
                row.SetActive(true);
                _rows.Add(row);

                var icon = FindChild<Image>(row.transform, "Icon");
                if (icon != null)
                {
                    icon.gameObject.SetActive(card.Icon != null);
                    if (card.Icon != null) icon.sprite = card.Icon;
                }
                var title = FindChild<TextMeshProUGUI>(row.transform, "Title");
                if (title != null) title.text = card.Title;
                var count = FindChild<TextMeshProUGUI>(row.transform, "Count");
                if (count != null)
                {
                    count.gameObject.SetActive(n > 1);
                    count.text = "×" + n;
                }
                var desc = FindChild<TextMeshProUGUI>(row.transform, "Desc");
                if (desc != null) desc.text = $"{MerchantInfo.Name(card.Merchant)}: {card.Description}\n<color={Green}>{MerchantEffectLine(card, n)}</color>";
            }
        }

        // "Gesamt: +40 % · Schwerthieb – Reichweite: 2,6 m → 3,6 m"
        private static string MerchantEffectLine(MerchantCardSO card, int n)
        {
            string total = "Gesamt: " + AbilityMods.FormatValue(card.Stat, card.Value * n);
            // Vorschau rückwärts: Wert ohne diese Karten → jetziger Wert
            if (MerchantManager.TryPreview(card, -card.Value * n, out string label, out float now, out float without, out string unit))
            {
                var pa = PlayerAbilities.Instance;
                string ability = pa != null && pa.ActiveKit != null ? pa.ActiveKit.GetName(card.Ability) : card.Ability.ToString();
                total += $" · {ability} – {label}: {MerchantManager.FormatNumber(without)}{unit} → {MerchantManager.FormatNumber(now)}{unit}";
            }
            return total;
        }

        // Gesamtwirkung aller Exemplare einer Karte
        private static string EffectLine(UpgradeDefinitionSO card, int n)
        {
            if (card.StatToBuff == StatType.BuddySlot)
            {
                int slots = n * Mathf.Max(1, Mathf.RoundToInt(card.Value));
                return $"+{slots} Buddy-Slot" + (slots == 1 ? "" : "s");
            }
            if (card.Type == UpgradeType.Heal)
                return $"Gesamt: {Num(n * card.Value, "0")} HP geheilt";

            string stat = StatName(card) + TargetSuffix(card.Target);
            if (card.StatToBuff == StatType.Cooldown)
            {
                float total = (1f - Mathf.Pow(1f - Mathf.Clamp(card.Value, 0f, 90f) / 100f, n)) * 100f;
                return $"Gesamt: −{Num(total, "0.#")} % {stat}";
            }
            if (card.IsPercentage)
            {
                float total = (Mathf.Pow(1f + card.Value / 100f, n) - 1f) * 100f;
                return $"Gesamt: {Signed(total, "0.#")} % {stat}";
            }
            // Flacher Spieler-Schaden / Mobilität zählt als ganze Prozentpunkte (siehe UpgradeManager)
            if (card.Target == UpgradeTarget.Player && (card.StatToBuff == StatType.Damage || card.StatToBuff == StatType.Mobility))
                return $"Gesamt: {Signed(n * card.Value, "0.#")} % {stat}";
            return $"Gesamt: {Signed(n * card.Value, "0.#")}{StatUnit(card.StatToBuff)} {stat}";
        }

        private static string StatName(UpgradeDefinitionSO card)
        {
            switch (card.StatToBuff)
            {
                case StatType.Damage: return card.Target == UpgradeTarget.Player ? "Fähigkeitsschaden" : "Schaden";
                case StatType.Cooldown: return "Abklingzeit";
                case StatType.Mobility: return "Mobilität";
                case StatType.Range: return "Reichweite";
                case StatType.FireRate: return "Feuerrate";
                case StatType.Health: return "Max. Leben";
                case StatType.Speed: return "Lauftempo";
                case StatType.ManaRegen: return "Mana-Regeneration";
                case StatType.ManaCap: return "Max. Mana";
                default: return "";
            }
        }

        private static string StatUnit(StatType stat)
        {
            switch (stat)
            {
                case StatType.Range: return " m";
                case StatType.FireRate: return "/s";
                case StatType.Health: return " HP";
                case StatType.Speed: return " m/s";
                case StatType.ManaRegen: return "/s";
                default: return "";
            }
        }

        private static string TargetSuffix(UpgradeTarget target)
        {
            switch (target)
            {
                case UpgradeTarget.AllUnits: return " (alle Buddies)";
                case UpgradeTarget.FireUnit: return $" ({ElementInfo.Name(0)}-Buddy)";
                case UpgradeTarget.IceUnit: return $" ({ElementInfo.Name(1)}-Buddy)";
                case UpgradeTarget.EarthUnit: return $" ({ElementInfo.Name(2)}-Buddy)";
                case UpgradeTarget.LightUnit: return $" ({ElementInfo.Name(3)}-Buddy)";
                default: return "";
            }
        }

        // ---------------- Werte ----------------

        private string BuildStats()
        {
            var sb = new StringBuilder();
            var um = UpgradeManager.Instance;

            // Champion + Fähigkeiten (exakte Live-Werte wie im Tooltip)
            var abilities = PlayerAbilities.Instance;
            if (abilities != null && abilities.ActiveKit != null)
            {
                AppendChampion(sb, abilities);
                sb.Append('\n');
            }

            // Spieler
            sb.Append(HeaderOpen).Append("Spieler").Append(HeaderClose).Append('\n');
            if (abilities != null)
            {
                float cur = abilities.DamageMultiplier;
                float bas = um != null ? um.BaseDamageMultiplier : 1f;
                string line = "Fähigkeitsschaden: " + Colored(Num(cur * 100f, "0") + " %", cur > bas + 0.0001f);
                if (!Same(cur, bas)) line += $" ({Signed((cur - bas) * 100f, "0")} %)";
                sb.Append(line).Append('\n');

                float cdBase = um != null ? um.BaseCooldownMultiplier : 1f;
                if (!Same(abilities.CooldownMultiplier, cdBase))
                    sb.Append("Abklingzeiten: ").Append(Colored(Num(abilities.CooldownMultiplier * 100f, "0") + " %", abilities.CooldownMultiplier < cdBase)).Append($" ({Signed((abilities.CooldownMultiplier - cdBase) * 100f, "0")} %)").Append('\n');
                float mobBase = um != null ? um.BaseMobilityMultiplier : 1f;
                if (!Same(abilities.MobilityMultiplier, mobBase))
                    sb.Append("Mobilität: ").Append(Colored(Num(abilities.MobilityMultiplier * 100f, "0") + " %", abilities.MobilityMultiplier > mobBase)).Append($" ({Signed((abilities.MobilityMultiplier - mobBase) * 100f, "0")} %)").Append('\n');
            }

            var controller = um != null && um.PlayerControllerRef != null ? um.PlayerControllerRef : FindFirstObjectByType<PlayerController>();
            if (controller != null)
                sb.Append(StatLine("Lauftempo", controller.MoveSpeed, um != null && um.BasePlayerSpeed > 0f ? um.BasePlayerSpeed : controller.MoveSpeed, "0.#", " m/s")).Append('\n');

            var stats = um != null && um.PlayerStatsRef != null ? um.PlayerStatsRef : FindFirstObjectByType<PlayerStats>();
            if (stats != null)
                sb.Append(StatLine("Max. Leben", stats.MaxHP, um != null && um.BasePlayerMaxHP > 0f ? um.BasePlayerMaxHP : stats.MaxHP, "0", "")).Append('\n');

            var eco = EconomyManager.Instance;
            if (eco != null)
            {
                float baseCap = eco.BaseManaCap > 0f ? eco.BaseManaCap : eco.MaxMana;
                sb.Append(StatLine("Max. Mana", eco.MaxMana, baseCap, "0", "")).Append('\n');

                bool regenChanged = !Same(eco.RegenOut, eco.BaseRegenOut) || !Same(eco.RegenIn, eco.BaseRegenIn);
                bool regenUp = eco.RegenOut > eco.BaseRegenOut + 0.0001f || eco.RegenIn > eco.BaseRegenIn + 0.0001f;
                string regen = Colored(Num(eco.RegenOut, "0.#") + "/s außerhalb, " + Num(eco.RegenIn, "0.#") + "/s im Kampf", regenUp);
                if (regenChanged)
                {
                    string pct = eco.BaseRegenOut > 0f ? $", {Signed((eco.RegenOut / eco.BaseRegenOut - 1f) * 100f, "0")} %" : "";
                    regen += $" (Basis {Num(eco.BaseRegenOut, "0.#")} / {Num(eco.BaseRegenIn, "0.#")}{pct})";
                }
                sb.Append("Mana-Regeneration: ").Append(regen).Append('\n');
            }

            // Buddies
            var im = InteractionManager.Instance;
            if (im != null && im.UnitConfigs != null && im.UnitConfigs.Count > 0)
            {
                sb.Append('\n').Append(HeaderOpen).Append("Buddies").Append(HeaderClose).Append('\n');
                foreach (var cfg in im.UnitConfigs)
                {
                    if (cfg == null) continue;
                    sb.Append(BuddyLine(cfg, um)).Append('\n');
                }

                // Aktive Fusions-Buddies pro Typ
                var fusionCounts = new int[FusionInfo.Names.Length];
                bool anyFusion = false;
                foreach (var b in ElementalBuddy.Active)
                {
                    if (!(b is FusionBuddy f)) continue;
                    fusionCounts[Mathf.Clamp((int)f.Element, 0, fusionCounts.Length - 1)]++;
                    anyFusion = true;
                }
                if (anyFusion)
                {
                    var fusions = new List<string>();
                    for (int i = 0; i < fusionCounts.Length; i++)
                        if (fusionCounts[i] > 0) fusions.Add($"{fusionCounts[i]}× {FusionInfo.Names[i]}");
                    sb.Append("Fusionen: ").Append(string.Join(" · ", fusions)).Append('\n');
                }
            }

            // Buddy-Slots
            var slots = BuddySlotManager.Instance;
            if (slots != null)
            {
                sb.Append('\n').Append(HeaderOpen).Append("Buddy-Slots").Append(HeaderClose).Append('\n');
                if (slots.Unlimited)
                    sb.Append($"Slots: unbegrenzt ({slots.UsedSlots} belegt)");
                else
                    sb.Append($"Slots: {slots.UsedSlots} / {slots.MaxSlots} belegt");

                int fromCards = 0;
                if (um != null)
                {
                    foreach (var up in um.PickedUpgrades)
                        if (up != null && up.StatToBuff == StatType.BuddySlot) fromCards += Mathf.Max(1, Mathf.RoundToInt(up.Value));
                }
                if (fromCards > 0) sb.Append(" · ").Append(Colored($"+{fromCards} durch Karten", true));
                sb.Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }

        // "Champion: Schwertkämpfer" + je Fähigkeit Name, Taste, Kosten und Beschreibung mit Live-Werten
        private static void AppendChampion(StringBuilder sb, PlayerAbilities abilities)
        {
            var kit = abilities.ActiveKit;
            string name = !string.IsNullOrEmpty(kit.DisplayName) ? kit.DisplayName : ChampionName(kit.Class);
            sb.Append(HeaderOpen).Append("Champion: ").Append(name).Append(HeaderClose).Append('\n');
            for (int i = 0; i < AbilitySlots.Count; i++)
            {
                var slot = (AbilitySlot)i;
                AbilityId id = abilities.GetAbility(slot);
                bool unlocked = abilities.IsUnlocked(id);
                sb.Append("<b>").Append(kit.GetName(id)).Append("</b> <size=85%>(").Append(AbilitySlots.ShortKey(slot)).Append(") · ")
                  .Append(kit.GetCostLine(id)).Append("</size>");
                if (!unlocked) sb.Append(" <size=85%><color=#8a2a12>– gesperrt (").Append(ElementInfo.ShrineName(AbilitySlots.ElementOf(slot))).Append(")</color></size>");
                sb.Append('\n');
                sb.Append("<size=85%>").Append(kit.Describe(id, abilities.GetDamageMultiplier(id))).Append("</size>").Append('\n');
            }
        }

        public static string ChampionName(ChampionClass cls)
        {
            switch (cls)
            {
                case ChampionClass.Knight: return "Schwertkämpfer";
                case ChampionClass.Archer: return "Bogenschütze";
                default: return "Magier";
            }
        }

        private static string BuddyLine(UnitConfigSO cfg, UpgradeManager um)
        {
            int element = ElementOf(cfg);
            Color c = Color.Lerp(ElementInfo.GetColor(element), Color.black, 0.3f); // abgedunkelt für hellen Hintergrund
            string label = $"<b><color=#{ColorUtility.ToHtmlStringRGB(c)}>{ElementInfo.Name(element)}</color></b>";

            float baseDmg = cfg.Damage, baseRange = cfg.Range, baseRate = cfg.FireRate;
            if (um != null && um.TryGetBaseUnitStats(cfg, out float d, out float r, out float f))
            {
                baseDmg = d;
                baseRange = r;
                baseRate = f;
            }

            var tank = cfg.Prefab != null ? cfg.Prefab.GetComponentInChildren<TankBuddy>(true) : null;
            var healer = cfg.Prefab != null ? cfg.Prefab.GetComponentInChildren<HealerBuddy>(true) : null;

            var parts = new List<string>();
            if (tank != null)
            {
                // Erd-Buddy: Aura-DPS/Radius aus dem Prefab, Config.FireRate = Spott-Rate
                parts.Add($"Aura-Schaden {Num(tank.AuraDps, "0.#")}/s");
                parts.Add("Spott alle " + Change(Interval(baseRate), Interval(cfg.FireRate), "0.#", " s", true));
                parts.Add($"Radius {Num(tank.TauntRadius, "0.#")} m");
            }
            else
            {
                parts.Add((healer != null ? "Strahl " : "Schaden ") + Change(baseDmg, cfg.Damage, "0.#", "", false));
                parts.Add((healer != null ? "Angriffsrate " : "Feuerrate ") + Change(baseRate, cfg.FireRate, "0.##", "/s", false));
                parts.Add("Reichweite " + Change(baseRange, cfg.Range, "0.#", " m", false));
            }

            float shrine = ShrineBonuses.GetDamageMultiplier(element);
            if (shrine > 1.0001f) parts.Add(Colored($"Schrein {Signed((shrine - 1f) * 100f, "0")} % Schaden", true));

            string line = label + ": " + string.Join(" · ", parts);
            if (tank != null && (!Same(baseDmg, cfg.Damage) || !Same(baseRange, cfg.Range)))
                line += " <i>(Schaden-/Reichweiten-Karten wirken nicht auf Erd-Buddy)</i>";
            return line;
        }

        // "12 → 13.2 (+10 %)" bei Änderung, sonst nur der Wert. lowerIsBetter für Intervalle.
        private static string Change(float bas, float cur, string fmt, string unit, bool lowerIsBetter)
        {
            if (Same(bas, cur)) return Num(cur, fmt) + unit;
            bool better = lowerIsBetter ? cur < bas : cur > bas;
            string pct = bas != 0f ? $" ({Signed((cur / bas - 1f) * 100f, "0")} %)" : "";
            return Num(bas, fmt) + " → " + Colored(Num(cur, fmt) + unit, better) + pct;
        }

        // "Name: <aktuell> (Basis <basis>, +X %)" – Klammer nur bei Abweichung
        private static string StatLine(string label, float cur, float bas, string fmt, string unit)
        {
            string line = label + ": " + Colored(Num(cur, fmt) + unit, cur > bas + 0.0001f);
            if (!Same(cur, bas))
            {
                string pct = bas != 0f ? $", {Signed((cur / bas - 1f) * 100f, "0")} %" : "";
                line += $" (Basis {Num(bas, fmt)}{unit}{pct})";
            }
            return line;
        }

        private static string BuildWaveInfo()
        {
            var wm = WaveManager.Instance;
            string wave;
            if (wm == null) wave = "";
            else if (wm.IsWaveActive) wave = $"Welle {wm.CurrentWaveIndex + 1} läuft";
            else if (wm.CurrentWaveIndex > 0) wave = $"Welle {wm.CurrentWaveIndex} geschafft";
            else wave = "Vor Welle 1";

            int best = GameManager.Instance != null ? GameManager.Instance.BestWave : 0;
            if (best <= 0) return wave;
            return string.IsNullOrEmpty(wave) ? $"Beste Welle: {best}" : $"{wave} · Beste Welle: {best}";
        }

        // ---------------- Helfer ----------------

        // Element über Config-Namen (wie ElementalBuddy.ResolveElementIndex), sonst UnitType
        private static int ElementOf(UnitConfigSO cfg)
        {
            string n = cfg.name.ToLowerInvariant();
            if (n.Contains("fire")) return 0;
            if (n.Contains("ice")) return 1;
            if (n.Contains("earth")) return 2;
            if (n.Contains("light")) return 3;
            int t = (int)cfg.Type;
            return t < ShrineBonuses.ElementCount ? t : 0;
        }

        private static T FindChild<T>(Transform root, string childName) where T : Component
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t.GetComponent<T>();
            return null;
        }

        private static float Interval(float rate) => rate > 0f ? 1f / rate : 0f;
        private static bool Same(float a, float b) => Mathf.Abs(a - b) < 0.0001f;
        private static string Num(float v, string fmt) => v.ToString(fmt, Inv);
        private static string Signed(float v, string fmt) => (v >= 0f ? "+" : "") + v.ToString(fmt, Inv);
        private static string Colored(string s, bool highlight) => highlight ? $"<color={Green}>{s}</color>" : s;
    }
}
