using System;

namespace ElementalBuddies
{
    // Arten der Unterstützungs-Buffs (Schamane). Neue Einträge nur hinten anhängen (gehen als int übers Netz).
    public enum SupportBuffKind
    {
        AttackSpeed,     // Grundangriff schneller (Kriegstotem): Abklingzeit des LMB-Slots ÷ (1 + Wert)
        MoveSpeed,       // Lauftempo (Windschritt): Tempo × (1 + Wert)
        DamageReduction  // weniger erlittener Schaden (Steinhaut-Totem): Schaden × (1 − Wert)
    }

    // Ein zeitlich begrenzter Buff: Stärke (Anteil, 0.25 = 25 %) und Ende (Spielzeit in s)
    [Serializable]
    public struct SupportBuff
    {
        public float Value;
        public float Until;

        public SupportBuff(float value, float until)
        {
            Value = value;
            Until = until;
        }
    }

    // Reine Regeln der Unterstützungs-Buffs (EditMode-Tests: SupportBuffRulesTests). Konzept Schamane §2 „Stapeln“:
    // gleiche Buffs stapeln NICHT, der stärkste gilt; ein gleich starker oder stärkerer erneuert die Dauer. Ein
    // schwächerer ändert nichts, solange der stärkere läuft (sonst verlängerte er dessen Wirkung).
    public static class SupportBuffRules
    {
        // Obergrenze der Schadensreduktion (wie Rüstung: kein Unverwundbar-Buff)
        public const float MaxDamageReduction = 0.75f;
        // Obergrenze für Tempo-Buffs (Schutz gegen Händlerkarten-Stapel)
        public const float MaxSpeedBonus = 2f;
        private const float Eps = 0.0001f;

        public static bool IsActive(SupportBuff b, float now) => now < b.Until && b.Value > 0f;

        // Aktuelle Stärke (0, wenn abgelaufen)
        public static float Current(SupportBuff b, float now) => IsActive(b, now) ? b.Value : 0f;

        // Neuen Buff verrechnen: abgelaufen → neu; stärker/gleich stark → übernehmen, Dauer erneuern; schwächer → bleibt
        public static SupportBuff Apply(SupportBuff current, float value, float duration, float now)
        {
            if (!(value > 0f) || !(duration > 0f)) return current;
            if (!IsActive(current, now)) return new SupportBuff(value, now + duration);
            if (value + Eps >= current.Value)
                return new SupportBuff(Math.Max(value, current.Value), Math.Max(current.Until, now + duration));
            return current;
        }

        // Begrenzte Stärke je Art
        public static float Clamp(SupportBuffKind kind, float value)
        {
            if (!(value > 0f)) return 0f;
            return kind == SupportBuffKind.DamageReduction ? Math.Min(value, MaxDamageReduction) : Math.Min(value, MaxSpeedBonus);
        }

        // Tempo-Faktor (Angriffstempo/Lauftempo): 1 + Bonus
        public static float SpeedFactor(float bonus) => 1f + Clamp(SupportBuffKind.MoveSpeed, bonus);

        // Abklingzeit-Faktor bei Angriffstempo-Bonus: 1 / (1 + Bonus)
        public static float CooldownFactor(float attackSpeedBonus) => 1f / SpeedFactor(attackSpeedBonus);

        // Faktor auf erlittenen Schaden: 1 − Reduktion (begrenzt)
        public static float DamageTakenFactor(float reduction) => 1f - Clamp(SupportBuffKind.DamageReduction, reduction);
    }

    // Satz aller Unterstützungs-Buffs einer Figur (PlayerAbilities.Buffs) – eine Stelle pro Art
    public class SupportBuffSet
    {
        private readonly SupportBuff[] _buffs = new SupportBuff[Enum.GetValues(typeof(SupportBuffKind)).Length];

        public void Apply(SupportBuffKind kind, float value, float duration, float now)
        {
            int i = (int)kind;
            if (i < 0 || i >= _buffs.Length) return;
            _buffs[i] = SupportBuffRules.Apply(_buffs[i], SupportBuffRules.Clamp(kind, value), duration, now);
        }

        public float Current(SupportBuffKind kind, float now)
        {
            int i = (int)kind;
            return i >= 0 && i < _buffs.Length ? SupportBuffRules.Current(_buffs[i], now) : 0f;
        }

        public float Remaining(SupportBuffKind kind, float now)
        {
            int i = (int)kind;
            return i >= 0 && i < _buffs.Length && SupportBuffRules.IsActive(_buffs[i], now) ? _buffs[i].Until - now : 0f;
        }

        public float AttackCooldownFactor(float now) => SupportBuffRules.CooldownFactor(Current(SupportBuffKind.AttackSpeed, now));
        public float MoveSpeedFactor(float now) => SupportBuffRules.SpeedFactor(Current(SupportBuffKind.MoveSpeed, now));
        public float DamageTakenFactor(float now) => SupportBuffRules.DamageTakenFactor(Current(SupportBuffKind.DamageReduction, now));

        public void Clear()
        {
            for (int i = 0; i < _buffs.Length; i++) _buffs[i] = default;
        }
    }
}
