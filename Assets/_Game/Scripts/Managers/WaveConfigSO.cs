using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    [System.Serializable]
    public class EnemySpawnInfo
    {
        public EnemyConfigSO EnemyType;
        public int Count;
        public float SpawnInterval; // Delay between spawning each enemy of this type
    }

    [CreateAssetMenu(fileName = "WaveConfig", menuName = "ElementalBuddies/Wave Config", order = 2)]
    public class WaveConfigSO : ScriptableObject
    {
        public List<EnemySpawnInfo> EnemiesToSpawn;
        public float StartDelay; // Delay before the first enemy of the wave spawns
        public float EndBonusMana; // Mana awarded at the end of the wave
    }
}