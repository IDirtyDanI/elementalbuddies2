using System;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Skin-Shop – reine Regeln (ohne Szene, EditMode-getestet in ShopRulesTests):
    // 1) Ausrüstung (Loadout) als ein int fürs Netz: 4 Bit je (Klasse, Slot) = NetIndex 0..15 (0 = Standard).
    //    Bit-Offset = (Klasse × SlotCount + Slot) × 4; Klasse = Enumwert von ChampionClass (Mage 0 … Berserker 3),
    //    Slot = ShopSlot (Weapon 0, Armor 1). 4 Klassen × 2 Slots × 4 Bit = 32 Bit – Klasse 3/Slot 1 belegt das
    //    Vorzeichenbit, darum wird intern mit uint gerechnet.
    // 2) Kaufregel mit Preis/Guthaben; FreeMode (derzeit immer an) = alles gratis.
    // 3) Besitzliste als kommagetrennte Ids (PlayerPrefs „Shop.Owned“).
    public static class ShopRules
    {
        public const int ClassCount = 4;   // ChampionClass-Werte 0..3
        public const int SlotCount = 2;    // ShopSlot: Weapon, Armor
        public const int BitsPerEntry = 4;
        public const int MaxNetIndex = 15; // 0 = Standard, 1..15 = Items

        public static bool ValidEntry(int cls, int slot) => cls >= 0 && cls < ClassCount && slot >= 0 && slot < SlotCount;
        public static bool ValidNetIndex(int netIndex) => netIndex >= 0 && netIndex <= MaxNetIndex;

        private static int Shift(int cls, int slot) => (cls * SlotCount + slot) * BitsPerEntry;

        // NetIndex von (Klasse, Slot); ungültige Klasse/Slot → 0
        public static int Get(int loadout, int cls, int slot)
        {
            if (!ValidEntry(cls, slot)) return 0;
            return (int)(((uint)loadout >> Shift(cls, slot)) & 0xFu);
        }

        // Loadout mit geändertem Eintrag; ungültiger NetIndex → 0 (Standard), ungültige Klasse/Slot → unverändert
        public static int With(int loadout, int cls, int slot, int netIndex)
        {
            if (!ValidEntry(cls, slot)) return loadout;
            if (!ValidNetIndex(netIndex)) netIndex = 0;
            int sh = Shift(cls, slot);
            uint u = (uint)loadout & ~(0xFu << sh);
            u |= (uint)netIndex << sh;
            return unchecked((int)u);
        }

        // indices[Klasse, Slot] → Loadout; Einträge außerhalb ClassCount × SlotCount werden ignoriert, ungültige Werte → 0
        public static int Pack(int[,] indices)
        {
            int loadout = 0;
            if (indices == null) return loadout;
            int nc = Math.Min(indices.GetLength(0), ClassCount);
            int ns = Math.Min(indices.GetLength(1), SlotCount);
            for (int c = 0; c < nc; c++)
                for (int s = 0; s < ns; s++)
                    loadout = With(loadout, c, s, indices[c, s]);
            return loadout;
        }

        // Loadout → [ClassCount, SlotCount]
        public static int[,] Unpack(int loadout)
        {
            var r = new int[ClassCount, SlotCount];
            for (int c = 0; c < ClassCount; c++)
                for (int s = 0; s < SlotCount; s++)
                    r[c, s] = Get(loadout, c, s);
            return r;
        }

        // ---------------- Kauf ----------------

        // Darf das Item erworben werden? FreeMode oder Preis ≤ 0 → immer; sonst Guthaben ≥ Preis
        public static bool CanAcquire(int price, int balance, bool freeMode)
        {
            if (freeMode || price <= 0) return true;
            return balance >= price;
        }

        // Guthaben nach dem Erwerb (gratis → unverändert; nicht leistbar → unverändert)
        public static int BalanceAfter(int price, int balance, bool freeMode)
        {
            if (freeMode || price <= 0) return balance;
            return balance >= price ? balance - price : balance;
        }

        // ---------------- Besitzliste ----------------

        // „a,b,c“ → Ids (Leerzeichen getrimmt, leere/doppelte entfernt, Reihenfolge bleibt)
        public static List<string> ParseIds(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(csv)) return list;
            foreach (var part in csv.Split(','))
            {
                string id = part.Trim();
                if (id.Length > 0 && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        // Ids → „a,b,c“ (Kommas in Ids sind nicht erlaubt und werden übersprungen)
        public static string JoinIds(IEnumerable<string> ids)
        {
            if (ids == null) return "";
            var list = new List<string>();
            foreach (var raw in ids)
            {
                string id = raw?.Trim();
                if (string.IsNullOrEmpty(id) || id.IndexOf(',') >= 0 || list.Contains(id)) continue;
                list.Add(id);
            }
            return string.Join(",", list);
        }
    }
}
