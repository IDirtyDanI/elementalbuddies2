using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Upgrade-Draft am Wellenende – pro Spieler:
    //   Server (WaveManager.EndWave → PresentUpgrades) zieht für JEDEN Spieler eigene Karten (eigene Pick-Zählung/MaxPicks)
    //   und schickt sie gezielt an dessen Rechner (NetGame.SendUpgradeOffer). Der Client zeigt die Auswahl
    //   (OnUpgradesAvailable → UpgradeScreenUI), wählt (SelectUpgrade → NetGame.RequestSelectUpgrade), der Server prüft
    //   und lässt die Karte auf ALLEN Rechnern anwenden (NetGame.BroadcastApplyUpgrade → ApplyPicked).
    //   Spieler-Karten wirken auf die Figur des Wählenden, Team-Karten (Buddy-Werte, Slots, Splitter) einmal global.
    // Die nächste Welle startet erst, wenn alle gewählt haben (WaveGate). Im Einzelspiel pausiert die Auswahl weiter
    // per Time.timeScale (Net.CanPauseTime).
    public class UpgradeManager : MonoBehaviour
    {
        public static UpgradeManager Instance { get; private set; }

        // Lokales Kartenfenster offen (Eingabesperre für andere Systeme)
        public static bool IsLocalChoosing => Instance != null && Instance.IsChoosing;

        public const int CardsPerDraft = 3;

        [Header("Data")]
        public List<UpgradeDefinitionSO> AllUpgrades;

        [Header("References")]
        [Tooltip("Veraltet: Spieler-Karten wirken auf die Figur des Wählenden (PlayerAvatar). Nur Fallback ohne Netzwerk-Figur.")]
        public PlayerStats PlayerStatsRef;
        [Tooltip("Veraltet: siehe PlayerStatsRef.")]
        public PlayerController PlayerControllerRef;
        public InteractionManager InteractionRef;
        public GlobalSettingsSO GlobalSettings;

        // UI-Events (lokal)
        public event System.Action<List<UpgradeDefinitionSO>> OnUpgradesAvailable;
        // Lokale Auswahl geschlossen (gewählt oder abgebrochen)
        public event System.Action OnUpgradeSelected;
        // Eigene Karte angewendet (lokaler Spieler)
        public event System.Action<UpgradeDefinitionSO> OnUpgradePicked;
        // Irgendein Spieler hat eine Karte bekommen (alle Rechner): Client-Id, Karte
        public event System.Action<ulong, UpgradeDefinitionSO> OnAnyUpgradePicked;

        // Bisher gewählte Karten des lokalen Spielers (in Wahl-Reihenfolge, Mehrfachwahl möglich)
        private readonly List<UpgradeDefinitionSO> _picked = new List<UpgradeDefinitionSO>();
        public IReadOnlyList<UpgradeDefinitionSO> PickedUpgrades => _picked;
        // Gewählte Karten aller Spieler (auf allen Rechnern gleich, da per ApplyPicked gepflegt)
        private readonly Dictionary<ulong, List<UpgradeDefinitionSO>> _pickedByClient = new Dictionary<ulong, List<UpgradeDefinitionSO>>();

        // Lokale Upgrade-Auswahl gerade offen
        public bool IsChoosing { get; private set; }
        private int _localSerial = -1;      // Nummer der offenen Auswahl
        private int _localClosedSerial = -1; // zuletzt lokal geschlossene Auswahl (verspätete Pakete ignorieren)
        private readonly List<UpgradeDefinitionSO> _localOffer = new List<UpgradeDefinitionSO>();

        // ---------------- Server: Draft-Zustand pro Spieler ----------------
        private class DraftState
        {
            public readonly List<int> Offer = new List<int>();
            public bool Open;    // Auswahl liegt beim Spieler
            public bool Pending; // wartet, bis der Händlerladen des Spielers zu ist
            public int Serial;
        }
        private readonly Dictionary<ulong, DraftState> _drafts = new Dictionary<ulong, DraftState>();
        private int _serialCounter;

        // ---------------- Basiswerte pro Figur ----------------
        private class PlayerBase
        {
            public float Speed, MaxHP, Damage = 1f, Cooldown = 1f, Mobility = 1f;
        }
        private readonly Dictionary<PlayerAvatar, PlayerBase> _bases = new Dictionary<PlayerAvatar, PlayerBase>();

        // Startwerte der lokalen Figur vor allen Karten (für die Pause-Übersicht)
        public float BasePlayerSpeed => LocalBase(b => b.Speed, c => c.MoveSpeed, null, null);
        public float BasePlayerMaxHP => LocalBase(b => b.MaxHP, null, s => s.MaxHP, null);
        public float BaseDamageMultiplier => LocalBase(b => b.Damage, null, null, a => a.DamageMultiplier, 1f);
        public float BaseCooldownMultiplier => LocalBase(b => b.Cooldown, null, null, a => a.CooldownMultiplier, 1f);
        public float BaseMobilityMultiplier => LocalBase(b => b.Mobility, null, null, a => a.MobilityMultiplier, 1f);

        // Backup for UnitConfigs
        private struct UnitBackup
        {
            public UnitConfigSO Config;
            public float Damage;
            public float Range;
            public float FireRate;
        }
        private List<UnitBackup> _backups = new List<UnitBackup>();

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
             if (InteractionRef == null) InteractionRef = FindFirstObjectByType<InteractionManager>();
             if (GlobalSettings == null) Debug.LogWarning("UpgradeManager: Please assign GlobalSettings in Inspector!");

             // Create Backups
             if (InteractionRef != null)
             {
                 foreach (var config in InteractionRef.UnitConfigs)
                 {
                     if (config != null)
                     {
                         _backups.Add(new UnitBackup
                         {
                             Config = config,
                             Damage = config.Damage,
                             Range = config.Range,
                             FireRate = config.FireRate
                         });
                     }
                 }
             }

             if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;
             NetPlayer.OnPlayerLeft += HandlePlayerLeft;
             PlayerAvatar.OnAvatarDespawned += HandleAvatarDespawned;
             WaveGate.Register(this, IsDraftBlocking, DraftBlockReason);
        }

        void OnDestroy()
        {
            WaveGate.Unregister(this);
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
            NetPlayer.OnPlayerLeft -= HandlePlayerLeft;
            PlayerAvatar.OnAvatarDespawned -= HandleAvatarDespawned;
            if (Instance == this) Instance = null;

            // Restore Backups
            foreach (var backup in _backups)
            {
                if (backup.Config != null)
                {
                    backup.Config.Damage = backup.Damage;
                    backup.Config.Range = backup.Range;
                    backup.Config.FireRate = backup.FireRate;
                }
            }
        }

        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.IsGameOver;

        // ======================= Server: Draft =======================

        // Wellenende (Server): zuerst fällige Händlerläden öffnen, dann für jeden Spieler den Draft (wartet ggf. auf den Laden)
        public void PresentUpgrades()
        {
            if (!Net.IsServer || IsGameOver) return;
            if (MerchantManager.Instance != null) MerchantManager.Instance.ServerOpenDueShops();
            foreach (ulong id in PlayerIds()) ServerBeginDraft(id);
        }

        // Alte API: wartender Draft (nach dem Händlerladen) – jetzt pro Spieler über ServerOnShopClosed
        public bool PresentPendingUpgrades()
        {
            if (!Net.IsServer) return false;
            bool any = false;
            foreach (ulong id in PlayerIds())
                if (ServerOnShopClosed(id)) any = true;
            return any;
        }

        // Server: Draft für einen Spieler starten (oder vormerken, solange dessen Laden offen ist)
        public void ServerBeginDraft(ulong clientId)
        {
            if (!Net.IsServer || IsGameOver) return;
            var st = GetDraft(clientId);
            if (MerchantManager.Instance != null && MerchantManager.Instance.ServerIsShopBusy(clientId))
            {
                st.Pending = true;
                return;
            }
            st.Pending = false;

            var selection = GetRandomUpgrades(CardsPerDraft, clientId);
            st.Offer.Clear();
            foreach (var up in selection)
            {
                int idx = IndexOfUpgrade(up);
                if (idx >= 0) st.Offer.Add(idx);
            }
            if (st.Offer.Count == 0)
            {
                // Pool leer → keine Auswahl; ggf. wartender Laden darf öffnen
                st.Open = false;
                if (MerchantManager.Instance != null) MerchantManager.Instance.ServerOnDraftFinished(clientId);
                return;
            }
            st.Open = true;
            st.Serial = ++_serialCounter;
            NetGame.SendUpgradeOffer(clientId, st.Serial, st.Offer.ToArray());
        }

        // Server: Laden des Spielers geschlossen → wartenden Draft zeigen. true, wenn einer wartete.
        public bool ServerOnShopClosed(ulong clientId)
        {
            if (!Net.IsServer) return false;
            if (!_drafts.TryGetValue(clientId, out var st) || !st.Pending) return false;
            st.Pending = false;
            if (IsGameOver) return false;
            ServerBeginDraft(clientId);
            return true;
        }

        // Server: Wahl eines Spielers prüfen und auf allen Rechnern anwenden
        public void ServerSelect(ulong clientId, int serial, int cardIndex)
        {
            if (!Net.IsServer) return;
            if (!_drafts.TryGetValue(clientId, out var st) || !st.Open || st.Serial != serial) return;
            if (IsGameOver) { st.Open = false; return; }
            if (!st.Offer.Contains(cardIndex))
            {
                // ungültig → Auswahl erneut schicken
                NetGame.SendUpgradeOffer(clientId, st.Serial, st.Offer.ToArray());
                return;
            }
            st.Open = false;
            st.Offer.Clear();
            NetGame.BroadcastApplyUpgrade(clientId, cardIndex);
            if (MerchantManager.Instance != null) MerchantManager.Instance.ServerOnDraftFinished(clientId);
        }

        // Server: hat dieser Spieler noch eine offene oder wartende Auswahl?
        public bool ServerIsDrafting(ulong clientId)
        {
            return _drafts.TryGetValue(clientId, out var st) && st.Open;
        }

        private DraftState GetDraft(ulong clientId)
        {
            if (!_drafts.TryGetValue(clientId, out var st))
            {
                st = new DraftState();
                _drafts[clientId] = st;
            }
            return st;
        }

        // Alle Spieler der Partie (ohne Netzwerk: nur der lokale)
        public static List<ulong> PlayerIds()
        {
            var ids = new List<ulong>();
            foreach (var np in NetPlayer.All)
                if (np != null && !ids.Contains(np.OwnerClientId)) ids.Add(np.OwnerClientId);
            if (ids.Count == 0) ids.Add(Net.LocalClientId);
            return ids;
        }

        private int CountWaiting(out int total)
        {
            var ids = PlayerIds();
            total = ids.Count;
            int waiting = 0;
            foreach (ulong id in ids)
                if (_drafts.TryGetValue(id, out var st) && (st.Open || st.Pending)) waiting++;
            return waiting;
        }

        private bool IsDraftBlocking()
        {
            if (!Net.IsServer || IsGameOver) return false;
            return CountWaiting(out _) > 0;
        }

        private string DraftBlockReason()
        {
            int waiting = CountWaiting(out int total);
            return GoldRules.WaitReason("Kartenwahl", total - waiting, total);
        }

        private void HandlePlayerLeft(NetPlayer np)
        {
            if (np == null) return;
            _drafts.Remove(np.OwnerClientId);
        }

        private void HandleAvatarDespawned(PlayerAvatar a)
        {
            if (a != null) _bases.Remove(a);
        }

        private void HandleGameOver(string reason)
        {
            _drafts.Clear();
            if (!IsChoosing) return;
            CloseLocal();
        }

        // ======================= Client: Auswahl =======================

        // Server-Angebot empfangen → Auswahl zeigen
        public void ClientReceiveOffer(int serial, int[] cards)
        {
            if (IsGameOver || cards == null) return;
            if (serial == _localClosedSerial) return; // bereits gewählt
            _localOffer.Clear();
            foreach (int idx in cards)
            {
                var up = GetUpgrade(idx);
                if (up != null) _localOffer.Add(up);
            }
            if (_localOffer.Count == 0) return;

            _localSerial = serial;
            if (Net.CanPauseTime) Time.timeScale = 0f; // Einzelspieler: Spiel pausiert wie bisher
            IsChoosing = true;
            OnUpgradesAvailable?.Invoke(new List<UpgradeDefinitionSO>(_localOffer));
        }

        // Von der UI (UpgradeCardUI): Karte wählen
        public void SelectUpgrade(UpgradeDefinitionSO upgrade)
        {
            // Pause-Menü liegt darüber -> keine Wahl (defensiv)
            if (PauseManager.IsPaused || upgrade == null || !IsChoosing) return;
            if (!_localOffer.Contains(upgrade)) return;
            int idx = IndexOfUpgrade(upgrade);
            if (idx < 0) return;

            int serial = _localSerial;
            CloseLocal();
            NetGame.RequestSelectUpgrade(serial, idx);
        }

        private void CloseLocal()
        {
            _localClosedSerial = _localSerial;
            IsChoosing = false;
            _localOffer.Clear();
            // Einzelspieler: weiter, sofern nicht noch der Händlerladen offen ist
            if (Net.CanPauseTime && !MerchantManager.IsLocalShopOpen) Time.timeScale = 1f;
            OnUpgradeSelected?.Invoke();
        }

        // ======================= Alle Rechner: Anwenden =======================

        // Karte cardIndex für Spieler clientId anwenden (auf jedem Rechner genau einmal)
        public void ApplyPicked(ulong clientId, int cardIndex)
        {
            var upgrade = GetUpgrade(cardIndex);
            if (upgrade == null) return;

            if (!_pickedByClient.TryGetValue(clientId, out var list))
            {
                list = new List<UpgradeDefinitionSO>();
                _pickedByClient[clientId] = list;
            }
            list.Add(upgrade);

            ApplyUpgrade(upgrade, ResolveAvatar(clientId), clientId);

            bool local = clientId == Net.LocalClientId;
            if (local) _picked.Add(upgrade);
            OnAnyUpgradePicked?.Invoke(clientId, upgrade);
            if (local) OnUpgradePicked?.Invoke(upgrade);
        }

        public IReadOnlyList<UpgradeDefinitionSO> GetPickedUpgrades(ulong clientId)
        {
            return _pickedByClient.TryGetValue(clientId, out var list) ? list : (IReadOnlyList<UpgradeDefinitionSO>)System.Array.Empty<UpgradeDefinitionSO>();
        }

        private static PlayerAvatar ResolveAvatar(ulong clientId)
        {
            var a = PlayerAvatar.ByClientId(clientId);
            if (a == null)
            {
                var np = NetPlayer.ByClientId(clientId);
                if (np != null) a = np.Avatar;
            }
            return a;
        }

        // ======================= Kartenziehung =======================

        public int IndexOfUpgrade(UpgradeDefinitionSO up) => up != null && AllUpgrades != null ? AllUpgrades.IndexOf(up) : -1;

        public UpgradeDefinitionSO GetUpgrade(int index) =>
            AllUpgrades != null && index >= 0 && index < AllUpgrades.Count ? AllUpgrades[index] : null;

        // count zufällige Karten ohne Doppelte für den lokalen Spieler
        public List<UpgradeDefinitionSO> GetRandomUpgrades(int count) => GetRandomUpgrades(count, Net.LocalClientId);

        // count zufällige Karten ohne Doppelte für einen Spieler; Slot-Karten fallen raus, sobald die Slot-Obergrenze
        // erreicht ist; MaxPicks zählt pro Spieler. Zufall nur auf dem Server.
        public List<UpgradeDefinitionSO> GetRandomUpgrades(int count, ulong clientId)
        {
            var pool = new List<UpgradeDefinitionSO>();
            if (AllUpgrades != null)
            {
                foreach (var up in AllUpgrades)
                {
                    if (up == null || pool.Contains(up)) continue;
                    if (!IsDraftable(up, clientId)) continue;
                    pool.Add(up);
                }
            }

            var picked = new List<UpgradeDefinitionSO>();
            while (picked.Count < count && pool.Count > 0)
            {
                int idx = Random.Range(0, pool.Count);
                picked.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return picked;
        }

        // Für den lokalen Spieler ziehbar?
        public static bool IsDraftable(UpgradeDefinitionSO up) => IsDraftable(up, Net.LocalClientId);

        public static bool IsDraftable(UpgradeDefinitionSO up, ulong clientId)
        {
            if (up == null) return false;
            var slots = BuddySlotManager.Instance;
            if (up.StatToBuff == StatType.BuddySlot && slots != null && slots.IsAtCap) return false;
            // Deckel pro Run und Spieler (z. B. Beschwörerband höchstens 6×)
            if (up.MaxPicks > 0 && Instance != null && Instance.GetPickCount(up, clientId) >= up.MaxPicks) return false;
            return true;
        }

        // Wie oft der lokale Spieler diese Karte in diesem Run schon gewählt hat
        public int GetPickCount(UpgradeDefinitionSO up) => GetPickCount(up, Net.LocalClientId);

        public int GetPickCount(UpgradeDefinitionSO up, ulong clientId)
        {
            int n = 0;
            if (_pickedByClient.TryGetValue(clientId, out var list))
                foreach (var p in list)
                    if (p == up) n++;
            return n;
        }

        // Unit-Werte vor allen Karten (aus dem Backup)
        public bool TryGetBaseUnitStats(UnitConfigSO cfg, out float damage, out float range, out float fireRate)
        {
            foreach (var b in _backups)
            {
                if (b.Config == cfg)
                {
                    damage = b.Damage;
                    range = b.Range;
                    fireRate = b.FireRate;
                    return true;
                }
            }
            damage = range = fireRate = 0f;
            return false;
        }

        // ======================= Wirkung =======================

        // Ziel-Komponenten eines Spielers (Figur; ohne Netzwerk-Figur Fallback auf die lokale Szene)
        private struct PlayerTarget
        {
            public PlayerAvatar Avatar;
            public PlayerStats Stats;
            public PlayerController Controller;
            public PlayerAbilities Abilities;
        }

        private PlayerTarget ResolveTarget(PlayerAvatar avatar, ulong clientId)
        {
            var t = new PlayerTarget { Avatar = avatar };
            if (avatar != null)
            {
                t.Stats = avatar.Stats;
                t.Controller = avatar.Controller;
                t.Abilities = avatar.Abilities;
            }
            else if (PlayerAvatar.All.Count == 0 && clientId == Net.LocalClientId)
            {
                // Fallback ohne Netzwerk-Figur (alte Szene / Tests)
                t.Abilities = PlayerAbilities.Instance;
                t.Stats = PlayerStatsRef != null ? PlayerStatsRef : FindFirstObjectByType<PlayerStats>();
                t.Controller = PlayerControllerRef != null ? PlayerControllerRef : FindFirstObjectByType<PlayerController>();
            }
            return t;
        }

        private PlayerBase GetBase(PlayerTarget t)
        {
            if (t.Avatar != null && _bases.TryGetValue(t.Avatar, out var b)) return b;
            b = new PlayerBase
            {
                Speed = t.Controller != null ? t.Controller.MoveSpeed : 0f,
                MaxHP = t.Stats != null ? t.Stats.MaxHP : 0f,
                Damage = t.Abilities != null ? t.Abilities.DamageMultiplier : 1f,
                Cooldown = t.Abilities != null ? t.Abilities.CooldownMultiplier : 1f,
                Mobility = t.Abilities != null ? t.Abilities.MobilityMultiplier : 1f,
            };
            if (t.Avatar != null) _bases[t.Avatar] = b;
            return b;
        }

        // Basiswert der lokalen Figur; ohne gemerkten Basiswert (noch keine Karte) der aktuelle Wert
        private float LocalBase(System.Func<PlayerBase, float> fromBase, System.Func<PlayerController, float> fromController,
            System.Func<PlayerStats, float> fromStats, System.Func<PlayerAbilities, float> fromAbilities, float fallback = 0f)
        {
            var local = PlayerAvatar.Local;
            if (local != null && _bases.TryGetValue(local, out var b)) return fromBase(b);
            var t = ResolveTarget(local, Net.LocalClientId);
            if (fromController != null && t.Controller != null) return fromController(t.Controller);
            if (fromStats != null && t.Stats != null) return fromStats(t.Stats);
            if (fromAbilities != null && t.Abilities != null) return fromAbilities(t.Abilities);
            return fallback;
        }

        private void ApplyUpgrade(UpgradeDefinitionSO upgrade, PlayerAvatar avatar, ulong clientId)
        {
            Debug.Log($"Applying Upgrade: {upgrade.Title} (Spieler {clientId})");
            var target = ResolveTarget(avatar, clientId);

            switch (upgrade.Type)
            {
                case UpgradeType.StatIncrease:
                    ApplyStatUpgrade(upgrade, target);
                    break;
                case UpgradeType.Heal:
                    // Heilung ist HP-Zustand → nur der Server
                    if (Net.IsServer && target.Stats != null) target.Stats.Heal(upgrade.Value);
                    break;
                case UpgradeType.ManaBoost:
                    ApplyManaUpgrade(upgrade, target);
                    break;
            }
        }

        private void ApplyStatUpgrade(UpgradeDefinitionSO upgrade, PlayerTarget t)
        {
            if (upgrade.StatToBuff == StatType.BuddySlot)
            {
                ApplySlotUpgrade(upgrade);
                return;
            }
            if (upgrade.StatToBuff == StatType.ShardGain)
            {
                ApplyShardGain(upgrade);
                return;
            }

            if (upgrade.Target == UpgradeTarget.Player)
            {
                // Basiswerte dieser Figur merken, bevor die erste Karte sie verändert
                var b = GetBase(t);
                if (upgrade.StatToBuff == StatType.Speed && t.Controller != null)
                {
                    t.Controller.MoveSpeed = ModifyValue(t.Controller.MoveSpeed, b.Speed, upgrade);
                }
                else if (upgrade.StatToBuff == StatType.Health && t.Stats != null)
                {
                    // MaxHP auf allen Rechnern gleich; die HP selbst verwaltet der Server
                    t.Stats.MaxHP = ModifyValue(t.Stats.MaxHP, b.MaxHP, upgrade);
                    if (Net.IsServer) t.Stats.Heal(0);
                }
                else if (upgrade.StatToBuff == StatType.Damage && t.Abilities != null)
                {
                    // Spieler-Schaden skaliert alle Fähigkeiten des aktiven Champions. Prozent additiv gegen den Basis-
                    // Multiplikator (Arkane Wucht +25 % → +0,25 je Karte), flache Werte zählen als ganze Prozent
                    var pa = t.Abilities;
                    pa.DamageMultiplier = upgrade.IsPercentage
                        ? ModifyValue(pa.DamageMultiplier, b.Damage, upgrade)
                        : pa.DamageMultiplier + upgrade.Value / 100f;
                }
                else if (upgrade.StatToBuff == StatType.Cooldown && t.Abilities != null)
                {
                    // Abklingzeit-Karte: Wert = Prozent schneller (20 = −20 % Abklingzeit), wirkt auf alle Fähigkeiten
                    var pa = t.Abilities;
                    float pct = Mathf.Clamp(upgrade.Value, 0f, 90f) / 100f;
                    pa.CooldownMultiplier = Mathf.Max(0.2f, pa.CooldownMultiplier * (1f - pct));
                }
                else if (upgrade.StatToBuff == StatType.Mobility && t.Abilities != null)
                {
                    // Mobilitäts-Karte: Blink-Reichweite bzw. Rollen-Distanz (Prozent oder flach in Prozentpunkten)
                    var pa = t.Abilities;
                    pa.MobilityMultiplier = upgrade.IsPercentage
                        ? ModifyValue(pa.MobilityMultiplier, b.Mobility, upgrade)
                        : pa.MobilityMultiplier + upgrade.Value / 100f;
                }
            }
            else
            {
                // Team-Karte: Buddy-Werte (geteilte UnitConfigSOs) – auf jedem Rechner einmal
                if (InteractionRef == null) return;
                var allConfigs = InteractionRef.UnitConfigs;

                foreach (var conf in allConfigs)
                {
                    if (conf != null && MatchesTarget(conf.Type, upgrade.Target))
                    {
                        ApplyToConfig(conf, upgrade);
                    }
                }
            }
        }

        private void ApplyToConfig(UnitConfigSO config, UpgradeDefinitionSO upgrade)
        {
            float oldValue = 0f;
            float newValue = 0f;
            // Basiswerte vor allen Karten (Backup); ohne Backup gilt der aktuelle Wert als Basis
            bool hasBase = TryGetBaseUnitStats(config, out float baseDamage, out float baseRange, out float baseFireRate);

            switch (upgrade.StatToBuff)
            {
                case StatType.Damage:
                    oldValue = config.Damage;
                    config.Damage = ModifyValue(config.Damage, hasBase ? baseDamage : config.Damage, upgrade);
                    newValue = config.Damage;
                    break;
                case StatType.Range:
                    oldValue = config.Range;
                    config.Range = ModifyValue(config.Range, hasBase ? baseRange : config.Range, upgrade);
                    newValue = config.Range;
                    break;
                case StatType.FireRate:
                    oldValue = config.FireRate;
                    config.FireRate = ModifyValue(config.FireRate, hasBase ? baseFireRate : config.FireRate, upgrade);
                    newValue = config.FireRate;
                    break;
            }
            Debug.Log($"UpgradeManager: Modified {config.name} {upgrade.StatToBuff} from {oldValue} to {newValue}");
        }

        private bool MatchesTarget(UnitType unitType, UpgradeTarget target)
        {
            if (target == UpgradeTarget.AllUnits) return true;
            if (target == UpgradeTarget.FireUnit && unitType == UnitType.Fire) return true;
            if (target == UpgradeTarget.IceUnit && unitType == UnitType.Ice) return true;
            if (target == UpgradeTarget.EarthUnit && unitType == UnitType.Earth) return true;
            if (target == UpgradeTarget.LightUnit && unitType == UnitType.Light) return true;
            return false;
        }

        // Prozentwerte sind ganze Prozent (10 = +10 %, 100 = +100 %) und stapeln ADDITIV gegen den Basiswert vor allen
        // Karten: Basis × (1 + Σ%) – kein Zinseszins (Balancing-Methode (e)). Flache Werte: + Value.
        private static float ModifyValue(float current, float baseValue, UpgradeDefinitionSO upgrade)
        {
            if (upgrade.IsPercentage)
                return current + baseValue * upgrade.Value / 100f;
            else
                return current + upgrade.Value;
        }

        // Mana ist pro Spieler (PlayerMana auf der Figur): Obergrenze flach, Regeneration in Prozent
        private void ApplyManaUpgrade(UpgradeDefinitionSO upgrade, PlayerTarget t)
        {
            if (upgrade.StatToBuff == StatType.BuddySlot)
            {
                ApplySlotUpgrade(upgrade);
                return;
            }

            if (upgrade.StatToBuff == StatType.ShardGain)
            {
                ApplyShardGain(upgrade);
                return;
            }

            var mana = t.Avatar != null ? t.Avatar.GetComponent<PlayerMana>() : null;
            if (mana == null && t.Abilities != null) mana = t.Abilities.GetComponent<PlayerMana>();
            if (mana == null)
            {
                Debug.LogWarning($"UpgradeManager: keine PlayerMana auf der Figur – Karte '{upgrade.Title}' ohne Wirkung.");
                return;
            }

            float baseCap = GlobalSettings != null ? GlobalSettings.ManaCap : 200f;
            float baseRegen = GlobalSettings != null ? GlobalSettings.RegenOutCombat : 1.5f;
            if (upgrade.StatToBuff == StatType.ManaCap)
            {
                float bonus = upgrade.IsPercentage ? baseCap * upgrade.Value / 100f : upgrade.Value;
                mana.AddCapBonus(bonus);
            }
            else if (upgrade.StatToBuff == StatType.ManaRegen)
            {
                float percent = upgrade.IsPercentage ? upgrade.Value : (baseRegen > 0f ? upgrade.Value / baseRegen * 100f : 0f);
                mana.AddRegenBonusPercent(percent);
            }
        }

        // Seelenernte: Wert = Prozent mehr Splitter pro Kill-Drop (additiv, 15 = +15 %). Teamkasse → nur der Server.
        private void ApplyShardGain(UpgradeDefinitionSO upgrade)
        {
            if (!Net.IsServer) return;
            if (EconomyManager.Instance != null) EconomyManager.Instance.AddShardGainPercent(upgrade.Value);
        }

        // Buddy-Slots sind Team-Zustand → nur der Server (Anzeige über den Sync der Wirtschaft)
        private void ApplySlotUpgrade(UpgradeDefinitionSO upgrade)
        {
            if (!Net.IsServer) return;
            if (BuddySlotManager.Instance == null) return;
            int amount = Mathf.Max(1, Mathf.RoundToInt(upgrade.Value));
            BuddySlotManager.Instance.AddSlot(amount);
        }
    }
}
