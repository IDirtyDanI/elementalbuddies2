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
    }
}
