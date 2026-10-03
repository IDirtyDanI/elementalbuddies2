using UnityEngine;

namespace ElementalBuddies
{
    // Händlerkarte: verbessert genau einen Wert (Stat) einer Fähigkeit des Champions. Karten stapeln sich,
    // es gibt keine Seltenheitsstufen. Der Pool liegt unter Assets/ScriptableObjects/MerchantCards/ und wird per
    // Editor-Menü „BuddyTD → Händler → Kartenpool neu erzeugen" (MerchantSetup) reproduzierbar erzeugt.
    [CreateAssetMenu(fileName = "NewMerchantCard", menuName = "ElementalBuddies/Merchant Card")]
    public class MerchantCardSO : ScriptableObject
    {
        [Header("Wirkung")]
        public ChampionClass Class;
        public AbilityId Ability;
        public AbilityStat Stat;
        [Tooltip("Prozent bei Prozent-/Reduktions-Stats (20 = +20 % bzw. −20 %), sonst flach (+1 Aufladung, +20°, +2 m).")]
        public float Value = 20f;

        [Header("Anzeige")]
        public string Title;
        [TextArea] public string Description;
        [Tooltip("Fähigkeits-Icon (ability_* / emblem_*).")]
        public Sprite Icon;
        [Tooltip("Optionale Modifikator-Plakette (badge_*), wird über das Icon gelegt.")]
        public Sprite Badge;

        // Taste der Fähigkeit (immer gleich für alle Champions) → welcher Händler die Karte verkauft
        public AbilitySlot Slot => SlotOf(Ability);

        public MerchantKind Merchant => MerchantInfo.KindOfSlot(Slot);

        // Wert als Text, z. B. "+20 %"
        public string ValueText => AbilityMods.FormatValue(Stat, Value);

        public static AbilitySlot SlotOf(AbilityId id)
        {
            // Reihenfolge im Enum: je Klasse 6 Fähigkeiten in Slot-Reihenfolge (LMB, RMB, R, F, C, V)
            return (AbilitySlot)((int)id % AbilitySlots.Count);
        }

        public static ChampionClass ClassOf(AbilityId id)
        {
            switch ((int)id / AbilitySlots.Count)
            {
                case 1: return ChampionClass.Knight;
                case 2: return ChampionClass.Archer;
                default: return ChampionClass.Mage;
            }
        }

        // Wirkung anwenden (PlayerAbilities.Mods)
        public void Apply(PlayerAbilities abilities)
        {
            if (abilities != null) abilities.Mods.Add(Ability, Stat, Value);
        }
    }
}
