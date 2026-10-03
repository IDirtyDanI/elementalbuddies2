using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Auf der Kamera: blendet Gebäude (FadeOccluder) zwischen Kamera und Spieler aus.
    public class OcclusionFader : MonoBehaviour
    {
        public Transform Target;
        public float Radius = 0.9f;
        public LayerMask Layers = ~0;

        private readonly HashSet<FadeOccluder> _current = new HashSet<FadeOccluder>();
        private readonly HashSet<FadeOccluder> _next = new HashSet<FadeOccluder>();
        private readonly RaycastHit[] _hits = new RaycastHit[32];

        void LateUpdate()
        {
            if (Target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                Target = p.transform;
            }
            _next.Clear();
            Vector3 from = transform.position;
            Vector3 to = Target.position + Vector3.up * 1.0f;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            int n = Physics.SphereCastNonAlloc(from, Radius, dir / dist, _hits, dist - 0.5f, Layers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var occ = _hits[i].collider.GetComponentInParent<FadeOccluder>();
                if (occ != null) _next.Add(occ);
            }
            foreach (var o in _current) if (o != null && !_next.Contains(o)) o.SetOccluding(false);
            foreach (var o in _next) o.SetOccluding(true);
            _current.Clear();
            foreach (var o in _next) _current.Add(o);
        }
    }
}
