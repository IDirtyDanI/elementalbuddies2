using UnityEngine;

namespace ElementalBuddies
{
    // Optik eines gebannten Buddys (alle Rechner): Fessel-Requisite am Buddy (HexerBrain.BindPropPrefab, Teile Bind_Ring
    // und Bind_Chain_*; Durchmesser ~1 m, wird auf die Buddy-Größe skaliert) und ein Strahl (LineRenderer) zum Bannwirker.
    // Entfernt sich selbst, sobald der Buddy nicht mehr von diesem Bannwirker gebannt ist (Klang HexRelease).
    public class BuddyBindVisual : MonoBehaviour
    {
        private ElementalBuddy _buddy;
        private EnemyBrain _source;
        private GameObject _prop;
        private Transform _ring;
        private LineRenderer _beam;
        private float _height = 1.2f;
        private float _width = 1f;
        private Color _color;

        public static void Attach(ElementalBuddy buddy, EnemyBrain source)
        {
            if (buddy == null || source == null) return;
            var v = buddy.GetComponent<BuddyBindVisual>();
            if (v == null) v = buddy.gameObject.AddComponent<BuddyBindVisual>();
            v.Bind(buddy, source);
        }

        private void Bind(ElementalBuddy buddy, EnemyBrain source)
        {
            if (_source == source && _prop != null) return;
            Clear(false);
            _buddy = buddy;
            _source = source;
            var hexer = source.GetComponent<HexerBrain>();
            _color = hexer != null ? hexer.BindColor : ThreatVisuals.WardColor;
            Measure();

            if (hexer != null && hexer.BindPropPrefab != null) _prop = Instantiate(hexer.BindPropPrefab);
            else
            {
                _prop = new GameObject("Bind_Placeholder");
                Color rc = _color;
                rc.a = 0.7f;
                var ring = ThreatVisuals.Primitive(PrimitiveType.Cylinder, "Bind_Ring", rc, _prop.transform);
                ring.transform.localScale = new Vector3(1f, 0.03f, 1f);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 120f * Mathf.Deg2Rad;
                    var c = ThreatVisuals.Primitive(PrimitiveType.Cylinder, "Bind_Chain_" + (i + 1), rc * 0.8f, _prop.transform);
                    c.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
                    c.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, -0.5f, Mathf.Sin(a) * 0.45f);
                }
            }
            _prop.name = "Bind_" + buddy.name;
            _ring = ThreatVisuals.FindPart(_prop.transform, "Bind_Ring");

            var go = new GameObject("HexBeam");
            go.transform.SetParent(_prop.transform, false);
            _beam = go.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.useWorldSpace = true;
            _beam.material = new Material(Shader.Find("Sprites/Default"));
            _beam.startColor = new Color(_color.r, _color.g, _color.b, 0.9f);
            _beam.endColor = new Color(_color.r, _color.g, _color.b, 0.35f);
            _beam.widthMultiplier = 0.08f;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;
            LateUpdate();
        }

        // Höhe/Breite des Buddys aus den Renderern
        private void Measure()
        {
            bool any = false;
            Bounds b = default;
            foreach (var r in _buddy.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is SpriteRenderer || !r.enabled) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            if (!any) return;
            _height = Mathf.Clamp(b.max.y - _buddy.transform.position.y, 0.6f, 4f);
            _width = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z), 0.8f, 3f);
        }

        void LateUpdate()
        {
            if (_buddy == null || !_buddy.IsBanished || _buddy.HexSource != _source)
            {
                if (_buddy != null && _buddy.IsBanished) { Bind(_buddy, _buddy.HexSource); return; } // anderer Bannwirker
                Clear(true);
                Destroy(this);
                return;
            }
            Vector3 p = _buddy.transform.position;
            _prop.transform.position = p + Vector3.up * _height * 0.75f;
            _prop.transform.localScale = Vector3.one * _width * 1.15f;
            if (_ring != null) _ring.Rotate(0f, 70f * Time.deltaTime, 0f, Space.World);
            if (_beam != null && _source != null)
            {
                _beam.SetPosition(0, p + Vector3.up * _height * 0.6f);
                _beam.SetPosition(1, _source.transform.position + Vector3.up * 1.5f);
                _beam.widthMultiplier = 0.07f + 0.03f * Mathf.Sin(Time.time * 6f);
            }
        }

        private void Clear(bool released)
        {
            if (_prop != null)
            {
                if (released)
                {
                    GameAudio.PlayOr(SfxId.HexRelease, _prop.transform.position);
                    ThreatVisuals.Burst(_prop.transform.position, _color, 6, 0.1f, 3f, 0.5f);
                }
                if (_beam != null && _beam.material != null) Destroy(_beam.material);
                Destroy(_prop);
            }
            _prop = null;
            _ring = null;
            _beam = null;
        }

        void OnDestroy() => Clear(false);
    }
}
