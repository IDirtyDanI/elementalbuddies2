using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Kartenauswahl beim Händler (Pergament-Stil wie die Wellenkarten): Kopf mit Händlername + Porträt,
    // 3 Karten aus BuffCard.prefab (MerchantCardUI statt UpgradeCardUI). Script auf dem Panel-Objekt
    // (Kind des Canvas, bleibt aktiv); Panel wird ein-/ausgeblendet.
    public class MerchantScreenUI : MonoBehaviour
    {
        [Tooltip("Wird ein-/ausgeblendet (Vollbild-Overlay).")]
        public GameObject Panel;
        public Transform CardsContainer;
        [Tooltip("BuffCard.prefab – UpgradeCardUI wird zur Laufzeit durch MerchantCardUI ersetzt.")]
        public GameObject CardPrefab;
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI SubtitleText;
        [Tooltip("Optional: Händler-Porträt (portrait_merchant_*); ausgeblendet, wenn keins da ist.")]
        public Image Portrait;
        [Tooltip("Optional: Banderole, wird in Händlerfarbe getönt.")]
        public Image Ribbon;
        public bool TintRibbon = false;

        private MerchantManager _mgr;

        void Start()
        {
            _mgr = MerchantManager.Instance;
            if (_mgr != null)
            {
                _mgr.OnCardsOffered += Show;
                _mgr.OnChoiceClosed += Hide;
            }
            if (Panel != null) Panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (_mgr != null)
            {
                _mgr.OnCardsOffered -= Show;
                _mgr.OnChoiceClosed -= Hide;
            }
        }

        private void Show(Merchant m, List<MerchantCardSO> cards)
        {
            if (Panel != null) Panel.SetActive(true);

            if (TitleText != null) TitleText.text = m != null ? m.DisplayName : "Händler";
            if (SubtitleText != null)
                SubtitleText.text = m != null ? $"Stand eingenommen! Wähle eine Verbesserung für: {MerchantInfo.Goods(m.Kind)}" : "Wähle eine Verbesserung";
            if (Portrait != null)
            {
                Sprite p = m != null && _mgr != null ? _mgr.GetPortrait(m.Kind) : null;
                Portrait.sprite = p;
                Portrait.gameObject.SetActive(p != null);
            }
            if (Ribbon != null && TintRibbon && m != null) Ribbon.color = Color.Lerp(Color.white, m.Color, 0.45f);

            if (CardsContainer == null || CardPrefab == null) return;
            foreach (Transform child in CardsContainer) Destroy(child.gameObject);
            foreach (var card in cards)
            {
                GameObject go = Instantiate(CardPrefab, CardsContainer);
                go.name = "MerchantCard_" + card.name;
                var old = go.GetComponent<UpgradeCardUI>();
                if (old != null) Destroy(old);
                var ui = go.GetComponent<MerchantCardUI>();
                if (ui == null) ui = go.AddComponent<MerchantCardUI>();
                ui.Setup(card, _mgr != null ? _mgr.GetBadge(card) : card.Badge);
            }
        }

        private void Hide()
        {
            if (Panel != null) Panel.SetActive(false);
        }
    }
}
