using UnityEngine;

namespace ElementalBuddies
{
    // Reine Optik für den Phönix (sitzt am fliegenden Kind "Visual"): setzt den Animator-Bool "Flying", solange
    // PhoenixBuddy.IsDiving (Cast → Fly-Loop → Idle), schaltet Sturzflug-Schweife/-Partikel nur während des Flugs ein
    // und spielt "Rebirth" (Trigger + Partikel), wenn dieser Phönix selbst wiedergeboren wurde (PhoenixRebirth.OnReborn).
    // Kein Einfluss auf Spiellogik.
    public class PhoenixVisualFx : MonoBehaviour
    {
        private static readonly int FlyingHash = Animator.StringToHash("Flying");
        private static readonly int RebirthHash = Animator.StringToHash("Rebirth");

        [Tooltip("Leer = PhoenixBuddy im Eltern-Objekt.")]
        public PhoenixBuddy Owner;
        [Tooltip("Leer = Animator in den Kindern.")]
        public Animator Animator;
        [Tooltip("Emittieren nur während des Sturzflugs.")]
        public TrailRenderer[] DiveTrails;
        [Tooltip("Emittieren nur während des Sturzflugs.")]
        public ParticleSystem[] DiveParticles;
        [Tooltip("Werden bei der eigenen Wiedergeburt abgespielt.")]
        public ParticleSystem[] RebirthParticles;

        private bool _flying;

        void Awake()
        {
            if (Owner == null) Owner = GetComponentInParent<PhoenixBuddy>();
            if (Animator == null) Animator = GetComponentInChildren<Animator>();
            Apply(false);
            ClearTrails();
        }

        void OnEnable()
        {
            PhoenixRebirth.OnReborn += HandleReborn;
            ClearTrails();
        }

        void OnDisable()
        {
            PhoenixRebirth.OnReborn -= HandleReborn;
            Apply(false);
        }

        void LateUpdate()
        {
            bool f = Owner != null && Owner.IsDiving;
            if (f != _flying) Apply(f);
        }

        private void Apply(bool flying)
        {
            _flying = flying;
            if (Animator != null && Animator.isActiveAndEnabled && Animator.runtimeAnimatorController != null)
                Animator.SetBool(FlyingHash, flying);
            // Schweife beim Start leeren: sonst verbindet der Trail den letzten gespeicherten Punkt (Spawn-/Ruheposition,
            // z. B. nach Versetzen des Buddys) per Linie mit der aktuellen Position
            if (DiveTrails != null)
                foreach (var t in DiveTrails)
                {
                    if (t == null) continue;
                    if (flying && !t.emitting) t.Clear();
                    t.emitting = flying;
                }
            if (DiveParticles != null)
                foreach (var ps in DiveParticles)
                {
                    if (ps == null) continue;
                    var em = ps.emission;
                    em.enabled = flying;
                }
        }

        private void ClearTrails()
        {
            if (DiveTrails == null) return;
            foreach (var t in DiveTrails)
                if (t != null) t.Clear();
        }

        private void HandleReborn(ElementalBuddy buddy)
        {
            if (buddy == null || buddy != Owner) return;
            if (Animator != null && Animator.isActiveAndEnabled && Animator.runtimeAnimatorController != null)
                Animator.SetTrigger(RebirthHash);
            if (RebirthParticles != null)
                foreach (var ps in RebirthParticles)
                    if (ps != null) ps.Play(true);
        }
    }
}
