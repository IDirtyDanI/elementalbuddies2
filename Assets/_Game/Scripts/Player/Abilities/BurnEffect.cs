using UnityEngine;

namespace ElementalBuddies
{
    // Damage over time added to an enemy by the Flammenwelle. Lives on the enemy, so it dies with it.
    // Mehrspieler: Apply darf überall laufen (Optik, z. B. von Paket B auf Clients nachgespielt); die Spieler-Fähigkeiten
    // rufen es nur auf dem Server auf. Der DoT-Schaden tickt nur auf dem Server.
    public class BurnEffect : MonoBehaviour
    {
        public const float TickInterval = 0.5f;

        private float _dps;
        private float _remaining;
        private float _tickTimer;
        private IDamageable _target;
        private GameObject _vfx;
        private ParticleSystem[] _vfxSystems;
        private static readonly Vector3 VfxOffset = new Vector3(0f, 0.9f, 0f);

        public bool IsActive => enabled && _remaining > 0f;

        // Adds or refreshes the burn (re-applying resets the duration and keeps the higher dps)
        public static BurnEffect Apply(GameObject target, float dps, float duration, GameObject vfxPrefab = null)
        {
            if (target == null || dps <= 0f || duration <= 0f) return null;

            // Nass löscht das Feuer: Nässe verdampft, der Gegner fängt nicht an zu brennen
            var wet = target.GetComponent<WetEffect>();
            if (wet != null && wet.IsActive)
            {
                wet.Stop();
                return null;
            }

            var burn = target.GetComponent<BurnEffect>();
            if (burn == null) burn = target.AddComponent<BurnEffect>();
            burn._target = target.GetComponent<IDamageable>();
            burn._dps = Mathf.Max(burn._remaining > 0f ? burn._dps : 0f, dps);
            burn._remaining = Mathf.Max(burn._remaining, duration);
            burn.enabled = true;
            burn.StartVfx(vfxPrefab);
            return burn;
        }

        // Sofort beenden (z. B. durch Nässe gelöscht)
        public void Stop()
        {
            _remaining = 0f;
            _tickTimer = 0f;
            enabled = false;
        }

        void Update()
        {
            if (_target == null || _remaining <= 0f)
            {
                enabled = false;
                return;
            }

            float dt = Mathf.Min(Time.deltaTime, _remaining);
            _remaining -= dt;
            _tickTimer += dt;

            if (_tickTimer >= TickInterval || _remaining <= 0f)
            {
                float damage = _dps * _tickTimer;
                _tickTimer = 0f;
                // Schaden nur auf dem Server (auf Clients ist der Brand reine Optik)
                if (Net.IsServer) _target.TakeDamage(damage); // may destroy the enemy (and this component) at frame end
            }

            if (_remaining <= 0f) enabled = false;
        }

        // Flammen am Körper, solange der Brand läuft
        private void StartVfx(GameObject prefab)
        {
            if (prefab == null) return;
            if (_vfx == null)
            {
                _vfx = Instantiate(prefab, transform);
                _vfx.transform.position = BodyCenter();
                _vfx.transform.localRotation = Quaternion.identity;
                _vfxSystems = _vfx.GetComponentsInChildren<ParticleSystem>();
            }
            foreach (var ps in _vfxSystems) if (ps != null && !ps.isEmitting) ps.Play(true);
            SetTint(true);
        }

        // Brennende Gegner glühen orange-rot (gemeinsam mit Frost über StatusTint)
        private void SetTint(bool on)
        {
            StatusTint.Refresh(gameObject);
        }

        // Mitte des sichtbaren Körpers (Modelle sind unterschiedlich groß); Fallback: feste Höhe
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
            return found ? new Vector3(transform.position.x, b.center.y - b.extents.y * 0.2f, transform.position.z) : transform.position + VfxOffset;
        }

        void OnDisable()
        {
            // Flammen ausklingen lassen statt hart abzuschneiden
            SetTint(false);
            if (_vfxSystems == null) return;
            foreach (var ps in _vfxSystems) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
