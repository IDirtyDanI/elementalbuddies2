using UnityEngine;

namespace ElementalBuddies
{
    // Zielvorschau am Boden beim Ziehen von einem Fähigkeits-Knopf (Mobil, Paket S): Ziel-Fähigkeiten zeigen den
    // Reichweitenkreis um die Figur, eine Linie zum Zielpunkt und den Wirkradius am Ziel; Richtungs-Fähigkeiten eine
    // Linie über die volle Reichweite. Linien wie die Pings (Resources/PingLine.mat, im Build sicher dabei), Farben über
    // Vertex-Farben. Wird erst beim ersten Zielen erzeugt.
    public class TouchAimPreview : MonoBehaviour
    {
        private const int RingPoints = 48;
        private const float Lift = 0.08f;
        private static readonly Color LineColor = new Color(1f, 0.85f, 0.4f, 0.9f);
        private static readonly Color TargetColor = new Color(1f, 0.82f, 0.3f, 0.95f);
        private static readonly Color RangeColor = new Color(1f, 0.95f, 0.8f, 0.35f);

        private TouchControls _tc;
        private Material _material;
        private LineRenderer _line, _target, _range;
        private bool _shown;

        void Awake()
        {
            _tc = GetComponent<TouchControls>();
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        void LateUpdate()
        {
            var player = _tc != null ? _tc.Player : null;
            if (player == null || !_tc.ControlsVisible
                || !_tc.TryGetAimPreview(out TouchAimKind kind, out Vector3 dir, out float dist, out float range, out float area))
            {
                Hide();
                return;
            }
            EnsureCreated();

            var abilities = _tc.Abilities;
            Vector3 origin = player.transform.position;
            origin.y = (abilities != null ? abilities.GetGroundHeight(origin, origin.y) : origin.y) + Lift;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 8f);

            if (kind == TouchAimKind.Point)
            {
                Vector3 target = origin + dir * dist;
                target.y = (abilities != null ? abilities.GetGroundHeight(target, origin.y) : origin.y) + Lift;
                SetLine(_line, origin, target, LineColor);
                SetRing(_target, target, Mathf.Max(0.3f, area), WithAlpha(TargetColor, TargetColor.a * pulse));
                SetRing(_range, origin, Mathf.Max(0.5f, range), RangeColor);
                _range.enabled = true;
            }
            else
            {
                Vector3 end = origin + dir * Mathf.Max(0.5f, range);
                SetLine(_line, origin, end, LineColor);
                SetRing(_target, end, 0.35f, WithAlpha(TargetColor, TargetColor.a * pulse));
                _range.enabled = false;
            }
            _line.enabled = _target.enabled = true;
            _shown = true;
        }

        private void Hide()
        {
            if (!_shown) return;
            _shown = false;
            if (_line != null) _line.enabled = false;
            if (_target != null) _target.enabled = false;
            if (_range != null) _range.enabled = false;
        }

        private void EnsureCreated()
        {
            if (_line != null) return;
            var src = Resources.Load<Material>("PingLine");
            _material = src != null ? new Material(src) : new Material(Shader.Find("Sprites/Default"));
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
            _line = MakeLine("AimLine", 2, false, 0.14f);
            _target = MakeLine("AimTarget", RingPoints, true, 0.1f);
            _range = MakeLine("AimRange", RingPoints, true, 0.06f);
        }

        private LineRenderer MakeLine(string name, int points, bool loop, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = points;
            lr.loop = loop;
            lr.widthMultiplier = width;
            lr.sharedMaterial = _material;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // flach auf dem Boden
            lr.enabled = false;
            return lr;
        }

        private static void SetLine(LineRenderer lr, Vector3 a, Vector3 b, Color c)
        {
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startColor = c;
            lr.endColor = c;
        }

        private static void SetRing(LineRenderer lr, Vector3 center, float radius, Color c)
        {
            for (int k = 0; k < RingPoints; k++)
            {
                float a = k / (float)RingPoints * Mathf.PI * 2f;
                lr.SetPosition(k, center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            lr.startColor = c;
            lr.endColor = c;
            lr.enabled = true;
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = Mathf.Clamp01(a);
            return c;
        }
    }
}
