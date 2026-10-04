using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Plant die Händler: ab FirstWave jede EveryNWaves-te Welle (11, 13, 15, …) öffnet genau ein Händler für diese
    // Welle. Auswahl über einen Beutel ohne Wiederholung unter den verfügbaren Händlern (Element-Händler nur, wenn
    // das Element freigeschaltet ist); ein gescheiterter Händler kommt zurück in den Beutel.
    // Nach der Einnahme: Laden mit 3 Karten (Pause wie der Wellen-Kartenbildschirm). Jede Karte einmal kaufbar, die
    // erste pro Besuch gratis, weitere kosten Seelensplitter (60, 90, 120 …). Neu würfeln ersetzt alle nicht gekauften
    // Karten (25, 40, 55 …). „Fertig" schließt. Trifft der Laden mit dem Upgrade-Bildschirm zusammen, wartet der
    // spätere, bis der erste geschlossen ist (UpgradeManager).
    public class MerchantManager : MonoBehaviour
    {
        public static MerchantManager Instance { get; private set; }

        [Header("Zeitplan")]
        [Tooltip("Erste Händler-Welle (1-basiert).")]
        public int FirstWave = 11;
        [Tooltip("Danach alle N Wellen.")]
        public int EveryNWaves = 2;

        [Header("Karten")]
        [Tooltip("Gesamter Kartenpool (alle Klassen, alle Fähigkeiten) – vom Editor-Setup befüllt.")]
        public List<MerchantCardSO> Cards = new List<MerchantCardSO>();
        public int CardsOffered = 3;

        [Header("Laden (Preise in Seelensplittern)")]
        [Tooltip("Preis der 2. gekauften Karte pro Besuch (die 1. ist gratis).")]
        public int ExtraCardBaseCost = 60;
        [Tooltip("Aufschlag je weiterer gekaufter Karte.")]
        public int ExtraCardCostStep = 30;
        public int RerollBaseCost = 25;
        public int RerollCostStep = 15;

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
        // Kartenauswahl gerade offen (Spiel pausiert)
        public bool IsChoosing { get; private set; }
        public Merchant OfferMerchant { get; private set; }
        public IReadOnlyList<MerchantCardSO> CurrentOffer => _offer;
        // Pro Besuch: gekaufte Karten / Würfe (Reset beim Öffnen)
        public int Purchases { get; private set; }
        public int Rerolls { get; private set; }
        public bool HasTakenFreeCard => Purchases > 0;
        public int NextCardPrice => CardPrice(Purchases);
        public int RerollPrice => RerollBaseCost + RerollCostStep * Rerolls;
        public bool IsBought(int slot) => slot >= 0 && slot < _bought.Count && _bought[slot];
        public bool CanReroll => IsChoosing && HasRerollableSlot();

        private readonly List<MerchantCardSO> _picked = new List<MerchantCardSO>();
        public IReadOnlyList<MerchantCardSO> PickedCards => _picked;

        public event System.Action<Merchant, List<MerchantCardSO>> OnCardsOffered;
        public event System.Action<MerchantCardSO> OnCardPicked;
        public event System.Action OnChoiceClosed;
        // Angebot geändert (Kauf / Neu würfeln) – UI baut Preise und Karten neu
        public event System.Action OnOfferChanged;
        // Kauf/Wurf fehlgeschlagen (zu wenig Splitter): Slot-Index, -1 = Neu-würfeln-Knopf
        public event System.Action<int> OnPurchaseFailed;

        private readonly List<MerchantKind> _bag = new List<MerchantKind>();
        private readonly List<MerchantCardSO> _offer = new List<MerchantCardSO>();
        private readonly List<bool> _bought = new List<bool>();
        private Merchant _pendingOffer; // Einnahme, während der Upgrade-Bildschirm offen war

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
            if (WaveManager.Instance != null) WaveManager.Instance.OnWaveStart += HandleWaveStart;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.OnUpgradeSelected += HandleUpgradeSelected;
            Merchant.OnAnyMerchantCaptured += HandleCaptured;
            Merchant.OnAnyMerchantFailed += HandleFailed;
        }

        void OnDestroy()
        {
            if (WaveManager.Instance != null) WaveManager.Instance.OnWaveStart -= HandleWaveStart;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.OnUpgradeSelected -= HandleUpgradeSelected;
            Merchant.OnAnyMerchantCaptured -= HandleCaptured;
            Merchant.OnAnyMerchantFailed -= HandleFailed;
            if (Instance == this) Instance = null;
        }

        private static bool IsGameOver => GameManager.Instance != null && GameManager.Instance.IsGameOver;

        // ---------------- Zeitplan ----------------

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
        public static bool IsAvailable(MerchantKind kind)
        {
            int e = MerchantInfo.ElementOf(kind);
            if (e < 0) return true;
            var pa = PlayerAbilities.Instance;
            return pa != null && pa.IsUnlocked(e);
        }

        // Beutel-Inhalt (für Tests/Anzeige)
        public IReadOnlyList<MerchantKind> Bag => _bag;

        private void HandleWaveStart()
        {
            if (ActiveMerchant != null && (ActiveMerchant.IsActive || ActiveMerchant.IsCaptured)) return;
            int wave = WaveManager.Instance != null ? WaveManager.Instance.UpcomingWaveNumber : 0;
            if (!IsMerchantWave(wave)) return;
            ActivateNext();
        }

        // Nächsten Händler aus dem Beutel öffnen (auch DevTools). null, wenn keiner verfügbar ist.
        public Merchant ActivateNext()
        {
            if (IsGameOver) return null;
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

        // Bestimmten Händler öffnen (DevTools)
        public bool Activate(Merchant m)
        {
            if (m == null || IsGameOver) return false;
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

        private void HandleFailed(Merchant m)
        {
            if (m == ActiveMerchant) ActiveMerchant = null;
            if (m != null && !_bag.Contains(m.Kind)) _bag.Add(m.Kind); // nächste Händler-Welle wieder möglich
        }

        // ---------------- Kartenauswahl ----------------

        private void HandleCaptured(Merchant m)
        {
            if (IsGameOver || m == null) return;
            var upgrades = UpgradeManager.Instance;
            if (upgrades != null && upgrades.IsChoosing)
            {
                _pendingOffer = m; // nach dem Upgrade-Bildschirm
                return;
            }
            Offer(m);
        }

        private void HandleUpgradeSelected()
        {
            PresentPending();
        }

        // Wartende Kartenauswahl zeigen (nach dem Upgrade-Bildschirm). true, wenn eine geöffnet wurde.
        public bool PresentPending()
        {
            if (_pendingOffer == null) return false;
            var m = _pendingOffer;
            _pendingOffer = null;
            return Offer(m);
        }

        // Preis der Karte nach n bisherigen Käufen in diesem Besuch: 0, 60, 90, 120 …
        public int CardPrice(int purchasesSoFar)
        {
            if (purchasesSoFar <= 0) return 0;
            return ExtraCardBaseCost + ExtraCardCostStep * (purchasesSoFar - 1);
        }

        private List<MerchantCardSO> PoolFor(Merchant m)
        {
            var pa = PlayerAbilities.Instance;
            ChampionClass cls = pa != null ? pa.ActiveClass : ChampionClass.Mage;
            var pool = new List<MerchantCardSO>();
            if (m == null) return pool;
            foreach (var c in Cards)
                if (c != null && c.Class == cls && c.Merchant == m.Kind && !pool.Contains(c)) pool.Add(c);
            return pool;
        }

        private bool Offer(Merchant m)
        {
            if (IsGameOver || m == null) return false;

            _offer.Clear();
            _bought.Clear();
            var pool = PoolFor(m);
            int n = Mathf.Min(Mathf.Max(1, CardsOffered), pool.Count);
            for (int i = 0; i < n; i++)
            {
                int idx = Random.Range(0, pool.Count);
                _offer.Add(pool[idx]);
                _bought.Add(false);
                pool.RemoveAt(idx);
            }
            if (_offer.Count == 0)
            {
                Debug.LogWarning($"MerchantManager: keine Karten für {(PlayerAbilities.Instance != null ? PlayerAbilities.Instance.ActiveClass : ChampionClass.Mage)} beim {m.DisplayName}.");
                return false;
            }

            OfferMerchant = m;
            Purchases = 0;
            Rerolls = 0;
            IsChoosing = true;
            Time.timeScale = 0f;
            OnCardsOffered?.Invoke(m, new List<MerchantCardSO>(_offer));
            return true;
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

        // Karte im Slot kaufen. Erste pro Besuch gratis, sonst Seelensplitter. Der Laden bleibt offen.
        public bool BuyCard(int slot)
        {
            if (!IsChoosing || PauseManager.IsPaused || IsGameOver) return false;
            if (slot < 0 || slot >= _offer.Count || _bought[slot] || _offer[slot] == null) return false;

            int price = NextCardPrice;
            if (price > 0)
            {
                var eco = EconomyManager.Instance;
                if (eco == null || !eco.TrySpendShards(price))
                {
                    OnPurchaseFailed?.Invoke(slot);
                    return false;
                }
            }

            var card = _offer[slot];
            card.Apply(PlayerAbilities.Instance);
            _picked.Add(card);
            _bought[slot] = true;
            Purchases++;
            Debug.Log($"MerchantManager: Karte '{card.Title}' gekauft für {price} ({card.Ability} {card.Stat} {card.Value}).");

            OnCardPicked?.Invoke(card);
            OnOfferChanged?.Invoke();
            return true;
        }

        private bool HasRerollableSlot()
        {
            for (int i = 0; i < _offer.Count; i++)
                if (!_bought[i]) return true;
            return false;
        }

        // Alle nicht gekauften Karten neu ziehen. Gekaufte Karten dürfen wieder erscheinen (mehrfach kaufen = stapeln);
        // unter den offenen Slots keine Doppelten, bisher sichtbare offene Karten möglichst vermeiden.
        public bool Reroll()
        {
            if (!IsChoosing || PauseManager.IsPaused || IsGameOver || OfferMerchant == null) return false;
            if (!HasRerollableSlot()) return false;

            int price = RerollPrice;
            var eco = EconomyManager.Instance;
            if (price > 0 && (eco == null || !eco.TrySpendShards(price)))
            {
                OnPurchaseFailed?.Invoke(-1);
                return false;
            }
            Rerolls++;

            var pool = PoolFor(OfferMerchant);
            // Bisher sichtbare offene Karten möglichst vermeiden (nur wenn genug andere da sind)
            int open = 0;
            for (int i = 0; i < _offer.Count; i++) if (!_bought[i]) open++;
            var fresh = new List<MerchantCardSO>(pool);
            for (int i = 0; i < _offer.Count; i++)
                if (!_bought[i]) fresh.Remove(_offer[i]);
            if (fresh.Count >= open) pool = fresh;

            for (int i = _offer.Count - 1; i >= 0; i--)
            {
                if (_bought[i]) continue;
                if (pool.Count == 0)
                {
                    _offer.RemoveAt(i);
                    _bought.RemoveAt(i);
                    continue;
                }
                int idx = Random.Range(0, pool.Count);
                _offer[i] = pool[idx];
                pool.RemoveAt(idx);
            }

            OnOfferChanged?.Invoke();
            return true;
        }

        // „Fertig": Laden schließen, Spiel weiter, ggf. wartender Wellen-Kartenbildschirm
        public void CloseShop()
        {
            if (!IsChoosing || PauseManager.IsPaused) return;
            IsChoosing = false;
            _offer.Clear();
            _bought.Clear();
            Time.timeScale = 1f;

            OnChoiceClosed?.Invoke();

            if (UpgradeManager.Instance != null) UpgradeManager.Instance.PresentPendingUpgrades();
        }

        private void HandleGameOver(string reason)
        {
            _pendingOffer = null;
            if (!IsChoosing) return;
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
