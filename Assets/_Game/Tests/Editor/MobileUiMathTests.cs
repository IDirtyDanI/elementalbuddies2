using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Mobil-UI (Paket U): Canvas-Skalierung nach DPI/Geräteklasse, sicherer Bereich, Einpassen, Top-Bar, Touch-Flächen
    public class MobileUiMathTests
    {
        private const float Eps = 0.01f;

        // ---------------- Canvas-Skalierung ----------------

        [Test]
        public void CanvasScale_WieUnity_Match05()
        {
            // 1920 × 1080 bei Referenz 2400 × 1350 → 0,8; 2868 × 1320 → √(1,195 · 0,978) ≈ 1,081
            Assert.AreEqual(0.8f, MobileUiMath.CanvasScale(1920f, 1080f, 2400f, 1350f, 0.5f), Eps);
            Assert.AreEqual(1.081f, MobileUiMath.CanvasScale(2868f, 1320f, 2400f, 1350f, 0.5f), Eps);
        }

        [Test]
        public void CanvasScale_MatchHoehe()
        {
            Assert.AreEqual(1320f / 1350f, MobileUiMath.CanvasScale(2868f, 1320f, 2400f, 1350f, 1f), Eps);
        }

        [Test]
        public void Diagonale_iPhoneUndTablet()
        {
            Assert.AreEqual(6.86f, MobileUiMath.DiagonalInches(2868f, 1320f, 460f), 0.02f);
            Assert.AreEqual(10.99f, MobileUiMath.DiagonalInches(1920f, 1200f, 206f), 0.02f);
            Assert.AreEqual(0f, MobileUiMath.DiagonalInches(1920f, 1200f, 0f), Eps);
        }

        [Test]
        public void Boost_iPhone16ProMax_DurchLayoutGrenzeGedeckelt()
        {
            // Ziel 1,60 px/Einheit, aber der sichere Bereich (1257 px hoch) muss 860 Einheiten fassen → 1,462
            float k = MobileUiMath.Boost(2868f, 1320f, 2496f, 1257f, 460f);
            Assert.AreEqual(1.352f, k, Eps);
            float s = MobileUiMath.CanvasScale(2868f, 1320f, 2400f, 1350f, 0.5f) * k;
            Assert.AreEqual(860f, 1257f / s, 1f);
        }

        [Test]
        public void Boost_TabM11_NachDpi()
        {
            Assert.AreEqual(1.106f, MobileUiMath.Boost(1920f, 1200f, 1920f, 1200f, 206f), Eps);
            // Meldet das Gerät 240 dpi, fällt die Diagonale kleiner aus → etwas mehr, aber gedeckelt
            float k240 = MobileUiMath.Boost(1920f, 1200f, 1920f, 1200f, 240f);
            Assert.Greater(k240, 1.106f);
            Assert.LessOrEqual(k240, 1.27f);
        }

        [Test]
        public void Boost_TelefonDeutlichGroesserAlsTablet()
        {
            float phone = MobileUiMath.Boost(2868f, 1320f, 2496f, 1257f, 460f);
            float tablet = MobileUiMath.Boost(1920f, 1200f, 1920f, 1200f, 206f);
            Assert.Greater(phone, tablet + 0.15f);
        }

        [Test]
        public void Boost_NieKleinerAlsEins()
        {
            // Großer Bildschirm mit wenig DPI (Desktop im erzwungenen Touch-Modus, fehlende DPI → 160)
            Assert.AreEqual(1f, MobileUiMath.Boost(2560f, 1440f, 2560f, 1440f, 96f), Eps);
            Assert.AreEqual(1f, MobileUiMath.Boost(1920f, 1080f, 1920f, 1080f, 160f), Eps);
            Assert.AreEqual(1f, MobileUiMath.Boost(0f, 0f, 0f, 0f, 400f), Eps);
        }

        [Test]
        public void Boost_HoechstensMaxBoost()
        {
            // Winziges Telefon mit sehr hoher DPI: Ziel riesig, aber Deckel 1,6
            float k = MobileUiMath.Boost(4000f, 1800f, 4000f, 1800f, 800f);
            Assert.LessOrEqual(k, MobileUiMath.PhoneMaxBoost + Eps);
        }

        [Test]
        public void BoostedReference_Teilt()
        {
            Vector2 r = MobileUiMath.BoostedReference(new Vector2(2400f, 1350f), 1.5f);
            Assert.AreEqual(1600f, r.x, Eps);
            Assert.AreEqual(900f, r.y, Eps);
        }

        // ---------------- Sicherer Bereich ----------------

        [Test]
        public void Insets_iPhoneQuer()
        {
            var safe = new Rect(186f, 63f, 2496f, 1257f);
            Vector4 i = MobileUiMath.Insets(2868f, 1320f, safe);
            Assert.AreEqual(186f, i.x, Eps);
            Assert.AreEqual(63f, i.y, Eps);
            Assert.AreEqual(186f, i.z, Eps);
            Assert.AreEqual(0f, i.w, Eps);
        }

        [Test]
        public void Insets_LeererBereich_Null()
        {
            Assert.AreEqual(Vector4.zero, MobileUiMath.Insets(1920f, 1080f, new Rect(0f, 0f, 0f, 0f)));
            Assert.AreEqual(Vector4.zero, MobileUiMath.Insets(1920f, 1080f, new Rect(0f, 0f, 1920f, 1080f)));
        }

        // ---------------- Einpassen ----------------

        [Test]
        public void FitScale_PauseMenueAufDemTelefon()
        {
            // Box 1560 × 940 (Mitte −20) → halbe Ausdehnung 780 × 490; Platz 1708 × 860 minus 2 · 16 Rand
            float f = MobileUiMath.FitScale(new Vector2(780f, 490f), new Vector2(838f, 414f));
            Assert.AreEqual(414f / 490f, f, Eps);
        }

        [Test]
        public void FitScale_PasstSchon_Eins_UndMindestwert()
        {
            Assert.AreEqual(1f, MobileUiMath.FitScale(new Vector2(380f, 235f), new Vector2(838f, 414f)), Eps);
            Assert.AreEqual(0.5f, MobileUiMath.FitScale(new Vector2(4000f, 4000f), new Vector2(100f, 100f)), Eps);
            Assert.AreEqual(1f, MobileUiMath.FitScale(Vector2.zero, new Vector2(100f, 100f)), Eps);
        }

        // ---------------- Top-Bar ----------------

        [Test]
        public void BarRequiredWidth_HUDTopBar()
        {
            // Links bis 1068 (Gold), Mitte 5 … 460, rechts 600, Abstand 16 → 2158
            Assert.AreEqual(2158f, MobileUiMath.BarRequiredWidth(1068f, 5f, 460f, 600f, 16f), Eps);
            Assert.AreEqual(1f, MobileUiMath.BarFit(2158f, 2300f), Eps);
            Assert.AreEqual(1684f / 2158f, MobileUiMath.BarFit(2158f, 1684f), Eps);
            Assert.AreEqual(0.6f, MobileUiMath.BarFit(2158f, 100f), Eps);
        }

        [Test]
        public void DpToUnits_48dpAufDemTelefon()
        {
            // 460 dpi, Canvas-Skalierung 1,462 → 48 dp = 138 px = 94 Einheiten
            Assert.AreEqual(94.4f, MobileUiMath.DpToUnits(48f, 460f, 1.462f), 0.2f);
        }

        // ---------------- Langes Drücken ----------------

        [Test]
        public void LongPress_ZeitUndVerrutschen()
        {
            Assert.IsFalse(MobileUiMath.IsLongPress(0.39f, 0f, 460f));
            Assert.IsTrue(MobileUiMath.IsLongPress(0.4f, 10f, 460f));
            // 14 dp bei 460 dpi = 40 px
            Assert.IsTrue(MobileUiMath.IsLongPress(1f, 40f, 460f));
            Assert.IsFalse(MobileUiMath.IsLongPress(1f, 41f, 460f));
            Assert.IsTrue(MobileUiMath.MovedTooFar(41f, 460f));
        }

        // ---------------- Touch-Trefferflächen ----------------

        [Test]
        public void TouchPadding_OhneNachbarn_Symmetrisch()
        {
            var r = new Rect(100f, 100f, 40f, 40f);
            Vector4 p = MobileUiMath.TouchPadding(r, new Vector2(140f, 140f), new Rect[0], 0);
            Assert.AreEqual(new Vector4(50f, 50f, 50f, 50f), p);
        }

        [Test]
        public void TouchPadding_GrossGenug_Null()
        {
            var r = new Rect(0f, 0f, 300f, 160f);
            Assert.AreEqual(Vector4.zero, MobileUiMath.TouchPadding(r, new Vector2(140f, 140f), null, 0));
        }

        [Test]
        public void TouchPadding_NachbarNimmtHalbeLuecke()
        {
            // Zwei Knöpfe übereinander, 18 px Lücke: jeder wächst nur 9 px aufeinander zu, nach außen voll
            var lower = new Rect(0f, 0f, 540f, 82f);
            var upper = new Rect(0f, 100f, 540f, 82f);
            Vector4 p = MobileUiMath.TouchPadding(lower, new Vector2(140f, 140f), new[] { upper }, 1);
            Assert.AreEqual(9f, p.w, Eps);   // oben
            Assert.AreEqual(29f, p.y, Eps);  // unten (140 − 82) / 2
            Assert.AreEqual(0f, p.x, Eps);   // breit genug
            Vector4 q = MobileUiMath.TouchPadding(upper, new Vector2(140f, 140f), new[] { lower }, 1);
            Assert.AreEqual(9f, q.y, Eps);
            Assert.LessOrEqual(lower.yMax + p.w, upper.yMin - q.y + Eps); // keine Überschneidung
        }

        [Test]
        public void TouchPadding_UeberlappendeIgnoriert_EntfernteIgnoriert()
        {
            var r = new Rect(100f, 100f, 40f, 40f);
            var parent = new Rect(0f, 0f, 500f, 500f);   // überlappt → keine Grenze
            var far = new Rect(1000f, 1000f, 40f, 40f);  // weder Zeile noch Spalte
            Vector4 p = MobileUiMath.TouchPadding(r, new Vector2(140f, 140f), new[] { parent, far }, 2);
            Assert.AreEqual(new Vector4(50f, 50f, 50f, 50f), p);
        }

        [Test]
        public void TouchPadding_NebeneinanderInDerZeile()
        {
            // Schwierigkeits-Knöpfe: 172 × 52, 12 px Lücke → seitlich nichts nötig, vertikal frei
            var a = new Rect(0f, 0f, 172f, 52f);
            var b = new Rect(184f, 0f, 172f, 52f);
            Vector4 p = MobileUiMath.TouchPadding(a, new Vector2(140f, 140f), new[] { b }, 1);
            Assert.AreEqual(0f, p.z, Eps);
            Assert.AreEqual(44f, p.y, Eps);
            Assert.AreEqual(44f, p.w, Eps);
        }
    }
}
