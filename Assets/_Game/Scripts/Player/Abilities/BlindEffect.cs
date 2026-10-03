using UnityEngine;

namespace ElementalBuddies
{
    // Geblendet (Heiliger Kreis): rein visuell – kreisende Sterne über dem Kopf + heller Goldschimmer.
    // Die Verlangsamung selbst läuft über EnemyBrain.ApplySlow. Lebt auf dem Gegner.
    public class BlindEffect : MonoBehaviour
    {
        private const float HeadClearance = -0.1f; // Renderer-Bounds sind meist etwas größer als der Kopf

        private float _remaining;
        private GameObject _vfx;
        private ParticleSystem[] _vfxSystems;
        private Light[] _lights;

        public bool IsActive => enabled && _remaining > 0f;

        // Fügt die Blendung hinzu oder verlängert sie (die längere Restdauer gewinnt)
        public static BlindEffect Apply(GameObject target, float duration, GameObject vfxPrefab = null)
        {
            if (target == null || duration <= 0f) return null;

            var blind = target.GetComponent<BlindEffect>();
            if (blind == null) blind = target.AddComponent<BlindEffect>();
            blind._remaining = Mathf.Max(blind.IsActive ? blind._remaining : 0f, duration);
            blind.enabled = true;
            blind.Begin(vfxPrefab);
            return blind;
        }

        private void Begin(GameObject prefab)
        {
            if (prefab != null)
            {
                if (_vfx == null)
                {
                    _vfx = Instantiate(prefab, transform);
                    _vfx.transform.position = HeadTop();
                    _vfx.transform.rotation = Quaternion.identity;
                    _vfxSystems = _vfx.GetComponentsInChildren<ParticleSystem>();
                    _lights = _vfx.GetComponentsInChildren<Light>(true);
                }
                foreach (var l in _lights) if (l != null) l.enabled = true;
                foreach (var ps in _vfxSystems)
                {
                    if (ps == null) continue;
                    // Sterne sind ein einmaliger Burst, der bis zum Ende stehen bleibt → nur neu starten, wenn weg
                    bool running = ps.name == "Stars" ? ps.isPlaying : ps.isEmitting;
                    if (!running) ps.Play(false);
                }
            }
            StatusTint.Refresh(gameObject);
        }

        void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f) enabled = false;
        }

        void LateUpdate()
        {
            // Sterne bleiben waagerecht über dem Kopf, auch wenn sich der Gegner dreht
            if (_vfx != null) _vfx.transform.rotation = Quaternion.identity;
        }

        void OnDisable()
        {
            _remaining = 0f;
            StatusTint.Refresh(gameObject);
            if (_lights != null) foreach (var l in _lights) if (l != null) l.enabled = false;
            if (_vfxSystems == null) return;
            // Sterne verschwinden sofort, der Glitzerstaub klingt aus
            foreach (var ps in _vfxSystems)
                if (ps != null) ps.Stop(false, ps.name == "Stars" ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }

        private Vector3 HeadTop()
        {
            bool found = false;
            Bounds b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return found ? new Vector3(transform.position.x, b.max.y + HeadClearance, transform.position.z) : transform.position + Vector3.up * 2f;
        }
    }
}
