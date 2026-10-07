using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Plant die Händler: ab FirstWave jede EveryNWaves-te Welle (11, 13, 15, …) öffnet genau ein Händler für diese
    // Welle. Auswahl über einen Beutel ohne Wiederholung unter den verfügbaren Händlern (Element-Händler nur, wenn
    // das Element freigeschaltet ist); ein gescheiterter Händler kommt zurück in den Beutel. Plan und Zufall: Server.
    // Nach der Einnahme bekommen die Einnehmenden einen Gold-Bonus (GlobalSettings.MerchantCaptureGoldBonus, geteilt)
    // und JEDER Spieler einen eigenen Laden (eigene 3 Karten, eigene Käufe/Würfe, Bezahlung mit dem eigenen Gold):
    //   Einzelspiel: sofort bei der Einnahme (Pause per Time.timeScale wie bisher).
    //   Koop: am Wellenende, vor dem Upgrade-Draft (keine globale Pause).
    // Jede Karte einmal kaufbar, die erste pro Besuch gratis, weitere kosten Gold (60, 90, 120 …). Neu würfeln ersetzt
    // alle nicht gekauften Karten (25, 40, 55 …). „Fertig" schließt; danach folgt ggf. der wartende Draft.
    // Netz: Server hält den Laden pro Spieler, schickt ihn per NetGame.SendShopState an den Spieler; gekaufte Karten
    // wendet NetGame.BroadcastApplyMerchantCard auf allen Rechnern auf die Figur des Käufers an.
    public class MerchantManager : MonoBehaviour
    {
        public static MerchantManager Instance { get; private set; }

        // Lokaler Laden offen (Eingabesperre für andere Systeme)
        public static bool IsLocalShopOpen => Instance != null && Instance.IsChoosing;

        [Header("Zeitplan")]
        [Tooltip("Erste Händler-Welle (1-basiert).")]
        public int FirstWave = 11;
        [Tooltip("Danach alle N Wellen.")]
        public int EveryNWaves = 2;

        [Header("Karten")]
        [Tooltip("Gesamter Kartenpool (alle Klassen, alle Fähigkeiten) – vom Editor-Setup befüllt. Index = Netz-Id der Karte.")]
        public List<MerchantCardSO> Cards = new List<MerchantCardSO>();
        public int CardsOffered = 3;

        [Header("Laden (Preise in Gold)")]
        [Tooltip("Preis der 2. gekauften Karte pro Besuch (die 1. ist gratis).")]
        public int ExtraCardBaseCost = 60;
        [Tooltip("Aufschlag je weiterer gekaufter Karte.")]
        public int ExtraCardCostStep = 30;
        public int RerollBaseCost = 25;
        public int RerollCostStep = 15;

        [Header("Gold")]
        [Tooltip("Gold-Werte (GoldPerWave, MerchantCaptureGoldBonus, StartGold). Leer → EconomyManager.Settings.")]
        public GlobalSettingsSO Settings;
        [Tooltip("Mindestzeit im Kreis (s), um als Einnehmender zu zählen (wer beim Abschluss drinsteht, zählt immer).")]
        public float MinCaptureContributionSeconds = 2f;

        [Header("Anzeige (optional, Reihenfolge Waffen, Feuer, Eis, Erde, Licht)")]
        public Sprite[] Portraits = new Sprite[MerchantInfo.Count];
        [Tooltip("Kleines Icon für Toast/Ziel/Randpfeil, falls kein Porträt da ist.")]
        public Sprite[] Emblems = new Sprite[MerchantInfo.Count];
        [Tooltip("Plaketten pro AbilityStat (Index = (int)AbilityStat), Fallback, wenn eine Karte keine eigene hat.")]
        public Sprite[] Badges = new Sprite[13];

        [Header("Dev")]
        [Tooltip("Statt Beutel immer diesen Händler öffnen (falls verfügbar).")]
        public bool ForceKind = false;
        public MerchantKind ForcedKind = MerchantKind.Waffen;

        public Merchant ActiveMerchant { get; private set; }

        // ---------------- Lokaler Laden (Abbild des Server-Zustands für die UI) ----------------
        // Laden des lokalen Spielers gerade offen
        public bool IsChoosing { get; private set; }
        public Merchant OfferMerchant { get; private set; }
        public IReadOnlyList<MerchantCardSO> CurrentOffer => _offer;
        // Pro Besuch: gekaufte Karten / Würfe
        public int Purchases { get; private set; }
        public int Rerolls { get; private set; }
        public bool HasTakenFreeCard => Purchases > 0;
        public int NextCardPrice => CardPrice(Purchases);
        public int RerollPrice => GoldRules.RerollPrice(Rerolls, RerollBaseCost, RerollCostStep);
        public bool IsBought(int slot) => slot >= 0 && slot < _bought.Count && _bought[slot];
        public bool CanReroll => IsChoosing && HasRerollableSlot();
        // Gold des lokalen Spielers
        public static float LocalGold => NetPlayer.Local != null ? NetPlayer.Local.Gold.Value : 0f;

        // Gekaufte Karten des lokalen Spielers
        private readonly List<MerchantCardSO> _picked = new List<MerchantCardSO>();
        public IReadOnlyList<MerchantCardSO> PickedCards => _picked;

        public event System.Action<Merchant, List<MerchantCardSO>> OnCardsOffered;
        // Eigene Karte gekauft (lokaler Spieler)
        public event System.Action<MerchantCardSO> OnCardPicked;
        // Irgendein Spieler hat eine Karte gekauft (alle Rechner): Client-Id, Karte
        public event System.Action<ulong, MerchantCardSO> OnAnyCardPicked;
        public event System.Action OnChoiceClosed;
        // Angebot geändert (Kauf / Neu würfeln) – UI baut Preise und Karten neu
        public event System.Action OnOfferChanged;
        // Kauf/Wurf fehlgeschlagen (zu wenig Gold): Slot-Index, -1 = Neu-würfeln-Knopf
        public event System.Action<int> OnPurchaseFailed;

        private readonly List<MerchantCardSO> _offer = new List<MerchantCardSO>();
        private readonly List<bool> _bought = new List<bool>();
        private int _localVisit = -1;
        private int _localClosedVisit = -1;

        // ---------------- Server ----------------
        private readonly List<MerchantKind> _bag = new List<MerchantKind>();

        private class ShopState
        {
            public int Kind;
            public readonly List<int> Offer = new List<int>();
            public readonly List<bool> Bought = new List<bool>();
            public int Purchases;
            public int Rerolls;
            public bool Open;    // Laden liegt beim Spieler
            public bool Pending; // wartet, bis der Draft des Spielers gewählt ist
            public int Visit;
        }
        private readonly Dictionary<ulong, ShopState> _shops = new Dictionary<ulong, ShopState>();
        private int _visitCounter;
        private int _dueKind = -1; // Koop: eingenommener Händler, dessen Laden am Wellenende öffnet

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart += HandleWaveStart;
                WaveManager.Instance.OnWaveEnd += HandleWaveEnd;
            }
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;
            Merchant.OnAnyMerchantActivated += HandleActivated;
            Merchant.OnAnyMerchantCaptured += HandleCaptured;
            Merchant.OnAnyMerchantFailed += HandleFailed;
            NetPlayer.OnPlayerLeft += HandlePlayerLeft;
            WaveGate.Register(this, IsShopBlocking, ShopBlockReason);

            // Startgold für den Run (Server)
            if (Net.IsServer)
            {
                var gs = GoldSettings;
                float start = gs != null ? gs.StartGold : 0f;
                foreach (var np in NetPlayer.All)
                    if (np != null && np.IsSpawned) np.Gold.Value = Mathf.Max(0f, start);
            }
        }

        void OnDestroy()
        {
            WaveGate.Unregister(this);
            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart -= HandleWaveStart;
                WaveManager.Instance.OnWaveEnd -= HandleWaveEnd;
            }
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
            Merchant.OnAnyMerchantActivated -= HandleActivated;
            Merchant.OnAnyMerchantCaptured -= HandleCaptured;
            Merchant.OnAnyMerchantFailed -= HandleFailed;
            NetPlayer.OnPlayerLeft -= HandlePlayerLeft;
            if (Instance == this) Instance = null;
        }

        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.IsGameOver;

        public GlobalSettingsSO GoldSettings
        {
            get
            {
                if (Settings != null) return Settings;
                if (EconomyManager.Instance != null && EconomyManager.Instance.Settings != null) return EconomyManager.Instance.Settings;
                return UpgradeManager.Instance != null ? UpgradeManager.Instance.GlobalSettings : null;
            }
        }

        // ---------------- Zeitplan (Server) ----------------

        public bool IsMerchantWave(int waveNumber)
        {
            return waveNumber >= FirstWave && (waveNumber - FirstWave) % Mathf.Max(1, EveryNWaves) == 0;
        }

        public Merchant GetMerchant(MerchantKind kind)
        {
            foreach (var m in Merchant.All)
                if (m != null && m.Kind == kind) return m;
            return null;
        }

        // Händler verfügbar? Waffenhändler immer, Element-Händler erst nach Freischaltung des Elements
        // (Schreine schalten für das ganze Team frei → irgendeine Figur genügt)
        public static bool IsAvailable(MerchantKind kind)
        {
            int e = MerchantInfo.ElementOf(kind);
            if (e < 0) return true;
            foreach (var a in PlayerAvatar.All)
                if (a != null && a.Abilities != null && a.Abilities.IsUnlocked(e)) return true;
            var pa = PlayerAbilities.Instance;
            return pa != null && pa.IsUnlocked(e);
        }

        // Beutel-Inhalt (für Tests/Anzeige)
        public IReadOnlyList<MerchantKind> Bag => _bag;

        private void HandleWaveStart()
        {
            if (!Net.IsServer) return;
            if (ActiveMerchant != null && (ActiveMerchant.IsActive || ActiveMerchant.IsCaptured)) return;
            int wave = WaveManager.Instance != null ? WaveManager.Instance.UpcomingWaveNumber : 0;
            if (!IsMerchantWave(wave)) return;
            ActivateNext();
        }

        // Nächsten Händler aus dem Beutel öffnen (auch DevTools). null, wenn keiner verfügbar ist. Nur Server.
        public Merchant ActivateNext()
        {
            if (!Net.IsServer || IsGameOver) return null;
            if (ActiveMerchant != null && ActiveMerchant.IsActive) return null;

            Merchant m = null;
            if (ForceKind && IsAvailable(ForcedKind)) m = GetMerchant(ForcedKind);
            if (m == null) m = DrawFromBag();
            if (m == null)
            {
                Debug.LogWarning("MerchantManager: kein verfügbarer Händler in der Szene.");
                return null;
            }
            return Activate(m) ? m : null;
        }

        // Bestimmten Händler öffnen (DevTools). Nur Server.
        public bool Activate(Merchant m)
        {
            if (m == null || IsGameOver || !Net.IsServer) return false;
            if (ActiveMerchant != null && ActiveMerchant.IsActive && ActiveMerchant != m) ActiveMerchant.Fail();
            if (ActiveMerchant != null && ActiveMerchant.IsCaptured && ActiveMerchant != m) ActiveMerchant.Close();
            if (!m.Activate()) return false;
            _bag.Remove(m.Kind);
            ActiveMerchant = m;
            return true;
        }

        private Merchant DrawFromBag()
        {
            var candidates = Candidates();
            if (candidates.Count == 0)
            {
                // Runde vorbei (oder nur noch gesperrte Händler drin) → Beutel neu füllen
                _bag.Clear();
                foreach (var m in Merchant.All)
                    if (m != null && !_bag.Contains(m.Kind)) _bag.Add(m.Kind);
                candidates = Candidates();
            }
            if (candidates.Count == 0) return null;
            return candidates[Random.Range(0, candidates.Count)];
        }

        private List<Merchant> Candidates()
        {
            var list = new List<Merchant>();
            foreach (var kind in _bag)
            {
                if (!IsAvailable(kind)) continue;
                var m = GetMerchant(kind);
                if (m != null && m.State == MerchantState.Geschlossen) list.Add(m);
            }
            return list;
        }

        private void HandleActivated(Merchant m)
        {
            if (m != null) ActiveMerchant = m; // auch auf Clients (Anzeige)
        }

        private void HandleFailed(Merchant m)
        {
            if (m == ActiveMerchant) ActiveMerchant = null;
            if (!Net.IsServer) return;
            if (m != null && !_bag.Contains(m.Kind)) _bag.Add(m.Kind); // nächste Händler-Welle wieder möglich
        }

        // ---------------- Gold (Server) ----------------

        // Wellenende: jeder Spieler bekommt GoldPerWave
        private void HandleWaveEnd()
        {
            if (!Net.IsServer || IsGameOver) return;
            var gs = GoldSettings;
            float amount = gs != null ? gs.GoldPerWave : 15f;
            if (amount <= 0f) return;
            foreach (var np in NetPlayer.All)
                if (np != null) np.AddGold(amount);
        }

        // Einnahme: Bonus geteilt durch die Anzahl der Einnehmenden
        private void PayCaptureBonus(Merchant m)
        {
            var gs = GoldSettings;
            float total = gs != null ? gs.MerchantCaptureGoldBonus : 60f;
            if (total <= 0f) return;
            var ids = new List<ulong>();
            m.GetCapturers(ids, MinCaptureContributionSeconds);
            if (ids.Count == 0) ids.AddRange(UpgradeManager.PlayerIds()); // z. B. DevTools-Einnahme
            float share = GoldRules.SplitCaptureBonus(total, ids.Count);
            foreach (ulong id in ids)
            {
                var np = NetPlayer.ByClientId(id);
                if (np == null) continue;
                np.AddGold(share);
                NetGame.SendToast(id, $"+{Mathf.FloorToInt(share)} Gold (Händler-Bonus)");
            }
        }

        // ---------------- Laden: Server ----------------

        private void HandleCaptured(Merchant m)
        {
            if (!Net.IsServer || IsGameOver || m == null) return;
            PayCaptureBonus(m);
            if (Net.CanPauseTime)
            {
                // Einzelspiel: Laden sofort (pausiert wie bisher)
                ServerOpenShopForAll((int)m.Kind);
            }
            else
            {
                // Koop: keine Pause mitten in der Welle → Laden für alle am Wellenende, vor dem Draft
                _dueKind = (int)m.Kind;
                foreach (ulong id in UpgradeManager.PlayerIds())
                    NetGame.SendToast(id, "Der Laden öffnet am Wellenende für alle.");
            }
        }

        // Wellenende (vom UpgradeManager vor dem Draft gerufen): fälligen Laden für alle öffnen
        public void ServerOpenDueShops()
        {
            if (!Net.IsServer || _dueKind < 0) return;
            int kind = _dueKind;
            _dueKind = -1;
            if (IsGameOver) return;
            ServerOpenShopForAll(kind);
        }

        private void ServerOpenShopForAll(int kind)
        {
            foreach (ulong id in UpgradeManager.PlayerIds()) ServerOpenShop(id, kind);
        }

        // Laden für einen Spieler öffnen (oder vormerken, solange dessen Draft offen ist)
        public void ServerOpenShop(ulong clientId, int kind)
        {
            if (!Net.IsServer || IsGameOver) return;
            var st = GetShop(clientId);
            st.Kind = kind;
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.ServerIsDrafting(clientId))
            {
                st.Pending = true;
                return;
            }
            st.Pending = false;

            st.Offer.Clear();
            st.Bought.Clear();
            var pool = PoolFor((MerchantKind)kind, ClassOf(clientId));
            int n = Mathf.Min(Mathf.Max(1, CardsOffered), pool.Count);
            for (int i = 0; i < n; i++)
            {
                int idx = Random.Range(0, pool.Count);
                st.Offer.Add(pool[idx]);
                st.Bought.Add(false);
                pool.RemoveAt(idx);
            }
            if (st.Offer.Count == 0)
            {
                Debug.LogWarning($"MerchantManager: keine Karten für {ClassOf(clientId)} beim {MerchantInfo.Name((MerchantKind)kind)}.");
                st.Open = false;
                if (UpgradeManager.Instance != null) UpgradeManager.Instance.ServerOnShopClosed(clientId);
                return;
            }

            st.Purchases = 0;
            st.Rerolls = 0;
            st.Open = true;
            st.Visit = ++_visitCounter;
            SendShop(clientId, st);
        }

        // Draft des Spielers gewählt → wartenden Laden öffnen
        public void ServerOnDraftFinished(ulong clientId)
        {
            if (!Net.IsServer) return;
            if (_shops.TryGetValue(clientId, out var st) && st.Pending) ServerOpenShop(clientId, st.Kind);
        }

        // Laden des Spielers offen? (Draft wartet so lange)
        public bool ServerIsShopBusy(ulong clientId) => _shops.TryGetValue(clientId, out var st) && st.Open;

        public void ServerBuy(ulong clientId, int visit, int slot)
        {
            if (!Net.IsServer || IsGameOver) return;
            if (!_shops.TryGetValue(clientId, out var st) || !st.Open || st.Visit != visit) return;
            if (slot < 0 || slot >= st.Offer.Count || st.Bought[slot]) return;
            int cardIndex = st.Offer[slot];
            if (GetCard(cardIndex) == null) return;

            int price = CardPrice(st.Purchases);
            if (price > 0)
            {
                var np = NetPlayer.ByClientId(clientId);
                if (np == null || !np.TrySpendGold(price))
                {
                    NetGame.SendPurchaseFailed(clientId, slot);
                    return;
                }
            }

            st.Bought[slot] = true;
            st.Purchases++;
            NetGame.BroadcastApplyMerchantCard(clientId, cardIndex);
            SendShop(clientId, st);
        }

        // Alle nicht gekauften Karten neu ziehen. Gekaufte Karten dürfen wieder erscheinen (mehrfach kaufen = stapeln);
        // unter den offenen Slots keine Doppelten, bisher sichtbare offene Karten möglichst vermeiden.
        public void ServerReroll(ulong clientId, int visit)
        {
            if (!Net.IsServer || IsGameOver) return;
            if (!_shops.TryGetValue(clientId, out var st) || !st.Open || st.Visit != visit) return;
            if (!st.Bought.Contains(false)) return;

            int price = GoldRules.RerollPrice(st.Rerolls, RerollBaseCost, RerollCostStep);
            if (price > 0)
            {
                var np = NetPlayer.ByClientId(clientId);
                if (np == null || !np.TrySpendGold(price))
                {
                    NetGame.SendPurchaseFailed(clientId, -1);
                    return;
                }
            }
            st.Rerolls++;

            var pool = PoolFor((MerchantKind)st.Kind, ClassOf(clientId));
            int open = 0;
            for (int i = 0; i < st.Offer.Count; i++) if (!st.Bought[i]) open++;
            var fresh = new List<int>(pool);
            for (int i = 0; i < st.Offer.Count; i++)
                if (!st.Bought[i]) fresh.Remove(st.Offer[i]);
            if (fresh.Count >= open) pool = fresh;

            for (int i = st.Offer.Count - 1; i >= 0; i--)
            {
                if (st.Bought[i]) continue;
                if (pool.Count == 0)
                {
                    st.Offer.RemoveAt(i);
                    st.Bought.RemoveAt(i);
                    continue;
                }
                int idx = Random.Range(0, pool.Count);
                st.Offer[i] = pool[idx];
                pool.RemoveAt(idx);
            }
            SendShop(clientId, st);
        }

        public void ServerCloseShop(ulong clientId, int visit)
        {
            if (!Net.IsServer) return;
            if (!_shops.TryGetValue(clientId, out var st) || !st.Open || st.Visit != visit) return;
            st.Open = false;
            SendShop(clientId, st);
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.ServerOnShopClosed(clientId);
        }

        private void SendShop(ulong clientId, ShopState st)
        {
            NetGame.SendShopState(clientId, st.Visit, st.Kind, st.Offer.ToArray(), st.Bought.ToArray(), st.Purchases, st.Rerolls, st.Open);
        }

        private ShopState GetShop(ulong clientId)
        {
            if (!_shops.TryGetValue(clientId, out var st))
            {
                st = new ShopState();
                _shops[clientId] = st;
            }
            return st;
        }

        private int CountWaiting(out int total)
        {
            var ids = UpgradeManager.PlayerIds();
            total = ids.Count;
            int waiting = 0;
            foreach (ulong id in ids)
                if (_shops.TryGetValue(id, out var st) && (st.Open || st.Pending)) waiting++;
            return waiting;
        }

        private bool IsShopBlocking()
        {
            if (!Net.IsServer || IsGameOver) return false;
            return CountWaiting(out _) > 0;
        }

        private string ShopBlockReason()
        {
            int waiting = CountWaiting(out int total);
            return GoldRules.WaitReason("Händler-Einkauf", total - waiting, total);
        }

        private void HandlePlayerLeft(NetPlayer np)
        {
            if (np != null) _shops.Remove(np.OwnerClientId);
        }

        // Champion-Klasse eines Spielers (für den Kartenpool)
        private static ChampionClass ClassOf(ulong clientId)
        {
            var a = PlayerAvatar.ByClientId(clientId);
            if (a != null)
            {
                if (a.Abilities != null && a.Abilities.ActiveKit != null) return a.Abilities.ActiveClass;
                return a.Champion;
            }
            var pa = PlayerAbilities.Instance;
            return pa != null ? pa.ActiveClass : ChampionClass.Mage;
        }

        // Kartenindizes (in Cards) für Händler kind und Klasse cls
        private List<int> PoolFor(MerchantKind kind, ChampionClass cls)
        {
            var pool = new List<int>();
            var seen = new HashSet<MerchantCardSO>();
            for (int i = 0; i < Cards.Count; i++)
            {
                var c = Cards[i];
                if (c != null && c.Class == cls && c.Merchant == kind && seen.Add(c)) pool.Add(i);
            }
            return pool;
        }

        public MerchantCardSO GetCard(int index) => index >= 0 && index < Cards.Count ? Cards[index] : null;

        // Preis der Karte nach n bisherigen Käufen in diesem Besuch: 0, 60, 90, 120 … (Gold)
        public int CardPrice(int purchasesSoFar) => GoldRules.CardPrice(purchasesSoFar, ExtraCardBaseCost, ExtraCardCostStep);

        // ---------------- Laden: Client (lokaler Spieler) ----------------

        // Ladenzustand vom Server
        public void ClientReceiveShop(int visit, int kind, int[] offer, bool[] bought, int purchases, int rerolls, bool open)
        {
            if (!open)
            {
                if (IsChoosing && visit == _localVisit) CloseLocal();
                return;
            }
            if (visit == _localClosedVisit || IsGameOver) return; // lokal schon geschlossen

            _offer.Clear();
            _bought.Clear();
            if (offer != null)
            {
                for (int i = 0; i < offer.Length; i++)
                {
                    var c = GetCard(offer[i]);
                    if (c == null) continue;
                    _offer.Add(c);
                    _bought.Add(bought != null && i < bought.Length && bought[i]);
                }
            }
            Purchases = purchases;
            Rerolls = rerolls;
            OfferMerchant = GetMerchant((MerchantKind)kind);

            bool first = !IsChoosing || _localVisit != visit;
            _localVisit = visit;
            if (first)
            {
                IsChoosing = true;
                if (Net.CanPauseTime) Time.timeScale = 0f; // Einzelspiel: Pause wie bisher
                OnCardsOffered?.Invoke(OfferMerchant, new List<MerchantCardSO>(_offer));
            }
            else
            {
                OnOfferChanged?.Invoke();
            }
        }

        public void ClientPurchaseFailed(int slot)
        {
            OnPurchaseFailed?.Invoke(slot);
        }

        // Alte API (Karte statt Slot) – kauft den ersten noch offenen Slot mit dieser Karte
        public void SelectCard(MerchantCardSO card)
        {
            if (card == null) return;
            for (int i = 0; i < _offer.Count; i++)
                if (_offer[i] == card && !_bought[i])
                {
                    BuyCard(i);
                    return;
                }
        }

        // Karte im Slot kaufen (Anfrage an den Server). Erste pro Besuch gratis, sonst Gold. Der Laden bleibt offen.
        // true = Anfrage gesendet (das Ergebnis kommt per OnOfferChanged / OnPurchaseFailed).
        public bool BuyCard(int slot)
        {
            if (!IsChoosing || PauseManager.IsPaused || IsGameOver) return false;
            if (slot < 0 || slot >= _offer.Count || _bought[slot] || _offer[slot] == null) return false;

            int price = NextCardPrice;
            if (price > 0 && LocalGold + 0.001f < price)
            {
                OnPurchaseFailed?.Invoke(slot);
                return false;
            }
            NetGame.RequestBuyCard(_localVisit, slot);
            return true;
        }

        private bool HasRerollableSlot()
        {
            for (int i = 0; i < _offer.Count; i++)
                if (!_bought[i]) return true;
            return false;
        }

        // Alle nicht gekauften Karten neu ziehen lassen (Anfrage an den Server)
        public bool Reroll()
        {
            if (!IsChoosing || PauseManager.IsPaused || IsGameOver || OfferMerchant == null) return false;
            if (!HasRerollableSlot()) return false;

            int price = RerollPrice;
            if (price > 0 && LocalGold + 0.001f < price)
            {
                OnPurchaseFailed?.Invoke(-1);
                return false;
            }
            NetGame.RequestReroll(_localVisit);
            return true;
        }

        // „Fertig": Laden schließen; danach öffnet der Server ggf. den wartenden Draft
        public void CloseShop()
        {
            if (!IsChoosing || PauseManager.IsPaused) return;
            int visit = _localVisit;
            CloseLocal();
            NetGame.RequestCloseShop(visit);
        }

        private void CloseLocal()
        {
            _localClosedVisit = _localVisit;
            IsChoosing = false;
            _offer.Clear();
            _bought.Clear();
            // Einzelspiel: weiter, sofern nicht noch die Kartenwahl offen ist
            if (Net.CanPauseTime && !UpgradeManager.IsLocalChoosing) Time.timeScale = 1f;
            OnChoiceClosed?.Invoke();
        }

        // Alle Rechner: gekaufte Karte auf die Figur des Käufers anwenden (AbilityMods pro Figur)
        public void ApplyBoughtCard(ulong clientId, int cardIndex)
        {
            var card = GetCard(cardIndex);
            if (card == null) return;
            var avatar = PlayerAvatar.ByClientId(clientId);
            PlayerAbilities abilities = avatar != null ? avatar.Abilities : null;
            if (abilities == null && PlayerAvatar.All.Count == 0 && clientId == Net.LocalClientId) abilities = PlayerAbilities.Instance;
            card.Apply(abilities);
            Debug.Log($"MerchantManager: Spieler {clientId} kauft '{card.Title}' ({card.Ability} {card.Stat} {card.Value}).");

            bool local = clientId == Net.LocalClientId;
            if (local) _picked.Add(card);
            OnAnyCardPicked?.Invoke(clientId, card);
            if (local) OnCardPicked?.Invoke(card);
        }

        private void HandleGameOver(string reason)
        {
            _dueKind = -1;
            _shops.Clear();
            if (!IsChoosing) return;
            _localClosedVisit = _localVisit;
            IsChoosing = false;
            _offer.Clear();
            _bought.Clear();
            OnChoiceClosed?.Invoke();
        }

        // ---------------- Anzeige-Helfer ----------------

        public Sprite GetPortrait(MerchantKind k)
        {
            int i = (int)k;
            return Portraits != null && i < Portraits.Length ? Portraits[i] : null;
        }

        public Sprite GetEmblem(MerchantKind k)
        {
            int i = (int)k;
            Sprite s = Emblems != null && i < Emblems.Length ? Emblems[i] : null;
            return s != null ? s : GetPortrait(k);
        }

        public Sprite GetBadge(MerchantCardSO card)
        {
            if (card == null) return null;
            if (card.Badge != null) return card.Badge;
            int i = (int)card.Stat;
            return Badges != null && i < Badges.Length ? Badges[i] : null;
        }

        // Gekaufte Karten zusammengefasst (Reihenfolge der ersten Wahl)
        public List<KeyValuePair<MerchantCardSO, int>> GetPickedSummary()
        {
            var order = new List<MerchantCardSO>();
            var counts = new Dictionary<MerchantCardSO, int>();
            foreach (var c in _picked)
            {
                if (c == null) continue;
                if (counts.TryGetValue(c, out int n)) counts[c] = n + 1;
                else
                {
                    counts[c] = 1;
                    order.Add(c);
                }
            }
            var list = new List<KeyValuePair<MerchantCardSO, int>>();
            foreach (var c in order) list.Add(new KeyValuePair<MerchantCardSO, int>(c, counts[c]));
            return list;
        }

        // "Schwerthieb-Reichweite: 2.6 → 3.1 m" – Wert jetzt und nach (zusätzlichem) Wert value.
        // Rechnet über die echten Kit-Werte: Modifikator kurz hinzufügen, ablesen, wieder abziehen.
        public static bool TryPreview(MerchantCardSO card, float extraValue, out string label, out float before, out float after, out string unit)
        {
            label = "";
            unit = "";
            before = after = 0f;
            var pa = PlayerAbilities.Instance;
            if (card == null || pa == null) return false;
            var kit = pa.ActiveKit; // nur das aktive Kit kennt seinen Owner (Modifikatoren)
            if (kit == null || kit.Class != card.Class) return false;
            if (!kit.TryGetStatValue(card.Ability, card.Stat, out before, out unit, out label)) return false;
            pa.Mods.AddSilently(card.Ability, card.Stat, extraValue);
            kit.TryGetStatValue(card.Ability, card.Stat, out after, out unit, out label);
            pa.Mods.AddSilently(card.Ability, card.Stat, -extraValue);
            return true;
        }

        // Zahl im deutschen Format (Komma), höchstens 2 Nachkommastellen
        public static string FormatNumber(float v)
        {
            return ChampionKit.Fmt(v).Replace('.', ',');
        }

        // Kurzform für die Karte: "2,6 m → 3,1 m" (Name und Wert stehen schon in Titel/Beschreibung)
        public static string PreviewValues(MerchantCardSO card)
        {
            if (!TryPreview(card, card.Value, out string label, out float before, out float after, out string unit)) return "";
            return $"{FormatNumber(before)}{unit} → <b>{FormatNumber(after)}{unit}</b>";
        }

        public static string PreviewLine(MerchantCardSO card)
        {
            if (!TryPreview(card, card.Value, out string label, out float before, out float after, out string unit)) return "";
            var pa = PlayerAbilities.Instance;
            string ability = pa != null && pa.ActiveKit != null ? pa.ActiveKit.GetName(card.Ability) : card.Ability.ToString();
            return $"{ability} – {label}: {FormatNumber(before)}{unit} → <b>{FormatNumber(after)}{unit}</b>";
        }
    }
}
