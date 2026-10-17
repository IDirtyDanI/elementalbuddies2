using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

namespace ElementalBuddies
{
    [System.Serializable]
    public class EnemySpawnInfo
    {
        public EnemyConfigSO EnemyType;
        public int Count;
        public float SpawnInterval; // Delay between spawning each enemy of this type
        // Laufzeit (Zusatzgruppe mit ExtraEnemyGroup.FixedCount, z. B. Beschwörer): Anzahl nicht durch Wellen-Rampe,
        // Boss-Eskorte, Ernte oder Deckel verändern
        [System.NonSerialized] public bool FixedCount;
    }

    [CreateAssetMenu(fileName = "WaveConfig", menuName = "ElementalBuddies/Wave Config", order = 2)]
    public class WaveConfigSO : ScriptableObject
    {
        public List<EnemySpawnInfo> EnemiesToSpawn;
        public float StartDelay; // Delay before the first enemy of the wave spawns
        [FormerlySerializedAs("EndBonusMana")]
        public float EndBonusShards; // Extra Seelensplitter am Wellenende (zusätzlich zur Formel aus GlobalSettings)
    }
}