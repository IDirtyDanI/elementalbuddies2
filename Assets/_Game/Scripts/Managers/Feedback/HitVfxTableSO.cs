using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Effekt-Familien der Treffer-Effekte am Gegner (Spieltest 2026-10-18, Paket V, Asset-Liste Punkt 6)
    public enum HitVfxFamily
    {
        Arcane, // violetter Funkenstoß + Runen-Blitz
        Blade,  // heller Schnitt-Streifen + Funken
        Heavy,  // breiter roter Schnitt + Staubstoß (Wucht)
        Pierce, // Holzsplitter + Federflocken (Stich)
        Fire,   // Glutstoß, Flammenzungen, Funken
        Ice,    // Eissplitter-Stoß, Frosthauch
        Earth,  // Steinbrocken + Staub
        Light,  // goldene Sternfunken + Lichtkreuz
        Nature  // Dornen- und Blattsplitter
    }

    // Zuordnung Fähigkeit → Treffer-Effekt (Prefab der Familie, je Fähigkeit getönt/skaliert). Liegt als
    // Resources/HitVfxTable.asset vor (gebaut von BuddyTD → Game Feel → Arkanball & Treffer-Effekte bauen) und wird von
    // HitVfx zur Laufzeit geladen – so hängt kein Kit-/Managers-Prefab an der Tabelle.
    [CreateAssetMenu(menuName = "BuddyTD/Treffer-Effekt-Tabelle", fileName = "HitVfxTable")]
    public class HitVfxTableSO : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public AbilityId Ability;
            public HitVfxFamily Family;
            public GameObject Prefab;
            [Tooltip("Multipliziert die Startfarben aller Partikel (Weiß = Prefab-Farben).")]
            public Color Tint = Color.white;
            public float Scale = 1f;
            [Tooltip("Derselbe Gegner bekommt für diese Fähigkeit frühestens nach so vielen Sekunden wieder einen Effekt (Ticks, Wellen).")]
            public float MinInterval = 0.2f;
            [Tooltip("Höhe am Körper: 0 = Füße, 1 = Oberkante des Kolliders (HitVfx zieht den Effekt zusätzlich Richtung Kamera, das hebt ihn im Bild an).")]
            [Range(0f, 1f)] public float Height = 0.4f;
        }

        public List<Entry> Entries = new List<Entry>();

        [Header("Budget (je Rechner)")]
        [Tooltip("Höchstens so viele Effekte je Auslösung (gleiche Fähigkeit im selben Frame) – der Rest blitzt nur auf.")]
        public int MaxPerTrigger = 8;
        [Tooltip("Global: so viele Effekte auf einmal …")]
        public float Burst = 14f;
        [Tooltip("… und so viele pro Sekunde nachlaufend.")]
        public float PerSecond = 30f;
        [Tooltip("Lebensdauer der Effekt-Instanz (s).")]
        public float Lifetime = 1.2f;

        public Entry Find(AbilityId id)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i] != null && Entries[i].Ability == id) return Entries[i];
            return null;
        }
    }
}
