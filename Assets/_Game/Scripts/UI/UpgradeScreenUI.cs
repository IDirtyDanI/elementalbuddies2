using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Kartenwahl am Wellenende. Seit Sprint 3: Karten decken sich nacheinander auf, darunter „Neu würfeln (n)“
    // (Gratis-Neuwürfe, +1 je Boss-Kill) und „Überspringen (+N Splitter)“. Die Knöpfe werden aus dem Händlerladen
    // geklont (gleicher Stil), falls keine verdrahtet sind.
    public class UpgradeScreenUI : MonoBehaviour
    {
        public GameObject Panel; // The whole popup
        public Transform CardsContainer;
        public GameObject CardPrefab;

        [Header("Sprint 3 (optional, sonst geklont)")]
        public Button RerollButton;
        public Button SkipButton;

        private TMP_Text _rerollText, _skipText;
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
            }
            if (SkipButton != null)
            {
                SkipButton.onClick.RemoveAllListeners();
                SkipButton.onClick.AddListener(() => { if (UpgradeManager.Instance != null) UpgradeManager.Instance.SkipDraft(); });
                _skipText = SkipButton.GetComponentInChildren<TMP_Text>(true);
            }
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
            if (RerollButton != null)
            {
                RerollButton.interactable = um.OfferRerolls > 0 && !um.IsRerollPending;
                if (_rerollText != null) _rerollText.text = $"Neu würfeln ({um.OfferRerolls})";
            }
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
