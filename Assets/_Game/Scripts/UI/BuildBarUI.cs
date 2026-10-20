using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Bottom build bar: one clickable card per buddy (portrait, name, shard cost, hotkey).
    // Nur zwischen den Wellen sichtbar: in der Welle blendet die Leiste weich aus und sinkt etwas ab (CanvasGroup, nicht
    // klickbar), da Buddies dann nicht platziert werden können (InteractionManager.CanBuildNow; Koop: Phase vom Host).
    // Touch-Modus (Mobil, Paket B): keine Tasten-Plaketten, Leiste bei Bedarf größer (Slots ≥ 48 dp, Preise lesbar, aber
    // möglichst zwischen den Stick-Zonen) und im sicheren Bereich; Antippen eines Slots startet Tippen-zum-Bauen.
    public class BuildBarUI : MonoBehaviour
    {
        [System.Serializable]
        public class Slot
        {
            public Button Button;
            public Image Frame;
            public Image Portrait;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI CostText;
            [Tooltip("Tasten-Plakette; leer = Kind „Hotkey/Num“ der Karte. Zeigt die aktuelle Belegung (KeyBindings).")]
            public TextMeshProUGUI HotkeyText;
        }

        public List<Slot> Slots = new List<Slot>();
        public string[] DisplayNames = { "Feuer", "Eis", "Erde", "Licht" };

        [Header("Frame Sprites")]
        public Sprite NormalSprite;
        public Sprite SelectedSprite;
        public Sprite DisabledSprite;

        [Header("Colors")]
        public Color CostAffordable = new Color(0.24f, 0.15f, 0.08f);
        public Color CostTooExpensive = new Color(0.62f, 0.12f, 0.1f);

        [Header("Ein-/Ausblenden (Welle)")]
        [Tooltip("Ein-/Ausblend-Tempo (Alpha pro Sekunde, Echtzeit).")]
        public float FadeSpeed = 4f;
        [Tooltip("So weit (px) sinkt die Leiste beim Ausblenden nach unten.")]
        public float HiddenDrop = 40f;

        [Header("Touch-Modus")]
        [Tooltip("Mindestgröße der Preis-Schrift im Touch-Modus (dp).")]
        public float TouchMinPriceDp = 13f;
        [Tooltip("Abstand der Leiste zum Rand des sicheren Bereichs im Touch-Modus (dp).")]
        public float TouchSafeMarginDp = 6f;

        private CanvasGroup _group;
        private RectTransform _rect;
        private Vector2 _basePos;
        private Vector2 _prefabPos;      // Ausgangslage aus dem Prefab (Touch: _basePos = _prefabPos + Safe-Area-Versatz)
        private float[] _costFontSize;   // ursprüngliche Preis-Schriftgrößen (Touch-Anpassung)

        void Start()
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                int index = i; // capture for the listener
                if (Slots[i].Button != null)
                {
                    Slots[i].Button.onClick.RemoveAllListeners();
                    Slots[i].Button.onClick.AddListener(() =>
                    {
                        if (InteractionManager.Instance != null) InteractionManager.Instance.SelectUnitByIndex(index);
                    });
                }
                if (Slots[i].NameText != null && i < DisplayNames.Length) Slots[i].NameText.text = DisplayNames[i];
            }

            _rect = transform as RectTransform;
            if (_rect != null) _basePos = _prefabPos = _rect.anchoredPosition;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = InteractionManager.CanBuildNow ? 1f : 0f; // Spielstart/Beitritt mitten in der Welle: gleich aus
            UpdateVisibility(true);

            RefreshKeys();
            if (MobilePlatform.TouchMode) ApplyTouchLayout();
            KeyBindings.OnBindingsChanged += RefreshKeys;
            if (InteractionManager.Instance != null) InteractionManager.Instance.OnSelectionChanged += Refresh;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnShardsChanged += Refresh;
            if (BuddySlotManager.Instance != null) BuddySlotManager.Instance.OnSlotsChanged += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            KeyBindings.OnBindingsChanged -= RefreshKeys;
            if (InteractionManager.Instance != null) InteractionManager.Instance.OnSelectionChanged -= Refresh;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnShardsChanged -= Refresh;
            if (BuddySlotManager.Instance != null) BuddySlotManager.Instance.OnSlotsChanged -= Refresh;
        }

        // Kosten/Plätze können sich auch ohne Event ändern (Netz-Abgleich), daher günstig nachpollen
        void Update()
        {
            if (MobilePlatform.TouchMode && Time.frameCount % 30 == 0) ApplyTouchLayout(); // Skalierung/Safe Area können sich ändern
            UpdateVisibility(false);
            if (Time.frameCount % 10 == 0) Refresh();
        }

        // Sichtbar nur in der Bauphase; Echtzeit, damit das Ausblenden auch bei Zeitlupe/Solo-Zeitstopp sauber läuft
        private void UpdateVisibility(bool instant)
        {
            if (_group == null) return;
            bool show = InteractionManager.CanBuildNow;
            float target = show ? 1f : 0f;
            _group.alpha = instant ? target : Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * FadeSpeed);
            _group.interactable = show;
            _group.blocksRaycasts = show; // ausgeblendet: Klicks gehen in die Welt (Zauber) statt an unsichtbare Karten
            if (_rect != null)
            {
                float t = 1f - _group.alpha;
                _rect.anchoredPosition = _basePos + Vector2.down * (HiddenDrop * t * t);
            }
        }

        // Hotkey-Plaketten (Standard 1–4) auf die aktuelle Belegung setzen
        private void RefreshKeys()
        {
            for (int i = 0; i < Slots.Count && i < 4; i++)
            {
                var s = Slots[i];
                if (s.HotkeyText == null && s.Button != null)
                {
                    var t = s.Button.transform.Find("Hotkey/Num");
                    if (t != null) s.HotkeyText = t.GetComponent<TextMeshProUGUI>();
                }
                if (s.HotkeyText == null) continue;
                if (MobilePlatform.TouchMode)
                {
                    // Touch: keine Tasten → Plakette (Kind „Hotkey“) bzw. Text ausblenden
                    var plaque = s.HotkeyText.transform.parent != null && s.HotkeyText.transform.parent.name == "Hotkey"
                        ? s.HotkeyText.transform.parent.gameObject : s.HotkeyText.gameObject;
                    if (plaque.activeSelf) plaque.SetActive(false);
                    continue;
                }
                if (!s.HotkeyText.enableAutoSizing)
                {
                    s.HotkeyText.fontSizeMax = s.HotkeyText.fontSize;
                    s.HotkeyText.fontSizeMin = 9f;
                    s.HotkeyText.enableAutoSizing = true;
                    s.HotkeyText.textWrappingMode = TextWrappingModes.NoWrap;
                }
                s.HotkeyText.text = KeyBindings.Short(KeyBindings.BuildOf(i));
            }
        }

        // ---------------- Touch-Modus ----------------

        // Leiste so weit vergrößern, dass jeder Slot ≥ 48 dp und die Preise ≥ TouchMinPriceDp groß sind – höchstens so breit
        // wie die Lücke zwischen Bewegungs- und Angriffs-Zone (außer die 48 dp verlangen mehr). Fehlt dann noch Schriftgröße,
        // wird nur die Preis-Schrift größer. Danach in den sicheren Bereich schieben (Notch, Home-Indikator).
        private void ApplyTouchLayout()
        {
            if (_rect == null) return;
            float canvasScale = TouchBuildUi.CanvasScale(_rect);

            float minSlot = float.MaxValue;
            float minPrice = float.MaxValue;
            for (int i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (s.Button != null)
                {
                    var r = ((RectTransform)s.Button.transform).rect;
                    minSlot = Mathf.Min(minSlot, Mathf.Min(r.width, r.height));
                }
                if (s.CostText != null) minPrice = Mathf.Min(minPrice, BaseCostFontSize(i));
            }
            if (minSlot == float.MaxValue) return;

            float slotPx = minSlot * canvasScale;                                  // bei Leisten-Skalierung 1
            float needSlot = MobilePlatform.DpToPixels(MobilePlatform.MinTouchTargetDp) / Mathf.Max(1f, slotPx);
            float needPrice = minPrice < float.MaxValue
                ? MobilePlatform.DpToPixels(TouchMinPriceDp) / Mathf.Max(1f, minPrice * canvasScale) : 1f;
            Rect safe = MobilePlatform.SafeArea;
            float gapPx = safe.width * (1f - TouchZones.MoveZoneWidth - TouchZones.AttackZoneWidth);
            float fit = gapPx / Mathf.Max(1f, _rect.rect.width * canvasScale);
            float scale = Mathf.Max(1f, needSlot, Mathf.Min(Mathf.Max(needSlot, needPrice), fit));
            if (!Mathf.Approximately(_rect.localScale.x, scale)) _rect.localScale = new Vector3(scale, scale, 1f);

            // Restliche Preis-Schrift (Leiste durfte nicht breiter werden)
            float minFont = MobilePlatform.DpToPixels(TouchMinPriceDp) / (canvasScale * scale);
            for (int i = 0; i < Slots.Count; i++)
            {
                var t = Slots[i].CostText;
                if (t == null) continue;
                float size = Mathf.Max(BaseCostFontSize(i), minFont);
                if (t.enableAutoSizing)
                {
                    if (t.fontSizeMin < size) t.fontSizeMin = size;
                    if (t.fontSizeMax < size) t.fontSizeMax = size;
                }
                else if (!Mathf.Approximately(t.fontSize, size)) t.fontSize = size;
                t.overflowMode = TextOverflowModes.Overflow;
            }

            // Sicherer Bereich: Lage ohne Ausblend-Absenkung prüfen und Ausgangslage entsprechend versetzen
            Rect r0 = TouchBuildUi.ScreenRect(_rect);
            float parentScale = _rect.parent != null ? Mathf.Max(0.0001f, _rect.parent.lossyScale.y) : 1f;
            Vector2 drop = (_rect.anchoredPosition - _basePos) * parentScale;   // aktuelle Absenkung (Pixel)
            Vector2 offset = (_basePos - _prefabPos) * parentScale;             // bisheriger Versatz (Pixel)
            r0.position -= drop + offset;                                      // Lage an der Prefab-Position
            Vector2 shift = TouchBuildUi.SafeAreaShift(r0, MobilePlatform.DpToPixels(TouchSafeMarginDp));
            Vector2 newBase = _prefabPos + shift / parentScale;
            if ((newBase - _basePos).sqrMagnitude > 0.25f)
            {
                _rect.anchoredPosition += newBase - _basePos;
                _basePos = newBase;
            }
        }

        private float BaseCostFontSize(int i)
        {
            if (_costFontSize == null || _costFontSize.Length != Slots.Count)
            {
                _costFontSize = new float[Slots.Count];
                for (int k = 0; k < Slots.Count; k++)
                {
                    var t = Slots[k].CostText;
                    _costFontSize[k] = t == null ? 0f : (t.enableAutoSizing ? t.fontSizeMax : t.fontSize);
                }
            }
            return _costFontSize[i];
        }

        private void Refresh()
        {
            var im = InteractionManager.Instance;
            if (im == null) return;

            bool slotFree = BuddySlotManager.Instance == null || BuddySlotManager.Instance.HasFreeSlot;
            float shards = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentShards : 0f;

            for (int i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                float cost = im.GetCurrentCost(i);
                bool affordable = cost >= 0 && shards >= cost;
                bool usable = affordable && slotFree;
                bool selected = im.SelectedIndex == i;

                if (s.CostText != null)
                {
                    s.CostText.text = cost >= 0 ? Mathf.CeilToInt(cost).ToString() : "-";
                    s.CostText.color = affordable ? CostAffordable : CostTooExpensive;
                }
                if (s.Frame != null)
                {
                    Sprite sprite = selected ? SelectedSprite : (usable ? NormalSprite : DisabledSprite);
                    if (sprite != null) s.Frame.sprite = sprite;
                }
                if (s.Portrait != null) s.Portrait.color = usable || selected ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.9f);
            }
        }
    }
}
