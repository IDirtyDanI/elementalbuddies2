using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Lobby-Zusatz auf dem NetPlayer-Prefab (Setup-Schritt NetSetupUI.SetupNetLobbyPrefab):
    // - Schwierigkeit: nur der Host wählt; sein Wert liegt in der NetworkVariable seines NetLobby und wird auf
    //   Clients in GameSession.DifficultyId übernommen (die eigene Wahl des Clients wird gemerkt und im Menü wiederhergestellt).
    // - Namensänderung in der Lobby (NetPlayer schickt den Namen sonst nur einmal beim Verbinden).
    public class NetLobby : NetworkBehaviour
    {
        public static NetLobby Local { get; private set; }
        public static NetLobby Host { get; private set; }

        // Schwierigkeit des Hosts hat sich geändert (auf Clients bereits in GameSession übernommen)
        public static event Action OnDifficultyChanged;

        // Eigene Stufe eines Clients vor der Übernahme der Host-Stufe (null = nichts zu restaurieren)
        private static string _savedLocalDifficulty;
        private static int _savedLocalSiege = -1;

        // Netzwert = "<Stufen-Id>#<Belagerungsstufe>[#<Map-Id>]" (Belagerungsstufe und Map reisen mit der Schwierigkeit;
        // Format und Rückwärtskompatibilität: LobbyCodec – für die Stadt identisch mit dem alten "<id>#<siege>")
        private static string Pack(string id) => LobbyCodec.Pack(id, GameSession.SiegeLevel, GameSession.MapId);
        private static void Unpack(string value, out string id, out int siege) => LobbyCodec.Unpack(value, out id, out siege, out _);

        public readonly NetworkVariable<FixedString32Bytes> Difficulty = new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NetPlayer _player;

        // Stufe des Raums (Host-Wert; ohne Verbindung die lokale Wahl)
        public static string RoomDifficultyId
        {
            get
            {
                if (Host != null && Host.IsSpawned)
                {
                    Unpack(Host.Difficulty.Value.ToString(), out string id, out _);
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                return GameSession.DifficultyId;
            }
        }

        // Map des Raums (Host-Wert; ohne Verbindung die lokale Wahl). Clients übernehmen sie nicht in ihre PlayerPrefs –
        // die Szene lädt ohnehin der Host (NGO-Szenenverwaltung), der Wert dient der Lobby-Anzeige.
        public static string RoomMapId
        {
            get
            {
                if (Host != null && Host.IsSpawned)
                {
                    LobbyCodec.Unpack(Host.Difficulty.Value.ToString(), out _, out _, out string map);
                    return map;
                }
                return GameSession.MapId;
            }
        }

        void Awake()
        {
            _player = GetComponent<NetPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
            if (OwnerClientId == NetworkManager.ServerClientId)
            {
                Host = this;
                if (IsServer) Difficulty.Value = new FixedString32Bytes(Pack(GameSession.DifficultyId));
            }
            Difficulty.OnValueChanged += HandleDifficultyChanged;
            if (Host == this) ApplyHostDifficulty();
        }

        public override void OnNetworkDespawn()
        {
            Difficulty.OnValueChanged -= HandleDifficultyChanged;
            if (Local == this) Local = null;
            if (Host == this) Host = null;
        }

        public override void OnDestroy()
        {
            if (Local == this) Local = null;
            if (Host == this) Host = null;
            base.OnDestroy();
        }

        private void HandleDifficultyChanged(FixedString32Bytes oldValue, FixedString32Bytes newValue)
        {
            if (Host == this) ApplyHostDifficulty();
        }

        private void ApplyHostDifficulty()
        {
            if (!IsServer)
            {
                Unpack(Difficulty.Value.ToString(), out string id, out int siege);
                if (!string.IsNullOrEmpty(id) && id != GameSession.DifficultyId)
                {
                    if (_savedLocalDifficulty == null) _savedLocalDifficulty = GameSession.DifficultyId;
                    GameSession.DifficultyId = id;
                }
                if (siege != GameSession.SiegeLevel)
                {
                    if (_savedLocalSiege < 0) _savedLocalSiege = GameSession.SiegeLevel;
                    GameSession.SiegeLevel = siege;
                }
            }
            OnDifficultyChanged?.Invoke();
        }

        // Host: gewählte Stufe an alle verteilen (Aufruf nach jeder Änderung im Menü und vor dem Spielstart)
        public static void PushDifficulty(string id)
        {
            if (Host == null || !Host.IsSpawned || !Host.IsServer || string.IsNullOrEmpty(id)) return;
            var v = new FixedString32Bytes(Pack(id));
            if (!Host.Difficulty.Value.Equals(v)) Host.Difficulty.Value = v;
        }

        // Menü ohne Verbindung: eigene Stufe des Clients wiederherstellen
        public static void RestoreLocalDifficulty()
        {
            if (_savedLocalSiege >= 0)
            {
                GameSession.SiegeLevel = _savedLocalSiege;
                _savedLocalSiege = -1;
            }
            if (_savedLocalDifficulty == null) return;
            GameSession.DifficultyId = _savedLocalDifficulty;
            _savedLocalDifficulty = null;
        }

        // ---------------- Name ----------------

        public void RequestName(string playerName)
        {
            if (!IsOwner || !IsSpawned) return;
            SetNameRpc(new FixedString64Bytes(Clip(playerName)));
        }

        [Rpc(SendTo.Server)]
        private void SetNameRpc(FixedString64Bytes playerName)
        {
            if (_player == null) _player = GetComponent<NetPlayer>();
            if (_player == null) return;
            string n = playerName.ToString().Trim();
            if (string.IsNullOrEmpty(n)) n = "Spieler " + (_player.Slot + 1);
            _player.PlayerName.Value = new FixedString64Bytes(n);
        }

        public static string Clip(string s)
        {
            s = (s ?? "").Trim();
            return s.Length > 20 ? s.Substring(0, 20) : s;
        }
    }
}
