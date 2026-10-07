using UnityEngine;

namespace ElementalBuddies
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target")]
        public Transform Target; // Wird zur Laufzeit auf PlayerAvatar.Local gesetzt (eigene Figur)

        [Header("Settings")]
        public Vector3 Offset = new Vector3(0, 15, -8); // Höhe und Abstand
        public float SmoothSpeed = 5f;
        public bool LookAtTarget = false; // Wenn true, rotiert die Kamera mit (meist nicht gewollt bei TopDown)

        private bool _snapped;

        void LateUpdate()
        {
            // Mehrspieler: immer der eigenen Figur folgen (sie wird erst nach dem Netz-Spawn erzeugt)
            var local = PlayerAvatar.Local;
            if (local != null && Target != local.transform)
            {
                Target = local.transform;
                _snapped = false;
            }
            if (Target == null)
            {
                // Ohne Netz-Figur (z. B. Testszene ohne Netzwerk): Player-Tag
                if (PlayerAvatar.All.Count == 0)
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    if (player != null) Target = player.transform;
                }
                return;
            }

            // Erstes Binden: direkt hinspringen statt quer über die Karte zu gleiten
            if (!_snapped)
            {
                _snapped = true;
                transform.position = Target.position + Offset;
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