using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Skin-Shop: Loadout packen/entpacken (4 Bit je Klasse+Slot, Vorzeichenbit), Kaufregel, Besitzliste
    public class ShopRulesTests
    {
        [Test]
        public void LeeresLoadout_AllesStandard()
        {
            for (int c = 0; c < ShopRules.ClassCount; c++)
                for (int s = 0; s < ShopRules.SlotCount; s++)
                    Assert.AreEqual(0, ShopRules.Get(0, c, s));
        }

        [Test]
        public void With_Get_Rundreise_JederEintrag()
        {
            for (int c = 0; c < ShopRules.ClassCount; c++)
                for (int s = 0; s < ShopRules.SlotCount; s++)
                {
                    int l = ShopRules.With(0, c, s, 9);
                    Assert.AreEqual(9, ShopRules.Get(l, c, s), $"Klasse {c} Slot {s}");
                    // andere Einträge bleiben 0
                    for (int c2 = 0; c2 < ShopRules.ClassCount; c2++)
                        for (int s2 = 0; s2 < ShopRules.SlotCount; s2++)
                            if (c2 != c || s2 != s) Assert.AreEqual(0, ShopRules.Get(l, c2, s2));
                }
        }

        [Test]
        public void Klasse3Slot1_BelegtVorzeichenbit()
        {
            int l = ShopRules.With(0, 3, 1, 15);
            Assert.Less(l, 0, "oberstes Nibble gesetzt → negativ als int");
            Assert.AreEqual(unchecked((int)0xF0000000u), l);
            Assert.AreEqual(15, ShopRules.Get(l, 3, 1));
            Assert.AreEqual(0, ShopRules.Get(l, 3, 0));
            Assert.AreEqual(8, ShopRules.Get(ShopRules.With(0, 3, 1, 8), 3, 1));
        }

        [Test]
        public void AlleBitsGesetzt_Ueberall15()
        {
            for (int c = 0; c < ShopRules.ClassCount; c++)
                for (int s = 0; s < ShopRules.SlotCount; s++)
                    Assert.AreEqual(15, ShopRules.Get(-1, c, s));
            // einen Eintrag löschen, Rest bleibt
            int l = ShopRules.With(-1, 2, 0, 0);
            Assert.AreEqual(0, ShopRules.Get(l, 2, 0));
            Assert.AreEqual(15, ShopRules.Get(l, 2, 1));
            Assert.AreEqual(15, ShopRules.Get(l, 3, 1));
        }

        [Test]
        public void With_UeberschreibtVorhandenenWert()
        {
            int l = ShopRules.With(0, 1, 1, 15);
            l = ShopRules.With(l, 1, 1, 2);
            Assert.AreEqual(2, ShopRules.Get(l, 1, 1));
        }

        [Test]
        public void UngueltigerNetIndex_WirdStandard()
        {
            int l = ShopRules.With(0, 0, 0, 7);
            Assert.AreEqual(0, ShopRules.Get(ShopRules.With(l, 0, 0, 16), 0, 0), "Überlauf");
            Assert.AreEqual(0, ShopRules.Get(ShopRules.With(l, 0, 0, -1), 0, 0), "negativ");
            Assert.AreEqual(0, ShopRules.Get(ShopRules.With(l, 0, 0, int.MaxValue), 0, 0));
            // Überlauf darf den Nachbarn nicht verändern
            int n = ShopRules.With(0, 0, 1, 5);
            n = ShopRules.With(n, 0, 0, 31);
            Assert.AreEqual(5, ShopRules.Get(n, 0, 1));
        }

        [Test]
        public void UngueltigeKlasseOderSlot_Get0_WithUnveraendert()
        {
            Assert.AreEqual(0, ShopRules.Get(-1, 4, 0));
            Assert.AreEqual(0, ShopRules.Get(-1, -1, 0));
            Assert.AreEqual(0, ShopRules.Get(-1, 0, 2));
            Assert.AreEqual(0, ShopRules.Get(-1, 0, -1));
            Assert.AreEqual(1234, ShopRules.With(1234, 4, 0, 3));
            Assert.AreEqual(1234, ShopRules.With(1234, 0, -1, 3));
        }

        [Test]
        public void Pack_Unpack_Rundreise()
        {
            var a = new int[4, 2] { { 1, 2 }, { 3, 4 }, { 5, 6 }, { 7, 15 } };
            int l = ShopRules.Pack(a);
            var b = ShopRules.Unpack(l);
            for (int c = 0; c < 4; c++)
                for (int s = 0; s < 2; s++)
                    Assert.AreEqual(a[c, s], b[c, s]);
            Assert.Less(l, 0);
        }

        [Test]
        public void Pack_UngueltigeWerteUndGroessen()
        {
            Assert.AreEqual(0, ShopRules.Pack(null));
            var small = new int[1, 1] { { 3 } };
            Assert.AreEqual(3, ShopRules.Get(ShopRules.Pack(small), 0, 0));
            var big = new int[6, 3];
            big[5, 2] = 9; big[0, 2] = 9; big[1, 0] = -4; big[2, 1] = 99; big[3, 0] = 6;
            int l = ShopRules.Pack(big);
            Assert.AreEqual(0, ShopRules.Get(l, 1, 0));
            Assert.AreEqual(0, ShopRules.Get(l, 2, 1));
            Assert.AreEqual(6, ShopRules.Get(l, 3, 0));
            Assert.AreEqual(6 << 24, l, "nur gültige Einträge");
        }

        // ---------------- Kauf ----------------

        [Test]
        public void FreeMode_ImmerGratis()
        {
            Assert.IsTrue(ShopRules.CanAcquire(500, 0, true));
            Assert.IsTrue(ShopRules.CanAcquire(500, -10, true));
            Assert.AreEqual(0, ShopRules.BalanceAfter(500, 0, true));
            Assert.AreEqual(42, ShopRules.BalanceAfter(500, 42, true));
        }

        [Test]
        public void MitWaehrung_PreisGegenGuthaben()
        {
            Assert.IsTrue(ShopRules.CanAcquire(100, 100, false));
            Assert.IsFalse(ShopRules.CanAcquire(100, 99, false));
            Assert.IsFalse(ShopRules.CanAcquire(100, -5, false));
            Assert.AreEqual(0, ShopRules.BalanceAfter(100, 100, false));
            Assert.AreEqual(50, ShopRules.BalanceAfter(100, 150, false));
            Assert.AreEqual(99, ShopRules.BalanceAfter(100, 99, false), "nicht leistbar → unverändert");
        }

        [Test]
        public void PreisNullOderNegativ_Gratis()
        {
            Assert.IsTrue(ShopRules.CanAcquire(0, 0, false));
            Assert.IsTrue(ShopRules.CanAcquire(-20, 0, false));
            Assert.AreEqual(7, ShopRules.BalanceAfter(-20, 7, false), "negativer Preis schreibt nichts gut");
        }

        // ---------------- Besitzliste ----------------

        [Test]
        public void Besitzliste_ParseUndJoin()
        {
            Assert.AreEqual(0, ShopRules.ParseIds(null).Count);
            Assert.AreEqual(0, ShopRules.ParseIds("").Count);
            var ids = ShopRules.ParseIds(" a ,b,,a, c ");
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ids);
            Assert.AreEqual("a,b,c", ShopRules.JoinIds(ids));
            Assert.AreEqual("x", ShopRules.JoinIds(new[] { "x", "", null, "y,z", "x" }));
            Assert.AreEqual("", ShopRules.JoinIds(null));
        }
    }
}
