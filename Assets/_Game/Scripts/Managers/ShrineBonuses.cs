using UnityEngine;

namespace ElementalBuddies
{
    // Passive Boni aus gereinigten Schreinen (pro Element 0 Feuer, 1 Eis, 2 Erde, 3 Licht).
    // Statisch, damit ElementalBuddy.GetDamageAtLevel ohne Manager-Referenz darauf zugreifen kann.
    // Reset bei Spielstart/Neustart durch ShrineManager.Awake (Szene wird beim Restart neu geladen, Statics nicht).
    public static class ShrineBonuses
    {
        public const int ElementCount = 4;
        private static readonly float[] _damageBonus = new float[ElementCount]; // additiv, 0.1 = +10 %

        public static event System.Action OnBonusesChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetAll()
        {
            for (int i = 0; i < ElementCount; i++) _damageBonus[i] = 0f;
            OnBonusesChanged?.Invoke();
        }

        public static float GetDamageMultiplier(int elementIndex)
        {
            if (elementIndex < 0 || elementIndex >= ElementCount) return 1f;
            return 1f + _damageBonus[elementIndex];
        }

        public static void AddDamageBonus(int elementIndex, float bonus)
        {
            if (elementIndex < 0 || elementIndex >= ElementCount) return;
            _damageBonus[elementIndex] += bonus;
            OnBonusesChanged?.Invoke();
        }
    }

    // Anzeige-Daten pro Element (Texte für Toasts/Objective-UI)
    public static class ElementInfo
    {
        public static readonly string[] Names = { "Feuer", "Eis", "Erde", "Licht" };
        public static readonly string[] ShrineNames = { "Feuer-Schrein", "Eis-Schrein", "Erd-Schrein", "Licht-Schrein" };
        // Magier-Namen als Fallback; klassenabhängig über PlayerAbilities.GetElementAbilityName
        public static readonly string[] AbilityNames = { "Flammenwelle", "Frostnova", "Steinwall", "Heiliger Kreis" };
        public static readonly string[] AbilityKeys = { "R", "F", "C", "V" };
        public static readonly Color[] Colors =
        {
            new Color(1f, 0.45f, 0.15f),   // Feuer
            new Color(0.45f, 0.8f, 1f),    // Eis
            new Color(0.6f, 0.45f, 0.25f), // Erde
            new Color(1f, 0.92f, 0.5f),    // Licht
        };

        private static int Clamp(int i) => Mathf.Clamp(i, 0, Names.Length - 1);
        public static string Name(int i) => Names[Clamp(i)];
        public static string ShrineName(int i) => ShrineNames[Clamp(i)];
        public static string AbilityName(int i) => AbilityNames[Clamp(i)];
        public static string AbilityKey(int i) => AbilityKeys[Clamp(i)];
        public static Color GetColor(int i) => Colors[Clamp(i)];
    }
}
