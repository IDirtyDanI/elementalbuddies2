using System;

namespace ElementalBuddies
{
    // Rest-Pool für Seelensplitter-Drops (Spieltest 2026-10-17, Paket A) – reine Rechenregel, damit EditMode-Tests sie
    // ohne Szene prüfen können. Nur der Server führt den Pool (EconomyManager), Drops werden wie bisher repliziert.
    //
    // Problem: Das Kill-Kopfgeld ist eine Kommazahl. Beschworene (×0,25), Schwarm-Gegner und die Drop-Abnahme im
    // Spätspiel ergeben oft Drops < 1 Splitter. Die Kasse wird abgerundet angezeigt (HUDManager.UpdateShards), das
    // „+N“-Popup erscheint erst ab 0,5 – solche Kristalle wirkten beim Einsammeln, als zählten sie nicht.
    //
    // Regel: Jeder sichtbare Kristall ist ganzzahlig und mindestens 1 wert. Vom Kopfgeld fällt nur der ganze Anteil
    // (inkl. angesammeltem Rest) als Kristall; der Nachkomma-Rest wandert in den Pool und kommt mit einem späteren Kill
    // heraus. Ganzzahlige Kristalle erhöhen die abgerundete Anzeige immer genau um ihren Wert. Am Wellenende wird der
    // Rest mit der Rückruf-Gutschrift verbucht (TakeRemainder) – der Gesamtbetrag bleibt unverändert, Anti-Farm bleibt.
    public sealed class ShardDropPool
    {
        // Rundungs-Toleranz: 10 × 0,1f ergibt in float 0,99999994 – das soll trotzdem einen Kristall ergeben
        public const float Epsilon = 1e-4f;

        // Noch nicht als Kristall gefallener Rest (immer in [0, 1))
        public float Remainder { get; private set; }

        // Kopfgeld eines Kills einrechnen. Rückgabe: Wert des jetzt zu spawnenden Kristalls (ganzzahlig, ≥ 1)
        // oder 0, wenn (noch) kein Kristall fällt.
        public float Add(float bounty)
        {
            if (!(bounty > 0f) || float.IsInfinity(bounty)) return 0f;
            float total = Remainder + bounty;
            float whole = (float)Math.Floor(total + Epsilon);
            if (whole < 1f)
            {
                Remainder = total;
                return 0f;
            }
            Remainder = Math.Max(0f, total - whole);
            if (Remainder < Epsilon) Remainder = 0f;
            return whole;
        }

        // Wellenende: Rest abholen (wird mit der Rückruf-Gutschrift verbucht) und Pool leeren
        public float TakeRemainder()
        {
            float r = Remainder;
            Remainder = 0f;
            return r;
        }

        public void Reset() => Remainder = 0f;
    }
}
