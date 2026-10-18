using System.Collections.Generic;
using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Spieltest 2026-10-18 (Paket M, Punkt 4): Blink in der Luft scheiterte, weil der NavMesh auf Flughöhe gesucht wurde.
    // Jetzt: in der Luft waagerecht auf aktueller Höhe, gültig nur über begehbarem Boden; sonst schrittweise zurück.
    public class BlinkRulesTests
    {
        [Test]
        public void Am_Boden_gilt_immer_das_Boden_Verhalten()
        {
            Assert.IsFalse(BlinkRules.UseAirBlink(true, 5f));
            Assert.IsFalse(BlinkRules.UseAirBlink(true, float.PositiveInfinity));
        }

        [Test]
        public void In_der_Luft_erst_ab_Mindesthoehe()
        {
            Assert.IsFalse(BlinkRules.UseAirBlink(false, 0.1f));  // isGrounded flackert an Stufe → wie am Boden
            Assert.IsTrue(BlinkRules.UseAirBlink(false, BlinkRules.MinAirHeight));
            Assert.IsTrue(BlinkRules.UseAirBlink(false, 2.5f));   // Sprungscheitel
            Assert.IsTrue(BlinkRules.UseAirBlink(false, float.PositiveInfinity)); // kein Boden unter der Figur
            Assert.IsFalse(BlinkRules.UseAirBlink(false, float.NaN));
        }

        [Test]
        public void Rueckschritte_wie_bisher_von_voller_Strecke_bis_ein_Viertel()
        {
            int n = BlinkRules.RetreatSteps;
            Assert.AreEqual(4, n);
            Assert.AreEqual(0f, BlinkRules.RetreatFraction(0, n), 1e-6f);
            Assert.AreEqual(0.25f, BlinkRules.RetreatFraction(1, n), 1e-6f);
            Assert.AreEqual(0.75f, BlinkRules.RetreatFraction(3, n), 1e-6f);
            Assert.AreEqual(1f, BlinkRules.RetreatFraction(9, n), 1e-6f);   // geklemmt
            Assert.AreEqual(0f, BlinkRules.RetreatFraction(2, 0), 1e-6f);   // keine Schritte → Ziel
        }

        [Test]
        public void Erster_gueltiger_Rueckschritt_wird_gewaehlt()
        {
            var tried = new List<int>();
            int step = BlinkRules.FirstValidStep(4, i => { tried.Add(i); return i >= 2; });
            Assert.AreEqual(2, step);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, tried); // bricht nach dem ersten Treffer ab
        }

        [Test]
        public void Ohne_gueltigen_Boden_bleibt_die_Figur_stehen()
        {
            Assert.AreEqual(-1, BlinkRules.FirstValidStep(4, i => false));
            Assert.AreEqual(-1, BlinkRules.FirstValidStep(4, null));
            Assert.AreEqual(0, BlinkRules.FirstValidStep(4, i => true)); // volle Strecke
        }

        [Test]
        public void Luft_Ziel_haelt_die_Hoehe_und_steckt_nie_im_Boden()
        {
            // Drehpunkt 1,08 m über den Füßen (Kapsel 2 m + Skin 0,08)
            Assert.AreEqual(5f, BlinkRules.AirPivotY(5f, 0f, 1.08f), 1e-5f);    // Boden tief darunter → Höhe halten
            Assert.AreEqual(4.08f, BlinkRules.AirPivotY(3.5f, 3f, 1.08f), 1e-5f); // Boden höher als die Füße → daraufsetzen
            Assert.AreEqual(4.08f, BlinkRules.AirPivotY(4.08f, 3f, 1.08f), 1e-5f);
        }

        [Test]
        public void Luft_Ziel_nur_mit_Boden_hoechstens_eine_Stufe_ueber_den_Fuessen()
        {
            Assert.IsTrue(BlinkRules.AirFloorReachable(0f, 3f));     // Boden tief unter der Figur
            Assert.IsTrue(BlinkRules.AirFloorReachable(3.4f, 3f));   // flache Stufe
            Assert.IsFalse(BlinkRules.AirFloorReachable(4.5f, 3f));  // Mauerkrone in Brusthöhe → kein Hochklettern
        }

        [Test]
        public void Luft_Ziel_hinter_Barriere_mit_langem_Fussweg_ist_verboten()
        {
            Assert.IsTrue(BlinkRules.AirPathShortEnough(8.5f, 8f));    // freie Strecke, kleiner Bogen um eine Kiste
            Assert.IsTrue(BlinkRules.AirPathShortEnough(13f, 8f));     // Umweg um einen Zaun (≤ +6 m)
            Assert.IsTrue(BlinkRules.AirPathShortEnough(7f, 2f));      // kurze Strecke: Umweg bis 6 m erlaubt
            Assert.IsFalse(BlinkRules.AirPathShortEnough(60f, 8f));    // hinter der Palisade: nur durchs Tor erreichbar
        }
    }
}
