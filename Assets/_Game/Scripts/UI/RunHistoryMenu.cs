using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Hauptmenü → „Bestenliste“ (2026-11-18): die eigenen Läufe (RunHistory), zur Laufzeit an MainMenuUI angehängt –
    // „Szene neu bauen“ verliert also nichts.
    //  - Knopf „Bestenliste“ in der Reihe Einstellungen | Erfolge | Bestenliste (drei gleich breite Knöpfe; die Reihe darunter
    //    Beenden | Kodex | Shop bleibt unverändert, auf iOS dort nur Kodex | Shop).
    //  - Seite als Kopie der Erfolge-Seite (wie der Kodex): Reiter „Alle“ + je Map (nur bei mehreren Maps), Liste mit Rang,
    //    Champion-Porträt, Welle, Champion · Map · Stufe · Belagerung, Dauer/Kills, Datum. Per Werbevideo fortgesetzte Läufe
    //    (Nexus wiederbelebt) sind getönt und tragen ▶; Legende unten links. Leerer Zustand: „Noch keine Läufe“.
    // Bedienung: Esc / Android-Zurück / Gamepad B schließt, Q/E bzw. ←/→ und LB/RB wechseln den Map-Reiter; Liste scrollt (Touch: wischen).
    public class RunHistoryMenu : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.24f, 0.15f, 0.08f);
        private static readonly Color Soft = new Color(0.42f, 0.3f, 0.18f);
        private static readonly Color ContinuedColor = new Color(0.72f, 0.27f, 0.08f);
        private static readonly Color ContinuedRow = new Color(1f, 0.84f, 0.74f, 1f);
        private static readonly Color DawnColor = new Color(0.2f, 0.45f, 0.18f);
        private const float RowHeight = 152f; // wie die Erfolge-Zeile (≥ 48 dp auch auf dem Handy)

        public static RunHistoryMenu Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._panel != null && Instance._panel.activeSelf;

        private MainMenuUI _menu;
        private Button _button;
        private GameObject _panel;
        private RectTransform _content;
        private ScrollRect _scroll;
        private TextMeshProUGUI _counter, _empty, _legend;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<Button> _tabs = new List<Button>();
        private readonly List<string> _tabMaps = new List<string>(); // null = alle Maps
        private int _tab;
        private string _mark = RunHistoryRules.ContinuedMark;

        public static RunHistoryMenu Attach(MainMenuUI menu)
        {
            if (menu == null || !Application.isPlaying) return null;
            var m = menu.GetComponent<RunHistoryMenu>();
            if (m == null) m = menu.gameObject.AddComponent<RunHistoryMenu>();
            m.Build(menu);
            return m;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        private void Build(MainMenuUI menu)
        {
            Instance = this;
            _menu = menu;
            if (_panel != null) return;
            if (menu.AchievementsPanel == null || menu.AchievementRowTemplate == null || menu.AchievementsButton == null
                || menu.SettingsButton == null) return;
            BuildButton();
            BuildPage();
        }

        // ---------------- Knopf: Einstellungen | Erfolge | Bestenliste ----------------

        private void BuildButton()
        {
            var settings = (RectTransform)_menu.SettingsButton.transform;
            var ach = (RectTransform)_menu.AchievementsButton.transform;
            var go = Instantiate(ach.gameObject, ach.parent, false);
            go.name = "RunHistoryButton";
            // direkt hinter „Erfolge“: die Overlays (Einstellungen, Erfolge, Kodex, Beitreten) liegen darüber
            go.transform.SetSiblingIndex(ach.GetSiblingIndex() + 1);
            _button = go.GetComponent<Button>();
            _button.onClick = new Button.ButtonClickedEvent();
            _button.onClick.AddListener(() => Show(true));
            var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = "Bestenliste";
            foreach (var img in go.GetComponentsInChildren<Image>(true))
                if (img.gameObject != go && img.name != "TouchPad") img.gameObject.SetActive(false); // Pokal-Symbol des Erfolge-Knopfs

            var row = new List<RectTransform> { settings, ach, (RectTransform)go.transform };
            float left = settings.anchoredPosition.x;
            float right = Mathf.Max(settings.anchoredPosition.x + settings.sizeDelta.x, ach.anchoredPosition.x + ach.sizeDelta.x);
            const float gap = 12f;
            float w = (right - left - gap * (row.Count - 1)) / row.Count;
            for (int i = 0; i < row.Count; i++)
            {
                row[i].anchoredPosition = new Vector2(left + i * (w + gap), settings.anchoredPosition.y);
                row[i].sizeDelta = new Vector2(w, settings.sizeDelta.y);
                var l = row[i].GetComponentInChildren<TextMeshProUGUI>(true);
                if (l == null) continue;
                // Text innerhalb des Holzrahmens halten (schmalere Knöpfe); Erfolge: Rand links für das kleinere Pokal-Symbol
                l.margin = row[i] == ach ? new Vector4(54f, 0f, 10f, 0f) : new Vector4(14f, 0f, 14f, 0f);
                if (!l.enableAutoSizing) l.fontSizeMax = l.fontSize;
                l.enableAutoSizing = true;
                l.fontSizeMin = Mathf.Min(l.fontSizeMin > 0f ? l.fontSizeMin : l.fontSize, 18f);
            }
            var icon = ach.Find("Icon") as RectTransform;
            if (icon != null)
            {
                icon.sizeDelta = new Vector2(44f, 44f);
                icon.anchoredPosition = new Vector2(32f, icon.anchoredPosition.y);
            }
        }

        // ---------------- Seite (Kopie der Erfolge-Seite) ----------------

        private void BuildPage()
        {
            _panel = Instantiate(_menu.AchievementsPanel, _menu.AchievementsPanel.transform.parent, false);
            _panel.name = "RunHistoryPanel";
            _panel.SetActive(false);
            var box = _panel.transform.Find("Box") as RectTransform;
            var title = box != null ? box.Find("Ribbon/Title") : null;
            if (title != null) title.GetComponent<TextMeshProUGUI>().text = "Bestenliste";
            var info = box != null ? box.Find("Info") : null;
            var counter = box != null ? box.Find("Counter") : null;
            _counter = counter != null ? counter.GetComponent<TextMeshProUGUI>() : null;
            _scroll = _panel.GetComponentInChildren<ScrollRect>(true);
            _content = _scroll != null ? _scroll.content : null;
            if (_content != null)
                for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            var back = box != null ? box.Find("BackButton") : null;
            if (back != null)
            {
                var b = back.GetComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() => Show(false));
            }

            // Map-Reiter (nur bei mehreren Maps), aus dem Zurück-Knopf geklont wie im Kodex
            _tabMaps.Add(null);
            if (MapRegistry.All.Count > 1) foreach (var m in MapRegistry.All) if (m != null) _tabMaps.Add(m.Id);
            if (_tabMaps.Count > 1 && back != null && box != null)
            {
                if (info != null) info.gameObject.SetActive(false);
                for (int i = 0; i < _tabMaps.Count; i++)
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
                    tb.onClick.AddListener(() => SelectTab(idx, true));
                    var tl = t.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (tl != null) tl.text = _tabMaps[i] == null ? "Alle" : MapName(_tabMaps[i], _tabMaps[i]);
                    _tabs.Add(tb);
                }
            }
            else if (info != null)
            {
                var it = info.GetComponent<TextMeshProUGUI>();
                if (it != null) it.text = "Deine besten Läufe";
            }

            // Zeichen für „per Video fortgesetzt“: ▶, sonst ►/»/> (je nach Schrift)
            var rowTitle = _menu.AchievementRowTemplate.transform.Find("Title")?.GetComponent<TextMeshProUGUI>();
            _mark = PickMark(rowTitle != null ? rowTitle.font : null);

            var refText = rowTitle;
            if (box != null)
            {
                // Legende unten links neben „Zurück“
                _legend = MakeText(box, "Legend", 24f, Soft, TextAlignmentOptions.Left, refText);
                var lr = _legend.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(0f, 0f);
                lr.pivot = new Vector2(0f, 0f);
                lr.anchoredPosition = new Vector2(72f, 52f);
                lr.sizeDelta = new Vector2(560f, 60f);
                _legend.enableAutoSizing = true;
                _legend.fontSizeMin = 16f;
                _legend.fontSizeMax = 24f;
                _legend.textWrappingMode = TextWrappingModes.Normal;
                _legend.text = $"<color=#{ColorUtility.ToHtmlStringRGB(ContinuedColor)}><b>{_mark}</b></color> = Nexus per Video wiederbelebt";
            }
            if (_scroll != null)
            {
                // Leerer Zustand mitten in der Liste
                _empty = MakeText((RectTransform)_scroll.transform, "Empty", 34f, Soft, TextAlignmentOptions.Center, refText);
                _empty.textWrappingMode = TextWrappingModes.Normal;
                _empty.text = "Noch keine Läufe.\n<size=75%>Spiele eine Runde – wie weit du kommst, steht dann hier.</size>";
                _empty.gameObject.SetActive(false);
            }
        }

        private static string PickMark(TMP_FontAsset font)
        {
            foreach (var c in new[] { '▶', '►', '»' })
                if (font != null && font.HasCharacter(c, true, true)) return c.ToString();
            return ">";
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, string name, float size, Color color, TextAlignmentOptions align, TextMeshProUGUI fontFrom)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (fontFrom != null) t.font = fontFrom.font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }

        // ---------------- Öffnen / Schließen / Tasten ----------------

        public void Show(bool show)
        {
            if (_panel == null) return;
            if (show && !_menu.IsMainView) return;
            if (_panel.activeSelf != show) GameAudio.Play(SfxId.UiClick);
            _panel.SetActive(show);
            if (show) SelectTab(_tab, false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (_menu == null) return;
            if (_button != null && _menu.AchievementsButton != null)
            {
                // nur im Hauptbildschirm (wie „Erfolge“), nicht in Lobby/Beitreten
                bool vis = _menu.AchievementsButton.gameObject.activeInHierarchy;
                if (_button.gameObject.activeSelf != vis) _button.gameObject.SetActive(vis);
            }
            if (!IsOpen) return;
            if (!_menu.IsMainView) { Show(false); return; }
            var gp = Gamepad.current;
            if (gp != null)
            {
                if (gp.buttonEast.wasPressedThisFrame) Show(false);
                else if (gp.leftShoulder.wasPressedThisFrame) SelectTab(_tab - 1, true);
                else if (gp.rightShoulder.wasPressedThisFrame) SelectTab(_tab + 1, true);
            }
        }

        // Von MainMenuUI.Update: Tasten, solange die Bestenliste offen ist; true = verbraucht
        public static bool HandleKeys(Keyboard kb)
        {
            if (!IsOpen || kb == null) return false;
            if (kb.escapeKey.wasPressedThisFrame) Instance.Show(false);
            else if (kb.qKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) Instance.SelectTab(Instance._tab - 1, true);
            else if (kb.eKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) Instance.SelectTab(Instance._tab + 1, true);
            return true;
        }

        private void SelectTab(int tab, bool click)
        {
            int prev = _tab;
            _tab = Mathf.Clamp(tab, 0, _tabMaps.Count - 1);
            if (click && prev != _tab) GameAudio.Play(SfxId.UiClick);
            for (int i = 0; i < _tabs.Count; i++)
            {
                var img = _tabs[i].GetComponent<Image>();
                if (img != null) img.color = i == _tab ? new Color(1f, 0.88f, 0.6f) : new Color(0.82f, 0.78f, 0.72f);
            }
            Fill();
        }

        // ---------------- Liste ----------------

        private static string MapName(string id, string fallback)
        {
            var m = MapRegistry.Find(id);
            return m != null && !string.IsNullOrEmpty(m.DisplayName) ? m.DisplayName : (string.IsNullOrEmpty(fallback) ? "?" : fallback);
        }

        private static ChampionDefinitionSO Champion(int cls)
        {
            var roster = ChampionRosterSO.Load();
            return roster != null ? roster.Find((ChampionClass)cls) : null;
        }

        public void Fill()
        {
            foreach (var r in _rows) if (r != null) Destroy(r);
            _rows.Clear();
            if (_content == null) return;

            var all = RunHistory.Load();
            string map = _tabMaps.Count > 0 ? _tabMaps[_tab] : null;
            var ranked = RunHistoryRules.Ranked(all, map);
            if (_counter != null) _counter.text = ranked.Count == 1 ? "1 Lauf" : $"{ranked.Count} Läufe";
            if (_empty != null)
            {
                _empty.gameObject.SetActive(ranked.Count == 0);
                if (ranked.Count == 0)
                    _empty.text = all.Count == 0
                        ? "Noch keine Läufe.\n<size=75%>Spiele eine Runde – wie weit du kommst, steht dann hier.</size>"
                        : $"Noch keine Läufe auf {MapName(map, map)}.";
            }
            if (_legend != null)
            {
                bool any = false;
                foreach (var r in ranked) any |= r.Continued;
                _legend.gameObject.SetActive(any);
            }

            long newest = long.MinValue;
            foreach (var r in all) if (r.Time > newest) newest = r.Time;
            for (int i = 0; i < ranked.Count; i++) AddRow(ranked[i], i + 1, ranked[i].Time == newest && all.Count > 1);

            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private void AddRow(RunRecord r, int rank, bool newest)
        {
            var row = Instantiate(_menu.AchievementRowTemplate, _content);
            row.name = "Run_" + rank;
            row.SetActive(true);
            _rows.Add(row);
            var t = row.transform;
            var rrt = (RectTransform)t;
            rrt.sizeDelta = new Vector2(rrt.sizeDelta.x, RowHeight);
            var le = row.GetComponent<LayoutElement>();
            if (le != null) { le.preferredHeight = RowHeight; le.minHeight = RowHeight; }

            var bg = row.GetComponent<Image>();
            if (bg != null) bg.color = r.Continued ? ContinuedRow : rank <= 3 ? _menu.AchievedRowColor : _menu.OpenRowColor;

            foreach (var n in new[] { "Bar", "Tiers", "RewardIcon" })
            {
                var c = t.Find(n);
                if (c != null) c.gameObject.SetActive(false);
            }

            var def = Champion(r.Champion);
            string champ = def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : (string.IsNullOrEmpty(r.ChampionName) ? "?" : r.ChampionName);

            // Rang links, Porträt rechts daneben
            var title = t.Find("Title")?.GetComponent<TextMeshProUGUI>();
            var rankText = MakeText(rrt, "Rank", 40f, rank <= 3 ? new Color(0.62f, 0.42f, 0.06f) : Ink, TextAlignmentOptions.Center, title);
            var rkt = rankText.rectTransform;
            rkt.anchorMin = rkt.anchorMax = new Vector2(0f, 0.5f);
            rkt.pivot = new Vector2(0f, 0.5f);
            rkt.anchoredPosition = new Vector2(14f, 0f);
            rkt.sizeDelta = new Vector2(84f, 60f);
            rankText.enableAutoSizing = true;
            rankText.fontSizeMin = 22f;
            rankText.fontSizeMax = 40f;
            rankText.text = rank.ToString();

            var medal = t.Find("Medal") as RectTransform;
            if (medal != null)
            {
                medal.anchoredPosition = new Vector2(160f, medal.anchoredPosition.y);
                medal.sizeDelta = new Vector2(116f, 116f);
                var icon = medal.Find("MedalIcon")?.GetComponent<Image>();
                if (icon != null)
                {
                    icon.sprite = def != null ? def.Portrait : null;
                    icon.enabled = icon.sprite != null;
                    icon.preserveAspect = true;
                    icon.color = Color.white;
                }
            }

            const float x0 = 238f;
            if (title != null)
            {
                var tr = title.rectTransform;
                tr.anchoredPosition = new Vector2(x0, -18f);
                string mark = r.Continued ? $"  <color=#{ColorUtility.ToHtmlStringRGB(ContinuedColor)}>{_mark}</color>" : "";
                string dawn = r.Dawn ? $"  <size=70%><color=#{ColorUtility.ToHtmlStringRGB(DawnColor)}>Morgengrauen</color></size>" : "";
                title.text = $"Welle {r.Wave}{mark}{dawn}";
            }

            var desc = t.Find("Desc")?.GetComponent<TextMeshProUGUI>();
            if (desc != null)
            {
                var dr = desc.rectTransform;
                dr.anchoredPosition = new Vector2(x0 + 2f, -68f);
                desc.textWrappingMode = TextWrappingModes.NoWrap;
                desc.overflowMode = TextOverflowModes.Ellipsis;
                var parts = new List<string> { champ, MapName(r.Map, r.MapName) };
                if (!string.IsNullOrEmpty(r.DifficultyName)) parts.Add(r.DifficultyName);
                if (r.Siege > 0) parts.Add($"Belagerung {r.Siege}");
                if (r.Coop) parts.Add("Koop");
                desc.text = string.Join(" · ", parts);
            }

            var progress = t.Find("Progress")?.GetComponent<TextMeshProUGUI>();
            if (progress != null)
            {
                var pr = progress.rectTransform;
                pr.anchoredPosition = new Vector2(x0 + 2f, 18f);
                pr.sizeDelta = new Vector2(700f, pr.sizeDelta.y);
                progress.alignment = TextAlignmentOptions.Left;
                progress.textWrappingMode = TextWrappingModes.NoWrap;
                progress.overflowMode = TextOverflowModes.Ellipsis;
                string s = $"{RunHistoryRules.FormatDuration(r.Seconds)} min";
                if (r.Kills > 0) s += $" · {r.Kills.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))} besiegt";
                if (r.TeamWipes > 0) s += $" · {r.TeamWipes}× Team-Ausfall";
                if (!string.IsNullOrEmpty(r.Reason) && !r.Dawn) s += $" · {r.Reason}";
                progress.text = s;
            }

            var rl = t.Find("RewardLabel")?.GetComponent<TextMeshProUGUI>();
            var rn = t.Find("RewardName")?.GetComponent<TextMeshProUGUI>();
            var date = RunHistoryRules.FromUnix(r.Time).ToLocalTime();
            if (rl != null) rl.text = date.ToString("dd.MM.yyyy  HH:mm") + (newest ? "  · zuletzt" : "");
            if (rn != null)
            {
                rn.textWrappingMode = TextWrappingModes.Normal;
                rn.enableAutoSizing = true;
                rn.fontSizeMin = 16f;
                if (r.Continued)
                {
                    rn.text = $"{_mark} Nexus per Video wiederbelebt";
                    rn.color = ContinuedColor;
                }
                else rn.text = r.Coop ? "mit Mitspielern" : "";
            }
        }
    }
}
