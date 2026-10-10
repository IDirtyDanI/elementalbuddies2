using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Meta-Fortschritt im Hauptmenü (Plan „Fesselung“ E3/E4/E6), zur Laufzeit an MainMenuUI angehängt:
    //  - Leiste unten Mitte: „Nächstes Ziel“ (offener Erfolg mit dem größten Fortschritt, Goal-Gradient) mit Balken und
    //    Belagerungsstufe (< >, gesperrt bis zum ersten Morgengrauen – zeigt von Anfang an, was noch kommt)
    //  - Knopf „Kodex“ neben „Beenden“ und Kodex-Seite (Kopie der Erfolge-Seite) mit Reitern Gegner / Elementare / Karten
    public class MainMenuMeta : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.24f, 0.15f, 0.08f);
        private static readonly Color Soft = new Color(0.42f, 0.3f, 0.18f);
        private static readonly Color Gold = new Color(0.86f, 0.64f, 0.18f);

        private MainMenuUI _menu;
        private TMP_FontAsset _font, _bodyFont;
        private RectTransform _strip;
        private TextMeshProUGUI _goalText, _goalProgress, _siegeText;
        private Image _goalFill;
        private Button _siegeLeft, _siegeRight;

        private Button _codexButton;
        private GameObject _codexPanel;
        private RectTransform _codexContent;
        private TextMeshProUGUI _codexCounter;
        private readonly List<GameObject> _codexRows = new List<GameObject>();
        private readonly List<Button> _tabs = new List<Button>();
        private int _tab;

        public static MainMenuMeta Instance { get; private set; }
        public static bool IsCodexOpen => Instance != null && Instance._codexPanel != null && Instance._codexPanel.activeSelf;

        public static MainMenuMeta Attach(MainMenuUI menu)
        {
            if (menu == null || !Application.isPlaying) return null;
            var meta = menu.GetComponent<MainMenuMeta>();
            if (meta == null) meta = menu.gameObject.AddComponent<MainMenuMeta>();
            meta.Build(menu);
            return meta;
        }

        void OnEnable() => Progression.OnProgressChanged += Refresh;
        void OnDisable() => Progression.OnProgressChanged -= Refresh;
        void OnDestroy() { if (Instance == this) Instance = null; }

        private void Build(MainMenuUI menu)
        {
            Instance = this;
            _menu = menu;
            if (_strip != null) { Refresh(); return; }
            var label = menu.PlayButton != null ? menu.PlayButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            _font = label != null ? label.font : TMP_Settings.defaultFontAsset;
            _bodyFont = menu.DifficultyDescription != null ? menu.DifficultyDescription.font : _font;
            BuildStrip();
            BuildCodex();
            Refresh();
        }

        // ---------------- Leiste: Nächstes Ziel + Belagerungsstufe ----------------

        private Sprite Parchment()
        {
            if (_menu.AchievementsPanel != null)
            {
                var box = _menu.AchievementsPanel.transform.Find("Box");
                var img = box != null ? box.GetComponent<Image>() : null;
                if (img != null) return img.sprite;
            }
            return null;
        }

        private void BuildStrip()
        {
            var parent = _menu.PlayButton != null ? _menu.PlayButton.transform.parent : _menu.transform;
            var go = new GameObject("MetaStrip", typeof(RectTransform));
            _strip = (RectTransform)go.transform;
            _strip.SetParent(parent, false);
            _strip.anchorMin = _strip.anchorMax = new Vector2(0.5f, 0f);
            _strip.pivot = new Vector2(0.5f, 0f);
            _strip.sizeDelta = new Vector2(700f, 150f);
            _strip.anchoredPosition = new Vector2(20f, 46f);
            var bg = go.AddComponent<Image>();
            var parch = Parchment();
            bg.sprite = parch;
            bg.type = parch != null && parch.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            bg.color = parch != null ? Color.white : new Color(0.93f, 0.86f, 0.7f, 0.95f);
            bg.raycastTarget = true;

            _goalText = Text(_strip, "Goal", "", 24f, Ink, TextAlignmentOptions.TopLeft, _bodyFont);
            Place(_goalText.rectTransform, new Vector2(34f, -20f), new Vector2(632f, 34f));
            var bar = new GameObject("GoalBar", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)bar.transform;
            brt.SetParent(_strip, false);
            Place(brt, new Vector2(34f, -60f), new Vector2(470f, 16f));
            bar.GetComponent<Image>().color = new Color(0.45f, 0.33f, 0.2f, 0.35f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var frt = (RectTransform)fill.transform;
            frt.SetParent(brt, false);
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0.5f, 1f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            _goalFill = fill.GetComponent<Image>();
            _goalFill.color = Gold;
            _goalProgress = Text(_strip, "GoalProgress", "", 20f, Soft, TextAlignmentOptions.Left, _bodyFont);
            Place(_goalProgress.rectTransform, new Vector2(516f, -54f), new Vector2(150f, 28f));

            _siegeLeft = SmallButton(_strip, "SiegeLeft", "<", new Vector2(34f, -98f));
            _siegeText = Text(_strip, "Siege", "", 22f, Ink, TextAlignmentOptions.Left, _bodyFont);
            Place(_siegeText.rectTransform, new Vector2(84f, -96f), new Vector2(530f, 40f));
            _siegeRight = SmallButton(_strip, "SiegeRight", ">", new Vector2(622f, -98f));
            _siegeLeft.onClick.AddListener(() => ChangeSiege(-1));
            _siegeRight.onClick.AddListener(() => ChangeSiege(+1));
        }

        private Button SmallButton(RectTransform parent, string name, string text, Vector2 pos)
        {
            var src = _menu.SettingsButton;
            GameObject go;
            if (src != null)
            {
                go = Instantiate(src.gameObject, parent, false);
                go.name = name;
                var b0 = go.GetComponent<Button>();
                b0.onClick = new Button.ButtonClickedEvent();
                var l = go.GetComponentInChildren<TextMeshProUGUI>(true);
                if (l != null) { l.text = text; l.enableAutoSizing = false; l.fontSize = 22f; }
                foreach (var img in go.GetComponentsInChildren<Image>(true))
                    if (img.gameObject != go) img.gameObject.SetActive(false); // Trophäen-Symbol o. Ä. entfernen
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                Text((RectTransform)go.transform, "Text", text, 22f, Ink, TextAlignmentOptions.Center, _font);
            }
            var rt = (RectTransform)go.transform;
            Place(rt, pos, new Vector2(44f, 40f));
            return go.GetComponent<Button>();
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        private TextMeshProUGUI Text(RectTransform parent, string name, string text, float size, Color color, TextAlignmentOptions align, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font != null ? font : _font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }

        private void ChangeSiege(int delta)
        {
            int max = SiegeLevels.MaxSelectable;
            int lvl = Mathf.Clamp(GameSession.SiegeLevel + delta, 0, max);
            if (lvl == GameSession.SiegeLevel) return;
            GameSession.SiegeLevel = lvl;
            GameAudio.Play(SfxId.UiClick);
            if (Net.IsRunning && Net.Manager != null && Net.Manager.IsServer) NetLobby.PushDifficulty(GameSession.DifficultyId);
            Refresh();
        }

        // Nächstliegender offener Erfolg auf der gewählten Stufe: größter Fortschrittsanteil, bei Gleichstand Reihenfolge
        public static AchievementDefinition NextGoal(out int cur, out int target)
        {
            cur = target = 0;
            AchievementDefinition best = null;
            float bestF = -1f;
            foreach (var a in Progression.All)
            {
                if (a == null || Progression.IsAchieved(a, 0)) continue;
                Progression.GetProgress(a, out int c, out int t, 0);
                if (t > 0 && c >= t) continue; // Ziel schon erfüllt, Vergabe erst im Spiel (keine rückwirkende Vergabe)
                float f = t > 0 ? c / (float)t : 0f;
                if (f > bestF) { bestF = f; best = a; cur = c; target = t; }
            }
            return best;
        }

        public void Refresh()
        {
            if (_strip == null) return;
            var goal = NextGoal(out int cur, out int target);
            if (goal != null)
            {
                string reward = goal.IsTrophy ? "" : $"  <color=#7a5a1c>→ {Progression.GetUnlockName(goal.Unlock)}</color>";
                _goalText.text = $"<b>Nächstes Ziel:</b> {goal.Title} – {AchievementDatabaseSO.GetGoalText(goal)}{reward}";
                _goalFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(target > 0 ? cur / (float)target : 0f), 1f);
                _goalProgress.text = $"{cur} / {target}";
                _goalFill.transform.parent.gameObject.SetActive(true);
            }
            else
            {
                _goalText.text = "<b>Alle Erfolge erreicht!</b> " + (SiegeLevels.Unlocked ? "Probiere eine höhere Belagerungsstufe." : "");
                _goalFill.transform.parent.gameObject.SetActive(false);
                _goalProgress.text = "";
            }

            bool unlocked = SiegeLevels.Unlocked;
            int lvl = Mathf.Min(GameSession.SiegeLevel, SiegeLevels.MaxSelectable);
            if (lvl != GameSession.SiegeLevel) GameSession.SiegeLevel = lvl;
            if (!unlocked)
                _siegeText.text = $"<color=#6b5a48><b>Gesperrt:</b> Belagerungsstufen – ab dem ersten Morgengrauen (Welle {SiegeLevels.DawnWave})</color>";
            else if (lvl == 0)
                _siegeText.text = "<b>Belagerung:</b> keine Erschwernis";
            else
            {
                int best = SiegeLevels.BestWave(lvl);
                _siegeText.text = $"<b>Belagerung {lvl}:</b> {SiegeLevels.Names[lvl]}" + (best > 0 ? $"  <color=#6b5a48>(Rekord W{best})</color>" : "");
            }
            _siegeLeft.gameObject.SetActive(unlocked);
            _siegeRight.gameObject.SetActive(unlocked);
            _siegeLeft.interactable = lvl > 0;
            _siegeRight.interactable = lvl < SiegeLevels.MaxSelectable;
            var srt = _siegeText.rectTransform;
            srt.anchoredPosition = new Vector2(unlocked ? 84f : 34f, srt.anchoredPosition.y);
            srt.sizeDelta = new Vector2(unlocked ? 530f : 632f, srt.sizeDelta.y);
        }

        // Kurzinfo der gewählten Belagerungsstufe (Hover über den Text): Erschwernisse
        public string SiegeTooltip => SiegeLevels.Summary(GameSession.SiegeLevel);

        void Update()
        {
            if (_menu == null) return;
            // Leiste und Kodex-Knopf nur im Hauptbildschirm (nicht in Lobby/Beitreten, nicht über Overlays)
            bool main = _menu.IsMainView && (_menu.SettingsPanel == null || !_menu.SettingsPanel.activeSelf)
                        && (_menu.AchievementsPanel == null || !_menu.AchievementsPanel.activeSelf) && !IsCodexOpen;
            if (_strip != null && _strip.gameObject.activeSelf != main) _strip.gameObject.SetActive(main);
            if (_codexButton != null && _menu.QuitButton != null)
            {
                bool vis = _menu.QuitButton.gameObject.activeInHierarchy;
                if (_codexButton.gameObject.activeSelf != vis) _codexButton.gameObject.SetActive(vis);
            }
        }

        // Von MainMenuUI.Update: Tasten, solange der Kodex offen ist; true = verbraucht
        public static bool HandleKeys(Keyboard kb)
        {
            if (!IsCodexOpen || kb == null) return false;
            if (kb.escapeKey.wasPressedThisFrame) Instance.ShowCodex(false);
            else if (kb.qKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) Instance.SelectTab(Instance._tab - 1);
            else if (kb.eKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) Instance.SelectTab(Instance._tab + 1);
            return true;
        }

        // ---------------- Kodex ----------------

        private static readonly string[] TabNames = { "Gegner", "Elementare", "Karten" };

        private void BuildCodex()
        {
            if (_menu.AchievementsPanel == null || _menu.AchievementRowTemplate == null || _menu.QuitButton == null) return;

            // Knopf: „Beenden“ halbiert, „Kodex“ rechts daneben (wie Einstellungen | Erfolge)
            var quit = (RectTransform)_menu.QuitButton.transform;
            var src = _menu.AchievementsButton != null ? _menu.AchievementsButton : _menu.SettingsButton;
            var bgo = Instantiate(src.gameObject, quit.parent, false);
            bgo.name = "CodexButton";
            _codexButton = bgo.GetComponent<Button>();
            _codexButton.onClick = new Button.ButtonClickedEvent();
            _codexButton.onClick.AddListener(() => ShowCodex(true));
            var brt = (RectTransform)bgo.transform;
            var srt = (RectTransform)src.transform;
            float half = srt.sizeDelta.x;
            brt.anchoredPosition = new Vector2(srt.anchoredPosition.x, quit.anchoredPosition.y);
            brt.sizeDelta = new Vector2(half, quit.sizeDelta.y);
            quit.sizeDelta = new Vector2(half, quit.sizeDelta.y);
            var bl = bgo.GetComponentInChildren<TextMeshProUGUI>(true);
            if (bl != null) bl.text = "Kodex";
            foreach (var img in bgo.GetComponentsInChildren<Image>(true))
                if (img.gameObject != bgo) img.gameObject.SetActive(false); // Pokal-Symbol des Erfolge-Knopfs

            // Seite: Kopie der Erfolge-Seite
            _codexPanel = Instantiate(_menu.AchievementsPanel, _menu.AchievementsPanel.transform.parent, false);
            _codexPanel.name = "CodexPanel";
            _codexPanel.SetActive(false);
            var box = _codexPanel.transform.Find("Box");
            var title = box != null ? box.Find("Ribbon/Title") : null;
            if (title != null) title.GetComponent<TextMeshProUGUI>().text = "Kodex";
            var info = box != null ? box.Find("Info") : null;
            if (info != null) info.gameObject.SetActive(false);
            var counter = box != null ? box.Find("Counter") : null;
            _codexCounter = counter != null ? counter.GetComponent<TextMeshProUGUI>() : null;
            var scroll = _codexPanel.GetComponentInChildren<ScrollRect>(true);
            _codexContent = scroll != null ? scroll.content : null;
            if (_codexContent != null)
                for (int i = _codexContent.childCount - 1; i >= 0; i--) Destroy(_codexContent.GetChild(i).gameObject);
            var back = box != null ? box.Find("BackButton") : null;
            if (back != null)
            {
                var b = back.GetComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() => ShowCodex(false));
            }

            // Reiter oben (statt Info-Zeile), aus dem Zurück-Knopf geklont
            if (back != null && box != null)
                for (int i = 0; i < TabNames.Length; i++)
                {
                    var t = Instantiate(back.gameObject, box, false);
                    t.name = "Tab" + i;
                    var rt = (RectTransform)t.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.sizeDelta = new Vector2(250f, 64f);
                    rt.anchoredPosition = new Vector2(72f + i * 266f, -12f);
                    var tb = t.GetComponent<Button>();
                    tb.onClick = new Button.ButtonClickedEvent();
                    int idx = i;
                    tb.onClick.AddListener(() => SelectTab(idx));
                    var tl = t.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (tl != null) tl.text = TabNames[i];
                    _tabs.Add(tb);
                }
        }

        public void ShowCodex(bool show)
        {
            if (_codexPanel == null) return;
            _codexPanel.SetActive(show);
            if (show) SelectTab(_tab);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void SelectTab(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, TabNames.Length - 1);
            for (int i = 0; i < _tabs.Count; i++)
            {
                var img = _tabs[i].GetComponent<Image>();
                if (img != null) img.color = i == _tab ? new Color(1f, 0.88f, 0.6f) : new Color(0.82f, 0.78f, 0.72f);
            }
            FillCodex();
        }

        private bool InTab(CodexDatabaseSO.Category c)
        {
            switch (_tab)
            {
                case 0: return c == CodexDatabaseSO.Category.Enemy || c == CodexDatabaseSO.Category.Boss
                               || c == CodexDatabaseSO.Category.Elite || c == CodexDatabaseSO.Category.Event;
                case 1: return c == CodexDatabaseSO.Category.Fusion || c == CodexDatabaseSO.Category.Super;
                default: return c == CodexDatabaseSO.Category.Card;
            }
        }

        private static string CategoryName(CodexDatabaseSO.Category c)
        {
            switch (c)
            {
                case CodexDatabaseSO.Category.Enemy: return "Gegner";
                case CodexDatabaseSO.Category.Boss: return "Boss";
                case CodexDatabaseSO.Category.Elite: return "Elite-Eigenschaft";
                case CodexDatabaseSO.Category.Event: return "Wellen-Ereignis";
                case CodexDatabaseSO.Category.Fusion: return "Fusion";
                case CodexDatabaseSO.Category.Super: return "Super-Elementar";
                default: return "Wellenkarte";
            }
        }

        private void FillCodex()
        {
            foreach (var r in _codexRows) if (r != null) Destroy(r);
            _codexRows.Clear();
            var db = CodexDatabaseSO.Instance;
            Codex.Count(null, out int foundAll, out int totalAll);
            if (_codexCounter != null) _codexCounter.text = $"{foundAll} / {totalAll}";
            if (db == null || _codexContent == null) return;

            foreach (var e in db.Entries)
            {
                if (e == null || !InTab(e.Category)) continue;
                bool known = Codex.IsDiscovered(e.Key);
                var row = Instantiate(_menu.AchievementRowTemplate, _codexContent);
                row.name = "Codex_" + e.Key;
                row.SetActive(true);
                _codexRows.Add(row);
                var t = row.transform;

                var bg = row.GetComponent<Image>();
                if (bg != null) bg.color = known ? _menu.AchievedRowColor : _menu.OpenRowColor;
                var icon = t.Find("Medal/MedalIcon")?.GetComponent<Image>();
                if (icon != null)
                {
                    icon.sprite = e.Icon;
                    icon.enabled = e.Icon != null;
                    icon.preserveAspect = true;
                    icon.color = known ? Color.white : new Color(0.08f, 0.07f, 0.07f, 0.85f); // Silhouette
                }
                var title = t.Find("Title")?.GetComponent<TextMeshProUGUI>();
                if (title != null) title.text = known ? e.Title : "???";
                var desc = t.Find("Desc")?.GetComponent<TextMeshProUGUI>();
                if (desc != null)
                {
                    desc.text = known ? e.Description : "Noch nicht entdeckt – begegne ihm im Spiel.";
                    desc.textWrappingMode = TextWrappingModes.Normal;
                    desc.enableAutoSizing = true;
                    desc.fontSizeMin = 16f;
                    desc.rectTransform.sizeDelta = new Vector2(desc.rectTransform.sizeDelta.x, 70f);
                }
                foreach (var n in new[] { "Bar", "Progress", "Tiers", "RewardIcon" })
                {
                    var c = t.Find(n);
                    if (c != null) c.gameObject.SetActive(false);
                }
                var rl = t.Find("RewardLabel")?.GetComponent<TextMeshProUGUI>();
                var rn = t.Find("RewardName")?.GetComponent<TextMeshProUGUI>();
                if (rl != null) rl.text = CategoryName(e.Category);
                if (rn != null)
                {
                    string extra = known ? e.Extra : "";
                    if (e.Category == CodexDatabaseSO.Category.Card && known)
                    {
                        string cardName = e.Key.Substring(5);
                        int taken = PlayerPrefs.GetInt("codex_taken_" + cardName, 0);
                        extra = (string.IsNullOrEmpty(extra) ? "" : extra + " · ") + (taken > 0 ? $"{taken}× genommen" : "gesehen");
                    }
                    rn.text = extra ?? "";
                }
            }
            Canvas.ForceUpdateCanvases();
            var scroll = _codexPanel.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }
    }
}
