namespace ElementalBuddies
{
    // Werbe-Schlüssel je Plattform (LevelPlayAds, iOS-Plan 2026-11-15): nur mit App-Key und einer Ad-Unit je AdReward
    // gibt es einen Anbieter. Leere Platzhalter (iOS, bis die App im LevelPlay-Portal existiert) = keine Werbung.
    public static class AdKeyRules
    {
        public const int RewardCount = 3; // AdReward: Reroll, BuddySlot, Continue

        public static bool IsConfigured(string appKey, string[] adUnits)
        {
            if (string.IsNullOrWhiteSpace(appKey) || adUnits == null || adUnits.Length < RewardCount) return false;
            for (int i = 0; i < RewardCount; i++)
                if (string.IsNullOrWhiteSpace(adUnits[i])) return false;
            return true;
        }
    }
}
