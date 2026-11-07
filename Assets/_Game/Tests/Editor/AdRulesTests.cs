using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Freiwillige Werbung (2026-11-02): Deckel pro Spiel, Buddy-Platz ab Welle 6, nie im Koop
    public class AdRulesTests
    {
        [Test]
        public void Deckel_3_Neuwuerfe_1_Platz_1_Weiterspielen()
        {
            Assert.AreEqual(3, AdRules.Cap(AdReward.Reroll));
            Assert.AreEqual(1, AdRules.Cap(AdReward.BuddySlot));
            Assert.AreEqual(1, AdRules.Cap(AdReward.Continue));
        }

        [Test]
        public void Zaehler_erreicht_den_Deckel()
        {
            var c = new AdRunCounter();
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(AdRules.CanOffer(AdReward.Reroll, c.Used(AdReward.Reroll), false, 1, false));
                c.Use(AdReward.Reroll);
            }
            Assert.IsFalse(AdRules.CanOffer(AdReward.Reroll, c.Used(AdReward.Reroll), false, 1, false));
            Assert.AreEqual(0, c.Remaining(AdReward.Reroll));
            Assert.AreEqual(1, c.Remaining(AdReward.Continue));
            c.Reset();
            Assert.AreEqual(3, c.Remaining(AdReward.Reroll));
        }

        [Test]
        public void Buddy_Platz_erst_ab_Welle_6_und_nicht_am_Slot_Deckel()
        {
            Assert.IsFalse(AdRules.CanOffer(AdReward.BuddySlot, 0, false, 5, false));
            Assert.IsTrue(AdRules.CanOffer(AdReward.BuddySlot, 0, false, 6, false));
            Assert.IsFalse(AdRules.CanOffer(AdReward.BuddySlot, 0, false, 9, true));
            Assert.IsFalse(AdRules.CanOffer(AdReward.BuddySlot, 1, false, 9, false));
        }

        [Test]
        public void Koop_nie()
        {
            foreach (AdReward r in System.Enum.GetValues(typeof(AdReward)))
                Assert.IsFalse(AdRules.CanOffer(r, 0, true, 10, false));
        }

        [Test]
        public void Weiterspielen_mit_halbem_Nexus()
        {
            Assert.AreEqual(0.5f, AdRules.ContinueNexusHp, 0.001f);
            Assert.Greater(AdRules.ContinueClearRadius, 5f);
        }
    }
}
