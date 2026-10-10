using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Kartenauswahl beim Händler (Pergament-Stil wie die Wellenkarten): Kopf mit Händlername + Porträt,
    // 3 Karten aus BuffCard.prefab (MerchantCardUI statt UpgradeCardUI). Script auf dem Panel-Objekt
    // (Kind des Canvas, bleibt aktiv); Panel wird ein-/ausgeblendet.
    // Laden: Gold-Anzeige im Kopf (persönliches Gold des lokalen Spielers), „Neu würfeln“ mit Preis, „Fertig“ (fragt
    // einmal nach, solange die Gratis-Karte noch nicht genommen ist). Alle Laden-Refs optional.
    // Mehrspieler: Der Laden gehört dem lokalen Spieler (MerchantManager spiegelt den Server-Zustand); keine Pause.
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

        [Header("Laden (optional)")]
        public Button RerollButton;
        [Tooltip("Preis auf dem Würfel-Knopf, z. B. „Neu würfeln (25 Gold)“.")]
        public TMP_Text RerollCostText;
        [Tooltip("Altes Splitter-Symbol hinter dem Würfel-Preis – wird ausgeblendet (Preis jetzt in Gold).")]
        public Image RerollPriceIcon;
        public Button DoneButton;
        public TMP_Text DoneButtonText;
        [Tooltip("Aktuelles Gold des lokalen Spielers im Kopf („123 Gold“).")]
        public TMP_Text ShardsText;
        public Color UnaffordableColor = new Color(0.78f, 0.15f, 0.12f);
        [Tooltip("Zeitfenster für den zweiten Klick auf „Fertig“, wenn die Gratis-Karte noch offen ist.")]
        public float ConfirmWindow = 3f;

        private const string DoneLabel = "Fertig";
        private const string ConfirmLabel = "Gratis-Karte verfallen lassen?";

        private MerchantManager _mgr;
        private readonly List<MerchantCardUI> _cards = new List<MerchantCardUI>();
        private float _confirmUntil = -1f;
        private Color _rerollColor = Color.white;
        private bool _rerollColorRead;
        private Coroutine _rerollShake;

        void Start()
        {
            _mgr = MerchantManager.Instance;
            if (_mgr != null)
            {
                _mgr.OnCardsOffered += Show;
                _mgr.OnChoiceClosed += Hide;
                _mgr.OnOfferChanged += HandleOfferChanged;
                _mgr.OnPurchaseFailed += HandlePurchaseFailed;
            }
            NetPlayer.OnGoldChanged += HandleGoldChanged;
            if (RerollButton != null) RerollButton.onClick.AddListener(OnRerollClicked);
            if (DoneButton != null) DoneButton.onClick.AddListener(OnDoneClicked);
            if (DoneButtonText == null && DoneButton != null) DoneButtonText = DoneButton.GetComponentInChildren<TMP_Text>(true);
            if (RerollCostText == null && RerollButton != null) RerollCostText = RerollButton.GetComponentInChildren<TMP_Text>(true);
            if (Panel != null) Panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (_mgr != null)
            {
                _mgr.OnCardsOffered -= Show;
                _mgr.OnChoiceClosed -= Hide;
                _mgr.OnOfferChanged -= HandleOfferChanged;
                _mgr.OnPurchaseFailed -= HandlePurchaseFailed;
            }
            NetPlayer.OnGoldChanged -= HandleGoldChanged;
        }

        void Update()
        {
            // Bestätigungs-Fenster für „Fertig“ abgelaufen → Beschriftung zurück
            if (_confirmUntil > 0f && Time.unscaledTime > _confirmUntil)
            {
                _confirmUntil = -1f;
                RefreshShop();
            }
        }

        private void Show(Merchant m, List<MerchantCardSO> cards)
        {
            if (Panel != null) Panel.SetActive(true);

            if (TitleText != null) TitleText.text = m != null ? m.DisplayName : "Händler";
            if (SubtitleText != null)
                SubtitleText.text = m != null ? $"Stand eingenommen! Verbesserungen für: {MerchantInfo.Goods(m.Kind)} – die erste Karte ist gratis." : "Die erste Karte ist gratis.";
            if (Portrait != null)
            {
                Sprite p = m != null && _mgr != null ? _mgr.GetPortrait(m.Kind) : null;
                Portrait.sprite = p;
                Portrait.gameObject.SetActive(p != null);
            }
            if (Ribbon != null && TintRibbon && m != null) Ribbon.color = Color.Lerp(Color.white, m.Color, 0.45f);

            _confirmUntil = -1f;
            BuildCards();
        }

        private void BuildCards()
        {
            _cards.Clear();
            if (CardsContainer == null || CardPrefab == null || _mgr == null)
            {
                RefreshShop();
                return;
            }
            foreach (Transform child in CardsContainer) Destroy(child.gameObject);
            var offer = _mgr.CurrentOffer;
            for (int i = 0; i < offer.Count; i++)
            {
                var card = offer[i];
                GameObject go = Instantiate(CardPrefab, CardsContainer);
                go.name = "MerchantCard_" + card.name;
                var old = go.GetComponent<UpgradeCardUI>();
                if (old != null) Destroy(old);
                var ui = go.GetComponent<MerchantCardUI>();
                if (ui == null) ui = go.AddComponent<MerchantCardUI>();
                ui.Setup(card, _mgr.GetBadge(card), i);
                _cards.Add(ui);
            }
            RefreshShop();
        }

        // Kauf / Neu würfeln: Karten an Ort und Stelle neu belegen (gleiche Anzahl) oder neu bauen
        private void HandleOfferChanged()
        {
            if (_mgr == null) return;
            // Ohne „Fertig“-Knopf (UI noch nicht gebaut): altes Verhalten, nach dem ersten Kauf schließen
            if (DoneButton == null && _mgr.Purchases > 0)
            {
                _mgr.CloseShop();
                return;
            }
            var offer = _mgr.CurrentOffer;
            if (offer.Count != _cards.Count)
            {
                BuildCards();
                return;
            }
            for (int i = 0; i < offer.Count; i++)
            {
                if (_cards[i] == null) continue;
                _cards[i].gameObject.name = "MerchantCard_" + offer[i].name;
                _cards[i].Setup(offer[i], _mgr.GetBadge(offer[i]), i);
            }
            RefreshShop();
        }

        private void HandleGoldChanged(NetPlayer np, float gold)
        {
            if (np != null && np == NetPlayer.Local) RefreshShop();
        }

        // Gold, Preise, Knöpfe aktualisieren
        private void RefreshShop()
        {
            if (_mgr == null || Panel == null || !Panel.activeSelf) return;
            float gold = MerchantManager.LocalGold;

            if (ShardsText != null) ShardsText.text = $"{Mathf.FloorToInt(gold)} {MerchantCardUI.CurrencyLabel}";

            foreach (var c in _cards)
                if (c != null) c.RefreshShopState();

            int rerollPrice = _mgr.RerollPrice;
            if (RerollButton != null) RerollButton.interactable = _mgr.CanReroll;
            if (RerollCostText != null)
            {
                if (!_rerollColorRead)
                {
                    _rerollColor = RerollCostText.color;
                    _rerollColorRead = true;
                }
                if (RerollPriceIcon != null) RerollPriceIcon.gameObject.SetActive(false);
                RerollCostText.text = $"Neu würfeln ({rerollPrice} {MerchantCardUI.CurrencyLabel})";
                RerollCostText.color = gold + 0.001f >= rerollPrice ? _rerollColor : UnaffordableColor;
            }

            if (DoneButtonText != null)
                DoneButtonText.text = _confirmUntil > 0f ? ConfirmLabel : DoneLabel;
        }

        private void HandlePurchaseFailed(int slot)
        {
            ToastUI.Show("Nicht genug Gold");
            if (slot >= 0)
            {
                foreach (var c in _cards)
                    if (c != null && c.Slot == slot) c.Shake();
            }
            else if (RerollButton != null && isActiveAndEnabled)
            {
                if (_rerollShake != null) StopCoroutine(_rerollShake);
                _rerollShake = StartCoroutine(ShakeRoutine(RerollButton.transform));
            }
        }

        private System.Collections.IEnumerator ShakeRoutine(Transform target)
        {
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
            _rerollShake = null;
        }

        private void OnRerollClicked()
        {
            GameAudio.Play(SfxId.Reroll);
            if (_mgr != null) _mgr.Reroll();
        }

        private void OnDoneClicked()
        {
            if (_mgr == null || PauseManager.IsPaused) return;
            // Gratis-Karte noch offen → einmal nachfragen
            if (!_mgr.HasTakenFreeCard && _confirmUntil < 0f)
            {
                _confirmUntil = Time.unscaledTime + Mathf.Max(0.5f, ConfirmWindow);
                RefreshShop();
                return;
            }
            _confirmUntil = -1f;
            _mgr.CloseShop();
        }

        private void Hide()
        {
            _confirmUntil = -1f;
            _cards.Clear();
            if (Panel != null) Panel.SetActive(false);
        }
    }
}
