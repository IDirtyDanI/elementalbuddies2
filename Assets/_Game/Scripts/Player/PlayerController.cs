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

        private Camera _mainCamera;
        private Vector3 _playerVelocity; // To store velocity for jumping/gravity

        [Header("Jump Settings")]
        public float JumpForce = 8.0f;
        public float Gravity = -9.81f;

        void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _mainCamera = Camera.main;

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
                }
            }
        }

        void OnEnable()
        {
            if (inputAsset != null) inputAsset.Enable();
        }

        void OnDisable()
        {
            if (inputAsset != null) inputAsset.Disable();
        }

        void Update()
        {
            HandleMovement();
            HandleRotation();
        }

        private void HandleMovement()
        {
            if (MoveAction == null) return;

            Vector2 input = MoveAction.ReadValue<Vector2>();
            Vector3 moveInput = new Vector3(input.x, 0, input.y);

            // Ground check for jumping
            if (_characterController.isGrounded)
            {
                _playerVelocity.y = 0f; // Reset vertical velocity when grounded

                if (JumpAction != null && JumpAction.WasPressedThisFrame())
                {
                    _playerVelocity.y = JumpForce;
                }
            }

            // Apply gravity
            _playerVelocity.y += Gravity * Time.deltaTime;

            // Apply movement input (Absolute / World Space)
            Vector3 movement = moveInput * MoveSpeed; 
            _characterController.Move((movement + _playerVelocity) * Time.deltaTime);
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