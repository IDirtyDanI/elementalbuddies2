using UnityEngine;
using UnityEngine.AI;
using System.Collections;

namespace ElementalBuddies
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyBrain : MonoBehaviour, IDamageable, ISlowable, ITauntable
    {
        public EnemyConfigSO Config;

        [Header("Targeting")]
        [Tooltip("Enemies switch from the Nexus to the player while the player is within this radius.")]
        public float PlayerAggroRadius = 6f;
        [Tooltip("Attack range against units (player / taunt target), measured center to center.")]
        public float AttackRange = 1.5f;
        [Tooltip("Attack range against the Nexus, measured to the closest point of its collider.")]
        public float NexusAttackRange = 2.5f;

        private NavMeshAgent _agent;
        private Transform _player;
        private Transform _tauntTarget;
        private float _currentHP;
        private float _baseSpeed;

        // Anti-Cheese
        private float _stuckTimer;
        
        public static event System.Action OnEnemyDeath;
        private bool _isDead;

        void Start()
        {
            _agent = GetComponent<NavMeshAgent>();
            _player = GameObject.FindGameObjectWithTag("Player")?.transform;
            
            if (Config != null)
            {
                if (_currentHP <= 0) _currentHP = Config.BaseHP;
                _agent.speed = Config.Speed;
                _baseSpeed = Config.Speed;
            }
            else
            {
                _currentHP = 60f;
                _agent.speed = 3.5f;
                _baseSpeed = 3.5f;
            }
        }

        public void Initialize(float hpBonus)
        {
             if (Config != null) _currentHP = Config.BaseHP + hpBonus; 
             else _currentHP = 60f + hpBonus;
        }

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver)
            {
                if (_agent.isOnNavMesh && !_agent.isStopped) _agent.isStopped = true;
                return;
            }

            HandleMovement();
            HandleAntiCheese();
            HandleAttack();
        }

        // Priority: Taunt > Player (within aggro radius) > Nexus > Player (fallback if no Nexus)
        private Transform GetCurrentTarget()
        {
            if (_tauntTarget != null) return _tauntTarget;

            if (_player != null)
            {
                float playerDist = Vector3.Distance(transform.position, _player.position);
                if (playerDist <= PlayerAggroRadius) return _player;
            }

            if (Nexus.Instance != null) return Nexus.Instance.transform;

            return _player;
        }

        private bool IsNexus(Transform target)
        {
            return Nexus.Instance != null && target == Nexus.Instance.transform;
        }

        private bool IsInAttackRange(Transform target)
        {
            if (target == null) return false;

            if (IsNexus(target))
                return Nexus.Instance.GetDistanceFrom(transform.position) <= NexusAttackRange;

            return Vector3.Distance(transform.position, target.position) < AttackRange;
        }

        private void HandleMovement()
        {
            Transform target = GetCurrentTarget();
            if (target == null) return;

            if (IsNexus(target))
            {
                // Walk to the Nexus surface instead of its pivot (the Nexus is big / may carve the NavMesh)
                _agent.SetDestination(Nexus.Instance.GetClosestPoint(transform.position));
            }
            else
            {
                _agent.SetDestination(target.position);
            }
        }

        private void HandleAntiCheese()
        {
            // Standing still while attacking our target is not "stuck"
            if (IsInAttackRange(GetCurrentTarget()))
            {
                _stuckTimer = 0;
                return;
            }

            if (_agent.velocity.magnitude < 0.1f && !_agent.pathPending && (_agent.hasPath || _player != null || Nexus.Instance != null))
            {
                _stuckTimer += Time.deltaTime;
            }
            else
            {
                _stuckTimer = 0;
            }

            if (_stuckTimer > 0.6f)
            {
                Collider[] hits = Physics.OverlapSphere(transform.position, 2.5f, LayerMask.GetMask("Buddy"));
                foreach (var hit in hits)
                {
                     var buddy = hit.GetComponent<IDamageable>();
                     if (buddy != null)
                     {
                         float dmg = (Config != null ? Config.AttackDamage : 10f) * Time.deltaTime;
                         buddy.TakeDamage(dmg);
                         return; 
                     }
                }
            }
        }
        
        private void HandleAttack()
        {
             Transform target = GetCurrentTarget();
             if (target != null && IsInAttackRange(target))
             {
                 var dmg = target.GetComponent<IDamageable>();
                 if (dmg != null)
                 {
                     dmg.TakeDamage((Config != null ? Config.AttackDamage : 10f) * Time.deltaTime);
                 }
             }
        }

        public void TakeDamage(float amount)
        {
            if (_isDead) return;
            _currentHP -= amount;
            if (_currentHP <= 0)
            {
                // Guard: several hits in one frame must not report the death twice (bounty / wave count)
                _isDead = true;
                OnEnemyDeath?.Invoke();
                Destroy(gameObject);
            }
        }

        public void ApplySlow(float percentage, float duration)
        {
            StartCoroutine(SlowRoutine(percentage, duration));
        }

        private IEnumerator SlowRoutine(float percentage, float duration)
        {
            _agent.speed = _baseSpeed * (1f - percentage);
            yield return new WaitForSeconds(duration);
            _agent.speed = _baseSpeed;
        }

        public void Taunt(Transform target, float duration)
        {
            StartCoroutine(TauntRoutine(target, duration));
        }

        private IEnumerator TauntRoutine(Transform target, float duration)
        {
            _tauntTarget = target;
            yield return new WaitForSeconds(duration);
            _tauntTarget = null;
        }
    }
}