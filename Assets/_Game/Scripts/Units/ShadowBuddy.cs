using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Schatten (Erde + Licht): verflucht bis zu N Gegner in Reichweite (bevorzugt noch nicht verfluchte, dann die mit den meisten LP).
    // Fluch = Schaden über Zeit (EffectiveDamage als DPS) + erhöhter erlittener Schaden.
    public class ShadowBuddy : FusionBuddy
    {
        [Header("Fluch")]
        public int Targets = 2;
        public float CurseDuration = 5f;
        [Range(0f, 2f)] public float DamageTakenBonus = 0.25f;

        [Header("Optik")]
        public Material BeamMaterial;      // optional; leer = Laufzeit-Material
        public GameObject CurseVfxPrefab;  // optional, an ApplyCurse übergeben
        public Color BeamColor = new Color(0.55f, 0.25f, 0.85f);
        public float BeamWidth = 0.12f;
        public float BeamLifetime = 0.2f;

        private readonly List<EnemyBrain> _candidates = new List<EnemyBrain>();

        protected override FusionElement DefaultElement => FusionElement.Shadow;

        protected override bool TryPerformAction()
        {
            _candidates.Clear();
            _candidates.AddRange(FindEnemies(transform.position, EffectiveRange));
            if (_candidates.Count == 0 || Targets <= 0) return false;

            // Unverfluchte zuerst, dann höchste aktuelle LP
            _candidates.Sort((a, b) =>
            {
                bool ca = a.IsCursed, cb = b.IsCursed;
                if (ca != cb) return ca ? 1 : -1;
                return b.CurrentHP.CompareTo(a.CurrentHP);
            });

            float dps = EffectiveDamage;
            Vector3 origin = FirePosition;
            int count = Mathf.Min(Targets, _candidates.Count);
            for (int i = 0; i < count; i++)
            {
                var e = _candidates[i];
                e.ApplyCurse(dps, CurseDuration, DamageTakenBonus, CurseVfxPrefab);
                FusionLineFx.Spawn(new[] { origin, BodyPoint(e) }, BeamMaterial, BeamColor, new Color(0.25f, 0.05f, 0.4f),
                    BeamWidth, BeamWidth * 0.5f, BeamLifetime, "CurseBeam");
            }
            CurrentTarget = _candidates[0].transform;
            return true;
        }
    }
}
