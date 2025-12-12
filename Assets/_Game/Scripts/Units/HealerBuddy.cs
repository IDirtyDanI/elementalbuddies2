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

        protected override bool TryPerformAction()
        {
            if (_playerStats == null) return false;

            float dist = Vector3.Distance(transform.position, _playerStats.transform.position);
            // Config.Damage used as Heal Amount
            if (dist <= Config.Range && _playerStats.CurrentHP < _playerStats.MaxHP)
            {
                _playerStats.Heal(Config.Damage > 0 ? Config.Damage : 8f);
                return true;
            }
            return false;
        }
    }
}