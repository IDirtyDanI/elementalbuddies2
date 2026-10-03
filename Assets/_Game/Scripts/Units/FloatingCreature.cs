using UnityEngine;

namespace ElementalBuddies
{
    // Idle-Animation für das Stufe-1-Visual (statisches Mesh ohne Animator): Schweben, Drehen/Pendeln, "Atmen"
    // und ein Squash-and-Stretch-Punch (+ kurze Ausfallbewegung zum Ziel) bei jeder Aktion des Buddys.
    // Auf das Stage1Visual-Objekt legen. Ausgangswerte = localPosition/-Rotation/-Scale beim ersten Aktivieren.
    public class FloatingCreature : MonoBehaviour
    {
        public enum IdleMotion { Spin, Sway }

        [Header("Schweben")]
        public float BobAmplitude = 0.08f;
        public float BobFrequency = 1.6f; // Hz

        [Header("Drehen / Pendeln")]
        public IdleMotion Motion = IdleMotion.Spin;
        public float SpinSpeed = 30f; // Grad pro Sekunde (Spin)
        public float SwayAngle = 12f; // Grad (Sway)
        public float SwayFrequency = 0.5f; // Hz (Sway)

        [Header("Atmen")]
        public float BreathAmount = 0.04f; // ±4 %
        public float BreathFrequency = 0.8f; // Hz

        [Header("Cast-Punch")]
        public float PunchScale = 1.25f;
        public float PunchDuration = 0.25f;
        public float LungeDistance = 0.15f; // 0 = keine Ausfallbewegung

        // Zusätzlicher Skalierungs-Faktor von außen (z. B. Entwicklungs-Pop durch BuddyEvolution)
        public float ScaleMultiplier { get; set; } = 1f;

        private ElementalBuddy _owner;
        private bool _baselineSet;
        private Vector3 _basePos;
        private Quaternion _baseRot;
        private Vector3 _baseScale;
        private float _phase; // Zufallsversatz, damit mehrere Buddies nicht synchron wippen
        private float _yaw;
        private float _punchTime = -1f;

        void Awake()
        {
            CaptureBaseline();
            _phase = Random.value * 10f;
        }

        void OnEnable()
        {
            CaptureBaseline();
            if (_owner == null) _owner = GetComponentInParent<ElementalBuddy>();
            if (_owner != null) _owner.OnCast += HandleCast;
            _punchTime = -1f;
        }

        void OnDisable()
        {
            if (_owner != null) _owner.OnCast -= HandleCast;
            if (!_baselineSet) return;
            // Ausgangszustand wiederherstellen (z. B. beim Wechsel auf Stufe 2)
            transform.localPosition = _basePos;
            transform.localRotation = _baseRot;
            transform.localScale = _baseScale;
        }

        private void CaptureBaseline()
        {
            if (_baselineSet) return;
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            _baseScale = transform.localScale;
            _baselineSet = true;
        }

        private void HandleCast()
        {
            _punchTime = 0f;
        }

        void Update()
        {
            float t = Time.time + _phase;

            // Schweben
            Vector3 pos = _basePos + Vector3.up * (Mathf.Sin(t * BobFrequency * 2f * Mathf.PI) * BobAmplitude);

            // Drehen / Pendeln
            float yaw;
            if (Motion == IdleMotion.Spin)
            {
                _yaw = (_yaw + SpinSpeed * Time.deltaTime) % 360f;
                yaw = _yaw;
            }
            else
            {
                yaw = Mathf.Sin(t * SwayFrequency * 2f * Mathf.PI) * SwayAngle;
            }

            // Atmen
            float breath = 1f + Mathf.Sin(t * BreathFrequency * 2f * Mathf.PI) * BreathAmount;
            Vector3 scale = _baseScale * breath;

            // Cast-Punch: Strecken nach oben (Volumen grob erhalten), dann zurück
            if (_punchTime >= 0f)
            {
                _punchTime += Time.deltaTime;
                float k = PunchDuration > 0f ? Mathf.Clamp01(_punchTime / PunchDuration) : 1f;
                float w = Mathf.Sin(k * Mathf.PI); // 0 -> 1 -> 0
                float stretch = Mathf.Lerp(1f, PunchScale, w);
                float squash = 1f / Mathf.Sqrt(stretch);
                scale = new Vector3(scale.x * squash, scale.y * stretch, scale.z * squash);

                if (LungeDistance > 0f && _owner != null && _owner.CurrentTarget != null)
                {
                    Vector3 dir = _owner.CurrentTarget.position - transform.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        Vector3 localDir = transform.parent != null ? transform.parent.InverseTransformDirection(dir.normalized) : dir.normalized;
                        pos += localDir * (LungeDistance * w);
                    }
                }

                if (k >= 1f) _punchTime = -1f;
            }

            transform.localPosition = pos;
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * _baseRot;
            transform.localScale = scale * ScaleMultiplier;
        }
    }
}
