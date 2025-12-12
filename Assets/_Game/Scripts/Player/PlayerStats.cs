using UnityEngine;
using System;

namespace ElementalBuddies
{
    public class PlayerStats : MonoBehaviour, IDamageable
    {
        [Header("Stats")]
        public float MaxHP = 100f;
        public float CurrentHP { get; private set; }
        
        public bool IsInvulnerable { get; set; } = false;

        public event Action OnHealthChanged;
        public event Action OnPlayerDeath;

        void Awake()
        {
            CurrentHP = MaxHP;
        }

        public void TakeDamage(float amount)
        {
            if (IsInvulnerable) return;

            CurrentHP -= amount;
            if (CurrentHP < 0) CurrentHP = 0;
            
            OnHealthChanged?.Invoke();

            if (CurrentHP <= 0)
            {
                Die();
            }
        }

        public void Heal(float amount)
        {
            CurrentHP += amount;
            if (CurrentHP > MaxHP) CurrentHP = MaxHP;
            
            OnHealthChanged?.Invoke();
        }

        private void Die()
        {
            Debug.Log("Player Died!");
            OnPlayerDeath?.Invoke();
            // Implement death logic (game over screen, etc.)
        }
    }
}