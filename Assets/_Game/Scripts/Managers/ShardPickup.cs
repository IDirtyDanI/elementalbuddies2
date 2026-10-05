using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Seelensplitter-Drop eines getöteten Gegners. Springt kurz heraus, schwebt dann auf der Stelle und fliegt zum
    // Spieler, sobald er im Magnet-Radius steht. Nur der Spieler sammelt ein; Drops verschwinden nie (auch nicht in
    // der Bauphase). Liegt beim Spawnen schon ein Drop in MergeRadius, wird der Wert dort aufaddiert.
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

        private static Transform _player;
        private static float _nextPlayerSearch;
        private static Material _fallbackMat;

        public float Value { get; private set; }
        public bool IsMagnetized => _magnet;

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
            _player = null;
            _nextPlayerSearch = 0f;
        }

        // Alle liegenden Drops fliegen zum Spieler (z. B. am Wellenende); läuft wie der Magnet, sobald die Zeit weiterläuft
        public static void RecallAll()
        {
            foreach (var p in _all)
            {
                if (p == null) continue;
                p._magnet = true;
                p._recall = true;
            }
        }

        // Drop am Todesort erzeugen (bzw. in einen nahen Drop einrechnen)
        public static ShardPickup Spawn(Vector3 position, float value, GlobalSettingsSO settings)
        {
            if (value <= 0f) return null;
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

            var go = new GameObject("ShardPickup");
            var pickup = go.AddComponent<ShardPickup>();
            pickup.Init(ground, value, settings);
            return pickup;
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

        private void Init(Vector3 ground, float value, GlobalSettingsSO settings)
        {
            _settings = settings;
            Value = value;
            // Kleiner Sprung in zufällige Richtung (0,5–1 m)
            Vector2 dir = Random.insideUnitCircle.normalized;
            if (dir == Vector2.zero) dir = Vector2.right;
            Vector3 offset = new Vector3(dir.x, 0f, dir.y) * Random.Range(0.5f, 1f);
            _origin = ground;
            _from = ground + Vector3.up * HoverHeight;
            _land = GroundPoint(ground + offset);
            transform.position = _from;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);
            RefreshVisual();
            _all.Add(this);
        }

        public void AddValue(float value)
        {
            if (value <= 0f) return;
            Value += value;
            RefreshVisual();
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

        private static bool Blocked =>
            PauseManager.IsPaused || Time.timeScale <= 0f ||
            (GameManager.Instance != null && GameManager.Instance.IsGameOver);

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

            Transform player = Player;
            if (player != null)
            {
                Vector3 chest = player.position + Vector3.up * 0.8f;
                if (!_magnet)
                {
                    float magnet = _settings != null ? _settings.ShardMagnetRadius : 3f;
                    Vector3 d = player.position - _land;
                    d.y = 0f;
                    if (d.sqrMagnitude <= magnet * magnet) _magnet = true;
                }
                if (_magnet)
                {
                    float maxSpeed = _settings != null ? _settings.ShardMagnetSpeed : 14f;
                    if (_recall) maxSpeed *= RecallSpeedFactor;
                    _speed = Mathf.MoveTowards(_speed, maxSpeed, maxSpeed * 2.5f * dt);
                    transform.position = Vector3.MoveTowards(transform.position, chest, _speed * dt);
                    transform.Rotate(0f, 720f * dt, 0f, Space.World);
                    float collect = _settings != null ? _settings.ShardCollectRadius : 0.5f;
                    if ((transform.position - chest).sqrMagnitude <= collect * collect) Collect();
                    return;
                }
            }

            // Schweben + Drehen
            _bobPhase += dt * 2.6f;
            transform.position = _land + Vector3.up * (HoverHeight + Mathf.Sin(_bobPhase) * 0.12f);
            transform.Rotate(0f, 90f * dt, 0f, Space.World);
        }

        private void Collect()
        {
            if (Value <= 0f) return;
            float v = Value;
            Value = 0f;
            if (EconomyManager.Instance != null) EconomyManager.Instance.EarnShards(v);
            GameAudio.Play(SfxId.ShardPickup, transform.position);
            if (_settings != null && _settings.ShardCollectEffect != null)
                Destroy(Instantiate(_settings.ShardCollectEffect, transform.position, Quaternion.identity), 3f);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            _all.Remove(this);
        }
    }
}
