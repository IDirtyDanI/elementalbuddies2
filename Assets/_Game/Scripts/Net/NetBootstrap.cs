using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Sitzt in der Spielszene. Startet einen lokalen Einzelspieler-Host, falls die Szene ohne Verbindung geladen
    // wurde (z. B. direkt im Editor), und spawnt als Server für jeden verbundenen Spieler eine Spielfigur.
    [DefaultExecutionOrder(-1000)]
    public class NetBootstrap : MonoBehaviour
    {
        [Tooltip("Player.prefab mit NetworkObject + PlayerAvatar")]
        public GameObject AvatarPrefab;

        [Tooltip("Spawnpunkte der Spielfiguren (Index = Beitrittsreihenfolge). Leer = eigene Position + Versatz.")]
        public Transform[] SpawnPoints;

        public float SpawnSpacing = 2f;

        public static NetBootstrap Instance { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            // Erst in Start: Netcode registriert seine Nachrichtentypen per RuntimeInitializeOnLoad (AfterSceneLoad),
            // also nach den Awake-Aufrufen der ersten Szene. Durch die Ausführungsreihenfolge läuft dieses Start vor allen anderen.
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                Debug.Log("[Net] Spielszene ohne Verbindung gestartet → lokaler Einzelspieler-Host");
                NetSession.Instance.StartSolo();
                nm = NetworkManager.Singleton;
            }
            if (nm == null || !nm.IsServer) return;
            nm.OnClientConnectedCallback += HandleClientConnected;
            if (nm.SceneManager != null) nm.SceneManager.OnLoadEventCompleted += HandleLoadCompleted;
            SpawnMissingAvatars();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnClientConnectedCallback -= HandleClientConnected;
            if (nm.SceneManager != null) nm.SceneManager.OnLoadEventCompleted -= HandleLoadCompleted;
        }

        private void HandleClientConnected(ulong clientId) => SpawnMissingAvatars();

        private void HandleLoadCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode,
            List<ulong> completed, List<ulong> timedOut) => SpawnMissingAvatars();

        // Server: jede verbundene Client-Id ohne Figur bekommt eine
        public void SpawnMissingAvatars()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || AvatarPrefab == null) return;
            var ids = new List<ulong>(nm.ConnectedClientsIds);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                ulong id = ids[i];
                if (PlayerAvatar.ByClientId(id) != null) continue;
                GetSpawnPose(i, out Vector3 pos, out Quaternion rot);
                var go = Instantiate(AvatarPrefab, pos, rot);
                go.GetComponent<NetworkObject>().SpawnWithOwnership(id, true);
            }
        }

        public void GetSpawnPose(int index, out Vector3 position, out Quaternion rotation)
        {
            if (SpawnPoints != null && SpawnPoints.Length > 0)
            {
                var t = SpawnPoints[index % SpawnPoints.Length];
                position = t.position;
                rotation = t.rotation;
                if (index >= SpawnPoints.Length) position += Vector3.right * SpawnSpacing * (index / SpawnPoints.Length);
                return;
            }
            // Spieler im Kreis um den eigenen Punkt verteilen (Index 0 = Mitte)
            Vector3 offset = Vector3.zero;
            if (index > 0)
            {
                float ang = (index - 1) * Mathf.PI * 2f / 3f;
                offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * SpawnSpacing;
            }
            position = transform.position + offset;
            rotation = transform.rotation;
        }
    }
}
