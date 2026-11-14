using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Phase des Dimensionsrisses (NetGame.Dimension synchronisiert sie; Werte nur anhängen)
    public enum DimensionPhase : byte
    {
        None,       // kein Event
        Open,       // Boss lebt, Portale auf der Karte sind begehbar, der Verfall läuft (oder ruht, solange jemand drin ist)
        BossDead,   // Boss tot: Rückportal in der Arena offen, nach ReturnWindow holt der Server alle zurück
        Closed,     // alle zurück, die Welle läuft noch (Rest-Gegner auf der Karte)
    }

    public enum DimensionTeleport : byte
    {
        Enter,       // durch ein Portal in die Arena
        Return,      // durch das Rückportal zurück
        AutoReturn,  // vom Server zurückgeholt (Frist, Wellenende, Spielende)
    }

    // Dimensionsriss (wiki: Design/Dimensionsriss-Plan, Abschnitt B): Boss-Event ab Welle 20, danach alle 5 Wellen.
    // Die Spieler gehen durch die Gegner-Portale in eine andere Dimension (DimensionArena, lokal bei (400, 0, 0)) und
    // erschlagen dort den Boss – ohne Buddies. Solange der Boss lebt und kein lebender Spieler drin ist, verliert der
    // Nexus nach einer Schonfrist stetig Leben (tödlich). Die Welle endet erst mit dem Boss (er zählt zur Welle).
    //
    // Aufgabenteilung:
    //  - Server: Start beim Wellenstart einer Event-Welle (WaveManager.ActiveEvent), Boss spawnen und an die Arena binden,
    //    Verfall (DimensionEventRules.DrainClock, Spielzeit), Betreten/Verlassen prüfen, „wer ist drin“, Rückholen.
    //  - Alle Rechner: bauen aus Phase/Maske/Verfall (NetGame.Dimension) alles Lokale – Arena + NavMesh, begehbare
    //    Portale, Banner, Kameraschwenk, Lichtstimmung, Ziel-Panel. Wer den Wellenstart verpasst, holt es über die
    //    Variablen nach (dann ohne Banner/Film).
    //  - Besitzer: Auslöser (Portal-Kreis) → Anfrage an den Server → Teleport mit Schwarzblende (LocalTeleport).
    // „In welcher Dimension ist etwas“ entscheidet überall die Position (x > 200): Boss-Leiste, Randpfeile und Minimap
    // zeigen nur, was in der Dimension der eigenen Figur liegt (VisibleToLocal).
    // Die Komponente hängt am Cutscene-Objekt (legt sich in jeder Spielszene selbst an, stirbt mit ihr).
    // Test im Editor/Dev-Build: DimensionEvent.DevForce(), DevEnter(), DevExit(), DevKillBoss(), DebugState().
    public class DimensionEvent : MonoBehaviour
    {
        public static DimensionEvent Instance { get; private set; }

        [Header("Verfall (Server)")]
        [Tooltip("Schonfrist ab Wellenstart (s), bevor der Nexus Leben verliert.")]
        public float DrainGrace = DimensionEventRules.DrainGrace;
        [Tooltip("Neue Schonfrist (s), wenn der letzte Spieler die Dimension verlässt (Tod).")]
        public float DrainRegrace = DimensionEventRules.DrainRegrace;
        [Tooltip("Takt des Verfalls (s).")]
        public float DrainInterval = DimensionEventRules.DrainInterval;
        [Tooltip("Anteil der Nexus-Max-LP je Takt (tödlich).")]
        public float DrainFraction = DimensionEventRules.DrainFraction;

        [Header("Betreten / Rückkehr")]
        [Tooltip("Auslöse-Radius um den Spawnpunkt eines offenen Portals bzw. vor dem Rückportal (m).")]
        public float EnterRadius = DimensionEventRules.EnterRadius;
        [Tooltip("Betreten nur mit Absicht (#569): so lange (s, Spielzeit) muss die Figur ununterbrochen im Portal-Kreis stehen.")]
        public float EnterHold = DimensionEventRules.EnterHold;
        [Tooltip("Haltezeit (s) im Kreis vor dem Rückportal.")]
        public float ReturnHold = DimensionEventRules.ReturnHold;
        [Tooltip("Nach dem Boss-Tod bleibt das Rückportal so lange offen (s), dann holt der Server alle zurück.")]
        public float ReturnWindow = DimensionEventRules.ReturnWindow;
        [Tooltip("Rückkehr: so weit (m) vom Portal-Spawnpunkt Richtung Kartenmitte (nicht im Gegnerstrom).")]
        public float ReturnInward = 5f;
        [Tooltip("Dauer der Schwarzblende je Richtung (s, Echtzeit).")]
        public float FadeTime = 0.35f;

        [Header("Boss an der Arena halten (Server)")]
        public float LeashRadius = DimensionEventRules.LeashRadius;

        [Header("Meldungen")]
        [Tooltip("Mindestabstand der Meldung „Der Riss zehrt am Nexus!“ (s, Echtzeit).")]
        public float DrainToastInterval = 20f;

        // ---------------- Zustand (alle Rechner) ----------------

        public DimensionPhase Phase { get; private set; }
        public ulong InsideMask { get; private set; }
        public DimensionEventRules.DrainPhase Drain { get; private set; }
        // Sekunden bis zum nächsten Verfalls-Takt bzw. (BossDead) bis zum Rückholen
        public int Countdown { get; private set; }
        public DimensionArena Arena { get; private set; }

        public static bool IsActive => Instance != null && Instance.Phase != DimensionPhase.None;
        public static bool PortalsEnterable => Instance != null && Instance.Phase == DimensionPhase.Open;
        public static bool ReturnPortalOpen => Instance != null && Instance.Phase == DimensionPhase.BossDead && Instance.Arena != null;
        public static bool IsDraining =>
            Instance != null && Instance.Phase == DimensionPhase.Open && Instance.Drain == DimensionEventRules.DrainPhase.Draining;

        // Steht die eigene Figur in der Dimension? (Position – auch ausgefallen bleibt der Blick dort bis zur Wiederbelebung)
        public static bool LocalInArena
        {
            get
            {
                var me = PlayerAvatar.Local;
                return me != null && DimensionEventRules.InArena(me.transform.position);
            }
        }

        // Liegt der Punkt in der Dimension der eigenen Figur? (Boss-Leiste, Randpfeile, Minimap)
        public static bool VisibleToLocal(Vector3 worldPos) => DimensionEventRules.InArena(worldPos) == LocalInArena;

        public static bool IsDimensionBoss(EnemyBrain e) =>
            e != null && e.IsBoss && DimensionEventRules.InArena(e.transform.position);

        // Fortschritt des Haltens im Portal-Kreis der eigenen Figur (0 = steht nicht im Kreis, 1 = Durchgang angefragt)
        public static float HoldProgress => Instance != null ? Instance._holdProgress : 0f;
        public static bool HoldIsReturn => Instance != null && Instance._holdReturn;

        // Helle, gut lesbare Banner-Farbe aus der Riss-Farbe der Map (das tiefe Violett des Ereignisses war auf dem
        // Tablet kaum lesbar)
        public static Color BannerColor
        {
            get
            {
                Color c = Color.Lerp(RiftColor, Color.white, 0.4f);
                float max = Mathf.Max(0.01f, c.maxColorComponent);
                c = c / max; // volle Helligkeit
                c.a = 1f;
                return c;
            }
        }
        // „Der Riss schließt sich“: helles Gold (Sieg), auf beiden Arenen lesbar
        public static readonly Color ClosedBannerColor = new Color(1f, 0.9f, 0.6f);

        public static Color RiftColor => MapContext.Current != null ? MapContext.Current.DimensionColor : WaveEvents.Color(WaveEvent.Dimension);

        // Name des Bosses für Texte (auf allen Rechnern gleich)
        public static string BossName
        {
            get
            {
                var boss = Instance != null ? Instance.FindBoss() : null;
                if (boss != null) return boss.DisplayName;
                var wm = WaveManager.Instance;
                var cfg = wm != null ? wm.DimensionBossConfig : null;
                return cfg != null ? WavePreview.NameOf(cfg) : "Boss";
            }
        }

        // Der Boss der Dimension (Server: gemerkt; Clients: lebender Boss in der Arena)
        public EnemyBrain FindBoss()
        {
            if (_boss != null && !_boss.IsDead) return _boss;
            var list = EnemyBrain.ActiveBosses;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !list[i].IsDead && DimensionEventRules.InArena(list[i].transform.position)) return list[i];
            return null;
        }

        public bool IsInside(ulong clientId) => DimensionEventRules.MaskHas(InsideMask, clientId);

        // Nächstes begehbares Portal (Spawnpunkt) – für Randpfeil, Kameraschwenk, Tests
        public static SpawnPortal NearestEnterablePortal(Vector3 from)
        {
            if (!PortalsEnterable) return null;
            SpawnPortal best = null;
            float bestSqr = float.MaxValue;
            foreach (var p in SpawnPortal.All)
            {
                if (p == null || !p.IsOpen) continue;
                Vector3 d = p.SpawnTransform.position - from;
                d.y = 0f;
                if (d.sqrMagnitude < bestSqr) { bestSqr = d.sqrMagnitude; best = p; }
            }
            return best;
        }

        // ---------------- intern ----------------

        private WaveManager _waves;
        private GameManager _game;
        private EnemyBrain _boss;                                   // Server
        private DimensionEventRules.DrainClock _clock;              // Server
        private float _returnAt;                                    // Server: Spielzeit des Rückholens
        private readonly Dictionary<ulong, Vector3> _enteredAt = new Dictionary<ulong, Vector3>(); // Server: Portal je Spieler
        private float _nextLeashCheck;
        private bool _remoteInitialized;
        private float _phaseNoneSince = -1f, _strandedSince = -1f;
        private float _nextEnterRequest, _nextDrainToast;
        private DimensionEventRules.HoldTimer _hold;
        private float _holdProgress;
        private bool _holdReturn;
        private bool _lowNexusWarned;
        private bool _atmoApplied;

        // Portal-Optik
        private class PortalLook
        {
            public SpawnPortal Portal;
            public GameObject Marker;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Light> Lights = new List<Light>();
            public readonly List<Color> LightColors = new List<Color>();
            public readonly List<float> LightIntensities = new List<float>();
            public readonly List<RiftVisuals.ParticleTint> Particles = new List<RiftVisuals.ParticleTint>();
        }
        private readonly List<PortalLook> _looks = new List<PortalLook>();
        private readonly List<Object> _owned = new List<Object>();
        private MaterialPropertyBlock _mpb;
        private float _nextPortalScan;

        // Schwarzblende
        private Canvas _fadeCanvas;
        private Image _fadeImage;
        private float _fadeAlpha;
        private Coroutine _teleportRoutine;
        private bool _teleporting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            _waves = WaveManager.Instance;
            _game = GameManager.Instance;
            if (_waves != null)
            {
                _waves.OnWaveStart += HandleWaveStart;
                _waves.OnWaveEnd += HandleWaveEnd;
            }
            if (_game != null) _game.OnGameOver += HandleGameOver;
            EnemyBrain.OnEnemyKilled += HandleEnemyKilled;
            PlayerAvatar.OnDowned += HandleDowned;
            PlayerAvatar.OnAvatarDespawned += HandleAvatarDespawned;
            NetGame.PullDimension(); // Client: laufendes Event nachholen
        }

        void OnDestroy()
        {
            if (_waves != null)
            {
                _waves.OnWaveStart -= HandleWaveStart;
                _waves.OnWaveEnd -= HandleWaveEnd;
            }
            if (_game != null) _game.OnGameOver -= HandleGameOver;
            EnemyBrain.OnEnemyKilled -= HandleEnemyKilled;
            PlayerAvatar.OnDowned -= HandleDowned;
            PlayerAvatar.OnAvatarDespawned -= HandleAvatarDespawned;
            UnmarkPortals();
            foreach (var o in _owned) if (o != null) Destroy(o);
            _owned.Clear();
            if (Arena != null) Destroy(Arena.gameObject);
            if (_fadeCanvas != null) Destroy(_fadeCanvas.gameObject);
            if (Instance == this) Instance = null;
        }

        // ======================= Server =======================

        // Entscheidet dieser Rechner über das Event? Ein Client, der den Host verliert, gilt bis zum Wechsel ins
        // Hauptmenü als „offline = Server“ (Net.IsServer) – er darf das gespiegelte Event dann nicht selbst
        // weiterführen (sonst: falsches Banner „Der Riss schließt sich“ samt Ton, weil sein Boss-Verweis leer ist).
        private bool IsAuthority => Net.IsServer && !_remoteInitialized;

        private void HandleWaveStart()
        {
            if (!IsAuthority || _waves == null) return;
            if (_waves.ActiveEvent == WaveEvent.Dimension) ServerBegin();
        }

        // Server: Event starten (Wellenstart einer Event-Welle oder Dev-Taste mitten in einer Welle)
        public bool ServerBegin()
        {
            if (!IsAuthority || _waves == null || Phase != DimensionPhase.None) return false;
            if (_game != null && _game.IsGameOver) return false;
            if (!_waves.IsWaveActive)
            {
                Debug.LogWarning("DimensionEvent: Kein Start ohne laufende Welle (der Boss zählt zur Welle).");
                return false;
            }
            EnsureArena();
            _boss = _waves.SpawnDimensionBoss(Arena.BossSpawn.position, out bool fallback);
            if (_boss == null)
            {
                Debug.LogError("DimensionEvent: Kein Boss für den Dimensionsriss (MapDefinitionSO.DimensionBoss leer und kein Boss in den Zusatzgruppen) – Event entfällt.");
                DestroyArena();
                return false;
            }
            if (fallback) Debug.Log($"DimensionEvent: MapDefinitionSO.DimensionBoss ist leer – Rückfall auf {_boss.Config.name}.");
            Leash(_boss);
            _enteredAt.Clear();
            _clock = DimensionEventRules.DrainClock.Start(Time.time, DrainGrace);
            SetState(DimensionPhase.Open, 0UL, DimensionEventRules.DrainPhase.Grace, Mathf.CeilToInt(DrainGrace), false);
            ServerSync();
            return true;
        }

        // Boss an die Arena binden: Ziel ohne Spieler = Arena-Mitte, Spieler in der ganzen Arena sichtbar, keine
        // Nachzügler-Rettung (die würde ihn an ein Karten-Portal setzen)
        private void Leash(EnemyBrain boss)
        {
            boss.ForcedTarget = Arena.Anchor;
            boss.PlayerAggroRadius = LeashRadius;
            boss.ForcedTargetPlayerAggroRadius = LeashRadius;
            boss.StragglerRescueTime = 0f;
        }

        private void ServerUpdate()
        {
            if (Phase == DimensionPhase.Open)
            {
                bool bossAlive = _boss != null && !_boss.IsDead;
                if (!bossAlive)
                {
                    ServerBossDead();
                    return;
                }
                ServerCleanMask();
                int ticks = _clock.Step(Time.time, true, AnyAliveInside(), DrainInterval, DrainRegrace);
                if (ticks > 0) ServerDrainTick();
                Drain = _clock.Phase;
                Countdown = Drain == DimensionEventRules.DrainPhase.Held ? 0 : Mathf.Max(0, Mathf.CeilToInt((float)(_clock.NextTick - Time.time)));
                ServerLeashCheck();
            }
            else if (Phase == DimensionPhase.BossDead)
            {
                Countdown = Mathf.Max(0, Mathf.CeilToInt(_returnAt - Time.time));
                if (Time.time >= _returnAt || !AnyoneInArena())
                {
                    ServerReturnAll();
                    SetState(DimensionPhase.Closed, 0UL, DimensionEventRules.DrainPhase.Off, 0, false);
                }
            }
            ServerSync();
        }

        private void ServerSync()
        {
            if (NetGame.Ready) NetGame.Instance.ServerSetDimension(Phase, InsideMask, Drain, Countdown);
        }

        private bool AnyAliveInside()
        {
            foreach (var a in PlayerAvatar.All)
                if (a != null && a.IsAlive && IsInside(a.OwnerClientId)) return true;
            return false;
        }

        private static bool AnyoneInArena()
        {
            foreach (var a in PlayerAvatar.All)
                if (a != null && DimensionEventRules.InArena(a.transform.position)) return true;
            return false;
        }

        // Bits ohne Figur (Spieler gegangen) räumen
        private void ServerCleanMask()
        {
            if (InsideMask == 0UL) return;
            ulong keep = 0UL;
            foreach (var a in PlayerAvatar.All)
                if (a != null && IsInside(a.OwnerClientId)) keep = DimensionEventRules.MaskSet(keep, a.OwnerClientId, true);
            InsideMask = keep;
        }

        private void ServerDrainTick()
        {
            var nexus = Nexus.Instance;
            if (nexus == null) return;
            float damage = DimensionEventRules.DrainDamage(nexus.MaxHP, DrainFraction);
            NetGame.BroadcastDimensionDrain(damage); // vor dem Schaden: beim letzten Takt folgt sofort das Spielende
            nexus.TakeDamage(damage, DrainFallReason); // tödlich (kein Sacrifice): fällt der Nexus, ist der Run vorbei
        }

        // Sicherheitsnetz: Der Boss darf die Insel nie verlassen (Rückstoß/Sprung bleiben auf dem NavMesh; das hier fängt
        // alles Unvorhergesehene) und muss an die Arena gebunden bleiben
        private void ServerLeashCheck()
        {
            if (Time.time < _nextLeashCheck || _boss == null || Arena == null) return;
            _nextLeashCheck = Time.time + 1f;
            if (_boss.ForcedTarget != Arena.Anchor) Leash(_boss);
            Vector3 d = _boss.transform.position - Arena.Anchor.position;
            d.y = 0f;
            if (DimensionEventRules.InArena(_boss.transform.position) && d.magnitude <= DimensionEventRules.FloorRadius + 3f) return;
            Debug.LogWarning($"DimensionEvent: Boss außerhalb der Arena ({_boss.transform.position}) – zurück in die Mitte.");
            var agent = _boss.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled) agent.Warp(Arena.BossSpawn.position);
            else _boss.transform.position = Arena.BossSpawn.position;
        }

        private void HandleEnemyKilled(EnemyBrain e)
        {
            if (!IsAuthority || Phase != DimensionPhase.Open || e == null || e != _boss) return;
            ServerBossDead();
        }

        private void ServerBossDead()
        {
            if (Phase != DimensionPhase.Open) return;
            _boss = null;
            _returnAt = Time.time + ReturnWindow;
            SetState(DimensionPhase.BossDead, InsideMask, DimensionEventRules.DrainPhase.Off, Mathf.CeilToInt(ReturnWindow), false);
            ServerSync();
        }

        private void HandleWaveEnd()
        {
            if (!IsAuthority || Phase == DimensionPhase.None) return;
            ServerEnd();
        }

        private void HandleGameOver(string reason)
        {
            if (!IsAuthority || Phase == DimensionPhase.None) return;
            ServerEnd();
        }

        // Server: Event beenden (Wellenende, Morgengrauen, Spielende) – alle sofort zurückholen
        private void ServerEnd()
        {
            ServerReturnAll();
            _boss = null;
            _enteredAt.Clear();
            SetState(DimensionPhase.None, 0UL, DimensionEventRules.DrainPhase.Off, 0, false);
            ServerSync();
        }

        private void HandleDowned(PlayerAvatar a)
        {
            if (!IsAuthority || a == null || !IsInside(a.OwnerClientId)) return;
            // Tod in der Dimension: zählt nicht mehr als „drin“ (neue Schonfrist, wenn es der Letzte war); die Figur
            // steht nach der Wiederbelebung auf der Karte und kann erneut hinein
            InsideMask = DimensionEventRules.MaskSet(InsideMask, a.OwnerClientId, false);
            ServerSync();
        }

        private void HandleAvatarDespawned(PlayerAvatar a)
        {
            if (!IsAuthority || a == null) return;
            InsideMask = DimensionEventRules.MaskSet(InsideMask, a.OwnerClientId, false);
            _enteredAt.Remove(a.OwnerClientId);
        }

        private static PlayerAvatar AvatarOf(ulong clientId)
        {
            var a = PlayerAvatar.ByClientId(clientId);
            if (a == null && !Net.IsRunning) a = PlayerAvatar.Local != null ? PlayerAvatar.Local : (PlayerAvatar.All.Count > 0 ? PlayerAvatar.All[0] : null);
            return a;
        }

        private static bool DevAllowed(bool dev)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return dev;
