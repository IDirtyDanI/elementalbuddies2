using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Wellenvorschau (Plan „Fesselung“ C1): Pergament-Panel über dem „Welle starten“-Knopf, nur in der Bauphase.
    // Zeigt Gegnertypen mit Anzahl (NEU-Stempel + Hinweis), Boss-Warnung, Ereignisse und den nächsten Boss.
    // Wird vom HUDManager zur Laufzeit angelegt; Stil (Pergament-Sprite, Schrift) kommt aus dem Ziel-Panel bzw. HUD.
    public class WavePreviewUI : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.23f, 0.14f, 0.07f);
        private static readonly Color Soft = new Color(0.36f, 0.25f, 0.14f);
        private static readonly Color Danger = new Color(0.62f, 0.12f, 0.08f);
        private static readonly Color Harvest = new Color(0.18f, 0.42f, 0.16f);

        private RectTransform _root;
        private CanvasGroup _group;
        private RectTransform _content;
        private TMP_FontAsset _font, _bodyFont;
        private int _shownWave = -1;
        private bool _visible;

        public static WavePreviewUI Create(HUDManager hud)
        {
            if (hud == null || hud.StartWaveButton == null) return null;
            var ui = hud.gameObject.AddComponent<WavePreviewUI>();
            ui.Build(hud);
            return ui;
        }

        private void Build(HUDManager hud)
        {
            var anchor = (RectTransform)hud.StartWaveButton.transform;
            var label = hud.StartWaveButton.GetComponentInChildren<TextMeshProUGUI>(true);
            _font = label != null ? label.font : TMP_Settings.defaultFontAsset;
            _bodyFont = hud.ShardText != null ? hud.ShardText.font : _font;

            Sprite parchment = null;
            var obj = FindFirstObjectByType<ObjectiveUI>(FindObjectsInactive.Include);
            if (obj != null)
                foreach (var img in obj.GetComponentsInChildren<Image>(true))
                    if (img.sprite != null && img.sprite.name.Contains("parchment")) { parchment = img.sprite; break; }

            var go = new GameObject("WavePreview", typeof(RectTransform));
            _root = go.GetComponent<RectTransform>();
            _root.SetParent(anchor.parent, false);
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.anchoredPosition = anchor.anchoredPosition + new Vector2(0f, anchor.sizeDelta.y + 14f);
            _root.sizeDelta = new Vector2(500f, 200f);
            _root.SetSiblingIndex(anchor.GetSiblingIndex());

            var bg = go.AddComponent<Image>();
            bg.sprite = parchment;
            bg.type = parchment != null && parchment.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            bg.color = parchment != null ? Color.white : new Color(0.93f, 0.86f, 0.7f, 0.95f);
            bg.raycastTarget = false;
            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(30, 30, 24, 26);
            v.spacing = 4f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _content = _root;
        }

        void Update()
        {
            var wm = WaveManager.Instance;
            var gm = GameManager.Instance;
            bool show = wm != null && !wm.IsWaveActive && (gm == null || !gm.IsGameOver);
            if (show && wm.UpcomingWaveNumber != _shownWave) Rebuild(wm);
            if (show != _visible) _visible = show;
            float target = _visible ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 4f);
        }

        private void Rebuild(WaveManager wm)
        {
            _shownWave = wm.UpcomingWaveNumber;
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var c = _content.GetChild(i).gameObject;
                c.SetActive(false); // Destroy greift erst am Frame-Ende – sonst zählt das Layout die alten Zeilen mit
                Destroy(c);
            }

            var p = WavePreview.Build(wm, _shownWave);
            if (p == null) return;

            AddText($"Welle {p.Wave}  <size=70%><color=#5c4024>· {p.TotalCount} Gegner</color></size>", 36f, Ink, _font, TextAlignmentOptions.Left);

            foreach (var g in p.Groups)
            {
                bool boss = g.Config.IsBoss;
                string name = WavePreview.NameOf(g.Config);
                string line = boss ? $"<b>Boss: {name}</b>" : $"{g.Count}× {name}";
                if (g.IsNew && !boss) line += "  <color=#a3261b><b>NEU</b></color>";
                string hint = g.IsNew || boss ? g.Config.PreviewHint : null;
                if (!string.IsNullOrEmpty(hint)) line += $"\n<size=72%><color=#5c4024>{hint}</color></size>";
                AddRow(g.Config.Icon, line, boss ? Danger : Ink);
            }

            foreach (var e in p.Events)
                AddText("• " + e, 25f, e.StartsWith("Ernte") ? Harvest : Soft, _bodyFont, TextAlignmentOptions.Left);

            if (p.Boss == null && p.NextBossWave > 0)
            {
                int inWaves = p.NextBossWave - p.Wave;
                string who = p.NextBoss != null ? WavePreview.NameOf(p.NextBoss) : "Ein Boss";
                AddText($"<color=#7a1f12>{who} kommt in {inWaves} {(inWaves == 1 ? "Welle" : "Wellen")}</color>", 24f, Soft, _bodyFont, TextAlignmentOptions.Left);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
        }

        private TextMeshProUGUI AddText(string text, float size, Color color, TMP_FontAsset font, TextAlignmentOptions align)
        {
            var t = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(_content, false);
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        private void AddRow(Sprite icon, string text, Color color)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(_content, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(row.transform, false);
            var img = iconGo.AddComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = icon != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            var le = iconGo.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 54f;
            le.preferredHeight = le.minHeight = 54f;

            var t = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(row.transform, false);
            t.font = _bodyFont;
            t.fontSize = 29f;
            t.color = color;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.richText = true;
            t.raycastTarget = false;
            t.text = text;
            var tle = t.gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
        }
    }
}
