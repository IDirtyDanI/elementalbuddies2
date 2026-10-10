using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Sonderwirkungen der Wellenkarten (Plan „Fesselung“ D3/D4): Schlüsselwort-Synergien und Build-Karten mit Zielkonflikt.
    // Team-Zustand für den ganzen Run, auf JEDEM Rechner gleich gepflegt (UpgradeManager.ApplyPicked → Add). Mehrfach
    // gewählte Karten addieren ihre Werte. Die Spielwirkung (Schaden, Status, Splitter) entscheidet nur der Server;
    // Clients brauchen die Werte für Anzeigen (Buddy-Info, Pause-Übersicht).
    public static class CardEffects
    {
        private static readonly Dictionary<CardEffect, float> _sum = new Dictionary<CardEffect, float>();
        private static readonly Dictionary<CardEffect, float> _sum2 = new Dictionary<CardEffect, float>();
        private static readonly Dictionary<CardEffect, int> _count = new Dictionary<CardEffect, int>();

        public static event System.Action OnChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            _sum.Clear();
            _sum2.Clear();
            _count.Clear();
            _purityFrame = -1;
            _shatterDepth = 0;
        }

        public static float Value(CardEffect e) => _sum.TryGetValue(e, out float v) ? v : 0f;
        public static float Value2(CardEffect e) => _sum2.TryGetValue(e, out float v) ? v : 0f;
        public static int Count(CardEffect e) => _count.TryGetValue(e, out int n) ? n : 0;
        public static bool Has(CardEffect e) => Count(e) > 0;

        // Karte auf diesem Rechner eintragen (einmal pro Wahl)
        public static void Add(UpgradeDefinitionSO card)
        {
            if (card == null || card.Effect == CardEffect.None) return;
            var e = card.Effect;
            float oldHp = BuddyHealthFactor;
            _sum[e] = Value(e) + card.Value;
            _sum2[e] = Value2(e) + card.Value2;
            _count[e] = Count(e) + 1;

            if (e == CardEffect.Loner && Net.IsServer && BuddySlotManager.Instance != null)
                BuddySlotManager.Instance.AddSlot(-Mathf.Max(1, Mathf.RoundToInt(card.Value2)));

            float newHp = BuddyHealthFactor;
            if (!Mathf.Approximately(oldHp, newHp) && Net.IsServer)
                foreach (var b in ElementalBuddy.Active)
                    if (b != null) b.RescaleHealth(newHp / oldHp);
            OnChanged?.Invoke();
        }

        // ---------------- Buddy-Werte ----------------

        // Leben aller Buddies (Steinhaut-Karte, Glaskanone), mindestens 30 %
        public static float BuddyHealthFactor =>
            Mathf.Max(0.3f, 1f + (Value(CardEffect.BuddyHealth) - Value2(CardEffect.GlassCannon)) / 100f);

        // Schaden eines Buddys aus den Build-Karten (additiv): Glaskanone, Einzelgänger, Elementar-Harmonie
        public static float BuddyDamageFactor(ElementalBuddy b)
        {
            float pct = Value(CardEffect.GlassCannon) + Value(CardEffect.Loner);
            if (Has(CardEffect.Harmony)) pct += Value(CardEffect.Harmony) * BuiltElementCount;
            return Mathf.Max(0.1f, 1f + pct / 100f);
        }

        // Anzahl verschiedener Basis-Elemente unter den aktiven Buddies (Fusionen zählen mit ihrem ersten Element).
        // Auf allen Rechnern gleich (gleiche Buddies); pro Frame gecacht.
        private static int _purityFrame = -1, _elementCount;
        public static int BuiltElementCount
        {
            get
            {
                if (_purityFrame == Time.frameCount) return _elementCount;
                _purityFrame = Time.frameCount;
                var seen = new bool[4];
                _elementCount = 0;
                foreach (var b in ElementalBuddy.Active)
                    if (b != null && !b.IsDead && b.ElementIndex >= 0 && b.ElementIndex < 4 && !seen[b.ElementIndex])
                    {
                        seen[b.ElementIndex] = true;
                        _elementCount++;
                    }
                return _elementCount;
            }
        }

        // Build-Karten (Episch, Glaskanone/Einzelgänger/Harmonie): höchstens eine pro Run und Spieler
        public static bool IsBuildCard(UpgradeDefinitionSO up) =>
            up != null && (up.Effect == CardEffect.GlassCannon || up.Effect == CardEffect.Loner || up.Effect == CardEffect.Harmony);

        public static float WaveEndHealBonus => Value(CardEffect.WaveEndHeal) / 100f;

        // ---------------- Schadens-Synergien (Server, EnemyBrain.TakeDamage) ----------------

        // Zusatzfaktor auf einen Treffer gegen enemy; source = austeilender Buddy (null: Champion, DoT, Rest)
        public static float DamageTakenFactor(EnemyBrain enemy, ElementalBuddy source)
        {
            if (_sum.Count == 0 || enemy == null) return 1f;
            float pct = 0f;
            if (Has(CardEffect.TauntVulnerable) && enemy.IsTaunted) pct += Value(CardEffect.TauntVulnerable);
            if (Has(CardEffect.WetVulnerable) && enemy.IsWet) pct += Value(CardEffect.WetVulnerable);
            if (source != null)
            {
                if (Has(CardEffect.SteamShock) && !source.IsFusion && source.ElementIndex == 1 && enemy.IsBurning)
                    pct += Value(CardEffect.SteamShock);
                if (Has(CardEffect.SlowVulnerable) && (enemy.IsSlowed || enemy.IsFrozen))
                    pct += Value(CardEffect.SlowVulnerable);
            }
            return 1f + pct / 100f;
        }

        // ---------------- Status-Auslöser ----------------

        // Glutgeschosse: Brand-DPS als Anteil des Treffers
        public static float IgniteFraction => Value(CardEffect.FireIgnite) / 100f;
        public static float IgniteDuration => Count(CardEffect.FireIgnite) > 0 ? Value2(CardEffect.FireIgnite) / Count(CardEffect.FireIgnite) : 0f;

        // Raureif: jeder N. Schuss friert ein; weitere Karten senken N um 1 (mindestens 2)
        public static int FreezeEvery
        {
            get
            {
                int n = Count(CardEffect.IceFreeze);
                if (n == 0) return 0;
                int baseN = Mathf.RoundToInt(Value(CardEffect.IceFreeze) / n);
                return Mathf.Max(2, baseN - (n - 1));
            }
        }
        public static float FreezeDuration => Count(CardEffect.IceFreeze) > 0 ? Value2(CardEffect.IceFreeze) / Count(CardEffect.IceFreeze) : 0f;

        public static float EarthSlow => Mathf.Clamp01(Value(CardEffect.EarthSlow) / 100f);

        public static float LightCurseBonus => Value(CardEffect.LightCurse) / 100f;
        public static float LightCurseDuration => Count(CardEffect.LightCurse) > 0 ? Value2(CardEffect.LightCurse) / Count(CardEffect.LightCurse) : 0f;

        // ---------------- Splitterfrost ----------------

        private static int _shatterDepth;
        public static event System.Action<Vector3, float> OnShatter; // Position, Radius (Optik, nur Server)

        // Server: eingefrorener Gegner ist gestorben → Flächenschaden (Kettenreaktion höchstens 3 tief)
        public static void Shatter(EnemyBrain dead)
        {
            if (!Net.IsServer || dead == null || !Has(CardEffect.FrostShatter) || _shatterDepth >= 3) return;
            float radius = Value2(CardEffect.FrostShatter) / Count(CardEffect.FrostShatter);
            float damage = dead.MaxHP * Value(CardEffect.FrostShatter) / 100f;
            Vector3 pos = dead.transform.position;
            _shatterDepth++;
            try
            {
                foreach (var e in CombatUtil.FindEnemies(pos, radius))
                    if (e != null && e != dead && !e.IsDead) e.TakeDamage(damage);
            }
            finally { _shatterDepth--; }
            OnShatter?.Invoke(pos, radius);
        }

        // Gesamtwirkung einer Sonderwirkung für die Pause-Übersicht (Werte aller gewählten Karten)
        public static string Summary(CardEffect e)
        {
            float v = Value(e), v2 = Value2(e);
            switch (e)
            {
                case CardEffect.BuddyHealth: return $"Buddy-Leben +{v:0} %";
                case CardEffect.WaveEndHeal: return $"Wellenende-Heilung +{v:0} %";
                case CardEffect.FireIgnite: return $"Brand {v:0} % des Treffers/s für {IgniteDuration:0.#} s";
                case CardEffect.SteamShock: return $"+{v:0} % Eis-Schaden gegen Brennende";
                case CardEffect.IceFreeze: return $"jeder {FreezeEvery}. Eis-Schuss friert {FreezeDuration:0.#} s ein";
                case CardEffect.FrostShatter: return $"Zerspringen: {v:0} % des Lebens";
                case CardEffect.TauntVulnerable: return $"+{v:0} % Schaden gegen Verspottete";
                case CardEffect.EarthSlow: return $"Erd-Aura verlangsamt um {EarthSlow * 100f:0} %";
                case CardEffect.SlowVulnerable: return $"+{v:0} % Schaden gegen Verlangsamte";
                case CardEffect.LightCurse: return $"Fluch +{v:0} % für {LightCurseDuration:0.#} s";
                case CardEffect.WetVulnerable: return $"+{v:0} % Schaden gegen Nasse";
                case CardEffect.EliteBounty: return $"Elite-/Boss-Splitter +{v:0} %";
                case CardEffect.Interest: return $"Zinsen {v:0} % (max. {v2:0})";
                case CardEffect.GlassCannon: return $"Buddies +{v:0} % Schaden, −{v2:0} % Leben";
                case CardEffect.Loner: return $"Buddies +{v:0} % Schaden, −{v2:0} Slots";
                case CardEffect.Harmony: return $"Buddies +{v * BuiltElementCount:0} % Schaden ({BuiltElementCount} Elemente)";
                default: return "";
            }
        }

        // ---------------- Wirtschaft ----------------

        public static float EliteBountyFactor => 1f + Value(CardEffect.EliteBounty) / 100f;

        // Zinsen am Wellenende für den aktuellen Kontostand
        public static int InterestFor(float shards)
        {
            if (!Has(CardEffect.Interest)) return 0;
            float cap = Value2(CardEffect.Interest);
            return Mathf.FloorToInt(Mathf.Min(cap, Mathf.Max(0f, shards) * Value(CardEffect.Interest) / 100f));
        }
    }
}
