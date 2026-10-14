using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Bottom build bar: one clickable card per buddy (portrait, name, shard cost, hotkey).
    // Nur zwischen den Wellen sichtbar: in der Welle blendet die Leiste weich aus und sinkt etwas ab (CanvasGroup, nicht
    // klickbar), da Buddies dann nicht platziert werden können (InteractionManager.CanBuildNow; Koop: Phase vom Host).
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

        private CanvasGroup _group;
        private RectTransform _rect;
        private Vector2 _basePos;

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
            if (_rect != null) _basePos = _rect.anchoredPosition;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = InteractionManager.CanBuildNow ? 1f : 0f; // Spielstart/Beitritt mitten in der Welle: gleich aus
            UpdateVisibility(true);

            RefreshKeys();
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
