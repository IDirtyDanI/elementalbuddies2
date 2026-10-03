using UnityEngine;

namespace ElementalBuddies
{
    [CreateAssetMenu(fileName = "GlobalSettings", menuName = "ElementalBuddies/Global Settings", order = 0)]
    public class GlobalSettingsSO : ScriptableObject
    {
        [Header("Mana Settings")]
        public float StartMana = 100f;
        public float ManaCap = 200f;
        public float RegenOutCombat = 1.5f;
        public float RegenInCombat = 0.5f;
        public float CombatSurcharge = 1.25f; // Multiplikator (25% = 1.25)
        public float RefundRatio = 0.7f; // 70% Rückerstattung
        public float ManaPerKill = 3f; // Mana-Bounty pro getötetem Gegner

        [Header("Buddy Slots")]
        public int StartBuddySlots = 4;
        public int SlotEveryNWaves = 3; // Alle N abgeschlossenen Wellen +1 Slot
        public int MaxBuddySlots = 12;
    }
}