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

        [Header("Buddy-Angriff")]
        [Tooltip("> 0: greift den nächsten lebenden Buddy in diesem Radius (horizontal) an. 0 = ignoriert Buddies.")]
        public float BuddyAggroRadius = 0f;
        [Tooltip("Buddy-Jäger: Buddies im BuddyAggroRadius haben Vorrang vor dem Spieler.")]
        public bool HuntsBuddies;
        [Tooltip("Schadensfaktor gegen Buddies.")]
        public float BuddyDamageMultiplier = 1f;

        [Header("Fernkampf")]
        [Tooltip("Angriffsreichweite gegen Einheiten/Nexus (0 = Nahkampf-Standard des EnemyBrain).")]
        public float AttackRange = 0f;
        [Tooltip("Projektil (mit EnemyProjectile). Leer = Nahkampf.")]
        public GameObject ProjectilePrefab;
        [Tooltip("Fernkampf: Sekunden zwischen zwei Schüssen; jeder Schuss macht AttackDamage.")]
        public float AttackInterval = 1.5f;
        public float ProjectileSpeed = 14f;
        [Tooltip("Verzögerung zwischen Attack-Trigger und Abschuss (s).")]
        public float AttackWindup = 0.35f;
        public float ProjectileSpawnHeight = 1.3f;

        [Header("Boss")]
        [Tooltip("Boss: Ankündigung, Boss-HP-Leiste (BossHealthBarUI). Fähigkeiten über BossBrain am Prefab.")]
        public bool IsBoss;
        [Tooltip("Anzeigename (z. B. „Knochenfürst“).")]
        public string DisplayName = "";
        [Tooltip("Faktor auf den Wellen-HP-Bonus aus Initialize.")]
        public float HpBonusMultiplier = 1f;
        [Tooltip("Verkürzt Betäubung, Einfrieren, Verlangsamung und Spott sowie die Rückstoß-Distanz um diesen Anteil.")]
        [Range(0f, 0.9f)] public float ControlResistance = 0f;
        [Tooltip("Boss-Farbe für UI und Boden-Warnflächen.")]
        public Color ThemeColor = Color.white;
    }
}