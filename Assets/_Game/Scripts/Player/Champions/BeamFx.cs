using UnityEngine;

namespace ElementalBuddies
{
    // Lichtstrahl (Lichtpfeil): LineRenderer, der schnell breit aufblitzt und schmal ausklingt.
    // Partikel-Kinder (z. B. Funken) werden entlang des Strahls gestreckt (Shape = Box, Z = Länge).
    [RequireComponent(typeof(LineRenderer))]
    public class BeamFx : MonoBehaviour
    {
        public float Lifetime = 0.45f;
        public float StartWidth = 0.9f;
        public AnimationCurve WidthOverLife = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f));
        [ColorUsage(true, true)] public Color Color = new Color(1.6f, 1.35f, 0.6f, 1f);

        private LineRenderer _line;
        private float _t;

        public static BeamFx Spawn(GameObject prefab, Vector3 from, Vector3 to)
        {
            if (prefab == null) return null;
            Vector3 dir = to - from;
            var go = Instantiate(prefab, from, dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity);
            var fx = go.GetComponent<BeamFx>();
            if (fx != null) fx.SetPoints(from, to);
            return fx;
        }

        public void SetPoints(Vector3 from, Vector3 to)
        {
            if (_line == null) _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            float len = Vector3.Distance(from, to);
            foreach (var ps in GetComponentsInChildren<ParticleSystem>())
            {
                var sh = ps.shape;
                if (!sh.enabled || sh.shapeType != ParticleSystemShapeType.Box) continue;
                sh.scale = new Vector3(sh.scale.x, sh.scale.y, len);
                ps.transform.position = (from + to) * 0.5f;
                ps.transform.rotation = Quaternion.LookRotation(to - from);
                ps.Clear();
                ps.Play();
            }
        }

        void Awake()
        {
            _line = GetComponent<LineRenderer>();
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, Lifetime));
            float w = StartWidth * WidthOverLife.Evaluate(k);
            _line.startWidth = w;
            _line.endWidth = w * 0.6f;
            Color c = Color;
            c.a = 1f - k * k;
            _line.startColor = c;
            _line.endColor = c;
            if (_t >= Lifetime + 0.6f) Destroy(gameObject);
        }
    }
}
