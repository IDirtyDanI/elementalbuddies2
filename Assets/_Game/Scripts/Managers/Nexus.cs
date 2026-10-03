using UnityEngine;
using UnityEngine.UI;
using System;

namespace ElementalBuddies
{
    // The base the player defends. Enemies walk here by default; destroying it ends the game.
    public class Nexus : MonoBehaviour, IDamageable
    {
        public static Nexus Instance { get; private set; }

        [Header("Stats")]
        public float MaxHP = 500f;
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

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void TakeDamage(float amount)
        {
            if (_isDestroyed) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return;

            CurrentHP -= amount;
            if (CurrentHP < 0) CurrentHP = 0;

            OnHealthChanged?.Invoke();
            UpdateSlider();

            if (CurrentHP <= 0)
            {
                _isDestroyed = true;
                Debug.Log("Nexus destroyed!");
                OnDestroyed?.Invoke();
                if (GameManager.Instance != null) GameManager.Instance.TriggerGameOver("Der Nexus wurde zerstört");
            }
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
