using UnityEngine;

namespace ElementalBuddies
{
    // Reine Optik: setzt beim Erscheinen die Ziel-Transforms auf den Boden darunter (SuperBuddy.GroundPoint).
    // Effekte werden oft an der Gegner-Position erzeugt – deren Pivot liegt je nach NavMeshAgent-BaseOffset bis zu
    // 1 m über dem Boden (Läufer), Bodenringe/Brandflecken würden sonst schweben.
    public class SnapToGround : MonoBehaviour
    {
        public Transform[] Targets;
        public float Offset = 0f;

        void Awake()
        {
            if (Targets == null) return;
            foreach (var t in Targets)
            {
                if (t == null) continue;
                Vector3 g = SuperBuddy.GroundPoint(t.position);
                t.position = new Vector3(t.position.x, g.y + Offset, t.position.z);
            }
        }
    }
}
