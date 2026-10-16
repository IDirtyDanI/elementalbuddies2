using UnityEngine;

namespace ElementalBuddies
{
    // Dreht ein Teil gleichmäßig um eine lokale Achse (Walddorf: Mühlrad der Wassermühle, Prefab Dorf_Muehle).
    // Rein optisch: nicht statisch, nicht im NavMesh (VillageSetup.BuildVillagePrefabs).
    public class WaterWheel : MonoBehaviour
    {
        [Tooltip("Lokale Drehachse (Mühlrad: Achse = lokales Z).")]
        public Vector3 Axis = Vector3.forward;
        [Tooltip("Grad pro Sekunde (positiv = Wasser kommt von +X auf den Scheitel).")]
        public float DegreesPerSecond = 28f;

        void Update()
        {
            transform.Rotate(Axis, DegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
