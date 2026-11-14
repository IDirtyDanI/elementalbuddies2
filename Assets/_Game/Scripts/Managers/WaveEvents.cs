using UnityEngine;

namespace ElementalBuddies
{
    // Wellen-Ereignisse (Plan „Fesselung“ C5): ab Welle 16 etwa jede 3. Welle etwas Neues für die zweite Run-Hälfte.
    // Termin und Typ bestimmt WaveManager.EventFor (deterministisch, Vorschau auf allen Rechnern gleich).
    public enum WaveEvent : byte
    {
        None,
        BloodMoon,  // Gegner schneller, mehr Splitter
        SoulStorm,  // doppelte Splitter, schwacher Magnet (selbst einsammeln)
        Ram,        // Belagerungsramme: zäher Mini-Boss, der nur den Nexus angreift
        Dimension,  // Dimensionsriss: Boss in einer anderen Dimension, betretbar durch die Gegner-Portale (DimensionEvent)
    }

    public static class WaveEvents
    {
        // Reihenfolge der normalen Ereignisse (im Wechsel). Der Dimensionsriss hat eigene Termine (DimensionEventRules)
        // und Vorrang – er steht nicht im Wechsel.
        public static readonly WaveEvent[] Cycle = { WaveEvent.BloodMoon, WaveEvent.Ram, WaveEvent.SoulStorm };

        public static string Name(WaveEvent e)
        {
            switch (e)
            {
                case WaveEvent.BloodMoon: return "Blutmond";
                case WaveEvent.SoulStorm: return "Seelensturm";
                case WaveEvent.Ram: return "Belagerungsramme";
                case WaveEvent.Dimension: return "Dimensionsriss";
                default: return "";
            }
        }

        public static string Description(WaveEvent e)
        {
            switch (e)
            {
                case WaveEvent.BloodMoon: return "Gegner +20 % Tempo, +50 % Splitter";
                case WaveEvent.SoulStorm: return "doppelte Splitter – aber der Magnet ist schwach: selbst einsammeln!";
                case WaveEvent.Ram: return "eine Ramme marschiert stur zum Nexus – aufhalten!";
                case WaveEvent.Dimension: return "ein Boss lauert hinter den Portalen – tretet hindurch, sonst zehrt der Riss am Nexus!";
                default: return "";
            }
        }

        public static Color Color(WaveEvent e)
        {
            switch (e)
            {
                case WaveEvent.BloodMoon: return new Color(0.85f, 0.15f, 0.12f);
                case WaveEvent.SoulStorm: return new Color(0.45f, 0.85f, 1f);
                case WaveEvent.Ram: return new Color(0.9f, 0.45f, 0.15f);
                case WaveEvent.Dimension: return new Color(0.52f, 0.22f, 0.95f); // tiefes Violett
                default: return UnityEngine.Color.white;
            }
        }

        // dunklere Variante für Text auf Pergament
        public static string DarkHex(WaveEvent e)
        {
            UnityEngine.Color.RGBToHSV(Color(e), out float h, out float s, out float v);
            return ColorUtility.ToHtmlStringRGB(UnityEngine.Color.HSVToRGB(h, Mathf.Min(1f, s + 0.2f), 0.48f));
        }
    }
}
