using UnityEngine;

namespace ElementalBuddies
{
    public abstract class ElementalBuddy : MonoBehaviour, IDamageable
    {
        public UnitConfigSO Config; // Public for setup if needed

        protected float lastActionTime;
        public float CurrentHP { get; protected set; }

        private static readonly int CastTrigger = Animator.StringToHash("Cast");
        private Animator _visualAnimator;

        protected virtual void Start()
        {
            CurrentHP = 50f; // Default HP
            _visualAnimator = GetComponentInChildren<Animator>();
        }

        protected virtual void Update()
        {
            if (Config == null) return;

            if (Config.FireRate > 0 && Time.time >= lastActionTime + (1f / Config.FireRate))
            {
                if (TryPerformAction())
                {
                    lastActionTime = Time.time;
                    if (_visualAnimator != null) _visualAnimator.SetTrigger(CastTrigger);
                }
            }
        }

        // Returns true if action was performed (and CD should reset)
        protected abstract bool TryPerformAction();

        public virtual void TakeDamage(float amount)
        {
            CurrentHP -= amount;
            if (CurrentHP <= 0)
            {
                Die();
            }
        }

        protected virtual void Die()
        {
            Destroy(gameObject);
        }
    }
}