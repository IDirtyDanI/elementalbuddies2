using System.Collections.Generic;
using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // #461 (2026-10-16): Liegende Seelensplitter werden beim Wellenende einmal gutgeschrieben – vor dem Anteil der
    // Kartenwahl, nach Wellenbonus/Zinsen. Beim Eintreffen des (nur noch optischen) Einflugs wird nichts doppelt gebucht.
    public class ShardRecallTests
    {
        private class Drop { public float Value; public bool Credited; }

        private static float Credit(List<Drop> drops) =>
            ShardRecall.CreditAll(drops, d => d.Value, d => d.Credited, d => d.Credited = true);

        private static List<Drop> Drops(params float[] values)
        {
            var l = new List<Drop>();
            foreach (var v in values) l.Add(new Drop { Value = v });
            return l;
        }

        [Test]
        public void Solo_Anteil_enthaelt_alle_liegenden_Splitter()
        {
            float kasse = 150f;                    // nach Wellenbonus und Zinsen
            var drops = Drops(4.2f, 12.5f, 40f, 2.1f);
            kasse += Credit(drops);                // EconomyManager.RecallShards
            var l = new DraftLedger();
            l.Open(kasse, 1);                      // UpgradeManager.PresentUpgrades
            Assert.AreEqual(208, l.Share);         // ⌊150 + 58,8⌋
            Assert.AreEqual(208, l.Spendable(kasse));
        }

        [Test]
        public void Koop_Anteil_je_Spieler_enthaelt_Rueckruf_geteilt()
        {
            float kasse = 182f;
            kasse += Credit(Drops(30f, 11f));      // +41 → 223
            var a = new DraftLedger(); a.Open(kasse, 2);
            var b = new DraftLedger(); b.Open(kasse, 2);
            Assert.AreEqual(111, a.Share);         // ⌊223 / 2⌋, Rest 1 bleibt in der Kasse
            Assert.AreEqual(111, b.Share);
            Assert.AreEqual(GoldRules.DraftShare(182f, 2) + 20, a.Share); // 91 + ⌊41/2⌋
        }

        [Test]
        public void Betrag_pro_Welle_unveraendert_nur_frueher()
        {
            var drops = Drops(5f, 7.5f, 120f);
            // vorher: Gutschrift erst beim Eintreffen nach der Kartenwahl
            float vorher = 0f;
            foreach (var d in drops) vorher += ShardRecall.CreditOnCollect(d.Value, d.Credited);
            // jetzt: einmal beim Wellenende, beim Eintreffen nichts mehr
            float jetzt = Credit(drops);
            foreach (var d in drops) jetzt += ShardRecall.CreditOnCollect(d.Value, d.Credited);
            Assert.AreEqual(vorher, jetzt, 0.0001f);
            Assert.AreEqual(132.5f, jetzt, 0.0001f);
        }

        [Test]
        public void Doppelter_Rueckruf_bucht_nicht_doppelt()
        {
            var drops = Drops(10f, 20f);
            Assert.AreEqual(30f, Credit(drops), 0.0001f);
            Assert.AreEqual(0f, Credit(drops), 0.0001f);
            drops.Add(new Drop { Value = 6f });    // nach dem Rückruf neu gefallener Drop
            Assert.AreEqual(6f, Credit(drops), 0.0001f);
        }

        [Test]
        public void Leere_und_wertlose_Drops_zaehlen_nicht()
        {
            Assert.AreEqual(0f, ShardRecall.CreditAll<Drop>(null, d => d.Value, d => d.Credited, d => d.Credited = true));
            var drops = Drops(0f, -3f);
            Assert.AreEqual(0f, Credit(drops), 0.0001f);
            Assert.IsFalse(drops[0].Credited);
            Assert.AreEqual(0f, ShardRecall.CreditOnCollect(0f, false));
            Assert.AreEqual(9f, ShardRecall.CreditOnCollect(9f, false));
        }
    }
}
