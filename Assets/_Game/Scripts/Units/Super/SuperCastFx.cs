using UnityEngine;

namespace ElementalBuddies
{
    // Reine Optik für Super-Elementare: spielt bei jeder Aktion des Buddys (ElementalBuddy.OnCast, gleichzeitig mit dem
    // Animator-Trigger "Cast") nach Delay alle Partikelsysteme unter diesem Objekt ab – z. B. den Wurf-Blitz am ThrowPoint,
    // passend zum Freigabe-Moment der Cast-Animation. Kein Einfluss auf Spiellogik.
    public class SuperCastFx : MonoBehaviour
    {
        [Tooltip("Sekunden nach dem Cast-Trigger (Freigabe-Frame der Animation ÷ State-Speed).")]
        public float Delay = 0.4f;
        [Tooltip("Leer = ElementalBuddy im Eltern-Objekt.")]
        public ElementalBuddy Owner;

        private ParticleSystem[] _particles;
        private float _playAt = -1f;

        void Awake()
        {
            if (Owner == null) Owner = GetComponentInParent<ElementalBuddy>();
            _particles = GetComponentsInChildren<ParticleSystem>(true);
        }

        void OnEnable()
        {
            if (Owner != null) Owner.OnCast += HandleCast;
        }

        void OnDisable()
        {
            if (Owner != null) Owner.OnCast -= HandleCast;
            _playAt = -1f;
        }

        private void HandleCast()
        {
            _playAt = Time.time + Delay;
        }

        void Update()
        {
            if (_playAt < 0f || Time.time < _playAt) return;
            _playAt = -1f;
            foreach (var ps in _particles)
                if (ps != null) ps.Play(false);
        }
    }
}
