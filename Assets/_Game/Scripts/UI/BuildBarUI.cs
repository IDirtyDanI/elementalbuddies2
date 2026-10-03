using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Bottom build bar: one clickable card per buddy (portrait, name, shard cost, hotkey).
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

            if (InteractionManager.Instance != null) InteractionManager.Instance.OnSelectionChanged += Refresh;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnShardsChanged += Refresh;
            if (BuddySlotManager.Instance != null) BuddySlotManager.Instance.OnSlotsChanged += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (InteractionManager.Instance != null) InteractionManager.Instance.OnSelectionChanged -= Refresh;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnShardsChanged -= Refresh;
            if (BuddySlotManager.Instance != null) BuddySlotManager.Instance.OnSlotsChanged -= Refresh;
        }

        // Costs change with the game state (combat surcharge), so poll cheaply
        void Update()
        {
            if (Time.frameCount % 10 == 0) Refresh();
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
