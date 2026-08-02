using UnityEngine;

namespace ElementalBuddies
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerVisualAnimator : MonoBehaviour
    {
        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        private CharacterController _controller;
        private Animator _animator;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _animator = GetComponentInChildren<Animator>();
        }

        void Update()
        {
            if (_animator == null) return;
            Vector3 v = _controller.velocity;
            v.y = 0f;
            _animator.SetFloat(SpeedParam, v.magnitude);
        }
    }
}
