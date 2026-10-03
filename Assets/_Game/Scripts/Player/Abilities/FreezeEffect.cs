using UnityEngine;

namespace ElementalBuddies
{
    // Eingefroren (Frostnova): Gegner steht still, greift nicht an, Animation stoppt.
    // Optik: Eiskristall-VFX am Körper + eisblaue Färbung. Lebt auf dem Gegner.
    public class FreezeEffect : MonoBehaviour
    {
        private float _remaining;
        private GameObject _vfx;
        private ParticleSystem[] _vfxSystems;
        private Animator[] _animators;

        public bool IsActive => enabled && _remaining > 0f;

        // Fügt den Frost hinzu oder verlängert ihn (die längere Restdauer gewinnt)
        public static FreezeEffect Apply(GameObject target, float duration, GameObject vfxPrefab = null)
        {
            if (target == null || duration <= 0f) return null;

            var freeze = target.GetComponent<FreezeEffect>();
            if (freeze == null) freeze = target.AddComponent<FreezeEffect>();
            freeze._remaining = Mathf.Max(freeze.IsActive ? freeze._remaining : 0f, duration);
            freeze.enabled = true;
            freeze.Begin(vfxPrefab);
            return freeze;
        }

        private void Begin(GameObject prefab)
        {
            if (_animators == null) _animators = GetComponentsInChildren<Animator>();
            foreach (var a in _animators) if (a != null) a.speed = 0f;

            if (prefab != null)
            {
                if (_vfx == null)
                {
                    _vfx = Instantiate(prefab, transform);
                    _vfx.transform.position = BodyCenter();
                    _vfx.transform.localRotation = Quaternion.identity;
                    _vfxSystems = _vfx.GetComponentsInChildren<ParticleSystem>();
                }
                foreach (var l in _vfx.GetComponentsInChildren<Light>(true)) l.enabled = true;
                foreach (var ps in _vfxSystems)
                {
                    if (ps == null || ps.name == "Shatter") continue;
                    if (ps.name == "Crystals") ps.Clear(false);
                    ps.Play(false);
                }
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
            if (_animators != null) foreach (var a in _animators) if (a != null) a.speed = 1f;
            StatusTint.Refresh(gameObject);
            if (_vfxSystems == null) return;
            if (_vfx != null) foreach (var l in _vfx.GetComponentsInChildren<Light>(true)) l.enabled = false;
            // Kristalle zerspringen (Shatter-Burst), Glitzern und Nebel klingen aus
            foreach (var ps in _vfxSystems)
            {
                if (ps == null || ps.name == "Shatter") continue;
                ps.Stop(false, ps.name == "Crystals" ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
            }
            var shatter = _vfx != null ? _vfx.transform.Find("Shatter") : null;
            if (shatter != null)
            {
                var sps = shatter.GetComponent<ParticleSystem>();
                if (sps != null) sps.Play(true);
            }
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
            return found ? new Vector3(transform.position.x, b.center.y - b.extents.y * 0.1f, transform.position.z) : transform.position + Vector3.up * 0.9f;
        }
    }
}
