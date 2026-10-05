using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Meta-Freischaltungen über Erfolge (gelten erst ab dem nächsten Spiel, siehe Progression)
    public enum UnlockId
    {
        None,
        ChampionKnight,
        ChampionArcher,
        Fusion2,     // alle 6 2er-Fusionen
        Stage4Fire,
        Stage4Ice,
        Stage4Earth,
        Stage4Light,
        TriFusion,   // alle 4 Super-Elementare
    }

    // Bedingungs-Arten; Parameter stehen in AchievementDefinition (Threshold, Level, Element)
    public enum AchievementCondition
    {
        ReachWave,             // Welle Threshold erreichen (im Spiel, keine rückwirkende Vergabe)
        BaseBuddiesAtLevel,    // Threshold Basis-Buddies (Element, -1 = beliebig) gleichzeitig auf Stufe >= Level
        FusionBuddiesOnField,  // Threshold Fusions-Buddies gleichzeitig auf dem Feld (Super-Elementare zählen mit)
        DefeatBoss,            // Threshold Bosse in einem Spiel besiegen
        DefeatBossKinds,       // Threshold verschiedene Boss-Arten besiegt, über alle Spiele (BossKinds)
        ShardsCollected,       // Threshold Seelensplitter in einem Spiel eingenommen (Summe der Gewinne)
        BuildSuper,            // Threshold Super-Elementare in einem Spiel erschaffen
    }

    [Serializable]
    public class AchievementDefinition
    {
        [Tooltip("Stabile ID (wird als PlayerPrefs-Schlüssel ach_<Id> gespeichert) – nie umbenennen.")]
        public string Id;
        public string Title;
        [Tooltip("Optional: eigener Bedingungstext. Leer = automatisch aus Bedingung + Parametern.")]
        [TextArea] public string Description;
        public Sprite Icon;

        [Header("Bedingung")]
        public AchievementCondition Condition;
        [Tooltip("Schwelle: Welle, Anzahl Buddies/Bosse/Boss-Arten bzw. Seelensplitter.")]
        public int Threshold = 1;
        [Tooltip("Nur BaseBuddiesAtLevel: Mindeststufe.")]
        public int Level = 2;
        [Tooltip("Nur BaseBuddiesAtLevel: Element 0 Feuer, 1 Eis, 2 Erde, 3 Licht; -1 = beliebig.")]
        public int Element = -1;

        [Header("Belohnung")]
        [Tooltip("Freischaltung (None = Trophäe). Haben mehrere Erfolge dieselbe Freischaltung, müssen alle erreicht sein.")]
        public UnlockId Unlock = UnlockId.None;

        public bool IsTrophy => Unlock == UnlockId.None;
    }

    [Serializable]
    public class UnlockInfo
    {
        public UnlockId Id;
        public string Name;       // z. B. "2er-Fusionen"
        public Sprite Icon;       // optional für die Erfolge-Seite
    }

    // Datenbasis aller Erfolge. Liegt unter Resources/AchievementDatabase (Spielszene + Hauptmenü).
    // Anlegen/Aktualisieren: Menü BuddyTD → Erfolge → Datenbank anlegen-aktualisieren.
    [CreateAssetMenu(fileName = "AchievementDatabase", menuName = "ElementalBuddies/Achievement Database")]
    public class AchievementDatabaseSO : ScriptableObject
    {
        public const string ResourcePath = "AchievementDatabase";

        public List<AchievementDefinition> Achievements = new List<AchievementDefinition>();
        public List<UnlockInfo> Unlocks = new List<UnlockInfo>();
        [Tooltip("Boss-Arten für DefeatBossKinds: Teil des EnemyConfig-Namens (Reihenfolge = Bit im gespeicherten Fortschritt).")]
        public List<string> BossKinds = new List<string> { "BossBoneLord", "BossNecromancer", "BossDeathHunter" };
        [Tooltip("Anzeigenamen der Boss-Arten (gleiche Reihenfolge wie BossKinds).")]
        public List<string> BossKindNames = new List<string> { "Knochenfürst", "Nekromant", "Totenjäger" };

        [Header("UI-Grafiken (Erfolge-Seite, Popup, Sperrhinweise)")]
        [Tooltip("Medaillen-Rahmen erreichter Erfolge (goldener Ring, Mitte transparent).")]
        public Sprite MedalFrame;
        [Tooltip("Medaillen-Rahmen noch nicht erreichter Erfolge (Eisenring).")]
        public Sprite MedalFrameLocked;
        public Sprite LockIcon;
        public Sprite TrophyIcon;

        private static AchievementDatabaseSO _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        public static AchievementDatabaseSO Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<AchievementDatabaseSO>(ResourcePath);
                return _instance;
            }
        }

        public AchievementDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var a in Achievements)
                if (a != null && a.Id == id) return a;
            return null;
        }

        public string GetUnlockName(UnlockId id)
        {
            foreach (var u in Unlocks)
                if (u != null && u.Id == id && !string.IsNullOrEmpty(u.Name)) return u.Name;
            return id.ToString();
        }

        public Sprite GetUnlockIcon(UnlockId id)
        {
            foreach (var u in Unlocks)
                if (u != null && u.Id == id) return u.Icon;
            return null;
        }

        // Alle Erfolge, die für diese Freischaltung nötig sind (leer = immer frei)
        public List<AchievementDefinition> GetRequirements(UnlockId id, List<AchievementDefinition> result = null)
        {
            if (result == null) result = new List<AchievementDefinition>();
            else result.Clear();
            if (id == UnlockId.None) return result;
            foreach (var a in Achievements)
                if (a != null && a.Unlock == id) result.Add(a);
            return result;
        }

        // -1, wenn der Config-Name zu keiner bekannten Boss-Art passt
        public int GetBossKindIndex(EnemyConfigSO config)
        {
            if (config == null || BossKinds == null) return -1;
            string n = config.name;
            for (int i = 0; i < BossKinds.Count; i++)
                if (!string.IsNullOrEmpty(BossKinds[i]) && n.IndexOf(BossKinds[i], StringComparison.OrdinalIgnoreCase) >= 0) return i;
            return -1;
        }

        public string GetBossKindName(int index) =>
            BossKindNames != null && index >= 0 && index < BossKindNames.Count ? BossKindNames[index]
            : BossKinds != null && index >= 0 && index < BossKinds.Count ? BossKinds[index] : "?";

        // Kurzer Auftrag für Sperrhinweise, z. B. "Erreiche Welle 6" (sonst wie GetConditionText)
        public static string GetGoalText(AchievementDefinition a)
        {
            if (a == null) return "";
            if (string.IsNullOrEmpty(a.Description) && a.Condition == AchievementCondition.ReachWave) return $"Erreiche Welle {a.Threshold}";
            return GetConditionText(a);
        }

        // Bedingungstext, z. B. "2 Buddies gleichzeitig auf Stufe 2"
        public static string GetConditionText(AchievementDefinition a)
        {
            if (a == null) return "";
            if (!string.IsNullOrEmpty(a.Description)) return a.Description;
            int t = a.Threshold;
            switch (a.Condition)
            {
                case AchievementCondition.ReachWave:
                    return $"Welle {t} erreichen";
                case AchievementCondition.BaseBuddiesAtLevel:
                    return a.Element >= 0 && a.Element < ElementInfo.Names.Length
                        ? $"{t} {ElementInfo.Names[a.Element]}-Buddies gleichzeitig auf Stufe {a.Level}"
                        : $"{t} Buddies gleichzeitig auf Stufe {a.Level}";
                case AchievementCondition.FusionBuddiesOnField:
                    return $"{t} Fusions-Buddies gleichzeitig auf dem Feld";
                case AchievementCondition.DefeatBoss:
                    return t <= 1 ? "Einen Boss besiegen" : $"{t} Bosse in einem Spiel besiegen";
                case AchievementCondition.DefeatBossKinds:
                    return $"{t} verschiedene Boss-Arten besiegen (über alle Spiele)";
                case AchievementCondition.ShardsCollected:
                    return $"{t} Seelensplitter in einem Spiel einsammeln";
                case AchievementCondition.BuildSuper:
                    return t <= 1 ? "Ein Super-Elementar erschaffen" : $"{t} Super-Elementare in einem Spiel erschaffen";
            }
            return "";
        }
    }
}
