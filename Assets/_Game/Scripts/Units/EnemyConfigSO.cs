using UnityEngine;

namespace ElementalBuddies
{
    [CreateAssetMenu(fileName = "EnemyConfig", menuName = "ElementalBuddies/Enemy Config", order = 3)]
    public class EnemyConfigSO : UnitConfigSO
    {
        [Header("Enemy Specific Stats")]
        public float BaseHP; // Basis-HP; Welle skaliert über WaveManager.HpMultiplier (Bosse: BaseHP · w · min(1, w/12)²)
        public float Speed;
        // Nahkampf: Schaden pro SEKUNDE (DPS) – ein Hieb macht AttackDamage × AttackInterval (seit 2026-10-12 Einzelhiebe,
        // gleicher DPS wie der frühere Dauerschaden). Fernkampf: Schaden pro Schuss.
        [Tooltip("Nahkampf: DPS (Schaden pro Hieb = AttackDamage × AttackInterval). Fernkampf: Schaden pro Schuss.")]
        public float AttackDamage;

        [Header("Rolle (Map-Theme)")]
        [Tooltip("Rolle in der Wellenlogik; ein EnemyThemeSO tauscht je Rolle die Config (Walddorf-Plan).")]
        public EnemyRole Role = EnemyRole.Runner;

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
        [Tooltip("Sekunden zwischen zwei Schüssen (Schuss = AttackDamage) bzw. Nahkampf-Hieben (Hieb = AttackDamage × AttackInterval).")]
        public float AttackInterval = 1.5f;
        public float ProjectileSpeed = 14f;
        [Tooltip("Verzögerung zwischen Attack-Trigger und Abschuss bzw. Nahkampf-Treffer (s) – auf den Trefferframe der Angriffs-Animation abstimmen.")]
        public float AttackWindup = 0.35f;
        public float ProjectileSpawnHeight = 1.3f;

        [Header("Nahkampf-Treffer")]
        [Tooltip("Ton im Trefferzeitpunkt eines Nahkampf-Hiebs (auf allen Rechnern, global begrenzt).")]
        public bool MeleeHitSound;
        public SfxId MeleeHitSfx = SfxId.EnemyHit;

        [Header("Klänge (optional, nur Ton – Walddorf 2026-10-16)")]
        [Tooltip("Ton beim Angriff: Fernkampf beim Abschuss (nach AttackWindup), Nahkampf/Flieger beim Ausholen bzw. Sturzflug-Beginn.")]
        public bool AttackSound;
        public SfxId AttackSfx = SfxId.EnemyHit;
        [Tooltip("Eigener Todeslaut statt des Knochenbruchs (SfxId.EnemyDeath).")]
        public bool DeathSound;
        public SfxId DeathSfx = SfxId.EnemyDeath;
        [Tooltip("Ruf beim Erscheinen (z. B. Wolfsheulen). Die MinInterval des GameAudio-Eintrags drosselt Rudel/Gruppen auf einen Ruf.")]
        public bool SpawnSound;
        public SfxId SpawnSfx = SfxId.EliteSpawn;

        [Header("Boss")]
        [Tooltip("Boss: Ankündigung, Boss-HP-Leiste (BossHealthBarUI). Fähigkeiten über BossBrain am Prefab.")]
        public bool IsBoss;
        [Tooltip("Anzeigename (z. B. „Knochenfürst“).")]
        public string DisplayName = "";
        [Tooltip("Verkürzt Betäubung, Einfrieren, Verlangsamung und Spott sowie die Rückstoß-Distanz um diesen Anteil.")]
        [Range(0f, 0.9f)] public float ControlResistance = 0f;
        [Tooltip("Boss-Farbe für UI und Boden-Warnflächen.")]
        public Color ThemeColor = Color.white;

        [Header("Wellenvorschau")]
        [Tooltip("Icon für Wellenvorschau und Statistik (GameFeelSetup rendert es aus dem Prefab).")]
        public Sprite Icon;
        [Tooltip("Kurzer Hinweis in der Wellenvorschau, z. B. „Fernkampf – greift Buddies an“.")]
        public string PreviewHint = "";

        [Header("Flieger (Walddorf-Plan)")]
        [Tooltip("Fliegt direkt aufs Ziel (FlyingMotor statt NavMesh), Trefferkapsel bis zum Boden, Bodenschatten.")]
        public bool IsFlyer;
        [Tooltip("Flughöhe über dem Boden (m).")]
        public float FlyHeight = 2f;
        [Tooltip("Abstand über Hindernissen (Dächer, Mauern, Bäume) in m.")]
        public float ObstacleClearance = 1.5f;
        [Tooltip("Herabstoßen: Höhe über dem Untergrund beim Treffer (mindestens Zielmitte), m.")]
        public float DiveHeight = 0.8f;

        [Header("Tod (optional)")]
        [Tooltip("> 0: Beim Tod löst sich das Modell, spielt den Animator-Trigger „Die“ so lange (s) und verschwindet dann " +
                 "(Flieger stürzen dabei ab). 0 = wie bisher sofort weg.")]
        public float DeathClipDuration = 0f;
        [Tooltip("Partikel beim Tod (nur Optik, auf allen Rechnern über EnemyBrain.OnEnemyKilled). Leer = Standard des " +
                 "FeedbackDirector (Untote: Knochensplitter, Bosse: Knochen-Explosion). Dorf/Flieger: DeathVfxBuilder.")]
        public GameObject DeathVfx;
    }
}