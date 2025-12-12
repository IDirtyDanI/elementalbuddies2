using UnityEngine;

namespace ElementalBuddies
{
    [CreateAssetMenu(fileName = "EnemyConfig", menuName = "ElementalBuddies/Enemy Config", order = 3)]
    public class EnemyConfigSO : UnitConfigSO
    {
        [Header("Enemy Specific Stats")]
        public float BaseHP; // Base HP, will be modified by wave progression
        public float Speed;
        public float AttackDamage; // Damage dealt to player or blocker
    }
}