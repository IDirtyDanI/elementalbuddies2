using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Händler-Flüche (Plan „Fesselung“ D7): Ab Welle 13 ist gelegentlich eine Karte im Laden verflucht. Sie wirkt
    // doppelt, bringt aber einen Nachteil – nur für den Käufer, nie fürs Team. Nur hinten anhängen (byte, übers Netz).
    public enum MerchantCurse : byte
    {
        None,
        Weakness,  // dein Champion macht bis zum Ende der nächsten Welle 30 % weniger Schaden
        BloodToll, // dein Champion verliert dauerhaft 15 % Max-Leben
        Debt,      // du bekommst die nächsten 3 Wellen kein Gold am Wellenende
    }

    public static class MerchantCurses
    {
        public const int Count = 3;
        public const float WeaknessFactor = 0.7f;
        public const float BloodTollFraction = 0.15f;
        public const int DebtWaves = 3;

        public static string Name(MerchantCurse c)
        {
            switch (c)
            {
                case MerchantCurse.Weakness: return "Schwäche";
                case MerchantCurse.BloodToll: return "Blutzoll";
                case MerchantCurse.Debt: return "Goldschuld";
                default: return "";
            }
        }

        public static string Description(MerchantCurse c)
        {
            switch (c)
            {
                case MerchantCurse.Weakness: return $"{(1f - WeaknessFactor) * 100f:0} % weniger Schaden bis Ende der nächsten Welle";
                case MerchantCurse.BloodToll: return $"dauerhaft {BloodTollFraction * 100f:0} % weniger Max-Leben";
                case MerchantCurse.Debt: return $"die nächsten {DebtWaves} Wellen kein Gold";
                default: return "";
            }
        }

        // Kurzform für die kleine Händlerkarte
        public static string Short(MerchantCurse c)
        {
            switch (c)
            {
                case MerchantCurse.Weakness: return $"−{(1f - WeaknessFactor) * 100f:0} % Schaden nächste Welle";
                case MerchantCurse.BloodToll: return $"−{BloodTollFraction * 100f:0} % Max-Leben";
                case MerchantCurse.Debt: return $"{DebtWaves} Wellen kein Gold";
                default: return "";
            }
        }

        // ---------------- Zustand je Spieler (auf jedem Rechner gleich geführt) ----------------

        private class State
        {
            public int WeakStacks;      // gekaufte Schwäche-Flüche, noch nicht abgelaufen
            public bool WeakArmed;      // eine Welle hat seit dem Kauf begonnen → endet mit ihrem Wellenende
            public int DebtWaves;       // Wellenenden ohne Gold
        }

        private static readonly Dictionary<ulong, State> _states = new Dictionary<ulong, State>();

        // Zählt jede Änderung hoch (Wellenvorschau baut sich danach neu auf)
        public static int Version { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _states.Clear();

        public static void ResetAll() { _states.Clear(); Version++; }

        private static State Get(ulong clientId, bool create)
        {
            if (_states.TryGetValue(clientId, out var s)) return s;
            if (!create) return null;
            s = new State();
            _states[clientId] = s;
            return s;
        }

        // Alle Rechner (MerchantManager.ApplyBoughtCard): Nachteil für den Käufer eintragen bzw. sofort anwenden
        public static void Apply(ulong clientId, MerchantCurse c, PlayerAbilities abilities)
        {
            var s = Get(clientId, true);
            switch (c)
            {
                case MerchantCurse.Weakness:
                    s.WeakStacks++;
                    s.WeakArmed = false; // gilt bis zum Ende der nächsten Welle, die nach dem Kauf beginnt
                    break;
                case MerchantCurse.BloodToll:
                    var stats = abilities != null ? abilities.GetComponent<PlayerStats>() : null;
                    if (stats != null)
                    {
                        stats.MaxHP = Mathf.Max(1f, stats.MaxHP * (1f - BloodTollFraction));
                        if (Net.IsServer) stats.Heal(0f); // LP auf das neue Maximum begrenzen
                    }
                    break;
                case MerchantCurse.Debt:
                    s.DebtWaves += DebtWaves;
                    break;
            }
            if (abilities != null) abilities.CurseDamageFactor = DamageFactor(clientId);
            Version++;
        }

        // Schadensfaktor des Champions (Schwäche, mehrfach gekauft → multipliziert)
        public static float DamageFactor(ulong clientId)
        {
            var s = Get(clientId, false);
            return s == null || s.WeakStacks <= 0 ? 1f : Mathf.Pow(WeaknessFactor, s.WeakStacks);
        }

        // Server: bekommt dieser Spieler am Wellenende Gold?
        public static bool OwesGold(ulong clientId)
        {
            var s = Get(clientId, false);
            return s != null && s.DebtWaves > 0;
        }

        // Alle Rechner, Wellenstart: laufende Schwäche gilt für diese Welle
        public static void HandleWaveStart()
        {
            foreach (var s in _states.Values)
                if (s.WeakStacks > 0) s.WeakArmed = true;
        }

        // Alle Rechner, Wellenende (nach der Gold-Ausschüttung): Schwäche endet, Goldschuld zählt herunter
        public static void HandleWaveEnd()
        {
            Version++;
            foreach (var kv in _states)
            {
                var s = kv.Value;
                if (s.DebtWaves > 0) s.DebtWaves--;
                if (s.WeakStacks > 0 && s.WeakArmed)
                {
                    s.WeakStacks = 0;
                    s.WeakArmed = false;
                    var avatar = PlayerAvatar.ByClientId(kv.Key);
                    var abilities = avatar != null ? avatar.Abilities : (PlayerAvatar.All.Count == 0 ? PlayerAbilities.Instance : null);
                    if (abilities != null) abilities.CurseDamageFactor = 1f;
                    if (kv.Key == Net.LocalClientId) ToastUI.Show("Fluch „Schwäche“ ist vorbei");
                }
            }
        }

        // Anzeige (Wellenvorschau): aktive Flüche des lokalen Spielers, leer wenn keine
        public static string LocalSummary()
        {
            var s = Get(Net.LocalClientId, false);
            if (s == null) return "";
            var parts = new List<string>();
            if (s.WeakStacks > 0) parts.Add($"Schwäche ({(1f - DamageFactor(Net.LocalClientId)) * 100f:0} % weniger Schaden bis Wellenende)");
            if (s.DebtWaves > 0) parts.Add(s.DebtWaves == 1 ? "Goldschuld (noch 1 Welle ohne Gold)" : $"Goldschuld (noch {s.DebtWaves} Wellen ohne Gold)");
            return string.Join(", ", parts);
        }
    }
}
