using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace ElementalBuddies
{
    // Eine Händlerkarte auf BuffCard.prefab-Basis: Fähigkeits-Icon + optionale Plakette, Titel, Wert und der
    // konkrete neue Wert („Schwerthieb – Reichweite: 2,6 m → 3,1 m“). Kinder werden über Namen gefunden.
    // Laden: Preisschild („Gratis“ / „60 Gold“, rot wenn zu teuer), „Gekauft“-Stempel, Abdunkeln nach dem Kauf.
    // Bezahlt wird mit dem persönlichen Gold des lokalen Spielers (NetPlayer.Gold); das alte Splitter-Symbol
    // (PriceIcon) bleibt ausgeblendet, der Preis steht als Text „60 Gold“.
    // Alle Laden-Refs sind optional (Fallback: Text auf dem Knopf).
    public class MerchantCardUI : MonoBehaviour
    {
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI DescriptionText;
        public Image IconImage;
        public Image BadgeImage;
        public Button SelectButton;

        [Header("Laden (optional)")]
        [Tooltip("Preis: „Gratis“ oder Zahl (mit PriceIcon) bzw. „60 ✦“.")]
        public TMP_Text PriceText;
        [Tooltip("Optional: Splitter-Symbol neben dem Preis (ausgeblendet bei „Gratis“).")]
        public Image PriceIcon;
        [Tooltip("Container des Preisschilds, wird nach dem Kauf ausgeblendet.")]
        public GameObject PriceTag;
        [Tooltip("Stempel „Gekauft“, sichtbar nach dem Kauf.")]
        public GameObject BoughtOverlay;
        [Tooltip("Abdunkeln nach dem Kauf (wird bei Bedarf ergänzt).")]
        public CanvasGroup CanvasGroup;
        public Color UnaffordableColor = new Color(0.78f, 0.15f, 0.12f);
        [Range(0f, 1f)] public float BoughtAlpha = 0.55f;

        public int Slot { get; private set; } = -1;

        private MerchantCardSO _data;
        private const string PreviewColor = "#2e6b2e";
        private Color _priceColor = Color.white;
        private bool _priceColorRead;
        private Color _labelColor = Color.white;
        private bool _labelColorRead;
        private Coroutine _shake;

        void Awake()
        {
            if (TitleText == null) TitleText = Find<TextMeshProUGUI>("Title");
            if (DescriptionText == null) DescriptionText = Find<TextMeshProUGUI>("Beschreibung");
            if (IconImage == null) IconImage = Find<Image>("Icon");
            if (SelectButton == null) SelectButton = Find<Button>("SelectButton");
            if (BadgeImage == null) BadgeImage = Find<Image>("Badge");
            if (BadgeImage == null && IconImage != null) BadgeImage = CreateBadge(IconImage.rectTransform);
            if (PriceText == null) PriceText = Find<TMP_Text>("Price");
            if (PriceIcon == null) PriceIcon = Find<Image>("PriceIcon");
            if (PriceTag == null) { var t = Find<Transform>("PriceTag"); if (t != null) PriceTag = t.gameObject; }
            if (BoughtOverlay == null) { var t = Find<Transform>("Bought"); if (t != null) BoughtOverlay = t.gameObject; }
            if (CanvasGroup == null) CanvasGroup = GetComponent<CanvasGroup>();
            if (CanvasGroup == null) CanvasGroup = gameObject.AddComponent<CanvasGroup>();
            if (PriceText != null && !_priceColorRead)
            {
                _priceColor = PriceText.color;
                _priceColorRead = true;
            }
        }

        public void Setup(MerchantCardSO data, Sprite badge) => Setup(data, badge, -1);

        public void Setup(MerchantCardSO data, Sprite badge, int slot)
        {
            if (TitleText == null) Awake();
            _data = data;
            Slot = slot;

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
            }
            RefreshShopState();
        }

        // Preis/Gekauft/Leistbar aus dem MerchantManager übernehmen
        public void RefreshShopState()
        {
            var mgr = MerchantManager.Instance;
            bool bought = mgr != null && Slot >= 0 && mgr.IsBought(Slot);
            int price = mgr != null ? mgr.NextCardPrice : 0;
            float gold = MerchantManager.LocalGold;
            bool affordable = price <= 0 || gold + 0.001f >= price;

            if (BoughtOverlay != null) BoughtOverlay.SetActive(bought);
            if (PriceTag != null) PriceTag.SetActive(!bought);
            if (CanvasGroup != null) CanvasGroup.alpha = bought ? BoughtAlpha : 1f;

            if (PriceText != null)
            {
                if (PriceTag == null) PriceText.gameObject.SetActive(!bought);
                PriceText.text = price <= 0 ? "Gratis" : $"{price} {CurrencyLabel}";
                PriceText.color = affordable ? _priceColor : UnaffordableColor;
            }
            if (PriceIcon != null) PriceIcon.gameObject.SetActive(false); // Splitter-Symbol passt nicht zu Gold

            if (SelectButton != null)
            {
                SelectButton.interactable = !bought;
                var label = SelectButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    // Ohne Preisschild steht der Preis auf dem Knopf
                    string buy = PriceText != null ? "Kaufen" : $"Kaufen ({price} {CurrencyLabel})";
                    label.text = bought ? "Gekauft" : price <= 0 ? (PriceText != null ? "Nehmen" : "Nehmen (gratis)") : buy;
                    if (!_labelColorRead)
                    {
                        _labelColor = label.color;
                        _labelColorRead = true;
                    }
                    label.color = PriceText == null && !bought && !affordable ? UnaffordableColor : _labelColor;
                }
            }
        }

        // Währung im Laden
        public const string CurrencyLabel = "Gold";

        // Zu wenig Gold: kurz wackeln (unskalierte Zeit, Spiel steht)
        public void Shake()
        {
            if (!isActiveAndEnabled) return;
            if (_shake != null) StopCoroutine(_shake);
            _shake = StartCoroutine(ShakeRoutine());
        }

        private IEnumerator ShakeRoutine()
        {
            Transform target = PriceTag != null ? PriceTag.transform : transform;
            Vector3 basePos = target.localPosition;
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.unscaledDeltaTime;
                float a = (1f - t / 0.35f) * 10f;
                target.localPosition = basePos + new Vector3(Mathf.Sin(t * 70f) * a, 0f, 0f);
                yield return null;
            }
            target.localPosition = basePos;
            _shake = null;
        }

        private void OnClick()
        {
            var mgr = MerchantManager.Instance;
            if (mgr == null) return;
            if (Slot >= 0) mgr.BuyCard(Slot);
            else mgr.SelectCard(_data);
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
