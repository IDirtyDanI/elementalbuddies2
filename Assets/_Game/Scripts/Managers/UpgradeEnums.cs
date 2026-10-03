namespace ElementalBuddies
{
    public enum UpgradeType
    {
        StatIncrease, 
        Heal,         
        ManaBoost     
    }

    public enum UpgradeTarget
    {
        Player,
        Global,
        FireUnit,
        IceUnit,
        EarthUnit,
        LightUnit,
        AllUnits
    }

    public enum StatType
    {
        None,
        Damage,
        Range,
        FireRate,
        Health,
        Speed,
        ManaRegen,
        ManaCap,
        BuddySlot,
        Cooldown,  // Spieler: Abklingzeiten aller Fähigkeiten (Wert = Prozent schneller)
        Mobility   // Spieler: Blink-Reichweite / Rollen-Distanz
    }
}