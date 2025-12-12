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
        ManaCap
    }
}