using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Champion-Auswahl im Hauptmenü als Karte mit Pfeilen (2026-11-09): Umlauf, Richtung, Wischen, Positionsanzeige
    public class ChampionCarouselRulesTests
    {
        [Test]
        public void Blaettern_laeuft_endlos_um()
        {
            Assert.AreEqual(0, ChampionCarouselRules.Step(4, 1, 5));  // nach der letzten kommt die erste
            Assert.AreEqual(4, ChampionCarouselRules.Step(0, -1, 5)); // vor der ersten kommt die letzte
            Assert.AreEqual(2, ChampionCarouselRules.Step(1, 1, 5));
            Assert.AreEqual(0, ChampionCarouselRules.Step(6, 1, 7));  // keine feste Anzahl
            Assert.AreEqual(1, ChampionCarouselRules.Wrap(-9, 5));
        }

        [Test]
        public void Leere_oder_einzelne_Liste_bleibt_bei_null()
        {
            Assert.AreEqual(0, ChampionCarouselRules.Step(0, 1, 0));
            Assert.AreEqual(0, ChampionCarouselRules.Step(0, -1, 1));
            Assert.AreEqual(0, ChampionCarouselRules.Direction(0, 0, 1));
        }

        [Test]
        public void Richtung_nimmt_den_kuerzeren_Weg()
        {
            Assert.AreEqual(1, ChampionCarouselRules.Direction(4, 0, 5));  // Umlauf vorwärts
            Assert.AreEqual(-1, ChampionCarouselRules.Direction(0, 4, 5)); // Umlauf rückwärts
            Assert.AreEqual(1, ChampionCarouselRules.Direction(1, 3, 5));
            Assert.AreEqual(-1, ChampionCarouselRules.Direction(3, 1, 5));
            Assert.AreEqual(0, ChampionCarouselRules.Direction(2, 2, 5));
            Assert.AreEqual(1, ChampionCarouselRules.Direction(0, 1, 2));  // gleich weit → vorwärts
        }

        [Test]
        public void Wischen_nach_links_blaettert_vor()
        {
            Assert.AreEqual(1, ChampionCarouselRules.SwipeStep(-120f, 10f, 0.2f, 50f));
            Assert.AreEqual(-1, ChampionCarouselRules.SwipeStep(120f, -10f, 0.2f, 50f));
        }

        [Test]
        public void Kurz_langsam_oder_steil_ist_kein_Wischen()
        {
            Assert.AreEqual(0, ChampionCarouselRules.SwipeStep(-30f, 0f, 0.2f, 50f));  // zu kurz (Tippen)
            Assert.AreEqual(0, ChampionCarouselRules.SwipeStep(-200f, 0f, 1.5f, 50f)); // zu langsam (Halten)
            Assert.AreEqual(0, ChampionCarouselRules.SwipeStep(-120f, 100f, 0.2f, 50f)); // eher senkrecht
        }

        [Test]
        public void Positionsanzeige_Punkte_oder_Zahl()
        {
            Assert.IsTrue(ChampionCarouselRules.UseDots(5));
            Assert.IsTrue(ChampionCarouselRules.UseDots(ChampionCarouselRules.MaxDots));
            Assert.IsFalse(ChampionCarouselRules.UseDots(ChampionCarouselRules.MaxDots + 1));
            Assert.IsFalse(ChampionCarouselRules.UseDots(1));
            Assert.AreEqual("3 / 5", ChampionCarouselRules.PositionText(2, 5));
            Assert.AreEqual("1 / 12", ChampionCarouselRules.PositionText(12, 12));
        }

        [Test]
        public void Easing_beginnt_bei_null_und_endet_bei_eins()
        {
            Assert.AreEqual(0f, ChampionCarouselRules.EaseOut(0f), 1e-5f);
            Assert.AreEqual(1f, ChampionCarouselRules.EaseOut(1f), 1e-5f);
            Assert.AreEqual(1f, ChampionCarouselRules.EaseIn(2f), 1e-5f);
            Assert.Greater(ChampionCarouselRules.EaseOut(0.5f), ChampionCarouselRules.EaseIn(0.5f));
        }
    }
}
