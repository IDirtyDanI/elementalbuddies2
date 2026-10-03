using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public abstract class ElementalBuddy : MonoBehaviour, IDamageable
    {
        public UnitConfigSO Config; // Public for setup if needed
        [HideInInspector] public float PaidCost; // Tatsächlich bezahlte Seelensplitter (für Refund beim Verkauf)

        // Registry aller aktiven Buddies (für Slot-Limit)
        private static readonly List<ElementalBuddy> _active = new List<ElementalBuddy>();
        public static IReadOnlyList<ElementalBuddy> Active => _active;
        public static int ActiveCount => _active.Count;
        public static event System.Action OnBuddyCountChanged;

        protected float lastActionTime;
        public float CurrentHP { get; protected set; }

        private static readonly int CastTrigger = Animator.StringToHash("Cast");
        private Animator _visualAnimator;

        // Reset bei deaktiviertem Domain Reload
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            _active.Clear();
            OnBuddyCountChanged = null;
        }

        // Ghosts werden direkt nach Instantiate deaktiviert -> netto nicht gezählt
        protected virtual void OnEnable()
        {
            if (!_active.Contains(this))
            {
                _active.Add(this);
                OnBuddyCountChanged?.Invoke();
            }
        }

        protected virtual void OnDisable()
        {
            if (_active.Remove(this)) OnBuddyCountChanged?.Invoke();
        }

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