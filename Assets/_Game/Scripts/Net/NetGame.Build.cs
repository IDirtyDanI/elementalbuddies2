using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Bereich Bauen (Paket C): Server-RPCs für Bauen, Aufwerten, Verkaufen und Fusion sowie geteilte Bauplätze.
    // Clients validieren nur lokal für die Anzeige (Ghost, Buttons) und schicken eine Anfrage; der Server prüft alles erneut
    // (Phase, Plätze, Kosten, Erreichbarkeit), zahlt aus der Teamkasse, spawnt/despawnt. Ablehnungen gehen als Hinweis-Text
    // (Toast) nur an den Anfragenden. Ohne gespawntes NetGame (offline/EditMode) laufen die Server-Pfade direkt lokal.
    public partial class NetGame
    {
        // Geteilte Bauplätze (BuddySlotManager) + wartende Phönix-Wiedergeburten (belegen einen Platz)
        private readonly NetworkVariable<int> _maxSlots = new NetworkVariable<int>(0);
        private readonly NetworkVariable<bool> _unlimitedSlots = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<int> _pendingRebirths = new NetworkVariable<int>(0);

        public int NetMaxSlots => _maxSlots.Value;
        public bool NetUnlimitedSlots => _unlimitedSlots.Value;

        partial void OnSpawnBuild()
        {
            if (IsServer)
            {
                var slots = BuddySlotManager.Instance;
                if (slots != null) ServerSetSlots(slots.MaxSlots, slots.Unlimited);
                PhoenixRebirth.OnPendingChanged += HandlePendingChanged;
                HandlePendingChanged();
                return;
            }
            _maxSlots.OnValueChanged += HandleSlotsChanged;
            _unlimitedSlots.OnValueChanged += HandleUnlimitedChanged;
            _pendingRebirths.OnValueChanged += HandlePendingRebirthsChanged;
            ClientPullSlots();
        }

        partial void OnDespawnBuild()
        {
            PhoenixRebirth.OnPendingChanged -= HandlePendingChanged;
            _maxSlots.OnValueChanged -= HandleSlotsChanged;
            _unlimitedSlots.OnValueChanged -= HandleUnlimitedChanged;
            _pendingRebirths.OnValueChanged -= HandlePendingRebirthsChanged;
        }

        // ---------------- Bauplätze ----------------

        // Server: BuddySlotManager meldet jede Änderung (AddSlot, Dev-Modus)
        public void ServerSetSlots(int maxSlots, bool unlimited)
        {
            if (!IsSpawned || !IsServer) return;
            _maxSlots.Value = maxSlots;
            _unlimitedSlots.Value = unlimited;
        }

        // Client: Server-Stand übernehmen
        public void ClientPullSlots()
        {
            if (!IsSpawned || IsServer) return;
            PhoenixRebirth.RemotePendingCount = _pendingRebirths.Value;
            var slots = BuddySlotManager.Instance;
            if (slots != null) slots.NetApply(_maxSlots.Value, _unlimitedSlots.Value);
        }

        private void HandlePendingChanged()
        {
            if (IsSpawned && IsServer) _pendingRebirths.Value = PhoenixRebirth.PendingCount;
        }

        private void HandleSlotsChanged(int previous, int current) => ClientPullSlots();
        private void HandleUnlimitedChanged(bool previous, bool current) => ClientPullSlots();
        private void HandlePendingRebirthsChanged(int previous, int current) => ClientPullSlots();

        // ---------------- Anfragen (lokaler Spieler -> Server) ----------------

        // Buddy aus der Bauleiste (Index in InteractionManager.UnitConfigs) an Rasterposition bauen
        public static void RequestBuild(int configIndex, Vector3 position)
        {
            if (Ready) Instance.BuildRpc(configIndex, position);
            else Respond(Net.LocalClientId, InteractionManager.Instance != null
                ? InteractionManager.Instance.ServerBuild(configIndex, position, Net.LocalClientId) : null);
        }

        public static void RequestUpgrade(ElementalBuddy buddy)
        {
            if (buddy == null) return;
            if (Ready && buddy.IsNetSpawned) Instance.UpgradeRpc(buddy.NetId);
            else Respond(Net.LocalClientId, ServerUpgrade(buddy));
        }

        public static void RequestSell(ElementalBuddy buddy)
        {
            if (buddy == null) return;
            if (Ready && buddy.IsNetSpawned) Instance.SellRpc(buddy.NetId);
            else Respond(Net.LocalClientId, InteractionManager.Instance != null ? InteractionManager.Instance.ServerSell(buddy) : null);
        }

        // 2er-Fusion (c == null) oder Tri-Fusion; Ergebnis wird beim Anfragenden ausgewählt
        public static void RequestFuse(ElementalBuddy a, ElementalBuddy b, ElementalBuddy c = null)
        {
            if (a == null || b == null) return;
            if (Ready && a.IsNetSpawned && b.IsNetSpawned && (c == null || c.IsNetSpawned))
                Instance.FuseRpc(a.NetId, b.NetId, c != null ? c.NetId : ulong.MaxValue);
            else Respond(Net.LocalClientId, FusionManager.Instance != null
                ? FusionManager.Instance.ServerFuse(a, b, c, Net.LocalClientId) : null);
        }

        // ---------------- Server-RPCs ----------------

        [Rpc(SendTo.Server)]
        private void BuildRpc(int configIndex, Vector3 position, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var im = InteractionManager.Instance;
            Respond(sender, im != null ? im.ServerBuild(configIndex, position, sender) : "Bauen nicht möglich.");
        }

        [Rpc(SendTo.Server)]
        private void UpgradeRpc(ulong buddyId, RpcParams rpcParams = default)
        {
            Respond(rpcParams.Receive.SenderClientId, ServerUpgrade(FindBuddy(buddyId)));
        }

        [Rpc(SendTo.Server)]
        private void SellRpc(ulong buddyId, RpcParams rpcParams = default)
        {
            var im = InteractionManager.Instance;
            Respond(rpcParams.Receive.SenderClientId, im != null ? im.ServerSell(FindBuddy(buddyId)) : null);
        }

        [Rpc(SendTo.Server)]
        private void FuseRpc(ulong a, ulong b, ulong c, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var fm = FusionManager.Instance;
            if (fm == null) return;
            var bc = c != ulong.MaxValue ? FindBuddy(c) : null;
            if (c != ulong.MaxValue && bc == null)
            {
                Respond(sender, "Fusion nicht mehr möglich.");
                return;
            }
            Respond(sender, fm.ServerFuse(FindBuddy(a), FindBuddy(b), bc, sender));
        }

        // Server: Aufwerten prüfen und ausführen; null = Erfolg, sonst Hinweis-Text
        public static string ServerUpgrade(ElementalBuddy buddy)
        {
            if (!Net.IsServer) return null;
            if (buddy == null || buddy.IsDead) return "Buddy existiert nicht mehr.";
            if (!ElementalBuddy.IsUpgradePhase) return "Aufwerten nur zwischen den Wellen.";
            if (buddy.Level >= buddy.AbsoluteMaxLevel) return $"{buddy.StageName} ist bereits auf der Höchststufe.";
            float cost = buddy.GetUpgradeCost(buddy.Level + 1);
            var eco = EconomyManager.Instance;
            if (eco != null && !eco.CanAfford(cost)) return "Nicht genug Seelensplitter.";
            return buddy.TryUpgrade(ignoreLocks: true) ? null : "Aufwerten nicht möglich.";
        }

        // ---------------- Antworten (Server -> Clients) ----------------

        // Ablehnung (Hinweis-Text) nur an den Anfragenden; null/leer = Erfolg (nichts zu melden)
        private static void Respond(ulong clientId, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (!Ready || clientId == Net.LocalClientId)
            {
                ToastUI.Show(message);
                return;
            }
            Instance.RejectRpc(message, Instance.RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void RejectRpc(string message, RpcParams rpcParams)
        {
            ToastUI.Show(message);
        }

        // Server: Fusion abgeschlossen -> Optik/Ton + OnFused auf allen Clients, Auswahl nur beim Anfragenden
        public void ServerFusionDone(ElementalBuddy result, Vector3 position, ulong requester)
        {
            if (IsSpawned && IsServer && result != null && result.IsNetSpawned) FusionDoneRpc(result.NetId, position, requester);
        }

        [Rpc(SendTo.NotServer)]
        private void FusionDoneRpc(ulong resultId, Vector3 position, ulong requester)
        {
            var fm = FusionManager.Instance;
            if (fm != null) fm.ClientFusionDone(FindBuddy(resultId) as FusionBuddy, position, requester == Net.LocalClientId);
        }

        // ---------------- Hilfen ----------------

        // Gespawnter Buddy zu einer NetworkObjectId (null, wenn nicht vorhanden)
        public static ElementalBuddy FindBuddy(ulong networkObjectId)
        {
            var nm = Net.Manager;
            if (nm == null || nm.SpawnManager == null) return null;
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var no) || no == null) return null;
            return no.GetComponentInChildren<ElementalBuddy>();
        }
    }
}
