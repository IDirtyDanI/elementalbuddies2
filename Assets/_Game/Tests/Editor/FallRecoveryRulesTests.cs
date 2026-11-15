using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    public class FallRecoveryRulesTests
    {
        [Test]
        public void Sprung_vom_hohen_Dach_mit_Boden_darunter_setzt_nicht_zurueck()
        {
            // Wachturm-Dach 9,5 m, Figur kurz vor der Landung bei 0,5 m: 9 m unter dem Dach, aber Boden darunter
            Assert.IsFalse(FallRecoveryRules.ShouldRecover(0.5f, 9.5f, 8f, true));
        }

        [Test]
        public void Durch_die_Map_gefallen_ohne_Boden_setzt_zurueck()
        {
            Assert.IsTrue(FallRecoveryRules.ShouldRecover(-9f, 0f, 8f, false));
        }

        [Test]
        public void Kurzer_Fall_ohne_Boden_noch_nicht_zurueck()
        {
            Assert.IsFalse(FallRecoveryRules.ShouldRecover(-3f, 0f, 8f, false));
        }

        [Test]
        public void Grenze_genau_bei_der_Tiefe()
        {
            Assert.IsTrue(FallRecoveryRules.ShouldRecover(-8f, 0f, 8f, false));
        }

        [NUnit.Framework.Test]
        public void SafePosition_OnlyDirectlyOverNavMesh()
        {
            var nav = new UnityEngine.Vector3(416f, 0f, -3.4f);
            NUnit.Framework.Assert.IsTrue(FallRecoveryRules.IsOverWalkable(new UnityEngine.Vector3(416.3f, 1.1f, -3.5f), nav, 0.75f), "auf dem Boden");
            NUnit.Framework.Assert.IsFalse(FallRecoveryRules.IsOverWalkable(new UnityEngine.Vector3(417.7f, 1.0f, -3.8f), nav, 0.75f), "hinter der Arenamauer");
            NUnit.Framework.Assert.IsTrue(FallRecoveryRules.IsOverWalkable(new UnityEngine.Vector3(416f, 6f, -3.4f), nav, 0.75f), "Höhe zählt nicht (Dach über NavMesh)");
        }
    }
}
