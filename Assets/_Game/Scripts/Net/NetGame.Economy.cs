using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Bereich Wirtschaft (Paket C): Teamkasse der Seelensplitter + replizierte Splitter-Drops.
    // - Teamkasse: EconomyManager rechnet nur auf dem Server; CurrentShards/ShardGainPercent werden hier gespiegelt,
    //   auf Clients feuern OnShardsChanged (über NetApplyShards) und OnShardsEarned (per RPC).
    // - Splitter-Drops: Der Server entscheidet Spawn, Zusammenfassen, Magnet-Ziel (nächste Spielfigur) und Einsammeln;
    //   Clients bekommen Spawn/Wert/Magnet/Einsammeln mit Drop-Id und zeigen die Drops samt Magnet-Flug nur optisch.
    public partial class NetGame
    {
        private readonly NetworkVariable<float> _teamShards = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<float> _shardGainPercent = new NetworkVariable<float>(0f);

        // Aktueller Stand der Teamkasse (Server-Wert)
        public float TeamShards => _teamShards.Value;

        partial void OnSpawnEconomy()
        {
            var eco = EconomyManager.Instance;
            if (IsServer)
            {
                if (eco != null) ServerSetEconomy(eco.CurrentShards, eco.ShardGainPercent);
                return;
            }
            _teamShards.OnValueChanged += HandleTeamShardsChanged;
            _shardGainPercent.OnValueChanged += HandleShardGainChanged;
            ClientPullEconomy();
        }

        partial void OnDespawnEconomy()
        {
            _teamShards.OnValueChanged -= HandleTeamShardsChanged;
            _shardGainPercent.OnValueChanged -= HandleShardGainChanged;
        }

        // ---------------- Teamkasse ----------------

        // Server: Stand veröffentlichen (EconomyManager ruft das nach jeder Änderung auf)
        public void ServerSetEconomy(float shards, float gainPercent)
        {
            if (!IsSpawned || !IsServer) return;
            _teamShards.Value = shards;
            _shardGainPercent.Value = gainPercent;
        }

        // Client: Server-Stand in den EconomyManager übernehmen
        public void ClientPullEconomy()
        {
            if (!IsSpawned || IsServer) return;
            var eco = EconomyManager.Instance;
            if (eco == null) return;
            eco.NetApplyShardGain(_shardGainPercent.Value);
            eco.NetApplyShards(_teamShards.Value);
        }

        private void HandleTeamShardsChanged(float previous, float current)
        {
            var eco = EconomyManager.Instance;
            if (eco != null) eco.NetApplyShards(current);
        }

        private void HandleShardGainChanged(float previous, float current)
        {
            var eco = EconomyManager.Instance;
            if (eco != null) eco.NetApplyShardGain(current);
        }

        // Server: Einnahme melden -> OnShardsEarned auch auf den Clients (Erfolge zählen lokal)
        public void ServerShardsEarned(float amount)
        {
            if (IsSpawned && IsServer && amount > 0f) ShardsEarnedRpc(amount);
        }

        [Rpc(SendTo.NotServer)]
        private void ShardsEarnedRpc(float amount)
        {
            var eco = EconomyManager.Instance;
            if (eco != null) eco.NetShardsEarned(amount);
        }

        // ---------------- Splitter-Drops ----------------

        public void ServerShardSpawned(int id, Vector3 origin, Vector3 land, float value, float rotY, float bobPhase)
        {
            if (IsSpawned && IsServer) ShardSpawnRpc(id, origin, land, value, rotY, bobPhase);
        }

        public void ServerShardValue(int id, float value)
        {
            if (IsSpawned && IsServer) ShardValueRpc(id, value);
        }

        // targetObjectId = NetworkObjectId der Spielfigur, auf die der Drop zufliegt
        public void ServerShardMagnet(int id, ulong targetObjectId, bool recall)
        {
            if (IsSpawned && IsServer) ShardMagnetRpc(id, targetObjectId, recall);
        }

        public void ServerShardCollected(int id, ulong collectorClientId)
        {
            if (IsSpawned && IsServer) ShardCollectRpc(id, collectorClientId);
        }

        [Rpc(SendTo.NotServer)]
        private void ShardSpawnRpc(int id, Vector3 origin, Vector3 land, float value, float rotY, float bobPhase)
        {
            ShardPickup.ClientSpawn(id, origin, land, value, rotY, bobPhase);
        }

        [Rpc(SendTo.NotServer)]
        private void ShardValueRpc(int id, float value)
        {
            ShardPickup.ClientSetValue(id, value);
        }

        [Rpc(SendTo.NotServer)]
        private void ShardMagnetRpc(int id, ulong targetObjectId, bool recall)
        {
            Transform target = null;
            if (NetworkManager != null && NetworkManager.SpawnManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetObjectId, out var no) && no != null)
                target = no.transform;
            ShardPickup.ClientMagnet(id, target, recall);
        }

        [Rpc(SendTo.NotServer)]
        private void ShardCollectRpc(int id, ulong collectorClientId)
        {
            ShardPickup.ClientCollect(id, collectorClientId);
        }
    }
}
