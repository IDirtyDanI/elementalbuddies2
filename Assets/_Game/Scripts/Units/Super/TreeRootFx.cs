using UnityEngine;

namespace ElementalBuddies
{
    // Reine Optik für VFX_TreeRoot (Weltenbaum): WorldTreeBuddy hängt den Effekt für RootDuration an jeden festgehaltenen
    // Gegner (Position = Gegner-Pivot, bei Läufern 1 m über dem Boden). Der Bodenteil wird auf den echten Boden gesetzt,
    // Welt-Drehung neutral und Welt-Skalierung 1 (unabhängig vom Gegner-Transform). Die Wurzel-Meshes wachsen nach Delay
    // (passend zum Stampfer der Cast-Animation) aus dem Boden (Skalierung Y 0 → 1 mit leichtem Überschwingen) und
    // versinken in den letzten SinkTime Sekunden. Partikel unter dem Objekt starten zum selben Zeitpunkt. Kein Einfluss auf Spiellogik.
    public class TreeRootFx : MonoBehaviour
    {
        public Transform Ground;          // Bodenteil (Wurzeln + Partikel)
        public Transform[] Roots;         // Wurzel-Meshes (Pivot am Boden, wachsen entlang +Y)
        public float Delay = 0.35f;
        public float GrowTime = 0.22f;
        public float Life = 2.5f;         // = WorldTreeBuddy.RootDuration
        public float SinkTime = 0.45f;
        public float SinkDepth = 0.25f;

        private float _age;
        private bool _played;
        private Vector3[] _scales;
        private Vector3[] _positions;
        private ParticleSystem[] _particles;
        private float _groundY;
        private Vector2 _groundAt = new Vector2(float.MaxValue, float.MaxValue);

        void Awake()
        {
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            if (Roots == null) Roots = new Transform[0];
            _scales = new Vector3[Roots.Length];
            _positions = new Vector3[Roots.Length];
            for (int i = 0; i < Roots.Length; i++)
            {
                if (Roots[i] == null) continue;
                _scales[i] = Roots[i].localScale;
                _positions[i] = Roots[i].localPosition;
            }
            Apply();
        }

        void LateUpdate()
        {
            _age += Time.deltaTime;
            if (!_played && _age >= Delay)
            {
                _played = true;
                foreach (var ps in _particles)
                    if (ps != null) ps.Play(false);
            }
            Apply();
        }

        private void Apply()
        {
            if (Ground != null)
            {
                Vector3 p = transform.position;
                Vector2 xz = new Vector2(p.x, p.z);
                if ((xz - _groundAt).sqrMagnitude > 0.09f)
                {
                    _groundAt = xz;
                    _groundY = SuperBuddy.GroundPoint(p).y;
                }
                Ground.position = new Vector3(p.x, _groundY, p.z);
                Ground.rotation = Quaternion.identity;
                Vector3 ls = transform.lossyScale;
                Ground.localScale = new Vector3(Inv(ls.x), Inv(ls.y), Inv(ls.z));
            }

            float t = _age - Delay;
            float grow = GrowTime > 0f ? Mathf.Clamp01(t / GrowTime) : (t >= 0f ? 1f : 0f);
            // Ease-out mit leichtem Überschwingen (1,12 bei 70 %)
            float k = grow < 0.7f ? Mathf.Lerp(0f, 1.12f, 1f - (1f - grow / 0.7f) * (1f - grow / 0.7f)) : Mathf.Lerp(1.12f, 1f, (grow - 0.7f) / 0.3f);
            float sink = SinkTime > 0f ? Mathf.Clamp01((Life - _age) / SinkTime) : 1f;
            k *= sink;
            for (int i = 0; i < Roots.Length; i++)
            {
                if (Roots[i] == null) continue;
                Vector3 s = _scales[i];
                Roots[i].localScale = new Vector3(s.x * Mathf.Max(0.3f, k), s.y * Mathf.Max(0.001f, k), s.z * Mathf.Max(0.3f, k));
                Roots[i].localPosition = _positions[i] - Vector3.up * (1f - sink) * SinkDepth;
                if (Roots[i].gameObject.activeSelf != (t >= 0f && k > 0.002f)) Roots[i].gameObject.SetActive(t >= 0f && k > 0.002f);
            }
        }

        private static float Inv(float v) { return Mathf.Abs(v) > 0.0001f ? 1f / v : 1f; }
    }
}
