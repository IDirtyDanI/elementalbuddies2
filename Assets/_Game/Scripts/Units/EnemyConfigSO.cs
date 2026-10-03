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

        [Header("Gegnertyp")]
        [Tooltip("Schadensreduktion (0.3 = 30 % weniger Schaden). Fluch ignoriert die Rüstung.")]
        [Range(0f, 0.9f)] public float Armor;
        [Tooltip("Faktor auf das Splitter-Kopfgeld (GlobalSettings.ShardsPerKill).")]
        public float BountyMultiplier = 1f;
        [Tooltip("Skalierung des gespawnten Gegners (1 = Prefab-Größe).")]
        public float VisualScale = 1f;
    }
}