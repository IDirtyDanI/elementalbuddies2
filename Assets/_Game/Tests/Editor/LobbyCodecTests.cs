using System.Text;
using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Lobby-Netzwert "<Stufe>#<Belagerung>[#<Map>]" (Walddorf P0): alte Werte müssen weiter gelesen werden,
    // die Stadt muss byteweise das alte Format behalten.
    public class LobbyCodecTests
    {
        [Test]
        public void AltesFormat_OhneMap_IstStadt()
        {
            LobbyCodec.Unpack("normal#3", out string id, out int siege, out string map);
            Assert.AreEqual("normal", id);
            Assert.AreEqual(3, siege);
            Assert.AreEqual("city", map);
        }

        [Test]
        public void SehrAltesFormat_NurStufe()
        {
            LobbyCodec.Unpack("schwer", out string id, out int siege, out string map);
            Assert.AreEqual("schwer", id);
            Assert.AreEqual(0, siege);
            Assert.AreEqual("city", map);
        }

        [Test]
        public void Leer_UndNull()
        {
            LobbyCodec.Unpack("", out string id, out int siege, out string map);
            Assert.AreEqual("", id); Assert.AreEqual(0, siege); Assert.AreEqual("city", map);
            LobbyCodec.Unpack(null, out id, out siege, out map);
            Assert.AreEqual("", id); Assert.AreEqual(0, siege); Assert.AreEqual("city", map);
        }

        [Test]
        public void Stadt_PackEntsprichtAltemFormat()
        {
            Assert.AreEqual("normal#3", LobbyCodec.Pack("normal", 3, "city"));
            Assert.AreEqual("leicht#0", LobbyCodec.Pack("leicht", 0, null));
            Assert.AreEqual("leicht#0", LobbyCodec.Pack("leicht", 0, ""));
        }

        [Test]
        public void NeuesFormat_RundReise()
        {
            string v = LobbyCodec.Pack("schwer", 5, "forest");
            Assert.AreEqual("schwer#5#forest", v);
            LobbyCodec.Unpack(v, out string id, out int siege, out string map);
            Assert.AreEqual("schwer", id);
            Assert.AreEqual(5, siege);
            Assert.AreEqual("forest", map);
        }

        [Test]
        public void LeererMapTeil_IstStadt()
        {
            LobbyCodec.Unpack("normal#2#", out string id, out int siege, out string map);
            Assert.AreEqual("normal", id); Assert.AreEqual(2, siege); Assert.AreEqual("city", map);
        }

        [Test]
        public void PasstInFixedString32()
        {
            foreach (var d in new[] { "leicht", "normal", "schwer" })
                for (int s = 0; s <= 5; s++)
                    Assert.LessOrEqual(Encoding.UTF8.GetByteCount(LobbyCodec.Pack(d, s, "forest")), LobbyCodec.MaxBytes);
            // zu lange Map-Id wird verworfen statt den Wert zu sprengen
            string lang = LobbyCodec.Pack("normal", 1, new string('x', 40));
            Assert.AreEqual("normal#1", lang);
        }
    }
}