#else
            return false;
#endif
        }

        // Server: Spieler will durch ein Portal (portalPosition = dessen Spawnpunkt)
        [Tooltip("Sekunden ohne Boss-Fähigkeit, nachdem ein Spieler die Arena betreten hat.")]
        public float EntryCastGrace = 3f;

        public void ServerHandleEnter(ulong clientId, Vector3 portalPosition, bool dev)
        {
            if (!IsAuthority || Phase != DimensionPhase.Open || Arena == null) return;
            if (_boss == null || _boss.IsDead) return;
            var avatar = AvatarOf(clientId);
            if (avatar == null || !avatar.IsAlive || IsInside(clientId)) return;
            dev = DevAllowed(dev);

            SpawnPortal portal = null;
            float best = float.MaxValue;
            foreach (var p in SpawnPortal.All)
            {
                if (p == null || !p.IsOpen) continue;
                float d = (p.SpawnTransform.position - (dev ? avatar.transform.position : portalPosition)).sqrMagnitude;
                if (d < best) { best = d; portal = p; }
            }
            if (portal == null) return;
            Vector3 spawn = portal.SpawnTransform.position;
            if (!dev)
            {
                // Besitzer-autoritative Position: großzügige Toleranz gegen Verzögerung, aber kein Sprung quer über die Karte
                Vector3 toPortal = spawn - avatar.transform.position;
                toPortal.y = 0f;
                if ((spawn - portalPosition).sqrMagnitude > 4f || toPortal.magnitude > EnterRadius + 4f) return;
            }

            int index = DimensionEventRules.MaskCount(InsideMask);
            _enteredAt[clientId] = spawn;
            InsideMask = DimensionEventRules.MaskSet(InsideMask, clientId, true);
            Vector3 pos = DimensionEventRules.EntryPosition(Arena.Entry.position, Arena.Entry.forward, index);
            Quaternion rot = Quaternion.LookRotation(FlatForward(Arena.Entry.forward));
            ServerSync();
            // Ankunfts-Schonzeit (#595): Der Boss wirkt kurz keine Fähigkeit, sonst trifft der Seelenregen den Eingang sofort
            var bossBrain = _boss.GetComponent<BossBrain>();
            if (bossBrain != null) bossBrain.DelayCasts(EntryCastGrace);
            avatar.ServerDimensionTeleport(pos, rot, DimensionTeleport.Enter);
        }

        // Server: Spieler will durch das Rückportal zurück (nur nach dem Boss-Tod; Dev jederzeit). Wer nach dem Ende des
        // Events noch in der Arena steht (Sicherheitsnetz des Besitzers), darf immer zurück.
        public void ServerHandleExit(ulong clientId, bool dev)
        {
            if (!IsAuthority) return;
            var avatar = AvatarOf(clientId);
            if (avatar == null) return;
            bool inside = IsInside(clientId) || DimensionEventRules.InArena(avatar.transform.position);
            if (!inside) return;
            if (Phase == DimensionPhase.Open && !DevAllowed(dev)) return;
            ServerReturn(avatar, DimensionTeleport.Return);
            ServerSync();
        }

        private void ServerReturnAll()
        {
            foreach (var a in new List<PlayerAvatar>(PlayerAvatar.All))
                if (a != null && (IsInside(a.OwnerClientId) || DimensionEventRules.InArena(a.transform.position)))
                    ServerReturn(a, DimensionTeleport.AutoReturn);
            InsideMask = 0UL;
        }

        // Zurück zum betretenen Portal (ein paar Meter Richtung Kartenmitte); ohne dieses zum offenen Portal, das der
        // Kartenmitte am nächsten liegt; ohne Portale zum Startplatz der Figur
        private void ServerReturn(PlayerAvatar avatar, DimensionTeleport mode)
        {
            ulong id = avatar.OwnerClientId;
            Vector3 center = Nexus.Instance != null ? Nexus.Instance.transform.position : Vector3.zero;
            int slot = 0;
            var np = avatar.Owner;
            if (np != null) slot = Mathf.Max(0, np.Slot);

            bool found = _enteredAt.TryGetValue(id, out Vector3 spawn) && PortalOpenAt(spawn);
            if (!found)
            {
                float best = float.MaxValue;
                foreach (var p in SpawnPortal.All)
                {
                    if (p == null || !p.IsOpen) continue;
                    float d = (p.SpawnTransform.position - center).sqrMagnitude;
                    if (d < best) { best = d; spawn = p.SpawnTransform.position; found = true; }
                }
            }
            Vector3 pos;
            Quaternion rot;
            if (found)
            {
                pos = DimensionEventRules.ReturnPosition(spawn, center, slot, ReturnInward);
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas)) pos = hit.position;
                rot = Quaternion.LookRotation(FlatForward(center - spawn));
            }
            else if (NetBootstrap.Instance != null) NetBootstrap.Instance.GetSpawnPose(slot, out pos, out rot);
            else { pos = center + Vector3.forward * 4f; rot = Quaternion.identity; }

            _enteredAt.Remove(id);
            InsideMask = DimensionEventRules.MaskSet(InsideMask, id, false);
            avatar.ServerDimensionTeleport(pos, rot, mode);
        }

        private static bool PortalOpenAt(Vector3 spawnPoint)
        {
            foreach (var p in SpawnPortal.All)
                if (p != null && p.IsOpen && (p.SpawnTransform.position - spawnPoint).sqrMagnitude < 1f) return true;
            return false;
        }

        private static Vector3 FlatForward(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
        }

        // ======================= Alle Rechner: Zustand =======================

        // Client: Werte vom Server (NetGame.Dimension). Der erste Stand nach dem Beitritt kommt ohne Banner/Film.
        public void ApplyRemote(DimensionPhase phase, ulong mask, DimensionEventRules.DrainPhase drain, int countdown)
        {
            if (Net.IsServer) return;
            bool silent = !_remoteInitialized;
            _remoteInitialized = true;
            SetState(phase, mask, drain, countdown, silent);
        }

        private void SetState(DimensionPhase phase, ulong mask, DimensionEventRules.DrainPhase drain, int countdown, bool silent)
        {
            var old = Phase;
            Phase = phase;
            InsideMask = mask;
            Drain = drain;
            Countdown = countdown;
            if (old != phase) OnPhaseChanged(old, phase, silent);
        }

        private void OnPhaseChanged(DimensionPhase old, DimensionPhase now, bool silent)
        {
            Debug.Log($"DimensionEvent: Phase {old} → {now}{(silent ? " (nachgeholt)" : "")}.");
            bool gameOver = _game != null && _game.IsGameOver;
            if (now == DimensionPhase.Open)
            {
                _phaseNoneSince = -1f;
                EnsureArena();
                Arena.SetReturnPortalOpen(false);
                MarkPortals();
                if (!silent) AnnounceOpen();
            }
            else if (old == DimensionPhase.Open)
            {
                UnmarkPortals();
                // Boss tot (auch wenn BossDead und Wellenende in einem Netz-Takt zusammenfallen): „Der Riss schließt sich“
                if (!silent && !gameOver)
                {
                    FeedbackDirector.ShowBanner("Der Riss schließt sich", $"{LastBossName} ist besiegt", ClosedBannerColor, 3.2f);
                    GameAudio.PlayOr(SfxId.RiftClosed);
                }
            }
            if (Arena != null) Arena.SetReturnPortalOpen(now == DimensionPhase.BossDead);
            if (now == DimensionPhase.BossDead && !silent && LocalInArena)
                ToastUI.Show("Das Rückportal ist offen – zurück auf die Karte!");
            if (now == DimensionPhase.None || now == DimensionPhase.Closed) _phaseNoneSince = Time.unscaledTime;
        }

        private string LastBossName => !string.IsNullOrEmpty(_lastBossName) ? _lastBossName : BossName;
        private string _lastBossName;

        private void AnnounceOpen()
        {
            string boss = BossName;
            _lastBossName = boss;
            string sub = $"Tretet durch das Portal – erschlagt den {boss}!";
            Codex.Discover(Codex.EventKey(WaveEvent.Dimension));
            FeedbackDirector.ShowBanner("Dimensionsriss!", sub, BannerColor, 3.6f);
            if (HintManager.Instance != null)
                HintManager.Instance.Show("dimension",
                    "Ein <b>Dimensionsriss</b>! Lauft in ein Portal, um den Boss dahinter zu stellen. Solange niemand drin ist, verliert der Nexus Leben. Eure Buddies können dort nicht helfen.",
                    () => !IsActive || LocalInArena);

            // Kameraschwenk (≈ 4 s) auf das nächste offene Portal – nur wenn Filme an sind und gerade kein Fenster/Film läuft
            bool film = false;
            var me = PlayerAvatar.Local;
            if (me != null && Cutscene.Enabled && Cutscene.CanStartNow && !Cutscene.IsPlaying)
            {
                var portal = NearestEnterablePortal(me.transform.position);
                if (portal != null)
                    film = Cutscene.PlayFocusShot(portal.transform.position, new FocusShotOptions
                    {
                        Name = "RiftOpen",
                        Title = "Der Riss ist offen",
                        Subtitle = sub,
                        FlyEnd = 1.4f,
                        HoldEnd = 3.2f,
                        ReturnDuration = 0.8f,
                        SweepDegrees = 24f,
                        LookHeight = 2f,
                        IgnoreRoot = portal.transform,
                        ArriveSfx = SfxId.RiftOpen,
                    });
            }
            if (!film) GameAudio.PlayOr(SfxId.RiftOpen);
        }

        // Alle Rechner: ein Verfalls-Takt (Ton, Puls am Nexus, gedrosselte Meldung)
        public void HandleDrainTick(float damage)
        {
            GameAudio.PlayOr(SfxId.NexusDrain);
            if (!LocalInArena)
            {
                CameraFollow.AddTrauma(0.12f);
                if (Nexus.Instance != null) StartCoroutine(DrainPulse(Nexus.Instance.transform.position));
            }
            if (Time.unscaledTime >= _nextDrainToast && !LocalInArena)
            {
                _nextDrainToast = Time.unscaledTime + DrainToastInterval;
                ToastUI.Show("Der Riss zehrt am Nexus! Tretet durch das Portal!");
            }
        }

        private IEnumerator DrainPulse(Vector3 at)
        {
            var mat = RiftVisuals.GlowMaterial(PortalGlowTemplate(), Shader.Find("Universal Render Pipeline/Lit"), RiftColor, 3f, null);
            var mesh = RiftVisuals.Annulus(0.8f, 1f, 40);
            var go = RiftVisuals.MeshObject("NexusDrainPulse", null, mesh, mat, null);
            go.transform.position = new Vector3(at.x, at.y + 0.15f, at.z);
            float t = 0f;
            while (t < 0.9f && go != null)
            {
                t += Time.unscaledDeltaTime;
                go.transform.localScale = Vector3.one * Mathf.Lerp(7f, 1.5f, t / 0.9f); // zieht sich zum Nexus zusammen
                yield return null;
            }
            if (go != null) Destroy(go);
            Destroy(mesh);
            Destroy(mat);
        }

        // ======================= Arena =======================

        private void EnsureArena()
        {
            if (Arena != null) return;
            var scene = _waves != null ? _waves.gameObject.scene : gameObject.scene;
            Arena = DimensionArena.Create(MapContext.Current, scene);
            Debug.Log($"DimensionEvent: Arena angelegt ({(Arena.IsPlaceholder ? "Platzhalter" : "Prefab")}, NavMesh {(Arena.HasNavMesh ? "ok" : "FEHLT")}).");
        }

        private void DestroyArena()
        {
            if (Arena == null) return;
            Destroy(Arena.gameObject);
            Arena = null;
        }

        // ======================= Portale: begehbar =======================

        private static Material PortalGlowTemplate()
        {
            foreach (var p in SpawnPortal.All)
            {
                if (p == null || p.ActiveVisuals == null) continue;
                foreach (var r in p.ActiveVisuals.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.sharedMaterial != null) return r.sharedMaterial;
            }
            return null;
        }

        private void MarkPortals()
        {
            foreach (var p in SpawnPortal.All)
            {
                if (p == null || !p.IsOpen) continue;
                bool known = false;
                foreach (var l in _looks) if (l.Portal == p) { known = true; break; }
                if (!known) _looks.Add(MarkPortal(p));
            }
        }

        // Tests: so viele Portale tragen gerade die Riss-Optik
        public int MarkedPortalCount => _looks.Count;

        // Portal klar anders färben (Fläche und Licht in der Riss-Farbe der Map) + Bodenring und Lichtsäule am Spawnpunkt
        private PortalLook MarkPortal(SpawnPortal p)
        {
            var look = new PortalLook { Portal = p };
            Color c = RiftColor;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            Material template = null;
            if (p.ActiveVisuals != null)
            {
                foreach (var r in p.ActiveVisuals.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (template == null) template = r.sharedMaterial;
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor("_BaseColor", c * 0.55f);
                    _mpb.SetColor("_Color", c * 0.55f);
                    // gedeckelt: Ohne Tonemapping (Mobil-Low) brannte ×2,6 zu Gelb/Weiß aus – die Riss-Farbe muss erkennbar bleiben
                    _mpb.SetColor("_EmissionColor", c * 1.3f);
                    r.SetPropertyBlock(_mpb);
                    look.Renderers.Add(r);
                }
                // Partikel (Wirbel, Funken) in die Riss-Farbe drehen: Farbton ersetzen, Helligkeit/Sättigung/Alpha bleiben.
                // Vorher die Geschwindigkeits-Kurven vereinheitlichen – sonst warnt die Engine bei den Walddorf-Portalen
                // bei jeder Skript-Änderung („Orbital Velocity curves …“, #563).
                foreach (var ps in p.ActiveVisuals.GetComponentsInChildren<ParticleSystem>(true))
                    look.Particles.Add(RiftVisuals.TintParticles(ps, c));
                foreach (var l in p.ActiveVisuals.GetComponentsInChildren<Light>(true))
                {
                    look.Lights.Add(l);
                    look.LightColors.Add(l.color);
                    look.LightIntensities.Add(l.intensity);
                    l.color = c;
                    l.intensity *= 1.6f;
                }
            }
            look.Marker = RiftVisuals.CreateMarker(p.transform, p.SpawnTransform.position, c, template, _owned, RiftVisuals.AdditiveTemplate(p));
            return look;
        }

        private void UnmarkPortals()
        {
            foreach (var look in _looks)
            {
                if (look.Marker != null) Destroy(look.Marker);
                foreach (var r in look.Renderers) if (r != null) r.SetPropertyBlock(null);
                foreach (var pt in look.Particles) pt.Restore();
                for (int i = 0; i < look.Lights.Count; i++)
                {
                    if (look.Lights[i] == null) continue;
                    look.Lights[i].color = look.LightColors[i];
                    look.Lights[i].intensity = look.LightIntensities[i];
                }
            }
            _looks.Clear();
            foreach (var o in _owned) if (o != null) Destroy(o);
            _owned.Clear();
        }

        // ======================= Laufzeit =======================

        void Update()
        {
            if (IsAuthority && Phase != DimensionPhase.None) ServerUpdate();

            // später geöffnete Portale nachziehen
            if (Phase == DimensionPhase.Open && Time.unscaledTime >= _nextPortalScan)
            {
                _nextPortalScan = Time.unscaledTime + 1f;
                MarkPortals();
            }

            UpdateAtmosphere(false);
            LocalTriggers();
            LowNexusWarning();
            CleanupArena();
        }

        // In der Arena sind die Meldungen der Karte stumm (ToastUI.ShowMap) – fällt der Nexus aber unter ein Viertel,
        // kommt eine eigene Warnung (einmal, erneut erst nach Erholung über 30 %)
        public const float LowNexusFraction = 0.25f;
        // Grund im Game-Over-Bild, wenn der Verfall den Nexus fällt (#560)
        public const string DrainFallReason = "Der Dimensionsriss hat den Nexus verzehrt";
        private void LowNexusWarning()
        {
            var nexus = Nexus.Instance;
            if (nexus == null || nexus.MaxHP <= 0f) return;
            float f = nexus.CurrentHP / nexus.MaxHP;
            if (f > LowNexusFraction + 0.05f) { _lowNexusWarned = false; return; }
            if (_lowNexusWarned || f > LowNexusFraction || f <= 0f || !LocalInArena || !IsActive) return;
            if (_game != null && _game.IsGameOver) return;
            _lowNexusWarned = true;
            ToastUI.Show($"Der Nexus fällt! Nur noch {Mathf.CeilToInt(f * 100f)} % Leben");
            GameAudio.PlayOr(SfxId.NexusAlarm);
        }

        // Lichtstimmung der Dimension nur für den, der drin steht
        private void UpdateAtmosphere(bool instant)
        {
            bool inArena = LocalInArena;
            if (inArena == _atmoApplied) return;
            _atmoApplied = inArena;
            var sp = SiegeProgression.Instance;
            var atmo = sp != null ? sp.Atmosphere : null;
            if (atmo == null) return;
            var map = MapContext.Current;
            atmo.SetDimensionOverride(map != null ? map.DimensionAtmosphere : null, inArena, instant || _fadeAlpha > 0.9f);
        }

        // Besitzer: im Portal-Kreis → EnterHold s stehen bleiben (Fortschritt am Ring und im Ziel-Panel, Verlassen bricht
        // ab) → Anfrage an den Server (er prüft und schickt den Teleport)
        private void LocalTriggers()
        {
            var me = PlayerAvatar.Local;
            if (me == null || _teleporting)
            {
                SetHold(false, 0f, false, null);
                return;
            }
            bool inArena = DimensionEventRules.InArena(me.transform.position);

            // Sicherheitsnetz: Event vorbei, aber die eigene Figur steht noch in der Arena → zurückholen lassen
            if (inArena && Phase != DimensionPhase.Open && Phase != DimensionPhase.BossDead)
            {
                SetHold(false, 0f, false, null);
                if (Time.unscaledTime < _nextEnterRequest) return;
                if (_strandedSince < 0f) _strandedSince = Time.unscaledTime;
                if (Time.unscaledTime - _strandedSince > 2f)
                {
                    _strandedSince = -1f;
                    _nextEnterRequest = Time.unscaledTime + 2f;
                    NetGame.RequestDimensionExit();
                }
                return;
            }
            _strandedSince = -1f;

            bool can = me.IsAlive && !Cutscene.IsPlaying;
            if (can && Phase == DimensionPhase.Open && !inArena && !IsInside(me.OwnerClientId))
            {
                var portal = NearestEnterablePortal(me.transform.position);
                bool inCircle = false;
                if (portal != null)
                {
                    Vector3 d = portal.SpawnTransform.position - me.transform.position;
                    d.y = 0f;
                    inCircle = d.magnitude <= EnterRadius;
                }
                if (SetHold(inCircle, EnterHold, false, inCircle ? MarkerOf(portal) : null))
                    NetGame.RequestDimensionEnter(portal.SpawnTransform.position);
            }
            else if (can && Phase == DimensionPhase.BossDead && inArena && Arena != null)
            {
                Vector3 d = Arena.ReturnTriggerPosition - me.transform.position;
                d.y = 0f;
                bool inCircle = d.magnitude <= EnterRadius;
                if (SetHold(inCircle, ReturnHold, true, inCircle ? Arena.ReturnMarker : null))
                    NetGame.RequestDimensionExit();
            }
            else SetHold(false, 0f, false, null);
        }

        private RiftMarkerPulse _holdMarker;

        private RiftMarkerPulse MarkerOf(SpawnPortal portal)
        {
            foreach (var l in _looks)
                if (l.Portal == portal && l.Marker != null) return l.Marker.GetComponent<RiftMarkerPulse>();
            return null;
        }

        // Halte-Zähler fortschreiben (Spielzeit) und am Ring anzeigen; true = Haltezeit eben erreicht → Anfrage senden.
        // Lehnt der Server ab (kein Teleport nach 1,5 s), beginnt das Halten von vorn.
        private bool SetHold(bool inCircle, float required, bool isReturn, RiftMarkerPulse marker)
        {
            if (_hold.Fired && inCircle && Time.unscaledTime >= _nextEnterRequest) _hold.Reset();
            bool fire = _hold.Step(inCircle, Time.deltaTime, required);
            if (fire) _nextEnterRequest = Time.unscaledTime + 1.5f;
            _holdProgress = inCircle ? Mathf.Max(0.001f, _hold.Progress(required)) : 0f;
            _holdReturn = inCircle && isReturn;
            if (_holdMarker != marker)
            {
                if (_holdMarker != null) _holdMarker.Hold = 0f;
                _holdMarker = marker;
            }
            if (_holdMarker != null) _holdMarker.Hold = _holdProgress;
            return fire;
        }

        // Arena (und ihr NavMesh) abbauen, sobald das Event vorbei ist und keine Figur mehr dort steht
        private void CleanupArena()
        {
            if (Arena == null || (Phase != DimensionPhase.None && Phase != DimensionPhase.Closed) || _teleporting) return;
            if (_phaseNoneSince < 0f) _phaseNoneSince = Time.unscaledTime;
            if (Time.unscaledTime - _phaseNoneSince < 1.5f) return;
            if (AnyoneInArena()) return;
            DestroyArena();
            Debug.Log("DimensionEvent: Arena abgebaut.");
        }

        // ======================= Teleport (Besitzer) =======================

        // Besitzer: Sprung mit kurzer Schwarzblende; Kamera springt mit, die Lichtstimmung wechselt im Schwarz
        public static void LocalTeleport(PlayerAvatar avatar, Vector3 position, Quaternion rotation, DimensionTeleport mode)
        {
            if (avatar == null) return;
            var ev = Instance;
            if (ev == null || !ev.isActiveAndEnabled)
            {
                avatar.TeleportLocal(position, rotation);
                CameraFollow.Snap();
                return;
            }
            if (ev._teleportRoutine != null) ev.StopCoroutine(ev._teleportRoutine);
            ev._teleportRoutine = ev.StartCoroutine(ev.TeleportRoutine(avatar, position, rotation, mode));
        }

        private IEnumerator TeleportRoutine(PlayerAvatar avatar, Vector3 position, Quaternion rotation, DimensionTeleport mode)
        {
            _teleporting = true;
            float fade = Mathf.Max(0.01f, FadeTime);
            while (_fadeAlpha < 1f)
            {
                SetFade(_fadeAlpha + Time.unscaledDeltaTime / fade);
                yield return null;
            }
            if (avatar != null)
            {
                if (mode == DimensionTeleport.Enter) EnsureArena(); // der Teleport kann vor der Phasen-Variable ankommen
                avatar.TeleportLocal(position + Vector3.up * 0.05f, rotation);
                if (avatar.Controller != null) avatar.Controller.ResetSafePosition();
                CameraFollow.Snap();
                UpdateAtmosphere(true);
                GameAudio.PlayOr(mode == DimensionTeleport.Enter ? SfxId.RiftEnter : SfxId.RiftReturn);
                if (mode == DimensionTeleport.Enter)
                {
                    var map = MapContext.Current;
                    FeedbackDirector.ShowBanner(map != null ? map.DimensionName : "Dimension", $"Erschlagt den {BossName}!", BannerColor, 2.6f);
                }
            }
            yield return null;
            yield return null; // Kamera steht, Lichtstimmung ist gesetzt
            while (_fadeAlpha > 0f)
            {
                SetFade(_fadeAlpha - Time.unscaledDeltaTime / fade);
                yield return null;
            }
            _teleporting = false;
            _teleportRoutine = null;
        }

        private void SetFade(float alpha)
        {
            _fadeAlpha = Mathf.Clamp01(alpha);
            if (_fadeCanvas == null)
            {
                if (_fadeAlpha <= 0f) return;
                var go = new GameObject("DimensionFade", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                _fadeCanvas = go.AddComponent<Canvas>();
                _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _fadeCanvas.sortingOrder = 32000;
                var img = new GameObject("Black", typeof(RectTransform));
                img.transform.SetParent(go.transform, false);
                var rt = (RectTransform)img.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                _fadeImage = img.AddComponent<Image>();
                _fadeImage.raycastTarget = false;
            }
            _fadeImage.color = new Color(0f, 0f, 0f, _fadeAlpha);
            bool on = _fadeAlpha > 0.001f;
            if (_fadeCanvas.enabled != on) _fadeCanvas.enabled = on;
        }

        // ======================= Tests / Dev =======================

        // Kurzbeschreibung des Zustands (NetAutoTest, MobileTestRemote, Tests)
        public static string DebugState()
        {
            var ev = Instance;
            if (ev == null) return "dim=–";
            var boss = ev.FindBoss();
            return $"dim={ev.Phase} drain={ev.Drain} cd={ev.Countdown} mask={ev.InsideMask} local={(LocalInArena ? "arena" : "karte")}" +
                   $" arena={(ev.Arena != null ? (ev.Arena.IsPlaceholder ? "platzhalter" : "prefab") : "–")}" +
                   (boss != null ? $" boss={boss.DisplayName} {boss.CurrentHP:0}/{boss.MaxHP:0}" : " boss=–");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Dev (nur Host): Event jetzt. Ohne laufende Welle: zur nächsten Event-Welle springen und sie starten; in einer
        // laufenden Welle: diese Welle wird zum Event (Zusammensetzung bleibt, der Boss kommt dazu).
        public static bool DevForce()
        {
            var ev = Instance;
            var wm = WaveManager.Instance;
            if (ev == null || wm == null || !Net.IsServer) return false;
            if (ev.Phase != DimensionPhase.None) return false;
            if (wm.IsWaveActive) return ev.ServerBegin();
            int next = wm.NextDimensionWave(wm.UpcomingWaveNumber);
            if (next < 1) return false;
            if (next != wm.UpcomingWaveNumber)
            {
                wm.DevSetStartWave(next);
                if (SiegeProgression.Instance != null) SiegeProgression.Instance.ApplyInstant(wm.CurrentWaveIndex);
            }
            wm.StartNextWave();
            return ev.Phase == DimensionPhase.Open;
        }

        // Dev: eigene Figur ohne Portal in die Arena (Event muss offen sein)
        public static void DevEnter()
        {
            var me = PlayerAvatar.Local;
            if (me != null) NetGame.RequestDimensionEnter(me.transform.position, true);
        }

        // Dev: eigene Figur zurück auf die Karte (auch während der Boss lebt)
        public static void DevExit() => NetGame.RequestDimensionExit(true);

        // Dev (nur Host): Boss der Dimension töten (als Champion-Treffer)
        public static bool DevKillBoss()
        {
            var ev = Instance;
            if (ev == null || !Net.IsServer) return false;
            var boss = ev.FindBoss();
            if (boss == null) return false;
            using (EnemyBrain.PlayerSource()) boss.TakeDamage(9999999f);
            return true;
        }

        // Dev (nur Host): einen Verfalls-Takt sofort auslösen (Test des tödlichen Pfads)
        public static void DevDrainTick()
        {
            if (Instance != null && Net.IsServer && Instance.Phase == DimensionPhase.Open) Instance.ServerDrainTick();
        }
#endif
    }
}
