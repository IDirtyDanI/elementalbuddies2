using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Runtime wall instance: static BoxCollider + carving NavMeshObstacle on the root,
    // the visual child rises out of the ground, stays, then sinks away and the wall is destroyed.
    public class StoneWall : MonoBehaviour
    {
        // Eigener Layer „SpellWall“: Gegner-Projektile, Sichtlinie der Fernkämpfer und Boss-Strahl/-Kegel prüfen
        // nur gegen diesen Layer. Kollisionsmatrix bleibt voll (Spieler/Gegner laufen weiter dagegen).
        public const string LayerName = "SpellWall";
        private static int _layer = -2;
        public static int Layer
        {
            get
            {
                if (_layer == -2) _layer = LayerMask.NameToLayer(LayerName);
                return _layer;
            }
        }
        // Maske für Wand-Abfragen (0, falls der Layer fehlt)
        public static int BlockMask => Layer >= 0 ? 1 << Layer : 0;

        // Anzahl blockierender Wände (Kollider aktiv) – ohne Wand sparen sich Gegner die Sichtlinien-Abfragen
        public static int ActiveCount { get; private set; }
        public static bool AnyActive => ActiveCount > 0 && BlockMask != 0;

        // Liegt eine aktive Wand zwischen from und to?
        public static bool Blocks(Vector3 from, Vector3 to)
        {
            if (!AnyActive) return false;
            return Physics.Linecast(from, to, BlockMask, QueryTriggerInteraction.Ignore);
        }

        // Erster Wandtreffer auf der Strecke from→to (für Strahl-Kürzung)
        public static bool Blocks(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            hit = default(RaycastHit);
            if (!AnyActive) return false;
            return Physics.Linecast(from, to, out hit, BlockMask, QueryTriggerInteraction.Ignore);
        }

        private Transform _visual;
        private float _height;
        private bool _counted;

        public static StoneWall Spawn(StoneWallSpell cfg, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("StoneWall");
            root.transform.SetPositionAndRotation(position, rotation);
            if (Layer >= 0) root.layer = Layer;

            Vector3 size = new Vector3(cfg.Length, cfg.Height, cfg.Thickness);
            Vector3 center = new Vector3(0f, cfg.Height * 0.5f, 0f);

            var box = root.AddComponent<BoxCollider>();
            box.size = size;
            box.center = center;

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = size;
            obstacle.center = center;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false; // carve immediately
            obstacle.carvingMoveThreshold = 0.1f;

            // Visual
            Transform visual;
            if (cfg.WallPrefab != null)
            {
                visual = Instantiate(cfg.WallPrefab, root.transform).transform;
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
                if (cfg.WallPrefabIsUnitSize) visual.localScale = Vector3.Scale(cfg.WallPrefab.transform.localScale, size);
                else if (cfg.WallPrefabLength > 0.01f && !Mathf.Approximately(cfg.Length, cfg.WallPrefabLength))
                {
                    // Mauerlänge-Karte: Optik entlang der Wand (lokal X) mitstrecken, damit sie zum Kollider passt
                    Vector3 s = visual.localScale;
                    s.x *= cfg.Length / cfg.WallPrefabLength;
                    visual.localScale = s;
                }
                // The root collider does the blocking; prefab colliders would only duplicate it
                foreach (var c in visual.GetComponentsInChildren<Collider>()) c.enabled = false;
            }
            else
            {
                var holder = new GameObject("Visual").transform;
                holder.SetParent(root.transform, false);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(holder, false);
                cube.transform.localPosition = center;
                cube.transform.localScale = size;
                var rend = cube.GetComponent<Renderer>();
                if (rend != null)
                {
                    var stone = new Color(0.45f, 0.38f, 0.3f);
                    var mat = rend.material;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", stone); // URP
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", stone);         // Built-in
                }
                visual = holder;
            }

            var wall = root.AddComponent<StoneWall>();
            wall._counted = true;
            ActiveCount++;
            wall._visual = visual;
            wall._height = cfg.Height;
            wall.StartCoroutine(wall.Run(cfg.RiseTime, cfg.Lifetime, cfg.SinkTime));
            return wall;
        }

        private IEnumerator Run(float riseTime, float lifetime, float sinkTime)
        {
            Vector3 up = _visual.localPosition;
            Vector3 down = up - new Vector3(0f, _height, 0f);

            // Rise (ease out)
            yield return Animate(down, up, riseTime, t => 1f - (1f - t) * (1f - t));

            float hold = Mathf.Max(0f, lifetime - riseTime);
            if (hold > 0f) yield return new WaitForSeconds(hold);

            // Stop blocking as soon as it starts sinking
            var box = GetComponent<BoxCollider>();
            if (box != null) box.enabled = false;
            var obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle != null) obstacle.enabled = false;
            Uncount();

            // Sink (ease in)
            yield return Animate(up, down, sinkTime, t => t * t);
            Destroy(gameObject);
        }

        void OnDestroy() => Uncount();

        private void Uncount()
        {
            if (!_counted) return;
            _counted = false;
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        // Domain-Reload aus (Enter Play Mode Options): Zähler beim Spielstart zurücksetzen
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveCount = 0;
            _layer = -2;
        }

        private IEnumerator Animate(Vector3 from, Vector3 to, float duration, System.Func<float, float> ease)
        {
            if (_visual == null) yield break;
            if (duration <= 0f)
            {
                _visual.localPosition = to;
                yield break;
            }

            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / duration);
                if (_visual == null) yield break;
                _visual.localPosition = Vector3.LerpUnclamped(from, to, ease(t));
                yield return null;
            }
        }
    }
}
