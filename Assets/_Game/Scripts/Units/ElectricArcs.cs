using UnityEngine;

namespace ElementalBuddies
{
    // Blitz-Aura: zuckende Lichtbögen zwischen Zufallspunkten auf einer Ellipsoid-Hülle um den Buddy.
    // Jeder Bogen ist ein LineRenderer, der kurz aufflackert, verblasst und an neuer Stelle wieder entsteht.
    public class ElectricArcs : MonoBehaviour
    {
        public Material ArcMaterial;
        public Color ArcColor = new Color(1f, 0.9f, 0.45f);
        public int ArcCount = 4;
        public int Segments = 9;
        public Vector3 HullCenter = new Vector3(0f, 1.25f, 0f);
        public Vector3 HullRadius = new Vector3(0.75f, 0.9f, 0.6f);
        public float Jitter = 0.14f;
        public float Width = 0.035f;
        public Vector2 LifeRange = new Vector2(0.06f, 0.16f);
        public Vector2 PauseRange = new Vector2(0.05f, 0.45f);
        [Tooltip("Optional: Licht, das mit den Bögen mitflackert.")]
        public Light FlickerLight;

        private LineRenderer[] _lines;
        private float[] _timer, _life;
        private bool[] _visible;
        private float _baseIntensity;

        void Awake()
        {
            _lines = new LineRenderer[ArcCount];
            _timer = new float[ArcCount];
            _life = new float[ArcCount];
            _visible = new bool[ArcCount];
            for (int i = 0; i < ArcCount; i++)
            {
                var go = new GameObject("Arc" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = Segments + 1;
                lr.sharedMaterial = ArcMaterial;
                lr.widthMultiplier = Width;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = false;
                _lines[i] = lr;
                _timer[i] = Random.Range(0f, PauseRange.y);
            }
            if (FlickerLight != null) _baseIntensity = FlickerLight.intensity;
        }

        void Update()
        {
            int lit = 0;
            for (int i = 0; i < ArcCount; i++)
            {
                _timer[i] -= Time.deltaTime;
                if (_visible[i])
                {
                    float k = Mathf.Clamp01(_timer[i] / _life[i]);
                    var c = ArcColor; c.a = k;
                    _lines[i].startColor = c; _lines[i].endColor = c;
                    _lines[i].widthMultiplier = Width * (0.5f + 0.5f * k);
                    lit++;
                    if (_timer[i] <= 0f)
                    {
                        _visible[i] = false;
                        _lines[i].enabled = false;
                        _timer[i] = Random.Range(PauseRange.x, PauseRange.y);
                    }
                }
                else if (_timer[i] <= 0f)
                {
                    Strike(i);
                }
            }
            if (FlickerLight != null)
                FlickerLight.intensity = _baseIntensity * (lit > 0 ? Random.Range(1.1f, 1.6f) : 0.85f);
        }

        private Vector3 HullPoint()
        {
            Vector3 d = Random.onUnitSphere;
            return HullCenter + Vector3.Scale(d, HullRadius);
        }

        private void Strike(int i)
        {
            Vector3 a = HullPoint();
            Vector3 b = HullPoint();
            // nicht zu kurze Bögen
            for (int tries = 0; tries < 4 && (a - b).sqrMagnitude < 0.3f; tries++) b = HullPoint();
            var lr = _lines[i];
            for (int s = 0; s <= Segments; s++)
            {
                float t = s / (float)Segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                // nach außen gewölbt, damit der Bogen um den Körper herum läuft
                Vector3 outward = (p - HullCenter); outward.y *= 0.3f;
                p += outward.normalized * Mathf.Sin(t * Mathf.PI) * 0.25f;
                if (s > 0 && s < Segments) p += Random.insideUnitSphere * Jitter;
                lr.SetPosition(s, p);
            }
            _life[i] = Random.Range(LifeRange.x, LifeRange.y);
            _timer[i] = _life[i];
            _visible[i] = true;
            lr.enabled = true;
        }
    }
}
