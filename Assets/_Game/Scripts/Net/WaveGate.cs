using System;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Server: Systeme, die den Start der nächsten Welle aufhalten dürfen (z. B. Upgrade-Draft, Händlerladen).
    // Registrieren in OnEnable/Start, abmelden in OnDisable/OnDestroy. Der WaveManager startet erst, wenn
    // keine Sperre aktiv ist; der Grund wird in der UI angezeigt ("Warte auf Kartenwahl …").
    public static class WaveGate
    {
        private struct Entry
        {
            public object Owner;
            public Func<bool> IsBlocking;
            public Func<string> Reason;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        public static void Register(object owner, Func<bool> isBlocking, Func<string> reason)
        {
            Unregister(owner);
            _entries.Add(new Entry { Owner = owner, IsBlocking = isBlocking, Reason = reason });
        }

        public static void Unregister(object owner)
        {
            _entries.RemoveAll(e => e.Owner == owner || e.Owner == null || e.Owner.Equals(null));
        }

        public static bool IsBlocked(out string reason)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Owner == null || e.Owner.Equals(null)) { _entries.RemoveAt(i); continue; }
                if (e.IsBlocking != null && e.IsBlocking())
                {
                    reason = e.Reason != null ? e.Reason() : "";
                    return true;
                }
            }
            reason = null;
            return false;
        }

        public static bool IsBlocked() => IsBlocked(out _);
    }
}
