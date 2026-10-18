using UnityEngine;

namespace ElementalBuddies
{
    // Reine Rechenregeln der Orbit-Kamera (Gier-Winkel um die Figur) und der kamerabezogenen Laufrichtung. Eigene
    // Assembly (ElementalBuddies.Rules), damit EditMode-Tests sie ohne Assembly-CSharp prüfen können.
    // Konvention: Yaw 0 = Kamera schaut nach Welt +Z (Norden, Standardansicht); positiver Yaw dreht im Uhrzeigersinn
    // von oben gesehen (wie Unitys Euler-Y). Blick nach links schwenken = Yaw kleiner, nach rechts = Yaw größer.
    public static class CameraOrbitMath
    {
        // Winkel auf [−180, 180) bringen
        public static float WrapAngle(float degrees) => Mathf.Repeat(degrees + 180f, 360f) - 180f;

        public static Quaternion YawRotation(float yaw) => Quaternion.Euler(0f, yaw, 0f);

        // Kamera-Versatz zur Figur (Höhe/Abstand) um die Hochachse der Figur gedreht
        public static Vector3 OrbitOffset(Vector3 offset, float yaw) => YawRotation(yaw) * offset;

        // Horizontale Blickrichtung der Kamera bei diesem Yaw (normiert)
        public static Vector3 FlatForward(float yaw) => YawRotation(yaw) * Vector3.forward;

        // Laufeingabe (x = rechts, y = vorne, Bildschirm-/Kamerasicht) → Weltrichtung in der XZ-Ebene.
        // Die Länge der Eingabe bleibt erhalten (WASD-Composite liefert bereits normierte Diagonalen).
        public static Vector3 InputToWorld(Vector2 input, float yaw)
        {
            Vector3 v = YawRotation(yaw) * new Vector3(input.x, 0f, input.y);
            v.y = 0f;
            return v;
        }

        // Ziel-Yaw beim Halten der Drehtasten: axis −1 (links) … +1 (rechts), speed in Grad pro Sekunde
        public static float StepTargetYaw(float targetYaw, float axis, float speed, float deltaTime)
        {
            return targetYaw + Mathf.Clamp(axis, -1f, 1f) * Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);
        }

        // Zurücksetzen: nächstgelegener Winkel, der der Standardansicht (0°) entspricht – so dreht die Kamera den
        // kürzeren Weg zurück, auch wenn der (ungewickelte) Yaw schon mehrere Umdrehungen gesammelt hat
        public static float ResetTarget(float currentYaw) => currentYaw - WrapAngle(currentYaw);
    }
}
