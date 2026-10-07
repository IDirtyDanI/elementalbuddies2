using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    public class GoldRulesTests
    {
        [TestCase(60f, 1, 60f)]
        [TestCase(60f, 2, 30f)]
        [TestCase(60f, 3, 20f)]
        [TestCase(60f, 4, 15f)]
        [TestCase(50f, 3, 50f / 3f)]
        public void SplitCaptureBonus_TeiltGleichmaessig(float total, int count, float expected)
        {
            Assert.AreEqual(expected, GoldRules.SplitCaptureBonus(total, count), 0.0001f);
        }

        [Test]
        public void SplitCaptureBonus_SummeBleibtErhalten()
        {
            for (int n = 1; n <= 4; n++)
                Assert.AreEqual(60f, GoldRules.SplitCaptureBonus(60f, n) * n, 0.001f);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void SplitCaptureBonus_OhneEinnehmende_Null(int count)
        {
            Assert.AreEqual(0f, GoldRules.SplitCaptureBonus(60f, count));
        }

        [Test]
        public void CardPrice_Staffel_GratisDann60_90_120()
        {
            Assert.AreEqual(0, GoldRules.CardPrice(0, 60, 30));
            Assert.AreEqual(60, GoldRules.CardPrice(1, 60, 30));
            Assert.AreEqual(90, GoldRules.CardPrice(2, 60, 30));
            Assert.AreEqual(120, GoldRules.CardPrice(3, 60, 30));
            Assert.AreEqual(150, GoldRules.CardPrice(4, 60, 30));
        }

        [Test]
        public void RerollPrice_Staffel_25_40_55()
        {
            Assert.AreEqual(25, GoldRules.RerollPrice(0, 25, 15));
            Assert.AreEqual(40, GoldRules.RerollPrice(1, 25, 15));
            Assert.AreEqual(55, GoldRules.RerollPrice(2, 25, 15));
        }

        [Test]
        public void CaptureContributor_ImKreisOderGenugZeit()
        {
            Assert.IsTrue(GoldRules.IsCaptureContributor(0f, true, 2f));
            Assert.IsTrue(GoldRules.IsCaptureContributor(2f, false, 2f));
            Assert.IsFalse(GoldRules.IsCaptureContributor(1.9f, false, 2f));
        }

        [Test]
        public void WaitReason_Format()
        {
            Assert.AreEqual("Warte auf Kartenwahl (2/3)", GoldRules.WaitReason("Kartenwahl", 2, 3));
        }
    }
}
