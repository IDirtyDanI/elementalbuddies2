using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Pfeil des Bogenschützen: fliegt geradeaus, prüft per SphereCast pro Schritt (keine verpassten Treffer
    // bei hoher Geschwindigkeit). Optional durchschlagend (Frostpfeil). Spieler, Buddies und Trigger werden ignoriert.
    public class ArrowProjectile : MonoBehaviour
    {
        public float Speed = 34f;
        public float Damage = 22f;
        public float MaxDistance = 24f;
        public float HitRadius = 0.3f;
        [Tooltip("Durchschlägt Gegner (trifft jeden höchstens einmal).")]
        public bool Pierce = false;
        [Tooltip("Ohne Pierce: so viele Gegner durchschlägt der Pfeil zusätzlich, bevor er stecken bleibt (Händlerkarte).")]
        public int ExtraPierce = 0;
        [Tooltip("Funken beim Treffer (optional).")]
        public GameObject HitFxPrefab;
        [Tooltip("Wird beim Aufprall vom Pfeil gelöst und darf ausklingen (z. B. Trail, Partikel-Schweif).")]
        public Transform DetachOnEnd;
        public ChampionSfx HitSfx;

        // Zusatzwirkung pro getroffenem Gegner (vor dem Schaden), z. B. Einfrieren
        public System.Action<EnemyBrain> OnHitEnemy;

        public float Travelled { get; private set; }
        public int HitCount { get; private set; }

        private readonly HashSet<EnemyBrain> _hit = new HashSet<EnemyBrain>();
        private bool _done;

        void Update()
        {
            Advance(Time.deltaTime);
        }

        // Öffentlich, damit Tests den Flug synchron simulieren können
        public void Advance(float dt)
        {
            if (_done) return;
            float step = Speed * dt;
            if (Travelled + step > MaxDistance) step = Mathf.Max(0f, MaxDistance - Travelled);
            Vector3 dir = transform.forward;
            Vector3 from = transform.position;

            var hits = Physics.SphereCastAll(from, HitRadius, dir, step, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var col = h.collider;
                if (col == null) continue;
                if (col.CompareTag("Player") || col.CompareTag("Buddy")) continue;
                if (col.GetComponentInParent<PlayerStats>() != null) continue;

                var enemy = col.GetComponentInParent<EnemyBrain>();
                if (enemy != null)
                {
                    if (!_hit.Add(enemy)) continue;
                    Vector3 point = h.point == Vector3.zero ? enemy.transform.position + Vector3.up : h.point;
                    HitEnemy(enemy, point);
                    if (!Pierce && HitCount > ExtraPierce)
                    {
                        transform.position = from + dir * h.distance;
                        End();
                        return;
                    }
                    continue;
                }

                // Wand / Hindernis (keine Trigger, kein Boden-Streifschuss direkt am Start)
                if (!col.isTrigger && h.distance > 0.01f && col.GetComponentInParent<ElementalBuddy>() == null)
                {
                    transform.position = from + dir * h.distance;
                    End();
                    return;
                }
            }

            transform.position = from + dir * step;
            Travelled += step;
            if (Travelled >= MaxDistance - 0.001f) End();
        }

        private void HitEnemy(EnemyBrain enemy, Vector3 point)
        {
            HitCount++;
            if (HitFxPrefab != null) CombatUtil.SpawnFx(HitFxPrefab, point, Quaternion.LookRotation(-transform.forward), 1.5f);
            if (HitSfx != null) HitSfx.Play(point);
            if (OnHitEnemy != null) OnHitEnemy(enemy);
            if (enemy != null) enemy.TakeDamage(Damage);
        }

        private void End()
        {
            if (_done) return;
            _done = true;
            if (DetachOnEnd != null)
            {
                DetachOnEnd.SetParent(null, true);
                foreach (var ps in DetachOnEnd.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(DetachOnEnd.gameObject, 1.2f);
            }
            Destroy(gameObject);
        }
    }
}
