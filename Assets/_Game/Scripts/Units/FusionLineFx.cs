using UnityEngine;

namespace ElementalBuddies
{
    // Kurzlebiger Laufzeit-LineRenderer (Blitz, Wasserstrahl, Fluch-Strahl, Böen-Ring) – blendet sich aus und zerstört sich selbst.
    // Braucht kein Prefab; ohne Material wird das gemeinsame "Sprites/Default"-Laufzeit-Material genutzt.
    public class FusionLineFx : MonoBehaviour
    {
        private LineRenderer _line;
        private float _life, _t;
        private Color _c0, _c1;
        private float _w0, _w1;
        private bool _ring;
        private float _r0, _r1;

        private const int RingSegments = 40;

        private static LineRenderer CreateLine(string name, Material material, out FusionLineFx fx)
        {
            var go = new GameObject(name);
            var line = go.AddComponent<LineRenderer>();
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 2;
            line.sharedMaterial = material != null ? material : RangeIndicator.DefaultMaterial;
            fx = go.AddComponent<FusionLineFx>();
            fx._line = line;
            return line;
        }

        // Linienzug in Weltkoordinaten (Breite und Alpha laufen auf 0 aus)
        public static FusionLineFx Spawn(Vector3[] points, Material material, Color startColor, Color endColor,
            float startWidth, float endWidth, float lifetime, string name = "FusionLine")
        {
            if (points == null || points.Length < 2) return null;
            var line = CreateLine(name, material, out var fx);
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            line.SetPositions(points);
            fx.Init(startColor, endColor, startWidth, endWidth, lifetime);
            return fx;
        }

        // Flacher Ring am Boden, der seinen Radius von radiusFrom nach radiusTo ändert
        public static FusionLineFx SpawnRing(Vector3 center, float radiusFrom, float radiusTo, Material material, Color color,
            float width, float lifetime, string name = "FusionRing")
        {
            var line = CreateLine(name, material, out var fx);
            fx.transform.position = center;
            line.useWorldSpace = false;
            line.loop = true;
            line.alignment = LineAlignment.TransformZ;
            fx.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lokale XY-Ebene -> Welt-XZ-Ebene
            line.positionCount = RingSegments;
            fx._ring = true;
            fx._r0 = radiusFrom;
            fx._r1 = radiusTo;
            fx.SetRadius(radiusFrom);
            fx.Init(color, color, width, width, lifetime);
            return fx;
        }

        private void Init(Color c0, Color c1, float w0, float w1, float lifetime)
        {
            _c0 = c0;
            _c1 = c1;
            _w0 = w0;
            _w1 = w1;
            _life = Mathf.Max(0.01f, lifetime);
            _t = 0f;
            Apply(1f);
        }

        void Update()
        {
            _t += Time.deltaTime;
            if (_t >= _life)
            {
                Destroy(gameObject);
                return;
            }
            float p = _t / _life;
            if (_ring) SetRadius(Mathf.Lerp(_r0, _r1, 1f - (1f - p) * (1f - p))); // ease-out
            Apply(1f - p);
        }

        private void Apply(float k)
        {
            if (_line == null) return;
            // Ringe behalten ihre Breite, Linien werden dünner
            float wk = _ring ? 1f : k;
            _line.startWidth = _w0 * wk;
            _line.endWidth = _w1 * wk;
            Color a = _c0; a.a *= k;
            Color b = _c1; b.a *= k;
            _line.startColor = a;
            _line.endColor = b;
        }

        private void SetRadius(float radius)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                _line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }
    }
}
