using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Touch-Steuerung (Mobil, Paket S): Stick-Werte, Zieh-Zielen, Auto-Ziel, Zwei-Finger-Drehen, Layout
    public class TouchControlMathTests
    {
        private const float Eps = 1e-4f;

        private static void AssertVec(Vector3 expected, Vector3 actual, float eps = Eps)
        {
            Assert.AreEqual(expected.x, actual.x, eps, "x");
            Assert.AreEqual(expected.y, actual.y, eps, "y");
            Assert.AreEqual(expected.z, actual.z, eps, "z");
        }

        // ---------------- Sticks ----------------

        [Test]
        public void Stick_InDerTotzone_IstNull()
        {
            Assert.AreEqual(Vector2.zero, TouchControlMath.StickValue(Vector2.zero, new Vector2(10f, 0f), 100f, 0.15f));
            Assert.AreEqual(Vector2.zero, TouchControlMath.StickValue(Vector2.zero, new Vector2(0f, 15f), 100f, 0.15f));
            Assert.AreEqual(Vector2.zero, TouchControlMath.StickValue(new Vector2(5f, 5f), new Vector2(5f, 5f), 100f, 0.15f));
        }

        [Test]
        public void Stick_HinterDerTotzone_BeginntSanftUndErreichtEinsAmRand()
        {
            Vector2 justOut = TouchControlMath.StickValue(Vector2.zero, new Vector2(16f, 0f), 100f, 0.15f);
            Assert.Greater(justOut.x, 0f);
            Assert.Less(justOut.x, 0.05f);
            Assert.AreEqual(1f, TouchControlMath.StickValue(Vector2.zero, new Vector2(100f, 0f), 100f, 0.15f).magnitude, Eps);
            // Mitte zwischen Totzone und Rand → 0,5
            Assert.AreEqual(0.5f, TouchControlMath.StickValue(Vector2.zero, new Vector2(0f, 57.5f), 100f, 0.15f).y, Eps);
        }

        [Test]
        public void Stick_UeberDenRand_BleibtBeiEins_RichtungStimmt()
        {
            Vector2 v = TouchControlMath.StickValue(new Vector2(200f, 200f), new Vector2(200f - 300f, 200f + 300f), 100f, 0.1f);
            Assert.AreEqual(1f, v.magnitude, Eps);
            Assert.AreEqual(-Mathf.Sqrt(0.5f), v.x, Eps);
            Assert.AreEqual(Mathf.Sqrt(0.5f), v.y, Eps);
        }

        [Test]
        public void Stick_OhneRadius_IstNull()
        {
            Assert.AreEqual(Vector2.zero, TouchControlMath.StickValue(Vector2.zero, new Vector2(50f, 0f), 0f, 0.1f));
        }

        [Test]
        public void Schwebend_MittelpunktFolgtNurUeberDemRand()
        {
            Vector2 o = new Vector2(100f, 100f);
            Assert.AreEqual(o, TouchControlMath.FollowOrigin(o, new Vector2(150f, 100f), 80f));
            Vector2 moved = TouchControlMath.FollowOrigin(o, new Vector2(300f, 100f), 80f);
            AssertVec(new Vector3(220f, 100f, 0f), moved);
            Assert.AreEqual(80f, (new Vector2(300f, 100f) - moved).magnitude, Eps);
        }

        [Test]
        public void Tippen_KurzUndOhneBewegung()
        {
            Assert.IsTrue(TouchControlMath.IsTap(0.12f, 5f, 0.25f, 20f));
            Assert.IsFalse(TouchControlMath.IsTap(0.4f, 5f, 0.25f, 20f));
            Assert.IsFalse(TouchControlMath.IsTap(0.12f, 30f, 0.25f, 20f));
            Assert.IsTrue(TouchControlMath.IsDoubleTap(10f, 10.3f, 0.4f));
            Assert.IsFalse(TouchControlMath.IsDoubleTap(10f, 10.6f, 0.4f));
            Assert.IsFalse(TouchControlMath.IsDoubleTap(10f, 10f, 0.4f));
        }

        // ---------------- Zieh-Zielen ----------------

        [Test]
        public void Ziehen_InDerTotzone_KeinZiel()
        {
            Assert.IsFalse(TouchControlMath.DragToGround(new Vector2(20f, 0f), 40f, 200f, 0f, 10f, 0.1f, out _, out _));
        }

        [Test]
        public void Ziehen_NachOben_IstVorwaerts_RandIstVolleReichweite()
        {
            Assert.IsTrue(TouchControlMath.DragToGround(new Vector2(0f, 200f), 40f, 200f, 0f, 10f, 0.1f, out var dir, out var dist));
            AssertVec(new Vector3(0f, 0f, 1f), dir);
            Assert.AreEqual(10f, dist, Eps);
            // weiter als der Rand → gedeckelt
            TouchControlMath.DragToGround(new Vector2(0f, 900f), 40f, 200f, 0f, 10f, 0.1f, out _, out dist);
            Assert.AreEqual(10f, dist, Eps);
        }

        [Test]
        public void Ziehen_KurzHinterDerTotzone_MindestEntfernung()
        {
            TouchControlMath.DragToGround(new Vector2(41f, 0f), 40f, 200f, 0f, 10f, 0.2f, out var dir, out var dist);
            AssertVec(new Vector3(1f, 0f, 0f), dir);
            Assert.AreEqual(2f, dist, 0.1f);
            // Hälfte des Wegs → Mitte zwischen Mindest- und Maximalentfernung
            TouchControlMath.DragToGround(new Vector2(120f, 0f), 40f, 200f, 0f, 10f, 0.2f, out _, out dist);
            Assert.AreEqual(6f, dist, Eps);
        }

        [Test]
        public void Ziehen_IstKamerabezogen()
        {
            // Kamera um 90° gedreht (schaut nach Osten): Bildschirm-oben = Welt +X, Bildschirm-rechts = Welt −Z
            TouchControlMath.DragToGround(new Vector2(0f, 150f), 40f, 200f, 90f, 10f, 0f, out var dir, out _);
            AssertVec(new Vector3(1f, 0f, 0f), dir);
            TouchControlMath.DragToGround(new Vector2(150f, 0f), 40f, 200f, 90f, 10f, 0f, out dir, out _);
            AssertVec(new Vector3(0f, 0f, -1f), dir);
            // gleiche Abbildung wie das Laufen
            AssertVec(CameraOrbitMath.InputToWorld(new Vector2(0.6f, 0.8f), 37f), RoughDir(new Vector2(60f, 80f) * 2f, 37f));
        }

        private static Vector3 RoughDir(Vector2 offset, float yaw)
        {
            TouchControlMath.DragToGround(offset, 1f, 1000f, yaw, 1f, 0f, out var dir, out _);
            return dir;
        }

        [Test]
        public void Abbrechen_ZurueckAufDenKnopf()
        {
            Assert.IsTrue(TouchControlMath.IsDragging(new Vector2(0f, 60f), 50f));
            Assert.IsFalse(TouchControlMath.IsDragging(new Vector2(0f, 40f), 50f));
            Assert.IsTrue(TouchControlMath.IsCancel(new Vector2(20f, 20f), 40f));
            Assert.IsFalse(TouchControlMath.IsCancel(new Vector2(40f, 40f), 40f));
        }

        // ---------------- Auto-Ziel ----------------

        [Test]
        public void AutoZiel_NaechsterInReichweite()
        {
            var c = new List<Vector3> { new Vector3(0f, 0f, 9f), new Vector3(3f, 0f, 0f), new Vector3(20f, 0f, 0f) };
            Assert.AreEqual(1, TouchControlMath.PickAutoTarget(Vector3.zero, Vector3.zero, c, 12f, 0f));
        }

        [Test]
        public void AutoZiel_AusserReichweite_KeinZiel()
        {
            var c = new List<Vector3> { new Vector3(0f, 0f, 15f), new Vector3(-13f, 0f, 0f) };
            Assert.AreEqual(-1, TouchControlMath.PickAutoTarget(Vector3.zero, Vector3.forward, c, 12f, 0.5f));
            Assert.AreEqual(-1, TouchControlMath.PickAutoTarget(Vector3.zero, Vector3.forward, new List<Vector3>(), 12f, 0.5f));
            Assert.AreEqual(-1, TouchControlMath.PickAutoTarget(Vector3.zero, Vector3.forward, null, 12f, 0.5f));
        }

        [Test]
        public void AutoZiel_VorneLeichtBevorzugt_HoeheZaehltNicht()
        {
            // 5 m vorne vs. 4 m hinten: mit Gewichtung 0,5 zählt hinten wie 6 m → vorne gewinnt
            var c = new List<Vector3> { new Vector3(0f, 0f, -4f), new Vector3(0f, 0f, 5f) };
            Assert.AreEqual(1, TouchControlMath.PickAutoTarget(Vector3.zero, Vector3.forward, c, 12f, 0.5f));
            // ohne Gewichtung: der nähere
            Assert.AreEqual(0, TouchControlMath.PickAutoTarget(Vector3.zero, Vector3.forward, c, 12f, 0f));
            // Höhe (Flieger, Hang) wird ignoriert
            var h = new List<Vector3> { new Vector3(0f, 30f, 3f) };
            Assert.AreEqual(0, TouchControlMath.PickAutoTarget(Vector3.zero, Vector3.forward, h, 4f, 0.5f));
        }

        [Test]
        public void AutoZiel_Suchradius()
        {
            Assert.AreEqual(6f, TouchControlMath.AutoAimRadius(2.6f, 6f, 16f, 10f), Eps);   // Nahkampf → Mindestradius
            Assert.AreEqual(16f, TouchControlMath.AutoAimRadius(24f, 6f, 16f, 10f), Eps);   // Fernkampf → gedeckelt
            Assert.AreEqual(9f, TouchControlMath.AutoAimRadius(8f, 6f, 16f, 10f), Eps);
            Assert.AreEqual(10f, TouchControlMath.AutoAimRadius(0f, 6f, 16f, 10f), Eps);    // unbekannt → Rückfall
        }

        // ---------------- Zwei-Finger-Drehen ----------------

        [Test]
        public void Drehen_GegenDenUhrzeigersinn_IstPositiv()
        {
            Vector2 a = new Vector2(100f, 100f);
            float d = TouchControlMath.TwistDelta(a, a + new Vector2(200f, 0f), a, a + new Vector2(0f, 200f), 50f);
            Assert.AreEqual(90f, d, Eps);
            d = TouchControlMath.TwistDelta(a, a + new Vector2(200f, 0f), a, a + new Vector2(200f, -200f), 50f);
            Assert.AreEqual(-45f, d, Eps);
        }

        [Test]
        public void Drehen_UeberDieMinus180Grenze_KuerzesterWeg()
        {
            Vector2 a = Vector2.zero;
            // von 170° nach −170° = +20°
            Vector2 p = new Vector2(Mathf.Cos(170f * Mathf.Deg2Rad), Mathf.Sin(170f * Mathf.Deg2Rad)) * 300f;
            Vector2 c = new Vector2(Mathf.Cos(-170f * Mathf.Deg2Rad), Mathf.Sin(-170f * Mathf.Deg2Rad)) * 300f;
            Assert.AreEqual(20f, TouchControlMath.TwistDelta(a, p, a, c, 50f), 1e-3f);
        }

        [Test]
        public void Drehen_VerschiebenUndZoomen_DrehtNicht_ZuDicht_Null()
        {
            Vector2 a = new Vector2(100f, 100f), b = new Vector2(300f, 200f);
            Vector2 shift = new Vector2(55f, -30f);
            Assert.AreEqual(0f, TouchControlMath.TwistDelta(a, b, a + shift, b + shift, 50f), Eps);
            Vector2 mid = (a + b) * 0.5f;
            Assert.AreEqual(0f, TouchControlMath.TwistDelta(a, b, mid + (a - mid) * 1.5f, mid + (b - mid) * 1.5f, 50f), Eps);
            Assert.AreEqual(0f, TouchControlMath.TwistDelta(a, a + new Vector2(20f, 0f), a, a + new Vector2(0f, 20f), 50f));
        }

        [Test]
        public void Drehen_WeltFolgtDenFingern()
        {
            // Finger gegen den Uhrzeigersinn → Yaw größer (Kamera schwenkt im Uhrzeigersinn, Welt dreht mit den Fingern)
            Assert.Greater(TouchControlMath.TwistToYaw(10f, 1f), 0f);
            Assert.AreEqual(-5f, TouchControlMath.TwistToYaw(-10f, 0.5f), Eps);
        }

        // ---------------- Layout ----------------

        [Test]
        public void Bogen_PunkteUndAbstand()
        {
            AssertVec(new Vector3(110f, 10f, 0f), TouchControlMath.ArcPoint(new Vector2(10f, 10f), 100f, 0f));
            AssertVec(new Vector3(10f, 110f, 0f), TouchControlMath.ArcPoint(new Vector2(10f, 10f), 100f, 90f));
            AssertVec(new Vector3(-90f, 10f, 0f), TouchControlMath.ArcPoint(new Vector2(10f, 10f), 100f, 180f));
            Vector2 p0 = TouchControlMath.ArcPoint(Vector2.zero, 125f, 70f), p1 = TouchControlMath.ArcPoint(Vector2.zero, 125f, 97f);
            Assert.AreEqual((p1 - p0).magnitude, TouchControlMath.ArcSpacing(125f, 27f), 1e-3f);
            // Standard-Layout (125 dp, 27°): Nachbarknöpfe ≥ 48 dp Mindestgröße überlappen nicht
            Assert.Greater(TouchControlMath.ArcSpacing(125f, 27f), MobileMinTargetDp);
        }

        private const float MobileMinTargetDp = 48f;

        [Test]
        public void Einpassen_NurVerkleinern_MitUntergrenze()
        {
            Assert.AreEqual(1f, TouchControlMath.FitScale(new Vector2(200f, 200f), new Vector2(500f, 600f), 0.8f), Eps);
            Assert.AreEqual(0.9f, TouchControlMath.FitScale(new Vector2(200f, 250f), new Vector2(180f, 600f), 0.8f), Eps);
            Assert.AreEqual(0.8f, TouchControlMath.FitScale(new Vector2(200f, 250f), new Vector2(100f, 100f), 0.8f), Eps);
        }

        [Test]
        public void Kreis_Treffer()
        {
            Assert.IsTrue(TouchControlMath.InCircle(new Vector2(3f, 4f), Vector2.zero, 5f));
            Assert.IsFalse(TouchControlMath.InCircle(new Vector2(3f, 4.1f), Vector2.zero, 5f));
        }
    }
}
