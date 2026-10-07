using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Feuerspur des Phönix: Fläche entlang einer Linie (Kapsel mit HalfWidth), tickt alle 0,5 s Schaden an Gegner darin.
    // Eigenständig; Optik = Prefab (Länge 1 entlang lokal Z, Breite 1, wird skaliert) oder Laufzeit-Streifen.
    public class FireTrail : MonoBehaviour
    {
        public const float TickInterval = 0.5f;
        private const float FadeTime = 0.5f;

        private Vector3 _a, _b;
        private float _halfWidth, _dps, _lifetime, _age, _tick;
        private Vector3 _baseScale;
        private readonly List<EnemyBrain> _buffer = new List<EnemyBrain>();

        public static FireTrail Spawn(Vector3 a, Vector3 b, float halfWidth, float lifetime, float dps, GameObject prefab)
        {
            Vector3 dir = b - a;
            dir.y = 0f;
            float length = Mathf.Max(0.1f, dir.magnitude);
            Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            Vector3 mid = (a + b) * 0.5f;

            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, mid, rot);
                go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(halfWidth * 2f, 1f, length));
            }
            else
            {
                go = SuperBuddy.CreatePrimitive(PrimitiveType.Cube, "FireTrail", new Color(1f, 0.45f, 0.05f, 0.8f));
                go.transform.SetPositionAndRotation(mid + Vector3.up * 0.04f, rot);
                go.transform.localScale = new Vector3(halfWidth * 2f, 0.04f, length);
            }
            go.name = "FireTrail";
            var trail = go.AddComponent<FireTrail>();
            trail._a = a;
            trail._b = b;
            trail._halfWidth = halfWidth;
            trail._dps = dps;
            trail._lifetime = Mathf.Max(0.1f, lifetime);
            trail._baseScale = go.transform.localScale;
            return trail;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            _tick += dt;
            if (_tick >= TickInterval && _age < _lifetime && Net.IsServer) // Schaden nur auf dem Server
            {
                _tick -= TickInterval;
                float dmg = _dps * TickInterval;
                _buffer.Clear();
                CollectOnSegment(_a, _b, _halfWidth, _buffer);
                foreach (var e in _buffer)
                    if (e != null && !e.IsDead) e.TakeDamage(dmg);
            }

            float remaining = _lifetime - _age;
            if (remaining <= FadeTime)
                transform.localScale = new Vector3(_baseScale.x * Mathf.Clamp01(remaining / FadeTime), _baseScale.y, _baseScale.z);
            if (_age >= _lifetime) Destroy(gameObject);
        }

        // Gegner mit horizontalem Abstand <= halfWidth zur Strecke a-b
        public static void CollectOnSegment(Vector3 a, Vector3 b, float halfWidth, List<EnemyBrain> result)
        {
            Vector3 mid = (a + b) * 0.5f;
            float reach = Vector3.Distance(a, b) * 0.5f + halfWidth + 1f;
            foreach (var e in CombatUtil.FindEnemies(mid, reach))
            {
                if (e == null || result.Contains(e)) continue;
                if (DistanceToSegment(e.transform.position, a, b) <= halfWidth) result.Add(e);
            }
        }

        public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
