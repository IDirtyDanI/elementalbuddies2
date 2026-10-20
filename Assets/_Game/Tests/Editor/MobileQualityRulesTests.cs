using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Mobil-Grafik (Paket P): Geräteklasse, Render Scale je Gerät, Bildraten-Rückfall
    public class MobileQualityRulesTests
    {
        private const float Eps = 0.001f;

        [Test]
        public void Classify_TabM11_Low_iPhone_High()
        {
            Assert.AreEqual(MobileQualityRules.Tier.Low, MobileQualityRules.Classify(false, "Mali-G52 MC2", 3700));
            Assert.AreEqual(MobileQualityRules.Tier.High, MobileQualityRules.Classify(true, "Apple A18 Pro GPU", 7800));
            Assert.AreEqual(MobileQualityRules.Tier.High, MobileQualityRules.Classify(false, "Adreno (TM) 740", 8000));
            // starke GPU, aber wenig Speicher → vorsichtig
            Assert.AreEqual(MobileQualityRules.Tier.Low, MobileQualityRules.Classify(false, "Adreno (TM) 740", 4000));
            Assert.AreEqual(MobileQualityRules.Tier.Low, MobileQualityRules.Classify(false, null, 0));
        }

        [Test]
        public void RenderScale_JeGeraet()
        {
            // Tab M11: Tablet, 1200 px kurze Seite → 0,8 (Deckel 1080/1200 = 0,9 greift nicht)
            Assert.AreEqual(0.8f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.Low, false, 207f, 1200f), Eps);
            // Android-Telefon mit hoher DPI → 0,7, sonst 0,75
            Assert.AreEqual(0.7f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.Low, true, 450f, 1080f), Eps);
            Assert.AreEqual(0.75f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.Low, true, 320f, 720f), Eps);
            // iPhone 16 Pro Max → 0,85 (kein Pixel-Deckel)
            Assert.AreEqual(0.85f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.High, true, 460f, 1320f), Eps);
            // Android mit sehr hoher Auflösung: kurze Seite höchstens 1080 px
            Assert.AreEqual(1080f / 1600f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.Low, false, 280f, 1600f), Eps);
        }

        [Test]
        public void Fallback_ErstRenderScaleDann30fps()
        {
            float s = 0.7f;
            int fps = 60;
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(0.65f, s, Eps);
            Assert.AreEqual(60, fps);
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(MobileQualityRules.MinRenderScale, s, Eps);
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(30, fps);
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(30, fps);
            Assert.AreEqual(MobileQualityRules.MinRenderScale, s, Eps);
        }

        [Test]
        public void Budgets_Mobil_KleinerAlsPc()
        {
            Assert.Less(MobileQualityRules.VfxBudgetScale(MobileQualityRules.Tier.Low), MobileQualityRules.VfxBudgetScale(MobileQualityRules.Tier.High));
            Assert.LessOrEqual(MobileQualityRules.VfxBudgetScale(MobileQualityRules.Tier.High), 1f);
            Assert.Less(MobileQualityRules.FireLights(MobileQualityRules.Tier.Low), 6);
            Assert.Less(MobileQualityRules.DetailDensityScale(MobileQualityRules.Tier.Low), 1f);
        }
    }
}
