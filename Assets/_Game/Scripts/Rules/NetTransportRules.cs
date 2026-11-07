namespace ElementalBuddies
{
    // Transport-Werte für den Online-Koop (2026-11-02, siehe NetDriverTuning).
    public static class NetTransportRules
    {
        // Unity Transport startet nach so langer Funkstille eine neue DTLS-Sitzung zum Relay (Standard 2000 ms). Das
        // scheiterte bei Testern mitten im Spiel und beendete den Koop. Erst nach 8 s neu verbinden: kurze Aussetzer
        // übersteht die alte Sitzung; bei echtem Netzwechsel (WLAN → Mobilfunk) bleibt der Neuaufbau als letzte Chance,
        // bevor die Relay-Allokation nach ~10 s Stille verfällt.
        public const int ReconnectionTimeoutMs = 8000;

        // Netcode trennt nach so langer Stille (Prefab-Wert m_DisconnectTimeoutMS)
        public const int DisconnectTimeoutMs = 30000;

        public static bool SurvivesGap(int gapMs) => gapMs < ReconnectionTimeoutMs && gapMs < DisconnectTimeoutMs;
    }
}
