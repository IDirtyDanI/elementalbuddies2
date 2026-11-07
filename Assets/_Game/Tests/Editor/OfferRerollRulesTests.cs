using System.Collections.Generic;
using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Neu würfeln (Händler + Kartenwahl, 2026-11-02): immer alle Plätze, bisher sichtbare Karten meiden
    public class OfferRerollRulesTests
    {
        private static int First(int n) => 0;

        [Test]
        public void Alle_Plaetze_werden_ersetzt()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, OfferRerollRules.SlotsToReroll(3, 3));
            CollectionAssert.AreEqual(new[] { 0, 1 }, OfferRerollRules.SlotsToReroll(2, 3));
            // leeres Angebot → Standardanzahl neuer Plätze
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, OfferRerollRules.SlotsToReroll(0, 3));
        }

        [Test]
        public void Neue_Karten_meiden_die_bisherigen()
        {
            var drawn = OfferRerollRules.DrawFresh(new[] { 1, 2, 3 }, new[] { 1, 2, 3, 4, 5, 6 }, 3, First);
            CollectionAssert.AreEquivalent(new[] { 4, 5, 6 }, drawn);
        }

        [Test]
        public void Kleiner_Pool_darf_bisherige_wiederholen_ohne_Doppelte()
        {
            var drawn = OfferRerollRules.DrawFresh(new[] { 1, 2, 3 }, new[] { 1, 2, 3, 4 }, 3, First);
            Assert.AreEqual(3, drawn.Count);
            CollectionAssert.AllItemsAreUnique(drawn);
        }

        [Test]
        public void Pool_zu_klein_liefert_weniger()
        {
            var drawn = OfferRerollRules.DrawFresh(new[] { 1 }, new[] { 1, 2 }, 3, First);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, drawn);
        }

        [Test]
        public void Zufall_wird_genutzt()
        {
            var calls = new List<int>();
            OfferRerollRules.DrawFresh(null, new[] { 7, 8, 9 }, 2, n => { calls.Add(n); return n - 1; });
            CollectionAssert.AreEqual(new[] { 3, 2 }, calls);
        }
    }
}
