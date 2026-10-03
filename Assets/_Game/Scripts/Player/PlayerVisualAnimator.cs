using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Treibt den Animator des aktiven Champion-Visuals (ChampionVisual): Speed/Grounded/Jump für die Fortbewegung,
    // Fähigkeiten-Trigger vom aktiven ChampionKit (z. B. "Cast", "Slash1", "Shoot", "Roll") und laufende Zustände
    // (z. B. Bool "Block"). Fehlende Parameter im Controller werden still übersprungen.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerVisualAnimator : MonoBehaviour
    {
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int GroundedParam = Animator.StringToHash("Grounded");
        private static readonly int JumpParam = Animator.StringToHash("Jump");

        [SerializeField] private float speedDampTime = 0.1f;
        [SerializeField] private float groundedGrace = 0.15f; // Tolerate brief isGrounded flicker
        [SerializeField] private float airLockDuration = 0.2f; // Force "airborne" right after a jump

        private CharacterController _controller;
        private PlayerController _playerController;
        private PlayerAbilities _abilities;
        private Animator _animator;
        private ChampionVisual _visual;

        private float _ungroundedTime;
        private float _airLockUntil;

        // Parameter-Cache pro Animator (Controller können sich unterscheiden)
        private static readonly Dictionary<int, HashSet<int>> _paramCache = new Dictionary<int, HashSet<int>>();

        public Animator CurrentAnimator => _animator;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _playerController = GetComponent<PlayerController>();
            _abilities = GetComponent<PlayerAbilities>();
            ResolveAnimator();
        }

        void OnEnable()
        {
            if (_playerController != null) _playerController.Jumped += OnJumped;
            if (_abilities != null)
            {
                _abilities.OnAbilityCast += OnAbilityCast;
                _abilities.OnChampionChanged += OnChampionChanged;
            }
        }

        void OnDisable()
        {
            if (_playerController != null) _playerController.Jumped -= OnJumped;
            if (_abilities != null)
            {
                _abilities.OnAbilityCast -= OnAbilityCast;
                _abilities.OnChampionChanged -= OnChampionChanged;
            }
        }

        private void OnChampionChanged(ChampionClass cls)
        {
            ResolveAnimator();
        }

        private void ResolveAnimator()
        {
            _visual = _abilities != null ? _abilities.ActiveVisual : null;
            _animator = _visual != null && _visual.Animator != null ? _visual.Animator : null;
            if (_animator == null)
            {
                foreach (var a in GetComponentsInChildren<Animator>())
                {
                    _animator = a;
                    break;
                }
            }

            // StarterAssets clips carry OnFootstep/OnLand events without a receiver here
            if (_animator != null) _animator.fireEvents = false;
        }

        void Update()
        {
            if (_animator == null || !_animator.isActiveAndEnabled || (_abilities != null && _abilities.ActiveVisual != _visual))
            {
                ResolveAnimator();
                if (_animator == null) return;
            }

            // Horizontal speed only
            Vector3 v = _controller.velocity;
            v.y = 0f;
            _animator.SetFloat(SpeedParam, v.magnitude, speedDampTime, Time.deltaTime);

            // Grounded debounce
            if (_controller.isGrounded) _ungroundedTime = 0f;
            else _ungroundedTime += Time.deltaTime;

            bool grounded = Time.time >= _airLockUntil && _ungroundedTime < groundedGrace;
            _animator.SetBool(GroundedParam, grounded);

            if (_abilities != null && _abilities.ActiveKit != null) _abilities.ActiveKit.UpdateAnimator(_animator, _visual);
        }

        private void OnJumped()
        {
            _airLockUntil = Time.time + airLockDuration;
            if (_animator != null) _animator.SetTrigger(JumpParam);
        }

        // Trigger-Name liefert das aktive Kit (Magier: "Cast", Blink ohne Animation)
        private void OnAbilityCast(AbilityId id)
        {
            if (_animator == null || _abilities == null || _abilities.ActiveKit == null) return;
            string trigger = _abilities.ActiveKit.GetAnimTrigger(id);
            if (!string.IsNullOrEmpty(trigger)) SetTriggerSafe(_animator, trigger);
        }

        // ---------------- Helfer (auch für Kits) ----------------

        public static bool HasParam(Animator animator, int hash)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            int key = animator.runtimeAnimatorController.GetInstanceID();
            if (!_paramCache.TryGetValue(key, out var set))
            {
                set = new HashSet<int>();
                foreach (var p in animator.parameters) set.Add(p.nameHash);
                _paramCache[key] = set;
            }
            return set.Contains(hash);
        }

        public static void SetTriggerSafe(Animator animator, string name)
        {
            int h = Animator.StringToHash(name);
            if (HasParam(animator, h)) animator.SetTrigger(h);
        }

        public static void SetBoolSafe(Animator animator, string name, bool value)
        {
            int h = Animator.StringToHash(name);
            if (HasParam(animator, h)) animator.SetBool(h, value);
        }
    }
}
