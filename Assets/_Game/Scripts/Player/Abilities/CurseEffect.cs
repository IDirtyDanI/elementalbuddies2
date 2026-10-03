using UnityEngine;

namespace ElementalBuddies
{
    // Verflucht: Schaden über Zeit + der Gegner nimmt mehr Schaden und seine Rüstung zählt nicht (siehe EnemyBrain.TakeDamage).
    // Optik: violette Aura am Körper + violette Färbung. Lebt auf dem Gegner.
    public class CurseEffect : MonoBehaviour
    {
        public const float TickInterval = 0.5f;

        private float _dps;
        private float _remaining;
        private float _tickTimer;
        private float _damageTakenBonus;
        private IDamageable _target;
        private GameObject _vfx;
        private ParticleSystem[] _vfxSystems;

        public bool IsActive => enabled && _remaining > 0f;
        public float DamageTakenBonus => IsActive ? _damageTakenBonus : 0f;

        // Fügt den Fluch hinzu oder frischt ihn auf (längere Restdauer, höherer DPS und höherer Bonus gewinnen)
        public static CurseEffect Apply(GameObject target, float dps, float duration, float damageTakenBonus, GameObject vfxPrefab = null)
        {
            if (target == null || duration <= 0f) return null;

            var curse = target.GetComponent<CurseEffect>();
            if (curse == null) curse = target.AddComponent<CurseEffect>();
            bool active = curse.IsActive;
            curse._target = target.GetComponent<IDamageable>();
            curse._dps = Mathf.Max(active ? curse._dps : 0f, Mathf.Max(0f, dps));
            curse._damageTakenBonus = Mathf.Max(active ? curse._damageTakenBonus : 0f, Mathf.Max(0f, damageTakenBonus));
            curse._remaining = Mathf.Max(active ? curse._remaining : 0f, duration);
            if (!active) curse._tickTimer = 0f;
            curse.enabled = true;
            curse.Begin(vfxPrefab);
            return curse;
        }

        private void Begin(GameObject prefab)
        {
            if (prefab != null)
            {
                if (_vfx == null)
                {
                    _vfx = Instantiate(prefab, transform);
                    _vfx.transform.position = BodyCenter();
                    _vfx.transform.localRotation = Quaternion.identity;
                    _vfxSystems = _vfx.GetComponentsInChildren<ParticleSystem>();
                }
                foreach (var ps in _vfxSystems) if (ps != null && !ps.isEmitting) ps.Play(false);
            }
            StatusTint.Refresh(gameObject);
        }

        void Update()
        {
            if (_remaining <= 0f)
            {
                enabled = false;
                return;
            }

            float dt = Mathf.Min(Time.deltaTime, _remaining);
            _remaining -= dt;
            _tickTimer += dt;

            if (_target != null && _dps > 0f && (_tickTimer >= TickInterval || _remaining <= 0f))
            {
                float damage = _dps * _tickTimer;
                _tickTimer = 0f;
                _target.TakeDamage(damage); // Fluch-Bonus gilt auch für den eigenen DoT; kann den Gegner zerstören
            }

            if (_remaining <= 0f) enabled = false;
        }

        void OnDisable()
        {
            _remaining = 0f;
            StatusTint.Refresh(gameObject);
            if (_vfxSystems == null) return;
            foreach (var ps in _vfxSystems) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private Vector3 BodyCenter()
        {
            bool found = false;
            Bounds b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return found ? new Vector3(transform.position.x, b.center.y, transform.position.z) : transform.position + Vector3.up * 0.9f;
        }
    }
}
