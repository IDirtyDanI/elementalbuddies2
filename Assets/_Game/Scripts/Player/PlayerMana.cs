using System;
using UnityEngine;

namespace ElementalBuddies
{
    // Ressourcen-Profil eines Champions, der statt Mana eine eigene Ressource nutzt (Berserker: Wut).
    // Liefert das aktive ChampionKit (ChampionKit.Resource); null = normales Mana aus GlobalSettings.
    [Serializable]
    public class ResourceProfile
    {
        public string Name = "Wut";
        [Tooltip("Farbe der HUD-Leiste und der Kostenzeilen.")]
        public Color Color = new Color(0.66f, 0.2f, 0.16f);
        [Tooltip("HUD-Symbol links neben der Leiste (statt des Mana-Tropfens), z. B. icon_rage.")]
        public Sprite Icon;
        public float Cap = 100f;
        [Tooltip("Wert beim Champion-Wechsel / Spielstart.")]
        public float StartValue = 0f;
        [Tooltip("Bei Wellenstart mindestens auf diesen Wert setzen (0 = nichts).")]
        public float WaveStartMinimum = 25f;
        [Tooltip("Regeneration pro Sekunde im Kampf / außerhalb (Wut: 0).")]
        public float RegenIn = 0f, RegenOut = 0f;
        [Tooltip("Verfall: so lange nach dem letzten ausgeteilten/erlittenen Schaden beginnt er (s).")]
        public float DecayDelay = 4f;
        [Tooltip("Verfall pro Sekunde bis 0.")]
        public float DecayRate = 8f;
        [Tooltip("Anteil der Mana-Obergrenzen-Boni (Draft-Karten), der auf diese Obergrenze wirkt.")]
        public float CapBonusScale = 0.5f;
    }

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

        // Ressourcen-Profil des aktiven Champions (null = Mana). Gesetzt von PlayerAbilities beim Champion-Wechsel.
        public ResourceProfile Profile { get; private set; }
        public bool HasProfile => Profile != null;
        public string ResourceName => Profile != null ? Profile.Name : "Mana";
        private float _lastCombat = -999f;

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

        public float MaxMana => Profile != null ? ProfileCap(Profile.Cap, CapBonus, Profile.CapBonusScale) : Mathf.Max(0f, BaseManaCap + CapBonus);
        public float RegenIn => (Profile != null ? Profile.RegenIn : BaseRegenIn) * RegenFactor;
        public float RegenOut => (Profile != null ? Profile.RegenOut : BaseRegenOut) * RegenFactor;
        private float RegenFactor => Mathf.Max(0f, 1f + RegenBonusPercent / 100f);
        // Wut-Aufbau: Regenerations-Karten (+%) erhöhen den Aufbau um denselben Prozentsatz
        public float GainFactor => RegenFactor;

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
                _waveSub.OnWaveStart -= HandleWaveStart;
                _waveSub.OnWaveEnd -= HandleWaveEnd;
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
                _waveSub.OnWaveStart -= HandleWaveStart;
                _waveSub.OnWaveEnd -= HandleWaveEnd;
            }
            _waveSub = wm;
            wm.OnWaveStart += HandleWaveStart;
            wm.OnWaveEnd += HandleWaveEnd;
        }

        void Update()
        {
            if (_waveSub == null) TrySubscribeWaves();
            if (!IsLocal) return;
            if (_current > MaxMana) { SetValue(MaxMana); return; } // Bonus entfernt / Grenze gesunken
            if (Profile != null)
            {
                // Wut: kein Auffüllen, nach DecayDelay ohne Kampf verfällt sie
                float next = DecayStep(_current, Time.time - _lastCombat, Profile.DecayDelay, Profile.DecayRate, Time.deltaTime);
                if (CurrentRegen > 0f) next = Mathf.Min(MaxMana, next + CurrentRegen * Time.deltaTime);
                if (!Mathf.Approximately(next, _current)) SetValue(next);
                return;
            }
            if (_current < MaxMana) Add(CurrentRegen * Time.deltaTime);
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

        private void HandleWaveStart()
        {
            if (Profile == null) { Refill(); return; }
            // Wut: kein Auffüllen, nur Mindestwert, damit die erste Fähigkeit schnell möglich ist
            if (!IsLocal || IsGameOver) return;
            if (_current < Profile.WaveStartMinimum) SetValue(Mathf.Min(MaxMana, Profile.WaveStartMinimum));
            MarkCombat();
        }

        private void HandleWaveEnd()
        {
            if (Profile == null) Refill();
        }

        // ---------------- Ressourcen-Profil (Wut) ----------------

        // Champion-Wechsel: Profil setzen (null = Mana) und auf den Startwert bringen
        public void ApplyProfile(ResourceProfile profile)
        {
            EnsureInitialized();
            bool changed = profile != Profile;
            Profile = profile;
            if (!changed) return;
            _lastCombat = -999f;
            SetValue(profile != null ? Mathf.Clamp(profile.StartValue, 0f, MaxMana) : MaxMana);
            NotifyChanged();
        }

        // Wut gewinnen (nur Besitzer; Server schickt sie über das Kit-Ereignis). Karten-Bonus (GainFactor) wird angerechnet.
        // amount 0 = nur Kampf markieren (Verfall aufschieben).
        public void Gain(float amount)
        {
            MarkCombat();
            if (amount <= 0f || !IsLocal) return;
            Add(amount * GainFactor);
        }

        // Ausgeteilter/erlittener Schaden: Verfall startet erst DecayDelay später
        public void MarkCombat() => _lastCombat = Time.time;

        // Obergrenze mit Profil: Draft-Boni wirken mit Faktor scale
        public static float ProfileCap(float cap, float capBonus, float scale) => Mathf.Max(0f, cap + capBonus * scale);

        // Ein Verfall-Schritt: erst nach delay Sekunden ohne Kampf, dann rate pro Sekunde bis 0
        public static float DecayStep(float current, float sinceCombat, float delay, float rate, float dt)
        {
            if (sinceCombat < delay || current <= 0f || rate <= 0f) return current;
            return Mathf.Max(0f, current - rate * dt);
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
            if (amount > 0f && IsLocal && Profile == null) Add(amount); // Wut wird nicht aufgefüllt
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
            SetValue(Profile != null ? Mathf.Clamp(Profile.StartValue, 0f, MaxMana) : MaxMana);
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
