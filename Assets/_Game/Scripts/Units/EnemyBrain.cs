using UnityEngine;
using UnityEngine.AI;
using System.Collections;

namespace ElementalBuddies
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyBrain : MonoBehaviour, IDamageable, ISlowable, ITauntable
    {
        public EnemyConfigSO Config;

        private NavMeshAgent _agent;
        private Transform _player;
        private Transform _tauntTarget;
        private float _currentHP;
        private float _baseSpeed;

        // Anti-Cheese
        private float _stuckTimer;
        
        public static event System.Action OnEnemyDeath;

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
            HandleMovement();
            HandleAntiCheese();
            HandleAttack();
        }

        private void HandleMovement()
        {
            if (_tauntTarget != null)
            {
                _agent.SetDestination(_tauntTarget.position);
            }
            else if (_player != null)
            {
                _agent.SetDestination(_player.position);
            }
        }

        private void HandleAntiCheese()
        {
            if (_agent.velocity.magnitude < 0.1f && !_agent.pathPending && (_agent.hasPath || _player != null))
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
             Transform target = _tauntTarget != null ? _tauntTarget : _player;
             if (target != null)
             {
                 float dist = Vector3.Distance(transform.position, target.position);
                 if (dist < 1.5f)
                 {
                     var dmg = target.GetComponent<IDamageable>();
                     if (dmg != null)
                     {
                         dmg.TakeDamage((Config != null ? Config.AttackDamage : 10f) * Time.deltaTime);
                     }
                 }
             }
        }

        public void TakeDamage(float amount)
        {
            _currentHP -= amount;
            if (_currentHP <= 0)
            {
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