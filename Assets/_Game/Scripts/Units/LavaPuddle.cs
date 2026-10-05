using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Lavapfütze des Magma-Buddys: eigenständiges Objekt (läuft weiter, auch wenn der Buddy stirbt).
    // Tickt alle TickInterval Sekunden: Schaden (Dps × Intervall) + leichte Verlangsamung für Gegner im Radius.
    public class LavaPuddle : MonoBehaviour
    {
        public const float TickInterval = 0.5f;
        private const float FadeTime = 0.5f;

        private float _bossMultiplier = 1f;
        private float _radius, _dps, _slow, _slowDuration, _lifetime, _age, _tickTimer;
        private Vector3 _baseScale;
        private ParticleSystem[] _particles;
        private bool _particlesStopped;

        public bool IsExpired => _age >= _lifetime;

        private static Material _runtimeMaterial;
        private static readonly List<EnemyBrain> _buffer = new List<EnemyBrain>();

        // Gemeinsames Laufzeit-Material (orange glühend, unbeleuchtet)
        public static Material RuntimeLavaMaterial
        {
            get
            {
                if (_runtimeMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        var color = new Color(1f, 0.35f, 0.05f);
                        _runtimeMaterial = new Material(shader) { name = "Lava (Runtime)", color = color };
                        if (_runtimeMaterial.HasProperty("_BaseColor")) _runtimeMaterial.SetColor("_BaseColor", color);
                    }
                }
                return _runtimeMaterial;
            }
        }

        // Pfütze erzeugen: Prefab (Einheitsradius 1, wird auf radius skaliert) oder Laufzeit-Scheibe
        // bossMultiplier: Schadensfaktor gegen Bosse (Magma-Fusion, FusionBuddy.BossDamageMultiplier)
        public static LavaPuddle Spawn(Vector3 groundPos, float radius, float lifetime, float dps, float slow, float slowDuration,
            GameObject prefab = null, float bossMultiplier = 1f)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, groundPos, Quaternion.identity);
                go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(radius, 1f, radius));
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var rend = go.GetComponent<MeshRenderer>();
                rend.sharedMaterial = RuntimeLavaMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                go.transform.position = groundPos + Vector3.up * 0.03f;
                go.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f); // Zylinder: Durchmesser 1, Höhe 2
            }
            go.name = "LavaPuddle";

            var puddle = go.AddComponent<LavaPuddle>();
            puddle._radius = radius;
            puddle._bossMultiplier = bossMultiplier > 0f ? bossMultiplier : 1f;
            puddle._lifetime = Mathf.Max(0.1f, lifetime);
            puddle._dps = dps;
            puddle._slow = slow;
            puddle._slowDuration = slowDuration;
            puddle._baseScale = go.transform.localScale;
            puddle._particles = go.GetComponentsInChildren<ParticleSystem>();
            return puddle;
        }

        // Vorzeitig ausklingen lassen (z. B. wenn das Pfützen-Limit erreicht ist)
        public void Expire()
        {
            _age = Mathf.Max(_age, _lifetime - FadeTime);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            _tickTimer += dt;

            if (_tickTimer >= TickInterval && !IsExpired)
            {
                _tickTimer -= TickInterval;
                Tick(TickInterval);
            }

            // Letzte FadeTime Sekunden: schrumpfen, Partikel stoppen
            float remaining = _lifetime - _age;
            if (remaining <= FadeTime)
            {
                float k = Mathf.Clamp01(remaining / FadeTime);
                transform.localScale = new Vector3(_baseScale.x * k, _baseScale.y, _baseScale.z * k);
                if (!_particlesStopped && _particles != null)
                {
                    _particlesStopped = true;
                    foreach (var ps in _particles) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            if (_age >= _lifetime) Destroy(gameObject);
        }

        private void Tick(float interval)
        {
            float damage = _dps * interval;
            _buffer.Clear();
            CollectEnemies(transform.position, _radius, _buffer);
            foreach (var e in _buffer)
            {
                if (e == null || e.CurrentHP <= 0f) continue;
                if (_slow > 0f) e.ApplySlow(_slow, _slowDuration);
                if (damage > 0f) e.TakeDamage(e.IsBoss ? damage * _bossMultiplier : damage);
            }
        }

        // Gegner (Tag "Enemy", EnemyBrain) mit horizontalem Abstand <= radius; hängt an result an (ohne Duplikate)
        public static void CollectEnemies(Vector3 center, float radius, List<EnemyBrain> result)
        {
            if (radius <= 0f) return;
            float r2 = radius * radius;
            foreach (var hit in Physics.OverlapSphere(center, radius + 1f))
            {
                if (!hit.CompareTag("Enemy")) continue;
                var e = hit.GetComponentInParent<EnemyBrain>();
                if (e == null || !e.isActiveAndEnabled || result.Contains(e)) continue;
                Vector3 d = e.transform.position - center;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) result.Add(e);
            }
        }
    }
}
