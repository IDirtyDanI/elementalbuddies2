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
        LightArrow,    // V - Lichtpfeil

        // Berserker (Klasse 3 – MerchantCardSO.ClassOf/SlotOf rechnen id / 6 bzw. id % 6)
        AxeCleave,      // LMB - Axthieb (3er-Kombo)
        BerserkCharge,  // RMB - Ansturm
        EmberStorm,     // R - Glutsturm
        IceBreaker,     // F - Eisbrecher
        Landslide,      // C - Bergsturz
        LightFrenzy     // V - Lichtrausch
    }

    // Fähigkeits-Slots, gleich für alle Champions (Tasten belegbar, KeyBindings). Reihenfolge = alte AbilityBar-Reihenfolge (serialisiert).
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

        // Kurzbeschriftung der aktuellen Belegung (HUD-Plakette, Pause-Übersicht), z. B. „LMT“, „R“, „Leer“
        public static string ShortKey(AbilitySlot slot) => KeyBindings.Short(KeyBindings.OfSlot(slot));

        // Ausgeschrieben für Tooltips: „Linke Maustaste“, „Taste R“, „Leertaste“
        public static string KeyName(AbilitySlot slot) => KeyBindings.Long(KeyBindings.OfSlot(slot));

        // Standard-Kürzel wie in den ChampionDefinitionSO-Assets („LMB“, „RMB“, „R“ …) – nur zum Zuordnen, nicht zur Anzeige
        public static string DefaultKey(AbilitySlot slot)
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

        // Asset-Kürzel („LMB“ …) → Slot
        public static bool TryParseDefaultKey(string key, out AbilitySlot slot)
        {
            for (int i = 0; i < Count; i++)
                if (string.Equals(DefaultKey((AbilitySlot)i), key, System.StringComparison.OrdinalIgnoreCase))
                {
                    slot = (AbilitySlot)i;
                    return true;
                }
            slot = AbilitySlot.Primary;
            return false;
        }
    }
}
