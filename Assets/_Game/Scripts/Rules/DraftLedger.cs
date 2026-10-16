using System;
using UnityEngine;

namespace ElementalBuddies
{
    // Warum ein Kauf / Neuwurf in der Kartenwahl abgelehnt wurde (übers Netz als int)
    public enum DraftDenial
    {
        None = 0,
        Share = 1,         // eigener Anteil reicht nicht (Koop) bzw. Kasse reicht nicht (Solo)
        Team = 2,          // Teamkasse reicht nicht mehr (z. B. Mitspieler hat inzwischen gebaut)
        Unavailable = 3,   // Karte nicht mehr wählbar (MaxPicks, Build-Karten-Regel, Klasse, Slot-Deckel)
        NothingToDraw = 4, // Neu würfeln: Pool gibt nichts her
        Invalid = 5,       // Slot ungültig / schon gekauft / Überspringen nach einem Kauf
    }

    // Kasse eines Spielers in der Kartenwahl nach der Welle („Kartenladen“, 2026-10-16) – reine Rechenregeln, damit
    // EditMode-Tests sie ohne Szene prüfen können. Der UpgradeManager hält pro Spieler einen Ledger (Server).
    //   Öffnen: Anteil = Teamkasse ÷ Spielerzahl (GoldRules.DraftShare), Staffeln von vorn.
    //   Karte:  erste gratis, dann CardBaseCost + CardCostStep · (n−1) (60, 90, 120 …) – aus dem Anteil UND der Teamkasse.
    //   Neu würfeln: zuerst Gratis-Neuwürfe (run-weit, +1 je Boss), sonst 25, 40, 55 … aus dem Anteil.
    // Reihenfolge immer: prüfen/ziehen → abbuchen → übernehmen. Schlägt etwas fehl, wird nichts abgebucht.
    public sealed class DraftLedger
    {
        public int CardBaseCost = 60, CardCostStep = 30;
        public int RerollBaseCost = 25, RerollCostStep = 15;

        // Rest-Anteil dieser Kartenwahl (Splitter)
        public int Share { get; private set; }
        // Anteil beim Öffnen (Anzeige/Telemetrie)
        public int ShareAtOpen { get; private set; }
        // genommene Karten dieser Kartenwahl (gratis + gekauft)
        public int Picks { get; private set; }
        // bezahlte Neuwürfe dieser Kartenwahl
        public int PaidRerolls { get; private set; }
        // Gratis-Neuwürfe (gelten den ganzen Run)
        public int FreeRerolls;
        // Preis der letzten erfolgreichen Aktion (0 = gratis)
        public int LastPrice { get; private set; }

        public DraftLedger(int freeRerolls = 1) { FreeRerolls = Mathf.Max(0, freeRerolls); }

        // Neue Kartenwahl
        public void Open(float treasury, int players) => Open(GoldRules.DraftShare(treasury, players));

        // Neue Kartenwahl mit vorab berechnetem Anteil (Koop: Kasse beim Wellenende ÷ Spielerzahl)
        public void Open(int share)
        {
            Share = ShareAtOpen = Mathf.Max(0, share);
            Picks = 0;
            PaidRerolls = 0;
            LastPrice = 0;
        }

        public int NextCardPrice => GoldRules.CardPrice(Picks, CardBaseCost, CardCostStep);
        public bool NextRerollFree => FreeRerolls > 0;
        public int NextRerollPrice => FreeRerolls > 0 ? 0 : GoldRules.RerollPrice(PaidRerolls, RerollBaseCost, RerollCostStep);

        // Was davon ausgegeben werden kann: Rest-Anteil, höchstens die Teamkasse
        public int Spendable(float treasury) => Mathf.Max(0, Mathf.Min(Share, Mathf.FloorToInt(treasury + 0.0001f)));

        public DraftDenial CheckAfford(int price, float treasury)
        {
            if (price <= 0) return DraftDenial.None;
            if (price > Share) return DraftDenial.Share;
            if (treasury + 0.001f < price) return DraftDenial.Team;
            return DraftDenial.None;
        }

        // Karte nehmen/kaufen. validate = Karte noch zulässig?; trySpend = Splitter aus der Teamkasse abbuchen.
        public DraftDenial TryBuy(float treasury, Func<bool> validate, Func<int, bool> trySpend)
        {
            int price = NextCardPrice;
            var d = CheckAfford(price, treasury);
            if (d != DraftDenial.None) return d;
            if (validate != null && !validate()) return DraftDenial.Unavailable;
            if (price > 0 && (trySpend == null || !trySpend(price))) return DraftDenial.Team;
            Share -= price;
            Picks++;
            LastPrice = price;
            return DraftDenial.None;
        }

        // Neu würfeln. draw = neue Karten ziehen (noch nicht übernehmen), false = nichts zu ziehen.
        public DraftDenial TryReroll(float treasury, Func<bool> draw, Func<int, bool> trySpend)
        {
            bool free = FreeRerolls > 0;
            int price = free ? 0 : NextRerollPrice;
            if (!free)
            {
                var d = CheckAfford(price, treasury);
                if (d != DraftDenial.None) return d;
            }
            if (draw != null && !draw()) return DraftDenial.NothingToDraw;
            if (free) FreeRerolls--;
            else
            {
                if (trySpend == null || !trySpend(price)) return DraftDenial.Team;
                Share -= price;
                PaidRerolls++;
            }
            LastPrice = price;
            return DraftDenial.None;
        }
    }
}
