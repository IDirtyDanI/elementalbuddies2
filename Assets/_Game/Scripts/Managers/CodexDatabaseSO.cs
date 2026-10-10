using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Kodex (Plan „Fesselung“ E4): alle entdeckbaren Einträge – Gegner, Bosse, Elite-Eigenschaften, Wellen-Ereignisse,
    // Fusionen, Super-Elementare und Wellenkarten. Liegt unter Resources/CodexDatabase (Hauptmenü und Spiel),
    // erzeugt von BuddyTD → Game Feel → Sprint 4 einrichten. Entdeckt-Stand: Codex (PlayerPrefs).
    public class CodexDatabaseSO : ScriptableObject
    {
        public enum Category { Enemy, Boss, Elite, Event, Fusion, Super, Card }

        [Serializable]
        public class Entry
        {
            public string Key;          // Entdeckungs-Schlüssel, z. B. "enemy:Runner", "card:FireStat"
            public Category Category;
            public string Title;
            [TextArea] public string Description;
            public Sprite Icon;
            public string Extra;        // z. B. Seltenheit der Karte, Rezept der Fusion
        }

        public List<Entry> Entries = new List<Entry>();

        private static CodexDatabaseSO _instance;
        public static CodexDatabaseSO Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<CodexDatabaseSO>("CodexDatabase");
                return _instance;
            }
        }
    }
}
