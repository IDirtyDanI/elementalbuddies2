using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Kartenwahl am Wellenende, seit 2026-10-16 als „Kartenladen“ (wie der Händler-Laden): Karten decken sich
    // nacheinander auf, die erste ist gratis, weitere kosten Seelensplitter (Preisschild unter der Karte, „Gekauft“-Stempel).
    // Kopf: Untertitel mit Preisregel und Splitter-Plakette (Solo: Kasse, Koop: eigener Rest-Anteil). Unten „Neu würfeln“
    // (Gratis-Neuwürfe zuerst, dann 25, 40, 55 …; ersetzt die nicht gekauften Karten) und „Überspringen (+N)“, solange
    // keine Karte genommen wurde – danach an derselben Stelle „Fertig“.
    // Knöpfe, Untertitel und Plakette werden aus dem Händlerladen geklont (gleicher Stil), falls nicht verdrahtet –
    // Canvas.prefab bleibt unverändert, ein Neubau des Canvas behält damit das Layout.
    public class UpgradeScreenUI : MonoBehaviour
    {
        public GameObject Panel; // The whole popup
        public Transform CardsContainer;
        public GameObject CardPrefab;

        [Header("Sprint 3 / Kartenladen (optional, sonst geklont)")]
        public Button RerollButton;
        public Button SkipButton;
        public Button DoneButton;
        public TMP_Text SubtitleText;
        public TMP_Text ShardsText;

        [Tooltip("Optional: Splitter-Symbol hinter dem Würfel-Preis (sonst icon_shard aus Händlerladen/HUD).")]
        public Sprite ShardIcon;
        [Tooltip("Preisfarbe (wie die Splitter-Zahl im HUD) bzw. wenn der eigene Anteil nicht reicht.")]
        public Color ShardPriceColor = new Color(0.851f, 0.722f, 1f);
        public Color UnaffordableColor = new Color(0.78f, 0.15f, 0.12f);

        [Header("Layout (Panel-Koordinaten, Referenz 1920×1080)")]
        public Vector2 RerollPos = new Vector2(-200f, -330f);
        public Vector2 SkipPos = new Vector2(200f, -330f);
        public Vector2 SubtitlePos = new Vector2(0f, 222f);
        public Vector2 ShardCounterPos = new Vector2(640f, 300f);
        [Tooltip("Platz unten für Bauleiste + „Buddies“-Reiter (Panel-Einheiten). Bei flachen Formaten (21:9) rückt der " +
                 "ganze Inhalt so weit nach oben, dass die Knöpfe darüber bleiben.")]
        public float BottomReserve = 300f;
        public float MaxLift = 120f;

        private TMP_Text _rerollText, _skipText, _rerollHint, _shareCaption;
        private Image _rerollIcon;
        private string _lastRerollLabel;
        private readonly List<UpgradeCardUI> _cards = new List<UpgradeCardUI>();
        private float _nextRefresh;
        private Coroutine _rerollShake;
        private const float RevealStagger = 0.14f;
        private readonly List<RectTransform> _liftTargets = new List<RectTransform>();
        private readonly List<Vector2> _liftBase = new List<Vector2>();
        private float _lift = -1f;

        void Start()
        {
            var um = UpgradeManager.Instance;
            if (um != null)
            {
                um.OnUpgradesAvailable += ShowUpgrades;
                um.OnUpgradeSelected += HideUpgrades;
                um.OnOfferChanged += HandleOfferChanged;
                um.OnDraftRejected += HandleRejected;
            }
            EnsureButtons();
            if (Panel != null) Panel.SetActive(false);
        }

        void OnDestroy()
        {
            var um = UpgradeManager.Instance;
            if (um != null)
            {
                um.OnUpgradesAvailable -= ShowUpgrades;
                um.OnUpgradeSelected -= HideUpgrades;
                um.OnOfferChanged -= HandleOfferChanged;
                um.OnDraftRejected -= HandleRejected;
            }
        }

        private void EnsureButtons()
        {
            if (Panel == null) return;
            var shop = FindFirstObjectByType<MerchantScreenUI>(FindObjectsInactive.Include);
            if (RerollButton == null || SkipButton == null || DoneButton == null)
            {
                Button template = shop != null ? (shop.DoneButton != null ? shop.DoneButton : shop.RerollButton) : null;
                if (template != null)
                {
                    if (RerollButton == null) RerollButton = CloneButton(template, "RerollButton", RerollPos);
                    if (SkipButton == null) SkipButton = CloneButton(template, "SkipButton", SkipPos);
                    if (DoneButton == null) DoneButton = CloneButton(template, "DoneButton", SkipPos);
                }
            }
            if (SubtitleText == null && shop != null && shop.SubtitleText != null)
                SubtitleText = CloneText(shop.SubtitleText.gameObject, "Subtitle", SubtitlePos);
            if (ShardsText == null && shop != null && shop.ShardsText != null && shop.ShardsText.transform.parent != null
                && shop.ShardsText.transform.parent != shop.transform)
                ShardsText = CloneText(shop.ShardsText.transform.parent.gameObject, "ShardCounter", ShardCounterPos);
            BuildShareCaption();

            if (RerollButton != null)
            {
                RerollButton.onClick.RemoveAllListeners();
                RerollButton.onClick.AddListener(OnRerollClicked);
                _rerollText = RerollButton.GetComponentInChildren<TMP_Text>(true);
                BuildRerollExtras();
            }
            if (SkipButton != null)
            {
                SkipButton.onClick.RemoveAllListeners();
                SkipButton.onClick.AddListener(() => { if (UpgradeManager.Instance != null) UpgradeManager.Instance.SkipDraft(); });
                _skipText = SkipButton.GetComponentInChildren<TMP_Text>(true);
            }
            CollectLiftTargets();
            if (DoneButton != null)
            {
                DoneButton.onClick.RemoveAllListeners();
                DoneButton.onClick.AddListener(() => { if (UpgradeManager.Instance != null) UpgradeManager.Instance.FinishDraft(); });
                var t = DoneButton.GetComponentInChildren<TMP_Text>(true);
                if (t != null) t.text = "Fertig";
                DoneButton.gameObject.SetActive(false);
            }
        }

        // Kopie eines Händler-Elements (Untertitel bzw. Splitter-Plakette) ins Kartenwahl-Panel; liefert den ersten Text
        private TMP_Text CloneText(GameObject template, string name, Vector2 pos)
        {
            var go = Instantiate(template, Panel.transform, false);
            go.name = name;
            go.SetActive(true);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(mb is Graphic) && !(mb is LayoutGroup) && !(mb is ContentSizeFitter) && !(mb is LayoutElement) && !(mb is BaseMeshEffect))
                    Destroy(mb);
            var txt = go.GetComponent<TMP_Text>();
            if (txt == null)
            {
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                    if (t.name == "ShardsText") return t;
                txt = go.GetComponentInChildren<TMP_Text>(true);
            }
            return txt;
        }

        // Koop: kleine Zeile unter der Plakette („Dein Anteil · Kasse 230“)
        private void BuildShareCaption()
        {
            if (ShardsText == null || SubtitleText == null) return;
            var plaque = ShardsText.transform.parent as RectTransform;
            if (plaque == null) return;
            var go = Instantiate(SubtitleText.gameObject, Panel.transform, false);
            go.name = "ShareCaption";
            _shareCaption = go.GetComponent<TMP_Text>();
            var rt = _shareCaption.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(Mathf.Max(plaque.sizeDelta.x + 120f, 300f), 30f);
            rt.anchoredPosition = plaque.anchoredPosition + new Vector2(0f, -plaque.sizeDelta.y * 0.5f - 4f);
            _shareCaption.enableAutoSizing = true;
            _shareCaption.fontSizeMin = 14f;
            _shareCaption.fontSizeMax = 22f;
            _shareCaption.alignment = TextAlignmentOptions.Top;
            _shareCaption.raycastTarget = false;
            go.SetActive(false);
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
            // zu teuer: Knopf bleibt klickbar (Toast + Wackeln), Preis rot, Hinweis darunter
            RerollButton.interactable = !um.IsRequestPending;
            if (_rerollHint != null)
            {
                _rerollHint.text = um.IsSharedTreasury ? "Dein Anteil reicht nicht" : "Zu wenig Seelensplitter";
                if (_rerollHint.gameObject.activeSelf != !affordable) _rerollHint.gameObject.SetActive(!affordable);
            }
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

        // Alle Inhalte des Panels (Banner, Untertitel, Karten, Plakette, Knöpfe) gemeinsam verschieben können
        private void CollectLiftTargets()
        {
            _liftTargets.Clear();
            _liftBase.Clear();
            foreach (Transform ch in Panel.transform)
            {
                var rt = ch as RectTransform;
                if (rt == null || rt.anchorMin != rt.anchorMax) continue; // Vollbild-Hintergründe bleiben
                _liftTargets.Add(rt);
                _liftBase.Add(rt.anchoredPosition);
            }
        }

        private void ApplyLift()
        {
            if (_liftTargets.Count == 0 || RerollButton == null) return;
            float h = ((RectTransform)Panel.transform).rect.height;
            float buttonBottom = RerollPos.y - ((RectTransform)RerollButton.transform).sizeDelta.y * 0.5f;
            float lift = Mathf.Clamp(-h * 0.5f + BottomReserve - buttonBottom, 0f, MaxLift);
            if (Mathf.Abs(lift - _lift) < 0.5f) return;
            _lift = lift;
            for (int i = 0; i < _liftTargets.Count; i++)
                if (_liftTargets[i] != null) _liftTargets[i].anchoredPosition = _liftBase[i] + new Vector2(0f, lift);
        }

        void Update()
        {
            var um = UpgradeManager.Instance;
            if (um == null || Panel == null || !Panel.activeSelf) return;
            ApplyLift();
            if (RerollButton != null) RefreshReroll(um);
            bool canSkip = um.CanSkip;
            if (SkipButton != null)
            {
                if (SkipButton.gameObject.activeSelf != canSkip) SkipButton.gameObject.SetActive(canSkip);
                SkipButton.interactable = !um.IsRequestPending;
                if (_skipText != null) _skipText.text = $"Überspringen <color=#9fe8ff>+{um.OfferSkipShards}</color>";
            }
            if (DoneButton != null)
            {
                bool showDone = !canSkip || SkipButton == null;
                if (DoneButton.gameObject.activeSelf != showDone) DoneButton.gameObject.SetActive(showDone);
                DoneButton.interactable = !um.IsRequestPending;
            }
            // Splitter (Koop: Mitspieler bauen nebenher) → Preise/Plakette regelmäßig nachziehen
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                RefreshHeader(um);
                foreach (var c in _cards) if (c != null) c.RefreshShopState();
            }
        }

        private void RefreshHeader(UpgradeManager um)
        {
            int spendable = um.SpendableShards;
            bool coop = um.IsSharedTreasury;
            if (ShardsText != null) ShardsText.text = spendable.ToString();
            if (_shareCaption != null)
            {
                if (_shareCaption.gameObject.activeSelf != coop) _shareCaption.gameObject.SetActive(coop);
                if (coop)
                {
                    float team = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentShards : 0f;
                    _shareCaption.text = $"Dein Anteil · Kasse {Mathf.FloorToInt(team)}";
                }
            }
            if (SubtitleText != null)
            {
                string who = coop ? "deinem Anteil" : "der Kasse";
                SubtitleText.text = um.OfferPicks == 0
                    ? $"Die erste Karte ist gratis – jede weitere kostet Seelensplitter aus {who}."
                    : $"Nächste Karte: {um.OfferCardPrice} Seelensplitter – „Fertig“ schließt die Kartenwahl.";
            }
        }

        private void ShowUpgrades(List<UpgradeDefinitionSO> upgrades)
        {
            if (Panel != null) Panel.SetActive(true);
            _lastRerollLabel = null;

            for (int i = CardsContainer.childCount - 1; i >= 0; i--) DestroyCard(CardsContainer.GetChild(i).gameObject);
            _cards.Clear();

            // Karten nacheinander aufdecken
            for (int i = 0; i < upgrades.Count; i++)
                _cards.Add(CreateCard(upgrades[i], i, 0.1f + RevealStagger * i, -1));
            _nextRefresh = 0f;
            Update();
        }

        private UpgradeCardUI CreateCard(UpgradeDefinitionSO up, int slot, float revealDelay, int siblingIndex)
        {
            GameObject go = Instantiate(CardPrefab, CardsContainer);
            if (siblingIndex >= 0) go.transform.SetSiblingIndex(siblingIndex);
            var ui = go.GetComponent<UpgradeCardUI>();
            if (ui == null) return null;
            ui.Setup(up, slot);
            if (revealDelay >= 0f) ui.Reveal(revealDelay);
            return ui;
        }

        // Kauf / Neu würfeln: neue Karten an ihrer Stelle aufdecken, übrige nur neu einfärben
        private void HandleOfferChanged()
        {
            var um = UpgradeManager.Instance;
            if (um == null || Panel == null || !Panel.activeSelf) return;
            var offer = um.LocalOffer;
            int revealIndex = 0;
            for (int i = 0; i < offer.Count; i++)
            {
                var cur = i < _cards.Count ? _cards[i] : null;
                bool reopened = cur != null && cur.Data == offer[i] && IsBoughtShown(cur) && !um.IsSlotBought(i);
                if (cur != null && cur.Data == offer[i] && !reopened)
                {
                    cur.RefreshShopState();
                    continue;
                }
                if (cur != null) DestroyCard(cur.gameObject);
                var fresh = CreateCard(offer[i], i, 0.05f + RevealStagger * revealIndex++, i);
                // (Geschwister-Index i stimmt, weil die alte Karte schon aus dem Container genommen ist)
                if (i < _cards.Count) _cards[i] = fresh;
                else _cards.Add(fresh);
            }
            for (int i = _cards.Count - 1; i >= offer.Count; i--)
            {
                if (_cards[i] != null) DestroyCard(_cards[i].gameObject);
                _cards.RemoveAt(i);
            }
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)CardsContainer);
            _nextRefresh = 0f;
        }

        // Sofort aus dem Layout nehmen (Destroy wirkt erst am Frame-Ende – sonst rechnet die HorizontalLayoutGroup
        // mit der alten Karte und die neue landet auf dem Platz der Nachbarkarte)
        private static void DestroyCard(GameObject go)
        {
            go.SetActive(false);
            go.transform.SetParent(null, false);
            Destroy(go);
        }

        private static bool IsBoughtShown(UpgradeCardUI c) => c.BoughtOverlay != null && c.BoughtOverlay.activeSelf;

        private void HandleRejected(int slot, DraftDenial reason)
        {
            if (slot >= 0)
            {
                foreach (var c in _cards)
                    if (c != null && c.Slot == slot) c.Shake();
            }
            else if (slot == -1 && RerollButton != null && isActiveAndEnabled)
            {
                if (_rerollShake != null) StopCoroutine(_rerollShake);
                _rerollShake = StartCoroutine(ShakeRoutine(RerollButton.transform));
            }
            _nextRefresh = 0f;
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
            var um = UpgradeManager.Instance;
            if (um == null) return;
            if (um.CanAffordReroll && !um.IsRequestPending) GameAudio.Play(SfxId.Reroll);
            um.RequestReroll();
        }

        private void HideUpgrades()
        {
            _cards.Clear();
            if (Panel != null) Panel.SetActive(false);
        }
    }
}
