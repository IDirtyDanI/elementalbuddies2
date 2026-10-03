using UnityEngine;

namespace ElementalBuddies
{
    // Damage over time added to an enemy by the Flammenwelle. Lives on the enemy, so it dies with it.
    public class BurnEffect : MonoBehaviour
    {
        public const float TickInterval = 0.5f;

        private float _dps;
        private float _remaining;
        private float _tickTimer;
        private IDamageable _target;

        // Adds or refreshes the burn (re-applying resets the duration and keeps the higher dps)
        public static BurnEffect Apply(GameObject target, float dps, float duration)
        {
            if (target == null || dps <= 0f || duration <= 0f) return null;

            var burn = target.GetComponent<BurnEffect>();
            if (burn == null) burn = target.AddComponent<BurnEffect>();
            burn._target = target.GetComponent<IDamageable>();
            burn._dps = Mathf.Max(burn._remaining > 0f ? burn._dps : 0f, dps);
            burn._remaining = Mathf.Max(burn._remaining, duration);
            burn.enabled = true;
            return burn;
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
                _target.TakeDamage(damage); // may destroy the enemy (and this component) at frame end
            }

            if (_remaining <= 0f) enabled = false;
        }
    }
}
