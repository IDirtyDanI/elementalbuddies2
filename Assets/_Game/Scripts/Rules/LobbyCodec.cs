using System.Text;

namespace ElementalBuddies
{
    // Lobby-Netzwert des Hosts (NetLobby.Difficulty, FixedString32Bytes = max. 29 Byte UTF-8):
    //   "<Stufen-Id>#<Belagerungsstufe>[#<Map-Id>]"
    // Rückwärtskompatibel: Ohne Map-Teil (alte Hosts) gilt die Standard-Map; Pack lässt den Map-Teil für die
    // Standard-Map weg, damit der Wert für die Stadt byteweise dem alten Format entspricht.
    public static class LobbyCodec
    {
        public const int MaxBytes = 29;
        public const string DefaultMapId = "city";

        public static string Pack(string difficultyId, int siege, string mapId)
        {
            string s = (difficultyId ?? "") + "#" + siege;
            if (!string.IsNullOrEmpty(mapId) && mapId != DefaultMapId) s += "#" + mapId;
            // Notbremse: zu lang für FixedString32Bytes → Map-Teil weglassen (darf praktisch nie passieren)
            if (Encoding.UTF8.GetByteCount(s) > MaxBytes) s = (difficultyId ?? "") + "#" + siege;
            return s;
        }

        public static void Unpack(string value, out string difficultyId, out int siege, out string mapId)
        {
            siege = 0;
            mapId = DefaultMapId;
            difficultyId = value ?? "";
            int i = difficultyId.IndexOf('#');
            if (i < 0) return;
            string rest = difficultyId.Substring(i + 1);
            difficultyId = difficultyId.Substring(0, i);
            int j = rest.IndexOf('#');
            if (j >= 0)
            {
                string m = rest.Substring(j + 1);
                if (!string.IsNullOrEmpty(m)) mapId = m;
                rest = rest.Substring(0, j);
            }
            int.TryParse(rest, out siege);
        }
    }
}
