using UnityEngine;
using System;

namespace ElementalBuddies
{
    // Begrenzt die Anzahl gleichzeitig platzierter Buddies; Slots wachsen mit abgeschlossenen Wellen
    public class BuddySlotManager : MonoBehaviour
    {
        public static BuddySlotManager Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private GlobalSettingsSO settings;

        public int MaxSlots { get; private set; } = 4;
        public int UsedSlots => ElementalBuddy.ActiveCount;
        public bool HasFreeSlot => Unlimited || UsedSlots < MaxSlots;
        public bool Unlimited { get; private set; } // Dev-Modus: kein Buddy-Limit
        public int Cap => settings != null ? settings.MaxBuddySlots : 30;
        // Obergrenze erreicht → keine Slot-Karten mehr im Wellen-Draft
        public bool IsAtCap => MaxSlots >= Cap;

        public void SetUnlimited(bool value)
        {
            if (Unlimited == value) return;
            Unlimited = value;
            OnSlotsChanged?.Invoke();
        }

        public event Action OnSlotsChanged;

        private int _wavesCompleted;
        private WaveManager _waveManager;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (settings == null) settings = Resources.Load<GlobalSettingsSO>("GlobalSettings");
            if (settings != null) MaxSlots = Mathf.Max(0, settings.StartBuddySlots);
        }

        void OnEnable()
        {
            ElementalBuddy.OnBuddyCountChanged += NotifyChanged;
        }

        void OnDisable()
        {
            ElementalBuddy.OnBuddyCountChanged -= NotifyChanged;
        }

        void Start()
        {
            // Fallback: Settings vom EconomyManager übernehmen
            if (settings == null && EconomyManager.Instance != null)
            {
                settings = EconomyManager.Instance.Settings;
                if (settings != null) MaxSlots = Mathf.Max(0, settings.StartBuddySlots);
            }

            _waveManager = WaveManager.Instance;
            if (_waveManager != null) _waveManager.OnWaveEnd += HandleWaveEnd;

            NotifyChanged();
        }

        void OnDestroy()
        {
            if (_waveManager != null) _waveManager.OnWaveEnd -= HandleWaveEnd;
            if (Instance == this) Instance = null;
        }

        private void HandleWaveEnd()
        {
            _wavesCompleted++;
            int every = settings != null ? settings.SlotEveryNWaves : 3;
            if (every > 0 && _wavesCompleted % every == 0) AddSlot();
        }

        public void AddSlot(int n = 1)
        {
            int cap = Cap;
            int newMax = Mathf.Clamp(MaxSlots + n, 0, Mathf.Max(cap, MaxSlots));
            if (newMax == MaxSlots) return;
            MaxSlots = newMax;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            OnSlotsChanged?.Invoke();
        }
    }
}
