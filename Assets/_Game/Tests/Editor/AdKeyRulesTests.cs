using System;
using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Werbe-Schlüssel (iOS-Plan 2026-11-15): leere Platzhalter = kein Anbieter
    public class AdKeyRulesTests
    {
        [Test]
        public void Ad_Units_passen_zu_AdReward()
        {
            Assert.AreEqual(Enum.GetValues(typeof(AdReward)).Length, AdKeyRules.RewardCount);
        }

        [Test]
        public void Vollstaendige_Schluessel_gelten()
        {
            Assert.IsTrue(AdKeyRules.IsConfigured("2935f9bed", new[] { "a", "b", "c" }));
        }

        [Test]
        public void Leere_Platzhalter_gelten_nicht()
        {
            Assert.IsFalse(AdKeyRules.IsConfigured("", new[] { "", "", "" }));
            Assert.IsFalse(AdKeyRules.IsConfigured("key", new[] { "", "", "" }));
            Assert.IsFalse(AdKeyRules.IsConfigured("", new[] { "a", "b", "c" }));
            Assert.IsFalse(AdKeyRules.IsConfigured("key", new[] { "a", " ", "c" }));
            Assert.IsFalse(AdKeyRules.IsConfigured("key", new[] { "a", "b" }));
            Assert.IsFalse(AdKeyRules.IsConfigured("key", null));
            Assert.IsFalse(AdKeyRules.IsConfigured(null, new[] { "a", "b", "c" }));
        }
    }
}
