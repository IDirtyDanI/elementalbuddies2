using UnityEngine;

namespace ElementalBuddies
{
    // Stilisierter Schwert-Bogen: flacher Ring-Sektor, der in SweepTime von links nach rechts aufgezogen wird
    // (heller Kopf, ausblendender Schweif) und danach verblasst. Mesh wird zur Laufzeit erzeugt.
    // Für Schwerthieb (120°), Flammenwirbel (360°) und Frostschlag. Farbe/Größe per Play() oder Inspector.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SlashArcFx : MonoBehaviour
    {
        public float Radius = 2.4f;
        public float Width = 0.9f;
        [Tooltip("Öffnungswinkel in Grad (360 = Vollkreis).")]
        public float Angle = 120f;
        public float SweepTime = 0.12f;
        public float FadeTime = 0.22f;
        [Tooltip("Anteil des Bogens hinter dem Kopf, der sichtbar bleibt (0..1).")]
        [Range(0.1f, 1f)] public float TailLength = 0.85f;
        public bool Clockwise = true;
        [ColorUsage(true, true)] public Color Color = new Color(0.85f, 0.95f, 1f, 1f);
        [Tooltip("Leichte Neigung des Bogens (Grad um die lokale Z-Achse), wirkt dynamischer.")]
        public float Tilt = 0f;
        public int Segments = 32;

        private Mesh _mesh;
        private float _t;
        private Vector3[] _verts;
        private Color[] _cols;
        private Vector2[] _uvs;
        private int[] _tris;

        public static SlashArcFx Spawn(GameObject prefab, Vector3 position, Vector3 forward, float radius, float angle, Color color, bool clockwise, float tilt = 0f)
        {
            if (prefab == null) return null;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            var go = Instantiate(prefab, position, Quaternion.LookRotation(forward.normalized));
            var fx = go.GetComponent<SlashArcFx>();
            if (fx != null)
            {
                fx.Radius = radius;
                fx.Angle = angle;
                fx.Color = color;
                fx.Clockwise = clockwise;
                fx.Tilt = tilt;
            }
            return fx;
        }

        void Start()
        {
            _mesh = new Mesh { name = "SlashArc" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            int n = Mathf.Max(4, Segments);
            _verts = new Vector3[(n + 1) * 2];
            _cols = new Color[_verts.Length];
            _uvs = new Vector2[_verts.Length];
            _tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int v = i * 2;
                // Gegen den Uhrzeigersinn gespiegelt → Dreiecke umdrehen, sonst Backface-Culling
                if (Clockwise)
                {
                    _tris[i * 6 + 0] = v; _tris[i * 6 + 1] = v + 1; _tris[i * 6 + 2] = v + 2;
                    _tris[i * 6 + 3] = v + 1; _tris[i * 6 + 4] = v + 3; _tris[i * 6 + 5] = v + 2;
                }
                else
                {
                    _tris[i * 6 + 0] = v; _tris[i * 6 + 1] = v + 2; _tris[i * 6 + 2] = v + 1;
                    _tris[i * 6 + 3] = v + 1; _tris[i * 6 + 4] = v + 2; _tris[i * 6 + 5] = v + 3;
                }
            }
            if (!Mathf.Approximately(Tilt, 0f)) transform.rotation *= Quaternion.Euler(0f, 0f, Tilt);
            Rebuild(0f);
            Destroy(gameObject, SweepTime + FadeTime + 0.05f);
        }

        void Update()
        {
            _t += Time.deltaTime;
            Rebuild(_t);
        }

        private void Rebuild(float t)
        {
            if (_mesh == null) return;
            int n = Mathf.Max(4, Segments);
            float sweep = SweepTime > 0f ? Mathf.Clamp01(t / SweepTime) : 1f;
            float fade = t <= SweepTime ? 1f : 1f - Mathf.Clamp01((t - SweepTime) / Mathf.Max(0.01f, FadeTime));
            float half = Angle * 0.5f;
            float head = Mathf.Lerp(0f, 1f, 1f - (1f - sweep) * (1f - sweep)); // ease-out
            float inner = Mathf.Max(0.05f, Radius - Width);

            for (int i = 0; i <= n; i++)
            {
                float u = (float)i / n;            // 0 = Start des Bogens, 1 = Ende
                float a = Mathf.Lerp(-half, half, u);
                if (!Clockwise) a = -a;
                float rad = a * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                // Kopf = hellste Stelle, dahinter Schweif, vor dem Kopf unsichtbar
                float behind = head - u;
                float alpha = behind < 0f ? 0f : Mathf.Clamp01(1f - behind / Mathf.Max(0.01f, TailLength));
                alpha = Mathf.Sqrt(alpha);
                // Kanten weicher, Mitte des Bogens etwas breiter
                float bulge = 0.65f + 0.35f * Mathf.Sin(u * Mathf.PI);
                float r0 = Mathf.Lerp(Radius, inner, bulge);
                _verts[i * 2] = dir * r0;
                _verts[i * 2 + 1] = dir * Radius;
                Color c = Color;
                c.a *= alpha * fade;
                Color edge = c;
                edge.a *= 0.4f;
                _cols[i * 2] = edge;
                _cols[i * 2 + 1] = c;
                _uvs[i * 2] = new Vector2(u, 0f);
                _uvs[i * 2 + 1] = new Vector2(u, 1f);
            }
            _mesh.vertices = _verts;
            _mesh.colors = _cols;
            _mesh.uv = _uvs;
            if (_mesh.triangles.Length != _tris.Length) _mesh.triangles = _tris;
            _mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
