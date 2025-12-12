using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerStats))]
    public class PlayerAbilities : MonoBehaviour
    {
        [Header("Arcane Ball (Q)")]
        public GameObject ArcaneBallPrefab;
        public float ArcaneBallManaCost = 15f;
        public float ArcaneBallCooldown = 1f;
        public Transform SpawnPoint; 

        [Header("Blink (E)")]
        public float BlinkManaCost = 30f;
        public float BlinkCooldown = 8f;
        public float BlinkRange = 8f;
        public float InvulnerabilityDuration = 0.4f;
        public LayerMask ObstacleLayer; // Assign "Default" or specific wall layer

        private float _lastArcaneBallTime;
        private float _lastBlinkTime;

        private PlayerController _controller;
        private CharacterController _characterController;
        private PlayerStats _stats;

        void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _characterController = GetComponent<CharacterController>();
            _stats = GetComponent<PlayerStats>();
        }

        void Update()
        {
            HandleSkills();
        }

        private void HandleSkills()
        {
            // Skill 6 - Arcane Ball
            if (_controller.Skill6Action != null && _controller.Skill6Action.WasPressedThisFrame())
            {
                TryCastArcaneBall();
            }

            // E - Blink
            if (_controller.SkillEAction != null && _controller.SkillEAction.WasPressedThisFrame())
            {
                TryCastBlink();
            }
        }

        private void TryCastArcaneBall()
        {
            if (Time.time < _lastArcaneBallTime + ArcaneBallCooldown) return;
            if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpendMana(ArcaneBallManaCost)) return;

            _lastArcaneBallTime = Time.time;

            Vector3 spawnPos = SpawnPoint != null ? SpawnPoint.position : transform.position + transform.forward + Vector3.up; 
            Instantiate(ArcaneBallPrefab, spawnPos, transform.rotation);
        }

        private void TryCastBlink()
        {
            if (Time.time < _lastBlinkTime + BlinkCooldown) return;
            if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpendMana(BlinkManaCost)) return;

            _lastBlinkTime = Time.time;
            StartCoroutine(PerformBlink());
        }

        private IEnumerator PerformBlink()
        {
            _stats.IsInvulnerable = true;
            
            Vector3 blinkDir = transform.forward; // Default to facing direction
            
            // Try get input direction
            if (_controller.MoveAction != null)
            {
                Vector2 input = _controller.MoveAction.ReadValue<Vector2>();
                if (input.sqrMagnitude > 0.1f)
                {
                    blinkDir = new Vector3(input.x, 0, input.y).normalized;
                }
            }
            
            // Wall Check
            Vector3 targetPos = transform.position + blinkDir * BlinkRange;
            if (Physics.Raycast(transform.position + Vector3.up, blinkDir, out RaycastHit hit, BlinkRange, ObstacleLayer))
            {
                targetPos = hit.point - blinkDir * 0.5f; // Stop slightly before wall
                targetPos.y = transform.position.y;
            }

            _characterController.enabled = false;
            transform.position = targetPos;
            _characterController.enabled = true;

            yield return new WaitForSeconds(InvulnerabilityDuration);
            _stats.IsInvulnerable = false;
        }
    }
}