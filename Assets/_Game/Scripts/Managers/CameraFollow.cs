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

        [Header("Wackeln (Trauma-Modell)")]
        [Tooltip("Maximaler Versatz in Metern bei Trauma 1.")]
        public float ShakeMaxOffset = 0.55f;
        [Tooltip("Maximale Neigung (Grad) bei Trauma 1.")]
        public float ShakeMaxRoll = 1.6f;
        [Tooltip("Rauschfrequenz des Wackelns.")]
        public float ShakeFrequency = 22f;
        [Tooltip("Abbau des Traumas pro Sekunde (Echtzeit, läuft auch im Treffer-Stopp).")]
        public float TraumaDecay = 1.8f;

        private static CameraFollow _instance;
        private bool _snapped;
        private Vector3 _followPos;
        private Quaternion _baseRotation;
        private float _trauma;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        // Wackeln auslösen (0..1, addiert sich, gedeckelt). Stärke = Trauma² × Spieler-Einstellung.
        public static void AddTrauma(float amount)
        {
            if (_instance == null || amount <= 0f) return;
            _instance._trauma = Mathf.Min(1f, _instance._trauma + amount);
        }

        // Wackeln mit Abstands-Abfall zur eigenen Figur (z. B. Boss-Einschläge)
        public static void AddTraumaAt(float amount, Vector3 worldPos, float radius = 25f)
        {
            if (_instance == null || _instance.Target == null) return;
            float d = Vector3.Distance(_instance.Target.position, worldPos);
            float falloff = 1f - Mathf.Clamp01(d / Mathf.Max(0.01f, radius));
            AddTrauma(amount * falloff);
        }

        void Awake()
        {
            _instance = this;
            _followPos = transform.position;
            _baseRotation = transform.rotation;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

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
                _followPos = Target.position + Offset;
            }

            // Berechne gewünschte Position basierend auf Player-Position + Offset
            Vector3 desiredPosition = Target.position + Offset;

            // Weiche Bewegung (Lerp) – ohne Wackel-Versatz, damit sich das Wackeln nicht in die Verfolgung schleppt
            _followPos = Vector3.Lerp(_followPos, desiredPosition, SmoothSpeed * Time.deltaTime);
            transform.position = _followPos;

            if (LookAtTarget)
            {
                transform.LookAt(Target);
                _baseRotation = transform.rotation;
            }
            else transform.rotation = _baseRotation;

            ApplyShake();
        }

        private void ApplyShake()
        {
            if (_trauma <= 0f) return;
            float shake = _trauma * _trauma * GameFeel.ShakeStrength;
            float t = Time.unscaledTime * ShakeFrequency;
            if (shake > 0f)
            {
                float x = (Mathf.PerlinNoise(t, 0.1f) * 2f - 1f) * ShakeMaxOffset * shake;
                float y = (Mathf.PerlinNoise(0.7f, t) * 2f - 1f) * ShakeMaxOffset * shake;
                float roll = (Mathf.PerlinNoise(t, 3.3f) * 2f - 1f) * ShakeMaxRoll * shake;
                transform.position += transform.right * x + transform.up * y;
                transform.rotation *= Quaternion.Euler(0f, 0f, roll);
            }
            _trauma = Mathf.Max(0f, _trauma - TraumaDecay * Time.unscaledDeltaTime);
        }
    }
}
