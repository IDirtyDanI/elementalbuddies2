using UnityEngine;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Input Settings")]
        [SerializeField] private InputActionAsset inputAsset;
        [SerializeField] private string actionMapName = "Player";

        [Header("Movement Settings")]
        public float MoveSpeed = 6.0f;
        public LayerMask FloorLayer;

        // Vom aktiven ChampionKit gesetzt (z. B. Schildblock = 0.5). MoveSpeed selbst bleibt der Wert der Upgrade-Karten.
        public float SpeedMultiplier { get; set; } = 1f;
        // Rolle / Sprung-Stampfer: Eingabe-Bewegung, Springen und Drehen aussetzen (Schwerkraft läuft weiter)
        public bool MovementLocked { get; set; }
        public bool RotationLocked { get; set; }

        // Verlangsamung durch Gegner (Boss-Fähigkeiten): stärkste laufende gewinnt, unabhängig von SpeedMultiplier.
        // Wirkt nur aufs normale Laufen (Rolle/Blink/Sprung-Stampfer bewegen selbst).
        private float _slowPercent;
        private float _slowUntil;
        public float SlowMultiplier => Time.time < _slowUntil ? 1f - _slowPercent : 1f;
        public bool IsSlowed => Time.time < _slowUntil && _slowPercent > 0f;

        // Rückstoß durch Gegner: Restweg, der über die Dauer abgebaut wird
        private Vector3 _pushDir;
        private float _pushRemaining, _pushTime, _pushDuration, _pushDistance;

        private CharacterController _characterController;
        public InputAction MoveAction { get; private set; } // Changed to public property
        private InputAction _aimAction;
        
        // Actions for skills/building will be handled by other managers or here later
        public InputAction FireAction { get; private set; }
        public InputAction Build1Action { get; private set; }
        public InputAction Build2Action { get; private set; }
        public InputAction Build3Action { get; private set; }
        public InputAction Build4Action { get; private set; }
        public InputAction Skill6Action { get; private set; } // Renamed from SkillQAction
        public InputAction SkillEAction { get; private set; }
        public InputAction JumpAction { get; private set; } // New Jump Action
        public InputAction SpellFireAction { get; private set; }  // R - Flammenwelle
        public InputAction SpellIceAction { get; private set; }   // F - Frostnova
        public InputAction SpellEarthAction { get; private set; } // C - Steinwall
        public InputAction SpellLightAction { get; private set; } // V - Heiliger Kreis

        // Last mouse point on the floor (world space). Falls back to a point in front of the player.
        public Vector3 AimPoint => _hasAimPoint ? _aimPoint : transform.position + transform.forward * 5f;
        public bool HasAimPoint => _hasAimPoint;
        // Horizontal aim direction (normalized); the player rotates toward the mouse, so this matches the facing
        public Vector3 AimDirection
        {
            get
            {
                Vector3 d = transform.forward;
                d.y = 0f;
                return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
            }
        }
        private Vector3 _aimPoint;
        private bool _hasAimPoint;

        private Camera _mainCamera;
        private Vector3 _playerVelocity; // To store velocity for jumping/gravity

        // Mehrspieler: Eingabe/Bewegung nur auf der eigenen Figur (PlayerAvatar = Besitzer). Ohne Avatar (offline) immer.
        private PlayerAvatar _avatar;
        private PlayerStats _stats;
        private bool _inputActive;
        public bool IsLocalControl => _avatar == null || _avatar.IsLocalControl;

        // Aktuelle Laufeingabe (x = rechts, y = vorne), zero ohne Eingabe / auf fremden Figuren
        public Vector2 MoveInput => IsLocalControl && MoveAction != null && _inputActive ? MoveAction.ReadValue<Vector2>() : Vector2.zero;

        [Header("Jump Settings")]
        public float JumpForce = 8.0f;
        public float Gravity = -9.81f;

        // Exposed for visuals/animation
        public bool IsGrounded => _characterController != null && _characterController.isGrounded;
        public event System.Action Jumped;

        void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _avatar = GetComponent<PlayerAvatar>();
            _stats = GetComponent<PlayerStats>();
            _mainCamera = Camera.main;
            // Absicherung: ohne gesetzte Boden-Ebene träfe der Maus-Raycast nichts (Zielen tot)
            if (FloorLayer.value == 0) FloorLayer = LayerMask.GetMask("Floor");

            if (inputAsset != null)
            {
                var map = inputAsset.FindActionMap(actionMapName);
                if (map != null)
                {
                    MoveAction = map.FindAction("Move");
                    _aimAction = map.FindAction("Aim");
                    FireAction = map.FindAction("Fire");
                    Build1Action = map.FindAction("Build1");
                    Build2Action = map.FindAction("Build2");
                    Build3Action = map.FindAction("Build3");
                    Build4Action = map.FindAction("Build4");
                    Skill6Action = map.FindAction("Skill6"); // Assign new Skill6 Action
                    SkillEAction = map.FindAction("SkillE");
                    JumpAction = map.FindAction("Jump");
                    SpellFireAction = map.FindAction("SpellFire");
                    SpellIceAction = map.FindAction("SpellIce");
                    SpellEarthAction = map.FindAction("SpellEarth");
                    SpellLightAction = map.FindAction("SpellLight");
                }
            }
        }

        void OnEnable()
        {
            // Das Input-Asset ist geteilt: nur die eigene Figur schaltet es ein (fremde Figuren nie)
            if (IsLocalControl) SetInputActive(true);
        }

        void OnDisable()
        {
            SetInputActive(false);
        }

        // Von PlayerAvatar nach der Besitzer-Erkennung (Netz-Spawn) aufgerufen
        public void SetInputActive(bool on)
        {
            if (inputAsset == null || _inputActive == on) return;
            _inputActive = on;
            if (on) inputAsset.Enable();
            else inputAsset.Disable();
        }

        void Update()
        {
            // Fremde Figur: Position/Drehung kommen per NetworkTransform vom Besitzer
            if (!IsLocalControl) return;
            // Ausgefallen (tot) -> keine Steuerung bis zur Wiederbelebung
            if (_stats != null && _stats.IsDead) return;
            // Pause-Menü offen -> kein Laufen/Drehen/Springen
            if (PauseManager.IsPaused) return;

            HandleMovement();
            if (!RotationLocked) HandleRotation();
            else UpdateAimPoint();
            GuardFallingThroughMap();
        }

        // ---------------- Sicherheitsnetz: durch die Map gefallen ----------------

        [Header("Sicherheitsnetz")]
        [Tooltip("Fällt die Figur so viele Meter unter die letzte sichere Bodenposition, wird sie dorthin zurückgesetzt.")]
        public float FallRecoverDepth = 8f;

        private Vector3 _lastSafePos;
        private bool _hasSafePos;
        private float _safeSampleAt;

        private void GuardFallingThroughMap()
        {
            if (_characterController == null || !_characterController.enabled) return;

            if (_characterController.isGrounded)
            {
                // Sichere Position höchstens 4× pro Sekunde merken, nur auf dem NavMesh (begehbarer Boden)
                if (Time.time >= _safeSampleAt)
                {
                    _safeSampleAt = Time.time + 0.25f;
                    if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out _, 2.5f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        _lastSafePos = transform.position;
                        _hasSafePos = true;
                    }
                }
                return;
            }

            float floorY = _hasSafePos ? _lastSafePos.y : 0f;
            if (transform.position.y > floorY - FallRecoverDepth) return;

            Vector3 target = _hasSafePos ? _lastSafePos : transform.position;
            if (!_hasSafePos && UnityEngine.AI.NavMesh.SamplePosition(new Vector3(target.x, 0f, target.z), out var hit, 50f, UnityEngine.AI.NavMesh.AllAreas))
                target = hit.position + Vector3.up * (_characterController.height * 0.5f);
            Debug.LogWarning($"[Player] Durch die Map gefallen ({transform.position.y:0.0}) – zurück auf {target}");
            _playerVelocity = Vector3.zero;
            _pushRemaining = 0f;
            if (_avatar != null && _avatar.IsSpawned) _avatar.TeleportLocal(target, transform.rotation);
            else
            {
                _characterController.enabled = false;
                transform.position = target;
                _characterController.enabled = true;
            }
        }

        private void HandleMovement()
        {
            if (MoveAction == null || _characterController == null || !_characterController.enabled) return;

            Vector2 input = MovementLocked ? Vector2.zero : MoveAction.ReadValue<Vector2>();
            Vector3 moveInput = new Vector3(input.x, 0, input.y);

            // Ground check for jumping
            if (_characterController.isGrounded)
            {
                _playerVelocity.y = 0f; // Reset vertical velocity when grounded

                if (!MovementLocked && JumpAction != null && JumpAction.WasPressedThisFrame())
                {
                    _playerVelocity.y = JumpForce;
                    Jumped?.Invoke();
                }
            }

            // Apply gravity
            _playerVelocity.y += Gravity * Time.deltaTime;

            // Apply movement input (Absolute / World Space)
            Vector3 movement = moveInput * (MoveSpeed * SpeedMultiplier * SlowMultiplier);
            Vector3 delta = (movement + _playerVelocity) * Time.deltaTime;

            // Rückstoß (ease-out)
            if (_pushRemaining > 0f)
            {
                _pushTime = Mathf.Min(_pushDuration, _pushTime + Time.deltaTime);
                float k = _pushTime / _pushDuration;
                float target = _pushDistance * (1f - (1f - k) * (1f - k));
                float step = Mathf.Min(_pushRemaining, target - (_pushDistance - _pushRemaining));
                if (step > 0f) { delta += _pushDir * step; _pushRemaining -= step; }
                if (_pushTime >= _pushDuration) _pushRemaining = 0f;
            }

            _characterController.Move(delta);
        }

        // Fremde Figur: Server leitet Slow/Rückstoß an den Besitzer weiter (der bewegt die Figur)
        private bool ForwardToOwner => !IsLocalControl && _avatar != null && _avatar.IsSpawned;

        // Von PlayerAvatar ausgelöst: Mitspieler ist gesprungen (Animation)
        public void RaiseRemoteJump()
        {
            Jumped?.Invoke();
        }

        // Wie EnemyBrain.ApplySlow: percent 0..1 (0.4 = 40 % langsamer), der stärkste laufende Slow gewinnt
        public void ApplySlow(float percent, float duration)
        {
            if (ForwardToOwner)
            {
                if (Net.IsServer) _avatar.SendSlowToOwner(percent, duration);
                return;
            }
            percent = Mathf.Clamp01(percent);
            if (percent <= 0f || duration <= 0f) return;
            bool active = Time.time < _slowUntil;
            if (!active || percent > _slowPercent)
            {
                _slowPercent = percent;
                _slowUntil = Time.time + duration;
            }
            else if (Mathf.Approximately(percent, _slowPercent))
            {
                _slowUntil = Mathf.Max(_slowUntil, Time.time + duration);
            }
        }

        // Horizontaler Rückstoß (z. B. Blutwirbel des Knochenfürsten)
        public void ApplyKnockback(Vector3 direction, float distance, float duration = 0.25f)
        {
            if (ForwardToOwner)
            {
                if (Net.IsServer) _avatar.SendKnockbackToOwner(direction, distance, duration);
                return;
            }
            direction.y = 0f;
            if (distance <= 0f || direction.sqrMagnitude < 0.0001f) return;
            _pushDir = direction.normalized;
            _pushDistance = distance;
            _pushRemaining = distance;
            _pushTime = 0f;
            _pushDuration = Mathf.Max(0.02f, duration);
        }

        // Nur den Mauspunkt aktualisieren (ohne Drehung), z. B. während einer Rolle
        private void UpdateAimPoint()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null || _aimAction == null) return;
            Ray ray = _mainCamera.ScreenPointToRay(_aimAction.ReadValue<Vector2>());
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, FloorLayer))
            {
                _aimPoint = hit.point;
                _hasAimPoint = true;
            }
        }

        private void HandleRotation()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) 
                {
                     Debug.LogError("PlayerController: No Camera tagged 'MainCamera' found!");
                     return;
                }
            }

            if (_aimAction == null) return;

            Vector2 mouseScreenPos = _aimAction.ReadValue<Vector2>();
            Ray ray = _mainCamera.ScreenPointToRay(mouseScreenPos);

            // Debug Log for troubleshooting (can be removed later)
            // Debug.Log($"Mouse: {mouseScreenPos}, Ray Origin: {ray.origin}, Dir: {ray.direction}");

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, FloorLayer))
            {
                _aimPoint = hit.point;
                _hasAimPoint = true;

                Vector3 targetPoint = hit.point;
                targetPoint.y = transform.position.y; // Keep looking horizontally

                Vector3 direction = (targetPoint - transform.position).normalized;
                if (direction != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }
            else
            {
                // Debug.Log("Raycast did not hit 'Floor' layer! Check Layer setup on Plane and Camera distance.");
            }
        }
    }
}