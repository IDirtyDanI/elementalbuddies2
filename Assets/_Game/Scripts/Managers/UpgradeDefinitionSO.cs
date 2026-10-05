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

        [Header("Draft")]
        [Tooltip("Höchstens so oft pro Run wählbar (0 = unbegrenzt), z. B. Beschwörerband 6.")]
        public int MaxPicks = 0;
    }
}