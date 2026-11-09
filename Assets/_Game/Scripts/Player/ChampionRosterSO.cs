using UnityEngine;

namespace ElementalBuddies
{
    // Alle Champion-Definitionen in ChampionClass-Reihenfolge, zur Laufzeit aus Resources/ChampionRoster ladbar.
    // Das Hauptmenü ergänzt damit Champions, die in der (nicht neu gebauten) Szene noch fehlen – z. B. den Schamanen –,
    // ohne dass die Szene gespeichert werden muss (MainMenuUI.AddMissingChampions, MenuChampionStage.EnsurePedestals).
    [CreateAssetMenu(fileName = "ChampionRoster", menuName = "BuddyTD/Champion-Liste")]
    public class ChampionRosterSO : ScriptableObject
    {
        public const string ResourcePath = "ChampionRoster";

        public ChampionDefinitionSO[] Champions = new ChampionDefinitionSO[0];

        private static ChampionRosterSO _cached;

        public static ChampionRosterSO Load()
        {
            if (_cached == null) _cached = Resources.Load<ChampionRosterSO>(ResourcePath);
            return _cached;
        }

        public ChampionDefinitionSO Find(ChampionClass c)
        {
            if (Champions == null) return null;
            foreach (var d in Champions) if (d != null && d.Class == c) return d;
            return null;
        }
    }
}
