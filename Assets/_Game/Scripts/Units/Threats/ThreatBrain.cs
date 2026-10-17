using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Basis der Champion-Bedrohungen (Beschwörer, Bannwirker) – Komponente am Gegner-Prefab neben EnemyBrain.
    // Server: EnemyBrain ruft ServerTick jeden Frame statt der normalen Ziel-/Angriffslogik (nicht während Betäubung,
    // Frost, Rückstoß – die Champion-Kontrolle unterbricht also auch Ritual/Wirken; EnemyBrain meldet das explizit über
    // MarkInterrupted). Kein Angriff, kein Anti-Cheese, keine Nachzügler-Rettung – stattdessen eine eigene Notbremse:
    // ohne Fortschritt seit RescueTime s und vom Nexus aus nicht vollständig erreichbar → zurück an ein offenes Portal.
    // Clients: Zustand + Bezugspunkt über EnemyNet.ThreatState/ThreatPoint (Requisiten, Animator-Bools), einmalige
    // Effekte per ServerFx → EnemyNet.ThreatFxRpc → PlayFx.
    [RequireComponent(typeof(EnemyBrain))]
    public abstract class ThreatBrain : MonoBehaviour
    {
        public const byte StateNone = 0;
        public const byte StateMoving = 1;
        public const byte StateChannel = 2;  // Ritual läuft (Beschwörer) bzw. Bann wird gehalten (Bannwirker)
        public const byte StateCasting = 3;  // Bannwirker wirkt (1,5 s)

        protected EnemyBrain Brain { get; private set; }
        private EnemyNet _net;
        private byte _state;
        private Vector3 _point;
        private byte _shownState = 255;
        private Animator _animator;
        private ChampionThreatRules.InterruptFlag _interrupt;

        [Header("Notbremse (unerreichbar)")]
        [Tooltip("So lange ohne Fortschritt (< RescueMoveDistance) und vom Nexus aus nicht vollständig erreichbar → zurück an " +
                 "ein offenes Portal (s). 0 = aus.")]
        public float RescueTime = 20f;
        [Tooltip("Bewegung ab dieser Strecke gilt als Fortschritt (m).")]
        public float RescueMoveDistance = 1.5f;
        private Vector3 _rescueProgressPos;
        private float _rescueNoProgress;
        private float _nextReachCheck;
        private Vector3 _reachCheckPos;
        private bool _reachCheckValid;
        private bool _reachCached;
        private const float ReachCheckInterval = 2f;   // Erreichbarkeit höchstens so oft neu berechnen (s)
        private const float ReachCacheDistance = 0.75f; // … und nur, wenn er sich seitdem so weit bewegt hat (m)

        // Fortschritt 0..1 (Ritual-Uhr; Server direkt, Clients aus EnemyNet)
        private float _progress;
        public float Progress => Net.IsClientOnly && _net != null && _net.IsSpawned ? _net.ThreatProgress.Value / 255f : _progress;

        protected void SetProgress(float p)
        {
            _progress = Mathf.Clamp01(p);
            if (_net != null) _net.ServerSetThreatProgress(_progress);
        }

        // Zustand/Bezugspunkt (Server direkt, Clients aus EnemyNet)
        public byte State => Net.IsClientOnly && _net != null && _net.IsSpawned ? _net.ThreatState.Value : _state;
        public Vector3 Point => Net.IsClientOnly && _net != null && _net.IsSpawned ? _net.ThreatPoint.Value : _point;

        protected virtual void Awake()
        {
            Brain = GetComponent<EnemyBrain>();
            _net = GetComponent<EnemyNet>();
            var agent = GetComponent<NavMeshAgent>();
            // Zwei NavMeshes (Humanoid + Ramme): Abfragen immer für den eigenen Agententyp
            Filter = new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = NavMesh.AllAreas };
        }

        // NavMesh-Abfragen (SamplePosition/CalculatePath) für den Agententyp dieses Gegners
        protected NavMeshQueryFilter Filter { get; private set; }

        protected virtual void Start()
        {
            _rescueProgressPos = transform.position;
            _animator = GetComponentInChildren<Animator>();
            if (Net.IsServer && WaveManager.Instance != null) WaveManager.Instance.AnnounceThreat(Brain);
        }

        // Server: ein Schritt der Bedrohungs-Logik (statt Bewegung/Angriff des EnemyBrain)
        public void ServerTick()
        {
            // Seit dem letzten Schritt betäubt/eingefroren/zurückgestoßen/gesperrt (EnemyBrain → MarkInterrupted)?
            bool interrupted = _interrupt.Consume();
            if (TickRescue()) return;
            Tick(interrupted);
        }

        // Server: EnemyBrain steht in einem Blockier-Zweig (Frost, Betäubung, Zauber-Sperre, Rückstoß) – ServerTick fällt aus.
        // Der nächste ServerTick meldet „unterbrochen“; BlockedTick erlaubt Prüfungen, die auch dann laufen müssen.
        public void MarkInterrupted()
        {
            _interrupt.Mark();
            BlockedTick();
        }

        protected abstract void Tick(bool interrupted);

        // Server: jeder Frame, in dem die Bedrohung blockiert ist (Standard: nichts)
        protected virtual void BlockedTick() { }

        // Server: nach dem Zurücksetzen an ein Portal (Notbremse) – Zustand neu aufbauen
        protected virtual void OnRescued() { }

        // ---------------- Notbremse ----------------

        // true = gerade an ein Portal zurückgesetzt (dieser Schritt entfällt)
        private bool TickRescue()
        {
            if (RescueTime <= 0f) return false;
            Vector3 pos = transform.position;
            if (Horizontal(pos, _rescueProgressPos) > RescueMoveDistance)
            {
                _rescueProgressPos = pos;
                _rescueNoProgress = 0f;
                return false;
            }
            _rescueNoProgress += Time.deltaTime;
            if (_rescueNoProgress < RescueTime || Time.time < _nextReachCheck) return false;
            _nextReachCheck = Time.time + ReachCheckInterval;
            if (!ChampionThreatRules.ShouldRescueThreat(_rescueNoProgress, RescueTime, CachedReachableFromNexus(pos))) return false;
            if (!Brain.WarpToRescue("unerreichbar ohne Fortschritt")) return false;
            _rescueProgressPos = transform.position;
            _rescueNoProgress = 0f;
            _reachCheckValid = false;
            OnRescued();
            return true;
        }

        // Erreichbarkeit der eigenen Position vom Nexus aus, gecacht (neu erst nach Bewegung)
        private bool CachedReachableFromNexus(Vector3 pos)
        {
            if (_reachCheckValid && Horizontal(pos, _reachCheckPos) <= ReachCacheDistance) return _reachCached;
            _reachCheckPos = pos;
            _reachCached = ReachableFromNexus(pos);
            _reachCheckValid = true;
            return _reachCached;
        }

        private static NavMeshPath _nexusPath;

        // Ein Punkt ist vom Nexus aus vollständig erreichbar (NavMesh des eigenen Agententyps; der Champion muss hinkommen).
        // Ohne Nexus: true. Punkt nicht auf dem NavMesh: false.
        protected bool ReachableFromNexus(Vector3 point)
        {
            var nexus = Nexus.Instance;
            if (nexus == null) return true;
            if (!NavMesh.SamplePosition(point, out NavMeshHit to, 2f, Filter)) return false;
            if (!NavMesh.SamplePosition(nexus.GetClosestPoint(point), out NavMeshHit from, 5f, Filter)) return true; // Nexus ohne NavMesh: nicht prüfbar
            if (_nexusPath == null) _nexusPath = new NavMeshPath();
            return NavMesh.CalculatePath(from.position, to.position, Filter, _nexusPath) && _nexusPath.status == NavMeshPathStatus.PathComplete;
        }

        // Alle Rechner: einmaliger Effekt (Riss, Telegraph …)
        public virtual void PlayFx(byte id, Vector3 position) { }

        // Alle Rechner: Zustand hat gewechselt (Requisiten an/aus, Animator-Bools)
        protected virtual void OnStateShown(byte state, Vector3 point) { }

        // Server: Effekt lokal + an die Clients
        protected void ServerFx(byte id, Vector3 position)
        {
            PlayFx(id, position);
            if (_net != null) _net.ServerThreatFx(id, position);
        }

        // Server: Zustand setzen (Clients über EnemyNet)
        protected void SetState(byte state, Vector3 point)
        {
            _state = state;
            _point = point;
            if (_net != null) _net.ServerSetThreat(state, point);
        }

        protected virtual void Update()
        {
            byte s = State;
            if (s != _shownState)
            {
                _shownState = s;
                OnStateShown(s, Point);
            }
        }

        // ---------------- Animator (optional, nur vorhandene Parameter) ----------------

        protected void SetAnimBool(string param, bool value)
        {
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            int hash = Animator.StringToHash(param);
            foreach (var p in _animator.parameters)
                if (p.nameHash == hash && p.type == AnimatorControllerParameterType.Bool) { _animator.SetBool(hash, value); return; }
        }

        // Server: Trigger lokal und auf den Clients (EnemyNet.ServerAnimTrigger)
        protected void ServerAnimTrigger(string param)
        {
            int hash = Animator.StringToHash(param);
            Brain.PlayAnimTrigger(hash);
            if (_net != null) _net.ServerAnimTrigger(hash);
        }

        protected static float Horizontal(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
