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

        // Taugt die Position als sichere Position? Nur, wenn der nächste NavMesh-Punkt waagerecht höchstens tolerance
        // entfernt ist (die Figur steht wirklich auf begehbarem Boden, nicht auf einer Kante hinter Mauer oder Kartenrand).
        public static bool IsOverWalkable(UnityEngine.Vector3 position, UnityEngine.Vector3 navPoint, float tolerance)
        {
            float dx = position.x - navPoint.x, dz = position.z - navPoint.z;
            return dx * dx + dz * dz <= tolerance * tolerance;
        }
    }
}
