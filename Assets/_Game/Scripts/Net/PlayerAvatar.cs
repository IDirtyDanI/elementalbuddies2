using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace ElementalBuddies
{
    // Netzwerk-Anker der Spielfigur (sitzt auf Player.prefab). Der Server spawnt pro Spieler eine Figur
    // (NetBootstrap) und überträgt sie dem jeweiligen Client als Besitzer.
    // Ersetzt alle "den einen Spieler suchen"-Aufrufe: PlayerAvatar.Local (eigene Figur), PlayerAvatar.All,
    // PlayerAvatar.Nearest(pos) (z. B. Gegner-Zielwahl).
    //
    // Aufgabenteilung:
    // - Besitzer: Eingabe, Bewegung (Owner-NetworkTransform), Abklingzeiten, Mana; schickt Casts an alle (CastRpc).
    // - Server: LP, Tod und Wiederbelebung (NetworkVariables), Treffer (PlayerStats.TakeDamage), Block-Manaabzug.
    // - Alle: führen Casts aus (Optik überall, Schaden nur auf dem Server), Animation fremder Figuren.
    public class PlayerAvatar : NetworkBehaviour
    {
        // Wartezeit bis zur Wiederbelebung (Sekunden), nur wenn noch ein Mitspieler lebt
        public const float DefaultRespawnDelay = 15f;
        public float RespawnDelay => DefaultRespawnDelay;

        public static readonly List<PlayerAvatar> All = new List<PlayerAvatar>();
        public static PlayerAvatar Local { get; private set; }

        public static event Action<PlayerAvatar> OnAvatarSpawned;
        public static event Action<PlayerAvatar> OnAvatarDespawned;
        public static event Action<PlayerAvatar> OnLocalAvatarSpawned;
        // Figur ausgefallen (LP 0) / wiederbelebt – feuert auf allen Rechnern (UI: Mitspieler-Anzeige, Respawn-Countdown)
        public static event Action<PlayerAvatar> OnDowned;
        public static event Action<PlayerAvatar> OnRevived;
        // LP oder Max-LP einer Figur geändert (alle Rechner) – für Mitspieler-Anzeigen
        public static event Action<PlayerAvatar> OnAnyHealthChanged;

        public PlayerStats Stats { get; private set; }
        public PlayerController Controller { get; private set; }
        public PlayerAbilities Abilities { get; private set; }
        public PlayerMana Mana { get; private set; }

        // Zugehöriges Netcode-Spielerobjekt (Name, Champion, Gold)
        public NetPlayer Owner => NetPlayer.ByClientId(OwnerClientId);

        // ---------------- Synchronisierter Zustand ----------------

        private readonly NetworkVariable<float> _health = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _maxHealth = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _downed = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // Server-Zeit (NetworkManager.ServerTime.Time), zu der die Figur wiederbelebt wird
        private readonly NetworkVariable<double> _respawnAt = new NetworkVariable<double>(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // Mana des Besitzers (für Mitspieler-Anzeige und den Schildblock auf dem Server)
        private readonly NetworkVariable<float> _mana = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<float> _maxMana = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private CharacterController _character;
        private NetworkTransform _netTransform;
        private Collider[] _bodyColliders;
        private bool _appliedDowned;
        private NetPlayer _championSource;
        private float _manaPushTimer;

        // Eigene Figur: im Netz der Besitzer, ohne laufendes Netz (z. B. Editor-Tests) immer
        public bool IsLocalControl => IsSpawned ? IsOwner : !Net.IsRunning;
        public bool IsLocal => IsOwner;

        // Ausgefallen (wartet auf Wiederbelebung)
        public bool IsDowned => IsSpawned ? _downed.Value : (Stats != null && Stats.IsDead);
        public virtual bool IsAlive => Stats != null && !IsDowned && (IsSpawned || Stats.CurrentHP > 0f);

        public float Health => IsSpawned ? _health.Value : (Stats != null ? Stats.CurrentHP : 0f);
        public float MaxHealth => IsSpawned ? _maxHealth.Value : (Stats != null ? Stats.MaxHP : 0f);
        public float Health01 => MaxHealth > 0f ? Mathf.Clamp01(Health / MaxHealth) : 0f;

        // Mana dieser Figur (eigene: lokal, fremde: vom Besitzer gemeldet)
        public float CurrentMana => Mana != null ? Mana.CurrentMana : 0f;
        public float MaxMana => IsLocalControl || !IsSpawned ? (Mana != null ? Mana.MaxMana : 0f) : _maxMana.Value;

        // Restzeit bis zur Wiederbelebung (0, wenn nicht ausgefallen)
        public float RespawnRemaining
        {
            get
            {
                if (!IsDowned || !IsSpawned || NetworkManager == null) return 0f;
                return Mathf.Max(0f, (float)(_respawnAt.Value - NetworkManager.ServerTime.Time));
            }
        }

        // Champion dieser Figur: aus dem NetPlayer, ohne Netzwerk aus der lokalen Menü-Wahl
        public ChampionClass Champion
        {
            get
            {
                var np = Owner;
                return np != null ? np.ChampionClass : GameSession.SelectedChampion;
            }
        }

        protected virtual void Awake()
        {
            Stats = GetComponent<PlayerStats>();
            Controller = GetComponent<PlayerController>();
            Abilities = GetComponent<PlayerAbilities>();
            Mana = GetComponent<PlayerMana>();
            if (Mana == null) Mana = gameObject.AddComponent<PlayerMana>();
            _character = GetComponent<CharacterController>();
            _netTransform = GetComponent<NetworkTransform>();
            // Körper-Kollider (ohne CharacterController), werden beim Ausfall abgeschaltet
            var cols = new List<Collider>();
            foreach (var c in GetComponents<Collider>()) if (!(c is CharacterController) && !c.isTrigger) cols.Add(c);
            _bodyColliders = cols.ToArray();
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this)) All.Add(this);
            var np = Owner;
            if (np != null) np.Avatar = this;

            // Besitzer-Erkennung: Eingabe, lokale Instanzen, Bewegung
            if (Controller != null) Controller.SetInputActive(IsOwner);
            if (Abilities != null) Abilities.MarkLocal(IsOwner);
            if (Mana != null) Mana.MarkLocal(IsOwner);
            // Fremde Figuren bewegt der NetworkTransform; der CharacterController würde nur stören
            // (der Körper-Kollider bleibt für Treffer/Physik aktiv)
            // Aus- und wieder einschalten: Netcode setzt die Spawn-Position nach dem Instanziieren; ein bereits aktiver
            // CharacterController hielte sonst seine interne Position (Ursprung) und zöge die Figur dorthin zurück.
            if (_character != null)
            {
                _character.enabled = false;
                _character.enabled = IsOwner && !_downed.Value;
            }

            if (IsServer)
            {
                if (Stats != null)
                {
                    _maxHealth.Value = Stats.MaxHP;
                    _health.Value = Stats.CurrentHP;
                    Stats.OnHealthChanged += ServerPushHealth;
                    Stats.OnPlayerDeath += ServerHandleDeath;
                }
            }
            _health.OnValueChanged += OnHealthValueChanged;
            _maxHealth.OnValueChanged += OnHealthValueChanged;
            _downed.OnValueChanged += OnDownedValueChanged;
            if (!IsServer) SyncStatsFromNet();

            if (IsOwner)
            {
                Local = this;
                if (Controller != null) Controller.Jumped += OnLocalJumped;
                PushMana(true);
            }
            else
            {
                _mana.OnValueChanged += OnManaValueChanged;
                if (Mana != null) Mana.SetRemoteValue(_mana.Value);
            }

            BindChampion();
            ApplyDowned(_downed.Value, false);

            OnAvatarSpawned?.Invoke(this);
            if (IsOwner) OnLocalAvatarSpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            _health.OnValueChanged -= OnHealthValueChanged;
            _maxHealth.OnValueChanged -= OnHealthValueChanged;
            _downed.OnValueChanged -= OnDownedValueChanged;
            _mana.OnValueChanged -= OnManaValueChanged;
            if (Stats != null)
            {
                Stats.OnHealthChanged -= ServerPushHealth;
                Stats.OnPlayerDeath -= ServerHandleDeath;
            }
            if (Controller != null) Controller.Jumped -= OnLocalJumped;
            UnbindChampion();

            All.Remove(this);
            var np = Owner;
            if (np != null && np.Avatar == this) np.Avatar = null;
            if (Local == this)
            {
                Local = null;
                if (Controller != null) Controller.SetInputActive(false);
                if (Abilities != null) Abilities.MarkLocal(false);
                if (Mana != null) Mana.MarkLocal(false);
            }
            OnAvatarDespawned?.Invoke(this);
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            UnbindChampion();
            base.OnDestroy();
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (_championSource == null) BindChampion(); // NetPlayer kam später an

            if (IsServer) ServerUpdate();
            if (IsOwner) PushMana(false);
        }

        // ---------------- Champion ----------------

        private void BindChampion()
        {
            var np = Owner;
            if (np == null)
            {
                // Ohne NetPlayer (sollte nicht vorkommen): Besitzer nimmt die Menü-Wahl
                if (IsOwner && Abilities != null && _championSource == null) Abilities.ApplyNetworkChampion(GameSession.SelectedChampion);
                return;
            }
            if (_championSource != np)
            {
                UnbindChampion();
                _championSource = np;
                np.Avatar = this;
                np.Champion.OnValueChanged += OnChampionValueChanged;
            }
            ApplyChampion();
        }

        // Dev-Cheats (z. B. DevTools.ForceChampion) dürfen gesperrte Champions spielen
        private static bool DevCheatsOn()
        {
            if (NetGame.HostCheatsActive) return true;
            var dev = FindFirstObjectByType<DevTools>();
            return dev != null && dev.CheatsActive;
        }

        private void UnbindChampion()
        {
            if (_championSource != null) _championSource.Champion.OnValueChanged -= OnChampionValueChanged;
            _championSource = null;
        }

        private void OnChampionValueChanged(int oldValue, int newValue) => ApplyChampion();

        private void ApplyChampion()
        {
            if (Abilities == null || _championSource == null) return;
            var cls = _championSource.ChampionClass;
            // Meta-Freischaltung nur beim Besitzer prüfen (die Lobby validiert ohnehin); gesperrt → Magier für alle
            if (IsOwner && !Abilities.ForceChampion && !DevCheatsOn() && !Progression.IsChampionUnlocked(cls, true) && cls != ChampionClass.Mage)
            {
                Debug.Log($"[Net] Champion {cls} ist noch gesperrt – Magier wird gespielt.");
                _championSource.RequestChampion(ChampionClass.Mage);
                cls = ChampionClass.Mage;
            }
            Abilities.ApplyNetworkChampion(cls);
            if (IsDowned) Abilities.SetDownedVisual(true);
        }

        // ---------------- LP / Tod (Server) ----------------

        private void ServerPushHealth()
        {
            if (!IsServer || Stats == null) return;
            if (!Mathf.Approximately(_maxHealth.Value, Stats.MaxHP)) _maxHealth.Value = Stats.MaxHP;
            if (!Mathf.Approximately(_health.Value, Stats.CurrentHP)) _health.Value = Stats.CurrentHP;
            OnAnyHealthChanged?.Invoke(this);
        }

        private void ServerHandleDeath()
        {
            if (!IsServer || _downed.Value) return;
            ServerPushHealth();
            _respawnAt.Value = NetworkManager.ServerTime.Time + RespawnDelay;
            _downed.Value = true;
            ApplyDowned(true, true);
        }

        private void ServerUpdate()
        {
            if (Stats == null) return;
            // Max-LP ändern Karten ohne Event; LP-Abweichungen nachziehen
            if (!Mathf.Approximately(_maxHealth.Value, Stats.MaxHP) || !Mathf.Approximately(_health.Value, Stats.CurrentHP))
                ServerPushHealth();
            if (Stats.IsDead && !_downed.Value) ServerHandleDeath();

            if (_downed.Value && NetworkManager.ServerTime.Time >= _respawnAt.Value)
            {
                bool gameOver = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;
                // Nur wiederbeleben, wenn noch jemand lebt oder der Nexus den Team-Ausfall bezahlt hat (GameManager)
                if (!gameOver && (AnyAlive() || _wipeRevive)) ServerRevive();
            }
        }

        // Server: Team-Ausfall bezahlt → nach delay wiederbeleben, auch wenn niemand lebt
        private bool _wipeRevive;
        public void ServerScheduleWipeRevive(float delay)
        {
            if (!IsServer || !_downed.Value) return;
            _wipeRevive = true;
            _respawnAt.Value = NetworkManager.ServerTime.Time + Mathf.Max(0f, delay);
        }

        // Server: sofort wiederbeleben (volle LP) und an einen Spawnpunkt setzen
        public void ServerRevive()
        {
            if (!IsServer || Stats == null) return;
            _wipeRevive = false;
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            var boot = NetBootstrap.Instance;
            if (boot != null)
            {
                var np = Owner;
                int index = np != null ? Mathf.Max(0, np.Slot) : Mathf.Max(0, All.IndexOf(this));
                boot.GetSpawnPose(index, out pos, out rot);
            }
            Stats.Revive();
            ServerPushHealth();
            _downed.Value = false;
            ApplyDowned(false, true);
            RespawnRpc(pos, rot);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void RespawnRpc(Vector3 position, Quaternion rotation)
        {
            TeleportLocal(position, rotation);
        }

        // ---------------- LP / Tod (alle) ----------------

        private void OnHealthValueChanged(float oldValue, float newValue)
        {
            if (!IsServer) SyncStatsFromNet();
            OnAnyHealthChanged?.Invoke(this);
        }

        private void OnDownedValueChanged(bool oldValue, bool newValue)
        {
            if (!IsServer) SyncStatsFromNet();
            ApplyDowned(newValue, true);
        }

        // Clients: PlayerStats spiegelt die Server-Werte (feuert OnHealthChanged / OnPlayerDeath / OnPlayerRevived)
        private void SyncStatsFromNet()
        {
            if (Stats != null) Stats.ApplyNetworkHealth(_health.Value, _maxHealth.Value, _downed.Value);
        }

        // Ausgefallen: unsichtbar, keine Kollision, keine Steuerung (PlayerController/PlayerAbilities prüfen Stats.IsDead)
        private void ApplyDowned(bool downed, bool raiseEvents)
        {
            bool changed = downed != _appliedDowned;
            _appliedDowned = downed;
            if (Abilities != null) Abilities.SetDownedVisual(downed);
            if (_bodyColliders != null) foreach (var c in _bodyColliders) if (c != null) c.enabled = !downed;
            if (_character != null) _character.enabled = !downed && IsLocalControl;
            if (!changed || !raiseEvents) return;
            if (downed) OnDowned?.Invoke(this);
            else OnRevived?.Invoke(this);
        }

        // ---------------- Bewegung ----------------

        // Besitzer: Figur versetzen ohne Interpolation bei den anderen (Blink, Wiederbelebung)
        public void TeleportLocal(Vector3 position, Quaternion rotation)
        {
            bool cc = _character != null && _character.enabled;
            if (cc) _character.enabled = false;
            if (_netTransform != null && IsSpawned && _netTransform.CanCommitToTransform)
                _netTransform.Teleport(position, rotation, transform.localScale);
            else
                transform.SetPositionAndRotation(position, rotation);
            if (cc) _character.enabled = true;
        }

        private void OnLocalJumped()
        {
            if (IsSpawned && IsOwner) JumpRpc();
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        private void JumpRpc()
        {
            if (Controller != null) Controller.RaiseRemoteJump();
        }

        // Server → Besitzer: Verlangsamung / Rückstoß durch Gegner (PlayerController leitet fremde Figuren hierher)
        public void SendSlowToOwner(float percent, float duration)
        {
            if (IsSpawned && IsServer) SlowRpc(percent, duration);
        }

        public void SendKnockbackToOwner(Vector3 direction, float distance, float duration)
        {
            if (IsSpawned && IsServer) KnockbackRpc(direction, distance, duration);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void SlowRpc(float percent, float duration)
        {
            if (Controller != null) Controller.ApplySlow(percent, duration);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void KnockbackRpc(Vector3 direction, float distance, float duration)
        {
            if (Controller != null) Controller.ApplyKnockback(direction, distance, duration);
        }

        // ---------------- Fähigkeiten ----------------

        // Besitzer: Cast an alle anderen Rechner (Server führt ihn mit Schaden aus, Clients für die Optik)
        public void SendCast(AbilityId id, SpellCastContext ctx)
        {
            if (!IsSpawned || !IsOwner) return;
            CastRpc((int)id, ctx.Origin, ctx.AimDirection, ctx.AimPoint, ctx.HasAimPoint, ctx.DamageMultiplier, ctx.MoveDirection, ctx.Variant);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        private void CastRpc(int abilityId, Vector3 origin, Vector3 aimDirection, Vector3 aimPoint, bool hasAimPoint,
            float damageMultiplier, Vector3 moveDirection, int variant)
        {
            if (Abilities == null || IsDowned) return;
            var ctx = new SpellCastContext
            {
                Caster = Abilities,
                Origin = origin,
                AimDirection = aimDirection,
                AimPoint = aimPoint,
                HasAimPoint = hasAimPoint,
                DamageMultiplier = damageMultiplier,
                MoveDirection = moveDirection,
                Variant = variant,
                IsRemote = true
            };
            Abilities.ExecuteRemoteCast((AbilityId)abilityId, ctx);
        }

        // Besitzer: gehaltene Fähigkeit beendet (Schildblock loslassen)
        public void SendHeld(AbilityId id, bool held)
        {
            if (IsSpawned && IsOwner) HeldRpc((int)id, held);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        private void HeldRpc(int abilityId, bool held)
        {
            if (Abilities != null) Abilities.ExecuteRemoteHeld((AbilityId)abilityId, held);
        }

        // Server: Kit-Ereignis (z. B. Schildbruch, Block-Funken) an alle anderen Rechner
        public void SendKitEvent(int evt, Vector3 point)
        {
            if (IsSpawned && IsServer) KitEventRpc(evt, point);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void KitEventRpc(int evt, Vector3 point)
        {
            var kit = Abilities != null ? Abilities.ActiveKit : null;
            if (kit != null) kit.OnNetEvent(evt, point);
        }

        // ---------------- Mana ----------------

        // Server → Besitzer: Mana abziehen (geblockter Schaden)
        public void SendManaSpendToOwner(float amount)
        {
            if (IsSpawned && IsServer && !IsOwner) ManaSpendRpc(amount);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ManaSpendRpc(float amount)
        {
            if (Mana != null) Mana.SpendFromServer(amount);
        }

        // Besitzer: Mana-Stand melden (gedrosselt; volle/leere Werte und größere Sprünge sofort)
        private void PushMana(bool force)
        {
            if (Mana == null || !IsSpawned || !IsOwner) return;
            float v = Mana.CurrentMana;
            float max = Mana.MaxMana;
            _manaPushTimer -= Time.deltaTime;
            bool edge = (v <= 0f || v >= max) && !Mathf.Approximately(v, _mana.Value);
            if (force || edge || Mathf.Abs(v - _mana.Value) >= 5f || (_manaPushTimer <= 0f && Mathf.Abs(v - _mana.Value) >= 0.5f))
            {
                _mana.Value = v;
                _manaPushTimer = 0.2f;
            }
            if (!Mathf.Approximately(_maxMana.Value, max)) _maxMana.Value = max;
        }

        private void OnManaValueChanged(float oldValue, float newValue)
        {
            if (Mana != null) Mana.SetRemoteValue(newValue);
        }

        // ---------------- Abfragen ----------------

        // Nächste (lebende) Spielfigur zu einer Position, XZ-Abstand. null, wenn keine.
        public static PlayerAvatar Nearest(Vector3 position, bool aliveOnly = true)
        {
            PlayerAvatar best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var a = All[i];
                if (a == null || (aliveOnly && !a.IsAlive)) continue;
                Vector3 d = a.transform.position - position;
                d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = a; }
            }
            return best;
        }

        // Alle (lebenden) Figuren innerhalb eines XZ-Radius
        public static void InRadius(Vector3 position, float radius, List<PlayerAvatar> result, bool aliveOnly = true)
        {
            result.Clear();
            float r2 = radius * radius;
            for (int i = 0; i < All.Count; i++)
            {
                var a = All[i];
                if (a == null || (aliveOnly && !a.IsAlive)) continue;
                Vector3 d = a.transform.position - position;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) result.Add(a);
            }
        }

        public static bool AnyAlive()
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].IsAlive) return true;
            return false;
        }

        public static PlayerAvatar ByClientId(ulong clientId)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].OwnerClientId == clientId) return All[i];
            return null;
        }
    }
}
