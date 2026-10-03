using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    public class HUDManager : MonoBehaviour
    {
        [Header("Stats")]
        public Slider HPSlider;
        public TextMeshProUGUI HPText; // Optional
        public Slider ManaSlider;
        public TextMeshProUGUI ManaText;
        public TextMeshProUGUI ShardText; // Seelensplitter (Bau-Währung)

        [Header("Wave")]
        public TextMeshProUGUI WaveText;
        public Button StartWaveButton;

        [Header("Buddies")]
        public TextMeshProUGUI BuddySlotText; // Optional

        private PlayerStats _playerStats;
        
        void Start()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnManaChanged += UpdateMana;
                EconomyManager.Instance.OnShardsChanged += UpdateShards;
                UpdateMana(); 
                UpdateShards();
            }
            
            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart += UpdateWaveInfo;
                WaveManager.Instance.OnWaveEnd += UpdateWaveInfo;
                UpdateWaveInfo();
                
                if (StartWaveButton != null)
                {
                    StartWaveButton.onClick.RemoveAllListeners(); // Clean slate to avoid doubles
                    StartWaveButton.onClick.AddListener(() => 
                    {
                        Debug.Log("HUDManager: Start Wave Button Clicked!");
                        WaveManager.Instance.StartNextWave();
                    });
                    Debug.Log("HUDManager: StartWaveButton connected successfully.");
                }
                else
                {
                    Debug.LogError("HUDManager: StartWaveButton is NOT assigned in Inspector!");
                }
            }

            if (BuddySlotManager.Instance != null)
            {
                BuddySlotManager.Instance.OnSlotsChanged += UpdateBuddySlots;
            }
            UpdateBuddySlots();

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                _playerStats = player.GetComponent<PlayerStats>();
                if (_playerStats != null)
                {
                    _playerStats.OnHealthChanged += UpdateHP;
                    UpdateHP();
                }
            }
        }

        void OnDestroy()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnManaChanged -= UpdateMana;
                EconomyManager.Instance.OnShardsChanged -= UpdateShards;
            }
            if (WaveManager.Instance != null)
            {
                 WaveManager.Instance.OnWaveStart -= UpdateWaveInfo;
                 WaveManager.Instance.OnWaveEnd -= UpdateWaveInfo;
            }
            if (_playerStats != null) _playerStats.OnHealthChanged -= UpdateHP;
            if (BuddySlotManager.Instance != null) BuddySlotManager.Instance.OnSlotsChanged -= UpdateBuddySlots;
        }

        private void UpdateBuddySlots()
        {
            if (BuddySlotText == null) return;
            if (BuddySlotManager.Instance == null)
            {
                BuddySlotText.text = "";
                return;
            }
            BuddySlotText.text = BuddySlotManager.Instance.Unlimited
                ? $"{BuddySlotManager.Instance.UsedSlots}/∞"
                : $"{BuddySlotManager.Instance.UsedSlots}/{BuddySlotManager.Instance.MaxSlots}";
        }

        private void UpdateMana()
        {
            if (EconomyManager.Instance == null) return;
            float current = EconomyManager.Instance.CurrentMana;
            float max = EconomyManager.Instance.MaxMana;
            
            if (ManaSlider != null)
            {
                ManaSlider.maxValue = max;
                ManaSlider.value = current;
            }
            if (ManaText != null) ManaText.text = $"{Mathf.FloorToInt(current)}/{Mathf.FloorToInt(max)}";
        }

        private void UpdateShards()
        {
            if (ShardText == null || EconomyManager.Instance == null) return;
            ShardText.text = $"{Mathf.FloorToInt(EconomyManager.Instance.CurrentShards)}";
        }

        private void UpdateHP()
        {
            if (_playerStats == null) return;
            if (HPSlider != null)
            {
                HPSlider.maxValue = _playerStats.MaxHP;
                HPSlider.value = _playerStats.CurrentHP;
            }
            if (HPText != null) HPText.text = $"{Mathf.CeilToInt(Mathf.Max(0f, _playerStats.CurrentHP))}/{Mathf.FloorToInt(_playerStats.MaxHP)}";
        }

        public void RefreshWave() => UpdateWaveInfo();

        private void UpdateWaveInfo()
        {
            if (WaveManager.Instance == null) return;
            if (WaveText != null) 
                WaveText.text = $"Welle {WaveManager.Instance.CurrentWaveIndex + 1}";
                
            if (StartWaveButton != null)
                StartWaveButton.interactable = !WaveManager.Instance.IsWaveActive;
        }
    }
}