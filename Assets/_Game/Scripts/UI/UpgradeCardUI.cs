using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Wellenkarte im Draft. Seit Sprint 3 (Plan „Fesselung“ D2/D3): Seltenheit als farbiger Rand + Banner,
    // Schlüsselwörter unter der Beschreibung, „Kombo!“-Hinweis und Aufdeck-Animation mit Klang je Seltenheit.
    // Die Zusatz-Elemente entstehen zur Laufzeit (BuffCard.prefab wird auch vom Händler genutzt).
    public class UpgradeCardUI : MonoBehaviour
    {
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI DescriptionText;
        public Image IconImage;
        public Button SelectButton;

        private UpgradeDefinitionSO _data;
        private readonly System.Collections.Generic.List<Image> _trim = new System.Collections.Generic.List<Image>();
        private Coroutine _pulse;

        public static readonly Color RareColor = new Color(0.32f, 0.62f, 1f);
        public static readonly Color EpicColor = new Color(0.78f, 0.38f, 1f);
        private static readonly Color ComboColor = new Color(1f, 0.78f, 0.2f);

        public static Color RarityColor(CardRarity r) =>
            r == CardRarity.Epic ? EpicColor : r == CardRarity.Rare ? RareColor : new Color(0.45f, 0.32f, 0.2f);

        public static string RarityName(CardRarity r) =>
            r == CardRarity.Epic ? "Episch" : r == CardRarity.Rare ? "Selten" : "Gewöhnlich";

        public void Setup(UpgradeDefinitionSO data)
        {
            _data = data;
            if (TitleText) TitleText.text = data.Title;
            if (DescriptionText)
            {
                string desc = data.Description;
                var kws = data.KeywordList;
                if (kws.Length > 0)
                    desc += $"\n<size=78%><color=#{ColorUtility.ToHtmlStringRGB(RarityColor(data.Rarity) * 0.8f)}>{string.Join(" · ", kws)}</color></size>";
                DescriptionText.text = desc;
                // Platz zwischen Trennlinie und Knopf; längere Texte (Sprint-3-Karten) werden kleiner statt überzulaufen
                var rt = DescriptionText.rectTransform;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -54f);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, 110f);
                DescriptionText.enableAutoSizing = true;
                DescriptionText.fontSizeMin = 13f;
                DescriptionText.fontSizeMax = 22f;
            }
            if (IconImage && data.Icon) IconImage.sprite = data.Icon;

            BuildRarity(data.Rarity);
            bool combo = UpgradeManager.Instance != null && UpgradeManager.Instance.IsCombo(data);
            if (combo) BuildCombo();

            SelectButton.onClick.RemoveAllListeners();
            SelectButton.onClick.AddListener(OnClick);
        }

        private void BuildRarity(CardRarity rarity)
        {
            if (rarity == CardRarity.Common) return;
            Color c = RarityColor(rarity);

            // farbige Zierleiste an der Kartenkante (über dem Holzrahmen, unter dem Inhalt)
            _trim.Clear();
            for (int i = 0; i < 4; i++)
            {
                var bar = new GameObject("RarityTrim" + i, typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)bar.transform;
                rt.SetParent(transform, false);
                rt.SetSiblingIndex(0);
                bool horizontal = i < 2;
                rt.anchorMin = horizontal ? new Vector2(0f, i == 0 ? 1f : 0f) : new Vector2(i == 2 ? 0f : 1f, 0f);
                rt.anchorMax = horizontal ? new Vector2(1f, i == 0 ? 1f : 0f) : new Vector2(i == 2 ? 0f : 1f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                const float inset = 7f, width = 6f;
                rt.sizeDelta = horizontal ? new Vector2(-2f * inset, width) : new Vector2(width, -2f * inset);
                rt.anchoredPosition = horizontal ? new Vector2(0f, i == 0 ? -inset : inset) : new Vector2(i == 2 ? inset : -inset, 0f);
                var im = bar.GetComponent<Image>();
                im.color = c;
                im.raycastTarget = false;
                _trim.Add(im);
            }

            // Banner oben auf der Karte
            var banner = new GameObject("RarityBanner", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)banner.transform;
            brt.SetParent(transform, false);
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(140f, 30f);
            brt.anchoredPosition = new Vector2(0f, 2f);
            var bimg = banner.GetComponent<Image>();
            bimg.color = c;
            bimg.raycastTarget = false;
            var label = NewText(brt, "Label", RarityName(rarity).ToUpperInvariant(), 17f, Color.white);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = Vector2.zero;
            label.characterSpacing = 6f;

            if (rarity == CardRarity.Epic) _pulse = StartCoroutine(PulseRoutine(c));
        }

        private void BuildCombo()
        {
            var t = NewText((RectTransform)transform, "Combo", "Kombo!", 22f, ComboColor);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(120f, 36f);
            rt.anchoredPosition = new Vector2(-30f, -10f);
            rt.localRotation = Quaternion.Euler(0f, 0f, -14f);
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(60, 30, 0, 255);
        }

        private TextMeshProUGUI NewText(RectTransform parent, string name, string text, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (TitleText != null) t.font = TitleText.font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        // Episch: Zierleiste pulsiert (läuft auch bei Time.timeScale 0)
        private IEnumerator PulseRoutine(Color c)
        {
            while (true)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
                var col = Color.Lerp(c, Color.white, 0.45f * k);
                foreach (var im in _trim) if (im != null) im.color = col;
                yield return null;
            }
        }

        // Aufdecken: Karte dreht sich nach delay auf (Echtzeit), Klang nach Seltenheit
        public void Reveal(float delay)
        {
            if (!isActiveAndEnabled) return;
            StartCoroutine(RevealRoutine(delay));
        }

        private IEnumerator RevealRoutine(float delay)
        {
            var t = transform;
            t.localScale = new Vector3(0f, 1f, 1f);
            if (SelectButton != null) SelectButton.interactable = false;
            float end = Time.unscaledTime + delay;
            while (Time.unscaledTime < end) yield return null;

            var r = _data != null ? _data.Rarity : CardRarity.Common;
            GameAudio.Play(r == CardRarity.Epic ? SfxId.CardRevealEpic : r == CardRarity.Rare ? SfxId.CardRevealRare : SfxId.CardRevealCommon);
            float dur = r == CardRarity.Epic ? 0.38f : 0.24f;
            float s = 0f;
            while (s < 1f)
            {
                s = Mathf.Min(1f, s + Time.unscaledDeltaTime / dur);
                // leichtes Überschwingen
                float x = 1f + 0.12f * Mathf.Sin(s * Mathf.PI) * (1f - s) * 2f;
                t.localScale = new Vector3(Mathf.SmoothStep(0f, 1f, s) * x, 1f + 0.06f * Mathf.Sin(s * Mathf.PI), 1f);
                yield return null;
            }
            t.localScale = Vector3.one;
            if (SelectButton != null) SelectButton.interactable = true;
        }

        private void OnClick()
        {
            UpgradeManager.Instance.SelectUpgrade(_data);
        }
    }
}
