using System;

namespace ElementalBuddies
{
    // Belohnung für ein freiwilliges Werbevideo (Mobil, Plan Werbung-Plan im Wiki, 2026-11-02)
    public enum AdReward { Reroll = 0, BuddySlot = 1, Continue = 2 }

    // Regeln für freiwillige Werbung (Nutzer-Entscheidungen 2026-11-02): nur Einzelspiel, feste Deckel pro Spiel
    // (3 Neuwürfe, 1 Buddy-Platz ab Welle 6, 1× Weiterspielen am Nexus). Rein, damit EditMode-Tests sie prüfen können.
    public static class AdRules
    {
        public const int MaxRerolls = 3;
        public const int MaxBuddySlots = 1;
        public const int MaxContinues = 1;
        // Buddy-Platz erst nach so vielen abgeschlossenen Wellen (früh wären +25 % Turmleistung zu stark)
        public const int BuddySlotFromWave = 6;
        // Weiterspielen: Nexus-Leben danach (Anteil der Max-LP) und Umkreis, in dem Gegner vernichtet werden
        public const float ContinueNexusHp = 0.5f;
        public const float ContinueClearRadius = 14f;
        public const float ContinueGraceSeconds = 3f;

        public static int Cap(AdReward r)
        {
            switch (r)
            {
                case AdReward.Reroll: return MaxRerolls;
                case AdReward.BuddySlot: return MaxBuddySlots;
                default: return MaxContinues;
            }
        }

        // Darf die Belohnung jetzt angeboten werden? used = in diesem Spiel schon erhalten
        public static bool CanOffer(AdReward r, int used, bool multiplayer, int completedWaves, bool slotsAtCap)
        {
            if (multiplayer || used >= Cap(r)) return false;
            if (r == AdReward.BuddySlot) return completedWaves >= BuddySlotFromWave && !slotsAtCap;
            return true;
        }
    }

    // Was in diesem Spiel schon per Video geholt wurde (neues Spiel = neuer Zähler)
    public sealed class AdRunCounter
    {
        private readonly int[] _used = new int[Enum.GetValues(typeof(AdReward)).Length];

        public int Used(AdReward r) => _used[(int)r];
        public int Remaining(AdReward r) => Math.Max(0, AdRules.Cap(r) - _used[(int)r]);
        public void Use(AdReward r) => _used[(int)r]++;
        public void Reset() => Array.Clear(_used, 0, _used.Length);
    }
}
