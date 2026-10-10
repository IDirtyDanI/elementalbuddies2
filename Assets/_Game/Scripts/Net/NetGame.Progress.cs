using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Bereich "Progress": Upgrade-Draft pro Spieler, Händlerladen pro Spieler (Gold), Einnahme von Händlern und
    // Schreinen. Die Manager (UpgradeManager, MerchantManager, Merchant, Shrine) bleiben MonoBehaviours und rufen
    // nur die statischen Send/Request-Helfer hier auf. Ohne gespawntes NetGame (z. B. vor dem Netz-Setup oder in
    // Tests) laufen die Helfer direkt lokal – dann ist dieser Rechner Server und einziger Spieler zugleich.
    public partial class NetGame
    {
        // ======================= Upgrade-Draft =======================

        // Server → ein Client: eigene Kartenauswahl (Indizes in UpgradeManager.AllUpgrades)
        // rerolls/skipShards: verfügbare Gratis-Neuwürfe und Splitter fürs Überspringen
        public static void SendUpgradeOffer(ulong clientId, int serial, int[] cards, int rerolls, int skipShards)
        {
            if (Ready) Instance.UpgradeOfferRpc(serial, cards, rerolls, skipShards, Instance.RpcTarget.Single(clientId, RpcTargetUse.Temp));
            else if (clientId == Net.LocalClientId && UpgradeManager.Instance != null) UpgradeManager.Instance.ClientReceiveOffer(serial, cards, rerolls, skipShards);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void UpgradeOfferRpc(int serial, int[] cards, int rerolls, int skipShards, RpcParams p)
        {
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.ClientReceiveOffer(serial, cards, rerolls, skipShards);
        }

        // Client → Server: Karte gewählt (card = Index) bzw. UpgradeManager.SkipCode / RerollCode
        public static void RequestSelectUpgrade(int serial, int card)
        {
            if (Ready) Instance.SelectUpgradeRpc(serial, card);
            else if (UpgradeManager.Instance != null) UpgradeManager.Instance.ServerSelect(Net.LocalClientId, serial, card);
        }

        [Rpc(SendTo.Server)]
        private void SelectUpgradeRpc(int serial, int card, RpcParams p = default)
        {
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.ServerSelect(p.Receive.SenderClientId, serial, card);
        }

        // Server → alle: Karte des Spielers clientId deterministisch anwenden
        public static void BroadcastApplyUpgrade(ulong clientId, int card)
        {
            if (Ready) Instance.ApplyUpgradeRpc(clientId, card);
            else if (UpgradeManager.Instance != null) UpgradeManager.Instance.ApplyPicked(clientId, card);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ApplyUpgradeRpc(ulong clientId, int card)
        {
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.ApplyPicked(clientId, card);
        }

        // ======================= Pings (Plan Fesselung G1) =======================

        // Spieler → Server → alle: Markierung an pos (PingSystem.PingType), mit Spielernamen im Koop
        public static void RequestPing(Vector3 pos, byte type)
        {
            if (Ready) Instance.PingRequestRpc(pos, type);
            else if (PingSystem.Instance != null) PingSystem.Instance.ShowPing(pos, (PingSystem.PingType)type, null);
        }

        [Rpc(SendTo.Server)]
        private void PingRequestRpc(Vector3 pos, byte type, RpcParams p = default)
        {
            var np = NetPlayer.ByClientId(p.Receive.SenderClientId);
            string who = Net.IsMultiplayer && np != null ? np.DisplayName : "";
            PingRpc(pos, type, new FixedString64Bytes(who));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PingRpc(Vector3 pos, byte type, FixedString64Bytes who)
        {
            if (PingSystem.Instance != null) PingSystem.Instance.ShowPing(pos, (PingSystem.PingType)type, who.ToString());
        }

        // ======================= Morgengrauen (Plan Fesselung E5) =======================

        // Server → alle: Welle 20 überstanden
        public static void BroadcastDawn()
        {
            if (Ready) Instance.DawnRpc();
            else if (WaveManager.Instance != null) WaveManager.Instance.ApplyDawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DawnRpc()
        {
            if (WaveManager.Instance != null) WaveManager.Instance.ApplyDawn();
        }

        // Host → Server: „Weiter (Endlos)“ (Clients warten auf den Host; „Beenden“ verlässt das Spiel lokal)
        public static void RequestDawnContinue()
        {
            if (!Net.IsServer) return;
            if (WaveManager.Instance != null) WaveManager.Instance.ServerContinueAfterDawn();
            if (Ready) Instance.DawnContinuedRpc();
            else if (WaveManager.Instance != null) WaveManager.Instance.RaiseDawnContinued();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DawnContinuedRpc()
        {
            if (WaveManager.Instance != null) WaveManager.Instance.RaiseDawnContinued();
        }

        // Server → alle: Team-Ausfall bezahlt (Banner, Countdown)
        public static void BroadcastTeamWipe(int wipe, float cost, float delay)
        {
            if (Ready) Instance.TeamWipeRpc(wipe, cost, delay);
            else if (GameManager.Instance != null) GameManager.Instance.RaiseTeamWipe(wipe, cost, delay);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TeamWipeRpc(int wipe, float cost, float delay)
        {
            if (GameManager.Instance != null) GameManager.Instance.RaiseTeamWipe(wipe, cost, delay);
        }

        // ======================= Händlerladen =======================

        // Server → ein Client: kompletter Ladenzustand dieses Spielers (Öffnen, Änderung, Schließen)
        // curses: MerchantCurse je Slot (0 = kein Fluch)
        public static void SendShopState(ulong clientId, int visit, int kind, int[] offer, bool[] bought, int[] curses, int purchases, int rerolls, bool open)
        {
            if (Ready) Instance.ShopStateRpc(visit, kind, offer, bought, curses, purchases, rerolls, open, Instance.RpcTarget.Single(clientId, RpcTargetUse.Temp));
            else if (clientId == Net.LocalClientId && MerchantManager.Instance != null)
                MerchantManager.Instance.ClientReceiveShop(visit, kind, offer, bought, curses, purchases, rerolls, open);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ShopStateRpc(int visit, int kind, int[] offer, bool[] bought, int[] curses, int purchases, int rerolls, bool open, RpcParams p)
        {
            if (MerchantManager.Instance != null) MerchantManager.Instance.ClientReceiveShop(visit, kind, offer, bought, curses, purchases, rerolls, open);
        }

        public static void RequestBuyCard(int visit, int slot)
        {
            if (Ready) Instance.BuyCardRpc(visit, slot);
            else if (MerchantManager.Instance != null) MerchantManager.Instance.ServerBuy(Net.LocalClientId, visit, slot);
        }

        [Rpc(SendTo.Server)]
        private void BuyCardRpc(int visit, int slot, RpcParams p = default)
        {
            if (MerchantManager.Instance != null) MerchantManager.Instance.ServerBuy(p.Receive.SenderClientId, visit, slot);
        }

        public static void RequestReroll(int visit)
        {
            if (Ready) Instance.RerollRpc(visit);
            else if (MerchantManager.Instance != null) MerchantManager.Instance.ServerReroll(Net.LocalClientId, visit);
        }

        [Rpc(SendTo.Server)]
        private void RerollRpc(int visit, RpcParams p = default)
        {
            if (MerchantManager.Instance != null) MerchantManager.Instance.ServerReroll(p.Receive.SenderClientId, visit);
        }

        public static void RequestCloseShop(int visit)
        {
            if (Ready) Instance.CloseShopRpc(visit);
            else if (MerchantManager.Instance != null) MerchantManager.Instance.ServerCloseShop(Net.LocalClientId, visit);
        }

        [Rpc(SendTo.Server)]
        private void CloseShopRpc(int visit, RpcParams p = default)
        {
            if (MerchantManager.Instance != null) MerchantManager.Instance.ServerCloseShop(p.Receive.SenderClientId, visit);
        }

        // Server → alle: gekaufte Händlerkarte auf die Figur des Käufers anwenden
        public static void BroadcastApplyMerchantCard(ulong clientId, int card, int curse)
        {
            if (Ready) Instance.ApplyMerchantCardRpc(clientId, card, curse);
            else if (MerchantManager.Instance != null) MerchantManager.Instance.ApplyBoughtCard(clientId, card, curse);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ApplyMerchantCardRpc(ulong clientId, int card, int curse)
        {
            if (MerchantManager.Instance != null) MerchantManager.Instance.ApplyBoughtCard(clientId, card, curse);
        }

        // Server → ein Client: Kauf/Wurf abgelehnt (zu wenig Gold). slot -1 = Neu würfeln
        public static void SendPurchaseFailed(ulong clientId, int slot)
        {
            if (Ready) Instance.PurchaseFailedRpc(slot, Instance.RpcTarget.Single(clientId, RpcTargetUse.Temp));
            else if (clientId == Net.LocalClientId && MerchantManager.Instance != null) MerchantManager.Instance.ClientPurchaseFailed(slot);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void PurchaseFailedRpc(int slot, RpcParams p)
        {
            if (MerchantManager.Instance != null) MerchantManager.Instance.ClientPurchaseFailed(slot);
        }

        // Server → ein Client: kurze Meldung (z. B. "+30 Gold (Händler-Bonus)")
        public static void SendToast(ulong clientId, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (Ready) Instance.ToastRpc(new FixedString128Bytes(message), Instance.RpcTarget.Single(clientId, RpcTargetUse.Temp));
            else if (clientId == Net.LocalClientId) ToastUI.Show(message);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ToastRpc(FixedString128Bytes message, RpcParams p)
        {
            ToastUI.Show(message.ToString());
        }

        // ======================= Einnahme (Händler / Schreine) =======================

        // Server → Clients: Zustandswechsel eines Händlers (Index = MerchantKind)
        public static void SendMerchantState(int kind, int state)
        {
            if (Ready && Net.IsServer) Instance.MerchantStateRpc(kind, state);
        }

        [Rpc(SendTo.NotServer)]
        private void MerchantStateRpc(int kind, int state)
        {
            var m = Merchant.Find((MerchantKind)kind);
            if (m != null) m.NetApplyState((MerchantState)state);
        }

        // Server → Clients: Zustandswechsel eines Schreins (+ aktueller Buddy-Schadensbonus seines Elements)
        public static void SendShrineState(int element, int state, float bonusTotal)
        {
            if (Ready && Net.IsServer) Instance.ShrineStateRpc(element, state, bonusTotal);
        }

        [Rpc(SendTo.NotServer)]
        private void ShrineStateRpc(int element, int state, float bonusTotal)
        {
            ShrineBonuses.SetDamageBonus(element, bonusTotal);
            var s = Shrine.Find(element);
            if (s != null) s.NetApplyState((ShrineState)state);
        }

        // Server → Clients: Einnahme-Fortschritt für die Optik (Kreis, Objective-UI). shrine=false → Händler.
        public static void SendCaptureProgress(bool shrine, int id, float progressSeconds, bool inside, bool contested)
        {
            if (Ready && Net.IsServer && Net.IsMultiplayer) Instance.CaptureProgressRpc(shrine, id, progressSeconds, inside, contested);
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        private void CaptureProgressRpc(bool shrine, int id, float progressSeconds, bool inside, bool contested)
        {
            if (shrine)
            {
                var s = Shrine.Find(id);
                if (s != null) s.NetApplyProgress(progressSeconds, inside, contested);
            }
            else
            {
                var m = Merchant.Find((MerchantKind)id);
                if (m != null) m.NetApplyProgress(progressSeconds, inside, contested);
            }
        }
    }
}
