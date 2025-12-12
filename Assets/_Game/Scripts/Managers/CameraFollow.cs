using UnityEngine;

namespace ElementalBuddies
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target")]
        public Transform Target; // Zieh hier deinen Player rein

        [Header("Settings")]
        public Vector3 Offset = new Vector3(0, 15, -8); // Höhe und Abstand
        public float SmoothSpeed = 5f;
        public bool LookAtTarget = false; // Wenn true, rotiert die Kamera mit (meist nicht gewollt bei TopDown)

        void LateUpdate()
        {
            if (Target == null)
            {
                // Versuche Player automatisch zu finden, falls noch nicht zugewiesen
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) Target = player.transform;
                return;
            }

            // Berechne gewünschte Position basierend auf Player-Position + Offset
            Vector3 desiredPosition = Target.position + Offset;
            
            // Weiche Bewegung (Lerp)
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, SmoothSpeed * Time.deltaTime);
            transform.position = smoothedPosition;

            if (LookAtTarget)
            {
                transform.LookAt(Target);
            }
        }
    }
}