using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public abstract class ElementalBuddy : MonoBehaviour, IDamageable
    {
        public UnitConfigSO Config; // Public for setup if needed
        [HideInInspector] public float PaidCost; // Tatsächlich bezahlte Seelensplitter inkl. Aufwertungen (für Refund beim Verkauf)

        // Registry aller aktiven Buddies (für Slot-Limit)
        private static readonly List<ElementalBuddy> _active = new List<ElementalBuddy>();
        public static IReadOnlyList<ElementalBuddy> Active => _active;
        public static int ActiveCount => _active.Count;
        public static event System.Action OnBuddyCountChanged;

        protected float lastActionTime;
        public float CurrentHP { get; protected set; }

        private static readonly int CastTrigger = Animator.StringToHash("Cast");
        private Animator _visualAnimator;

        // --- Aufwertung (pro Instanz) ---
        private int _level = 1;
        public int Level => _level;
        public event System.Action OnLevelChanged;

        // Feuert bei jeder ausgeführten Aktion (gleichzeitig mit dem Cast-Trigger) – für Visuals ohne Animator (Stufe 1)
        public event System.Action OnCast;
        // Ziel der letzten Aktion (Gegner bzw. Spieler), optional für Visuals (z. B. Ausfallbewegung); kann null sein
        public Transform CurrentTarget { get; protected set; }

        private static readonly string[] ElementNames = { "Feuer", "Eis", "Erde", "Licht" };
        // Entwicklungsstufen-Namen [Stufe - 1][ElementIndex]
        private static readonly string[][] StageNames =
        {
            new[] { "Flämmchen", "Eiszapfen", "Kiesel", "Funkenlicht" },
            new[] { "Feuer-Elementar", "Eis-Elementar", "Erd-Golem", "Licht-Geist" },
            new[] { "Flammenritter", "Frostwächter", "Steinkoloss", "Sonnenpaladin" },
        };
        private int _elementIndex = -1;

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
            RefreshVisual();
        }

        // Aktives Animator-Visual neu suchen (nach Stufenwechsel durch BuddyEvolution); inaktive Visuals werden ignoriert
        public void RefreshVisual()
        {
            _visualAnimator = GetComponentInChildren<Animator>();
        }

        protected virtual void Update()
        {
            if (Config == null) return;

            float fireRate = EffectiveFireRate;
            if (fireRate > 0 && Time.time >= lastActionTime + (1f / fireRate))
            {
                if (TryPerformAction())
                {
                    lastActionTime = Time.time;
                    if (_visualAnimator != null && _visualAnimator.isActiveAndEnabled) _visualAnimator.SetTrigger(CastTrigger);
                    OnCast?.Invoke();
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

        // ---------------- Aufwertung ----------------

        private static GlobalSettingsSO Settings => EconomyManager.Instance != null ? EconomyManager.Instance.Settings : null;

        protected static float DamageBonusPerLevel => Settings != null ? Settings.DamageBonusPerLevel : 0.35f;
        protected static float FireRateBonusPerLevel => Settings != null ? Settings.FireRateBonusPerLevel : 0.2f;
        protected static float RangeBonusPerLevel => Settings != null ? Settings.RangeBonusPerLevel : 0.1f;

        public int MaxLevel => Settings != null ? Mathf.Max(1, Settings.BuddyMaxLevel) : 3;
        public bool CanUpgrade => Config != null && _level < MaxLevel;

        // Aufwerten nur in der Bauphase (nicht im Kampf, nicht bei Game Over / Pause / Upgrade-Screen)
        public static bool IsUpgradePhase =>
            (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Building) && Time.timeScale > 0f;

        // Kosten für die nächste Stufe in Seelensplittern; -1 bei Maximalstufe
        public float NextUpgradeCost => CanUpgrade ? GetUpgradeCost(_level + 1) : -1f;

        // Kosten, um targetLevel (>= 2) zu erreichen: CostOutCombat × UpgradeCostFactors[targetLevel - 2]
        public float GetUpgradeCost(int targetLevel)
        {
            if (Config == null || targetLevel < 2) return -1f;
            float[] factors = Settings != null ? Settings.UpgradeCostFactors : null;
            float factor;
            if (factors == null || factors.Length == 0)
                factor = targetLevel == 2 ? 0.6f : 1.0f;
            else
                factor = factors[Mathf.Min(targetLevel - 2, factors.Length - 1)];
            return Mathf.Ceil(Config.CostOutCombat * factor);
        }

        protected static float LevelMultiplier(float bonusPerLevel, int level)
        {
            return 1f + bonusPerLevel * Mathf.Max(0, level - 1);
        }

        // Basiswerte (Config, inkl. globaler Roguelike-Upgrades) × Stufen-Multiplikator; Subklassen können umdeuten
        public virtual float GetDamageAtLevel(int level) => Config != null ? Config.Damage * LevelMultiplier(DamageBonusPerLevel, level) : 0f;
        public virtual float GetFireRateAtLevel(int level) => Config != null ? Config.FireRate * LevelMultiplier(FireRateBonusPerLevel, level) : 0f;
        public virtual float GetRangeAtLevel(int level) => Config != null ? Config.Range * LevelMultiplier(RangeBonusPerLevel, level) : 0f;

        public float EffectiveDamage => GetDamageAtLevel(_level);
        public float EffectiveFireRate => GetFireRateAtLevel(_level);
        public float EffectiveRange => GetRangeAtLevel(_level);

        public bool TryUpgrade()
        {
            if (!CanUpgrade || !IsUpgradePhase) return false;
            var eco = EconomyManager.Instance;
            if (eco == null) return false;

            float cost = NextUpgradeCost;
            if (cost < 0f || !eco.TrySpendShards(cost)) return false;

            // Vorplatzierte Buddies ohne PaidCost: Basis-Kosten als Grundlage (wie beim Verkauf)
            if (PaidCost <= 0f && Config != null) PaidCost = Config.CostOutCombat;
            PaidCost += cost;

            _level++;
            OnLevelChanged?.Invoke();
            return true;
        }

        // ---------------- Anzeige ----------------

        // 0 = Feuer, 1 = Eis, 2 = Erde, 3 = Licht. Über Config-/Prefab-Namen, da UnitType in den Configs nicht verlässlich gesetzt ist.
        public int ElementIndex
        {
            get
            {
                if (_elementIndex < 0) _elementIndex = ResolveElementIndex();
                return _elementIndex;
            }
        }

        public string DisplayName => ElementNames[ElementIndex];

        // Entwicklungsstufe 1–3 (Stufen über 3 zeigen die letzte Entwicklung)
        public int Stage => Mathf.Clamp(_level, 1, StageNames.Length);
        public string StageName => StageNames[Stage - 1][ElementIndex];

        private int ResolveElementIndex()
        {
            int idx = MatchElementName(Config != null ? Config.name : null);
            if (idx < 0) idx = MatchElementName(gameObject.name);
            if (idx < 0 && Config != null && (int)Config.Type < ElementNames.Length) idx = (int)Config.Type;
            return idx < 0 ? 0 : idx;
        }

        private static int MatchElementName(string n)
        {
            if (string.IsNullOrEmpty(n)) return -1;
            n = n.ToLowerInvariant();
            if (n.Contains("fire")) return 0;
            if (n.Contains("ice")) return 1;
            if (n.Contains("earth")) return 2;
            if (n.Contains("light")) return 3;
            return -1;
        }
    }
}
