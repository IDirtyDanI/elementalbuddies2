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

        [Header("Rückblick (Plan Fesselung E1/E2)")]
        public float RecapWidth = 720f;
        public float RecapMinHeight = 640f;
        public float RecapGap = 30f;

        private int _bestAtStart;

        void Start()
        {
            if (Panel != null) Panel.SetActive(false);
            RunStats.Ensure();
            _bestAtStart = GameManager.Instance != null ? GameManager.Instance.BestWave : 0;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameOver += ShowGameOver;
            }
            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnDawn += ShowDawn;
                WaveManager.Instance.OnDawnContinued += HideDawn;
            }

            if (RestartButton != null)
            {
                RestartButton.onClick.RemoveAllListeners();
                RestartButton.onClick.AddListener(() =>
                {
                    // Neustart nur beim Host (lädt die Spielszene für alle neu)
                    if (Net.IsServer && GameManager.Instance != null) GameManager.Instance.Restart();
                });
                _restartLabel = RestartButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (_restartLabel != null) _restartText = _restartLabel.text;
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
                    if (Net.CanPauseTime) Time.timeScale = 1f;
                    AudioListener.pause = false;
                    if (Net.IsRunning) NetSession.Instance.LeaveToMenu();
                    else SceneManager.LoadScene(GameSession.MenuScene);
                });
            }
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameOver -= ShowGameOver;
            }
            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnDawn -= ShowDawn;
                WaveManager.Instance.OnDawnContinued -= HideDawn;
            }
        }

        // ---------------- Morgengrauen (Plan Fesselung E5): gleiches Panel im Sieg-Modus ----------------

        private bool _dawnMode;
        private string _reasonDefault, _menuLabelDefault;

        private void ShowDawn()
        {
            if (Panel == null || (GameManager.Instance != null && GameManager.Instance.IsGameOver)) return;
            _dawnMode = true;
            Panel.SetActive(true);
            if (Net.CanPauseTime) Time.timeScale = 0f;

            int level = SiegeLevels.Current;
            var diff = WaveManager.Instance != null ? WaveManager.Instance.Difficulty : null;
            if (ReasonText != null)
            {
                if (_reasonDefault == null) _reasonDefault = ReasonText.text;
                ReasonText.text = "<color=#b5651d><b>Morgengrauen!</b></color>\nDie Stadt hat die Nacht überstanden.";
            }
            string where = diff != null ? diff.DisplayName : "";
            if (level > 0) where += $" · Belagerungsstufe {level}";
            if (WaveText != null) WaveText.text = $"Welle {SiegeLevels.DawnWave} überstanden ({where})";
            if (BestText != null)
            {
                bool next = level < SiegeLevels.Max && SiegeLevels.HighestWon >= level && SiegeLevels.Unlocked;
                BestText.text = next
                    ? $"<color=#2e6b2e><b>Belagerungsstufe {level + 1} – „{SiegeLevels.Names[level + 1]}“ freigeschaltet!</b></color>"
                    : level >= SiegeLevels.Max ? "<b>Die höchste Belagerungsstufe ist gemeistert!</b>" : "Weiter geht es endlos – wie lange hält die Stadt?";
            }
            ShowEarnedAchievements();
            ShowRecap(SiegeLevels.DawnWave);

            // Knöpfe: „Weiter (Endlos)“ (Host) und „Beenden“
            if (RestartButton != null)
            {
                RestartButton.onClick.RemoveAllListeners();
                RestartButton.onClick.AddListener(() => NetGame.RequestDawnContinue());
                RestartButton.interactable = Net.IsServer;
                if (_restartLabel != null) _restartLabel.text = Net.IsServer ? "Weiter (Endlos)" : "Warte auf Host …";
            }
            if (MainMenuButton != null)
            {
                var l = MainMenuButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (l != null) { if (_menuLabelDefault == null) _menuLabelDefault = l.text; l.text = "Beenden"; }
            }
        }

        private void HideDawn()
        {
            if (!_dawnMode) return;
            _dawnMode = false;
            if (Panel != null) Panel.SetActive(false);
            if (Net.CanPauseTime && !UpgradeManager.IsLocalChoosing) Time.timeScale = 1f;
            if (ReasonText != null && _reasonDefault != null) ReasonText.text = _reasonDefault;
            if (RestartButton != null)
            {
                RestartButton.onClick.RemoveAllListeners();
                RestartButton.onClick.AddListener(() =>
                {
                    if (Net.IsServer && GameManager.Instance != null) GameManager.Instance.Restart();
                });
                if (_restartLabel != null) _restartLabel.text = _restartText;
            }
            if (MainMenuButton != null && _menuLabelDefault != null)
            {
                var l = MainMenuButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (l != null) l.text = _menuLabelDefault;
            }
        }

        private TextMeshProUGUI _restartLabel;
        private string _restartText;

        private void ShowGameOver(string reason)
        {
            if (_dawnMode) HideDawn();
            if (Panel != null) Panel.SetActive(true);

            // Clients: Neustart entscheidet der Host
            if (RestartButton != null)
            {
                bool host = Net.IsServer;
                RestartButton.interactable = host;
                if (_restartLabel != null)
                {
                    _restartLabel.text = host ? _restartText : "Warte auf Host …";
                    _restartLabel.enableAutoSizing = !host || _restartLabel.enableAutoSizing;
                }
            }

            int wave = GameManager.Instance != null ? GameManager.Instance.LastWaveReached : 0;
            int best = GameManager.Instance != null ? GameManager.Instance.BestWave : 0;

            if (ReasonText != null) ReasonText.text = reason;
            var diff = WaveManager.Instance != null ? WaveManager.Instance.Difficulty : null;
            if (WaveText != null) WaveText.text = diff != null ? $"Welle erreicht: {wave} ({diff.DisplayName})" : $"Welle erreicht: {wave}";
            if (BestText != null)
                BestText.text = wave > _bestAtStart && _bestAtStart > 0
                    ? $"<color=#2e6b2e><b>Neuer Rekord!</b></color>  (vorher Welle {_bestAtStart})"
                    : $"Beste Welle: {best}";
            ShowEarnedAchievements();
            ShowRecap(wave);
        }

        // ---------------- Rückblick: Beinahe-Momente + Statistik ----------------

        private static readonly System.Globalization.CultureInfo De = new System.Globalization.CultureInfo("de-DE");
        private TextMeshProUGUI _recapText;

        private void ShowRecap(int wave)
        {
            var box = Panel != null ? Panel.transform.Find("Box") as RectTransform : null;
            if (box == null) return;
            if (_recapText == null) BuildRecapPanel(box);
            if (_recapText == null) return;

            var sb = new System.Text.StringBuilder();
            var near = _dawnMode ? new System.Collections.Generic.List<string>() : NearMisses(wave);
            if (near.Count > 0)
            {
                sb.Append("<b><color=#7a1f12>Knapp!</color></b>\n");
                foreach (var n in near) sb.Append("• ").Append(n).Append('\n');
                sb.Append('\n');
            }

            var rs = RunStats.Instance;
            if (rs != null)
            {
                int min = Mathf.FloorToInt(rs.PlaySeconds / 60f), sec = Mathf.FloorToInt(rs.PlaySeconds % 60f);
                sb.Append($"<b>Spielzeit</b> {min}:{sec:00} min   ·   <b>Besiegt</b> {rs.Kills.ToString("N0", De)}");
                if (rs.BossesKilled.Count > 0) sb.Append($"   ·   <b>Bosse</b> {rs.BossesKilled.Count}");
                if (rs.EliteKills > 0) sb.Append($"   ·   <b>Eliten</b> {rs.EliteKills}");
                if (GameManager.Instance != null && GameManager.Instance.TeamWipes > 0) sb.Append($"   ·   <b>Wiederbelebt</b> {GameManager.Instance.TeamWipes}×");
                sb.Append('\n');

                float total = rs.ChampionDamage + rs.TowerDamage;
                if (total > 0f && Net.IsServer)
                    sb.Append($"<b>Schaden</b> {Num(total)}: Champion {rs.ChampionDamage / total * 100f:0} % · Buddies {rs.TowerDamage / total * 100f:0} %\n");
                else if (total > 0f)
                    sb.Append($"<b>Schaden</b> {Num(total)}\n");

                if (rs.TryGetBestBuddy(out string best, out float dmg, out float share))
                    sb.Append($"<b>Bester Buddy</b> {best} – {Num(dmg)} Schaden ({share * 100f:0} %)\n");

                sb.Append($"<b>Seelensplitter</b> {Num(rs.ShardsEarned)} verdient");
                if (rs.EarlyStarts > 0) sb.Append($" (davon {Num(rs.EarlyCallBonusTotal)} durch {rs.EarlyStarts}× früh gestartet)");
                sb.Append('\n');
                sb.Append($"<b>Buddies</b> {rs.BuddiesBuilt} gebaut");
                if (rs.Fusions > 0) sb.Append($" · {rs.Fusions} Fusionen");
                sb.Append('\n');

                if (rs.Leaks.Count > 0)
                {
                    int leaks = 0; string worst = null; int worstN = 0;
                    foreach (var kv in rs.Leaks) { leaks += kv.Value; if (kv.Value > worstN) { worstN = kv.Value; worst = kv.Key; } }
                    sb.Append($"<b>Zum Nexus durchgebrochen</b> {leaks} Gegner (am häufigsten {worst})\n");
                }
                if (rs.Cards.Count > 0)
                {
                    var parts = new System.Collections.Generic.List<string>();
                    foreach (var kv in rs.Cards) parts.Add(kv.Value > 1 ? $"{kv.Key} ×{kv.Value}" : kv.Key);
                    sb.Append($"<b>Karten</b> {string.Join(", ", parts)}\n");
                }
            }
            _recapText.text = sb.ToString().TrimEnd('\n');
        }

        // Ehrliche Beinahe-Momente (nur echte Werte): Boss fast besiegt, Rekord nah, Erfolg nah
        private System.Collections.Generic.List<string> NearMisses(int wave)
        {
            var list = new System.Collections.Generic.List<string>();
            // Alle lebenden Bosse (ActiveBosses füllt sich erst in Start – ein gerade erschienener Boss fehlt dort noch)
            foreach (var b in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (b == null || !b.IsBoss || b.IsDead || b.MaxHP <= 0f) continue;
                float pct = b.CurrentHP / b.MaxHP * 100f;
                if (pct <= 35f) list.Add($"{b.DisplayName} hatte nur noch {Mathf.Max(1f, pct):0} % Leben");
            }
            if (_bestAtStart > 0 && wave < _bestAtStart && _bestAtStart - wave <= 3)
            {
                int d = _bestAtStart - wave;
                list.Add($"Nur {d} {(d == 1 ? "Welle" : "Wellen")} bis zu deinem Rekord (Welle {_bestAtStart})");
            }
            AchievementDefinition next = null;
            int nextDiff = int.MaxValue;
            foreach (var a in Progression.All)
            {
                if (a == null || a.Condition != AchievementCondition.ReachWave || Progression.IsAchieved(a)) continue;
                int d = a.Threshold - wave;
                if (d > 0 && d < nextDiff) { nextDiff = d; next = a; }
            }
            if (next != null && nextDiff <= 5)
            {
                string unlock = !next.IsTrophy && Progression.Database != null ? $" – schaltet {Progression.Database.GetUnlockName(next.Unlock)} frei" : "";
                list.Add($"Noch {nextDiff} {(nextDiff == 1 ? "Welle" : "Wellen")} bis zum Erfolg „{next.Title}“{unlock}");
            }
            return list;
        }

        private static string Num(float v) => v >= 10000f ? (v / 1000f).ToString("0.#", De) + "k" : Mathf.RoundToInt(v).ToString("N0", De);

        // Zweite Pergament-Box rechts neben der Game-Over-Box (Ribbon „Rückblick“, ein Textblock)
        private void BuildRecapPanel(RectTransform box)
        {
            var boxImg = box.GetComponent<Image>();
            float h = Mathf.Max(RecapMinHeight, box.sizeDelta.y);
            float total = box.sizeDelta.x + RecapGap + RecapWidth;
            box.anchoredPosition = new Vector2(-total * 0.5f + box.sizeDelta.x * 0.5f, box.anchoredPosition.y);

            var go = new GameObject("Recap", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(box.parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(RecapWidth, h);
            rt.anchoredPosition = new Vector2(total * 0.5f - RecapWidth * 0.5f, box.anchoredPosition.y);
            var img = go.AddComponent<Image>();
            if (boxImg != null) { img.sprite = boxImg.sprite; img.type = boxImg.type; img.color = boxImg.color; }

            var ribbon = box.Find("Ribbon");
            if (ribbon != null)
            {
                var r = Instantiate(ribbon.gameObject, rt);
                r.name = "Ribbon";
                var title = r.GetComponentInChildren<TextMeshProUGUI>(true);
                if (title != null) title.text = "Rückblick";
                var rr = (RectTransform)r.transform;
                rr.sizeDelta = new Vector2(Mathf.Min(rr.sizeDelta.x, RecapWidth - 80f), rr.sizeDelta.y);
            }

            _recapText = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            var trt = _recapText.rectTransform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(52f, 40f);
            trt.offsetMax = new Vector2(-52f, -96f);
            var refText = ReasonText != null ? ReasonText : WaveText;
            if (refText != null) _recapText.font = refText.font;
            _recapText.color = new Color(0.23f, 0.14f, 0.07f);
            _recapText.fontSize = 32f;
            _recapText.enableAutoSizing = true;
            _recapText.fontSizeMin = 20f;
            _recapText.fontSizeMax = 32f;
            _recapText.alignment = TextAlignmentOptions.TopLeft;
            _recapText.richText = true;
            _recapText.raycastTarget = false;
            _recapText.lineSpacing = 6f;
        }

        // "In diesem Spiel erreichte Erfolge (Normal): Verteidiger, Zwillingskraft" (nur wenn welche erreicht wurden)
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
            AchievementsText.text = $"<b>In diesem Spiel erreichte Erfolge ({Progression.RankName(Progression.SnapshotRank)}):</b>\n{string.Join(", ", names)}";
            AchievementsText.gameObject.SetActive(true);
        }
    }
}
