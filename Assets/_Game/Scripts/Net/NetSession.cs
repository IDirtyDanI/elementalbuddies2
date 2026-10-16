using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElementalBuddies
{
    public enum NetMode
    {
        None,
        Solo,    // lokaler Host ohne Mitspieler
        Online,  // Unity Relay + Sessions (Raumcode)
        Direct   // direkte IP (LAN / Tests)
    }

    // Verbindungsverwaltung (überlebt Szenenwechsel): Raum hosten/beitreten per Code, Einzelspieler,
    // direkte IP (Tests/LAN), Spielstart, Neustart, Verlassen.
    //
    // Test-Startparameter (Standalone-Build):
    //   -nethost [port]           Host über direkte IP, lädt nach -netplayers Spielern die Spielszene
    //   -netjoin <ip[:port]>      Client über direkte IP
    //   -netplayers <n>           (Host) automatischer Spielstart, sobald n Spieler verbunden sind
    //   -netname <name>           Anzeigename
    //   -netchampion <0|1|2>      Champion
    public class NetSession : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        public const ushort DefaultPort = 7777;
        private const string ManagerResource = "Net/NetworkManager";

        private static NetSession _instance;
        public static NetSession Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("NetSession");
                    _instance = go.AddComponent<NetSession>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        public static bool Exists => _instance != null;

        public NetMode Mode { get; private set; } = NetMode.None;
        public string RoomCode { get; private set; }
        public ISession Session { get; private set; }
        public bool IsBusy { get; private set; }

        // Letzter Trennungsgrund (für das Hauptmenü)
        public static string LastDisconnectReason;

        public event Action OnConnected;                 // eigener Client verbunden (Lobby beginnt)
        public event Action<string> OnDisconnected;       // Verbindung verloren / Raum geschlossen
        public event Action<string> OnStatus;            // Statusmeldungen für die UI

        // Spiel läuft bereits (Spielszene geladen) → keine Beitritte mehr
        public bool GameStarted { get; private set; }

        private bool _shuttingDown;
        private bool _handlingLoss;   // Rückweg ins Menü nach Verbindungsverlust läuft
        private bool _quitting;       // Anwendung/Play-Mode endet → kein Szenenwechsel mehr
        private string _pendingReason; // genauerer Grund (z. B. Transportfehler), bevor der Stopp gemeldet wird

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Application.quitting += HandleQuitting;
        }

        void OnDestroy()
        {
            if (_instance == this) Application.quitting -= HandleQuitting;
        }

        private void HandleQuitting() => _quitting = true;

        // Netcode fährt beim Beenden selbst herunter (im Editor schon bei „Exiting Play Mode“, also VOR
        // Application.quitting; im Build in seinem OnApplicationQuit) – das ist kein Verbindungsverlust.
        void OnApplicationQuit() => _quitting = true;

        private bool IsQuitting
        {
            get
            {
#if UNITY_EDITOR
                if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return true;
#endif
                return _quitting;
            }
        }

        // ---------------- NetworkManager ----------------

        public NetworkManager EnsureManager()
        {
            if (NetworkManager.Singleton != null) return NetworkManager.Singleton;
            var prefab = Resources.Load<GameObject>(ManagerResource);
            if (prefab == null)
            {
                Debug.LogError("[Net] NetworkManager-Prefab fehlt (Resources/" + ManagerResource + "). Menü BuddyTD/Netzwerk/Einrichten ausführen.");
                return null;
            }
            var go = Instantiate(prefab);
            go.name = "NetworkManager";
            var nm = go.GetComponent<NetworkManager>();
            // NetworkManager setzt Singleton erst in OnEnable/Awake – sicherstellen
            if (NetworkManager.Singleton == null) nm.SetSingleton();
            DontDestroyOnLoad(go);
            return nm;
        }

        private void HookManager(NetworkManager nm)
        {
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = ApproveConnection;
            nm.OnClientConnectedCallback -= HandleClientConnected;
            nm.OnClientConnectedCallback += HandleClientConnected;
            nm.OnClientDisconnectCallback -= HandleClientDisconnected;
            nm.OnClientDisconnectCallback += HandleClientDisconnected;
            nm.OnServerStopped -= HandleServerStopped;
            nm.OnServerStopped += HandleServerStopped;
            nm.OnClientStopped -= HandleClientStopped;
            nm.OnClientStopped += HandleClientStopped;
            nm.OnTransportFailure -= HandleTransportFailure;
            nm.OnTransportFailure += HandleTransportFailure;
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var nm = NetworkManager.Singleton;
            bool isHostSelf = request.ClientNetworkId == NetworkManager.ServerClientId;
            int connected = nm.ConnectedClientsIds.Count;

            response.CreatePlayerObject = true;
            response.Approved = true;

            if (!isHostSelf)
            {
                if (Mode == NetMode.Solo) { response.Approved = false; response.Reason = "Einzelspiel – keine Mitspieler."; }
                else if (GameStarted) { response.Approved = false; response.Reason = "Das Spiel läuft bereits."; }
                else if (connected >= MaxPlayers) { response.Approved = false; response.Reason = "Der Raum ist voll."; }
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (clientId == NetworkManager.Singleton.LocalClientId) OnConnected?.Invoke();
            Debug.Log("[Net] Client verbunden: " + clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            Debug.Log("[Net] Client getrennt: " + clientId);
            // Eigene Verbindung verloren (Client) → zurück ins Menü
            if (!nm.IsServer && clientId == nm.LocalClientId)
            {
                string reason = string.IsNullOrEmpty(nm.DisconnectReason) ? "Verbindung zum Host verloren." : nm.DisconnectReason;
                _ = HandleLostConnection(reason);
                return;
            }
            // Host: ein Mitspieler ist weg. Netcode entfernt dessen Spielfigur und NetPlayer selbst,
            // die Partie läuft für die übrigen Spieler weiter (Bereit-Stimmen: NetGame.Waves).
            if (nm.IsServer && nm.IsListening && !nm.ShutdownInProgress && !_shuttingDown && clientId != nm.LocalClientId && GameStarted && ToastUI.Instance != null)
                ToastUI.Show("Ein Mitspieler hat die Partie verlassen.");
        }

        // Transport kaputt (z. B. Relay-Allokation ungültig nach >10 s ohne Lebenszeichen). Netcode fährt danach
        // selbst herunter → OnClientStopped/OnServerStopped meldet den Verlust.
        private void HandleTransportFailure()
        {
            Debug.LogWarning("[Net] Transportfehler – Netcode wird beendet.");
            _pendingReason = Mode == NetMode.Online
                ? "Verbindung zum Relay-Server verloren."
                : "Netzwerkfehler – Verbindung verloren.";
        }

        // Netcode wurde beendet, ohne dass wir es angestoßen haben (Transportfehler beim Host, Sitzung vom
        // Multiplayer-Dienst beendet, …). Ohne diese Behandlung bleibt die Spielszene samt HUD stehen, während
        // Netcode alle Gegner, Spielfiguren und Buddys entfernt hat (#162).
        private void HandleServerStopped(bool wasHost) => HandleUnexpectedStop(true);
        private void HandleClientStopped(bool wasHost) => HandleUnexpectedStop(wasHost);

        private void HandleUnexpectedStop(bool asHost)
        {
            // IsBusy: Raum erstellen/beitreten läuft noch – Fehler meldet JoinOnlineAsync/HostOnlineAsync selbst
            if (_shuttingDown || _handlingLoss || IsQuitting || IsBusy) return;
            string reason = _pendingReason;
            _pendingReason = null;
            if (string.IsNullOrEmpty(reason))
            {
                var nm = NetworkManager.Singleton;
                string nmReason = nm != null ? nm.DisconnectReason : null;
                reason = asHost
                    ? "Die Netzwerkverbindung wurde unerwartet beendet."
                    : (string.IsNullOrEmpty(nmReason) || nmReason.Contains("was shutdown") ? "Verbindung zum Host verloren." : nmReason);
            }
            Debug.LogWarning("[Net] Netcode unerwartet beendet (" + (asHost ? "Host" : "Client") + "): " + reason);
            _ = HandleLostConnection(reason);
        }

        private async Task HandleLostConnection(string reason)
        {
            if (_shuttingDown || _handlingLoss || IsQuitting) return;
            _handlingLoss = true;
            try
            {
                LastDisconnectReason = reason;
                ResetLocalState();
                OnDisconnected?.Invoke(reason);
                // Sitzung im Hintergrund verlassen: ohne Internet kann LeaveAsync lange hängen – der Rückweg ins
                // Menü darf darauf nicht warten (sonst bleibt die leere Spielszene stehen).
                var s = Session;
                Session = null;
                if (s != null) _ = LeaveSessionQuietly(s);
                await ShutdownAsync();
                if (!IsQuitting && SceneManager.GetActiveScene().name != GameSession.MenuScene)
                    SceneManager.LoadScene(GameSession.MenuScene);
            }
            finally { _handlingLoss = false; }
        }

        private static async Task LeaveSessionQuietly(ISession s)
        {
            try { await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("[Net] Raum verlassen (nach Verbindungsverlust): " + e.Message); }
        }

        // Lokale Pausen-/Zeitzustände zurücksetzen, damit das Menü nicht eingefroren startet
        private static void ResetLocalState()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // ---------------- Einzelspieler ----------------

        // Lokaler Host ohne Mitspieler. Lädt die Spielszene nicht selbst (siehe StartGame).
        public bool StartSolo()
        {
            var nm = EnsureManager();
            if (nm == null) return false;
            if (nm.IsListening) return true;
            HookManager(nm);
            var utp = nm.GetComponent<UnityTransport>();
            // Port 0 = beliebiger freier Port; gelauscht wird nur lokal
            utp.SetConnectionData("127.0.0.1", 0, "127.0.0.1");
            Mode = NetMode.Solo;
            RoomCode = null;
            bool ok = nm.StartHost();
            if (!ok) { Mode = NetMode.None; Debug.LogError("[Net] Lokaler Host konnte nicht starten."); }
            return ok;
        }

        // ---------------- Direkte IP (LAN / Tests) ----------------

        public bool HostDirect(ushort port = DefaultPort)
        {
            var nm = EnsureManager();
            if (nm == null || nm.IsListening) return false;
            HookManager(nm);
            nm.GetComponent<UnityTransport>().SetConnectionData("0.0.0.0", port, "0.0.0.0");
            Mode = NetMode.Direct;
            RoomCode = LocalIPv4() + ":" + port;
            bool ok = nm.StartHost();
            if (!ok) Mode = NetMode.None;
            return ok;
        }

        public bool JoinDirect(string address, ushort port = DefaultPort)
        {
            var nm = EnsureManager();
            if (nm == null || nm.IsListening) return false;
            HookManager(nm);
            ParseAddress(address, ref port, out string ip);
            nm.GetComponent<UnityTransport>().SetConnectionData(ip, port);
            Mode = NetMode.Direct;
            RoomCode = ip + ":" + port;
            bool ok = nm.StartClient();
            if (!ok) Mode = NetMode.None;
            return ok;
        }

        // ---------------- Online (Relay + Sessions, Raumcode) ----------------

        public async Task<bool> EnsureServicesAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();
#if UNITY_EDITOR
                    // Mehrere Editor-/Build-Instanzen auf einem Rechner brauchen getrennte anonyme Konten
                    options.SetProfile("p" + Mathf.Abs((Application.dataPath + System.Diagnostics.Process.GetCurrentProcess().Id).GetHashCode() % 100000));
#else
                    options.SetProfile("p" + System.Diagnostics.Process.GetCurrentProcess().Id);
#endif
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Net] Unity Services nicht verfügbar: " + e.Message);
                OnStatus?.Invoke("Online-Dienste nicht erreichbar: " + e.Message);
                return false;
            }
        }

        // Erstellt einen Raum und liefert den Raumcode (null bei Fehler)
        public async Task<string> HostOnlineAsync()
        {
            if (IsBusy) return null;
            IsBusy = true;
            try
            {
                var nm = EnsureManager();
                if (nm == null) return null;
                if (nm.IsListening) await ShutdownAsync();
                HookManager(nm);
                OnStatus?.Invoke("Verbinde mit Unity-Diensten …");
                if (!await EnsureServicesAsync()) return null;

                OnStatus?.Invoke("Erstelle Raum …");
                var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }.WithRelayNetwork();
                var session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Session = session;
                Mode = NetMode.Online;
                RoomCode = session.Code;
                Debug.Log("[Net] Raum erstellt, Code " + RoomCode);
                return RoomCode;
            }
            catch (Exception e)
            {
                Debug.LogError("[Net] Raum erstellen fehlgeschlagen: " + e);
                OnStatus?.Invoke("Raum konnte nicht erstellt werden: " + e.Message);
                Mode = NetMode.None;
                return null;
            }
            finally { IsBusy = false; }
        }

        public async Task<bool> JoinOnlineAsync(string code)
        {
            if (IsBusy) return false;
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) return false;
            IsBusy = true;
            try
            {
                var nm = EnsureManager();
                if (nm == null) return false;
                if (nm.IsListening) await ShutdownAsync();
                HookManager(nm);
                OnStatus?.Invoke("Verbinde mit Unity-Diensten …");
                if (!await EnsureServicesAsync()) return false;

                OnStatus?.Invoke("Trete Raum " + code + " bei …");
                var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                Session = session;
                Mode = NetMode.Online;
                RoomCode = session.Code;
                Debug.Log("[Net] Raum beigetreten: " + RoomCode);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Net] Beitritt fehlgeschlagen: " + e);
                OnStatus?.Invoke("Beitritt fehlgeschlagen: " + FriendlyError(e));
                Mode = NetMode.None;
                return false;
            }
            finally { IsBusy = false; }
        }

        private static string FriendlyError(Exception e)
        {
            string m = e.Message ?? "";
            if (m.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0) return "Raum nicht gefunden.";
            if (m.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return "Der Raum ist voll.";
            return m;
        }

        // ---------------- Spielablauf ----------------

        // Host: lädt die Spielszene der gewählten Map (GameSession.MapId) für alle Verbundenen
        public void StartGame()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            GameStarted = true;
            foreach (var p in NetPlayer.All) p.ResetForNewRun();
            if (Session is IHostSession host)
            {
                try { host.IsLocked = true; _ = host.SavePropertiesAsync(); } catch (Exception e) { Debug.LogWarning("[Net] Raum sperren: " + e.Message); }
            }
            Debug.Log("[Net] Spielstart: Map " + GameSession.MapId + " → Szene " + GameSession.GameScene);
            nm.SceneManager.LoadScene(GameSession.GameScene, LoadSceneMode.Single);
        }

        // Host: Spielszene neu laden (Neustart für alle) – die aktive Szene, also dieselbe Map wie bisher
        public void RestartGame()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            foreach (var p in NetPlayer.All) p.ResetForNewRun();
            string scene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene) || scene == GameSession.MenuScene) scene = GameSession.GameScene;
            nm.SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        // Verbindung beenden und ins Hauptmenü wechseln (Host schließt damit den Raum für alle)
        public async void LeaveToMenu()
        {
            ResetLocalState();
            await ShutdownAsync();
            SceneManager.LoadScene(GameSession.MenuScene);
        }

        public async Task ShutdownAsync()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            try
            {
                var s = Session;
                Session = null;
                if (s != null)
                {
                    try { await s.LeaveAsync(); } catch (Exception e) { Debug.LogWarning("[Net] Raum verlassen: " + e.Message); }
                }
                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsListening) nm.Shutdown();
                // Shutdown ist erst im nächsten Frame abgeschlossen
                for (int i = 0; i < 30 && nm != null && nm.ShutdownInProgress; i++) await Task.Yield();
            }
            finally
            {
                Mode = NetMode.None;
                RoomCode = null;
                GameStarted = false;
                _pendingReason = null;
                _shuttingDown = false;
            }
        }

        // ---------------- Kommandozeile (automatisierte Tests) ----------------

        private static bool _argsHandled;
        public static int AutoStartPlayers { get; private set; }

        // Vom Hauptmenü beim Start aufgerufen. true = Kommandozeile hat eine Verbindung gestartet.
        public static bool HandleCommandLine()
        {
            if (_argsHandled) return false;
            _argsHandled = true;
            string[] args = Environment.GetCommandLineArgs();
            string host = null, join = null;
            bool hostFlag = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
                switch (a)
                {
                    case "-nethost": hostFlag = true; host = next; break;
                    case "-netjoin": join = next; break;
                    case "-netplayers": if (int.TryParse(next, out int n)) AutoStartPlayers = n; break;
                    case "-netname": if (next != null) NetPlayer.LocalPlayerName = next; break;
                    case "-netmap": if (next != null) GameSession.MapId = next; break; // Map für Host-Tests (z. B. forest)
                    case "-netchampion": if (int.TryParse(next, out int c)) GameSession.SelectedChampion = GameSession.ClampChampion(c); break;
                }
            }
            if (hostFlag)
            {
                ushort port = DefaultPort;
                if (host != null) ushort.TryParse(host, out port);
                bool ok = Instance.HostDirect(port);
                Debug.Log("[Net] Kommandozeile: Host auf Port " + port + " → " + ok);
                if (ok && AutoStartPlayers > 0) Instance.StartCoroutine(Instance.AutoStartWhenReady());
                return ok;
            }
            if (join != null && join.IndexOf('.') < 0 && join.IndexOf(':') < 0)
            {
                // Raumcode (Relay)
                _ = Instance.JoinOnlineAsync(join);
                Debug.Log("[Net] Kommandozeile: Beitritt zu Raum " + join);
                return true;
            }
            if (join != null)
            {
                bool ok = Instance.JoinDirect(join);
                Debug.Log("[Net] Kommandozeile: Beitritt zu " + join + " → " + ok);
                return ok;
            }
            return false;
        }

        private System.Collections.IEnumerator AutoStartWhenReady()
        {
            while (NetPlayer.All.Count < AutoStartPlayers) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            StartGame();
        }

        // ---------------- Hilfen ----------------

        private static void ParseAddress(string address, ref ushort port, out string ip)
        {
            ip = (address ?? "127.0.0.1").Trim();
            int colon = ip.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(ip.Substring(colon + 1), out ushort p))
            {
                port = p;
                ip = ip.Substring(0, colon);
            }
            if (ip.Length == 0 || ip == "localhost") ip = "127.0.0.1";
        }

        public static string LocalIPv4()
        {
            try
            {
                foreach (var addr in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(addr))
                        return addr.ToString();
            }
            catch { }
            return "127.0.0.1";
        }
    }
}
