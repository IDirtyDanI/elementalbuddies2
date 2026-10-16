using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Rechenregeln der Flieger (FlightMath, genutzt von FlyingMotor/FlyerHurtbox) – Walddorf Etappe 1, Paket C-Flieger
    public class FlyingMotorTests
    {
        private const float FlyHeight = 2f, Clearance = 1.5f, Climb = 4f;

        [Test]
        public void Sollhoehe_ueberStrasse_istBodenPlusFlughoehe()
        {
            // Straße/Gelände: Oberfläche = Boden
            Assert.AreEqual(2f, FlightMath.RequiredAltitude(0f, 0f, FlyHeight, Clearance), 1e-4f);
            // leicht erhöhter Bordstein (0,2 m) ändert nichts: 0,2 + 1,5 < 0 + 2
            Assert.AreEqual(2f, FlightMath.RequiredAltitude(0f, 0.2f, FlyHeight, Clearance), 1e-4f);
            // Gelände auf 3 m
            Assert.AreEqual(5f, FlightMath.RequiredAltitude(3f, 3f, FlyHeight, Clearance), 1e-4f);
        }

        [Test]
        public void Sollhoehe_ueberHaus_istDachPlusAbstand()
        {
            Assert.AreEqual(9.5f, FlightMath.RequiredAltitude(0f, 8f, FlyHeight, Clearance), 1e-4f);
            // Plan-Abnahme: über Häusern ≥ Dach + 1,3 m
            Assert.GreaterOrEqual(FlightMath.RequiredAltitude(0f, 8f, FlyHeight, Clearance) - 8f, 1.3f);
        }

        [Test]
        public void Vorausschau_steigtRechtzeitig()
        {
            // Haus (Dach 8 m → Soll 9,5 m) 6 m voraus, Tempo 4,2 m/s, Steigrate 4 m/s:
            // jetzt nötig = 9,5 − 6/4,2·4 ≈ 3,79 m
            float need = FlightMath.AnticipatedAltitude(9.5f, 6f, 4.2f, Climb);
            Assert.AreEqual(9.5f - 6f / 4.2f * 4f, need, 1e-4f);
            // Simulation mit der Planung des FlyingMotor (PlanAltitude): Haus 8 m bzw. 15 m voraus → an der Hauskante
            // mindestens Dach + 1,3 m, Steigrate nie über 4 m/s
            foreach (float wallX in new[] { 8f, 15f })
            {
                float y = 2f, x = 0f, dt = 0.02f;
                const float speed = 4.2f;
                var dists = FlightMath.LookAheadDistances;
                var req = new float[dists.Length];
                for (int i = 0; i < 2000 && x < wallX; i++)
                {
                    for (int k = 0; k < dists.Length; k++)
                        req[k] = FlightMath.RequiredAltitude(0f, x + dists[k] >= wallX ? 8f : 0f, FlyHeight, Clearance);
                    float target = FlightMath.PlanAltitude(req, dists.Length, speed, Climb);
                    float ny = FlightMath.StepAltitude(y, target, Climb, dt);
                    Assert.LessOrEqual(Mathf.Abs(ny - y) / dt, Climb + 1e-3f, "Steigrate überschritten");
                    y = ny;
                    x += speed * dt;
                }
                Assert.GreaterOrEqual(y, 8f + 1.3f, "an der Hauskante (" + wallX + " m) nicht hoch genug: " + y);
            }
        }

        [Test]
        public void Vorausschau_ohneTempo_zaehltNur_dieStelleDarunter()
        {
            Assert.AreEqual(float.NegativeInfinity, FlightMath.AnticipatedAltitude(9.5f, 4f, 0f, Climb));
            Assert.AreEqual(9.5f, FlightMath.AnticipatedAltitude(9.5f, 0f, 0f, Climb), 1e-4f);
        }

        [Test]
        public void Hoehenschritt_begrenztSteigUndSinkrate()
        {
            float dt = 0.1f;
            float up = FlightMath.StepAltitude(0f, 100f, Climb, dt);
            Assert.AreEqual(0.4f, up, 1e-4f);
            float down = FlightMath.StepAltitude(10f, 0f, Climb, dt);
            Assert.AreEqual(9.6f, down, 1e-4f);
            // kurz vor dem Ziel: weich (proportional), kein Überschießen
            float near = FlightMath.StepAltitude(1.95f, 2f, Climb, dt);
            Assert.Greater(near, 1.95f);
            Assert.LessOrEqual(near, 2f);
        }

        [Test]
        public void Hoehenschritt_konvergiert_aufFlughoehe()
        {
            float y = 0f;
            for (int i = 0; i < 200; i++) y = FlightMath.StepAltitude(y, 2f, Climb, 0.02f);
            Assert.AreEqual(2f, y, 0.01f);
        }

        [Test]
        public void Drehrate_240GradProSekunde()
        {
            Vector3 h = FlightMath.TurnTowards(Vector3.forward, Vector3.back, 240f, 0.25f); // höchstens 60°
            Assert.AreEqual(60f, Vector3.Angle(Vector3.forward, h), 0.5f);
            Vector3 done = FlightMath.TurnTowards(Vector3.forward, Vector3.right, 240f, 1f);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, done), 0.01f);
            Assert.AreEqual(0f, done.y, 1e-5f);
        }

        [Test]
        public void Kartengrenze_62m()
        {
            Vector3 p = FlightMath.ClampToSquare(new Vector3(80f, 5f, -70f), Vector2.zero, 62f);
            Assert.AreEqual(new Vector3(62f, 5f, -62f), p);
            Vector3 q = FlightMath.ClampToSquare(new Vector3(10f, 1f, 3f), new Vector2(5f, 5f), 62f);
            Assert.AreEqual(new Vector3(10f, 1f, 3f), q);
        }

        [Test]
        public void Separation_nurInnerhalbDesRadius()
        {
            Assert.AreEqual(Vector3.zero, FlightMath.SeparationPush(Vector3.zero, new Vector3(2f, 0f, 0f), 1.2f, Vector3.right));
            Vector3 push = FlightMath.SeparationPush(Vector3.zero, new Vector3(0.6f, 3f, 0f), 1.2f, Vector3.right);
            Assert.AreEqual(-0.5f, push.x, 1e-4f, "halber Radius → halbe Stärke, weg vom Nachbarn, Höhe egal");
            Assert.AreEqual(0f, push.y, 1e-5f);
            Vector3 same = FlightMath.SeparationPush(Vector3.zero, Vector3.zero, 1.2f, Vector3.right);
            Assert.AreEqual(Vector3.right, same);
        }

        [Test]
        public void Trefferkapsel_reichtBisKnappUeberDenBoden()
        {
            FlightMath.HurtboxSpan(2f, 0.5f, 0f, 0.3f, 1f, out float bottom, out float top);
            Assert.AreEqual(0.3f, bottom, 1e-4f);
            Assert.AreEqual(2.5f, top, 1e-4f);
            // über einem Dach endet sie am Dach
            FlightMath.HurtboxSpan(9.5f, 0.5f, 8f, 0.3f, 1f, out bottom, out top);
            Assert.AreEqual(8.3f, bottom, 1e-4f);
            // Sturzflug knapp über dem Boden: Mindesthöhe 1 m nach unten
            FlightMath.HurtboxSpan(0.6f, 0.5f, 0f, 0.3f, 1f, out bottom, out top);
            Assert.AreEqual(0.1f, bottom, 1e-4f);
            Assert.AreEqual(1.1f, top, 1e-4f);
        }

        [Test]
        public void Kreisbogen_haeltRadiusUndUmkreist()
        {
            Vector3 center = Vector3.zero;
            Vector3 pos = new Vector3(1.5f, 2f, 0f);
            float startAngle = Mathf.Atan2(pos.z, pos.x);
            for (int i = 0; i < 50; i++) pos += FlightMath.OrbitVelocity(pos, center, 1.5f, 3.4f, 1f) * 0.02f;
            float r = FlightMath.Horizontal(pos, center);
            Assert.AreEqual(1.5f, r, 0.15f);
            Assert.AreNotEqual(startAngle, Mathf.Atan2(pos.z, pos.x));
            Assert.AreEqual(2f, pos.y, 1e-5f, "Kreisbogen ist rein horizontal");
        }

        [Test]
        public void Horizontalabstand_ignoriertHoehe()
        {
            Assert.AreEqual(5f, FlightMath.Horizontal(new Vector3(0f, 10f, 0f), new Vector3(3f, 0f, 4f)), 1e-4f);
        }
    }
}
