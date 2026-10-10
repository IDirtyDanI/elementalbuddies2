using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Seelensplitter-Drop eines getöteten Gegners. Springt kurz heraus, schwebt dann auf der Stelle und fliegt zur
    // nächsten Spielfigur, sobald eine im Magnet-Radius steht. Nur Spielfiguren sammeln ein (Gutschrift in die Teamkasse);
    // Drops verschwinden nie (auch nicht in der Bauphase). Liegt beim Spawnen schon ein Drop in MergeRadius, wird der Wert
    // dort aufaddiert.
    // Mehrspieler: Der Server entscheidet Spawn, Zusammenfassen, Magnet-Ziel und Einsammeln und meldet alles mit Drop-Id
    // über NetGame.Economy. Clients halten reine Abbilder (_replica): gleiche Optik samt Magnet-Flug, aber keine Gutschrift.
    // Optik: GlobalSettings.ShardPickupSmall/Medium/Large (reine Visuals, Collider werden abgeschaltet), sonst Primitive.
    public class ShardPickup : MonoBehaviour
    {
        public const float SmallBelow = 10f;   // Wert < 10 → klein
        public const float MediumBelow = 40f;  // Wert < 40 → mittel, sonst groß
        public const float MergeRadius = 0.6f;
        private const float HoverHeight = 0.45f;
        private const float PopDuration = 0.4f;
        private const float RecallSpeedFactor = 2.2f; // Rückruf am Wellenende: schneller als der normale Magnet

        private static readonly List<ShardPickup> _all = new List<ShardPickup>();
        public static IReadOnlyList<ShardPickup> All => _all;
        private static readonly Dictionary<int, ShardPickup> _byId = new Dictionary<int, ShardPickup>();
        private static int _nextId;

        private static Transform _player;
        private static float _nextPlayerSearch;
        private static Material _fallbackMat;

        public float Value { get; private set; }
        public bool IsMagnetized => _magnet;
        // Drop-Id (vom Server vergeben, gleich auf allen Rechnern)
        public int Id { get; private set; }

        private bool _replica;          // Client-Abbild: nur Optik
        private Transform _target;      // Magnet-Ziel (Spielfigur)
        private PlayerAvatar _targetAvatar;

        private GlobalSettingsSO _settings;
        private Vector3 _from, _land, _origin;
        private float _popT;
        private bool _magnet;
        private bool _recall;
        private float _speed;
        private float _bobPhase;
        private int _tier = -1;
        private GameObject _visual;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            _byId.Clear();
            _nextId = 0;
            _player = null;
            _nextPlayerSearch = 0f;
        }

        // Nur Server: Alle liegenden Drops fliegen zur jeweils nächsten lebenden Spielfigur (z. B. am Wellenende)
        public static void RecallAll()
        {
            if (!Net.IsServer) return;
            foreach (var p in _all)
            {
                if (p == null || p._replica) continue;
                p._magnet = true;
                p._recall = true;
            }
        }

        // Nur Server: Drop am Todesort erzeugen (bzw. in einen nahen Drop einrechnen); Clients bekommen ihn per RPC
        public static ShardPickup Spawn(Vector3 position, float value, GlobalSettingsSO settings)
        {
            if (value <= 0f || !Net.IsServer) return null;
            Vector3 ground = GroundPoint(position);

            foreach (var p in _all)
            {
                if (p == null || p._magnet) continue;
                Vector3 d = p._land - ground;
                Vector3 o = p._origin - ground;
                d.y = 0f;
                o.y = 0f;
                if (d.sqrMagnitude <= MergeRadius * MergeRadius || o.sqrMagnitude <= MergeRadius * MergeRadius)
                {
                    p.AddValue(value);
                    return p;
                }
            }

            // Kleiner Sprung in zufällige Richtung (0,5–1 m) – Zufall nur auf dem Server, Ergebnis geht an die Clients
            Vector2 dir = Random.insideUnitCircle.normalized;
            if (dir == Vector2.zero) dir = Vector2.right;
            Vector3 offset = new Vector3(dir.x, 0f, dir.y) * Random.Range(0.5f, 1f);
            Vector3 land = GroundPoint(ground + offset);
            float rotY = Random.Range(0f, 360f);
            float bob = Random.Range(0f, Mathf.PI * 2f);

            var pickup = Create(++_nextId, ground, land, value, rotY, bob, settings, false);
            if (NetGame.Ready) NetGame.Instance.ServerShardSpawned(pickup.Id, ground, land, value, rotY, bob);
            return pickup;
        }

        private static ShardPickup Create(int id, Vector3 ground, Vector3 land, float value, float rotY, float bob,
            GlobalSettingsSO settings, bool replica)
        {
            var go = new GameObject("ShardPickup");
            var pickup = go.AddComponent<ShardPickup>();
            pickup.Id = id;
            pickup._replica = replica;
            pickup.Init(ground, land, value, rotY, bob, settings);
            _byId[id] = pickup;
            return pickup;
        }

        // ---------------- Clients (Abbilder, aufgerufen von NetGame.Economy) ----------------

        private static GlobalSettingsSO ClientSettings => EconomyManager.Instance != null ? EconomyManager.Instance.Settings : null;

        public static void ClientSpawn(int id, Vector3 ground, Vector3 land, float value, float rotY, float bob)
        {
            if (_byId.TryGetValue(id, out var existing) && existing != null) return;
            Create(id, ground, land, value, rotY, bob, ClientSettings, true);
        }

        public static void ClientSetValue(int id, float value)
        {
            if (!_byId.TryGetValue(id, out var p) || p == null) return;
            p.Value = value;
            p.RefreshVisual();
        }

        public static void ClientMagnet(int id, Transform target, bool recall)
        {
            if (!_byId.TryGetValue(id, out var p) || p == null) return;
            p._magnet = true;
            p._recall |= recall;
            p._target = target;
        }

        public static void ClientCollect(int id, ulong collectorClientId)
        {
            if (!_byId.TryGetValue(id, out var p) || p == null) return;
            p.PlayCollectFx();
            Destroy(p.gameObject);
        }

        // Boden unter dem Todesort: NavMesh, sonst Raycast nach unten (Boden-Layer des InteractionManagers)
        private static Vector3 GroundPoint(Vector3 pos)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit nav, 3f, NavMesh.AllAreas)) return nav.position;
            int mask = InteractionManager.Instance != null && InteractionManager.Instance.FloorLayer.value != 0
                ? InteractionManager.Instance.FloorLayer.value : Physics.DefaultRaycastLayers;
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 30f, mask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return pos;
        }

        private void Init(Vector3 ground, Vector3 land, float value, float rotY, float bob, GlobalSettingsSO settings)
        {
            _settings = settings;
            Value = value;
            _origin = ground;
            _from = ground + Vector3.up * HoverHeight;
            _land = land;
            transform.position = _from;
            transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            _bobPhase = bob;
            RefreshVisual();
            _all.Add(this);
        }

        // Nur Server (Zusammenfassen); Clients bekommen den neuen Wert per RPC
        public void AddValue(float value)
        {
            if (value <= 0f || _replica) return;
            Value += value;
            RefreshVisual();
            if (NetGame.Ready) NetGame.Instance.ServerShardValue(Id, Value);
        }

        public static int TierOf(float value) => value < SmallBelow ? 0 : value < MediumBelow ? 1 : 2;

        private void RefreshVisual()
        {
            int tier = TierOf(Value);
            if (tier == _tier && _visual != null) return;
            _tier = tier;
            if (_visual != null) Destroy(_visual);

            GameObject prefab = null;
            if (_settings != null)
                prefab = tier == 0 ? _settings.ShardPickupSmall : tier == 1 ? _settings.ShardPickupMedium : _settings.ShardPickupLarge;

            if (prefab != null)
            {
                _visual = Instantiate(prefab, transform);
                _visual.transform.localPosition = Vector3.zero;
                foreach (var c in _visual.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
            else
            {
                _visual = CreateFallbackVisual(tier);
                _visual.transform.SetParent(transform, false);
            }
        }

        // Platzhalter: kleiner leuchtender Kristall (gekippter Würfel)
        private static GameObject CreateFallbackVisual(int tier)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ShardVisual";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            float s = tier == 0 ? 0.22f : tier == 1 ? 0.34f : 0.5f;
            go.transform.localScale = new Vector3(s * 0.7f, s, s * 0.7f);
            go.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (_fallbackMat == null)
                {
                    _fallbackMat = new Material(r.sharedMaterial) { name = "ShardFallback" };
                    Color c = new Color(0.55f, 0.85f, 1f);
                    _fallbackMat.color = c;
                    if (_fallbackMat.HasProperty("_BaseColor")) _fallbackMat.SetColor("_BaseColor", c);
                    if (_fallbackMat.HasProperty("_EmissionColor"))
                    {
                        _fallbackMat.EnableKeyword("_EMISSION");
                        _fallbackMat.SetColor("_EmissionColor", c * 1.6f);
                    }
                }
                r.sharedMaterial = _fallbackMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }

        private static Transform Player
        {
            get
            {
                if (_player == null && Time.unscaledTime >= _nextPlayerSearch)
                {
                    _nextPlayerSearch = Time.unscaledTime + 1f;
                    var go = GameObject.FindGameObjectWithTag("Player");
                    _player = go != null ? go.transform : null;
                }
                return _player;
            }
        }

        // Pause/Zeitstopp sperren nur noch im Solo (im Koop ist die Pause ein lokales Overlay)
        private static bool Blocked =>
            (Net.CanPauseTime && (PauseManager.IsPaused || Time.timeScale <= 0f)) ||
            (GameManager.Instance != null && GameManager.Instance.IsGameOver);

        // Server: nächstes Ziel (lebende Spielfigur) innerhalb maxDist (XZ); ohne Netz-Figuren Fallback auf den Tag "Player"
        private static Transform FindTarget(Vector3 from, float maxDist, out PlayerAvatar avatar)
        {
            avatar = null;
            if (PlayerAvatar.All.Count > 0)
            {
                avatar = PlayerAvatar.Nearest(from);
                if (avatar == null) return null;
                Vector3 d = avatar.transform.position - from;
                d.y = 0f;
                if (d.sqrMagnitude > maxDist * maxDist) { avatar = null; return null; }
                return avatar.transform;
            }
            Transform player = Player;
            if (player == null) return null;
            Vector3 dp = player.position - from;
            dp.y = 0f;
            return dp.sqrMagnitude <= maxDist * maxDist ? player : null;
        }

        private bool TargetValid => _target != null && (_targetAvatar == null || _targetAvatar.IsAlive);

        private void SetTarget(Transform target, PlayerAvatar avatar)
        {
            _target = target;
            _targetAvatar = avatar;
            if (avatar != null && NetGame.Ready) NetGame.Instance.ServerShardMagnet(Id, avatar.NetworkObjectId, _recall);
        }

        void Update()
        {
            if (Blocked) return;
            float dt = Time.deltaTime;

            // Herausspringen (Bogen)
            if (_popT < 1f && !_magnet)
            {
                _popT = Mathf.Min(1f, _popT + dt / PopDuration);
                Vector3 target = _land + Vector3.up * HoverHeight;
                Vector3 p = Vector3.Lerp(_from, target, _popT);
                p.y += Mathf.Sin(_popT * Mathf.PI) * 0.7f;
                transform.position = p;
                transform.Rotate(0f, 540f * dt, 0f, Space.World);
                return;
            }

            // Server: Magnet-Ziel wählen (nächste Spielfigur im Radius; Rückruf/verlorenes Ziel: nächste lebende überhaupt)
            if (!_replica)
            {
                if (!_magnet)
                {
                    float magnet = _settings != null ? _settings.ShardMagnetRadius : 3f;
                    if (WaveManager.Instance != null) magnet *= WaveManager.Instance.ShardMagnetFactor; // Seelensturm
                    var t = FindTarget(_land, magnet, out var avatar);
                    if (t != null)
                    {
                        _magnet = true;
                        SetTarget(t, avatar);
                    }
                }
                else if (!TargetValid)
                {
                    var t = FindTarget(transform.position, float.MaxValue, out var avatar);
                    if (t != null) SetTarget(t, avatar);
                    else _target = null;
                }
            }

            if (_magnet && _target != null)
            {
                Vector3 chest = _target.position + Vector3.up * 0.8f;
                float maxSpeed = _settings != null ? _settings.ShardMagnetSpeed : 14f;
                if (_recall) maxSpeed *= RecallSpeedFactor;
                _speed = Mathf.MoveTowards(_speed, maxSpeed, maxSpeed * 2.5f * dt);
                transform.position = Vector3.MoveTowards(transform.position, chest, _speed * dt);
                transform.Rotate(0f, 720f * dt, 0f, Space.World);
                float collect = _settings != null ? _settings.ShardCollectRadius : 0.5f;
                // Abbilder sammeln nie selbst ein (warten am Ziel auf die Meldung des Servers)
                if (!_replica && (transform.position - chest).sqrMagnitude <= collect * collect) Collect();
                return;
            }
            if (_magnet) return; // Ziel verloren (z. B. alle Figuren tot): in der Luft warten

            // Schweben + Drehen
            _bobPhase += dt * 2.6f;
            transform.position = _land + Vector3.up * (HoverHeight + Mathf.Sin(_bobPhase) * 0.12f);
            transform.Rotate(0f, 90f * dt, 0f, Space.World);
        }

        // Nur Server: Gutschrift in die Teamkasse, Meldung an die Clients
        private void Collect()
        {
            if (Value <= 0f || _replica) return;
            float v = Value;
            Value = 0f;
            if (EconomyManager.Instance != null) EconomyManager.Instance.EarnShards(v);
            ulong collector = _targetAvatar != null ? _targetAvatar.OwnerClientId : Net.LocalClientId;
            if (NetGame.Ready) NetGame.Instance.ServerShardCollected(Id, collector);
            PlayCollectFx();
            Destroy(gameObject);
        }

        private void PlayCollectFx()
        {
            GameAudio.PlayChain(SfxId.ShardPickup, transform.position); // Kette: steigende Tonleiter
            if (_settings != null && _settings.ShardCollectEffect != null)
                Destroy(Instantiate(_settings.ShardCollectEffect, transform.position, Quaternion.identity), 3f);
        }

        void OnDestroy()
        {
            _all.Remove(this);
            if (_byId.TryGetValue(Id, out var p) && p == this) _byId.Remove(Id);
        }
    }
}
