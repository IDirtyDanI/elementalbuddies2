using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Luft (Feuer + Eis): Unterstützer. Passive Aura: +Feuerrate für andere Buddies in Reichweite (kein Stapeln, nicht für Luft-Buddies).
    // Aktion: Böe – alle Gegner in Reichweite werden den Pfad zurückgestoßen und nehmen Schaden; brennende Gegner
    // verteilen ihren Brand auf Gegner in der Nähe.
    public class AirBuddy : FusionBuddy
    {
        [Header("Aura")]
        public float FireRateBonus = 0.25f; // +25 % Feuerrate, Radius = EffectiveRange

        [Header("Böe")]
        public float KnockbackDistance = 2.5f;
        public float KnockbackDuration = 0.25f;
        public float SpreadRadius = 2.5f;
        public float SpreadBurnDps = 6f;
        public float SpreadBurnDuration = 3f;

        [Header("Optik")]
        public GameObject BurnVfxPrefab;  // optional, für verbreiteten Brand
        public GameObject GustVfxPrefab;  // optional, am Buddy (1.5 s); leer = Laufzeit-Ring
        public Material GustMaterial;     // optional für den Laufzeit-Ring
        public Color GustColor = new Color(0.75f, 0.95f, 0.85f);

        private readonly List<EnemyBrain> _targets = new List<EnemyBrain>();
        private readonly List<Vector3> _burningPositions = new List<Vector3>();
        private readonly List<EnemyBrain> _burningSources = new List<EnemyBrain>();

        protected override FusionElement DefaultElement => FusionElement.Air;

        // Feuerrate-Multiplikator für einen Buddy: stärkste abdeckende Luft-Aura (kein Stapeln), Luft-Buddies profitieren nicht
        public static float GetFireRateMultiplier(ElementalBuddy buddy)
        {
            if (buddy == null || buddy is AirBuddy) return 1f;
            float best = 1f;
            Vector3 pos = buddy.transform.position;
            var active = Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (!(active[i] is AirBuddy air) || air == null || air.Config == null || !air.isActiveAndEnabled || air.IsStunned) continue;
                float r = air.EffectiveRange;
                Vector3 d = air.transform.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude <= r * r) best = Mathf.Max(best, 1f + air.FireRateBonus);
            }
            return best;
        }

        protected override bool TryPerformAction()
        {
            _targets.Clear();
            _targets.AddRange(FindEnemies(transform.position, EffectiveRange));
            if (_targets.Count == 0) return false;

            // Brand verteilen (vor Rückstoß/Schaden, damit Positionen und Status noch stimmen)
            _burningPositions.Clear();
            _burningSources.Clear();
            foreach (var e in _targets)
            {
                if (e == null || !e.IsBurning) continue;
                _burningSources.Add(e);
                _burningPositions.Add(e.transform.position);
            }
            for (int i = 0; Net.IsServer && i < _burningPositions.Count; i++)
            {
                foreach (var other in FindEnemies(_burningPositions[i], SpreadRadius))
                {
                    if (other == null || _burningSources.Contains(other)) continue;
                    BurnEffect.Apply(other.gameObject, SpreadBurnDps, SpreadBurnDuration, BurnVfxPrefab);
                }
            }

            float damage = EffectiveDamage;
            EnemyBrain nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var e in _targets)
            {
                if (e == null) continue;
                float d = (e.transform.position - transform.position).sqrMagnitude;
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = e;
                }
                if (!Net.IsServer) continue; // Rückstoß/Schaden nur auf dem Server
                e.Knockback(e.PathBackDirection, KnockbackDistance, KnockbackDuration);
                e.TakeDamage(DamageAgainst(e, damage));
            }
            CurrentTarget = nearest != null ? nearest.transform : null;

            if (GustVfxPrefab != null)
                SpawnVfx(GustVfxPrefab, transform.position, 1.5f);
            else
                FusionLineFx.SpawnRing(transform.position + Vector3.up * 0.5f, 0.5f, EffectiveRange, GustMaterial, GustColor, 0.15f, 0.35f, "Gust");
            return true;
        }
    }
}
