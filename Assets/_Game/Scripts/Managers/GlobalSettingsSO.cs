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

        [Header("Seelensplitter-Drops")]
        [Tooltip("Ab diesem Abstand zum Spieler fliegt ein Splitter-Drop zu ihm.")]
        public float ShardMagnetRadius = 3f;
        [Tooltip("Abstand, ab dem der Drop eingesammelt wird.")]
        public float ShardCollectRadius = 0.5f;
        [Tooltip("Endgeschwindigkeit (m/s) des Magnet-Flugs.")]
        public float ShardMagnetSpeed = 14f;
        [Tooltip("Optik für Drops mit Wert < 10 (leer = Laufzeit-Primitive).")]
        public GameObject ShardPickupSmall;
        [Tooltip("Optik für Drops mit Wert < 40.")]
        public GameObject ShardPickupMedium;
        [Tooltip("Optik für Drops mit Wert ≥ 40.")]
        public GameObject ShardPickupLarge;
        [Tooltip("Optional: Effekt beim Einsammeln (wird nach 3 s zerstört).")]
        public GameObject ShardCollectEffect;

        [Header("Buddy Slots")]
        public int StartBuddySlots = 4;
        public int SlotEveryNWaves = 3; // Alle N abgeschlossenen Wellen +1 Slot
        public int MaxBuddySlots = 30;

        [Header("Buddy-Aufwertung")]
        public int BuddyMaxLevel = 3;
        public float DamageBonusPerLevel = 0.35f; // +35 % pro Stufe über 1
        public float FireRateBonusPerLevel = 0.2f; // +20 % pro Stufe über 1
        public float RangeBonusPerLevel = 0.1f; // +10 % pro Stufe über 1
        public float HPBonusPerLevel = 0.35f; // +35 % Leben pro Stufe über 1
        // Kosten-Faktor auf CostOutCombat: [0] = Stufe 2, [1] = Stufe 3, ... (letzter Wert gilt für höhere Stufen)
        public float[] UpgradeCostFactors = new float[] { 0.6f, 1.0f };

        [Header("Buddy-Leben")]
        [Tooltip("Anteil des Max-Lebens, den jeder Buddy am Wellenende zurückbekommt.")]
        [Range(0f, 1f)] public float BuddyWaveEndHealPercent = 0.5f;
        [Tooltip("World-Space-HP-Bar (mit EnemyHealthBar) über Buddies.")]
        public GameObject BuddyHealthBarPrefab;
        [Tooltip("Optional: Effekt beim Zerstören eines Buddys (3 s).")]
        public GameObject BuddyDeathEffectPrefab;
        [Tooltip("Optional: Treffer-Effekt am Buddy (höchstens alle 0,5 s pro Buddy).")]
        public GameObject BuddyHitEffectPrefab;
        [Tooltip("Optional: Betäubungs-Optik am Buddy (z. B. Sterne wie VFX_Blinded), wenn die Boss-Fähigkeit keine eigene mitbringt.")]
        public GameObject BuddyStunEffectPrefab;
    }
}