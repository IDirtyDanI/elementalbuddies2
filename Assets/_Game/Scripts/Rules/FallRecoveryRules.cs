namespace ElementalBuddies
{
    // Sicherheitsnetz „durch die Map gefallen“ (#527, 2026-11-07): Auf den Dächern der Stadt liegt NavMesh, also gilt ein Dach
    // als sichere Position. Wer von einem hohen Dach (Taverne 8,3 m, Wachturm 9,5 m) springt, fällt mehr als FallRecoverDepth
    // unter diese Höhe und wurde kurz vor der Landung aufs Dach zurückgesetzt. Darum nur noch zurücksetzen, wenn unter der
    // Figur gar kein Boden mehr ist (unter dem Gelände durchgefallen oder über den Kartenrand).
    public static class FallRecoveryRules
    {
        public static bool ShouldRecover(float y, float safeFloorY, float recoverDepth, bool groundBelow)
        {
            if (groundBelow) return false;
            return y <= safeFloorY - recoverDepth;
        }
    }
}
