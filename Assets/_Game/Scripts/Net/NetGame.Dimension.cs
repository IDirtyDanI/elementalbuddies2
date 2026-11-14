using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Bereich „Dimensionsriss“ (wiki: Design/Dimensionsriss-Plan): Zustand des Events für alle Rechner.
    // Der Server (DimensionEvent) schreibt Phase, „wer ist drin“ (Bitmaske über Client-Ids), Verfalls-Zustand und einen
    // Sekunden-Countdown; Clients spiegeln die Werte in ihr DimensionEvent und bauen daraus alles Lokale (Arena, NavMesh,
    // begehbare Portale, Banner, Ziel-Panel) – auch wer den Wellenstart verpasst hat oder später beitritt.
    // Betreten/Verlassen: Besitzer fragt an (RPC), der Server prüft und schickt dem Besitzer den Teleport
    // (PlayerAvatar.ServerDimensionTeleport). Der Countdown läuft in Spielzeit des Servers (steht in der Pause).
    public partial class NetGame
    {
        private readonly NetworkVariable<byte> _dimPhase = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> _dimInside = new NetworkVariable<ulong>(
            0UL, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _dimDrain = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // Sekunden (aufgerundet) bis zum nächsten Verfalls-Takt bzw. – nach dem Boss-Tod – bis alle zurückgeholt werden
        private readonly NetworkVariable<byte> _dimCountdown = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        partial void OnSpawnDimension()
        {
            if (IsServer)
            {
                // Server: laufendes Event (NetGame kam nach dem DimensionEvent) in die Variablen schreiben
                var ev = DimensionEvent.Instance;
                if (ev != null) ServerSetDimension(ev.Phase, ev.InsideMask, ev.Drain, ev.Countdown);
                return;
            }
            _dimPhase.OnValueChanged += HandleDimByteChanged;
            _dimDrain.OnValueChanged += HandleDimByteChanged;
            _dimCountdown.OnValueChanged += HandleDimByteChanged;
            _dimInside.OnValueChanged += HandleDimMaskChanged;
            PushDimensionToLocal();
        }

        partial void OnDespawnDimension()
        {
            _dimPhase.OnValueChanged -= HandleDimByteChanged;
            _dimDrain.OnValueChanged -= HandleDimByteChanged;
            _dimCountdown.OnValueChanged -= HandleDimByteChanged;
            _dimInside.OnValueChanged -= HandleDimMaskChanged;
        }

        private void HandleDimByteChanged(byte oldValue, byte newValue) => PushDimensionToLocal();
        private void HandleDimMaskChanged(ulong oldValue, ulong newValue) => PushDimensionToLocal();

        // Client: Variablen → DimensionEvent (legt bei Bedarf Arena und Portal-Optik an)
        private void PushDimensionToLocal()
        {
            if (IsServer) return;
            var ev = DimensionEvent.Instance;
            if (ev != null)
                ev.ApplyRemote((DimensionPhase)_dimPhase.Value, _dimInside.Value,
                    (DimensionEventRules.DrainPhase)_dimDrain.Value, _dimCountdown.Value);
        }

        // Client: aktuellen Stand abholen (DimensionEvent entsteht nach NetGame)
        public static void PullDimension()
        {
            if (Ready && !Instance.IsServer) Instance.PushDimensionToLocal();
        }

        // Server: Zustand schreiben (nur Änderungen gehen ins Netz)
        public void ServerSetDimension(DimensionPhase phase, ulong insideMask, DimensionEventRules.DrainPhase drain, int countdown)
        {
            if (!IsSpawned || !IsServer) return;
            byte cd = (byte)Mathf.Clamp(countdown, 0, 255);
            if (_dimPhase.Value != (byte)phase) _dimPhase.Value = (byte)phase;
            if (_dimInside.Value != insideMask) _dimInside.Value = insideMask;
            if (_dimDrain.Value != (byte)drain) _dimDrain.Value = (byte)drain;
            if (_dimCountdown.Value != cd) _dimCountdown.Value = cd;
        }

        // ---------------- Betreten / Verlassen ----------------

        // Besitzer: durch ein offenes Portal in die Dimension (portalPosition = Spawnpunkt des Portals)
        public static void RequestDimensionEnter(Vector3 portalPosition, bool dev = false)
        {
            if (Ready) Instance.DimEnterRpc(portalPosition, dev);
            else if (DimensionEvent.Instance != null) DimensionEvent.Instance.ServerHandleEnter(Net.LocalClientId, portalPosition, dev);
        }

        // Besitzer: durch das Rückportal zurück auf die Karte
        public static void RequestDimensionExit(bool dev = false)
        {
            if (Ready) Instance.DimExitRpc(dev);
            else if (DimensionEvent.Instance != null) DimensionEvent.Instance.ServerHandleExit(Net.LocalClientId, dev);
        }

        [Rpc(SendTo.Server)]
        private void DimEnterRpc(Vector3 portalPosition, bool dev, RpcParams rpcParams = default)
        {
            var ev = DimensionEvent.Instance;
            if (ev != null) ev.ServerHandleEnter(rpcParams.Receive.SenderClientId, portalPosition, dev);
        }

        [Rpc(SendTo.Server)]
        private void DimExitRpc(bool dev, RpcParams rpcParams = default)
        {
            var ev = DimensionEvent.Instance;
            if (ev != null) ev.ServerHandleExit(rpcParams.Receive.SenderClientId, dev);
        }

        // ---------------- Verfalls-Takt (Optik/Ton auf allen Rechnern) ----------------

        public static void BroadcastDimensionDrain(float damage)
        {
            if (Ready) Instance.DimDrainRpc(damage);
            else if (DimensionEvent.Instance != null) DimensionEvent.Instance.HandleDrainTick(damage);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DimDrainRpc(float damage)
        {
            if (DimensionEvent.Instance != null) DimensionEvent.Instance.HandleDrainTick(damage);
        }
    }
}
