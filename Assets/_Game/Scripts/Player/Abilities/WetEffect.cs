using UnityEngine;

namespace ElementalBuddies
{
    // Nass: keine eigene Wirkung, aber Synergien (Frost hält doppelt so lange, Blitz-Bonus beim Buddy).
    // Nass und Brand heben sich auf. Optik: tropfendes Wasser am Körper + bläuliche Färbung. Lebt auf dem Gegner.
    public class WetEffect : MonoBehaviour
    {
        private float _remaining;
        private GameObject _vfx;
        private ParticleSystem[] _vfxSystems;

        public bool IsActive => enabled && _remaining > 0f;

        // Fügt Nässe hinzu oder verlängert sie (die längere Restdauer gewinnt). Löscht einen laufenden Brand.
        public static WetEffect Apply(GameObject target, float duration, GameObject vfxPrefab = null)
        {
            if (target == null || duration <= 0f) return null;

            var burn = target.GetComponent<BurnEffect>();
            if (burn != null && burn.IsActive) burn.Stop();

            var wet = target.GetComponent<WetEffect>();
            if (wet == null) wet = target.AddComponent<WetEffect>();
            wet._remaining = Mathf.Max(wet.IsActive ? wet._remaining : 0f, duration);
            wet.enabled = true;
            wet.Begin(vfxPrefab);
            return wet;
        }

        // Sofort beenden (z. B. durch Feuer verdampft)
        public void Stop()
        {
            _remaining = 0f;
            enabled = false;
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
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f) enabled = false;
        }

        void OnDisable()
        {
            _remaining = 0f;
            StatusTint.Refresh(gameObject);
            // Tropfen klingen aus statt hart abzuschneiden
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
