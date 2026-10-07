using UnityEngine;

namespace ElementalBuddies
{
    public class ArcaneBall : MonoBehaviour
    {
        public float Speed = 20f;
        public float Damage = 35f;
        public float Lifetime = 5f;
        [Tooltip("Trefferzone reicht so weit unter die Kugel (Meter, unabhängig von der Prefab-Skalierung) – trifft auch kleine Gegner (Schwarm), über die die Kugel sonst hinwegfliegt.")]
        public float VerticalReach = 1.8f;
        [Tooltip("Mindest-Trefferradius der Kugel in Metern (der Kollider ist durch die Prefab-Skalierung sehr klein).")]
        public float MinHitRadius = 0.45f;
        [Tooltip("Kugelgröße durch Händlerkarten (setzt der MageKit); vergrößert Trefferradius und -zone.")]
        public float SizeFactor = 1f;

        private SphereCollider _sphere;
        private bool _hit;

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
            float size = Mathf.Max(0.1f, SizeFactor);
            float radius = Mathf.Max((_sphere != null ? _sphere.radius : 0.5f) * transform.lossyScale.x, MinHitRadius * size);
            Vector3 top = transform.position;
            Vector3 bottom = top + Vector3.down * VerticalReach * size;
            // Ohne festen Puffer: in der Stadt liegen Boden, Deko und Trigger mit in der Kapsel (Gegner nicht alle auf Ebene "Enemy")
            var hits = Physics.OverlapCapsule(top, bottom, radius, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] != null && hits[i].CompareTag("Enemy"))
                {
                    HitEnemy(hits[i]);
                    return;
                }
            }
        }

        private void HitEnemy(Collider other)
        {
            if (_hit) return;
            _hit = true;
            var damageable = other.GetComponentInParent<IDamageable>(); // Kollider kann am Kind (Visual) hängen
            if (damageable != null && Net.IsServer) // Schaden nur auf dem Server, Clients zeigen den Einschlag
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