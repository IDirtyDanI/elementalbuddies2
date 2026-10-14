using UnityEngine;

namespace ElementalBuddies
{
    public enum UnitType
    {
        Fire,
        Ice,
        Earth,
        Light,
        EnemyRunner, // For EnemyConfigSO which will inherit from this
        // Fusions-Buddies (angehängt, damit serialisierte Werte nicht verrutschen)
        Lightning,
        Water,
        Air,
        Shadow,
        Magma,
        Crystal,
        // Super-Elementare (Tri-Fusion, angehängt)
        VolcanoTitan,
        StormLord,
        Phoenix,
        WorldTree
    }

    [CreateAssetMenu(fileName = "UnitConfig", menuName = "ElementalBuddies/Unit Config", order = 1)]
    public class UnitConfigSO : ScriptableObject
    {
        public UnitType Type;
        public float CostOutCombat;
        public float Range;
        public float FireRate; // Attacks per second
        public float Damage;
        public GameObject Prefab; // Reference to the unit's prefab

        [Header("Buddy-Leben")]
        [Tooltip("Basis-Leben des Buddys auf Stufe 1 (0 = Standard der Buddy-Klasse).")]
        public float BuddyMaxHP = 0f;

        [Header("Fusion")]
        [Tooltip("Schadensfaktor gegen Bosse (nur Fusions-Buddies werten ihn aus; 1 = kein Bonus). 2er-Fusionen sind auf viele " +
                 "Ziele ausgelegt und gegen einzelne Bosse mit viel Leben sonst zu schwach (Balancing-Modell, Strategie „fusion“).")]
        public float BossDamageMultiplier = 1f;

        // Referenz auf die globalen Einstellungen (früher für den Kampf-Aufschlag CostInCombat)
        private GlobalSettingsSO _globalSettings;
        public GlobalSettingsSO GlobalSettings
        {
            get
            {
                if (_globalSettings == null)
                {
                    // Attempt to load from resources or find existing instance
                    _globalSettings = Resources.Load<GlobalSettingsSO>("GlobalSettings");
                    if (_globalSettings == null)
                    {
                        Debug.LogError("GlobalSettingsSO not found! Please create one in Assets/ScriptableObjects/Configs.");
                    }
                }
                return _globalSettings;
            }
        }
    }
}