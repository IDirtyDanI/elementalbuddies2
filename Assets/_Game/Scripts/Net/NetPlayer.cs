using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Netcode-Spielerobjekt (PlayerPrefab des NetworkManagers). Existiert ab dem Verbinden – schon in der Lobby –
    // und überlebt Szenenwechsel. Hält Lobby-Daten (Name, Champion, Bereit) und die persönliche Händlerwährung (Gold).
    // Die Spielfigur selbst ist ein eigenes Objekt (PlayerAvatar), das der Server in der Spielszene spawnt.
    public class NetPlayer : NetworkBehaviour
    {
        public static readonly List<NetPlayer> All = new List<NetPlayer>();
        public static NetPlayer Local { get; private set; }

        public static event Action<NetPlayer> OnPlayerJoined;
        public static event Action<NetPlayer> OnPlayerLeft;
        public static event Action OnLobbyChanged; // Name/Champion/Bereit eines Spielers geändert

        public readonly NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public readonly NetworkVariable<int> Champion = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public readonly NetworkVariable<bool> Ready = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Persönliche Händlerwährung. Nur der Server schreibt (AddGold / TrySpendGold).
        public readonly NetworkVariable<float> Gold = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public ChampionClass ChampionClass => (ChampionClass)Mathf.Clamp(Champion.Value, 0, 2);
        public string DisplayName => PlayerName.Value.ToString();
        public bool IsLocal => IsOwner;

        // Spielfigur dieses Spielers in der Spielszene (null im Menü / vor dem Spawn)
        public PlayerAvatar Avatar { get; internal set; }

        // Reihenfolge des Beitritts (0 = Host) – für Farben, Spawnpunkte, Anzeige
        public int Slot => All.IndexOf(this);

        public static event Action<NetPlayer, float> OnGoldChanged;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this))
            {
                All.Add(this);
                All.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            }
            if (IsOwner)
            {
                Local = this;
                SubmitLobbyDataRpc(new FixedString64Bytes(LocalPlayerName), (int)GameSession.SelectedChampion);
            }

            PlayerName.OnValueChanged += OnLobbyValueChanged;
            Champion.OnValueChanged += OnLobbyValueChanged;
            Ready.OnValueChanged += OnLobbyValueChanged;
            Gold.OnValueChanged += HandleGoldChanged;

            OnPlayerJoined?.Invoke(this);
            OnLobbyChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            PlayerName.OnValueChanged -= OnLobbyValueChanged;
            Champion.OnValueChanged -= OnLobbyValueChanged;
            Ready.OnValueChanged -= OnLobbyValueChanged;
            Gold.OnValueChanged -= HandleGoldChanged;

            All.Remove(this);
            if (Local == this) Local = null;
            OnPlayerLeft?.Invoke(this);
            OnLobbyChanged?.Invoke();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            base.OnDestroy();
        }

        private void OnLobbyValueChanged<T>(T oldValue, T newValue) => OnLobbyChanged?.Invoke();
        private void HandleGoldChanged(float oldValue, float newValue) => OnGoldChanged?.Invoke(this, newValue);

        // ---------------- Lobby ----------------

        [Rpc(SendTo.Server)]
        private void SubmitLobbyDataRpc(FixedString64Bytes playerName, int champion)
        {
            string n = playerName.ToString().Trim();
            if (string.IsNullOrEmpty(n)) n = "Spieler " + (Slot + 1);
            PlayerName.Value = new FixedString64Bytes(n);
            Champion.Value = Mathf.Clamp(champion, 0, 2);
        }

        // Champion-Wahl in der Lobby (vom Besitzer aufgerufen)
        public void RequestChampion(ChampionClass champion)
        {
            if (IsOwner) SetChampionRpc((int)champion);
        }

        [Rpc(SendTo.Server)]
        private void SetChampionRpc(int champion) => Champion.Value = Mathf.Clamp(champion, 0, 2);

        public void RequestReady(bool ready)
        {
            if (IsOwner) SetReadyRpc(ready);
        }

        [Rpc(SendTo.Server)]
        private void SetReadyRpc(bool ready) => Ready.Value = ready;

        // ---------------- Gold (nur Server) ----------------

        public void AddGold(float amount)
        {
            if (!IsServer || amount == 0f) return;
            Gold.Value = Mathf.Max(0f, Gold.Value + amount);
        }

        public bool TrySpendGold(float amount)
        {
            if (!IsServer || Gold.Value + 0.001f < amount) return false;
            Gold.Value -= amount;
            return true;
        }

        public void ResetForNewRun()
        {
            if (!IsServer) return;
            Gold.Value = 0f;
            Ready.Value = false;
        }

        // ---------------- Hilfen ----------------

        public static NetPlayer ByClientId(ulong clientId)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].OwnerClientId == clientId) return All[i];
            return null;
        }

        private const string NamePrefKey = "playerName";

        // Lokal gespeicherter Anzeigename (Hauptmenü)
        public static string LocalPlayerName
        {
            get
            {
                string n = PlayerPrefs.GetString(NamePrefKey, "");
                return string.IsNullOrWhiteSpace(n) ? Environment.UserName : n;
            }
            set
            {
                PlayerPrefs.SetString(NamePrefKey, value ?? "");
                PlayerPrefs.Save();
            }
        }
    }
}
