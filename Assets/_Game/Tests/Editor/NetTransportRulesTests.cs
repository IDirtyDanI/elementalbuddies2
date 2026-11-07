using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Relay-Verbindung übersteht Funk-Aussetzer (Koop-Abbruch-Fix 2026-11-02)
    public class NetTransportRulesTests
    {
        [Test]
        public void Kurze_Aussetzer_loesen_keinen_Neuaufbau_aus()
        {
            // Unity-Standard 2 s war zu knapp für Handy-WLAN/Mobilfunk
            Assert.IsTrue(NetTransportRules.SurvivesGap(2500));
            Assert.IsTrue(NetTransportRules.SurvivesGap(6000));
        }

        [Test]
        public void Neuaufbau_bleibt_vor_dem_Verfall_der_Allokation_moeglich()
        {
            // Relay verwirft eine Allokation nach ~10 s ohne Lebenszeichen
            Assert.Less(NetTransportRules.ReconnectionTimeoutMs, 10000);
            Assert.Less(NetTransportRules.ReconnectionTimeoutMs, NetTransportRules.DisconnectTimeoutMs);
            Assert.IsFalse(NetTransportRules.SurvivesGap(NetTransportRules.ReconnectionTimeoutMs));
        }
    }
}
