using UnityEngine;
using System;

namespace ElementalBuddies
{
    // Begrenzt die Anzahl gleichzeitig platzierter Buddies; Slots wachsen mit abgeschlossenen Wellen.
    // Mehrspieler: Bauplätze sind geteilt. MaxSlots/Unlimited ändert nur der Server (AddSlot, SetUnlimited, Wellenende);
    // NetGame.Build spiegelt sie auf die Clients. UsedSlots zählt das auf allen Rechnern synchrone Buddy-Register.
    public class BuddySlotManager : MonoBehaviour
    {
        public static BuddySlotManager Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private GlobalSettingsSO settings;

        public int MaxSlots { get; private set; } = 4;
        // Wartende Phönix-Wiedergeburten reservieren ihren Slot
        public int UsedSlots => ElementalBuddy.ActiveCount + (Net.IsServer ? PhoenixRebirth.PendingCount : PhoenixRebirth.RemotePendingCount);
        public bool HasFreeSlot => Unlimited || UsedSlots < MaxSlots;
        public bool Unlimited { get; private set; } // Dev-Modus: kein Buddy-Limit
        public int Cap => settings != null ? settings.MaxBuddySlots : 30;
        // Obergrenze erreicht → keine Slot-Karten mehr im Wellen-Draft
        public bool IsAtCap => MaxSlots >= Cap;

        // Nur Server (Dev-Modus des Hosts)
        public void SetUnlimited(bool value)
        {
            if (!Net.IsServer || Unlimited == value) return;
            Unlimited = value;
            PushToNet();
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
            PhoenixRebirth.OnPendingChanged += NotifyChanged;
        }

        void OnDisable()
        {
            ElementalBuddy.OnBuddyCountChanged -= NotifyChanged;
            PhoenixRebirth.OnPendingChanged -= NotifyChanged;
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

            if (Net.IsServer) PushToNet();
            else if (NetGame.Ready) NetGame.Instance.ClientPullSlots();
            NotifyChanged();
        }

        void OnDestroy()
        {
            if (_waveManager != null) _waveManager.OnWaveEnd -= HandleWaveEnd;
            if (Instance == this) Instance = null;
        }

        private void HandleWaveEnd()
        {
            if (!Net.IsServer) return; // Wellenende feuert auch auf Clients; Plätze vergibt der Server
            _wavesCompleted++;
            int every = settings != null ? settings.SlotEveryNWaves : 3;
            if (every > 0 && _wavesCompleted % every == 0) AddSlot();
        }

        // Nur Server (Wellen-Draft, Händler, Dev-Tools); auf Clients wirkungslos
        public void AddSlot(int n = 1)
        {
            if (!Net.IsServer) return;
            int cap = Cap;
            int newMax = Mathf.Clamp(MaxSlots + n, 0, Mathf.Max(cap, MaxSlots));
            if (newMax == MaxSlots) return;
            MaxSlots = newMax;
            PushToNet();
            NotifyChanged();
        }

        private void PushToNet()
        {
            if (Net.IsServer && NetGame.Ready) NetGame.Instance.ServerSetSlots(MaxSlots, Unlimited);
        }

        // Clients: Stand vom Server (NetGame.Build)
        internal void NetApply(int maxSlots, bool unlimited)
        {
            MaxSlots = Mathf.Max(0, maxSlots);
            Unlimited = unlimited;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            OnSlotsChanged?.Invoke();
        }
    }
}
