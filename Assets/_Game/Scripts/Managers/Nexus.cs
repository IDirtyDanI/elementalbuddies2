using UnityEngine;
using UnityEngine.UI;
using System;

namespace ElementalBuddies
{
    // The base the player defends. Enemies walk here by default; destroying it ends the game.
    // Mehrspieler: Schaden/Heilung nur auf dem Server; HP gehen über NetGame.Waves an die Clients (ApplyRemoteHealth).
    public class Nexus : MonoBehaviour, IDamageable
    {
        public static Nexus Instance { get; private set; }

        [Header("Stats")]
        public float MaxHP = 500f;
        [HideInInspector] public bool Invulnerable; // Dev-Modus
        private float _graceUntil = -1f; // Schutzzeit nach Revive (Weiterspielen per Video)
        public float CurrentHP { get; private set; }

        [Header("UI (optional)")]
        public Slider HPSlider;

        public event Action OnHealthChanged;
        public event Action OnDestroyed;

        private Collider _collider;
        private bool _isDestroyed;

        void Awake()
        {
            Instance = this;
            CurrentHP = MaxHP;
            _collider = GetComponentInChildren<Collider>();
        }

        void Start()
        {
            UpdateSlider();
        }

        // Server: auch Änderungen von außen (z. B. MaxHP-Aufwertungen) an die Clients (schreibt nur bei Änderung)
        void Update()
        {
            if (Net.IsServer) SyncNet();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void TakeDamage(float amount)
        {
            if (!Net.IsServer || _isDestroyed || Invulnerable || Time.time < _graceUntil) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return;

            CurrentHP -= amount;
            if (CurrentHP < 0) CurrentHP = 0;
            SyncNet();

            OnHealthChanged?.Invoke();
            UpdateSlider();

            if (CurrentHP <= 0)
            {
                _isDestroyed = true;
                Debug.Log("Nexus destroyed!");
                OnDestroyed?.Invoke();
                // Mobil: ggf. erst „Weiterspielen per Video?“ (GameManager.NexusFell), sonst sofort Game Over
                if (GameManager.Instance != null) GameManager.Instance.NexusFell("Der Nexus wurde zerstört");
            }
        }

        // Server: Nexus nach „Weiterspielen“ (Werbevideo, GameManager.AcceptContinue) mit einem Anteil der Max-LP
        // wiederherstellen, kurze Schutzzeit gegen sofortigen erneuten Fall
        public void Revive(float hpFraction, float graceSeconds)
        {
            if (!Net.IsServer || !_isDestroyed) return;
            _isDestroyed = false;
            CurrentHP = Mathf.Max(1f, MaxHP * Mathf.Clamp01(hpFraction));
            _graceUntil = Time.time + graceSeconds;
            SyncNet();
            OnHealthChanged?.Invoke();
            UpdateSlider();
        }
        // Server: Lebensopfer für eine Wiederbelebung (GameManager, Team-Ausfall) – zerstört den Nexus nie
        public void Sacrifice(float amount)
        {
            if (!Net.IsServer || _isDestroyed || amount <= 0f) return;
            CurrentHP = Mathf.Max(1f, CurrentHP - amount);
            SyncNet();
            OnHealthChanged?.Invoke();
            UpdateSlider();
        }

        // Reparatur (Segen des Licht-Buddys); gibt die tatsächlich geheilte Menge zurück
        public float Heal(float amount)
        {
            if (!Net.IsServer || _isDestroyed || amount <= 0f || CurrentHP >= MaxHP) return 0f;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return 0f;
            float before = CurrentHP;
            CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
            SyncNet();
            OnHealthChanged?.Invoke();
            UpdateSlider();
            return CurrentHP - before;
        }

        private void SyncNet()
        {
            if (Net.IsServer && NetGame.Ready) NetGame.Instance.ServerSetNexusHP(CurrentHP, MaxHP);
        }

        // Client: HP vom Server (NetGame.Waves)
        public void ApplyRemoteHealth(float hp, float maxHp)
        {
            if (Net.IsServer) return;
            if (maxHp > 0f) MaxHP = maxHp;
            bool changed = !Mathf.Approximately(CurrentHP, hp);
            CurrentHP = Mathf.Clamp(hp, 0f, MaxHP);
            UpdateSlider();
            if (changed) OnHealthChanged?.Invoke();
            if (CurrentHP <= 0f && !_isDestroyed)
            {
                _isDestroyed = true;
                OnDestroyed?.Invoke();
            }
            else if (CurrentHP > 0f) _isDestroyed = false; // Revive auf dem Server
        }

        // Closest point on the Nexus surface (collider) to a given position. Falls back to the pivot.
        public Vector3 GetClosestPoint(Vector3 from)
        {
            if (_collider == null || !_collider.enabled) return transform.position;

            // ClosestPoint only supports Box/Sphere/Capsule/convex MeshColliders
            var mesh = _collider as MeshCollider;
            if (mesh != null && !mesh.convex) return _collider.ClosestPointOnBounds(from);

            return _collider.ClosestPoint(from);
        }

        public float GetDistanceFrom(Vector3 from)
        {
            Vector3 p = GetClosestPoint(from);
            p.y = from.y; // Ignore height difference
            return Vector3.Distance(from, p);
        }

        private void UpdateSlider()
        {
            if (HPSlider == null) return;
            HPSlider.maxValue = MaxHP;
            HPSlider.value = CurrentHP;
        }
    }
}
