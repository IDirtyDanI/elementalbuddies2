using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Kartenwahl am Wellenende. Seit Sprint 3: Karten decken sich nacheinander auf, darunter „Neu würfeln“
    // und „Überspringen (+N Splitter)“. Neu würfeln nutzt zuerst Gratis-Neuwürfe („frei: n“, +1 je Boss-Kill), danach
    // kostet es Seelensplitter (25, 40, 55 … je Kartenwahl, Preis mit Splitter-Symbol; zu teuer → gesperrt + Hinweis).
    // Die Knöpfe werden aus dem Händlerladen geklont (gleicher Stil), falls keine verdrahtet sind.
    public class UpgradeScreenUI : MonoBehaviour
    {
        public GameObject Panel; // The whole popup
        public Transform CardsContainer;
        public GameObject CardPrefab;

        [Header("Sprint 3 (optional, sonst geklont)")]
        public Button RerollButton;
        public Button SkipButton;

        [Tooltip("Optional: Splitter-Symbol hinter dem Würfel-Preis (sonst icon_shard aus Händlerladen/HUD).")]
        public Sprite ShardIcon;
        [Tooltip("Preisfarbe (wie die Splitter-Zahl im HUD) bzw. wenn die Teamkasse nicht reicht.")]
        public Color ShardPriceColor = new Color(0.851f, 0.722f, 1f);
        public Color UnaffordableColor = new Color(0.78f, 0.15f, 0.12f);

        private TMP_Text _rerollText, _skipText, _rerollHint;
        private Image _rerollIcon;
        private string _lastRerollLabel;
        private const float RevealStagger = 0.14f;

        void Start()
        {
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradesAvailable += ShowUpgrades;
                UpgradeManager.Instance.OnUpgradeSelected += HideUpgrades;
            }
            EnsureButtons();
            if (Panel != null) Panel.SetActive(false);
        }

        void OnDestroy()
        {
             if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradesAvailable -= ShowUpgrades;
                UpgradeManager.Instance.OnUpgradeSelected -= HideUpgrades;
            }
        }

        private void EnsureButtons()
        {
            if (Panel == null) return;
            if (RerollButton == null || SkipButton == null)
            {
                var shop = FindFirstObjectByType<MerchantScreenUI>(FindObjectsInactive.Include);
                Button template = shop != null ? (shop.DoneButton != null ? shop.DoneButton : shop.RerollButton) : null;
                if (template != null)
                {
                    if (RerollButton == null) RerollButton = CloneButton(template, "RerollButton", new Vector2(-170f, -330f));
                    if (SkipButton == null) SkipButton = CloneButton(template, "SkipButton", new Vector2(170f, -330f));
                }
            }
            if (RerollButton != null)
            {
                RerollButton.onClick.RemoveAllListeners();
                RerollButton.onClick.AddListener(() => { if (UpgradeManager.Instance != null) UpgradeManager.Instance.RequestReroll(); });
                _rerollText = RerollButton.GetComponentInChildren<TMP_Text>(true);
                BuildRerollExtras();
            }
            if (SkipButton != null)
            {
                SkipButton.onClick.RemoveAllListeners();
                SkipButton.onClick.AddListener(() => { if (UpgradeManager.Instance != null) UpgradeManager.Instance.SkipDraft(); });
                _skipText = SkipButton.GetComponentInChildren<TMP_Text>(true);
            }
        }

        // Splitter-Symbol hinter dem Preis + Hinweiszeile unter dem Knopf (nur bei bezahltem Neuwurf sichtbar)
        private void BuildRerollExtras()
        {
            if (_rerollText == null) return;
            if (_rerollText.enableAutoSizing == false)
            {
                _rerollText.enableAutoSizing = true;
                _rerollText.fontSizeMax = _rerollText.fontSize;
            }
            _rerollText.fontSizeMin = Mathf.Min(_rerollText.fontSizeMin > 0f ? _rerollText.fontSizeMin : 16f, 16f);

            if (ShardIcon == null)
            {
                var shop = FindFirstObjectByType<MerchantScreenUI>(FindObjectsInactive.Include);
                if (shop != null && shop.RerollPriceIcon != null) ShardIcon = shop.RerollPriceIcon.sprite;
            }
            if (ShardIcon == null)
            {
                var hud = FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include);
                var icon = hud != null && hud.ShardText != null && hud.ShardText.transform.parent != null
                    ? hud.ShardText.transform.parent.Find("ShardIcon") : null;
                var img = icon != null ? icon.GetComponent<Image>() : null;
                if (img != null) ShardIcon = img.sprite;
            }
            if (ShardIcon != null)
            {
                var go = new GameObject("ShardIcon", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(_rerollText.rectTransform, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(32f, 32f);
                _rerollIcon = go.GetComponent<Image>();
                _rerollIcon.sprite = ShardIcon;
                _rerollIcon.preserveAspect = true;
                _rerollIcon.raycastTarget = false;
                go.SetActive(false);
            }

            var hint = Instantiate(_rerollText.gameObject, RerollButton.transform, false);
            hint.name = "RerollHint";
            foreach (Transform ch in hint.transform) Destroy(ch.gameObject); // mitkopiertes Symbol
            _rerollHint = hint.GetComponent<TMP_Text>();
            var hrt = _rerollHint.rectTransform;
            hrt.anchorMin = new Vector2(0f, 0f);
            hrt.anchorMax = new Vector2(1f, 0f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.anchoredPosition = new Vector2(0f, -4f);
            hrt.sizeDelta = new Vector2(0f, 30f);
            _rerollHint.margin = Vector4.zero;
            _rerollHint.enableAutoSizing = true;
            _rerollHint.fontSizeMin = 12f;
            _rerollHint.fontSizeMax = 20f;
            _rerollHint.color = UnaffordableColor;
            _rerollHint.raycastTarget = false;
            _rerollHint.text = "Zu wenig Seelensplitter";
            hint.SetActive(false);
        }

        private void RefreshReroll(UpgradeManager um)
        {
            bool free = um.OfferRerolls > 0;
            bool affordable = um.CanAffordReroll;
            RerollButton.interactable = affordable && !um.IsRerollPending;
            if (_rerollHint != null && _rerollHint.gameObject.activeSelf != !affordable) _rerollHint.gameObject.SetActive(!affordable);
            if (_rerollText == null) return;

            string col = ColorUtility.ToHtmlStringRGB(affordable ? ShardPriceColor : UnaffordableColor);
            string label = free
                ? $"Neu würfeln (frei: {um.OfferRerolls})"
                : $"Neu würfeln – <color=#{col}>{um.OfferRerollCost}</color>" + (_rerollIcon == null ? $" <color=#{col}>Splitter</color>" : "");
            if (label == _lastRerollLabel) return;
            _lastRerollLabel = label;

            bool showIcon = !free && _rerollIcon != null;
            float iconW = showIcon ? ((RectTransform)_rerollIcon.transform).sizeDelta.x + 6f : 0f;
            // Platz für das Symbol rechts freihalten → Text + Symbol stehen gemeinsam mittig
            _rerollText.margin = new Vector4(0f, 0f, iconW, 0f);
            _rerollText.text = label;
            if (_rerollIcon == null) return;
            _rerollIcon.gameObject.SetActive(showIcon);
            if (!showIcon) return;
            _rerollText.ForceMeshUpdate();
            Bounds b = _rerollText.textBounds;
            var irt = (RectTransform)_rerollIcon.transform;
            float size = Mathf.Clamp(_rerollText.fontSize * 1.1f, 18f, 34f);
            irt.sizeDelta = new Vector2(size, size);
            irt.localPosition = new Vector3(b.max.x + 4f + size * 0.5f, b.center.y, 0f);
        }

        private Button CloneButton(Button template, string name, Vector2 pos)
        {
            var go = Instantiate(template.gameObject, Panel.transform, false);
            go.name = name;
            go.SetActive(true);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x, 300f), Mathf.Max(rt.sizeDelta.y, 60f));
            // geklonte Zusatz-Bilder (z. B. Preis-Symbol) und Fremd-Skripte entfernen
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(mb is Graphic) && !(mb is Selectable) && !(mb is LayoutGroup) && !(mb is ContentSizeFitter) && !(mb is LayoutElement) && !(mb is BaseMeshEffect))
                    Destroy(mb);
            var b = go.GetComponent<Button>();
            b.onClick.RemoveAllListeners();
            return b;
        }

        void Update()
        {
            var um = UpgradeManager.Instance;
            if (um == null || Panel == null || !Panel.activeSelf) return;
            if (RerollButton != null) RefreshReroll(um);
            if (SkipButton != null)
            {
                SkipButton.interactable = !um.IsRerollPending;
                if (_skipText != null) _skipText.text = $"Überspringen <color=#9fe8ff>+{um.OfferSkipShards}</color>";
            }
        }

        private void ShowUpgrades(List<UpgradeDefinitionSO> upgrades)
        {
            if (Panel != null) Panel.SetActive(true);

            // Clear old cards
            foreach (Transform child in CardsContainer)
            {
                Destroy(child.gameObject);
            }

            // Create new cards (nacheinander aufdecken)
            int i = 0;
            foreach (var up in upgrades)
            {
                GameObject go = Instantiate(CardPrefab, CardsContainer);
                var ui = go.GetComponent<UpgradeCardUI>();
                if (ui != null)
                {
                    ui.Setup(up);
                    ui.Reveal(0.1f + RevealStagger * i);
                }
                i++;
            }
        }

        private void HideUpgrades()
        {
            if (Panel != null) Panel.SetActive(false);
        }
    }
}
