using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Kartenladen nach der Welle (2026-10-16): Preisstaffel, Anteil Solo/Koop, Abbuchung erst nach erfolgreichem
    // Prüfen/Ziehen, Ablehnung ohne Abbuchung.
    public class DraftLedgerTests
    {
        // Teamkasse für die Tests (TrySpend wie EconomyManager.TrySpendShards)
        private float _kasse;
        private int _spendCalls;
        private bool Spend(int price)
        {
            _spendCalls++;
            if (_kasse < price) return false;
            _kasse -= price;
            return true;
        }

        private DraftLedger Open(float kasse, int spieler, int gratisNeuwuerfe = 0)
        {
            _kasse = kasse;
            _spendCalls = 0;
            var l = new DraftLedger(gratisNeuwuerfe);
            l.Open(kasse, spieler);
            return l;
        }

        [Test]
        public void Kartenpreis_Staffel_Gratis_60_90_120_150()
        {
            Assert.AreEqual(0, GoldRules.DraftCardPrice(0));
            Assert.AreEqual(60, GoldRules.DraftCardPrice(1));
            Assert.AreEqual(90, GoldRules.DraftCardPrice(2));
            Assert.AreEqual(120, GoldRules.DraftCardPrice(3));
            Assert.AreEqual(150, GoldRules.DraftCardPrice(4));
        }

        [Test]
        public void Anteil_Solo_GanzeKasse_Koop_GeteiltAbgerundet()
        {
            Assert.AreEqual(230, GoldRules.DraftShare(230f, 1));
            Assert.AreEqual(115, GoldRules.DraftShare(230f, 2));
            Assert.AreEqual(115, GoldRules.DraftShare(231f, 2)); // Rest bleibt in der Kasse
            Assert.AreEqual(76, GoldRules.DraftShare(230f, 3));
            Assert.AreEqual(0, GoldRules.DraftShare(-5f, 2));
            Assert.AreEqual(230, GoldRules.DraftShare(230f, 0)); // 0 Spieler wie Solo
            Assert.AreEqual(100, GoldRules.DraftShare(100.9f, 1));
        }

        [Test]
        public void Solo_GratisKarte_DannZweiKaeufe_BuchtAb_StaffelSteigt()
        {
            var l = Open(200f, 1);
            Assert.AreEqual(DraftDenial.None, l.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(0, l.LastPrice);
            Assert.AreEqual(0, _spendCalls, "Gratis-Karte bucht nichts ab");
            Assert.AreEqual(200f, _kasse);
            Assert.AreEqual(60, l.NextCardPrice);

            Assert.AreEqual(DraftDenial.None, l.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(140f, _kasse);
            Assert.AreEqual(140, l.Share);
            Assert.AreEqual(DraftDenial.None, l.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(50f, _kasse);
            Assert.AreEqual(50, l.Share);
            Assert.AreEqual(3, l.Picks);
            Assert.AreEqual(120, l.NextCardPrice);

            // 4. Karte (120) reicht nicht → abgelehnt, nichts abgebucht
            int calls = _spendCalls;
            Assert.AreEqual(DraftDenial.Share, l.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(50f, _kasse);
            Assert.AreEqual(calls, _spendCalls);
            Assert.AreEqual(3, l.Picks);
        }

        [Test]
        public void Koop_ZweiSpieler_JederNurSeinAnteil()
        {
            _kasse = 240f;
            var a = new DraftLedger(0);
            var b = new DraftLedger(0);
            a.Open(_kasse, 2);
            b.Open(_kasse, 2);
            Assert.AreEqual(120, a.Share);
            Assert.AreEqual(120, b.Share);

            // A: gratis + 60 → Rest 60; die nächste (90) übersteigt den Anteil, obwohl die Kasse noch 180 hat
            Assert.AreEqual(DraftDenial.None, a.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(DraftDenial.None, a.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(180f, _kasse);
            Assert.AreEqual(60, a.Share);
            Assert.AreEqual(DraftDenial.Share, a.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(180f, _kasse);

            // B hat seine eigene Staffel und seinen vollen Anteil
            Assert.AreEqual(0, b.NextCardPrice);
            Assert.AreEqual(DraftDenial.None, b.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(DraftDenial.None, b.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(120f, _kasse);
            Assert.AreEqual(60, b.Share);
        }

        [Test]
        public void Koop_KasseSchrumpftNebenher_TeamAblehnungOhneAbbuchung()
        {
            var l = Open(240f, 2);
            Assert.AreEqual(DraftDenial.None, l.TryBuy(_kasse, () => true, Spend)); // gratis
            _kasse = 40f; // Mitspieler hat inzwischen Buddies gebaut
            Assert.AreEqual(40, l.Spendable(_kasse));
            Assert.AreEqual(DraftDenial.Team, l.TryBuy(_kasse, () => true, Spend));
            Assert.AreEqual(40f, _kasse);
            Assert.AreEqual(120, l.Share);
            Assert.AreEqual(1, l.Picks);
        }

        [Test]
        public void Kauf_UnzulaessigeKarte_KeineAbbuchung_StaffelBleibt()
        {
            var l = Open(300f, 1);
            Assert.AreEqual(DraftDenial.None, l.TryBuy(_kasse, () => true, Spend));
            int calls = _spendCalls;
            Assert.AreEqual(DraftDenial.Unavailable, l.TryBuy(_kasse, () => false, Spend)); // z. B. zweite Build-Karte
            Assert.AreEqual(calls, _spendCalls);
            Assert.AreEqual(300f, _kasse);
            Assert.AreEqual(1, l.Picks);
            Assert.AreEqual(60, l.NextCardPrice);
        }

        [Test]
        public void Kauf_AbbuchungScheitert_NichtsUebernommen()
        {
            var l = Open(300f, 1);
            l.TryBuy(_kasse, () => true, Spend);
            Assert.AreEqual(DraftDenial.Team, l.TryBuy(_kasse, () => true, p => false));
            Assert.AreEqual(1, l.Picks);
            Assert.AreEqual(300, l.Share);
        }

        [Test]
        public void Neuwurf_ErstGratis_Dann25_40_AusDemAnteil()
        {
            var l = Open(100f, 1, gratisNeuwuerfe: 1);
            Assert.IsTrue(l.NextRerollFree);
            Assert.AreEqual(DraftDenial.None, l.TryReroll(_kasse, () => true, Spend));
            Assert.AreEqual(0, l.LastPrice);
            Assert.AreEqual(0, _spendCalls);
            Assert.AreEqual(0, l.FreeRerolls);

            Assert.AreEqual(25, l.NextRerollPrice);
            Assert.AreEqual(DraftDenial.None, l.TryReroll(_kasse, () => true, Spend));
            Assert.AreEqual(75f, _kasse);
            Assert.AreEqual(40, l.NextRerollPrice);
            Assert.AreEqual(DraftDenial.None, l.TryReroll(_kasse, () => true, Spend));
            Assert.AreEqual(35f, _kasse);
            Assert.AreEqual(35, l.Share);
            // 55 > 35 → abgelehnt, ohne zu ziehen und ohne Abbuchung
            bool gezogen = false;
            Assert.AreEqual(DraftDenial.Share, l.TryReroll(_kasse, () => { gezogen = true; return true; }, Spend));
            Assert.IsFalse(gezogen);
            Assert.AreEqual(35f, _kasse);
        }

        [Test]
        public void Neuwurf_NichtsZuZiehen_KeineAbbuchung_GratisBleibt()
        {
            var l = Open(100f, 1, gratisNeuwuerfe: 1);
            Assert.AreEqual(DraftDenial.NothingToDraw, l.TryReroll(_kasse, () => false, Spend));
            Assert.AreEqual(1, l.FreeRerolls);
            l.FreeRerolls = 0;
            Assert.AreEqual(DraftDenial.NothingToDraw, l.TryReroll(_kasse, () => false, Spend));
            Assert.AreEqual(0, _spendCalls);
            Assert.AreEqual(100f, _kasse);
            Assert.AreEqual(0, l.PaidRerolls);
        }

        [Test]
        public void Neuwurf_Zieht_VorDerAbbuchung()
        {
            var l = Open(100f, 1);
            int order = 0, drawAt = -1, spendAt = -1;
            l.TryReroll(_kasse, () => { drawAt = order++; return true; }, p => { spendAt = order++; return Spend(p); });
            Assert.AreEqual(0, drawAt);
            Assert.AreEqual(1, spendAt);
        }

        [Test]
        public void Kauf_UndNeuwurf_TeilenDenAnteil_NeueWahlSetztZurueck()
        {
            var l = Open(150f, 1, gratisNeuwuerfe: 0);
            l.TryBuy(_kasse, () => true, Spend);          // gratis
            l.TryReroll(_kasse, () => true, Spend);       // 25
            l.TryBuy(_kasse, () => true, Spend);          // 60
            Assert.AreEqual(65, l.Share);
            Assert.AreEqual(65f, _kasse);
            Assert.AreEqual(DraftDenial.Share, l.TryBuy(_kasse, () => true, Spend)); // 90 > 65

            l.Open(_kasse, 1); // nächste Welle
            Assert.AreEqual(65, l.Share);
            Assert.AreEqual(0, l.Picks);
            Assert.AreEqual(0, l.NextCardPrice);
            Assert.AreEqual(25, l.NextRerollPrice);
        }
    }
}
