using UnityEngine;

namespace ElementalBuddies
{
    public class ShooterBuddy : ElementalBuddy
    {
        public GameObject ProjectilePrefab;
        public Transform FirePoint;

        protected override bool TryPerformAction()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, Config.Range);
            Transform bestTarget = null;
            float closestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit.CompareTag("Enemy"))
                {
                    float d = Vector3.Distance(transform.position, hit.transform.position);
                    if (d < closestDist)
                    {
                        closestDist = d;
                        bestTarget = hit.transform;
                    }
                }
            }

            if (bestTarget != null)
            {
                Shoot(bestTarget);
                return true;
            }
            return false;
        }

        private void Shoot(Transform target)
        {
            Vector3 spawnPos = FirePoint != null ? FirePoint.position : transform.position + Vector3.up;
            GameObject proj = Instantiate(ProjectilePrefab, spawnPos, Quaternion.identity);
            var projectileScript = proj.GetComponent<BuddyProjectile>();
            if (projectileScript != null)
            {
                projectileScript.Initialize(target, Config.Damage, Config.Type);
            }
        }
    }
}