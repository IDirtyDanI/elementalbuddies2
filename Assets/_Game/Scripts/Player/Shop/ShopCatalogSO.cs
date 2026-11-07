using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Alle Shop-Items (Resources/ShopCatalog.asset, erzeugt vom Editor-Setup). Fehlt das Asset, ist der Katalog leer –
    // Shop/Skins tun dann einfach nichts.
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "BuddyTD/Shop Catalog")]
    public class ShopCatalogSO : ScriptableObject
    {
        public const string ResourcePath = "ShopCatalog";

        public List<ShopItemSO> Items = new List<ShopItemSO>();

        private static ShopCatalogSO _instance;
        private static bool _searched;

        // Katalog aus Resources (null, wenn nicht vorhanden – Aufrufer prüfen oder die statischen Hilfen nutzen)
        public static ShopCatalogSO Instance
        {
            get
            {
                if (_instance == null && !_searched)
                {
                    _searched = true;
                    _instance = Resources.Load<ShopCatalogSO>(ResourcePath);
                }
                return _instance;
            }
            set { _instance = value; _searched = true; } // Tests / Editor-Setup
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _searched = false;
        }

        // Items einer Klasse und eines Slots (Reihenfolge wie im Katalog)
        public List<ShopItemSO> ItemsFor(ChampionClass cls, ShopSlot slot)
        {
            var list = new List<ShopItemSO>();
            foreach (var it in Items)
                if (it != null && it.Class == cls && it.Slot == slot) list.Add(it);
            return list;
        }

        public ShopItemSO Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var it in Items)
                if (it != null && it.Id == id) return it;
            return null;
        }

        // Item zu (Klasse, Slot, NetIndex); 0/ungültig → null (Standard)
        public ShopItemSO Find(ChampionClass cls, ShopSlot slot, int netIndex)
        {
            if (netIndex <= 0 || netIndex > ShopRules.MaxNetIndex) return null;
            foreach (var it in Items)
                if (it != null && it.Class == cls && it.Slot == slot && it.NetIndex == netIndex) return it;
            return null;
        }

        // ---------------- Statische Hilfen (sicher ohne Katalog) ----------------

        public static ShopItemSO FindItem(string id) => Instance != null ? Instance.Find(id) : null;
        public static ShopItemSO FindItem(ChampionClass cls, ShopSlot slot, int netIndex) => Instance != null ? Instance.Find(cls, slot, netIndex) : null;
        public static List<ShopItemSO> ItemsOf(ChampionClass cls, ShopSlot slot) => Instance != null ? Instance.ItemsFor(cls, slot) : new List<ShopItemSO>();
    }
}
