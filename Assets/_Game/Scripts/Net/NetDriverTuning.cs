using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;

namespace ElementalBuddies
{
    // Robustere Relay-Verbindung (Koop-Abbruch-Fix 2026-11-02, Tester-Foto vom Host):
    //   „DTLS handshake failed at step 12 (SERVER_CHANGE_CIPHER_SPEC)“ → „Failed to establish connection with the Relay
    //   server.“ → „Transport failure! Relay allocation needs to be recreated“ → Host fährt herunter, alle fliegen raus.
    // Ursache (Unity Transport 2.7.4, DTLSLayer.CheckForReconnection/HandlePossibleReconnection, RelayLayer): Kommt
    // 2 s lang (Standard reconnectionTimeoutMS = 2000) nichts vom Relay-Server an – auf Handy-WLAN/Mobilfunk ein
    // normaler Aussetzer –, baut Transport nebenher eine NEUE DTLS-Sitzung auf (gedacht für IP-Wechsel). Antwortet der
    // Server darauf, wird umgeschaltet; scheitert dieser Handshake, schließt die Verbindung, und da die erste
    // Verbindung länger als maxConnectAttempts × connectTimeoutMS zurückliegt, gibt die Relay-Schicht sofort auf.
    // Fix: Neuaufbau erst nach ReconnectionTimeoutMs. Kürzere Aussetzer überbrückt die bestehende Sitzung (Netcode
    // wartet bis DisconnectTimeout 30 s, Relay-Rebind nach 1,5 s hält die Allokation am Leben).
    // Sonst alles wie NGO-Standard (UnityTransport.CreateDriver): Einstellungen, UDP-Schnittstelle, Pipelines.
    public sealed class NetDriverTuning : INetworkStreamDriverConstructor
    {
        public void CreateDriver(UnityTransport transport, out NetworkDriver driver,
            out NetworkPipeline unreliableFragmentedPipeline,
            out NetworkPipeline unreliableSequencedFragmentedPipeline,
            out NetworkPipeline reliableSequencedPipeline)
        {
            var settings = transport.GetDefaultNetworkSettings();
            var config = settings.GetNetworkConfigParameters();
            config.reconnectionTimeoutMS = NetTransportRules.ReconnectionTimeoutMs;
            settings.AddRawParameterStruct(ref config);

            driver = NetworkDriver.Create(new UDPNetworkInterface(), settings);
            transport.GetDefaultPipelineConfigurations(ref driver,
                out var unreliableFragmentedStages,
                out var unreliableSequencedFragmentedStages,
                out var reliableSequencedStages);
            unreliableFragmentedPipeline = driver.CreatePipeline(unreliableFragmentedStages);
            unreliableSequencedFragmentedPipeline = driver.CreatePipeline(unreliableSequencedFragmentedStages);
            reliableSequencedPipeline = driver.CreatePipeline(reliableSequencedStages);
        }

        // Vor jedem Start (Solo/Host/Client) setzen – NGO setzt das statische Feld im Editor beim Laden zurück
        public static void Install()
        {
            if (!(UnityTransport.s_DriverConstructor is NetDriverTuning))
                UnityTransport.s_DriverConstructor = new NetDriverTuning();
        }
    }
}
