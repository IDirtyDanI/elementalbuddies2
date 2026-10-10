using UnityEngine;

namespace ElementalBuddies
{
    [CreateAssetMenu(fileName = "NewUpgrade", menuName = "ElementalBuddies/Upgrade Definition")]
    public class UpgradeDefinitionSO : ScriptableObject
    {
        [Header("Display")]
        public string Title;
        [TextArea] public string Description;
        public Sprite Icon; 

        [Header("Effect")]
        public UpgradeType Type;
        public UpgradeTarget Target;
        public StatType StatToBuff;
        public float Value; 
        public bool IsPercentage; // true: Value 10 = +10 % des Basiswerts (additiv: n Karten = +n·10 %). false: Value 5 = +5 flach.

        [Header("Sonderwirkung (Sprint 3)")]
        public CardEffect Effect;
        [Tooltip("Zweiter Wert der Sonderwirkung (Dauer, Radius, Nachteil …), siehe CardEffect.")]
        public float Value2;
        [Tooltip("Schlüsselwörter für Kombos, kommagetrennt (z. B. „Brand, Eis“). Karten mit gemeinsamem Schlüsselwort " +
                 "zu bereits gewählten Karten kommen öfter und tragen den Hinweis „Kombo“.")]
        public string Keywords;

        [Header("Draft")]
        public CardRarity Rarity;
        public CardAffinity Affinity;
        [Tooltip("Höchstens so oft pro Run wählbar (0 = unbegrenzt), z. B. Beschwörerband 6.")]
        public int MaxPicks = 0;

        public string[] KeywordList
        {
            get
            {
                if (string.IsNullOrEmpty(Keywords)) return System.Array.Empty<string>();
                var parts = Keywords.Split(',');
                for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
                return parts;
            }
        }
    }
}