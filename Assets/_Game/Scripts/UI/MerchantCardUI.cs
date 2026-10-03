using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Eine Händlerkarte auf BuffCard.prefab-Basis: Fähigkeits-Icon + optionale Plakette, Titel, Wert und der
    // konkrete neue Wert („Schwerthieb – Reichweite: 2,6 m → 3,1 m“). Kinder werden über Namen gefunden.
    public class MerchantCardUI : MonoBehaviour
    {
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI DescriptionText;
        public Image IconImage;
        public Image BadgeImage;
        public Button SelectButton;

        private MerchantCardSO _data;
        private const string PreviewColor = "#2e6b2e";

        void Awake()
        {
            if (TitleText == null) TitleText = Find<TextMeshProUGUI>("Title");
            if (DescriptionText == null) DescriptionText = Find<TextMeshProUGUI>("Beschreibung");
            if (IconImage == null) IconImage = Find<Image>("Icon");
            if (SelectButton == null) SelectButton = Find<Button>("SelectButton");
            if (BadgeImage == null) BadgeImage = Find<Image>("Badge");
            if (BadgeImage == null && IconImage != null) BadgeImage = CreateBadge(IconImage.rectTransform);
        }

        public void Setup(MerchantCardSO data, Sprite badge)
        {
            if (TitleText == null) Awake();
            _data = data;

            if (TitleText != null) TitleText.text = data.Title;
            if (DescriptionText != null)
            {
                string preview = MerchantManager.PreviewValues(data);
                string text = data.Description ?? "";
                if (!string.IsNullOrEmpty(preview)) text += (text.Length > 0 ? "\n" : "") + $"<color={PreviewColor}>{preview}</color>";
                DescriptionText.text = text;
                DescriptionText.enableAutoSizing = true;
                DescriptionText.fontSizeMin = 14f;
                DescriptionText.fontSizeMax = Mathf.Max(18f, DescriptionText.fontSizeMax > 0f ? Mathf.Min(DescriptionText.fontSizeMax, 24f) : 22f);
            }
            if (IconImage != null && data.Icon != null) IconImage.sprite = data.Icon;
            if (BadgeImage != null)
            {
                BadgeImage.sprite = badge;
                BadgeImage.gameObject.SetActive(badge != null);
            }

            if (SelectButton != null)
            {
                SelectButton.onClick.RemoveAllListeners();
                SelectButton.onClick.AddListener(OnClick);
                var label = SelectButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = "Nehmen";
            }
        }

        private void OnClick()
        {
            if (MerchantManager.Instance != null) MerchantManager.Instance.SelectCard(_data);
        }

        // Plakette unten rechts am Icon (Modifikator-Symbol)
        private static Image CreateBadge(RectTransform icon)
        {
            var go = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(icon, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-8f, 8f);
            rt.sizeDelta = new Vector2(52f, 52f);
            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            go.SetActive(false);
            return img;
        }

        private T Find<T>(string childName) where T : Component
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t.GetComponent<T>();
            return null;
        }
    }
}
