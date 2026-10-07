using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Bereich "Wellen & Spielzustand" (Paket B): Wellen-Nummer, verbleibende Gegner, Spielzustand, Nexus-HP,
    // Bereit-Abstimmung für den Wellenstart, Warnflächen der Bosse.
    // Der Server schreibt (WaveManager / GameManager / Nexus rufen die Server*-Methoden), Clients spiegeln die Werte in
    // die bestehenden Properties und feuern die bekannten Ereignisse (OnWaveStart/OnWaveEnd/OnPortalOpened/OnGameOver/
    // OnStateChanged) lokal, damit HUD, Belagerungs-Optik, Erfolge usw. unverändert funktionieren.
    public partial class NetGame
    {
        // ---------------- Zustand ----------------

        private readonly NetworkVariable<int> _waveIndex = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _waveActive = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _enemiesRemaining = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _gameState = new NetworkVariable<byte>(
            (byte)GameState.Building, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString128Bytes> _gameOverReason = new NetworkVariable<FixedString128Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _lastWaveReached = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _nexusHP = new NetworkVariable<float>(
            -1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _nexusMaxHP = new NetworkVariable<float>(
            -1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // Grund, warum die nächste Welle noch nicht starten darf (WaveGate auf dem Server), leer = frei
        private readonly NetworkVariable<FixedString128Bytes> _waitReason = new NetworkVariable<FixedString128Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // DevTools-Cheats beim Host aktiv → auch Clients vergeben keine Erfolge
        private readonly NetworkVariable<bool> _hostCheats = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // Client-Ids mit Bereit-Stimme für die nächste Welle
        private readonly NetworkList<ulong> _readyClients = new NetworkList<ulong>();

        // Server mit mindestens einem entfernten Client (sonst sparen sich die Optik-RPCs)
        public static bool HasRemoteClients
        {
            get
            {
                if (!Ready || !Instance.IsServer) return false;
                var nm = Instance.NetworkManager;
                return nm != null && nm.ConnectedClientsIds.Count > 1;
            }
        }

        // Bereit-Abstimmung geändert (Stimme, Spieler beigetreten/gegangen)
        public static event Action OnReadyChanged;

        public static string WaitReason => Ready ? Instance._waitReason.Value.ToString() : "";
        public static bool HostCheatsActive => Ready && Instance._hostCheats.Value;

        partial void OnSpawnWaves()
        {
            _readyClients.OnListChanged += HandleReadyListChanged;
            if (IsServer)
            {
                var nm = NetworkManager;
                if (nm != null) nm.OnClientDisconnectCallback += HandleClientDisconnectedWaves;
                var wm = WaveManager.Instance;
                if (wm != null) ServerSyncWaveState(wm.CurrentWaveIndex, wm.IsWaveActive, wm.EnemiesRemaining);
                var gm = GameManager.Instance;
                if (gm != null) _gameState.Value = (byte)gm.CurrentState;
                var nexus = Nexus.Instance;
                if (nexus != null) ServerSetNexusHP(nexus.CurrentHP, nexus.MaxHP);
                return;
            }

            _waveIndex.OnValueChanged += HandleWaveValueChanged;
            _waveActive.OnValueChanged += HandleWaveActiveChanged;
            _enemiesRemaining.OnValueChanged += HandleWaveValueChanged;
            _gameState.OnValueChanged += HandleGameStateChanged;
            _nexusHP.OnValueChanged += HandleNexusChanged;
            _nexusMaxHP.OnValueChanged += HandleNexusChanged;
            // Anfangszustand übernehmen
            ApplyWaveMirror();
            HandleNexusChanged(0f, 0f);
            HandleGameStateChanged(0, _gameState.Value);
        }

        partial void OnDespawnWaves()
        {
            _readyClients.OnListChanged -= HandleReadyListChanged;
            var nm = NetworkManager;
            if (nm != null) nm.OnClientDisconnectCallback -= HandleClientDisconnectedWaves;
            _waveIndex.OnValueChanged -= HandleWaveValueChanged;
            _waveActive.OnValueChanged -= HandleWaveActiveChanged;
            _enemiesRemaining.OnValueChanged -= HandleWaveValueChanged;
            _gameState.OnValueChanged -= HandleGameStateChanged;
            _nexusHP.OnValueChanged -= HandleNexusChanged;
            _nexusMaxHP.OnValueChanged -= HandleNexusChanged;
        }

        // ---------------- Wellen (Server → Clients) ----------------

        public void ServerSyncWaveState(int waveIndex, bool waveActive, int enemiesRemaining)
        {
            if (!IsSpawned || !IsServer) return;
            if (_waveIndex.Value != waveIndex) _waveIndex.Value = waveIndex;
            if (_waveActive.Value != waveActive) _waveActive.Value = waveActive;
            if (_enemiesRemaining.Value != enemiesRemaining) _enemiesRemaining.Value = enemiesRemaining;
        }

        public void ServerWaveStarted(int waveIndex, int enemiesRemaining)
        {
            if (!IsSpawned || !IsServer) return;
            ServerSyncWaveState(waveIndex, true, enemiesRemaining);
            if (HasRemoteClients) WaveStartedRpc(waveIndex, enemiesRemaining);
        }

        public void ServerWaveEnded(int newWaveIndex)
        {
            if (!IsSpawned || !IsServer) return;
            ServerSyncWaveState(newWaveIndex, false, 0);
            if (HasRemoteClients) WaveEndedRpc(newWaveIndex);
        }

        public void ServerPortalOpened(Vector3 portalPosition, bool fireEvents)
        {
            if (HasRemoteClients) PortalOpenedRpc(portalPosition, fireEvents);
        }

        [Rpc(SendTo.NotServer)]
        private void WaveStartedRpc(int waveIndex, int enemiesRemaining)
        {
            var wm = WaveManager.Instance;
            if (wm != null) wm.HandleRemoteWaveStart(waveIndex, enemiesRemaining);
        }

        [Rpc(SendTo.NotServer)]
        private void WaveEndedRpc(int newWaveIndex)
        {
            var wm = WaveManager.Instance;
            if (wm != null) wm.HandleRemoteWaveEnd(newWaveIndex);
        }

        [Rpc(SendTo.NotServer)]
        private void PortalOpenedRpc(Vector3 portalPosition, bool fireEvents)
        {
            var wm = WaveManager.Instance;
            if (wm != null) wm.HandleRemotePortalOpened(portalPosition, fireEvents);
        }

        private void HandleWaveValueChanged(int oldValue, int newValue) => ApplyWaveMirror();
        private void HandleWaveActiveChanged(bool oldValue, bool newValue) => ApplyWaveMirror();

        private void ApplyWaveMirror()
        {
            var wm = WaveManager.Instance;
            if (wm != null) wm.ApplyRemoteWaveState(_waveIndex.Value, _waveActive.Value, _enemiesRemaining.Value);
        }

        // ---------------- Spielzustand ----------------

        public void ServerSetGameState(GameState state)
        {
            if (!IsSpawned || !IsServer) return;
            if (_gameState.Value != (byte)state) _gameState.Value = (byte)state;
        }

        public void ServerGameOver(string reason, int lastWaveReached)
        {
            if (!IsSpawned || !IsServer) return;
            var r = new FixedString128Bytes(Truncate(reason, 120));
            _gameOverReason.Value = r;
            _lastWaveReached.Value = lastWaveReached;
            _gameState.Value = (byte)GameState.GameOver;
            if (HasRemoteClients) GameOverRpc(r, lastWaveReached);
        }

        [Rpc(SendTo.NotServer)]
        private void GameOverRpc(FixedString128Bytes reason, int lastWaveReached)
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.ApplyRemoteGameOver(reason.ToString(), lastWaveReached);
        }

        private void HandleGameStateChanged(byte oldValue, byte newValue)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var state = (GameState)newValue;
            if (state == GameState.GameOver) gm.ApplyRemoteGameOver(_gameOverReason.Value.ToString(), _lastWaveReached.Value);
            else gm.ApplyRemoteState(state);
        }

        // ---------------- Nexus ----------------

        public void ServerSetNexusHP(float hp, float maxHp)
        {
            if (!IsSpawned || !IsServer) return;
            if (!Mathf.Approximately(_nexusHP.Value, hp)) _nexusHP.Value = hp;
            if (!Mathf.Approximately(_nexusMaxHP.Value, maxHp)) _nexusMaxHP.Value = maxHp;
        }

        private void HandleNexusChanged(float oldValue, float newValue)
        {
            var nexus = Nexus.Instance;
            if (nexus != null && _nexusHP.Value >= 0f) nexus.ApplyRemoteHealth(_nexusHP.Value, _nexusMaxHP.Value);
        }

        // ---------------- Bereit-Abstimmung ----------------

        public int ReadyVotes
        {
            get
            {
                int n = 0;
                foreach (var id in _readyClients)
                    if (NetPlayer.ByClientId(id) != null) n++;
                return n;
            }
        }

        public bool IsClientReady(ulong clientId) => _readyClients.Contains(clientId);

        // Beliebiger Rechner: eigene Bereit-Stimme setzen/zurückziehen
        public void RequestReady(bool ready)
        {
            if (!IsSpawned) return;
            SetReadyRpc(ready);
        }

        [Rpc(SendTo.Server)]
        private void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var wm = WaveManager.Instance;
            // Während einer Welle / nach Game Over keine Stimmen
            if (ready && (wm == null || wm.IsWaveActive || (GameManager.Instance != null && GameManager.Instance.IsGameOver))) return;
            bool has = _readyClients.Contains(sender);
            if (ready && !has) _readyClients.Add(sender);
            else if (!ready && has) _readyClients.Remove(sender);
        }

        public void ServerClearReady()
        {
            if (!IsSpawned || !IsServer || _readyClients.Count == 0) return;
            _readyClients.Clear();
        }

        public void ServerSetWaitReason(string reason)
        {
            if (!IsSpawned || !IsServer) return;
            var r = new FixedString128Bytes(Truncate(reason ?? "", 120));
            if (!_waitReason.Value.Equals(r)) _waitReason.Value = r;
        }

        public void ServerSetHostCheats(bool active)
        {
            if (!IsSpawned || !IsServer || _hostCheats.Value == active) return;
            _hostCheats.Value = active;
        }

        private void HandleReadyListChanged(NetworkListEvent<ulong> change) => OnReadyChanged?.Invoke();

        private void HandleClientDisconnectedWaves(ulong clientId)
        {
            if (IsServer && IsSpawned && _readyClients.Contains(clientId)) _readyClients.Remove(clientId);
            OnReadyChanged?.Invoke();
        }

        // ---------------- Boss-Warnflächen ----------------

        public void ServerTelegraphSpawn(int id, AoeShape shape, Color color, float fillDuration)
        {
            if (HasRemoteClients) TelegraphSpawnRpc(id, shape, color, fillDuration);
        }

        public void ServerTelegraphEnd(int id, bool finish, float flashTime)
        {
            if (HasRemoteClients) TelegraphEndRpc(id, finish, flashTime);
        }

        [Rpc(SendTo.NotServer)]
        private void TelegraphSpawnRpc(int id, AoeShape shape, Color color, float fillDuration)
        {
            AoeTelegraph.SpawnRemote(id, shape, color, fillDuration);
        }

        [Rpc(SendTo.NotServer)]
        private void TelegraphEndRpc(int id, bool finish, float flashTime)
        {
            var t = AoeTelegraph.FindByNetId(id);
            if (t == null) return;
            if (finish) t.Finish(flashTime);
            else t.Cancel();
        }

        // ---------------- Hilfen ----------------

        // FixedString128Bytes fasst 125 UTF-8-Bytes – Umlaute zählen doppelt
        private static string Truncate(string s, int maxBytes)
        {
            if (string.IsNullOrEmpty(s)) return "";
            while (System.Text.Encoding.UTF8.GetByteCount(s) > maxBytes) s = s.Substring(0, s.Length - 1);
            return s;
        }
    }
}
