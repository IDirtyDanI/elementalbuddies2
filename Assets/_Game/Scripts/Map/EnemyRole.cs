namespace ElementalBuddies
{
    // Rolle eines Gegners in der Wellenlogik (Walddorf-Plan, Paket P0 – Schnittstelle für C-Theme/C-Flieger).
    // Die heutigen Untoten-Configs sind die kanonische Vorlage je Rolle; ein Gegner-Theme (EnemyThemeSO) ordnet jeder
    // Rolle eine eigene Config zu. Reihenfolge nur anhängen (Werte können serialisiert werden).
    public enum EnemyRole
    {
        Runner,
        Swarm,
        Knight,
        Archer,
        Spider,
        Flyer,
        BossMelee,
        BossCaster,
        BossHunter,
        Ram,
        // Champion-Bedrohungen (2026-10-17): nur der Champion kann sie verletzen (Bannschild)
        Summoner, // Beschwörer: errichtet abseits einen Ritualkreis und ruft Verstärkung
        Hexer,    // Bannwirker: bannt Buddy-Gruppen (keine Aktionen), solange er lebt und nah genug ist
        // Dimensionsriss (2026-11-14): Boss der anderen Dimension (MapDefinitionSO.DimensionBoss). Eigene Rolle, damit kein
        // Gegner-Theme die Config austauscht (Themes ordnen diese Rolle nicht zu) und er nicht als normale Boss-Art zählt.
        DimensionBoss,
    }
}
