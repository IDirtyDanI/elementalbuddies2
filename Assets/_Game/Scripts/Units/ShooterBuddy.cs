using UnityEngine;

namespace ElementalBuddies
{
    public class ShooterBuddy : ElementalBuddy
    {
        public GameObject ProjectilePrefab;
        public Transform FirePoint;

        [Header("Scharfschütze (z. B. Feuer Stufe 3)")]
        [Tooltip("Ab dieser Stufe schießt der Buddy als Scharfschütze (0 = nie).")]
        public int SniperFromLevel = 0;
        [Tooltip("Feste Reichweite im Scharfschützen-Modus (m) – reicht bis zu den Fernkampf-Bossen.")]
        public float SniperRange = 18f;
        [Tooltip("Faktor auf die Feuerrate im Scharfschützen-Modus (langsamer, dafür schwere Schüsse).")]
        public float SniperFireRateFactor = 0.45f;
        [Tooltip("Faktor auf den Schaden pro Schuss im Scharfschützen-Modus.")]
        public float SniperDamageFactor = 3f;
        [Tooltip("Zusätzlicher Schadensfaktor gegen Bosse.")]
        public float SniperBossDamageMultiplier = 1.5f;
        [Tooltip("Projektil-Tempo im Scharfschützen-Modus (0 = Prefab-Wert).")]
        public float SniperProjectileSpeed = 40f;

        public bool IsSniperAtLevel(int level) => SniperFromLevel > 0 && level >= SniperFromLevel;
        public bool IsSniper => IsSniperAtLevel(Level);

        protected override float GetBaseDamageAtLevel(int level)
        {
            float dmg = base.GetBaseDamageAtLevel(level);
            return IsSniperAtLevel(level) ? dmg * SniperDamageFactor : dmg;
        }

        public override float GetFireRateAtLevel(int level)
        {
            float rate = base.GetFireRateAtLevel(level);
            return IsSniperAtLevel(level) ? rate * SniperFireRateFactor : rate;
        }

        public override float GetRangeAtLevel(int level)
        {
            return IsSniperAtLevel(level) ? Mathf.Max(SniperRange, base.GetRangeAtLevel(level)) : base.GetRangeAtLevel(level);
        }

        protected override bool TryPerformAction()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveRange);
            Transform bestTarget = null;
            float closestDist = float.MaxValue;
            int bestPriority = -1;
            bool sniper = IsSniper;

            foreach (var hit in hits)
            {
                if (hit.CompareTag("Enemy"))
                {
                    // Scharfschütze: Bosse > Fernkämpfer > Rest, jeweils der nächste
                    int priority = sniper ? SniperPriority(hit) : 0;
                    float d = Vector3.Distance(transform.position, hit.transform.position);
                    if (priority > bestPriority || (priority == bestPriority && d < closestDist))
                    {
                        bestPriority = priority;
                        closestDist = d;
                        bestTarget = hit.transform;
                    }
                }
            }

            if (bestTarget != null)
            {
                CurrentTarget = bestTarget;
                Shoot(bestTarget);
                return true;
            }
            return false;
        }

        private static int SniperPriority(Collider hit)
        {
            var enemy = hit.GetComponent<EnemyBrain>();
            if (enemy == null || enemy.Config == null) return 0;
            if (enemy.Config.IsBoss) return 2;
            return enemy.Config.ProjectilePrefab != null ? 1 : 0;
        }

        private void Shoot(Transform target)
        {
            Vector3 spawnPos = FirePoint != null ? FirePoint.position : transform.position + Vector3.up;
            GameObject proj = Instantiate(ProjectilePrefab, spawnPos, Quaternion.identity);
            var projectileScript = proj.GetComponent<BuddyProjectile>();
            if (projectileScript != null)
            {
                float damage = EffectiveDamage;
                if (IsSniper)
                {
                    var enemy = target.GetComponent<EnemyBrain>();
                    if (enemy != null && enemy.IsBoss) damage *= SniperBossDamageMultiplier;
                    if (SniperProjectileSpeed > 0f) projectileScript.Speed = SniperProjectileSpeed;
                    proj.transform.localScale *= 1.4f;
                }
                projectileScript.Initialize(target, damage, Config.Type);
            }
        }
    }
}
