using UnityEngine;

namespace ElementalBuddies
{
    // Ausrüstungsplatz eines Shop-Items
    public enum ShopSlot { Weapon = 0, Armor = 1 }

    // Stufe (Anzeige „Standard“ / „Günstig“ / „Edel“)
    public enum ShopTier { Standard = 0, Common = 1, Epic = 2 }

    // Ein kosmetisches Item im Skin-Shop (Waffe oder Rüstung einer Champion-Klasse). Angewendet von ChampionSkin.
    // Effekte (Partikel, Licht …) stecken als Kinder in den Waffen-/Zusatzteil-Prefabs.
    // Netz: Items werden über (Klasse, Slot, NetIndex 1..15) im Loadout-long übertragen (ShopRules), 0 = Standard.
    [CreateAssetMenu(fileName = "ShopItem", menuName = "BuddyTD/Shop Item")]
    public class ShopItemSO : ScriptableObject
    {
        [System.Serializable]
        public class WeaponOverride
        {
            [Tooltip("Ersatz-Prefab; leer = Standardwaffe dieses Sockels behalten.")]
            public GameObject Prefab;
            [Tooltip("Eigene Lage am Knochen; aus = Position/Rotation/Größe des Standard-Attachments übernehmen.")]
            public bool OverrideTransform;
            public Vector3 LocalPosition;
            public Vector3 LocalEuler;
            public float WorldScale = 1f;
        }

        [System.Serializable]
        public class MaterialSwap
        {
            [Tooltip("Name des Original-Materials am Modell (ohne „ (Instance)“), z. B. Knight2_Steel.")]
            public string SourceMaterial;
            public Material Replacement;
        }

        [System.Serializable]
        public class AddOn
        {
            public GameObject Prefab;
            public HumanBodyBones Bone = HumanBodyBones.Head;
            [Tooltip("Versatz in Metern entlang der Knochen-Achsen.")]
            public Vector3 LocalPosition;
            public Vector3 LocalEuler;
            [Tooltip("Größe relativ zum Prefab, unabhängig von der Skalierung des Rigs.")]
            public Vector3 LocalScale = Vector3.one;
            [Tooltip("An der X-Achse spiegeln (Gegenstück für die linke Seite).")]
            public bool Mirror;
        }

        [Tooltip("Stabile Id (PlayerPrefs/Besitz), z. B. berserker_warhammer – nach Veröffentlichung nicht mehr ändern.")]
        public string Id;
        public ChampionClass Class;
        public ShopSlot Slot;
        public ShopTier Tier = ShopTier.Common;
        public string DisplayName;
        [TextArea] public string Description;
        [Tooltip("Preis in Edelsteinen (künftig; derzeit ist alles gratis, ShopInventory.FreeMode).")]
        public int Price;
        public Sprite Icon;
        [Tooltip("1..15, eindeutig je Klasse + Slot (Netz-Übertragung); 0 = Standard.")]
        [Range(0, ShopRules.MaxNetIndex)] public int NetIndex = 1;

        [Header("Waffe (Slot Weapon)")]
        public WeaponOverride RightHand = new WeaponOverride();
        public WeaponOverride LeftHand = new WeaponOverride();
        public WeaponOverride Back = new WeaponOverride();

        [Header("Rüstung (Slot Armor)")]
        public MaterialSwap[] Materials = new MaterialSwap[0];
        public AddOn[] AddOns = new AddOn[0];

        // Standard-Item (Stufe Standard oder NetIndex 0) = Grundausrüstung, immer besessen
        public bool IsStandard => Tier == ShopTier.Standard || NetIndex <= 0;

        // Anzeigename der Stufe
        public static string TierLabel(ShopTier t)
        {
            switch (t)
            {
                case ShopTier.Common: return "Günstig";
                case ShopTier.Epic: return "Edel";
                default: return "Standard";
            }
        }
    }
}
