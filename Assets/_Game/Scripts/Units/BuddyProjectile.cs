using UnityEngine;

namespace ElementalBuddies
{
    public class BuddyProjectile : MonoBehaviour
    {
        private Transform _target;
        private float _damage;
        private UnitType _type;
        public float Speed = 15f;

        public void Initialize(Transform target, float damage, UnitType type)
        {
            _target = target;
            _damage = damage;
            _type = type;
            Destroy(gameObject, 5f);
        }

        void Update()
        {
            if (_target == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 dir = (_target.position - transform.position).normalized;
            transform.Translate(dir * Speed * Time.deltaTime, Space.World);
            transform.LookAt(_target);
        }
        
        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Enemy"))
            {
                var dmg = other.GetComponent<IDamageable>();
                if (dmg != null) dmg.TakeDamage(_damage);
                
                if (_type == UnitType.Ice)
                {
                    var slowable = other.GetComponent<ISlowable>();
                    if (slowable != null) slowable.ApplySlow(0.25f, 2.5f);
                }
                
                Destroy(gameObject);
            }
        }
    }
}