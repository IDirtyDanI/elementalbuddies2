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
        // Wiederbelebt (Mehrspieler: nach PlayerAvatar.RespawnDelay)
        public event Action OnPlayerRevived;

        private bool _isDead;
        public bool IsDead => _isDead;

        void Awake()
        {
            CurrentHP = MaxHP;
        }

        // Schaden ohne Richtung (wird nicht geblockt, nur allgemeine Reduktionen greifen). Nur auf dem Server.
        public void TakeDamage(float amount)
        {
            ApplyDamage(amount, transform.position, false);
        }

        // Schaden mit Herkunft (z. B. Nahkampf-Gegner) – frontal geblockt, wenn der Schwertkämpfer den Schild hebt. Nur auf dem Server.
        public void TakeDamage(float amount, Vector3 sourcePosition)
        {
            ApplyDamage(amount, sourcePosition, true);
        }

        private void ApplyDamage(float amount, Vector3 sourcePosition, bool hasSource)
        {
            // Treffer entscheidet nur der Server; Clients bekommen die LP über PlayerAvatar
            if (!Net.IsServer) return;
            if (IsInvulnerable || GodMode || _isDead) return;
            if (amount > 0f && DamageModifier != null) amount = DamageModifier(amount, sourcePosition, hasSource);
            if (amount <= 0f) return;

            CurrentHP -= amount;
            if (CurrentHP < 0) CurrentHP = 0;
            PlayHurtSound();

            OnHealthChanged?.Invoke();

            if (CurrentHP <= 0)
            {
                Die();
            }
        }

        // Heilung nur auf dem Server (Clients: No-Op, der Wert kommt über das Netz)
        public void Heal(float amount)
        {
            if (!Net.IsServer || _isDead) return;
            CurrentHP += amount;
            if (CurrentHP > MaxHP) CurrentHP = MaxHP;

            OnHealthChanged?.Invoke();
        }

        private void Die()
        {
            if (_isDead) return;
            _isDead = true;

            Debug.Log($"[Player] {DeathMessage()}");
            OnPlayerDeath?.Invoke();
            // Kein Game Over mehr hier: Die Figur fällt aus und wird wiederbelebt (PlayerAvatar);
            // Game Over entscheidet der Server, wenn niemand mehr lebt (PlayerAvatar.AnyAlive).
        }

        // Server: mit vollen LP zurückholen
        public void Revive()
        {
            if (!Net.IsServer) return;
            CurrentHP = MaxHP;
            bool wasDead = _isDead;
            _isDead = false;
            OnHealthChanged?.Invoke();
            if (wasDead) OnPlayerRevived?.Invoke();
        }

        // ---------------- Netz (PlayerAvatar) ----------------

        // Clients: LP/Max/Tod vom Server übernehmen und dieselben Events feuern wie auf dem Server
        internal void ApplyNetworkHealth(float hp, float maxHp, bool dead)
        {
            if (Net.IsServer) return;
            bool changed = !Mathf.Approximately(hp, CurrentHP) || !Mathf.Approximately(maxHp, MaxHP);
            bool hurt = hp < CurrentHP - 0.01f;
            MaxHP = maxHp;
            CurrentHP = hp;
            if (hurt && !dead) PlayHurtSound();
            if (changed) OnHealthChanged?.Invoke();
            if (dead && !_isDead)
            {
                _isDead = true;
                OnPlayerDeath?.Invoke();
            }
            else if (!dead && _isDead)
            {
                _isDead = false;
                OnPlayerRevived?.Invoke();
            }
        }

        // Treffer-Laut nur für die eigene Figur (sonst hört man jeden Mitspieler)
        private void PlayHurtSound()
        {
            var av = GetComponent<PlayerAvatar>();
            if (av == null || av.IsLocalControl) GameAudio.Play(SfxId.PlayerHurt);
        }

        // Text für Game Over / Meldungen ("Der Zauberer ist gefallen")
        public string DeathMessage()
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
