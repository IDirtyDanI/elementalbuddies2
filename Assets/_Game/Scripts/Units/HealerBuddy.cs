using UnityEngine;

namespace ElementalBuddies
{
    public class HealerBuddy : ElementalBuddy
    {
        private PlayerStats _playerStats;

        protected override void Start()
        {
            base.Start();
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) _playerStats = player.GetComponent<PlayerStats>();
        }

        // Damage = Heilmenge pro Aktion (Fallback 8, falls Config.Damage 0 ist)
        public override float GetDamageAtLevel(int level)
        {
            float baseHeal = Config != null && Config.Damage > 0 ? Config.Damage : 8f;
            return baseHeal * LevelMultiplier(DamageBonusPerLevel, level);
        }

        protected override bool TryPerformAction()
        {
            if (_playerStats == null) return false;

            float dist = Vector3.Distance(transform.position, _playerStats.transform.position);
            // EffectiveDamage used as Heal Amount
            if (dist <= EffectiveRange && _playerStats.CurrentHP < _playerStats.MaxHP)
            {
                CurrentTarget = _playerStats.transform;
                _playerStats.Heal(EffectiveDamage);
                return true;
            }
            return false;
        }
    }
}