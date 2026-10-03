using System;
using UnityEngine;

namespace ElementalBuddies
{
    // Werte, die Händlerkarten an einer einzelnen Fähigkeit verändern. Neue Einträge nur hinten anhängen (serialisiert).
    public enum AbilityStat
    {
        Damage,   // Schaden (Prozent)
        Area,     // Größe / Radius / Kegel (Prozent)
        Range,    // Reichweite / Distanz / Länge (Prozent)
        Cooldown, // Abklingzeit (Prozent schneller)
        Duration, // Effektdauer: Brand, Frost, Betäubung, Spott, Falle, Blendung, Unverwundbarkeit (Prozent)
        Charges,  // Aufladungen (flach)
        Pierce,   // zusätzlich durchschlagene Gegner (flach)
        Speed,    // Tempo: Angriffstempo / Projektiltempo / Lauftempo beim Blocken (Prozent)
        Angle,    // Bogen- / Blockwinkel in Grad (flach)
        ManaCost, // Manakosten (Prozent günstiger)
        Count,    // Anzahl: Pfeil-Wellen, gefangene Gegner (flach)
        Heal,     // Heilung (Prozent)
        Length    // Länge in Metern (flach), z. B. Steinwall
    }

    public enum AbilityStatKind
    {
        Percent,   // Faktor = 1 + Summe/100
        Reduction, // Faktor = 1 − Summe/100 (Untergrenze MinReductionFactor)
        Flat       // Wert + Summe
    }

    // Fähigkeits-Modifikatoren des Spielers (Händlerkarten). Pro (AbilityId, AbilityStat) wird die Summe der
    // Kartenwerte gespeichert; gleiche Karten stapeln sich additiv (2× +20 % = +40 %). Die Kits lesen die Werte
    // über Apply/Factor genau dort, wo sie Schaden, Radius, Reichweite usw. berechnen. Globale Upgrade-Faktoren
    // (PlayerAbilities.DamageMultiplier/CooldownMultiplier/MobilityMultiplier) wirken multiplikativ dazu.
    public class AbilityMods
    {
        public const float MinReductionFactor = 0.25f;

        private static readonly int AbilityCount = Enum.GetValues(typeof(AbilityId)).Length;
        private static readonly int StatCount = Enum.GetValues(typeof(AbilityStat)).Length;

        private readonly float[,] _sum = new float[AbilityCount, StatCount];
        private readonly bool[] _any = new bool[AbilityCount];

        public event Action OnChanged;

        public static AbilityStatKind KindOf(AbilityStat stat)
        {
            switch (stat)
            {
                case AbilityStat.Cooldown:
                case AbilityStat.ManaCost:
                    return AbilityStatKind.Reduction;
                case AbilityStat.Charges:
                case AbilityStat.Pierce:
                case AbilityStat.Angle:
                case AbilityStat.Count:
                case AbilityStat.Length:
                    return AbilityStatKind.Flat;
                default:
                    return AbilityStatKind.Percent;
            }
        }

        public float Sum(AbilityId id, AbilityStat stat) => _sum[(int)id, (int)stat];

        public bool HasAny(AbilityId id) => _any[(int)id];

        // Multiplikator eines Prozent-/Reduktions-Werts (flache Werte: 1)
        public float Factor(AbilityId id, AbilityStat stat)
        {
            float s = Sum(id, stat);
            switch (KindOf(stat))
            {
                case AbilityStatKind.Percent: return Mathf.Max(0.05f, 1f + s / 100f);
                case AbilityStatKind.Reduction: return Mathf.Max(MinReductionFactor, 1f - s / 100f);
                default: return 1f;
            }
        }

        // Basiswert mit Modifikator (Prozent/Reduktion multiplikativ, flach additiv)
        public float Apply(AbilityId id, AbilityStat stat, float baseValue)
        {
            return KindOf(stat) == AbilityStatKind.Flat ? baseValue + Sum(id, stat) : baseValue * Factor(id, stat);
        }

        public int ApplyInt(AbilityId id, AbilityStat stat, int baseValue)
        {
            return Mathf.RoundToInt(Apply(id, stat, baseValue));
        }

        public void Add(AbilityId id, AbilityStat stat, float value)
        {
            _sum[(int)id, (int)stat] += value;
            RecalcAny((int)id);
            OnChanged?.Invoke();
        }

        // Ohne Event (Vorschau-Rechnung: kurz hinzufügen, ablesen, wieder abziehen)
        public void AddSilently(AbilityId id, AbilityStat stat, float value)
        {
            _sum[(int)id, (int)stat] += value;
            RecalcAny((int)id);
        }

        public void Clear()
        {
            Array.Clear(_sum, 0, _sum.Length);
            for (int i = 0; i < _any.Length; i++) _any[i] = false;
            OnChanged?.Invoke();
        }

        private void RecalcAny(int id)
        {
            _any[id] = false;
            for (int s = 0; s < StatCount; s++)
                if (Mathf.Abs(_sum[id, s]) > 0.0001f) { _any[id] = true; return; }
        }

        // ---------------- Anzeige ----------------

        // Kurzname des Werts (Kartentitel, Pause-Übersicht)
        public static string StatName(AbilityStat stat)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return "Schaden";
                case AbilityStat.Area: return "Größe";
                case AbilityStat.Range: return "Reichweite";
                case AbilityStat.Cooldown: return "Abklingzeit";
                case AbilityStat.Duration: return "Dauer";
                case AbilityStat.Charges: return "Aufladungen";
                case AbilityStat.Pierce: return "Durchschlag";
                case AbilityStat.Speed: return "Tempo";
                case AbilityStat.Angle: return "Winkel";
                case AbilityStat.ManaCost: return "Manakosten";
                case AbilityStat.Count: return "Anzahl";
                case AbilityStat.Heal: return "Heilung";
                default: return "Länge";
            }
        }

        // Wert einer Karte als Text: "+20 %", "−15 %", "+1", "+20°", "+2 m"
        public static string FormatValue(AbilityStat stat, float value)
        {
            string num = ChampionKit.Fmt(Mathf.Abs(value));
            switch (KindOf(stat))
            {
                case AbilityStatKind.Percent: return (value >= 0f ? "+" : "−") + num + " %";
                case AbilityStatKind.Reduction: return (value >= 0f ? "−" : "+") + num + " %";
            }
            string sign = value >= 0f ? "+" : "−";
            if (stat == AbilityStat.Angle) return sign + num + "°";
            if (stat == AbilityStat.Length) return sign + num + " m";
            return sign + num;
        }

        // Dateiname der Modifikator-Plakette (Assets/_Game/UI/Icons/badge_*.png)
        public static string BadgeName(AbilityStat stat)
        {
            switch (stat)
            {
                case AbilityStat.Damage: return "damage";
                case AbilityStat.Area:
                case AbilityStat.Angle:
                case AbilityStat.Length: return "area";
                case AbilityStat.Range: return "range";
                case AbilityStat.Cooldown: return "cooldown";
                case AbilityStat.Duration: return "duration";
                case AbilityStat.Charges:
                case AbilityStat.Count: return "charge";
                case AbilityStat.Pierce: return "pierce";
                case AbilityStat.Speed: return "speed";
                case AbilityStat.ManaCost: return "mana";   // Fallback-Datei: icon_mana.png
                case AbilityStat.Heal: return "heal";       // Fallback-Datei: icon_heart.png
                default: return "";
            }
        }
    }
}
