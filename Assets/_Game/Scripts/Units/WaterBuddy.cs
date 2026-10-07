using UnityEngine;

namespace ElementalBuddies
{
    // Wasser (Eis + Licht): Wasserstrahl bis zur vollen Reichweite in Richtung des nächsten Gegners.
    // Alle Gegner im Strahl nehmen Schaden, werden nass und leicht verlangsamt. Jede n-te Aktion zusätzlich ein Strudel am Ziel (festhalten + nass).
    public class WaterBuddy : FusionBuddy
    {
        [Header("Wasserstrahl")]
        public float JetWidth = 0.9f;        // Abstand zur Strahl-Achse (horizontal)
        public float WetDuration = 4f;
        [Range(0f, 1f)] public float JetSlow = 0.2f;
        public float JetSlowDuration = 1.5f;

        [Header("Strudel")]
        public int WhirlpoolEvery = 4;
        public float WhirlpoolRadius = 2.5f;
        public float WhirlpoolRootDuration = 1.6f;
        [Range(0f, 2f)] public float WhirlpoolDamageFactor = 0.5f;

        [Header("Optik")]
        public Material JetMaterial;          // optional; leer = Laufzeit-Material
        public GameObject WetVfxPrefab;       // optional, an ApplyWet übergeben
        public GameObject WhirlpoolVfxPrefab; // optional, am Strudel (2.5 s)
        public Color JetColor = new Color(0.3f, 0.65f, 1f);
        public float JetVisualWidth = 0.22f;
        public float JetLifetime = 0.18f;

        private int _actionCount;

        protected override FusionElement DefaultElement => FusionElement.Water;

        protected override bool TryPerformAction()
        {
            float range = EffectiveRange;
            var target = FindNearestEnemy(transform.position, range);
            if (target == null) return false;

            CurrentTarget = target.transform;
            float damage = EffectiveDamage;
            Vector3 origin = FirePosition;
            Vector3 targetPos = target.transform.position;

            // Strahl horizontal bis zur vollen Reichweite verlängern
            Vector3 dir = targetPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            dir.Normalize();
            Vector3 start = transform.position;
            Vector3 end = start + dir * range;

            foreach (var e in FindEnemies(start + dir * (range * 0.5f), range * 0.5f + JetWidth + 0.5f).ToArray())
            {
                if (!Net.IsServer || e == null || HorizontalDistanceToSegment(e.transform.position, start, end) > JetWidth) continue;
                e.ApplyWet(WetDuration, WetVfxPrefab);
                e.ApplySlow(JetSlow, JetSlowDuration);
                e.TakeDamage(DamageAgainst(e, damage));
            }

            Vector3 jetEnd = end;
            jetEnd.y = BodyPoint(target).y;
            FusionLineFx.Spawn(new[] { origin, jetEnd }, JetMaterial, JetColor, new Color(0.75f, 0.9f, 1f), JetVisualWidth, JetVisualWidth * 0.6f, JetLifetime, "WaterJet");

            _actionCount++;
            if (WhirlpoolEvery > 0 && _actionCount % WhirlpoolEvery == 0) Whirlpool(targetPos, damage * WhirlpoolDamageFactor);
            return true;
        }

        public bool NextIsWhirlpool => WhirlpoolEvery > 0 && (_actionCount + 1) % WhirlpoolEvery == 0;

        private void Whirlpool(Vector3 center, float damage)
        {
            foreach (var e in FindEnemies(center, WhirlpoolRadius).ToArray())
            {
                if (!Net.IsServer || e == null) continue;
                e.ApplyWet(WetDuration, WetVfxPrefab);
                e.ApplySlow(1f, WhirlpoolRootDuration); // festhalten
                e.TakeDamage(DamageAgainst(e, damage));
            }

            if (WhirlpoolVfxPrefab != null)
                SpawnVfx(WhirlpoolVfxPrefab, center, 2.5f);
            else
                FusionLineFx.SpawnRing(center + Vector3.up * 0.5f, WhirlpoolRadius, 0.3f, JetMaterial, JetColor, 0.12f, 0.8f, "Whirlpool");
        }

        private static float HorizontalDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 p2 = new Vector2(p.x, p.z), a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z);
            Vector2 ab = b2 - a2;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p2, a2 + ab * t);
        }
    }
}
