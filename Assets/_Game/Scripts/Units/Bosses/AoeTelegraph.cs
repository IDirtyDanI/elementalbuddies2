using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    public enum AoeShapeKind { Circle, Cone, Line }

    // Horizontale Trefferfläche einer Boss-Fähigkeit (gleiche Form für Warnfläche und Treffertest)
    [System.Serializable]
    public struct AoeShape
    {
        public AoeShapeKind Kind;
        public Vector3 Origin;   // Kreis: Mitte, Kegel: Spitze, Linie: Startpunkt
        public Vector3 Forward;  // horizontal, normalisiert
        public float Radius;     // Kreis-Radius bzw. Kegel-Reichweite
        public float Angle;      // volle Kegelöffnung (Grad)
        public float Length;     // Linie
        public float Width;      // Linie (volle Breite)

        public static AoeShape Circle(Vector3 center, float radius) =>
            new AoeShape { Kind = AoeShapeKind.Circle, Origin = center, Forward = Vector3.forward, Radius = radius };

        public static AoeShape Cone(Vector3 origin, Vector3 forward, float range, float angle) =>
            new AoeShape { Kind = AoeShapeKind.Cone, Origin = origin, Forward = Flat(forward), Radius = range, Angle = angle };

        public static AoeShape Line(Vector3 origin, Vector3 forward, float length, float width) =>
            new AoeShape { Kind = AoeShapeKind.Line, Origin = origin, Forward = Flat(forward), Length = length, Width = width };

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }

        // Liegt p (horizontal) in der Fläche? pad = Radius des Ziels
        public bool Contains(Vector3 p, float pad = 0f)
        {
            Vector3 to = p - Origin;
            to.y = 0f;
            switch (Kind)
            {
                case AoeShapeKind.Circle:
                    return to.magnitude <= Radius + pad;
                case AoeShapeKind.Cone:
                    if (to.magnitude > Radius + pad) return false;
                    return to.sqrMagnitude < 0.36f || Vector3.Angle(Forward, to) <= Angle * 0.5f + Mathf.Rad2Deg * Mathf.Atan2(pad, Mathf.Max(0.5f, to.magnitude));
                default:
                    float along = Vector3.Dot(to, Forward);
                    if (along < -pad || along > Length + pad) return false;
                    return (to - Forward * along).magnitude <= Width * 0.5f + pad;
            }
        }
    }

    // Boden-Warnfläche (Kreis / Kegel / Linie): schwache Grundfläche + Umriss + Füllung, die über die
    // Ausholzeit von 0 auf 1 wächst. Mesh und Material entstehen zur Laufzeit ("Sprites/Default", Vertex-Farben).
    public class AoeTelegraph : MonoBehaviour
    {
        private const float BaseAlpha = 0.14f, FillAlpha = 0.38f, OutlineAlpha = 0.9f;
        private const float OutlineWidth = 0.12f;
        private const float GroundOffset = 0.05f;

        private static Material _material;

        private AoeShape _shape;
        private Color _color;
        private float _duration, _t;
        private Transform _fill;
        private MeshRenderer[] _renderers;
        private Mesh[] _meshes;
        private bool _ending;
        private float _endT, _endDuration;
        private bool _flash;

        public float Progress => _duration > 0f ? Mathf.Clamp01(_t / _duration) : 1f;

        private static Material SharedMaterial
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null) _material = new Material(shader) { name = "AoeTelegraph (Runtime)", renderQueue = 3100 };
                }
                return _material;
            }
        }

        // Warnfläche erzeugen; füllt sich in fillDuration s und bleibt dann voll, bis Finish() / Cancel()
        public static AoeTelegraph Spawn(AoeShape shape, Color color, float fillDuration)
        {
            var go = new GameObject("AoeTelegraph");
            var t = go.AddComponent<AoeTelegraph>();
            t.Init(shape, color, fillDuration);
            return t;
        }

        // Bodenhöhe: Floor-Layer per Raycast, sonst NavMesh, sonst p.y
        public static float GroundY(Vector3 p)
        {
            int floor = LayerMask.GetMask("Floor");
            if (floor != 0 && Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, floor, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            if (NavMesh.SamplePosition(p, out NavMeshHit nh, 3f, NavMesh.AllAreas)) return nh.position.y;
            return p.y;
        }

        private void Init(AoeShape shape, Color color, float fillDuration)
        {
            _shape = shape;
            _color = color;
            _duration = Mathf.Max(0.01f, fillDuration);
            Vector3 pos = shape.Origin;
            pos.y = GroundY(pos) + GroundOffset;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(shape.Forward));

            _meshes = new[] { BuildArea(BaseAlpha), BuildArea(FillAlpha), BuildOutline() };
            _renderers = new MeshRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var child = new GameObject(i == 0 ? "Base" : i == 1 ? "Fill" : "Outline");
                child.transform.SetParent(transform, false);
                // Reihenfolge über minimale Höhenstaffelung (kein Z-Fighting untereinander)
                child.transform.localPosition = Vector3.up * (0.004f * i);
                child.AddComponent<MeshFilter>().sharedMesh = _meshes[i];
                var r = child.AddComponent<MeshRenderer>();
                r.sharedMaterial = SharedMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _renderers[i] = r;
                if (i == 1) _fill = child.transform;
            }
            ApplyFill(0f);
            Destroy(gameObject, _duration + 15f); // Sicherheitsnetz
        }

        void Update()
        {
            if (_ending)
            {
                _endT += Time.deltaTime;
                float k = Mathf.Clamp01(_endT / _endDuration);
                if (_flash)
                {
                    // Aufblitzen (Einschlag): kurz heller, dann ausblenden
                    float s = 1f + 0.08f * k;
                    transform.localScale = new Vector3(s, 1f, s);
                    SetTint(Mathf.Lerp(1.2f, 0f, k));
                }
                else SetTint(1f - k);
                if (k >= 1f) Destroy(gameObject);
                return;
            }
            _t += Time.deltaTime;
            ApplyFill(Progress);
        }

        private void ApplyFill(float f)
        {
            if (_fill == null) return;
            float e = Mathf.Max(0.001f, f);
            _fill.localScale = _shape.Kind == AoeShapeKind.Line ? new Vector3(1f, 1f, e) : new Vector3(e, 1f, e);
            // Umriss pulsiert kurz vor dem Einschlag
            float pulse = f > 0.7f ? 0.75f + 0.25f * Mathf.Sin(Time.time * 30f) : 1f;
            SetTint(1f, pulse);
        }

        private void SetTint(float alphaScale, float outlineScale = 1f)
        {
            // Vertex-Alpha ist fest, Tönung über die Mesh-Farben (gemeinsames Material)
            for (int i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] == null) continue;
                float a = (i == 0 ? BaseAlpha : i == 1 ? FillAlpha : OutlineAlpha) * alphaScale * (i == 2 ? outlineScale : 1f);
                Color c = _color;
                c.a = Mathf.Clamp01(a);
                var cols = _meshes[i].colors;
                for (int v = 0; v < cols.Length; v++) cols[v] = c;
                _meshes[i].colors = cols;
            }
        }

        // Einschlag: kurz aufblitzen, dann weg
        public void Finish(float flashTime = 0.3f)
        {
            if (_ending) return;
            ApplyFill(1f);
            _ending = true;
            _flash = true;
            _endDuration = Mathf.Max(0.01f, flashTime);
        }

        // Abgebrochen: schnell ausblenden
        public void Cancel()
        {
            if (_ending) return;
            _ending = true;
            _flash = false;
            _endDuration = 0.15f;
        }

        void OnDestroy()
        {
            if (_meshes == null) return;
            foreach (var m in _meshes) if (m != null) Destroy(m);
        }

        // ---------------- Meshes (lokal: Z = Forward) ----------------

        private Mesh BuildArea(float alpha)
        {
            var mesh = new Mesh { name = "AoeArea" };
            if (_shape.Kind == AoeShapeKind.Line)
            {
                float w = _shape.Width * 0.5f, l = _shape.Length;
                mesh.vertices = new[] { new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(-w, 0f, l), new Vector3(w, 0f, l) };
                mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            }
            else
            {
                bool circle = _shape.Kind == AoeShapeKind.Circle;
                float angle = circle ? 360f : Mathf.Clamp(_shape.Angle, 1f, 360f);
                int n = Mathf.Max(8, Mathf.CeilToInt(angle / 6f));
                var verts = new Vector3[n + 2];
                var tris = new int[n * 3];
                verts[0] = Vector3.zero;
                for (int i = 0; i <= n; i++)
                {
                    float a = (-angle * 0.5f + angle * i / n) * Mathf.Deg2Rad;
                    verts[i + 1] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * _shape.Radius;
                }
                for (int i = 0; i < n; i++)
                {
                    tris[i * 3] = 0;
                    tris[i * 3 + 1] = i + 1;
                    tris[i * 3 + 2] = i + 2;
                }
                mesh.vertices = verts;
                mesh.triangles = tris;
            }
            Colorize(mesh, alpha);
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildOutline()
        {
            // Umriss als geschlossener Linienzug in lokalen XZ-Koordinaten
            var pts = new System.Collections.Generic.List<Vector3>();
            if (_shape.Kind == AoeShapeKind.Line)
            {
                float w = _shape.Width * 0.5f, l = _shape.Length;
                pts.Add(new Vector3(-w, 0f, 0f)); pts.Add(new Vector3(-w, 0f, l)); pts.Add(new Vector3(w, 0f, l)); pts.Add(new Vector3(w, 0f, 0f));
            }
            else
            {
                bool circle = _shape.Kind == AoeShapeKind.Circle;
                float angle = circle ? 360f : Mathf.Clamp(_shape.Angle, 1f, 360f);
                int n = Mathf.Max(8, Mathf.CeilToInt(angle / 6f));
                if (!circle) pts.Add(Vector3.zero);
                for (int i = 0; i <= n; i++)
                {
                    if (circle && i == n) break;
                    float a = (-angle * 0.5f + angle * i / n) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * _shape.Radius);
                }
            }

            // Pro Kante ein nach innen versetztes Viereck (Ecken überlappen leicht, fällt bei der Breite nicht auf)
            int count = pts.Count;
            var verts = new Vector3[count * 4];
            var tris = new int[count * 6];
            Vector3 centroid = Vector3.zero;
            foreach (var p in pts) centroid += p;
            centroid /= Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % count];
                Vector3 dir = (b - a).normalized;
                Vector3 normal = new Vector3(-dir.z, 0f, dir.x);
                if (Vector3.Dot(normal, centroid - (a + b) * 0.5f) < 0f) normal = -normal; // nach innen
                Vector3 off = normal * OutlineWidth;
                int v = i * 4;
                verts[v] = a; verts[v + 1] = b; verts[v + 2] = a + off; verts[v + 3] = b + off;
                tris[i * 6] = v; tris[i * 6 + 1] = v + 2; tris[i * 6 + 2] = v + 1;
                tris[i * 6 + 3] = v + 1; tris[i * 6 + 4] = v + 2; tris[i * 6 + 5] = v + 3;
            }
            var mesh = new Mesh { name = "AoeOutline", vertices = verts, triangles = tris };
            Colorize(mesh, OutlineAlpha);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Colorize(Mesh mesh, float alpha)
        {
            Color c = _color;
            c.a = alpha;
            var cols = new Color[mesh.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            mesh.colors = cols;
        }
    }
}
