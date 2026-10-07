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

        // Fremde Figuren (Mehrspieler): Tempo/Bodenkontakt aus der Positionsänderung (NetworkTransform) statt CharacterController
        private Vector3 _lastPos;
        private Vector3 _remoteVelocity;
        private bool _hasLastPos;

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
            // Modell versteckt (ausgefallene Figur) → nichts zu animieren
            if (_abilities != null && _abilities.ActiveVisual != null && !_abilities.ActiveVisual.gameObject.activeInHierarchy) return;
            if (_animator == null || !_animator.isActiveAndEnabled || (_abilities != null && _abilities.ActiveVisual != _visual))
            {
                ResolveAnimator();
                if (_animator == null) return;
            }

            bool local = _playerController == null || _playerController.IsLocalControl;
            Vector3 v;
            bool isGrounded;
            if (local && _controller.enabled)
            {
                v = _controller.velocity;
                isGrounded = _controller.isGrounded;
                _hasLastPos = false;
            }
            else
            {
                v = RemoteVelocity();
                isGrounded = RemoteGrounded(v);
            }

            // Horizontal speed only
            v.y = 0f;
            _animator.SetFloat(SpeedParam, v.magnitude, speedDampTime, Time.deltaTime);

            // Grounded debounce
            if (isGrounded) _ungroundedTime = 0f;
            else _ungroundedTime += Time.deltaTime;

            bool grounded = Time.time >= _airLockUntil && _ungroundedTime < groundedGrace;
            _animator.SetBool(GroundedParam, grounded);

            if (_abilities != null && _abilities.ActiveKit != null) _abilities.ActiveKit.UpdateAnimator(_animator, _visual);
        }

        // Geschwindigkeit aus der Positionsänderung (geglättet; Teleports/Blinks werden ignoriert)
        private Vector3 RemoteVelocity()
        {
            Vector3 pos = transform.position;
            float dt = Time.deltaTime;
            if (!_hasLastPos || dt <= 0f)
            {
                _lastPos = pos;
                _hasLastPos = true;
                return _remoteVelocity;
            }
            Vector3 raw = (pos - _lastPos) / dt;
            _lastPos = pos;
            if (raw.sqrMagnitude > 30f * 30f) raw = Vector3.zero; // Teleport
            _remoteVelocity = Vector3.Lerp(_remoteVelocity, raw, 1f - Mathf.Exp(-15f * dt));
            return _remoteVelocity;
        }

        // Bodenkontakt: kurzer Strahl nach unten (Boden-Layer des PlayerControllers), sonst über die Fallgeschwindigkeit
        private bool RemoteGrounded(Vector3 velocity)
        {
            int mask = _playerController != null ? _playerController.FloorLayer.value : 0;
            if (mask != 0)
            {
                // Vom Fußpunkt aus prüfen: der Drehpunkt der Figur liegt in der Kapselmitte (~1,1 m über dem Boden)
                float foot = 0f;
                if (_controller != null)
                    foot = (_controller.center.y - _controller.height * 0.5f) * transform.lossyScale.y;
                Vector3 from = transform.position + Vector3.up * (foot + 0.3f);
                return Physics.Raycast(from, Vector3.down, 0.6f, mask, QueryTriggerInteraction.Ignore);
            }
            return Mathf.Abs(velocity.y) < 1f;
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
