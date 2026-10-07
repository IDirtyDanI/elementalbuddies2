using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Einrichtungsschritte des Mehrspieler-Menüs/HUDs (Paket E), aufgerufen von "BuddyTD/Netzwerk/Einrichten (alles)".
    public static class NetSetupUI
    {
        private const string IconDir = "Assets/_Game/UI/Icons/";

        // NetPlayer-Prefab: Lobby-Zusatz (Host-Schwierigkeit, Namensänderung)
        [NetSetupStep(110)]
        public static void SetupNetLobbyPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(NetworkSetup.NetPlayerPath) == null)
            {
                Debug.LogWarning("[NetSetup] NetPlayer-Prefab fehlt – Kern-Schritt 100 zuerst.");
                return;
            }
            NetworkSetup.EditPrefab(NetworkSetup.NetPlayerPath, root => NetworkSetup.Ensure<NetLobby>(root));
        }

        // Hauptmenü-Szene (nicht test.unity, daher Order < 200): Hosten/Beitreten/Lobby einbauen.
        // Öffnet MainMenu.unity additiv, baut, speichert und schließt sie wieder.
        [NetSetupStep(150)]
        public static void SetupMainMenuMultiplayer()
        {
            MainMenuBuilder.AddMultiplayerUIToScene();
        }

        // Spielszene: HUD-Sprites für Gold-Anzeige und Mitspieler-Liste (TeamHUD) zuweisen
        [NetSetupStep(260)]
        public static void SetupHud()
        {
            var hud = Object.FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include);
            if (hud == null) { Debug.LogWarning("[NetSetup] kein HUDManager in der Spielszene."); return; }
            if (hud.GoldIcon == null) hud.GoldIcon = LoadSprite("icon_gold") ?? LoadSprite("icon_coin");
            hud.ChampionPortraits = new[]
            {
                LoadSprite("portrait_champion_mage"),
                LoadSprite("portrait_champion_knight"),
                LoadSprite("portrait_champion_archer"),
            };
            EditorUtility.SetDirty(hud);
        }

        private static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + name + ".png");
    }
}
