using UnityEngine;

namespace ElementalBuddies
{
    public class ArcaneBall : MonoBehaviour
    {
        public float Speed = 20f;
        public float Damage = 35f;
        public float Lifetime = 5f;
        [Tooltip("Trefferzone reicht so weit unter die Kugel (m, × Kugelgröße) – trifft auch kleine Gegner (Schwarm), über die die Kugel sonst hinwegfliegt.")]
        public float VerticalReach = 1.4f;

        private SphereCollider _sphere;
        private bool _hit;
        private static readonly Collider[] _overlap = new Collider[16];

        void Start()
        {
            _sphere = GetComponent<SphereCollider>();
            Destroy(gameObject, Lifetime);
        }

        void Update()
        {
            transform.Translate(Vector3.forward * Speed * Time.deltaTime);
            CheckBelow();
        }

        // Kapsel von der Kugel nach unten: kleine Gegner unter der Flugbahn werden ebenfalls getroffen
        private void CheckBelow()
        {
            if (_hit || VerticalReach <= 0f) return;
            float scale = transform.lossyScale.x;
            float radius = (_sphere != null ? _sphere.radius : 0.5f) * scale;
            Vector3 top = transform.position;
            Vector3 bottom = top + Vector3.down * VerticalReach * scale;
            int n = Physics.OverlapCapsuleNonAlloc(top, bottom, radius, _overlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                if (_overlap[i] != null && _overlap[i].CompareTag("Enemy"))
                {
                    HitEnemy(_overlap[i]);
                    return;
                }
            }
        }

        private void HitEnemy(Collider other)
        {
            if (_hit) return;
            _hit = true;
            var damageable = other.GetComponent<IDamageable>();
            if (damageable != null)
            {
                EnemyBrain.DealPlayerDamage(damageable, Damage); // Quelle Spieler (Telemetrie)
            }
            GameAudio.Play(SfxId.ArcaneBallHit, transform.position);
            Destroy(gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            if (_hit) return;
            if (other.CompareTag("Player") || other.CompareTag("Buddy")) return; // Ignore Player and Buddies

            if (other.CompareTag("Enemy"))
            {
                HitEnemy(other);
            }
            else if (!other.isTrigger) // Hit a wall or obstacle
            {
                Destroy(gameObject);
            }
        }
    }
}