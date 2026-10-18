using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Treffer-Effekte je Fähigkeit (Spieltest 2026-10-18, Paket V): Budget gegen Massen-Treffer (HitVfxBudget)
    public class HitVfxBudgetTests
    {
        [Test]
        public void JeAusloesung_HoechstensMaxPerTrigger()
        {
            var b = new HitVfxBudget(8, 100f, 100f);
            int shown = 0;
            for (int enemy = 0; enemy < 30; enemy++)
                if (b.TryShow(enemy, 3, 0.2f, 10f, 1)) shown++;
            Assert.AreEqual(8, shown, "Flächenzauber auf 30 Gegnern: 8 Effekte, Rest nur Aufblitzen");
            // nächster Frame = neue Auslösung
            Assert.IsTrue(b.TryShow(100, 3, 0.2f, 10.02f, 2));
        }

        [Test]
        public void VerschiedeneFaehigkeiten_ZaehlenGetrennt()
        {
            var b = new HitVfxBudget(2, 100f, 100f);
            Assert.IsTrue(b.TryShow(1, 0, 0f, 1f, 5));
            Assert.IsTrue(b.TryShow(2, 0, 0f, 1f, 5));
            Assert.IsFalse(b.TryShow(3, 0, 0f, 1f, 5));
            Assert.IsTrue(b.TryShow(3, 7, 0f, 1f, 5), "andere Fähigkeit im selben Frame");
        }

        [Test]
        public void GlobalerEimer_LaeuftNach()
        {
            var b = new HitVfxBudget(0, 4f, 10f); // ohne Auslöse-Grenze
            int shown = 0;
            for (int i = 0; i < 10; i++) if (b.TryShow(i, 1, 0f, 0f, i)) shown++;
            Assert.AreEqual(4, shown, "Burst");
            Assert.IsFalse(b.TryShow(50, 1, 0f, 0.05f, 20), "0,05 s → erst 0,5 Plätze");
            Assert.IsTrue(b.TryShow(51, 1, 0f, 0.1f, 21), "0,1 s × 10/s = 1 Platz");
            for (int i = 0; i < 10; i++) b.TryShow(60 + i, 1, 0f, 100f, 30 + i);
            Assert.LessOrEqual(b.Tokens, 4f, "Eimer läuft nicht über");
        }

        [Test]
        public void GleicherGegner_GleicheFaehigkeit_Mindestabstand()
        {
            var b = new HitVfxBudget(8, 100f, 100f);
            Assert.IsTrue(b.TryShow(7, 14, 0.6f, 1f, 1));
            Assert.IsFalse(b.TryShow(7, 14, 0.6f, 1.3f, 2), "Glutsturm-Tick nach 0,3 s");
            Assert.IsTrue(b.TryShow(7, 15, 0.6f, 1.3f, 2), "andere Fähigkeit am selben Gegner");
            Assert.IsTrue(b.TryShow(8, 14, 0.6f, 1.3f, 2), "anderer Gegner");
            Assert.IsTrue(b.TryShow(7, 14, 0.6f, 1.61f, 3), "nach 0,61 s wieder");
        }

        [Test]
        public void NegativeInstanzIds_KollidierenNicht()
        {
            var b = new HitVfxBudget(8, 100f, 100f);
            Assert.IsTrue(b.TryShow(-12345, 2, 1f, 0f, 1));
            Assert.IsTrue(b.TryShow(-12344, 2, 1f, 0f, 1));
            Assert.IsFalse(b.TryShow(-12345, 2, 1f, 0.5f, 2));
        }
    }
}
