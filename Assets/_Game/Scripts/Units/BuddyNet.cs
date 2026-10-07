using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Netzwerk-Anker eines Buddys (liegt mit dem NetworkObject auf dem Prefab-Root, eingerichtet von NetSetupBuddies).
    // "Überall simulieren, Server entscheidet": Die Buddy-Logik läuft auf allen Rechnern, Zustand (Leben, Schild, Stufe,
    // Betäubung) ändert nur der Server und spiegelt ihn hier per NetworkVariable; Clients spielen die Werte in den
    // ElementalBuddy ein (HP-Leiste, BuddyEvolution, Info-Panel funktionieren unverändert).
    // Spawnen/Entfernen nur über ServerSpawn / DespawnOrDestroy (Server).
    [DisallowMultipleComponent]
    public class BuddyNet : NetworkBehaviour
    {
        // Server schreibt, alle lesen (Standard-Rechte)
        public readonly NetworkVariable<int> Level = new NetworkVariable<int>(1);
        public readonly NetworkVariable<float> HP = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<float> Shield = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<double> StunEnd = new NetworkVariable<double>(0d);   // Server-Zeit (NetworkManager.ServerTime)
        public readonly NetworkVariable<float> PaidCost = new NetworkVariable<float>(0f);    // für die Verkaufs-Anzeige
        public readonly NetworkVariable<int> Parents = new NetworkVariable<int>(0);          // Eltern-Elemente (gepackt, siehe Pack)
        public readonly NetworkVariable<ulong> BuilderClientId = new NetworkVariable<ulong>(0); // wer gebaut hat (nur Anzeige/Telemetrie)
        public readonly NetworkVariable<bool> Reborn = new NetworkVariable<bool>(false);     // durch Phönix wiedergeboren (Effekt beim Erscheinen)

        private ElementalBuddy _buddy;
        public ElementalBuddy Buddy
        {
            get
            {
                if (_buddy == null) _buddy = GetComponentInChildren<ElementalBuddy>(true);
                return _buddy;
            }
        }

        private static bool _warnedMissingNetObject;

        void Awake()
        {
            _buddy = GetComponentInChildren<ElementalBuddy>(true);
        }

        // ---------------- Server ----------------

        // Server: frisch instanzierten Buddy (Config/Stufe/Kosten bereits gesetzt) im Netz spawnen. Ohne Netz bleibt er lokal.
        public static void ServerSpawn(ElementalBuddy buddy, ulong builderClientId, bool reborn = false)
        {
            if (buddy == null || !Net.IsRunning || !Net.IsServer) return;
            var bn = buddy.NetState;
            if (bn == null || bn.GetComponent<NetworkObject>() == null)
            {
                if (!_warnedMissingNetObject)
                {
                    _warnedMissingNetObject = true;
                    Debug.LogError($"[Net] Buddy-Prefab '{buddy.name}' hat kein NetworkObject/BuddyNet – Menü \"BuddyTD/Netzwerk/Einrichten (alles)\" ausführen. Buddy bleibt nur beim Host sichtbar.");
                }
                return;
            }
            // Startwerte erst in OnNetworkSpawn schreiben (vor dem Spawn geschrieben warnt Netcode)
            bn._spawnBuilder = builderClientId;
            bn._spawnReborn = reborn;
            bn._hasSpawnInit = true;
            bn.NetworkObject.Spawn(true);
        }

        private bool _hasSpawnInit;
        private ulong _spawnBuilder;
        private bool _spawnReborn;

        private void ApplySpawnInit()
        {
            _hasSpawnInit = false;
            var buddy = Buddy;
            if (buddy == null) return;
            Level.Value = buddy.Level;
            HP.Value = buddy.MaxHP * Mathf.Clamp(buddy.StartHealthFraction, 0.01f, 1f);
            PaidCost.Value = buddy.PaidCost;
            Parents.Value = Pack(buddy);
            BuilderClientId.Value = _spawnBuilder;
            Reborn.Value = _spawnReborn;
        }

        // Server (bzw. offline): Buddy-Objekt entfernen. Verlässt sofort die Registry (Slots, Auren, Gegner-Ziele).
        public static void DespawnOrDestroy(GameObject go)
        {
            if (go == null) return;
            var no = go.GetComponentInParent<NetworkObject>();
            if (no != null && no.IsSpawned)
            {
                if (!Net.IsServer) return; // Clients entfernen nie selbst
                no.Despawn(true);
                if (go != null) go.SetActive(false);
                return;
            }
            go.SetActive(false);
            Object.Destroy(go);
        }

        // Server: Betäubung für die Clients
        public void ServerSetStun(float duration)
        {
            if (!IsSpawned || !IsServer) return;
            double end = NetworkManager.ServerTime.Time + Mathf.Max(0f, duration);
            if (end > StunEnd.Value) StunEnd.Value = end;
        }

        // Server: Tod melden (vor dem Despawn) -> Clients zeigen Tod-Optik, wählen ab, feuern OnBuddyDestroyed
        public void ServerNotifyDeath()
        {
            if (IsSpawned && IsServer) DiedRpc();
        }

        // Server: rein optisches Ereignis an die Clients (ElementalBuddy.OnNetFx), z. B. Splitter-Nova, Phönix-Glut
        public void ServerFx(int id, Vector3 position, float value)
        {
            if (IsSpawned && IsServer) FxRpc(id, position, value);
        }

        [Rpc(SendTo.NotServer)]
        private void DiedRpc()
        {
            if (Buddy != null) Buddy.NetClientDie();
        }

        [Rpc(SendTo.NotServer)]
        private void FxRpc(int id, Vector3 position, float value)
        {
            if (Buddy != null) Buddy.OnNetFx(id, position, value);
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            var b = Buddy;
            if (b == null || b.IsDead) return;
            // Gleiche Werte machen die Variablen nicht dirty -> nur Änderungen gehen raus
            Level.Value = b.Level;
            HP.Value = b.CurrentHP;
            Shield.Value = b.ShieldAmount;
            PaidCost.Value = b.PaidCost;
        }

        // ---------------- Clients ----------------

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (_hasSpawnInit) ApplySpawnInit();
                return;
            }
            var b = Buddy;
            if (b == null) return;

            Level.OnValueChanged += HandleLevel;
            HP.OnValueChanged += HandleHP;
            Shield.OnValueChanged += HandleShield;
            StunEnd.OnValueChanged += HandleStun;
            PaidCost.OnValueChanged += HandlePaid;

            // Erst-Synchronisation (vor Start des Buddys): still, ohne Level-Up-Effekte
            Unpack(Parents.Value, b);
            b.PaidCost = PaidCost.Value;
            b.NetApplyLevel(Level.Value, true);
            if (HP.Value > 0f) b.NetApplyHP(HP.Value);
            b.NetApplyShield(Shield.Value);
            ApplyStun(false);
            if (Reborn.Value) PhoenixRebirth.NotifyRebornRemote(b);
        }

        public override void OnNetworkDespawn()
        {
            Level.OnValueChanged -= HandleLevel;
            HP.OnValueChanged -= HandleHP;
            Shield.OnValueChanged -= HandleShield;
            StunEnd.OnValueChanged -= HandleStun;
            PaidCost.OnValueChanged -= HandlePaid;

            // Verkauft/verschmolzen/zerstört: Auswahl lösen
            var im = InteractionManager.Instance;
            if (im != null && Buddy != null && im.SelectedBuddy == Buddy) im.DeselectBuddy();
        }

        private void HandleLevel(int previous, int current)
        {
            if (Buddy != null) Buddy.NetApplyLevel(current, false);
        }

        private void HandleHP(float previous, float current)
        {
            if (Buddy != null) Buddy.NetApplyHP(current);
        }

        private void HandleShield(float previous, float current)
        {
            if (Buddy != null) Buddy.NetApplyShield(current);
        }

        private void HandleStun(double previous, double current) => ApplyStun(true);

        private void HandlePaid(float previous, float current)
        {
            if (Buddy != null) Buddy.PaidCost = current;
        }

        private void ApplyStun(bool showFx)
        {
            if (Buddy == null || NetworkManager == null) return;
            float remaining = (float)(StunEnd.Value - NetworkManager.ServerTime.Time);
            Buddy.NetApplyStun(remaining, showFx && remaining > 0.05f);
        }

        // ---------------- Eltern-Elemente ----------------

        // Je 4 Bit pro Elternteil (Wert + 1, 0 = nicht gesetzt)
        private static int Pack(ElementalBuddy b)
        {
            if (!(b is FusionBuddy f)) return 0;
            int a = f.ParentElementA + 1, bb = f.ParentElementB + 1, c = f is SuperBuddy s ? s.ParentElementC + 1 : 0;
            return (a & 0xF) | ((bb & 0xF) << 4) | ((c & 0xF) << 8);
        }

        private static void Unpack(int packed, ElementalBuddy b)
        {
            if (!(b is FusionBuddy f) || packed == 0) return;
            f.ParentElementA = (packed & 0xF) - 1;
            f.ParentElementB = ((packed >> 4) & 0xF) - 1;
            if (f is SuperBuddy s) s.ParentElementC = ((packed >> 8) & 0xF) - 1;
        }
    }
}
