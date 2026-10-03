using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Runtime wall instance: static BoxCollider + carving NavMeshObstacle on the root,
    // the visual child rises out of the ground, stays, then sinks away and the wall is destroyed.
    public class StoneWall : MonoBehaviour
    {
        private Transform _visual;
        private float _height;

        public static StoneWall Spawn(StoneWallSpell cfg, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("StoneWall");
            root.transform.SetPositionAndRotation(position, rotation);

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

            // Sink (ease in)
            yield return Animate(up, down, sinkTime, t => t * t);
            Destroy(gameObject);
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
