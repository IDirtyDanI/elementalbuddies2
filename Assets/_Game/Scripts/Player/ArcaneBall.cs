using UnityEngine;

namespace ElementalBuddies
{
    public class ArcaneBall : MonoBehaviour
    {
        public float Speed = 20f;
        public float Damage = 35f;
        public float Lifetime = 5f;

        void Start()
        {
            Destroy(gameObject, Lifetime);
        }

        void Update()
        {
            transform.Translate(Vector3.forward * Speed * Time.deltaTime);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") || other.CompareTag("Buddy")) return; // Ignore Player and Buddies

            if (other.CompareTag("Enemy"))
            {
                var damageable = other.GetComponent<IDamageable>();
                if (damageable != null)
                {
                    damageable.TakeDamage(Damage);
                }
                Destroy(gameObject);
            }
            else if (!other.isTrigger) // Hit a wall or obstacle
            {
                Destroy(gameObject);
            }
        }
    }
}