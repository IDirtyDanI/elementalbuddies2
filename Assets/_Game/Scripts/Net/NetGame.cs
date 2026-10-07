using Unity.Netcode;

namespace ElementalBuddies
{
    // Netzwerk-Knoten der Spielszene (ein in der Szene platziertes NetworkObject, Objekt "NetGame").
    // Szenen-Manager (WaveManager, EconomyManager, …) bleiben MonoBehaviours und synchronisieren ihren Zustand
    // über diese Klasse. Sie ist partial: jeder Bereich ergänzt seine NetworkVariables/RPCs in einer eigenen Datei
    // NetGame.<Bereich>.cs (z. B. NetGame.Waves.cs), damit Bereiche unabhängig voneinander wachsen.
    public partial class NetGame : NetworkBehaviour
    {
        public static NetGame Instance { get; private set; }

        // true, sobald das Objekt im Netz gespawnt ist (vorher keine RPCs/NetworkVariable-Schreibzugriffe)
        public static bool Ready => Instance != null && Instance.IsSpawned;

        void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            OnSpawnHooks();
        }

        public override void OnNetworkDespawn()
        {
            OnDespawnHooks();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // Bereiche hängen sich hier ein (partial-Methoden, optional implementiert)
        partial void OnSpawnWaves();
        partial void OnSpawnEconomy();
        partial void OnSpawnBuild();
        partial void OnSpawnProgress();
        partial void OnDespawnWaves();
        partial void OnDespawnEconomy();
        partial void OnDespawnBuild();
        partial void OnDespawnProgress();

        private void OnSpawnHooks()
        {
            OnSpawnWaves();
            OnSpawnEconomy();
            OnSpawnBuild();
            OnSpawnProgress();
        }

        private void OnDespawnHooks()
        {
            OnDespawnWaves();
            OnDespawnEconomy();
            OnDespawnBuild();
            OnDespawnProgress();
        }
    }
}
