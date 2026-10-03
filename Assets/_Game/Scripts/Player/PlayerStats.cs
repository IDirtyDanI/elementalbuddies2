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
        public bool GodMode { get; set; } = false; // Dev-Modus (unabhängig von Blink-I-Frames)

        public event Action OnHealthChanged;
        public event Action OnPlayerDeath;

        private bool _isDead;

        void Awake()
        {
            CurrentHP = MaxHP;
        }

        public void TakeDamage(float amount)
        {
            if (IsInvulnerable || GodMode || _isDead) return;

            CurrentHP -= amount;
            if (CurrentHP < 0) CurrentHP = 0;
            if (amount > 0f) GameAudio.Play(SfxId.PlayerHurt);
            
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
            if (_isDead) return;
            _isDead = true;

            Debug.Log("Player Died!");
            OnPlayerDeath?.Invoke();
            GameManager.Instance?.TriggerGameOver("Der Zauberer ist gefallen");
        }
    }
}