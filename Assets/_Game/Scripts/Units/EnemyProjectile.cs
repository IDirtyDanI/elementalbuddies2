using UnityEngine;

namespace ElementalBuddies
{
    // Gegner-Projektil (Fernkämpfer): fliegt kinematisch mit leichtem Homing aufs Ziel (Spieler, Buddy, Nexus …),
    // Schaden beim Ankommen. Stirbt das Ziel unterwegs, fliegt es zur letzten bekannten Position und verpufft. Vorwärts = +Z.
    public class EnemyProjectile : MonoBehaviour
    {
        [Tooltip("Optional: Effekt beim Einschlag (2 s).")]
        public GameObject HitEffectPrefab;
        public float Lifetime = 4f;
        public float HitDistance = 0.4f;

        private Transform _target;
        private Object _targetObject; // Unity-Null-Check für zerstörte Ziele
        private ElementalBuddy _buddy;
        private bool _isNexus;
        private Vector3 _aimOffset;
        private Vector3 _lastAim;
        private Vector3 _sourcePos;
        private float _damage;
        private float _speed;
        private float _dieAt;
        private bool _initialized;

        public void Init(Transform target, Vector3 aimPointOffset, float damage, float speed, Vector3 sourcePos)
        {
            _target = target;
            _targetObject = target;
            _aimOffset = aimPointOffset;
            _damage = damage;
            _speed = Mathf.Max(0.1f, speed);
            _sourcePos = sourcePos;
            _isNexus = target != null && Nexus.Instance != null && target == Nexus.Instance.transform;
            _buddy = target != null ? target.GetComponent<ElementalBuddy>() : null;
            _dieAt = Time.time + Lifetime;
            _lastAim = target != null ? AimPoint() : transform.position + transform.forward * 5f;
            _initialized = true;
        }

        private bool TargetAlive =>
            _targetObject != null && _target.gameObject.activeInHierarchy && (_buddy == null || !_buddy.IsDead);

        private Vector3 AimPoint()
        {
            // Nexus: Oberfläche statt Pivot (groß)
            if (_isNexus && Nexus.Instance != null)
                return Nexus.Instance.GetClosestPoint(transform.position) + Vector3.up * _aimOffset.y;
            return _target.position + _aimOffset;
        }

        void Update()
        {
            if (!_initialized || Time.time >= _dieAt)
            {
                Destroy(gameObject);
                return;
            }

            bool alive = TargetAlive;
            if (alive) _lastAim = AimPoint();

            Vector3 to = _lastAim - transform.position;
            float step = _speed * Time.deltaTime;
            if (to.magnitude <= Mathf.Max(HitDistance, step))
            {
                transform.position = _lastAim;
                if (alive) Hit();
                Destroy(gameObject);
                return;
            }

            Vector3 dir = to.normalized;
            transform.position += dir * step;
            transform.rotation = Quaternion.LookRotation(dir);
        }

        private void Hit()
        {
            var directional = _target.GetComponent<IDirectionalDamageable>();
            if (directional != null) directional.TakeDamage(_damage, _sourcePos);
            else
            {
                var dmg = _target.GetComponent<IDamageable>();
                if (dmg != null) dmg.TakeDamage(_damage);
            }
            if (HitEffectPrefab != null) Destroy(Instantiate(HitEffectPrefab, transform.position, Quaternion.identity), 2f);
        }
    }
}
