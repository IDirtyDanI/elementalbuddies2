using UnityEngine;

namespace ElementalBuddies
{
    [CreateAssetMenu(fileName = "GlobalSettings", menuName = "ElementalBuddies/Global Settings", order = 0)]
    public class GlobalSettingsSO : ScriptableObject
    {
        [Header("Mana Settings")]
        [Tooltip("Nicht mehr genutzt: Das Spiel startet immer mit vollem Mana (ManaCap), jede Welle füllt beim Start und Ende auf.")]
        public float StartMana = 100f;
        public float ManaCap = 200f;
        public float RegenOutCombat = 1.5f;
        public float RegenInCombat = 0.5f;

        [Header("Seelensplitter (Bau-Währung)")]
        public float StartShards = 130f;
        public float ShardsPerKill = 6f; // Splitter-Kopfgeld pro getötetem Gegner
        public float WaveBonusShardsBase = 40f; // Wellen-Bonus nach Welle 1
        public float WaveBonusShardsPerWave = 10f; // + pro weiterer abgeschlossener Welle
        public float CombatSurcharge = 1.25f; // Multiplikator (25% = 1.25)
        public float RefundRatio = 0.7f; // 70% Rückerstattung

        [Header("Buddy Slots")]
        public int StartBuddySlots = 4;
        public int SlotEveryNWaves = 3; // Alle N abgeschlossenen Wellen +1 Slot
        public int MaxBuddySlots = 12;

        [Header("Buddy-Aufwertung")]
        public int BuddyMaxLevel = 3;
        public float DamageBonusPerLevel = 0.35f; // +35 % pro Stufe über 1
        public float FireRateBonusPerLevel = 0.2f; // +20 % pro Stufe über 1
        public float RangeBonusPerLevel = 0.1f; // +10 % pro Stufe über 1
        // Kosten-Faktor auf CostOutCombat: [0] = Stufe 2, [1] = Stufe 3, ... (letzter Wert gilt für höhere Stufen)
        public float[] UpgradeCostFactors = new float[] { 0.6f, 1.0f };
    }
}