using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public abstract class ElementalBuddy : MonoBehaviour, IDamageable, IHealthBarTarget, IShieldedTarget
    {
        public UnitConfigSO Config; // Public for setup if needed
        [HideInInspector] public float PaidCost; // Tatsächlich bezahlte Seelensplitter inkl. Aufwertungen (für Refund beim Verkauf)

        // Registry aller aktiven Buddies (für Slot-Limit)
        private static readonly List<ElementalBuddy> _active = new List<ElementalBuddy>();
        public static IReadOnlyList<ElementalBuddy> Active => _active;
        public static int ActiveCount => _active.Count;
        public static event System.Action OnBuddyCountChanged;
        // Buddy im Kampf zerstört (nicht bei Verkauf/Fusion)
        public static event System.Action<ElementalBuddy> OnBuddyDestroyed;

        protected float lastActionTime;
        public float CurrentHP { get; protected set; }
        [Tooltip("Leben auf Stufe 1, falls die Config kein BuddyMaxHP vorgibt.")]
        [FormerlySerializedAs("MaxHP")] public float BaseMaxHP = 50f;
        // Klassen-Standard (Subklassen können ihn überschreiben)
        protected virtual float DefaultMaxHP => BaseMaxHP;
        // Config.BuddyMaxHP > 0 hat Vorrang; skaliert mit der Stufe
        public float GetMaxHPAtLevel(int level) =>
            (Config != null && Config.BuddyMaxHP > 0f ? Config.BuddyMaxHP : DefaultMaxHP) * LevelMultiplier(HPBonusPerLevel, level);
        public float MaxHP => GetMaxHPAtLevel(_level);
        public bool IsDead => _isDead;
        bool IHealthBarTarget.HealthBarVisible => isActiveAndEnabled && !_isDead;

        private bool _isDead;
        private EnemyHealthBar _healthBar;
        private WaveManager _waveManager;
        private float _nextHitFxTime;
        private bool _lowHPWarned;
        private static float _nextAttackedToastTime;

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
            new[] { "Flammenkaiser", "Frostkönig", "Bergkönig", "Sonnenerzengel" },
        };
        // Ab dieser Stufe hat ein Basis-Buddy seinen Stufe-4-Bonus (Perk)
        public const int PerkLevel = 4;
        public bool HasPerk => !IsFusion && _level >= PerkLevel;
        private int _elementIndex = -1;

        // Reset bei deaktiviertem Domain Reload
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            _active.Clear();
            OnBuddyCountChanged = null;
            OnBuddyDestroyed = null;
            _nextAttackedToastTime = 0f;
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
            CurrentHP = MaxHP * Mathf.Clamp(_startHPFraction, 0.01f, 1f);
            RefreshVisual();

            // Ghosts sind deaktiviert -> Start läuft nur bei platzierten Buddies
            var s = Settings;
            if (s != null && s.BuddyHealthBarPrefab != null)
            {
                var bar = Instantiate(s.BuddyHealthBarPrefab);
                _healthBar = bar.GetComponent<EnemyHealthBar>();
                if (_healthBar != null) _healthBar.Bind(this);
                else Destroy(bar);
            }

            _waveManager = WaveManager.Instance;
            if (_waveManager != null) _waveManager.OnWaveEnd += HandleWaveEnd;
        }

        protected virtual void OnDestroy()
        {
            if (_waveManager != null) _waveManager.OnWaveEnd -= HandleWaveEnd;
            if (_healthBar != null) Destroy(_healthBar.gameObject);
        }

        // Wellenende: einen Teil des Lebens zurück
        private void HandleWaveEnd()
        {
            if (_isDead || !isActiveAndEnabled) return;
            float pct = Settings != null ? Settings.BuddyWaveEndHealPercent : 0.5f;
            Heal(MaxHP * pct);
        }

        // Aktives Animator-Visual neu suchen (nach Stufenwechsel durch BuddyEvolution); inaktive Visuals werden ignoriert
        public void RefreshVisual()
        {
            _visualAnimator = GetComponentInChildren<Animator>();
        }

        // Betäubt (Boss-Fähigkeiten): keine Aktionen, Auren/Segen der Subklassen pausieren
        private float _stunUntil;
        public bool IsStunned => Time.time < _stunUntil;

        public void Stun(float duration, GameObject vfxPrefab = null)
        {
            if (_isDead || duration <= 0f) return;
            _stunUntil = Mathf.Max(_stunUntil, Time.time + duration);
            if (vfxPrefab == null && Settings != null) vfxPrefab = Settings.BuddyStunEffectPrefab;
            if (vfxPrefab != null) BlindEffect.Apply(gameObject, duration, vfxPrefab);
        }

        protected virtual void Update()
        {
            if (Config == null || IsStunned) return;

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
            if (_isDead || amount <= 0f) return;
            // Steinhaut eines Stufe-4-Erd-Buddys in der Nähe (nach den eigenen Reduktionen der Subklassen)
            amount *= TankBuddy.GetDamageTakenMultiplier(this);
            // Schild fängt zuerst ab
            if (ShieldAmount > 0f)
            {
                float absorbed = Mathf.Min(_shield, amount);
                _shield -= absorbed;
                amount -= absorbed;
                if (amount <= 0f) return;
            }
            CurrentHP -= amount;
            if (CurrentHP <= 0)
            {
                Die();
                return;
            }

            var s = Settings;
            if (s != null && s.BuddyHitEffectPrefab != null && Time.time >= _nextHitFxTime)
            {
                _nextHitFxTime = Time.time + 0.5f;
                Destroy(Instantiate(s.BuddyHitEffectPrefab, transform.position + Vector3.up * 0.9f, Quaternion.identity), 2f);
            }

            // Warnung unter 60 % (einmal pro Absinken, global höchstens alle 8 s)
            if (!_lowHPWarned && CurrentHP < MaxHP * 0.6f)
            {
                _lowHPWarned = true;
                if (Time.time >= _nextAttackedToastTime)
                {
                    _nextAttackedToastTime = Time.time + 8f;
                    ToastUI.Show($"{StageName} wird angegriffen!");
                }
            }
        }

        // ---------------- Schild (Segen des Sonnenerzengels) ----------------

        private float _shield, _shieldUntil;
        public float ShieldAmount => Time.time < _shieldUntil ? _shield : 0f;

        // Schild, der Schaden zuerst abfängt; stapelt nicht (stärkerer Wert bleibt, Dauer wird erneuert)
        public void AddShield(float amount, float duration)
        {
            if (_isDead || amount <= 0f || duration <= 0f) return;
            _shield = Mathf.Max(ShieldAmount, amount);
            _shieldUntil = Time.time + duration;
        }

        // ---------------- Wiedergeburt (Phönix) ----------------

        private float _startHPFraction = 1f;
        public bool SuppressLevelFx { get; private set; }

        // Vor Start() aufrufen: Stufe ohne Kosten setzen (Wiedergeburt), Level-Up-Effekte unterdrückt
        public void RestoreLevel(int level)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            if (level == _level) return;
            _level = level;
            SuppressLevelFx = true;
            try { OnLevelChanged?.Invoke(); }
            finally { SuppressLevelFx = false; }
        }

        // Vor Start() aufrufen: mit diesem Anteil des Max-Lebens starten
        public void SetStartHealthFraction(float fraction) => _startHPFraction = Mathf.Clamp01(fraction);

        // Heilung (z. B. durch den Segen des Licht-Buddys); gibt die tatsächlich geheilte Menge zurück
        public float Heal(float amount)
        {
            if (_isDead || amount <= 0f || CurrentHP >= MaxHP) return 0f;
            float before = CurrentHP;
            CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
            if (CurrentHP >= MaxHP * 0.6f) _lowHPWarned = false;
            return CurrentHP - before;
        }

        protected virtual void Die()
        {
            if (_isDead) return;
            _isDead = true;
            CurrentHP = 0f;
            Vector3 pos = transform.position;

            var s = Settings;
            if (s != null && s.BuddyDeathEffectPrefab != null)
                Destroy(Instantiate(s.BuddyDeathEffectPrefab, pos, Quaternion.identity), 3f);
            GameAudio.Play(GameAudio.Has(SfxId.BuddyDeath) ? SfxId.BuddyDeath : SfxId.StoneWall, pos);
            ToastUI.Show($"{StageName} wurde zerstört!");

            var im = InteractionManager.Instance;
            if (im != null && im.SelectedBuddy == this) im.DeselectBuddy();
            if (_healthBar != null) Destroy(_healthBar.gameObject);

            OnBuddyDestroyed?.Invoke(this);
            // Sofort deaktivieren: verlässt die Registry (Slots, Gegner-Ziele, Auren) noch in diesem Frame
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        // Nächster Punkt auf dem Collider (große Buddies wie der Kristall), sonst Pivot
        public Vector3 GetClosestPoint(Vector3 from)
        {
            if (_collider == null) _collider = GetComponentInChildren<Collider>();
            if (_collider == null || !_collider.enabled) return transform.position;
            var mesh = _collider as MeshCollider;
            if (mesh != null && !mesh.convex) return _collider.ClosestPointOnBounds(from);
            return _collider.ClosestPoint(from);
        }
        private Collider _collider;

        // ---------------- Aufwertung ----------------

        private static GlobalSettingsSO Settings => EconomyManager.Instance != null ? EconomyManager.Instance.Settings : null;

        protected static float DamageBonusPerLevel => Settings != null ? Settings.DamageBonusPerLevel : 0.35f;
        protected static float FireRateBonusPerLevel => Settings != null ? Settings.FireRateBonusPerLevel : 0.2f;
        protected static float RangeBonusPerLevel => Settings != null ? Settings.RangeBonusPerLevel : 0.1f;
        protected static float HPBonusPerLevel => Settings != null ? Settings.HPBonusPerLevel : 0.35f;

        public virtual int MaxLevel => Settings != null ? Mathf.Max(1, Settings.BuddyMaxLevel) : 3;
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
        // Schaden inkl. passivem Schrein-Bonus des eigenen Elements (ShrineBonuses); Subklassen überschreiben GetBaseDamageAtLevel
        public float GetDamageAtLevel(int level) => GetBaseDamageAtLevel(level) * ShrineDamageMultiplier;
        // Fusionen: Produkt der Schrein-Boni beider Eltern-Elemente
        protected virtual float ShrineDamageMultiplier => ShrineBonuses.GetDamageMultiplier(ElementIndex);
        protected virtual float GetBaseDamageAtLevel(int level) => Config != null ? Config.Damage * LevelMultiplier(DamageBonusPerLevel, level) : 0f;
        public virtual float GetFireRateAtLevel(int level) => Config != null ? Config.FireRate * LevelMultiplier(FireRateBonusPerLevel, level) : 0f;
        public virtual float GetRangeAtLevel(int level) => Config != null ? Config.Range * LevelMultiplier(RangeBonusPerLevel, level) : 0f;

        // inkl. Phönix-Aura (nur hier, nicht in GetDamageAtLevel -> kein Doppelzählen in Subklassen/Anzeige)
        public float EffectiveDamage => GetDamageAtLevel(_level) * PhoenixBuddy.GetDamageMultiplier(this);
        // inkl. Feuerrate-Aura eines Luft-Buddys in der Nähe
        public float EffectiveFireRate => GetFireRateAtLevel(_level) * AirBuddy.GetFireRateMultiplier(this);
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

            float oldMaxHP = MaxHP;
            _level++;
            CurrentHP += MaxHP - oldMaxHP; // Leben steigt mit
            if (_healthBar != null) _healthBar.MarkDirty();
            OnLevelChanged?.Invoke();
            return true;
        }

        // ---------------- Anzeige ----------------

        // 0 = Feuer, 1 = Eis, 2 = Erde, 3 = Licht. Über Config-/Prefab-Namen, da UnitType in den Configs nicht verlässlich gesetzt ist.
        // Fusionen: erstes Eltern-Element (hält element-indizierte UI-Arrays sicher)
        public virtual int ElementIndex
        {
            get
            {
                if (_elementIndex < 0) _elementIndex = ResolveElementIndex();
                return _elementIndex;
            }
        }

        public virtual string DisplayName => ElementNames[ElementIndex];
        public virtual bool IsFusion => false;
        public virtual bool IsSuper => false;

        // Entwicklungsstufe 1–4 (höhere Stufen zeigen die letzte Entwicklung)
        public int Stage => Mathf.Clamp(_level, 1, StageNames.Length);
        public virtual string StageName => StageNames[Stage - 1][ElementIndex];

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
            if (n.Contains("fusion") || n.Contains("super")) return -1; // Fusions-/Super-Configs nie als Basis-Element deuten
            if (n.Contains("fire")) return 0;
            if (n.Contains("ice")) return 1;
            if (n.Contains("earth")) return 2;
            if (n.Contains("light")) return 3;
            return -1;
        }
    }
}
