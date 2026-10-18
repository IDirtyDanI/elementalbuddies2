using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Pfeil des Bogenschützen: fliegt geradeaus, prüft per SphereCast pro Schritt (keine verpassten Treffer
    // bei hoher Geschwindigkeit). Optional durchschlagend (Frostpfeil). Spieler, Buddies und Trigger werden ignoriert.
    // Gegner werden mit einer senkrechten Kapsel (Pfeilhöhe bis LowReach darunter) gesucht, damit auch flache Gegner
    // (Grabspinne, Collider-Oberkante ~0,95 m) getroffen werden, obwohl der Pfeil auf ~1,3 m fliegt (#164).
    // Wände/Hindernisse stoppen den Pfeil weiterhin nur auf Pfeilhöhe (kein Hängenbleiben an Boden oder Bordsteinen).
    public class ArrowProjectile : MonoBehaviour
    {
        public float Speed = 34f;
        public float Damage = 22f;
        public float MaxDistance = 24f;
        public float HitRadius = 0.3f;
        [Tooltip("Gegner-Trefferzone reicht so weit unter die Flughöhe (flache Gegner wie die Grabspinne).")]
        public float LowReach = 1.0f;
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
        // Fähigkeit dieses Pfeils (Treffer-Effekt je Fähigkeit, HitVfx): Pfeilschuss oder Frostpfeil
        [System.NonSerialized] public AbilityId Ability = AbilityId.ArrowShot;

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

            // 1) Wände/Hindernisse auf Pfeilhöhe: kürzeste Blockade-Distanz im Schritt
            float block = float.MaxValue;
            foreach (var h in Physics.SphereCastAll(from, HitRadius, dir, step, ~0, QueryTriggerInteraction.Ignore))
            {
                var col = h.collider;
                if (col == null || h.distance <= 0.01f) continue; // kein Boden-Streifschuss direkt am Start
                if (col.CompareTag("Player") || col.CompareTag("Buddy")) continue;
                if (col.GetComponentInParent<PlayerStats>() != null || col.GetComponentInParent<EnemyBrain>() != null
                    || col.GetComponentInParent<ElementalBuddy>() != null) continue;
                if (h.distance < block) block = h.distance;
            }

            // 2) Gegner in der senkrechten Kapsel (Flughöhe bis LowReach darunter), nur vor der Blockade
            Vector3 low = from - Vector3.up * Mathf.Max(0f, LowReach);
            var hits = Physics.CapsuleCastAll(from, low, HitRadius, dir, step, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var col = h.collider;
                if (col == null || h.distance > block) continue;
                if (col.CompareTag("Player") || col.CompareTag("Buddy")) continue;
                var enemy = col.GetComponentInParent<EnemyBrain>();
                if (enemy == null || !_hit.Add(enemy)) continue;
                // Startüberlappung liefert point = 0 → Mitte des Gegners
                Vector3 point = h.point == Vector3.zero ? col.bounds.center : h.point;
                HitEnemy(enemy, point);
                if (!Pierce && HitCount > ExtraPierce)
                {
                    transform.position = from + dir * h.distance;
                    End();
                    return;
                }
            }

            if (block <= step)
            {
                transform.position = from + dir * block;
                End();
                return;
            }

            transform.position = from + dir * step;
            Travelled += step;
            if (Travelled >= MaxDistance - 0.001f) End();
        }

        private void HitEnemy(EnemyBrain enemy, Vector3 point)
        {
            HitCount++;
            // Treffer-Effekt der Fähigkeit am Gegner (HitVfx-Tabelle, sonst HitFxPrefab) – auf allen Rechnern
            HitVfx.Show(enemy, Ability, transform.forward, 1f, HitFxPrefab);
            if (HitSfx != null) HitSfx.Play(point);
            if (!Net.IsServer) return; // Clients: nur Optik (Funken, Laut, Pfeil bleibt stecken)
            if (OnHitEnemy != null) using (EnemyBrain.PlayerSource()) OnHitEnemy(enemy);
            if (enemy != null) EnemyBrain.DealPlayerDamage(enemy, Damage, Ability, hitFx: false); // Quelle Spieler (Telemetrie)
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
