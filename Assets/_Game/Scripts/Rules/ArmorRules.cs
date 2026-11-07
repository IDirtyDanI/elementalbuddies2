using System;

namespace ElementalBuddies
{
    // Rüstung der Champions (ChampionKit.Armor) – reine Rechenregel, damit EditMode-Tests (ArmorRulesTests) sie ohne
    // Szene prüfen können. Champion-Balancing 2026-11-04: Der Schwertkämpfer bekommt 20 % Rüstung, damit er als
    // Frontkämpfer spürbar zäher ist. PlayerAbilities.ModifyIncomingDamage wendet sie vor der Kit-Logik (Block) an.
    public static class ArmorRules
    {
        // Obergrenze: auch mit späteren Boni bleibt jeder Treffer spürbar (keine Unverwundbarkeit über Rüstung)
        public const float MaxArmor = 0.75f;

        // Rüstung auf 0 … MaxArmor begrenzen (negativ oder NaN → 0)
        public static float Clamp(float armor)
        {
            if (!(armor > 0f)) return 0f;
            return Math.Min(armor, MaxArmor);
        }

        // Verbleibender Schaden nach Rüstung: damage × (1 − Rüstung), nie negativ
        public static float Reduce(float damage, float armor)
        {
            if (!(damage > 0f)) return 0f;
            return damage * (1f - Clamp(armor));
        }
    }
}
