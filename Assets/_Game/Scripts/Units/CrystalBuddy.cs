using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Kristall (Eis + Erde): Super-Tank. Viel Leben + Schadensreduktion, Frost-Aura (dauerhafte Verlangsamung in Reichweite),
    // Aktion = Spott aller Gegner in Reichweite. Erlittener Schaden lädt die Splitter-Ladung; ist sie voll,
    // entlädt sich eine Splitter-Nova (Ladung × ReflectFactor + EffectiveDamage an alle Gegner in Reichweite).
    public class CrystalBuddy : FusionBuddy
    {
        [Header("Panzer")]
        [Range(0f, 0.9f)] public float DamageReduction = 0.35f;

        [Header("Frost-Aura")]
        [Range(0f, 1f)] public float AuraSlow = 0.35f;
        public float AuraSlowDuration = 0.7f;
        public float AuraTickInterval = 0.5f;

        [Header("Spott")]
        public float TauntDuration = 2.5f;

        [Header("Splitter-Ladung")]
        public float ShardBurstThreshold = 60f;
        public float ReflectFactor = 1f;

        [Header("Optik")]
        public GameObject ShardBurstVfxPrefab; // optional, 2 s; leer = Laufzeit-Ring
        public GameObject TauntVfxPrefab;      // optional, 1.5 s am Buddy
        public Material RingMaterial;          // optional für die Laufzeit-Ringe
        public Color CrystalColor = new Color(0.6f, 0.9f, 1f);

        private float _charge;
        private float _auraTimer;
        private readonly List<EnemyBrain> _targets = new List<EnemyBrain>();

        protected override FusionElement DefaultElement => FusionElement.Crystal;

        public float Charge => _charge;
        public float Charge01 => ShardBurstThreshold > 0f ? Mathf.Clamp01(_charge / ShardBurstThreshold) : 0f;

        protected override void Reset()
        {
            base.Reset();
            BaseMaxHP = 300f;
        }

        public override void TakeDamage(float amount)
        {
            if (amount <= 0f || CurrentHP <= 0f) return;
            float reduced = amount * (1f - Mathf.Clamp(DamageReduction, 0f, 0.9f));
            _charge += Mathf.Min(reduced, CurrentHP); // nur tatsächlich erlittener Schaden lädt
            base.TakeDamage(reduced);
            if (ShardBurstThreshold > 0f && _charge >= ShardBurstThreshold) ReleaseShardBurst();
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
                    foreach (var e in FindEnemies(transform.position, EffectiveRange))
                        e.ApplySlow(AuraSlow, AuraSlowDuration);
            }
        }

        // Spott: alle Gegner in Reichweite greifen den Kristall an
        protected override bool TryPerformAction()
        {
            bool any = false;
            foreach (var e in FindEnemies(transform.position, EffectiveRange))
            {
                ((ITauntable)e).Taunt(transform, TauntDuration);
                any = true;
            }
            if (!any) return false;

            if (TauntVfxPrefab != null) SpawnVfx(TauntVfxPrefab, transform.position, 1.5f);
            else FusionLineFx.SpawnRing(transform.position + Vector3.up * 0.1f, EffectiveRange, 0.5f, RingMaterial,
                CrystalColor, 0.12f, 0.4f, "CrystalTaunt");
            return true;
        }

        // Splitter-Nova: Ladung entladen
        private void ReleaseShardBurst()
        {
            float damage = _charge * ReflectFactor + EffectiveDamage;
            _charge = 0f;

            Vector3 center = transform.position;
            _targets.Clear();
            _targets.AddRange(FindEnemies(center, EffectiveRange));
            foreach (var e in _targets)
                if (e != null && e.CurrentHP > 0f) e.TakeDamage(DamageAgainst(e, damage));

            if (ShardBurstVfxPrefab != null) SpawnVfx(ShardBurstVfxPrefab, center + Vector3.up * 0.5f, 2f);
            else FusionLineFx.SpawnRing(center + Vector3.up * 0.1f, 0.5f, EffectiveRange, RingMaterial, CrystalColor,
                0.25f, 0.45f, "ShardNova");
        }
    }
}
