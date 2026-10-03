using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Gemeinsame Treffer-Helfer für alle Champion-Fähigkeiten (Kreis, Kegel, Linie)
    public static class CombatUtil
    {
        private static readonly HashSet<EnemyBrain> _seen = new HashSet<EnemyBrain>();

        // Alle lebenden Gegner, deren Collider die Kugel berühren (pro Gegner nur einmal)
        public static List<EnemyBrain> FindEnemies(Vector3 center, float radius)
        {
            var result = new List<EnemyBrain>();
            _seen.Clear();
            Collider[] hits = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide);
            foreach (var hit in hits)
            {
                if (hit == null) continue;
                var enemy = hit.GetComponentInParent<EnemyBrain>();
                if (enemy == null || !enemy.isActiveAndEnabled) continue;
                if (_seen.Add(enemy)) result.Add(enemy);
            }
            _seen.Clear();
            return result;
        }

        // Gegner im horizontalen Kegel (volle Öffnung coneAngle). Gegner direkt am Ursprung zählen immer.
        public static List<EnemyBrain> FindEnemiesInCone(Vector3 origin, Vector3 direction, float range, float coneAngle)
        {
            var result = new List<EnemyBrain>();
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            float half = coneAngle * 0.5f;
            foreach (var enemy in FindEnemies(origin, range))
            {
                if (enemy == null) continue;
                Vector3 to = enemy.transform.position - origin;
                to.y = 0f;
                if (to.sqrMagnitude > 0.36f && Vector3.Angle(direction, to) > half) continue;
                result.Add(enemy);
            }
            return result;
        }

        // Gegner entlang einer horizontalen Linie (Kapsel mit Radius halfWidth), sortiert nach Abstand
        public static List<EnemyBrain> FindEnemiesOnLine(Vector3 origin, Vector3 direction, float length, float halfWidth)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();
            Vector3 mid = origin + direction * (length * 0.5f);
            var result = new List<EnemyBrain>();
            foreach (var enemy in FindEnemies(mid, length * 0.5f + halfWidth + 1f))
            {
                if (enemy == null) continue;
                Vector3 to = enemy.transform.position - origin;
                to.y = 0f;
                float along = Vector3.Dot(to, direction);
                if (along < -0.5f || along > length + 0.5f) continue;
                float side = (to - direction * along).magnitude;
                if (side > halfWidth + EnemyRadius(enemy)) continue;
                result.Add(enemy);
            }
            result.Sort((a, b) => Vector3.Dot(a.transform.position - origin, direction).CompareTo(Vector3.Dot(b.transform.position - origin, direction)));
            return result;
        }

        // Grobe Breite eines Gegners (NavMeshAgent-Radius), damit breite Gegner leichter getroffen werden
        public static float EnemyRadius(EnemyBrain enemy)
        {
            var agent = enemy != null ? enemy.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
            return agent != null ? agent.radius * enemy.transform.lossyScale.x : 0.4f;
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // Horizontale Richtung von a nach b (Fallback, falls beide aufeinander liegen)
        public static Vector3 FlatDirection(Vector3 from, Vector3 to, Vector3 fallback)
        {
            Vector3 d = to - from;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) d = fallback;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
        }

        // Kurzlebiges Effekt-Objekt erzeugen (null-sicher)
        public static GameObject SpawnFx(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, float scale = 1f)
        {
            if (prefab == null) return null;
            GameObject fx = Object.Instantiate(prefab, position, rotation);
            if (!Mathf.Approximately(scale, 1f)) fx.transform.localScale = prefab.transform.localScale * scale;
            if (lifetime > 0f) Object.Destroy(fx, lifetime);
            return fx;
        }
    }
}
