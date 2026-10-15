using UnityEngine;

namespace ElementalBuddies
{
    [CreateAssetMenu(fileName = "NewUpgrade", menuName = "ElementalBuddies/Upgrade Definition")]
    public class UpgradeDefinitionSO : ScriptableObject
    {
        [Header("Display")]
        public string Title;
        [TextArea] public string Description;
        public Sprite Icon; 

        [Header("Effect")]
        public UpgradeType Type;
        public UpgradeTarget Target;
        public StatType StatToBuff;
        public float Value; 
        public bool IsPercentage; // true: Value 10 = +10 % des Basiswerts (additiv: n Karten = +n·10 %). false: Value 5 = +5 flach.

        [Header("Sonderwirkung (Sprint 3)")]
        public CardEffect Effect;
        [Tooltip("Zweiter Wert der Sonderwirkung (Dauer, Radius, Nachteil …), siehe CardEffect.")]
        public float Value2;
        [Tooltip("Schlüsselwörter für Kombos, kommagetrennt (z. B. „Brand, Eis“). Karten mit gemeinsamem Schlüsselwort " +
                 "zu bereits gewählten Karten kommen öfter und tragen den Hinweis „Kombo“.")]
        public string Keywords;

        [Header("Draft")]
        public CardRarity Rarity;
        public CardAffinity Affinity;
        [Tooltip("Höchstens so oft pro Run wählbar (0 = unbegrenzt), z. B. Beschwörerband 6.")]
        public int MaxPicks = 0;

        [Header("Klassen (2026-10-15)")]
        [Tooltip("Für welche Champion-Klassen die Karte im Draft erscheint (Standard: alle). Karten ohne Wirkung " +
                 "für eine Klasse werden dort nicht gezogen.")]
        public ChampionClassMask Classes = ChampionClassMask.All;
        [Tooltip("Klassen-Varianten der Anzeige (Titel/Beschreibung/Icon), wenn dieselbe Wirkung bei einer Klasse etwas " +
                 "anderes bedeutet – z. B. Mana-Karten beim Berserker (Wut), Ausweichkunst beim Ritter (Schildblock).")]
        public ClassVariant[] Variants;

        [System.Serializable]
        public class ClassVariant
        {
            public ChampionClass Class;
            public string Title;
            [TextArea] public string Description;
            public Sprite Icon;
        }

        public bool AppliesTo(ChampionClass cls) => (Classes & (ChampionClassMask)(1 << (int)cls)) != 0;

        public ClassVariant VariantFor(ChampionClass cls)
        {
            if (Variants != null)
                foreach (var v in Variants)
                    if (v != null && v.Class == cls) return v;
            return null;
        }

        public string TitleFor(ChampionClass cls)
        {
            var v = VariantFor(cls);
            return v != null && !string.IsNullOrEmpty(v.Title) ? v.Title : Title;
        }

        public string DescriptionFor(ChampionClass cls)
        {
            var v = VariantFor(cls);
            return v != null && !string.IsNullOrEmpty(v.Description) ? v.Description : Description;
        }

        public Sprite IconFor(ChampionClass cls)
        {
            var v = VariantFor(cls);
            return v != null && v.Icon != null ? v.Icon : Icon;
        }

        // Anzeige für den lokalen Spieler (aktive Klasse)
        public static ChampionClass LocalClass => PlayerAbilities.Instance != null ? PlayerAbilities.Instance.ActiveClass : GameSession.SelectedChampion;
        public string LocalTitle => TitleFor(LocalClass);
        public string LocalDescription => DescriptionFor(LocalClass);
        public Sprite LocalIcon => IconFor(LocalClass);

        public string[] KeywordList
        {
            get
            {
                if (string.IsNullOrEmpty(Keywords)) return System.Array.Empty<string>();
                var parts = Keywords.Split(',');
                for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
                return parts;
            }
        }
    }
}