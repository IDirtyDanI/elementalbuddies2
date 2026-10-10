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
        Mobility,  // Spieler: Blink-Reichweite / Rollen-Distanz
        ShardGain  // Seelensplitter-Drops (Wert = Prozent mehr, additiv)
    }

    // Seltenheit einer Wellenkarte (Plan „Fesselung“ D2): bestimmt Draft-Wahrscheinlichkeit und Rahmen
    public enum CardRarity
    {
        Common,
        Rare,
        Epic
    }

    // Wozu eine Karte passt (Draft-Garantie D6): gebautes Element, Champion oder neutral (Wirtschaft/alle Buddies)
    public enum CardAffinity
    {
        Neutral,
        Fire,
        Ice,
        Earth,
        Light,
        Champion
    }

    // Sonderwirkungen jenseits einfacher Werte (D3/D4); Werte in CardEffects. Nur hinten anhängen (serialisiert).
    public enum CardEffect
    {
        None,
        BuddyHealth,       // Buddy-Leben +Value %
        WaveEndHeal,       // Wellenende-Heilung der Buddies +Value Prozentpunkte
        FireIgnite,        // Feuer-Buddy-Treffer: Brand mit Value % des Treffers pro s, Value2 s
        SteamShock,        // Brennende Gegner: +Value % Schaden von Eis-Buddies
        IceFreeze,         // Eis-Buddies: jeder Value. Schuss friert Value2 s ein
        FrostShatter,      // Eingefrorene zersplittern beim Tod: Value % ihres Max-Lebens im Radius Value2
        TauntVulnerable,   // Gespottete Gegner: +Value % Schaden
        EarthSlow,         // Erd-Aura verlangsamt um Value %
        SlowVulnerable,    // Verlangsamte/eingefrorene Gegner: +Value % Schaden von Buddies
        LightCurse,        // Licht-Strahl: Gegner erleidet Value2 s lang +Value % Schaden (Fluch)
        WetVulnerable,     // Nasse Gegner: +Value % Schaden
        EliteBounty,       // Elite- und Boss-Kills: +Value % Splitter
        Interest,          // Wellenende: +Value % der Splitter (höchstens Value2)
        GlassCannon,       // Buddies +Value % Schaden, −Value2 % Leben
        Loner,             // −Value2 Buddy-Slots, Buddies +Value % Schaden
        Harmony,           // Buddies +Value % Schaden je gebautem Basis-Element (Vielfalt statt Monokultur)
        BloodPact          // Champion: −Value2 % Max-Leben (Schaden über StatToBuff)
    }
}
