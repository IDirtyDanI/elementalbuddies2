using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Alle Map-Definitionen aus Resources/Maps (sortiert nach Order). Fehlt dort alles (z. B. Tests), liefert Default
    // eine zur Laufzeit erzeugte Stadt-Definition mit den Feld-Standardwerten von MapDefinitionSO.
    public static class MapRegistry
    {
        public const string ResourceFolder = "Maps";

        private static List<MapDefinitionSO> _all;
        private static MapDefinitionSO _fallback;

        public static IReadOnlyList<MapDefinitionSO> All
        {
            get
            {
                if (_all == null || _all.Exists(m => m == null)) Load();
                return _all;
            }
        }

        // Stadt (Id „city“), sonst die erste Map, sonst der Laufzeit-Ersatz
        public static MapDefinitionSO Default
        {
            get
            {
                var all = All;
                foreach (var m in all) if (m.Id == MapDefinitionSO.CityId) return m;
                if (all.Count > 0) return all[0];
                if (_fallback == null)
                {
                    _fallback = ScriptableObject.CreateInstance<MapDefinitionSO>();
                    _fallback.name = "Map_City (Ersatz)";
                    _fallback.hideFlags = HideFlags.DontSave;
                }
                return _fallback;
            }
        }

        // Map zur Id; unbekannt/leer → null
        public static MapDefinitionSO Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var m in All) if (m.Id == id) return m;
            return null;
        }

        // Map zur Id; unbekannt → Default
        public static MapDefinitionSO Get(string id) => Find(id) ?? Default;

        // Map, deren Szene so heißt (Name oder Pfad); unbekannt → null
        public static MapDefinitionSO FindByScene(string sceneNameOrPath)
        {
            if (string.IsNullOrEmpty(sceneNameOrPath)) return null;
            foreach (var m in All)
                if (m.ScenePath == sceneNameOrPath || m.SceneName == sceneNameOrPath) return m;
            return null;
        }

        private static void Load()
        {
            _all = new List<MapDefinitionSO>();
            foreach (var m in Resources.LoadAll<MapDefinitionSO>(ResourceFolder))
                if (m != null && !string.IsNullOrEmpty(m.Id)) _all.Add(m);
            _all.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        }

        // Editor/Tests: nach Asset-Änderungen neu einlesen
        public static void Reload() { _all = null; }
    }
}
