using Unity.Netcode;
using UnityEngine;

namespace ElementalBuddies
{
    // Zentrale Abfragen für den Mehrspieler-Modus. Das Spiel läuft immer über Netcode:
    // Einzelspieler = lokaler Host ohne Mitspieler (siehe NetSession.StartSolo).
    // Ohne laufenden NetworkManager (z. B. EditMode-Tests) verhält sich alles wie ein Server.
    public static class Net
    {
        public static NetworkManager Manager => NetworkManager.Singleton;

        // Netcode läuft (Host, Server oder Client)
        public static bool IsRunning => Manager != null && Manager.IsListening;

        // Darf Spielzustand verändern (Schaden, Spawns, Geld …). Offline immer true.
        public static bool IsServer => !IsRunning || Manager.IsServer;

        // Reiner Client (nicht Host): sieht nur Abbilder, entscheidet nichts
        public static bool IsClientOnly => IsRunning && !Manager.IsServer;

        // Eigene Client-Id (offline 0)
        public static ulong LocalClientId => IsRunning ? Manager.LocalClientId : 0;

        // Anzahl Spieler in der Partie (mindestens 1)
        public static int PlayerCount => Mathf.Max(1, NetPlayer.All.Count);

        // Mehr als ein Spieler verbunden → keine globalen Pausen (Time.timeScale) mehr
        public static bool IsMultiplayer => NetPlayer.All.Count > 1;

        // Einzelspieler darf weiterhin per Time.timeScale pausieren
        public static bool CanPauseTime => !IsMultiplayer;
    }
}
