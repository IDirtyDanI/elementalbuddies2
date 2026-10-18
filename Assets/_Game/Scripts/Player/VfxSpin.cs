using UnityEngine;

namespace ElementalBuddies
{
    // Dreht ein Effekt-Teil gleichmäßig um eine lokale Achse (Runenringe des Arkanballs, ArcaneVfxBuilder). Rein optisch.
    public class VfxSpin : MonoBehaviour
    {
        public Vector3 Axis = Vector3.forward;
        [Tooltip("Grad pro Sekunde (negativ = gegenläufig).")]
        public float DegreesPerSecond = 240f;
        [Tooltip("Zufälliger Startwinkel, damit mehrere Kugeln nicht im Gleichschritt drehen.")]
        public bool RandomStart = true;

        void OnEnable()
        {
            if (RandomStart) transform.Rotate(Axis, Random.Range(0f, 360f), Space.Self);
        }

        void Update()
        {
            transform.Rotate(Axis, DegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
