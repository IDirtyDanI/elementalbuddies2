namespace ElementalBuddies
{
    // Alle Spieler-Fähigkeiten aller Champions (Cooldown-API in PlayerAbilities, AbilityBarUI, Tooltips).
    // Neue Einträge nur hinten anhängen – die Werte sind serialisiert.
    public enum AbilityId
    {
        // Magier
        ArcaneBall, // LMB
        Blink,      // RMB
        FireWave,   // R - Flammenwelle (Feuer, Element 0)
        FrostNova,  // F - Frostnova (Eis, Element 1)
        StoneWall,  // C - Steinwall (Erde, Element 2)
        HolyCircle, // V - Heiliger Kreis (Licht, Element 3)

        // Schwertkämpfer
        SwordSlash,   // LMB - Schwerthieb (3er-Kombo)
        ShieldBlock,  // RMB (halten) - Schildblock
        FlameWhirl,   // R - Flammenwirbel
        FrostStrike,  // F - Frostschlag
        Earthquake,   // C - Erdbeben
        LightOath,    // V - Lichtschwur

        // Bogenschütze
        ArrowShot,     // LMB - Pfeilschuss
        Roll,          // RMB - Rolle (2 Aufladungen)
        FireArrowRain, // R - Feuerpfeil-Regen
        FrostArrow,    // F - Frostpfeil
        ThornTrap,     // C - Dornenfalle
        LightArrow     // V - Lichtpfeil
    }

    // Tastenbelegung, gleich für alle Champions. Reihenfolge = alte AbilityBar-Reihenfolge (serialisiert).
    public enum AbilitySlot
    {
        Primary,   // Linke Maustaste
        Secondary, // Rechte Maustaste
        Fire,      // R
        Ice,       // F
        Earth,     // C
        Light      // V
    }

    public static class AbilitySlots
    {
        public const int Count = 6;

        // -1 für LMB/RMB, sonst Element-Index 0..3
        public static int ElementOf(AbilitySlot slot)
        {
            int i = (int)slot - 2;
            return i >= 0 ? i : -1;
        }

        public static AbilitySlot OfElement(int elementIndex)
        {
            return (AbilitySlot)(UnityEngine.Mathf.Clamp(elementIndex, 0, 3) + 2);
        }

        // Kurzbeschriftung für die Leiste
        public static string ShortKey(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Primary: return "LMB";
                case AbilitySlot.Secondary: return "RMB";
                case AbilitySlot.Fire: return "R";
                case AbilitySlot.Ice: return "F";
                case AbilitySlot.Earth: return "C";
                default: return "V";
            }
        }

        // Ausgeschrieben für Tooltips / Pause-Übersicht
        public static string KeyName(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Primary: return "Linke Maustaste";
                case AbilitySlot.Secondary: return "Rechte Maustaste";
                default: return "Taste " + ShortKey(slot);
            }
        }
    }
}
