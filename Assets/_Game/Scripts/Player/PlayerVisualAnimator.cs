using UnityEngine;

namespace ElementalBuddies
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerVisualAnimator : MonoBehaviour
    {
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int GroundedParam = Animator.StringToHash("Grounded");
        private static readonly int JumpParam = Animator.StringToHash("Jump");
        private static readonly int CastParam = Animator.StringToHash("Cast");

        [SerializeField] private float speedDampTime = 0.1f;
        [SerializeField] private float groundedGrace = 0.15f; // Tolerate brief isGrounded flicker
        [SerializeField] private float airLockDuration = 0.2f; // Force "airborne" right after a jump
        [SerializeField] private bool castOnBlink = false; // Blink is a movement skill, no cast animation by default

        private CharacterController _controller;
        private PlayerController _playerController;
        private PlayerAbilities _abilities;
        private Animator _animator;

        private float _ungroundedTime;
        private float _airLockUntil;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _playerController = GetComponent<PlayerController>();
            _abilities = GetComponent<PlayerAbilities>();
            _animator = GetComponentInChildren<Animator>();

            // StarterAssets clips carry OnFootstep/OnLand events without a receiver here
            if (_animator != null) _animator.fireEvents = false;
        }

        void OnEnable()
        {
            if (_playerController != null) _playerController.Jumped += OnJumped;
            if (_abilities != null) _abilities.OnAbilityCast += OnAbilityCast;
        }

        void OnDisable()
        {
            if (_playerController != null) _playerController.Jumped -= OnJumped;
            if (_abilities != null) _abilities.OnAbilityCast -= OnAbilityCast;
        }

        void Update()
        {
            if (_animator == null) return;

            // Horizontal speed only
            Vector3 v = _controller.velocity;
            v.y = 0f;
            _animator.SetFloat(SpeedParam, v.magnitude, speedDampTime, Time.deltaTime);

            // Grounded debounce
            if (_controller.isGrounded) _ungroundedTime = 0f;
            else _ungroundedTime += Time.deltaTime;

            bool grounded = Time.time >= _airLockUntil && _ungroundedTime < groundedGrace;
            _animator.SetBool(GroundedParam, grounded);
        }

        private void OnJumped()
        {
            _airLockUntil = Time.time + airLockDuration;
            if (_animator != null) _animator.SetTrigger(JumpParam);
        }

        // Every spell cast plays the "Cast" trigger (Blink only if castOnBlink)
        private void OnAbilityCast(AbilityId id)
        {
            if (id == AbilityId.Blink && !castOnBlink) return;
            if (_animator != null) _animator.SetTrigger(CastParam);
        }
    }
}
