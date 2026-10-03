using UnityEngine;
using System;

namespace ElementalBuddies
{
    // Treffer mit bekannter Herkunft (für Block / Richtungs-Effekte). Gegner rufen bevorzugt diese Variante auf.
    public interface IDirectionalDamageable
    {
        void TakeDamage(float amount, Vector3 sourcePosition);
    }

    public class PlayerStats : MonoBehaviour, IDamageable, IDirectionalDamageable
    {
        [Header("Stats")]
        public float MaxHP = 100f;
        public float CurrentHP { get; private set; }

        public bool IsInvulnerable { get; set; } = false;
        public bool GodMode { get; set; } = false; // Dev-Modus (unabhängig von Blink-/Rollen-I-Frames)

        // Vom aktiven ChampionKit gesetzt (über PlayerAbilities): (Schaden, Quelle, Quelle bekannt) → verbleibender Schaden
        public Func<float, Vector3, bool, float> DamageModifier;

        public event Action OnHealthChanged;
        public event Action OnPlayerDeath;

        private bool _isDead;

        void Awake()
        {
            CurrentHP = MaxHP;
        }

        // Schaden ohne Richtung (wird nicht geblockt, nur allgemeine Reduktionen greifen)
        public void TakeDamage(float amount)
        {
            ApplyDamage(amount, transform.position, false);
        }

        // Schaden mit Herkunft (z. B. Nahkampf-Gegner) – frontal geblockt, wenn der Schwertkämpfer den Schild hebt
        public void TakeDamage(float amount, Vector3 sourcePosition)
        {
            ApplyDamage(amount, sourcePosition, true);
        }

        private void ApplyDamage(float amount, Vector3 sourcePosition, bool hasSource)
        {
            if (IsInvulnerable || GodMode || _isDead) return;
            if (amount > 0f && DamageModifier != null) amount = DamageModifier(amount, sourcePosition, hasSource);
            if (amount <= 0f) return;

            CurrentHP -= amount;
            if (CurrentHP < 0) CurrentHP = 0;
            GameAudio.Play(SfxId.PlayerHurt);

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
            GameManager.Instance?.TriggerGameOver(DeathMessage());
        }

        private string DeathMessage()
        {
            var pa = GetComponent<PlayerAbilities>();
            switch (pa != null ? pa.ActiveClass : ChampionClass.Mage)
            {
                case ChampionClass.Knight: return "Der Schwertkämpfer ist gefallen";
                case ChampionClass.Archer: return "Der Bogenschütze ist gefallen";
                default: return "Der Zauberer ist gefallen";
            }
        }
    }
}
