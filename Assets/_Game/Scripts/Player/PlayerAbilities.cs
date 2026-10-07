using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    // Klassen-unabhängiger Kern der Spieler-Fähigkeiten: Eingabe (Slots LMB/RMB/R/F/C/V), Abklingzeiten,
    // Aufladungen, Mana, Element-Freischaltung (Schreine) und Events. Was eine Taste tut, liefert das
    // aktive ChampionKit (MageKit, KnightKit, ArcherKit – je eine Komponente auf dem Player).
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerStats))]
    public class PlayerAbilities : MonoBehaviour
    {
        // Fähigkeiten der EIGENEN Figur (Besitzer). Im Mehrspieler gibt es mehrere PlayerAbilities (eine pro Spieler);
        // Instance ist immer die lokale (UI, Händler, Schreine, Karten). Vor dem Netz-Spawn null.
        public static PlayerAbilities Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var local = PlayerAvatar.Local;
                return local != null ? local.Abilities : null;
            }
            private set { _instance = value; }
        }
        private static PlayerAbilities _instance;

        public const int ElementCount = 4; // 0 Fire, 1 Ice, 2 Earth, 3 Light

        [Header("Champion")]
        [Tooltip("Zum Testen: Klasse erzwingen statt GameSession.SelectedChampion (Hauptmenü-Wahl).")]
        public bool ForceChampion = false;
        public ChampionClass ForcedChampion = ChampionClass.Mage;

        [Header("Scaling (Upgrade-Karten, klassenunabhängig)")]
        [Tooltip("Multipliziert den Schaden aller Fähigkeiten. Wird von Spieler-Schadenskarten erhöht (UpgradeManager).")]
        public float DamageMultiplier = 1f;
        [Tooltip("Multipliziert alle Abklingzeiten (< 1 = schneller).")]
        public float CooldownMultiplier = 1f;
        [Tooltip("Multipliziert die Reichweite der Mobilitäts-Fähigkeit (Blink-Reichweite, Rollen-Distanz).")]
        public float MobilityMultiplier = 1f;

        // Fähigkeits-Modifikatoren pro Fähigkeit (Händlerkarten, MerchantManager). Wirken zusätzlich zu den globalen Faktoren.
        public readonly AbilityMods Mods = new AbilityMods();

        [Header("Debug")]
        [Tooltip("Unlock all four element spells at start (testing).")]
        public bool UnlockAllOnStart = false;

        // Raised on every successful ability cast
        public event Action<AbilityId> OnAbilityCast;
        // Raised when an element spell gets unlocked (element index 0..3)
        public event Action<int> OnAbilityUnlocked;
        // Raised when the key of a still locked spell is pressed (e.g. for a toast)
        public event Action<AbilityId> OnLockedAbilityPressed;
        // Raised after the active champion changed (UI rebinds icons/texts)
        public event Action<ChampionClass> OnChampionChanged;

        public ChampionKit ActiveKit { get; private set; }
        public ChampionClass ActiveClass => ActiveKit != null ? ActiveKit.Class : ChampionClass.Mage;
        public ChampionVisual ActiveVisual { get; private set; }

        private static readonly int AbilityCount = Enum.GetValues(typeof(AbilityId)).Length;
        private readonly float[] _readyAt = new float[AbilityCount];
        private readonly int[] _charges = new int[AbilityCount];
        private readonly float[] _rechargeAt = new float[AbilityCount];
        private readonly bool[] _elementUnlocked = new bool[ElementCount];
        private readonly bool[] _holding = new bool[AbilitySlots.Count];

        private PlayerController _controller;
        private PlayerStats _stats;
        private PlayerAvatar _avatar;
        private PlayerMana _mana;
        private readonly List<ChampionKit> _kits = new List<ChampionKit>();
        private bool _initialized;

        // Eigene Figur (Besitzer) bzw. offline ohne Netz: liest Eingaben, verbucht Abklingzeiten/Mana und schickt Casts
        public bool IsLocalControl => _avatar == null || _avatar.IsLocalControl;
        public PlayerAvatar Avatar => _avatar;
        public PlayerMana Mana
        {
            get
            {
                if (_mana == null) _mana = GetComponent<PlayerMana>();
                return _mana;
            }
        }

        void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _stats = GetComponent<PlayerStats>();
            _avatar = GetComponent<PlayerAvatar>();
            _mana = GetComponent<PlayerMana>();
            if (_mana == null) _mana = gameObject.AddComponent<PlayerMana>();

            // Offline (ohne Netz-Avatar) ist diese Figur sofort die lokale; sonst setzt PlayerAvatar das beim Spawn
            if (_avatar == null || !Net.IsRunning) Instance = this;

            if (UnlockAllOnStart)
                for (int i = 0; i < ElementCount; i++) _elementUnlocked[i] = true;

            EnsureInitialized();
        }

        // Von PlayerAvatar nach der Besitzer-Erkennung
        public void MarkLocal(bool local)
        {
            if (local) Instance = this;
            else if (_instance == this) Instance = null;
        }

        void OnDestroy()
        {
            if (_instance == this) Instance = null;
            if (_stats != null && _stats.DamageModifier == (Func<float, Vector3, bool, float>)ModifyIncomingDamage) _stats.DamageModifier = null;
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (_controller == null) _controller = GetComponent<PlayerController>();
            if (_stats == null) _stats = GetComponent<PlayerStats>();
            _kits.Clear();
            GetComponents(_kits);
            if (_stats != null) _stats.DamageModifier = ModifyIncomingDamage;
            // Vorläufige Wahl; im Netz legt PlayerAvatar nach dem Spawn den Champion des Besitzers fest (ApplyNetworkChampion)
            var cls = ForceChampion ? ForcedChampion : GameSession.SelectedChampion;
            bool networked = _avatar != null && Net.IsRunning;
            // Meta-Freischaltung: gesperrter Champion (Stand dieses Spiels) → Magier; Inspector-Override/DevTools.UnlockAllContent ausgenommen
            if (!networked && !ForceChampion && !Progression.IsChampionUnlocked(cls, true))
            {
                Debug.Log($"PlayerAbilities: Champion {cls} ist noch gesperrt ({Progression.RequirementText(Progression.ChampionUnlock(cls))}) – Magier wird gespielt.");
                cls = ChampionClass.Mage;
            }
            ApplyChampion(cls, false);
        }

        // Champion aus dem Netz (NetPlayer.Champion des Besitzers). Inspector-Override (ForceChampion) gilt auf allen Rechnern.
        public void ApplyNetworkChampion(ChampionClass cls)
        {
            EnsureInitialized();
            if (ForceChampion) cls = ForcedChampion;
            if (ActiveKit != null && ActiveKit.Class == cls) return;
            ApplyChampion(cls, true);
        }

        // ---------------- Champion ----------------

        public ChampionKit GetKit(ChampionClass cls)
        {
            foreach (var k in _kits) if (k != null && k.Class == cls) return k;
            return null;
        }

        // Klasse wechseln (Szenenstart, DevTools, Inspector-Override). Setzt Abklingzeiten zurück, Freischaltungen bleiben.
        public void SetChampion(ChampionClass cls)
        {
            EnsureInitialized();
            ApplyChampion(cls, true);
        }

        private void ApplyChampion(ChampionClass cls, bool notify)
        {
            ChampionKit kit = GetKit(cls);
            if (kit == null)
            {
                if (_kits.Count == 0)
                {
                    Debug.LogError("PlayerAbilities: kein ChampionKit auf dem Player.");
                    return;
                }
                Debug.LogWarning($"PlayerAbilities: kein Kit für {cls} – nehme {_kits[0].Class}.");
                kit = _kits[0];
            }

            ReleaseAllHolds();
            if (ActiveKit != null && ActiveKit != kit) ActiveKit.OnDeactivated();

            foreach (var k in _kits) if (k != null) k.enabled = k == kit;
            ActiveKit = kit;
            kit.Initialize(this);

            for (int i = 0; i < AbilityCount; i++)
            {
                _readyAt[i] = 0f;
                _rechargeAt[i] = 0f;
                _lastLockout[i] = 0f;
                _charges[i] = kit.GetMaxCharges((AbilityId)i);
            }

            // Passendes Modell einschalten
            ActiveVisual = null;
            foreach (var v in GetComponentsInChildren<ChampionVisual>(true))
            {
                bool match = v.Class == kit.Class;
                bool on = match && !_downedVisual; // ausgefallen: Modell bleibt versteckt
                if (v.gameObject.activeSelf != on) v.gameObject.SetActive(on);
                if (match && ActiveVisual == null) ActiveVisual = v;
            }

            kit.OnActivated();
            if (notify) OnChampionChanged?.Invoke(kit.Class);
        }

        // Ausgefallene Figur (PlayerAvatar): Modell aus-/einblenden, laufende Fähigkeiten beenden
        private bool _downedVisual;
        public void SetDownedVisual(bool downed)
        {
            _downedVisual = downed;
            if (downed)
            {
                if (IsLocalControl) ReleaseAllHolds();
                if (ActiveKit != null) ActiveKit.OnDeactivated();
            }
            if (ActiveVisual != null && ActiveVisual.gameObject.activeSelf == downed) ActiveVisual.gameObject.SetActive(!downed);
        }

        // ---------------- Update / Eingabe ----------------

        void Update()
        {
            UpdateCharges();
            if (_controller != null && ActiveKit != null) _controller.SpeedMultiplier = ActiveKit.MoveSpeedMultiplier;
            // Eingabe nur auf der eigenen Figur; fremde Figuren wirken über Cast-RPCs (PlayerAvatar)
            if (IsLocalControl) HandleSkills();
        }

        private void HandleSkills()
        {
            if (ActiveKit == null) return;
            if (!CanCastNow() || (_stats != null && _stats.IsDead))
            {
                ReleaseAllHolds();
                return;
            }

            var im = InteractionManager.Instance;
            HandleSlot(AbilitySlot.Primary, _controller.Skill6Action, im != null && Pressed(_controller.Skill6Action) && im.WouldConsumeLeftClick()); // Linke Maustaste
            HandleSlot(AbilitySlot.Secondary, _controller.SkillEAction, im != null && Pressed(_controller.SkillEAction) && im.WouldConsumeRightClick()); // Rechte Maustaste
            HandleSlot(AbilitySlot.Fire, _controller.SpellFireAction, false);   // R
            HandleSlot(AbilitySlot.Ice, _controller.SpellIceAction, false);     // F
            HandleSlot(AbilitySlot.Earth, _controller.SpellEarthAction, false); // C
            HandleSlot(AbilitySlot.Light, _controller.SpellLightAction, false); // V
        }

        private void HandleSlot(AbilitySlot slot, InputAction action, bool consumed)
        {
            if (action == null) return;
            AbilityId id = ActiveKit.GetAbility(slot);
            int s = (int)slot;

            if (ActiveKit.IsHoldAbility(id))
            {
                if (_holding[s] && !action.IsPressed())
                {
                    _holding[s] = false;
                    ReleaseHold(id);
                }
                else if (!_holding[s] && action.WasPressedThisFrame() && !consumed)
                {
                    if (TryCast(id)) _holding[s] = true;
                }
                // Block durch Manamangel gebrochen → Halten beenden (neuer Klick nötig)
                if (_holding[s] && !ActiveKit.IsActive(id)) _holding[s] = false;
                return;
            }

            if (action.WasPressedThisFrame())
            {
                if (!consumed) TryCast(id);
            }
            else if (slot == AbilitySlot.Primary && ActiveKit.AutoRepeatPrimary && action.IsPressed() && !IsPointerBusy())
            {
                // Gehaltener Grundangriff: erneut auslösen, sobald bereit (still, kein Locked-Toast)
                if (IsAvailable(id)) TryCast(id);
            }
        }

        private static bool IsPointerBusy()
        {
            var im = InteractionManager.Instance;
            return im != null && im.WouldConsumeLeftClick();
        }

        private void ReleaseAllHolds()
        {
            if (ActiveKit == null) return;
            for (int i = 0; i < _holding.Length; i++)
            {
                if (!_holding[i]) continue;
                _holding[i] = false;
                ReleaseHold(ActiveKit.GetAbility((AbilitySlot)i));
            }
        }

        // Halten beenden – lokal und auf allen anderen Rechnern (z. B. Schildblock loslassen)
        private void ReleaseHold(AbilityId id)
        {
            ActiveKit.SetHeld(id, false);
            if (_avatar != null && _avatar.IsSpawned && _avatar.IsLocalControl) _avatar.SendHeld(id, false);
        }

        private static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();

        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;

        private static bool CanCastNow() => Time.timeScale > 0f && !IsGameOver;

        // ---------------- Slots ----------------

        public AbilityId GetAbility(AbilitySlot slot)
        {
            EnsureInitialized();
            return ActiveKit != null ? ActiveKit.GetAbility(slot) : (AbilityId)(int)slot;
        }

        public AbilityId AbilityOfElement(int elementIndex) => GetAbility(AbilitySlots.OfElement(elementIndex));

        public string GetAbilityName(AbilityId id) => ActiveKit != null ? ActiveKit.GetName(id) : id.ToString();

        public Sprite GetAbilityIcon(AbilityId id) => ActiveKit != null ? ActiveKit.GetIcon(id) : null;

        // Name der Element-Fähigkeit des aktiven Champions (Toasts, Schrein-Texte)
        public string GetElementAbilityName(int elementIndex) => GetAbilityName(AbilityOfElement(elementIndex));

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

        // Grundangriff und Mobilität sind immer frei
        public bool IsUnlocked(AbilityId id)
        {
            int element = ElementIndexOf(id);
            return element < 0 || _elementUnlocked[element];
        }

        // Element index of an ability (-1 for primary / secondary abilities)
        public static int ElementIndexOf(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.FireWave:
                case AbilityId.FlameWhirl:
                case AbilityId.FireArrowRain:
                    return 0;
                case AbilityId.FrostNova:
                case AbilityId.FrostStrike:
                case AbilityId.FrostArrow:
                    return 1;
                case AbilityId.StoneWall:
                case AbilityId.Earthquake:
                case AbilityId.ThornTrap:
                    return 2;
                case AbilityId.HolyCircle:
                case AbilityId.LightOath:
                case AbilityId.LightArrow:
                    return 3;
                default:
                    return -1;
            }
        }

        // ---------------- Cooldown / Ladungen / Kosten (UI) ----------------

        public int GetMaxCharges(AbilityId id) => ActiveKit != null ? Mathf.Max(1, ActiveKit.GetMaxCharges(id)) : 1;
        public int GetCharges(AbilityId id) => GetMaxCharges(id) > 1 ? _charges[(int)id] : (GetCooldownRemaining(id) > 0f ? 0 : 1);

        // Restzeit bis zur nächsten Ladung (0 = alle voll)
        public float GetRechargeRemaining(AbilityId id)
        {
            if (GetMaxCharges(id) <= 1) return GetCooldownRemaining(id);
            if (_charges[(int)id] >= GetMaxCharges(id)) return 0f;
            return Mathf.Max(0f, _rechargeAt[(int)id] - Time.time);
        }

        public float GetCooldownRemaining(AbilityId id)
        {
            float lockout = Mathf.Max(0f, _readyAt[(int)id] - Time.time);
            if (GetMaxCharges(id) > 1 && _charges[(int)id] <= 0)
                return Mathf.Max(lockout, Mathf.Max(0f, _rechargeAt[(int)id] - Time.time));
            return lockout;
        }

        // Basis-Abklingzeit × CooldownMultiplier × Händlerkarten (bei Aufladungen: Zeit pro Ladung)
        public float GetCooldownDuration(AbilityId id)
        {
            if (ActiveKit == null) return 0f;
            return ActiveKit.GetCooldown(id) * GetCooldownFactor(id);
        }

        // Gesamtfaktor für Abklingzeiten einer Fähigkeit (global × Händlerkarten), auch für Kit-eigene Sperren
        public float GetCooldownFactor(AbilityId id) => Mathf.Max(0.1f, CooldownMultiplier) * Mods.Factor(id, AbilityStat.Cooldown);

        // Gesamtschaden einer Fähigkeit: globaler DamageMultiplier × Händlerkarten dieser Fähigkeit
        public float GetDamageMultiplier(AbilityId id) => DamageMultiplier * Mods.Factor(id, AbilityStat.Damage);

        // Wofür der Radial-Balken gerade läuft (bei Aufladungen: Sperre oder Wiederaufladung)
        public float GetCooldownDisplayDuration(AbilityId id)
        {
            if (GetMaxCharges(id) > 1 && _charges[(int)id] <= 0) return GetCooldownDuration(id);
            return _lastLockout[(int)id] > 0f ? _lastLockout[(int)id] : GetCooldownDuration(id);
        }

        public float GetManaCost(AbilityId id) => ActiveKit != null ? ActiveKit.GetManaCost(id) : 0f;

        // Unlocked, off cooldown, enough mana, not game over
        public bool IsAvailable(AbilityId id)
        {
            if (IsGameOver || !IsUnlocked(id) || ActiveKit == null) return false;
            if (GetCooldownRemaining(id) > 0f) return false;
            if (!ActiveKit.CanCast(id)) return false;
            float mana = Mana != null ? Mana.CurrentMana : 0f;
            return mana >= ActiveKit.GetRequiredMana(id);
        }

        public bool IsAbilityActive(AbilityId id) => ActiveKit != null && ActiveKit.IsActive(id);

        // Kits setzen eigene Abklingzeiten (z. B. gebrochener Block, Kombo-Ende)
        private readonly float[] _lastLockout = new float[AbilityCount];
        public void StartCooldown(AbilityId id, float seconds)
        {
            _readyAt[(int)id] = Time.time + seconds;
            _lastLockout[(int)id] = seconds;
        }

        private void UpdateCharges()
        {
            if (ActiveKit == null) return;
            for (int i = 0; i < AbilityCount; i++)
            {
                int max = ActiveKit.GetMaxCharges((AbilityId)i);
                if (max <= 1 || _charges[i] >= max) continue;
                if (Time.time >= _rechargeAt[i])
                {
                    _charges[i]++;
                    if (_charges[i] < max) _rechargeAt[i] += GetCooldownDuration((AbilityId)i);
                }
            }
        }

        // ---------------- Casting ----------------

        // Public so UI buttons / tests could trigger casts as well
        public bool TryCast(AbilityId id)
        {
            if (!CanCastNow() || ActiveKit == null) return false;

            if (!IsUnlocked(id))
            {
                OnLockedAbilityPressed?.Invoke(id);
                return false;
            }

            if (GetCooldownRemaining(id) > 0f) return false;
            if (!ActiveKit.CanCast(id)) return false;

            if (!IsLocalControl) return false; // fremde Figuren wirken nur über Cast-RPCs
            if (_stats != null && _stats.IsDead) return false;

            var mana = Mana;
            if (mana == null) return false;
            float cost = ActiveKit.GetManaCost(id);
            if (mana.CurrentMana < ActiveKit.GetRequiredMana(id)) return false;
            if (cost > 0f && !mana.TrySpend(cost)) return false;

            int idx = (int)id;
            int max = GetMaxCharges(id);
            if (max > 1)
            {
                if (_charges[idx] >= max) _rechargeAt[idx] = Time.time + GetCooldownDuration(id);
                _charges[idx]--;
                StartCooldown(id, ActiveKit.GetChargeLockout(id));
            }
            else
            {
                StartCooldown(id, GetCooldownDuration(id));
            }

            var ctx = BuildContext(id);
            ActiveKit.Cast(id, ctx);
            // Mehrspieler: alle anderen Rechner führen denselben Cast aus (Server = Schaden, Clients = Optik)
            if (_avatar != null && _avatar.IsSpawned)
            {
                ctx.Variant = ActiveKit.GetCastVariant(id, ctx);
                _avatar.SendCast(id, ctx);
            }

            OnAbilityCast?.Invoke(id);
            return true;
        }

        // Abbild eines Casts des Besitzers (vom Netz, PlayerAvatar.CastRpc): ohne Kosten/Abklingzeit/Freischalt-Prüfung
        public void ExecuteRemoteCast(AbilityId id, SpellCastContext ctx)
        {
            EnsureInitialized();
            if (ActiveKit == null) return;
            ctx.Caster = this;
            ctx.IsRemote = true;
            ActiveKit.Cast(id, ctx);
            OnAbilityCast?.Invoke(id);
        }

        // Abbild: gehaltene Fähigkeit losgelassen (PlayerAvatar.HeldRpc)
        public void ExecuteRemoteHeld(AbilityId id, bool held)
        {
            if (ActiveKit == null) return;
            ActiveKit.SetHeld(id, held);
        }

        // Kontext für eine bestimmte Fähigkeit: Schaden inkl. Händlerkarten dieser Fähigkeit
        public SpellCastContext BuildContext(AbilityId id)
        {
            var ctx = BuildContext();
            ctx.DamageMultiplier = GetDamageMultiplier(id);
            return ctx;
        }

        public SpellCastContext BuildContext()
        {
            Vector3 origin = transform.position;
            origin.y = GetGroundHeight(origin, origin.y);
            return new SpellCastContext
            {
                Caster = this,
                Origin = origin,
                AimDirection = _controller.AimDirection,
                DamageMultiplier = DamageMultiplier,
                AimPoint = _controller.AimPoint,
                HasAimPoint = _controller.HasAimPoint,
                MoveDirection = MoveDirectionNow()
            };
        }

        // Laufrichtung aus der WASD-Eingabe (Welt-XZ), zero ohne Eingabe
        private Vector3 MoveDirectionNow()
        {
            if (_controller == null) return Vector3.zero;
            Vector2 input = _controller.MoveInput;
            return input.sqrMagnitude > 0.04f ? new Vector3(input.x, 0f, input.y).normalized : Vector3.zero;
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

        // ---------------- Schaden (Block etc.) ----------------

        private float ModifyIncomingDamage(float amount, Vector3 sourcePosition, bool hasSource)
        {
            return ActiveKit != null && ActiveKit.isActiveAndEnabled ? ActiveKit.ModifyIncomingDamage(amount, sourcePosition, hasSource) : amount;
        }
    }
}
