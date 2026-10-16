using UnityEngine;

namespace ElementalBuddies
{
    // Trefferkapsel eines Fliegers (Walddorf-Plan „Treffbarkeit“): Die CapsuleCollider der Wurzel ist ein Trigger auf
    // Layer Enemy und reicht von der Körperoberkante bis MinAboveGround über den Untergrund darunter (Boden oder Dach).
    // So treffen Boden-AoEs, horizontale Pfeile, Nahkampfkegel und Buddies den Flieger wie einen Bodengegner, ohne
    // Änderung an den Fähigkeiten. Bewusst an der WURZEL (nicht an einem Kind): viele Treffer-Wege lesen
    // other.GetComponent<IDamageable>() direkt am getroffenen Collider (BuddyProjectile, Effekte).
    // Trigger → kein Spieler-CharacterController und keine Gegner bleiben daran hängen.
    // Läuft auf jedem Rechner lokal (Position kommt auf Clients per NetworkTransform).
    [DisallowMultipleComponent]
    public class FlyerHurtbox : MonoBehaviour
    {
        [Tooltip("Oberkante über dem Pivot (Weltmeter).")]
        public float TopOffset = 0.5f;
        [Tooltip("Unterkante über dem Untergrund (Weltmeter).")]
        public float MinAboveGround = 0.3f;
        [Tooltip("Mindesthöhe der Kapsel (Weltmeter), z. B. kurz vor dem Aufsetzen.")]
        public float MinHeight = 1.0f;
        [Tooltip("Radius (Weltmeter).")]
        public float Radius = 0.5f;

        private CapsuleCollider _capsule;

        public CapsuleCollider Capsule => _capsule;
        // Weltkoordinaten der Kapsel (Tests/Logging)
        public float WorldBottom { get; private set; }
        public float WorldTop { get; private set; }

        void Awake()
        {
            _capsule = GetComponent<CapsuleCollider>();
            if (_capsule == null) _capsule = gameObject.AddComponent<CapsuleCollider>();
            _capsule.isTrigger = true;
            _capsule.direction = 1;
            Refresh();
        }

        void LateUpdate()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (_capsule == null) return;
            Vector3 pos = transform.position;
            float ground = pos.y - 2f;
            if (Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 60f, FlyingMotor.SurfaceMask, QueryTriggerInteraction.Ignore))
                ground = hit.point.y;
            FlightMath.HurtboxSpan(pos.y, TopOffset, ground, MinAboveGround, MinHeight, out float bottom, out float top);
            WorldBottom = bottom;
            WorldTop = top;
            // Welt → lokal (Wurzel-Skalierung, z. B. VisualScale/Elite)
            Vector3 s = transform.lossyScale;
            float sy = Mathf.Max(0.0001f, Mathf.Abs(s.y));
            float sxz = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)));
            float height = top - bottom;
            float mid = (top + bottom) * 0.5f;
            _capsule.isTrigger = true;
            _capsule.direction = 1;
            _capsule.radius = Radius / sxz;
            _capsule.height = Mathf.Max(height, Radius * 2f) / sy;
            // Rotation nur um Y → lokale Y-Achse = Welt-Y
            _capsule.center = new Vector3(0f, (mid - pos.y) / sy, 0f);
        }
    }
}
