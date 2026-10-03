using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Dornenfalle des Bogenschützen: nach ArmTime scharf; betritt ein Gegner den Kreis, schnappt sie zu:
    // bis zu MaxTargets Gegner im Kreis werden festgehalten (Tempo 0, greifen weiter an) und verletzt.
    // Danach wachsen die Dornen kurz hoch und die Falle verschwindet. Läuft nach Lifetime ungenutzt ab.
    public class ThornTrap : MonoBehaviour
    {
        [Tooltip("Optik-Kind, das beim Zuschnappen hochwächst (leer = ganzes Objekt).")]
        public Transform SnapVisual;
        [Tooltip("Leuchten, solange die Falle scharf ist (optional).")]
        public GameObject ArmedIndicator;

        private ThornTrapSpell _spell;
        private float _dm = 1f;
        private float _age;
        private bool _triggered;
        private float _snapT;
        private Vector3 _baseScale;

        public bool IsArmed => !_triggered && _spell != null && _age >= _spell.ArmTime;
        public bool Triggered => _triggered;
        public int CaughtCount { get; private set; }

        public void Setup(ThornTrapSpell spell, float damageMultiplier)
        {
            _spell = spell;
            _dm = damageMultiplier;
            if (SnapVisual == null) SnapVisual = transform;
            _baseScale = SnapVisual.localScale;
            if (ArmedIndicator != null) ArmedIndicator.SetActive(false);
        }

        void Update()
        {
            Tick(Time.deltaTime);
        }

        // Öffentlich für synchrone Tests
        public void Tick(float dt)
        {
            if (_spell == null) return;
            _age += dt;

            if (_triggered)
            {
                // Zuschnappen: Dornen schießen hoch und sinken wieder
                _snapT += dt;
                float k = _snapT / 0.6f;
                if (SnapVisual != null) SnapVisual.localScale = new Vector3(_baseScale.x, _baseScale.y * (1f + 1.2f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI)), _baseScale.z);
                if (k >= 1.2f) Destroy(gameObject);
                return;
            }

            if (ArmedIndicator != null && IsArmed && !ArmedIndicator.activeSelf) ArmedIndicator.SetActive(true);
            if (IsArmed && CombatUtil.FindEnemies(transform.position, _spell.TriggerRadius).Count > 0) Snap();
            else if (_age >= _spell.Lifetime) Destroy(gameObject);
        }

        private void Snap()
        {
            _triggered = true;
            var enemies = CombatUtil.FindEnemies(transform.position, _spell.TriggerRadius);
            Vector3 c = transform.position;
            enemies.Sort((a, b) => CombatUtil.HorizontalDistance(a.transform.position, c).CompareTo(CombatUtil.HorizontalDistance(b.transform.position, c)));
            int n = Mathf.Min(enemies.Count, Mathf.Max(1, _spell.MaxTargets));
            var caught = new List<EnemyBrain>(n);
            for (int i = 0; i < n; i++) caught.Add(enemies[i]);

            foreach (var enemy in caught)
            {
                if (enemy == null) continue;
                CaughtCount++;
                // Festhalten = 100 % Verlangsamung (Angriffe bleiben möglich)
                enemy.ApplySlow(1f, _spell.RootDuration);
                var agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.speed = 0f;
                    agent.velocity = Vector3.zero; // sofort stehen bleiben statt auszurollen
                }
                if (_spell.RootVfxPrefab != null)
                {
                    var vfx = Instantiate(_spell.RootVfxPrefab, enemy.transform.position, Quaternion.identity, enemy.transform);
                    Destroy(vfx, _spell.RootDuration);
                }
                enemy.TakeDamage(_spell.Damage * _dm);
            }

            if (ArmedIndicator != null) ArmedIndicator.SetActive(false);
            if (_spell.SnapFxPrefab != null) CombatUtil.SpawnFx(_spell.SnapFxPrefab, c, Quaternion.identity, 2f);
            GameAudio.Play(SfxId.StoneWall, c);
        }
    }
}
