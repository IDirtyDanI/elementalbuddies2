using UnityEngine;

namespace ElementalBuddies
{
    // Reichweiten-Kreis (LineRenderer) am Boden; wird zur Laufzeit vom InteractionManager erzeugt.
    [RequireComponent(typeof(LineRenderer))]
    public class RangeIndicator : MonoBehaviour
    {
        public int Segments = 64;
        public float Width = 0.1f;
        public float HeightOffset = 0.5f; // above the 3D grass (~0.45 m)

        private LineRenderer _line;
        private Transform _target;
        private float _radius = -1f;

        private static Material _sharedMaterial;

        // Fallback-Material: "Sprites/Default" ist unlit, transparent und nutzt Vertex-Farben -> funktioniert in URP mit LineRenderer
        public static Material DefaultMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader != null) _sharedMaterial = new Material(shader) { name = "RangeIndicator (Runtime)" };
                }
                return _sharedMaterial;
            }
        }

        public static RangeIndicator Create(string name, Material material)
        {
            var go = new GameObject(name);
            var line = go.AddComponent<LineRenderer>();
            var indicator = go.AddComponent<RangeIndicator>();
            indicator.Setup(line, material != null ? material : DefaultMaterial);
            go.SetActive(false);
            return indicator;
        }

        private void Setup(LineRenderer line, Material material)
        {
            _line = line;
            _line.useWorldSpace = false;
            _line.loop = true;
            _line.positionCount = Segments;
            _line.startWidth = Width;
            _line.endWidth = Width;
            _line.alignment = LineAlignment.TransformZ; // flach auf dem Boden
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.numCapVertices = 0;
            if (material != null) _line.sharedMaterial = material;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lokale XY-Ebene -> Welt-XZ-Ebene
        }

        public void Show(Transform target, float radius, Color color)
        {
            _target = target;
            if (!Mathf.Approximately(radius, _radius)) SetRadius(radius);
            _line.startColor = color;
            _line.endColor = color;
            FollowTarget();
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            _target = null;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void SetRadius(float radius)
        {
            _radius = radius;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                _line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }

        void LateUpdate()
        {
            if (_target == null)
            {
                Hide();
                return;
            }
            FollowTarget();
        }

        private void FollowTarget()
        {
            if (_target != null) transform.position = _target.position + Vector3.up * HeightOffset;
        }

        // Farbe pro Element (Index wie ElementalBuddy.ElementIndex)
        public static Color ElementColor(int elementIndex, float alpha = 0.7f)
        {
            Color c;
            switch (elementIndex)
            {
                case 0: c = new Color(1f, 0.45f, 0.15f); break; // Feuer
                case 1: c = new Color(0.45f, 0.85f, 1f); break; // Eis
                case 2: c = new Color(0.55f, 0.85f, 0.35f); break; // Erde
                case 3: c = new Color(1f, 0.92f, 0.5f); break; // Licht
                default: c = Color.white; break;
            }
            c.a = alpha;
            return c;
        }
    }
}
