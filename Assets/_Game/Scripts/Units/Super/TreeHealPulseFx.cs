using UnityEngine;

namespace ElementalBuddies
{
    // Reine Optik für VFX_TreeHealPulse (Weltenbaum): WorldTreeBuddy skaliert den Puls nach dem Instanziieren auf
    // (Radius, 1, Radius). Partikel würden dadurch verzerrt → in Start wird die Skalierung zurückgesetzt und der Radius
    // stattdessen auf die Partikelgrößen (ScaleSize) und Emissionsformen (ScaleShape) übertragen, danach starten alle Partikel.
    public class TreeHealPulseFx : MonoBehaviour
    {
        public ParticleSystem[] ScaleSize;
        public ParticleSystem[] ScaleShape;

        void Start()
        {
            float r = Mathf.Max(0.1f, transform.localScale.x);
            transform.localScale = Vector3.one;
            if (ScaleSize != null)
                foreach (var ps in ScaleSize)
                {
                    if (ps == null) continue;
                    var m = ps.main;
                    var c = m.startSize;
                    if (c.mode == ParticleSystemCurveMode.TwoConstants) { c.constantMin *= r; c.constantMax *= r; }
                    else if (c.mode == ParticleSystemCurveMode.Constant) c.constant *= r;
                    else c.curveMultiplier *= r;
                    m.startSize = c;
                }
            if (ScaleShape != null)
                foreach (var ps in ScaleShape)
                {
                    if (ps == null) continue;
                    var sh = ps.shape; sh.radius *= r;
                }
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                if (ps != null) ps.Play(false);
        }
    }
}
