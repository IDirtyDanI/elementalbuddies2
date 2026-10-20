using UnityEngine;

namespace ElementalBuddies
{
    // Wie eine Fähigkeit auf Touch gezielt wird (Mobil, Paket S). Point = Zielpunkt am Boden (Blink, Feuerpfeil-Regen,
    // Dornenfalle, Sprünge – die Kits nutzen ctx.AimPoint), Direction = nur Richtung (Wellen, Pfeile, Kegel, Steinwall,
    // Ansturm, Rolle), None = wirkt um die Figur (Novas, Wirbel, Schwüre) – Antippen löst sofort aus.
    public enum TouchAimKind { None, Direction, Point }

    public static class TouchAbilityInfo
    {
        public static TouchAimKind KindOf(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.Blink:
                case AbilityId.FireArrowRain:
                case AbilityId.ThornTrap:
                case AbilityId.Earthquake:
                case AbilityId.Landslide:
                    return TouchAimKind.Point;

                case AbilityId.ArcaneBall:
                case AbilityId.SwordSlash:
                case AbilityId.ArrowShot:
                case AbilityId.AxeCleave:
                case AbilityId.FireWave:
                case AbilityId.StoneWall:
                case AbilityId.FrostStrike:
                case AbilityId.FrostArrow:
                case AbilityId.LightArrow:
                case AbilityId.IceBreaker:
                case AbilityId.BerserkCharge:
                case AbilityId.Roll:
                    return TouchAimKind.Direction;

                default:
                    return TouchAimKind.None;
            }
        }

        // Rückfall-Reichweiten (m), falls ein Kit den Wert nicht als Karten-Stat liefert
        private static float DefaultRange(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.Blink: return 8f;
                case AbilityId.FireArrowRain: return 16f;
                case AbilityId.ThornTrap: return 10f;
                case AbilityId.Earthquake: return 3.5f;
                case AbilityId.Landslide: return 8f;
                case AbilityId.ArcaneBall: return 14f;
                case AbilityId.SwordSlash: return 2.6f;
                case AbilityId.ArrowShot: return 24f;
                case AbilityId.AxeCleave: return 2.9f;
                case AbilityId.FrostArrow: return 24f;
                case AbilityId.LightArrow: return 25f;
                case AbilityId.Roll: return 4.5f;
                case AbilityId.BerserkCharge: return 6f;
                case AbilityId.StoneWall: return 4f;
                default: return 7f;
            }
        }

        // Maximale Reichweite für Zielvorschau und Auto-Ziel (inkl. Händlerkarten/Mobilität, soweit das Kit sie meldet)
        public static float RangeOf(PlayerAbilities abilities, AbilityId id)
        {
            var kit = abilities != null ? abilities.ActiveKit : null;
            if (kit != null)
            {
                if (kit.TryGetStatValue(id, AbilityStat.Range, out float v, out string unit, out _) && v > 0f && IsMeters(unit)) return v;
                var spell = kit.GetModdedSpell(id);
                if (spell is LandslideSpell ls) return ls.LeapDistance * Mathf.Max(0.1f, abilities.MobilityMultiplier);
                if (spell is ThornTrapSpell tt) return tt.MaxCastRange;
                if (spell is FireArrowRainSpell fr) return fr.MaxCastRange;
            }
            return DefaultRange(id);
        }

        // Wirkradius am Zielpunkt (Kreis der Zielvorschau); ohne Angabe ein kleiner Marker
        public static float AreaRadiusOf(PlayerAbilities abilities, AbilityId id)
        {
            var kit = abilities != null ? abilities.ActiveKit : null;
            if (kit != null && kit.TryGetStatValue(id, AbilityStat.Area, out float v, out string unit, out _) && v > 0f && IsMeters(unit))
                return v;
            return 0.8f;
        }

        private static bool IsMeters(string unit) => unit != null && unit.Trim() == "m";
    }
}
