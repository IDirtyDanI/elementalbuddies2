using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Bildschirm wach halten + Pausen-Meldung (Koop-Absturz-Fix 2026-11-02)
    public class ScreenAwakeRulesTests
    {
        [Test]
        public void Im_Spiel_auf_Touch_immer_wach()
        {
            Assert.IsTrue(ScreenAwakeRules.KeepAwake(true, true, false));
            Assert.IsTrue(ScreenAwakeRules.KeepAwake(true, true, true));
        }

        [Test]
        public void Im_Menue_nur_mit_offenem_Online_Raum()
        {
            Assert.IsTrue(ScreenAwakeRules.KeepAwake(true, false, true));
            Assert.IsFalse(ScreenAwakeRules.KeepAwake(true, false, false));
        }

        [Test]
        public void PC_bleibt_unberuehrt()
        {
            Assert.IsFalse(ScreenAwakeRules.KeepAwake(false, true, true));
        }

        [Test]
        public void Lange_Pause_kurz_vor_dem_Verlust_erklaert_ihn()
        {
            Assert.IsTrue(ScreenAwakeRules.PauseExplainsLoss(60f, 1f));
            Assert.IsTrue(ScreenAwakeRules.PauseExplainsLoss(ScreenAwakeRules.PauseExplainsLossSeconds, ScreenAwakeRules.LossAfterResumeWindow));
        }

        [Test]
        public void Kurze_oder_alte_Pause_erklaert_nichts()
        {
            Assert.IsFalse(ScreenAwakeRules.PauseExplainsLoss(3f, 1f));
            Assert.IsFalse(ScreenAwakeRules.PauseExplainsLoss(60f, 120f));
            Assert.IsFalse(ScreenAwakeRules.PauseExplainsLoss(0f, -1f)); // noch nie pausiert
        }
    }
}
