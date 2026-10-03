using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Anzeige-Daten eines Champions (Hauptmenü, Tooltips, Pause-Übersicht)
    [CreateAssetMenu(fileName = "Champion", menuName = "BuddyTD/Champion")]
    public class ChampionDefinitionSO : ScriptableObject
    {
        [System.Serializable]
        public class AbilityInfo
        {
            public string Key;          // z. B. "LMB", "RMB", "R", "F", "C", "V"
            public string Name;
            [TextArea] public string Description;
            public Sprite Icon;
            [Tooltip("-1 = immer verfügbar, 0..3 = Element (Feuer, Eis, Erde, Licht), wird über den Schrein freigeschaltet")]
            public int Element = -1;
        }

        public ChampionClass Class;
        public string DisplayName;
        public string Tagline;
        [TextArea(3, 6)] public string Description;
        public Sprite Portrait;
        [Tooltip("Visual-Prefab für die 3D-Vorschau im Menü (Modell + Animator mit Idle).")]
        public GameObject PreviewPrefab;
        public Color AccentColor = Color.white;
        public List<AbilityInfo> Abilities = new List<AbilityInfo>();
    }
}
