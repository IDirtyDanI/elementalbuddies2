using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools.Tests
{
    // Spieltest 2026-10-17, Paket B: Canvas-Ebenen und Porträts der Gruppenanzeige.
    // Liegt in Assembly-CSharp-Editor (das Test-Assembly unter _Game/Tests sieht die Spielklassen nicht).
    public class UiLayerTests
    {
        private const string HudPrefab = "Assets/_Game/Prefabs/Core/Canvas.prefab";
        private const string AchievementPrefab = "Assets/Resources/AchievementPopup.prefab";

        // #4: Porträt-Array in der Reihenfolge von ChampionClass, jeder Eintrag zeigt den richtigen Champion
        [Test]
        public void ChampionPortraits_PassenZuChampionClass()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
            Assert.IsNotNull(root, HudPrefab);
            var hud = root.GetComponentInChildren<HUDManager>(true);
            Assert.IsNotNull(hud, "HUDManager im HUD-Prefab");
            Assert.AreEqual(GameSession.ChampionCount, hud.ChampionPortraits.Length, "ein Porträt je Champion");
            for (int i = 0; i < GameSession.ChampionCount; i++)
            {
                var c = (ChampionClass)i;
                Assert.IsNotNull(hud.ChampionPortraits[i], c.ToString());
                Assert.AreEqual(TeamHUD.PortraitName(c), hud.ChampionPortraits[i].name, c.ToString());
            }
        }

        // #1/#2/#5: Namensschilder < Gruppenliste < HUD < Randpfeile < Pings (40) < Erfolgs-Popup
        [Test]
        public void Ebenen_Reihenfolge()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
            var hudCanvas = root.GetComponent<Canvas>();
            Assert.IsNotNull(hudCanvas);
            int hud = hudCanvas.sortingOrder;
            Assert.Less(TeamHUD.NameTagSortingOrder, TeamHUD.ListSortingOrder);
            Assert.Less(TeamHUD.ListSortingOrder, hud, "Gruppenliste unter dem HUD (Buddy-Menü, Händler, Pause liegen darüber)");
            Assert.Greater(OffscreenIndicatorUI.ArrowSortingOrder, hud, "Randpfeile über dem HUD");
            Assert.Less(OffscreenIndicatorUI.ArrowSortingOrder, 40, "Randpfeile unter den Pings (PingSystem 40)");
            var ach = AssetDatabase.LoadAssetAtPath<GameObject>(AchievementPrefab);
            Assert.IsNotNull(ach);
            Assert.Greater(ach.GetComponentInChildren<Canvas>(true).sortingOrder, OffscreenIndicatorUI.ArrowSortingOrder);
        }
    }
}
