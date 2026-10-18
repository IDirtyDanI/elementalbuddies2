using UnityEngine;

namespace ElementalBuddies
{
    public class ArcaneBall : MonoBehaviour
    {
        public float Speed = 20f;
        public float Damage = 30f;
        public float Lifetime = 5f;
        [Tooltip("Trefferzone reicht so weit unter die Kugel (Meter, unabhängig von der Prefab-Skalierung) – trifft auch kleine Gegner (Schwarm), über die die Kugel sonst hinwegfliegt.")]
        public float VerticalReach = 1.8f;
        [Tooltip("Mindest-Trefferradius der Kugel in Metern (der Kollider ist durch die Prefab-Skalierung sehr klein).")]
        public float MinHitRadius = 0.45f;
        [Tooltip("Kugelgröße durch Händlerkarten (setzt der MageKit); vergrößert Trefferradius und -zone.")]
        public float SizeFactor = 1f;
        [Tooltip("Arkaner Splitterschaden am Einschlag: Anteil des Treffer-Schadens an allen anderen Gegnern im Splitter-Radius (0,4 = 40 %).")]
        [Range(0f, 1f)] public float SplashFraction = 0.4f;
        [Tooltip("Splitter-Radius in Metern um den getroffenen Gegner (wächst mit der Kugelgröße).")]
        public float SplashRadius = 1.6f;
        [Tooltip("Optionaler Einschlag-Effekt (nur bei Gegnertreffer): wird am Einschlag gespawnt, nach 2 s zerstört und auf " +
                 "Splitter-Radius × Kugelgröße skaliert – das Prefab also für 1 m Radius bauen.")]
        public GameObject ImpactVfx;
        [Tooltip("Kurzer Blitz am Abschusspunkt beim Wirken (MageKit.CastArcaneBall, alle Rechner) – wächst mit der Kugelgröße.")]
        public GameObject MuzzleVfx;

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
            Vector3 impact = other.bounds.center; // Splitter um den getroffenen Gegner (die Kugel fliegt evtl. über ihn hinweg)
            float splashRadius = SplashRadius * Mathf.Max(0.1f, SizeFactor);
            if (damageable != null && Net.IsServer) // Schaden nur auf dem Server, Clients zeigen den Einschlag
            {
                // Quelle Spieler (Telemetrie); den Treffer-Effekt zeigt der Einschlag (ImpactVfx) auf allen Rechnern selbst
                EnemyBrain.DealPlayerDamage(damageable, Damage, AbilityId.ArcaneBall, hitFx: false);
                DealSplash(impact, splashRadius, other.GetComponentInParent<EnemyBrain>());
            }
            SpawnImpactVfx(impact, splashRadius);
            GameAudio.Play(SfxId.ArcaneBallHit, transform.position);
            Destroy(gameObject);
        }

        // Splitterschaden (nur Server): Anteil des Treffer-Schadens an allen anderen Gegnern im Radius
        private void DealSplash(Vector3 center, float radius, EnemyBrain primary)
        {
            float splash = Damage * SplashFraction;
            if (splash <= 0f || radius <= 0f) return;
            foreach (var enemy in CombatUtil.FindEnemies(center, radius))
            {
                if (enemy == null || enemy == primary) continue;
                EnemyBrain.DealPlayerDamage(enemy, splash, AbilityId.ArcaneBall); // Splitter: kleiner Arkan-Treffer am Gegner
            }
        }

        // Einschlag-Effekt auf allen Rechnern (rein optisch)
        private void SpawnImpactVfx(Vector3 position, float radius)
        {
            if (ImpactVfx == null) return;
            var vfx = Instantiate(ImpactVfx, position, Quaternion.identity);
            vfx.transform.localScale = ImpactVfx.transform.localScale * radius;
            Destroy(vfx, 2f);
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