using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    public class BuddyProjectile : MonoBehaviour
    {
        private Transform _target;
        private float _damage;
        private UnitType _type;
        public float Speed = 15f;

        // Stufe-4-Perks (über ShooterBuddy gesetzt)
        [Tooltip("Schadensfaktor pro Durchschuss (Feuer Stufe 4).")]
        public float PierceDamageFactor = 0.5f;
        private int _pierceLeft;
        private float _pierceRange, _burnDps, _burnDuration, _freezeDuration;
        private GameObject _burnVfx, _freezeVfx;
        private readonly List<EnemyBrain> _hit = new List<EnemyBrain>();

        public void Initialize(Transform target, float damage, UnitType type)
        {
            _target = target;
            _damage = damage;
            _type = type;
            Destroy(gameObject, 5f);
        }

        // Durchschlag: nach einem Treffer weiter zum nächsten Gegner (bevorzugt dahinter) + Brand auf jedem Treffer
        public void SetPierce(int count, float range, float burnDps, float burnDuration, GameObject burnVfx)
        {
            _pierceLeft = Mathf.Max(0, count);
            _pierceRange = range;
            _burnDps = burnDps;
            _burnDuration = burnDuration;
            _burnVfx = burnVfx;
        }

        // Einfrieren beim (ersten) Treffer
        public void SetFreeze(float duration, GameObject vfx)
        {
            _freezeDuration = duration;
            _freezeVfx = vfx;
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
            if (!other.CompareTag("Enemy")) return;
            var enemy = other.GetComponentInParent<EnemyBrain>();
            if (enemy != null && _hit.Contains(enemy)) return; // durchschlagener Gegner nicht doppelt

            Vector3 travel = _target != null ? _target.position - transform.position : transform.forward;
            Vector3 hitPos = other.transform.position;

            if (_burnDps > 0f) BurnEffect.Apply(other.gameObject, _burnDps, _burnDuration, _burnVfx);
            if (_freezeDuration > 0f && enemy != null)
            {
                enemy.Freeze(_freezeDuration, _freezeVfx);
                _freezeDuration = 0f;
            }

            var dmg = other.GetComponent<IDamageable>();
            if (dmg != null) dmg.TakeDamage(_damage);

            if (_type == UnitType.Ice)
            {
                var slowable = other.GetComponent<ISlowable>();
                if (slowable != null) slowable.ApplySlow(0.25f, 2.5f);
            }

            if (enemy != null) _hit.Add(enemy);
            if (_pierceLeft > 0)
            {
                var next = FindPierceTarget(hitPos, travel);
                if (next != null)
                {
                    _pierceLeft--;
                    _damage *= PierceDamageFactor; // Durchschuss: Folgetreffer schwächer
                    _target = next.transform;
                    return;
                }
            }
            Destroy(gameObject);
        }

        // Nächster noch nicht getroffener Gegner im Radius; Gegner hinter dem Treffer (in Flugrichtung) haben Vorrang
        private EnemyBrain FindPierceTarget(Vector3 from, Vector3 travel)
        {
            travel.y = 0f;
            EnemyBrain best = null;
            float bestScore = float.MaxValue;
            foreach (var e in CombatUtil.FindEnemies(from, _pierceRange))
            {
                if (e == null || _hit.Contains(e) || e.IsDead) continue;
                Vector3 d = e.transform.position - from;
                d.y = 0f;
                float score = d.sqrMagnitude;
                if (travel.sqrMagnitude > 0.0001f && Vector3.Dot(d, travel) < 0f) score += 10000f; // davor: nur notfalls
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }
            return best;
        }
    }
}
