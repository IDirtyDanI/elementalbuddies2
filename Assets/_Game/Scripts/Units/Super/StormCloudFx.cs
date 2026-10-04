using UnityEngine;

namespace ElementalBuddies
{
    // Reine Optik für VFX_StormCloud (Sturmfürst): lässt das Wolken-Mesh beim Erscheinen aufquellen, dreht es langsam
    // und hält den Bodenteil (Schattenscheibe, Bereichsring, Spritzer) auf dem Boden unter der Wolke – StormCloud skaliert
    // beim Ausklingen die ganze Wolke, ohne Ausgleich würde der Bodenteil dabei mit nach oben wandern; zudem liegt die
    // Wolken-„Bodenhöhe" auf Gegner-Pivot-Höhe, daher wird der echte Boden per Raycast gesucht.
    public class StormCloudFx : MonoBehaviour
    {
        public Transform Body;            // Wolken-Mesh (+ innere Blitze)
        public Transform Ground;          // Bodenteil (Schatten, Ring, Spritzer)
        public float Height = 5f;         // = StormLordBuddy.CloudHeight
        public float GrowTime = 0.6f;
        public float SpinSpeed = 6f;      // Grad/s

        private float _age;
        private Vector3 _bodyScale = Vector3.one;

        void Awake()
        {
            if (Body != null) _bodyScale = Body.localScale;
            Apply();
        }

        void LateUpdate()
        {
            _age += Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            if (Body != null)
            {
                float k = GrowTime > 0f ? Mathf.Clamp01(_age / GrowTime) : 1f;
                k = 1f - (1f - k) * (1f - k); // ease out
                Body.localScale = _bodyScale * Mathf.Lerp(0.25f, 1f, k);
                Body.localRotation = Quaternion.Euler(0f, _age * SpinSpeed, 0f);
            }
            if (Ground != null)
            {
                // StormCloud führt seine Bodenhöhe zur Pivot-Höhe der Gegner nach (Läufer: 1 m über dem Boden) → echten Boden suchen
                Vector3 p = transform.position - Vector3.up * Height;
                Ground.position = new Vector3(p.x, SuperBuddy.GroundPoint(p).y, p.z);
            }
        }
    }
}
