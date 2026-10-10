using UnityEngine;

namespace ElementalBuddies
{
    // Händler-Flüche (Plan „Fesselung“ D7): Ab Welle 13 ist gelegentlich eine Karte im Laden verflucht. Sie wirkt
    // doppelt, bringt aber einen Nachteil für das ganze Team. Nur hinten anhängen (byte, übers Netz).
    public enum MerchantCurse : byte
    {
        None,
        EliteSurge, // nächste Welle +2 Eliten
        BloodToll,  // Nexus verliert sofort 10 % seines Max-Lebens
        Debt,       // nächster Wellenbonus halbiert
    }

    public static class MerchantCurses
    {
        public const int Count = 3;
        public const int EliteSurgeCount = 2;
        public const float BloodTollFraction = 0.10f;
        public const float DebtFactor = 0.5f;

        public static string Name(MerchantCurse c)
        {
            switch (c)
            {
                case MerchantCurse.EliteSurge: return "Elitenruf";
                case MerchantCurse.BloodToll: return "Blutzoll";
                case MerchantCurse.Debt: return "Splitterschuld";
                default: return "";
            }
        }

        public static string Description(MerchantCurse c)
        {
            switch (c)
            {
                case MerchantCurse.EliteSurge: return $"nächste Welle +{EliteSurgeCount} Eliten";
                case MerchantCurse.BloodToll: return $"der Nexus verliert sofort {BloodTollFraction * 100f:0} % Leben";
                case MerchantCurse.Debt: return "der nächste Wellenbonus ist halbiert";
                default: return "";
            }
        }

        // Kurzform für die kleine Händlerkarte
        public static string Short(MerchantCurse c)
        {
            switch (c)
            {
                case MerchantCurse.EliteSurge: return $"+{EliteSurgeCount} Eliten nächste Welle";
                case MerchantCurse.BloodToll: return $"Nexus −{BloodTollFraction * 100f:0} % Leben";
                case MerchantCurse.Debt: return "nächster Wellenbonus halbiert";
                default: return "";
            }
        }

        // Server: Nachteil sofort bzw. für die nächste Welle wirksam machen
        public static void Apply(MerchantCurse c)
        {
            if (!Net.IsServer) return;
            var wm = WaveManager.Instance;
            switch (c)
            {
                case MerchantCurse.EliteSurge:
                    if (wm != null) wm.AddCurseElites(EliteSurgeCount);
                    break;
                case MerchantCurse.BloodToll:
                    if (Nexus.Instance != null) Nexus.Instance.Sacrifice(Nexus.Instance.MaxHP * BloodTollFraction);
                    break;
                case MerchantCurse.Debt:
                    if (wm != null) wm.AddBonusDebt(DebtFactor);
                    break;
            }
        }
    }
}
