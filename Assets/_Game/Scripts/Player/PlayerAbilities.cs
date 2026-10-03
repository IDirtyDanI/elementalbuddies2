using UnityEngine;
using System;
using System.Collections;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerStats))]
    public class PlayerAbilities : MonoBehaviour
    {
        public static PlayerAbilities Instance { get; private set; }

        public const int ElementCount = 4; // 0 Fire, 1 Ice, 2 Earth, 3 Light

        [Header("Arcane Ball (Q / 6)")]
        public GameObject ArcaneBallPrefab;
        public float ArcaneBallManaCost = 15f;
        public float ArcaneBallCooldown = 1f;
        public Transform SpawnPoint;

        [Header("Blink (Rechte Maustaste)")]
        public float BlinkManaCost = 30f;
        public float BlinkCooldown = 8f;
        public float BlinkRange = 8f;
        public float InvulnerabilityDuration = 0.4f;
        public LayerMask ObstacleLayer; // Assign "Default" or specific wall layer

        [Header("Element Spells (unlockable)")]
        public FireWaveSpell FireWave = new FireWaveSpell();     // R
        public FrostNovaSpell FrostNova = new FrostNovaSpell();  // F
        public StoneWallSpell StoneWall = new StoneWallSpell();  // C
        public HolyCircleSpell HolyCircle = new HolyCircleSpell(); // V

        [Header("Damage Scaling")]
        [Tooltip("Multiplies Arcane Ball and element spell damage. Raised by Player-target Damage upgrades (UpgradeManager).")]
        public float DamageMultiplier = 1f;

        [Header("Debug")]
        [Tooltip("Unlock all four element spells at start (testing).")]
        public bool UnlockAllOnStart = false;

        // Raised on a successful Arcane Ball cast (e.g. for animation)
        public event Action ArcaneBallCast;
        // Raised on every successful ability cast (incl. Arcane Ball and Blink)
        public event Action<AbilityId> OnAbilityCast;
        // Raised when an element spell gets unlocked (element index 0..3)
        public event Action<int> OnAbilityUnlocked;
        // Raised when the key of a still locked spell is pressed (e.g. for a toast)
        public event Action<AbilityId> OnLockedAbilityPressed;

        private static readonly int AbilityCount = Enum.GetValues(typeof(AbilityId)).Length;
        private readonly float[] _readyAt = new float[AbilityCount];
        private readonly bool[] _elementUnlocked = new bool[ElementCount];

        private PlayerController _controller;
        private CharacterController _characterController;
        private PlayerStats _stats;

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("PlayerAbilities: more than one instance in the scene.");
            Instance = this;

            _controller = GetComponent<PlayerController>();
            _characterController = GetComponent<CharacterController>();
            _stats = GetComponent<PlayerStats>();

            if (UnlockAllOnStart)
                for (int i = 0; i < ElementCount; i++) _elementUnlocked[i] = true;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            HandleSkills();
        }

        private void HandleSkills()
        {
            if (!CanCastNow()) return;

            var im = InteractionManager.Instance;
            if (Pressed(_controller.Skill6Action) && (im == null || !im.WouldConsumeLeftClick())) TryCast(AbilityId.ArcaneBall); // Linke Maustaste
            if (Pressed(_controller.SkillEAction) && (im == null || !im.WouldConsumeRightClick())) TryCast(AbilityId.Blink);      // Rechte Maustaste
            if (Pressed(_controller.SpellFireAction)) TryCast(AbilityId.FireWave);   // R
            if (Pressed(_controller.SpellIceAction)) TryCast(AbilityId.FrostNova);   // F
            if (Pressed(_controller.SpellEarthAction)) TryCast(AbilityId.StoneWall); // C
            if (Pressed(_controller.SpellLightAction)) TryCast(AbilityId.HolyCircle); // V
        }

        private static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();

        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;

        private static bool CanCastNow() => Time.timeScale > 0f && !IsGameOver;

        // ---------------- Unlocking ----------------

        // 0 Fire, 1 Ice, 2 Earth, 3 Light. Returns false if invalid or already unlocked.
        public bool UnlockElementAbility(int elementIndex)
        {
            if (elementIndex < 0 || elementIndex >= ElementCount) return false;
            if (_elementUnlocked[elementIndex]) return false;
            _elementUnlocked[elementIndex] = true;
            OnAbilityUnlocked?.Invoke(elementIndex);
            return true;
        }

        public bool IsUnlocked(int elementIndex)
        {
            return elementIndex >= 0 && elementIndex < ElementCount && _elementUnlocked[elementIndex];
        }

        // Arcane Ball and Blink are always unlocked
        public bool IsUnlocked(AbilityId id)
        {
            int element = ElementIndexOf(id);
            return element < 0 || _elementUnlocked[element];
        }

        // Element index of an ability (-1 for Arcane Ball / Blink)
        public static int ElementIndexOf(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.FireWave: return 0;
                case AbilityId.FrostNova: return 1;
                case AbilityId.StoneWall: return 2;
                case AbilityId.HolyCircle: return 3;
                default: return -1;
            }
        }

        public static AbilityId AbilityOfElement(int elementIndex)
        {
            switch (elementIndex)
            {
                case 0: return AbilityId.FireWave;
                case 1: return AbilityId.FrostNova;
                case 2: return AbilityId.StoneWall;
                default: return AbilityId.HolyCircle;
            }
        }

        // ---------------- Cooldown / cost API (UI) ----------------

        public float GetCooldownRemaining(AbilityId id)
        {
            return Mathf.Max(0f, _readyAt[(int)id] - Time.time);
        }

        public float GetCooldownDuration(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall: return ArcaneBallCooldown;
                case AbilityId.Blink: return BlinkCooldown;
                default: return GetSpell(id).Cooldown;
            }
        }

        public float GetManaCost(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall: return ArcaneBallManaCost;
                case AbilityId.Blink: return BlinkManaCost;
                default: return GetSpell(id).ManaCost;
            }
        }

        // Unlocked, off cooldown, enough mana, not game over
        public bool IsAvailable(AbilityId id)
        {
            if (IsGameOver || !IsUnlocked(id)) return false;
            if (GetCooldownRemaining(id) > 0f) return false;
            float mana = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentMana : 0f;
            return mana >= GetManaCost(id);
        }

        public ElementSpell GetSpell(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.FireWave: return FireWave;
                case AbilityId.FrostNova: return FrostNova;
                case AbilityId.StoneWall: return StoneWall;
                case AbilityId.HolyCircle: return HolyCircle;
                default: return null;
            }
        }

        // ---------------- Casting ----------------

        // Public so UI buttons could trigger casts as well
        public bool TryCast(AbilityId id)
        {
            if (!CanCastNow()) return false;

            if (!IsUnlocked(id))
            {
                OnLockedAbilityPressed?.Invoke(id);
                return false;
            }

            if (GetCooldownRemaining(id) > 0f) return false;
            if (id == AbilityId.ArcaneBall && ArcaneBallPrefab == null) return false;
            if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpendMana(GetManaCost(id))) return false;

            _readyAt[(int)id] = Time.time + GetCooldownDuration(id);

            switch (id)
            {
                case AbilityId.ArcaneBall:
                    CastArcaneBall();
                    break;
                case AbilityId.Blink:
                    StartCoroutine(PerformBlink());
                    break;
                default:
                    GetSpell(id).Cast(BuildContext());
                    break;
            }

            OnAbilityCast?.Invoke(id);
            return true;
        }

        private SpellCastContext BuildContext()
        {
            Vector3 origin = transform.position;
            origin.y = GetGroundHeight(origin, origin.y);
            return new SpellCastContext
            {
                Caster = this,
                Origin = origin,
                AimDirection = _controller.AimDirection,
                DamageMultiplier = DamageMultiplier
            };
        }

        // Height of the floor below/around a point (PlayerController.FloorLayer); fallback if nothing is hit
        public float GetGroundHeight(Vector3 point, float fallback)
        {
            if (_controller == null || _controller.FloorLayer.value == 0) return fallback;
            Vector3 from = new Vector3(point.x, point.y + 5f, point.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 20f, _controller.FloorLayer, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return fallback;
        }

        private void CastArcaneBall()
        {
            Vector3 spawnPos = SpawnPoint != null ? SpawnPoint.position : transform.position + transform.forward + Vector3.up;
            GameObject ball = Instantiate(ArcaneBallPrefab, spawnPos, transform.rotation);
            if (!Mathf.Approximately(DamageMultiplier, 1f))
            {
                var arcane = ball.GetComponent<ArcaneBall>();
                if (arcane != null) arcane.Damage *= DamageMultiplier;
            }
            ArcaneBallCast?.Invoke();
        }

        private IEnumerator PerformBlink()
        {
            _stats.IsInvulnerable = true;

            // Richtung Mauszeiger (Rechtsklick); liegt der Zeiger näher als die Reichweite, landet man genau dort
            Vector3 blinkDir = transform.forward;
            float distance = BlinkRange;
            if (_controller.HasAimPoint)
            {
                Vector3 to = _controller.AimPoint - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.04f)
                {
                    blinkDir = to.normalized;
                    distance = Mathf.Min(BlinkRange, to.magnitude);
                }
            }

            // Wall Check
            Vector3 targetPos = transform.position + blinkDir * distance;
            if (Physics.Raycast(transform.position + Vector3.up, blinkDir, out RaycastHit hit, distance, ObstacleLayer))
            {
                targetPos = hit.point - blinkDir * 0.5f; // Stop slightly before wall
                targetPos.y = transform.position.y;
            }

            _characterController.enabled = false;
            transform.position = targetPos;
            _characterController.enabled = true;

            yield return new WaitForSeconds(InvulnerabilityDuration);
            _stats.IsInvulnerable = false;
        }
    }
}
