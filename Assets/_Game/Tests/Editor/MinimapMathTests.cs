using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Minimap: Welt → Karte (Norden oben, nicht rotierend), Randbegrenzung, Pfeil-Ausrichtung
    public class MinimapMathTests
    {
        private static readonly Vector2 Center = Vector2.zero;
        private const float WorldR = 68f;   // Standard der Minimap (ganze Karte inkl. Schreine bei ±42/±42)
        private const float MapR = 125f;

        [Test]
        public void Mitte_LiegtImKartenmittelpunkt()
        {
            Assert.AreEqual(Vector2.zero, MinimapMath.WorldToMap(new Vector3(0f, 5f, 0f), Center, WorldR, MapR));
        }

        [Test]
        public void Himmelsrichtungen_NordenObenOstenRechts()
        {
            Vector2 nord = MinimapMath.WorldToMap(new Vector3(0f, 0f, 35f), Center, WorldR, MapR);   // Nordtor
            Vector2 west = MinimapMath.WorldToMap(new Vector3(-35f, 0f, 0f), Center, WorldR, MapR);  // Westtor
            Vector2 nexus = MinimapMath.WorldToMap(new Vector3(26f, 0f, 0f), Center, WorldR, MapR);  // Nexus (Osten)
            Assert.AreEqual(0f, nord.x, 1e-4f);
            Assert.Greater(nord.y, 0f);
            Assert.Less(west.x, 0f);
            Assert.AreEqual(0f, west.y, 1e-4f);
            Assert.Greater(nexus.x, 0f);
        }

        [Test]
        public void Massstab_IstLinear()
        {
            Vector2 p = MinimapMath.WorldToMap(new Vector3(WorldR, 0f, -WorldR * 0.5f), Center, WorldR, MapR);
            Assert.AreEqual(MapR, p.x, 1e-3f);
            Assert.AreEqual(-MapR * 0.5f, p.y, 1e-3f);
        }

        [Test]
        public void VerschobeneMitte_WirdAbgezogen()
        {
            var c = new Vector2(10f, -4f);
            Vector2 p = MinimapMath.WorldToMap(new Vector3(10f, 0f, -4f), c, WorldR, MapR);
            Assert.AreEqual(Vector2.zero, p);
        }

        [Test]
        public void HinUndZurueck_ErgibtDieWeltposition()
        {
            var c = new Vector2(3f, 7f);
            var w = new Vector3(-41.5f, 0f, 44.8f);
            Vector3 back = MinimapMath.MapToWorld(MinimapMath.WorldToMap(w, c, WorldR, MapR), c, WorldR, MapR);
            Assert.AreEqual(w.x, back.x, 1e-3f);
            Assert.AreEqual(w.z, back.z, 1e-3f);
        }

        [Test]
        public void AlleKartenpunkte_PassenInDenKreis()
        {
            // Schreine (±42/±42), Händler (±44,8/±22), Portale (44), Nexus (26/0)
            var pts = new[]
            {
                new Vector3(42f, 0f, 42f), new Vector3(-42f, 0f, -42f), new Vector3(-44.8f, 0f, 20f),
                new Vector3(22f, 0f, -44.8f), new Vector3(0f, 0f, 44f), new Vector3(-44f, 0f, 0f), new Vector3(26f, 0f, 0f),
            };
            foreach (var w in pts)
                Assert.LessOrEqual(MinimapMath.WorldToMap(w, Center, WorldR, MapR).magnitude, MapR * 0.97f, w.ToString());
        }

        [Test]
        public void ClampToCircle_InnenUnveraendert()
        {
            var p = new Vector2(30f, -40f);
            Assert.AreEqual(p, MinimapMath.ClampToCircle(p, 100f, out bool clamped));
            Assert.IsFalse(clamped);
        }

        [Test]
        public void ClampToCircle_AussenAufDenRand()
        {
            Vector2 p = MinimapMath.ClampToCircle(new Vector2(300f, 400f), 100f, out bool clamped);
            Assert.IsTrue(clamped);
            Assert.AreEqual(100f, p.magnitude, 1e-3f);
            Assert.AreEqual(0.6f, p.x / 100f, 1e-4f); // Richtung bleibt erhalten
        }

        [TestCase(0f, 1f, 0f)]      // Norden
        [TestCase(1f, 0f, -90f)]    // Osten: Pfeil nach rechts
        [TestCase(-1f, 0f, 90f)]    // Westen: Pfeil nach links
        [TestCase(1f, 1f, -45f)]    // Nordosten
        public void HeadingToRotation_PfeilZeigtInBlickrichtung(float x, float z, float expected)
        {
            Assert.AreEqual(expected, MinimapMath.HeadingToRotation(new Vector3(x, 0f, z)), 1e-3f);
        }

        [Test]
        public void HeadingToRotation_Sueden()
        {
            Assert.AreEqual(180f, Mathf.Abs(MinimapMath.HeadingToRotation(new Vector3(0f, 0f, -1f))), 1e-3f);
            Assert.AreEqual(0f, MinimapMath.HeadingToRotation(Vector3.up), 1e-6f);
        }

        [Test]
        public void RadiusToFit_NimmtDenWeitestenPunktPlusRand()
        {
            var pts = new[] { new Vector2(42f, 42f), new Vector2(0f, 44f) };
            Assert.AreEqual(Mathf.Sqrt(42f * 42f * 2f) + 2f, MinimapMath.RadiusToFit(Vector2.zero, pts, 2f), 1e-3f);
        }
    }
}
