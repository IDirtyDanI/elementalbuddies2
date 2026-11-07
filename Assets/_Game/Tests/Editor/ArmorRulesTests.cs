using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Champion-Rüstung (Balancing 2026-11-04): Schwertkämpfer 20 %, Begrenzung 0 … MaxArmor
    public class ArmorRulesTests
    {
        private const float Eps = 0.001f;

        [Test]
        public void OhneRuestung_SchadenUnveraendert()
        {
            Assert.AreEqual(30f, ArmorRules.Reduce(30f, 0f), Eps);
        }

        [Test]
        public void Ruestung20Prozent_Bleiben80Prozent()
        {
            Assert.AreEqual(80f, ArmorRules.Reduce(100f, 0.2f), Eps);
            Assert.AreEqual(16f, ArmorRules.Reduce(20f, 0.2f), Eps);
        }

        [Test]
        public void RuestungUeberMaximum_WirdBegrenzt()
        {
            Assert.AreEqual(100f * (1f - ArmorRules.MaxArmor), ArmorRules.Reduce(100f, 0.95f), Eps);
            Assert.AreEqual(100f * (1f - ArmorRules.MaxArmor), ArmorRules.Reduce(100f, 5f), Eps);
            Assert.AreEqual(ArmorRules.MaxArmor, ArmorRules.Clamp(1f), Eps);
        }

        [Test]
        public void NegativeRuestung_ZaehltAlsNull()
        {
            Assert.AreEqual(50f, ArmorRules.Reduce(50f, -0.3f), Eps);
            Assert.AreEqual(0f, ArmorRules.Clamp(-1f), Eps);
            Assert.AreEqual(50f, ArmorRules.Reduce(50f, float.NaN), Eps);
        }

        [Test]
        public void KeinSchaden_BleibtNull()
        {
            Assert.AreEqual(0f, ArmorRules.Reduce(0f, 0.2f), Eps);
            Assert.AreEqual(0f, ArmorRules.Reduce(-5f, 0.2f), Eps); // nie negativ
        }
    }
}
