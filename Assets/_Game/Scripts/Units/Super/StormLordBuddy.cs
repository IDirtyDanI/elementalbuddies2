using UnityEngine;

namespace ElementalBuddies
{
    // Sturmfürst (Feuer + Eis + Licht): schwebender Kontrolleur. Ruft alle ~7 s eine Sturmwolke über die dichteste Gegnergruppe
    // in Reichweite (max. 1 Wolke). Die Wolke macht Gegner nass und schlägt regelmäßig mit Blitzen ein (StormCloud).
    // Passive Aura: Gegner im AuraRadius sind verlangsamt.
    public class StormLordBuddy : SuperBuddy
    {
        [Header("Sturmwolke")]
        public float CloudRadius = 4f;
        public float CloudLifetime = 6f;
        public float CloudHeight = 5f;
        public float CloudDriftSpeed = 2f;
        public float WetInterval = 0.5f;
        public float WetDuration = 3f;
        public float StrikeInterval = 0.7f;
        public float WetMultiplier = 2f;
        public float ChainRange = 4f;
        [Range(0f, 1f)] public float ChainFactor = 0.6f;

        [Header("Aura")]
        public float AuraRadius = 9f;
        [Range(0f, 1f)] public float AuraSlow = 0.25f;
        public float AuraSlowDuration = 0.5f;
        public float AuraTickInterval = 0.25f;

        [Header("Optik")]
        public GameObject StormCloudPrefab;    // optional; leer = Laufzeit-Wolke (dunkle Scheiben)
        public GameObject StrikeEffectPrefab;  // optional, an jedem Einschlag (1 s)
        public GameObject WetVfxPrefab;        // optional, an ApplyWet übergeben
        public Material BoltMaterial;          // optional; leer = Laufzeit-Material
        public Color BoltColor = new Color(0.75f, 0.8f, 1f);

        private StormCloud _cloud;
        private float _auraTimer;

        public StormCloud ActiveCloud => _cloud != null && !_cloud.IsExpired ? _cloud : null;

        protected override FusionElement DefaultElement => FusionElement.StormLord;

        protected override void ApplyClassDefaults()
        {
            BaseMaxHP = 220f;
        }

        protected override void Update()
        {
            base.Update();
            if (IsStunned) return;
            _auraTimer += Time.deltaTime;
            if (_auraTimer >= AuraTickInterval)
            {
                _auraTimer = 0f;
                if (AuraSlow > 0f)
                    foreach (var e in FindEnemies(transform.position, AuraRadius))
                        e.ApplySlow(AuraSlow, AuraSlowDuration);
            }
        }

        protected override bool TryPerformAction()
        {
            if (ActiveCloud != null) return false; // max. 1 Wolke
            var best = FindDensestEnemy(transform.position, EffectiveRange, CloudRadius, out _);
            if (best == null) return false;
            CurrentTarget = best.transform;
            _cloud = StormCloud.Spawn(this, best.transform.position);
            return true;
        }
    }
}
