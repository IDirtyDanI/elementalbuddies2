using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Orbit-Kamera: Versatz um die Figur, kamerabezogene Laufrichtung, Drehen und Zurücksetzen
    public class CameraOrbitMathTests
    {
        private static readonly Vector3 Offset = new Vector3(0f, 15f, -8f); // Standard von CameraFollow
        private const float Eps = 1e-4f;

        private static void AssertVec(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Eps, "x");
            Assert.AreEqual(expected.y, actual.y, Eps, "y");
            Assert.AreEqual(expected.z, actual.z, Eps, "z");
        }

        [Test]
        public void Yaw0_EntsprichtDerBisherigenAnsicht()
        {
            AssertVec(Offset, CameraOrbitMath.OrbitOffset(Offset, 0f));
            AssertVec(new Vector3(0f, 0f, 1f), CameraOrbitMath.InputToWorld(new Vector2(0f, 1f), 0f));
            AssertVec(new Vector3(1f, 0f, 0f), CameraOrbitMath.InputToWorld(new Vector2(1f, 0f), 0f));
        }

        [Test]
        public void Orbit_BehaeltHoeheUndAbstand()
        {
            foreach (float yaw in new[] { -135f, -90f, 37f, 90f, 180f, 270f })
            {
                Vector3 o = CameraOrbitMath.OrbitOffset(Offset, yaw);
                Assert.AreEqual(15f, o.y, Eps);
                Assert.AreEqual(8f, new Vector2(o.x, o.z).magnitude, Eps);
            }
        }

        [Test]
        public void Orbit_Yaw90_KameraStehtImWestenUndSchautNachOsten()
        {
            Vector3 o = CameraOrbitMath.OrbitOffset(Offset, 90f);
            AssertVec(new Vector3(-8f, 15f, 0f), o);
            AssertVec(new Vector3(1f, 0f, 0f), CameraOrbitMath.FlatForward(90f));
        }

        [Test]
        public void Vorwaerts_IstImmerDieBlickrichtungDerKamera()
        {
            foreach (float yaw in new[] { -170f, -45f, 0f, 60f, 90f, 180f })
            {
                Vector3 w = CameraOrbitMath.InputToWorld(new Vector2(0f, 1f), yaw);
                AssertVec(CameraOrbitMath.FlatForward(yaw), w);
                // Kamera liegt hinter der Figur: Versatz zeigt entgegen der Laufrichtung „vorwärts“
                Vector3 o = CameraOrbitMath.OrbitOffset(Offset, yaw);
                Assert.Less(Vector3.Dot(new Vector3(o.x, 0f, o.z), w), 0f);
            }
        }

        [Test]
        public void Rechts_StehtSenkrechtZuVorwaerts_UndZeigtNachBildschirmRechts()
        {
            Vector3 fwd = CameraOrbitMath.InputToWorld(new Vector2(0f, 1f), 90f);
            Vector3 right = CameraOrbitMath.InputToWorld(new Vector2(1f, 0f), 90f);
            Assert.AreEqual(0f, Vector3.Dot(fwd, right), Eps);
            // Gleiche Händigkeit wie die Standardansicht (dort ist vorwärts × rechts = oben)
            Assert.Greater(Vector3.Cross(fwd, right).y, 0f);
            AssertVec(new Vector3(0f, 0f, -1f), right);
        }

        [Test]
        public void Eingabe_LaengeBleibtErhalten_YIstNull()
        {
            Vector2 diag = new Vector2(0.7071f, 0.7071f);
            Vector3 w = CameraOrbitMath.InputToWorld(diag, 33f);
            Assert.AreEqual(diag.magnitude, w.magnitude, Eps);
            Assert.AreEqual(0f, w.y, Eps);
            AssertVec(Vector3.zero, CameraOrbitMath.InputToWorld(Vector2.zero, 120f));
        }

        [Test]
        public void WrapAngle_LiegtImBereich()
        {
            Assert.AreEqual(0f, CameraOrbitMath.WrapAngle(360f), Eps);
            Assert.AreEqual(-170f, CameraOrbitMath.WrapAngle(190f), Eps);
            Assert.AreEqual(170f, CameraOrbitMath.WrapAngle(-190f), Eps);
            Assert.AreEqual(-180f, CameraOrbitMath.WrapAngle(180f), Eps);
            Assert.AreEqual(45f, CameraOrbitMath.WrapAngle(45f + 720f), Eps);
        }

        [Test]
        public void Drehen_NeunzigGradProSekunde()
        {
            Assert.AreEqual(90f, CameraOrbitMath.StepTargetYaw(0f, 1f, 90f, 1f), Eps);
            Assert.AreEqual(-45f, CameraOrbitMath.StepTargetYaw(0f, -1f, 90f, 0.5f), Eps);
            Assert.AreEqual(10f, CameraOrbitMath.StepTargetYaw(10f, 0f, 90f, 1f), Eps);
            // Achse gedeckelt, negative Zeit ignoriert
            Assert.AreEqual(90f, CameraOrbitMath.StepTargetYaw(0f, 5f, 90f, 1f), Eps);
            Assert.AreEqual(0f, CameraOrbitMath.StepTargetYaw(0f, 1f, 90f, -1f), Eps);
        }

        [Test]
        public void Zuruecksetzen_NimmtDenKuerzerenWeg()
        {
            Assert.AreEqual(0f, CameraOrbitMath.ResetTarget(80f), Eps);
            Assert.AreEqual(0f, CameraOrbitMath.ResetTarget(-120f), Eps);
            Assert.AreEqual(360f, CameraOrbitMath.ResetTarget(190f), Eps);
            Assert.AreEqual(-360f, CameraOrbitMath.ResetTarget(-200f), Eps);
            Assert.AreEqual(720f, CameraOrbitMath.ResetTarget(700f), Eps);
            // Ziel entspricht immer der Standardansicht
            Assert.AreEqual(0f, CameraOrbitMath.WrapAngle(CameraOrbitMath.ResetTarget(1234.5f)), Eps);
        }
    }
}
