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
        public bool IsPercentage; // If true, Value 10 means +10% (whole percent). If false, Value 5 means +5 flat.
    }
}