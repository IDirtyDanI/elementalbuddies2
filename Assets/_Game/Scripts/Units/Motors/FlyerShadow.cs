using UnityEngine;

namespace ElementalBuddies
{
    // Bodenschatten-Klecks unter einem Flieger (Lesbarkeit: zeigt, wo er ist und wohin er herabstößt).
    // Eigenes Objekt (kein Kind, damit Lebensleiste, Treffer-Aufblitzen und Status-Tönung es nicht mitzählen) mit flachem
    // Quad, jedes Frame per Raycast auf den Untergrund (Boden/Dach) gelegt; je höher der Flieger, desto größer und
    // blasser. Rein lokal auf jedem Rechner, kein Collider, wirft/empfängt keine Schatten; stirbt mit dem Flieger.
    [DisallowMultipleComponent]
    public class FlyerShadow : MonoBehaviour
    {
        [Tooltip("Durchmesser des Kleckses direkt unter dem Flieger (Weltmeter).")]
        public float Size = 1.3f;
        [Tooltip("Deckkraft bei Flughöhe ≤ 2 m.")]
        public float Alpha = 0.55f;
        [Tooltip("Ab dieser Höhe über dem Untergrund ist der Schatten am blassesten.")]
        public float FadeHeight = 10f;
        public float MinAlpha = 0.2f;

        private static Material _material;
        private static Mesh _mesh;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Transform _shadow;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private float _lastAlpha = -1f;

        public Transform ShadowTransform => _shadow;

        void Start()
        {
            var mat = SharedMaterial();
            if (mat == null) { enabled = false; return; }
            var go = new GameObject("FlyerShadow");
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            _shadow = go.transform;
            _shadow.position = transform.position;
            go.AddComponent<MeshFilter>().sharedMesh = SharedMesh();
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = mat;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _mpb = new MaterialPropertyBlock();
            LateUpdate();
        }

        void LateUpdate()
        {
            if (_shadow == null) return;
            Vector3 pos = transform.position;
            if (!Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 60f, FlyingMotor.SurfaceMask, QueryTriggerInteraction.Ignore))
            {
                if (_renderer.enabled) _renderer.enabled = false;
                return;
            }
            if (!_renderer.enabled) _renderer.enabled = true;
            float h = Mathf.Max(0f, pos.y - hit.point.y);
            float k = Mathf.Clamp01((h - 2f) / Mathf.Max(0.1f, FadeHeight - 2f));
            float size = Size * Mathf.Lerp(1f, 1.5f, k);
            float alpha = Mathf.Lerp(Alpha, MinAlpha, k);

            _shadow.position = hit.point + hit.normal * 0.04f;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, hit.normal);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.ProjectOnPlane(Vector3.forward, hit.normal);
            _shadow.rotation = Quaternion.LookRotation(fwd, hit.normal);
            _shadow.localScale = new Vector3(size, 1f, size);
            if (Mathf.Abs(alpha - _lastAlpha) > 0.01f)
            {
                _lastAlpha = alpha;
                _renderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, new Color(0f, 0f, 0f, alpha));
                _renderer.SetPropertyBlock(_mpb);
            }
        }

        void OnEnable()
        {
            if (_shadow != null) _shadow.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            if (_shadow != null) _shadow.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_shadow != null) Destroy(_shadow.gameObject);
        }

        private static Material SharedMaterial()
        {
            if (_material != null) return _material;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            _material = new Material(shader) { name = "FlyerShadow (Runtime)", renderQueue = 3050, mainTexture = BuildBlob(64) };
            return _material;
        }

        // Quad 1 × 1 m in der XZ-Ebene, Normale nach oben
        private static Mesh SharedMesh()
        {
            if (_mesh != null) return _mesh;
            _mesh = new Mesh { name = "FlyerShadowQuad" };
            _mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            _mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _mesh.RecalculateBounds();
            return _mesh;
        }

        // Weicher runder Klecks (weiß, Alpha fällt nach außen ab; getönt über _Color)
        public static Texture2D BuildBlob(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "FlyerShadowBlob", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = 1f - Mathf.SmoothStep(0.35f, 1f, r);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
