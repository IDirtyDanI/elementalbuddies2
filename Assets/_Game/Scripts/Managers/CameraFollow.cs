using UnityEngine;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    // Früh im LateUpdate: Billboards (HP-Leisten, Pings, Sprechblasen) und der OcclusionFader lesen danach schon die
    // Kamera-Lage dieses Frames – sonst hinken sie beim Drehen um einen Frame hinterher
    [DefaultExecutionOrder(-50)]
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target")]
        public Transform Target; // Wird zur Laufzeit auf PlayerAvatar.Local gesetzt (eigene Figur)

        [Header("Settings")]
        public Vector3 Offset = new Vector3(0, 15, -8); // Höhe und Abstand (bei Drehwinkel 0, wird um die Figur gedreht)
        public float SmoothSpeed = 5f;
        public bool LookAtTarget = false; // Wenn true, rotiert die Kamera mit (meist nicht gewollt bei TopDown)

        [Header("Drehen um die Figur (Orbit)")]
        [Tooltip("Drehgeschwindigkeit beim Halten von „Kamera links/rechts drehen“ (Grad pro Sekunde, Echtzeit).")]
        public float RotateSpeed = 90f;
        [Tooltip("Glättung des Drehwinkels beim Drehen (s) – kleiner = direkter.")]
        public float RotateSmoothTime = 0.12f;
        [Tooltip("Glättung beim Zurücksetzen auf die Standardansicht (s).")]
        public float ResetSmoothTime = 0.3f;

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
        private Vector3 _followPos; // geglätteter Drehpunkt (Figurposition), die Kamera sitzt bei _followPos + gedrehtem Offset
        private Quaternion _baseRotation; // Kamera-Rotation bei Drehwinkel 0 (Neigung aus der Szene)
        private float _trauma;

        // Drehwinkel um die Figur (ungewickelt, Grad): _yaw = geglättet, _targetYaw = Ziel der Eingabe
        private float _yaw, _targetYaw, _yawVelocity;
        private bool _resetting;
        private InputAction _rotateLeftAction, _rotateRightAction, _rotateResetAction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        // Horizontale Blickrichtung der Spielkamera als Welt-Gierwinkel (Grad, 0 = Blick nach +Z). Grundlage für
        // kamerabezogenes Laufen (PlayerController.MoveWorld); ohne CameraFollow 0 (= frühere feste Ansicht).
        public static float Yaw =>
            _instance != null ? CameraOrbitMath.WrapAngle(_instance._baseRotation.eulerAngles.y + _instance._yaw) : 0f;

        // Nur der vom Spieler eingestellte Drehwinkel (0 = Standardansicht), z. B. für Anzeigen
        public static float OrbitYaw => _instance != null ? CameraOrbitMath.WrapAngle(_instance._yaw) : 0f;

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
            _followPos = transform.position - Offset;
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

            // Drehwinkel bleibt beim Wechsel der Figur (Wiederbelebung) erhalten
            UpdateYaw();
            Quaternion yawRotation = CameraOrbitMath.YawRotation(_yaw);

            // Erstes Binden: direkt hinspringen statt quer über die Karte zu gleiten
            if (!_snapped)
            {
                _snapped = true;
                _followPos = Target.position;
            }

            // Weiche Verfolgung des Drehpunkts (Lerp) – ohne Wackel-Versatz, damit sich das Wackeln nicht in die
            // Verfolgung schleppt. Der Versatz wird erst danach gedreht: beim Drehen bleibt der Abstand zur Figur exakt.
            _followPos = Vector3.Lerp(_followPos, Target.position, SmoothSpeed * Time.deltaTime);
            transform.position = _followPos + yawRotation * Offset;

            if (LookAtTarget)
            {
                transform.LookAt(Target);
                _baseRotation = Quaternion.Inverse(yawRotation) * transform.rotation;
            }
            else transform.rotation = yawRotation * _baseRotation;

            ApplyShake();
        }

        // Eingabe „Kamera links/rechts drehen“ (gedrückt halten, stufenlos) und „Kamera zurücksetzen“ (weich auf 0°).
        // Echtzeit, damit Treffer-Stopp/Zeitlupe das Drehen nicht bremsen; im Pause-Menü keine Eingabe.
        private void UpdateYaw()
        {
            float dt = Time.unscaledDeltaTime;
            if (!PauseManager.IsPaused)
            {
                if (_rotateLeftAction == null) _rotateLeftAction = KeyBindings.Action("CameraLeft");
                if (_rotateRightAction == null) _rotateRightAction = KeyBindings.Action("CameraRight");
                if (_rotateResetAction == null) _rotateResetAction = KeyBindings.Action("CameraReset");

                float axis = (IsHeld(_rotateRightAction) ? 1f : 0f) - (IsHeld(_rotateLeftAction) ? 1f : 0f);
                if (axis != 0f)
                {
                    _resetting = false;
                    _targetYaw = CameraOrbitMath.StepTargetYaw(_targetYaw, axis, RotateSpeed, dt);
                }
                else if (_rotateResetAction != null && _rotateResetAction.WasPressedThisFrame())
                {
                    _resetting = true;
                    _targetYaw = CameraOrbitMath.ResetTarget(_yaw);
                }
            }

            float smooth = Mathf.Max(0.01f, _resetting ? ResetSmoothTime : RotateSmoothTime);
            _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVelocity, smooth, Mathf.Infinity, dt);
            if (_resetting && Mathf.Abs(Mathf.DeltaAngle(_yaw, _targetYaw)) < 0.05f)
            {
                _yaw = _targetYaw;
                _yawVelocity = 0f;
                _resetting = false;
            }

            // Ungewickelten Winkel klein halten (gleiche Verschiebung für Ist und Ziel, Ansicht bleibt gleich)
            if (Mathf.Abs(_yaw) > 720f)
            {
                float shift = CameraOrbitMath.ResetTarget(_yaw);
                _yaw -= shift;
                _targetYaw -= shift;
            }
        }

        private static bool IsHeld(InputAction action) => action != null && action.enabled && action.IsPressed();

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
