using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Netzwerk-Anker eines Gegners (sitzt neben EnemyBrain auf allen Gegner-/Boss-Prefabs, siehe Editor/NetSetupEnemies).
    // Der Server simuliert (NavMesh, EnemyBrain-KI, BossBrain) und schreibt HP, Status-Flags und Laufgeschwindigkeit
    // in NetworkVariables; die Position läuft über eine server-autoritative NetworkTransform.
    // Clients: NavMeshAgent aus, KI aus, Animation/HP-Leiste/Boss-Leiste aus den synchronisierten Werten.
    // Einmalige Ereignisse (Angriffs-Trigger, Boss-Zauber, Projektile, Tod) kommen per RPC.
    [RequireComponent(typeof(EnemyBrain))]
    public class EnemyNet : NetworkBehaviour
    {
        // Status-Bits (Status-NetworkVariable)
        public const byte StatusFrozen = 1;
        public const byte StatusStunned = 2;
        public const byte StatusSlowed = 4;
        public const byte StatusWet = 8;
        public const byte StatusCursed = 16;
        public const byte StatusBurning = 32;
        public const byte StatusCasting = 64;

        public readonly NetworkVariable<float> HP = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public readonly NetworkVariable<float> MaxHP = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public readonly NetworkVariable<byte> Status = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // Laufgeschwindigkeit für den Animator (Speed/Moving), grob quantisiert geschrieben
        public readonly NetworkVariable<float> MoveSpeed = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private EnemyBrain _brain;
        private BossBrain _boss;

        public EnemyBrain Brain => _brain;

        // Läuft dieser Gegner nur als Abbild (reiner Client)?
        public bool IsRemoteCopy => IsSpawned && !IsServer;

        public bool HasStatus(byte flag) => (Status.Value & flag) != 0;

        void Awake()
        {
            _brain = GetComponent<EnemyBrain>();
            _boss = GetComponent<BossBrain>();
        }

        // Server, vor dem Spawn: Startwerte setzen (gehen mit der Spawn-Nachricht raus)
        public void ServerPrepare()
        {
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            HP.Value = _brain.CurrentHP;
            MaxHP.Value = _brain.MaxHP;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                ServerPrepare();
                return;
            }

            // Client: keine eigene Simulation – Position kommt von der NetworkTransform
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            _brain.ApplyRemoteHealth(HP.Value, MaxHP.Value);
            HP.OnValueChanged += HandleHealthChanged;
            MaxHP.OnValueChanged += HandleHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            HP.OnValueChanged -= HandleHealthChanged;
            MaxHP.OnValueChanged -= HandleHealthChanged;
        }

        private void HandleHealthChanged(float oldValue, float newValue)
        {
            if (_brain != null) _brain.ApplyRemoteHealth(HP.Value, MaxHP.Value);
        }

        // ---------------- Server: Zustand schreiben ----------------

        public void ServerSetHealth(float current, float max)
        {
            if (!IsSpawned || !IsServer) return;
            if (!Mathf.Approximately(HP.Value, current)) HP.Value = current;
            if (!Mathf.Approximately(MaxHP.Value, max)) MaxHP.Value = max;
        }

        public void ServerSetStatus(byte flags)
        {
            if (!IsSpawned || !IsServer || Status.Value == flags) return;
            Status.Value = flags;
        }

        public void ServerSetMoveSpeed(float speed)
        {
            if (!IsSpawned || !IsServer) return;
            float old = MoveSpeed.Value;
            // nur bei spürbarer Änderung oder beim Wechsel Stehen/Laufen schreiben
            bool movingChanged = (old > 0.1f) != (speed > 0.1f);
            if (movingChanged || Mathf.Abs(old - speed) > 0.25f) MoveSpeed.Value = speed;
        }

        // ---------------- Server → Clients: Ereignisse ----------------

        private bool HasRemotes => IsSpawned && IsServer && NetGame.HasRemoteClients;

        // Tod: vor dem Despawn, damit OnEnemyDeath/OnEnemyKilled auch auf Clients feuern (zuverlässig vor der Despawn-Nachricht)
        public void ServerKilled()
        {
            if (HasRemotes) KilledRpc();
        }

        [Rpc(SendTo.NotServer)]
        private void KilledRpc()
        {
            if (_brain != null) _brain.HandleRemoteKilled();
        }

        public void ServerAttackTrigger()
        {
            if (HasRemotes) AttackTriggerRpc();
        }

        [Rpc(SendTo.NotServer)]
        private void AttackTriggerRpc()
        {
            if (_brain != null) _brain.PlayAttackTrigger();
        }

        // Beliebiger Animator-Trigger (Boss-Zauber), per Hash
        public void ServerAnimTrigger(int triggerHash)
        {
            if (HasRemotes) AnimTriggerRpc(triggerHash);
        }

        [Rpc(SendTo.NotServer)]
        private void AnimTriggerRpc(int triggerHash)
        {
            if (_brain != null) _brain.PlayAnimTrigger(triggerHash);
        }

        // Fernkampf-Projektil als kosmetische Kopie. targetId = NetworkObjectId des Ziels (0 = keins), nexus = Nexus-Ziel,
        // fallbackAim = Zielpunkt, falls das Ziel auf dem Client nicht auffindbar ist.
        public void ServerShot(Vector3 spawn, Quaternion rotation, Transform target, Vector3 aimOffset, bool nexus, Vector3 fallbackAim)
        {
            if (!HasRemotes) return;
            ulong targetId = 0;
            if (!nexus && target != null)
            {
                var no = target.GetComponentInParent<NetworkObject>();
                if (no != null && no.IsSpawned) targetId = no.NetworkObjectId;
            }
            ShotRpc(spawn, rotation, targetId, nexus, aimOffset, fallbackAim);
        }

        [Rpc(SendTo.NotServer)]
        private void ShotRpc(Vector3 spawn, Quaternion rotation, ulong targetId, bool nexus, Vector3 aimOffset, Vector3 fallbackAim)
        {
            if (_brain == null || _brain.Config == null || _brain.Config.ProjectilePrefab == null) return;
            Transform target = null;
            if (nexus && Nexus.Instance != null) target = Nexus.Instance.transform;
            else if (targetId != 0 && NetworkManager != null && NetworkManager.SpawnManager != null
                     && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var no) && no != null)
                target = no.transform;

            var go = Instantiate(_brain.Config.ProjectilePrefab, spawn, rotation);
            var proj = go.GetComponent<EnemyProjectile>();
            if (proj == null) proj = go.AddComponent<EnemyProjectile>();
            proj.InitVisual(target, aimOffset, _brain.Config.ProjectileSpeed, fallbackAim);
        }

        // Boss-Fähigkeit ausgelöst: Optik (Strahl, Bogen, Einschlag, Ton, Pfeilhagel) auf den Clients nachspielen
        public void ServerBossFx(int abilityIndex, BossFxData data)
        {
            if (HasRemotes && abilityIndex >= 0) BossFxRpc(abilityIndex, data);
        }

        [Rpc(SendTo.NotServer)]
        private void BossFxRpc(int abilityIndex, BossFxData data)
        {
            if (_boss == null) _boss = GetComponent<BossBrain>();
            if (_boss != null) _boss.PlayRemoteFx(abilityIndex, data);
        }
    }

    // Optik-Daten einer ausgelösten Boss-Fähigkeit (Server → Clients)
    public struct BossFxData : INetworkSerializable
    {
        public AoeShape Shape;
        public Vector3 Me;       // Boss-Position beim Auslösen (Slam: Landepunkt)
        public Vector3 Dir;      // Blickrichtung
        public Color Theme;
        public float ScaleY;     // lossyScale.y des Bosses (Bogen-Höhe)

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Shape);
            serializer.SerializeValue(ref Me);
            serializer.SerializeValue(ref Dir);
            serializer.SerializeValue(ref Theme);
            serializer.SerializeValue(ref ScaleY);
        }
    }
}
