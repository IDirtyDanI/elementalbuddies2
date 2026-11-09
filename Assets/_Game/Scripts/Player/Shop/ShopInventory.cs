using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Besitz und Ausrüstung der Shop-Items (lokal, PlayerPrefs):
    //   „Shop.Owned“ = kommagetrennte Ids, „Shop.Equip.<Klasse>.<Slot>“ = Id des ausgerüsteten Items (fehlt = Standard).
    // Standard-Items sind immer besessen. Acquire ist derzeit gratis (FreeMode); die spätere Währung (Edelsteine) wird
    // nur hier über ShopRules.CanAcquire/BalanceAfter eingehängt. Netz: LocalLoadout → NetPlayer.Loadout.
    public static class ShopInventory
    {
        public const string OwnedKey = "Shop.Owned";
        public const string EquipPrefix = "Shop.Equip.";

        // Alles gratis (bis die Währung kommt)
        public static bool FreeMode = true;

        // Platzhalter für die künftige Währung (Edelsteine); derzeit immer 0, nichts wird gespeichert
        public static int Gems = 0;

        // Besitz/Ausrüstung geändert (Shop-UI, Menü-Bühne, NetPlayer)
        public static event Action Changed;

        private static readonly List<string> _owned = new List<string>();
        private static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _owned.Clear();
            _loaded = false;
            Changed = null;
            FreeMode = true;
            Gems = 0;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _owned.Clear();
            _owned.AddRange(ShopRules.ParseIds(PlayerPrefs.GetString(OwnedKey, "")));
        }

        public static string EquipKey(ChampionClass cls, ShopSlot slot) => EquipPrefix + cls + "." + slot;

        public static bool IsOwned(ShopItemSO item)
        {
            if (item == null) return false;
            if (item.IsStandard) return true;
            EnsureLoaded();
            return !string.IsNullOrEmpty(item.Id) && _owned.Contains(item.Id);
        }

        public static bool CanAcquire(ShopItemSO item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) return false;
            return IsOwned(item) || ShopRules.CanAcquire(item.Price, Gems, FreeMode);
        }

        // Item erwerben (derzeit gratis). true = besessen (auch wenn schon vorher)
        public static bool Acquire(ShopItemSO item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) return false;
            if (IsOwned(item)) return true;
            if (!ShopRules.CanAcquire(item.Price, Gems, FreeMode)) return false;
            Gems = ShopRules.BalanceAfter(item.Price, Gems, FreeMode);
            _owned.Add(item.Id);
            PlayerPrefs.SetString(OwnedKey, ShopRules.JoinIds(_owned));
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        // Ausrüsten (nur besessene Items; Standard-Item = Unequip). true = ausgerüstet
        public static bool Equip(ShopItemSO item)
        {
            if (item == null || !IsOwned(item)) return false;
            if (item.IsStandard)
            {
                Unequip(item.Class, item.Slot);
                return true;
            }
            string key = EquipKey(item.Class, item.Slot);
            if (PlayerPrefs.GetString(key, "") == item.Id) return true;
            PlayerPrefs.SetString(key, item.Id);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        // Zurück zur Standardausrüstung
        public static void Unequip(ChampionClass cls, ShopSlot slot)
        {
            string key = EquipKey(cls, slot);
            if (!PlayerPrefs.HasKey(key)) return;
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // Ausgerüstetes Item oder null (Standard; auch wenn die Id nicht mehr im Katalog steht oder nicht besessen ist)
        public static ShopItemSO GetEquipped(ChampionClass cls, ShopSlot slot)
        {
            string id = PlayerPrefs.GetString(EquipKey(cls, slot), "");
            if (string.IsNullOrEmpty(id)) return null;
            var item = ShopCatalogSO.FindItem(id);
            if (item == null || item.Class != cls || item.Slot != slot || item.IsStandard || !IsOwned(item)) return null;
            return item;
        }

        public static bool IsEquipped(ShopItemSO item)
        {
            if (item == null) return false;
            var eq = GetEquipped(item.Class, item.Slot);
            return eq == null ? item.IsStandard : eq == item;
        }

        // Gepackte Ausrüstung aller Klassen (ShopRules) für Netz/Anwenden
        public static long LocalLoadout
        {
            get
            {
                long loadout = 0;
                for (int c = 0; c < ShopRules.ClassCount && c < GameSession.ChampionCount; c++)
                    for (int s = 0; s < ShopRules.SlotCount; s++)
                    {
                        var it = GetEquipped((ChampionClass)c, (ShopSlot)s);
                        if (it != null) loadout = ShopRules.With(loadout, c, s, it.NetIndex);
                    }
                return loadout;
            }
        }

        // Alles zurücksetzen (Dev/Tests): Besitz und Ausrüstung löschen
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(OwnedKey);
            for (int c = 0; c < GameSession.ChampionCount; c++)
                for (int s = 0; s < ShopRules.SlotCount; s++)
                    PlayerPrefs.DeleteKey(EquipKey((ChampionClass)c, (ShopSlot)s));
            PlayerPrefs.Save();
            _owned.Clear();
            _loaded = true;
            Changed?.Invoke();
        }
    }
}
