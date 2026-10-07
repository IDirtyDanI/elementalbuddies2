using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Everything a spell needs to know about the caster at cast time
    public struct SpellCastContext
    {
        public PlayerAbilities Caster;
        public Vector3 Origin;        // Player position (on the ground if a floor was found)
        public Vector3 AimDirection;  // Horizontal, normalized
        public float DamageMultiplier;
        public Vector3 AimPoint;      // Mauspunkt auf dem Boden (Fallback: 5 m vor dem Spieler)
        public bool HasAimPoint;
        // Mehrspieler: Daten, die der Besitzer beim Wirken festlegt und per Cast-RPC an alle Rechner schickt
        public Vector3 MoveDirection; // Laufeingabe (WASD) zum Zeitpunkt des Wirkens, horizontal (Rolle); zero = keine
        public int Variant;           // Kit-spezifisch, z. B. Kombo-Schritt des Schwerthiebs (0 = selbst bestimmen)
        public bool IsRemote;         // true = Abbild eines fremden Casts (Besitzer hat ihn schon ausgeführt)
    }

    // Common config for the unlockable element abilities of all champions
    [System.Serializable]
    public abstract class ElementSpell
    {
        [Header("Cost / Cooldown")]
        public float ManaCost = 30f;
        public float Cooldown = 8f;

        [Header("VFX (optional)")]
        [Tooltip("Spawned on cast, destroyed after EffectLifetime.")]
        public GameObject CastEffectPrefab;
        public float EffectLifetime = 3f;
        [Tooltip("If true, the effect's localScale is multiplied by the spell size (radius / range / wall length) - author the VFX for a size of 1 m then.")]
        public bool ScaleEffectBySize = false;

        public abstract void Cast(SpellCastContext ctx);

        // ---------------- Händlerkarten (AbilityMods) ----------------

        // Kopie mit angewendeten Fähigkeits-Modifikatoren (ohne Modifikatoren: diese Instanz). Die Kits wirken und
        // beschreiben immer die Kopie, Laufzeit-Objekte (Pfeilregen, Falle, Mauer) bekommen sie als Konfiguration.
        // Abklingzeit und Schaden laufen zentral über PlayerAbilities (GetCooldownDuration, ctx.DamageMultiplier).
        public ElementSpell WithMods(AbilityMods mods, AbilityId id)
        {
            if (mods == null || !mods.HasAny(id)) return this;
            var copy = (ElementSpell)MemberwiseClone();
            copy.ApplyMods(mods, id);
            return copy;
        }

        protected virtual void ApplyMods(AbilityMods m, AbilityId id) { }

        // Aktueller Wert eines Karten-Stats für die Kartenanzeige ("2.6 → 3.1 m"). dm = Schadens-Multiplikator.
        public virtual bool TryGetStat(AbilityStat stat, float dm, out float value, out string unit, out string label)
        {
            value = 0f;
            unit = "";
            label = "";
            return false;
        }

        protected static bool Stat(float v, string u, string l, out float value, out string unit, out string label)
        {
            value = v;
            unit = u;
            label = l;
            return true;
        }

        protected void SpawnEffect(Vector3 position, Quaternion rotation, float size)
        {
            if (CastEffectPrefab == null) return;
            GameObject fx = Object.Instantiate(CastEffectPrefab, position, rotation);
            if (ScaleEffectBySize) fx.transform.localScale = CastEffectPrefab.transform.localScale * size;
            if (EffectLifetime > 0f) Object.Destroy(fx, EffectLifetime);
        }

        // ---------------- Shared helpers ----------------

        // All living enemies whose colliders overlap the sphere (deduplicated per enemy)
        protected static List<EnemyBrain> FindEnemies(Vector3 center, float radius)
        {
            return CombatUtil.FindEnemies(center, radius);
        }

        protected static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
