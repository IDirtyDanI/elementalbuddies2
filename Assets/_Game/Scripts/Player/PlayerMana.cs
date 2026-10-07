using System;
using UnityEngine;

namespace ElementalBuddies
{
    // Mana einer Spielfigur (jeder Spieler hat sein eigenes). Sitzt auf Player.prefab.
    // Besitzer-lokal: Nur die eigene Figur regeneriert, gibt aus und füllt bei Wellenstart/-ende auf.
    // Andere Rechner sehen den Wert über PlayerAvatar (Besitzer schreibt eine NetworkVariable) – damit kann der
    // Server z. B. den Schildblock des Schwertkämpfers gegen das Mana des Besitzers prüfen (SpendFromServer).
    // Werte aus GlobalSettingsSO (ManaCap, RegenInCombat/RegenOutCombat) + persönliche Boni (Karten).
    public class PlayerMana : MonoBehaviour
    {
        // Mana der eigenen Figur (null vor dem Netz-Spawn)
        public static PlayerMana Local { get; private set; }
        // Feuert bei jeder Änderung des lokalen Manas und wenn die lokale Figur wechselt (für HUD/EconomyManager-Weiterleitung)
        public static event Action OnLocalManaChanged;

        [Tooltip("Leer = GlobalSettings aus EconomyManager bzw. Resources/GlobalSettings")]
        [SerializeField] private GlobalSettingsSO settings;

        public event Action OnManaChanged;

        private float _current;
        private bool _hasRemoteValue;
        private bool _initialized;
        private WaveManager _waveSub;
        private PlayerAvatar _avatar;

        // Persönliche Boni (ManaBoost-Karte u. ä.)
        public float CapBonus { get; private set; }
        public float RegenBonusPercent { get; private set; }

        public GlobalSettingsSO Settings
        {
            get
            {
                if (settings == null)
                {
                    var eco = EconomyManager.Instance;
                    settings = eco != null && eco.Settings != null ? eco.Settings : Resources.Load<GlobalSettingsSO>("GlobalSettings");
                }
                return settings;
            }
        }

        public float BaseManaCap => Settings != null ? Settings.ManaCap : 200f;
        public float BaseRegenIn => Settings != null ? Settings.RegenInCombat : 0.5f;
        public float BaseRegenOut => Settings != null ? Settings.RegenOutCombat : 1.5f;

        public float MaxMana => Mathf.Max(0f, BaseManaCap + CapBonus);
        public float RegenIn => BaseRegenIn * RegenFactor;
        public float RegenOut => BaseRegenOut * RegenFactor;
        private float RegenFactor => Mathf.Max(0f, 1f + RegenBonusPercent / 100f);

        // Aktuelle Regeneration pro Sekunde (Kampf / außerhalb)
        public float CurrentRegen => InCombat ? RegenIn : RegenOut;

        public float CurrentMana => _current;
        public float Mana01 => MaxMana > 0f ? Mathf.Clamp01(_current / MaxMana) : 0f;

        // Eigene Figur (Besitzer) bzw. offline ohne Netz
        public bool IsLocal => _avatar == null ? true : _avatar.IsLocalControl;

        private static bool InCombat => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Combat;
        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;

        void Awake()
        {
            _avatar = GetComponent<PlayerAvatar>();
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _current = MaxMana; // Start immer mit vollem Mana
        }

        void Start()
        {
            if (_avatar == null || !Net.IsRunning) MarkLocal(true);
            TrySubscribeWaves();
        }

        void OnDestroy()
        {
            if (Local == this)
            {
                Local = null;
                OnLocalManaChanged?.Invoke();
            }
            if (_waveSub != null)
            {
                _waveSub.OnWaveStart -= Refill;
                _waveSub.OnWaveEnd -= Refill;
                _waveSub = null;
            }
        }

        // Von PlayerAvatar (Besitzer-Erkennung) bzw. offline aus Start gesetzt
        public void MarkLocal(bool local)
        {
            if (local)
            {
                if (Local == this) return;
                Local = this;
                OnLocalManaChanged?.Invoke();
            }
            else if (Local == this)
            {
                Local = null;
                OnLocalManaChanged?.Invoke();
            }
        }

        private void TrySubscribeWaves()
        {
            var wm = WaveManager.Instance;
            if (wm == null || wm == _waveSub) return;
            if (_waveSub != null)
            {
                _waveSub.OnWaveStart -= Refill;
                _waveSub.OnWaveEnd -= Refill;
            }
            _waveSub = wm;
            wm.OnWaveStart += Refill;
            wm.OnWaveEnd += Refill;
        }

        void Update()
        {
            if (_waveSub == null) TrySubscribeWaves();
            if (!IsLocal) return;
            if (_current < MaxMana) Add(CurrentRegen * Time.deltaTime);
            else if (_current > MaxMana) SetValue(MaxMana); // Bonus entfernt / Grenze gesunken
        }

        // ---------------- Ändern (Besitzer) ----------------

        public void Add(float amount)
        {
            if (amount == 0f) return;
            SetValue(Mathf.Clamp(_current + amount, 0f, MaxMana));
        }

        public bool TrySpend(float amount)
        {
            if (amount <= 0f) return true;
            if (_current < amount) return false;
            SetValue(_current - amount);
            return true;
        }

        // Komplett auffüllen (Wellenstart/-ende, nur eigene Figur; Abbilder bekommen den Wert vom Besitzer)
        public void Refill()
        {
            if (!IsLocal || IsGameOver) return;
            SetValue(MaxMana);
        }

        // Server zieht Mana ab (z. B. geblockter Schaden). Eigene Figur: direkt; fremde: per RPC beim Besitzer.
        // Der gespiegelte Wert sinkt sofort mit, damit weitere Treffer im selben Moment nicht doppelt abbuchen.
        public void SpendFromServer(float amount)
        {
            if (amount <= 0f) return;
            if (IsLocal || _avatar == null || !_avatar.IsSpawned)
            {
                SetValue(Mathf.Max(0f, _current - amount));
                return;
            }
            SetValue(Mathf.Max(0f, _current - amount));
            _avatar.SendManaSpendToOwner(amount);
        }

        // ---------------- Persönliche Boni ----------------

        // Mana-Obergrenze erhöhen (füllt um den Bonus auf, wie die alte ManaBoost-Karte)
        public void AddCapBonus(float amount)
        {
            if (amount == 0f) return;
            CapBonus += amount;
            if (amount > 0f && IsLocal) Add(amount);
            else NotifyChanged();
        }

        // Regeneration um Prozent erhöhen (additiv, 25 = +25 %)
        public void AddRegenBonusPercent(float percent)
        {
            if (percent == 0f) return;
            RegenBonusPercent += percent;
            NotifyChanged();
        }

        // Neues Spiel: Boni weg, voll
        public void ResetForNewRun()
        {
            CapBonus = 0f;
            RegenBonusPercent = 0f;
            SetValue(MaxMana);
        }

        // ---------------- Netz ----------------

        // Abbild: Wert vom Besitzer (PlayerAvatar)
        internal void SetRemoteValue(float value)
        {
            if (IsLocal) return;
            _hasRemoteValue = true;
            SetValue(value);
        }

        // Wurde schon ein Wert vom Besitzer empfangen?
        public bool HasRemoteValue => _hasRemoteValue;

        private void SetValue(float value)
        {
            if (Mathf.Approximately(value, _current)) { _current = value; return; }
            _current = value;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            OnManaChanged?.Invoke();
            if (Local == this) OnLocalManaChanged?.Invoke();
        }
    }
}
